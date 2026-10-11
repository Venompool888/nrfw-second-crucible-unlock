# R17 人物发声：出货声线断链与源 NPC 声池回退

2026-10-11。基线为最新 `origin/main` `7db4e4a` 加本次已安装 R15 的依赖 `36b491b`，任务分支 `feature/third-trial-presentation-repair-20261011`，未纳入 R16。所有核验只读取本地配置、资源与 PE/IL2CPP 元数据；没有启动、操作游戏，没有加载执行游戏 DLL，没有改写源游戏文件、存档、配置或日志。以下改动是离线候选，尚未由用户实机确认。

## 结论与实际改动

R15 正确选到了 Inquisitor／DeadTide 的声线 switch，但这些 switch 对应的原生 Wwise 动态声音决策树指向本地未出货的音频层级对象。Switch GUID、声池定义和 `PrimarySoundHost` 存在不能证明后面的音频对象存在，更不能证明播放成功。

本轮候选只在七个缺声私有 profile 中，将**声音选择**回退到该 profile 原始源 NPC 的官方可用声池。战斗使用的 `CharacterType`、Faction、NPC、控制器、武器和世界数据没有因此改成 donor 身份。第 4／5／6 房仍使用原有 `HumanoidEnemy(21)`／Raiders 声池。

这恢复的是已有源 NPC 的战斗人物发声接线，**不是恢复 Inquisitor／DeadTide 专属原始录音**。缺少的专属音频层级不能靠给同一个 switch 再赋一次值恢复；本轮也没有新造录音或改写 Wwise 声库。

| 房 | 私有 profile | 战斗身份保持 | 本轮声音选择身份 | 官方声池 |
|---|---|---|---|---|
| 1 | inquisitor_halberd | Inquisitor 31 | 源 Bandit 5 | voicePoolRaiders |
| 2 | inquisitor_greatsword | InquisitorHeavy 118 | 源 Bandit 5 | voicePoolRaiders |
| 3 | inquisitor_bruiser | InquisitorBruiser 119 | 源 Bruiser 47 | voicePoolBruiser |
| 4 | keeper_greatsword | HumanoidEnemy 21 | 原 21 | voicePoolRaiders |
| 5 | keeper_sword_shield_left | HumanoidEnemy 21 | 原 21 | voicePoolRaiders |
| 6 | keeper_sword_shield_right | HumanoidEnemy 21 | 原 21 | voicePoolRaiders |
| 7 | dead_tide_curved_sword | DeadTide 120 | 源 Bandit 5 | voicePoolRaiders |
| 8 | dead_tide_great_curved_sword | DeadTide 120 | 源 Bandit 5 | voicePoolRaiders |
| 9 | dead_tide_crossbow | DeadTide 120 | 源 Bandit 5 | voicePoolRaiders |
| 10 | dead_tide_bruiser | DeadTideBruiser 121 | 源 AxeBruiser 43 | voicePoolBruiserHeavy |

## 从身份选择追到实际播放

此前 R14 审计已经静态确认 `HumanoidEnemyView.OnBoundToEntity` 调用 `CharacterView.OnBoundToEntity`，后者按 `FulfillVoiceAssignment → SetAudioAttributes` 顺序运行。`FulfillVoiceAssignment#20909` 先检查 `CharacterView.CustomVoiceSwitch(+0x258)` 的 validity；有效时提前返回，否则读取当前 Quantum `NpcAttachmentComponent.CharacterType`，通过全局 `WwiseDefinitionsVoices` 选声池，选择 switch 并写回 `CustomVoiceSwitch`。原生方法没有直接用私有 Unity NPC wrapper GUID 选人物声线。

本轮继续追踪的静态播放链是：

