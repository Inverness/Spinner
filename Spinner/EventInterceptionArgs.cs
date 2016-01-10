using System;
using System.Diagnostics;
using System.Reflection;

namespace Spinner
{
    public abstract class EventInterceptionArgs : AdviceArgs
    {
        [DebuggerStepThrough]
        protected EventInterceptionArgs(object instance, Arguments arguments)
            : base(instance)
        {
            Arguments = arguments;
        }

        public Arguments Arguments { get; set; }

        public Delegate Handler { get; set; }

        public EventInfo Event { get; set; }

        public object ReturnValue { get; set; }

        public abstract void ProceedAddHandler();

        public abstract void ProceedRemoveHandler();

        public abstract void ProceedInvokeHandler();

        public abstract void AddHandler(Delegate handler);

        public abstract void RemoveHandler(Delegate handler);

        public abstract object InvokeHandler(Delegate handler, Arguments arguments);
    }
}