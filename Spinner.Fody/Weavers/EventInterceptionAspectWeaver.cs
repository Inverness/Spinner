using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    /// <summary>
    /// Weaves events for which IEventInterceptionAspect is applied.
    /// </summary>
    internal sealed class EventInterceptionAspectWeaver : AspectWeaver
    {
        private const string AddHandlerMethodName = "AddHandler";
        private const string RemoveHandlerMethodName = "RemoveHandler";
        private const string InvokeHandlerMethodName = "InvokeHandler";

        internal static void Weave(
            ModuleWeavingContext mwc,
            EventDefinition evt,
            TypeDefinition aspectType,
            int aspectIndex)
        {
            MethodDefinition adder = evt.AddMethod;
            MethodDefinition remover = evt.RemoveMethod;

            MethodDefinition originalAdder = adder != null ? DuplicateOriginalMethod(mwc, adder, aspectIndex) : null;
            MethodDefinition originalRemover = remover != null ? DuplicateOriginalMethod(mwc, remover, aspectIndex) : null;

            FieldReference aspectField;
            CreateAspectCacheField(mwc, evt, aspectType, aspectIndex, out aspectField);

            TypeDefinition bindingClass;
            CreateEventBindingClass(mwc, evt, aspectIndex, originalAdder, originalRemover, out bindingClass);

            FieldDefinition backingField = GetEventBackingField(evt);
            if (backingField != null)
            {
                MethodDefinition invoker;
                CreateEventInvoker(mwc, evt, aspectType, backingField, aspectField, bindingClass, aspectIndex, out invoker);

                FieldDefinition invokerDelegateField;
                CreateEventInvokerDelegateField(mwc, evt, aspectIndex, out invokerDelegateField);

                foreach (MethodDefinition m in evt.DeclaringType.Methods)
                {
                    if (m == invoker)
                        continue;

                    RewriteEventBackingFieldReferences(mwc, evt, m, backingField, invoker, invokerDelegateField);
                }
            }
            // TODO: Replace backing field invocations in a class with a call to a generated method that will
            //       initialize the aspect and call OnInvoke() for each handler in the backing field

            if (adder != null)
                RewriteMethod(mwc, evt, adder, aspectType, aspectField, bindingClass);
            if (remover != null)
                RewriteMethod(mwc, evt, remover, aspectType, aspectField, bindingClass);
        }

        private static void RewriteMethod(
            ModuleWeavingContext mwc,
            EventDefinition evt,
            MethodDefinition method,
            TypeDefinition aspectType,
            FieldReference aspectField,
            TypeDefinition bindingType)
        {
            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            Collection<Ins> insc = method.Body.Instructions;

            WriteAspectInit(mwc, method, insc.Count, aspectType, aspectField);

            WriteBindingInit(method, insc.Count, bindingType);
            
            VariableDefinition eiaVariable;
            WriteEiaInit(mwc, method, insc.Count, null, bindingType, out eiaVariable);

            // Event handlers never have any arguments except the handler itself, which is not considered part of
            // the 'effective arguments' and thus not included in the arguments container.
            MethodReference setHandler = mwc.SafeImport(mwc.Spinner.EventInterceptionArgs_Handler.SetMethod);
            insc.Add(Ins.Create(OpCodes.Ldloc, eiaVariable));
            insc.Add(Ins.Create(OpCodes.Ldarg, method.Parameters.First()));
            insc.Add(Ins.Create(OpCodes.Callvirt, setHandler));

            MethodReference baseReference = method.IsRemoveOn
                ? mwc.Spinner.IEventInterceptionAspect_OnRemoveHandler
                : mwc.Spinner.IEventInterceptionAspect_OnAddHandler;

            WriteCallAdvice(mwc, method, insc.Count, baseReference, aspectType, aspectField, eiaVariable);

            insc.Add(Ins.Create(OpCodes.Ret));

            method.Body.RemoveNops();
            method.Body.OptimizeMacros();
        }

        private static void CreateEventBindingClass(
            ModuleWeavingContext mwc,
            EventDefinition evt,
            int aspectIndex,
            MethodReference originalAdder,
            MethodReference originalRemover,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = evt.Module;

            TypeReference baseDelegateType = mwc.SafeImport(mwc.Framework.Delegate);
            TypeReference argumentsBaseType = mwc.SafeImport(mwc.Spinner.Arguments);
            TypeDefinition delegateTypeDef = evt.EventType.Resolve();
            TypeReference delegateType = mwc.SafeImport(delegateTypeDef);
            MethodDefinition delegateInvokeMethodDef = delegateTypeDef.Methods.Single(m => m.Name == "Invoke");
            MethodReference delegateInvokeMethod = mwc.SafeImport(delegateInvokeMethodDef);

            string name = NameGenerator.MakeEventBindingName(evt.Name, aspectIndex);
            TypeReference baseType = mwc.SafeImport(mwc.Spinner.EventBinding);
            CreateBindingClass(mwc, evt.DeclaringType, baseType, name, out bindingTypeDef);

            for (int i = 0; i < 2; i++)
            {
                string methodName = i == 0 ? AddHandlerMethodName : RemoveHandlerMethodName;
                MethodReference original = i == 0 ? originalAdder : originalRemover;
                MethodDefinition eventMethod = i == 0 ? evt.AddMethod : evt.RemoveMethod;

                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(methodName, mattrs, module.TypeSystem.Void);

                bindingTypeDef.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, baseDelegateType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                if (eventMethod != null)
                {
                    // Load the instance for the method call
                    if (!eventMethod.IsStatic)
                    {
                        // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                        bil.Emit(OpCodes.Ldarg_1);
                        bil.Emit(OpCodes.Ldind_Ref);
                        bil.Emit(evt.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 evt.DeclaringType);
                    }

                    bil.Emit(OpCodes.Ldarg_2);
                    bil.Emit(OpCodes.Castclass, delegateType);

                    if (eventMethod.IsStatic || eventMethod.DeclaringType.IsValueType)
                        bil.Emit(OpCodes.Call, original);
                    else
                        bil.Emit(OpCodes.Callvirt, original);
                }
                // TODO: Manual event add

                bil.Emit(OpCodes.Ret);
            }

            {

                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(InvokeHandlerMethodName, mattrs, module.TypeSystem.Object);

                bindingTypeDef.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, baseDelegateType));
                bmethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                GenericInstanceType argumentContainerType;
                FieldReference[] argumentContainerFields;
                GetArgumentContainerInfo(mwc,
                                         delegateInvokeMethodDef,
                                         out argumentContainerType,
                                         out argumentContainerFields);

                VariableDefinition argsContainer = null;
                if (delegateInvokeMethodDef.Parameters.Count != 0)
                {
                    argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                    bil.Emit(OpCodes.Ldarg_3);
                    bil.Emit(OpCodes.Castclass, argumentContainerType);
                    bil.Emit(OpCodes.Stloc, argsContainer);
                }

                // Invoke the delegate

                bil.Emit(OpCodes.Ldarg_2);
                bil.Emit(OpCodes.Castclass, delegateType);

                for (int i = 0; i < delegateInvokeMethodDef.Parameters.Count; i++)
                {
                    bool byRef = delegateInvokeMethodDef.Parameters[i].ParameterType.IsByReference;

                    bil.Emit(OpCodes.Ldloc, argsContainer);
                    bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
                }

                bil.Emit(OpCodes.Callvirt, delegateInvokeMethod);

                if (delegateInvokeMethod.ReturnType.IsSimilar(module.TypeSystem.Void))
                    bil.Emit(OpCodes.Ldnull);

                bil.Emit(OpCodes.Ret);
            }
        }

        /// <summary>
        /// Write MethodExecutionArgs initialization.
        /// </summary>
        private static void WriteEiaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVarOpt,
            TypeDefinition bindingType,
            out VariableDefinition eiaVar)
        {
            TypeReference eiaType = mwc.SafeImport(mwc.Spinner.BoundEventInterceptionArgs);
            MethodReference eiaCtor = mwc.SafeImport(mwc.Spinner.BoundEventInterceptionArgs_ctor);
            FieldDefinition bindingField = bindingType.Fields.First(f => f.Name == BindingInstanceFieldName);

            eiaVar = method.Body.AddVariableDefinition(eiaType);

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

            insc.Add(argumentsVarOpt == null ? Ins.Create(OpCodes.Ldnull) : Ins.Create(OpCodes.Ldloc, argumentsVarOpt));

            insc.Add(Ins.Create(OpCodes.Ldsfld, bindingField));

            insc.Add(Ins.Create(OpCodes.Newobj, eiaCtor));

            insc.Add(Ins.Create(OpCodes.Stloc, eiaVar));

            method.Body.InsertInstructions(offset, insc);
        }

        /// <summary>
        /// Create a field used to cache the event invoker delegate.
        /// </summary>
        private static void CreateEventInvokerDelegateField(
            ModuleWeavingContext mwc,
            EventDefinition evt,
            int aspectIndex,
            out FieldDefinition field)
        {
            Debug.Assert(evt.AddMethod != null);

            string name = NameGenerator.MakeEventInvokerDelegateCacheName(evt.Name, aspectIndex);

            var attrs = FieldAttributes.Private | (evt.AddMethod.IsStatic ? FieldAttributes.Static : 0);

            field = new FieldDefinition(name, attrs, evt.EventType)
            {
                DeclaringType = evt.DeclaringType
            };

            AddCompilerGeneratedAttribute(mwc, field);

            evt.DeclaringType.Fields.Add(field);
        }

        /// <summary>
        /// Create a method that will be used to invoke an event with OnInvokeHandler() calls for each handler.
        /// </summary>
        private static void CreateEventInvoker(
            ModuleWeavingContext mwc,
            EventDefinition evt,
            TypeDefinition aspectType,
            FieldDefinition eventField,
            FieldReference aspectField,
            TypeDefinition bindingClass,
            int aspectIndex,
            out MethodDefinition invoker)
        {
            MethodDefinition invokeEventMethodDef = mwc.Spinner.WeaverHelpers_InvokeEvent;
            GenericInstanceMethod invokeEventMethod = new GenericInstanceMethod(mwc.SafeImport(invokeEventMethodDef));
            invokeEventMethod.GenericArguments.Add(aspectType);

            // Create the method definition

            string name = NameGenerator.MakeEventInvokerName(evt.Name, aspectIndex);
            var attrs = MethodAttributes.Private |
                        MethodAttributes.HideBySig |
                        (eventField.IsStatic ? MethodAttributes.Static : 0);

            invoker = new MethodDefinition(name, attrs, evt.Module.TypeSystem.Void)
            {
                DeclaringType = evt.DeclaringType
            };

            AddCompilerGeneratedAttribute(mwc, invoker);
            
            evt.DeclaringType.Methods.Add(invoker);
            
            // Add parameters matching the event delegate

            var delegateInvokeMethod = eventField.FieldType.Resolve().Methods.Single(m => m.Name == "Invoke");

            if (delegateInvokeMethod.HasParameters)
            {
                foreach (ParameterDefinition p in delegateInvokeMethod.Parameters)
                {
                    invoker.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, mwc.SafeImport(p.ParameterType)));
                }
            }

            // Start writing the body by capturing the event backing field and checking if its null.

            VariableDefinition handlerVar = invoker.Body.AddVariableDefinition(eventField.FieldType);

            ILProcessor il = invoker.Body.GetILProcessor();

            if (invoker.IsStatic)
            {
                il.Emit(OpCodes.Ldsfld, eventField);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, eventField);
            }
            il.Emit(OpCodes.Dup);

            Ins notNullLabel = Ins.Create(OpCodes.Nop);

            il.Emit(OpCodes.Brtrue, notNullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            il.Append(notNullLabel);
            il.Emit(OpCodes.Stloc, handlerVar);

            // Capture arguments

            VariableDefinition argumentsVar = null;
            if (invoker.HasParameters)
            {
                WriteArgumentContainerInit(mwc, invoker, invoker.Body.Instructions.Count, out argumentsVar);
                WriteCopyArgumentsToContainer(mwc, invoker, invoker.Body.Instructions.Count, argumentsVar, true);
            }

            // Initialize the EventInterceptionArgs

            WriteAspectInit(mwc, invoker, invoker.Body.Instructions.Count, aspectType, aspectField);

            WriteBindingInit(invoker, invoker.Body.Instructions.Count, bindingClass);

            VariableDefinition eiaVar;
            WriteEiaInit(mwc, invoker, invoker.Body.Instructions.Count, argumentsVar, bindingClass, out eiaVar);

            // The remaining work is handed off to a helper method since the code is not type-specific.

            il.Emit(OpCodes.Ldloc, handlerVar);
            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, eiaVar);
            il.Emit(OpCodes.Call, invokeEventMethod);
            il.Emit(OpCodes.Ret);

            invoker.Body.RemoveNops();
            invoker.Body.OptimizeMacros();

            // Create a field that will be used to cache the invoker's delegate in the future
        }

        private static void RewriteEventBackingFieldReferences(
            ModuleWeavingContext mwc,
            EventDefinition evt,
            MethodDefinition method,
            FieldDefinition eventBackingField,
            MethodDefinition invokerMethod,
            FieldDefinition invokerField)
        {
            bool isStatic = eventBackingField.IsStatic;

            Collection<Ins> insc = method.Body.Instructions;
            Collection<Ins> newinsc = null;
            MethodReference delegateCtor = null;
            HashSet<Ins> existingNops = null;

            OpCode loadOpCode = isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;

            for (int i = 0; i < insc.Count; i++)
            {
                Ins ins = insc[i];
                if (ins.OpCode != loadOpCode)
                    continue;

                var fr = (FieldReference) ins.Operand;
                if (!fr.IsSimilar(eventBackingField) || fr.Resolve() != eventBackingField)
                    continue;
                
                // Replace with reference to new field
                ins.Operand = invokerField;

                // Lazily initialize some stuff the first time work needs to be done
                if (newinsc == null)
                {
                    newinsc = new Collection<Ins>();
                    existingNops = new HashSet<Ins>(insc.Where(ir => ir.OpCode == OpCodes.Nop));

                    MethodDefinition delegateCtorDef = evt.EventType.Resolve().Methods.Single(m => m.IsConstructor);
                    delegateCtor = mwc.SafeImport(delegateCtorDef);
                    if (evt.EventType.IsGenericInstance)
                        delegateCtor = delegateCtor.WithGenericDeclaringType((GenericInstanceType) evt.EventType);
                }

                // Insert delegate initializer BEFORE the current instruction
                Ins notNullLabel = Ins.Create(OpCodes.Nop);
                if (isStatic)
                {
                    newinsc.Add(Ins.Create(OpCodes.Ldsfld, invokerField));
                    newinsc.Add(Ins.Create(OpCodes.Brtrue, notNullLabel));
                    newinsc.Add(Ins.Create(OpCodes.Ldnull));
                    newinsc.Add(Ins.Create(OpCodes.Ldftn, invokerMethod));
                    newinsc.Add(Ins.Create(OpCodes.Newobj, delegateCtor));
                    newinsc.Add(Ins.Create(OpCodes.Stsfld, invokerField));
                    newinsc.Add(notNullLabel);

                    method.Body.InsertInstructions(i, newinsc);
                }
                else
                {
                    newinsc.Add(Ins.Create(OpCodes.Ldarg_0));
                    newinsc.Add(Ins.Create(OpCodes.Ldfld, invokerField));
                    newinsc.Add(Ins.Create(OpCodes.Brtrue, notNullLabel));
                    newinsc.Add(Ins.Create(OpCodes.Ldarg_0));
                    newinsc.Add(Ins.Create(OpCodes.Dup));
                    newinsc.Add(Ins.Create(OpCodes.Ldftn, invokerMethod));
                    newinsc.Add(Ins.Create(OpCodes.Newobj, delegateCtor));
                    newinsc.Add(Ins.Create(OpCodes.Stfld, invokerField));
                    newinsc.Add(notNullLabel);
                    
                    Debug.Assert(insc[i - 1].OpCode == OpCodes.Ldarg_0 || insc[i - 1].OpCode == OpCodes.Ldarg);
                    method.Body.InsertInstructions(i - 1, newinsc);
                }

                method.Body.UpdateOffsets();

                newinsc.Clear();
            }

            if (newinsc != null)
            {
                method.Body.RemoveNops(existingNops);
                method.Body.OptimizeMacros();
            }
        }

        /// <summary>
        /// Gets the backing field used to store an event's handler. This only exists for events without custom add
        /// and remove methods.
        /// </summary>
        private static FieldDefinition GetEventBackingField(EventDefinition evt)
        {
            if (!evt.DeclaringType.HasFields)
                return null;

            return evt.DeclaringType.Fields.SingleOrDefault(f => f.Name == evt.Name &&
                                                                    f.FieldType == evt.EventType);
        }
    }
}