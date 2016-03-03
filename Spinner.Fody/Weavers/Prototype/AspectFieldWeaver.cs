using System.Collections.Generic;
using Mono.Cecil;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class AspectFieldWeaver : AdviceWeaver
    {
        public AspectFieldWeaver(AspectWeaver2 p, MethodDefinition adviceMethod)
            : base(p, adviceMethod)
        {
        }

        public FieldReference Field { get; private set; }

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset, ICollection<AdviceWeaver> previous)
        {
            string name = NameGenerator.MakeAspectFieldName(P.AspectTarget.Name, P.AspectIndex);

            var fattrs = FieldAttributes.Private | FieldAttributes.Static;

            var aspectFieldDef = new FieldDefinition(name, fattrs, P.Context.SafeImport(P.AspectType));
            AddCompilerGeneratedAttribute(aspectFieldDef);
            P.AspectTarget.DeclaringType.Fields.Add(aspectFieldDef);

            Field = aspectFieldDef;
        }
    }
}