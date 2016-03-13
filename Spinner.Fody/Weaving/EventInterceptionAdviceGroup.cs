using Mono.Cecil;
using Spinner.Fody.Weaving.AdviceWeavers;

namespace Spinner.Fody.Weaving
{
    internal sealed class EventInterceptionAdviceGroup : AdviceGroup
    {
        public EventInterceptionAdviceGroup(AdviceInfo master)
            : base(master)
        {
            SetProperty(master);
        }

        internal AdviceInfo AddHandler { get; private set; }

        internal AdviceInfo RemoveHandler { get; private set; }

        internal AdviceInfo InvokeHandler { get; private set; }

        internal override AdviceWeaver CreateWeaver(AspectWeaver parent, IMetadataTokenProvider target)
        {
            return new EventInterceptionAdviceWeaver(parent, this, (EventDefinition) target);
        }

        internal override void AddChild(AdviceInfo advice)
        {
            SetProperty(advice);
            base.AddChild(advice);
        }

        private void SetProperty(AdviceInfo advice)
        {
            switch (advice.AdviceType)
            {
                case AdviceType.EventAddHandler:
                    ThrowIfDuplicate(AddHandler);
                    AddHandler = advice;
                    break;
                case AdviceType.EventRemoveHandler:
                    ThrowIfDuplicate(RemoveHandler);
                    RemoveHandler = advice;
                    break;
                case AdviceType.EventInvokeHandler:
                    ThrowIfDuplicate(InvokeHandler);
                    InvokeHandler = advice;
                    break;
                default:
                    ThrowInvalidAdviceForGroup(advice);
                    break;
            }
        }
    }
}