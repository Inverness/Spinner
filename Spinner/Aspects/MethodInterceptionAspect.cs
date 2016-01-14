using System;

namespace Spinner.Aspects
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
