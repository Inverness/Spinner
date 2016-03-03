using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers.Prototype
{
    internal abstract class AdviceArgsInitWeaver : AdviceWeaver
    {
        protected AdviceArgsInitWeaver(AspectWeaver2 p, MethodDefinition adviceMethod)
            : base(p, adviceMethod)
        {
        }

        public VariableDefinition Variable { get; protected set; }

        public FieldReference Field { get; protected set; }
    }

    internal sealed class MethodExecutionArgsInitWeaver : AdviceArgsInitWeaver
    {
        public MethodExecutionArgsInitWeaver(AspectWeaver2 p, MethodDefinition adviceMethod)
            : base(p, adviceMethod)
        {
        }

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset, ICollection<AdviceWeaver> previous)
        {
            var methodArgsWeaver = previous.OfType<MethodArgsInitWeaver>().FirstOrDefault();

            TypeReference meaType = P.Context.SafeImport(P.Context.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = P.Context.SafeImport(P.Context.Spinner.MethodExecutionArgs_ctor);

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

            var methodArgsVar = methodArgsWeaver?.Variable;

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