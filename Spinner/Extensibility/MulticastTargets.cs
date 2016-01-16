using System;

namespace Spinner.Extensibility
{
    [Flags]
    public enum MulticastTargets : uint
    {
        Default = 0,
        Class = 1 << 0,
        Struct = 1 << 1,
        Enum = 1 << 2,
        Delegate = 1 << 3,
        Interface = 1 << 4,
        AnyType = Class | Struct | Enum | Delegate | Interface,
        Method = 1 << 5,
        InstanceConstructor = 1 << 6,
        StaticConstructor = 1 << 7,
        Field = 1 << 8,
        Property = 1 << 9,
        Event = 1 << 10,
        AnyMember = Field | Method | InstanceConstructor | StaticConstructor | Property | Event,
        Assembly = 1 << 11,
        Parameter = 1 << 12,
        ReturnValue = 1 << 13,
        All = AnyType | AnyMember | Assembly | Parameter | ReturnValue
    }
}