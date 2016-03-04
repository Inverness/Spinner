using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weavers.Prototype
{
    internal class AspectWeaver2
    {
        protected const string BindingInstanceFieldName = "Instance";
        protected const string StateMachineThisFieldName = "<>4__this";

        internal AspectWeaver2(
            ModuleWeavingContext mwc,
            MulticastInstance mi,
            int aspectIndex,
            IMemberDefinition aspectTarget)
        {
            Context = mwc;
            MulticastInstance = mi;
            AspectType = mi.AttributeType;
            AspectIndex = aspectIndex;
            AspectTarget = aspectTarget;
        }

        public ModuleWeavingContext Context { get; }

        public TypeDefinition AspectType { get; }

        public int AspectIndex { get; }

        public IMemberDefinition AspectTarget { get; }

        public MulticastInstance MulticastInstance { get; }

        public static AspectWeaver2 CreateWeaver(ModuleWeavingContext mwc, MulticastAttributeRegistry mar, TypeDefinition type)
        {
            

            return null;
        }

        public void Weave()
        {
            // Build advice plan
            if (AspectTarget is MethodDefinition)
                WeaveMethod((MethodDefinition) AspectTarget);
        }

        private void WeaveMethod(MethodDefinition method)
        {
            var weavers = new List<AdviceWeaver>();
            var finishedWeavers = new LinkedList<AdviceWeaver>();

            method.Body.SimplifyMacros();
            var existingNops = new HashSet<Instruction>(method.Body.Instructions.Where(i => i.OpCode == OpCodes.Nop));

            weavers.Add(new AspectFieldWeaver(this, null));
            weavers.Add(new AspectInitWeaver(this, null));
            weavers.Add(new MethodArgsInitWeaver(this, null));
            weavers.Add(new MethodExecutionArgsInitWeaver(this, null));
            weavers.Add(new MethodEntryAdviceWeaver(this, AspectType.Methods.FirstOrDefault(m => m.Name == "OnEntry")));

            Collection<Instruction> insc = method.Body.Instructions;
            int eoffset = insc.Count;
            for (int i = 0; i < weavers.Count; i++)
            {
                AdviceWeaver w = weavers[i];
                w.Weave(method, null, insc.Count - eoffset, finishedWeavers);
                finishedWeavers.AddFirst(w);
            }

            method.Body.RemoveNops(existingNops);
            method.Body.OptimizeMacros();
            method.Body.UpdateOffsets();
        }

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

    //internal class AdviceWeaverContext
    //{
    //    public FieldReference AspectField { get; set; }

    //    public VariableDefinition AdviceArgsVariable { get; set; }

    //    public FieldReference AdviceArgsField { get; set; }

    //    public VariableDefinition MethodArgsVariable { get; set; }
    //}
}
