using Mono.Cecil;
using Spinner.Fody.Weaving.AdviceWeavers;

namespace Spinner.Fody.Weaving
{
    internal class MethodBoundaryAdviceGroup : AdviceGroup
    {
        public MethodBoundaryAdviceGroup(AdviceInfo master)
            : base(master)
        {
            SetProperty(master);
        }

        internal AdviceInfo Entry { get; private set; }

        internal AdviceInfo Exit { get; private set; }

        internal AdviceInfo Success { get; private set; }

        internal AdviceInfo Exception { get; private set; }

        internal AdviceInfo FilterException { get; private set; }

        internal AdviceInfo Yield { get; private set; }

        internal AdviceInfo Resume { get; private set; }

        internal override void AddChild(AdviceInfo advice)
        {
            SetProperty(advice);
            base.AddChild(advice);
        }

        internal override AdviceWeaver CreateWeaver(AspectWeaver parent, IMetadataTokenProvider target)
        {
            return new MethodBoundaryAdviceWeaver(parent, this, (MethodDefinition) target);
        }

        private void SetProperty(AdviceInfo advice)
        {
            switch (advice.AdviceType)
            {
                case AdviceType.MethodEntry:
                    ThrowIfDuplicate(Entry);
                    Entry = advice;
                    break;
                case AdviceType.MethodExit:
                    ThrowIfDuplicate(Exit);
                    Exit = advice;
                    break;
                case AdviceType.MethodSuccess:
                    ThrowIfDuplicate(Success);
                    Success = advice;
                    break;
                case AdviceType.MethodException:
                    ThrowIfDuplicate(Exception);
                    Exception = advice;
                    break;
                case AdviceType.MethodFilterException:
                    ThrowIfDuplicate(FilterException);
                    FilterException = advice;
                    break;
                case AdviceType.MethodYield:
                    ThrowIfDuplicate(Yield);
                    Yield = advice;
                    break;
                case AdviceType.MethodResume:
                    ThrowIfDuplicate(Resume);
                    Resume = advice;
                    break;
                default:
                    ThrowInvalidAdviceForGroup(advice);
                    break;
            }
        }
    }
}