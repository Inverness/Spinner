using System;

namespace Spinner.Aspects
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public abstract class EventInterceptionAspect : EventLevelAspect, IEventInterceptionAspect
    {
        public virtual void OnAddHandler(EventInterceptionArgs args)
        {
            args.ProceedAddHandler();
        }

        public virtual void OnRemoveHandler(EventInterceptionArgs args)
        {
            args.ProceedRemoveHandler();
        }

        public virtual void OnInvokeHandler(EventInterceptionArgs args)
        {
            args.ProceedInvokeHandler();
        }
    }
}