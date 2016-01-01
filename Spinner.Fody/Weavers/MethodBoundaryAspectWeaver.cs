using System;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
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
        private const string OnEntryAdviceName = "OnEntry";
        private const string OnExitAdviceName = "OnExit";
        private const string OnExceptionAdviceName = "OnException";
        private const string OnSuccessAdviceName = "OnSuccess";
        private const string OnYieldAdviceName = "OnYield";
        private const string OnResumeAdviceName = "OnResume";
        private const string FilterExceptionAdviceName = "FilterException";

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

                    // TODO: Check if removing nop's has implications for debug builds.
                    method.Body.RemoveNops();
                    method.Body.OptimizeMacros();
                    //method.Body.UpdateOffsets();
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
                    //moveNextMethod.Body.UpdateOffsets();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void WeaveMethod(
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

        private static void WeaveAsyncMethod(
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
            bool withEntry = (features & Features.OnEntry) != 0;
            bool withException = (features & Features.OnException) != 0;
            bool withExit = (features & Features.OnExit) != 0;
            bool withSuccess = (features & Features.OnSuccess) != 0;
            bool withYield = (features & Features.OnYield) != 0;
            bool withResume = (features & Features.OnResume) != 0;

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
            if (effectiveReturnType != null)
            {
                int resultStore = SeekR(insc, insc.Count - leaveEndOffset, i => i.OpCode == OpCodes.Stloc);
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

            if (withEntry)
            {
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
            }

            // Search through the body for places to insert OnYield() and OnResume() calls
            if (withYield || withResume)
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

                    WriteYieldAndResume(mwc,
                                        stateMachine,
                                        awaitable,
                                        callYield,
                                        callResume,
                                        aspectType,
                                        aspectField,
                                        mea,
                                        awaitableStorage,
                                        awaitResultVar);
                }
            }

            // Everything following this point is written after the body but inside the async exception handler.

            if (withException || withExit || withSuccess)
            {
                Ins labelSuccess = CreateNop();

                // Rewrite all leaves that go to the SetResult() area, but not the ones that return after an await.
                // They will be replaced by breaks to labelSuccess.
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
                // This is done last to ensure that insertions for the exception and exit handlers do not break
                // any offsets.
                if (withException || withExit)
                    stateMachine.Body.InsertInstructions(leaveInsertPoint, Ins.Create(OpCodes.Leave, insc[insc.Count - leaveEndOffset]));
                
                // Ensure the task EH is last
                stateMachine.Body.ExceptionHandlers.Remove(taskExceptionHandler);
                stateMachine.Body.ExceptionHandlers.Add(taskExceptionHandler);
            }
        }

        private static void WeaveIteratorMethod(
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
            TypeReference awaiterType = null;
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

                    awaiterType = mr.DeclaringType;
                }
                else if (callYield != -1 && mr.Name == "GetResult" && mr.DeclaringType.IsSame(awaiterType))
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

        /// <summary>
        /// Resolves a generic parameter type or return type for a method.
        /// </summary>
        private static TypeReference ResolveMethodGenericParameter(
            TypeReference parameter,
            MethodReference method)
        {
            if (!parameter.IsGenericParameter)
                return parameter;

            var gp = (GenericParameter) parameter;

            if (gp.Type == GenericParameterType.Type)
            {
                Debug.Assert(method.DeclaringType.IsGenericInstance, "method declaring type is not a generic instance");
                return ((GenericInstanceType) method.DeclaringType).GenericArguments[gp.Position];
            }
            else
            {
                Debug.Assert(method.IsGenericInstance, "method is not a generic instance");
                return ((GenericInstanceMethod) method).GenericArguments[gp.Position];
            }
        }

        private static void WriteMeaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            VariableDefinition argumentsVar,
            int offset,
            out VariableDefinition meaVar)
        {
            TypeReference meaType = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs_ctor);

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

        private static void WriteSmMeaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            MethodDefinition stateMachine,
            VariableDefinition arguments,
            int offset,
            out FieldDefinition mea)
        {
            TypeReference meaType = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs);
            MethodReference meaCtor = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs_ctor);

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
                    //insc.Add(Ins.Create(OpCodes.Ldobj, method.DeclaringType));
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

        private static void WriteOnEntryCall(
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

        private static void WriteSmOnEntryCall(
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

        private static void RewriteReturns(
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
                Ins newIns = i == endIndex && skipLast ? CreateNop() : Ins.Create(OpCodes.Br, brTarget);

                body.ReplaceInstruction(i, newIns);
            }
        }

        private static void WriteSuccessHandler(
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

        private static void WriteCatchExceptionHandler(
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
                MethodReference getException = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs_Exception.GetMethod);
                MethodReference setException = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs_Exception.SetMethod);

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

        private static void WriteFinallyExceptionHandler(
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

        private static void WriteYieldAndResume(
            ModuleWeavingContext mwc,
            MethodDefinition stateMachine,
            int getAwaiterOffset,
            int yieldOffset,
            int resumeOffset,
            TypeDefinition aspectType,
            FieldReference aspectField,
            FieldReference meaField,
            VariableDefinition awaitableVar,
            VariableDefinition resultVar
            )
        {
            MethodReference getYieldValue = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs_YieldValue.GetMethod);
            MethodReference setYieldValue = mwc.SafeImport(mwc.Spinner.MethodExecutionArgs_YieldValue.SetMethod);

            var insc = new Collection<Ins>();
            int offset = 0;

            if (yieldOffset != -1)
            {
                MethodDefinition onYieldDef = aspectType.GetInheritedMethods().First(m => m.Name == OnYieldAdviceName);
                MethodReference onYield = mwc.SafeImport(onYieldDef);

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

                insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
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
                MethodDefinition onResumeDef = aspectType.GetInheritedMethods().First(m => m.Name == OnResumeAdviceName);
                MethodReference onResume = mwc.SafeImport(onResumeDef);

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

                insc.Add(Ins.Create(OpCodes.Ldsfld, aspectField));
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
        private static StateMachineKind GetStateMachineInfo(ModuleWeavingContext mwc, MethodDefinition method, out MethodDefinition moveNextMethod)
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

        private static Features GetFeatures(ModuleWeavingContext mwc, TypeDefinition aspectType)
        {
            TypeDefinition featuresAttributeType = mwc.Spinner.FeaturesAttribute;

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
