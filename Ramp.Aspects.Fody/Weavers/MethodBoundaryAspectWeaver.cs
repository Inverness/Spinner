﻿using System;
using System.Collections.Generic;
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

                    method.Body.SimplifyMacros();

                    WeaveMethod(mwc,
                                method,
                                aspectType,
                                aspectIndex,
                                features,
                                aspectField,
                                effectiveReturnType);

                    method.Body.RemoveNops();
                    method.Body.OptimizeMacros();
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

                    moveNextMethod.Body.RemoveNops();
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

            bool withException = (features & Features.OnException) != 0;
            bool withExit = (features & Features.OnExit) != 0;
            bool withSuccess = (features & Features.OnSuccess) != 0;

            if (withException || withExit || withSuccess)
            {
                Ins labelNewReturn = CreateNop();
                Ins labelSuccess = CreateNop();

                RewriteReturns(method,
                               tryStartIndex,
                               method.Body.Instructions.Count - 1,
                               returnValueVar,
                               labelSuccess,
                               withSuccess);

                insc.Add(labelSuccess);

                // Write success block

                if (withSuccess)
                {
                    WriteSuccessHandler(mwc,
                                        method,
                                        aspectType,
                                        aspectField,
                                        meaVar,
                                        null,
                                        returnValueVar,
                                        insc.Count);
                }

                if (withException || withExit)
                    insc.Add(Ins.Create(OpCodes.Leave, labelNewReturn));

                // Write exception filter and handler

                if (withException)
                {
                    WriteCatchExceptionHandler(mwc,
                                               method,
                                               null,
                                               insc.Count,
                                               aspectType,
                                               aspectField,
                                               meaVar,
                                               null,
                                               tryStartIndex,
                                               labelNewReturn);
                }

                // End of try block for the finally handler

                if (withExit)
                {
                    WriteFinallyExceptionHandler(mwc,
                                                 method,
                                                 null,
                                                 insc.Count,
                                                 aspectType,
                                                 aspectField,
                                                 meaVar,
                                                 null,
                                                 tryStartIndex,
                                                 labelNewReturn);
                }

                // End finally block

                var insc2 = new Collection<Ins>();

                insc2.Add(labelNewReturn);

                // Return the previously stored result
                if (returnValueVar != null)
                    insc2.Add(Ins.Create(OpCodes.Ldloc, returnValueVar));
                insc2.Add(Ins.Create(OpCodes.Ret));

                method.Body.InsertInstructions(null, insc2);
            }
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
            Collection<Ins> yields;
            Collection<Ins> resumes;
            HashSet<Ins> awaitLeaves;

            FindAsyncStateMachineInsertionPoints(method,
                                                 stateMachine,
                                                 out onEntryIndex,
                                                 out tryIndex,
                                                 out yields,
                                                 out resumes,
                                                 out awaitLeaves);

            // The last exception handler is used for setting the returning task state.
            // This is needed to identify insertion points.
            ExceptionHandler taskExceptionHandler = stateMachine.Body.ExceptionHandlers.Last();
            
            var insc = stateMachine.Body.Instructions;

            // Start the try block in the same place as the state machine. This prevents the need to mess with existing
            // breaks.
            int tryStartOffset = insc.IndexOf(taskExceptionHandler.TryStart);

            // The offset from the end to where initialization code will go.
            int initEndOffset = insc.Count - onEntryIndex;

            // Offset from the end to the return leave instruction
            int leaveEndOffset = insc.Count - insc.IndexOf(taskExceptionHandler.TryEnd) + 1;

            // Find the variable used to set the task result.
            VariableDefinition resultVar = null;
            if (effectiveReturnType != null)
            {
                int resultStore = SeekInstructionR(insc, insc.Count - leaveEndOffset, IsStoreLocal);
                resultVar = (VariableDefinition) insc[resultStore].Operand;
            }

            WriteAspectInit(mwc, stateMachine, insc.Count - initEndOffset, aspectType, aspectField);

            VariableDefinition arguments = null;
            if ((features & Features.GetArguments) != 0)
            {
                WriteSmArgumentContainerInit(mwc, method, stateMachine, insc.Count - initEndOffset, out arguments);
                WriteSmCopyArgumentsToContainer(mwc, method, stateMachine, insc.Count - initEndOffset, arguments, true);
            }

            FieldDefinition mea;
            WriteSmMeaInit(mwc, method, stateMachine, arguments, insc.Count - initEndOffset, out mea);

            // Write OnEntry call
            WriteSmOnEntryCall(mwc,
                               method,
                               stateMachine,
                               insc.Count - initEndOffset,
                               aspectType,
                               features,
                               resultVar,
                               aspectField,
                               mea);

            bool withException = (features & Features.OnException) != 0;
            bool withExit = (features & Features.OnExit) != 0;
            bool withSuccess = (features & Features.OnSuccess) != 0;

            if (withException || withExit || withSuccess)
            {
                Ins labelSuccess = CreateNop();

                RewriteSmLeaves(stateMachine,
                                tryStartOffset,
                                insc.Count - leaveEndOffset - 1,
                                labelSuccess,
                                false);

                insc.Insert(insc.Count - leaveEndOffset, labelSuccess);

                // Write success block

                if (withSuccess)
                {
                    WriteSuccessHandler(mwc,
                                        stateMachine,
                                        aspectType,
                                        aspectField,
                                        null,
                                        mea,
                                        resultVar,
                                        insc.Count - leaveEndOffset);
                }

                // Mark where the leave instruction needs to be inserted before the exception handler
                int leaveInsertPoint = insc.Count - leaveEndOffset;

                if (withException)
                {
                    WriteCatchExceptionHandler(mwc,
                                               method,
                                               stateMachine,
                                               insc.Count - leaveEndOffset,
                                               aspectType,
                                               aspectField,
                                               null,
                                               mea,
                                               tryStartOffset,
                                               insc[insc.Count - leaveEndOffset]);
                }

                if (withExit)
                {
                    WriteFinallyExceptionHandler(mwc,
                                                 method,
                                                 stateMachine,
                                                 insc.Count - leaveEndOffset,
                                                 aspectType,
                                                 aspectField,
                                                 null,
                                                 mea,
                                                 tryStartOffset,
                                                 insc[insc.Count - leaveEndOffset]);
                }

                // Insert the try block leave right before the exception handlers
                if (withException || withExit)
                    stateMachine.Body.InsertInstructions(leaveInsertPoint, Ins.Create(OpCodes.Leave, insc[insc.Count - leaveEndOffset]));
                
                // Ensure the task EH is last
                stateMachine.Body.ExceptionHandlers.Remove(taskExceptionHandler);
                stateMachine.Body.ExceptionHandlers.Add(taskExceptionHandler);
            }
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
            out Collection<Ins> yields,
            out Collection<Ins> resumes,
            out HashSet<Ins> awaitLeaves 
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
            yields = new Collection<Ins>();
            resumes = new Collection<Ins>();
            awaitLeaves = new HashSet<Ins>();

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

                    for (int r = yieldIndex; r < bodyLeaveIndex; r++)
                    {
                        if (inslist[r].OpCode == OpCodes.Leave || inslist[r].OpCode == OpCodes.Leave_S)
                        {
                            awaitLeaves.Add(inslist[r]);
                            break;
                        }
                    }
                }
                else if (yieldIndex != -1 && mr.Name == "GetResult" && mr.DeclaringType.Resolve() == awaitableType)
                {
                    // resume after the store local
                    int resumeIndex = mr.ReturnType == mr.Module.TypeSystem.Void ? i + 1 : i + 2;

                    yields.Add(inslist[yieldIndex]);
                    resumes.Add(inslist[resumeIndex]);

                    yieldIndex = -1;
                }
            }
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

        protected static int SeekInstructionR(Collection<Ins> list, int start, Func<Ins, bool> check)
        {
            for (int i = start; i > -1; i--)
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

        protected static void RewriteReturns(
            MethodDefinition method,
            int startIndex,
            int endIndex,
            VariableDefinition returnValueHolder,
            Ins brTarget,
            bool skipLast)
        {
            var insc = method.Body.Instructions;

            // Find the jump target for leaves that are normal returns

            // Replace returns with a break or leave 
            for (int i = startIndex; i <= endIndex; i++)
            {
                Ins ins = insc[i];
                
                if (ins.OpCode != OpCodes.Ret)
                    continue;

                if (returnValueHolder != null)
                {
                    Ins insStoreReturnValue = Ins.Create(OpCodes.Stloc, returnValueHolder);

                    method.Body.ReplaceInstruction(i, insStoreReturnValue);

                    // Do not write a jump to the very next instruction
                    if (i == endIndex && skipLast)
                        break;

                    insc.Insert(++i, Ins.Create(OpCodes.Br, brTarget));
                    endIndex++;
                }
                else
                {
                    // Need to replace with Nop if last so branch targets aren't broken.
                    Ins newIns = i == endIndex && skipLast ? CreateNop() : Ins.Create(OpCodes.Br, brTarget);

                    method.Body.ReplaceInstruction(i, newIns);
                }
            }
        }

        protected static void RewriteSmLeaves(
            MethodDefinition method,
            int startIndex,
            int endIndex,
            Ins brTarget,
            bool skipLast)
        {
            // Assumes macros have been simplified
            MethodBody body = method.Body;

            Ins leaveTarget = body.ExceptionHandlers.Last().HandlerEnd;

            for (int i = startIndex; i <= endIndex; i++)
            {
                Ins ins = body.Instructions[i];

                if (!ReferenceEquals(ins.Operand, leaveTarget))
                    continue;
                Debug.Assert(ins.OpCode == OpCodes.Leave);

                // Need to replace with Nop if last so branch targets aren't broken.
                Ins newIns = i == endIndex && skipLast ? CreateNop() : Ins.Create(OpCodes.Br, brTarget);

                body.ReplaceInstruction(i, newIns);
            }
        }

        protected static void WriteSuccessHandler(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            FieldReference aspectField,
            VariableDefinition meaVar,
            FieldReference meaField,
            VariableDefinition resultVar,
            int offset)
        {
            MethodDefinition onSuccessDef = aspectType.GetInheritedMethods().First(m => m.Name == OnSuccessAdviceName);
            MethodReference onSuccess = mwc.SafeImport(onSuccessDef);

            var insc = new Collection<Ins>();
            
            insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));

            if (meaField != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
            }
            else if (meaVar != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }

            insc.Add(Ins.Create(OpCodes.Callvirt, onSuccess));

            method.Body.InsertInstructions(offset, insc);
        }

        protected static void WriteCatchExceptionHandler(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            TypeDefinition aspectType,
            FieldReference aspectField,
            VariableDefinition meaVar,
            FieldReference meaField,
            int tryStart,
            Ins leaveTarget)
        {
            var insc = new Collection<Ins>();

            MethodDefinition filterExceptionDef =
                aspectType.GetInheritedMethods().First(m => m.Name == FilterExceptionAdviceName);
            MethodReference filterExcetion = mwc.SafeImport(filterExceptionDef);
            MethodDefinition onExceptionDef =
                aspectType.GetInheritedMethods().First(m => m.Name == OnExceptionAdviceName);
            MethodReference onException = mwc.SafeImport(onExceptionDef);
            var exceptionType = mwc.SafeImport(mwc.Framework.Exception);

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            VariableDefinition exceptionHolder = targetMethod.Body.AddVariableDefinition(exceptionType);

            Ins labelFilterTrue = CreateNop();
            Ins labelFilterEnd = CreateNop();

            int ehTryCatchEnd = insc.Count;
            int ehFilterStart = insc.Count;

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
            if (meaField != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
            }
            else if (meaVar != null)
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

            int ehCatchStart = insc.Count;

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
            insc.Add(Ins.Create(OpCodes.Leave, leaveTarget));

            int ehCatchEnd = insc.Count;

            insc.Add(Ins.Create(OpCodes.Nop));

            targetMethod.Body.InsertInstructions(offset, insc);

            targetMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Filter)
            {
                TryStart = targetMethod.Body.Instructions[tryStart],
                TryEnd = insc[ehTryCatchEnd],
                FilterStart = insc[ehFilterStart],
                HandlerStart = insc[ehCatchStart],
                HandlerEnd = insc[ehCatchEnd],
                CatchType = exceptionType
            });
        }

        protected static void WriteFinallyExceptionHandler(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            TypeDefinition aspectType,
            FieldReference aspectField,
            VariableDefinition meaVar,
            FieldReference meaField,
            int tryStart,
            Ins leaveTarget)
        {
            MethodDefinition onExitDef = aspectType.GetInheritedMethods().First(m => m.Name == OnExitAdviceName);
            MethodReference onExit = mwc.SafeImport(onExitDef);

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            var insc = new Collection<Ins>();

            insc.Add(Ins.Create(OpCodes.Leave, leaveTarget));

            int ehTryFinallyEnd = insc.Count;

            // Begin finally block

            int ehFinallyStart = insc.Count;

            // Call OnExit()
            insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
            if (meaField != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
            }
            else if (meaVar != null)
                insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
            else
                insc.Add(Ins.Create(OpCodes.Ldnull));
            insc.Add(Ins.Create(OpCodes.Callvirt, onExit));
            insc.Add(Ins.Create(OpCodes.Endfinally));

            int ehFinallyEnd = insc.Count;
            insc.Add(Ins.Create(OpCodes.Nop));

            targetMethod.Body.InsertInstructions(offset, insc);

            var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = targetMethod.Body.Instructions[tryStart],
                TryEnd = insc[ehTryFinallyEnd],
                HandlerStart = insc[ehFinallyStart],
                HandlerEnd = insc[ehFinallyEnd]
            };

            targetMethod.Body.ExceptionHandlers.Add(finallyHandler);
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