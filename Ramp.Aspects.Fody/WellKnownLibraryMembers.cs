using System.Linq;
using Mono.Cecil;

namespace Ramp.Aspects.Fody
{
    internal class WellKnownLibraryMembers
    {
        private const string Namespace = "Ramp.Aspects";
        private const string InternalNamespace = "Ramp.Aspects.Internal";

        internal readonly TypeDefinition ArgumentContainerBase;
        internal readonly TypeDefinition[] ArgumentContainer;
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

        // ReSharper restore InconsistentNaming

        internal WellKnownLibraryMembers(ModuleDefinition module)
        {
            ArgumentContainerBase = module.GetType(Namespace, "Arguments");

            ArgumentContainer = new TypeDefinition[Arguments.MaxItems + 1];
            for (int i = 1; i <= Arguments.MaxItems; i++)
                ArgumentContainer[i] = module.GetType(InternalNamespace, "Arguments`" + i);

            BoundMethodInterceptionArgs = module.GetType(InternalNamespace, "BoundMethodInterceptionArgs");
            BoundMethodInterceptionArgsT1 = module.GetType(InternalNamespace, "BoundMethodInterceptionArgs`1");
            MethodBinding = module.GetType(InternalNamespace, "MethodBinding");
            MethodBindingT1 = module.GetType(InternalNamespace, "MethodBinding`1");
            PropertyBindingT1 = module.GetType(InternalNamespace, "PropertyBinding`1");
            BoundPropertyInterceptionArgsT1 = module.GetType(InternalNamespace, "BoundPropertyInterceptionArgs`1");

            BoundMethodInterceptionArgs_ctor = BoundMethodInterceptionArgs.Methods.Single(m => m.IsConstructor && !m.IsStatic);

            BoundMethodInterceptionArgsT1_ctor = BoundMethodInterceptionArgsT1.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            BoundMethodInterceptionArgsT1_TypedReturnValue = BoundMethodInterceptionArgsT1.Fields.Single(f => f.Name == "TypedReturnValue");

            BoundPropertyInterceptionArgsT1_ctor = BoundPropertyInterceptionArgsT1.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            BoundPropertyInterceptionArgsT1_TypedValue = BoundPropertyInterceptionArgsT1.Fields.Single(f => f.Name == "TypedValue");
        }
    }
}