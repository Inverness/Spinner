namespace Spinner.Internal
{
    public sealed class BoundPropertyInterceptionArgs<T> : PropertyInterceptionArgs
    {
        public T TypedValue;

        private readonly PropertyBinding<T> _binding; 

        public BoundPropertyInterceptionArgs(object instance, Arguments index, PropertyBinding<T> binding)
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