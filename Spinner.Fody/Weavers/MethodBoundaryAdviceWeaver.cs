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
    internal sealed class MethodBoundaryAdviceWeaver : AdviceWeaver
    {
        //private readonly IReadOnlyList<AdviceInfo> _advices;
        private readonly AdviceInfo _entryAdvice;
        private readonly AdviceInfo _exitAdvice;
        private readonly AdviceInfo _successAdvice;
        private readonly AdviceInfo _exceptionAdvice;
        private readonly AdviceInfo _filterExceptionAdvice;
        private readonly AdviceInfo _yieldAdvice;
        private readonly AdviceInfo _resumeAdvice;
        private readonly MethodDefinition _method;
        private MethodDefinition _stateMachine;
        private TypeReference _effectiveReturnType;
        private readonly bool _applyToStateMachine;

        internal MethodBoundaryAdviceWeaver(
            AspectWeaver parent,
            MethodBoundaryAdviceGroup adviceGroup,
            MethodDefinition method)
            : base(parent, method)
        {
            //Debug.Assert(advices.All(a => a.Aspect == aspect), "advices must be for the same aspect");

            //_advices = advices;
            _entryAdvice = adviceGroup.Entry;
            _exitAdvice = adviceGroup.Exit;
            _successAdvice = adviceGroup.Success;
            _exceptionAdvice = adviceGroup.Exception;
            _filterExceptionAdvice = adviceGroup.FilterException;
            _yieldAdvice = adviceGroup.Yield;
            _resumeAdvice = adviceGroup.Resume;
            _method = method;
            _applyToStateMachine =
                parent.Aspect.Source.Attribute.GetNamedArgumentValue(
                    nameof(MethodBoundaryAspect.AttributeApplyToStateMachine)) as bool? ?? true;
        }

        public override void Weave()
        {
            Parent.CreateAspectCacheField();

            // State machines are very different and have their own weaving methods.
            StateMachineKind stateMachineKind = _applyToStateMachine
                                                    ? GetStateMachineInfo(_method, out _stateMachine)
                                                    : StateMachineKind.None;

            HashSet<Ins> existingNops;
            switch (stateMachineKind)
            {
                case StateMachineKind.None:
                    _effectiveReturnType = !_method.IsReturnVoid() ? Context.SafeImport(_method.ReturnType) : null;

                    _method.Body.SimplifyMacros();
                    // Preserve existing Nops in a debug build. These are used for optimal breakpoint placement.
                    existingNops = new HashSet<Ins>(_method.Body.Instructions.Where(i => i.OpCode == OpCodes.Nop));

                    WeaveMethod();

                    _method.Body.RemoveNops(existingNops);
                    _method.Body.OptimizeMacros();
                    break;

                case StateMachineKind.Iterator:
                    _effectiveReturnType = _method.ReturnType.IsGenericInstance
                        ? Context.SafeImport(((GenericInstanceType) _method.ReturnType).GenericArguments.Single())
                        : _method.Module.TypeSystem.Object;

                    _stateMachine.Body.SimplifyMacros();
                    existingNops = new HashSet<Ins>(_stateMachine.Body.Instructions.Where(i => i.OpCode == OpCodes.Nop));

                    WeaveIteratorMethod();

                    _stateMachine.Body.RemoveNops(existingNops);
                    _stateMachine.Body.OptimizeMacros();
                    _stateMachine.Body.UpdateOffsets();
                    break;

                case StateMachineKind.Async:
                    // void for Task and T for Task<T>
                    _effectiveReturnType = _method.ReturnType.IsGenericInstance
                        ? Context.SafeImport(((GenericInstanceType) _method.ReturnType).GenericArguments.Single())
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
            bool needNewReturn = false;
            
            // Will be reinserted later. This is a bit more efficient than doing repeated insertions at the beginning.
            var originalInsc = new Collection<Ins>(insc);
            insc.Clear();

            WriteAspectInit(_method, insc.Count);

            VariableDefinition argumentsVar = null;
            if (Aspect.Features.Has(Features.GetArguments))
            {
                WriteArgumentContainerInit(method, insc.Count, out argumentsVar);
                WriteCopyArgumentsToContainer(method, insc.Count, argumentsVar, true);
            }

            VariableDefinition meaVar;
            WriteMeaInit(method, argumentsVar, insc.Count, out meaVar);

            if (Aspect.Features.Has(Features.MemberInfo))
                WriteSetMethodInfo(method, null, insc.Count, meaVar, null);

            // Exception handlers will start before OnEntry().
            int tryStartOffset = insc.Count;

            // Write OnEntry call
            if (_entryAdvice != null)
                WriteOnEntryCall(method, insc.Count, meaVar, null);

            // Re-add original body
            insc.AddRange(originalInsc);

            // Supporting OnSuccess, OnException, or OnExit requires rewriting the body of the method to redirect
            // return statements so that they:
            // 1. Save the return expression result to a local
            // 2. Break to a new block of code that calls OnSuccess() if necessary
            // 3. Leave the exception handler to code that returns the value stored in the previously mentioned local

            VariableDefinition returnValueVar = null;
            if (_successAdvice != null || _exceptionAdvice != null || _exitAdvice != null)
            {
                needNewReturn = true;

                if (_effectiveReturnType != null)
                    returnValueVar = _method.Body.AddVariableDefinition(_effectiveReturnType);

                Ins successLabel = Ins.Create(OpCodes.Nop);
                
                ReplaceReturnsWithBreaks(method,
                                         tryStartOffset,
                                         method.Body.Instructions.Count - 1,
                                         returnValueVar,
                                         successLabel);

                insc.Add(successLabel);

                // Write success block

                if (_successAdvice != null)
                {
                    WriteSuccessHandler(method,
                                        insc.Count,
                                        meaVar,
                                        null,
                                        Aspect.Features.Has(Features.ReturnValue) ? returnValueVar : null);
                }
            }
            
            // Need to leave the exception block
            Ins exceptionHandlerLeaveTarget = null;
            if (_exceptionAdvice != null || _exitAdvice != null)
            {
                exceptionHandlerLeaveTarget = Ins.Create(OpCodes.Nop);
                insc.Add(Ins.Create(OpCodes.Leave, exceptionHandlerLeaveTarget));
            }

            // Write exception filter and handler
            
            if (_exceptionAdvice != null)
            {
                WriteCatchExceptionHandler(method,
                                           null,
                                           insc.Count,
                                           meaVar,
                                           null,
                                           tryStartOffset);
            }

            // End of try block for the finally handler

            if (_exitAdvice != null)
            {
                WriteFinallyExceptionHandler(method,
                                             null,
                                             insc.Count,
                                             meaVar,
                                             null,
                                             tryStartOffset,
                                             null);
            }

            // End finally block
            
            if (exceptionHandlerLeaveTarget != null)
                insc.Add(exceptionHandlerLeaveTarget);

            if (needNewReturn)
            {
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
            Collection<Ins> insc = stateMachine.Body.Instructions;

            // All async state machines have an outer exception handler that is used to set the task's state if an
            // exception occurrs inside of it. This means that the method body is already set up to break to a leave
            // instruction at the end of the try block rather than returning directly.
            // All generated code will be placed inside of this task exception handler.

            ExceptionHandler taskExceptionHandler = stateMachine.Body.ExceptionHandlers.Last();
            
            // Get the offset to where init code will go, which is before the logical body. This can be found by 
            // searching for the first non-branching instruction after the first set of branching instructions.
            int offTryStart = insc.IndexOf(taskExceptionHandler.TryStart);
            int offFirstBranch = Seek(insc, offTryStart, IsBranching);
            int offBodyBegin = Seek(insc, offFirstBranch, i => !IsBranching(i));
            int eoffInit = insc.Count - offBodyBegin;

            // Find the instruction to leave the body and set the task result. This will be the last instruction
            // in the try block.
            int offTryEnd = insc.IndexOf(taskExceptionHandler.TryEnd);
            int offBodyLeave = offTryEnd - 1;
            Debug.Assert(insc[offBodyLeave].OpCode == OpCodes.Leave);
            int eoffLeave = insc.Count - offBodyLeave;

            // Find the variable used to set the task result. No need to create our own.
            // This should be the first stloc before the leave.
            VariableDefinition resultVar = null;
            if (_effectiveReturnType != null)
            {
                int resultStore = SeekR(insc, insc.Count - eoffLeave, i => i.OpCode == OpCodes.Stloc);
                resultVar = (VariableDefinition) insc[resultStore].Operand;
            }
            
            // Write initialization code and OnEntry() call

            WriteAspectInit(stateMachine, insc.Count - eoffInit);

            FieldDefinition arguments = null;
            if (Aspect.Features.Has(Features.GetArguments))
            {
                WriteSmArgumentContainerInit(method, stateMachine, insc.Count - eoffInit, out arguments);
                WriteSmCopyArgumentsToContainer(method, stateMachine, insc.Count - eoffInit, arguments, true);
            }
            
            FieldDefinition meaField;
            WriteSmMeaInit(method, stateMachine, arguments, insc.Count - eoffInit, out meaField);

            if (Aspect.Features.Has(Features.MemberInfo))
                WriteSetMethodInfo(method, stateMachine, insc.Count - eoffInit, null, meaField);
            
            if (_entryAdvice != null)
                WriteOnEntryCall(stateMachine, insc.Count - eoffInit, null, meaField);

            // Write OnYield() and OnResume() calls. This is done by searching the body for the offsets of await
            // method calls like GetAwaiter(), get_IsCompleted(), and GetResult(), and then writing instructions
            // and those offsets before searching the next part of the body.
            // TODO: Fix OnEntry() and OnYield() will be called in opposite orders with multiple aspects

            if (_yieldAdvice != null || _resumeAdvice != null)
            {
                int offSearchStart = insc.Count - eoffInit;
                //VariableDefinition awaitableStorage = null;

                while (offSearchStart < insc.Count - eoffLeave)
                {
                    int eoffAwaitable;
                    int eoffYield;
                    int eoffResume;
                    VariableDefinition awaitResultVarOpt;

                    bool found = GetAwaitOffsets(stateMachine,
                                                 offSearchStart,
                                                 insc.Count - eoffLeave,
                                                 out eoffAwaitable,
                                                 out eoffYield,
                                                 out eoffResume,
                                                 out awaitResultVarOpt);

                    if (!found)
                        break;

                    offSearchStart = insc.Count - eoffResume + 1;

                    //// This variable is used to capture the awaitable object from the stack so YieldValue can be set
                    //// before the await
                    //if (awaitableStorage == null)
                    //    awaitableStorage = stateMachine.Body.AddVariableDefinition(stateMachine.Module.TypeSystem.Object);

                    WriteYieldAndResume(stateMachine,
                                        eoffAwaitable,
                                        eoffYield,
                                        eoffResume,
                                        meaField,
                                        null,
                                        awaitResultVarOpt);
                }
            }

            // Everything following this point is written after the body but BEFORE the async exception handler's
            // leave instruction.

            if (_successAdvice != null)
            {
                // If a MethodBoundaryAspect has already been applied to this state machine then
                // we might need to use a leave instead of a break if control must cross an exception handler
                bool isCrossEh = HasDifferentExceptionHandlers(stateMachine.Body,
                                                               insc.Count - eoffLeave - 1,
                                                               insc.Count - eoffLeave);

                Ins labelSuccess = Ins.Create(OpCodes.Nop);

                // Rewrite all leaves that go to the SetResult() area, but not the ones that return after an await.
                // They will be replaced by breaks to labelSuccess.
                RewriteAsyncReturnsWithBreaks(stateMachine,
                                              offTryStart,
                                              insc.Count - eoffLeave - 1,
                                              labelSuccess,
                                              isCrossEh ? OpCodes.Leave : OpCodes.Br);

                insc.Insert(insc.Count - eoffLeave, labelSuccess);

                WriteSuccessHandler(stateMachine,
                                    insc.Count - eoffLeave,
                                    null,
                                    meaField,
                                    Aspect.Features.Has(Features.ReturnValue) ? resultVar : null);
            }

            // Leave the the exception handlers that will be written next.
            Ins exceptionHandlerLeaveTarget = null;
            if (_exceptionAdvice != null || _exitAdvice != null)
            {
                exceptionHandlerLeaveTarget = Ins.Create(OpCodes.Nop);

                // Must fix offsets here since there could be an EH HandlerEnd that points to the current position.
                stateMachine.Body.InsertInstructions(insc.Count - eoffLeave,
                                                     true,
                                                     Ins.Create(OpCodes.Leave, exceptionHandlerLeaveTarget));
            }

            if (_exceptionAdvice != null)
            {
                WriteCatchExceptionHandler(_method,
                                           _stateMachine,
                                           insc.Count - eoffLeave,
                                           null,
                                           meaField,
                                           offTryStart);
            }

            if (_exitAdvice != null)
            {
                WriteFinallyExceptionHandler(_method,
                                             _stateMachine,
                                             insc.Count - eoffLeave,
                                             null,
                                             meaField,
                                             offTryStart,
                                             null);
            }

            if (exceptionHandlerLeaveTarget != null)
                insc.Insert(insc.Count - eoffLeave, exceptionHandlerLeaveTarget);

            if (_exceptionAdvice != null || _exitAdvice != null)
            {
                // Ensure the task EH is last
                stateMachine.Body.ExceptionHandlers.Remove(taskExceptionHandler);
                stateMachine.Body.ExceptionHandlers.Add(taskExceptionHandler);
            }
        }

        private void WeaveIteratorMethod()
        {
            MethodDefinition method = _method;
            MethodDefinition stateMachine = _stateMachine;
            Collection<Ins> insc = stateMachine.Body.Instructions;
            
            // Discover where the body begins by examining branching instructions

            int tryStartOffset = 0;
            int firstBranchOffset = Seek(insc, tryStartOffset, IsBranching);
            Ins firstBranch = insc[firstBranchOffset];

            int bodyBeginOffset;
            if (firstBranch.OpCode == OpCodes.Switch)
            {
                Ins nextBranch = ((Ins[]) firstBranch.Operand)[0];
                Debug.Assert(IsBranching(nextBranch));
                bodyBeginOffset = insc.IndexOf((Ins) nextBranch.Operand);
            }
            else
            {
                Ins nextBranch = (Ins) firstBranch.Operand;
                Debug.Assert(IsBranching(nextBranch));
                bodyBeginOffset = insc.IndexOf((Ins) nextBranch.Operand);
            }

            int initEndOffset = insc.Count - bodyBeginOffset;

            // Begin writing init code

            WriteAspectInit(stateMachine, insc.Count - initEndOffset);

            FieldDefinition arguments = null;
            if (Aspect.Features.Has(Features.GetArguments))
            {
                WriteSmArgumentContainerInit(method, stateMachine, insc.Count - initEndOffset, out arguments);
                WriteSmCopyArgumentsToContainer(method, stateMachine, insc.Count - initEndOffset, arguments, true);
            }

            // The meaVar would be used temporarily after loading from the field.
            FieldDefinition meaField;
            WriteSmMeaInit(method, stateMachine, arguments, insc.Count - initEndOffset, out meaField);

            if (Aspect.Features.Has(Features.MemberInfo))
                WriteSetMethodInfo(method, stateMachine, insc.Count - initEndOffset, null, meaField);
            
            if (_entryAdvice != null)
                WriteOnEntryCall(stateMachine, insc.Count - initEndOffset, null, meaField);

            // TODO: Yield and Resume

            // Due to the way iterators work, calls to OnSuccess() and OnExit() must be made by checking the boolean
            // that is returned for MoveNext(). When false, OnSuccess() and OnExit() are called.
            // If an exception occurs, OnException() will be called but OnExit() will not. This is because iterators
            // can have exceptions occur but that doesn't stop them from being resumed.

            VariableDefinition hasNextVar = null;
            bool needsNewReturn = false;
            if (_successAdvice != null || _exceptionAdvice != null || _exitAdvice != null)
            {
                hasNextVar = _stateMachine.Body.AddVariableDefinition(_stateMachine.Module.TypeSystem.Boolean);
                needsNewReturn = true;

                Ins labelSuccess = Ins.Create(OpCodes.Nop);

                // Need to rewrite returns if we're adding an exception handler or need a success block
                ReplaceReturnsWithBreaks(stateMachine,
                                         tryStartOffset,
                                         insc.Count - 1,
                                         hasNextVar,
                                         labelSuccess);

                insc.Add(labelSuccess);

                // Write success block

                if (_successAdvice != null)
                {
                    // TODO: Insert this into branches that set false instead of checking here
                    Ins notFinishedLabel = Ins.Create(OpCodes.Nop);

                    var il = new ILProcessorEx(insc);
                    il.Emit(OpCodes.Ldloc, hasNextVar);
                    il.Emit(OpCodes.Brtrue, notFinishedLabel);

                    WriteSuccessHandler(stateMachine, insc.Count, null, meaField, null);

                    il.Append(notFinishedLabel);
                }
            }

            // Need to leave the exception block
            Ins exceptionHandlerLeaveTarget = null;
            if (_exceptionAdvice != null || _exitAdvice != null)
            {
                exceptionHandlerLeaveTarget = Ins.Create(OpCodes.Nop);
                insc.Add(Ins.Create(OpCodes.Leave, exceptionHandlerLeaveTarget));
            }

            // Write exception filter and handler

            if (_exceptionAdvice != null)
            {
                WriteCatchExceptionHandler(method,
                                           stateMachine,
                                           insc.Count,
                                           null,
                                           meaField,
                                           tryStartOffset);
            }

            // End of try block for the finally handler

            if (_exitAdvice != null)
            {
                WriteFinallyExceptionHandler(method,
                                             stateMachine,
                                             insc.Count,
                                             null,
                                             meaField,
                                             tryStartOffset,
                                             hasNextVar);
            }

            // End finally block

            if (exceptionHandlerLeaveTarget != null)
                insc.Add(exceptionHandlerLeaveTarget);

            if (needsNewReturn)
            {
                // Return the previously stored result
                if (hasNextVar != null)
                    insc.Add(Ins.Create(OpCodes.Ldloc, hasNextVar));

                insc.Add(Ins.Create(OpCodes.Ret));
            }
        }

        /// <summary>
        /// Searches a range of instructions to get the offsets for awaitable capture, OnYield() call, and OnResume() call
        /// </summary>
        private static bool GetAwaitOffsets(
            MethodDefinition stateMachine,
            int start,
            int end,
            out int eoffAwaitable,
            out int eoffYield,
            out int eoffResume,
            out VariableDefinition resultVarOpt)
        {
            Collection<Ins> insc = stateMachine.Body.Instructions;

            eoffAwaitable = -1;
            eoffYield = -1;
            eoffResume = -1;
            resultVarOpt = null;

            // Identify yield and resume points by looking for calls to a get_IsCompleted property and a GetResult
            // method. These can be defined on any type due to how awaitables work. IsCompleted is required to be
            // a property, not a field.
            TypeReference awaiterType = null;
            for (int i = start; i < end; i++)
            {
                OpCode opcode = insc[i].OpCode;

                if (opcode != OpCodes.Call && opcode != OpCodes.Callvirt)
                    continue;

                var mr = (MethodReference) insc[i].Operand;

                if (eoffAwaitable == -1 && mr.Name == "GetAwaiter")
                {
                    // The awaitable will be on the stack just before this call
                    eoffAwaitable = insc.Count - i;
                }
                else if (eoffAwaitable != -1 && eoffYield == -1 && mr.Name == "get_IsCompleted")
                {
                    // Yield points will be after the branch instruction that acts on the IsCompleted result.
                    // This way OnYield() is only called if an await is actually necessary.

                    Debug.Assert(IsBranching(insc[i + 1]) && !IsBranching(insc[i + 2]));

                    eoffYield = insc.Count - i + 2;

                    awaiterType = mr.DeclaringType;
                }
                else if (eoffYield != -1 && mr.Name == "GetResult" && mr.DeclaringType.IsSame(awaiterType))
                {
                    if (mr.IsReturnVoid())
                    {
                        // Resume after GetResult() is called, which will be a void method for Task
                        eoffResume = insc.Count - i + 1;
                    }
                    else
                    {
                        // Release builds initialize the awaiter before storing the GetResult() result, so the
                        // stloc can not be assumed to be the very next instruction.
                        int n = Seek(insc, i + 1, x => x.OpCode == OpCodes.Stloc);
                        Debug.Assert(n < end);

                        resultVarOpt = (VariableDefinition) insc[n].Operand;

                        eoffResume = insc.Count - n + 1;
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
            TypeReference meaType = Context.SafeImport(Context.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = Context.SafeImport(Context.Spinner.MethodExecutionArgs_ctor);

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
            TypeReference meaType = Context.SafeImport(Context.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = Context.SafeImport(Context.Spinner.MethodExecutionArgs_ctor);

            string fieldName = NameGenerator.MakeAdviceArgsFieldName(Aspect.Index);
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
            MethodReference onEntry = _entryAdvice.ImportSourceMethod();
            //Features adviceFeatures = GetFeatures(onEntryDef);

            var il = new ILProcessorEx();

            // Invoke OnEntry with the MEA field, variable, or null.

            il.Emit(OpCodes.Ldsfld, Parent.AspectField);
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
            Ins breakTarget,
            OpCode breakOpCode)
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

                body.ReplaceInstruction(i, Ins.Create(breakOpCode, breakTarget));
            }
        }

        /// <summary>
        /// Writes the OnSuccess() call and ReturnValue get and set for it.
        /// </summary>
        private void WriteSuccessHandler(
            MethodDefinition method,
            int offset,
            VariableDefinition meaVarOpt,
            FieldReference meaFieldOpt,
            VariableDefinition returnVarOpt)
        {
            MethodReference onSuccess = _successAdvice.ImportSourceMethod();

            var il = new ILProcessorEx();

            // Set ReturnValue to returnVar

            if (returnVarOpt != null && (meaVarOpt != null || meaFieldOpt != null))
            {
                MethodReference setReturnValue = Context.SafeImport(Context.Spinner.MethodExecutionArgs_ReturnValue.SetMethod);

                il.EmitLoadOrNull(meaVarOpt, meaFieldOpt);
                il.Emit(OpCodes.Ldloc, returnVarOpt);
                il.EmitBoxIfValueType(returnVarOpt.VariableType);
                il.Emit(OpCodes.Callvirt, setReturnValue);
            }

            // Call OnSuccess()

            il.Emit(OpCodes.Ldsfld, Parent.AspectField);
            il.EmitLoadOrNull(meaVarOpt, meaFieldOpt);
            il.Emit(OpCodes.Callvirt, onSuccess);

            // Set resultVar to ReturnValue

            if (returnVarOpt != null && (meaVarOpt != null || meaFieldOpt != null))
            {
                MethodReference getReturnValue = Context.SafeImport(Context.Spinner.MethodExecutionArgs_ReturnValue.GetMethod);

                il.EmitLoadOrNull(meaVarOpt, meaFieldOpt);
                il.Emit(OpCodes.Callvirt, getReturnValue);
                il.EmitCastOrUnbox(returnVarOpt.VariableType);
                il.Emit(OpCodes.Stloc, returnVarOpt);
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
            bool withFilter = _filterExceptionAdvice != null;

            MethodReference onFilterException = null;
            if (withFilter)
                onFilterException = _filterExceptionAdvice.ImportSourceMethod();

            MethodReference onException = _exceptionAdvice.ImportSourceMethod();
            TypeReference exceptionType = Context.SafeImport(Context.Framework.Exception);

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            VariableDefinition exceptionHolder = targetMethod.Body.AddVariableDefinition(exceptionType);

            var il = new ILProcessorEx();

            int ehCatchStart;
            if (withFilter)
            {
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
                il.Emit(OpCodes.Ldsfld, Parent.AspectField);
                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Ldloc, exceptionHolder);
                il.Emit(OpCodes.Callvirt, onFilterException);

                // Compare FilterException result with 0 to get the endfilter argument
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Cgt_Un);
                il.Append(labelFilterEnd);
                il.Emit(OpCodes.Endfilter);

                ehCatchStart = il.Instructions.Count;

                il.Emit(OpCodes.Pop); // Exception already stored
            }
            else
            {
                ehCatchStart = il.Instructions.Count;

                il.Emit(OpCodes.Stloc, exceptionHolder);
            }

            // Call OnException()
            if (meaVar != null || meaField != null)
            {
                MethodReference setException = Context.SafeImport(Context.Spinner.MethodExecutionArgs_Exception.SetMethod);

                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Ldloc, exceptionHolder);
                il.Emit(OpCodes.Callvirt, setException);

                il.Emit(OpCodes.Ldsfld, Parent.AspectField);
                il.EmitLoadOrNull(meaVar, meaField);
                il.Emit(OpCodes.Callvirt, onException);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, Parent.AspectField);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, onException);
            }

            il.Emit(OpCodes.Rethrow);

            int ehCatchEnd = il.Instructions.Count;
            il.Emit(OpCodes.Nop); // so ehCatchEnd has an instruction

            // Do not fix offsets since exception handlers are special.
            targetMethod.Body.InsertInstructions(offset, false, il.Instructions);

            var eh = new ExceptionHandler(withFilter ? ExceptionHandlerType.Filter : ExceptionHandlerType.Catch)
            {
                TryStart = targetMethod.Body.Instructions[tryStart],
                TryEnd = il.Instructions[0],
                FilterStart = withFilter ? il.Instructions[0] : null,
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
            int tryStart,
            VariableDefinition hasNextVarOpt)
        {
            MethodReference onExit = _exitAdvice.ImportSourceMethod();

            MethodDefinition targetMethod = stateMachineOpt ?? method;

            var il = new ILProcessorEx();

            // For iterators, OnExit() can only be called if the iterator is done
            Ins hasNextTrueLabel = null;
            if (hasNextVarOpt != null)
            {
                hasNextTrueLabel = Ins.Create(OpCodes.Nop);

                il.Emit(OpCodes.Ldloc, hasNextVarOpt);
                il.Emit(OpCodes.Brtrue, hasNextTrueLabel);
            }

            // Call OnExit()
            il.Emit(OpCodes.Ldsfld, Parent.AspectField);
            il.EmitLoadOrNull(meaVar, meaField);
            il.Emit(OpCodes.Callvirt, onExit);

            if (hasNextVarOpt != null)
                il.Append(hasNextTrueLabel);

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
            int eoffAwaitable,
            int eoffYield,
            int eoffResume,
            FieldReference meaField,
            VariableDefinition awaitableVar,
            VariableDefinition resultVarOpt
            )
        {
            //MethodReference getYieldValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_YieldValue.GetMethod);
            //MethodReference setYieldValue = _mwc.SafeImport(_mwc.Spinner.MethodExecutionArgs_YieldValue.SetMethod);

            Collection<Ins> insc = stateMachine.Body.Instructions;

            var il = new ILProcessorEx();

            if (eoffYield != -1 && _yieldAdvice != null)
            {
                MethodReference onYield = _yieldAdvice.ImportSourceMethod();

                //// Need to know whether the awaitable is a value type. It will be boxed as object if so, instead
                //// of trying to create a local variable for each type found.
                //TypeReference awaitableType = GetExpressionType(stateMachine.Body, insc.Count - eoffAwaitable - 1);
                //if (awaitableType == null)
                //    throw new InvalidOperationException("unable to determine expression type");

                //il.Emit(OpCodes.Dup);
                //il.EmitBoxIfValueType(awaitableType);
                //il.Emit(OpCodes.Stloc, awaitableVar);

                //stateMachine.Body.InsertInstructions(insc.Count - eoffAwaitable, true, il.Instructions);
                //il.Instructions.Clear();

                //// Store the awaitable, currently an object or boxed value type, as YieldValue

                //if (meaField != null)
                //{
                //    il.Emit(OpCodes.Ldarg_0);
                //    il.Emit(OpCodes.Ldfld, meaField);
                //    il.Emit(OpCodes.Ldloc, awaitableVar);
                //    il.Emit(OpCodes.Callvirt, setYieldValue);
                //}

                // Invoke OnYield()

                il.Emit(OpCodes.Ldsfld, Parent.AspectField);
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

                stateMachine.Body.InsertInstructions(insc.Count - eoffYield, true, il.Instructions);
                il.Instructions.Clear();
            }

            if (eoffResume != -1 && _resumeAdvice != null)
            {
                MethodReference onResume = _resumeAdvice.ImportSourceMethod();

                //// Store the typed awaitable as YieldValue, optionally boxing it.

                //if (meaField != null && resultVarOpt != null)
                //{
                //    il.Emit(OpCodes.Ldarg_0);
                //    il.Emit(OpCodes.Ldfld, meaField);
                //    il.Emit(OpCodes.Ldloc, resultVarOpt);
                //    il.EmitBoxIfValueType(resultVarOpt.VariableType);
                //    il.Emit(OpCodes.Callvirt, setYieldValue);
                //}

                il.Emit(OpCodes.Ldsfld, Parent.AspectField);
                il.EmitLoadOrNull(null, meaField);
                il.Emit(OpCodes.Callvirt, onResume);

                //// Unbox the YieldValue and store it back in the result. Changing it is permitted here.

                //if (meaField != null && resultVarOpt != null)
                //{
                //    il.Emit(OpCodes.Ldarg_0);
                //    il.Emit(OpCodes.Ldfld, meaField);
                //    il.Emit(OpCodes.Callvirt, getYieldValue);
                //    il.EmitCastOrUnbox(resultVarOpt.VariableType);
                //    il.Emit(OpCodes.Stloc, resultVarOpt);

                //    //il.Emit(OpCodes.Ldarg_0);
                //    //il.Emit(OpCodes.Ldfld, meaField);
                //    //il.Emit(OpCodes.Ldnull);
                //    //il.Emit(OpCodes.Callvirt, setYieldValue);
                //}

                stateMachine.Body.InsertInstructions(insc.Count - eoffResume, true, il.Instructions);
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
                TypeDefinition asmType = Context.Framework.AsyncStateMachineAttribute;
                TypeDefinition ismType = Context.Framework.IteratorStateMachineAttribute;

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

        private static bool HasDifferentExceptionHandlers(MethodBody body, int offsetA, int offsetB)
        {
            return GetExceptionHandlerIndex(body, offsetA) != GetExceptionHandlerIndex(body, offsetB);
        }

        private static int GetExceptionHandlerIndex(MethodBody body, int offset)
        {
            if (body.HasExceptionHandlers && offset < body.Instructions.Count)
            {
                for (int i = 0; i < body.ExceptionHandlers.Count; i++)
                {
                    ExceptionHandler eh = body.ExceptionHandlers[i];

                    if (offset >= body.Instructions.IndexOf(eh.TryStart) &&
                        offset <= body.Instructions.IndexOf(eh.HandlerEnd) - 1)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }
    }
}
