using System.Diagnostics;

namespace Spinner.Aspects.Internal
{
    [DebuggerStepThrough]
    public sealed class BoundMethodInterceptionArgs : MethodInterceptionArgs
    {
        private readonly MethodBinding _binding;

        public BoundMethodInterceptionArgs(object instance, Arguments arguments, MethodBinding binding)
            : base(instance, arguments)
        {
            _binding = binding;
        }

        public override object ReturnValue
        {
            get { return null; }

            set { }
        }

        public override void Proceed()
        {
            _binding.Invoke(ref InternalInstance, Arguments);
        }

        public override object Invoke(Arguments args)
        {
            _binding.Invoke(ref InternalInstance, args);
            return null;
        }
    }
}
