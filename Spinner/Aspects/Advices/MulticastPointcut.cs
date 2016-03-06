using Spinner.Extensibility;

namespace Spinner.Aspects.Advices
{
    public sealed class MulticastPointcut : Pointcut
    {
        public MulticastAttributes Attributes { get; set; }

        public string MemberName { get; set; }

        public MulticastTargets Targets { get; set; }
    }
}