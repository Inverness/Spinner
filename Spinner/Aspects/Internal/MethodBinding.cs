namespace Spinner.Aspects.Internal
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
}