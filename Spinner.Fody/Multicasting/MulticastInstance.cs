using System.Diagnostics;
using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// An instance of a multicast attribute that was specified on an assembly, type, or type member.
    /// </summary>
    internal sealed class MulticastInstance
    {
        internal MulticastInstance(
            ICustomAttributeProvider origin,
            ProviderType ot,
            CustomAttribute attribute,
            TypeDefinition attributeType)
        {
            Target = origin;
            TargetType = ot;
            Origin = origin;
            OriginType = ot;
            Attribute = attribute;
            AttributeType = attributeType;
            TargetElements = MulticastTargets.All;
            TargetTypeAttributes = MulticastAttributes.All;
            TargetExternalTypeAttributes = MulticastAttributes.All;
            // Not applied to all members by default
            TargetAssemblies = StringMatcher.AnyMatcher;
            TargetTypes = StringMatcher.AnyMatcher;
            TargetMembers = StringMatcher.AnyMatcher;
            TargetParameters = StringMatcher.AnyMatcher;

            if (!attribute.HasProperties)
                return;

            foreach (CustomAttributeNamedArgument ca in attribute.Properties)
            {
                object value = ca.Argument.Value;

                switch (ca.Name)
                {
                    case nameof(MulticastAttribute.AttributeExclude):
                        Exclude = (bool) value;
                        break;
                    case nameof(MulticastAttribute.AttributeInheritance):
                        Inheritance = (MulticastInheritance) (byte) value;
                        break;
                    case nameof(MulticastAttribute.AttributePriority):
                        Priority = (int) value;
                        break;
                    case nameof(MulticastAttribute.AttributeReplace):
                        Replace = (bool) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetElements):
                        TargetElements = (MulticastTargets) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetAssemblies):
                        TargetAssemblies = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetTypes):
                        TargetTypes = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetTypeAttributes):
                        TargetTypeAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetExternalTypeAttributes):
                        TargetExternalTypeAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetMembers):
                        TargetMembers = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetMemberAttributes):
                        TargetMemberAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetExternalMemberAttributes):
                        TargetExternalMemberAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetParameters):
                        TargetParameters = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetParameterAttributes):
                        TargetParameterAttributes = (MulticastAttributes) (uint) value;
                        break;
                }
            }
        }

        public ICustomAttributeProvider Target { get; private set; }

        public ProviderType TargetType { get; }

        public ICustomAttributeProvider Origin { get; }

        public ProviderType OriginType { get; }

        public CustomAttribute Attribute { get; }

        public TypeDefinition AttributeType { get; }
        
        public bool Exclude { get; }

        public MulticastInheritance Inheritance { get; }

        public int Priority { get; }

        public bool Replace { get; }

        public MulticastTargets TargetElements { get; }

        public StringMatcher TargetAssemblies { get; }

        public StringMatcher TargetTypes { get; }

        public MulticastAttributes TargetTypeAttributes { get; }

        public MulticastAttributes TargetExternalTypeAttributes { get; }

        public StringMatcher TargetMembers { get; }

        public MulticastAttributes TargetMemberAttributes { get; }

        public MulticastAttributes TargetExternalMemberAttributes { get; }

        public StringMatcher TargetParameters { get; }

        public MulticastAttributes TargetParameterAttributes { get; }

        public MulticastInstance WithTarget(ICustomAttributeProvider newTarget)
        {
            Debug.Assert(newTarget.GetType() == Target.GetType());

            var clone = (MulticastInstance) MemberwiseClone();
            clone.Target = newTarget;
            return clone;
        }
    }
}