using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppMoon.Forsaken;
using UnityEngine;
using CharacterBloodAmount = Il2Cpp.CharacterBloodAmount;

namespace Necromancy
{
    public sealed partial class NecromancyMod
    {
        private const ShaderApi.Flags LookFlags = ShaderApi.Flags.UsePropertyBlockInEditMode;
        private static readonly Dictionary<IntPtr, LookState> LookStates = new();
        private static MinionLookConfig _lookConfig = MinionLookPolicy.Default;
        private static string _lookPath;
        private static DateTime _lookWriteTime;
        private static bool _lookReadFailed;
        private static long _nextLookCheck;
        private static double _nextLookRefresh;
        private static int _lookApplyLogs, _lookRestoreLogs;
        private static long _bloodSkips, _nextBloodLog = 1;

        private sealed record LookState(RaisedMinionVisualEffect Component, string Profile)
        {
            internal readonly List<LookOriginal> Originals = new();
        }

        private readonly record struct LookOriginal(Renderer Renderer, ShaderPropertyIdColor? ColorProperty,
            ShaderPropertyIdFloat FloatProperty, Color Color, float Float);

        private static void InstallMinionLook(HarmonyLib.Harmony harmony)
        {
            _lookPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "NRFWNecromancyLook.cfg");
            LoadMinionLook(startup: true);
            long now = Environment.TickCount64;
            _nextLookCheck = now + 1000;
            _nextLookRefresh = now + _lookConfig.RefreshSeconds * 1000d;
            Patch(harmony, typeof(RaisedMinionVisualEffect), "RemoveTintEffect", Type.EmptyTypes, nameof(AfterRemoveTint));
            PatchPrefix(harmony, typeof(CharacterBloodAmount), "AddBlood", new[] { typeof(float), typeof(bool) }, nameof(BeforeAddBlood), typeof(void));
        }

