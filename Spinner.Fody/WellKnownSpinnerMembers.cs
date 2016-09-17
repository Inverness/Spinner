using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using SA = Spinner.Aspects;
using SAv = Spinner.Aspects.Advices;
using SAi = Spinner.Aspects.Internal;
using SE = Spinner.Extensibility;

namespace Spinner.Fody
{
    /// <summary>
    /// Cache well known aspect library definitions for use by weavers. These are not imported by default.
    /// </summary>
    internal class WellKnownSpinnerMembers
    {
        internal const int MaxArguments = Aspects.Arguments.MaxItems;

        // ReSharper disable InconsistentNaming
        private const string NA = "Spinner.Aspects";
        private const string NAv = "Spinner.Aspects.Advices";
        private const string NAi = "Spinner.Aspects.Internal";
        private const string NE = "Spinner.Extensibility";
        
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

        internal readonly TypeDefinition ILocationInterceptionAspect;
        internal readonly MethodDefinition ILocationInterceptionAspect_OnGetValue;
        internal readonly MethodDefinition ILocationInterceptionAspect_OnSetValue;

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

        internal readonly TypeDefinition LocationBindingT1;

        internal readonly TypeDefinition EventBinding;

        internal readonly TypeDefinition LocationInterceptionArgs;
        internal readonly PropertyDefinition LocationInterceptionArgs_Property;
        internal readonly PropertyDefinition LocationInterceptionArgs_Index;

        internal readonly TypeDefinition BoundLocationInterceptionArgsT1;
        internal readonly MethodDefinition BoundLocationInterceptionArgsT1_ctor;
        internal readonly FieldDefinition BoundLocationInterceptionArgsT1_TypedValue;

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
        internal readonly MethodDefinition WeaverHelpers_InvokeEventAdvice;
        internal readonly MethodDefinition WeaverHelpers_GetEventInfo;
        internal readonly MethodDefinition WeaverHelpers_GetPropertyInfo;

        internal readonly TypeDefinition MulticastAttribute;
        internal readonly TypeDefinition MulticastAttributes;
        internal readonly TypeDefinition MulticastInheritance;
        internal readonly TypeDefinition MulticastTargets;

        internal readonly TypeDefinition GroupingAdvice;
        internal readonly PropertyDefinition GroupingAdvice_Master;
        internal readonly TypeDefinition MethodInvokeAdvice;
        internal readonly TypeDefinition MethodBoundaryAdvice;
        internal readonly PropertyDefinition MethodBoundaryAdvice_ApplyToStateMachine;
        internal readonly TypeDefinition MethodEntryAdvice;
        internal readonly TypeDefinition MethodExitAdvice;
        internal readonly TypeDefinition MethodSuccessAdvice;
        internal readonly TypeDefinition MethodExceptionAdvice;
        internal readonly TypeDefinition MethodFilterExceptionAdvice;
        internal readonly TypeDefinition MethodYieldAdvice;
        internal readonly TypeDefinition MethodResumeAdvice;
        internal readonly TypeDefinition MethodPointcut;
        internal readonly TypeDefinition SelfPointcut;
        internal readonly TypeDefinition MulticastPointcut;

        // ReSharper restore InconsistentNaming

        private readonly HashSet<TypeDefinition> _emptyAspectBaseTypes; 

