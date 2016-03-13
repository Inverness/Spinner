using System.Collections.Generic;
using Mono.Cecil;

namespace Spinner.Fody.Weavers
{
    internal class MethodLevelAspectWeaver : AspectWeaver
    {
        public MethodLevelAspectWeaver(AspectInfo aspect, IEnumerable<AdviceGroup> advices, MethodDefinition method)
            : base(aspect, advices, method)
        {
            TargetMethod = method;
        }

        internal MethodDefinition TargetMethod { get; }
    }
}