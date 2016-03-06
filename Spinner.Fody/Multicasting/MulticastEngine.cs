using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Spinner.Extensibility;
using IMtp = Mono.Cecil.IMetadataTokenProvider;

namespace Spinner.Fody.Multicasting
{
    internal class MulticastEngine
    {
        private delegate void ResultHandler(IMtp provider);

        private readonly ModuleWeavingContext _mwc;
        private readonly ModuleDefinition _module;
        private readonly TypeDefinition _compilerGeneratedAttributeType;
        private readonly TypeDefinition _multicastAttributeType;

        // Maps a provider to its derived providers. This can include assemblies, types, and methods.
        private readonly Dictionary<IMtp, List<IMtp>> _derived =
            new Dictionary<IMtp, List<IMtp>>();
        private readonly Dictionary<IMtp, List<IMtp>> _children =
            new Dictionary<IMtp, List<IMtp>>();

        private readonly IReadOnlyList<IMtp> _noProviders = new IMtp[0];

        internal MulticastEngine(ModuleWeavingContext mwc)
        {
            _mwc = mwc;
            _module = mwc.Module;
            _compilerGeneratedAttributeType = mwc.Framework.CompilerGeneratedAttribute;
            _multicastAttributeType = mwc.Spinner.MulticastAttribute;

            var filter = new HashSet<IMtp>();
            ProcessAssembly(_mwc.Module.Assembly, null, filter);

            foreach (List<IMtp> list in _derived.Values)
                list?.TrimExcess();

            foreach (List<IMtp> list in _children.Values)
                list?.TrimExcess();
        }

        /// <summary>
        /// Gets tokens derived from the specified token.
        /// Assembly: The assemblies that reference it.
        /// Class: The classes that inherit it directly.
        /// Interface: The classes and interfaces that implement it directly.
        /// Methods: The methods that override it (from a class) or implement it (from an interface).
        /// MethodReturnType: The method return types of methods that override it.
        /// ParameterType: The matching parameter types of methods that override it.
        /// </summary>
        internal IReadOnlyList<IMtp> GetDerived(IMtp baseToken)
        {
            List<IMtp> result;
            _derived.TryGetValue(baseToken, out result);
            return result ?? _noProviders;
        }

        internal IReadOnlyList<IMtp> GetChildren(IMtp parent)
        {
            List<IMtp> result;
            _children.TryGetValue(parent, out result);
            return result ?? _noProviders;
        }

        internal ICollection<IMtp> GetDescendants(IMtp parent, MulticastArguments args)
        {
            var results = new List<IMtp>();
            GetDescendants(parent, args, results);
            return results;
        }

        internal void GetDescendants(IMtp parent, MulticastArguments args, ICollection<IMtp> results)
        {
            MulticastAttributes memberCompareAttrs = args.IsExternal ? args.TargetExternalMemberAttributes : args.TargetMemberAttributes;

            switch (parent.GetProviderType())
            {
                case ProviderType.Assembly:
                    GetIndirectMulticastTargets(args, (AssemblyDefinition) parent, results);
                    break;
                case ProviderType.Type:
                    GetIndirectMulticastTargets(args, (TypeDefinition) parent, results);
                    break;
                case ProviderType.Method:
                    if (memberCompareAttrs != 0)
                    {
                        var method = (MethodDefinition) parent;
                        if (method.SemanticsAttributes == MethodSemanticsAttributes.None)
                            GetIndirectMulticastTargets(args, method, results);
                    }
                    break;
                case ProviderType.Property:
                    if (memberCompareAttrs != 0)
                        GetIndirectMulticastTargets(args, (PropertyDefinition) parent, results);
                    break;
                case ProviderType.Event:
                    if (memberCompareAttrs != 0)
                        GetIndirectMulticastTargets(args, (EventDefinition) parent, results);
                    break;
                case ProviderType.Field:
                case ProviderType.Parameter:
                case ProviderType.MethodReturn:
                    // None of these have children
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(parent));
            }
        }

        /// <summary>
        /// Gets indirect multicasts for an assembly and its types.
        /// </summary>
        private void GetIndirectMulticastTargets(MulticastArguments args, AssemblyDefinition assembly, ICollection<IMtp> results)
        {
            if (args.TargetElements.IsMatch(MulticastTargets.Assembly) && args.TargetAssemblies.IsMatch(assembly.FullName))
                results.Add(assembly);

            foreach (TypeDefinition type in assembly.MainModule.Types)
                GetIndirectMulticastTargets(args, type, results);
        }

