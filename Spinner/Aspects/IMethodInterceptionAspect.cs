
namespace Spinner.Aspects
{
    /// <summary>
    /// Describes the advices that must be implemented for a method boundary aspect.
    /// </summary>
    public interface IMethodInterceptionAspect : IAspect
    {
        void OnInvoke(MethodInterceptionArgs args);
    }
}
