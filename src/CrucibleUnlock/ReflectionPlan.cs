using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CrucibleUnlock
{
    /// <summary>日志抽象：插件里接 MelonLoader 日志，自测里接控制台。核心逻辑不依赖任何加载器或游戏类型。</summary>
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    /// <summary>取当前 Quantum Frame 的路径描述（可配置，避免写死本作集成方式）。</summary>
    public sealed class FramePlan
    {
        public string Type { get; set; }
        public string StaticMember { get; set; }
        public string[] Path { get; set; } = Array.Empty<string>();
    }

    /// <summary>一次模拟层调用的描述。</summary>
    public sealed class CallPlan
    {
        public string Type { get; set; }
        public string Method { get; set; }
        /// <summary>实参来源标记：frame / guid64 / methodinfo / zero。</summary>
        public string[] Args { get; set; } = Array.Empty<string>();
    }

    /// <summary>解锁计划：全部来自配置文件，这样探测结果回来只需改配置，不改代码。</summary>
    public sealed class UnlockPlan
    {
        public string Mode { get; set; } = "unlock-dry";
        public FramePlan Frame { get; set; } = new FramePlan();
        public CallPlan Read { get; set; } = new CallPlan();
        public CallPlan Write { get; set; } = new CallPlan();
        /// <summary>读到的值等于其中任意一个即视为"已完成"（字符串比较）。</summary>
        public string[] CompletedValues { get; set; } = new[] { "2" };
        public bool ConfirmWrite { get; set; }
        public long StepGuid { get; set; }
        public long PrimeStateGuid { get; set; }
    }

    public sealed class UnlockOutcome
    {
        public bool FrameResolved { get; set; }
        public bool AlreadyMarked { get; set; }
        public bool ReadSucceeded { get; set; }
        public string ReadValue { get; set; }
        public bool AlreadyCompleted { get; set; }
        public bool WriteAttempted { get; set; }
        public bool WriteSucceeded { get; set; }
        public string WriteResult { get; set; }
        public string VerifyValue { get; set; }
        public bool MarkerWritten { get; set; }
        public string Error { get; set; }
    }

    public static class ReflectionPlan
    {
        public static Type FindType(IEnumerable<Assembly> assemblies, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in SafeTypes(assembly))
                {
                    if (type == null) continue;
                    if (type.FullName == name || type.Name == name) return type;
                }
            }
            return null;
        }

        /// <summary>先按 StaticMember 取静态成员，再沿 Path 逐级取实例成员。</summary>
        public static object ResolveFrame(FramePlan plan, IEnumerable<Assembly> assemblies, ILog log, out string trace)
        {
            StringBuilder steps = new StringBuilder();
            trace = "";
            if (plan == null || string.IsNullOrWhiteSpace(plan.Type))
            {
                log.Error("[unlock] 计划里没有 frame.type，无法取 Frame");
                return null;
            }

            Type type = FindType(assemblies, plan.Type);
            if (type == null)
            {
                log.Error($"[unlock] 找不到 frame.type = {plan.Type}");
                return null;
            }
            steps.Append(type.FullName);

            object current;
            if (string.IsNullOrWhiteSpace(plan.StaticMember))
            {
                log.Error("[unlock] 计划里没有 frame.static_member（本作需要给出静态入口，例如 Default）");
                return null;
            }
            current = ReadMember(type, null, plan.StaticMember, log);
            steps.Append('.').Append(plan.StaticMember);
            if (current == null)
            {
                trace = steps.ToString();
                log.Error($"[unlock] 静态入口 {plan.Type}.{plan.StaticMember} 取值为 null");
                return null;
            }

            foreach (string member in plan.Path ?? Array.Empty<string>())
            {
                object next = ReadMember(current.GetType(), current, member, log);
                steps.Append('.').Append(member);
                if (next == null)
                {
                    trace = steps.ToString();
                    log.Error($"[unlock] 取值链在 {member} 处中断");
                    return null;
                }
                current = next;
            }

            trace = steps.ToString();
            log.Info($"[unlock] Frame 取得：{trace} -> {current.GetType().FullName}");
            return current;
        }

        private static object ReadMember(Type type, object instance, string member, ILog log)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            PropertyInfo property = type.GetProperty(member, flags);
            if (property != null)
            {
                try { return property.GetValue(property.GetGetMethod(true).IsStatic ? null : instance); }
                catch (Exception error) { log.Error($"[unlock] 读属性 {type.Name}.{member} 失败：{error.GetType().Name} {error.Message}"); return null; }
            }
            FieldInfo field = type.GetField(member, flags);
            if (field != null)
            {
                try { return field.GetValue(field.IsStatic ? null : instance); }
                catch (Exception error) { log.Error($"[unlock] 读字段 {type.Name}.{member} 失败：{error.GetType().Name} {error.Message}"); return null; }
            }
            log.Error($"[unlock] {type.FullName} 上找不到成员 {member}");
            return null;
        }

        /// <summary>按名字与参数个数挑方法；0 个或多于 1 个候选都视为失败（宁可不做，不猜）。</summary>
        public static MethodInfo SelectMethod(Type type, string name, int argCount, ILog log)
        {
            if (type == null || string.IsNullOrWhiteSpace(name)) return null;
            List<MethodInfo> all = SafeMethods(type).Where(m => m.Name == name).ToList();
            if (all.Count == 0)
            {
                log.Error($"[unlock] {type.FullName} 上没有名为 {name} 的方法");
                return null;
            }
            List<MethodInfo> exact = all.Where(m => m.GetParameters().Length == argCount).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count == 0)
            {
                log.Error($"[unlock] {type.FullName}.{name} 没有 {argCount} 个参数的版本；候选：{string.Join(" | ", all.Select(Describe))}");
                return null;
            }
            log.Error($"[unlock] {type.FullName}.{name} 有 {exact.Count} 个 {argCount} 参数版本，拒绝猜：{string.Join(" | ", exact.Select(Describe))}");
            return null;
        }

        public static string Describe(MethodInfo method)
        {
            string parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            return $"{method.Name}({parameters}) {(method.IsStatic ? "static" : "instance")} -> {method.ReturnType.Name}";
        }

        /// <summary>按标记填参数；任何无法满足的参数都让整次调用失败。</summary>
        public static object[] BuildArguments(MethodInfo method, IDictionary<string, object> values, ILog log)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                string token = TokenFor(parameter, values);
                if (token == null)
                {
                    log.Error($"[unlock] 参数 #{i}（{parameter.ParameterType.Name} {parameter.Name}）没有对应的取值来源，放弃调用");
                    return null;
                }
                object value;
                if (!values.TryGetValue(token, out value))
                {
                    log.Error($"[unlock] 取值来源 {token} 在当前上下文没有提供值，放弃调用");
                    return null;
                }
                if (value != null && !parameter.ParameterType.IsInstanceOfType(value))
                {
                    if (parameter.ParameterType == typeof(long) && value is long) { /* ok */ }
                    else
                    {
                        log.Error($"[unlock] 参数 #{i} 需要 {parameter.ParameterType.Name}，实际给的是 {value.GetType().Name}，放弃调用");
                        return null;
                    }
                }
                if (parameter.ParameterType == typeof(long) && value is long longValue) { args[i] = longValue; continue; }
                args[i] = value;
            }
            return args;
        }

        private static string TokenFor(ParameterInfo parameter, IDictionary<string, object> values)
        {
            string typeName = parameter.ParameterType.Name;
            string name = parameter.Name ?? "";
            if (values.ContainsKey("frame") && (typeName.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               values["frame"]?.GetType() == parameter.ParameterType))
                return "frame";
            if (values.ContainsKey("guid64") && (parameter.ParameterType == typeof(long) || parameter.ParameterType == typeof(ulong)))
                return "guid64";
            if (typeName.IndexOf("MethodInfo", StringComparison.OrdinalIgnoreCase) >= 0) return "methodinfo";
            if (parameter.HasDefaultValue) return typeName == "Int32" ? "zero" : "methodinfo";
            return null;
        }

        public static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error) { return error.Types.Where(t => t != null); }
            catch (Exception) { return Array.Empty<Type>(); }
        }

        public static IEnumerable<MethodInfo> SafeMethods(Type type)
        {
            try { return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance); }
            catch (Exception) { return Array.Empty<MethodInfo>(); }
        }
    }
}
