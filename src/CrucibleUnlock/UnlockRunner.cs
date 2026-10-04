using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CrucibleUnlock
{
    /// <summary>
    /// 解锁执行流程（与加载器、游戏类型完全解耦，可在离线自测里用假类型跑通）。
    ///
    /// 固定行为：
    ///   - 先看标记：已标记过就直接结束，绝不重复写；
    ///   - 先读后写：读不到状态就不写；
    ///   - unlock-dry 只把"准备怎么调"写进日志，不调用写接口；
    ///   - unlock 模式要求 ConfirmWrite=true，且最多写一次；失败不重试；
    ///   - 写完再读一次做验证，验证通过才落标记。
    /// </summary>
    public static class UnlockRunner
    {
        public static UnlockOutcome Run(
            UnlockPlan plan,
            IEnumerable<Assembly> assemblies,
            ILog log,
            Func<bool> markerExists,
            Action writeMarker)
        {
            UnlockOutcome outcome = new UnlockOutcome();
            List<Assembly> assemblyList = assemblies.ToList();

            string mode = (plan?.Mode ?? "unlock-dry").Trim().ToLowerInvariant();
            if (mode != "unlock" && mode != "unlock-dry")
            {
                outcome.Error = $"mode 必须是 unlock 或 unlock-dry，当前为 '{plan?.Mode}'";
                log.Error($"[unlock] {outcome.Error}");
                return outcome;
            }

            if (markerExists())
            {
                outcome.AlreadyMarked = true;
                log.Info("[unlock] 已存在完成标记，跳过（不会重复写入）");
                return outcome;
            }

            object frame = ReflectionPlan.ResolveFrame(plan.Frame, assemblyList, log, out string frameTrace);
            if (frame == null)
            {
                outcome.Error = "未能取得 Frame";
                log.Error($"[unlock] {outcome.Error}（轨迹：{frameTrace}）");
                return outcome;
            }
            outcome.FrameResolved = true;

            Dictionary<string, object> values = new Dictionary<string, object>
            {
                { "frame", frame },
                { "guid64", plan.StepGuid },
                { "methodinfo", null },
                { "zero", 0 },
            };

            string readValue = InvokeValueCall(plan.Read, assemblyList, values, log, out string readError);
            if (readError != null)
            {
                outcome.Error = readError;
                log.Error($"[unlock] 读状态失败：{readError}");
                return outcome;
            }
            outcome.ReadSucceeded = true;
            outcome.ReadValue = readValue;
            log.Info($"[unlock] 当前状态读数：{readValue}");

            bool completed = (plan.CompletedValues ?? Array.Empty<string>())
                .Any(v => string.Equals(v, readValue, StringComparison.OrdinalIgnoreCase));
            if (completed)
            {
                outcome.AlreadyCompleted = true;
                log.Info($"[unlock] 读数 {readValue} 属于已完成取值，无需写入；落标记以免每次启动重复检查");
                writeMarker();
                outcome.MarkerWritten = true;
                return outcome;
            }

            if (mode == "unlock-dry")
            {
                log.Info("[unlock] unlock-dry：以下写入**不会**执行，仅供核对");
                LogCallPlan(plan.Write, assemblyList, values, log);
                return outcome;
            }

            if (!plan.ConfirmWrite)
            {
                outcome.Error = "unlock 模式但 confirm_write=false，拒绝写入";
                log.Error($"[unlock] {outcome.Error}");
                return outcome;
            }

            outcome.WriteAttempted = true;
            string writeResult = InvokeValueCall(plan.Write, assemblyList, values, log, out string writeError);
            if (writeError != null)
            {
                outcome.Error = writeError;
                log.Error($"[unlock] 写入失败，不再重试：{writeError}");
                return outcome;
            }
            outcome.WriteSucceeded = true;
            outcome.WriteResult = writeResult;

            string verifyValue = InvokeValueCall(plan.Read, assemblyList, values, log, out string verifyError);
            if (verifyError != null)
            {
                outcome.Error = "写入后复读失败：" + verifyError;
                log.Error($"[unlock] {outcome.Error}");
                return outcome;
            }
            outcome.VerifyValue = verifyValue;

            bool verified = (plan.CompletedValues ?? Array.Empty<string>())
                .Any(v => string.Equals(v, verifyValue, StringComparison.OrdinalIgnoreCase));
            if (!verified)
            {
                outcome.Error = $"写入后复读仍为 {verifyValue}，不落标记（交由人工判断）";
                log.Error($"[unlock] {outcome.Error}");
                return outcome;
            }

            writeMarker();
            outcome.MarkerWritten = true;
            log.Info($"[unlock] 写入成功并复读确认：{verifyValue}；已落标记，后续启动不再写入");
            return outcome;
        }

        private static string InvokeValueCall(CallPlan call, List<Assembly> assemblies, IDictionary<string, object> values, ILog log, out string error)
        {
            error = null;
            if (call == null || string.IsNullOrWhiteSpace(call.Type) || string.IsNullOrWhiteSpace(call.Method))
            {
                error = "调用计划缺少 type/method";
                return null;
            }

            Type type = ReflectionPlan.FindType(assemblies, call.Type);
            if (type == null)
            {
                error = $"找不到类型 {call.Type}";
                return null;
            }

            string[] tokens = call.Args ?? Array.Empty<string>();
            MethodInfo method = ReflectionPlan.SelectMethod(type, call.Method, tokens.Length, log);
            if (method == null)
            {
                error = $"{call.Type}.{call.Method} 未能唯一定位（参数个数 {tokens.Length}）";
                return null;
            }

            // 按 tokens 顺序把值排好：tokens 说明"第 i 个参数从哪来"
            Dictionary<string, object> callValues = new Dictionary<string, object>(values);
            object[] args = new object[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                object value;
                if (!callValues.TryGetValue(tokens[i], out value))
                {
                    error = $"参数 #{i} 的来源 '{tokens[i]}' 没有值";
                    return null;
                }
                args[i] = value;
            }

            // 再做一次类型适配检查（用真实签名）
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null) continue;
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]) && parameters[i].ParameterType != args[i].GetType())
                {
                    error = $"参数 #{i} 需要 {parameters[i].ParameterType.Name}，实际 {args[i].GetType().Name}";
                    return null;
                }
            }

            try
            {
                object instance = method.IsStatic ? null : values["frame"];
                object result = method.Invoke(instance, args);
                log.Info($"[unlock] 已调用 {ReflectionPlan.Describe(method)}");
                return result == null ? "(null)" : result.ToString();
            }
            catch (TargetInvocationException error2)
            {
                Exception inner = error2.InnerException ?? error2;
                error = $"{method.Name} 抛出 {inner.GetType().Name}: {inner.Message}";
                return null;
            }
            catch (Exception error3)
            {
                error = $"{method.Name} 调用失败 {error3.GetType().Name}: {error3.Message}";
                return null;
            }
        }

        private static void LogCallPlan(CallPlan call, List<Assembly> assemblies, IDictionary<string, object> values, ILog log)
        {
            if (call == null || string.IsNullOrWhiteSpace(call.Type))
            {
                log.Warn("[unlock] 写入计划为空");
                return;
            }
            Type type = ReflectionPlan.FindType(assemblies, call.Type);
            if (type == null)
            {
                log.Warn($"[unlock] 计划写入类型 {call.Type} 在当前程序集里找不到");
                return;
            }
            string[] tokens = call.Args ?? Array.Empty<string>();
            MethodInfo method = ReflectionPlan.SelectMethod(type, call.Method, tokens.Length, log);
            if (method == null)
            {
                log.Warn($"[unlock] 计划写入 {call.Type}.{call.Method} 未能唯一定位");
                return;
            }
            string rendered = string.Join(", ", tokens.Select((t, i) => $"{method.GetParameters()[i].ParameterType.Name} <- {t}" +
                (t == "guid64" ? $"=0x{plan_guid_hex(values)}" : "")));
            log.Info($"[unlock] 计划调用：{ReflectionPlan.Describe(method)}  实参：{rendered}");
        }

        private static string plan_guid_hex(IDictionary<string, object> values)
        {
            object value;
            if (values.TryGetValue("guid64", out value) && value is long guid) return guid.ToString("x");
            return "?";
        }
    }
}
