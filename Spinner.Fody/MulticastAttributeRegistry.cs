using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Spinner.Extensibility;
using Spinner.Fody.Utilities;

namespace Spinner.Fody
{
    internal enum DefinitionType
    {
        AssemblyDefinition,
        TypeDefinition,
        MethodDefinition,
        PropertyDefinition,
        EventDefinition,
        FieldDefinition,
        ParameterDefinition
    }

    /// <summary>
    /// 
    /// </summary>
    internal class MulticastAttributeRegistry
    {
        private const int MaximumTargetBit = 13;

        private readonly ModuleWeavingContext _mwc;
        private readonly TypeDefinition _multicastAttributeType;
        private readonly TypeDefinition _compilerGeneratedAttributeType;
        
        private readonly HashSet<AssemblyDefinition> _assemblies = new HashSet<AssemblyDefinition>(); 
        private readonly List<MulticastInstance> _instances = new List<MulticastInstance>();

        //private readonly Dictionary<MulticastTargets, List<MulticastInstance>> _instancesByTargetType =
        //    new Dictionary<MulticastTargets, List<MulticastInstance>>();
        private readonly Dictionary<ICustomAttributeProvider, MulticastInstance[]> _instancesByProvider
            = new Dictionary<ICustomAttributeProvider, MulticastInstance[]>(); 

        private readonly Dictionary<ICustomAttributeProvider, MulticastInstance[]> _results =
            new Dictionary<ICustomAttributeProvider, MulticastInstance[]>();

        private static readonly Dictionary<TokenType, DefinitionType> s_definitionTypes =
            new Dictionary<TokenType, DefinitionType>
            {
                {TokenType.Assembly, DefinitionType.AssemblyDefinition},
                {TokenType.TypeDef, DefinitionType.TypeDefinition},
                {TokenType.Method, DefinitionType.MethodDefinition},
                {TokenType.Property, DefinitionType.PropertyDefinition},
                {TokenType.Event, DefinitionType.EventDefinition},
                {TokenType.Field, DefinitionType.FieldDefinition},
                {TokenType.Param, DefinitionType.ParameterDefinition}
            };

        internal MulticastAttributeRegistry(ModuleWeavingContext mwc)
        {
            _mwc = mwc;
            _multicastAttributeType = mwc.Spinner.MulticastAttribute;
            _compilerGeneratedAttributeType = mwc.Framework.CompilerGeneratedAttribute;
        }

