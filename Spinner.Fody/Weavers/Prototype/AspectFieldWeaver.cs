using Mono.Cecil;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class AspectFieldWeaver : AdviceWeaver
    {
        internal FieldReference Field { get; private set; }

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset)
        {
            var targetMember = (IMemberDefinition) Aspect.Target;

            string name = NameGenerator.MakeAspectFieldName(targetMember.Name, Aspect.Index);

            var fattrs = FieldAttributes.Private | FieldAttributes.Static;

            var aspectFieldDef = new FieldDefinition(name, fattrs, Aspect.Context.SafeImport(Aspect.AspectType));
            AddCompilerGeneratedAttribute(aspectFieldDef);
            targetMember.DeclaringType.Fields.Add(aspectFieldDef);

            Field = aspectFieldDef;
        }
    }
}