namespace Spinner.Aspects.Advices
{
    public abstract class MethodBoundaryAdvice : GroupingAdvice
    {
        public bool AttributeApplyToStateMachine { get; set; }
    }
}