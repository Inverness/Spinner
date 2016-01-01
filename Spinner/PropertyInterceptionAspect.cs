using System;

namespace Spinner
{
    /// <summary>
    /// A default implementation of IPropertyInterceptionAspect
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PropertyInterceptionAspect : Aspect, IPropertyInterceptionAspect
    {
        public virtual void OnGetValue(PropertyInterceptionArgs args)
        {
        }

        public virtual void OnSetValue(PropertyInterceptionArgs args)
        {
        }
    }
}