using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Spinner.Fody
{
    /// <summary>
    /// Cache well known aspect library definitions for use by weavers. These are not imported by default.
    /// </summary>
    internal class WellKnownSpinnerMembers
    {
        internal const int MaxArguments = Spinner.Arguments.MaxItems;

        private const string Ns = "Spinner";
        private const string IntNs = "Spinner.Internal";
        
        // ReSharper disable InconsistentNaming
        internal readonly ModuleDefinition Module;
        internal readonly TypeDefinition IAspect;
        internal readonly TypeDefinition IMethodBoundaryAspect;
        internal readonly MethodDefinition IMethodBoundaryAspect_OnEntry;
        internal readonly MethodDefinition IMethodBoundaryAspect_OnExit;
        internal readonly MethodDefinition IMethodBoundaryAspect_OnSuccess;
        internal readonly MethodDefinition IMethodBoundaryAspect_OnException;
        internal readonly MethodDefinition IMethodBoundaryAspect_OnYield;
        internal readonly MethodDefinition IMethodBoundaryAspect_OnResume;
        internal readonly TypeDefinition IMethodInterceptionAspect;
        internal readonly MethodDefinition IMethodInterceptionAspect_OnInvoke;
        internal readonly TypeDefinition IPropertyInterceptionAspect;
        internal readonly MethodDefinition IPropertyInterceptionAspect_OnGetValue;
        internal readonly MethodDefinition IPropertyInterceptionAspect_OnSetValue;
        internal readonly TypeDefinition MethodBoundaryAspect;
        internal readonly TypeDefinition MethodInterceptionAspect;
        internal readonly TypeDefinition PropertyInterceptionAspect;
        internal readonly TypeDefinition AdviceArgs;
        internal readonly PropertyDefinition AdviceArgs_Tag;
        internal readonly PropertyDefinition AdviceArgs_Instance;
        internal readonly TypeDefinition MethodArgs;
        internal readonly PropertyDefinition MethodArgs_Method;
        internal readonly PropertyDefinition MethodArgs_Arguments;
        internal readonly TypeDefinition MethodExecutionArgs;
        internal readonly MethodDefinition MethodExecutionArgs_ctor;
        internal readonly PropertyDefinition MethodExecutionArgs_Exception;
        internal readonly PropertyDefinition MethodExecutionArgs_FlowBehavior;
        internal readonly PropertyDefinition MethodExecutionArgs_ReturnValue;
        internal readonly PropertyDefinition MethodExecutionArgs_YieldValue;
        internal readonly TypeDefinition MethodInterceptionArgs;
        internal readonly TypeDefinition BoundMethodInterceptionArgs;
        internal readonly MethodDefinition BoundMethodInterceptionArgs_ctor;
        internal readonly TypeDefinition BoundMethodInterceptionArgsT1;
        internal readonly MethodDefinition BoundMethodInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundMethodInterceptionArgsT1_TypedReturnValue;
        internal readonly TypeDefinition MethodBinding;
        internal readonly TypeDefinition MethodBindingT1;
        internal readonly TypeDefinition PropertyBindingT1;
        internal readonly TypeDefinition PropertyInterceptionArgs;
        internal readonly PropertyDefinition PropertyInterceptionArgs_Property;
        internal readonly PropertyDefinition PropertyInterceptionArgs_Index;
        internal readonly TypeDefinition BoundPropertyInterceptionArgsT1;
        internal readonly MethodDefinition BoundPropertyInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundPropertyInterceptionArgsT1_TypedValue;
        internal readonly TypeDefinition Features;
        internal readonly TypeDefinition FeaturesAttribute;
        internal readonly TypeDefinition AnalyzedFeaturesAttribute;
        internal readonly MethodDefinition AnalyzedFeaturesAttribute_ctor;
        internal readonly TypeDefinition Arguments;
        internal readonly MethodDefinition Arguments_set_Item;
        internal readonly MethodDefinition Arguments_SetValue;
        internal readonly MethodDefinition Arguments_SetValueT;
        internal readonly TypeDefinition[] ArgumentsT;
        internal readonly MethodDefinition[] ArgumentsT_ctor;
        internal readonly FieldDefinition[][] ArgumentsT_Item;
        // ReSharper restore InconsistentNaming

        private readonly HashSet<TypeDefinition> _emptyAspectBaseTypes; 

        internal WellKnownSpinnerMembers(ModuleDefinition module)
        {
            Module = module;

            IAspect = module.GetType(Ns, "IAspect");

            IMethodBoundaryAspect = module.GetType(Ns, "IMethodBoundaryAspect");
            IMethodBoundaryAspect_OnEntry = IMethodBoundaryAspect.Methods.First(m => m.Name == "OnEntry");
            IMethodBoundaryAspect_OnExit = IMethodBoundaryAspect.Methods.First(m => m.Name == "OnExit");
            IMethodBoundaryAspect_OnSuccess = IMethodBoundaryAspect.Methods.First(m => m.Name == "OnSuccess");
            IMethodBoundaryAspect_OnException = IMethodBoundaryAspect.Methods.First(m => m.Name == "OnException");
            IMethodBoundaryAspect_OnYield = IMethodBoundaryAspect.Methods.First(m => m.Name == "OnYield");
            IMethodBoundaryAspect_OnResume = IMethodBoundaryAspect.Methods.First(m => m.Name == "OnResume");

            IMethodInterceptionAspect = module.GetType(Ns, "IMethodInterceptionAspect");
            IMethodInterceptionAspect_OnInvoke = IMethodInterceptionAspect.Methods.First(m => m.Name == "OnInvoke");

            IPropertyInterceptionAspect = module.GetType(Ns, "IPropertyInterceptionAspect");
            IPropertyInterceptionAspect_OnGetValue = IPropertyInterceptionAspect.Methods.First(m => m.Name == "OnGetValue");
            IPropertyInterceptionAspect_OnSetValue = IPropertyInterceptionAspect.Methods.First(m => m.Name == "OnSetValue");

            MethodBoundaryAspect = module.GetType(Ns, "MethodBoundaryAspect");
            MethodInterceptionAspect = module.GetType(Ns, "MethodInterceptionAspect");
            PropertyInterceptionAspect = module.GetType(Ns, "PropertyInterceptionAspect");

            _emptyAspectBaseTypes = new HashSet<TypeDefinition>
            {
                MethodBoundaryAspect,
                MethodInterceptionAspect,
                PropertyInterceptionAspect
            };

            AdviceArgs = module.GetType(Ns, "AdviceArgs");
            AdviceArgs_Instance = AdviceArgs.Properties.First(p => p.Name == "Instance");
            AdviceArgs_Tag = AdviceArgs.Properties.First(p => p.Name == "Tag");

            MethodArgs = module.GetType(Ns, "MethodArgs");
            MethodArgs_Method = MethodArgs.Properties.First(p => p.Name == "Method");
            MethodArgs_Arguments = MethodArgs.Properties.First(p => p.Name == "Arguments");

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
            AnalyzedFeaturesAttribute = module.GetType(IntNs, "AnalyzedFeaturesAttribute");
            AnalyzedFeaturesAttribute_ctor = AnalyzedFeaturesAttribute.Methods.First(m => m.IsConstructor && !m.IsStatic);

            PropertyInterceptionArgs = module.GetType(Ns, "PropertyInterceptionArgs");
            PropertyInterceptionArgs_Property = PropertyInterceptionArgs.Properties.First(p => p.Name == "Property");
            PropertyInterceptionArgs_Index = PropertyInterceptionArgs.Properties.First(p => p.Name == "Index");

            MethodInterceptionArgs = module.GetType(Ns, "MethodInterceptionArgs");

            BoundMethodInterceptionArgs = module.GetType(IntNs, "BoundMethodInterceptionArgs");
            BoundMethodInterceptionArgs_ctor = BoundMethodInterceptionArgs.Methods.First(m => m.IsConstructor && !m.IsStatic);

            BoundMethodInterceptionArgsT1 = module.GetType(IntNs, "BoundMethodInterceptionArgs`1");
            BoundMethodInterceptionArgsT1_ctor = BoundMethodInterceptionArgsT1.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundMethodInterceptionArgsT1_TypedReturnValue = BoundMethodInterceptionArgsT1.Fields.First(f => f.Name == "TypedReturnValue");

            BoundPropertyInterceptionArgsT1 = module.GetType(IntNs, "BoundPropertyInterceptionArgs`1");
            BoundPropertyInterceptionArgsT1_ctor = BoundPropertyInterceptionArgsT1.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundPropertyInterceptionArgsT1_TypedValue = BoundPropertyInterceptionArgsT1.Fields.First(f => f.Name == "TypedValue");

            Arguments = module.GetType(Ns, "Arguments");
            Arguments_set_Item = Arguments.Methods.First(m => m.Name == "set_Item");
            Arguments_SetValue = Arguments.Methods.First(m => m.Name == "SetValue" && !m.HasGenericParameters);
            Arguments_SetValueT = Arguments.Methods.First(m => m.Name == "SetValue" && m.HasGenericParameters);

            ArgumentsT = new TypeDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                ArgumentsT[i] = module.GetType(IntNs, "Arguments`" + i);

            ArgumentsT_ctor = new MethodDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                ArgumentsT_ctor[i] = ArgumentsT[i].Methods.First(m => m.IsConstructor && !m.IsStatic);

            ArgumentsT_Item = new FieldDefinition[MaxArguments + 1][];
            for (int i = 1; i <= MaxArguments; i++)
            {
                TypeDefinition type = ArgumentsT[i];
                var fields = new FieldDefinition[i];
                for (int f = 0; f < i; f++)
                {
                    string fieldName = "Item" + f;
                    fields[f] = type.Fields.First(fe => fe.Name == fieldName);
                }
                ArgumentsT_Item[i] = fields;
            }
        }

        /// <summary>
        /// Check if a type is one of the abstract aspect base classes with empty virtual methods.
        /// </summary>
        internal bool IsEmptyAdviceBase(TypeDefinition type)
        {
            return _emptyAspectBaseTypes.Contains(type);
        }
    }
}