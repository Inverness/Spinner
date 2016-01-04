using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Spinner.Fody.Analysis
{
    /// <summary>
    /// Analyzies aspect types and advice methods to see what AdviceArgs features they use. This allows the aspect
    /// weaver to exclude code generation that will not be used by the aspect.
    /// </summary>
    internal static class AspectFeatureAnalyzer
    {
        // Constants used to optimize various parts of analysis.
        private const string GeneratedNamePrefix = "<";
        private const int AspectInterfaceNameMinimumLength = 21;
        private const string AspectInterfaceNameSuffix = "Aspect";
        private const string AdviceArgsNamespace = "Spinner";
        private const string AdviceNamePrefix = "On";

        /// <summary>
        /// Analyzes a type and adds AnalyzedFeatureAttribute to the type and its methods where necessary.
        /// It is safe to invoke this in parallel with different types.
        /// </summary>
        internal static void Analyze(ModuleWeavingContext mwc, TypeDefinition type)
        {
            // Aspects can only be types that are valid as attributes.
            if (!type.IsClass || type.IsValueType || type.IsAbstract || type.IsSpecialName || !type.HasMethods ||
                type.Name.StartsWith(GeneratedNamePrefix, StringComparison.Ordinal))
                return;

            // Identify the fundamental aspect kind and inheritance list
            AspectKind? ak = null;
            List<TypeDefinition> types = null;
            GetAspectKindAndTypes(mwc, type, ref ak, ref types);

            // The type might not actually be an aspect.
            if (!ak.HasValue)
                return;

            Debug.Assert(types.Last() == type);

            // Method and type feature flags are inherited
            var inheritedMethodFeatures = new Dictionary<MethodDefinition, Features>();
            var inheritedTypeFeatures = Features.None;

            foreach (TypeDefinition t in types)
            {
                if (mwc.Spinner.IsEmptyAdviceBase(t) || !t.HasMethods)
                    continue;
                
                // Do not analyze the same type more than once.
                lock (t)
                {
                    if (HasAttribute(t, mwc.Spinner.AnalyzedFeaturesAttribute))
                        continue;

                    foreach (MethodDefinition m in t.Methods)
                    {
                        // Skip what can't be an advice implementation.
                        if (m.IsStatic || !m.IsPublic || m.IsConstructor || !m.HasParameters || !m.HasBody)
                            continue;

                        // All advice method names start with "On"
                        if (!m.Name.StartsWith(AdviceNamePrefix, StringComparison.Ordinal))
                            continue;

                        // Get the base definition from the aspect interface that is being implemented or overridden.
                        Features typeFeatures;
                        MethodDefinition baseDefinition = GetBaseDefinition(mwc, m, ak.Value, out typeFeatures);
                        if (baseDefinition == null)
                            continue;

                        inheritedTypeFeatures |= typeFeatures;

                        // Inherit features from base classes.
                        Features methodFeatures;
                        inheritedMethodFeatures.TryGetValue(baseDefinition, out methodFeatures);

                        methodFeatures |= AnalyzeAdvice(mwc, m);
                        inheritedTypeFeatures |= methodFeatures;

                        inheritedMethodFeatures[baseDefinition] = methodFeatures;

                        AddAnalyzedFeaturesAttribute(mwc, m, methodFeatures);
                    }

                    AddAnalyzedFeaturesAttribute(mwc, t, inheritedTypeFeatures);

                    Debug.Assert(HasAttribute(t, mwc.Spinner.AnalyzedFeaturesAttribute));
                }
            }
        }

        /// <summary>
        /// Analyze an advice's body to see what features it uses.
        /// </summary>
        private static Features AnalyzeAdvice(ModuleWeavingContext mwc, MethodDefinition method)
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
                if (mwc.MethodFeatures.TryGetValue(mr.Resolve(), out mf))
                    features |= mf;
            }

            return features;
        }

        /// <summary>
        /// Determines the kind of aspect the type implements and the list of inherited classes containing advices.
        /// </summary>
        private static void GetAspectKindAndTypes(
            ModuleWeavingContext mwc,
            TypeDefinition type,
            ref AspectKind? ak,
            ref List<TypeDefinition> types)
        {
            TypeDefinition baseType = type.BaseType?.Resolve();
            if (baseType != null && !mwc.Spinner.IsEmptyAdviceBase(type) && baseType != mwc.Framework.Attribute)
                GetAspectKindAndTypes(mwc, baseType, ref ak, ref types);

            if (!ak.HasValue)
                ak = GetAspectKind(mwc, type);

            if (ak.HasValue)
            {
                if (types == null)
                    types = new List<TypeDefinition>();
                types.Add(type);
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
                if (iref.Name.Length < AspectInterfaceNameMinimumLength ||
                    !iref.Name.EndsWith(AspectInterfaceNameSuffix, StringComparison.Ordinal))
                    continue;

                TypeDefinition idef = iref.Resolve();

                if (idef == spinner.IMethodBoundaryAspect)
                    return AspectKind.MethodBoundary;
                if (idef == spinner.IMethodInterceptionAspect)
                    return AspectKind.MethodInterception;
                if (idef == spinner.IPropertyInterceptionAspect)
                    return AspectKind.PropertyInterception;
            }

            return null;
        }

        /// <summary>
        /// Add the AnalyzedFeaturesAttribute() to a target type or method.
        /// </summary>
        private static void AddAnalyzedFeaturesAttribute(
            ModuleWeavingContext mwc,
            ICustomAttributeProvider target,
            Features features)
        {
            var attr = new CustomAttribute(mwc.SafeImport(mwc.Spinner.AnalyzedFeaturesAttribute_ctor));
            attr.ConstructorArguments.Add(new CustomAttributeArgument(mwc.Spinner.Features, (uint) features));
            target.CustomAttributes.Add(attr);
        }

        private static bool HasAttribute(ICustomAttributeProvider target, TypeReference attributeType)
        {
            if (!target.HasCustomAttributes)
                return false;
            
            for (int c = 0; c < target.CustomAttributes.Count; c++)
            {
                TypeReference at = target.CustomAttributes[c].AttributeType;
                if (at.IsSimilar(attributeType) && at.Resolve() == attributeType.Resolve())
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get base definition from one of the aspect interfaces that is being implemented.
        /// </summary>
        private static MethodDefinition GetBaseDefinition(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            AspectKind ak,
            out Features typeFeature)
        {
            TypeDefinition baseType;

            switch (ak)
            {
                case AspectKind.MethodBoundary:
                    baseType = mwc.Spinner.IMethodBoundaryAspect;
                    break;
                case AspectKind.MethodInterception:
                    baseType = mwc.Spinner.IMethodInterceptionAspect;
                    break;
                case AspectKind.PropertyInterception:
                    baseType = mwc.Spinner.IPropertyInterceptionAspect;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ak), ak, null);
            }

            MethodDefinition baseMethod = baseType.GetMethod(method);
            if (baseMethod == null)
            {
                typeFeature = Features.None;
                return null;
            }

            typeFeature = mwc.TypeFeatures[baseMethod];
            return baseMethod;
        }
    }
}
