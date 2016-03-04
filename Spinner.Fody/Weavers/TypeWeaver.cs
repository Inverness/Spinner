using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weavers
{
    internal class TypeWeaver
    {
        protected const string BindingInstanceFieldName = "Instance";
        protected const string StateMachineThisFieldName = "<>4__this";

        private readonly List<AspectInfo> _aspects;

        private TypeWeaver(ModuleWeavingContext context, TypeDefinition type, IList<AspectInfo> aspects)
        {
            Context = context;
            TargetType = type;
            _aspects = new List<AspectInfo>(aspects);
        }

        public ModuleWeavingContext Context { get; }

        public TypeDefinition TargetType { get; }

        public void Weave()
        {
            TypeDefinition methodEntryAdviceType = Context.Spinner.MethodEntryAdvice;

            // Collect all advices that have been specified in the aspect

            var advices = new List<AdviceInfo>();//new MultiValueDictionary<ICustomAttributeProvider, AdviceInfo>();

            foreach (AspectInfo aspect in _aspects)
            {
                foreach (MethodDefinition method in aspect.AspectType.Methods)
                {
                    if (method.HasCustomAttributes)
                    {
                        foreach (CustomAttribute ca in method.CustomAttributes)
                        {
                            if (ca.AttributeType.IsSame(methodEntryAdviceType))
                            {
                                advices.Add(new MethodBoundaryAdviceInfo(AdviceType.MethodEntry, aspect, method, ca));
                            }
                        }
                    }
                }
            }

            var targets = new MultiValueDictionary<ICustomAttributeProvider, AdviceInfo>();

            Func<MethodDefinition, bool> isValidTest =
                m =>
                m.IsPublic && !m.IsStatic && !m.IsAbstract && m.HasBody &&
                m.SemanticsAttributes == MethodSemanticsAttributes.None;

            foreach (AdviceInfo a in advices)
            {
                if (a.AdviceType == AdviceType.MethodEntry)
                {
                    var targetType = (TypeDefinition) a.Aspect.Target;

                    IEnumerable<MethodDefinition> adviceTargets = targetType.Methods.Where(isValidTest);

                    foreach (MethodDefinition target in adviceTargets)
                    {
                        targets.Add(target, a);
                    }
                }
            }

            foreach (KeyValuePair<ICustomAttributeProvider, IReadOnlyCollection<AdviceInfo>> t in targets)
            {
                var method = t.Key as MethodDefinition;
                if (method != null)
                {
                    WeaveMethod(method, t.Value);   
                }
            }
        }

        private void WeaveMethod(MethodDefinition method, IReadOnlyCollection<AdviceInfo> advicesSource)
        {
            // A method can have multiple method boundary (change existing code) or method interception (duplicate method)
            // code. Need to organize advices so that this happens in the correct order and that method boundary
            // advices from different aspects can be optimized.
            
            AdviceInfo[] advices = advicesSource.ToArray();
            var insertionScope = new List<AdviceInfo>();

            for (int i = 0; i < advices.Length; i++)
            {
                if (!IsInterceptionAdvice(advices[i].AdviceType))
                {
                    insertionScope.Add(advices[i]);
                }
                else
                {
                    if (insertionScope.Count != 0)
                    {
                        WeaveMethodInsertions(method, insertionScope);
                        insertionScope.Clear();
                    }

                    WeaveMethodInterception(method, advices[i]);
                }
            }

            if (insertionScope.Count != 0)
            {
                WeaveMethodInsertions(method, insertionScope);
                insertionScope.Clear();
            }
        }

        private void WeaveMethodInsertions(MethodDefinition method, IReadOnlyList<AdviceInfo> advices)
        {
            foreach (AspectInfo a in advices.Select(a => a.Aspect).Distinct())
            {
                // write aspect init
            }

            foreach (AdviceInfo a in advices.Where(a => a.AdviceType == AdviceType.MethodEntry))
            {
                // write entry advices
            }
        }

        private void WeaveMethodInterception(MethodDefinition method, AdviceInfo advice)
        {
            
        }

        private static bool IsInterceptionAdvice(AdviceType type)
        {
            switch (type)
            {
                case AdviceType.MethodInvoke:
                case AdviceType.LocationGetValue:
                case AdviceType.LocationSetValue:
                case AdviceType.EventAddHandler:
                case AdviceType.EventRemoveHandler:
                case AdviceType.EventInvokeHandler:
                    return true;
                default:
                    return false;
            }
        }

        public static TypeWeaver Create(ModuleWeavingContext mwc, MulticastAttributeRegistry mar, TypeDefinition type)
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
                }
            }

            if (aspects == null)
                return null;

            return new TypeWeaver(mwc, type, aspects);
        }

        private static void AddAspects(ModuleWeavingContext mwc, MulticastAttributeRegistry mar, ICustomAttributeProvider target, ref List<AspectInfo> aspects, ref int orderCounter)
        {
            TypeDefinition aspectInterfaceType = mwc.Spinner.IAspect;

            foreach (MulticastInstance m in mar.GetMulticasts(target))
            {
                if (m.AttributeType.HasInterface(aspectInterfaceType, true))
                {
                    var info = new AspectInfo(mwc, m, target, mwc.NewAspectIndex(), orderCounter++);

                    if (aspects == null)
                        aspects = new List<AspectInfo>();
                    aspects.Add(info);
                }
            }
        }

        //private void WeaveMethod(MethodDefinition method)
        //{
        //    var weavers = new List<AdviceWeaver>();
        //    var finishedWeavers = new LinkedList<AdviceWeaver>();

        //    method.Body.SimplifyMacros();
        //    var existingNops = new HashSet<Instruction>(method.Body.Instructions.Where(i => i.OpCode == OpCodes.Nop));

        //    weavers.Add(new AspectFieldWeaver(this, null));
        //    weavers.Add(new AspectInitWeaver(this, null));
        //    weavers.Add(new MethodArgsInitWeaver(this, null));
        //    weavers.Add(new MethodExecutionArgsInitWeaver(this, null));
        //    weavers.Add(new MethodEntryAdviceWeaver(this, AspectType.Methods.FirstOrDefault(m => m.Name == "OnEntry")));

        //    Collection<Instruction> insc = method.Body.Instructions;
        //    int eoffset = insc.Count;
        //    for (int i = 0; i < weavers.Count; i++)
        //    {
        //        AdviceWeaver w = weavers[i];
        //        w.Weave(method, null, insc.Count - eoffset);
        //        finishedWeavers.AddFirst(w);
        //    }

        //    method.Body.RemoveNops(existingNops);
        //    method.Body.OptimizeMacros();
        //    method.Body.UpdateOffsets();
        //}

        /// <summary>
        /// Get the features declared for an advice. AnalzyedFeaturesAttribute takes precedence over FeaturesAttribute.
        /// </summary>
        internal Features GetFeatures(MethodDefinition advice)
        {
            TypeDefinition attrType = Context.Spinner.FeaturesAttribute;
            TypeDefinition analyzedAttrType = Context.Spinner.AnalyzedFeaturesAttribute;

            Features? features = null;

            MethodDefinition current = advice;
            while (current != null)
            {
                if (current.HasCustomAttributes)
                {
                    foreach (CustomAttribute a in current.CustomAttributes)
                    {
                        TypeReference atype = a.AttributeType;

                        if (atype.IsSame(analyzedAttrType))
                        {
                            return (Features) (uint) a.ConstructorArguments.First().Value;
                        }

                        if (atype.IsSame(attrType))
                        {
                            features = (Features) (uint) a.ConstructorArguments.First().Value;
                            // Continue in case AnalyzedFeaturesAttribute is found on same type.
                        }
                    }
                }

                if (features.HasValue)
                    return features.Value;

                current = current.HasOverrides ? current.Overrides.Single().Resolve() : null;
            }

            return Features.None;
        }
    }
}
