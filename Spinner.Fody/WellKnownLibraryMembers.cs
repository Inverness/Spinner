using System.Linq;
using Mono.Cecil;

namespace Spinner.Fody
{
    /// <summary>
    /// Cache well known aspect library definitions for use by weavers.
    /// </summary>
    internal class WellKnownLibraryMembers
    {
        internal const int MaxArguments = Spinner.Arguments.MaxItems;

        private const string Ns = "Spinner";
        private const string IntNs = "Spinner.Internal";
        
        // ReSharper disable InconsistentNaming
        internal readonly TypeDefinition ArgumentsBase;
        internal readonly TypeDefinition[] Arguments;
        internal readonly TypeDefinition MethodExecutionArgs;
        internal readonly MethodDefinition MethodExecutionArgs_ctor;
        internal readonly PropertyDefinition MethodExecutionArgs_Exception;
        internal readonly PropertyDefinition MethodExecutionArgs_FlowBehavior;
        internal readonly PropertyDefinition MethodExecutionArgs_ReturnValue;
        internal readonly PropertyDefinition MethodExecutionArgs_YieldValue;
        internal readonly TypeDefinition BoundMethodInterceptionArgs;
        internal readonly MethodDefinition BoundMethodInterceptionArgs_ctor;
        internal readonly TypeDefinition BoundMethodInterceptionArgsT1;
        internal readonly MethodDefinition BoundMethodInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundMethodInterceptionArgsT1_TypedReturnValue;
        internal readonly TypeDefinition MethodBinding;
        internal readonly TypeDefinition MethodBindingT1;
        internal readonly TypeDefinition PropertyBindingT1;
        internal readonly TypeDefinition BoundPropertyInterceptionArgsT1;
        internal readonly MethodDefinition BoundPropertyInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundPropertyInterceptionArgsT1_TypedValue;
        internal readonly TypeDefinition Features;
        internal readonly TypeDefinition FeaturesAttribute;


        internal readonly MethodDefinition[] Arguments_ctor;
        internal readonly FieldDefinition[][] Arguments_Item;
        // ReSharper restore InconsistentNaming

        internal WellKnownLibraryMembers(ModuleDefinition module)
        {
            ArgumentsBase = module.GetType(Ns, "Arguments");

            Arguments = new TypeDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                Arguments[i] = module.GetType(IntNs, "Arguments`" + i);

            MethodExecutionArgs = module.GetType(Ns, "MethodExecutionArgs");
            MethodExecutionArgs_ctor = MethodExecutionArgs.Methods.First(m => m.IsConstructor && !m.IsStatic);
            MethodExecutionArgs_Exception = MethodExecutionArgs.Properties.First(m => m.Name == "Exception");
            MethodExecutionArgs_FlowBehavior = MethodExecutionArgs.Properties.First(m => m.Name == "FlowBehavior");
            MethodExecutionArgs_ReturnValue = MethodExecutionArgs.Properties.First(m => m.Name == "ReturnValue");
            MethodExecutionArgs_YieldValue = MethodExecutionArgs.Properties.First(m => m.Name == "YieldValue");

            MethodBinding = module.GetType(IntNs, "MethodBinding");
            MethodBindingT1 = module.GetType(IntNs, "MethodBinding`1");
            PropertyBindingT1 = module.GetType(IntNs, "PropertyBinding`1");

            Features = module.GetType(Ns, "Features");
            FeaturesAttribute = module.GetType(Ns, "FeaturesAttribute");

            BoundMethodInterceptionArgs = module.GetType(IntNs, "BoundMethodInterceptionArgs");
            BoundMethodInterceptionArgs_ctor = BoundMethodInterceptionArgs.Methods.First(m => m.IsConstructor && !m.IsStatic);

            BoundMethodInterceptionArgsT1 = module.GetType(IntNs, "BoundMethodInterceptionArgs`1");
            BoundMethodInterceptionArgsT1_ctor = BoundMethodInterceptionArgsT1.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundMethodInterceptionArgsT1_TypedReturnValue = BoundMethodInterceptionArgsT1.Fields.First(f => f.Name == "TypedReturnValue");

            BoundPropertyInterceptionArgsT1 = module.GetType(IntNs, "BoundPropertyInterceptionArgs`1");
            BoundPropertyInterceptionArgsT1_ctor = BoundPropertyInterceptionArgsT1.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundPropertyInterceptionArgsT1_TypedValue = BoundPropertyInterceptionArgsT1.Fields.First(f => f.Name == "TypedValue");

            Arguments_ctor = new MethodDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                Arguments_ctor[i] = Arguments[i].Methods.First(m => m.IsConstructor && !m.IsStatic);

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