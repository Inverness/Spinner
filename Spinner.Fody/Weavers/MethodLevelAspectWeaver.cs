using System;
using System.Collections.Generic;
using Mono.Cecil;
using Spinner.Extensibility;
using Spinner.Fody.Multicasting;

namespace Spinner.Fody.Weavers
{
    internal class MethodLevelAspectWeaver : AspectWeaver
    {
        public MethodLevelAspectWeaver(AspectInfo aspect, IEnumerable<AdviceGroup> advices, MethodDefinition method)
            : base(aspect, advices, method)
        {
            TargetMethod = method;
        }

        internal MethodDefinition TargetMethod { get; }

        public override void Weave()
        {
            Func<MethodDefinition, bool> temporaryDebugTest =
                m => m.IsPublic && !m.IsStatic && !m.IsAbstract && m.HasBody &&
                     m.SemanticsAttributes == MethodSemanticsAttributes.None;

            MulticastTargets targetType = Target.GetMulticastTargetType();

            foreach (AdviceGroup g in AdviceGroups)
            {
                if ((g.Master.Targets & targetType) != 0)
                {
                    var w = g.CreateWeaver(this, Target);
                    w.Weave();
                }

                var type = Target as TypeDefinition;
                if (type != null)
                {
                    foreach (MethodDefinition method in type.Methods)
                    {
                        if (temporaryDebugTest(method))
                        {
                            var w = g.CreateWeaver(this, method);
                            w.Weave();
                        }
                    }

                    //foreach (IMemberDefinition member in type.GetMembers())
                    //{
                    //    if ((g.Master.Targets & member.GetMulticastTargetType()) != 0)
                    //    {
                    //        var w = g.CreateWeaver(this, member);
                    //        w.Weave();
                    //    }
                    //}
                }
            }
        }

        //public override void Weave()
        //{
        //    List<AdviceGroup> groups = CreateGroups(Advices);

        //    foreach (AdviceGroup g in groups)
        //    {
        //        var w = g.CreateWeaver(this);
        //        w.Weave();
        //    }
        //}
    }
}