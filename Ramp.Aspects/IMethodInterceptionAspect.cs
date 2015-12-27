
namespace Ramp.Aspects
{
    public interface IMethodInterceptionAspect : IAspect
    {
        void OnInvoke(ref MethodInterceptionArgs args);
    }
}
