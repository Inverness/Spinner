
namespace Spinner
{
    public interface IMethodInterceptionAspect : IAspect
    {
        void OnInvoke(MethodInterceptionArgs args);
    }
}
