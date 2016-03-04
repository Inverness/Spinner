using Mono.Cecil;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weavers.Prototype
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
            MulticastInstance = mi;
            AspectType = mi.AttributeType;
            Index = index;
            Target = target;
            Order = order;
        }

        public ModuleWeavingContext Context { get; }

        public TypeDefinition AspectType { get; }

        public int Index { get; }

        public int Order { get; }

        public ICustomAttributeProvider Target { get; }

        public MulticastInstance MulticastInstance { get; }
    }
}