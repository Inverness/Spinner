using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Weaving
{
    internal sealed class EventInterceptionAdviceInfo : AdviceInfo
    {
        public EventInterceptionAdviceInfo(AdviceType type, AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
            AdviceType = type;
        }

        public override AdviceType AdviceType { get; }

        public override MulticastTargets ValidTargets => MulticastTargets.Event;
    }
}