using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMoon.Collections;
using Il2CppMoon.Forsaken;
using Il2CppMoon.TheLoop;
using Il2CppQuantum;
using PatchOwner = HarmonyLib.Harmony;
using EntityView = Il2CppMoon.Forsaken.EntityView;

namespace CrucibleUnlock
{
    internal static class BowgunInputRepair
    {
        private static bool _enabled;
        private static int _errors, _inputLogs, _comboLogs, _projectileLogs;
        private static long _boundHeroRaw;
        private static int _bindingLogs, _callbackSeen, _buttonLogs, _lastButtonMask;
        private static int _hudLogs, _pollLogs, _armamentLogs, _actionLogs, _lastPollMask;
        private static int _flagLogs, _lastRoutedModifiers;
        private static int _weaponClass, _classLogs;
        private static long _captureUntil;
        private static string _lastAction;
        [ThreadStatic] private static bool _suppressHudBlock;
        // Rooted for the whole session: the converted RuntimeActionInfo keeps a managed reference
        // into this array, so a local array could be collected and leave a dangling reference.
        private static Il2CppStructArray<AssetRefActionData> _shotActions;
        private static ActionView _animationView;
        private static long _animationStarted;
        private static int _animationStage, _animationRuns, _animationErrors;
        private static int _viewLogs, _resolveLogs, _startLogs, _bridgeLogs;
        private static string _lastViewState;
        private static int _viewRepairLogs, _viewRepairErrors;
        private static IntPtr _presentationItem;
        private static ulong _presentationHero, _presentationEntity;
        private static long _nextPresentationCheck;
        private static int _presentationAttempts, _presentationLogs;
        private static string _presentationState;
        private static Il2CppSystem.Collections.Generic.List<AssetRefActionData> _presentationActions;

        // Run after equipment assignment, not inside the initial pool callback. Preserve
        // all existing dependencies, wait for native requests, and request at most three
        // times per actual equipment binding. Native code retains ownership of cleanup.
        private static void AfterEquipmentUpdate(HeroView __instance, Frame __0, EntityRef __1)
        {
            if (!_enabled || _viewRepairErrors >= 3 || __instance == null || __0 == null ||
                __1.Raw != unchecked((ulong)Volatile.Read(ref _boundHeroRaw))) return;
            try
            {
                var item = EquipmentAPI.GetEquippedOffhand(__0, __1);
                var armament = item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(__0, item) : null;
                if (armament == null || armament.Id.Value != BowgunInputPolicy.ArmamentGuid) return;
                var itemView = __instance.LeftHandItemView;
                var pointer = itemView?.Pointer ?? IntPtr.Zero;
                if (_presentationItem != pointer || _presentationHero != __1.Raw || _presentationEntity != item.Raw)
                {
                    _presentationItem = pointer; _presentationHero = __1.Raw; _presentationEntity = item.Raw;
                    _presentationAttempts = 0; _nextPresentationCheck = Environment.TickCount64 + 1000;
                }
                bool bound = itemView != null && itemView.ItemEntity.Raw == item.Raw && itemView.HeroView?.Pointer == __instance.Pointer;
                bool hasView = __instance.HasViewForAction(new AssetRefActionData { Id = new AssetGuid(BowgunInputPolicy.ActionGuid) });
                bool pending = __instance.HasAnyPendingInstantiationRequest();
                string state = "hero=" + __1.Raw + "; item=" + item.Raw + "; itemView=" + pointer +
                    "; bound=" + bound + "; hasView=" + hasView + "; pending=" + pending;
                if (state != _presentationState && _presentationLogs++ < 30)
                { _presentationState = state; TrialRepairLog.Write("[bowgun-view-repair/r2] EQUIPMENT " + state); }
                if (!bound || hasView || pending || _presentationAttempts >= 3 || Environment.TickCount64 < _nextPresentationCheck) return;
                ++_presentationAttempts;
                _nextPresentationCheck = Environment.TickCount64 + 5000;
                // Copy the native shared scratch list before starting asynchronous work.
                var source = __instance.GetTempListOfItemActions(__0, item);
                if (source == null) throw new InvalidOperationException("Native item action list is null");
                _presentationActions = new Il2CppSystem.Collections.Generic.List<AssetRefActionData>();
                bool found = false;
                foreach (var action in source)
                { _presentationActions.Add(action); found |= action.Id.Value == BowgunInputPolicy.ActionGuid; }
                if (!found) _presentationActions.Add(new AssetRefActionData { Id = new AssetGuid(BowgunInputPolicy.ActionGuid) });
                TrialRepairLog.Write("[bowgun-view-repair/r2] REQUEST attempt=" + _presentationAttempts +
                    "; count=" + _presentationActions.Count + "; itemView=" + pointer + "; alreadyListed=" + found);
                __instance.InstantiateDynamicItemActionsAsync(__0, itemView, _presentationActions);
            }
            catch (Exception e)
            { if (++_viewRepairErrors <= 3) TrialRepairLog.Write("[bowgun-view-repair/r2] ERROR " + e); }
        }