        internal MulticastInstance[] GetMulticasts(ICustomAttributeProvider target)
        {
            var definitionType = GetDefinitionType(target);
            switch (definitionType)
            {
                case DefinitionType.AssemblyDefinition:
                    throw new NotImplementedException();
                case DefinitionType.TypeDefinition:
                    return GetMulticasts((TypeDefinition) target);
                case DefinitionType.MethodDefinition:
                case DefinitionType.PropertyDefinition:
                case DefinitionType.EventDefinition:
                case DefinitionType.FieldDefinition:
                    return GetMulticasts((IMemberDefinition) target);
                case DefinitionType.ParameterDefinition:
                    throw new NotImplementedException();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal MulticastInstance[] GetMulticasts(TypeDefinition type)
        {
            MulticastInstance[] results;
            bool hasResults;

            lock (_results)
                hasResults = _results.TryGetValue(type, out results);

            if (!hasResults)
            {
                lock (type)
                {
                    results = ComputeMulticasts(type);
                    lock (_results)
                        _results[type] = results;
                }
            }

            return results ?? CollectionUtility<MulticastInstance>.EmptyArray;
        }

        internal MulticastInstance[] GetMulticasts(IMemberDefinition member)
        {
            MulticastInstance[] results;
            bool hasResults;

            lock (_results)
                hasResults = _results.TryGetValue(member, out results);

            if (!hasResults)
            {
                lock (member.DeclaringType)
                {
                    results = ComputeMulticasts(member);
                    lock (_results)
                        _results[member] = results;
                }
            }

            return results ?? CollectionUtility<MulticastInstance>.EmptyArray;
        }

        internal void InspectAssemblies()
        {
            _mwc.Multicasts.AddAssembly(_mwc.Module.Assembly);
        }

        internal void AddModule(ModuleDefinition module)
        {
            foreach (AssemblyNameReference ar in module.AssemblyReferences)
            {
                if (ar.Name == "Spinner")
                    continue;
                if (ar.Name.StartsWith("System.") || ar.Name == "System" || ar.Name == "mscorlib")
                    continue;
                AssemblyDefinition assemblyDefinition = module.AssemblyResolver.Resolve(ar);
            }
        }

        internal void AddAssembly(AssemblyDefinition assembly)
        {
            if (_assemblies.Contains(assembly))
                return;
            _assemblies.Add(assembly);

            if (assembly.HasCustomAttributes)
            {
                foreach (CustomAttribute a in assembly.CustomAttributes)
                {
                    TypeDefinition atype = a.AttributeType.Resolve();
                    if (IsMulticastAttribute(atype))
                    {
                        _instances.Add(new MulticastInstance(assembly, a, atype));
                    }
                }
            }

            foreach (TypeDefinition t in assembly.MainModule.Types)
            {
                InstantiateMulticastAttributes(t);
            }
        }

        private MulticastInstance[] ComputeMulticasts(TypeDefinition type)
        {
            MulticastTargets targetType = GetTargetType(type);
            MulticastAttributes targetAttributes = ComputeTargetAttributes(type);
            string name = type.FullName;
            List<MulticastInstance> results = null;

            MulticastInstance[] assemblyMcs;
            if (_instancesByProvider.TryGetValue(type.Module.Assembly, out assemblyMcs))
            {
                foreach (MulticastInstance m in assemblyMcs)
                {
                    
                }
            }

            if (type.BaseType != null)
            {
                MulticastInstance[] baseMcs = GetMulticasts(type.BaseType.Resolve());

                foreach (MulticastInstance m in baseMcs)
                {
                    if (m.Inheritance == MulticastInheritance.None)
                        continue;

                    if ((m.TargetTypeAttributes & targetAttributes) == 0)
                        continue;

                    if (m.TargetTypes != null && !m.TargetTypes.IsMatch(name))
                        continue;

                    if (results == null)
                        results = new List<MulticastInstance>();
                    results.Add(m);
                }
            }

            MulticastInstance[] mcs;
            if (_instancesByProvider.TryGetValue(type, out mcs))
            {
                foreach (MulticastInstance m in mcs)
                {
                    if ((m.TargetElements & targetType) == 0)
                        continue;

                    //if ((m.TargetTypeAttributes & targetAttributes) == 0)
                    //    continue;

                    //if (m.TargetTypes != null && !m.TargetTypes.IsMatch(name))
                    //    continue;

                    if (results == null)
                        results = new List<MulticastInstance>();
                    results.Add(m);
                }
            }

            if (results == null)
                return null;

            results.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            ComputeExclusions(results);

            return results.Distinct().ToArray();
        }

        private MulticastInstance[] ComputeMulticasts(IMemberDefinition member)
        {
            MulticastTargets targetType = GetTargetType(member);
            MulticastAttributes targetAttributes = ComputeTargetAttributes(member);
            string name = member.Name;
            List<MulticastInstance> results = null;

            MulticastInstance[] typeMcs = GetMulticasts(member.DeclaringType);
            foreach (MulticastInstance m in typeMcs)
            {
                if ((m.TargetElements & targetType) == 0)
                    continue;

                bool inherited = m.Provider is TypeDefinition && m.Provider != member.DeclaringType;

                if (inherited && m.Inheritance != MulticastInheritance.Multicast)
                    continue;

                bool external = m.Attribute.Constructor.Module != _mwc.Module;

                MulticastAttributes compareAttributes = external ? m.TargetExternalMemberAttributes : m.TargetMemberAttributes;

                if ((compareAttributes & targetAttributes) == 0)
                    continue;

                if (m.TargetMembers != null && !m.TargetMembers.IsMatch(name))
                    continue;

                if (results == null)
                    results = new List<MulticastInstance>();
                results.Add(m);
            }

            MulticastInstance[] mcs;
            if (_instancesByProvider.TryGetValue(member, out mcs))
            {
                foreach (MulticastInstance m in mcs)
                {
                    if ((m.TargetElements & targetType) == 0)
                        continue;

                    if (results == null)
                        results = new List<MulticastInstance>();
                    results.Add(m);
                }
            }

            if (results == null)
                return null;

            results.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            ComputeExclusions(results);

            return results.Distinct().ToArray();
        }

        private void ComputeExclusions(List<MulticastInstance> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                MulticastInstance a = results[i];

                if (a.Exclude)
                {
                    for (int r = results.Count - 1; r > -1; r--)
                    {
                        if (results[r].AttributeType == a.AttributeType)
                            results.RemoveAt(r);
                    }
                    i = -1; // restart from beginning
                }
            }
        }

        private MulticastAttributes ComputeTargetAttributes(TypeDefinition type)
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

            a |= type.Name.StartsWith("<") || IsCompilerGenerated(type) ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeTargetAttributes(IMemberDefinition member)
        {
            switch (GetDefinitionType(member))
            {
                case DefinitionType.MethodDefinition:
                    return ComputeTargetAttributes((MethodDefinition) member);
                case DefinitionType.PropertyDefinition:
                    return ComputeTargetAttributes((PropertyDefinition) member);
                case DefinitionType.EventDefinition:
                    return ComputeTargetAttributes((EventDefinition) member);
                case DefinitionType.FieldDefinition:
                    return ComputeTargetAttributes((FieldDefinition) member);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private MulticastAttributes ComputeTargetAttributes(MethodDefinition method)
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

            a |= method.Name.StartsWith("<") || IsCompilerGenerated(method) ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeTargetAttributes(PropertyDefinition property)
        {
            MulticastAttributes ga = property.GetMethod != null ? ComputeTargetAttributes(property.GetMethod) : 0;
            MulticastAttributes sa = property.SetMethod != null ? ComputeTargetAttributes(property.SetMethod) : 0;

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

            a |= property.Name.StartsWith("<") || ((ga | sa) & MulticastAttributes.CompilerGenerated) != 0 ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private MulticastAttributes ComputeTargetAttributes(EventDefinition evt)
        {
            Debug.Assert(evt.AddMethod != null);
            
            return ComputeTargetAttributes(evt.AddMethod);
        }

        private MulticastAttributes ComputeTargetAttributes(FieldDefinition field)
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

            a |= field.Name.StartsWith("<") || IsCompilerGenerated(field) ? MulticastAttributes.CompilerGenerated : MulticastAttributes.UserGenerated;

            return a;
        }

        private bool MaybeHasMulticastAttribute(IMemberDefinition def)
        {
            return !def.Name.StartsWith("<");
        }

        private void InstantiateMulticastAttributes(TypeDefinition t)
        {
            if (!MaybeHasMulticastAttribute(t) || IsCompilerGenerated(t))
                return;

            foreach (MethodDefinition m in t.Methods)
            {
                if (!MaybeHasMulticastAttribute(m))
                    continue;

                if (m.HasCustomAttributes)
                    InstantiateMulticastAttributes(m);

                if (m.HasParameters)
                {
                    foreach (ParameterDefinition p in m.Parameters)
                    {
                        if (p.HasCustomAttributes)
                            InstantiateMulticastAttributes(p);
                    }

                    if (m.MethodReturnType.HasCustomAttributes)
                        InstantiateMulticastAttributes(m.MethodReturnType);
                }
            }

            foreach (PropertyDefinition p in t.Properties)
            {
                if (!MaybeHasMulticastAttribute(p))
                    continue;

                if (p.HasCustomAttributes)
                    InstantiateMulticastAttributes(p);
            }

            foreach (EventDefinition e in t.Events)
            {
                if (!MaybeHasMulticastAttribute(e))
                    continue;

                if (e.HasCustomAttributes)
                    InstantiateMulticastAttributes(e);
            }

            if (t.HasNestedTypes)
            {
                foreach (TypeDefinition nt in t.NestedTypes)
                {
                    InstantiateMulticastAttributes(nt);
                }
            }
        }

        private void InstantiateMulticastAttributes(ICustomAttributeProvider provider)
        {
            List<MulticastInstance> instances = null;

            foreach (CustomAttribute a in provider.CustomAttributes)
            {
                TypeDefinition atype = a.AttributeType.Resolve();

                if (atype == _compilerGeneratedAttributeType)
                    return;

                if (!IsMulticastAttribute(atype))
                    continue;

                var mai = new MulticastInstance(provider, a, atype);
                _instances.Add(mai);

                if (instances == null)
                    instances = new List<MulticastInstance>();
                instances.Add(mai);

                //MulticastTargets targets = mai.TargetElements;

                //if (targets == 0)
                //    continue;

                //for (int i = 0; i <= MaximumTargetBit; i++)
                //{
                //    MulticastTargets c = (MulticastTargets) ((uint) targets & (uint) (1 << i));
                //    if (c == 0)
                //        continue;

                //    List<MulticastInstance> instances;
                //    if (!_instancesByTargetType.TryGetValue(c, out instances))
                //    {
                //        instances = new List<MulticastInstance>();
                //        _instancesByTargetType.Add(c, instances);
                //    }
                //    instances.Add(mai);
                //}
            }

            if (instances != null)
                _instancesByProvider.Add(provider, instances.ToArray());
        }

        private bool IsMulticastAttribute(TypeDefinition type)
        {
            var m = _multicastAttributeType;

            TypeReference current = type.BaseType;
            while (current != null)
            {
                if (current.IsSimilar(m))
                    return true;

                current = current.Resolve().BaseType;
            }

            return false;
        }

        private bool IsCompilerGenerated(ICustomAttributeProvider target)
        {
            if (!target.HasCustomAttributes)
                return false;

            foreach (CustomAttribute a in target.CustomAttributes)
            {
                if (a.AttributeType.Resolve() == _compilerGeneratedAttributeType)
                    return true;
            }

            return false;
        }

        private static DefinitionType GetDefinitionType(IMetadataTokenProvider target)
        {
            return s_definitionTypes[target.MetadataToken.TokenType];
        }

        private static string GetName(ICustomAttributeProvider target)
        {
            switch (GetDefinitionType(target))
            {
                case DefinitionType.AssemblyDefinition:
                    return ((AssemblyDefinition) target).FullName;
                case DefinitionType.TypeDefinition:
                    return ((TypeDefinition) target).FullName;
                case DefinitionType.MethodDefinition:
                case DefinitionType.PropertyDefinition:
                case DefinitionType.EventDefinition:
                case DefinitionType.FieldDefinition:
                    return ((IMemberDefinition) target).Name;
                case DefinitionType.ParameterDefinition:
                    return ((ParameterDefinition) target).Name;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static MulticastTargets GetTargetType(IMetadataTokenProvider target)
        {
            switch (GetDefinitionType(target))
            {
                case DefinitionType.AssemblyDefinition:
                    return MulticastTargets.Assembly;
                case DefinitionType.TypeDefinition:
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
                case DefinitionType.MethodDefinition:
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
                case DefinitionType.PropertyDefinition:
                    return MulticastTargets.Property;
                case DefinitionType.EventDefinition:
                    return MulticastTargets.Event;
                case DefinitionType.FieldDefinition:
                    return MulticastTargets.Field;
                case DefinitionType.ParameterDefinition:
                    return MulticastTargets.Parameter;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            throw new ArgumentOutOfRangeException(nameof(target));

            //var member = target as IMemberDefinition;
            //if (member != null)
            //{
            //    var type = member as TypeDefinition;
            //    if (type != null)
            //    {
            //        if (type.IsInterface)
            //            return MulticastTargets.Interface;

            //        if (type.BaseType?.Namespace == "System")
            //        {
            //            switch (type.BaseType.Name)
            //            {
            //                case "Enum":
            //                    return MulticastTargets.Enum;
            //                case "ValueType":
            //                    return MulticastTargets.Struct;
            //                case "Delegate":
            //                    return MulticastTargets.Delegate;
            //            }
            //        }

            //        if (type.IsClass)
            //            return MulticastTargets.Class;

            //        throw new ArgumentOutOfRangeException(nameof(target));
            //    }

            //    var method = member as MethodDefinition;
            //    if (method != null)
            //    {
            //        switch (method.Name)
            //        {
            //            case ".ctor":
            //                return MulticastTargets.InstanceConstructor;
            //            case ".cctor":
            //                return MulticastTargets.StaticConstructor;
            //            default:
            //                return MulticastTargets.Method;
            //        }
            //    }

            //    var property = member as PropertyDefinition;
            //    if (property != null)
            //        return MulticastTargets.Property;

            //    var evt = member as EventDefinition;
            //    if (evt != null)
            //        return MulticastTargets.Event;

            //    var field = member as FieldDefinition;
            //    if (field != null)
            //        return MulticastTargets.Field;
            //}

            //var assembly = target as AssemblyDefinition;
            //if (assembly != null)
            //    return MulticastTargets.Assembly;

            //throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    internal class MulticastInstance
    {
        public readonly ICustomAttributeProvider Provider;

        public readonly CustomAttribute Attribute;

        public readonly TypeDefinition AttributeType;

        public MulticastInstance(ICustomAttributeProvider provider, CustomAttribute attribute, TypeDefinition attributeType)
        {
            Provider = provider;
            Attribute = attribute;
            AttributeType = attributeType;
            TargetElements = MulticastTargets.All;
            TargetTypeAttributes = MulticastAttributes.All;
            TargetExternalTypeAttributes = MulticastAttributes.All;
            //TargetMemberAttributes = MulticastAttributes.All;
            //TargetExternalMemberAttributes = MulticastAttributes.All;
            //TargetParameterAttributes = MulticastAttributes.All;

            if (attribute.HasProperties)
            {
                foreach (CustomAttributeNamedArgument ca in attribute.Properties)
                {
                    object value = ca.Argument.Value;
                    switch (ca.Name)
                    {
                        case nameof(MulticastAttribute.AttributeExclude):
                            Exclude = (bool) value;
                            break;
                        case nameof(MulticastAttribute.AttributeInheritance):
                            Inheritance = (MulticastInheritance) (byte) value;
                            break;
                        case nameof(MulticastAttribute.AttributePriority):
                            Priority = (int) value;
                            break;
                        case nameof(MulticastAttribute.AttributeReplace):
                            Replace = (bool) value;
                            break;
                        case nameof(MulticastAttribute.AttributeTargetElements):
                            TargetElements = (MulticastTargets) (uint) value;
                            break;
                        case nameof(MulticastAttribute.AttributeTargetAssemblies):
                            TargetAssemblies = StringMatcher.Create((string) value);
                            break;
                        case nameof(MulticastAttribute.AttributeTargetTypes):
                            TargetTypes = StringMatcher.Create((string) value);
                            break;
                        case nameof(MulticastAttribute.AttributeTargetTypeAttributes):
                            TargetTypeAttributes = (MulticastAttributes) (uint) value;
                            break;
                        case nameof(MulticastAttribute.AttributeTargetExternalTypeAttributes):
                            TargetExternalTypeAttributes = (MulticastAttributes) (uint) value;
                            break;
                        case nameof(MulticastAttribute.AttributeTargetMembers):
                            TargetMembers = StringMatcher.Create((string) value);
                            break;
                        case nameof(MulticastAttribute.AttributeTargetMemberAttributes):
                            TargetMemberAttributes = (MulticastAttributes) (uint) value;
                            break;
                        case nameof(MulticastAttribute.AttributeTargetExternalMemberAttributes):
                            TargetExternalMemberAttributes = (MulticastAttributes) (uint) value;
                            break;
                        case nameof(MulticastAttribute.AttributeTargetParameters):
                            TargetParameters = StringMatcher.Create((string) value);
                            break;
                        case nameof(MulticastAttribute.AttributeTargetParameterAttributes):
                            TargetParameterAttributes = (MulticastAttributes) (uint) value;
                            break;
                    }
                }
            }
        }

        public bool Exclude { get; }

        public MulticastInheritance Inheritance { get; }

        public int Priority { get; }

        public bool Replace { get; }

        public MulticastTargets TargetElements { get; }

        public StringMatcher TargetAssemblies { get; }

        public StringMatcher TargetTypes { get; }

        public MulticastAttributes TargetTypeAttributes { get; }

        public MulticastAttributes TargetExternalTypeAttributes { get; }

        public StringMatcher TargetMembers { get; }

        public MulticastAttributes TargetMemberAttributes { get; }

        public MulticastAttributes TargetExternalMemberAttributes { get; }

        public StringMatcher TargetParameters { get; }

        public MulticastAttributes TargetParameterAttributes { get; }
    }

    internal abstract class StringMatcher
    {
        internal const string RegexPrefix = "regex:";

        public abstract bool IsMatch(string value);

        public static StringMatcher Create(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            if (pattern.StartsWith(RegexPrefix))
                return new RegexMatcher(new Regex(pattern.Substring(RegexPrefix.Length + 1)));

            int wcindex = pattern.IndexOf('*');
            int swcindex = pattern.IndexOf('?');

            if (wcindex == -1 && swcindex == -1)
                return new EqualityMatcher(pattern);

            if (wcindex == pattern.Length - 1 && swcindex == -1)
                return new PrefixMatcher(pattern.Substring(0, pattern.Length - 1));

            return new RegexMatcher(new Regex(StringUtility.WildcardToRegex(pattern)));
        }

        private sealed class EqualityMatcher : StringMatcher
        {
            private readonly string _value;

            public EqualityMatcher(string value)
            {
                _value = value;
            }

            public override bool IsMatch(string value)
            {
                return value == _value;
            }
        }

        private sealed class PrefixMatcher : StringMatcher
        {
            private readonly string _value;

            public PrefixMatcher(string value)
            {
                _value = value;
            }

            public override bool IsMatch(string value)
            {
                return value.StartsWith(_value);
            }
        }

        private sealed class RegexMatcher : StringMatcher
        {
            private readonly Regex _regex;

            public RegexMatcher(Regex regex)
            {
                _regex = regex;
            }

            public override bool IsMatch(string value)
            {
                return _regex.IsMatch(value);
            }
        }
    }
}
