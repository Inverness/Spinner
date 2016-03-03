using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class MethodEntryAdviceWeaver : AdviceWeaver
    {
        public MethodEntryAdviceWeaver(AspectWeaver2 p, MethodDefinition adviceMethod)
            : base(p, adviceMethod)
        {
        }

        public VariableDefinition MeaVar { get; set; }

        public FieldReference MeaField { get; set; }

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset, ICollection<AdviceWeaver> previous)
        {
            var il = new ILProcessorEx();

            var aspectWeaver = previous.OfType<AspectFieldWeaver>().First();
            var adviceArgsWeaver = previous.OfType<MethodExecutionArgsInitWeaver>().First();

            // Invoke OnEntry with the MEA field, variable, or null.

            il.Emit(OpCodes.Ldsfld, aspectWeaver.Field);
            il.EmitLoadOrNull(adviceArgsWeaver.Variable, adviceArgsWeaver.Field);
            il.Emit(OpCodes.Callvirt, P.Context.SafeImport(AdviceMethod));

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }
    }
}