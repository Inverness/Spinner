using System.Diagnostics;
using Mono.Cecil;
using Spinner.Fody.Weaving.AdviceWeavers;

namespace Spinner.Fody.Weaving
{
    internal sealed class MethodInterceptionAdviceGroup : AdviceGroup
    {
        public MethodInterceptionAdviceGroup(AdviceInfo master)
            : base(master)
        {
            SetProperty(master);
        }

        internal AdviceInfo Invoke { get; private set; }

        internal override AdviceWeaver CreateWeaver(AspectWeaver parent, IMetadataTokenProvider target)
        {
            return new MethodInterceptionAdviceWeaver(parent, Invoke, (MethodDefinition) target);
        }

        internal override void AddChild(AdviceInfo advice)
        {
            Debug.Assert(Invoke != null);
            ThrowInvalidAdviceForGroup(advice);
        }

        private void SetProperty(AdviceInfo advice)
        {
            if (advice.AdviceType == AdviceType.MethodInvoke)
            {
                ThrowIfDuplicate(Invoke);
                Invoke = advice;
            }
            ThrowInvalidAdviceForGroup(advice);
        }
    }
}