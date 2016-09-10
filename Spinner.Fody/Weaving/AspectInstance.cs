using Mono.Cecil;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weaving
{
    /// <summary>
    /// Represents an application of an aspect onto a target.
    /// </summary>
    internal class AspectInstance
    {
        internal AspectInstance(
            AspectInfo aspect,
            MulticastAttributeInstance mi,
            ICustomAttributeProvider target,
            int index,
            int order)
        {
            Aspect = aspect;
            Source = mi;
            Target = target;
            Index = index;
            Order = order;
        }

        public AspectInfo Aspect { get; }

        public MulticastAttributeInstance Source { get; }

        public ICustomAttributeProvider Target { get; }

        public int Index { get; }

        public int Order { get; }
    }
}