        // Native GetTempListOfItemActions enumerates the armament table; the routed LB
        // GetSpecialCombo override intentionally does not run during equipment setup.
        // Supply its missing presentation dependency to the native async loader. Native
        // code owns prefab loading, item resolver hookup, registration and pool cleanup.
        private static void AfterCollectItemActions(HeroView __instance, Frame __0, EntityRef __1,
            Il2CppSystem.Collections.Generic.List<AssetRefActionData> __result)
        {
            if (!_enabled || _viewRepairErrors >= 3 || __instance == null || __0 == null || __result == null || !__1.IsValid) return;
            try
            {
                if (!__instance.IsBoundToEntity) return;
                var offhand = EquipmentAPI.GetEquippedOffhand(__0, __instance.EntityRef);
                if (!offhand.IsValid || offhand.Raw != __1.Raw) return;
                var armament = ArmamentAPI.TryGetRuntimeArmament(__0, __1);
                if (armament == null || armament.Id.Value != BowgunInputPolicy.ArmamentGuid) return;
                foreach (var action in __result)
                    if (action.Id.Value == BowgunInputPolicy.ActionGuid) return;
                __result.Add(new AssetRefActionData { Id = new AssetGuid(BowgunInputPolicy.ActionGuid) });
                if (++_viewRepairLogs <= 12)
                    TrialRepairLog.Write("[bowgun-view-repair] ACTION_DEPENDENCY_ADDED hero=" + __instance.EntityRef.Raw +
                        "; item=" + __1.Raw + "; action=" + BowgunInputPolicy.ActionGuid + "; native async loader owns presentation");
            }
            catch (Exception e)
            {
                if (++_viewRepairErrors <= 3) TrialRepairLog.Write("[bowgun-view-repair] ERROR " + e);
            }
        }

        internal static void Configure(int weaponClass) => _weaponClass = weaponClass;

