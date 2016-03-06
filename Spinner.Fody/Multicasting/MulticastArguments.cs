using Spinner.Extensibility;

namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// Arguments for the multicast engine. Matches anything by default.
    /// </summary>
    internal sealed class MulticastArguments
    {
        private StringMatcher _targetAssemblies;
        private StringMatcher _targetTypes;
        private StringMatcher _targetMembers;
        private StringMatcher _targetParameters;

        public bool IsExternal { get; set; }

        public MulticastTargets TargetElements { get; set; } = MulticastTargets.All;

        public MulticastAttributes TargetTypeAttributes { get; set; } = MulticastAttributes.All;

        public MulticastAttributes TargetExternalTypeAttributes { get; set; } = MulticastAttributes.All;

        public MulticastAttributes TargetMemberAttributes { get; set; } = MulticastAttributes.All;

        public MulticastAttributes TargetExternalMemberAttributes { get; set; } = MulticastAttributes.All;

        public MulticastAttributes TargetParameterAttributes { get; set; } = MulticastAttributes.All;

        public StringMatcher TargetAssemblies
        {
            get { return _targetAssemblies ?? StringMatcher.AnyMatcher; }

            set { _targetAssemblies = value; }
        }

        public StringMatcher TargetTypes
        {
            get { return _targetTypes ?? StringMatcher.AnyMatcher; }

            set { _targetTypes = value; }
        }

        public StringMatcher TargetMembers
        {
            get { return _targetMembers ?? StringMatcher.AnyMatcher; }

            set { _targetMembers = value; }
        }

        public StringMatcher TargetParameters
        {
            get { return _targetParameters ?? StringMatcher.AnyMatcher; }

            set { _targetParameters = value; }
        }

        public MulticastArguments Clone()
        {
            return (MulticastArguments) MemberwiseClone();
        }
    }
}