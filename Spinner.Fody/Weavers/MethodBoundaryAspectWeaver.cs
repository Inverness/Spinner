using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Aspects;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    internal enum StateMachineKind
    {
        None,
        Iterator,
        Async
    }

    internal sealed class MethodBoundaryAspectWeaver : AspectWeaver
    {
        private readonly MethodDefinition _method;
        private MethodDefinition _stateMachine;
        private TypeDefinition _effectiveReturnType;

        private MethodBoundaryAspectWeaver(
            ModuleWeavingContext mwc,
            TypeDefinition aspectType,
            int aspectIndex,
            MethodDefinition aspectTarget)
            : base(mwc, aspectType, aspectIndex, aspectTarget)
        {
            _method = aspectTarget;
        }

        internal static void Weave(ModuleWeavingContext mwc, MethodDefinition method, TypeDefinition aspect, int index)
        {
            new MethodBoundaryAspectWeaver(mwc, aspect, index, method).Weave();
        }

        protected override void Weave()
        {
            CreateAspectCacheField();

            // State machines are very different and have their own weaving methods.
            StateMachineKind stateMachineKind = GetStateMachineInfo(_method, out _stateMachine);

            HashSet<Ins> existingNops;
            switch (stateMachineKind)
            {
                case StateMachineKind.None:
                    _effectiveReturnType = _method.ReturnType == _mwc.Module.TypeSystem.Void
                        ? null
                        : _method.ReturnType.Resolve();

                    _method.Body.SimplifyMacros();
                    // Preserve existing Nops in a debug build. These are used for optimal breakpoint placement.
                    existingNops = new HashSet<Ins>(_method.Body.Instructions.Where(i => i.OpCode == OpCodes.Nop));

                    WeaveMethod();

                    _method.Body.RemoveNops(existingNops);
                    _method.Body.OptimizeMacros();
                    break;

                case StateMachineKind.Iterator:
                    throw new NotSupportedException();

                case StateMachineKind.Async:
                    // void for Task and T for Task<T>
                    _effectiveReturnType = _method.ReturnType.IsGenericInstance
                        ? ((GenericInstanceType) _method.ReturnType).GenericArguments.Single().Resolve()
                        : null;

                    _stateMachine.Body.SimplifyMacros();
                    existingNops = new HashSet<Ins>(_stateMachine.Body.Instructions.Where(i => i.OpCode == OpCodes.Nop));

                    WeaveAsyncMethod();

                    _stateMachine.Body.RemoveNops(existingNops);
                    _stateMachine.Body.OptimizeMacros();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void WeaveMethod()
        {
            MethodDefinition method = _method;
            Collection<Ins> insc = method.Body.Instructions;
            var originalInsc = new Collection<Ins>(insc);
            insc.Clear();

            VariableDefinition returnValueVar = null;
            if (_effectiveReturnType != null)
                returnValueVar = _method.Body.AddVariableDefinition(_effectiveReturnType);

            WriteAspectInit(_method, insc.Count);

            VariableDefinition argumentsVar = null;
            if (_aspectFeatures.Has(Features.GetArguments))
            {
                WriteArgumentContainerInit(method, insc.Count, out argumentsVar);
                WriteCopyArgumentsToContainer(method, insc.Count, argumentsVar, true);
            }

            VariableDefinition meaVar;
            WriteMeaInit(method, argumentsVar, insc.Count, out meaVar);

            if (_aspectFeatures.Has(Features.MemberInfo))
                WriteSetMethodInfo(method, null, insc.Count, meaVar, null);

            // Write OnEntry call
            if (_aspectFeatures.Has(Features.OnEntry))
                WriteOnEntryCall(method, insc.Count, meaVar);

            // Re-add original body
            int tryStartIndex = insc.Count;
            insc.AddRange(originalInsc);

            if (_aspectFeatures.Has(Features.OnException | Features.OnExit | Features.OnSuccess))
            {
                Ins labelNewReturn = Ins.Create(OpCodes.Nop);
                Ins labelSuccess = Ins.Create(OpCodes.Nop);

                RewriteReturns(method,
                               tryStartIndex,
                               method.Body.Instructions.Count - 1,
                               returnValueVar,
                               labelSuccess,
                               _aspectFeatures.Has(Features.OnSuccess));

                insc.Add(labelSuccess);

                // Write success block

                if (_aspectFeatures.Has(Features.OnSuccess))
                {
                    WriteSuccessHandler(method,
                                        insc.Count,
                                        meaVar,
                                        null,
                                        _aspectFeatures.Has(Features.ReturnValue) ? returnValueVar : null);
                }

                if (_aspectFeatures.Has(Features.OnException | Features.OnExit))
                    insc.Add(Ins.Create(OpCodes.Leave, labelNewReturn));

                // Write exception filter and handler

                if (_aspectFeatures.Has(Features.OnException))
                {
                    WriteCatchExceptionHandler(method,
                                               null,
                                               insc.Count,
                                               meaVar,
                                               null,
                                               tryStartIndex,
                                               labelNewReturn);
                }

                // End of try block for the finally handler

                if (_aspectFeatures.Has(Features.OnExit))
                {
                    WriteFinallyExceptionHandler(method,
                                                 null,
                                                 insc.Count,
                                                 meaVar,
                                                 null,
                                                 tryStartIndex);
                }

                // End finally block

                insc.Add(labelNewReturn);
                
                // Return the previously stored result
                if (returnValueVar != null)
                    insc.Add(Ins.Create(OpCodes.Ldloc, returnValueVar));
                insc.Add(Ins.Create(OpCodes.Ret));
            }
        }

        private void WeaveAsyncMethod()
        {
            MethodDefinition method = _method;
            MethodDefinition stateMachine = _stateMachine;

            // The last exception handler is used for setting the returning task state.
            // This is needed to identify insertion points.
            ExceptionHandler taskExceptionHandler = stateMachine.Body.ExceptionHandlers.Last();
            
            Collection<Ins> insc = stateMachine.Body.Instructions;

            // Find the start of the body, which will be the first instruction after the first set of branch
            // instructions
            int tryStartIndex = insc.IndexOf(taskExceptionHandler.TryStart);
            int firstBranch = Seek(insc, tryStartIndex, IsBranching);
            int bodyBegin = Seek(insc, firstBranch, i => !IsBranching(i));

            // Find the instruction to leave the body and set the task result. This will be the instruction right
            // before the TryEnd.
            int tryEndIndex = insc.IndexOf(taskExceptionHandler.TryEnd);
            int bodyLeave = tryEndIndex - 1;
            Debug.Assert(insc[bodyLeave].OpCode == OpCodes.Leave);

            // Start the try block in the same place as the state machine. This prevents the need to mess with
            // existing breaks
            int tryStartOffset = insc.IndexOf(taskExceptionHandler.TryStart);

            // The offset from the end to where initialization code will go.
            int initEndOffset = insc.Count - bodyBegin;

            // Offset from the end to the return leave instruction
            int leaveEndOffset = insc.Count - bodyLeave;

            // Find the variable used to set the task result. No need to create our own.
            VariableDefinition resultVar = null;
            if (_effectiveReturnType != null)
            {
                int resultStore = SeekR(insc, insc.Count - leaveEndOffset, i => i.OpCode == OpCodes.Stloc);
                resultVar = (VariableDefinition) insc[resultStore].Operand;
            }

            WriteAspectInit(stateMachine, insc.Count - initEndOffset);

            FieldDefinition arguments = null;
            if (_aspectFeatures.Has(Features.GetArguments))
            {
                WriteSmArgumentContainerInit(method, stateMachine, insc.Count - initEndOffset, out arguments);
                WriteSmCopyArgumentsToContainer(method, stateMachine, insc.Count - initEndOffset, arguments, true);
            }

            // The meaVar would be used temporarily after loading from the field.
            FieldDefinition meaField;
            WriteSmMeaInit(method, stateMachine, arguments, insc.Count - initEndOffset, out meaField);

            if (_aspectFeatures.Has(Features.OnEntry))
            {
                // Write OnEntry call
                WriteSmOnEntryCall(stateMachine,
                                   insc.Count - initEndOffset,
                                   meaField);
            }

            // Search through the body for places to insert OnYield() and OnResume() calls
            if (_aspectFeatures.Has(Features.OnYield | Features.OnResume))
            {
                int awaitSearchStart = insc.Count - initEndOffset;
                VariableDefinition awaitableStorage = null;

                while (awaitSearchStart < insc.Count - leaveEndOffset)
                {
                    int awaitable;
                    int callYield;
                    int callResume;
                    VariableDefinition awaitResultVar;
                    bool found = GetAwaitInfo(stateMachine,
                                              awaitSearchStart,
                                              insc.Count - leaveEndOffset,
                                              out awaitable,
                                              out callYield,
                                              out callResume,
                                              out awaitResultVar);

                    if (!found)
                        break;

                    awaitSearchStart = callResume + 1;

                    if (awaitableStorage == null)
                        awaitableStorage = stateMachine.Body.AddVariableDefinition(stateMachine.Module.TypeSystem.Object);

                    WriteYieldAndResume(stateMachine,
                                        awaitable,
                                        callYield,
                                        callResume,
                                        meaField,
                                        awaitableStorage,
                                        awaitResultVar);
                }
            }

            // Everything following this point is written after the body but BEFORE the async exception handler's
            // leave instruction.

            Ins labelLeaveTarget = null;
            if (_aspectFeatures.Has(Features.OnSuccess))
            {
                Ins labelSuccess = Ins.Create(OpCodes.Nop);

                // Rewrite all leaves that go to the SetResult() area, but not the ones that return after an await.
                // They will be replaced by breaks to labelSuccess.
                RewriteSmLeaves(stateMachine,
                                tryStartOffset,
                                insc.Count - leaveEndOffset - 1,
                                labelSuccess,
                                false);

                insc.Insert(insc.Count - leaveEndOffset, labelSuccess);

                WriteSuccessHandler(stateMachine,
                                    insc.Count - leaveEndOffset,
                                    null,
                                    meaField,
                                    _aspectFeatures.Has(Features.ReturnValue) ? resultVar : null);

                // Leave the the exception handlers that will be written next.
                if (_aspectFeatures.Has(Features.OnException | Features.OnExit))
                {
                    labelLeaveTarget = Ins.Create(OpCodes.Nop);
                    insc.Insert(insc.Count - leaveEndOffset, Ins.Create(OpCodes.Leave, labelLeaveTarget));
                }
            }

            if (_aspectFeatures.Has(Features.OnException))
            {
                WriteCatchExceptionHandler(_method,
                                           _stateMachine,
                                           insc.Count - leaveEndOffset,
                                           null,
                                           meaField,
                                           tryStartOffset,
                                           insc[insc.Count - leaveEndOffset]);
            }

            if (_aspectFeatures.Has(Features.OnExit))
            {
                WriteFinallyExceptionHandler(_method,
                                             _stateMachine,
                                             insc.Count - leaveEndOffset,
                                             null,
                                             meaField,
                                             tryStartOffset);
            }
                
            if (labelLeaveTarget != null)
                insc.Insert(insc.Count - leaveEndOffset, labelLeaveTarget);

            if (_aspectFeatures.Has(Features.OnException | Features.OnExit))
            {
                // Ensure the task EH is last
                stateMachine.Body.ExceptionHandlers.Remove(taskExceptionHandler);
                stateMachine.Body.ExceptionHandlers.Add(taskExceptionHandler);
            }
        }

        private static void WeaveIteratorMethod()
        {
        }

        private static bool GetAwaitInfo(
            MethodDefinition stateMachine,
            int start,
            int end,
            out int awaitable,
            out int callYield,
            out int callResume,
            out VariableDefinition resultVar)
        {
            Collection<Ins> insc = stateMachine.Body.Instructions;

            awaitable = -1;
            callYield = -1;
            callResume = -1;
            resultVar = null;

            // Identify yield and resume points by looking for calls to a get_IsCompleted property and a GetResult
            // method. These can be defined on any type due to how awaitables work. IsCompleted is required to be
            // a property, not a field.
            TypeDefinition awaiterType = null;
            int count = 0;
            for (int i = start; i < end; i++)
            {
                count++;
                Ins ins = insc[i];

                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                    continue;

                var mr = (MethodReference) ins.Operand;

                if (awaitable == -1 && mr.Name == "GetAwaiter")
                {
                    awaitable = i;
                }
                else if (awaitable != -1 && callYield == -1 && mr.Name == "get_IsCompleted")
                {
                    // Yield points will be after the branch instruction that acts on the IsCompleted result.
                    // This way OnYield() is only called if an await is actually necessary.

                    Debug.Assert(IsBranching(insc[i + 1]) && !IsBranching(insc[i + 2]));

                    callYield = i + 2;

                    awaiterType = mr.DeclaringType.Resolve();
                }
                else if (callYield != -1 && mr.Name == "GetResult" && mr.DeclaringType.Resolve() == awaiterType)
                {
                    if (mr.ReturnType == mr.Module.TypeSystem.Void)
                    {
                        callResume = i + 1;
                    }
                    else
                    {
                        // Release builds initialize the awaiter before storing the GetResult() result, so the
                        // stloc can not be assumed to be the next instruction.
                        int n;
                        for (n = i + 1; n < end; n++)
                        {
                            if (insc[n].OpCode == OpCodes.Stloc)
                            {
                                resultVar = (VariableDefinition) insc[n].Operand;
                                break;
                            }
                        }

                        callResume = n + 1;
                    }
                    return true;
                }
            }

            return false;
        }

        ///// <summary>
        ///// Resolves a generic parameter type or return type for a method.
        ///// </summary>
        //private static TypeReference ResolveMethodGenericParameter(
        //    TypeReference parameter,
        //    MethodReference method)
        //{
        //    if (!parameter.IsGenericParameter)
        //        return parameter;

        //    var gp = (GenericParameter) parameter;

        //    if (gp.Type == GenericParameterType.Type)
        //    {
        //        Debug.Assert(method.DeclaringType.IsGenericInstance, "method declaring type is not a generic instance");
        //        return ((GenericInstanceType) method.DeclaringType).GenericArguments[gp.Position];
        //    }
        //    else
        //    {
        //        Debug.Assert(method.IsGenericInstance, "method is not a generic instance");
        //        return ((GenericInstanceMethod) method).GenericArguments[gp.Position];
        //    }
        //}

        /// <summary>
        /// Write MethodExecutionArgs initialization.
        /// </summary>
        private void WriteMeaInit(
            MethodDefinition method,
            VariableDefinition argumentsVarOpt,
            int offset,
            out VariableDefinition meaVar)
        {
            TypeReference meaType = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_ctor);

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

            if (argumentsVarOpt == null)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, argumentsVarOpt));
            }

            insc.Add(Ins.Create(OpCodes.Newobj, meaCtor));
            insc.Add(Ins.Create(OpCodes.Stloc, meaVar));

            method.Body.InsertInstructions(offset, insc);
        }

        /// <summary>
        /// Write MethodExecutionArgs initialization for a state machine.
        /// </summary>
        private void WriteSmMeaInit(
            MethodDefinition method,
            MethodDefinition stateMachine,
            FieldReference argumentsFieldOpt,
            int offset,
            out FieldDefinition meaField)
        {
            TypeReference meaType = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_ctor);

            string fieldName = NameGenerator.MakeAdviceArgsFieldName(_aspectIndex);
            meaField = new FieldDefinition(fieldName, FieldAttributes.Private, meaType);
            stateMachine.DeclaringType.Fields.Add(meaField);

            // Field can be missing on release builds if its not used by the state machine.
            FieldReference thisField = stateMachine.DeclaringType.Fields.FirstOrDefault(f => f.Name == StateMachineThisFieldName);

            var insc = new Collection<Ins>();

            insc.Add(Ins.Create(OpCodes.Ldarg_0)); // for stfld

            if (method.IsStatic || thisField == null)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Dup));
                insc.Add(Ins.Create(OpCodes.Ldfld, thisField));
                if (method.DeclaringType.IsValueType)
                {
                    //insc.Add(Ins.Create(OpCodes.Ldobj, method.DeclaringType));
                    insc.Add(Ins.Create(OpCodes.Box, method.DeclaringType));
                }
            }

            if (argumentsFieldOpt == null)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, argumentsFieldOpt));
            }

            insc.Add(Ins.Create(OpCodes.Newobj, meaCtor));

            insc.Add(Ins.Create(OpCodes.Stfld, meaField));

            stateMachine.Body.InsertInstructions(offset, insc);
        }

        /// <summary>
        /// Write the OnEntry advice call.
        /// </summary>
        private void WriteOnEntryCall(MethodDefinition method, int offset, VariableDefinition meaVarOpt)
        {
            MethodDefinition onEntryDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnEntry, true);
            MethodReference onEntry = _mwc.SafeImport(onEntryDef);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldsfld, _aspectField),
                meaVarOpt == null
                    ? Ins.Create(OpCodes.Ldnull)
                    : Ins.Create(OpCodes.Ldloc, meaVarOpt),
                Ins.Create(OpCodes.Callvirt, onEntry)
            };
            
            method.Body.InsertInstructions(offset, insc);
        }

        private void WriteSmOnEntryCall(
            MethodDefinition stateMachine,
            int offset,
            FieldReference meaFieldOpt)
        {
            MethodDefinition onEntryDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnEntry, true);
            MethodReference onEntry = _mwc.SafeImport(onEntryDef);

            var insc = new Collection<Ins>();

            insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
            if (meaFieldOpt == null)
            {
                insc.Add(Ins.Create(OpCodes.Ldnull));
            }
            else
            {
                insc.Add(Ins.Create(OpCodes.Ldarg_0));
                insc.Add(Ins.Create(OpCodes.Ldfld, meaFieldOpt));
            }
            insc.Add(Ins.Create(OpCodes.Callvirt, onEntry));

            stateMachine.Body.InsertInstructions(offset, insc);
        }

        private static void RewriteReturns(
            MethodDefinition method,
            int startIndex,
            int endIndex,
            VariableDefinition returnVar,
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

                if (returnVar != null)
                {
                    Ins insStoreReturnValue = Ins.Create(OpCodes.Stloc, returnVar);

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
                    Ins newIns = i == endIndex && skipLast ? Ins.Create(OpCodes.Nop) : Ins.Create(OpCodes.Br, brTarget);

                    method.Body.ReplaceInstruction(i, newIns);
                }
            }
        }

        private static void RewriteSmLeaves(
            MethodDefinition method,
            int startIndex,
            int endIndex,
            Ins brTarget,
            bool skipLast)
        {
            // Assumes macros have been simplified
            MethodBody body = method.Body;

            // This is the target for leave instructions that will go on to set the task result.
            Ins leaveTarget = body.ExceptionHandlers.Last().HandlerEnd;

            for (int i = startIndex; i <= endIndex; i++)
            {
                Ins ins = body.Instructions[i];

                if (!ReferenceEquals(ins.Operand, leaveTarget))
                    continue;
                Debug.Assert(ins.OpCode == OpCodes.Leave);

                // Need to replace with Nop if last so branch targets aren't broken.
                Ins newIns = i == endIndex && skipLast ? Ins.Create(OpCodes.Nop) : Ins.Create(OpCodes.Br, brTarget);

                body.ReplaceInstruction(i, newIns);
            }
        }

        /// <summary>
        /// Writes the OnSuccess() call and ReturnValue get and set for it.
        /// </summary>
        private void WriteSuccessHandler(
            MethodDefinition method,
            int offset,
            VariableDefinition meaVar,
            FieldReference meaField,
            VariableDefinition returnVar)
        {
            MethodReference onSuccess = _mwc.SafeImport(_aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnSuccess, true));

            // For state machines, the return var type would need to be imported
            TypeReference returnVarType = null;
            if (returnVar != null)
                returnVarType = _mwc.SafeImport(returnVar.VariableType);

            var insc = new Collection<Ins>();

            // Set ReturnValue to returnVar

            if (returnVar != null && (meaVar != null || meaField != null))
            {
                MethodReference setReturnValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_ReturnValue.SetMethod);

                if (meaField != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                }
                else
                {
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                }
                insc.Add(Ins.Create(OpCodes.Ldloc, returnVar));
                if (returnVarType.IsValueType)
                    insc.Add(Ins.Create(OpCodes.Box, returnVarType));
                insc.Add(Ins.Create(OpCodes.Callvirt, setReturnValue));
            }

            // Call OnSuccess()

            insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
            
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

            // Set resultVar to ReturnValue

            if (returnVar != null && (meaVar != null || meaField != null))
            {
                MethodReference getReturnValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_ReturnValue.GetMethod);

                if (meaField != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                }
                else
                {
                    insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                }
                insc.Add(Ins.Create(OpCodes.Callvirt, getReturnValue));
                if (returnVarType.IsValueType)
                    insc.Add(Ins.Create(OpCodes.Unbox_Any, returnVarType));
                insc.Add(Ins.Create(OpCodes.Stloc, returnVar));
            }

            method.Body.InsertInstructions(offset, insc);
        }

        private void WriteCatchExceptionHandler(
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            VariableDefinition meaVar,
            FieldReference meaField,
            int tryStart,
            Ins leaveTarget)
        {
            var insc = new Collection<Ins>();

            MethodDefinition filterExceptionDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_FilterException, true);
            MethodReference filterExcetion = _mwc.SafeImport(filterExceptionDef);
            MethodDefinition onExceptionDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnException, true);
            MethodReference onException = _mwc.SafeImport(onExceptionDef);
            TypeReference exceptionType = _mwc.SafeImport(_mwc.Framework.Exception);

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            VariableDefinition exceptionHolder = targetMethod.Body.AddVariableDefinition(exceptionType);

            Ins labelFilterTrue = Ins.Create(OpCodes.Nop);
            Ins labelFilterEnd = Ins.Create(OpCodes.Nop);

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
            insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
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
                MethodReference getException = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_Exception.GetMethod);
                MethodReference setException = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_Exception.SetMethod);

                insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                insc.Add(Ins.Create(OpCodes.Ldloc, exceptionHolder));
                insc.Add(Ins.Create(OpCodes.Callvirt, setException));

                insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
                insc.Add(Ins.Create(OpCodes.Ldloc, meaVar));
                insc.Add(Ins.Create(OpCodes.Callvirt, onException));

                Ins labelCaught = Ins.Create(OpCodes.Nop);

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
                insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
                insc.Add(Ins.Create(OpCodes.Ldnull));
                insc.Add(Ins.Create(OpCodes.Callvirt, onException));
            }
            insc.Add(Ins.Create(OpCodes.Leave, leaveTarget));

            int ehCatchEnd = insc.Count;

            insc.Add(Ins.Create(OpCodes.Nop));

            targetMethod.Body.Instructions.InsertRange(offset, insc);

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

        private void WriteFinallyExceptionHandler(
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            VariableDefinition meaVar,
            FieldReference meaField,
            int tryStart)
        {
            MethodDefinition onExitDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnExit, true);
            MethodReference onExit = _mwc.SafeImport(onExitDef);

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            var insc = new Collection<Ins>();

            int ehTryFinallyEnd = insc.Count;
            int ehFinallyStart = insc.Count;

            // Call OnExit()
            insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
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

            targetMethod.Body.Instructions.InsertRange(offset, insc);

            var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = targetMethod.Body.Instructions[tryStart],
                TryEnd = insc[ehTryFinallyEnd],
                HandlerStart = insc[ehFinallyStart],
                HandlerEnd = insc[ehFinallyEnd]
            };

            targetMethod.Body.ExceptionHandlers.Add(finallyHandler);
        }

        private void WriteYieldAndResume(
            MethodDefinition stateMachine,
            int getAwaiterOffset,
            int yieldOffset,
            int resumeOffset,
            FieldReference meaField,
            VariableDefinition awaitableVar,
            VariableDefinition resultVar
            )
        {
            MethodReference getYieldValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_YieldValue.GetMethod);
            MethodReference setYieldValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_YieldValue.SetMethod);

            var insc = new Collection<Ins>();
            int offset = 0;

            if (yieldOffset != -1)
            {
                MethodDefinition onYieldDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnYield, true);
                MethodReference onYield = _mwc.SafeImport(onYieldDef);

                // Need to know whether the awaitable is a value type. It will be boxed as object if so, instead
                // of trying to create a local variable for each type found.
                TypeReference awaitableType = GetExpressionType(stateMachine.Body, getAwaiterOffset - 1);
                if (awaitableType == null)
                    throw new InvalidOperationException("unable to determine expression type");

                insc.Add(Ins.Create(OpCodes.Dup));
                if (awaitableType.IsValueType)
                    insc.Add(Ins.Create(OpCodes.Box, awaitableType));
                insc.Add(Ins.Create(OpCodes.Stloc, awaitableVar));

                offset += stateMachine.Body.InsertInstructions(getAwaiterOffset, insc);
                insc.Clear();

                // Store the awaitable, currently an object or boxed value type, as YieldValue

                if (meaField != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                    insc.Add(Ins.Create(OpCodes.Ldloc, awaitableVar));
                    insc.Add(Ins.Create(OpCodes.Callvirt, setYieldValue));
                }

                // Invoke OnYield()

                insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
                if (meaField != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                }
                else
                {
                    insc.Add(Ins.Create(OpCodes.Ldnull));
                }
                insc.Add(Ins.Create(OpCodes.Callvirt, onYield));

                //// Set YieldValue to null so we don't keep the object alive. altering the YieldValue is not permitted

                //if (meaField != null)
                //{
                //    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                //    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                //    insc.Add(Ins.Create(OpCodes.Ldnull));
                //    insc.Add(Ins.Create(OpCodes.Callvirt, setYieldValue));
                //}

                offset += stateMachine.Body.InsertInstructions(yieldOffset + offset, insc);
                insc.Clear();
            }

            if (resumeOffset != -1)
            {
                MethodDefinition onResumeDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnResume, true);
                MethodReference onResume = _mwc.SafeImport(onResumeDef);

                // Store the typed awaitable as YieldValue, optionally boxing it.

                if (meaField != null && resultVar != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                    insc.Add(Ins.Create(OpCodes.Ldloc, resultVar));
                    if (resultVar.VariableType.IsValueType)
                        insc.Add(Ins.Create(OpCodes.Box, resultVar.VariableType));
                    insc.Add(Ins.Create(OpCodes.Callvirt, setYieldValue));
                }

                insc.Add(Ins.Create(OpCodes.Ldsfld, _aspectField));
                if (meaField != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                }
                else
                {
                    insc.Add(Ins.Create(OpCodes.Ldnull));
                }
                insc.Add(Ins.Create(OpCodes.Callvirt, onResume));

                // Unbox the YieldValue and store it back in the result. Changing it is permitted here.

                if (meaField != null && resultVar != null)
                {
                    insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                    insc.Add(Ins.Create(OpCodes.Callvirt, getYieldValue));
                    if (resultVar.VariableType.IsValueType)
                        insc.Add(Ins.Create(OpCodes.Unbox_Any, resultVar.VariableType));
                    else
                        insc.Add(Ins.Create(OpCodes.Castclass, resultVar.VariableType));
                    insc.Add(Ins.Create(OpCodes.Stloc, resultVar));

                    //insc.Add(Ins.Create(OpCodes.Ldarg_0));
                    //insc.Add(Ins.Create(OpCodes.Ldfld, meaField));
                    //insc.Add(Ins.Create(OpCodes.Ldnull));
                    //insc.Add(Ins.Create(OpCodes.Callvirt, setYieldValue));
                }

                stateMachine.Body.InsertInstructions(resumeOffset + offset, insc);
            }
        }

        private static TypeReference GetExpressionType(MethodBody body, int index)
        {
            TypeSystem typeSystem = body.Method.Module.TypeSystem;
            Ins ins = body.Instructions[index];

            while (ins != null)
            {
                Debug.Assert(ins.OpCode.OpCodeType != OpCodeType.Macro, "body must be simplified");

                if (ins.OpCode == OpCodes.Dup)
                {
                    ins = body.Instructions[index - 1];
                    continue;
                }

                switch (ins.OpCode.OperandType)
                {
                    case OperandType.InlineField:
                        return ((FieldReference) ins.Operand).FieldType;
                    case OperandType.InlineI:
                        return typeSystem.Int32;
                    case OperandType.InlineI8:
                        return typeSystem.Int64;
                    case OperandType.InlineMethod:
                        return ((MethodReference) ins.Operand).ReturnType;
                    case OperandType.InlineR:
                        return ins.OpCode == OpCodes.Ldc_R4 ? typeSystem.Single : typeSystem.Double;
                    case OperandType.InlineString:
                        return typeSystem.String;
                    case OperandType.InlineType:
                        return (TypeReference) ins.Operand;
                    case OperandType.InlineVar:
                        return ((VariableDefinition) ins.Operand).VariableType;
                    case OperandType.InlineArg:
                        return ((ParameterDefinition) ins.Operand).ParameterType.GetElementType();
                }

                break;
            }

            return null;
        }

        /// <summary>
        /// Discover whether a method is an async state machine creator and the nested type that implements it.
        /// </summary>
        private StateMachineKind GetStateMachineInfo(MethodDefinition method, out MethodDefinition stateMachine)
        {
            if (method.HasCustomAttributes)
            {
                TypeDefinition asmType = _mwc.Framework.AsyncStateMachineAttribute;
                TypeDefinition ismType = _mwc.Framework.IteratorStateMachineAttribute;

                foreach (CustomAttribute a in method.CustomAttributes)
                {
                    TypeReference atype = a.AttributeType;
                    if (atype.IsSimilar(asmType) && atype.Resolve() == asmType)
                    {
                        var type = (TypeDefinition) a.ConstructorArguments.First().Value;
                        stateMachine = type.Methods.Single(m => m.Name == "MoveNext");
                        return StateMachineKind.Async;
                    }
                    if (atype.IsSimilar(ismType) && atype.Resolve() == ismType)
                    {
                        var type = (TypeDefinition) a.ConstructorArguments.First().Value;
                        stateMachine = type.Methods.Single(m => m.Name == "MoveNext");
                        return StateMachineKind.Iterator;
                    }
                }
            }

            stateMachine = null;
            return StateMachineKind.None;
        }

        private static bool IsBranching(Ins ins)
        {
            OperandType ot = ins.OpCode.OperandType;

            return ot == OperandType.InlineSwitch || ot == OperandType.InlineBrTarget || ot == OperandType.ShortInlineBrTarget;
        }

        private static bool IsCall(Ins ins)
        {
            OpCode op = ins.OpCode;
            return op == OpCodes.Call || op == OpCodes.Callvirt;
        }

        private static int Seek(Collection<Ins> list, int start, Func<Ins, bool> check)
        {
            for (int i = start; i < list.Count; i++)
            {
                if (check(list[i]))
                    return i;
            }
            return -1;
        }

        private static int SeekR(Collection<Ins> list, int start, Func<Ins, bool> check)
        {
            for (int i = start; i > -1; i--)
            {
                if (check(list[i]))
                    return i;
            }
            return -1;
        }
    }
}
