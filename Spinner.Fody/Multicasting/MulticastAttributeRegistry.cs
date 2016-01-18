using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// 
    /// </summary>
    internal class MulticastAttributeRegistry
    {
        private delegate void ResultHandler(MulticastInstance mi, ICustomAttributeProvider provider);

        private static readonly IList<MulticastInstance> s_noInstances = new MulticastInstance[0];

        private static readonly Dictionary<TokenType, ProviderType> s_providerTypes =
            new Dictionary<TokenType, ProviderType>
            {
                {TokenType.Assembly, ProviderType.Assembly},
                {TokenType.TypeDef, ProviderType.Type},
                {TokenType.Method, ProviderType.Method},
                {TokenType.Property, ProviderType.Property},
                {TokenType.Event, ProviderType.Event},
                {TokenType.Field, ProviderType.Field},
                {TokenType.Param, ProviderType.Parameter}
            };

        private readonly ModuleWeavingContext _mwc;
        private readonly HashSet<AssemblyDefinition> _instantiatedAssemblies = new HashSet<AssemblyDefinition>();
        private readonly HashSet<AssemblyDefinition> _derivedAssemblies = new HashSet<AssemblyDefinition>();

        private readonly Dictionary<ICustomAttributeProvider, List<MulticastInstance>> _targets =
            new Dictionary<ICustomAttributeProvider, List<MulticastInstance>>();

        private Dictionary<ICustomAttributeProvider, List<ICustomAttributeProvider>> _derived =
            new Dictionary<ICustomAttributeProvider, List<ICustomAttributeProvider>>();

        private Dictionary<ICustomAttributeProvider, Tuple<int, List<MulticastInstance>>> _instances =
            new Dictionary<ICustomAttributeProvider, Tuple<int, List<MulticastInstance>>>();

        private int _initOrderCounter;

        internal MulticastAttributeRegistry(ModuleWeavingContext mwc)
        {
            _mwc = mwc;
        }

        internal IList<MulticastInstance> GetMulticasts(ICustomAttributeProvider provider)
        {
            List<MulticastInstance> multicasts;
            // ReSharper disable once InconsistentlySynchronizedField
            return _targets.TryGetValue(provider, out multicasts) ? multicasts : s_noInstances;
        }

        internal void IntantiateAndProcessMulticasts()
        {
            // Creates multicast attribute instances for all types in assembly and referenced assemblies. Also for
            // types that have multicast 
            InstantiateMulticasts(_mwc.Module.Assembly, null);

            // Identify all types and assemblies that provide multicast attributes
            AddDerivedProviders(_mwc.Module.Assembly, null);

            // Create new instances where inheritance is allowed
            InheritMulticasts();

            // Apply multicasts in the order the instances were created
            var initLists = _instances.Values.ToList();
            initLists.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            foreach (Tuple<int, List<MulticastInstance>> group in initLists)
                ProcessMulticastList(group.Item2);

            // No longer need this data
            _derived = null;
            _instances = null;
        }

        private void InheritMulticasts()
        {
            foreach (KeyValuePair<ICustomAttributeProvider, Tuple<int, List<MulticastInstance>>> item in _instances.ToList())
            {
                List<ICustomAttributeProvider> derivedList;
                if (!_derived.TryGetValue(item.Key, out derivedList))
                    continue;

                foreach (MulticastInstance mi in item.Value.Item2)
                {
                    if (mi.Inheritance == MulticastInheritance.None)
                        continue;

                    foreach (ICustomAttributeProvider d in derivedList)
                    {
                        AddInstance(d, mi.WithTarget(d));
                    }
                }
            }
        }

        private void AddDerivedProviders(AssemblyDefinition assembly, AssemblyDefinition referencer)
        {
            if (_derivedAssemblies.Contains(assembly))
                return;
            _derivedAssemblies.Add(assembly);

            if (!IsSpinnerOrReferencesSpinner(assembly))
                return;

            if (referencer != null)
                TryAddDerivedProvider(assembly, referencer);
            
            foreach (AssemblyNameReference ar in assembly.MainModule.AssemblyReferences)
            {
                if (!IsFrameworkAssemblyReference(ar))
                    AddDerivedProviders(assembly.MainModule.AssemblyResolver.Resolve(ar), assembly);
            }
            
            foreach (TypeDefinition t in assembly.MainModule.Types)
            {
                AddDerivedProviders(t);
            }
        }

        private void AddDerivedProviders(TypeDefinition type)
        {
            if (type.BaseType != null)
                TryAddDerivedProvider(type.BaseType.Resolve(), type);

            if (type.HasInterfaces)
            {
                foreach (TypeReference itr in type.Interfaces)
                {
                    TryAddDerivedProvider(itr.Resolve(), type);
                }
            }

            if (type.HasMethods)
            {
                foreach (MethodDefinition m in type.Methods)
                {
                    if (m.HasOverrides)
                    {
                        foreach (MethodReference ovr in m.Overrides)
                        {
                            MethodDefinition ov = ovr.Resolve();

                            TryAddDerivedProvider(ov, m);
                            TryAddDerivedProvider(ov.MethodReturnType, m.MethodReturnType);

                            if (m.HasParameters)
                            {
                                for (int i = 0; i < m.Parameters.Count; i++)
                                    TryAddDerivedProvider(ov.Parameters[i], m.Parameters[i]);
                            }
                        }
                    }
                }
            }
        }

        private void InstantiateMulticasts(AssemblyDefinition assembly, AssemblyDefinition referencer)
        {
            if (_instantiatedAssemblies.Contains(assembly))
                return;
            _instantiatedAssemblies.Add(assembly);

            if (!IsSpinnerOrReferencesSpinner(assembly))
                return;

            foreach (AssemblyNameReference ar in assembly.MainModule.AssemblyReferences)
            {
                // Skip some of the big ones
                if (IsFrameworkAssemblyReference(ar))
                    continue;

                AssemblyDefinition referencedAssembly = assembly.MainModule.AssemblyResolver.Resolve(ar);

                InstantiateMulticasts(referencedAssembly, assembly);
            }

            if (assembly.HasCustomAttributes)
            {
                var attrs = new List<MulticastInstance>();
                bool hasMulticasts = InstantiateMulticasts(assembly, ProviderType.Assembly);
                if (hasMulticasts && referencer != null)
                    TryAddDerivedProvider(assembly, null);
                ProcessMulticastList(attrs);
            }
            
            foreach (TypeDefinition t in assembly.MainModule.Types)
            {
                if (!HasGeneratedName(t) && !HasGeneratedAttribute(t))
                {
                    InstantiateMulticasts(t);
                }
            }
        }

        private void InstantiateMulticasts(TypeDefinition type)
        {
            bool hasMulticasts = false;
            if (type.HasCustomAttributes)
                hasMulticasts |= InstantiateMulticasts(type, ProviderType.Type);

            //TypeDefinition currentType = type;
            //while (currentType != null)
            //{
            //    if (currentType.HasCustomAttributes)
            //        GetMulticasts(type, ProviderType.Type, instances.Add, currentType, ProviderType.Type);

            //    if (currentType.HasInterfaces)
            //    {
            //        foreach (TypeReference itr in currentType.Interfaces)
            //        {
            //            TypeDefinition it = itr.Resolve();
            //            if (it.HasCustomAttributes)
            //                GetMulticasts(type, ProviderType.Type, instances.Add, it, ProviderType.Type);
            //        }
            //    }

            //    // TODO: Bases first
            //    currentType = currentType.BaseType?.Resolve();
            //}
            
            //OrderAndProcessAndClearMulticasts(instances);

            if (type.HasMethods)
            {
                foreach (MethodDefinition m in type.Methods)
                {
                    // getters, setters, and event adders and removers are handled by their owning property/event
                    if (m.SemanticsAttributes != MethodSemanticsAttributes.None)
                        continue;

                    if (!HasGeneratedName(m) && m.HasCustomAttributes)
                        hasMulticasts |= InstantiateMulticasts(m, ProviderType.Method);

                    if (m.HasParameters)
                    {
                        foreach (ParameterDefinition p in m.Parameters)
                        {
                            if (p.HasCustomAttributes)
                                hasMulticasts |= InstantiateMulticasts(p, ProviderType.Parameter);
                        }
                    }

                    if (m.MethodReturnType.HasCustomAttributes && !m.ReturnType.IsSimilar(m.Module.TypeSystem.Void))
                        hasMulticasts |= InstantiateMulticasts(m.MethodReturnType, ProviderType.MethodReturn);
                }
            }

            if (type.HasProperties)
            {
                foreach (PropertyDefinition p in type.Properties)
                {
                    if (!HasGeneratedName(p) && p.HasCustomAttributes)
                        hasMulticasts |= InstantiateMulticasts(p, ProviderType.Property);

                    if (p.GetMethod != null)
                        hasMulticasts |= InstantiateMulticasts(p.GetMethod, ProviderType.Method);

                    if (p.SetMethod != null)
                        hasMulticasts |= InstantiateMulticasts(p.SetMethod, ProviderType.Method);
                }
            }

            if (type.HasEvents)
            {
                foreach (EventDefinition e in type.Events)
                {
                    if (!HasGeneratedName(e) && e.HasCustomAttributes)
                        hasMulticasts |= InstantiateMulticasts(e, ProviderType.Event);

                    if (e.AddMethod != null)
                        hasMulticasts |= InstantiateMulticasts(e.AddMethod, ProviderType.Method);

                    if (e.RemoveMethod != null)
                        hasMulticasts |= InstantiateMulticasts(e.RemoveMethod, ProviderType.Method);
                }
            }

            if (type.HasFields)
            {
                foreach (FieldDefinition f in type.Fields)
                {
                    if (!HasGeneratedName(f) && f.HasCustomAttributes)
                        hasMulticasts |= InstantiateMulticasts(f, ProviderType.Field);
                }
            }

            if (type.HasNestedTypes)
            {
                foreach (TypeDefinition nt in type.NestedTypes)
                {
                    if (!HasGeneratedName(nt) && !HasGeneratedAttribute(nt))
                        InstantiateMulticasts(nt);
                }
            }

            if (!hasMulticasts)
                return;

            //AddDerivedProvider(type, null);
        }

        private void AddBaseProvider(ICustomAttributeProvider baseType)
        {
            List<ICustomAttributeProvider> derivedTypes;
            if (!_derived.TryGetValue(baseType, out derivedTypes))
            {
                derivedTypes = new List<ICustomAttributeProvider>();
                _derived.Add(baseType, derivedTypes);
            }
        }

        private void TryAddDerivedProvider(ICustomAttributeProvider baseType, ICustomAttributeProvider derivedType)
        {
            Debug.Assert(derivedType != null);

            List<ICustomAttributeProvider> derivedTypes;
            if (_derived.TryGetValue(baseType, out derivedTypes))
                derivedTypes.Add(derivedType);
        }

        /// <summary>
        /// Process a multicast instance list by ordering them, applying exclusion rules, and removing instances
        /// inherited from the same origin.
        /// </summary>
        /// <param name="instances"></param>
        private void ProcessMulticastList(List<MulticastInstance> instances)
        {
            if (instances == null || instances.Count == 0)
                return;

            if (instances.Count > 1)
            {
                // Remove duplicates inherited from same origin
                instances = instances.Distinct().ToList();

                // Sort by priority
                instances.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }

            // Apply exclusions
            for (int i = 0; i < instances.Count; i++)
            {
                MulticastInstance a = instances[i];

                if (a.Exclude)
                {
                    for (int r = instances.Count - 1; r > -1; r--)
                    {
                        if (instances[r].AttributeType == a.AttributeType)
                            instances.RemoveAt(r);
                    }
                    i = -1; // restart from beginning
                }
            }

            foreach (MulticastInstance mi in instances)
                ProcessMulticasts(mi);
        }

        private bool InstantiateMulticasts(ICustomAttributeProvider origin, ProviderType originType)
        {
            List<MulticastInstance> results = null;

            foreach (CustomAttribute a in origin.CustomAttributes)
            {
                TypeDefinition atype = a.AttributeType.Resolve();

                if (atype == _mwc.Framework.CompilerGeneratedAttribute)
                    break;

                if (!IsMulticastAttribute(atype))
                    continue;

                var mai = new MulticastInstance(origin, originType, a, atype);

                if (results == null)
                    results = new List<MulticastInstance>();
                results.Add(mai);
            }

            if (results == null)
                return false;

            int initOrder = _initOrderCounter++;

            AddBaseProvider(origin);
            _instances.Add(origin, Tuple.Create(initOrder, results));
            return true;
        }

        private void AddInstance(ICustomAttributeProvider target, MulticastInstance mi)
        {
            Tuple<int, List<MulticastInstance>> mis;
            if (!_instances.TryGetValue(target, out mis))
            {
                int initOrder = _initOrderCounter++;
                mis = Tuple.Create(initOrder, new List<MulticastInstance>());
                _instances.Add(target, mis);
            }
            mis.Item2.Add(mi);
        }

        private void ProcessMulticasts(MulticastInstance mi)
        {
            bool external = mi.Attribute.Constructor.Module != _mwc.Module;

            var results = new List<Tuple<MulticastInstance, ICustomAttributeProvider>>();

            ResultHandler resultHandler = (i, p) => results.Add(Tuple.Create(i, p));
            
            MulticastAttributes memberCompareAttrs = external
                ? mi.TargetExternalMemberAttributes
                : mi.TargetMemberAttributes;

            switch (mi.TargetType)
            {
                case ProviderType.Assembly:
                    ProcessIndirectMulticasts(mi, (AssemblyDefinition) mi.Target, resultHandler);
                    break;
                case ProviderType.Type:
                    ProcessIndirectMulticasts(mi, (TypeDefinition) mi.Target, resultHandler);
                    break;
                case ProviderType.Method:
                    if (memberCompareAttrs != 0)
                    {
                        var method = (MethodDefinition) mi.Target;
                        if (method.SemanticsAttributes == MethodSemanticsAttributes.None)
                            ProcessIndirectMulticasts(mi, method, resultHandler);
                    }
                    break;
                case ProviderType.Property:
                    if (memberCompareAttrs != 0)
                        ProcessIndirectMulticasts(mi, (PropertyDefinition) mi.Target, resultHandler);
                    break;
                case ProviderType.Event:
                    if (memberCompareAttrs != 0)
                        ProcessIndirectMulticasts(mi, (EventDefinition) mi.Target, resultHandler);
                    break;
                case ProviderType.Field:
                    if (memberCompareAttrs != 0)
                        ProcessIndirectMulticasts(mi, (FieldDefinition) mi.Target, resultHandler);
                    break;
                case ProviderType.Parameter:
                case ProviderType.MethodReturn:
                    // these are handled by their parent method
                    break;
            }

            if ((mi.TargetElements & GetTargetType(mi.Target)) != 0)
                resultHandler(mi, mi.Target);

            if (results.Count == 0)
                return;

            lock (_targets)
            {
                foreach (Tuple<MulticastInstance, ICustomAttributeProvider> item in results)
                {
                    List<MulticastInstance> current;
                    if (!_targets.TryGetValue(item.Item2, out current))
                    {
                        current = new List<MulticastInstance>();
                        _targets.Add(item.Item2, current);
                    }
                    current.Add(item.Item1);
                }
            }
        }

        private void ProcessIndirectMulticasts(MulticastInstance mi, AssemblyDefinition assembly, ResultHandler handler)
        {
            if ((mi.TargetElements & MulticastTargets.Assembly) != 0 && mi.TargetAssemblies.IsMatch(assembly.FullName))
                handler(mi, assembly);

            foreach (TypeDefinition type in assembly.MainModule.Types)
                ProcessIndirectMulticasts(mi, type, handler);
        }

        private void ProcessIndirectMulticasts(MulticastInstance mi, TypeDefinition type, ResultHandler handler)
        {
            const MulticastTargets typeChildTargets =
                MulticastTargets.AnyMember | MulticastTargets.Parameter | MulticastTargets.ReturnValue;

            const MulticastTargets methodAndChildTargets =
                MulticastTargets.Method | MulticastTargets.Parameter | MulticastTargets.ReturnValue;

            const MulticastTargets propertyAndChildTargets =
                MulticastTargets.Property | methodAndChildTargets;

            const MulticastTargets eventAndChildTargets =
                MulticastTargets.Event | methodAndChildTargets;

            MulticastTargets typeTargetType = GetTargetType(type);

            if ((mi.TargetElements & (typeTargetType | typeChildTargets)) == 0)
                return;

            MulticastAttributes attrs = ComputeAttributes(type);

            bool external = mi.Attribute.Constructor.Module != _mwc.Module;

            MulticastAttributes compareAttrs = external
                ? mi.TargetExternalTypeAttributes
                : mi.TargetTypeAttributes;

            if ((compareAttrs & attrs) == 0)
                return;

            if (!mi.TargetTypes.IsMatch(type.FullName))
                return;

            if ((mi.TargetElements & typeTargetType) != 0)
                handler(mi, type);

            MulticastAttributes memberCompareAttrs = external
                ? mi.TargetExternalMemberAttributes
                : mi.TargetMemberAttributes;

            // If no members then don't continue.
            if (memberCompareAttrs == 0)
                return;

            if (type.HasMethods && (mi.TargetElements & methodAndChildTargets) != 0)
            {
                foreach (MethodDefinition method in type.Methods)
                    ProcessIndirectMulticasts(mi, method, handler);
            }

            if (type.HasProperties && (mi.TargetElements & propertyAndChildTargets) != 0)
            {
                foreach (PropertyDefinition property in type.Properties)
                    ProcessIndirectMulticasts(mi, property, handler);
            }

            if (type.HasEvents && (mi.TargetElements & eventAndChildTargets) != 0)
            {
                foreach (EventDefinition evt in type.Events)
                    ProcessIndirectMulticasts(mi, evt, handler);
            }

            if (type.HasFields && (mi.TargetElements & MulticastTargets.Field) != 0)
            {
                foreach (FieldDefinition field in type.Fields)
                    ProcessIndirectMulticasts(mi, field, handler);
            }

            if (type.HasNestedTypes)
            {
                foreach (TypeDefinition nestedType in type.NestedTypes)
                    ProcessIndirectMulticasts(mi, nestedType, handler);
            }
        }

        private void ProcessIndirectMulticasts(MulticastInstance mi, MethodDefinition method, ResultHandler handler)
        {
            const MulticastTargets childTargets = MulticastTargets.ReturnValue | MulticastTargets.Parameter;

            MulticastTargets methodTargetType = GetTargetType(method);

            // Stop if the attribute does not apply to this method, the return value, or parameters.
            if ((mi.TargetElements & (methodTargetType | childTargets)) == 0)
                return;

            // If member name and attributes check fails, then it cannot apply to return value or parameters
            if (!IsValidMemberAttributes(mi, method))
                return;

            // If this is not the child of a property or event, compare the name.
            bool hasParent = method.SemanticsAttributes != MethodSemanticsAttributes.None;
            if (!hasParent && !mi.TargetMembers.IsMatch(method.Name))
                return;

            if ((mi.TargetElements & methodTargetType) != 0)
                handler(mi, method);

            if ((mi.TargetElements & MulticastTargets.ReturnValue) != 0)
                handler(mi, method.MethodReturnType);

            if (method.HasParameters && (mi.TargetElements & MulticastTargets.Parameter) != 0)
            {
                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    MulticastAttributes pattrs = ComputeAttributes(parameter);

                    if ((mi.TargetParameterAttributes & pattrs) != 0 && mi.TargetParameters.IsMatch(parameter.Name))
                    {
                        handler(mi, parameter);
                    }
                }
            }
        }

        private void ProcessIndirectMulticasts(MulticastInstance mi, PropertyDefinition property, ResultHandler handler)
        {
            if ((mi.TargetElements & (MulticastTargets.Property | MulticastTargets.Method)) == 0)
                return;

            if (!IsValidMemberAttributes(mi, property) || !mi.TargetMembers.IsMatch(property.Name))
                return;

            if ((mi.TargetElements & MulticastTargets.Property) != 0)
                handler(mi, property);

            if ((mi.TargetElements & MulticastTargets.Method) != 0)
            {
                if (property.GetMethod != null)
                    ProcessIndirectMulticasts(mi, property.GetMethod, handler);

                if (property.SetMethod != null)
                    ProcessIndirectMulticasts(mi, property.SetMethod, handler);
            }
        }

        private void ProcessIndirectMulticasts(MulticastInstance mi, EventDefinition evt, ResultHandler handler)
        {
            if ((mi.TargetElements & (MulticastTargets.Event | MulticastTargets.Method)) == 0)
                return;

            if (!IsValidMemberAttributes(mi, evt) || !mi.TargetMembers.IsMatch(evt.Name))
                return;

            if ((mi.TargetElements & MulticastTargets.Event) != 0)
                handler(mi, evt);

            if ((mi.TargetElements & MulticastTargets.Method) != 0)
            {
                if (evt.AddMethod != null)
                    ProcessIndirectMulticasts(mi, evt.AddMethod, handler);

                if (evt.RemoveMethod != null)
                    ProcessIndirectMulticasts(mi, evt.RemoveMethod, handler);
            }
        }

        private void ProcessIndirectMulticasts(MulticastInstance mi, FieldDefinition field, ResultHandler handler)
        {
            if ((mi.TargetElements & MulticastTargets.Field) == 0)
                return;

            if (!IsValidMemberAttributes(mi, field) || !mi.TargetMembers.IsMatch(field.Name))
                return;

            handler(mi, field);
        }

        private bool IsValidMemberAttributes(MulticastInstance mi, IMemberDefinition member)
        {
            MulticastAttributes attrs = ComputeAttributes(member);

            bool external = mi.Attribute.Constructor.Module != _mwc.Module;

            MulticastAttributes compareAttrs = external
                ? mi.TargetExternalMemberAttributes
                : mi.TargetMemberAttributes;

            return (compareAttrs & attrs) != 0;
        }

        private MulticastAttributes ComputeAttributes(TypeDefinition type)
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

        private MulticastAttributes ComputeAttributes(IMemberDefinition member)
        {
            switch (GetProviderType(member))
            {
                case ProviderType.Method:
                    return ComputeAttributes((MethodDefinition) member);
                case ProviderType.Property:
                    return ComputeAttributes((PropertyDefinition) member);
                case ProviderType.Event:
                    return ComputeAttributes((EventDefinition) member);
                case ProviderType.Field:
                    return ComputeAttributes((FieldDefinition) member);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private MulticastAttributes ComputeAttributes(MethodDefinition method)
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

        private MulticastAttributes ComputeAttributes(PropertyDefinition property)
        {
            MulticastAttributes ga = property.GetMethod != null ? ComputeAttributes(property.GetMethod) : 0;
            MulticastAttributes sa = property.SetMethod != null ? ComputeAttributes(property.SetMethod) : 0;

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

        private MulticastAttributes ComputeAttributes(EventDefinition evt)
        {
            Debug.Assert(evt.AddMethod != null);

            return ComputeAttributes(evt.AddMethod);
        }

        private MulticastAttributes ComputeAttributes(FieldDefinition field)
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

        private MulticastAttributes ComputeAttributes(ParameterDefinition parameter)
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
            var m = _mwc.Spinner.MulticastAttribute;

            TypeReference current = type.BaseType;
            while (current != null)
            {
                if (current.IsSimilar(m))
                    return true;

                current = current.Resolve().BaseType;
            }

            return false;
        }

        private static bool HasGeneratedName(IMemberDefinition def)
        {
            return def.Name.StartsWith("<");
        }

        private bool HasGeneratedAttribute(ICustomAttributeProvider target)
        {
            if (!target.HasCustomAttributes)
                return false;

            foreach (CustomAttribute a in target.CustomAttributes)
            {
                if (a.AttributeType.Resolve() == _mwc.Framework.CompilerGeneratedAttribute)
                    return true;
            }

            return false;
        }

        private static ProviderType GetProviderType(IMetadataTokenProvider target)
        {
            if (target is MethodReturnType)
                return ProviderType.MethodReturn;
            return s_providerTypes[target.MetadataToken.TokenType];
        }

        private static string GetName(ICustomAttributeProvider target)
        {
            switch (GetProviderType(target))
            {
                case ProviderType.Assembly:
                    return ((AssemblyDefinition) target).FullName;
                case ProviderType.Type:
                    return ((TypeDefinition) target).FullName;
                case ProviderType.Method:
                case ProviderType.Property:
                case ProviderType.Event:
                case ProviderType.Field:
                    return ((IMemberDefinition) target).Name;
                case ProviderType.Parameter:
                    return ((ParameterDefinition) target).Name;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static MulticastTargets GetTargetType(IMetadataTokenProvider target)
        {
            switch (GetProviderType(target))
            {
                case ProviderType.Assembly:
                    return MulticastTargets.Assembly;
                case ProviderType.Type:
                    var type = (TypeDefinition) target;

                    if (type.IsInterface)
                        return MulticastTargets.Interface;

                    if (type.BaseType?.Namespace == "System")
                    {
                        switch (type.BaseType.Name)
                        {
                            case "Enum":
                                return MulticastTargets.Enum;
                            case "ValueType":
                                return MulticastTargets.Struct;
                            case "Delegate":
                                return MulticastTargets.Delegate;
                        }
                    }

                    if (type.IsClass)
                        return MulticastTargets.Class;

                    throw new ArgumentOutOfRangeException(nameof(target));
                case ProviderType.Method:
                    var method = (MethodDefinition) target;
                    if (method != null)
                    {
                        switch (method.Name)
                        {
                            case ".ctor":
                                return MulticastTargets.InstanceConstructor;
                            case ".cctor":
                                return MulticastTargets.StaticConstructor;
                            default:
                                return MulticastTargets.Method;
                        }
                    }
                    break;
                case ProviderType.Property:
                    return MulticastTargets.Property;
                case ProviderType.Event:
                    return MulticastTargets.Event;
                case ProviderType.Field:
                    return MulticastTargets.Field;
                case ProviderType.Parameter:
                    return MulticastTargets.Parameter;
                case ProviderType.MethodReturn:
                    return MulticastTargets.ReturnValue;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            throw new ArgumentOutOfRangeException(nameof(target));
        }

        private static bool IsFrameworkAssemblyReference(AssemblyNameReference ar)
        {
            return ar.Name.StartsWith("System.") || ar.Name == "System" || ar.Name == "mscorlib";
        }

        private static bool IsSpinnerOrReferencesSpinner(AssemblyDefinition assembly)
        {
            return assembly.Name.Name == "Spinner" || assembly.MainModule.AssemblyReferences.Any(ar => ar.Name == "Spinner");
        }
    }
}
