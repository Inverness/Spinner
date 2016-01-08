using System;

namespace Spinner
{
    /// <summary>
    /// A default implementation of IMethodInterceptionAspect.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class MethodInterceptionAspect : Aspect, IMethodInterceptionAspect
    {
        public virtual void OnInvoke(MethodInterceptionArgs args)
        {
            args.Proceed();
        }
    }
}
