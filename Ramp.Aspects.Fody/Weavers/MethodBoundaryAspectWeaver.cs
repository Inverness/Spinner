using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using InstructionCollection = Mono.Collections.Generic.Collection<Mono.Cecil.Cil.Instruction>;
using Instruction = Mono.Cecil.Cil.Instruction;

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

            Features features = GetFeatures(mwc, aspectType);
            
            FieldReference aspectField;
            CreateAspectCacheField(mwc, method.DeclaringType, aspectType, cacheFieldName, out aspectField);

            TypeDefinition stateMachineType;
            StateMachineKind stateMachineKind = GetStateMachineKind(mwc, method, out stateMachineType);

            if (stateMachineKind != StateMachineKind.None)
                throw new NotImplementedException("state machines not yet supported");
            
            // Decide the effective return type
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

            var originalBody = new InstructionCollection(method.Body.Instructions);
            method.Body.Instructions.Clear();
            ILProcessor il = method.Body.GetILProcessor();

            VariableDefinition returnValueVar = null;
            if (effectiveReturnType != null)
                returnValueVar = method.Body.AddVariableDefinition(effectiveReturnType);

            WriteAspectInit(mwc, method, aspectType, aspectField, il);

            VariableDefinition argumentsVar = null;
            if ((features & Features.GetArguments) != 0)
            {
                WriteArgumentContainerInit(mwc, method, il, out argumentsVar);
                WriteCopyArgumentsToContainer(mwc, method, il, argumentsVar, true);
            }

            VariableDefinition meaVar;
            WriteMeaInit(mwc, method, argumentsVar, il, out meaVar);

            // Write OnEntry call
            WriteOnEntryCall(mwc, method, aspectType, features, returnValueVar, aspectField, meaVar, il);

            // Re-add original body
            int tryStartIndex = method.Body.Instructions.Count;
            method.Body.Instructions.AddRange(originalBody);

            // Wrap the body, including the OnEntry() call, in an exception handler
            WriteExceptionHandler(mwc, method, aspectType, features, returnValueVar, aspectField, meaVar, il, tryStartIndex);

            method.Body.OptimizeMacros();
            method.Body.RemoveNops();
        }

        protected static void WriteMeaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            VariableDefinition argumentsVar,
            ILProcessor il,
            out VariableDefinition meaVar
            )
        {
            TypeReference meaType = mwc.SafeImport(mwc.Library.MethodExecutionArgs);
            MethodReference meaCtor = mwc.SafeImport(mwc.Library.MethodExecutionArgs_ctor);

            meaVar = method.Body.AddVariableDefinition(meaType);

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

            if (argumentsVar == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, argumentsVar);
            }

            il.Emit(OpCodes.Newobj, meaCtor);
            il.Emit(OpCodes.Stloc, meaVar);
        }

        protected static void WriteOnEntryCall(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            Features features,
            VariableDefinition returnValueHolder,
            FieldReference aspectField,
            VariableDefinition meaVar,
            ILProcessor il)
        {
            if ((features & Features.OnEntry) == 0)
                return;

            MethodDefinition onEntryDef = aspectType.GetInheritedMethods().First(m => m.Name == OnEntryAdviceName);
            MethodReference onEntry = mwc.SafeImport(onEntryDef);

            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, meaVar);
            il.Emit(OpCodes.Callvirt, onEntry);
        }

        protected static void WriteExceptionHandler(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            Features features,
            VariableDefinition returnValueHolder,
            FieldReference aspectField,
            VariableDefinition meaVar,
            ILProcessor il,
            int tryStartIndex)
        {
            bool withException = (features & Features.OnException) != 0;
            bool withExit = (features & Features.OnExit) != 0;
            bool withSuccess = (features & Features.OnSuccess) != 0;

            if (!(withException || withExit || withSuccess))
                return;

            Instruction labelNewReturn = CreateNop();
            Instruction labelSuccess = CreateNop();
            InstructionCollection inslist = il.Body.Instructions;

            // Replace returns with a break or leave 
            int last = inslist.Count - 1;
            for (int i = tryStartIndex; i < inslist.Count; i++)
            {
                Instruction ins = inslist[i];

                if (ins.OpCode == OpCodes.Ret)
                {
                    var insNewBreak = withSuccess
                        ? Instruction.Create(OpCodes.Br, labelSuccess)
                        : Instruction.Create(OpCodes.Leave, labelNewReturn);

                    if (returnValueHolder != null)
                    {
                        var insStoreReturnValue = Instruction.Create(OpCodes.Stloc, returnValueHolder);

                        inslist.ReplaceOperands(ins, insStoreReturnValue);
                        inslist[i] = insStoreReturnValue;

                        // Do not write a jump to the very next instruction
                        if (withSuccess && i == last)
                            break;

                        inslist.Insert(++i, insNewBreak);
                        last++;
                    }
                    else
                    {
                        if (withSuccess && i == last)
                        {
                            // Replace with Nop since there is not another operand to replace it with
                            var insNop = CreateNop();

                            inslist.ReplaceOperands(ins, insNop);
                            inslist[i] = insNop;
                        }
                        else
                        {
                            inslist.ReplaceOperands(ins, insNewBreak);
                            inslist[i] = insNewBreak;
                        }
                    }
                }
            }

            // Write success block

            if (withSuccess)
            {
                MethodDefinition onSuccessDef = aspectType.GetInheritedMethods().First(m => m.Name == OnSuccessAdviceName);
                MethodReference onSuccess = mwc.SafeImport(onSuccessDef);
                
                il.Append(labelSuccess);
                il.Emit(OpCodes.Ldsfld, aspectField);
                if (meaVar != null)
                    il.Emit(OpCodes.Ldloc, meaVar);
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, onSuccess);

                if (withException || withExit)
                    il.Emit(OpCodes.Leave, labelNewReturn);
            }

            // Write exception filter and handler

            TypeReference exceptionType = null;
            int ehTryCatchEnd = -1;
            int ehFilterStart = -1;
            int ehCatchStart = -1;
            int ehCatchEnd = -1;

            if (withException)
            {
                MethodDefinition filterExceptionDef = aspectType.GetInheritedMethods().First(m => m.Name == FilterExceptionAdviceName);
                MethodReference filterExcetion = mwc.SafeImport(filterExceptionDef);
                MethodDefinition onExceptionDef = aspectType.GetInheritedMethods().First(m => m.Name == OnExceptionAdviceName);
                MethodReference onException = mwc.SafeImport(onExceptionDef);
                exceptionType = mwc.SafeImport(mwc.Framework.Exception);

                VariableDefinition exceptionHolder = method.Body.AddVariableDefinition(exceptionType);

                Instruction labelFilterTrue = CreateNop();
                Instruction labelFilterEnd = CreateNop();

                ehTryCatchEnd = inslist.Count;
                ehFilterStart = inslist.Count;

                // Check if its actually Exception. Non-Exception types can be thrown by native code.
                il.Emit(OpCodes.Isinst, exceptionType);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brtrue, labelFilterTrue);

                // Not an Exception, load 0 as endfilter argument
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Br, labelFilterEnd);

                // Call FilterException()
                il.Append(labelFilterTrue);
                il.Emit(OpCodes.Stloc, exceptionHolder);
                il.Emit(OpCodes.Ldsfld, aspectField);
                if (meaVar != null)
                    il.Emit(OpCodes.Ldloc, meaVar);
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldloc, exceptionHolder);
                il.Emit(OpCodes.Callvirt, filterExcetion);

                // Compare FilterException result with 0 to get the endfilter argument
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Cgt_Un);
                il.Append(labelFilterEnd);
                il.Emit(OpCodes.Endfilter);

                ehCatchStart = inslist.Count;

                il.Emit(OpCodes.Pop); // Exception already stored

                // Call OnException()
                if (meaVar != null)
                {
                    MethodReference getException = mwc.SafeImport(mwc.Library.MethodExecutionArgs_Exception.GetMethod);
                    MethodReference setException = mwc.SafeImport(mwc.Library.MethodExecutionArgs_Exception.SetMethod);

                    il.Emit(OpCodes.Ldloc, meaVar);
                    il.Emit(OpCodes.Ldloc, exceptionHolder);
                    il.Emit(OpCodes.Callvirt, setException);

                    il.Emit(OpCodes.Ldsfld, aspectField);
                    il.Emit(OpCodes.Ldloc, meaVar);
                    il.Emit(OpCodes.Callvirt, onException);

                    Instruction labelCaught = CreateNop();

                    // If the Exception property was set to null, return normally, otherwise rethrow
                    il.Emit(OpCodes.Ldloc, meaVar);
                    il.Emit(OpCodes.Callvirt, getException);
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brfalse, labelCaught);

                    il.Emit(OpCodes.Rethrow);

                    il.Append(labelCaught);
                    il.Emit(OpCodes.Pop);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, aspectField);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Callvirt, onException);
                }
                il.Emit(OpCodes.Leave, labelNewReturn);

                ehCatchEnd = inslist.Count;
            }

            // End of try block for the finally handler

            int ehFinallyEnd = -1;
            int ehFinallyStart = -1;
            int ehTryFinallyEnd = -1;

            if (withExit)
            {
                MethodDefinition onExitDef = aspectType.GetInheritedMethods().First(m => m.Name == OnExitAdviceName);
                MethodReference onExit = mwc.SafeImport(onExitDef);

                il.Emit(OpCodes.Leave, labelNewReturn);

                ehTryFinallyEnd = inslist.Count;

                // Begin finally block

                ehFinallyStart = inslist.Count;

                // Call OnExit()
                il.Emit(OpCodes.Ldsfld, aspectField);
                if (meaVar != null)
                    il.Emit(OpCodes.Ldloc, meaVar);
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, onExit);
                il.Emit(OpCodes.Endfinally);

                ehFinallyEnd = inslist.Count;
            }

            // End finally block
            
            il.Append(labelNewReturn);

            // Return the previously stored result
            if (returnValueHolder != null)
                il.Emit(OpCodes.Ldloc, returnValueHolder);
            il.Emit(OpCodes.Ret);

            if (withException)
            {
                // NOTE TryEnd and HandlerEnd point to the instruction AFTER the 'leave'

                var catchHandler = new ExceptionHandler(ExceptionHandlerType.Filter)
                {
                    TryStart = inslist[tryStartIndex],
                    TryEnd = inslist[ehTryCatchEnd],
                    FilterStart = inslist[ehFilterStart],
                    HandlerStart = inslist[ehCatchStart],
                    HandlerEnd = inslist[ehCatchEnd],
                    CatchType = exceptionType
                };

                method.Body.ExceptionHandlers.Add(catchHandler);
            }

            if (withExit)
            {
                var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
                {
                    TryStart = inslist[tryStartIndex],
                    TryEnd = inslist[ehTryFinallyEnd],
                    HandlerStart = inslist[ehFinallyStart],
                    HandlerEnd = inslist[ehFinallyEnd]
                };

                method.Body.ExceptionHandlers.Add(finallyHandler);
            }
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

        protected static Features GetFeatures(ModuleWeavingContext mwc, TypeDefinition aspectType)
        {
            TypeDefinition featuresAttributeType = mwc.Library.FeaturesAttribute;

            TypeDefinition currentType = aspectType;
            while (currentType != null)
            {
                if (currentType.HasCustomAttributes)
                {
                    foreach (CustomAttribute a in currentType.CustomAttributes)
                    {
                        if (a.AttributeType.Name == featuresAttributeType.Name &&
                            a.AttributeType.Namespace == featuresAttributeType.Namespace)
                        {
                            return (Features) (int) a.ConstructorArguments.First().Value;
                        }
                    }
                }

                currentType = currentType.BaseType.Resolve();
            }

            return Features.None;
        }
    }
}
