using System;
using System.Collections.Generic;
using Mono.Cecil;
using Spinner.Extensibility;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weaving
{
    internal class AspectWeaver
    {
        private HashSet<MethodDefinition> _wroteInitFor; 

        internal AspectWeaver(AspectInstance instance)
        {
            Instance = instance;
            Context = instance.Aspect.Context;
        }

        internal AspectInstance Instance { get; }

        internal ModuleWeavingContext Context { get; }

        internal FieldReference AspectField { get; set; }

        public virtual void Weave()
        {
            MulticastTargets targetType = Instance.Target.GetMulticastTargetType();

            foreach (AdviceGroup group in Instance.Aspect.AdviceGroups)
            {
                AdviceInfo master = group.Master;

                switch (group.PointcutType)
                {
                    case null:
                    case PointcutType.Self:
                        if ((group.Master.ValidTargets & targetType) != 0)
                        {
                            var w = group.CreateWeaver(this, Instance.Target);
                            w.Weave();
                        }

                        break;
                    case PointcutType.Multicast:
                        var ma = new MulticastArguments
                        {
                            TargetElements = group.PointcutTargets & master.ValidTargets,
                            TargetMemberAttributes = group.PointcutAttributes,
                            TargetExternalMemberAttributes = group.PointcutAttributes,
                            TargetMembers = group.PointcutMemberName
                        };

                        foreach (IMetadataTokenProvider d in Context.MulticastEngine.GetDescendants(Instance.Target, ma))
                        {
                            var w = group.CreateWeaver(this, d);
                            w.Weave();
                        }
                        break;
                    case PointcutType.Method:
                        throw new NotImplementedException();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        internal bool NeedsAspectInit(MethodDefinition method)
        {
            return _wroteInitFor == null || !_wroteInitFor.Contains(method);
        }

        internal void NotifyWroteAspectInit(MethodDefinition method)
        {
            if (_wroteInitFor == null)
                _wroteInitFor = new HashSet<MethodDefinition>();
            _wroteInitFor.Add(method);
        }

        internal void CreateAspectCacheField()
        {
            if (AspectField != null)
                return;

            IMemberDefinition member;
            TypeDefinition hostType;
            switch (Instance.Target.GetProviderType())
            {
                case ProviderType.Assembly:
                    throw new InvalidOperationException();
                case ProviderType.Type:
                    member = hostType = (TypeDefinition) Instance.Target;
                    break;
                case ProviderType.Method:
                case ProviderType.Property:
                case ProviderType.Event:
                case ProviderType.Field:
                    member = (IMemberDefinition) Instance.Target;
                    hostType = member.DeclaringType;
                    break;
                case ProviderType.Parameter:
                    member = ((ParameterDefinition) Instance.Target).GetMethodDefinition();
                    hostType = member.DeclaringType;
                    break;
                case ProviderType.MethodReturn:
                    member = ((MethodReturnType) Instance.Target).GetMethodDefinition();
                    hostType = member.DeclaringType;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            string name = NameGenerator.MakeAspectFieldName(member.Name, Instance.Index);

            var fattrs = FieldAttributes.Private | FieldAttributes.Static;

            var aspectFieldDef = new FieldDefinition(name, fattrs, Context.SafeImport(Instance.Aspect.AspectType));
            AddCompilerGeneratedAttribute(aspectFieldDef);

            hostType.Fields.Add(aspectFieldDef);

            AspectField = aspectFieldDef;
        }

        internal void AddCompilerGeneratedAttribute(ICustomAttributeProvider definition)
        {
            MethodReference ctor = Context.SafeImport(Context.Framework.CompilerGeneratedAttribute_ctor);

            definition.CustomAttributes.Add(new CustomAttribute(ctor));
        }
    }
}