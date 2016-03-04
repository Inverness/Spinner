namespace Spinner.Aspects.Advices
{
    public abstract class MethodBoundaryAdvice : GroupingAdvice
    {
        public bool ApplyToStateMachine { get; set; }
    }
}