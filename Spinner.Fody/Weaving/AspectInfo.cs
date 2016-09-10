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

        internal AspectInfo(ModuleWeavingContext mwc, TypeDefinition aspectType, AspectKind kind)
        {
            Context = mwc;
            AspectType = aspectType;
            Kind = kind;
            Features = mwc.GetFeatures(aspectType) ?? Features.None;
        }

        public ModuleWeavingContext Context { get; }

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