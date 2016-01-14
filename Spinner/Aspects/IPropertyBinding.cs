namespace Spinner.Aspects
{
    public interface IPropertyBinding
    {
        object GetValue(ref object instance, Arguments index);

        void SetValue(ref object instance, Arguments index, object value);
    }
}