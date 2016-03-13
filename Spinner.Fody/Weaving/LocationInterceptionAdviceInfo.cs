using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Weaving
{
    internal sealed class LocationInterceptionAdviceInfo : AdviceInfo
    {
        public LocationInterceptionAdviceInfo(AdviceType type, AspectInfo aspect, PropertyDefinition source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
            AdviceType = type;
        }

        public override AdviceType AdviceType { get; }

        public override MulticastTargets ValidTargets => MulticastTargets.Property;
    }
}