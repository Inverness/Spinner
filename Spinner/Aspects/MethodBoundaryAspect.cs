using System;

namespace Spinner.Aspects
{
    /// <summary>
    /// A default implementation of IMethodBoundaryAspect.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    public abstract class MethodBoundaryAspect : MethodLevelAspect, IMethodBoundaryAspect
    {
        protected MethodBoundaryAspect()
        {
            AttributeApplyToStateMachine = true;
        }

        public bool AttributeApplyToStateMachine { get; set; }

        public virtual void OnEntry(MethodExecutionArgs args)
        {

        }

        public virtual void OnExit(MethodExecutionArgs args)
        {

        }

        public virtual void OnException(MethodExecutionArgs args)
        {

        }

        public virtual void OnSuccess(MethodExecutionArgs args)
        {

        }

        public virtual void OnYield(MethodExecutionArgs args)
        {

        }

        public virtual void OnResume(MethodExecutionArgs args)
        {

        }

        public virtual bool FilterException(MethodExecutionArgs args, Exception ex)
        {
            return true;
        }
    }
}