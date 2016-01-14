using System;

namespace Spinner.Aspects.Internal
{
    public abstract class EventBinding : IEventBinding
    {
        public abstract void AddHandler(ref object instance, Delegate handler);

        public abstract void RemoveHandler(ref object instance, Delegate handler);

        public abstract object InvokeHandler(ref object instance, Delegate handler, Arguments arguments);
    }

    internal sealed class EventBindingTest : EventBinding
    {
        public override void AddHandler(ref object instance, Delegate handler)
        {
            throw new NotImplementedException();
        }

        public override void RemoveHandler(ref object instance, Delegate handler)
        {
            throw new NotImplementedException();
        }

        public override object InvokeHandler(ref object instance, Delegate handler, Arguments arguments)
        {
            throw new NotImplementedException();
        }
    }
}