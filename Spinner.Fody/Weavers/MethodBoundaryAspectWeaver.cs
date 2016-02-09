using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Aspects;
using Spinner.Fody.Utilities;
using Ins = Mono.Cecil.Cil.Instruction;

// ReSharper disable UnusedMember.Local -- Left in for future reference

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

            // Exception handlers will start before OnEntry().
            int tryStartIndex = insc.Count;

            // Write OnEntry call
            if (_aspectFeatures.Has(Features.OnEntry))
                WriteOnEntryCall(method, insc.Count, meaVar, null);

            // Re-add original body
            insc.AddRange(originalInsc);

            if (_aspectFeatures.Has(Features.OnSuccess | Features.OnException | Features.OnExit))
            {
                Ins labelSuccess = Ins.Create(OpCodes.Nop);

                // Need to rewrite returns if we're adding an exception handler or need a success block
                ReplaceReturnsWithBreaks(method,
                                         tryStartIndex,
                                         method.Body.Instructions.Count - 1,
                                         returnValueVar,
                                         labelSuccess);

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
            }
            
            // Need to leave the exception block
            Ins exceptionHandlerLeaveTarget = null;
            if (_aspectFeatures.Has(Features.OnException | Features.OnExit))
            {
                exceptionHandlerLeaveTarget = Ins.Create(OpCodes.Nop);
                insc.Add(Ins.Create(OpCodes.Leave, exceptionHandlerLeaveTarget));
            }

            // Write exception filter and handler
            
            if (_aspectFeatures.Has(Features.OnException))
            {
                WriteCatchExceptionHandler(method,
                                           null,
                                           insc.Count,
                                           meaVar,
                                           null,
                                           tryStartIndex);
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
            
            if (exceptionHandlerLeaveTarget != null)
                insc.Add(exceptionHandlerLeaveTarget);
                
            // Return the previously stored result
            if (returnValueVar != null)
                insc.Add(Ins.Create(OpCodes.Ldloc, returnValueVar));

            insc.Add(Ins.Create(OpCodes.Ret));
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
                WriteOnEntryCall(stateMachine, insc.Count - initEndOffset, null, meaField);

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

            if (_aspectFeatures.Has(Features.OnSuccess))
            {
                Ins labelSuccess = Ins.Create(OpCodes.Nop);

                // Rewrite all leaves that go to the SetResult() area, but not the ones that return after an await.
                // They will be replaced by breaks to labelSuccess.
                RewriteAsyncReturnsWithBreaks(stateMachine,
                                              tryStartOffset,
                                              insc.Count - leaveEndOffset - 1,
                                              labelSuccess);

                insc.Insert(insc.Count - leaveEndOffset, labelSuccess);

                WriteSuccessHandler(stateMachine,
                                    insc.Count - leaveEndOffset,
                                    null,
                                    meaField,
                                    _aspectFeatures.Has(Features.ReturnValue) ? resultVar : null);
            }

            // Leave the the exception handlers that will be written next.
            Ins exceptionHandlerLeaveTarget = null;
            if (_aspectFeatures.Has(Features.OnException | Features.OnExit))
            {
                exceptionHandlerLeaveTarget = Ins.Create(OpCodes.Nop);

                // Must fix offsets here since there could be an EH HandlerEnd that points to the current position.
                stateMachine.Body.InsertInstructions(insc.Count - leaveEndOffset,
                                                     true,
                                                     Ins.Create(OpCodes.Leave, exceptionHandlerLeaveTarget));
            }

            if (_aspectFeatures.Has(Features.OnException))
            {
                WriteCatchExceptionHandler(_method,
                                           _stateMachine,
                                           insc.Count - leaveEndOffset,
                                           null,
                                           meaField,
                                           tryStartOffset);
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

            if (exceptionHandlerLeaveTarget != null)
                insc.Insert(insc.Count - leaveEndOffset, exceptionHandlerLeaveTarget);

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
            for (int i = start; i < end; i++)
            {
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

            if (argumentsVarOpt == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, argumentsVarOpt);
            }

            il.Emit(OpCodes.Newobj, meaCtor);
            il.Emit(OpCodes.Stloc, meaVar);

            method.Body.InsertInstructions(offset, true, il.Instructions);
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

            var il = new ILProcessorEx();

            il.Emit(OpCodes.Ldarg_0); // for stfld

            if (method.IsStatic || thisField == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldfld, thisField);
                if (method.DeclaringType.IsValueType)
                {
                    //il.Emit(OpCodes.Ldobj, method.DeclaringType);
                    il.Emit(OpCodes.Box, method.DeclaringType);
                }
            }

            if (argumentsFieldOpt == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, argumentsFieldOpt);
            }

            il.Emit(OpCodes.Newobj, meaCtor);

            il.Emit(OpCodes.Stfld, meaField);

            stateMachine.Body.InsertInstructions(offset, true, il.Instructions);
        }

        /// <summary>
        /// Write the OnEntry advice call and FlowBehavior handler.
        /// </summary>
        private void WriteOnEntryCall(
            MethodDefinition method,
            int offset,
            VariableDefinition meaVarOpt,
            FieldReference meaFieldOpt)
        {
            MethodDefinition onEntryDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnEntry, true);
            MethodReference onEntry = _mwc.SafeImport(onEntryDef);
            //Features adviceFeatures = GetFeatures(onEntryDef);

            var il = new ILProcessorEx();

            // Invoke OnEntry with the MEA field, variable, or null.

            il.Emit(OpCodes.Ldsfld, _aspectField);
            il.EmitLoadOrNull(meaVarOpt, meaFieldOpt);
            il.Emit(OpCodes.Callvirt, onEntry);

            //// If this advice uses flow control, need to check for FlowBehavior.Return
            //if (adviceFeatures.Has(Features.FlowControl))
            //{
            //    Debug.Assert(meaVarOpt != null || meaFieldOpt != null);

            //    MethodDefinition getFlowBehaviorDef = _mwc.Spinner.MethodExecutionArgs_FlowBehavior.GetMethod;
            //    MethodReference getFlowBehavior = _mwc.SafeImport(getFlowBehaviorDef);

            //    Ins notReturningLabel = Ins.Create(OpCodes.Nop);

            //    if (meaFieldOpt != null)
            //        insc.AddRange(Ins.Create(OpCodes.Ldarg_0), Ins.Create(OpCodes.Ldfld, meaFieldOpt));
            //    else
            //        insc.Add(Ins.Create(OpCodes.Ldloc, meaVarOpt));
            //    insc.Add(Ins.Create(OpCodes.Callvirt, getFlowBehavior));
            //    insc.Add(Ins.Create(OpCodes.Ldc_I4, (int) FlowBehavior.Return));
            //    insc.Add(Ins.Create(OpCodes.Ceq));
            //    insc.Add(Ins.Create(OpCodes.Brfalse, notReturningLabel));

            //    // Store the ReturnValue property in the variable if necessary
            //    if (returnValueVarOpt != null)
            //    {
            //        MethodDefinition getReturnValueDef = _mwc.Spinner.MethodExecutionArgs_ReturnValue.GetMethod;
            //        MethodReference getReturnValue = _mwc.SafeImport(getReturnValueDef);
            //        TypeReference varType = _mwc.SafeImport(returnValueVarOpt.VariableType);

            //        if (meaFieldOpt != null)
            //            insc.AddRange(Ins.Create(OpCodes.Ldarg_0), Ins.Create(OpCodes.Ldfld, meaFieldOpt));
            //        else
            //            insc.Add(Ins.Create(OpCodes.Ldloc, meaVarOpt));
            //        insc.Add(Ins.Create(OpCodes.Callvirt, getReturnValue));
            //        if (varType.IsValueType)
            //            insc.Add(Ins.Create(OpCodes.Unbox_Any, varType));
            //        else if (!varType.IsSame(_method.Module.TypeSystem.Object))
            //            insc.Add(Ins.Create(OpCodes.Castclass, varType));
            //        insc.Add(Ins.Create(OpCodes.Stloc, returnValueVarOpt));
            //    }

            //    // Copy ref and out arguments from container to method. These are not allowed on state machines.
            //    if (!isStateMachine)
            //        WriteCopyArgumentsFromContainer(method, offset, argsVarOpt, false, true);

            //    breakTarget = Ins.Create(OpCodes.Nop);
            //    insc.Add(Ins.Create(OpCodes.Br, breakTarget));

            //    insc.Add(notReturningLabel);
            //}
            
            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        /// <summary>
        /// Replaces return instructions with breaks. If the method is not void, stores the result of the return
        /// expression in returnVar first.
        /// </summary>
        private static void ReplaceReturnsWithBreaks(
            MethodDefinition method,
            int startIndex,
            int endIndex,
            VariableDefinition returnVar,
            Ins breakTarget)
        {
            Collection<Ins> insc = method.Body.Instructions;

            for (int i = startIndex; i <= endIndex; i++)
            {
                Ins ins = insc[i];
                
                if (ins.OpCode != OpCodes.Ret)
                    continue;

                if (returnVar != null)
                {
                    method.Body.ReplaceInstruction(i, Ins.Create(OpCodes.Stloc, returnVar));
                    
                    insc.Insert(++i, Ins.Create(OpCodes.Br, breakTarget));
                    endIndex++;
                }
                else
                {
                    // Need to replace with Nop if last so branch targets aren't broken.
                    method.Body.ReplaceInstruction(i, Ins.Create(OpCodes.Br, breakTarget));
                }
            }
        }

        /// <summary>
        /// Rewrites return leaves in an async state machine body into breaks.
        /// </summary>
        private static void RewriteAsyncReturnsWithBreaks(
            MethodDefinition method,
            int startIndex,
            int endIndex,
            Ins breakTarget)
        {
            MethodBody body = method.Body;

            // This is the target for leave instructions that will go on to set the task result.
            Ins leaveTarget = body.ExceptionHandlers.Last().HandlerEnd;

            for (int i = startIndex; i <= endIndex; i++)
            {
                Ins ins = body.Instructions[i];

                if (!ReferenceEquals(ins.Operand, leaveTarget))
                    continue;
                Debug.Assert(ins.OpCode == OpCodes.Leave, "instructions must have been simplified already");

                body.ReplaceInstruction(i, Ins.Create(OpCodes.Br, breakTarget));
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
            MethodReference onSuccess =
                _mwc.SafeImport(_aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnSuccess, true));

            // For state machines, the return var type would need to be imported
            TypeReference returnVarType = null;
            if (returnVar != null)
                returnVarType = _mwc.SafeImport(returnVar.VariableType);

            var il = new ILProcessorEx();

            // Set ReturnValue to returnVar

            if (returnVar != null && (meaVar != null || meaField != null))
            {
                MethodReference setReturnValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_ReturnValue.SetMethod);

                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Ldloc, returnVar);
                if (returnVarType.IsValueType)
                    il.Emit(OpCodes.Box, returnVarType);
                il.Emit(OpCodes.Callvirt, setReturnValue);
            }

            // Call OnSuccess()

            il.Emit(OpCodes.Ldsfld, _aspectField);
            il.EmitLoadOrNull(meaVar, meaField);
            il.Emit(OpCodes.Callvirt, onSuccess);

            // Set resultVar to ReturnValue

            if (returnVar != null && (meaVar != null || meaField != null))
            {
                MethodReference getReturnValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_ReturnValue.GetMethod);

                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Callvirt, getReturnValue);
                if (returnVarType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, returnVarType);
                il.Emit(OpCodes.Stloc, returnVar);
            }

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        private void WriteCatchExceptionHandler(
            MethodDefinition method,
            MethodDefinition stateMachineOpt,
            int offset,
            VariableDefinition meaVar,
            FieldReference meaField,
            int tryStart)
        {
            MethodDefinition filterExceptionDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_FilterException, true);
            MethodReference filterExcetion = _mwc.SafeImport(filterExceptionDef);
            MethodDefinition onExceptionDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnException, true);
            MethodReference onException = _mwc.SafeImport(onExceptionDef);
            TypeReference exceptionType = _mwc.SafeImport(_mwc.Framework.Exception);

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            VariableDefinition exceptionHolder = targetMethod.Body.AddVariableDefinition(exceptionType);

            var il = new ILProcessorEx();

            Ins labelFilterTrue = il.CreateNop();
            Ins labelFilterEnd = il.CreateNop();

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
            il.Emit(OpCodes.Ldsfld, _aspectField);
            il.EmitLoadOrNull(meaVar, meaField);
            il.Emit(OpCodes.Ldloc, exceptionHolder);
            il.Emit(OpCodes.Callvirt, filterExcetion);

            // Compare FilterException result with 0 to get the endfilter argument
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt_Un);
            il.Append(labelFilterEnd);
            il.Emit(OpCodes.Endfilter);

            int ehCatchStart = il.Instructions.Count;

            il.Emit(OpCodes.Pop); // Exception already stored

            // Call OnException()
            if (meaVar != null || meaField != null)
            {
                //Features onExceptionFeatures = GetFeatures(onExceptionDef);
                //MethodReference getException = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_Exception.GetMethod);
                MethodReference setException = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_Exception.SetMethod);

                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Ldloc, exceptionHolder);
                il.Emit(OpCodes.Callvirt, setException);

                il.Emit(OpCodes.Ldsfld, _aspectField);
                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Callvirt, onException);

                //if (onExceptionFeatures.Has(Features.FlowControl))
                //{
                //    MethodReference getFlowBehavior = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_FlowBehavior.GetMethod);

                //    returnTarget = il.CreateNop();

                //    Ins continueCase = il.CreateNop();
                //    Ins rethrowCase = il.CreateNop();
                //    Ins returnCase = il.CreateNop();

                //    il.EmitLoadVarOrField(meaVar, meaField);
                //    il.Emit(OpCodes.Callvirt, getFlowBehavior);

                //    il.Emit(OpCodes.Switch, new[] {rethrowCase, continueCase, rethrowCase, returnCase});
                //    il.Emit(OpCodes.Br, rethrowCase); // fallthrough

                //    il.Append(continueCase);
                //    il.Emit(OpCodes.Leave, continueTarget); // TEMP!!!

                //    il.Append(rethrowCase);
                //    il.Emit(OpCodes.Rethrow);

                //    il.Append(returnCase);

                //    // Store the ReturnValue property in the variable if necessary
                //    if (returnValueVarOpt != null)
                //    {
                //        MethodDefinition getReturnValueDef = _mwc.Spinner.MethodExecutionArgs_ReturnValue.GetMethod;
                //        MethodReference getReturnValue = _mwc.SafeImport(getReturnValueDef);
                //        TypeReference varType = _mwc.SafeImport(returnValueVarOpt.VariableType);

                //        il.EmitLoadVarOrField(meaVar, meaField);
                //        il.Emit(OpCodes.Callvirt, getReturnValue);
                //        if (varType.IsValueType)
                //            il.Emit(OpCodes.Unbox_Any, varType);
                //        else if (!varType.IsSame(_method.Module.TypeSystem.Object))
                //            il.Emit(OpCodes.Castclass, varType);
                //        il.Emit(OpCodes.Stloc, returnValueVarOpt);
                //    }

                //    il.Emit(OpCodes.Leave, returnTarget);
                //}
                //else
                //{
                //    il.Emit(OpCodes.Rethrow);
                //}

                //Ins labelCaught = il.CreateNop();

                //// If the Exception property was set to null, return normally, otherwise rethrow
                //// Changeing the exception object will not throw the new value, instead the OnException() advice
                //// should throw.
                //il.EmitLoadAdviceArgs(meaVar, meaField);
                //il.Emit(OpCodes.Callvirt, getException);
                //il.Emit(OpCodes.Dup);
                //il.Emit(OpCodes.Brfalse, labelCaught);

                //il.Emit(OpCodes.Rethrow);

                //il.Append(labelCaught);
                //il.Emit(OpCodes.Pop);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, _aspectField);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, onException);
            }
            il.Emit(OpCodes.Rethrow);

            int ehCatchEnd = il.Instructions.Count;
            il.Emit(OpCodes.Nop); // so ehCatchEnd has an instruction

            // Do not fix offsets since exception handlers are special.
            targetMethod.Body.InsertInstructions(offset, false, il.Instructions);

            var eh = new ExceptionHandler(ExceptionHandlerType.Filter)
            {
                TryStart = targetMethod.Body.Instructions[tryStart],
                TryEnd = il.Instructions[0],
                FilterStart = il.Instructions[0],
                HandlerStart = il.Instructions[ehCatchStart],
                HandlerEnd = il.Instructions[ehCatchEnd],
                CatchType = exceptionType
            };

            targetMethod.Body.ExceptionHandlers.Add(eh);
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

            var il = new ILProcessorEx();

            // Call OnExit()
            il.Emit(OpCodes.Ldsfld, _aspectField);
            il.EmitLoadOrNull(meaVar, meaField);
            il.Emit(OpCodes.Callvirt, onExit);
            il.Emit(OpCodes.Endfinally);

            int ehFinallyEnd = il.Instructions.Count;
            il.Emit(OpCodes.Nop);

            // Do not fix offsets since exception handlers are special.
            targetMethod.Body.InsertInstructions(offset, false, il.Instructions);

            var eh = new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = targetMethod.Body.Instructions[tryStart],
                TryEnd = il.Instructions[0],
                HandlerStart = il.Instructions[0],
                HandlerEnd = il.Instructions[ehFinallyEnd]
            };

            targetMethod.Body.ExceptionHandlers.Add(eh);
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

            var il = new ILProcessorEx();
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

                il.Emit(OpCodes.Dup);
                il.EmitBoxIfValueType(awaitableType);
                il.Emit(OpCodes.Stloc, awaitableVar);

                offset += stateMachine.Body.InsertInstructions(getAwaiterOffset, true, il.Instructions);
                il.Instructions.Clear();

                // Store the awaitable, currently an object or boxed value type, as YieldValue

                if (meaField != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, meaField);
                    il.Emit(OpCodes.Ldloc, awaitableVar);
                    il.Emit(OpCodes.Callvirt, setYieldValue);
                }

                // Invoke OnYield()

                il.Emit(OpCodes.Ldsfld, _aspectField);
                il.EmitLoadOrNull(null, meaField);
                il.Emit(OpCodes.Callvirt, onYield);

                //// Set YieldValue to null so we don't keep the object alive. altering the YieldValue is not permitted

                //if (meaField != null)
                //{
                //    il.Emit(OpCodes.Ldarg_0);
                //    il.Emit(OpCodes.Ldfld, meaField);
                //    il.Emit(OpCodes.Ldnull);
                //    il.Emit(OpCodes.Callvirt, setYieldValue);
                //}

                offset += stateMachine.Body.InsertInstructions(yieldOffset + offset, true, il.Instructions);
                il.Instructions.Clear();
            }

            if (resumeOffset != -1)
            {
                MethodDefinition onResumeDef = _aspectType.GetMethod(_mwc.Spinner.IMethodBoundaryAspect_OnResume, true);
                MethodReference onResume = _mwc.SafeImport(onResumeDef);

                // Store the typed awaitable as YieldValue, optionally boxing it.

                if (meaField != null && resultVar != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, meaField);
                    il.Emit(OpCodes.Ldloc, resultVar);
                    il.EmitBoxIfValueType(resultVar.VariableType);
                    il.Emit(OpCodes.Callvirt, setYieldValue);
                }

                il.Emit(OpCodes.Ldsfld, _aspectField);
                il.EmitLoadOrNull(null, meaField);
                il.Emit(OpCodes.Callvirt, onResume);

                // Unbox the YieldValue and store it back in the result. Changing it is permitted here.

                if (meaField != null && resultVar != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, meaField);
                    il.Emit(OpCodes.Callvirt, getYieldValue);
                    il.EmitCastOrUnbox(resultVar.VariableType);
                    il.Emit(OpCodes.Stloc, resultVar);

                    //il.Emit(OpCodes.Ldarg_0);
                    //il.Emit(OpCodes.Ldfld, meaField);
                    //il.Emit(OpCodes.Ldnull);
                    //il.Emit(OpCodes.Callvirt, setYieldValue);
                }

                stateMachine.Body.InsertInstructions(resumeOffset + offset, true, il.Instructions);
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
                    if (atype.IsSame(asmType))
                    {
                        var type = (TypeDefinition) a.ConstructorArguments.First().Value;
                        stateMachine = type.Methods.Single(m => m.Name == "MoveNext");
                        return StateMachineKind.Async;
                    }
                    if (atype.IsSame(ismType))
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
        
        private static bool IsInTryBlock(MethodDefinition method, int index)
        {
            Ins ins = method.Body.Instructions[index];

            method.Body.UpdateOffsets();

            foreach (ExceptionHandler eh in method.Body.ExceptionHandlers)
            {
                if (ins.Offset >= eh.TryStart.Offset && ins.Offset < eh.TryEnd.Offset)
                    return true;
            }

            return false;
        }
    }
}
