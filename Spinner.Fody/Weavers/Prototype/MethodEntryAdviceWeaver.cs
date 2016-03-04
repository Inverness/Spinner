using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class MethodEntryAdviceWeaver : AdviceWeaver
    {
        internal AspectFieldWeaver AspectField;

        internal MethodExecutionArgsInitWeaver MethodExecutionArgsInit;

        internal MethodReference AdviceMethod;

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset)
        {
            var il = new ILProcessorEx();

            // Invoke OnEntry with the MEA field, variable, or null.

            il.Emit(OpCodes.Ldsfld, AspectField.Field);
            il.EmitLoadOrNull(MethodExecutionArgsInit.Variable, MethodExecutionArgsInit.Field);
            il.Emit(OpCodes.Callvirt, Aspect.Context.SafeImport(AdviceMethod));

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }
    }
}