        private static bool LoadMinionLook(bool startup)
        {
            IReadOnlyList<string> warnings = Array.Empty<string>();
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(_lookPath);
                if (!startup && !_lookReadFailed && stamp == _lookWriteTime) return false;
                if (!File.Exists(_lookPath))
                {
                    File.WriteAllText(_lookPath, MinionLookPolicy.DefaultFileText, Encoding.UTF8);
                    stamp = File.GetLastWriteTimeUtc(_lookPath);
                }
                (_lookConfig, warnings) = MinionLookPolicy.Parse(File.ReadAllText(_lookPath, Encoding.UTF8));
                _lookWriteTime = stamp;
                _lookReadFailed = false;
            }
            catch (Exception e)
            {
                if (_lookReadFailed) return false;
                _lookReadFailed = true;
                _lookConfig = MinionLookPolicy.Default;
                Write("look config read failed; using defaults: " + e.GetType().Name + ": " + e.Message);
            }
            Write(FormattableString.Invariant($"look config look={(_lookConfig.Ghost ? "ghost" : "stock")} blood={(_lookConfig.Blood ? "on" : "off")} refresh={_lookConfig.RefreshSeconds} warnings={warnings.Count}"));
            foreach (string warning in warnings) Write("look config warning " + warning);
            return true;
        }

        private static void ApplyLook(RaisedMinionVisualEffect component, bool refresh = false)
        {
            try
            {
                if (component == null || !component.m_effectApplied) return;
                IntPtr key = component.Pointer;
                bool first = !LookStates.TryGetValue(key, out var state);
                if (first)
                {
                    long action = component.m_raiseActionGuid.Value;
                    string name = MinionLookPolicy.ProfileName(IsPrivateBossAction(action), action == NecromancyPolicy.ReviveAction);
                    // Track stock components too, so hot reload can switch live minions back to ghost.
                    LookStates.Add(key, state = new LookState(component, name));
                }
                if (!_lookConfig.Ghost || (!first && !refresh)) return;
                // A dying minion fades out through the same dithered alpha; a timed rewrite would pop it back in.
                if (refresh && component.m_entityView?.TryCast<CharacterView>() is { IsDead: true }) return;
                var profile = state.Profile == "boss" ? _lookConfig.Boss : state.Profile == "revive" ? _lookConfig.Revive : _lookConfig.Raise;
                var renderers = component.m_renderers;
                var written = first && ++_lookApplyLogs <= 150 ? new Dictionary<string, string>() : null;
                int skipped = 0;
                if (renderers != null)
                    foreach (var renderer in renderers)
                    {
                        var material = renderer != null ? renderer.sharedMaterial : null;
                        var shader = material != null ? material.shader : null;
                        if (shader == null || !shader.name.StartsWith("forsaken/baseCharacter", StringComparison.Ordinal))
                        {
                            ++skipped;
                            continue;
                        }

                        SetColor(renderer, ShaderPropertyIdColor.Tint, profile.Tint);
                        SetColor(renderer, ShaderPropertyIdColor.Overlay1Color, profile.Overlay);
                        SetColor(renderer, ShaderPropertyIdColor.EffectColor, profile.EffectColor);
                        SetColor(renderer, ShaderPropertyIdColor.EmissiveColor, profile.Emissive);
                        SetFloat(renderer, ShaderPropertyIdFloat.EffectAmount, profile.EffectAmount);
                        SetFloat(renderer, ShaderPropertyIdFloat.EffectLocalYAddOpacity, profile.EffectLocalY);
                        SetFloat(renderer, ShaderPropertyIdFloat.DitheredAlpha, profile.DitheredAlpha);
                    }
                if (written != null)
                    Write($"look APPLY go={component.gameObject.name} profile={state.Profile} renderers={renderers?.Length ?? 0} skipped={skipped} written=[{string.Join(" ", written.Values)}]");

                void SetColor(Renderer renderer, ShaderPropertyIdColor property, LookColor? setting)
                {
                    if (setting is not LookColor value || !ShaderApi.HasColorProperty(renderer, property)) return;
                    if (!state.Originals.Exists(o => o.ColorProperty == property && o.Renderer.Pointer == renderer.Pointer))
                        state.Originals.Add(new LookOriginal(renderer, property, default, ShaderApi.GetColor(renderer, property, LookFlags), 0));
                    ShaderApi.SetColor(renderer, property, new Color(value.R, value.G, value.B, value.A), LookFlags);
                    if (written != null) written[property.ToString()] = FormattableString.Invariant($"{property}={value.R},{value.G},{value.B},{value.A}");
                }

                void SetFloat(Renderer renderer, ShaderPropertyIdFloat property, float? setting)
                {
                    if (setting is not float value || !ShaderApi.HasProperty(renderer, property)) return;
                    if (!state.Originals.Exists(o => o.ColorProperty == null && o.FloatProperty == property && o.Renderer.Pointer == renderer.Pointer))
                        state.Originals.Add(new LookOriginal(renderer, null, property, default, ShaderApi.GetFloat(renderer, property, LookFlags)));
                    ShaderApi.SetFloat(renderer, property, value, LookFlags);
                    if (written != null) written[property.ToString()] = FormattableString.Invariant($"{property}={value}");
                }
            }
            catch (Exception e) { Error("look apply", e); }
        }

        private static void RestoreMinionLook(IntPtr key)
        {
            if (!LookStates.TryGetValue(key, out var state)) return;
            int restored = 0;
            foreach (var original in state.Originals)
            {
                try
                {
                    if (original.Renderer == null) continue;
                    if (original.ColorProperty is ShaderPropertyIdColor property)
                        ShaderApi.SetColor(original.Renderer, property, original.Color, LookFlags);
                    else
                        ShaderApi.SetFloat(original.Renderer, original.FloatProperty, original.Float, LookFlags);
                    ++restored;
                }
                catch (Exception e) { Error("look restore", e); }
            }
            LookStates.Remove(key);
            if (++_lookRestoreLogs <= 150 && state.Component != null)
                Write($"look RESTORE go={state.Component.gameObject.name} properties={restored}");
        }

        private static void AfterRemoveTint(RaisedMinionVisualEffect __instance)
        {
            try { if (__instance != null) RestoreMinionLook(__instance.Pointer); }
            catch (Exception e) { Error("look remove tint", e); }
        }

        private static void PumpMinionLook(long now)
        {
            foreach (var pair in LookStates.ToArray())
                if (pair.Value.Component == null) LookStates.Remove(pair.Key);

            if (now >= _nextLookCheck)
            {
                _nextLookCheck = now + 1000;
                if (LoadMinionLook(startup: false))
                {
                    var active = LookStates.ToArray();
                    foreach (var pair in active) RestoreMinionLook(pair.Key);
                    foreach (var pair in active) ApplyLook(pair.Value.Component);
                    _nextLookRefresh = now + _lookConfig.RefreshSeconds * 1000d;
                    Write("look RELOAD live minions=" + active.Length);
                    return;
                }
            }

            if (!_lookConfig.Ghost || _lookConfig.RefreshSeconds == 0 || now < _nextLookRefresh) return;
            _nextLookRefresh = now + _lookConfig.RefreshSeconds * 1000d;
            foreach (var state in LookStates.Values) ApplyLook(state.Component, refresh: true);
        }

        private static bool BeforeAddBlood(CharacterBloodAmount __instance)
        {
            try
            {
                if (!_enabled || _lookConfig.Blood || __instance == null) return true;
                var view = __instance.CharacterView;
                if (view == null) return true;
                ulong entity = view.EntityRef.Raw;
                if (!Minions.ContainsKey(entity)) return true;
                if (++_bloodSkips == _nextBloodLog)
                {
                    Write($"blood SKIP minion entity={entity} total={_bloodSkips}");
                    _nextBloodLog *= 10;
                }
                return false;
            }
            catch (Exception e) { Error("look blood", e); return true; }
        }
    }
}
