namespace Spinner.Aspects.Internal
{
    public abstract class MethodBinding<T> : IMethodBinding
    {
        object IMethodBinding.Invoke(ref object instance, Arguments args)
        {
            return Invoke(ref instance, args);
        }

        public abstract T Invoke(ref object instance, Arguments args);
    }
}