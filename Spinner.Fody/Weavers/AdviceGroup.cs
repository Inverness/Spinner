using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mono.Cecil;
using Spinner.Aspects.Advices;
using Spinner.Extensibility;
using Spinner.Fody.Multicasting;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers
{
    internal abstract class AdviceGroup
    {
        private List<AdviceInfo> _slaves; 

        internal AdviceGroup(AdviceInfo master)
        {
            Master = master;
            ParsePointcutAttribute();
        }

        internal AdviceInfo Master { get; }

        internal IReadOnlyList<AdviceInfo> Slaves
            => (IReadOnlyList<AdviceInfo>) _slaves ?? CollectionUtility<AdviceInfo>.EmptyArray;

        internal bool HasSlaves => _slaves != null;

        internal IEnumerable<AdviceInfo> All
        {
            get
            {
                yield return Master;

                if (_slaves != null)
                {
                    foreach (AdviceInfo a in _slaves)
                        yield return a;
                }
            }
        }

        internal PointcutType? PointcutType { get; private set; }

        internal MulticastAttributes PointcutAttributes { get; private set; }

        internal StringMatcher PointcutMemberName { get; private set; }

        internal MulticastTargets PointcutTargets { get; private set; }

        internal virtual void AddChild(AdviceInfo advice)
        {
            Debug.Assert(advice.Aspect == Master.Aspect);
            if (_slaves == null)
                _slaves = new List<AdviceInfo>();
            _slaves.Add(advice);
        }

        internal abstract AdviceWeaver CreateWeaver(AspectWeaver parent, IMetadataTokenProvider target);

        protected static void ThrowIfDuplicate(AdviceInfo advice)
        {
            if (advice != null)
                throw new ValidationException("duplicate advice type in group");
        }

        protected static void ThrowInvalidAdviceForGroup(AdviceInfo advice)
        {
            throw new ValidationException($"{advice.Source}");
        }

        private void ParsePointcutAttribute()
        {
            var mwc = Master.Aspect.Context;

            foreach (CustomAttribute ca in Master.Source.CustomAttributes)
            {
                if (ca.AttributeType.IsSame(mwc.Spinner.SelfPointcut))
                {
                    PointcutType = Weavers.PointcutType.Self;
                }
                else if (ca.AttributeType.IsSame(mwc.Spinner.MulticastPointcut))
                {
                    uint attributes = ca.GetNamedArgumentValue(nameof(MulticastPointcut.Attributes)) as uint? ?? 0;
                    uint targets = ca.GetNamedArgumentValue(nameof(MulticastPointcut.Targets)) as uint? ?? 0;
                    string memberName = ca.GetNamedArgumentValue(nameof(MulticastPointcut.MemberName)) as string;

                    PointcutType = Weavers.PointcutType.Multicast;
                    PointcutAttributes = (MulticastAttributes) attributes;
                    PointcutTargets = (MulticastTargets) targets;
                    PointcutMemberName = StringMatcher.Create(memberName);
                }
                else if (ca.AttributeType.IsSame(mwc.Spinner.MethodPointcut))
                {
                    throw new NotImplementedException();
                }
            }
        }
    }
}