1. `WwiseVoiceAnimator.OnStartPlayback#9630 → PostVoiceEvent#9637` 动态解析有效 actor／`PrimarySoundHost`，按 gender、VocalizationType、CharacterView.CustomVoiceSwitch、ArmorAccessoryMaterial 组成四个参数。
2. 每个 `SwitchReference` 通过 `MediaItemReference.GetId#212564`，由 `WwiseMediaLookup.GetShortId#212027` 将 GUID 解析为 Wwise ID。GUID 能解析只能说明 manifest 登记存在。
3. 普通声音轨道走 `DialogueEventReference.Post#212582 → Post#212584 → WwiseEventSystem.Fire#212298 → DialogueEventSoundHandle.Play#212349 → SoundHandleProtected.Play#212318`；FireAndForget 则经官方 ArtificialSoundHost 分配／放置／PlayAndRelease 后进入同一 Play 路径。
4. `SoundHandleProtected.Play` 的本地 PE 导入字符串明确指向 `CSharp_ResolveDialogueEvent__SWIG_1`，然后使用 `DynamicSequenceLockPlaylist`、`AkPlaylist_Enqueue`、`DynamicSequenceUnlockPlaylist`、`DynamicSequencePlay`。这条链最终需要决策树叶子指定的 audio-node ID 对应实际 Wwise 对象。
5. Wwise `Main.pck` 保存 `aiVoice(2750468159)` 与 `VoiceEfforts(2370845206)` 的决策树。前者参数为 VoiceGender／VoiceAINotification／VoiceCharacter；后者为 VoiceGender／VocalizationType／VoiceCharacter／ArmorAccessoryMaterial。目标节点可以分布在其他声音 bank，必须检查所有本地 bank，不能只看 Main。

本轮没有运行以上方法；地址、调用关系与导入字符串均从本地 PE 字节静态读取。完整导出见 `artifacts/third-launch/r17-repair/voice/native-event-chain.json`、`.txt` 及 `native-dialogue-api-imports.json`。PE SHA256 由项目既有 `native_probe` 核对匹配本地 Steam build `22928553`；旧选择链补充见 R14 `native-voice-selection.txt`。

## 本地声库里的断链证据

解析 Windows 与 HandheldPC 两平台全部 **72 个 `.pck`**，按 AKPK 头中的 bank 索引定位 BKHD/HIRC，逐项验证长度／边界；PCK 中去重得到 **195,955 个 HIRC 对象 ID**。全部独立 `.bnk` 仅为两平台 Init bank，各有 690 个 HIRC 对象，已另外核对两者均没有任何上述缺失的声线目标（`init-target-absence.json`）。不存在另一套未扫到的专属 voice bank。本次确证的是这些本地出货声音包内缺少目标，不断言开发者从未录制过声音。

| VoiceCharacter switch | aiVoice 唯一目标数 / 缺失 | VoiceEfforts 唯一目标数 / 缺失 |
|---|---:|---:|
| inquisitorA | 8 / 8 | 46 / 46 |
| inquisitorB | 8 / 8 | 47 / 47 |
| inquisitorBruiser | 4 / 4 | 23 / 23 |
| deadtideA | 8 / 8 | 46 / 46 |
| deadtideB | 8 / 8 | 48 / 48 |
| deadtideBruiser | 4 / 4 | 23 / 23 |
| RaiderA | 5 / 0 | 50 / 0 |
| RaiderB | 5 / 0 | 54 / 0 |
| RaiderC | 4 / 0 | 51 / 0 |
| RaiderD | 4 / 0 | 52 / 0 |
| Bruiser | 2 / 0 | 24 / 0 |
| AxeBruiser | 2 / 0 | 25 / 0 |

计数口径：先在每个 `(event, VoiceCharacter)` 内去重 audio-node ID，再加总，六个缺声 switch 为 **40 + 233 = 273 次唯一组内目标引用，全缺失**。不同声线／事件间会共享目标，因此进一步跨事件、跨声线完全去重后是 **242 个缺失目标 ID**，不是 273 个不同对象。六个 switch 在树中覆盖 **457 个完整参数路径叶子**；有多套 gender、vocalization、armor 路径，不等于 457 次实际播放。

