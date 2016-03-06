using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Spinner.Fody.Weavers
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
            foreach (AdviceGroup g in AdviceGroups)
            {
                var w = g.CreateWeaver(this, null);
                w.Weave();
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
    }
}