        internal static unsafe void Install()
        {
            var owner = new PatchOwner("NRFW.CrucibleUnlock.BowgunLB");
            try
            {
                if (sizeof(Input) != 0x110 || sizeof(Button) != 12 || sizeof(EntityRef) != 8 ||
                    Marshal.OffsetOf<Input>(nameof(Input.Attack)).ToInt32() != 0x28 ||
                    Marshal.OffsetOf<Input>(nameof(Input.Offhand)).ToInt32() != 0x94 ||
                    Marshal.OffsetOf<Input>(nameof(Input.Modifiers)).ToInt32() != 0x0c)
                    throw new InvalidOperationException("Bowgun input ABI differs from the inspected native build.");
                _shotActions = new Il2CppStructArray<AssetRefActionData>(1);
                _shotActions[0] = new AssetRefActionData { Id = new AssetGuid(BowgunInputPolicy.ActionGuid) };
                foreach (var hook in BowgunHookPlan.Hooks)
                {
                    var assembly = hook.Assembly == "Il2Cpp__forsaken"
                        ? typeof(QuantumLocalInputSource).Assembly : typeof(WeaponStaticData).Assembly;
                    var type = assembly.GetType(hook.Type, throwOnError: true);
                    var matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => m.Name == hook.Method && TypeName(m.ReturnType) == hook.ReturnType &&
                            m.GetParameters().Select(p => TypeName(p.ParameterType)).SequenceEqual(hook.Parameters)).ToArray();
                    if (matches.Length != 1) throw new MissingMethodException(hook.Type, hook.Method + " exact signature");
                    var method = matches[0];
                    TrialRepairLog.Patch(owner, type, hook.Method, method.GetParameters().Select(p => p.ParameterType).ToArray(),
                        typeof(BowgunInputRepair), prefix: hook.Prefix, postfix: hook.Postfix, returnType: method.ReturnType);
                }
                _enabled = true;
                TrialRepairLog.Write("[bowgun-lb/r6] INSTALLED " + BowgunHookPlan.Hooks.Length +
                    " hooks; routed-window GetSpecialCombo override, anchored action array, weapon-class write, HUD block suppression; official 0.9.5 baseline");
                try
                {
                    TrialRepairLog.Write("[bowgun-lb/r4] native OffhandSpecialModifiers=" + (int)Input.OffhandSpecialModifiers +
                        "; routed modifiers=" + BowgunInputPolicy.OffhandAttack1);
                }
                catch (Exception probe) { TrialRepairLog.Write("[bowgun-lb/r4] OffhandSpecialModifiers probe failed: " + probe.Message); }
            }
            catch (Exception e)
            {
                _enabled = false;
                owner.UnpatchSelf();
                TrialRepairLog.Write("[bowgun-lb] INSTALL_FAILED " + e);
            }
        }

        private static string TypeName(Type type)
        {
            if (type == typeof(void)) return "Void";
            if (type.IsGenericType)
                return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
            return type.FullName;
        }

        // Main thread only. Native input-source m_localHeroRef is not populated.
        // Native OnItemEquipped uses this live binding API; retain only a value,
        // and query actual equipment from the input callback's simulation frame.
        internal static void UpdateLocalHero()
        {
            if (!_enabled) return;
            ObserveAnimationProgress();
            ulong raw = 0;
            try
            {
                var viewFrame = ViewFrame.Active;
                if (viewFrame != null && !viewFrame.IsDisposed)
                {
                    var players = viewFrame.Players.Item2;
                    if (players != null && players.TryGetBoundHeroEntity(out EntityRef hero) && hero.IsValid)
                        raw = hero.Raw;
                }
            }
            catch (Exception e) { Error("hero-binding", e); }
            long previous = Interlocked.Exchange(ref _boundHeroRaw, unchecked((long)raw));
            if (unchecked((ulong)previous) != raw && Interlocked.Increment(ref _bindingLogs) <= 20)
                TrialRepairLog.Write("[bowgun-lb/r2] HERO_BINDING previous=" + unchecked((ulong)previous) + "; current=" + raw);
        }

