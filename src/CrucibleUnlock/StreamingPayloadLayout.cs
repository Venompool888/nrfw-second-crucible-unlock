using System;
using System.Runtime.InteropServices;

namespace CrucibleUnlock
{
    // Build 22928553: native BasePayload fields are Nullable<ulong> at 0,
    // AssetGuid at 16. Nullable<BasePayload> has its value at 8, total 32 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct StreamingPayloadValue
    {
        [FieldOffset(0)] public byte HasMask;
        [FieldOffset(8)] public ulong Mask;
        [FieldOffset(16)] public long Nugget;

        internal bool Matches(long nugget, ulong mask) => HasMask == 1 && Nugget == nugget && Mask == mask;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct StreamingPayloadArgument
    {
        [FieldOffset(0)] public byte HasValue;
        [FieldOffset(8)] public StreamingPayloadValue Value;

        internal static StreamingPayloadArgument Present(StreamingPayloadValue value) =>
            new StreamingPayloadArgument { HasValue = 1, Value = value };

        internal static StreamingPayloadArgument Selected(long nugget, ulong mask)
        {
            if (nugget == 0 || mask == 0) throw new ArgumentException("A boss payload requires a resolved nugget and nonempty mask.");
            return Present(new StreamingPayloadValue { HasMask = 1, Mask = mask, Nugget = nugget });
        }
    }
}
