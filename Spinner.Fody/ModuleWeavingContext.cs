using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mono.Cecil;
using Mono.Collections.Generic;
using Spinner.Aspects;
using Spinner.Fody.Execution;
using Spinner.Fody.Multicasting;
using Spinner.Fody.Utilities;
using Spinner.Fody.Weaving;

namespace Spinner.Fody
{
    /// <summary>
    /// Provides global context and functionality for weavers.
    /// </summary>
    internal class ModuleWeavingContext
    {
        private const string AspectInterfaceNameSuffix = "Aspect";

        /// <summary>
        /// The module currently being weaved.
        /// </summary>
        internal readonly ModuleDefinition Module;

        /// <summary>
        /// Well known Spinner members.
        /// </summary>
        internal readonly WellKnownSpinnerMembers Spinner;

        /// <summary>
        /// Well known .NET members.
        /// </summary>
        internal readonly WellKnownFrameworkMembers Framework;
        
        private readonly Dictionary<MethodDefinition, Features> _methodFeatures;
        private readonly Dictionary<MethodDefinition, Features> _typeFeatures;
        private readonly Dictionary<TypeReference, AdviceType> _adviceTypes;
        private int _aspectIndexCounter;

        private readonly ConcurrentDictionary<TypeDefinition, AspectKind?> _aspectKinds =
            new ConcurrentDictionary<TypeDefinition, AspectKind?>();
        private readonly ConcurrentDictionary<TypeDefinition, AspectInfo> _aspectInfo =
            new ConcurrentDictionary<TypeDefinition, AspectInfo>();
        private readonly LockTargetProvider<TypeDefinition> _aspectInfoLocks = new LockTargetProvider<TypeDefinition>();

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
                {Spinner.LocationInterceptionArgs_Index.GetMethod, Features.GetArguments},
                {Spinner.EventInterceptionArgs_Arguments.GetMethod, Features.GetArguments},
                {Spinner.Arguments_set_Item, Features.SetArguments},
                {Spinner.Arguments_SetValue, Features.SetArguments},
                {Spinner.Arguments_SetValueT, Features.SetArguments},
                {Spinner.MethodExecutionArgs_FlowBehavior.SetMethod, Features.FlowControl},
                {Spinner.MethodExecutionArgs_ReturnValue.GetMethod, Features.ReturnValue},
                {Spinner.MethodExecutionArgs_ReturnValue.SetMethod, Features.ReturnValue},
                {Spinner.EventInterceptionArgs_ReturnValue.GetMethod, Features.ReturnValue},
                {Spinner.EventInterceptionArgs_ReturnValue.SetMethod, Features.ReturnValue},
                {Spinner.MethodExecutionArgs_YieldValue.GetMethod, Features.YieldValue},
                {Spinner.MethodExecutionArgs_YieldValue.SetMethod, Features.YieldValue},
                {Spinner.MethodArgs_Method.GetMethod, Features.MemberInfo},
                {Spinner.LocationInterceptionArgs_Property.GetMethod, Features.MemberInfo},
                {Spinner.EventInterceptionArgs_Event.GetMethod, Features.MemberInfo},
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
                {Spinner.ILocationInterceptionAspect_OnGetValue, Features.None},
                {Spinner.ILocationInterceptionAspect_OnSetValue, Features.None},
                {Spinner.IEventInterceptionAspect_OnAddHandler, Features.None},
                {Spinner.IEventInterceptionAspect_OnRemoveHandler, Features.None},
                {Spinner.IEventInterceptionAspect_OnInvokeHandler, Features.None}
            };

            _adviceTypes = new Dictionary<TypeReference, AdviceType>(new TypeReferenceIsSameComparer())
            {
                {Spinner.MethodEntryAdvice, AdviceType.MethodEntry},
                {Spinner.MethodExitAdvice, AdviceType.MethodExit},
                {Spinner.MethodSuccessAdvice, AdviceType.MethodSuccess},
                {Spinner.MethodExceptionAdvice, AdviceType.MethodException},
                {Spinner.MethodFilterExceptionAdvice, AdviceType.MethodFilterException},
                {Spinner.MethodYieldAdvice, AdviceType.MethodYield},
                {Spinner.MethodResumeAdvice, AdviceType.MethodResume},
                {Spinner.MethodInvokeAdvice, AdviceType.MethodInvoke}
            };

            MulticastEngine = new MulticastEngine(Module, Framework.CompilerGeneratedAttribute, Spinner.MulticastAttribute);

