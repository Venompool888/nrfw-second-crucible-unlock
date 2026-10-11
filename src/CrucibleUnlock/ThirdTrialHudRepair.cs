using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon.Forsaken;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using UnityEngine.UI;
using PatchOwner = HarmonyLib.Harmony;
using UObject = UnityEngine.Object;

namespace CrucibleUnlock
{
    // Presentation only. No retained Frame, simulation writes, asset edits or global localization overrides.
    internal static class ThirdTrialHudRepair
    {
        internal const string OwnerId = "NRFW.CrucibleUnlock.ThirdTrialHud";
        private const int MaxMessages = 64, MaxDiagnostics = 32;
        private static readonly Dictionary<IntPtr, OwnedHud> Owned = new();
        private static PatchOwner _owner;
        private static volatile bool _enabled;
        private static int _diagnostics;

        private sealed class OwnedHud
        {
            internal PlayerCrucibleHUD Hud;
            internal int InstanceId, Character;
            internal IntPtr Storage;
            internal long RunStart;
            internal Il2CppReferenceArray<LocalizedMessage> Original, Replacement;
            internal readonly List<LocalizedMessage> Clones = new();
            internal Image HiddenImage;
            internal UnityEngine.Sprite HiddenSprite;
            internal bool PreviousEnabled;
            internal Il2CppTMPro.TMP_Text Text, Shadow;
            internal string PreviousText, PreviousShadow, LastOwnedLabel;
        }

        internal static void Install()
        {
            if (_owner != null) throw new InvalidOperationException("Third HUD repair already installed.");
            var owner = new PatchOwner(OwnerId);
            try
            {
                PrepareHelpers(TrialRepairLog.Write);
                TrialRepairLog.Patch(owner, typeof(PlayerCrucibleHUD), "UpdateCurrentFloor", Type.EmptyTypes,
                    typeof(ThirdTrialHudRepair), prefix: nameof(BeforeUpdateCurrentFloor), returnType: typeof(void));
                TrialRepairLog.Patch(owner, typeof(PlayerCrucibleHUD), "OnDisable", Type.EmptyTypes,
                    typeof(ThirdTrialHudRepair), prefix: nameof(BeforeReleaseHud), returnType: typeof(void));
                TrialRepairLog.Patch(owner, typeof(PlayerCrucibleHUD), "ResetHUD", Type.EmptyTypes,
                    typeof(ThirdTrialHudRepair), prefix: nameof(BeforeReleaseHud), returnType: typeof(void));
                _owner = owner; _diagnostics = 0; _enabled = true;
                Diagnostic("INSTALLED current-frame private-run floor guard; per-instance message clones; capacity ceiling=" + MaxMessages);
            }
            catch { _enabled = false; owner.UnpatchSelf(); throw; }
        }

        // Called once before ANY new view detours, then again immediately before our own registration.
        // Compile the real bodies eagerly; this does not invoke them or touch native scene/frame state.
        internal static void PrepareHelpers(Action<string> startupLog)
        {
            if (startupLog == null) throw new ArgumentNullException(nameof(startupLog));
            foreach (string name in new[] { nameof(BeforeUpdateCurrentFloor), nameof(BeforeReleaseHud),
                nameof(RepairCurrentFloor), nameof(Extend), nameof(HideIcon), nameof(RestoreIcon), nameof(Release),
                nameof(RestoreLabel), nameof(Roman) })
            {
                startupLog("[third-hud/r15] preparing helper=" + name);
                var method = typeof(ThirdTrialHudRepair).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null) throw new MissingMethodException(typeof(ThirdTrialHudRepair).FullName, name);
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                startupLog("[third-hud/r15] helper compilation ready=" + name);
            }
        }

        // May be called from non-Unity failure paths. Keep references/patches until a safe lifecycle callback or Dispose.
        internal static void StopWithoutUnityReads() { _enabled = false; }