        internal WellKnownSpinnerMembers(ModuleDefinition module)
        {
            Module = module;

            TypeDefinition type;

            IAspect = module.GetType(NA, nameof(SA.IAspect));

            IMethodBoundaryAspect = type = module.GetType(NA, nameof(SA.IMethodBoundaryAspect));
            IMethodBoundaryAspect_OnEntry = type.Methods.First(m => m.Name == "OnEntry");
            IMethodBoundaryAspect_OnExit = type.Methods.First(m => m.Name == "OnExit");
            IMethodBoundaryAspect_OnSuccess = type.Methods.First(m => m.Name == "OnSuccess");
            IMethodBoundaryAspect_OnException = type.Methods.First(m => m.Name == "OnException");
            IMethodBoundaryAspect_OnYield = type.Methods.First(m => m.Name == "OnYield");
            IMethodBoundaryAspect_OnResume = type.Methods.First(m => m.Name == "OnResume");
            IMethodBoundaryAspect_FilterException = type.Methods.First(m => m.Name == "FilterException");

            IMethodInterceptionAspect = type = module.GetType(NA, nameof(SA.IMethodInterceptionAspect));
            IMethodInterceptionAspect_OnInvoke = type.Methods.First(m => m.Name == "OnInvoke");

            ILocationInterceptionAspect = type = module.GetType(NA, nameof(SA.ILocationInterceptionAspect));
            ILocationInterceptionAspect_OnGetValue = type.Methods.First(m => m.Name == "OnGetValue");
            ILocationInterceptionAspect_OnSetValue = type.Methods.First(m => m.Name == "OnSetValue");

            IEventInterceptionAspect = type = module.GetType(NA, nameof(SA.IEventInterceptionAspect));
            IEventInterceptionAspect_OnAddHandler = type.Methods.First(m => m.Name == "OnAddHandler");
            IEventInterceptionAspect_OnRemoveHandler = type.Methods.First(m => m.Name == "OnRemoveHandler");
            IEventInterceptionAspect_OnInvokeHandler = type.Methods.First(m => m.Name == "OnInvokeHandler");

            MethodBoundaryAspect = module.GetType(NA, nameof(SA.MethodBoundaryAspect));
            MethodInterceptionAspect = module.GetType(NA, nameof(SA.MethodInterceptionAspect));
            PropertyInterceptionAspect = module.GetType(NA, nameof(SA.LocationInterceptionAspect));
            EventInterceptionAspect = module.GetType(NA, nameof(SA.EventInterceptionAspect));

            _emptyAspectBaseTypes = new HashSet<TypeDefinition>
            {
                MethodBoundaryAspect,
                MethodInterceptionAspect,
                PropertyInterceptionAspect,
                EventInterceptionAspect
            };

            AdviceArgs = type = module.GetType(NA, nameof(SA.AdviceArgs));
            AdviceArgs_Instance = type.Properties.First(p => p.Name == "Instance");
            AdviceArgs_Tag = type.Properties.First(p => p.Name == "Tag");

            MethodArgs = type = module.GetType(NA, nameof(SA.MethodArgs));
            MethodArgs_Method = type.Properties.First(p => p.Name == "Method");
            MethodArgs_Arguments = type.Properties.First(p => p.Name == "Arguments");

            MethodExecutionArgs = type = module.GetType(NA, nameof(SA.MethodExecutionArgs));
            MethodExecutionArgs_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);
            MethodExecutionArgs_Exception = type.Properties.First(m => m.Name == "Exception");
            MethodExecutionArgs_FlowBehavior = type.Properties.First(m => m.Name == "FlowBehavior");
            MethodExecutionArgs_ReturnValue = type.Properties.First(m => m.Name == "ReturnValue");
            MethodExecutionArgs_YieldValue = type.Properties.First(m => m.Name == "YieldValue");

            MethodBinding = module.GetType(NAi, nameof(SAi.MethodBinding));
            MethodBindingT1 = module.GetType(NAi, nameof(SAi.MethodBinding) + "`1");
            LocationBindingT1 = module.GetType(NAi, "LocationBinding`1");
            EventBinding = module.GetType(NAi, nameof(SAi.EventBinding));

            Features = module.GetType(NA, nameof(SA.Features));
            FeaturesAttribute = module.GetType(NA, nameof(SA.FeaturesAttribute));
            AnalyzedFeaturesAttribute = type = module.GetType(NAi, nameof(SAi.AnalyzedFeaturesAttribute));
            AnalyzedFeaturesAttribute_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);

            LocationInterceptionArgs = type = module.GetType(NA, nameof(SA.LocationInterceptionArgs));
            LocationInterceptionArgs_Property = type.Properties.First(p => p.Name == "Location");
            LocationInterceptionArgs_Index = type.Properties.First(p => p.Name == "Index");

            MethodInterceptionArgs = module.GetType(NA, nameof(SA.MethodInterceptionArgs));

