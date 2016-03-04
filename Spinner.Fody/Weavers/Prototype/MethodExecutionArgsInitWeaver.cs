using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class MethodExecutionArgsInitWeaver : AdviceArgsInitWeaver
    {
        internal MethodArgsInitWeaver MethodArgsInit;

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset)
        {
            TypeReference meaType = Aspect.Context.SafeImport(Aspect.Context.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = Aspect.Context.SafeImport(Aspect.Context.Spinner.MethodExecutionArgs_ctor);

            Variable = method.Body.AddVariableDefinition(meaType);

            var il = new ILProcessorEx();

            if (method.IsStatic)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType.IsValueType)
                {
                    il.Emit(OpCodes.Ldobj, method.DeclaringType);
                    il.Emit(OpCodes.Box, method.DeclaringType);
                }
            }

            var methodArgsVar = MethodArgsInit?.Variable;

            if (methodArgsVar == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, methodArgsVar);
            }

            il.Emit(OpCodes.Newobj, meaCtor);
            il.Emit(OpCodes.Stloc, Variable);

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }
    }
}