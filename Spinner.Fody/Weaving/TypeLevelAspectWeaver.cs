using System.Collections.Generic;
using Mono.Cecil;

namespace Spinner.Fody.Weaving
{
    internal class TypeLevelAspectWeaver : AspectWeaver
    {
        public TypeLevelAspectWeaver(AspectInfo aspect, IEnumerable<AdviceGroup> advices, TypeDefinition target)
            : base(aspect, advices, target)
        {
            TargetType = target;
        }

        public TypeDefinition TargetType { get; }
    }
}