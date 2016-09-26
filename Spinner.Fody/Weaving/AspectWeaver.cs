using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using NLog;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weaving
{
    internal class AspectWeaver
    {
        private HashSet<MethodDefinition> _wroteInitFor;

        private static readonly Logger s_log = LogManager.GetCurrentClassLogger();

        internal AspectWeaver(AspectInstance instance)
        {
            Instance = instance;
            Context = instance.Aspect.Context;
        }

        internal AspectInstance Instance { get; }

        internal SpinnerContext Context { get; }

        internal FieldDefinition AspectField { get; set; }

        public virtual void Weave()
        {
            ICustomAttributeProvider target = Instance.Target;

            s_log.Trace("Begin weaving aspect {0} for target {1} {2} with {3} advice groups",
                        Instance.Aspect.AspectType.Name,
                        target.GetProviderType(),
                        target.GetName(),
                        Instance.Aspect.AdviceGroups.Count);

            foreach (AdviceGroup group in Instance.Aspect.AdviceGroups)
            {
                s_log.Trace("  Applying group {0} with pointcut type {1}",
                            group.Master.Source.GetName(),
                            group.PointcutType ?? PointcutType.Self);

                AdviceInfo master = group.Master;

                switch (group.PointcutType)
                {
                    case null:
                    case PointcutType.Self:
                        if ((group.Master.ValidTargets & target.GetMulticastTargetType()) != 0)
                        {
                            group.CreateWeaver(this, target).Weave();
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

                        foreach (IMetadataTokenProvider d in Context.MulticastEngine.GetDescendants(target, ma))
                        {
                            s_log.Trace("    Apply to {0} {1}", d.GetProviderType(), d.GetName());
                            group.CreateWeaver(this, d).Weave();
                        }

                        break;

                    case PointcutType.Method:
                        string methodName = group.PointcutMethodName;
                        MethodDefinition methodDef = Instance.Aspect.AspectType.Methods.First(m => m.Name == methodName);

                        Debug.Assert(target is IMemberDefinition, "target is IMemberDefinition");

                        TypeDefinition targetTypeDef = target as TypeDefinition;
                        if (targetTypeDef == null)
                            targetTypeDef = ((IMemberDefinition) target).DeclaringType;

                        MemberReference[] members = Context.BuildTimeExecutionEngine.ExecuteMethodPointcut(methodDef, targetTypeDef);

                        foreach (MemberReference m in members)
                        {
                            if ((m.GetMulticastTargetType() & master.ValidTargets) != 0)
                            {
                                s_log.Trace("    Apply to {0} {1}", m.GetProviderType(), m.GetName());
                                group.CreateWeaver(this, m.Resolve()).Weave();
                            }
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            s_log.Trace("Finished weaving");
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

            var aspectFieldDef = new FieldDefinition(name, fattrs, Context.Import(Instance.Aspect.AspectType));
            AddCompilerGeneratedAttribute(aspectFieldDef);

            hostType.Fields.Add(aspectFieldDef);

            AspectField = aspectFieldDef;
        }

        internal void AddCompilerGeneratedAttribute(ICustomAttributeProvider definition)
        {
            MethodReference ctor = Context.Import(Context.Framework.CompilerGeneratedAttribute_ctor);

            definition.CustomAttributes.Add(new CustomAttribute(ctor));
        }
    }
}