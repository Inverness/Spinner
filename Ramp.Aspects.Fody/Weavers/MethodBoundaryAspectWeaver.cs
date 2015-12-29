using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Ramp.Aspects.Fody.Weavers
{
    internal enum StateMachineKind
    {
        None,
        Iterator,
        Async
    }

    internal class MethodBoundaryAspectWeaver : AspectWeaver
    {
        protected const string OnEntryAdviceName = "OnEntry";
        protected const string OnExitAdviceName = "OnExit";
        protected const string OnExceptionAdviceName = "OnException";
        protected const string OnSuccessAdviceName = "OnSuccess";
        protected const string OnYieldAdviceName = "OnYield";
        protected const string OnResumeAdviceName = "OnResume";
        protected const string FilterExceptionAdviceName = "FilterException";

        internal static void Weave(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex)
        {
            string cacheFieldName = $"<{method.Name}>z__CachedAspect" + aspectIndex;
            
            FieldReference aspectField;
            CreateAspectCacheField(mwc, method.DeclaringType, aspectType, cacheFieldName, out aspectField);

            TypeDefinition stateMachineType;
            StateMachineKind stateMachineKind = GetStateMachineKind(mwc, method, out stateMachineType);
            
            TypeDefinition effectiveReturnType;

            switch (stateMachineKind)
            {
                case StateMachineKind.None:
                    effectiveReturnType = method.ReturnType == mwc.Module.TypeSystem.Void
                        ? null
                        : method.ReturnType.Resolve();
                    break;
                case StateMachineKind.Iterator:
                    effectiveReturnType = null;
                    break;
                case StateMachineKind.Async:
                    effectiveReturnType = method.ReturnType.IsGenericInstance
                        ? ((GenericInstanceType) method.ReturnType).GenericArguments.Single().Resolve()
                        : null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            VariableDefinition returnValueHolder = null;
            if (effectiveReturnType != null)
                returnValueHolder = method.Body.AddVariableDefinition(effectiveReturnType);

            WrapWithTryCatch(mwc, method, aspectType, returnValueHolder, aspectField, null);

            method.Body.RemoveNops();
        }

        protected static void WrapWithTryCatch(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            VariableDefinition returnValueHolder,
            FieldReference aspectField,
            VariableDefinition meaVariable)
        {
            List<MethodDefinition> allMethods = aspectType.GetInheritedMethods().ToList();
            MethodDefinition filterExceptionDef = allMethods.Single(m => m.Name == FilterExceptionAdviceName);
            MethodReference filterExcetion = mwc.SafeImport(filterExceptionDef);
            MethodDefinition onExceptionDef = allMethods.Single(m => m.Name == OnExceptionAdviceName);
            MethodReference onException = mwc.SafeImport(onExceptionDef);

            Collection<Ins> inslist = method.Body.Instructions;

            var jtNewReturn = Ins.Create(OpCodes.Nop);

            // Replace returns with store and leave
            for (int i = 0; i < inslist.Count; i++)
            {
                Ins ins = inslist[i];

                if (ins.OpCode == OpCodes.Ret)
                {
                    if (returnValueHolder != null)
                    {
                        var nins1 = Ins.Create(OpCodes.Stloc, returnValueHolder);
                        var nins2 = Ins.Create(OpCodes.Leave, jtNewReturn);

                        ReplaceOperands(inslist, ins, nins1);

                        inslist[i] = nins1;
                        inslist.Insert(++i, nins2);
                    }
                    else
                    {
                        var nins = Ins.Create(OpCodes.Leave, jtNewReturn);

                        ReplaceOperands(inslist, ins, nins);

                        inslist[i] = nins;
                    }
                }
            }

            Ins tryStart = inslist.First();

            // Write filter
            TypeReference exceptionType = mwc.SafeImport(mwc.Framework.Exception);

            VariableDefinition exceptionHolder = method.Body.AddVariableDefinition(exceptionType);
            
            Ins labelFilterTrue = Ins.Create(OpCodes.Nop);
            Ins labelFilterEnd = Ins.Create(OpCodes.Nop);
            
            inslist.Add(Ins.Create(OpCodes.Isinst, exceptionType));
            Ins filterStart = inslist.Last();
            inslist.Add(Ins.Create(OpCodes.Dup));
            inslist.Add(Ins.Create(OpCodes.Brtrue, labelFilterTrue));

            inslist.Add(Ins.Create(OpCodes.Pop)); // exception
            inslist.Add(Ins.Create(OpCodes.Ldc_I4_0));
            inslist.Add(Ins.Create(OpCodes.Br, labelFilterEnd));

            inslist.Add(labelFilterTrue);
            inslist.Add(Ins.Create(OpCodes.Stloc, exceptionHolder));
            inslist.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
            if (meaVariable != null)
                inslist.Add(Ins.Create(OpCodes.Ldloc, meaVariable));
            else
                inslist.Add(Ins.Create(OpCodes.Ldnull));
            inslist.Add(Ins.Create(OpCodes.Ldloc, exceptionHolder));
            inslist.Add(Ins.Create(OpCodes.Callvirt, filterExcetion));
            inslist.Add(Ins.Create(OpCodes.Ldc_I4_0));
            inslist.Add(Ins.Create(OpCodes.Cgt_Un));
            inslist.Add(labelFilterEnd);
            inslist.Add(Ins.Create(OpCodes.Endfilter));

            // Write Handler

            inslist.Add(Ins.Create(OpCodes.Pop)); // not sure, in generated code
            Ins handlerStart = inslist.Last();
            inslist.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
            if (meaVariable != null)
                inslist.Add(Ins.Create(OpCodes.Ldloc, meaVariable));
            else
                inslist.Add(Ins.Create(OpCodes.Ldnull));
            inslist.Add(Ins.Create(OpCodes.Callvirt, onException));
            inslist.Add(Ins.Create(OpCodes.Leave, jtNewReturn));

            // Finish

            inslist.Add(jtNewReturn);
            Ins handlerEnd = inslist.Last();
            if (returnValueHolder != null)
                inslist.Add(Ins.Create(OpCodes.Ldloc, returnValueHolder));
            inslist.Add(Ins.Create(OpCodes.Ret));

            // NOTE TryEnd and HandlerEnd point to the instruction AFTER the 'leave'

            // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
            var eh = new ExceptionHandler(ExceptionHandlerType.Catch | ExceptionHandlerType.Filter)
            {
                TryStart = tryStart,
                TryEnd = filterStart,
                FilterStart = filterStart,
                HandlerStart = handlerStart,
                HandlerEnd = handlerEnd,
                CatchType = exceptionType
            };

            method.Body.ExceptionHandlers.Add(eh);
        }

        private static void ReplaceOperands(Collection<Ins> instructions, Ins oldValue, Ins newValue)
        {
            foreach (Ins instruction in instructions)
                if (ReferenceEquals(instruction.Operand, oldValue))
                    instruction.Operand = newValue;
        }

        /// <summary>
        /// Discover whether a method is an async state machine creator and the nested type that implements it.
        /// </summary>
        protected static StateMachineKind GetStateMachineKind(ModuleWeavingContext mwc, MethodDefinition method, out TypeDefinition type)
        {
            TypeDefinition asmType = mwc.Framework.AsyncStateMachineAttribute;
            TypeDefinition ismType = mwc.Framework.IteratorStateMachineAttribute;

            foreach (CustomAttribute a in method.CustomAttributes)
            {
                TypeReference atype = a.AttributeType;
                if (atype.Name == asmType.Name && atype.Namespace == asmType.Namespace)
                {
                    type = (TypeDefinition) a.ConstructorArguments.First().Value;
                    return StateMachineKind.Async;
                }
                if (atype.Name == ismType.Name && atype.Namespace == ismType.Namespace)
                {
                    type = (TypeDefinition) a.ConstructorArguments.First().Value;
                    return StateMachineKind.Iterator;
                }
            }

            type = null;
            return StateMachineKind.None;
        }
    }
}