            BuildTimeExecutionEngine = new BuildTimeExecutionEngine(Module);
        }

        /// <summary>
        /// Gets a dictionary that maps method definitions to what feature their use in IL indicates.
        /// </summary>
        internal IReadOnlyDictionary<MethodDefinition, Features> MethodFeatures => _methodFeatures;

        /// <summary>
        /// Gets a dictionary that maps aspect interface method definitions to the type feature they indicate support of.
        /// </summary>
        internal IReadOnlyDictionary<MethodDefinition, Features> TypeFeatures => _typeFeatures;

        /// <summary>
        /// Gets a dictionary that maps advice attribute types to the AdviceType enum.
        /// </summary>
        internal IReadOnlyDictionary<TypeReference, AdviceType> AdviceTypes => _adviceTypes;

        internal MulticastEngine MulticastEngine { get; }

        internal BuildTimeExecutionEngine BuildTimeExecutionEngine { get; }

        internal TypeReference SafeImport(Type type)
        {
            return Module.Import(type);
        }

        internal TypeReference SafeImport(TypeReference type)
        {
            if (type.Module == Module)
                return type;
            return Module.Import(type);
        }

        internal MethodReference SafeImport(MethodReference method)
        {
            if (method.Module == Module)
                return method;
            return Module.Import(method);
        }

        internal FieldReference SafeImport(FieldReference field)
        {
            if (field.Module == Module)
                return field;
            return Module.Import(field);
        }

        internal TypeDefinition SafeGetType(string fullName)
        {
            return Module.GetType(fullName);
        }

        internal int NewAspectIndex()
        {
            return Interlocked.Increment(ref _aspectIndexCounter);
        }

        /// <summary>
        /// Get the features declared for a type. AnalzyedFeaturesAttribute takes precedence over FeaturesAttribute.
        /// </summary>
        internal Features? GetFeatures(TypeDefinition aspectType)
        {
            TypeDefinition attrType = Spinner.FeaturesAttribute;
            TypeDefinition analyzedAttrType = Spinner.AnalyzedFeaturesAttribute;

            Features? features = null;

            TypeDefinition current = aspectType;
            while (current != null)
            {
                if (current.HasCustomAttributes)
                {
                    foreach (CustomAttribute a in current.CustomAttributes)
                    {
                        TypeReference atype = a.AttributeType;

                        if (atype.IsSame(analyzedAttrType))
                        {
                            return (Features) (uint) a.ConstructorArguments.First().Value;
                        }

                        if (atype.IsSame(attrType))
                        {
                            features = (Features) (uint) a.ConstructorArguments.First().Value;
                            // Continue in case AnalyzedFeaturesAttribute is found.
                        }
                    }
                }

                // No need to examine base type if found here
                if (features.HasValue)
                    return features.Value;

                current = current.BaseType?.Resolve();
            }

            return null;
        }

        /// <summary>
        /// Get the features declared for an advice. AnalzyedFeaturesAttribute takes precedence over FeaturesAttribute.
        /// </summary>
        internal Features? GetFeatures(MethodDefinition advice)
        {
            TypeDefinition attrType = Spinner.FeaturesAttribute;
            TypeDefinition analyzedAttrType = Spinner.AnalyzedFeaturesAttribute;

            Features? features = null;

            MethodDefinition current = advice;
            while (current != null)
            {
                if (current.HasCustomAttributes)
                {
                    foreach (CustomAttribute a in current.CustomAttributes)
                    {
                        TypeReference atype = a.AttributeType;

                        if (atype.IsSame(analyzedAttrType))
                        {
                            return (Features) (uint) a.ConstructorArguments.First().Value;
                        }

                        if (atype.IsSame(attrType))
                        {
                            features = (Features) (uint) a.ConstructorArguments.First().Value;
                            // Continue in case AnalyzedFeaturesAttribute is found on same type.
                        }
                    }
                }

                if (features.HasValue)
                    return features.Value;

                current = current.DeclaringType.BaseType?.Resolve()?.GetMethod(advice, true);
            }

            return null;
        }

        internal AspectKind? GetAspectKind(TypeDefinition type, bool withBases)
        {
            if (type == null || !type.IsClass)
                return null;

            if (!withBases)
                return GetAspectKindCore(type);

            TypeDefinition current = type;

            while (current != null)
            {
                AspectKind? result = GetAspectKindCore(current);
                if (result.HasValue)
                    return result;

                current = current.BaseType?.Resolve();
            }

            return null;
        }

        internal AspectInfo GetAspectInfo(TypeDefinition type)
        {
            AspectInfo info;
            if (_aspectInfo.TryGetValue(type, out info))
                return info;
            
            AspectKind? kind = GetAspectKind(type, true);

            if (kind.HasValue)
            {
                lock (_aspectInfoLocks.Get(type))
                    info = AspectInfoFactory.Create(this, type, kind.Value);
            }

            return _aspectInfo.GetOrAdd(type, info);
        }

        /// <summary>
        /// Finds if a class implements one of the aspect interfaces.
        /// </summary>
        private AspectKind? GetAspectKindCore(TypeDefinition type)
        {
            if (!type.HasInterfaces)
                return null;

            AspectKind? result;
            if (_aspectKinds.TryGetValue(type, out result))
                return result;

            Collection<TypeReference> interfaces = type.Interfaces;
            WellKnownSpinnerMembers spinner = Spinner;

            for (int i = 0; i < interfaces.Count; i++)
            {
                TypeReference iref = interfaces[i];

                // Before resolving, try examining the name.
                if (!iref.Name.EndsWith(AspectInterfaceNameSuffix, StringComparison.Ordinal))
                    continue;

                TypeDefinition idef = iref.Resolve();

                if (idef == spinner.IAspect)
                {
                    result = AspectKind.Composed;
                    break;
                }
                if (idef == spinner.IMethodBoundaryAspect)
                {
                    result = AspectKind.MethodBoundary;
                    break;
                }
                if (idef == spinner.IMethodInterceptionAspect)
                {
                    result = AspectKind.MethodInterception;
                    break;
                }
                if (idef == spinner.ILocationInterceptionAspect)
                {
                    result = AspectKind.PropertyInterception;
                    break;
                }
                if (idef == spinner.IEventInterceptionAspect)
                {
                    result = AspectKind.EventInterception;
                    break;
                }
            }

            _aspectKinds.TryAdd(type, result);

            return result;
        }

        private class TypeReferenceIsSameComparer : IEqualityComparer<TypeReference>
        {
            public bool Equals(TypeReference x, TypeReference y)
            {
                return x.IsSame(y);
            }

            public int GetHashCode(TypeReference obj)
            {
                return obj.Name.GetHashCode();
            }
        }
    }
}