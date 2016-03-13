using Mono.Cecil;

namespace Spinner.Fody.Weavers
{
    internal sealed class LocationInterceptionAdviceGroup : AdviceGroup
    {
        public LocationInterceptionAdviceGroup(AdviceInfo master)
            : base(master)
        {
            SetProperty(master);
        }

        internal AdviceInfo GetValue { get; private set; }

        internal AdviceInfo SetValue { get; private set; }

        internal override AdviceWeaver CreateWeaver(AspectWeaver parent, IMetadataTokenProvider target)
        {
            // Only properties supported for now.
            return new PropertyInterceptionAdviceWeaver(parent, this, (PropertyDefinition) target);
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
                case AdviceType.LocationGetValue:
                    ThrowIfDuplicate(GetValue);
                    GetValue = advice;
                    break;
                case AdviceType.LocationSetValue:
                    ThrowIfDuplicate(SetValue);
                    SetValue = advice;
                    break;
                default:
                    ThrowInvalidAdviceForGroup(advice);
                    break;
            }
        }
    }
}