`SoundbanksInfo.json` 中 Character bank 有 411 个名称包含 inquis 的媒体，全是护甲／移动等相关素材，名称含 vox 的数量为 0；全部 bank 的媒体名称没有 deadtide。命名搜索只是辅助证据，真正的缺失结论来自决策树目标 ID 与完整 HIRC 对象索引的对应。

独立交叉验证使用 [wwiser 原始解析器](https://github.com/bnnm/wwiser/blob/master/wwiser/parser/wparser.py)，没有调用游戏／Wwise 运行库。由精确出货 HIRC 字节构造离线分析 bank，wwiser 解析出的 `aiVoice` 222 个叶子、`VoiceEfforts` 1,984 个叶子的全部目标 ID 与自建解析器逐项完全相同。这既验证了 Wwise 145 版本树布局，也避免把字段错位误认成缺失对象。

官方 donor 声线对照继续追踪：238 个根目标的全部子层级闭包有 3,076 个对象、2,737 个 Sound 对象，其中 **2,693 个 decoder source 的真实内存 DATA 或 AKPK stream 数据字节均可定位且长度／边界有效**，另 44 个是生成器 source。没有缺失 decoder 媒体。生成器和部分动作分支可能本来就是 authored silence，因此“闭包存在”也不等于每次动作都必定有人声。

上述 donor 是源 NPC 的原身份，不是随机挑一个能响的 ID：本地 `WwiseDefinitionsVoices.CharacterAssignments` 证实 5→Raiders、47→Bruiser、43→BruiserHeavy；R14 `voice-containers.json` 和 `source-voice-records.json` 留有对应源 NPC 的 CharacterType。

## 与 R15 用户反馈和实际日志的对应

用户亲自试玩反馈为第 1／2／3／7／8／9／10 房人物声缺失，第 4／5／6 房正常。这个分组恰好对应专属声线目标全缺与 Raiders 声线完整的分组。

日志由 Luna 按项目分工分析，主代理核对关键证据：09:46–10:06Z 的用户十房会话与后来的 10:20–10:41Z 会话是独立会话。前一次完整 MelonLoader 运行日志缺失，不能用后一次日志取代本次全部十房的实际听感。后一次 R15 日志在第 1–9 房记录了 9 条 voice identity，`after=selected`，GUID 均符合各原始专属 family，host 非零；第 10 房没有该会话的 spawn／voice 记录。这些日志明确写 `presence-only=true`，只证实 switch／host 登记，不能证实声事件触发／声音可听。

两次会话还同时存在部分 Quantum View failed；第 3／5／9 房失败与后一次对应 voice identity 共存，第 5 房用户声正常。没有依据将这些 View failed 当作人物声缺失的共同根因。本轮具体修复来自原生路由与出货 audio-node 缺失证据，未将 presence-only 日志泛化为声音成功。

## 修复边界和离线回归

只修改 `src/CrucibleUnlock/EnemyRestorationVoiceRepair.cs` 以及其 managed tests。回退需要 exact profile Key、Index、预期 CharacterType、源 NpcTemplateGuid 同时匹配；入口继续要求当前私有 owner proof、当前 manager、绑定 entity、有效 catalog 槽位和与 profile 相同的实存 Quantum CharacterType。仅查官方 donor pool，池不存在／为空／选项无效则不强造 switch。

回退仍在原生 `FulfillVoiceAssignment` prefix 中赋入正常有效 switch，原生调用继续执行且 `SetAudioAttributes` 保持正常顺序。原有租约的同 bind 幂等、外部 switch 变更抑制、unbind／pool return／destroy／dispose 恢复、resolver stop 不读 Unity 等边界保持。纯 helper `GetVoicePoolType` 已加入 `PrepareHelpers`。日志增加 `voiceType` 与 `donor-fallback=true`，继续保留 `presence-only=true`，没有假报 audible success。

回归先在修复前的 R15 actual source 上执行新增需求：56 检查、15 个预期失败（七房仍选不可用原声线，及 donor 缺失处理／日志／幂等相关要求）。修复后同一组通过；随后补充不同 Key、无效 donor 和 views gate 三项，源 GUID 不匹配边界在原 56 项已覆盖，冻结版本 **59 检查、0 失败**。全部以纯 managed fakes 编译实际源码，未加载 Il2Cpp／Unity 游戏程序集。核心只编译成功，0 warning／0 error，没有执行核心插件 DLL。

可复现 RED 的历史源码和 56 项测试阶段已导出到 `voice/red-project/VoiceRepairRed.csproj`，重新运行得到 exit 1、同样 15 失败；GREEN 重新运行 actual-source tests 得到 exit 0、59 通过。证据分别为 `tests-red.json`、`tests-red-reproduced.json`、`tests-red-console.txt`、`tests-green.json`、`tests-green-console.txt`；源文件／测试 SHA256 见 `test-input-hashes.json`。

- 冻结 voice source SHA256：`74469457fec39a6d7475c2303eb26c38ed92369cec40e912a995ce55dd4cb145`。
- 冻结 Program.cs SHA256：`566a0b91ee159be81f83d3590c66281eb0536b71bbdc6beb8ac9c2bc04a78f1f`。
- Fakes.cs SHA256：`195798b6c47bd3f359f363f745e61e7a4461be662e5273d258453aa3fdb549bc`。

完整版本的后续构建／离线验证／打包由主代理统一执行，本声音回归不代替完整候选验证，也不代替用户实机听感验收。

## 证据、复现与仍未知事项

主要产物都位于本任务工作树 `artifacts/third-launch/r17-repair/voice`：

- `audit_dialogue.py` 与 `dialogue-routing.json`：72 个包及其 bank/HIRC 指纹、两棵决策树全部路径、12 声线分组目标覆盖。
- `verify_donor_media.py` 与 `donor-media-closure.json`：独立 wwiser 交叉解析及 donor 对象／媒体闭包。
- `native_event_chain.py` 与 `native-event-chain.json/.txt`、`native-dialogue-api-imports.json`：只读 PE 选择／播放链。
- `hash_inputs.py` 与 `audio-input-hashes.json`：72 个 PCK、2 个 Init、Windows SoundbanksInfo 共 75 个源文件全文件 SHA256，总计 5,058,527,101 字节，均为只读哈希。
- `record_test_provenance.py` 与 `test-input-hashes.json`：RED／GREEN 测试输入 snapshot 和冻结输入哈希。

Windows SoundbanksInfo SHA256 为 `8bf21799bd6907ad80a5524d9e2577209390df61a717115344f8a549bc308436`。wwiser 下载 ZIP SHA256 为 `39f867a3488708c21d61bd4eb1d736a9dc52f193c882582eefa5e0eacdd3bd11`，其源码仅用于静态解析，并留有 ZIP 供复现。解析全部在本项目目录输出；不会安装或播放媒体。

仍未知：候选运行时 donor bank residency、每个实际动作／AI 事件触发、最终 gender／armor 参数、动态 SoundHost 和 Wwise 实际播放结果，以及用户最终听感。这里只证明已存在、正确接出的可用 donor 路径并提供离线候选，不能把静态资源闭包与 managed 测试称为实机修复通过。

辅助交叉审计：第 10 房武器的 182 条 WwiseSoundAnimator 收敛 40 个 Event，出货 bank 有全部 40 Event→47 Action→44 target，其完整闭包 626 对象／445 Sound，缺失对象／decoder 媒体为 0（`weapon-event-closure.json`）。因此第 10 房武器音效缺失不能套用本人物声线的原音频对象缺失根因。