                /// <summary>
        /// Gets indirect multicasts for a type and its members.
        /// </summary>
        private void GetIndirectMulticastTargets(MulticastArguments args, TypeDefinition type, ICollection<IMtp> results)
        {
            const MulticastTargets typeChildTargets = MulticastTargets.AnyMember | MulticastTargets.Parameter | MulticastTargets.ReturnValue;

            const MulticastTargets methodAndChildTargets = MulticastTargets.Method | MulticastTargets.Parameter | MulticastTargets.ReturnValue;

            const MulticastTargets propertyAndChildTargets = MulticastTargets.Property | methodAndChildTargets;

            const MulticastTargets eventAndChildTargets = MulticastTargets.Event | methodAndChildTargets;

            MulticastTargets typeTargetType = type.GetMulticastTargetType();

            if ((args.TargetElements & (typeTargetType | typeChildTargets)) == 0)
                return;

            MulticastAttributes attrs = ComputeMulticastAttributes(type);

            MulticastAttributes compareAttrs = args.IsExternal ? args.TargetExternalTypeAttributes : args.TargetTypeAttributes;

            if ((compareAttrs & attrs) == 0)
                return;

            if (!args.TargetTypes.IsMatch(type.FullName))
                return;

            if ((args.TargetElements & typeTargetType) != 0)
                results.Add(type);

            MulticastAttributes memberCompareAttrs = args.IsExternal ? args.TargetExternalMemberAttributes : args.TargetMemberAttributes;

            // If no members then don't continue.
            if (memberCompareAttrs == 0)
                return;
            
            if (type.HasMethods && (args.TargetElements & methodAndChildTargets) != 0)
            {
                foreach (MethodDefinition method in type.Methods)
                    GetIndirectMulticastTargets(args, method, results);
            }

            if (type.HasProperties && (args.TargetElements & propertyAndChildTargets) != 0)
            {
                foreach (PropertyDefinition property in type.Properties)
                    GetIndirectMulticastTargets(args, property, results);
            }

            if (type.HasEvents && (args.TargetElements & eventAndChildTargets) != 0)
            {
                foreach (EventDefinition evt in type.Events)
                    GetIndirectMulticastTargets(args, evt, results);
            }

            if (type.HasFields && (args.TargetElements & MulticastTargets.Field) != 0)
            {
                foreach (FieldDefinition field in type.Fields)
                    GetIndirectMulticastTargets(args, field, results);
            }

            if (type.HasNestedTypes)
            {
                foreach (TypeDefinition nestedType in type.NestedTypes)
                    GetIndirectMulticastTargets(args, nestedType, results);
            }
        }

