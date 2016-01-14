
namespace Spinner.Aspects
{
    /// <summary>
    /// An object that handles invocation of a method from an aspect.
    /// </summary>
    public interface IMethodBinding
    {
        /// <summary>
        ///     Invoke the bound method.
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        object Invoke(ref object instance, Arguments args);
    }
}
