using System;

namespace Spinner.Aspects
{
    /// <summary>
    /// A default implementation of IPropertyInterceptionAspect
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class LocationInterceptionAspect : LocationLevelAspect, ILocationInterceptionAspect
    {
        public virtual void OnGetValue(LocationInterceptionArgs args)
        {
            args.ProceedGetValue();
        }

        public virtual void OnSetValue(LocationInterceptionArgs args)
        {
            args.ProceedSetValue();
        }
    }
}