namespace Spinner.Aspects.Internal
{
    public abstract class PropertyBinding<T> : IPropertyBinding
    {
        object IPropertyBinding.GetValue(ref object instance, Arguments index)
        {
            return GetValue(ref instance, index);
        }

        void IPropertyBinding.SetValue(ref object instance, Arguments index, object value)
        {
            SetValue(ref instance, index, (T) value);
        }

        public abstract T GetValue(ref object instance, Arguments index);

        public abstract void SetValue(ref object instance, Arguments index, T value);
    }

    public sealed class PropertyBindingImplTest : PropertyBinding<int>
    {
        public override int GetValue(ref object instance, Arguments index)
        {
            throw new System.NotImplementedException();
        }

        public override void SetValue(ref object instance, Arguments index, int value)
        {
            throw new System.NotImplementedException();
        }
    }
}