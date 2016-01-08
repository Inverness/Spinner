using System;

namespace Spinner
{
    [AttributeUsage(AttributeTargets.Event, AllowMultiple = true)]
    public abstract class EventInterceptionAspect : Aspect, IEventInterceptionAspect
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