        private static void AfterInput(QuantumLocalInputSource __instance, Frame __0)
        {
            if (Interlocked.Exchange(ref _callbackSeen, 1) == 0)
                TrialRepairLog.Write("[bowgun-lb/r2] INPUT_CALLBACK_ENTERED frame=" + (__0 != null));
            if (!_enabled || __instance == null || __0 == null) return;
            try
            {
                var revised = __instance.m_revisedInput;
                var hero = new EntityRef { Raw = unchecked((ulong)Volatile.Read(ref _boundHeroRaw)) };
                if (revised == null) return;
                var input = __instance.m_input;
                // Native logical action 2 feeds Input.Offhand; default gamepad binding is LB.
                // GetButton respects game focus, category locks and the game's input buffer.
                bool lb = revised.GetButton(RevisedInputActions.Default_Block, true);
                bool special = revised.GetButton(RevisedInputActions.Default_SpecialAttack, true);
                bool gameplay = input.GameplayInputSent && !revised.CinematicLock &&
                    !__instance.m_cancellingMenu && __instance.m_menuBuffer <= 0;
                bool gamepad = revised.CurrentInputStyleDetected == CurrentInputStyle.Gamepad;
                int mask = (lb ? 1 : 0) | (special ? 2 : 0);
                int previousMask = Interlocked.Exchange(ref _lastButtonMask, mask);
                if (mask == 0) return;
                var item = hero.IsValid ? EquipmentAPI.GetEquippedOffhand(__0, hero) : default;
                var armament = item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(__0, item) : null;
                if (mask != 0 && mask != previousMask && Interlocked.Increment(ref _buttonLogs) <= 20)
                    TrialRepairLog.Write("[bowgun-lb/r2] BUTTON_CHECK block=" + lb + "; special=" + special +
                        "; style=" + revised.CurrentInputStyleDetected + "; gameplay=" + gameplay +
                        "; sent=" + (bool)input.GameplayInputSent + "; menu=" + __instance.m_cancellingMenu +
                        "; menuBuffer=" + __instance.m_menuBuffer + "; cinematic=" + revised.CinematicLock +
                        "; hero=" + hero.Raw + "; offhand=" + item.Raw +
                        "; armament=" + (armament == null ? 0 : armament.Id.Value));
                if (!gameplay || !gamepad || !lb || !hero.IsValid || !item.IsValid) return;
                if (!BowgunInputPolicy.ShouldRoute(armament == null ? 0 : armament.Id.Value, gameplay, gamepad, lb)) return;

                // Use the same bool -> Button representation as native input polling.
                // Quantum updates WasPressed from held input per simulation frame, so holding
                // LB is not turned into repeated synthetic press/release events.
                input.Offhand = true;
                input.Attack = true;
                input.Modifiers = (InputModifier)BowgunInputPolicy.ShotModifiers((int)input.Modifiers);
                __instance.m_input = input;
                Volatile.Write(ref _lastRoutedModifiers, (int)input.Modifiers);
                Volatile.Write(ref _captureUntil, Environment.TickCount64 + 2500);
                if (Interlocked.Increment(ref _inputLogs) <= 8)
                    TrialRepairLog.Write("[bowgun-lb] INPUT_ROUTED hero=" + hero.Raw + "; item=" + item.Raw + "; modifiers=" + input.Modifiers);
            }
            catch (Exception e) { Error("input", e); }
        }

        // Native PlayerEquipmentHUD.UpdateState reads logical Block directly,
        // independently of Quantum.Input. Suppress only this HUD read, for this
        // exact local offhand. Other callers and the RB/SpecialAttack read survive.
        private static void BeforeHudState(Frame __0, EntityRef __1, out bool __state)
        {
            __state = _suppressHudBlock;
            _suppressHudBlock = false;
            if (!_enabled || __0 == null || !__1.IsValid ||
                __1.Raw != unchecked((ulong)Volatile.Read(ref _boundHeroRaw))) return;
            try
            {
                var item = EquipmentAPI.GetEquippedOffhand(__0, __1);
                var armament = item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(__0, item) : null;
                _suppressHudBlock = armament != null && armament.Id.Value == BowgunInputPolicy.ArmamentGuid;
            }
            catch (Exception e) { Error("hud-scope", e); }
        }

        private static void AfterHudState(bool __state) => _suppressHudBlock = __state;

        private static void AfterHudButton(RevisedInput __instance, int __0, ref bool __result)
        {
            if (!_enabled || !_suppressHudBlock || !__result || __0 != RevisedInputActions.Default_Block ||
                __instance.CurrentInputStyleDetected != CurrentInputStyle.Gamepad) return;
            __result = false;
            if (Interlocked.Increment(ref _hudLogs) <= 8)
                TrialRepairLog.Write("[bowgun-lb/r3] HUD_LB_CONSUMED; RB and gameplay input preserved");
        }

