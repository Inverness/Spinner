using System.Collections.Generic;
using Mono.Cecil;
using Spinner.Aspects;

namespace Spinner.Fody.Weaving
{
    /// <summary>
    /// Contains information about an aspect type such as the kind, features, and advice groups.
    /// </summary>
    internal class AspectInfo
    {
        private readonly List<AdviceGroup> _adviceGroups = new List<AdviceGroup>();

        internal AspectInfo(SpinnerContext context, TypeDefinition aspectType, AspectKind kind)
        {
            Context = context;
            AspectType = aspectType;
            Kind = kind;
            Features = context.GetFeatures(aspectType) ?? Features.None;
        }

        public SpinnerContext Context { get; }

        public TypeDefinition AspectType { get; }

        public AspectKind Kind { get; }

        public Features Features { get; }

        public IReadOnlyList<AdviceGroup> AdviceGroups => _adviceGroups;

        internal void AddAdviceGroup(AdviceGroup g)
        {
            _adviceGroups.Add(g);
        }
    }
}