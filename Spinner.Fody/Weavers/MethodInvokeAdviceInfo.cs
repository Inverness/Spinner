using Mono.Cecil;

namespace Spinner.Fody.Weavers
{
    internal sealed class MethodInvokeAdviceInfo : AdviceInfo
    {
        public MethodInvokeAdviceInfo(AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
        }

        public override AdviceType AdviceType => AdviceType.MethodInvoke;
    }
}