        // Input is a real blittable managed struct (0x110 bytes), unlike the
        // reference-bearing RuntimeActionInfo wrapper. Observe the complete
        // Poll return after m_input has been copied and cleared by native code.
        private static void AfterPoll(Input __result)
        {
            if (!_enabled || _pollLogs >= 24) return;
            try
            {
                var input = __result;
                int mask = (input.Attack._frameDown > input.Attack._frameUp ? 1 : 0) |
                    (input.Offhand._frameDown > input.Offhand._frameUp ? 2 : 0) | (int)input.Modifiers;
                if (Interlocked.Exchange(ref _lastPollMask, mask) == mask ||
                    Environment.TickCount64 > Volatile.Read(ref _captureUntil)) return;
                Interlocked.Increment(ref _pollLogs);
                TrialRepairLog.Write("[bowgun-lb/r3] POLL_OUTPUT modifiers=" + input.Modifiers +
                    "; attack=" + input.Attack._frameUp + "/" + input.Attack._frameDown + "/" + input.Attack._frameCurrent +
                    "; offhand=" + input.Offhand._frameUp + "/" + input.Offhand._frameDown + "/" + input.Offhand._frameCurrent);
            }
            catch (Exception e) { Error("poll-observer", e); }
        }

        private static void AfterControllerUpdate(Frame __0, EntityRef __2)
        {
            if (!_enabled || __0 == null || _actionLogs >= 32 ||
                Environment.TickCount64 > Volatile.Read(ref _captureUntil)) return;
            try
            {
                if (!__0.Has<PlayerControllerComponent>(__2)) return;
                var controller = __0.Get<PlayerControllerComponent>(__2);
                var hero = controller.Hero;
                if (!hero.IsValid || hero.Raw != unchecked((ulong)Volatile.Read(ref _boundHeroRaw)) ||
                    !__0.Has<ActionComponent>(hero)) return;
                var action = __0.Get<ActionComponent>(hero);
                string state = "base=" + action.BaseLayerData.Id.Value + "; layer=" + action.ActionLayerData.Id.Value +
                    "; baseType=" + action.CurrentBaseType + "; actionType=" + action.CurrentActionType +
                    "; blocked=" + (bool)action.BlockActionExecution + "; queued=" + controller.QueuedPluginIndex;
                if (state == _lastAction) return;
                _lastAction = state;
                Interlocked.Increment(ref _actionLogs);
                TrialRepairLog.Write("[bowgun-lb/r3] CONTROLLER_ACTION " + state);
            }
            catch (Exception e) { Error("controller-observer", e); }
        }

        // Observation only: the element layout of a hand-built Il2CppReferenceArray<RuntimeActionInfo>
        // cannot be matched to the native 0x28-byte inline stride with the evidence we have, so the
        // armament table is never replaced here.
        private static void AfterArmamentCombo(RuntimeArmament __instance, ArmamentActionType __1,
            Il2CppReferenceArray<RuntimeActionInfo> __result)
        {
            if (!_enabled || __instance == null || __instance.Id.Value != BowgunInputPolicy.ArmamentGuid ||
                Interlocked.Increment(ref _armamentLogs) > 16) return;
            TrialRepairLog.Write("[bowgun-lb/r3] ARMAMENT_COMBO type=" + __1 + "; length=" + (__result == null ? -1 : __result.Length));
        }

