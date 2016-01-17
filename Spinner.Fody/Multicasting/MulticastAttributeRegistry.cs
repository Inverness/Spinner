using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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

        private readonly ModuleWeavingContext _mwc;
        private readonly TypeDefinition _multicastAttributeType;
        private readonly TypeDefinition _compilerGeneratedAttributeType;
        
        private readonly HashSet<AssemblyDefinition> _assemblies = new HashSet<AssemblyDefinition>(); 

        private readonly Dictionary<ICustomAttributeProvider, List<MulticastInstance>> _targets =
            new Dictionary<ICustomAttributeProvider, List<MulticastInstance>>();

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

        internal MulticastAttributeRegistry(ModuleWeavingContext mwc)
        {
            _mwc = mwc;
            _multicastAttributeType = mwc.Spinner.MulticastAttribute;
            _compilerGeneratedAttributeType = mwc.Framework.CompilerGeneratedAttribute;
        }

        internal IList<MulticastInstance> GetMulticasts(ICustomAttributeProvider provider)
        {
            List<MulticastInstance> multicasts;
            // ReSharper disable once InconsistentlySynchronizedField
            return _targets.TryGetValue(provider, out multicasts) ? multicasts : s_noInstances;
        }

        internal void ProcessMulticasts()
        {
            ProcessMulticasts(_mwc.Module.Assembly);
        }

        //internal void AddModule(ModuleDefinition module)
        //{
        //    foreach (AssemblyNameReference ar in module.AssemblyReferences)
        //    {
        //        if (ar.Name == "Spinner")
        //            continue;
        //        if (ar.Name.StartsWith("System.") || ar.Name == "System" || ar.Name == "mscorlib")
        //            continue;
        //        AssemblyDefinition assemblyDefinition = module.AssemblyResolver.Resolve(ar);
        //    }
        //}

        private void ProcessMulticasts(AssemblyDefinition assembly)
        {
            if (_assemblies.Contains(assembly))
                return;
            _assemblies.Add(assembly);

            if (assembly.Name.Name != "Spinner" && assembly.MainModule.AssemblyReferences.All(ar => ar.Name != "Spinner"))
                return; // not an assembly that references Spinner, so nothing to do

            foreach (AssemblyNameReference ar in assembly.MainModule.AssemblyReferences)
            {
                // Skip some of the big ones
                if (ar.Name.StartsWith("System.") || ar.Name == "System" || ar.Name == "mscorlib")
                    continue;

                AssemblyDefinition referencedAssembly = assembly.MainModule.AssemblyResolver.Resolve(ar);

                ProcessMulticasts(referencedAssembly);
            }

            if (assembly.HasCustomAttributes)
                 InstantiateMulticastAttributes(assembly, ProviderType.Assembly);

            var tasks = new List<Task>();

            foreach (TypeDefinition t in assembly.MainModule.Types)
            {
                if (!HasGeneratedName(t) && !HasGeneratedAttribute(t))
                {
                    tasks.Add(Task.Run(() => InstantiateMulticastAttributes(t)));
                }
            }

            Task.WhenAll(tasks).Wait();
        }

        private void ProcessMulticasts(MulticastInstance mi)
        {
            bool external = mi.Attribute.Constructor.Module != _mwc.Module;

            List<Tuple<MulticastInstance, ICustomAttributeProvider>> results = null;

            ResultHandler handler = (i, p) =>
            {
                if (results == null)
                    results = new List<Tuple<MulticastInstance, ICustomAttributeProvider>>();
                results.Add(Tuple.Create(i, p));
            };
            
            MulticastAttributes memberCompareAttrs = external
                ? mi.TargetExternalMemberAttributes
                : mi.TargetMemberAttributes;

            switch (mi.ProviderType)
            {
                case ProviderType.Assembly:
                    ProcessIndirectMulticasts(mi, (AssemblyDefinition) mi.Provider, handler);
                    break;
                case ProviderType.Type:
                    ProcessIndirectMulticasts(mi, (TypeDefinition) mi.Provider, handler);
                    break;
                case ProviderType.Method:
                    if (memberCompareAttrs != 0)
                    {
                        var method = (MethodDefinition) mi.Provider;
                        if (method.SemanticsAttributes == MethodSemanticsAttributes.None)
                            ProcessIndirectMulticasts(mi, method, handler);
                    }
                    break;
                case ProviderType.Property:
                    if (memberCompareAttrs != 0)
                        ProcessIndirectMulticasts(mi, (PropertyDefinition) mi.Provider, handler);
                    break;
                case ProviderType.Event:
                    if (memberCompareAttrs != 0)
                        ProcessIndirectMulticasts(mi, (EventDefinition) mi.Provider, handler);
                    break;
                case ProviderType.Field:
                    if (memberCompareAttrs != 0)
                        ProcessIndirectMulticasts(mi, (FieldDefinition) mi.Provider, handler);
                    break;
                case ProviderType.Parameter:
                case ProviderType.MethodReturn:
                    // these are handled by their parent method
                    break;
            }

            if ((mi.TargetElements & GetTargetType(mi.Provider)) != 0)
                handler(mi, mi.Provider);

            if (results == null)
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

        private void InstantiateMulticastAttributes(TypeDefinition type)
        {
            if (type.HasCustomAttributes)
                InstantiateMulticastAttributes(type, ProviderType.Type);

            if (type.HasMethods)
            {
                foreach (MethodDefinition m in type.Methods)
                {
                    if (m.SemanticsAttributes != MethodSemanticsAttributes.None)
                        continue;

                    if (!HasGeneratedName(m) && m.HasCustomAttributes)
                        InstantiateMulticastAttributes(m, ProviderType.Method);

                    if (m.HasParameters)
                    {
                        foreach (ParameterDefinition p in m.Parameters)
                        {
                            if (p.HasCustomAttributes)
                                InstantiateMulticastAttributes(p, ProviderType.Parameter);
                        }
                    }

                    if (m.MethodReturnType.HasCustomAttributes)
                        InstantiateMulticastAttributes(m.MethodReturnType, ProviderType.MethodReturn);
                }
            }

            if (type.HasProperties)
            {
                foreach (PropertyDefinition p in type.Properties)
                {
                    if (!HasGeneratedName(p) && p.HasCustomAttributes)
                        InstantiateMulticastAttributes(p, ProviderType.Property);

                    if (p.GetMethod != null)
                        InstantiateMulticastAttributes(p.GetMethod, ProviderType.Method);

                    if (p.SetMethod != null)
                        InstantiateMulticastAttributes(p.SetMethod, ProviderType.Method);
                }
            }

            if (type.HasEvents)
            {
                foreach (EventDefinition e in type.Events)
                {
                    if (!HasGeneratedName(e) && e.HasCustomAttributes)
                        InstantiateMulticastAttributes(e, ProviderType.Event);

                    if (e.AddMethod != null)
                        InstantiateMulticastAttributes(e.AddMethod, ProviderType.Method);

                    if (e.RemoveMethod != null)
                        InstantiateMulticastAttributes(e.RemoveMethod, ProviderType.Method);
                }
            }

            if (type.HasFields)
            {
                foreach (FieldDefinition f in type.Fields)
                {
                    if (!HasGeneratedName(f) && f.HasCustomAttributes)
                        InstantiateMulticastAttributes(f, ProviderType.Field);
                }
            }

            if (type.HasNestedTypes)
            {
                foreach (TypeDefinition nt in type.NestedTypes)
                {
                    if (!HasGeneratedName(nt) && !HasGeneratedAttribute(nt))
                        InstantiateMulticastAttributes(nt);
                }
            }
        }

        private void InstantiateMulticastAttributes(ICustomAttributeProvider provider, ProviderType dt)
        {
            List<MulticastInstance> instances = null;

            foreach (CustomAttribute a in provider.CustomAttributes)
            {
                TypeDefinition atype = a.AttributeType.Resolve();

                if (atype == _compilerGeneratedAttributeType)
                    return;

                if (!IsMulticastAttribute(atype))
                    continue;

                var mai = new MulticastInstance(provider, dt, a, atype);

                if (instances == null)
                    instances = new List<MulticastInstance>();
                instances.Add(mai);
            }

            if (instances == null)
                return;

            instances.Sort((a, b) => a.Priority.CompareTo(b.Priority));

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
                if (a.AttributeType.Resolve() == _compilerGeneratedAttributeType)
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
}
