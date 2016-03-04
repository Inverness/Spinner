using Mono.Cecil;
using Spinner.Aspects.Advices;

namespace Spinner.Fody.Weavers
{
    internal abstract class GroupingAdviceInfo : AdviceInfo
    {
        protected GroupingAdviceInfo(AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
            : base(aspect, source, attr)
        {
        }

        public string Master { get; private set; }

        protected override void ParseAttribute()
        {
            base.ParseAttribute();

            Master = (string) Attribute.GetNamedArgumentValue(nameof(GroupingAdvice.Master));
        }
    }
}