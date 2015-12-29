using System.Linq;
using Mono.Cecil;

namespace Ramp.Aspects.Fody
{
    /// <summary>
    /// Cache well known aspect library definitions for use by weavers.
    /// </summary>
    internal class WellKnownLibraryMembers
    {
        internal const int MaxArguments = Aspects.Arguments.MaxItems;

        private const string Namespace = "Ramp.Aspects";
        private const string InternalNamespace = "Ramp.Aspects.Internal";

        internal readonly TypeDefinition ArgumentsBase;
        internal readonly TypeDefinition[] Arguments;
        internal readonly TypeDefinition BoundMethodInterceptionArgs;
        internal readonly TypeDefinition BoundMethodInterceptionArgsT1;
        internal readonly TypeDefinition MethodBinding;
        internal readonly TypeDefinition MethodBindingT1;
        internal readonly TypeDefinition PropertyBindingT1;
        internal readonly TypeDefinition BoundPropertyInterceptionArgsT1;

        // ReSharper disable InconsistentNaming
        internal readonly MethodDefinition BoundMethodInterceptionArgs_ctor;

        internal readonly MethodDefinition BoundMethodInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundMethodInterceptionArgsT1_TypedReturnValue;

        internal readonly MethodDefinition BoundPropertyInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundPropertyInterceptionArgsT1_TypedValue;

        internal readonly MethodDefinition[] Arguments_ctor;
        internal readonly FieldDefinition[][] Arguments_Item;
        // ReSharper restore InconsistentNaming

        internal WellKnownLibraryMembers(ModuleDefinition libraryModule)
        {
            ArgumentsBase = libraryModule.GetType(Namespace, "Arguments");

            Arguments = new TypeDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                Arguments[i] = libraryModule.GetType(InternalNamespace, "Arguments`" + i);

            BoundMethodInterceptionArgs = libraryModule.GetType(InternalNamespace, "BoundMethodInterceptionArgs");
            BoundMethodInterceptionArgsT1 = libraryModule.GetType(InternalNamespace, "BoundMethodInterceptionArgs`1");
            MethodBinding = libraryModule.GetType(InternalNamespace, "MethodBinding");
            MethodBindingT1 = libraryModule.GetType(InternalNamespace, "MethodBinding`1");
            PropertyBindingT1 = libraryModule.GetType(InternalNamespace, "PropertyBinding`1");
            BoundPropertyInterceptionArgsT1 = libraryModule.GetType(InternalNamespace, "BoundPropertyInterceptionArgs`1");

            BoundMethodInterceptionArgs_ctor = BoundMethodInterceptionArgs.Methods.Single(m => m.IsConstructor && !m.IsStatic);

            BoundMethodInterceptionArgsT1_ctor = BoundMethodInterceptionArgsT1.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            BoundMethodInterceptionArgsT1_TypedReturnValue = BoundMethodInterceptionArgsT1.Fields.Single(f => f.Name == "TypedReturnValue");

            BoundPropertyInterceptionArgsT1_ctor = BoundPropertyInterceptionArgsT1.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            BoundPropertyInterceptionArgsT1_TypedValue = BoundPropertyInterceptionArgsT1.Fields.Single(f => f.Name == "TypedValue");

            Arguments_ctor = new MethodDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                Arguments_ctor[i] = Arguments[i].Methods.Single(m => m.IsConstructor && !m.IsStatic);

            Arguments_Item = new FieldDefinition[MaxArguments + 1][];
            for (int i = 1; i <= MaxArguments; i++)
            {
                TypeDefinition type = Arguments[i];
                var fields = new FieldDefinition[i];
                for (int f = 0; f < i; f++)
                {
                    string fieldName = "Item" + f;
                    fields[f] = type.Fields.First(fe => fe.Name == fieldName);
                }
                Arguments_Item[i] = fields;
            }
        }
    }
}