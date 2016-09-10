using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weaving
{
    /// <summary>
    /// Creates an AspectInfo by examining the members of a type.
    /// </summary>
    internal static class AspectInfoFactory
    {
        internal static AspectInfo Create(ModuleWeavingContext context, TypeDefinition type, AspectKind kind)
        {
            var info = new AspectInfo(context, type, kind);
            var advices = new List<AdviceInfo>();
            WellKnownSpinnerMembers spinner = context.Spinner;

            switch (info.Kind)
            {
                case AspectKind.Composed:
                    AddAdvices(info, info.AspectType, advices);

                    foreach (IMemberDefinition member in info.AspectType.GetMembers())
                    {
                        AddAdvices(info, member, advices);

                        var method = member as MethodDefinition;
                        if (method != null)
                        {
                            if (!method.IsReturnVoid())
                                AddAdvices(info, method.MethodReturnType, advices);

                            if (method.HasParameters)
                            {
                                foreach (ParameterDefinition p in method.Parameters)
                                    AddAdvices(info, p, advices);
                            }
                        }
                    }
                    break;
                case AspectKind.MethodBoundary:
                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnEntry,
                                         AdviceType.MethodEntry,
                                         spinner.IMethodBoundaryAspect_OnEntry);

                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnExit,
                                         AdviceType.MethodExit,
                                         spinner.IMethodBoundaryAspect_OnExit);

                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnSuccess,
                                         AdviceType.MethodSuccess,
                                         spinner.IMethodBoundaryAspect_OnSuccess);

                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnException,
                                         AdviceType.MethodException,
                                         spinner.IMethodBoundaryAspect_OnException);

                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnException,
                                         AdviceType.MethodFilterException,
                                         spinner.IMethodBoundaryAspect_FilterException);

                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnYield,
                                         AdviceType.MethodYield,
                                         spinner.IMethodBoundaryAspect_OnYield);

                    AddAspectClassAdvice(info,
                                         advices,
                                         Features.OnResume,
                                         AdviceType.MethodResume,
                                         spinner.IMethodBoundaryAspect_OnResume);

                    GroupAspectClassAdvices(info, advices);

                    break;
                case AspectKind.MethodInterception:
                    AddAspectClassAdvice(info,
                                         advices,
                                         null,
                                         AdviceType.MethodInvoke,
                                         spinner.IMethodInterceptionAspect_OnInvoke);
                    break;
                case AspectKind.PropertyInterception:
                    AddAspectClassAdvice(info,
                                         advices,
                                         null,
                                         AdviceType.LocationGetValue,
                                         spinner.ILocationInterceptionAspect_OnGetValue);

                    AddAspectClassAdvice(info,
                                         advices,
                                         null,
                                         AdviceType.LocationSetValue,
                                         spinner.ILocationInterceptionAspect_OnSetValue);

                    GroupAspectClassAdvices(info, advices);

                    break;
                case AspectKind.EventInterception:
                    AddAspectClassAdvice(info,
                                         advices,
                                         null,
                                         AdviceType.EventAddHandler,
                                         spinner.IEventInterceptionAspect_OnAddHandler);

                    AddAspectClassAdvice(info,
                                         advices,
                                         null,
                                         AdviceType.EventRemoveHandler,
                                         spinner.IEventInterceptionAspect_OnRemoveHandler);

                    AddAspectClassAdvice(info,
                                         advices,
                                         null,
                                         AdviceType.EventInvokeHandler,
                                         spinner.IEventInterceptionAspect_OnInvokeHandler);

                    GroupAspectClassAdvices(info, advices);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            
            // Create groups

            var groups = new List<AdviceGroup>();

            foreach (AdviceInfo advice in advices)
            {
                if (advice.Master == null)
                {
                    groups.Add(CreateGroup(advice));
                }
                else
                {
                    foreach (AdviceInfo other in advices)
                    {
                        if (advice.Aspect == other.Aspect && advice.Master == ((IMemberDefinition) other.Source).Name)
                        {
                            advice.MasterObject = other;
                            break;
                        }
                    }

                    if (advice.MasterObject == null)
                        throw new ValidationException("invalid master: " + advice.Master);
                }
            }

            foreach (AdviceInfo advice in advices)
            {
                if (advice.MasterObject == null)
                    continue;

                var masterGroup = groups.FirstOrDefault(g => g.Master == advice.MasterObject);
                Debug.Assert(masterGroup != null);
                masterGroup.AddChild(advice);
            }

            foreach (AdviceGroup g in groups)
                info.AddAdviceGroup(g);

            return info;
        }

        private static void AddAdvices(
            AspectInfo aspect,
            ICustomAttributeProvider source,
            List<AdviceInfo> results)
        {
            if (!source.HasCustomAttributes)
                return;

            IReadOnlyDictionary<TypeReference, AdviceType> adviceTypes = aspect.Context.AdviceTypes;

            foreach (CustomAttribute attr in source.CustomAttributes)
            {
                AdviceType type;
                if (!adviceTypes.TryGetValue(attr.AttributeType, out type))
                    continue;

                AdviceInfo result = CreateAdviceInfo(type, aspect, source, attr);

                results.Add(result);
            }
        }

        private static AdviceInfo CreateAdviceInfo(
            AdviceType type,
            AspectInfo aspect,
            IMetadataTokenProvider source,
            CustomAttribute attr,
            string master = null)
        {
            AdviceInfo result;
            switch (type)
            {
                case AdviceType.MethodEntry:
                case AdviceType.MethodExit:
                case AdviceType.MethodSuccess:
                case AdviceType.MethodException:
                case AdviceType.MethodFilterException:
                case AdviceType.MethodYield:
                case AdviceType.MethodResume:
                    result = new MethodBoundaryAdviceInfo(type, aspect, (MethodDefinition) source, attr);
                    break;
                case AdviceType.MethodInvoke:
                    result = new MethodInterceptionAdviceInfo(aspect, (MethodDefinition) source, attr);
                    break;
                case AdviceType.LocationGetValue:
                case AdviceType.LocationSetValue:
                    result = new LocationInterceptionAdviceInfo(type, aspect, (PropertyDefinition) source, attr);
                    break;
                case AdviceType.EventAddHandler:
                case AdviceType.EventRemoveHandler:
                case AdviceType.EventInvokeHandler:
                    result = new EventInterceptionAdviceInfo(type, aspect, (EventDefinition) source, attr);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (master != null)
                result.Master = master;

            return result;
        }

        private static AdviceGroup CreateGroup(AdviceInfo advice)
        {
            switch (advice.AdviceType)
            {
                case AdviceType.MethodEntry:
                case AdviceType.MethodExit:
                case AdviceType.MethodSuccess:
                case AdviceType.MethodException:
                case AdviceType.MethodFilterException:
                case AdviceType.MethodYield:
                case AdviceType.MethodResume:
                    return new MethodBoundaryAdviceGroup(advice);
                case AdviceType.MethodInvoke:
                    return new MethodInterceptionAdviceGroup(advice);
                case AdviceType.LocationGetValue:
                case AdviceType.LocationSetValue:
                    return new LocationInterceptionAdviceGroup(advice);
                case AdviceType.EventAddHandler:
                case AdviceType.EventRemoveHandler:
                case AdviceType.EventInvokeHandler:
                    return new EventInterceptionAdviceGroup(advice);
                default:
                    throw new NotImplementedException();
            }
        }

        private static void AddAspectClassAdvice(
            AspectInfo aspect,
            List<AdviceInfo> advices,
            Features? typeFeature,
            AdviceType adviceType,
            MethodReference interfaceMethod)
        {
            if (typeFeature == null || aspect.Features.Has(typeFeature.Value))
            {
                MethodDefinition md = aspect.AspectType.GetMethod(interfaceMethod, true);

                advices.Add(CreateAdviceInfo(adviceType, aspect, md, null));
            }
        }

        private static void GroupAspectClassAdvices(AspectInfo aspect, List<AdviceInfo> advices)
        {
            int first = advices.Count;
            for (int i = advices.Count - 1; i > -1; i--)
            {
                if (advices[i].Aspect == aspect)
                {
                    first = i;
                }
                else
                {
                    break;
                }
            }

            string masterName = null;
            for (int i = first; i < advices.Count; i++)
            {
                if (masterName == null)
                {
                    masterName = advices[i].Source.GetName();
                }
                else
                {
                    advices[i].Master = masterName;
                }
            }
        }
    }
}
