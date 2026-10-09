using System.Reflection;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;

namespace BowgunRepair;

// Patch inspected CLR callbacks in our existing 0.9.16 module. Keep all native
// hooks and the tested gamepad path intact; fill only the missing keyboard path.
internal static class BlockInputRouting
{
    private static FieldInfo _coreEnabled, _hero, _captureUntil, _modifiers, _hudScope;
    private static Func<int,int> _shotModifiers;
    private static Action<string> _log;
    private static HarmonyLib.Harmony _patches;
    private static bool _enabled;
    private static int _errors, _routes;
    private static bool _held;

    internal static bool Enabled => _enabled;
    internal static unsafe void Install(Type core, Action<string> log)
    {
        if (sizeof(Input) != 0x110 || sizeof(Button) != 12 || sizeof(EntityRef) != 8 ||
            System.Runtime.InteropServices.Marshal.OffsetOf<Input>(nameof(Input.Attack)).ToInt32() != 0x28 ||
            System.Runtime.InteropServices.Marshal.OffsetOf<Input>(nameof(Input.Offhand)).ToInt32() != 0x94 ||
            System.Runtime.InteropServices.Marshal.OffsetOf<Input>(nameof(Input.Modifiers)).ToInt32() != 0x0c)
            throw new InvalidOperationException("Inspected keyboard input ABI differs");
        _coreEnabled=Field(core,"_enabled",typeof(bool));
        _hero=Field(core,"_boundHeroRaw",typeof(long));
        _captureUntil=Field(core,"_captureUntil",typeof(long));
        _modifiers=Field(core,"_lastRoutedModifiers",typeof(int));
        _hudScope=Field(core,"_suppressHudBlock",typeof(bool));
        if (!_hudScope.IsDefined(typeof(ThreadStaticAttribute),false))
            throw new InvalidOperationException("Expected thread-scoped native HUD read guard");
        var policy=core.Assembly.GetType("CrucibleUnlock.BowgunInputPolicy",true);
        _shotModifiers=Method(policy,"ShotModifiers",typeof(int),typeof(int)).CreateDelegate<Func<int,int>>();
        var input=Method(core,"AfterInput",typeof(void),typeof(QuantumLocalInputSource),typeof(Frame));
        var hud=Method(core,"AfterHudButton",typeof(void),typeof(RevisedInput),typeof(int),typeof(bool).MakeByRefType());
        _log=log;
        _patches=new HarmonyLib.Harmony("NRFW.BowgunRepair.KeyboardBlock");
        try
        {
            _patches.Patch(input,prefix:new HarmonyMethod(typeof(BlockInputRouting),nameof(BeforeInput)) { priority=Priority.First });
            _patches.Patch(hud,prefix:new HarmonyMethod(typeof(BlockInputRouting),nameof(BeforeHudButton)) { priority=Priority.First });
            _enabled=true;
        }
        catch { _enabled=false; _patches.UnpatchSelf(); throw; }
        _log("INPUT_EXTENSION_INSTALLED version=0.3.3; two CLR callbacks; keyboard native Default_Block; core main-thread hero snapshot; existing gamepad route preserved");
    }

    private static FieldInfo Field(Type type,string name,Type expected)
    {
        var field=type.GetField(name,BindingFlags.Static|BindingFlags.NonPublic);
        if (field==null || field.FieldType!=expected || field.IsInitOnly)
            throw new MissingFieldException(type.FullName,name+" exact writable CLR field");
        return field;
    }
    private static MethodInfo Method(Type type,string name,Type result,params Type[] arguments)
    {
        var method=type.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic,null,arguments,null);
        if (method==null || method.ReturnType!=result || method.GetMethodBody()==null)
            throw new MissingMethodException(type.FullName,name+" inspected CLR signature");
        return method;
    }
    private static bool CoreEnabled => (bool)_coreEnabled.GetValue(null);

    private static bool BeforeInput(QuantumLocalInputSource __0,Frame __1)
    {
        if (!_enabled || __0==null || __1==null) return true;
        try
        {
            if (!CoreEnabled) return true;
            var revised=__0.m_revisedInput;
            if (revised==null || revised.CurrentInputStyleDetected!=CurrentInputStyle.KeyboardAndMouse)
            { _held=false; return true; }
            bool block=revised.GetButton(RevisedInputActions.Default_Block,true);
            if (!block) { _held=false; return false; }
            var input=__0.m_input;
            bool gameplay=input.GameplayInputSent && !revised.CinematicLock &&
                !__0.m_cancellingMenu && __0.m_menuBuffer<=0;
            if (!gameplay) { _held=false; return false; }
            var hero=new EntityRef { Raw=unchecked((ulong)(long)_hero.GetValue(null)) };
            // UpdateLocalHero publishes this value on the main thread and
            // clears it when unbound. This callback can run on Quantum's input
            // thread: never enter ViewFrame/PlayerViewsAPI here. Equipment and
            // armament must come from this callback's simulation frame.
            bool local=hero.IsValid;
            var item=local ? EquipmentAPI.GetEquippedOffhand(__1,hero) : default;
            var armament=item.IsValid ? ArmamentAPI.TryGetRuntimeArmament(__1,item) : null;
            if (!BlockInputPolicy.ShouldRoute(armament?.Id.Value ?? 0,gameplay,local && item.IsValid,block,ControlStyle.KeyboardMouse))
            { _held=false; return false; }
            // Match the baseline's held bool -> native Button representation.
            // No synthetic key polling or extra key down/up events.
            input.Offhand=true;
            input.Attack=true;
            input.Modifiers=(InputModifier)_shotModifiers((int)input.Modifiers);
            __0.m_input=input;
            _modifiers.SetValue(null,(int)input.Modifiers);
            // The unchanged native special-combo callback reads this core CLR
            // window. Publish modifiers first, then the active routing window.
            System.Threading.Thread.MemoryBarrier();
            _captureUntil.SetValue(null,Environment.TickCount64+2500);
            if (!_held && ++_routes<=24)
                _log("INPUT_ROUTED style=KeyboardAndMouse; action=Default_Block; hero="+hero.Raw+"; item="+item.Raw+"; modifiers="+input.Modifiers);
            _held=true;
            return false; // The old callback only handles gamepad, already skipped here.
        }
        catch (Exception e) { Error("keyboard routing",e); return true; }
    }

    private static bool BeforeHudButton(RevisedInput __0,int __1,ref bool __2)
    {
        if (!_enabled || __0==null) return true;
        try
        {
            if (!CoreEnabled || __0.CurrentInputStyleDetected!=CurrentInputStyle.KeyboardAndMouse) return true;
            bool scope=(bool)_hudScope.GetValue(null);
            if (!BlockInputPolicy.ShouldHideHudBlock(scope,__2,__1==RevisedInputActions.Default_Block,ControlStyle.KeyboardMouse)) return true;
            __2=false;
            return false;
        }
        catch (Exception e) { Error("keyboard HUD scope",e); return true; }
    }
    private static void Error(string stage,Exception error)
    {
        if (++_errors<=3) _log("INPUT_EXTENSION_ERROR stage="+stage+"; "+error);
        if (_errors>=3 && _enabled)
        { _enabled=false; _held=false; _log("INPUT_EXTENSION_DISABLED; original controller input and audio callbacks remain active"); }
    }
    internal static void Shutdown() { _enabled=false; _held=false; _patches?.UnpatchSelf(); }
}
