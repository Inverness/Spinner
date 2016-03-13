using System.Collections.Generic;
using Mono.Cecil;

namespace Spinner.Fody.Weaving
{
    internal class EventLevelAspectWeaver : AspectWeaver
    {
        public EventLevelAspectWeaver(AspectInfo aspect, IEnumerable<AdviceGroup> advices, EventDefinition target)
            : base(aspect, advices, target)
        {
            TargetEvent = target;
        }

        public EventDefinition TargetEvent { get; }
    }
}