        // Native GetSpecialStart#56744 -> GetSpecialCombo#56742. Supplying the list here keeps the rest
        // native: GetSpecialStart builds the RuntimeActionInfo, the offhand controller owns action
        // eligibility, left-hand source, aiming, animation events, costs and the projectile.
        // Only answered while this plugin's LB shot is in flight (see BowgunInputPolicy).
        private static bool BeforeSpecialComboList(WeaponStaticData __instance, Frame __0, HeroSpecialType __1,
            EntityRef __2, EntityRef __3, bool __4, ref PooledList<RuntimeActionInfo> __result)
        {
            if (!_enabled || __instance == null) return true;
            try
            {
                bool routedWindow = Environment.TickCount64 <= Volatile.Read(ref _captureUntil);
                if (!BowgunInputPolicy.ShouldProvideSpecialCombo(__instance.Identifier.Guid.Value, (int)__1, routedWindow)) return true;
                ApplyWeaponClass(__instance);
                // RuntimeActionInfo.New is the game's own factory, so the returned list and its
                // 40-byte inline elements are built by native code. The source array is session-rooted.
                __result = RuntimeActionInfo.New(_shotActions);
                if (Interlocked.Increment(ref _comboLogs) <= 8)
                    TrialRepairLog.Write("[bowgun-lb/r5] SPECIAL_LIST_RESOLVED special=" + __1 + "; offhand=" + __4 +
                        "; action=" + BowgunInputPolicy.ActionGuid);
                return false;
            }
            catch (Exception e) { Error("special-list", e); return true; }
        }

        // WeaponClass reaches the hero animator through HeroView.UpdateAnimationParametersValues
        // (native WeaponStaticData.Class 0x128 -> HeroView.WeaponClassParameter 0x518). The shipped
        // bowgun is None(0) and the interop type has no setter, so the field is written directly and
        // only while it still holds the shipped value.
        private static void ApplyWeaponClass(WeaponStaticData weapon)
        {
            if (_weaponClass <= 0 || _classLogs >= 8) return;
            var pointer = weapon.Pointer;
            if (pointer == IntPtr.Zero) return;
            var field = IntPtr.Add(pointer, BowgunInputPolicy.ClassFieldOffset);
            int current = Marshal.ReadInt32(field);
            if (!BowgunInputPolicy.ShouldWriteWeaponClass(_weaponClass, current))
            {
                if (current != _weaponClass)
                {
                    Interlocked.Increment(ref _classLogs);
                    TrialRepairLog.Write("[bowgun-lb/r6] WEAPON_CLASS skipped; field reads " + current + ", expected 0");
                }
                return;
            }
            Marshal.WriteInt32(field, _weaponClass);
            Interlocked.Increment(ref _classLogs);
            TrialRepairLog.Write("[bowgun-lb/r6] WEAPON_CLASS old=" + current + "; new=" + _weaponClass + " (native offset 0x128)");
        }

        private static void BeforeProjectile(WeaponStaticData __instance)
        {
            if (!_enabled || __instance == null) return;
            try
            {
                if (__instance.Identifier.Guid.Value != BowgunInputPolicy.WeaponGuid) return;
                if (!__instance.AllowOffhandSpecials)
                {
                    __instance.AllowOffhandSpecials = true;
                    if (Interlocked.Increment(ref _flagLogs) <= 4)
                        TrialRepairLog.Write("[bowgun-lb/r5] ALLOW_OFFHAND_SPECIALS set on the reinforced bowgun");
                }
                var projectile = __instance.Projectile;
                if (!projectile.ProjectileDataRef.Id.IsValid)
                {
                    projectile.ProjectileOverride = true;
                    projectile.ProjectileDataRef = new AssetRefProjectileData { Id = new AssetGuid(BowgunInputPolicy.ProjectileGuid) };
                    __instance.Projectile = projectile;
                }
                if (Interlocked.Increment(ref _projectileLogs) <= 8)
                    TrialRepairLog.Write("[bowgun-lb] PROJECTILE_RESOLVED guid=" + __instance.Projectile.ProjectileDataRef.Id.Value);
            }
            catch (Exception e) { Error("projectile", e); }
        }

