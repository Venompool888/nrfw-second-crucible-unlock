using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon;
using Il2CppMoon.Forsaken;
using Il2CppMoon.Wwise;
using Il2CppMoon.Wwise.MoonWwiseTypes;
using UnityEngine;

namespace BowgunRepair;

// Native component layout/configuration follows shortBowView. Only the exact
// reinforcedBowgunView is completed; it has no AudioCompanion in shipped assets.
internal static class NativeAudio
{
    private static EventReference _raise, _fire;
    internal static EventReference Raise => _raise ??= Event(NativeAudioPolicy.RaiseGuid);
    internal static EventReference Fire => _fire ??= Event(NativeAudioPolicy.FireGuid);
    private static EventReference Event(string guid) => new() { SerializedGuid = new SerializableGuid(guid) };
    private static DialogueEventReference Dialogue(string guid) => new() { SerializedGuid = new SerializableGuid(guid) };
    private static SwitchReference Switch(string value, string group) => new()
    { SerializedGuid = new SerializableGuid(value), m_mediaGroupGuid = new SerializableGuid(group) };

    internal static bool CompleteWeapon(HeroWeaponView view)
    {
        if (view == null || !NativeAudioPolicy.NeedsCompanion(view.gameObject.name,
            view.WeaponData.Id.Value, view.AudioCompanion != null)) return false;
        GameObject child = null;
        OwnedField<WwiseWeaponCompanion> companionSlot = null;
        OwnedField<bool> offhandSlot = null;
        try
        {
            // Add/initialize components while our own child is inactive so the
            // engine cannot register/play from partially populated references.
            child = new GameObject("NRFW-BowgunAudio");
            child.SetActive(false);
            var parent = view.LeftHandWeapon.Transform;
            if (parent == null) parent = view.transform;
            // This build's two-argument SetParent wrapper is a throwing stub.
            // The three-argument native overload is present and preserves local space.
            child.transform.SetParent(parent, false, false);
            var host = child.AddComponent<SoundHost>();
            host.SyncPosition = true;
            host.StartEvents = new Il2CppReferenceArray<EventReference>(0);
            host.StopFadeCurve = (AkCurveInterpolation)4;
            host.StopFadeTime = .1f;
            host.Offset = Vector3.zero;
            host.UseSpatialRoomReverb = true;
            var companion = child.AddComponent<WwiseWeaponCompanion>();
            companion.WeaponSoundHost = host;
            companion.WeaponTypeSwitch = Switch("b5c800b8-77c6-4683-9606-6d00868cefaf", "8e6175ed-d322-4bc7-8d4e-c8b70792dc55");
            companion.DamageTypeSwitch = SwitchReference.Empty;
            companion.BlockTypeSwitch = new SwitchReference
            { SerializedGuid = new SerializableGuid("a029663a-fddb-4166-b15c-64fe23acb4c6"),
              m_mediaGroupGuid = new SerializableGuid(5377615695553125717UL,4450971188186151870UL) };
            // Full companion event references follow the native ranged weapon;
            // actual raise/fire tracks use the existing crossbow Event assets.
            companion.AttackEvent = Dialogue("f995fc5a-8f60-4392-95b3-d46386c07425");
            companion.ContextEvent = Dialogue("ab234b29-86c6-43e0-9c97-5c1dee00d5cb");
            companion.MagicAttackEvent = new DialogueEventReference();
            companion.MagicSkillEvent = new DialogueEventReference();
            companion.ReactionEvent = Dialogue("1fe7f55e-b63b-4808-8fb2-d170f4896e1a");
            companion.SkillsEvent = Dialogue("9724e957-4a02-4432-b741-328e8b7bfcca");
            companion.SpeedRTPC = new RtpcReference { SerializedGuid = new SerializableGuid("94a23821-6f68-41af-aa32-3bf6cf3dadd4") };
            companion.DamageRatioMultiplier = 1f;
            companion.DamageVisualFallback = (Il2CppQuantum.DamageVisualType)7;
            companion.SwingSpeedThreshold = 20f;
            companion.ModulateSpeedRTPC = false;
            companion.IsShield = false;
            child.SetActive(true);
            // Original HeroWeaponView.OnInstantiatedFromPool invokes the
            // companion's normal native lifecycle callback.
            companionSlot = new OwnedField<WwiseWeaponCompanion>(()=>view.AudioCompanion,v=>view.AudioCompanion=v,
                companion,(a,b)=>a?.Pointer==b?.Pointer);
            offhandSlot = new OwnedField<bool>(()=>view.IsOffhandWeapon,v=>view.IsOffhandWeapon=v,true);
            companionSlot.Apply();
            offhandSlot.Apply();
            return true;
        }
        catch (Exception failure)
        {
            // Roll back published pointers before destroying their component.
            // If rollback itself fails, keep the child alive rather than leave
            // an engine field pointing at a destroyed native object.
            var errors = new List<Exception> { failure };
            try { offhandSlot?.Restore(); } catch (Exception e) { errors.Add(e); }
            try { companionSlot?.Restore(); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 1) throw new AggregateException("Weapon audio completion rollback failed; child retained",errors);
            if (child != null) { child.SetActive(false); UnityEngine.Object.Destroy(child); }
            throw;
        }
    }
}
