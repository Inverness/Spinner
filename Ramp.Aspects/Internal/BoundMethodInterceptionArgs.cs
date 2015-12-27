
namespace Ramp.Aspects.Internal
{
    public abstract class MethodBinding : IMethodBinding
    {
        object IMethodBinding.Invoke(ref object instance, Arguments args)
        {
            Invoke(ref instance, args);
            return null;
        }

        public abstract void Invoke(ref object instance, Arguments args);
    }

    public abstract class MethodBinding<T> : IMethodBinding
    {
        object IMethodBinding.Invoke(ref object instance, Arguments args)
        {
            return Invoke(ref instance, args);
        }

        public abstract T Invoke(ref object instance, Arguments args);
    }

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
