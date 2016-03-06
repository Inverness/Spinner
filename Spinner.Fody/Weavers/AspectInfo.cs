using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weavers
{
    internal class AspectInfo
    {
        internal AspectInfo(
            ModuleWeavingContext mwc,
            MulticastInstance mi,
            ICustomAttributeProvider target,
            int index,
            int order)
        {
            Context = mwc;
            Source = mi;
            AspectType = mi.AttributeType;
            Index = index;
            Target = target;
            Order = order;
            Features = mwc.GetFeatures(mi.AttributeType);
        }

        public ModuleWeavingContext Context { get; }

        public TypeDefinition AspectType { get; }

        public int Index { get; }

        public int Order { get; }

        public ICustomAttributeProvider Target { get; }

        public MulticastInstance Source { get; }

        public Features Features { get; }
    }
}