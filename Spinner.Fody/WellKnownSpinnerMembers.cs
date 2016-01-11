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
        internal readonly MethodDefinition IMethodBoundaryAspect_FilterException;

        internal readonly TypeDefinition IMethodInterceptionAspect;
        internal readonly MethodDefinition IMethodInterceptionAspect_OnInvoke;

        internal readonly TypeDefinition IPropertyInterceptionAspect;
        internal readonly MethodDefinition IPropertyInterceptionAspect_OnGetValue;
        internal readonly MethodDefinition IPropertyInterceptionAspect_OnSetValue;

        internal readonly TypeDefinition IEventInterceptionAspect;
        internal readonly MethodDefinition IEventInterceptionAspect_OnAddHandler;
        internal readonly MethodDefinition IEventInterceptionAspect_OnRemoveHandler;
        internal readonly MethodDefinition IEventInterceptionAspect_OnInvokeHandler;

        internal readonly TypeDefinition MethodBoundaryAspect;

        internal readonly TypeDefinition MethodInterceptionAspect;

        internal readonly TypeDefinition PropertyInterceptionAspect;

        internal readonly TypeDefinition EventInterceptionAspect;

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

        internal readonly TypeDefinition EventBinding;

        internal readonly TypeDefinition PropertyInterceptionArgs;
        internal readonly PropertyDefinition PropertyInterceptionArgs_Property;
        internal readonly PropertyDefinition PropertyInterceptionArgs_Index;

        internal readonly TypeDefinition BoundPropertyInterceptionArgsT1;
        internal readonly MethodDefinition BoundPropertyInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundPropertyInterceptionArgsT1_TypedValue;

        internal readonly TypeDefinition EventInterceptionArgs;
        internal readonly PropertyDefinition EventInterceptionArgs_Arguments;
        internal readonly PropertyDefinition EventInterceptionArgs_Handler;
        internal readonly PropertyDefinition EventInterceptionArgs_Event;
        internal readonly PropertyDefinition EventInterceptionArgs_ReturnValue;

        internal readonly TypeDefinition BoundEventInterceptionArgs;
        internal readonly MethodDefinition BoundEventInterceptionArgs_ctor;

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

        internal readonly MethodDefinition WeaverHelpers_InvokeEvent;
        internal readonly MethodDefinition WeaverHelpers_GetEventInfo;
        internal readonly MethodDefinition WeaverHelpers_GetPropertyInfo;

        // ReSharper restore InconsistentNaming

        private readonly HashSet<TypeDefinition> _emptyAspectBaseTypes; 

        internal WellKnownSpinnerMembers(ModuleDefinition module)
        {
            Module = module;

            TypeDefinition type;

            IAspect = module.GetType(Ns, "IAspect");

            IMethodBoundaryAspect = type = module.GetType(Ns, "IMethodBoundaryAspect");
            IMethodBoundaryAspect_OnEntry = type.Methods.First(m => m.Name == "OnEntry");
            IMethodBoundaryAspect_OnExit = type.Methods.First(m => m.Name == "OnExit");
            IMethodBoundaryAspect_OnSuccess = type.Methods.First(m => m.Name == "OnSuccess");
            IMethodBoundaryAspect_OnException = type.Methods.First(m => m.Name == "OnException");
            IMethodBoundaryAspect_OnYield = type.Methods.First(m => m.Name == "OnYield");
            IMethodBoundaryAspect_OnResume = type.Methods.First(m => m.Name == "OnResume");
            IMethodBoundaryAspect_FilterException = type.Methods.First(m => m.Name == "FilterException");

            IMethodInterceptionAspect = type = module.GetType(Ns, "IMethodInterceptionAspect");
            IMethodInterceptionAspect_OnInvoke = type.Methods.First(m => m.Name == "OnInvoke");

            IPropertyInterceptionAspect = type = module.GetType(Ns, "IPropertyInterceptionAspect");
            IPropertyInterceptionAspect_OnGetValue = type.Methods.First(m => m.Name == "OnGetValue");
            IPropertyInterceptionAspect_OnSetValue = type.Methods.First(m => m.Name == "OnSetValue");

            IEventInterceptionAspect = type = module.GetType(Ns, "IEventInterceptionAspect");
            IEventInterceptionAspect_OnAddHandler = type.Methods.First(m => m.Name == "OnAddHandler");
            IEventInterceptionAspect_OnRemoveHandler = type.Methods.First(m => m.Name == "OnRemoveHandler");
            IEventInterceptionAspect_OnInvokeHandler = type.Methods.First(m => m.Name == "OnInvokeHandler");

            MethodBoundaryAspect = module.GetType(Ns, "MethodBoundaryAspect");
            MethodInterceptionAspect = module.GetType(Ns, "MethodInterceptionAspect");
            PropertyInterceptionAspect = module.GetType(Ns, "PropertyInterceptionAspect");
            EventInterceptionAspect = module.GetType(Ns, "EventInterceptionAspect");

            _emptyAspectBaseTypes = new HashSet<TypeDefinition>
            {
                MethodBoundaryAspect,
                MethodInterceptionAspect,
                PropertyInterceptionAspect,
                EventInterceptionAspect
            };

            AdviceArgs = type = module.GetType(Ns, "AdviceArgs");
            AdviceArgs_Instance = type.Properties.First(p => p.Name == "Instance");
            AdviceArgs_Tag = type.Properties.First(p => p.Name == "Tag");

            MethodArgs = type = module.GetType(Ns, "MethodArgs");
            MethodArgs_Method = type.Properties.First(p => p.Name == "Method");
            MethodArgs_Arguments = type.Properties.First(p => p.Name == "Arguments");

            MethodExecutionArgs = type = module.GetType(Ns, "MethodExecutionArgs");
            MethodExecutionArgs_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);
            MethodExecutionArgs_Exception = type.Properties.First(m => m.Name == "Exception");
            MethodExecutionArgs_FlowBehavior = type.Properties.First(m => m.Name == "FlowBehavior");
            MethodExecutionArgs_ReturnValue = type.Properties.First(m => m.Name == "ReturnValue");
            MethodExecutionArgs_YieldValue = type.Properties.First(m => m.Name == "YieldValue");

            MethodBinding = module.GetType(IntNs, "MethodBinding");
            MethodBindingT1 = module.GetType(IntNs, "MethodBinding`1");
            PropertyBindingT1 = module.GetType(IntNs, "PropertyBinding`1");
            EventBinding = module.GetType(IntNs, "EventBinding");

            Features = module.GetType(Ns, "Features");
            FeaturesAttribute = module.GetType(Ns, "FeaturesAttribute");
            AnalyzedFeaturesAttribute = type = module.GetType(IntNs, "AnalyzedFeaturesAttribute");
            AnalyzedFeaturesAttribute_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);

            PropertyInterceptionArgs = type = module.GetType(Ns, "PropertyInterceptionArgs");
            PropertyInterceptionArgs_Property = type.Properties.First(p => p.Name == "Property");
            PropertyInterceptionArgs_Index = type.Properties.First(p => p.Name == "Index");

            MethodInterceptionArgs = module.GetType(Ns, "MethodInterceptionArgs");

            BoundMethodInterceptionArgs = type = module.GetType(IntNs, "BoundMethodInterceptionArgs");
            BoundMethodInterceptionArgs_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);

            BoundMethodInterceptionArgsT1 = type = module.GetType(IntNs, "BoundMethodInterceptionArgs`1");
            BoundMethodInterceptionArgsT1_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundMethodInterceptionArgsT1_TypedReturnValue = type.Fields.First(f => f.Name == "TypedReturnValue");

            BoundPropertyInterceptionArgsT1 = type = module.GetType(IntNs, "BoundPropertyInterceptionArgs`1");
            BoundPropertyInterceptionArgsT1_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundPropertyInterceptionArgsT1_TypedValue = type.Fields.First(f => f.Name == "TypedValue");

            EventInterceptionArgs = type = module.GetType(Ns, "EventInterceptionArgs");
            EventInterceptionArgs_Arguments = type.Properties.First(p => p.Name == "Arguments");
            EventInterceptionArgs_Handler = type.Properties.First(p => p.Name == "Handler");
            EventInterceptionArgs_ReturnValue = type.Properties.First(p => p.Name == "ReturnValue");
            EventInterceptionArgs_Event = type.Properties.First(p => p.Name == "Event");

            BoundEventInterceptionArgs = type = module.GetType(IntNs, "BoundEventInterceptionArgs");
            BoundEventInterceptionArgs_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);

            Arguments = type = module.GetType(Ns, "Arguments");
            Arguments_set_Item = type.Methods.First(m => m.Name == "set_Item");
            Arguments_SetValue = type.Methods.First(m => m.Name == "SetValue" && !m.HasGenericParameters);
            Arguments_SetValueT = type.Methods.First(m => m.Name == "SetValue" && m.HasGenericParameters);

            ArgumentsT = new TypeDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                ArgumentsT[i] = module.GetType(IntNs, "Arguments`" + i);

            ArgumentsT_ctor = new MethodDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                ArgumentsT_ctor[i] = ArgumentsT[i].Methods.First(m => m.IsConstructor && !m.IsStatic);

            ArgumentsT_Item = new FieldDefinition[MaxArguments + 1][];
            for (int i = 1; i <= MaxArguments; i++)
            {
                type = ArgumentsT[i];
                var fields = new FieldDefinition[i];
                for (int f = 0; f < i; f++)
                {
                    string fieldName = "Item" + f;
                    fields[f] = type.Fields.First(fe => fe.Name == fieldName);
                }
                ArgumentsT_Item[i] = fields;
            }

            type = module.GetType(IntNs, "WeaverHelpers");
            WeaverHelpers_InvokeEvent = type.Methods.First(m => m.Name == "InvokeEvent");
            WeaverHelpers_GetEventInfo = type.Methods.First(m => m.Name == "GetEventInfo");
            WeaverHelpers_GetPropertyInfo = type.Methods.First(m => m.Name == "GetPropertyInfo");
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