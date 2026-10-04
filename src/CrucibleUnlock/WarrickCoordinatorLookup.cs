using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppQuantum;

namespace CrucibleUnlock
{
    internal static class WarrickCoordinatorLookup
    {
        private static IntPtr _method;

        internal static unsafe void Initialize()
        {
            // Generated FindCoordinatorWithAgent corruptly emits newobj on T* and stind.ref.
            // Reuse its native MethodInfo, but marshal the out pointer as a pointer.
            const string fieldName = "NativeMethodInfoPtr_FindCoordinatorWithAgent_Public_Static_Boolean_Frame_EntityRef_byref_ptr_AiCoordinatorComponent_0";
            var field = typeof(AiCoordinatorSystem).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || field.FieldType != typeof(IntPtr) || sizeof(EntityRef) != 8 ||
                Marshal.OffsetOf<AiCoordinatorComponent>(nameof(AiCoordinatorComponent.StaticData)).ToInt64() != 24)
                throw new InvalidOperationException("Coordinator lookup signature/layout differs from verified build.");
            _method = (IntPtr)field.GetValue(null);
            if (_method == IntPtr.Zero) throw new InvalidOperationException("Coordinator lookup native method unavailable.");
        }

        internal static unsafe bool TryGetGuid(Frame frame, EntityRef entity, out long guid)
        {
            guid = 0;
            if (_method == IntPtr.Zero || frame == null || entity.Raw == 0) return false;
            AiCoordinatorComponent* coordinator = null;
            void** args = stackalloc void*[3];
            args[0] = (void*)IL2CPP.Il2CppObjectBaseToPtrNotNull(frame);
            args[1] = &entity;
            args[2] = &coordinator;
            IntPtr exception = IntPtr.Zero;
            var result = IL2CPP.il2cpp_runtime_invoke(_method, IntPtr.Zero, args, ref exception);
            Il2CppException.RaiseExceptionIfNecessary(exception);
            if (result == IntPtr.Zero || coordinator == null) return false;
            var value = IL2CPP.il2cpp_object_unbox(result);
            if (value == IntPtr.Zero || !*(bool*)value) return false;
            guid = coordinator->StaticData.Id.Value;
            return true;
        }
    }
}
