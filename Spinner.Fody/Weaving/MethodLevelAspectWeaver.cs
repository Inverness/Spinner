using Mono.Cecil;

namespace Spinner.Fody.Weaving
{
    internal class MethodLevelAspectWeaver : AspectWeaver
    {
        public MethodLevelAspectWeaver(AspectInstance instance)
            : base(instance)
        {
            TargetMethod = (MethodDefinition) instance.Target;
        }

        internal MethodDefinition TargetMethod { get; }
    }
}