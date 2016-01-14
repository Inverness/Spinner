using System;

namespace Spinner.Aspects
{
    /// <summary>
    /// A default implementation of IPropertyInterceptionAspect
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PropertyInterceptionAspect : Aspect, IPropertyInterceptionAspect
    {
        public virtual void OnGetValue(PropertyInterceptionArgs args)
        {
            args.ProceedGetValue();
        }

        public virtual void OnSetValue(PropertyInterceptionArgs args)
        {
            args.ProceedSetValue();
        }
    }
}