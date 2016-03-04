using Mono.Cecil;
using Spinner.Aspects.Advices;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class MethodBoundaryAdviceInfo : GroupingAdviceInfo
    {
        internal MethodBoundaryAdviceInfo(AdviceType type, AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
            AdviceType = type;
        }

        public override AdviceType AdviceType { get; }

        public bool? ApplyToStateMachine { get; private set; }

        protected override void ParseAttribute()
        {
            base.ParseAttribute();
            
            ApplyToStateMachine = (bool?) Attribute.GetNamedArgumentValue(nameof(MethodBoundaryAdvice.ApplyToStateMachine));
        }
    }
}