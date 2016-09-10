using Mono.Cecil;

namespace Spinner.Fody.Weaving
{
    internal class EventLevelAspectWeaver : AspectWeaver
    {
        public EventLevelAspectWeaver(AspectInstance instance)
            : base(instance)
        {
            TargetEvent = (EventDefinition) instance.Target;
        }

        public EventDefinition TargetEvent { get; }
    }
}