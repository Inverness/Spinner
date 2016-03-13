using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weavers
{
    internal class AspectWeaverFactory
    {
        public static AspectWeaver[] TryCreate(ModuleWeavingContext mwc, MulticastAttributeRegistry mar, TypeDefinition type)
        {
            // Identifies aspects that have been applied to the type first

            List<AspectInfo> aspects = null;
            int orderCounter = 0;

            AddAspects(mwc, mar, type, ref aspects, ref orderCounter);

            if (type.HasProperties)
            {
                foreach (PropertyDefinition prop in type.Properties)
                {
                    AddAspects(mwc, mar, prop, ref aspects, ref orderCounter);
                }
            }

            if (type.HasEvents)
            {
                foreach (EventDefinition evt in type.Events)
                {
                    AddAspects(mwc, mar, evt, ref aspects, ref orderCounter);
                }
            }

            if (type.HasMethods)
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    AddAspects(mwc, mar, method, ref aspects, ref orderCounter);

                    if (method.HasParameters)
                    {
                        foreach (ParameterDefinition parameter in method.Parameters)
                        {
                            AddAspects(mwc, mar, parameter, ref aspects, ref orderCounter);
                        }
                    }

                    AddAspects(mwc, mar, method.MethodReturnType, ref aspects, ref orderCounter);
                }
            }

            if (aspects == null)
                return null;

            // Create AdviceInfo instances for everything that is applicable.

            var advices = new List<AdviceInfo>();
            
            foreach (AspectInfo aspect in aspects)
            {
                AddAdvices(mwc, aspect, aspect.AspectType, advices);

                foreach (IMemberDefinition member in aspect.AspectType.GetMembers())
                {
                    AddAdvices(mwc, aspect, member, advices);

                    var method = member as MethodDefinition;
                    if (method != null)
                    {
                        if (!method.IsReturnVoid())
                            AddAdvices(mwc, aspect, method.MethodReturnType, advices);

                        if (method.HasParameters)
                        {
                            foreach (ParameterDefinition p in method.Parameters)
                                AddAdvices(mwc, aspect, p, advices);
                        }
                    }
                }
            }

            var groups = CreateGroups(advices);

            // Create a weaver for each aspect

            var weavers = new List<AspectWeaver>();

            foreach (IGrouping<AspectInfo, AdviceGroup> groupsByAspect in groups.GroupBy(g => g.Master.Aspect))
            {
                ICustomAttributeProvider target = groupsByAspect.Key.Target;
                AspectWeaver weaver = null;
                switch (target.GetProviderType())
                {
                    case ProviderType.Assembly:
                        break;
                    case ProviderType.Type:
                        weaver = new TypeLevelAspectWeaver(groupsByAspect.Key, groupsByAspect, (TypeDefinition) target);
                        break;
                    case ProviderType.Method:
                        weaver = new MethodLevelAspectWeaver(groupsByAspect.Key, groupsByAspect, (MethodDefinition) target);
                        break;
                    case ProviderType.Property:
                        weaver = new LocationLevelAspectWeaver(groupsByAspect.Key, groupsByAspect, (PropertyDefinition) target);
                        break;
                    case ProviderType.Event:
                        weaver = new EventLevelAspectWeaver(groupsByAspect.Key, groupsByAspect, (EventDefinition) target);
                        break;
                    case ProviderType.Field:
                        break;
                    case ProviderType.Parameter:
                        break;
                    case ProviderType.MethodReturn:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (weaver != null)
                    weavers.Add(weaver);
            }

            return weavers.ToArray();
        }

        private static void AddAspects(ModuleWeavingContext mwc, MulticastAttributeRegistry mar, ICustomAttributeProvider target, ref List<AspectInfo> aspects, ref int orderCounter)
        {
            IReadOnlyList<MulticastAttributeInstance> multicasts = mar.GetMulticasts(target);
            if (multicasts.Count == 0)
                return;

            TypeDefinition aspectInterfaceType = mwc.Spinner.IAspect;

            foreach (MulticastAttributeInstance m in multicasts)
            {
                //Features? features = mwc.GetFeatures(m.AttributeType);
                //if (features.HasValue)
                //{
                //    var info = new AspectInfo(mwc, m, target, mwc.NewAspectIndex(), orderCounter++);

                //    if (aspects == null)
                //        aspects = new List<AspectInfo>();
                //    aspects.Add(info);
                //}
                if (m.AttributeType.HasInterface(aspectInterfaceType, true))
                {
                    var info = new AspectInfo(mwc, m, target, mwc.NewAspectIndex(), orderCounter++);

                    if (aspects == null)
                        aspects = new List<AspectInfo>();
                    aspects.Add(info);
                }
            }
        }

        private static void AddAdvices(ModuleWeavingContext mwc, AspectInfo aspect, ICustomAttributeProvider source, List<AdviceInfo> results)
        {
            if (!source.HasCustomAttributes)
                return;

            foreach (CustomAttribute a in source.CustomAttributes)
            {
                AdviceType type;
                if (!mwc.AdviceTypes.TryGetValue(a.AttributeType, out type))
                    continue;

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
                        result = new MethodBoundaryAdviceInfo(type, aspect, (MethodDefinition) source, a);
                        break;
                    case AdviceType.MethodInvoke:
                        result = new MethodInterceptionAdviceInfo(aspect, (MethodDefinition) source, a);
                        break;
                    //case AdviceType.LocationGetValue:
                    //case AdviceType.LocationSetValue:
                    //    break;
                    //case AdviceType.EventAddHandler:
                    //case AdviceType.EventRemoveHandler:
                    //case AdviceType.EventInvokeHandler:
                    //    break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                results.Add(result);
            }
        }

        protected static List<AdviceGroup> CreateGroups(IReadOnlyList<AdviceInfo> advices)
        {
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

            return groups;
        }

        protected static AdviceGroup CreateGroup(AdviceInfo advice)
        {
            switch (advice.AdviceType)
            {
                case AdviceType.MethodEntry:
                case AdviceType.MethodExit:
                case AdviceType.MethodSuccess:
                case AdviceType.MethodException:
                case AdviceType.MethodYield:
                case AdviceType.MethodResume:
                    return new MethodBoundaryAdviceGroup(advice);
                case AdviceType.MethodInvoke:
                    return new MethodInterceptionAdviceGroup(advice);
                default:
                    throw new NotImplementedException();
            }
        }
    }
}