namespace Spinner.Aspects.Internal
{
    public sealed class BoundLocationInterceptionArgs<T> : LocationInterceptionArgs
    {
        public T TypedValue;

        private readonly LocationBinding<T> _binding; 

        public BoundLocationInterceptionArgs(object instance, Arguments index, LocationBinding<T> binding)
            : base(instance, index)
        {
            _binding = binding;
        }

        public override object Value
        {
            get { return TypedValue; }

            set { TypedValue = (T) value; }
        }

        public override void ProceedGetValue()
        {
            TypedValue = _binding.GetValue(ref InternalInstance, Index);
        }

        public override void ProceedSetValue()
        {
            _binding.SetValue(ref InternalInstance, Index, TypedValue);
        }

        public override object InvokeGetValue(Arguments index)
        {
            return _binding.GetValue(ref InternalInstance, index);
        }

        public override void InvokeSetValue(Arguments index, object value)
        {
            _binding.SetValue(ref InternalInstance, index, (T) value);
        }
    }
}