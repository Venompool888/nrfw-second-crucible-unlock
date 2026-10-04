using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppQuantum;

namespace CrucibleUnlock
{
    internal static class StreamingPayloadInterop
    {
        private static IntPtr _mask, _get, _set;

        internal static unsafe void Initialize()
        {
            if (sizeof(AssetGuid) != 8 || sizeof(EntityRef) != 8 ||
                sizeof(StreamingPayloadValue) != 24 || sizeof(StreamingPayloadArgument) != 32)
                throw new InvalidOperationException("Streaming payload ABI size mismatch.");
            RuntimeHelpers.RunClassConstructor(typeof(BasePayload).TypeHandle);
            var klass = Il2CppClassPointerStore<BasePayload>.NativeClassPtr;
            if (klass == IntPtr.Zero || ValueSize(klass) != 24 ||
                !HasFieldOffset(klass, "ContentMask", 16) || !HasFieldOffset(klass, "AssetGuid", 32))
                throw new InvalidOperationException("Native BasePayload layout differs from verified build.");
            _mask = Method("NativeMethodInfoPtr_GetContentMask_Public_Static_Nullable_1_UInt64_Frame_AssetGuid_AssetGuid_0");
            _get = Method("NativeMethodInfoPtr_GetDesiredDirectorsState_Public_Static_Nullable_1_BasePayload_Frame_EntityRef_0");
            _set = Method("NativeMethodInfoPtr_SetDesiredDirectorsState_Public_Static_Void_Frame_EntityRef_Nullable_1_BasePayload_0");
        }

        private static bool HasFieldOffset(IntPtr klass, string name, uint offset)
        {
            var field = IL2CPP.il2cpp_class_get_field_from_name(klass, name);
            return field != IntPtr.Zero && IL2CPP.il2cpp_field_get_offset(field) == offset;
        }

        private static IntPtr Method(string name)
        {
            var field = typeof(StreamingAPI).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(IntPtr))
                throw new InvalidOperationException("Missing verified streaming method: " + name);
            var pointer = (IntPtr)field.GetValue(null);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Unresolved streaming method: " + name);
            return pointer;
        }

        // Runtime::Invoke boxes Nullable<T> as null or boxed T, NOT boxed Nullable<T>.
        // Verified native box path 0x180961220 and allocation path 0x180961550.
        // Never use generated Nullable<T>.HasValue/Value or BasePayload.ContentMask.
        internal static unsafe bool TryMask(Frame frame, AssetGuid bucket, AssetGuid nugget, out ulong mask)
        {
            mask = 0;
            void** args = stackalloc void*[3];
            args[0] = (void*)IL2CPP.Il2CppObjectBaseToPtrNotNull(frame);
            args[1] = &bucket;
            args[2] = &nugget;
            var result = Invoke(_mask, args);
            if (result == IntPtr.Zero) return false;
            var root = new Il2CppSystem.Object(result);
            var data = UnboxChecked(result, "System", "UInt64", 8);
            mask = *(ulong*)data;
            GC.KeepAlive(root);
            return true;
        }

        internal static unsafe bool TryDesired(Frame frame, EntityRef entity, out StreamingPayloadValue value)
        {
            value = default;
            void** args = stackalloc void*[2];
            args[0] = (void*)IL2CPP.Il2CppObjectBaseToPtrNotNull(frame);
            args[1] = &entity;
            var result = Invoke(_get, args);
            if (result == IntPtr.Zero) return false;
            var root = new Il2CppSystem.Object(result);
            var data = UnboxChecked(result, "Quantum", "BasePayload", 24);
            value = *(StreamingPayloadValue*)data;
            GC.KeepAlive(root);
            if (value.HasMask > 1) throw new InvalidOperationException("Invalid native payload presence flag.");
            return true;
        }

        internal static unsafe void SetDesired(Frame frame, EntityRef entity, StreamingPayloadArgument payload)
        {
            void** args = stackalloc void*[3];
            args[0] = (void*)IL2CPP.Il2CppObjectBaseToPtrNotNull(frame);
            args[1] = &entity;
            args[2] = &payload; // Native parameter is an unboxed 32-byte Nullable<BasePayload>.
            Invoke(_set, args);
        }

        private static unsafe IntPtr Invoke(IntPtr method, void** args)
        {
            if (method == IntPtr.Zero) throw new InvalidOperationException("Streaming payload bridge was not initialized.");
            IntPtr exception = IntPtr.Zero;
            var result = IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, args, ref exception);
            Il2CppException.RaiseExceptionIfNecessary(exception);
            return result;
        }

        private static IntPtr UnboxChecked(IntPtr obj, string expectedNamespace, string expectedName, int size)
        {
            var klass = IL2CPP.il2cpp_object_get_class(obj);
            if (klass == IntPtr.Zero || !IL2CPP.il2cpp_class_is_valuetype(klass) ||
                Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_namespace(klass)) != expectedNamespace ||
                Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass)) != expectedName ||
                ValueSize(klass) != size)
                throw new InvalidOperationException("Unexpected IL2CPP boxed return type for " + expectedName);
            var data = IL2CPP.il2cpp_object_unbox(obj);
            if (data == IntPtr.Zero) throw new InvalidOperationException("Null boxed payload data.");
            return data;
        }

        private static int ValueSize(IntPtr klass)
        {
            uint alignment = 0;
            return IL2CPP.il2cpp_class_value_size(klass, ref alignment);
        }
    }
}
