using System.Collections.Generic;
using Mono.Cecil;

namespace Spinner.Fody.Weavers
{
    internal class LocationLevelAspectWeaver : AspectWeaver
    {
        public LocationLevelAspectWeaver(AspectInfo aspect, IEnumerable<AdviceGroup> advices, ICustomAttributeProvider target)
            : base(aspect, advices, target)
        {
        }
    }
}