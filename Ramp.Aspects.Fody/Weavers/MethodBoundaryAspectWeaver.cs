using System;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
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

            Features features = GetFeatures(mwc, aspectType);
            
            FieldReference aspectField;
            CreateAspectCacheField(mwc, method.DeclaringType, aspectType, cacheFieldName, out aspectField);

            MethodDefinition moveNextMethod;
            StateMachineKind stateMachineKind = GetStateMachineInfo(mwc, method, out moveNextMethod);
            
            // Decide the effective return type
            TypeDefinition effectiveReturnType;
            switch (stateMachineKind)
            {
                case StateMachineKind.None:
                    effectiveReturnType = method.ReturnType == mwc.Module.TypeSystem.Void
                        ? null
                        : method.ReturnType.Resolve();

                    WeaveMethod(mwc,
                                method,
                                aspectType,
                                aspectIndex,
                                features,
                                aspectField,
                                effectiveReturnType);

                    method.Body.OptimizeMacros();
                    method.Body.RemoveNops();
                    method.Body.UpdateOffsets();
                    break;
                case StateMachineKind.Iterator:
                    WeaveIteratorMethod(mwc,
                                        method,
                                        aspectType,
                                        aspectIndex,
                                        features,
                                        aspectField,
                                        null,
                                        moveNextMethod);
                    break;
                case StateMachineKind.Async:
                    effectiveReturnType = method.ReturnType.IsGenericInstance
                        ? ((GenericInstanceType) method.ReturnType).GenericArguments.Single().Resolve()
                        : null;

                    moveNextMethod.Body.SimplifyMacros();

                    WeaveAsyncMethod(mwc,
                                     method,
                                     aspectType,
                                     aspectIndex,
                                     features,
                                     aspectField,
                                     effectiveReturnType,
                                     moveNextMethod);

                    //moveNextMethod.Body.RemoveNops();
                    moveNextMethod.Body.OptimizeMacros();
                    moveNextMethod.Body.UpdateOffsets();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected static void WeaveMethod(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex,
            Features features,
            FieldReference aspectField,
            TypeDefinition effectiveReturnType
            )

        {
            var insc = method.Body.Instructions;
            var originalInsc = new Collection<Ins>(insc);
            insc.Clear();

            VariableDefinition returnValueVar = null;
            if (effectiveReturnType != null)
                returnValueVar = method.Body.AddVariableDefinition(effectiveReturnType);

            WriteAspectInit(mwc, method, insc.Count, aspectType, aspectField);

            VariableDefinition argumentsVar = null;
            if ((features & Features.GetArguments) != 0)
            {
                WriteArgumentContainerInit(mwc, method, insc.Count, out argumentsVar);
                WriteCopyArgumentsToContainer(mwc, method, insc.Count, argumentsVar, true);
            }

            VariableDefinition meaVar;
            WriteMeaInit(mwc, method, argumentsVar, insc.Count, out meaVar);

            // Write OnEntry call
            WriteOnEntryCall(mwc, method, insc.Count, aspectType, features, returnValueVar, aspectField, meaVar);

            // Re-add original body
            int tryStartIndex = insc.Count;
            insc.AddRange(originalInsc);

            // Wrap the body, including the OnEntry() call, in an exception handler
            WriteExceptionHandler(mwc, method, aspectType, features, returnValueVar, aspectField, meaVar, insc.Count, tryStartIndex);
        }

        protected static void WeaveAsyncMethod(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex,
            Features features,
            FieldReference aspectField,
            TypeDefinition effectiveReturnType,
            MethodDefinition stateMachine
            )
        {
            int onEntryIndex;
            int tryIndex;
            int[] yieldIndices;
            int[] resumeIndices;
            
            FindAsyncStateMachineInsertionPoints(method,
                                                 stateMachine,
                                                 out onEntryIndex,
                                                 out tryIndex,
                                                 out yieldIndices,
                                                 out resumeIndices);


            VariableDefinition returnValueVar = null;
            if (effectiveReturnType != null)
                returnValueVar = method.Body.AddVariableDefinition(effectiveReturnType);

            var insc = stateMachine.Body.Instructions;
            int roffset = insc.Count - onEntryIndex;

            WriteAspectInit(mwc, stateMachine, insc.Count - roffset, aspectType, aspectField);

            VariableDefinition arguments = null;
            if ((features & Features.GetArguments) != 0)
            {
                WriteSmArgumentContainerInit(mwc, method, stateMachine, insc.Count - roffset, out arguments);
                WriteSmCopyArgumentsToContainer(mwc, method, stateMachine, insc.Count - roffset, arguments, true);
            }

            FieldDefinition mea;
            WriteSmMeaInit(mwc, method, stateMachine, arguments, insc.Count - roffset, out mea);

            // Write OnEntry call
            WriteSmOnEntryCall(mwc,
                               method,
                               stateMachine,
                               insc.Count - roffset,
                               aspectType,
                               features,
                               returnValueVar,
                               aspectField,
                               mea);

        }

        protected static void WeaveIteratorMethod(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex,
            Features features,
            FieldReference aspectField,
            TypeDefinition effectiveReturnType,
            MethodDefinition moveNextMethod
            )
        {
        }

        protected static void FindAsyncStateMachineInsertionPoints(
            MethodDefinition method,
            MethodDefinition moveNextMethod,
            out int bodyStartIndex,
            out int bodyLeaveIndex,
            out int[] yieldIndices,
            out int[] resumeIndices
            )
        {
            Collection<Ins> inslist = moveNextMethod.Body.Instructions;
            ExceptionHandler outerExceptionHandler = moveNextMethod.Body.ExceptionHandlers.Last();

            int tryStartIndex = inslist.IndexOf(outerExceptionHandler.TryStart);
            int tryEndIndex = inslist.IndexOf(outerExceptionHandler.TryEnd);

            // Find the start of the body, which will be the first instruction after the first set of branch
            // instructions
            int firstBranch = SeekInstruction(inslist, tryStartIndex, IsBranching);
            bodyStartIndex = SeekInstruction(inslist, firstBranch, i => !IsBranching(i));

            bodyLeaveIndex = tryEndIndex - 1;
            Debug.Assert(IsLeave(inslist[bodyLeaveIndex]));

            // Identify yield and resume points by looking for calls to a get_IsCompleted property and a GetResult
            // method. These can be defined on any type due to how awaitables work. IsCompleted is required to be
            // a property, not a field.
            var yieldIndicesList = new Collection<int>();
            var resumeIndicesList = new Collection<int>();

            TypeDefinition awaitableType = null;
            int yieldIndex = -1;
            for (int i = bodyStartIndex; i < bodyLeaveIndex; i++)
            {
                if (inslist[i].OpCode != OpCodes.Call)
                    continue;

                var mr = (MethodReference) inslist[i].Operand;

                if (yieldIndex == -1 && mr.Name == "get_IsCompleted")
                {
                    // Expect a brtrue after the call
                    Debug.Assert(IsBranching(inslist[i + 1]) && !IsBranching(inslist[i + 2]));

                    awaitableType = mr.DeclaringType.Resolve();
                    yieldIndex = i + 2;
                }
                else if (yieldIndex != -1 && mr.Name == "GetResult" && mr.DeclaringType.Resolve() == awaitableType)
                {
                    // resume after the store local
                    int resumeIndex = mr.ReturnType == mr.Module.TypeSystem.Void ? i + 1 : i + 2;

                    yieldIndicesList.Add(yieldIndex);
                    resumeIndicesList.Add(resumeIndex);

                    yieldIndex = -1;
                }
            }

            yieldIndices = yieldIndicesList.ToArray();
            resumeIndices = resumeIndicesList.ToArray();
        }

        protected static bool IsStoreLocal(Ins ins)
        {
            return IsStoreLocal(ins.OpCode);
        }

        protected static bool IsStoreLocal(OpCode op)
        {
            return op == OpCodes.Stloc ||
                   op == OpCodes.Stloc_0 ||
                   op == OpCodes.Stloc_1 ||
                   op == OpCodes.Stloc_2 ||
                   op == OpCodes.Stloc_3 ||
                   op == OpCodes.Stloc_S;
        }

        protected static bool IsBranching(Ins ins)
        {
            OperandType ot = ins.OpCode.OperandType;

            return ot == OperandType.InlineSwitch ||
                   ot == OperandType.InlineBrTarget ||
                   ot == OperandType.ShortInlineBrTarget;
        }

        protected static bool IsLeave(Ins ins)
        {
            return ins.OpCode == OpCodes.Leave || ins.OpCode == OpCodes.Leave_S;
        }

        protected static int SeekInstruction(Collection<Ins> list, int start, Func<Ins, bool> check)
        {
            for (int i = start; i < list.Count; i++)
            {
                if (check(list[i]))
                    return i;
            }
            return -1;
        }

        protected static void ExpectInstruction(Collection<Ins> list, int index, OpCode opcode)
        {
            if (index >= list.Count)
                throw new InvalidOperationException("end of instruction list");
            if (list[index].OpCode != opcode)
                throw new InvalidOperationException("unexpected instruction");
        }

        protected static void WriteMeaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            VariableDefinition argumentsVar,
            int offset,
            out VariableDefinition meaVar)
        {
            TypeReference meaType = mwc.SafeImport(mwc.Library.MethodExecutionArgs);
            MethodReference meaCtor = mwc.SafeImport(mwc.Library.MethodExecutionArgs_ctor);

            meaVar = method.Body.AddVariableDefinition(meaType);

            var insc = new Collection<Ins>();

            if (method.IsStatic)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                if (method.DeclaringType.IsValueType)
                {
                    insc.Add(Ins.Create(OpCodes.Ldobj, method.DeclaringType));
                    insc.Add(Ins.Create(OpCodes.Box, method.DeclaringType));
                }
            }

            if (argumentsVar == null)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, argumentsVar));
            }

            insc.Add(Ins.Create(OpCodes.Newobj, meaCtor));
            insc.Add(Ins.Create(OpCodes.Stloc, meaVar));

            method.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteSmMeaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachine,
            VariableDefinition arguments,
            int offset,
            out FieldDefinition mea)
        {
            TypeReference meaType = mwc.SafeImport(mwc.Library.MethodExecutionArgs);
            MethodReference meaCtor = mwc.SafeImport(mwc.Library.MethodExecutionArgs_ctor);

            mea = new FieldDefinition("<>z__mea", FieldAttributes.Private, meaType);
            stateMachine.DeclaringType.Fields.Add(mea);

            FieldReference thisField = stateMachine.DeclaringType.Fields.First(f => f.Name == StateMachineThisFieldName);

            var insc = new Collection<Ins>();

            insc.Add(Ins.Create(OpCodes.Ldarg_0)); // for stfld

            if (method.IsStatic)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Dup));
                insc.Add(Ins.Create(OpCodes.Ldfld, thisField));
                if (method.DeclaringType.IsValueType)
                {
                    insc.Add(Ins.Create(OpCodes.Ldobj, method.DeclaringType));
                    insc.Add(Ins.Create(OpCodes.Box, method.DeclaringType));
                }
            }

            if (arguments == null)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, arguments));
            }

            insc.Add(Ins.Create(OpCodes.Newobj, meaCtor));

            insc.Add(Ins.Create(OpCodes.Stfld, mea));

            stateMachine.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteOnEntryCall(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int offset,
            TypeDefinition aspectType,
            Features features,
            VariableDefinition returnValueHolder,
            FieldReference aspectField,
            VariableDefinition meaVar)
        {
            if ((features & Features.OnEntry) == 0)
                return;

            MethodDefinition onEntryDef = aspectType.GetInheritedMethods().First(m => m.Name == OnEntryAdviceName);
            MethodReference onEntry = mwc.SafeImport(onEntryDef);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, aspectField),
                Ins.Create(OpCodes.Ldloc, meaVar),
                Ins.Create(OpCodes.Callvirt, onEntry)
            };

            method.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteSmOnEntryCall(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachine,
            int offset,
            TypeDefinition aspectType,
            Features features,
            VariableDefinition returnValueHolder,
            FieldReference aspectField,
            FieldReference meaVar)
        {
            if ((features & Features.OnEntry) == 0)
                return;

            MethodDefinition onEntryDef = aspectType.GetInheritedMethods().First(m => m.Name == OnEntryAdviceName);
            MethodReference onEntry = mwc.SafeImport(onEntryDef);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, aspectField),
                Ins.Create(OpCodes.Ldarg_0),
                Ins.Create(OpCodes.Ldfld, meaVar),
                Ins.Create(OpCodes.Callvirt, onEntry)
            };

            stateMachine.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteExceptionHandler(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            Features features,
            VariableDefinition returnValueHolder,
            FieldReference aspectField,
            VariableDefinition meaVar,
            int offset,
            int tryStartIndex)
        {
            bool withException = (features & Features.OnException) != 0;
            bool withExit = (features & Features.OnExit) != 0;
            bool withSuccess = (features & Features.OnSuccess) != 0;

            if (!(withException || withExit || withSuccess))
                return;

            Ins labelNewReturn = CreateNop();
            Ins labelSuccess = CreateNop();
            Collection<Ins> bodyInsc = method.Body.Instructions;

            // Replace returns with a break or leave 
            int last = bodyInsc.Count - 1;
            for (int i = tryStartIndex; i < bodyInsc.Count; i++)
            {
                Ins ins = bodyInsc[i];

                if (ins.OpCode == OpCodes.Ret)
                {
                    var insNewBreak = withSuccess
                        ? Ins.Create(OpCodes.Br, labelSuccess)
                        : Ins.Create(OpCodes.Leave, labelNewReturn);

                    if (returnValueHolder != null)
                    {
                        var insStoreReturnValue = Ins.Create(OpCodes.Stloc, returnValueHolder);

                        method.Body.ReplaceBranchTargets(ins, insStoreReturnValue);
                        bodyInsc[i] = insStoreReturnValue;

                        // Do not write a jump to the very next instruction
                        if (withSuccess && i == last)
                            break;

                        bodyInsc.Insert(++i, insNewBreak);
                        last++;
                    }
                    else
                    {
                        if (withSuccess && i == last)
                        {
                            // Replace with Nop since there is not another operand to replace it with
                            var insNop = CreateNop();

                            method.Body.ReplaceBranchTargets(ins, insNop);
                            bodyInsc[i] = insNop;
                        }
                        else
                        {
                            method.Body.ReplaceBranchTargets(ins, insNewBreak);
                            bodyInsc[i] = insNewBreak;
                        }
                    }
                }
            }

            var insc = new Collection<Ins>();

            // Write success block

            if (withSuccess)
            {
                MethodDefinition onSuccessDef =
                    aspectType.GetInheritedMethods().First(m => m.Name == OnSuccessAdviceName);
                MethodReference onSuccess = mwc.SafeImport(onSuccessDef);

                insc.Add(labelSuccess);
                insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
                if (meaVar != null)
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                else
                    insc.Add(Ins.Create(OpCodes.Ldnull));
                insc.Add(Ins.Create(OpCodes.Callvirt, onSuccess));

                if (withException || withExit)
                    insc.Add(Ins.Create(OpCodes.Leave, labelNewReturn));
            }

            // Write exception filter and handler

            TypeReference exceptionType = null;
            int ehTryCatchEnd = -1;
            int ehFilterStart = -1;
            int ehCatchStart = -1;
            int ehCatchEnd = -1;

            if (withException)
            {
                MethodDefinition filterExceptionDef =
                    aspectType.GetInheritedMethods().First(m => m.Name == FilterExceptionAdviceName);
                MethodReference filterExcetion = mwc.SafeImport(filterExceptionDef);
                MethodDefinition onExceptionDef =
                    aspectType.GetInheritedMethods().First(m => m.Name == OnExceptionAdviceName);
                MethodReference onException = mwc.SafeImport(onExceptionDef);
                exceptionType = mwc.SafeImport(mwc.Framework.Exception);

                VariableDefinition exceptionHolder = method.Body.AddVariableDefinition(exceptionType);

                Ins labelFilterTrue = CreateNop();
                Ins labelFilterEnd = CreateNop();

                ehTryCatchEnd = insc.Count;
                ehFilterStart = insc.Count;

                // Check if its actually Exception. Non-Exception types can be thrown by native code.
                insc.Add(Ins.Create(OpCodes.Isinst, exceptionType));
                insc.Add(Ins.Create(OpCodes.Dup));
                insc.Add(Ins.Create(OpCodes.Brtrue, labelFilterTrue));

                // Not an Exception, load 0 as endfilter argument
                insc.Add(Ins.Create(OpCodes.Pop));
                insc.Add(Ins.Create(OpCodes.Ldc_I4_0));
                insc.Add(Ins.Create(OpCodes.Br, labelFilterEnd));

                // Call FilterException()
                insc.Add(labelFilterTrue);
                insc.Add(Ins.Create(OpCodes.Stloc, exceptionHolder));
                insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
                if (meaVar != null)
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                else
                    insc.Add(Ins.Create(OpCodes.Ldnull));
                insc.Add(Ins.Create(OpCodes.Ldloc, exceptionHolder));
                insc.Add(Ins.Create(OpCodes.Callvirt, filterExcetion));

                // Compare FilterException result with 0 to get the endfilter argument
                insc.Add(Ins.Create(OpCodes.Ldc_I4_0));
                insc.Add(Ins.Create(OpCodes.Cgt_Un));
                insc.Add(labelFilterEnd);
                insc.Add(Ins.Create(OpCodes.Endfilter));

                ehCatchStart = insc.Count;

                insc.Add(Ins.Create(OpCodes.Pop)); // Exception already stored

                // Call OnException()
                if (meaVar != null)
                {
                    MethodReference getException = mwc.SafeImport(mwc.Library.MethodExecutionArgs_Exception.GetMethod);
                    MethodReference setException = mwc.SafeImport(mwc.Library.MethodExecutionArgs_Exception.SetMethod);

                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                    insc.Add(Ins.Create(OpCodes.Ldloc, exceptionHolder));
                    insc.Add(Ins.Create(OpCodes.Callvirt, setException));

                    insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                    insc.Add(Ins.Create(OpCodes.Callvirt, onException));

                    Ins labelCaught = CreateNop();

                    // If the Exception property was set to null, return normally, otherwise rethrow
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                    insc.Add(Ins.Create(OpCodes.Callvirt, getException));
                    insc.Add(Ins.Create(OpCodes.Dup));
                    insc.Add(Ins.Create(OpCodes.Brfalse, labelCaught));

                    insc.Add(Ins.Create(OpCodes.Rethrow));

                    insc.Add(labelCaught);
                    insc.Add(Ins.Create(OpCodes.Pop));
                }
                else
                {
                    insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
                    insc.Add(Ins.Create(OpCodes.Ldnull));
                    insc.Add(Ins.Create(OpCodes.Callvirt, onException));
                }
                insc.Add(Ins.Create(OpCodes.Leave, labelNewReturn));

                ehCatchEnd = insc.Count;
            }

            // End of try block for the finally handler

            int ehFinallyEnd = -1;
            int ehFinallyStart = -1;
            int ehTryFinallyEnd = -1;

            if (withExit)
            {
                MethodDefinition onExitDef = aspectType.GetInheritedMethods().First(m => m.Name == OnExitAdviceName);
                MethodReference onExit = mwc.SafeImport(onExitDef);

                insc.Add(Ins.Create(OpCodes.Leave, labelNewReturn));

                ehTryFinallyEnd = insc.Count;

                // Begin finally block

                ehFinallyStart = insc.Count;

                // Call OnExit()
                insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
                if (meaVar != null)
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                else
                    insc.Add(Ins.Create(OpCodes.Ldnull));
                insc.Add(Ins.Create(OpCodes.Callvirt, onExit));
                insc.Add(Ins.Create(OpCodes.Endfinally));

                ehFinallyEnd = insc.Count;
            }

            // End finally block

            insc.Add(labelNewReturn);

            // Return the previously stored result
            if (returnValueHolder != null)
                insc.Add(Ins.Create(OpCodes.Ldloc, returnValueHolder));
            insc.Add(Ins.Create(OpCodes.Ret));
            
            method.Body.InsertInstructions(offset, insc);

            if (withException)
            {
                // NOTE TryEnd and HandlerEnd point to the instruction AFTER the 'leave'

                var catchHandler = new ExceptionHandler(ExceptionHandlerType.Filter)
                {
                    TryStart = bodyInsc[tryStartIndex],
                    TryEnd = bodyInsc[offset + ehTryCatchEnd],
                    FilterStart = bodyInsc[offset + ehFilterStart],
                    HandlerStart = bodyInsc[offset + ehCatchStart],
                    HandlerEnd = bodyInsc[offset + ehCatchEnd],
                    CatchType = exceptionType
                };

                method.Body.ExceptionHandlers.Add(catchHandler);
            }

            if (withExit)
            {
                var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
                {
                    TryStart = bodyInsc[tryStartIndex],
                    TryEnd = bodyInsc[offset + ehTryFinallyEnd],
                    HandlerStart = bodyInsc[offset + ehFinallyStart],
                    HandlerEnd = bodyInsc[offset + ehFinallyEnd]
                };

                method.Body.ExceptionHandlers.Add(finallyHandler);
            }
        }

        /// <summary>
        /// Discover whether a method is an async state machine creator and the nested type that implements it.
        /// </summary>
        protected static StateMachineKind GetStateMachineInfo(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            out MethodDefinition moveNextMethod)
        {
            if (method.HasCustomAttributes)
            {
                TypeDefinition asmType = mwc.Framework.AsyncStateMachineAttribute;
                TypeDefinition ismType = mwc.Framework.IteratorStateMachineAttribute;

                foreach (CustomAttribute a in method.CustomAttributes)
                {
                    TypeReference atype = a.AttributeType;
                    if (atype.Name == asmType.Name && atype.Namespace == asmType.Namespace)
                    {
                        var type = (TypeDefinition) a.ConstructorArguments.First().Value;
                        moveNextMethod = type.Methods.Single(m => m.Name == "MoveNext");
                        return StateMachineKind.Async;
                    }
                    if (atype.Name == ismType.Name && atype.Namespace == ismType.Namespace)
                    {
                        var type = (TypeDefinition) a.ConstructorArguments.First().Value;
                        moveNextMethod = type.Methods.Single(m => m.Name == "MoveNext");
                        return StateMachineKind.Iterator;
                    }
                }
            }

            moveNextMethod = null;
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
                        if (a.AttributeType.Name == featuresAttributeType.Name && a.AttributeType.Namespace == featuresAttributeType.Namespace)
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