            BoundMethodInterceptionArgs = type = module.GetType(NAi, nameof(SAi.BoundMethodInterceptionArgs));
            BoundMethodInterceptionArgs_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);

            BoundMethodInterceptionArgsT1 = type = module.GetType(NAi, nameof(SAi.BoundMethodInterceptionArgs) + "`1");
            BoundMethodInterceptionArgsT1_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundMethodInterceptionArgsT1_TypedReturnValue = type.Fields.First(f => f.Name == "TypedReturnValue");

            BoundLocationInterceptionArgsT1 = type = module.GetType(NAi, "BoundLocationInterceptionArgs`1");
            BoundLocationInterceptionArgsT1_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);
            BoundLocationInterceptionArgsT1_TypedValue = type.Fields.First(f => f.Name == "TypedValue");

            EventInterceptionArgs = type = module.GetType(NA, nameof(SA.EventInterceptionArgs));
            EventInterceptionArgs_Arguments = type.Properties.First(p => p.Name == "Arguments");
            EventInterceptionArgs_Handler = type.Properties.First(p => p.Name == "Handler");
            EventInterceptionArgs_ReturnValue = type.Properties.First(p => p.Name == "ReturnValue");
            EventInterceptionArgs_Event = type.Properties.First(p => p.Name == "Event");

            BoundEventInterceptionArgs = type = module.GetType(NAi, nameof(SAi.BoundEventInterceptionArgs));
            BoundEventInterceptionArgs_ctor = type.Methods.First(m => m.IsConstructor && !m.IsStatic);

            Arguments = type = module.GetType(NA, nameof(SA.Arguments));
            Arguments_set_Item = type.Methods.First(m => m.Name == "set_Item");
            Arguments_SetValue = type.Methods.First(m => m.Name == "SetValue" && !m.HasGenericParameters);
            Arguments_SetValueT = type.Methods.First(m => m.Name == "SetValue" && m.HasGenericParameters);

            ArgumentsT = new TypeDefinition[MaxArguments + 1];
            for (int i = 1; i <= MaxArguments; i++)
                ArgumentsT[i] = module.GetType(NAi, "Arguments`" + i);

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

            type = module.GetType(NAi, nameof(SAi.WeaverHelpers));
            WeaverHelpers_InvokeEvent = type.Methods.First(m => m.Name == "InvokeEvent");
            WeaverHelpers_InvokeEventAdvice = type.Methods.First(m => m.Name == "InvokeEventAdvice");
            WeaverHelpers_GetEventInfo = type.Methods.First(m => m.Name == "GetEventInfo");
            WeaverHelpers_GetPropertyInfo = type.Methods.First(m => m.Name == "GetPropertyInfo");

            MulticastAttribute = module.GetType(NE, nameof(SE.MulticastAttribute));
            MulticastAttributes = module.GetType(NE, nameof(SE.MulticastAttributes));
            MulticastInheritance = module.GetType(NE, nameof(SE.MulticastInheritance));
            MulticastTargets = module.GetType(NE, nameof(SE.MulticastTargets));

            GroupingAdvice = type = module.GetType(NAv, nameof(SAv.GroupingAdvice));
            GroupingAdvice_Master = type.GetProperty(nameof(SAv.GroupingAdvice.Master), false);
            MethodInvokeAdvice = module.GetType(NAv, nameof(SAv.MethodInvokeAdvice));
            MethodBoundaryAdvice = type = module.GetType(NAv, nameof(SAv.MethodBoundaryAdvice));
            MethodBoundaryAdvice_ApplyToStateMachine = type.GetProperty(nameof(SAv.MethodBoundaryAdvice.ApplyToStateMachine), false);
            MethodEntryAdvice = module.GetType(NAv, nameof(SAv.MethodEntryAdvice));
            MethodExitAdvice = module.GetType(NAv, nameof(SAv.MethodExitAdvice));
            MethodSuccessAdvice = module.GetType(NAv, nameof(SAv.MethodSuccessAdvice));
            MethodExceptionAdvice = module.GetType(NAv, nameof(SAv.MethodExceptionAdvice));
            MethodFilterExceptionAdvice = module.GetType(NAv, nameof(SAv.MethodFilterExceptionAdvice));
            MethodYieldAdvice = module.GetType(NAv, nameof(SAv.MethodYieldAdvice));
            MethodResumeAdvice = module.GetType(NAv, nameof(SAv.MethodResumeAdvice));
            MethodPointcut = module.GetType(NAv, nameof(SAv.MethodPointcut));
            MulticastPointcut = module.GetType(NAv, nameof(SAv.MulticastPointcut));
            SelfPointcut = module.GetType(NAv, nameof(SAv.SelfPointcut));
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