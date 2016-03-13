using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Weaving
{
    internal sealed class MethodInterceptionAdviceInfo : AdviceInfo
    {
        public MethodInterceptionAdviceInfo(AspectInfo aspect, MethodDefinition source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
        }

        public override AdviceType AdviceType => AdviceType.MethodInvoke;

        public override MulticastTargets ValidTargets => MulticastTargets.Method;
    }
}