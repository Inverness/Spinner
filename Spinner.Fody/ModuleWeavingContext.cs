using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace Spinner.Fody
{
    /// <summary>
    /// Provides global context and functionality for weavers.
    /// </summary>
    internal class ModuleWeavingContext
    {
        internal readonly ModuleDefinition Module;
        internal readonly WellKnownSpinnerMembers Spinner;
        internal readonly WellKnownFrameworkMembers Framework;

        private readonly Dictionary<MethodDefinition, Features> _methodFeatures;
        private readonly Dictionary<MethodDefinition, Features> _typeFeatures;

        internal ModuleWeavingContext(ModuleDefinition module, ModuleDefinition libraryModule)
        {
            Module = module;
            Spinner = new WellKnownSpinnerMembers(libraryModule);
            Framework = new WellKnownFrameworkMembers(module);

            _methodFeatures = new Dictionary<MethodDefinition, Features>
            {
                {Spinner.AdviceArgs_Instance.GetMethod, Features.Instance},
                {Spinner.AdviceArgs_Instance.SetMethod, Features.Instance},
                {Spinner.MethodArgs_Arguments.GetMethod, Features.GetArguments},
                {Spinner.PropertyInterceptionArgs_Index.GetMethod, Features.GetArguments},
                {Spinner.Arguments_set_Item, Features.SetArguments},
                {Spinner.Arguments_SetValue, Features.SetArguments},
                {Spinner.Arguments_SetValueT, Features.SetArguments},
                {Spinner.MethodExecutionArgs_FlowBehavior.SetMethod, Features.FlowControl},
                {Spinner.MethodExecutionArgs_ReturnValue.GetMethod, Features.ReturnValue},
                {Spinner.MethodExecutionArgs_ReturnValue.SetMethod, Features.ReturnValue},
                {Spinner.MethodExecutionArgs_YieldValue.GetMethod, Features.YieldValue},
                {Spinner.MethodExecutionArgs_YieldValue.SetMethod, Features.YieldValue},
                {Spinner.MethodArgs_Method.GetMethod, Features.MemberInfo},
                {Spinner.AdviceArgs_Tag.GetMethod, Features.Tag},
                {Spinner.AdviceArgs_Tag.SetMethod, Features.Tag},
            };

            _typeFeatures = new Dictionary<MethodDefinition, Features>
            {
                {Spinner.IMethodBoundaryAspect_OnEntry, Features.OnEntry},
                {Spinner.IMethodBoundaryAspect_OnExit, Features.OnExit},
                {Spinner.IMethodBoundaryAspect_OnSuccess, Features.OnSuccess},
                {Spinner.IMethodBoundaryAspect_OnException, Features.OnException},
                {Spinner.IMethodBoundaryAspect_OnYield, Features.OnYield},
                {Spinner.IMethodBoundaryAspect_OnResume, Features.OnResume},
                {Spinner.IMethodInterceptionAspect_OnInvoke, Features.None},
                {Spinner.IPropertyInterceptionAspect_OnGetValue, Features.None},
                {Spinner.IPropertyInterceptionAspect_OnSetValue, Features.None}
            };
        }

        /// <summary>
        /// Gets a dictionary that maps method definitions to what feature their use in IL indicates.
        /// </summary>
        internal IReadOnlyDictionary<MethodDefinition, Features> MethodFeatures => _methodFeatures;

        /// <summary>
        /// Gets a dictionary that maps aspect interface method definitions to the type feature they indicate support of.
        /// </summary>
        internal IReadOnlyDictionary<MethodDefinition, Features> TypeFeatures => _typeFeatures; 

        internal TypeReference SafeImport(Type type)
        {
            lock (Module)
                return Module.Import(type);
        }

        internal TypeReference SafeImport(TypeReference type)
        {
            if (type.Module == Module)
                return type;
            lock (Module)
                return Module.Import(type);
        }

        internal MethodReference SafeImport(MethodReference method)
        {
            if (method.Module == Module)
                return method;
            lock (Module)
                return Module.Import(method);
        }

        internal FieldReference SafeImport(FieldReference field)
        {
            if (field.Module == Module)
                return field;
            lock (Module)
                return Module.Import(field);
        }

        internal TypeDefinition SafeGetType(string fullName)
        {
            lock (Module)
                return Module.GetType(fullName);
        }
    }
}