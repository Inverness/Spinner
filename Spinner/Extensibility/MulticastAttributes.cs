using System;

namespace Spinner.Extensibility
{
    [Flags]
    public enum MulticastAttributes : uint
    {
        Default = 0,
        Private = 1 << 0,
        Protected = 1 << 1,
        Internal = 1 << 2,
        InternalAndProtected = 1 << 3,
        InternalOrProtected = 1 << 4,
        Public = 1 << 5,
        AnyVisibility = Private | Protected | Internal | InternalAndProtected | InternalOrProtected | Public,
        Static = 1 << 6,
        Instance = 1 << 7,
        AnyScope = Instance | Static,
        Abstract = 1 << 8,
        NonAbstract = 1 << 9,
        AnyAbstraction = Abstract | NonAbstract,
        Virtual = 1 << 10,
        NonVirtual = 1 << 11,
        AnyVirtuality = Virtual | NonVirtual,
        Managed = 1 << 12,
        NonManaged = 1 << 13,
        AnyImplementation = Managed | NonManaged,
        Literal = 1 << 14,
        NonLiteral = 1 << 15,
        AnyLiterality = Literal | NonLiteral,
        InParameter = 1 << 16,
        OutParameter = 1 << 17,
        RefParameter = 1 << 18,
        AnyParameter = InParameter | OutParameter | RefParameter,
        CompilerGenerated = 1 << 19,
        UserGenerated = 1 << 20,
        AnyGeneration = CompilerGenerated | UserGenerated,
        All = AnyVisibility | AnyScope | AnyAbstraction | AnyVirtuality | AnyImplementation | AnyLiterality | AnyParameter | AnyGeneration
    }
}