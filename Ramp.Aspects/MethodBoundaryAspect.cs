using System;

namespace Ramp.Aspects
{
    /// <summary>
    ///     The base class for method boundary aspects.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
    public abstract class MethodBoundaryAspect : Aspect, IMethodBoundaryAspect
    {
        public virtual void OnEntry(ref MethodExecutionArgs args)
        {

        }

        public virtual void OnExit(ref MethodExecutionArgs args)
        {

        }

        public virtual void OnException(ref MethodExecutionArgs args)
        {

        }

        public virtual void OnSuccess(ref MethodExecutionArgs args)
        {

        }

        public virtual void OnYield(ref MethodExecutionArgs args)
        {

        }

        public virtual void OnResume(ref MethodExecutionArgs args)
        {

        }

        public virtual bool FilterException(ref MethodExecutionArgs args, Exception ex)
        {
            return true;
        }
    }
}