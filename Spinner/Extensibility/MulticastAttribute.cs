using System;

namespace Spinner.Extensibility
{
    public abstract class MulticastAttribute : Attribute
    {
        protected MulticastAttribute()
        {
            AttributeTargetElements = MulticastTargets.All;
            AttributeTargetTypeAttributes = MulticastAttributes.All;
            AttributeTargetExternalTypeAttributes = MulticastAttributes.All;
        }

        public bool AttributeExclude { get; set; }

        public MulticastInheritance AttributeInheritance { get; set; }

        public int AttributePriority { get; set; }

        public bool AttributeReplace { get; set; }

        public MulticastTargets AttributeTargetElements { get; set; }

        public string AttributeTargetAssemblies { get; set; }

        public string AttributeTargetTypes { get; set; }

        public MulticastAttributes AttributeTargetTypeAttributes { get; set; }

        public MulticastAttributes AttributeTargetExternalTypeAttributes { get; set; }

        public string AttributeTargetMembers { get; set; }

        public MulticastAttributes AttributeTargetMemberAttributes { get; set; }

        public MulticastAttributes AttributeTargetExternalMemberAttributes { get; set; }

        public string AttributeTargetParameters { get; set; }

        public MulticastAttributes AttributeTargetParameterAttributes { get; set; }
    }
}
