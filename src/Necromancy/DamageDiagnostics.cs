using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppQuantum;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        // ---- read-only damage diagnostics (T-006) ---------------------------------------------
        // CharacterViewEffects.OnDamageResolved receives the resolved event object; nothing here changes damage.

        private static int _hitLogs, _heroHitLogs, _diagErrors;
        private static volatile bool _diagOff;

        // 0.13.0 playtest: proxy.Main / TryGetProxyOfType returned 0..4 instead of entity refs. The interop struct
        // models PayloadProxyGroup's fixed proxy buffer as one element, so the managed copy is truncated. Read the
        // proxy slots straight from the native event object instead (layout resolved from IL2CPP at runtime).
        private static int _proxyLayout;   // 0 = unresolved, 1 = ok, -1 = unusable
        private static int _offDamage, _offProxy, _offSlots, _offSlotIndex, _slotCount;

        private static void InstallDamageDiagnostics()
        {
            var diagHarmony = new HarmonyLib.Harmony("NRFW.Necromancy.DamageDiagnostics");
            try
            {
                Patch(diagHarmony, typeof(Il2CppMoon.Forsaken.CharacterViewEffects), "OnDamageResolved", new[] { typeof(EventDamageResolved) },
                    nameof(AfterCharacterDamageResolvedInstance));
                Write("minion hit diagnostics installed");
                Patch(diagHarmony, typeof(Il2CppMoon.Forsaken.HeroView), "OnDamageResolved", new[] { typeof(EventDamageResolved) },
                    nameof(AfterHeroDamageResolved));
                Write("hero hit diagnostics installed");
            }
            catch (Exception e)
            {
                _diagOff = true;
                diagHarmony.UnpatchSelf();
                Write("minion hit diagnostics FAILED " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void DiagnosticsError(string stage, Exception e)
        {
            int count = Interlocked.Increment(ref _diagErrors);
            if (count <= NecromancyPolicy.DefaultFeatureMaxErrors)
                Write(stage + " ERROR " + e.GetType().Name + ": " + e.Message);
            if (count >= NecromancyPolicy.DefaultFeatureMaxErrors && !_diagOff)
            {
                _diagOff = true;
                Write("diagnostics disabled after repeated errors; rest of plugin unaffected");
            }
        }

        private static void AfterCharacterDamageResolvedInstance(EventDamageResolved __0) => AfterCharacterDamageResolved(__0);

        // 0.12.0 playtest: the maintainer still lost health near minions but no hero/minion HIT was logged.
        // Log every hit on the local hero with all source proxies so the real attacker (minion, its projectile or
        // cascade, or an enemy) and the damage TargetGroup are visible. Read-only.
        private static void AfterHeroDamageResolved(EventDamageResolved __0)
        {
            if (__0 == null || !_enabled || _diagOff) return;
            try
            {
                ulong hero = unchecked((ulong)Interlocked.Read(ref _heroRaw));
                if (hero == 0 || __0.HitEntity.Raw != hero) return;
                if (Interlocked.Increment(ref _heroHitLogs) > 400) return;
                var damage = __0.Damage;
                string sources = DescribeProxies(__0, hero, out bool anyMinion);
                Write("HERO HIT taken=" + (__0.Response.AmountTaken.RawValue / 65536.0).ToString("0.##") + " type=" + __0.Response.Type +
                      " group=" + damage.TargetGroup + " flags=" + damage.Flags + " school=" + damage.School +
                      (anyMinion ? " FROM-MINION" : "") + " " + sources);
            }
            catch (Exception ex) { DiagnosticsError("hero-hit-log", ex); }
        }

        private static bool ResolveProxyLayout()
        {
            if (_proxyLayout != 0) return _proxyLayout > 0;
            _proxyLayout = -1;
            uint align = 0;
            IntPtr evt = Il2CppInterop.Runtime.Il2CppClassPointerStore<EventDamageResolved>.NativeClassPtr;
            IntPtr dmg = Il2CppInterop.Runtime.Il2CppClassPointerStore<Damage>.NativeClassPtr;
            IntPtr ppg = Il2CppInterop.Runtime.Il2CppClassPointerStore<PayloadProxyGroup>.NativeClassPtr;
            if (evt == IntPtr.Zero || dmg == IntPtr.Zero || ppg == IntPtr.Zero) { Write("proxy layout: class missing"); return false; }
            IntPtr fDamage = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(evt, "Damage");
            IntPtr fProxy = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(dmg, "Proxy");
            IntPtr fSlots = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(ppg, "_m_proxies_");
            IntPtr fIndex = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(ppg, "m_availableProxySlotIndex");
            if (fDamage == IntPtr.Zero || fProxy == IntPtr.Zero || fSlots == IntPtr.Zero || fIndex == IntPtr.Zero) { Write("proxy layout: field missing"); return false; }
            int header = 2 * IntPtr.Size;   // value-type field offsets include the object header
            _offDamage = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fDamage);
            _offProxy = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fProxy) - header;
            _offSlots = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fSlots) - header;
            _offSlotIndex = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fIndex) - header;
            int ppgSize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(ppg, ref align);
            int damageSize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(dmg, ref align);
            _slotCount = (ppgSize - _offSlots) / 8;
            bool ok = NecromancyPolicy.ProxyLayoutOk(_offDamage, _offProxy, ppgSize, damageSize, _offSlots, _offSlotIndex, _slotCount);
            Write("proxy layout " + (ok ? "OK" : "REJECTED") + ": event.Damage@" + _offDamage + " Damage.Proxy@" + _offProxy + " size " + damageSize +
                  "; proxies@" + _offSlots + " slotIndex@" + _offSlotIndex + " group size " + ppgSize + " slots=" + _slotCount);
            _proxyLayout = ok ? 1 : -1;
            return ok;
        }

        private static string DescribeProxies(EventDamageResolved e, ulong hero, out bool anyMinion)
        {
            anyMinion = false;
            if (!ResolveProxyLayout()) return "sources=?";
            IntPtr group = IntPtr.Add(e.Pointer, _offDamage + _offProxy);
            int used = Marshal.ReadInt32(group, _offSlotIndex);
            var parts = new List<string>();
            for (int i = 0; i < _slotCount; i++)
            {
                ulong raw = unchecked((ulong)Marshal.ReadInt64(group, _offSlots + i * 8));
                if (raw == 0) continue;
                bool m = Minions.ContainsKey(raw);
                anyMinion |= m;
                parts.Add(i + ":" + raw + (m ? "(MINION)" : raw == hero ? "(HERO)" : ""));
            }
            return "used=" + used + " sources=[" + string.Join(" ", parts) + "]";
        }

        private static void AfterCharacterDamageResolved(EventDamageResolved e)
        {
            if (e == null || !_enabled || _diagOff) return;
            try
            {
                ulong hit = e.HitEntity.Raw, hero = unchecked((ulong)Interlocked.Read(ref _heroRaw));
                bool hitMinion = Minions.ContainsKey(hit), hitHero = hit == hero;
                if (!hitMinion || hitHero) return;   // hero hits are logged by AfterHeroDamageResolved
                string sources = DescribeProxies(e, hero, out bool srcMinion);
                bool srcHero = sources.Contains("(HERO)");
                if (!(srcHero || srcMinion)) return;   // enemy vs minion is expected damage
                if (Interlocked.Increment(ref _hitLogs) > 40) return;
                Write("HIT minion=" + hit + " by " + (srcHero ? "hero" : "minion") + " taken=" + (e.Response.AmountTaken.RawValue / 65536.0).ToString("0.##") +
                      " type=" + e.Response.Type + " " + sources);
            }
            catch (Exception ex) { DiagnosticsError("hit-log", ex); }
        }
    }
}
