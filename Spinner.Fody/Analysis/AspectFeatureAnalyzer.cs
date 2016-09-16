using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NLog;
using Spinner.Aspects;
using Spinner.Fody.Utilities;

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

        private static readonly Logger s_log = LogManager.GetCurrentClassLogger();

        private readonly SpinnerContext _context;
        private readonly TypeDefinition _type;
        private readonly LockTargetProvider<TypeDefinition> _ltp;
        private readonly AspectKind _aspectKind;
        private readonly TypeDefinition[] _inheritanceList;
        private readonly Dictionary<MethodDefinition, Features> _inheritedMethodFeatures =
            new Dictionary<MethodDefinition, Features>();
        private Features _inheritedTypeFeatures;

        private AspectFeatureAnalyzer(
            SpinnerContext context,
            TypeDefinition type,
            LockTargetProvider<TypeDefinition> ltp,
            AspectKind aspectKind,
            TypeDefinition[] inheritanceList)
        {
            _context = context;
            _type = type;
            _ltp = ltp;
            _aspectKind = aspectKind;
            _inheritanceList = inheritanceList;

            Debug.Assert(inheritanceList.Last() == type);
        }

        /// <summary>
        /// Checks basic properties of the type to determine if it could potentially be an aspect type and requires
        /// further inspection of the inheritence list.
        /// </summary>
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
        internal static void Analyze(SpinnerContext context, TypeDefinition type, LockTargetProvider<TypeDefinition> ltp)
        {
            // Aspects can only be types that are valid as attributes.
            Debug.Assert(IsMaybeAspect(type), "this should be checked before starting analysis");

            // Identify the fundamental aspect kind and inheritance list
            AspectKind? kind = null;
            List<TypeDefinition> inheritanceList = null;
            GetAspectInfo(context, type, ref kind, ref inheritanceList);

            // The type might not actually be an aspect.
            if (kind.HasValue)
                new AspectFeatureAnalyzer(context, type, ltp, kind.Value, inheritanceList.ToArray()).Analyze();
        }

        private void Analyze()
        {
            foreach (TypeDefinition type in _inheritanceList)
            {
                if (_context.Spinner.IsEmptyAdviceBase(type) || !type.HasMethods)
                    continue;

                // Do not analyze the same type more than once.
                lock (_ltp.Get(type))
                {
                    AnalyzeType(type);
                }
            }
        }

        private void AnalyzeType(TypeDefinition type)
        {
            // A lock is held on the type
            if (HasAttribute(type, _context.Spinner.AnalyzedFeaturesAttribute))
                return;

            s_log.Debug("Analyzing features for aspect type {0}", type.Name);

            foreach (MethodDefinition m in type.Methods)
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

                    MethodDefinition baseMethod = type.BaseType?.Resolve().GetMethod(m, true);

                    Features methodFeatures = Features.None;
                    if (baseMethod != null)
                        _inheritedMethodFeatures.TryGetValue(baseMethod, out methodFeatures);

                    methodFeatures |= AnalyzeAdvice(m);

                    _inheritedMethodFeatures[m] = methodFeatures;

                    AddAnalyzedFeaturesAttribute(m, methodFeatures);

                    _inheritedTypeFeatures |= methodFeatures;
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
                    _inheritedMethodFeatures.TryGetValue(baseDefinition, out methodFeatures);

                    methodFeatures |= AnalyzeAdvice(m);

                    _inheritedMethodFeatures[baseDefinition] = methodFeatures;

                    AddAnalyzedFeaturesAttribute(m, methodFeatures);

                    // Add type and method level features that this advice uses.
                    _inheritedTypeFeatures |= typeFeatures | methodFeatures;
                }
            }

            AddAnalyzedFeaturesAttribute(type, _inheritedTypeFeatures);

            Debug.Assert(HasAttribute(type, _context.Spinner.AnalyzedFeaturesAttribute));
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
                if (_context.MethodFeatures.TryGetValue(mr.Resolve(), out mf))
                    features |= mf;
            }

            return features;
        }

        /// <summary>
        /// Determines the kind of aspect the type implements and the list of inherited classes containing advices.
        /// </summary>
        private static void GetAspectInfo(
            SpinnerContext context,
            TypeDefinition type,
            ref AspectKind? ak,
            ref List<TypeDefinition> inheritanceList)
        {
            // Recursively invoke this on bases first
            TypeDefinition baseType = type.BaseType?.Resolve();
            if (baseType != null && !context.Spinner.IsEmptyAdviceBase(type) && baseType != context.Framework.Attribute)
                GetAspectInfo(context, baseType, ref ak, ref inheritanceList);

            // Try to determine the aspect kind from the current type
            if (!ak.HasValue)
                ak = context.GetAspectKind(type, false);

            // If aspect kind was determined by current type or a base type, add it to the list.
            if (ak.HasValue)
            {
                if (inheritanceList == null)
                    inheritanceList = new List<TypeDefinition>();
                inheritanceList.Add(type);
            }
        }

        /// <summary>
        /// Add the AnalyzedFeaturesAttribute() to a target type or method.
        /// </summary>
        private void AddAnalyzedFeaturesAttribute(
            ICustomAttributeProvider target,
            Features features)
        {
            var attr = new CustomAttribute(_context.Import(_context.Spinner.AnalyzedFeaturesAttribute_ctor));
            attr.ConstructorArguments.Add(new CustomAttributeArgument(_context.Spinner.Features, (uint) features));
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
                    baseType = _context.Spinner.IMethodBoundaryAspect;
                    break;
                case AspectKind.MethodInterception:
                    baseType = _context.Spinner.IMethodInterceptionAspect;
                    break;
                case AspectKind.PropertyInterception:
                    baseType = _context.Spinner.ILocationInterceptionAspect;
                    break;
                case AspectKind.EventInterception:
                    baseType = _context.Spinner.IEventInterceptionAspect;
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

            typeFeature = _context.TypeFeatures[baseMethod];
            return baseMethod;
        }

        private AdviceType? GetMethodAdviceType(MethodDefinition method)
        {
            if (method.HasCustomAttributes)
            {
                foreach (CustomAttribute ca in method.CustomAttributes)
                {
                    AdviceType adviceType;
                    if (_context.AdviceTypes.TryGetValue(ca.AttributeType, out adviceType))
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
