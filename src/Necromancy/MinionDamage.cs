using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppQuantum;
using MelonLoader.NativeUtils;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // Public 22928553, Windows x64. ResolveDamage has three pointer arguments and MethodInfo*.
        // OnCollisionWithDamageReceiver takes two EntityRefs by value and two 24-byte vectors
        // indirectly under the Windows x64 ABI. Forward those addresses without remarshal.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate void ResolveDamageNative(IntPtr frame, Damage* damage, DamageResponse* response, IntPtr method);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProjectileImpactNative(IntPtr self, IntPtr frame, ulong projectile, ulong receiver,
            IntPtr hitPosition, IntPtr hitNormal, IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool ProxyLookupNative(IntPtr group, ProxyTypeFlags kind, out EntityRef entity, IntPtr method);

        private static unsafe readonly ResolveDamageNative ResolveDamageDetour = BeforeResolveRaisedDamage;
        private static readonly ProjectileImpactNative ProjectileImpactDetour = BeforeRaisedProjectileImpact;

        private static NativeHook<ResolveDamageNative> _resolveDamageHook;
        private static NativeHook<ProjectileImpactNative> _projectileImpactHook;
        private static ProxyLookupNative _proxyLookup;
        private static IntPtr _proxyLookupMethod;
        private static volatile bool _damageProtectionActive;
        private static int _damageProtectionLogs;

        private static readonly ConcurrentDictionary<(IntPtr Context, ulong Entity), ulong> RaisedDamageOwners = new();
        private static readonly ConcurrentDictionary<ulong, long> MinionLastDamage = new();

        private static unsafe void InstallRaisedMinionDamageProtection()
        {
            try
            {
                CheckDamageLayouts();
                using var process = Process.GetCurrentProcess();
                IntPtr gameAssembly = IntPtr.Zero;
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        gameAssembly = module.BaseAddress;
                        break;
                    }
                }

                if (IntPtr.Size != 8 || gameAssembly == IntPtr.Zero)
                    throw new InvalidOperationException("Windows x64 GameAssembly is required.");

                IntPtr resolve = FindDamageEntry(typeof(DamageResolverSystem), "ResolveDamage",
                    new[] { typeof(Frame), typeof(Damage).MakeByRefType(), typeof(DamageResponse).MakeByRefType() },
                    true, typeof(void), IntPtr.Add(gameAssembly, 0x5C0C170), out _);
                IntPtr impact = FindDamageEntry(typeof(ProjectileData), "OnCollisionWithDamageReceiver",
                    new[] { typeof(Frame), typeof(EntityRef), typeof(EntityRef),
                        typeof(Il2CppPhoton.Deterministic.FPVector3), typeof(Il2CppPhoton.Deterministic.FPVector3) },
                    false, typeof(void), IntPtr.Add(gameAssembly, 0x5B522A0), out _);
                IntPtr lookup = FindDamageEntry(typeof(PayloadProxyGroup), "TryGetProxyOfType",
                    new[] { typeof(ProxyTypeFlags), typeof(EntityRef).MakeByRefType() },
                    false, typeof(bool), IntPtr.Add(gameAssembly, 0x5DCB8C0), out _proxyLookupMethod);
                _proxyLookup = Marshal.GetDelegateForFunctionPointer<ProxyLookupNative>(lookup);

                _resolveDamageHook = new NativeHook<ResolveDamageNative>(resolve, Marshal.GetFunctionPointerForDelegate(ResolveDamageDetour));
                _projectileImpactHook = new NativeHook<ProjectileImpactNative>(impact, Marshal.GetFunctionPointerForDelegate(ProjectileImpactDetour));
                _resolveDamageHook.Attach();
                _projectileImpactHook.Attach();
                if (!_resolveDamageHook.IsHooked || _resolveDamageHook.TrampolineHandle == IntPtr.Zero ||
                    _resolveDamageHook.TrampolineHandle == resolve ||
                    !_projectileImpactHook.IsHooked || _projectileImpactHook.TrampolineHandle == IntPtr.Zero ||
                    _projectileImpactHook.TrampolineHandle == impact)
                    throw new InvalidOperationException("MelonLoader did not provide both damage trampolines.");

                _damageProtectionActive = true;
                Write("minion damage and projectile impact protection installed");
            }
            catch (Exception e)
            {
                StopRaisedMinionDamageProtection();
                Write("minion damage protection setup FAILED " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void StopRaisedMinionDamageProtection()
        {
            _damageProtectionActive = false;
            try { _projectileImpactHook?.Detach(); }
            catch (Exception e) { Error("minion-projectile-detach", e); }
            try { _resolveDamageHook?.Detach(); }
            catch (Exception e) { Error("minion-resolve-detach", e); }
        }

        private static unsafe void CheckDamageLayouts()
        {
            uint alignment = 0;
            IntPtr responseClass = Il2CppInterop.Runtime.Il2CppClassPointerStore<DamageResponse>.NativeClassPtr;
            if (responseClass == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(responseClass, ref alignment) != 72 ||
                sizeof(EntityRef) != 8 || sizeof(DamageResponse) != 72 ||
                Marshal.OffsetOf<DamageResponse>(nameof(DamageResponse.Type)).ToInt32() != 8 ||
                Marshal.OffsetOf<Damage>(nameof(Damage.HitEntity)).ToInt32() != 0x28 ||
                Marshal.OffsetOf<Damage>(nameof(Damage.Proxy)).ToInt32() != 0xb8 ||
                Marshal.OffsetOf<RaisedMinionComponent>(nameof(RaisedMinionComponent.Owner)).ToInt32() != 0x10 ||
                Marshal.OffsetOf<ProjectileComponent>(nameof(ProjectileComponent.InstigatorRef)).ToInt32() != 0x30 ||
                Marshal.OffsetOf<CascadeInstanceComponent>(nameof(CascadeInstanceComponent.Caster)).ToInt32() != 0x40)
                throw new InvalidOperationException("Damage and instigator layouts differ from public build 22928553.");
        }

        private static IntPtr FindDamageEntry(Type type, string name, Type[] args, bool isStatic, Type returnType,
            IntPtr expected, out IntPtr methodInfo)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var method = type.GetMethod(name, flags, null, args, null);
            if (method == null || method.IsStatic != isStatic || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);

            FieldInfo nativeField = null;
            foreach (var field in type.GetFields(flags | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(IntPtr) || !field.IsStatic ||
                    !field.Name.StartsWith("NativeMethodInfoPtr_" + name + "_", StringComparison.Ordinal))
                    continue;
                if (nativeField != null) throw new AmbiguousMatchException(type.FullName + "." + name);
                nativeField = field;
            }

            if (nativeField?.GetValue(null) is not IntPtr info || info == IntPtr.Zero || Marshal.ReadIntPtr(info) != expected)
                throw new InvalidOperationException(type.FullName + "." + name + " differs from public build 22928553.");
            methodInfo = info;
            return expected;
        }

        private static unsafe void BeforeResolveRaisedDamage(IntPtr frame, Damage* damage, DamageResponse* response, IntPtr method)
        {
            if (_damageProtectionActive && frame != IntPtr.Zero && damage != null && response != null)
            {
                try
                {
                    var simulation = new Frame(frame);
                    var target = ClassifyDamageActor(simulation, damage->HitEntity);
                    if (target.Player || target.RaisedByPlayer)
                    {
                        var source = ReadDamageSource(simulation, (IntPtr)(&damage->Proxy));
                        if (BlocksRaisedDamage(source, target, "resolve"))
                        {
                            // This is also the native resolver's response when it cannot resolve a hit.
                            *response = default;
                            return;
                        }
                    }
                    else
                    {
                        try
                        {
                            if (!EntityRefExtensions.Has<RaisedMinionComponent>(target.Entity, simulation))
                            {
                                var source = ReadDamageSource(simulation, (IntPtr)(&damage->Proxy));
                                if (source.RaisedByPlayer)
                                    MinionLastDamage[source.Entity.Raw] = simulation.TimeSinceStart.RawValue;
                            }
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception e) { Error("minion-damage-resolve", e); }
            }
            _resolveDamageHook.Trampoline(frame, damage, response, method);
        }

        private static void BeforeRaisedProjectileImpact(IntPtr self, IntPtr frame, ulong projectile, ulong receiver,
            IntPtr hitPosition, IntPtr hitNormal, IntPtr method)
        {
            if (_damageProtectionActive && frame != IntPtr.Zero)
            {
                try
                {
                    var simulation = new Frame(frame);
                    var target = ClassifyDamageActor(simulation, new EntityRef { Raw = receiver });
                    if (target.Player || target.RaisedByPlayer)
                    {
                        var source = ClassifyDamageActor(simulation, FindDamageCaster(simulation, new EntityRef { Raw = projectile }), true);
                        if (BlocksRaisedDamage(source, target, "projectile-impact")) return;
                    }
                }
                catch (Exception e) { Error("minion-projectile-impact", e); }
            }
            _projectileImpactHook.Trampoline(self, frame, projectile, receiver, hitPosition, hitNormal, method);
        }

        private readonly record struct DamageActor(EntityRef Entity, bool Player, ulong RaisedOwner)
        {
            internal bool RaisedByPlayer => RaisedOwner != 0;
        }

        private static bool BlocksRaisedDamage(DamageActor source, DamageActor target, string stage)
        {
            if (!NecromancyPolicy.BlocksRaisedMinionDamage(source.Player, source.RaisedByPlayer, target.Player, target.RaisedByPlayer))
                return false;
            if (Interlocked.Increment(ref _damageProtectionLogs) <= 40)
                Write("minion damage BLOCKED stage=" + stage + " source=" + source.Entity.Raw + " sourceOwner=" + source.RaisedOwner +
                      " target=" + target.Entity.Raw + " targetOwner=" + target.RaisedOwner);
            return true;
        }

        private static void RememberRaisedDamageSource(Frame frame, EntityRef minion)
        {
            if (!_damageProtectionActive) return;
            try
            {
                var context = frame.Context;
                if (context == null) return;
                var key = (context.Pointer, minion.Raw);
                if (RaisedDamageOwners.ContainsKey(key)) return;
                var actor = ClassifyDamageActor(frame, minion);
                if (actor.RaisedByPlayer) RaisedDamageOwners.TryAdd(key, actor.RaisedOwner);
            }
            catch (Exception e) { Error("remember-raised-source", e); }
        }

        private static unsafe DamageActor ClassifyDamageActor(Frame frame, EntityRef entity, bool useRetiredSource = false)
        {
            if (!entity.IsValid) return default;
            bool isRaised = EntityRefExtensions.Has<RaisedMinionComponent>(entity, frame);
            ulong currentOwner = 0;
            if (isRaised)
            {
                var minion = EntityRefExtensions.Get<RaisedMinionComponent>(entity, frame);
                if (minion != null && minion->Owner.IsValid)
                {
                    currentOwner = minion->Owner.Raw;
                    if (EntityRefExtensions.Has<HeroComponent>(minion->Owner, frame))
                        return new DamageActor(entity, false, currentOwner);
                }
            }

            if (isRaised || useRetiredSource && !EntityRefExtensions.ExistsAndNotPendingDestruction(entity, frame))
            {
                var context = frame.Context;
                if (context != null && RaisedDamageOwners.TryGetValue((context.Pointer, entity.Raw), out ulong owner) &&
                    (!isRaised || owner == currentOwner))
                    return new DamageActor(entity, false, owner);
            }

            return new DamageActor(entity, EntityRefExtensions.Has<HeroComponent>(entity, frame), 0);
        }

        private static bool TryGetDamageProxy(IntPtr group, ProxyTypeFlags kind, out EntityRef entity)
            => _proxyLookup(group, kind, out entity, _proxyLookupMethod) && entity.IsValid;

        private static DamageActor ReadDamageSource(Frame frame, IntPtr proxy)
        {
            // Device components carry the actual caster, which can differ from the hero used for stats.
            if (TryGetDamageProxy(proxy, ProxyTypeFlags.Cascade, out var cascade))
            {
                var caster = FindDamageCaster(frame, cascade);
                if (caster.IsValid) return ClassifyDamageActor(frame, caster, true);
            }

            if (TryGetDamageProxy(proxy, ProxyTypeFlags.Projectile, out var projectile))
            {
                var caster = FindDamageCaster(frame, projectile);
                if (caster.IsValid) return ClassifyDamageActor(frame, caster, true);
            }

            var enemy = TryGetDamageProxy(proxy, ProxyTypeFlags.Enemy, out var enemyRef)
                ? ClassifyDamageActor(frame, enemyRef, true) : default;
            if (!TryGetDamageProxy(proxy, ProxyTypeFlags.Hero, out var heroRef))
                return enemy;
            if (!enemy.Entity.IsValid || enemy.Entity.Raw == heroRef.Raw)
                return ClassifyDamageActor(frame, heroRef, true);

            // When both are present, recognize a raised minion credited to its owner.
            return enemy.RaisedOwner == heroRef.Raw ? enemy : default;
        }

        private static unsafe EntityRef FindDamageCaster(Frame frame, EntityRef carrier)
        {
            // The input starts as a carrier. Only return an actor recovered from an actual owner field.
            for (int depth = 0; depth < 8 && carrier.IsValid; depth++)
            {
                EntityRef next;
                if (EntityRefExtensions.Has<ProjectileComponent>(carrier, frame))
                {
                    var projectile = EntityRefExtensions.Get<ProjectileComponent>(carrier, frame);
                    next = projectile == null ? default : projectile->InstigatorRef;
                }
                else if (EntityRefExtensions.Has<CascadeInstanceComponent>(carrier, frame))
                {
                    var cascade = EntityRefExtensions.Get<CascadeInstanceComponent>(carrier, frame);
                    next = cascade == null ? default : cascade->Caster;
                }
                else
                {
                    return depth != 0 ? carrier : default;
                }

                if (next.Raw == carrier.Raw) return default;
                carrier = next;
            }

            return default;
        }
    }
}
