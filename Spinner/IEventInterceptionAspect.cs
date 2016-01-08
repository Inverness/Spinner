namespace Spinner
{
    public interface IEventInterceptionAspect : IAspect
    {
        void OnAddHandler(EventInterceptionArgs args);

        void OnRemoveHandler(EventInterceptionArgs args);

        void OnInvokeHandler(EventInterceptionArgs args);
    }
}