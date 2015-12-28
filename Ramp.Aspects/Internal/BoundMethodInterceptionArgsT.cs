namespace Ramp.Aspects.Internal
{
    public sealed class BoundMethodInterceptionArgs<T> : MethodInterceptionArgs
    {
        public T TypedReturnValue;

        private readonly MethodBinding<T> _binding;

        public BoundMethodInterceptionArgs(object instance, Arguments arguments, MethodBinding<T> binding)
            : base(instance, arguments)
        {
            _binding = binding;
        }

        public override object ReturnValue
        {
            get { return TypedReturnValue; }

            set { TypedReturnValue = (T) value; }
        }

        public override void Proceed()
        {
            TypedReturnValue = _binding.Invoke(ref InternalInstance, Arguments);
        }

        public override object Invoke(Arguments args)
        {
            return _binding.Invoke(ref InternalInstance, args);
        }
    }
}