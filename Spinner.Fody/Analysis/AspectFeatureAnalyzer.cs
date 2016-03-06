using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Spinner.Aspects;

namespace Spinner.Fody.Analysis
{
    /// <summary>
    /// Analyzies aspect types and advice methods to see what AdviceArgs features they use. This allows the aspect
    /// weaver to exclude code generation that will not be used by the aspect.
    /// </summary>
    internal class AspectFeatureAnalyzer
    {
        // Constants used to optimize various parts of analysis.
        private const char GeneratedNamePrefix = '<';
        private const string AspectInterfaceNameSuffix = "Aspect";
        private const string AdviceTypeNameSuffix = "Advice";
        private const string AdviceArgsNamespace = "Spinner.Aspects";
        private const string AdviceNamePrefix = "On";

        private readonly ModuleWeavingContext _mwc;
        private readonly TypeDefinition _type;
        private readonly AspectKind _aspectKind;
        private readonly TypeDefinition[] _inheritanceList; 

        private AspectFeatureAnalyzer(ModuleWeavingContext mwc, TypeDefinition type, AspectKind aspectKind, TypeDefinition[] inheritanceList)
        {
            _mwc = mwc;
            _type = type;
            _aspectKind = aspectKind;
            _inheritanceList = inheritanceList;

            Debug.Assert(inheritanceList.Last() == type);
        }

        internal static bool IsMaybeAspect(TypeDefinition type)
        {
            return type.IsClass &&
                   type.Name[0] != GeneratedNamePrefix &&
                   !type.IsValueType &&
                   !type.IsAbstract &&
                   !type.IsSpecialName &&
                   type.HasMethods;
        }

        /// <summary>
        /// Analyzes a type and adds AnalyzedFeatureAttribute to the type and its methods where necessary.
        /// It is safe to invoke this in parallel with different types.
        /// </summary>
        internal static void Analyze(ModuleWeavingContext mwc, TypeDefinition type)
        {
            // Aspects can only be types that are valid as attributes.
            Debug.Assert(IsMaybeAspect(type), "this should be checked before starting analysis");

            // Identify the fundamental aspect kind and inheritance list
            AspectKind? kind = null;
            List<TypeDefinition> inheritanceList = null;
            GetAspectInfo(mwc, type, ref kind, ref inheritanceList);

            // The type might not actually be an aspect.
            if (kind.HasValue)
                new AspectFeatureAnalyzer(mwc, type, kind.Value, inheritanceList.ToArray()).Analyze();
        }

        private void Analyze()
        {
            // Method and type feature flags are inherited
            var inheritedMethodFeatures = new Dictionary<MethodDefinition, Features>();
            var inheritedTypeFeatures = Features.None;

            foreach (TypeDefinition t in _inheritanceList)
            {
                if (_mwc.Spinner.IsEmptyAdviceBase(t) || !t.HasMethods)
                    continue;

                // Do not analyze the same type more than once.
                lock (t)
                {
                    if (HasAttribute(t, _mwc.Spinner.AnalyzedFeaturesAttribute))
                        continue;

                    _mwc.LogDebug($"Analyzing features for aspect type {t.Name}");

                    foreach (MethodDefinition m in t.Methods)
                    {
                        // Skip what can't be an advice implementation.
                        if (m.IsStatic || !m.IsPublic || m.IsConstructor || !m.HasParameters || !m.HasBody)
                            continue;

                        // All advice method names start with "On"
                        if (!m.Name.StartsWith(AdviceNamePrefix, StringComparison.Ordinal))
                            continue;

                        if (_aspectKind == AspectKind.Composed)
                        {
                            AdviceType? adviceType = GetMethodAdviceType(m);
                            if (!adviceType.HasValue)
                                continue;

                            MethodDefinition baseMethod = t.BaseType.Resolve().GetMethod(m, true);
                            
                            Features methodFeatures = Features.None;
                            if (baseMethod != null)
                                inheritedMethodFeatures.TryGetValue(baseMethod, out methodFeatures);

                            methodFeatures |= AnalyzeAdvice(m);

                            inheritedMethodFeatures[m] = methodFeatures;

                            AddAnalyzedFeaturesAttribute(m, methodFeatures);

                            inheritedTypeFeatures |= methodFeatures;
                        }
                        else
                        {
                            // Get the base definition from the aspect interface that is being implemented or overridden.
                            Features typeFeatures;
                            MethodDefinition baseDefinition = GetBaseDefinition(m, _aspectKind, out typeFeatures);
                            if (baseDefinition == null)
                                continue;

                            // Join method features inherited from the overriden method with those analyzed now.
                            Features methodFeatures;
                            inheritedMethodFeatures.TryGetValue(baseDefinition, out methodFeatures);

                            methodFeatures |= AnalyzeAdvice(m);

                            inheritedMethodFeatures[baseDefinition] = methodFeatures;

                            AddAnalyzedFeaturesAttribute(m, methodFeatures);

                            // Add type and method level features that this advice uses.
                            inheritedTypeFeatures |= typeFeatures | methodFeatures;
                        }
                    }

                    AddAnalyzedFeaturesAttribute(t, inheritedTypeFeatures);

                    Debug.Assert(HasAttribute(t, _mwc.Spinner.AnalyzedFeaturesAttribute));
                }
            }
        }

