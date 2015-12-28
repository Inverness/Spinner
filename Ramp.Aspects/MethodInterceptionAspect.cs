using System;

namespace Ramp.Aspects
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class MethodInterceptionAspect : Aspect, IMethodInterceptionAspect
    {
        public virtual void OnInvoke(MethodInterceptionArgs args)
        {

        }
    }
}
