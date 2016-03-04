using Mono.Cecil;

namespace Spinner.Fody.Weavers
{
    internal abstract class AdviceInfo
    {
        internal AdviceInfo(AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
        {
            Aspect = aspect;
            Source = source;
            Attribute = attr;

            // ReSharper disable once VirtualMemberCallInContructor
            ParseAttribute();
        }

        public abstract AdviceType AdviceType { get; }

        public AspectInfo Aspect { get; }

        public ICustomAttributeProvider Source { get; }

        public CustomAttribute Attribute { get; }

        protected virtual void ParseAttribute()
        {
        }
    }
}