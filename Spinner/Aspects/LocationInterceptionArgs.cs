using System.Reflection;

namespace Spinner.Aspects
{
    public abstract class LocationInterceptionArgs : AdviceArgs
    {
        protected LocationInterceptionArgs(object instance, Arguments index)
            : base(instance)
        {
            Index = index;
        }

        public Arguments Index { get; set; }

        public abstract object Value { get; set; }

        public PropertyInfo Location { get; set; }

        public abstract void ProceedGetValue();

        public abstract void ProceedSetValue();

        public abstract object InvokeGetValue(Arguments index);

        public abstract void InvokeSetValue(Arguments index, object value);
    }
}