        // Observe the native presentation bridge, without altering its return value,
        // timing, animation references or actor binding. A diagnostic error must never
        // disable the independently verified firing path.
        private static void AfterAnimationBridge(ActionView __instance, bool __result)
        {
            if (!_enabled || __instance == null || _animationRuns >= 6) return;
            try
            {
                long guid = __instance.QuantumAsset?.AssetGuid.Value ?? 0;
                var owner = __instance.OwningEntityView;
                bool window = Environment.TickCount64 <= Volatile.Read(ref _captureUntil);
                bool local = owner != null && owner.EntityRef.Raw == unchecked((ulong)Volatile.Read(ref _boundHeroRaw));
                if (!window || (guid != BowgunInputPolicy.ActionGuid && !local)) return;
                if (_bridgeLogs++ < 24)
                    TrialRepairLog.Write("[bowgun-animation/r2] BRIDGE_ENTRY result=" + __result +
                        "; guid=" + guid + "; owner=" + (owner?.EntityRef.Raw ?? 0) +
                        "; ownerType=" + NativeType(owner) + "; local=" + local + "; shotWindow=" + window);
                if (guid != BowgunInputPolicy.ActionGuid) return;
                var hero = __instance.OwningEntityView?.TryCast<HeroView>();
                // Report each old gate instead of silently discarding the entire observation.
                // Progress snapshots are observation-only even when the owner is not HeroView.
                var frame = hero?.PredictedFrame;
                var item = frame != null ? EquipmentAPI.GetEquippedOffhand(frame, hero.EntityRef) : default;
                var armament = item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(frame, item) : null;
                TrialRepairLog.Write("[bowgun-animation/r2] BRIDGE_GATES heroCast=" + (hero != null) +
                    "; bound=" + (hero?.IsBoundToEntity ?? false) + "; frame=" + (frame != null) +
                    "; offhand=" + item.Raw + "; armament=" + (armament?.Id.Value ?? 0));
                ++_animationRuns;
                _animationView = __instance;
                _animationStarted = Environment.TickCount64;
                _animationStage = 0;
                AnimationSnapshot("BRIDGE result=" + __result);
            }
            catch (Exception e) { AnimationError(e); }
        }

        private static string NativeType(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj)
        {
            if (obj == null) return "<null>";
            var klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(obj.Pointer);
            return Marshal.PtrToStringAnsi(Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_namespace(klass)) + "." +
                Marshal.PtrToStringAnsi(Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name(klass));
        }

        private static bool LocalShot(EntityView view)
            => _enabled && view != null && view.EntityRef.Raw == unchecked((ulong)Volatile.Read(ref _boundHeroRaw)) &&
                Environment.TickCount64 <= Volatile.Read(ref _captureUntil);

        private static void AfterEntityActionView(EntityView __instance, Frame __0, EntityRef __1)
        {
            if (_viewLogs >= 36) return;
            try
            {
                if (!LocalShot(__instance)) return;
                var action = __instance.CurrentActionLayerActionData;
                var view = __instance.CurrentActionLayerActionView;
                string state = "hero=" + __1.Raw + "; entityType=" + NativeType(__instance) +
                    "; viewData=" + (action?.Identifier.Guid.Value ?? 0) + "; view=" + (view?.Pointer ?? IntPtr.Zero) +
                    "; viewType=" + NativeType(view) + "; viewAsset=" + (view?.QuantumAsset?.AssetGuid.Value ?? 0) +
                    "; playback=" + (view?.m_playback != null);
                if (state == _lastViewState) return;
                _lastViewState = state;
                ++_viewLogs;
                TrialRepairLog.Write("[bowgun-animation/r2] ENTITY_VIEW " + state);
            }
            catch (Exception e) { AnimationError(e); }
        }

        private static void AfterResolveActionView(EntityView __instance, ActionData __0, ActionView __result)
        {
            if (_resolveLogs >= 24) return;
            try
            {
                if (!LocalShot(__instance)) return;
                ++_resolveLogs;
                TrialRepairLog.Write("[bowgun-animation/r2] VIEW_RESOLVE requested=" + (__0?.Identifier.Guid.Value ?? 0) +
                    "; result=" + (__result?.Pointer ?? IntPtr.Zero) + "; type=" + NativeType(__result) +
                    "; asset=" + (__result?.QuantumAsset?.AssetGuid.Value ?? 0));
            }
            catch (Exception e) { AnimationError(e); }
        }

