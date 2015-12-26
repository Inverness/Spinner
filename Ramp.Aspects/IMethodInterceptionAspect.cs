
namespace Ramp.Aspects
{
    public interface IMethodInterceptionAspect : IAspect
    {
        void OnInvoke(MethodInterceptionArgs args);
    }
}
