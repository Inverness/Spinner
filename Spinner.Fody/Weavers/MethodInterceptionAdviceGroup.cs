using System.Diagnostics;
using Mono.Cecil;

namespace Spinner.Fody.Weavers
{
    internal sealed class MethodInterceptionAdviceGroup : AdviceGroup
    {
        public MethodInterceptionAdviceGroup(AdviceInfo master)
            : base(master)
        {
            SetProperty(master);
        }

        internal AdviceInfo Invoke { get; private set; }

        internal override AdviceWeaver CreateWeaver(AspectWeaver parent, ICustomAttributeProvider target)
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