using System;

namespace Spinner.Internal
{
    public sealed class BoundEventInterceptionArgs : EventInterceptionArgs
    {
        private readonly EventBinding _binding; 

        public BoundEventInterceptionArgs(object instance, Arguments arguments, EventBinding binding)
            : base(instance, arguments)
        {
            _binding = binding;
        }

        public override void ProceedAddHandler()
        {
            _binding.AddHandler(ref InternalInstance, Handler);
        }

        public override void ProceedRemoveHandler()
        {
            _binding.RemoveHandler(ref InternalInstance, Handler);
        }

        public override void ProceedInvokeHandler()
        {
            ReturnValue = _binding.InvokeHandler(ref InternalInstance, Handler, Arguments);
        }

        public override void AddHandler(Delegate handler)
        {
            _binding.AddHandler(ref InternalInstance, handler);
        }

        public override void RemoveHandler(Delegate handler)
        {
            _binding.RemoveHandler(ref InternalInstance, handler);
        }

        public override object InvokeHandler(Delegate handler, Arguments arguments)
        {
            return _binding.InvokeHandler(ref InternalInstance, handler, arguments);
        }
    }
}