        private static void BeforeAnimationStart(ActionView __instance, EntityView __0, int __2)
        {
            if (_startLogs >= 24) return;
            try
            {
                if (!LocalShot(__0)) return;
                ++_startLogs;
                TrialRepairLog.Write("[bowgun-animation/r2] VIEW_START guid=" + (__instance?.QuantumAsset?.AssetGuid.Value ?? 0) +
                    "; view=" + (__instance?.Pointer ?? IntPtr.Zero) + "; type=" + NativeType(__instance) +
                    "; ownerType=" + NativeType(__0) + "; layer=" + __2);
            }
            catch (Exception e) { AnimationError(e); }
        }

        private static void AfterAnimationStart(ActionView __instance)
        {
            if (__instance == null) return;
            try
            {
                if (!LocalShot(__instance.OwningEntityView) || __instance.QuantumAsset?.AssetGuid.Value != BowgunInputPolicy.ActionGuid) return;
                TrialRepairLog.Write("[bowgun-animation/r2] VIEW_STARTED bridgeContext=" + (__instance.m_contextControl != null) +
                    "; playback=" + (__instance.m_playback != null) + "; animator=" + (__instance.MainAnimator?.Pointer ?? IntPtr.Zero));
                // Capture progress even if TryStartViewQBridge was never called.
                if (_animationView == null && _animationRuns < 6)
                {
                    ++_animationRuns;
                    _animationView = __instance;
                    _animationStarted = Environment.TickCount64;
                    _animationStage = 0;
                    AnimationSnapshot("START_WITHOUT_BRIDGE_SAMPLE");
                }
            }
            catch (Exception e) { AnimationError(e); }
        }

        private static void ObserveAnimationProgress()
        {
            if (_animationView == null) return;
            try
            {
                long elapsed = Environment.TickCount64 - _animationStarted;
                int[] times = { 300, 1000, 1600, 2500 };
                if (_animationStage >= times.Length) { _animationView = null; return; }
                if (elapsed < times[_animationStage]) return;
                ++_animationStage;
                AnimationSnapshot("PROGRESS elapsedMs=" + elapsed);
            }
            catch (Exception e) { _animationView = null; AnimationError(e); }
        }

        private static void BeforeAnimationReturn(ActionView __instance)
        {
            if (_animationView == null || __instance == null || _animationView.Pointer != __instance.Pointer) return;
            try { AnimationSnapshot("RETURN"); }
            catch (Exception e) { AnimationError(e); }
            finally { _animationView = null; }
        }

        private static void AnimationSnapshot(string stage)
        {
            var view = _animationView;
            var hero = view.OwningEntityView?.TryCast<HeroView>();
            var control = view.m_contextControl;
            var clip = control?.m_clipAnim;
            TrialRepairLog.Write("[bowgun-animation] " + stage + "; run=" + _animationRuns +
                "; view=" + view.Pointer + "; layer=" + view.Layer + "; time=" + view.ActionViewTime +
                "; cachedAsset=" + (view.m_cachedActionDataAsset != null) +
                "; mainAnimator=" + (view.MainAnimator?.Pointer ?? IntPtr.Zero) +
                "; heroAnimator=" + (hero?.Animator?.Pointer ?? IntPtr.Zero) +
                "; playback=" + (view.m_playback != null) + "; context=" + (control != null) +
                "; qanim=" + (control?.m_qanim != null) + "; clip=" + (clip == null ? "<null>" : clip.name));
        }

        private static void AnimationError(Exception e)
        {
            if (++_animationErrors <= 3) TrialRepairLog.Write("[bowgun-animation] OBSERVATION_ERROR " + e.GetType().Name + ": " + e.Message);
        }

        private static void Error(string stage, Exception e)
        {
            if (Interlocked.Increment(ref _errors) <= 3) TrialRepairLog.Write("[bowgun-lb] " + stage + " ERROR " + e);
            if (_errors >= 3) _enabled = false;
        }
    }
}