        /// <summary>
        /// Analyze an advice's body to see what features it uses.
        /// </summary>
        private Features AnalyzeAdvice(MethodDefinition method)
        {
            Debug.Assert(!method.IsStatic && method.HasParameters);

            Features features = Features.None;

            foreach (Instruction ins in method.Body.Instructions)
            {
                if (ins.OpCode != OpCodes.Callvirt && ins.OpCode != OpCodes.Call)
                    continue;

                var mr = (MethodReference) ins.Operand;

                // Can easily eliminate non-Spinner types here before having to resolve the method.
                if (mr.DeclaringType.Namespace != AdviceArgsNamespace)
                    continue;

                Features mf;
                if (_mwc.MethodFeatures.TryGetValue(mr.Resolve(), out mf))
                    features |= mf;
            }

            return features;
        }

        /// <summary>
        /// Determines the kind of aspect the type implements and the list of inherited classes containing advices.
        /// </summary>
        private static void GetAspectInfo(
            ModuleWeavingContext mwc,
            TypeDefinition type,
            ref AspectKind? ak,
            ref List<TypeDefinition> inheritanceList)
        {
            // Recursively invoke this on bases first
            TypeDefinition baseType = type.BaseType?.Resolve();
            if (baseType != null && !mwc.Spinner.IsEmptyAdviceBase(type) && baseType != mwc.Framework.Attribute)
                GetAspectInfo(mwc, baseType, ref ak, ref inheritanceList);

            // Try to determine the aspect kind from the current type
            if (!ak.HasValue)
                ak = GetAspectKind(mwc, type);

            // If aspect kind was determined by current type or a base type, add it to the list.
            if (ak.HasValue)
            {
                if (inheritanceList == null)
                    inheritanceList = new List<TypeDefinition>();
                inheritanceList.Add(type);
            }
        }

        /// <summary>
        /// Finds if a type implements one of the aspect interfaces.
        /// </summary>
        private static AspectKind? GetAspectKind(ModuleWeavingContext mwc, TypeDefinition type)
        {
            if (type == null || !type.HasInterfaces)
                return null;

            Collection<TypeReference> interfaces = type.Interfaces;
            WellKnownSpinnerMembers spinner = mwc.Spinner;

            for (int i = 0; i < interfaces.Count; i++)
            {
                TypeReference iref = interfaces[i];

                // Before resolving, try examining the name.
                if (!iref.Name.EndsWith(AspectInterfaceNameSuffix, StringComparison.Ordinal))
                    continue;

                TypeDefinition idef = iref.Resolve();

                if (idef == spinner.IAspect)
                    return AspectKind.Composed;
                if (idef == spinner.IMethodBoundaryAspect)
                    return AspectKind.MethodBoundary;
                if (idef == spinner.IMethodInterceptionAspect)
                    return AspectKind.MethodInterception;
                if (idef == spinner.ILocationInterceptionAspect)
                    return AspectKind.PropertyInterception;
                if (idef == spinner.IEventInterceptionAspect)
                    return AspectKind.EventInterception;
            }

            return null;
        }

