using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Spinner.Fody.Weavers.Prototype
{
    internal abstract class AdviceArgsInitWeaver : AdviceWeaver
    {
        public VariableDefinition Variable { get; protected set; }

        public FieldReference Field { get; protected set; }
    }
}