namespace Spinner.Aspects.Advices
{
    public sealed class MethodPointcut : Pointcut
    {
        public MethodPointcut(string methodName)
        {
            MethodName = methodName;
        }

        public string MethodName { get; }
    }
}