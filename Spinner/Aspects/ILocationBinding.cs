namespace Spinner.Aspects
{
    public interface ILocationBinding
    {
        object GetValue(ref object instance, Arguments index);

        void SetValue(ref object instance, Arguments index, object value);
    }
}