namespace Spinner.Aspects
{
    /// <summary>
    /// Describes the advices that must be implemented for a property interception aspect.
    /// </summary>
    public interface ILocationInterceptionAspect : ILocationLevelAspect
    {
        /// <summary>
        /// Invoked when accessing the property getter.
        /// </summary>
        /// <param name="args"></param>
        void OnGetValue(LocationInterceptionArgs args);

        /// <summary>
        /// Invoked when accessing the property setter.
        /// </summary>
        /// <param name="args"></param>
        void OnSetValue(LocationInterceptionArgs args);
    }
}
