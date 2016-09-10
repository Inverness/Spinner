using Mono.Cecil;

namespace Spinner.Fody.Weaving
{
    internal class TypeLevelAspectWeaver : AspectWeaver
    {
        public TypeLevelAspectWeaver(AspectInstance instance)
            : base(instance)
        {
            TargetType = (TypeDefinition) instance.Target;
        }

        public TypeDefinition TargetType { get; }
    }
}