using System.Diagnostics;
using Mono.Cecil;
using Spinner.Extensibility;

namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// An instance of a multicast attribute that was specified on an assembly, type, or type member.
    /// </summary>
    internal sealed class MulticastAttributeInstance
    {
        private readonly MulticastArguments _a = new MulticastArguments();

        internal MulticastAttributeInstance(
            ICustomAttributeProvider origin,
            ProviderType ot,
            CustomAttribute attribute,
            TypeDefinition attributeType,
            bool isExternal)
        {
            Target = origin;
            TargetType = ot;
            Origin = origin;
            OriginType = ot;
            Attribute = attribute;
            AttributeType = attributeType;

            _a.IsExternal = isExternal;

            // Not applied to all members by default
            _a.TargetMemberAttributes = MulticastAttributes.Default;
            _a.TargetExternalMemberAttributes = MulticastAttributes.Default;
            _a.TargetParameterAttributes = MulticastAttributes.Default;

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
                        _a.TargetElements = (MulticastTargets) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetAssemblies):
                        _a.TargetAssemblies = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetTypes):
                        _a.TargetTypes = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetTypeAttributes):
                        _a.TargetTypeAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetExternalTypeAttributes):
                        _a.TargetExternalTypeAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetMembers):
                        _a.TargetMembers = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetMemberAttributes):
                        _a.TargetMemberAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetExternalMemberAttributes):
                        _a.TargetExternalMemberAttributes = (MulticastAttributes) (uint) value;
                        break;
                    case nameof(MulticastAttribute.AttributeTargetParameters):
                        _a.TargetParameters = StringMatcher.Create((string) value);
                        break;
                    case nameof(MulticastAttribute.AttributeTargetParameterAttributes):
                        _a.TargetParameterAttributes = (MulticastAttributes) (uint) value;
                        break;
                }
            }
        }

        public ICustomAttributeProvider Target { get; private set; }

        public ProviderType TargetType { get; }

        public ICustomAttributeProvider Origin { get; }

        public ProviderType OriginType { get; }

        public bool Exclude { get; }

        public MulticastInheritance Inheritance { get; }

        public int Priority { get; }

        public bool Replace { get; }

        public CustomAttribute Attribute { get; }

        public TypeDefinition AttributeType { get; }

        public MulticastArguments Arguments => _a;

        public MulticastAttributeInstance WithTarget(ICustomAttributeProvider newTarget)
        {
            Debug.Assert(newTarget.GetType() == Target.GetType());

            var clone = (MulticastAttributeInstance) MemberwiseClone();
            clone.Target = newTarget;
            return clone;
        }
    }
}