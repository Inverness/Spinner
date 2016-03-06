using System.Diagnostics;
using Mono.Cecil;
using Spinner.Aspects;
using Spinner.Aspects.Advices;
using Spinner.Extensibility;

namespace Spinner.Fody.Weavers
{
    [DebuggerDisplay("{Aspect.AspectType.Name} {Aspect.Index} {AdviceType}")]
    internal abstract class AdviceInfo
    {
        internal AdviceInfo(AspectInfo aspect, ICustomAttributeProvider source, CustomAttribute attr)
        {
            Aspect = aspect;
            Source = source;
            Attribute = attr;

            // ReSharper disable once VirtualMemberCallInContructor
            ParseAttribute();

            MethodDefinition sourceMethod;
            TypeDefinition sourceType;
            if ((sourceMethod = source as MethodDefinition) != null)
                Features = aspect.Context.GetFeatures(sourceMethod);
            else if ((sourceType = source as TypeDefinition) != null)
                Features = aspect.Context.GetFeatures(sourceType);
        }

        public abstract AdviceType AdviceType { get; }

        public abstract MulticastTargets Targets { get; }

        public AspectInfo Aspect { get; }

        public ICustomAttributeProvider Source { get; }

        public CustomAttribute Attribute { get; }

        public bool Applied { get; set; }

        public string Master { get; private set; }

        public AdviceInfo MasterObject { get; set; }

        public bool HasMaster => Master != null;

        public Features Features { get; }

        protected virtual void ParseAttribute()
        {
            Master = (string) Attribute.GetNamedArgumentValue(nameof(GroupingAdvice.Master));
        }
    }
}