        private void GetIndirectMulticastTargets(MulticastArguments args, MethodDefinition method, ICollection<IMtp> results)
        {
            const MulticastTargets childTargets = MulticastTargets.ReturnValue | MulticastTargets.Parameter;

            MulticastTargets methodTargetType = method.GetMulticastTargetType();

            // Stop if the attribute does not apply to this method, the return value, or parameters.
            if ((args.TargetElements & (methodTargetType | childTargets)) == 0)
                return;

            // If member name and attributes check fails, then it cannot apply to return value or parameters
            if (!IsValidMemberAttributes(args, method))
                return;

            // If this is not the child of a property or event, compare the name.
            bool hasParent = method.SemanticsAttributes != MethodSemanticsAttributes.None;
            if (!hasParent && !args.TargetMembers.IsMatch(method.Name))
                return;

            if ((args.TargetElements & methodTargetType) != 0)
                results.Add(method);

            if ((args.TargetElements & MulticastTargets.ReturnValue) != 0 && method.ReturnType != method.Module.TypeSystem.Void)
                results.Add(method.MethodReturnType);

            if (method.HasParameters && (args.TargetElements & MulticastTargets.Parameter) != 0)
            {
                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    MulticastAttributes pattrs = ComputeMulticastAttributes(parameter);

                    if ((args.TargetParameterAttributes & pattrs) != 0 && args.TargetParameters.IsMatch(parameter.Name))
                    {
                        results.Add(parameter);
                    }
                }
            }
        }

        private void GetIndirectMulticastTargets(MulticastArguments args, PropertyDefinition property, ICollection<IMtp> results)
        {
            if ((args.TargetElements & (MulticastTargets.Property | MulticastTargets.Method)) == 0)
                return;

            if (!IsValidMemberAttributes(args, property) || !args.TargetMembers.IsMatch(property.Name))
                return;

            if ((args.TargetElements & MulticastTargets.Property) != 0)
                results.Add(property);

            if ((args.TargetElements & MulticastTargets.Method) != 0)
            {
                if (property.GetMethod != null)
                    GetIndirectMulticastTargets(args, property.GetMethod, results);

                if (property.SetMethod != null)
                    GetIndirectMulticastTargets(args, property.SetMethod, results);
            }
        }

        private void GetIndirectMulticastTargets(MulticastArguments args, EventDefinition evt, ICollection<IMtp> results)
        {
            if ((args.TargetElements & (MulticastTargets.Event | MulticastTargets.Method)) == 0)
                return;

            if (!IsValidMemberAttributes(args, evt) || !args.TargetMembers.IsMatch(evt.Name))
                return;

            if ((args.TargetElements & MulticastTargets.Event) != 0)
                results.Add(evt);

            if ((args.TargetElements & MulticastTargets.Method) != 0)
            {
                if (evt.AddMethod != null)
                    GetIndirectMulticastTargets(args, evt.AddMethod, results);

                if (evt.RemoveMethod != null)
                    GetIndirectMulticastTargets(args, evt.RemoveMethod, results);
            }
        }

        private void GetIndirectMulticastTargets(MulticastArguments args, FieldDefinition field, ICollection<IMtp> results)
        {
            if ((args.TargetElements & MulticastTargets.Field) == 0)
                return;

            if (!IsValidMemberAttributes(args, field) || !args.TargetMembers.IsMatch(field.Name))
                return;

            results.Add(field);
        }

        private bool IsValidMemberAttributes(MulticastArguments mi, MethodDefinition member)
        {
            MulticastAttributes attrs = ComputeMulticastAttributes(member);

            MulticastAttributes compareAttrs = mi.IsExternal ? mi.TargetExternalMemberAttributes : mi.TargetMemberAttributes;
            
            return (compareAttrs & attrs) == attrs;
        }

        private bool IsValidMemberAttributes(MulticastArguments mi, EventDefinition member)
        {
            MulticastAttributes attrs = ComputeMulticastAttributes(member);

            MulticastAttributes compareAttrs = mi.IsExternal ? mi.TargetExternalMemberAttributes : mi.TargetMemberAttributes;

            return (compareAttrs & attrs) == attrs;
        }

        private bool IsValidMemberAttributes(MulticastArguments mi, PropertyDefinition member)
        {
            MulticastAttributes attrs = ComputeMulticastAttributes(member);

            MulticastAttributes compareAttrs = mi.IsExternal ? mi.TargetExternalMemberAttributes : mi.TargetMemberAttributes;

            return (compareAttrs & attrs) == attrs;
        }

        private bool IsValidMemberAttributes(MulticastArguments mi, FieldDefinition member)
        {
            MulticastAttributes attrs = ComputeMulticastAttributes(member);

            MulticastAttributes compareAttrs = mi.IsExternal ? mi.TargetExternalMemberAttributes : mi.TargetMemberAttributes;

            return (compareAttrs & attrs) == attrs;
        }

        private MulticastAttributes ComputeMulticastAttributes(TypeDefinition type)
        {
            MulticastAttributes a = 0;

            a |= type.IsAbstract ? MulticastAttributes.Abstract : MulticastAttributes.NonAbstract;

            a |= type.IsAbstract && type.IsSealed ? MulticastAttributes.Static : MulticastAttributes.Instance;

            if (type.IsPublic || type.IsNestedPublic)
            {
                a |= MulticastAttributes.Public;
            }
            else if (type.IsNestedAssembly || type.IsNotPublic)
            {
                a |= MulticastAttributes.Internal;
            }
            else if (type.IsNestedFamily)
            {
                a |= MulticastAttributes.Protected;
            }
            else if (type.IsNestedFamilyAndAssembly)
            {
                a |= MulticastAttributes.InternalAndProtected;
            }
            else if (type.IsNestedFamilyOrAssembly)
            {
                a |= MulticastAttributes.InternalOrProtected;
            }

            a |= HasGeneratedName(type) || HasGeneratedAttribute(type) ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeMulticastAttributes(IMemberDefinition member)
        {
            switch (member.GetProviderType())
            {
                case ProviderType.Method:
                    return ComputeMulticastAttributes((MethodDefinition) member);
                case ProviderType.Property:
                    return ComputeMulticastAttributes((PropertyDefinition) member);
                case ProviderType.Event:
                    return ComputeMulticastAttributes((EventDefinition) member);
                case ProviderType.Field:
                    return ComputeMulticastAttributes((FieldDefinition) member);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private MulticastAttributes ComputeMulticastAttributes(MethodDefinition method)
        {
            MulticastAttributes a = 0;

            if (method.IsPublic)
                a |= MulticastAttributes.Public;
            else if (method.IsFamilyAndAssembly)
                a |= MulticastAttributes.InternalAndProtected;
            else if (method.IsFamilyOrAssembly)
                a |= MulticastAttributes.InternalOrProtected;
            else if (method.IsAssembly)
                a |= MulticastAttributes.Internal;
            else if (method.IsFamily)
                a |= MulticastAttributes.Protected;
            else if (method.IsPrivate)
                a |= MulticastAttributes.Private;

            a |= method.IsStatic ? MulticastAttributes.Static : MulticastAttributes.Instance;

            a |= method.IsAbstract ? MulticastAttributes.Abstract : MulticastAttributes.NonAbstract;

            a |= method.IsVirtual ? MulticastAttributes.Virtual : MulticastAttributes.NonVirtual;

            a |= method.IsManaged ? MulticastAttributes.Managed : MulticastAttributes.NonManaged;

            a |= HasGeneratedName(method) || HasGeneratedAttribute(method) ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeMulticastAttributes(PropertyDefinition property)
        {
            MulticastAttributes ga = property.GetMethod != null ? ComputeMulticastAttributes(property.GetMethod) : 0;
            MulticastAttributes sa = property.SetMethod != null ? ComputeMulticastAttributes(property.SetMethod) : 0;

            MulticastAttributes a = 0;

            if ((ga & MulticastAttributes.Public) != 0 || (sa & MulticastAttributes.Public) != 0)
                a |= MulticastAttributes.Public;
            else if ((ga & MulticastAttributes.InternalOrProtected) != 0 || (sa & MulticastAttributes.InternalOrProtected) != 0)
                a |= MulticastAttributes.InternalOrProtected;
            else if ((ga & MulticastAttributes.InternalAndProtected) != 0 || (sa & MulticastAttributes.InternalAndProtected) != 0)
                a |= MulticastAttributes.InternalAndProtected;
            else if ((ga & MulticastAttributes.Internal) != 0 || (sa & MulticastAttributes.Internal) != 0)
                a |= MulticastAttributes.Internal;
            else if ((ga & MulticastAttributes.Protected) != 0 || (sa & MulticastAttributes.Protected) != 0)
                a |= MulticastAttributes.Protected;
            else if ((ga & MulticastAttributes.Private) != 0 || (sa & MulticastAttributes.Private) != 0)
                a |= MulticastAttributes.Private;

            a |= ((ga | sa) & MulticastAttributes.Static) != 0 ? MulticastAttributes.Static : MulticastAttributes.Instance;

            a |= ((ga | sa) & MulticastAttributes.Abstract) != 0 ? MulticastAttributes.Abstract : MulticastAttributes.NonAbstract;

            a |= ((ga | sa) & MulticastAttributes.Virtual) != 0 ? MulticastAttributes.Virtual : MulticastAttributes.NonVirtual;

            a |= ((ga | sa) & MulticastAttributes.Managed) != 0 ? MulticastAttributes.Managed : MulticastAttributes.NonManaged;

            a |= HasGeneratedName(property) || ((ga | sa) & MulticastAttributes.CompilerGenerated) != 0 ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeMulticastAttributes(EventDefinition evt)
        {
            Debug.Assert(evt.AddMethod != null);

            return ComputeMulticastAttributes(evt.AddMethod);
        }

        private MulticastAttributes ComputeMulticastAttributes(FieldDefinition field)
        {
            MulticastAttributes a = 0;

            if (field.IsPublic)
                a |= MulticastAttributes.Public;
            else if (field.IsFamilyAndAssembly)
                a |= MulticastAttributes.InternalAndProtected;
            else if (field.IsFamilyOrAssembly)
                a |= MulticastAttributes.InternalOrProtected;
            else if (field.IsAssembly)
                a |= MulticastAttributes.Internal;
            else if (field.IsFamily)
                a |= MulticastAttributes.Protected;
            else if (field.IsPrivate)
                a |= MulticastAttributes.Private;

            a |= field.IsStatic ? MulticastAttributes.Static : MulticastAttributes.Instance;

            a |= field.IsLiteral ? MulticastAttributes.Literal : MulticastAttributes.NonLiteral;

            a |= HasGeneratedName(field) || HasGeneratedAttribute(field) ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeMulticastAttributes(ParameterDefinition parameter)
        {
            MulticastAttributes a = 0;

            if (parameter.IsOut)
                a |= MulticastAttributes.OutParameter;
            else if (parameter.ParameterType.IsByReference)
                a |= MulticastAttributes.RefParameter;
            else
                a |= MulticastAttributes.InParameter;

            return a;
        }

        private bool IsMulticastAttribute(TypeDefinition type)
        {
            TypeReference current = type.BaseType;
            while (current != null)
            {
                if (current.IsSame(_multicastAttributeType))
                    return true;

                current = current.Resolve().BaseType;
            }

            return false;
        }

        private static bool HasGeneratedName(IMemberDefinition def)
        {
            return def.Name.StartsWith("<") || def.Name.StartsWith("CS$");
        }

        private bool HasGeneratedAttribute(ICustomAttributeProvider target)
        {
            if (target.HasCustomAttributes)
            {
                foreach (CustomAttribute a in target.CustomAttributes)
                {
                    if (a.AttributeType.IsSame(_compilerGeneratedAttributeType))
                        return true;
                }
            }

            return false;
        }

        private void ProcessAssembly(AssemblyDefinition assembly, AssemblyDefinition referencer, HashSet<IMtp> filter)
        {
            if (filter.Contains(assembly))
                return;
            filter.Add(assembly);

            if (!IsSpinnerOrReferencesSpinner(assembly))
                return;

            if (referencer != null)
                AddDerivedItem(assembly, referencer);

            foreach (AssemblyNameReference ar in assembly.MainModule.AssemblyReferences)
            {
                if (!IsFrameworkAssemblyReference(ar))
                {
                    AssemblyDefinition a = assembly.MainModule.AssemblyResolver.Resolve(ar);
                    ProcessAssembly(a, assembly, filter);
                }
            }

            foreach (TypeDefinition t in assembly.MainModule.Types)
            {
                AddChildItem(assembly, t);

                ProcessType(t, filter);
            }
        }

        private void ProcessType(TypeDefinition type, HashSet<IMtp> filter)
        {
            if (filter.Contains(type))
                return;
            filter.Add(type);

            if (type.BaseType != null && type.BaseType != type.Module.TypeSystem.Object)
            {
                if (!IsFrameworkReference(type.BaseType))
                {
                    TypeDefinition baseType = type.BaseType.Resolve();

                    ProcessType(baseType, filter);

                    AddDerivedItem(baseType, type);
                }
            }

            if (type.HasInterfaces)
            {
                foreach (TypeReference itr in type.Interfaces)
                {
                    if (!IsFrameworkReference(itr))
                    {
                        TypeDefinition it = itr.Resolve();

                        ProcessType(it, filter);

                        AddDerivedItem(it, type);
                    }
                }
            }

            if (type.HasFields)
            {
                foreach (FieldDefinition f in type.Fields)
                    AddChildItem(type, f);
            }

            if (type.HasProperties)
            {
                foreach (PropertyDefinition p in type.Properties)
                {
                    AddChildItem(type, p);

                    if (p.GetMethod != null)
                    {
                        AddChildItem(p, p.GetMethod);
                        ProcessMethod(p.GetMethod, filter);
                    }
                    if (p.SetMethod != null)
                    {
                        AddChildItem(p, p.SetMethod);
                        ProcessMethod(p.SetMethod, filter);
                    }
                }
            }

            if (type.HasEvents)
            {
                foreach (EventDefinition e in type.Events)
                {
                    AddChildItem(type, e);

                    if (e.AddMethod != null)
                        AddChildItem(e, e.AddMethod);
                    if (e.RemoveMethod != null)
                        AddChildItem(e, e.RemoveMethod);
                }
            }

            if (type.HasMethods)
            {
                foreach (MethodDefinition m in type.Methods)
                {
                    if (m.SemanticsAttributes != MethodSemanticsAttributes.None)
                        continue;

                    AddChildItem(type, m);

                    ProcessMethod(m, filter);
                }
            }

            if (type.HasNestedTypes)
            {
                foreach (TypeDefinition nt in type.NestedTypes)
                {
                    AddChildItem(type, nt);

                    ProcessType(nt, filter);
                }
            }
        }

        private void ProcessMethod(MethodDefinition method, HashSet<IMtp> filter)
        {
            if (filter.Contains(method))
                return;
            filter.Add(method);

            if (method.ReturnType != method.Module.TypeSystem.Void)
                AddChildItem(method, method.MethodReturnType);

            if (method.HasParameters)
            {
                foreach (ParameterDefinition p in method.Parameters)
                    AddChildItem(method, p);
            }

            List<MethodDefinition> overrides = GetOverrides(method);

            if (overrides != null)
            {
                bool returnsVoid = method.ReturnType == method.Module.TypeSystem.Void;

                foreach (MethodDefinition ov in overrides)
                {
                    AddDerivedItem(ov, method);

                    if (!returnsVoid)
                        AddDerivedItem(ov.MethodReturnType, method.MethodReturnType);

                    if (method.HasParameters)
                    {
                        for (int i = 0; i < method.Parameters.Count; i++)
                            AddDerivedItem(ov.Parameters[i], method.Parameters[i]);
                    }
                }

                overrides.Clear();
            }
        }

        private void AddDerivedItem(IMtp baseType, IMtp derivedType)
        {
            List<IMtp> derivedTypes;
            if (!_derived.TryGetValue(baseType, out derivedTypes) || derivedTypes == null)
            {
                derivedTypes = new List<IMtp>();
                _derived[baseType] = derivedTypes;
            }

            derivedTypes.Add(derivedType);
        }

        private void AddChildItem(IMtp parent, IMtp child)
        {
            List<IMtp> children;
            if (!_children.TryGetValue(parent, out children) || children == null)
            {
                children = new List<IMtp>();
                _children[parent] = children;
            }

            children.Add(child);
        }

        private static List<MethodDefinition> GetOverrides(MethodDefinition m)
        {
            List<MethodDefinition> results = null;

            TypeDefinition type = m.DeclaringType;

            if (type.BaseType != null && type.BaseType != type.Module.TypeSystem.Object)
            {
                if (!IsFrameworkReference(type.BaseType))
                {
                    MethodDefinition ovr = type.BaseType.Resolve().GetMethod(m, false);
                    if (ovr != null)
                    {
                        results = new List<MethodDefinition> {ovr};
                    }
                }
            }

            if (type.HasInterfaces)
            {
                foreach (TypeReference ir in type.Interfaces)
                {
                    if (IsFrameworkReference(ir))
                        continue;

                    MethodDefinition ovr = ir.Resolve().GetMethod(m, false);

                    if (ovr == null)
                        continue;

                    if (results == null)
                        results = new List<MethodDefinition>();
                    results.Add(ovr);
                }
            }

            return results;
        }

        private static bool IsFrameworkReference(TypeReference type)
        {
            IMetadataScope scope = type.Scope;
            return scope.MetadataScopeType == MetadataScopeType.AssemblyNameReference && IsFrameworkAssemblyReference(scope.Name);
        }

        private static bool IsFrameworkAssemblyReference(AssemblyNameReference ar)
        {
            return IsFrameworkAssemblyReference(ar.Name);
        }

        private static bool IsFrameworkAssemblyReference(string name)
        {
            return name.StartsWith("System.") || name == "System" || name == "mscorlib";
        }

        private static bool IsSpinnerOrReferencesSpinner(AssemblyDefinition assembly)
        {
            return assembly.Name.Name == "Spinner" || assembly.MainModule.AssemblyReferences.Any(ar => ar.Name == "Spinner");
        }
    }
}