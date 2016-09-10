using System;
using System.Collections.Generic;
using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weaving
{
    /// <summary>
    /// Creates aspect weavers for all aspect applied to a type and its members.
    /// </summary>
    internal static class AspectWeaverFactory
    {
        public static AspectWeaver[] TryCreate(ModuleWeavingContext mwc, MulticastAttributeRegistry mar, TypeDefinition type)
        {
            // Identifies aspects that have been applied to the type first

            List<AspectInstance> aspects = null;
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

            // Create a weaver for each aspect

            var weavers = new List<AspectWeaver>();

            foreach (AspectInstance inst in aspects)
            {
                ICustomAttributeProvider target = inst.Target;
                AspectWeaver weaver = null;

                switch (target.GetProviderType())
                {
                    case ProviderType.Assembly:
                        break;
                    case ProviderType.Type:
                        weaver = new TypeLevelAspectWeaver(inst);
                        break;
                    case ProviderType.Method:
                        weaver = new MethodLevelAspectWeaver(inst);
                        break;
                    case ProviderType.Property:
                        weaver = new LocationLevelAspectWeaver(inst);
                        break;
                    case ProviderType.Event:
                        weaver = new EventLevelAspectWeaver(inst);
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

        private static void AddAspects(
            ModuleWeavingContext mwc,
            MulticastAttributeRegistry mar,
            ICustomAttributeProvider target,
            ref List<AspectInstance> aspects,
            ref int orderCounter)
        {
            IReadOnlyList<MulticastAttributeInstance> multicasts = mar.GetMulticasts(target);
            if (multicasts.Count == 0)
                return;

            foreach (MulticastAttributeInstance m in multicasts)
            {
                var info = mwc.GetAspectInfo(m.AttributeType);

                if (info != null)
                {
                    var instance = new AspectInstance(info, m, target, mwc.NewAspectIndex(), orderCounter++);

                    if (aspects == null)
                        aspects = new List<AspectInstance>();
                    aspects.Add(instance);
                }
            }
        }
    }
}