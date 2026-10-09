using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MelonLoader;

[assembly: MelonInfo(typeof(CrucibleUnlock.ModMain), "Crucible Unlock", "0.9.16-bowgun-view-repair-r2", "NRFW research", "")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
[assembly: MelonAdditionalDependencies("0Harmony")]

namespace CrucibleUnlock
{
    /// <summary>
    /// 第二试炼解锁 mod。
    ///
    /// 本轮支持两种模式（MelonPreferences 的 mode，默认 probe）：
    ///   probe        只探测并记录类型/方法/Frame 入口，**不调用任何游戏 API**；
    ///   runtime-unlock 校验版本后，仅覆盖第二试炼的两处完成状态读取，退出游戏即失效。
    ///
    /// 旧持久写入代码保留供研究，但本版本入口不执行 unlock / unlock-dry。
    ///
    /// 核心逻辑在 ReflectionPlan.cs / UnlockRunner.cs，与加载器和游戏类型解耦，可用 selftest.ps1 离线验证。
    /// </summary>
    public class ModMain : MelonMod
    {
        private const string CategoryId = "CrucibleUnlock";
        private const int TypeSweepLimit = 200;
        private const int RunnerSweepLimit = 60;
        private const int MembersPerType = 14;
        private const string PlanFileName = "unlock-plan.json";
        private const string MarkerFileName = "CrucibleUnlock.state.json";

        /// <summary>既有逆向结论里的目标：quest step GUID 与其 PrimeState GUID。</summary>
        private const long StepGuid = 346285554720904463L;
        private const long PrimeStateGuid = 3803516328734635953L;

        private static readonly string[] ProbeTypes =
        {
            "Quantum.QuestAPI",
            "Quantum.QuantumConditionQuestStepState",
            "Quantum.InfiniteDungeonAPI",
            "Quantum.InfiniteDungeonDirector",
            "Quantum.InfiniteDungeonBossRushPlaylistRuleDirector",
            "Quantum.BossBattleDirector",
            "Quantum.PrimeStatesAPI",
            "Quantum.PrimeStateCollection",
            "Quantum.RuntimeQuestStep",
        };

        private static readonly string[] FrameEntryTypes =
        {
            "QuantumRunner",
            "Quantum.QuantumRunner",
            "QuantumGame",
            "Quantum.QuantumGame",
            "Quantum.Frame",
            "Quantum.Frames",
            "Quantum.Simulation",
        };

        private static readonly HashSet<string> LogAllMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Quantum.QuestAPI",
            "Quantum.PrimeStatesAPI",
            "Quantum.PrimeStateCollection",
            "Quantum.RuntimeQuestStep",
        };

        private static readonly string[] MethodKeywords =
        {
            "Complete", "QuestStep", "PrimeState", "Condition", "Playlist",
            "Boss", "Floor", "Teleport", "Finalize", "Offering", "Crucible", "State",
        };

        private static readonly string[] SweepNameParts = { "Quest", "Crucible", "PrimeState", "InfiniteDungeon" };
        private static readonly string[] RunnerSweepNameParts = { "Runner", "Session", "Simulation", "Frames" };
        private static readonly string[] RunnerMemberKeywords = { "Game", "Frame", "Session", "Default", "Runner", "Predicted", "Verified", "Simulation" };

        private sealed class MelonLog : ILog
        {
            private readonly MelonLogger.Instance _logger;
            public MelonLog(MelonLogger.Instance logger) { _logger = logger; }
            public void Info(string message) { _logger.Msg(message); }
            public void Warn(string message) { _logger.Warning(message); }
            public void Error(string message) { _logger.Error(message); }
        }

        private MelonPreferences_Entry<string> _mode;
        private MelonPreferences_Entry<bool> _guardBrokenWarrickMusic;
        private MelonPreferences_Entry<bool> _traceBossMotion;
        private MelonPreferences_Entry<bool> _repairWarrickPhase2Target;
        private MelonPreferences_Entry<bool> _repairBossRushProgression, _repairRitualAnimator;
        private MelonPreferences_Entry<int> _bossTraceDropAmount;
        private MelonPreferences_Entry<string> _motionTraceDirectory;
        private MelonPreferences_Entry<int> _bowgunWeaponClass;
        private MotionTraceSink _motionSink;
        private bool _motionActive;
        private long _lastMotionHealth;
        private bool _motionErrorReported;
        private long _lastMotionDropped;
        private string _baseDirectory;
        private bool _runtimeActive;
        private bool _phase2RepairActive;

        public override void OnInitializeMelon()
        {
            MelonPreferences_Category category = MelonPreferences.CreateCategory(CategoryId, "Crucible Unlock");
            _mode = category.CreateEntry("mode", "probe",
                "运行模式", "probe = 只探测；runtime-unlock = 临时解锁第二试炼（不写完成状态）；旧持久模式停用");
            _repairBossRushProgression = category.CreateEntry("repair_bossrush_progression", true,
                "第二试炼清场修复", "开层时调用原生SetFloor，沿用原生清场、提示、交互与转场");
            _repairRitualAnimator = category.CreateEntry("repair_ritual_animator", true,
                "献祭动作角色绑定修复", "只在献祭轨绑定缺失或错误时临时指向当前献祭角色，结束恢复");
            _bossTraceDropAmount = category.CreateEntry("boss_trace_drop_amount", BossTraceDropApi.DefaultAmount,
                "第二试炼 Boss 痕迹掉落数量", "每个 Boss 原生痕迹掉落基数，0关闭，允许0至1000；多人修正沿用原生规则；重启生效");
            _guardBrokenWarrickMusic = category.CreateEntry("guard_broken_warrick_music", false,
                "首 Boss 坏音乐防护", "仅在已确认的隐藏 Warrick 对象身份与坏子引用结构相符时跳过音乐时间轴；不处理献祭动画");
            _repairWarrickPhase2Target = category.CreateEntry("repair_warrick_phase2_target", false,
                "沃里克二阶段目标修复", "每次二阶段动作执行时，将本实体已确认错误的远方落点改为当刻位置；覆盖重复出生，不改共享资产或存档");
            _baseDirectory = Path.GetDirectoryName(typeof(ModMain).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _traceBossMotion = category.CreateEntry("trace_boss_motion", false,
                "首 Boss 位移日志", "主线程轮询隐藏首 Warrick 的显示位置、模型偏移与动作，不添加位置同步钩子，后台写入独立 JSONL");
            _motionTraceDirectory = category.CreateEntry("motion_trace_directory", Path.Combine(_baseDirectory, "motion-logs"),
                "位移日志目录", "每次启动创建独立文件，不覆盖旧日志");
            _bowgunWeaponClass = category.CreateEntry("bowgun_weapon_class", 30,
                "弩枪武器类别（动画）", "写入强化弩枪 WeaponStaticData.Class（原生偏移 0x128）：30=Crossbow、21=Greatbow、20=Bow、0=不写；重启生效");

            LoggerInstance.Msg("=====================================================");
            LoggerInstance.Msg("Crucible Unlock 0.9.16-bowgun-view-repair-r2 已加载");
            LoggerInstance.Msg($"MelonLoader: {typeof(MelonMod).Assembly.GetName().Version}");
            LoggerInstance.Msg($"CLR: {Environment.Version}  64bit={Environment.Is64BitProcess}");
            LoggerInstance.Msg($"mode = {_mode.Value}   目录 = {_baseDirectory}");
            LoggerInstance.Msg($"guard_broken_warrick_music = {_guardBrokenWarrickMusic.Value}");
            LoggerInstance.Msg($"trace_boss_motion = {_traceBossMotion.Value}");
            LoggerInstance.Msg($"repair_warrick_phase2_target = {_repairWarrickPhase2Target.Value}");
            LoggerInstance.Msg($"repair_bossrush_progression = {_repairBossRushProgression.Value}; repair_ritual_animator = {_repairRitualAnimator.Value}");
            LoggerInstance.Msg($"boss_trace_drop_amount = {_bossTraceDropAmount.Value}");
            LoggerInstance.Msg($"bowgun_weapon_class = {_bowgunWeaponClass.Value}（30=Crossbow、21=Greatbow、20=Bow、0=不写）");
            LoggerInstance.Msg($"目标：quest step GUID {StepGuid} / PrimeState GUID {PrimeStateGuid}");
            LoggerInstance.Msg("=====================================================");
        }

        public override void OnLateInitializeMelon()
        {
            try
            {
                string mode = (_mode.Value ?? "probe").Trim().ToLowerInvariant();
                if (mode == "runtime-unlock")
                {
                    BuildGuard.Verify(_baseDirectory, message => LoggerInstance.Msg(message));
                    try { BossTraceDropApi.SetAmount(_bossTraceDropAmount.Value); }
                    catch (ArgumentOutOfRangeException)
                    {
                        BossTraceDropApi.ResetToDefault();
                        LoggerInstance.Warning("[boss-traces] invalid configuration; using default=" + BossTraceDropApi.DefaultAmount);
                    }
                    if (_guardBrokenWarrickMusic.Value)
                        BrokenWarrickMusicGuard.Install(message => LoggerInstance.Msg(message), message => LoggerInstance.Error(message));
                    try
                    {
                        RuntimeUnlock.Install(message => LoggerInstance.Msg(message), message => LoggerInstance.Error(message));
                    }
                    catch (Exception installError)
                    {
                        if (_guardBrokenWarrickMusic.Value)
                        {
                            try { BrokenWarrickMusicGuard.Dispose(); }
                            catch (Exception rollbackError) { throw new AggregateException(installError, rollbackError); }
                        }
                        throw;
                    }
                    _runtimeActive = true;
                    if (_repairWarrickPhase2Target.Value)
                    {
                        WarrickPhase2TargetRepair.Install(message => LoggerInstance.Msg(message), message => LoggerInstance.Error(message));
                        _phase2RepairActive = true;
                    }
                    if (_traceBossMotion.Value) StartMotionTrace();
                    if (_repairBossRushProgression.Value) BossRushProgressionRepair.Install();
                    BossTraceDropRepair.Install();
                    BrokenVowBossRepair.Install();
                    BossTracePickupDiagnostics.Install();
                    HuskBossNameRepair.Install();
                    EchoCapRepair.Install();
                    BowgunInputRepair.Configure(_bowgunWeaponClass.Value);
                    BowgunInputRepair.Install();
                    if (_repairRitualAnimator.Value)
                    {
                        RitualAnimatorRepair.Install();
                        RitualViewRepair.Install();
                    }
                    LoggerInstance.Msg("[runtime-unlock] 本轮临时解锁；不调用完成任务或写入存档接口。先在城里测试动作，再献祭进入首Boss。");
                }
                else if (mode == "probe")
                {
                    ProbeIl2CppAssemblies();
                    ProbeFrameAccess();
                    LoggerInstance.Msg("[unlock] mode=probe：只做类型探测，不调用解锁接口。");
                }
                else
                {
                    LoggerInstance.Error($"[unlock] mode='{_mode.Value}' 本轮停用；仅支持 probe / runtime-unlock。未安装解锁补丁。");
                }
            }
            catch (Exception error)
            {
                LoggerInstance.Error($"执行失败: {error}");
            }
        }

        public override void OnUpdate()
        {
            BowgunInputRepair.UpdateLocalHero();
            TrialRepairLog.Flush(message => LoggerInstance.Msg(message));
            if (_runtimeActive) RuntimeUnlock.Flush();
            if (_motionSink != null && Stopwatch.GetTimestamp() - _lastMotionHealth >= 5L * Stopwatch.Frequency)
            {
                _lastMotionHealth = Stopwatch.GetTimestamp();
                if (_motionSink.Error != null && !_motionErrorReported)
                {
                    _motionErrorReported = true;
                    LoggerInstance.Error("[boss-motion] writer stopped: " + _motionSink.Error);
                }
                if (_motionSink.DroppedCount != _lastMotionDropped)
                {
                    _lastMotionDropped = _motionSink.DroppedCount;
                    LoggerInstance.Warning("[boss-motion] dropped records=" + _lastMotionDropped);
                }
            }
        }

        public override void OnLateUpdate()
        {
            if (_phase2RepairActive) WarrickPhase2TargetRepair.Tick();
            if (_motionActive) BossMotionTrace.LateUpdate();
        }

        private void StartMotionTrace()
        {
            string path = Path.Combine(Path.GetFullPath(_motionTraceDirectory.Value),
                "boss-motion-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-pid" + Process.GetCurrentProcess().Id + ".jsonl");
            try
            {
                _motionSink = new MotionTraceSink(path);
                var start = _motionSink.NewRecord("trace_started");
                start.note = "CrucibleUnlock 0.9.5-husk-hud-candidate; build 22928553; hook-free motion sampling; ritual/progression hooks logged separately; phase2 target repair enabled=" + _repairWarrickPhase2Target.Value + "; per-entity action target application reported in loader log; raw/interpolated/simulation-frame/teleport unknown";
                _motionSink.TryWrite(start);
                BossMotionTrace.Install(_motionSink, message => LoggerInstance.Msg(message), message => LoggerInstance.Error(message));
                _motionActive = true;
                LoggerInstance.Msg("[boss-motion] JSONL path: " + path);
            }
            catch (Exception error)
            {
                _motionSink?.Dispose();
                _motionActive = false;
                LoggerInstance.Error("[boss-motion] logging setup failed; unlock remains active: " + error);
            }
        }

        public override void OnApplicationQuit()
        {
            _phase2RepairActive = false;
            WarrickPhase2TargetRepair.StopWithoutUnityReads();
            if (_runtimeActive) RuntimeUnlock.Flush();
            if (_motionSink != null)
            {
                BossMotionTrace.StopSampling();
                var end = _motionSink.NewRecord("trace_stopped");
                end.note = "application quit; dropped=" + _motionSink.DroppedCount;
                _motionSink.TryWrite(end);
                _motionActive = false;
                _motionSink.Dispose();
                LoggerInstance.Msg("[boss-motion] written=" + _motionSink.WrittenCount + " dropped=" + _motionSink.DroppedCount + " writerError=" + (_motionSink.Error ?? "none"));
            }
            // Process exit releases the detours. Avoid touching IL2CPP during teardown.
            LoggerInstance.Msg("[session] application quit; temporary unlock ends with this process");
        }

        // ---------------- 解锁流程 ----------------

        private void RunUnlockFlow()
        {
            string mode = (_mode.Value ?? "probe").Trim().ToLowerInvariant();
            if (mode == "probe")
            {
                LoggerInstance.Msg("[unlock] mode=probe：只做探测，不解析也不调用任何解锁接口。");
                return;
            }
            if (mode != "unlock" && mode != "unlock-dry")
            {
                LoggerInstance.Error($"[unlock] 未知 mode='{_mode.Value}'；可选 probe / unlock-dry / unlock。本次不做任何事。");
                return;
            }

            UnlockPlan plan = LoadPlan(mode);
            plan.Mode = mode;
            string markerPath = Path.Combine(_baseDirectory, MarkerFileName);
            ILog log = new MelonLog(LoggerInstance);

            LoggerInstance.Msg($"[unlock] 计划：frame={plan.Frame?.Type}.{plan.Frame?.StaticMember}[{string.Join(".", plan.Frame?.Path ?? Array.Empty<string>())}]");
            LoggerInstance.Msg($"[unlock] 计划：read={plan.Read?.Type}.{plan.Read?.Method}  write={plan.Write?.Type}.{plan.Write?.Method}  confirm_write={plan.ConfirmWrite}");
            LoggerInstance.Msg($"[unlock] 完成标记文件：{markerPath}（存在={File.Exists(markerPath)}）");

            UnlockOutcome outcome = UnlockRunner.Run(
                plan,
                AppDomain.CurrentDomain.GetAssemblies(),
                log,
                () => File.Exists(markerPath),
                () => WriteMarker(markerPath, plan));

            LoggerInstance.Msg($"[unlock] 结果：frame={outcome.FrameResolved} marked={outcome.AlreadyMarked} read={outcome.ReadSucceeded}({outcome.ReadValue}) " +
                               $"alreadyCompleted={outcome.AlreadyCompleted} writeAttempted={outcome.WriteAttempted} writeOk={outcome.WriteSucceeded} verify={outcome.VerifyValue} marker={outcome.MarkerWritten}");
            if (!string.IsNullOrEmpty(outcome.Error)) LoggerInstance.Error($"[unlock] 错误：{outcome.Error}");
        }

        private void WriteMarker(string path, UnlockPlan plan)
        {
            try
            {
                string json = JsonSerializer.Serialize(new
                {
                    written_utc = DateTime.UtcNow.ToString("o"),
                    version = "0.4.0",
                    step_guid = plan.StepGuid,
                    prime_state_guid = plan.PrimeStateGuid,
                    completed_values = plan.CompletedValues,
                    note = "本文件表示第二试炼解锁已按计划处理过；删除它不会撤销游戏内进度。",
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                LoggerInstance.Msg($"[unlock] 已写完成标记：{path}");
            }
            catch (Exception error)
            {
                LoggerInstance.Error($"[unlock] 写完成标记失败（不影响游戏内状态）：{error.Message}");
            }
        }

        private UnlockPlan LoadPlan(string mode)
        {
            string path = Path.Combine(_baseDirectory, PlanFileName);
            if (File.Exists(path))
            {
                try
                {
                    UnlockPlan loaded = JsonSerializer.Deserialize<UnlockPlan>(
                        File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (loaded != null)
                    {
                        LoggerInstance.Msg($"[unlock] 已读取计划文件：{path}");
                        return loaded;
                    }
                    LoggerInstance.Warning($"[unlock] 计划文件解析为空，改用内置默认值：{path}");
                }
                catch (Exception error)
                {
                    LoggerInstance.Error($"[unlock] 计划文件解析失败，改用内置默认值：{error.Message}");
                }
            }
            else
            {
                LoggerInstance.Warning($"[unlock] 未找到计划文件（{path}），使用内置默认值；内置值与当前版本是否匹配需用探测结果核对。");
            }
            return DefaultPlan(mode);
        }

        /// <summary>内置默认计划：名字来自既有逆向结论，**必须**用 probe 结果核对后再用于 unlock。</summary>
        private static UnlockPlan DefaultPlan(string mode)
        {
            return new UnlockPlan
            {
                Mode = mode,
                ConfirmWrite = false,
                StepGuid = StepGuid,
                PrimeStateGuid = PrimeStateGuid,
                CompletedValues = new[] { "2" },
                Frame = new FramePlan
                {
                    Type = "QuantumRunner",
                    StaticMember = "Default",
                    Path = new[] { "Game", "Frames", "Predicted" },
                },
                Read = new CallPlan
                {
                    Type = "Quantum.PrimeStatesAPI",
                    Method = "GetValue",
                    Args = new[] { "frame", "guid64" },
                },
                Write = new CallPlan
                {
                    Type = "Quantum.QuestAPI",
                    Method = "CompleteQuestStep",
                    Args = new[] { "frame", "guid64" },
                },
            };
        }

        // ---------------- 探测 ----------------

        private void ProbeIl2CppAssemblies()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            LoggerInstance.Msg($"[probe] 已加载程序集 {assemblies.Length} 个");

            List<Type> allTypes = new List<Type>();
            List<string> interestingAssemblies = new List<string>();
            foreach (Assembly assembly in assemblies)
            {
                string name = assembly.GetName().Name ?? "";
                if (name.IndexOf("Quantum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Il2Cpp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("CERIMAL", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    interestingAssemblies.Add(name);
                }
                allTypes.AddRange(ReflectionPlan.SafeTypes(assembly));
            }
            interestingAssemblies.Sort(StringComparer.OrdinalIgnoreCase);
            LoggerInstance.Msg($"[probe] 相关程序集: {(interestingAssemblies.Count == 0 ? "(无)" : string.Join(", ", interestingAssemblies))}");
            LoggerInstance.Msg($"[probe] 可见类型总数: {allTypes.Count}");

            foreach (string fullName in ProbeTypes)
            {
                Type found = allTypes.FirstOrDefault(t => t != null && (t.FullName == fullName || t.FullName == "Il2Cpp" + fullName));
                if (found == null)
                {
                    LoggerInstance.Warning($"[probe] {fullName} -> 未找到");
                    continue;
                }

                LoggerInstance.Msg($"[probe] {fullName} -> {found.FullName}，程序集 {found.Assembly.GetName().Name}（方法 {ReflectionPlan.SafeMethods(found).Count()} 个）");
                bool logAll = LogAllMethods.Contains(fullName);
                foreach (MethodInfo method in ReflectionPlan.SafeMethods(found))
                {
                    if (!logAll && !MethodKeywords.Any(k => method.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    LoggerInstance.Msg($"        {ReflectionPlan.Describe(method)}");
                }
            }

            List<Type> swept = allTypes
                .Where(t => t != null && t.FullName != null &&
                            SweepNameParts.Any(p => t.FullName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            LoggerInstance.Msg($"[probe] 名字含 {string.Join("/", SweepNameParts)} 的类型 {swept.Count} 个，列出前 {Math.Min(TypeSweepLimit, swept.Count)} 个：");
            foreach (Type type in swept.Take(TypeSweepLimit))
            {
                string markers = string.Join(",", ReflectionPlan.SafeMethods(type)
                    .Where(m => m.Name.IndexOf("Complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                m.Name.IndexOf("Condition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                m.Name.IndexOf("Offering", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                m.Name.IndexOf("Playlist", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => m.Name)
                    .Distinct()
                    .Take(6));
                LoggerInstance.Msg($"        {type.FullName}{(markers.Length > 0 ? "   [" + markers + "]" : "")}");
            }
            if (swept.Count > TypeSweepLimit) LoggerInstance.Msg($"[probe] （其余 {swept.Count - TypeSweepLimit} 个未列出）");

            LoggerInstance.Msg("[probe] 类型探测完成。");
        }

        private void ProbeFrameAccess()
        {
            LoggerInstance.Msg("[probe/frame] 开始探测 Frame 入口（阶段二需要）");
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Type> allTypes = new List<Type>();
            foreach (Assembly assembly in assemblies) allTypes.AddRange(ReflectionPlan.SafeTypes(assembly));

            foreach (string fullName in FrameEntryTypes)
            {
                Type found = allTypes.FirstOrDefault(t => t != null && (t.FullName == fullName || t.FullName == "Il2Cpp" + fullName));
                if (found == null)
                {
                    LoggerInstance.Msg($"[probe/frame] {fullName} -> 未找到");
                    continue;
                }

                LoggerInstance.Msg($"[probe/frame] {fullName} -> 程序集 {found.Assembly.GetName().Name}");
                int shown = 0;
                foreach (string line in DescribeKeyMembers(found))
                {
                    LoggerInstance.Msg($"        {line}");
                    if (++shown >= MembersPerType) { LoggerInstance.Msg("        …"); break; }
                }
            }

            List<Type> swept = allTypes
                .Where(t => t != null && t.FullName != null &&
                            RunnerSweepNameParts.Any(p => t.FullName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            LoggerInstance.Msg($"[probe/frame] 名字含 {string.Join("/", RunnerSweepNameParts)} 的类型 {swept.Count} 个，列出前 {Math.Min(RunnerSweepLimit, swept.Count)} 个：");
            foreach (Type type in swept.Take(RunnerSweepLimit))
            {
                string statics = string.Join(",", DescribeKeyMembers(type)
                    .Where(l => l.StartsWith("static", StringComparison.Ordinal))
                    .Take(4));
                LoggerInstance.Msg($"        {type.FullName}{(statics.Length > 0 ? "   [" + statics + "]" : "")}");
            }
            if (swept.Count > RunnerSweepLimit) LoggerInstance.Msg($"[probe/frame] （其余 {swept.Count - RunnerSweepLimit} 个未列出）");

            LoggerInstance.Msg("[probe/frame] 完成。");
        }

        private static IEnumerable<string> DescribeKeyMembers(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!RunnerMemberKeywords.Any(k => property.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                yield return $"static prop {property.Name}: {property.PropertyType.Name}";
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!RunnerMemberKeywords.Any(k => field.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                yield return $"static field {field.Name}: {field.FieldType.Name}";
            }
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!RunnerMemberKeywords.Any(k => property.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                yield return $"prop {property.Name}: {property.PropertyType.Name}";
            }
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!RunnerMemberKeywords.Any(k => method.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                yield return $"static {ReflectionPlan.Describe(method)}";
            }
        }
    }
}