        // Loader must call on its Unity/main-thread cleanup path, before unloading the plugin.
        internal static void Dispose()
        {
            _enabled = false;
            foreach (var pointer in new List<IntPtr>(Owned.Keys)) Release(pointer);
            _owner?.UnpatchSelf(); _owner = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeReleaseHud(PlayerCrucibleHUD __instance)
        {
            if (__instance != null) Release(__instance.Pointer);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool BeforeUpdateCurrentFloor(PlayerCrucibleHUD __instance) => RepairCurrentFloor(__instance);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool RepairCurrentFloor(PlayerCrucibleHUD __instance)
        {
            if (__instance == null) return true;
            bool privateSource = false;
            try
            {
                // A stop removes our owned presentation on this safe UI callback, but an exact
                // private overflow must remain guarded while detours are installed. Passing the
                // restored eleven-entry array to native here would recreate the original failure.
                if (!_enabled) Release(__instance.Pointer);
                var view = ViewFrame.Active;
                if (view == null || view.IsDisposed) { Release(__instance.Pointer); return true; }
                var frame = view.GetQtn();
                if (frame == null) { Release(__instance.Pointer); return true; }
                privateSource = InfiniteDungeonAPI.GetSourcePlaylist(frame).Id.Value == ThirdTrialPolicy.PlaylistGuid;
                if (!privateSource) { Release(__instance.Pointer); return true; }

                // Match the official HUD: local player/hero view, bound hero, then that hero's current-frame character ref.
                var players = view.Players.Item2;
                if (players == null || !players.TryGetLocalPlayerRef(out PlayerRef player) ||
                    !players.TryGetHeroView(player, out HeroView heroView) || heroView == null ||
                    !players.TryGetBoundHeroEntity(out Frame bindingFrame, out EntityRef hero) || !hero.IsValid ||
                    !RuntimeCharacterDataAPI.TryGetCharacterRef(frame, hero, out RuntimeCharacterReference character) ||
                    !character.IsValid)
                { Release(__instance.Pointer); return true; }
                // bindingFrame is deliberately not retained or substituted for official HUD's current GetQtn frame.
                if (!InfiniteDungeonAPI.IsPlayerInActiveRun(frame, character)) { Release(__instance.Pointer); return true; }
                var storage = frame.InstanceDataStorage;
                if (storage == null) throw new InvalidOperationException("Private HUD has no current storage.");
                int instanceId = __instance.GetInstanceID();
                long runStart = InfiniteDungeonAPI.GetRunStartTime(frame).RawValue;
                if (Owned.TryGetValue(__instance.Pointer, out var owned) &&
                    (owned.InstanceId != instanceId || owned.Storage != storage.Pointer ||
                     owned.RunStart != runStart || owned.Character != character.Raw))
                { Release(__instance.Pointer); owned = null; }

                int completed = InfiniteDungeonAPI.GetCompletedFloorsForPlayer(frame, character);
                int skipped = InfiniteDungeonAPI.GetSkippedFloors(frame);
                var generated = InfiniteDungeonAPI.GetRuntimePlaylist(frame);
                int generatedCount = generated == null ? 0 : generated.Count;
                int index, capacity;
                checked { index = completed + skipped; capacity = generatedCount + skipped + 1; }
                var messages = __instance.floorMessages;
                if (completed < 0 || skipped < 0 || generatedCount < 1 || completed > generatedCount ||
                    capacity > MaxMessages || capacity < 1 || index < 0 || index >= capacity ||
                    messages == null || messages.Length == 0 || messages.Length > MaxMessages)
                {
                    Diagnostic("SUPPRESSED invalid private HUD; completed=" + completed + "; skipped=" + skipped +
                        "; generated=" + generatedCount + "; index=" + index + "; messages=" + (messages?.Length ?? 0));
                    return false;
                }

                if (owned != null && messages.Pointer != owned.Replacement?.Pointer)
                {
                    // Another owner replaced the field. Preserve it; release only clones not referenced by it.
                    Release(__instance.Pointer); owned = null; messages = __instance.floorMessages;
                }
                if (index >= messages.Length)
                {
                    if (!_enabled)
                    {
                        Diagnostic("SUPPRESSED stopped private HUD overflow; index=" + index + "; messages=" + messages.Length);
                        return false;
                    }
                    if (owned == null)
                    {
                        owned = new OwnedHud { Hud = __instance, InstanceId = instanceId, Storage = storage.Pointer,
                            RunStart = runStart, Character = character.Raw, Original = messages,
                            Text = __instance.BoonTierText, Shadow = __instance.BoonTierShadowText,
                            PreviousText = __instance.BoonTierText?.text, PreviousShadow = __instance.BoonTierShadowText?.text };
                        Owned.Add(__instance.Pointer, owned);
                    }
                    Extend(owned, capacity);
                    messages = __instance.floorMessages;
                    Diagnostic("EXTENDED hud=" + instanceId + "; completed=" + completed + "; skipped=" + skipped +
                        "; generated=" + generatedCount + "; index=" + index + "; original=" + owned.Original.Length + "; capacity=" + capacity);
                }
                if (messages[index] == null) { Diagnostic("SUPPRESSED null private floor message; index=" + index); return false; }
                if (owned != null)
                {
                    owned.LastOwnedLabel = index >= owned.Original.Length ? Roman(index + 1) : null;
                    var images = __instance.FloorImages;
                    if (images == null || index >= images.Length) HideIcon(owned);
                    else RestoreIcon(owned);
                }
                return true;
            }
            catch (Exception error)
            {
                Diagnostic("ERROR private=" + privateSource + "; " + error.GetType().Name + ": " + error.Message);
                // Unknown source is never evidence permitting a global HUD suppression.
                if (!privateSource) Release(__instance.Pointer);
                return !privateSource;
            }
        }

        private static void Extend(OwnedHud owned, int capacity)
        {
            var previous = owned.Replacement ?? owned.Original;
            LocalizedMessage template = null;
            for (int i = 0; i < owned.Original.Length; i++)
                if (owned.Original[i] != null) { template = owned.Original[i]; break; }
            if (template == null) throw new InvalidOperationException("No original localized floor message template.");
            var replacement = new Il2CppReferenceArray<LocalizedMessage>(capacity);
            for (int i = 0; i < previous.Length; i++) replacement[i] = previous[i];
            for (int i = previous.Length; i < capacity; i++)
            {
                var message = UObject.Instantiate(template).Cast<LocalizedMessage>();
                if (message == null || message.Pointer == template.Pointer) throw new InvalidOperationException("LocalizedMessage Unity clone failed.");
                owned.Clones.Add(message); // Retain partial allocation for lifecycle cleanup on failure.
                string label = Roman(i + 1);
                message.Id = "nrFW.thirdTrial.ownedFloor." + owned.InstanceId + "." + (i + 1);
                message.name = "NRFWThirdFloor" + (i + 1);
                message.English = label; message.French = label; message.Italian = label; message.German = label;
                message.Spanish = label; message.BrazilianPortuguese = label; message.TraditionalChinese = label;
                message.SimplifiedChinese = label; message.Korean = label; message.Russian = label;
                message.Japanese = label; message.Polish = label;
                replacement[i] = message;
            }
            owned.Replacement = replacement;
            owned.Hud.floorMessages = replacement; // Generated setter includes native GC write barrier.
        }

        private static void HideIcon(OwnedHud owned)
        {
            var image = owned.Hud.FloorImage;
            if (owned.HiddenImage != null && (image == null || image.Pointer != owned.HiddenImage.Pointer)) RestoreIcon(owned);
            if (image == null || owned.HiddenImage != null || !image.enabled) return;
            owned.HiddenImage = image; owned.HiddenSprite = image.sprite; owned.PreviousEnabled = image.enabled;
            image.enabled = false; // No official sprite exists past XI; hide rather than show a stale icon.
        }

        private static void RestoreIcon(OwnedHud owned)
        {
            var image = owned.HiddenImage;
            if (image != null && !image.enabled && image.sprite?.Pointer == owned.HiddenSprite?.Pointer)
                image.enabled = owned.PreviousEnabled;
            owned.HiddenImage = null; owned.HiddenSprite = null;
        }

        private static void Release(IntPtr pointer)
        {
            if (!Owned.TryGetValue(pointer, out var owned)) return;
            try
            {
                var hud = owned.Hud;
                Il2CppReferenceArray<LocalizedMessage> current = null;
                if (hud != null && hud.GetInstanceID() == owned.InstanceId)
                {
                    current = hud.floorMessages;
                    if (owned.Replacement != null && current?.Pointer == owned.Replacement.Pointer)
                    {
                        hud.floorMessages = owned.Original;
                        // Native HUD caches the last index; force its next ordinary update to read the restored array.
                        hud.m_currentFloor = -1;
                        RestoreLabel(hud.BoonTierText, owned.Text, owned.PreviousText, owned.LastOwnedLabel);
                        RestoreLabel(hud.BoonTierShadowText, owned.Shadow, owned.PreviousShadow, owned.LastOwnedLabel);
                        current = owned.Original;
                    }
                    RestoreIcon(owned);
                }
                foreach (var clone in owned.Clones)
                {
                    bool foreignReference = false;
                    if (current != null && current.Pointer != owned.Original.Pointer)
                        for (int i = 0; i < current.Length; i++)
                            if (current[i]?.Pointer == clone.Pointer) { foreignReference = true; break; }
                    // A foreign replacement can retain one of our clones. It then owns that reference;
                    // destroying it here would damage foreign UI. All exclusive clones are destroyed.
                    if (!foreignReference && clone != null) UObject.Destroy(clone);
                }
                Owned.Remove(pointer);
            }
            catch (Exception error) { Diagnostic("CLEANUP deferred " + error.GetType().Name + ": " + error.Message); }
        }

        private static void RestoreLabel(Il2CppTMPro.TMP_Text current, Il2CppTMPro.TMP_Text original, string previous, string label)
        {
            if (label != null && current != null && original != null && current.Pointer == original.Pointer && current.text == label)
                current.text = previous;
        }

        private static string Roman(int value)
        {
            if (value < 1 || value > MaxMessages) throw new ArgumentOutOfRangeException(nameof(value));
            string text = "";
            foreach (var entry in new[] { (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
                while (value >= entry.Item1) { text += entry.Item2; value -= entry.Item1; }
            return text;
        }

        private static void Diagnostic(string text)
        {
            if (++_diagnostics <= MaxDiagnostics) TrialRepairLog.Write("[third-hud/r15] " + text);
        }
    }
}
