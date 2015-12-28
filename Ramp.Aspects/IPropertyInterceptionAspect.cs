namespace Ramp.Aspects
{
    public interface IPropertyInterceptionAspect : IAspect
    {
        void OnGetValue(PropertyInterceptionArgs args);

        void OnSetValue(PropertyInterceptionArgs args);
    }
}
