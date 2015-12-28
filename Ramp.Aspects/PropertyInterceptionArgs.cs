using System.Reflection;

namespace Ramp.Aspects
{
    public abstract class PropertyInterceptionArgs : AdviceArgs
    {
        protected PropertyInterceptionArgs(object instance, Arguments index)
            : base(instance)
        {
            Index = index;
        }

        public Arguments Index { get; set; }

        public abstract object Value { get; set; }

        public PropertyInfo Property { get; set; }

        public abstract void ProceedGetValue();

        public abstract void ProceedSetValue();

        public abstract object InvokeGetValue(Arguments index);

        public abstract void InvokeSetValue(Arguments index, object value);
    }
}