        /// <summary>
        /// Add the AnalyzedFeaturesAttribute() to a target type or method.
        /// </summary>
        private void AddAnalyzedFeaturesAttribute(
            ICustomAttributeProvider target,
            Features features)
        {
            var attr = new CustomAttribute(_mwc.SafeImport(_mwc.Spinner.AnalyzedFeaturesAttribute_ctor));
            attr.ConstructorArguments.Add(new CustomAttributeArgument(_mwc.Spinner.Features, (uint) features));
            target.CustomAttributes.Add(attr);
        }

        private static bool HasAttribute(ICustomAttributeProvider target, TypeReference attributeType)
        {
            if (!target.HasCustomAttributes)
                return false;
            
            for (int c = 0; c < target.CustomAttributes.Count; c++)
            {
                TypeReference at = target.CustomAttributes[c].AttributeType;
                if (at.IsSame(attributeType))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get base definition from one of the aspect interfaces that is being implemented.
        /// </summary>
        private MethodDefinition GetBaseDefinition(
            MethodDefinition method,
            AspectKind ak,
            out Features typeFeature)
        {
            TypeDefinition baseType;

            switch (ak)
            {
                case AspectKind.Composed:
                    typeFeature = Features.None;
                    return null;
                case AspectKind.MethodBoundary:
                    baseType = _mwc.Spinner.IMethodBoundaryAspect;
                    break;
                case AspectKind.MethodInterception:
                    baseType = _mwc.Spinner.IMethodInterceptionAspect;
                    break;
                case AspectKind.PropertyInterception:
                    baseType = _mwc.Spinner.ILocationInterceptionAspect;
                    break;
                case AspectKind.EventInterception:
                    baseType = _mwc.Spinner.IEventInterceptionAspect;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ak), ak, null);
            }

            MethodDefinition baseMethod = baseType.GetMethod(method, false);
            if (baseMethod == null)
            {
                typeFeature = Features.None;
                return null;
            }

            typeFeature = _mwc.TypeFeatures[baseMethod];
            return baseMethod;
        }

        private AdviceType? GetMethodAdviceType(MethodDefinition method)
        {
            if (method.HasCustomAttributes)
            {
                foreach (CustomAttribute ca in method.CustomAttributes)
                {
                    AdviceType adviceType;
                    if (_mwc.AdviceTypes.TryGetValue(ca.AttributeType, out adviceType))
                        return adviceType;
                }
            }

            return null;
        }

        //private AdviceType? GetMethodAdviceType(MethodDefinition method, bool inherited, out MethodDefinition adviceMethod)
        //{
        //    MethodDefinition currentMethod = method;

        //    do
        //    {
        //        if (currentMethod.HasCustomAttributes)
        //        {
        //            foreach (CustomAttribute ca in currentMethod.CustomAttributes)
        //            {
        //                AdviceType adviceType;
        //                if (_mwc.AdviceTypes.TryGetValue(ca.AttributeType, out adviceType))
        //                {
        //                    adviceMethod = currentMethod;
        //                    return adviceType;
        //                }
        //            }
        //        }

        //        if (!inherited)
        //            break;

        //        currentMethod = currentMethod.DeclaringType.BaseType?.Resolve()?.GetMethod(method, true);
        //    } while (currentMethod != null);

        //    adviceMethod = null;
        //    return null;
        //}
    }
}
