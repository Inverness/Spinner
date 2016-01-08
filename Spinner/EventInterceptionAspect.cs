using System;

namespace Spinner
{
    [AttributeUsage(AttributeTargets.Event, AllowMultiple = true)]
    public abstract class EventInterceptionAspect : Aspect, IEventInterceptionAspect
    {
        public virtual void OnAddHandler(EventInterceptionArgs args)
        {
        }

        public virtual void OnRemoveHandler(EventInterceptionArgs args)
        {
        }

        public virtual void OnInvokeHandler(EventInterceptionArgs args)
        {
        }
    }
}