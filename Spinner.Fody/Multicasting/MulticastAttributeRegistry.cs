using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// Builds and provides a registry that maps custom attribute providers to multicast attribute instances that
    /// apply to them either through direct attribute application, container multicasting, or inheritance.
    /// </summary>
    internal class MulticastAttributeRegistry
    {
        private static readonly IList<MulticastAttributeInstance> s_noInstances = new MulticastAttributeInstance[0];

        private readonly ModuleWeavingContext _mwc;
        private readonly ModuleDefinition _module;
        private readonly TypeDefinition _compilerGeneratedAttributeType;
        private readonly TypeDefinition _multicastAttributeType;

        // The final mapping of attribute providers to the multicast instances that apply to them.
        private readonly Dictionary<ICustomAttributeProvider, List<MulticastAttributeInstance>> _targets = new Dictionary<ICustomAttributeProvider, List<MulticastAttributeInstance>>();

        // Maps providers that define actual multicast attributes to their instances. An ordering integer is used to
        // ensure that multicasts are processed in order from base to derived.
        private Dictionary<ICustomAttributeProvider, Tuple<int, List<MulticastAttributeInstance>>> _instances = new Dictionary<ICustomAttributeProvider, Tuple<int, List<MulticastAttributeInstance>>>();

        private int _inheritOrderCounter = int.MinValue;
        private int _directOrderCounter;

        private MulticastAttributeRegistry(ModuleWeavingContext mwc)
        {
            _mwc = mwc;
            _module = mwc.Module;
            _compilerGeneratedAttributeType = mwc.Framework.CompilerGeneratedAttribute;
            _multicastAttributeType = mwc.Spinner.MulticastAttribute;
        }

        internal IList<MulticastAttributeInstance> GetMulticasts(ICustomAttributeProvider provider)
        {
            List<MulticastAttributeInstance> multicasts;
            // ReSharper disable once InconsistentlySynchronizedField
            return _targets.TryGetValue(provider, out multicasts) ? multicasts : s_noInstances;
        }

        internal static MulticastAttributeRegistry Create(ModuleWeavingContext mwc)
        {
            var inst = new MulticastAttributeRegistry(mwc);
            inst.Initialize();
            return inst;
        }

        private void Initialize()
        {
            //
            // Creates multicast attribute instances for all types in assembly and referenced assemblies. Also for
            // types that have multicast 
            //

            var filter = new HashSet<ICustomAttributeProvider>();
            InstantiateMulticasts(_module.Assembly, filter);
            filter = null;

            //
            // Create new instances where inheritance is allowed
            //

            foreach (KeyValuePair<ICustomAttributeProvider, Tuple<int, List<MulticastAttributeInstance>>> item in _instances.ToList())
            {
                IReadOnlyList<IMetadataTokenProvider> derivedList = _mwc.MulticastEngine.GetDerived(item.Key);
                if (derivedList.Count == 0)
                    continue;

                foreach (MulticastAttributeInstance mi in item.Value.Item2)
                {
                    if (mi.Inheritance == MulticastInheritance.None)
                        continue;

                    foreach (ICustomAttributeProvider d in derivedList.OfType<ICustomAttributeProvider>())
                    {
                        MulticastAttributeInstance nmi = mi.WithTarget(d);

                        Tuple<int, List<MulticastAttributeInstance>> mis;
                        if (!_instances.TryGetValue(d, out mis))
                        {
                            // Inherit order counter ensures that inherited instances are applied first
                            int initOrder = _inheritOrderCounter++;
                            mis = Tuple.Create(initOrder, new List<MulticastAttributeInstance>());
                            _instances.Add(d, mis);
                        }

                        mis.Item2.Add(nmi);

                        _mwc.LogDebug($"Multicast Inheritance: AttributeType: {mi.AttributeType}, Origin: {mi.Origin}, Inheritor: {d}");
                    }
                }
            }

            //
            // Apply multicasts in the order the instances were created
            //

            var initLists = _instances.Values.ToList();
            initLists.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            foreach (Tuple<int, List<MulticastAttributeInstance>> group in initLists)
                UpdateMulticastTargets(group.Item2);

            //
            // Apply ordering and exclusions
            //

            foreach (KeyValuePair<ICustomAttributeProvider, List<MulticastAttributeInstance>> item in _targets)
            {
                List<MulticastAttributeInstance> instances = item.Value;

                if (instances.Count > 1)
                {
                    // Remove duplicates inherited from same origin and order by priority
                    // LINQ ensures that things remain in the original order where possible
                    List<MulticastAttributeInstance> newInstances = instances.Distinct().OrderBy(i => i.Priority).ToList();
                    instances.Clear();
                    instances.AddRange(newInstances);
                }

                // Apply exclusions
                for (int i = 0; i < instances.Count; i++)
                {
                    MulticastAttributeInstance a = instances[i];

                    if (a.Exclude)
                    {
                        for (int r = instances.Count - 1; r > -1; r--)
                        {
                            if (instances[r].AttributeType == a.AttributeType)
                                instances.RemoveAt(r);
                        }

                        i = -1;
                    }
                }
            }

            // No longer need this data
            _instances = null;
        }

        private void InstantiateMulticasts(AssemblyDefinition assembly, HashSet<ICustomAttributeProvider> filter)
        {
            if (filter.Contains(assembly))
                return;
            filter.Add(assembly);

            if (!IsSpinnerOrReferencesSpinner(assembly))
                return;

            foreach (AssemblyNameReference ar in assembly.MainModule.AssemblyReferences)
            {
                // Skip some of the big ones
                if (IsFrameworkAssemblyReference(ar))
                    continue;

                AssemblyDefinition referencedAssembly = assembly.MainModule.AssemblyResolver.Resolve(ar);

                InstantiateMulticasts(referencedAssembly, filter);
            }

            if (assembly.HasCustomAttributes)
            {
                var attrs = new List<MulticastAttributeInstance>();
                InstantiateMulticasts(assembly, ProviderType.Assembly);

                UpdateMulticastTargets(attrs);
            }

            foreach (TypeDefinition t in assembly.MainModule.Types)
            {
                if (!HasGeneratedName(t) && !HasGeneratedAttribute(t))
                    InstantiateMulticasts(t, filter);
            }
        }

        private void InstantiateMulticasts(TypeDefinition type, HashSet<ICustomAttributeProvider> filter)
        {
            if (!filter.Contains(type))
                InstantiateMulticastsSlow(type, filter);
        }

        private void InstantiateMulticastsSlow(TypeDefinition type, HashSet<ICustomAttributeProvider> filter)
        {
            filter.Add(type);

            if (type.BaseType != null)
                InstantiateMulticasts(type.BaseType.Resolve(), filter);

            if (type.HasInterfaces)
                foreach (TypeReference itr in type.Interfaces)
                    InstantiateMulticasts(itr.Resolve(), filter);

            if (type.HasCustomAttributes)
                InstantiateMulticasts(type, ProviderType.Type);

            if (type.HasMethods)
            {
                foreach (MethodDefinition m in type.Methods)
                {
                    if (HasGeneratedName(m))
                        continue;

                    // getters, setters, and event adders and removers are handled by their owning property/event
                    if (m.SemanticsAttributes != MethodSemanticsAttributes.None)
                        continue;

                    if (m.HasCustomAttributes)
                        InstantiateMulticasts(m, ProviderType.Method);

                    if (m.HasParameters)
                    {
                        foreach (ParameterDefinition p in m.Parameters)
                        {
                            if (p.HasCustomAttributes)
                                InstantiateMulticasts(p, ProviderType.Parameter);
                        }
                    }

                    if (m.MethodReturnType.HasCustomAttributes && !m.ReturnType.IsSame(m.Module.TypeSystem.Void))
                        InstantiateMulticasts(m.MethodReturnType, ProviderType.MethodReturn);
                }
            }

            if (type.HasProperties)
            {
                foreach (PropertyDefinition p in type.Properties)
                {
                    if (HasGeneratedName(p))
                        continue;

                    if (p.HasCustomAttributes)
                        InstantiateMulticasts(p, ProviderType.Property);

                    if (p.GetMethod != null)
                        InstantiateMulticasts(p.GetMethod, ProviderType.Method);

                    if (p.SetMethod != null)
                        InstantiateMulticasts(p.SetMethod, ProviderType.Method);
                }
            }

            if (type.HasEvents)
            {
                foreach (EventDefinition e in type.Events)
                {
                    if (HasGeneratedName(e))
                        continue;

                    if (e.HasCustomAttributes)
                        InstantiateMulticasts(e, ProviderType.Event);

                    if (e.AddMethod != null)
                        InstantiateMulticasts(e.AddMethod, ProviderType.Method);

                    if (e.RemoveMethod != null)
                        InstantiateMulticasts(e.RemoveMethod, ProviderType.Method);
                }
            }

            if (type.HasFields)
            {
                foreach (FieldDefinition f in type.Fields)
                {
                    if (!HasGeneratedName(f) && f.HasCustomAttributes)
                        InstantiateMulticasts(f, ProviderType.Field);
                }
            }

            if (type.HasNestedTypes)
            {
                foreach (TypeDefinition nt in type.NestedTypes)
                {
                    if (!HasGeneratedName(nt) && !HasGeneratedAttribute(nt))
                        InstantiateMulticasts(nt, filter);
                }
            }
        }

        /// <summary>
        /// Process a multicast instance list by ordering them, applying exclusion rules, and removing instances
        /// inherited from the same origin.
        /// </summary>
        /// <param name="instances"></param>
        private void UpdateMulticastTargets(List<MulticastAttributeInstance> instances)
        {
            if (instances != null && instances.Count != 0)
            {
                foreach (MulticastAttributeInstance mi in instances)
                    UpdateMulticastTargets(mi);
            }
        }

        private void InstantiateMulticasts(ICustomAttributeProvider origin, ProviderType originType)
        {
            List<MulticastAttributeInstance> results = null;

            foreach (CustomAttribute a in origin.CustomAttributes)
            {
                TypeDefinition atype = a.AttributeType.Resolve();

                if (atype.IsSame(_compilerGeneratedAttributeType))
                    break;

                if (!IsMulticastAttribute(atype))
                    continue;

                bool isExternal;
                switch (originType)
                {
                    case ProviderType.Assembly:
                        isExternal = ((AssemblyDefinition) origin).MainModule != _module;
                        break;
                    case ProviderType.Type:
                    case ProviderType.Method:
                    case ProviderType.Property:
                    case ProviderType.Event:
                    case ProviderType.Field:
                        isExternal = ((MemberReference) origin).Module != _module;
                        break;
                    case ProviderType.Parameter:
                        isExternal = ((MethodDefinition) ((ParameterDefinition) origin).Method).Module != _module;
                        break;
                    case ProviderType.MethodReturn:
                        isExternal = ((MethodDefinition) ((MethodReturnType) origin).Method).Module != _module;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(originType), originType, null);
                }

                var mai = new MulticastAttributeInstance(origin, originType, a, atype, isExternal);

                if (results == null)
                    results = new List<MulticastAttributeInstance>();
                results.Add(mai);
            }

            if (results == null)
                return;

            int initOrder = _directOrderCounter++;

            //_derived.Add(origin, null);
            _instances.Add(origin, Tuple.Create(initOrder, results));
        }

        private void UpdateMulticastTargets(MulticastAttributeInstance mi)
        {
            // Find indirect multicasts up the tree. Indirect meaning targets where a multicast attribute was not
            // directly specified in the source code.
            if (mi.Origin == mi.Target || mi.Inheritance == MulticastInheritance.Multicast)
            {
                foreach (IMetadataTokenProvider item in _mwc.MulticastEngine.GetDescendants(mi.Target, mi.Arguments))
                    AddMulticastTarget(mi, (ICustomAttributeProvider) item);
            }

            if ((mi.Arguments.TargetElements & mi.Target.GetMulticastTargetType()) != 0)
                AddMulticastTarget(mi, mi.Target);
        }

        private void AddMulticastTarget(MulticastAttributeInstance mi, ICustomAttributeProvider target)
        {
            List<MulticastAttributeInstance> current;
            if (!_targets.TryGetValue(target, out current))
            {
                current = new List<MulticastAttributeInstance>();
                _targets.Add(target, current);
            }
            current.Add(mi);
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
