using System.Diagnostics;
using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Aspects.Advices;
using Spinner.Extensibility;

namespace Spinner.Fody.Weaving
{
    [DebuggerDisplay("{Aspect.AspectType.Name} {Aspect.Index} {AdviceType}")]
    internal abstract class AdviceInfo
    {
        internal AdviceInfo(AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
        {
            Aspect = aspect;
            Source = source;
            Attribute = attr;

            if (attr != null)
            {
                // ReSharper disable once VirtualMemberCallInContructor
                ParseAttribute();
            }

            MethodDefinition sourceMethod;
            TypeDefinition sourceType;
            if ((sourceMethod = source as MethodDefinition) != null)
                Features = aspect.Context.GetFeatures(sourceMethod) ?? Features.None;
            else if ((sourceType = source as TypeDefinition) != null)
                Features = aspect.Context.GetFeatures(sourceType) ?? Features.None;
        }

        public abstract AdviceType AdviceType { get; }

        public abstract MulticastTargets ValidTargets { get; }

        public AspectInfo Aspect { get; }

        public ICustomAttributeProvider Source { get; }

        public CustomAttribute Attribute { get; }

        public bool Applied { get; set; }

        public string Master { get; set; }

        public AdviceInfo MasterObject { get; set; }

        public bool HasMaster => Master != null;

        public Features Features { get; }

        public MethodReference ImportSourceMethod()
        {
            return Aspect.Context.SafeImport((MethodReference) Source);
        }

        protected virtual void ParseAttribute()
        {
            Master = (string) Attribute.GetNamedArgumentValue(nameof(GroupingAdvice.Master));
        }
    }
}