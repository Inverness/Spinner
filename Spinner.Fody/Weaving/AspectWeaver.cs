using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Spinner.Extensibility;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weaving
{
    internal class AspectWeaver
    {
        private HashSet<MethodDefinition> _wroteInitFor; 

        internal AspectWeaver(AspectInfo aspect, IEnumerable<AdviceGroup> advices, ICustomAttributeProvider target)
        {
            Aspect = aspect;
            AdviceGroups = advices.ToArray();
            Target = target;
            Context = aspect.Context;
        }

        internal AspectInfo Aspect { get; }

        internal IReadOnlyList<AdviceGroup> AdviceGroups { get; }

        internal ICustomAttributeProvider Target { get; }

        internal ModuleWeavingContext Context { get; }

        internal FieldReference AspectField { get; set; }

        public virtual void Weave()
        {
            MulticastTargets targetType = Target.GetMulticastTargetType();

            foreach (AdviceGroup group in AdviceGroups)
            {
                AdviceInfo master = group.Master;

                switch (group.PointcutType)
                {
                    case null:
                    case PointcutType.Self:
                        if ((group.Master.ValidTargets & targetType) != 0)
                        {
                            var w = group.CreateWeaver(this, Target);
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

                        foreach (IMetadataTokenProvider d in Context.MulticastEngine.GetDescendants(Target, ma))
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
            switch (Target.GetProviderType())
            {
                case ProviderType.Assembly:
                    throw new InvalidOperationException();
                case ProviderType.Type:
                    member = hostType = (TypeDefinition) Target;
                    break;
                case ProviderType.Method:
                case ProviderType.Property:
                case ProviderType.Event:
                case ProviderType.Field:
                    member = (IMemberDefinition) Target;
                    hostType = member.DeclaringType;
                    break;
                case ProviderType.Parameter:
                    member = ((ParameterDefinition) Target).GetMethodDefinition();
                    hostType = member.DeclaringType;
                    break;
                case ProviderType.MethodReturn:
                    member = ((MethodReturnType) Target).GetMethodDefinition();
                    hostType = member.DeclaringType;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            string name = NameGenerator.MakeAspectFieldName(member.Name, Aspect.Index);

            var fattrs = FieldAttributes.Private | FieldAttributes.Static;

            var aspectFieldDef = new FieldDefinition(name, fattrs, Context.SafeImport(Aspect.AspectType));
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