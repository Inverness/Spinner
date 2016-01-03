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
        private const int ShortestAspectInterfaceNameLength = 21;
        private const string AspectInterfaceSuffix = "Aspect";
        private const string AdviceArgsNamespace = "Spinner";

        internal static void Analyze(ModuleWeavingContext mwc, TypeDefinition type)
        {
            AspectKind? ak = null;
            List<TypeDefinition> types = null;
            FindAspectImplementations(mwc, type, ref ak, ref types);

            if (!ak.HasValue)
                return;

            // base class is first
            Debug.Assert(types != null && types.Last() == type);

            foreach (TypeDefinition t in types)
            {
                if (mwc.Spinner.IsEmptyAdviceBase(t))
                    continue;

                foreach (MethodDefinition m in t.Methods)
                {
                    if (m.IsStatic || !m.IsPublic || m.IsConstructor || !m.HasParameters || !m.HasBody)
                        continue;

                    // If this method is not in the current module it might have already been analyzed.
                    if (m.HasCustomAttributes)
                    {
                        bool alreadyAnalyzed = false;
                        for (int c = 0; c < m.CustomAttributes.Count; c++)
                        {
                            if (m.CustomAttributes[c].AttributeType.IsSame(mwc.Spinner.AnalyzedFeaturesAttribute))
                            {
                                alreadyAnalyzed = true;
                                break;
                            }
                        }

                        if (alreadyAnalyzed)
                            continue;
                    }

                    Features? f = AnalyzeAdvice(mwc, m, ak.Value);

                    if (f.HasValue)
                        AddAnalyzedFeaturesAttribute(mwc, m, f.Value);
                }
            }
        }

        private static void AddAnalyzedFeaturesAttribute(ModuleWeavingContext mwc, MethodDefinition method, Features features)
        {
            var attr = new CustomAttribute(mwc.SafeImport(mwc.Spinner.AnalyzedFeaturesAttribute_ctor));
            attr.ConstructorArguments.Add(new CustomAttributeArgument(mwc.Spinner.Features, (uint) features));
            method.CustomAttributes.Add(attr);
        }

        /// <summary>
        /// Analyze an advice's body to see what features it uses.
        /// </summary>
        private static Features? AnalyzeAdvice(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            AspectKind ak)
        {
            Debug.Assert(!method.IsStatic && method.HasParameters);

            TypeDefinition argsDef = method.Parameters[0].ParameterType.Resolve();

            switch (ak)
            {
                case AspectKind.MethodBoundary:
                    if (argsDef != mwc.Spinner.MethodExecutionArgs)
                        return null;
                    break;
                case AspectKind.MethodInterception:
                    if (argsDef != mwc.Spinner.MethodInterceptionArgs)
                        return null;
                    break;
                case AspectKind.PropertyInterception:
                    if (argsDef != mwc.Spinner.PropertyInterceptionArgs)
                        return null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ak), ak, null);
            }

            Features f = Features.None;

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
                    f |= mf;
            }

            return f;
        }

        /// <summary>
        /// Finds types from the inheritence chain that implement an aspect.
        /// </summary>
        private static void FindAspectImplementations(
            ModuleWeavingContext mwc,
            TypeDefinition type,
            ref AspectKind? ak,
            ref List<TypeDefinition> results)
        {
            TypeDefinition baseType = type.BaseType?.Resolve();
            if (baseType != null && !mwc.Spinner.IsEmptyAdviceBase(type) && baseType != mwc.Framework.Attribute)
                FindAspectImplementations(mwc, baseType, ref ak, ref results);

            if (!ak.HasValue)
                ak = GetAspectKind(mwc, type);

            if (ak.HasValue)
            {
                if (results == null)
                    results = new List<TypeDefinition>();
                results.Add(type);
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

                // Quick check without having to resolve.
                if (iref.Name.Length < ShortestAspectInterfaceNameLength || !iref.Name.EndsWith(AspectInterfaceSuffix))
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
    }
}
