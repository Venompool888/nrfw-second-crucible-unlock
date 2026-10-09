using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using Il2CppMoon.TheLoop;
using Il2CppMoon.Timeline;
using Il2CppQuantum;
using MelonLoader;
using EventReference = Il2CppMoon.Wwise.MoonWwiseTypes.EventReference;
using EntityView = Il2CppMoon.Forsaken.EntityView;
using Il2CppMoon;
using Il2CppMoon.Wwise;

[assembly: MelonInfo(typeof(BowgunRepair.Repair), "Bowgun Audio Sync", "0.3.3", "NRFW research")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
[assembly: MelonAdditionalDependencies("0Harmony")]

namespace BowgunRepair;

// Preserve the exact working 0.9.16 module. New native hooks target only the
// previously unpatched weapon pooling callback and actual projectile release.
public sealed class Repair : MelonMod
{
    private static Repair _self;
    private static HarmonyLib.Harmony _patches;
    private static readonly Dictionary<IntPtr, Shot> Saved = new();
    private static readonly StartupPolicy Startup = new();
    private static Shot _active;
    private static HeroView _hero;
    private static ulong _heroRaw;
    private static long _startupAt, _cleanupAt;
    private static int _errors, _sequence;
    private static string _startupState;
    private static bool _enabled;

    private sealed class Shot
    {
        internal ActionView View;
        internal WwiseSoundAnimator Sound;
        internal WwiseSoundAnimator Raise;
        internal EventReference Empty;
        internal HeroView HeroView;
        internal IntPtr ViewPointer, HostPointer;
        internal ShotBinding Binding;
        internal readonly List<IRestorableField> CastFields = new(), RaiseFields = new();
        internal int Sequence;
        internal bool Fired, CastCacheReset, RaiseCacheReset;
        internal readonly ReleasePolicy Release = new();
    }

    public override void OnInitializeMelon()
    {
        _self = this;
        try
        {
            VerifyProjectileLayout();
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "CrucibleUnlock");
            var type = assembly.GetType("CrucibleUnlock.BowgunInputRepair", true);
            var start = type.GetMethod("BeforeAnimationStart", BindingFlags.NonPublic|BindingFlags.Static,
                null, new[] { typeof(ActionView), typeof(EntityView), typeof(int) }, null);
            var returned = type.GetMethod("BeforeAnimationReturn", BindingFlags.NonPublic|BindingFlags.Static,
                null, new[] { typeof(ActionView) }, null);
            if (start?.GetMethodBody() == null || returned?.GetMethodBody() == null || start.ReturnType != typeof(void) || returned.ReturnType != typeof(void))
                throw new MissingMethodException("Expected inspected 0.9.16 CLR callbacks");
            _patches = new HarmonyLib.Harmony("NRFW.BowgunRepair.ReleaseStateAndStartup");
            _patches.Patch(start, prefix:new HarmonyMethod(typeof(Repair),nameof(BeforeStart)) { priority = HarmonyLib.Priority.First });
            _patches.Patch(returned, prefix:new HarmonyMethod(typeof(Repair),nameof(BeforeReturn)));
            PatchNative(typeof(HeroWeaponView), "OnInstantiatedFromPool", nameof(BeforeWeaponInstantiation), true);
            PatchNative(typeof(ProjectileView), "OnProjectileReleased", nameof(AfterProjectileReleased), false);
            try { BlockInputRouting.Install(type,Log); }
            catch (Exception input) { Log("INPUT_EXTENSION_INSTALL_FAILED "+input); }
            _enabled = true;
            Log("INSTALLED version=0.3.3; two managed audio callbacks; two narrow native hooks; native crossbow raise/fire; native sound animator and handles; timeline constraints unchanged; guarded lifecycle cleanup; keyboardExtension="+BlockInputRouting.Enabled);
        }
        catch (Exception e) { BlockInputRouting.Shutdown(); _patches?.UnpatchSelf(); LoggerInstance.Error("[bowgun-repair] INSTALL_FAILED " + e); }
    }

    public override void OnLateUpdate()
    {
        try
        {
            // Cleanup remains available after fail-closed disabling. Never
            // restore a live timeline merely because audio arming is disabled.
            SweepSaved();
            if (!_enabled) return;
            var vf = ViewFrame.Active;
            if (vf == null || vf.IsDisposed || vf.Players.Item2 == null ||
                !vf.Players.Item2.TryGetBoundHeroEntity(out EntityRef local) || !local.IsValid)
            { ClearBinding(); return; }
            if (_hero == null || !_hero.IsBoundToEntity || _heroRaw != local.Raw)
            {
                ClearBinding();
                if (!EntityViewTracker.TryGetByEntityRef(local, out EntityView ev)) return;
                _hero = ev?.TryCast<HeroView>();
                if (_hero == null || !_hero.IsBoundToEntity) return;
                _heroRaw = local.Raw;
                Log("HERO entity=" + local.Raw);
            }
            WatchAction();
            long now = Environment.TickCount64;
            if (now < _startupAt) return;
            _startupAt = now+250;
            RepairStartup(now, local);
        }
        catch (Exception e) { Error("late-update",e); }
    }

    private static void RepairStartup(long now, EntityRef local)
    {
        var frame = _hero.PredictedFrame;
        if (frame == null) return;
        var item = EquipmentAPI.GetEquippedOffhand(frame,local);
        var armament = item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(frame,item) : null;
        if (armament?.Id.Value != RepairPolicy.Armament) { Startup.Reset(); return; }
        var itemView = _hero.LeftHandItemView;
        if (itemView == null || itemView.ItemEntity.Raw != item.Raw || itemView.HeroView?.Pointer != _hero.Pointer) return;
        var guid = new AssetGuid(RepairPolicy.Action);
        var action = new AssetRefActionData { Id = guid };
        bool loaded = _hero.HasViewForAction(action);
        bool targetPending = _hero.HasPendingInstantiationRequest(guid);
        bool anyPending = _hero.HasAnyPendingInstantiationRequest();
        bool shooting = _hero.CurrentActionLayerActionView?.QuantumAsset?.AssetGuid.Value == RepairPolicy.Action;
        string state = "hero="+local.Raw+"; item="+item.Raw+"; itemView="+itemView.Pointer+
            "; loaded="+loaded+"; targetPending="+targetPending+"; anyPending="+anyPending;
        if (state != _startupState) { _startupState = state; Log("STARTUP_STATE "+state); }
        if (!Startup.Decide(now,local.Raw,item.Raw,itemView.Pointer.ToInt64(),loaded,targetPending,anyPending,shooting)) return;
        // Native implementation cancels item-specific requests before enumerating
        // the full list. Preserve every dependency; never pass a target-only list.
        var source = _hero.GetTempListOfItemActions(frame,item);
        if (source == null) throw new InvalidOperationException("Native action dependency list absent");
        var copy = new Il2CppSystem.Collections.Generic.List<AssetRefActionData>();
        bool found = false;
        foreach (var entry in source) { copy.Add(entry); found |= entry.Id.Value == RepairPolicy.Action; }
        if (!found) copy.Add(action);
        if (_hero.HasViewForAction(action) || _hero.HasPendingInstantiationRequest(guid)) return;
        // Native #21423 enumerates synchronously and copies each action/item
        // into the async request. No collection is captured beyond this call.
        Startup.Requested(now);
        Log("STARTUP_REQUEST attempt="+Startup.Attempts+"; count="+copy.Count+"; "+state);
        _hero.InstantiateDynamicItemActionsAsync(frame,itemView,copy);
    }

    private static void BeforeStart(ActionView __0, EntityView __1)
    {
        if (__0 == null) return;
        try
        {
            // A reused view is a safe boundary even after the addon disables.
            if (Saved.TryGetValue(__0.Pointer,out var old))
            {
                if (_active == old) CancelActive("reused");
                if (!Restore(old)) { Log("SKIP previous field restoration pending"); return; }
            }
            if (!_enabled || __1 == null) return;
            if (__0.QuantumAsset?.AssetGuid.Value != RepairPolicy.Action) return;
            var hero = __1.TryCast<HeroView>();
            var vf = ViewFrame.Active;
            if (hero == null || !hero.IsBoundToEntity || vf == null || vf.IsDisposed || vf.Players.Item2 == null ||
                !vf.Players.Item2.TryGetBoundHeroEntity(out EntityRef local) || local.Raw != hero.EntityRef.Raw) return;
            var frame = hero.PredictedFrame;
            if (frame == null) return;
            var item = EquipmentAPI.GetEquippedOffhand(frame,local);
            var armament = item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(frame,item) : null;
            if (armament?.Id.Value != RepairPolicy.Armament) return;
            if (_active != null) CancelActive("next action");
            var timeline = __0.GetComponent<MoonTimeline>();
            if (timeline == null) { Log("SKIP no root timeline"); return; }
            var weaponView = hero.LeftHandItemView?.TryCast<HeroWeaponView>();
            if (weaponView == null || weaponView.ItemEntity.Raw != item.Raw || weaponView.HeroView?.Pointer != hero.Pointer)
            { Log("SKIP weapon view binding absent"); return; }
            // Covers an already bound view if a plugin was loaded after creation.
            if (NativeAudio.CompleteWeapon(weaponView))
                weaponView.AudioCompanion.OnInstantiatedFromPool(local);
            var host = weaponView.AudioCompanion?.WeaponSoundHost;
            if (host == null || !host.IsActive) { Log("SKIP native weapon sound host unavailable"); return; }
            WwiseSoundAnimator raise = null;
            foreach (var entry in timeline.EntityRecords)
            {
                if (entry == null || entry.Id.Value != 1441) continue;
                var candidate = entry.TimelineItem?.TryCast<WwiseSoundAnimator>();
                if (candidate != null && entry.StartConstraint != null && entry.EndConstraint != null &&
                    NativeAudioPolicy.CanReplaceRaise(candidate.Event?.Guid.ToString(),entry.Id.Value,
                        entry.StartConstraint.TimeOffset,entry.EndConstraint.TimeOffset)) raise = candidate;
            }
            if (raise == null) { Log("SKIP unknown native raise pair"); return; }
            // Resolve both real Event references before muting any original track.
            var nativeRaise = NativeAudio.Raise; var nativeFire = NativeAudio.Fire;
            if (nativeRaise.Guid.ToString() != NativeAudioPolicy.RaiseGuid || nativeFire.Guid.ToString() != NativeAudioPolicy.FireGuid ||
                !nativeRaise.IsValid() || !nativeFire.IsValid() || nativeRaise.GetId(raise) != NativeAudioPolicy.RaiseId ||
                nativeFire.GetId(raise) != NativeAudioPolicy.FireId)
                throw new InvalidOperationException("Crossbow native event reference identity mismatch");
            foreach (var record in timeline.EntityRecords)
            {
                if (record == null || record.Id.Value != 2099) continue;
                var sound = record.TimelineItem?.TryCast<WwiseSoundAnimator>();
                var begin = record.StartConstraint; var end = record.EndConstraint;
                if (sound == null || (int)sound.EventStyle != 0 || sound.FireAndForget || sound.LoopWithParent ||
                    begin == null || end == null || begin.ConstrainedTo.Value != -1 || begin.EventId != 0 ||
                    (int)begin.Flags != 0 || end.ConstrainedTo.Value != 2099 || end.EventId != 0 || (int)end.Flags != 0 ||
                    !RepairPolicy.CanMute(RepairPolicy.Action,armament.Id.Value,true,record.Id.Value,sound.Event?.Guid.ToString(),begin.TimeOffset,end.TimeOffset))
                { Log("SKIP unknown cast record/constraints"); return; }
                var empty = EventReference.Empty;
                if (empty == null || empty.m_shortId != 0 || empty.Guid.ToString() != Guid.Empty.ToString())
                    throw new InvalidOperationException("Empty sound reference must be a non-null invalid event");
                var excluded = new List<ulong>();
                foreach (var projectile in UnityEngine.Object.FindObjectsOfType<ProjectileView>())
                    if (projectile != null && projectile.IsBoundToEntity) excluded.Add(projectile.EntityRef.Raw);
                var shot = new Shot { View=__0, ViewPointer=__0.Pointer, HostPointer=host.Pointer, Sound=sound, Empty=empty, HeroView=hero,
                    Binding=new ShotBinding(local.Raw,item.Raw,hero.Pointer.ToInt64(),weaponView.Pointer.ToInt64(),__0.Pointer.ToInt64()),
                    Sequence=++_sequence, Raise=raise };
                shot.Release.Begin(local.Raw,item.Raw,excluded);
                Saved[__0.Pointer] = shot;
                Change(shot.RaiseFields,()=>raise.Event,v=>raise.Event=v,nativeRaise,(a,b)=>a?.Pointer==b?.Pointer);
                Change(shot.RaiseFields,()=>(int)raise.EventStyle,v=>raise.EventStyle=(Il2Cpp.WwiseGlobalDefinitions.TimelineEventStyle)v,0);
                Change(shot.RaiseFields,()=>raise.MoonSoundHost,v=>raise.MoonSoundHost=v,new MoonReference<SoundHost>(host),(a,b)=>a?.Pointer==b?.Pointer);
                Change(shot.CastFields,()=>sound.MoonSoundHost,v=>sound.MoonSoundHost=v,new MoonReference<SoundHost>(host),(a,b)=>a?.Pointer==b?.Pointer);
                // Native #9596 only clears the cached host. Re-resolve the exact
                // weapon host even if this pooled action was previously used.
                raise.OnInstantiatedFromPool();
                sound.OnInstantiatedFromPool();
                if (raise.EffectiveSoundHost?.Pointer != host.Pointer || sound.EffectiveSoundHost?.Pointer != host.Pointer)
                    throw new InvalidOperationException("Native animator did not resolve the intended weapon sound host");
                Change(shot.CastFields,()=>sound.Event,v=>sound.Event=v,empty,(a,b)=>a?.Pointer==b?.Pointer);
                // Keep both original constraints and total timeline length.
                if (sound.Event?.Pointer != empty.Pointer) throw new InvalidOperationException("Empty event assignment did not stick");
                // A shot can begin before this addon's first late-update discovery.
                // Establish the same binding here so discovery cannot cancel it.
                _hero = hero; _heroRaw = local.Raw;
                _active = shot;
                Log("ARMED shot="+shot.Sequence+"; hero="+local.Raw+"; item="+item.Raw+"; view="+__0.Pointer+
                    "; sound="+sound.Pointer+"; start="+begin.TimeOffset+"; endRelative="+end.TimeOffset+"; existingExcluded="+excluded.Count+
                    "; raise="+NativeAudioPolicy.RaiseGuid+"; fire="+NativeAudioPolicy.FireGuid+"; weaponHost="+host.Pointer);
                return;
            }
            Log("SKIP cast record absent");
        }
        catch (Exception e)
        {
            try
            {
                if (__0 != null && Saved.TryGetValue(__0.Pointer,out var state))
                { if (_active == state) CancelActive("arming failed"); Restore(state); }
            }
            catch (Exception rollback) { Error("arm rollback",rollback); }
            Error("arm",e);
        }
    }

    private static void WatchAction()
    {
        var shot = _active;
        if (shot == null || shot.Fired) return;
        if (!HasCurrentBinding(shot)) CancelActive("action/equipment ended or rebound");
    }

    private static bool HasCurrentBinding(Shot shot)
    {
        var vf = ViewFrame.Active;
        if (_hero == null || !_hero.IsBoundToEntity || vf == null || vf.IsDisposed || vf.Players.Item2 == null ||
            !vf.Players.Item2.TryGetBoundHeroEntity(out EntityRef local) || local.Raw != _hero.EntityRef.Raw) return false;
        var current = _hero.CurrentActionLayerActionView;
        if (current == null || current.m_playback == null) return false;
        var frame = _hero.PredictedFrame;
        if (frame == null) return false;
        var item = EquipmentAPI.GetEquippedOffhand(frame,local);
        var view = _hero.LeftHandItemView;
        if (!item.IsValid || view == null || view.ItemEntity.Raw != item.Raw || view.HeroView?.Pointer != _hero.Pointer) return false;
        return shot.Binding.Matches(new ShotBinding(local.Raw,item.Raw,_hero.Pointer.ToInt64(),view.Pointer.ToInt64(),current.Pointer.ToInt64()));
    }

    private static void PatchNative(Type type, string method, string callback, bool prefix)
    {
        var target = type.GetMethod(method, BindingFlags.Public|BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (target == null || target.ReturnType != typeof(void) || target.IsAbstract)
            throw new MissingMethodException(type.FullName,method+" exact void/no-argument signature");
        var patch = new HarmonyMethod(typeof(Repair),callback);
        _patches.Patch(target, prefix:prefix ? patch : null, postfix:prefix ? null : patch);
    }

    private static void BeforeWeaponInstantiation(HeroWeaponView __instance)
    {
        if (!_enabled) return;
        try
        {
            if (NativeAudio.CompleteWeapon(__instance))
                Log("WEAPON_AUDIO_COMPLETED view="+__instance.Pointer+"; prefab="+__instance.gameObject.name+
                    "; companion="+__instance.AudioCompanion.Pointer+"; host="+__instance.AudioCompanion.WeaponSoundHost.Pointer);
        }
        catch (Exception e) { Error("weapon audio binding",e); }
    }

    private static void AfterProjectileReleased(ProjectileView __instance)
    {
        if (!_enabled || __instance == null || !__instance.IsBoundToEntity) return;
        var shot = _active;
        if (shot == null || shot.Fired || _hero == null) return;
        try
        {
            if (!HasCurrentBinding(shot))
            { CancelActive("action/equipment changed before native release"); return; }
            if (shot.Sound == null || shot.Sound.Event?.Pointer != shot.Empty.Pointer)
            { CancelActive("cast event ownership changed"); return; }
            var frame = _hero.PredictedFrame;
            if (frame == null) return;
            var projectile = __instance;
            ulong entity = projectile.EntityRef.Raw;
            // ProjectileView.m_ownerEntity/m_weaponEntity belong to dummy arrows.
            // The simulation component proves real projectile ownership. This
            // generated component is an unmanaged value type; layout is checked
            // before enabling. Never use this out-buffer bridge for class types.
            if (!frame.TryGet<ProjectileComponent>(projectile.EntityRef,out var data) ||
                data.Data.Id.Value != 3144285392061863817) return;
            ulong owner = data.InstigatorRef.Raw;
            ulong weapon = data.FiringWeaponData.Raw;
            // The native callback is the release transition; polling IsReleased
            // or current simulated State can miss arrows stopped in this frame.
            if (!shot.Release.Claim(entity,owner,weapon,true)) return;
            shot.Fired = true; // Claim before calling audio; never retry a possible successful post.
            long observed = Stopwatch.GetTimestamp();
            var host = shot.Sound.EffectiveSoundHost;
            if (host == null || !host.IsActive || host.Pointer != shot.HostPointer)
                throw new InvalidOperationException("Released shot sound host unavailable or changed");
            // Use the original engine sound animator, including its SoundHandle
            // and host lifecycle, rather than posting directly to Wwise.
            try { shot.Sound.Event = NativeAudio.Fire; shot.Sound.OnStartPlayback(); }
            finally
            {
                if (shot.Sound.Event?.Pointer == NativeAudio.Fire.Pointer) shot.Sound.Event = shot.Empty;
            }
            uint playing = shot.Sound.m_playingId;
            if (playing == 0) throw new InvalidOperationException("Cast event returned an invalid playing ID");
            long posted = Stopwatch.GetTimestamp();
            Log("FIRED shot="+shot.Sequence+"; entity="+entity+"; owner="+owner+"; weapon="+weapon+
                "; event="+NativeAudioPolicy.FireGuid+"; playingId="+playing+"; sound="+shot.Sound.Pointer+
                "; view="+shot.View.Pointer+"; actionTime="+shot.View.ActionViewTime+"; frame="+UnityEngine.Time.frameCount+
                "; releaseObservedTick="+observed+"; postedTick="+posted+"; frequency="+Stopwatch.Frequency+
                "; postCallMs="+(posted-observed)*1000.0/Stopwatch.Frequency+"; source=native release callback; soundHandle="+shot.Sound.m_soundHandle.IsValid);
            return;
        }
        catch (Exception e) { Error("native release sound",e); }
    }

    private static unsafe void VerifyProjectileLayout()
    {
        // sizeof is also a compile-time rejection of managed fields in this
        // struct. Native offsets were independently read from metadata registration.
        if (sizeof(ProjectileComponent) != ProjectileComponent.SIZE || sizeof(EntityRef) != 8 ||
            Marshal.OffsetOf<ProjectileComponent>(nameof(ProjectileComponent.Data)).ToInt32() != 32 ||
            Marshal.OffsetOf<ProjectileComponent>(nameof(ProjectileComponent.State)).ToInt32() != 20 ||
            Marshal.OffsetOf<ProjectileComponent>(nameof(ProjectileComponent.FiringWeaponData)).ToInt32() != 40 ||
            Marshal.OffsetOf<ProjectileComponent>(nameof(ProjectileComponent.InstigatorRef)).ToInt32() != 48)
            throw new InvalidOperationException("Projectile value-component ABI differs from inspected local build");
    }

    private static void BeforeReturn(ActionView __0)
    {
        if (__0 == null) return;
        try
        {
            if (Saved.TryGetValue(__0.Pointer,out var state))
            { if (_active == state) CancelActive("returned"); Restore(state); }
        }
        catch (Exception e) { Error("restore",e); }
    }
    private static void CancelActive(string reason)
    {
        var shot = _active; _active = null;
        if (shot == null) return;
        shot.Release.Cancel();
        if (!shot.Fired) Log("CANCELLED shot="+shot.Sequence+"; reason="+reason);
        // A still-live cancelled view stays muted until return/reuse. Restoring
        // too early could let a later natural timeline start fire an orphan sound.
    }
    private static void Change<T>(List<IRestorableField> fields, Func<T> get, Action<T> set, T replacement, Func<T,T,bool> same=null)
    {
        var field = new OwnedField<T>(get,set,replacement,same);
        fields.Add(field); // Register before mutation, including partial setter failures.
        field.Apply();
    }
    private static bool Restore(Shot shot)
    {
        RestoreFields(shot.CastFields,shot.Sound != null,shot.Sequence);
        RestoreFields(shot.RaiseFields,shot.Raise != null,shot.Sequence);
        if (shot.CastFields.Any(f=>f.Pending) || shot.RaiseFields.Any(f=>f.Pending)) return false;
        try
        {
            // #9596 only clears the native cached host. A missing return callback
            // must not leave the restored fields backed by the previous host.
            if (!shot.CastCacheReset && shot.Sound != null && shot.CastFields.Count > 0) shot.Sound.OnInstantiatedFromPool();
            shot.CastCacheReset=true;
            if (!shot.RaiseCacheReset && shot.Raise != null && shot.RaiseFields.Count > 0) shot.Raise.OnInstantiatedFromPool();
            shot.RaiseCacheReset=true;
        }
        catch (Exception e) { Error("restored host cache",e); return false; }
        Saved.Remove(shot.ViewPointer);
        return true;
    }
    private static void RestoreFields(List<IRestorableField> fields, bool ownerAlive, int sequence)
    {
        foreach (var field in fields)
        {
            if (!ownerAlive) { field.Forget(); continue; }
            try
            {
                if (field.Restore()) Log("RESTORE_CONFLICT shot="+sequence+"; foreign field preserved");
            }
            catch (Exception e) { Error("field restore",e); }
        }
    }
    private static void SweepSaved()
    {
        long now = Environment.TickCount64;
        if (now < _cleanupAt) return;
        _cleanupAt = now+250;
        foreach (var shot in Saved.Values.ToArray())
        {
            try
            {
                if (shot.View == null)
                {
                    if (_active == shot) CancelActive("view destroyed");
                    Saved.Remove(shot.ViewPointer);
                    Log("CLEANED shot="+shot.Sequence+"; reason=view destroyed; savedRemaining="+Saved.Count);
                    continue;
                }
                bool current = shot.HeroView != null && shot.HeroView.IsBoundToEntity &&
                    shot.HeroView.CurrentActionLayerActionView?.Pointer == shot.ViewPointer;
                var playback = shot.View.m_playback;
                bool playing = playback != null && !playback.IsDisposed && playback.IsTimelinePlayingOrPaused();
                if (!RestorePolicy.CanRestore(false,current,playing)) continue;
                if (_active == shot) CancelActive("inactive timeline cleanup");
                if (Restore(shot)) Log("CLEANED shot="+shot.Sequence+"; reason=inactive timeline; savedRemaining="+Saved.Count);
            }
            catch (Exception e) { Error("saved view cleanup",e); }
        }
    }
    private static void ClearBinding()
    {
        CancelActive("hero/map unbound");
        _hero = null; _heroRaw = 0; Startup.Reset(); _startupState = null;
    }
    private static void Log(string text) => _self.LoggerInstance.Msg("[bowgun-repair] utc="+DateTime.UtcNow.ToString("o")+" "+text);
    private static void Error(string stage,Exception error)
    {
        if (++_errors <= 3) _self.LoggerInstance.Warning("[bowgun-repair] ERROR stage="+stage+"; "+error);
        if (_errors >= 3 && _enabled)
        {
            _enabled = false; CancelActive("disabled after errors");
            _self.LoggerInstance.Warning("[bowgun-repair] DISABLED; live tracks remain muted until return/reuse or inactive timeline cleanup; baseline callbacks continue");
        }
    }
    public override void OnDeinitializeMelon()
    {
        BlockInputRouting.Shutdown();
        _enabled = false; CancelActive("shutdown");
        SweepSaved();
        // Process shutdown tears down live engine objects. Avoid unmuting them
        // while their timeline could still run during teardown.
        Saved.Clear(); _hero=null; _heroRaw=0;
        _patches?.UnpatchSelf();
    }
}
