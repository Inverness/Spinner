using System;

namespace Spinner
{
    public interface IEventBinding
    {
        void AddHandler(ref object instance, Delegate handler);

        void RemoveHandler(ref object instance, Delegate handler);

        object InvokeHandler(ref object instance, Delegate handler, Arguments arguments);
    }
}