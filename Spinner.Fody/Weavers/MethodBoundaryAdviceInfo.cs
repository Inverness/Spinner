using Mono.Cecil;
using Spinner.Aspects.Advices;
using Spinner.Extensibility;

namespace Spinner.Fody.Weavers
{
    internal sealed class MethodBoundaryAdviceInfo : AdviceInfo
    {
        internal MethodBoundaryAdviceInfo(AdviceType type, AspectInfo aspect, MethodDefinition source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
            AdviceType = type;
        }

        public override AdviceType AdviceType { get; }

        public override MulticastTargets Targets => MulticastTargets.Method;

        public bool? ApplyToStateMachine { get; private set; }

        protected override void ParseAttribute()
        {
            base.ParseAttribute();
            
            ApplyToStateMachine = (bool?) Attribute.GetNamedArgumentValue(nameof(MethodBoundaryAdvice.ApplyToStateMachine));
        }
    }
}