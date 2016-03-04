
namespace Spinner.Aspects
{
    /// <summary>
    /// A default implementation of IMethodInterceptionAspect.
    /// </summary>
    public class MethodInterceptionAspect : MethodLevelAspect, IMethodInterceptionAspect
    {
        public virtual void OnInvoke(MethodInterceptionArgs args)
        {
            args.Proceed();
        }
    }
}
