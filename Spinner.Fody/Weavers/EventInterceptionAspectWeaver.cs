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

        private readonly EventDefinition _evt;
        private FieldDefinition _evtBackingField;

        private MethodDefinition _originalAdder;
        private MethodDefinition _originalRemover;

        private FieldDefinition _invokerDelegateField;
        private MethodDefinition _invokerMethod;

        public EventInterceptionAspectWeaver(
            ModuleWeavingContext mwc,
            TypeDefinition aspectType,
            int aspectIndex,
            EventDefinition aspectTarget)
            : base(mwc, aspectType, aspectIndex, aspectTarget)
        {
            _evt = aspectTarget;
        }

        protected override void Weave()
        {
            MethodDefinition adder = _evt.AddMethod;
            MethodDefinition remover = _evt.RemoveMethod;

            _originalAdder = adder != null ? DuplicateOriginalMethod(adder) : null;
            _originalRemover = remover != null ? DuplicateOriginalMethod(remover) : null;

            CreateAspectCacheField();

            CreateEventBindingClass();

            _evtBackingField = GetEventBackingField(_evt);
            if (_evtBackingField != null)
            {
                CreateEventInvoker();
                
                CreateEventInvokerDelegateField();

                foreach (MethodDefinition m in _evt.DeclaringType.Methods)
                {
                    if (m == _invokerMethod)
                        continue;

                    RewriteEventBackingFieldReferences(m);
                }
            }

            if (adder != null)
                RewriteMethod(adder);
            if (remover != null)
                RewriteMethod(remover);
        }

        internal static void Weave(ModuleWeavingContext mwc, EventDefinition evt, TypeDefinition aspect, int index)
        {
            new EventInterceptionAspectWeaver(mwc, aspect, index, evt).Weave();
        }

        private void RewriteMethod(MethodDefinition method)
        {
            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            Collection<Ins> insc = method.Body.Instructions;

            WriteAspectInit(method, insc.Count);

            WriteBindingInit(method, insc.Count);
            
            VariableDefinition eiaVariable;
            WriteEiaInit(method, insc.Count, null, out eiaVariable);

            if (_aspectFeatures.Has(Features.MemberInfo))
                WriteSetEventInfo(method, insc.Count, eiaVariable);

            // Event handlers never have any arguments except the handler itself, which is not considered part of
            // the 'effective arguments' and thus not included in the arguments container.
            MethodReference setHandler = _mwc.SafeImport(_mwc.Spinner.EventInterceptionArgs_Handler.SetMethod);
            insc.Add(Ins.Create(OpCodes.Ldloc, eiaVariable));
            insc.Add(Ins.Create(OpCodes.Ldarg, method.Parameters.First()));
            insc.Add(Ins.Create(OpCodes.Callvirt, setHandler));

            MethodReference adviceBase = method.IsRemoveOn
                ? _mwc.Spinner.IEventInterceptionAspect_OnRemoveHandler
                : _mwc.Spinner.IEventInterceptionAspect_OnAddHandler;

            WriteCallAdvice(method, insc.Count, adviceBase, eiaVariable);

            insc.Add(Ins.Create(OpCodes.Ret));

            method.Body.RemoveNops();
            method.Body.OptimizeMacros();
        }

        private void WriteSetEventInfo(
            MethodDefinition method,
            int offset,
            VariableDefinition eiaVariable)
        {
            MethodReference getTypeFromHandle = _mwc.SafeImport(_mwc.Framework.Type_GetTypeFromHandle);
            MethodReference setEvent = _mwc.SafeImport(_mwc.Spinner.EventInterceptionArgs_Event.SetMethod);
            MethodReference getEventInfo = _mwc.SafeImport(_mwc.Spinner.WeaverHelpers_GetEventInfo);

            var insc = new[]
            {
                Ins.Create(OpCodes.Ldloc, eiaVariable),
                Ins.Create(OpCodes.Ldtoken, _evt.DeclaringType),
                Ins.Create(OpCodes.Call, getTypeFromHandle),
                Ins.Create(OpCodes.Ldstr, _evt.Name),
                Ins.Create(OpCodes.Call, getEventInfo),
                Ins.Create(OpCodes.Callvirt, setEvent)
            };

            method.Body.InsertInstructions(offset, insc);
        }

        private void CreateEventBindingClass()
        {
            ModuleDefinition module = _evt.Module;

            TypeReference baseDelegateType = _mwc.SafeImport(_mwc.Framework.Delegate);
            TypeReference argumentsBaseType = _mwc.SafeImport(_mwc.Spinner.Arguments);
            TypeDefinition delegateTypeDef = _evt.EventType.Resolve();
            TypeReference delegateType = _mwc.SafeImport(delegateTypeDef);
            MethodDefinition delegateInvokeMethodDef = delegateTypeDef.Methods.Single(m => m.Name == "Invoke");
            MethodReference delegateInvokeMethod = _mwc.SafeImport(delegateInvokeMethodDef);

            string name = NameGenerator.MakeEventBindingName(_evt.Name, _aspectIndex);
            TypeReference baseType = _mwc.SafeImport(_mwc.Spinner.EventBinding);
            CreateBindingClass(baseType, name);

            for (int i = 0; i < 2; i++)
            {
                string methodName = i == 0 ? AddHandlerMethodName : RemoveHandlerMethodName;
                MethodReference original = i == 0 ? _originalAdder : _originalRemover;
                MethodDefinition eventMethod = i == 0 ? _evt.AddMethod : _evt.RemoveMethod;

                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(methodName, mattrs, module.TypeSystem.Void);

                _bindingClass.Methods.Add(bmethod);

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
                        bil.Emit(_evt.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 _evt.DeclaringType);
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

                _bindingClass.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, baseDelegateType));
                bmethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                GenericInstanceType argumentContainerType;
                FieldReference[] argumentContainerFields;
                GetArgumentContainerInfo(delegateInvokeMethodDef,
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
        private void WriteEiaInit(
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVarOpt,
            out VariableDefinition eiaVar)
        {
            TypeReference eiaType = _mwc.SafeImport(_mwc.Spinner.BoundEventInterceptionArgs);
            MethodReference eiaCtor = _mwc.SafeImport(_mwc.Spinner.BoundEventInterceptionArgs_ctor);

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

            insc.Add(Ins.Create(OpCodes.Ldsfld, _bindingInstanceField));

            insc.Add(Ins.Create(OpCodes.Newobj, eiaCtor));

            insc.Add(Ins.Create(OpCodes.Stloc, eiaVar));

            method.Body.InsertInstructions(offset, insc);
        }

        /// <summary>
        /// Create a field used to cache the event invoker delegate.
        /// </summary>
        private void CreateEventInvokerDelegateField()
        {
            Debug.Assert(_evt.AddMethod != null);

            string name = NameGenerator.MakeEventInvokerDelegateCacheName(_evt.Name, _aspectIndex);

            var attrs = FieldAttributes.Private | (_evt.AddMethod.IsStatic ? FieldAttributes.Static : 0);

            _invokerDelegateField = new FieldDefinition(name, attrs, _evt.EventType)
            {
                DeclaringType = _evt.DeclaringType
            };

            AddCompilerGeneratedAttribute(_invokerDelegateField);

            _evt.DeclaringType.Fields.Add(_invokerDelegateField);
        }

        /// <summary>
        /// Create a method that will be used to invoke an event with OnInvokeHandler() calls for each handler.
        /// </summary>
        private void CreateEventInvoker()
        {
            MethodDefinition invokeEventMethodDef = _mwc.Spinner.WeaverHelpers_InvokeEvent;
            GenericInstanceMethod invokeEventMethod = new GenericInstanceMethod(_mwc.SafeImport(invokeEventMethodDef));
            invokeEventMethod.GenericArguments.Add(_aspectType);

            // Create the method definition

            string name = NameGenerator.MakeEventInvokerName(_evt.Name, _aspectIndex);
            var attrs = MethodAttributes.Private |
                        MethodAttributes.HideBySig |
                        (_evtBackingField.IsStatic ? MethodAttributes.Static : 0);

            _invokerMethod = new MethodDefinition(name, attrs, _evt.Module.TypeSystem.Void)
            {
                DeclaringType = _evt.DeclaringType
            };

            AddCompilerGeneratedAttribute(_invokerMethod);
            
            _evt.DeclaringType.Methods.Add(_invokerMethod);
            
            // Add parameters matching the event delegate

            var delegateInvokeMethod = _evtBackingField.FieldType.Resolve().Methods.Single(m => m.Name == "Invoke");

            if (delegateInvokeMethod.HasParameters)
            {
                foreach (ParameterDefinition p in delegateInvokeMethod.Parameters)
                {
                    _invokerMethod.Parameters.Add(new ParameterDefinition(p.Name,
                                                                    ParameterAttributes.None,
                                                                    _mwc.SafeImport(p.ParameterType)));
                }
            }

            // Start writing the body by capturing the event backing field and checking if its null.

            VariableDefinition handlerVar = _invokerMethod.Body.AddVariableDefinition(_evtBackingField.FieldType);

            ILProcessor il = _invokerMethod.Body.GetILProcessor();

            if (_invokerMethod.IsStatic)
            {
                il.Emit(OpCodes.Ldsfld, _evtBackingField);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _evtBackingField);
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
            if (_invokerMethod.HasParameters)
            {
                WriteArgumentContainerInit(_invokerMethod, _invokerMethod.Body.Instructions.Count, out argumentsVar);
                WriteCopyArgumentsToContainer(_invokerMethod, _invokerMethod.Body.Instructions.Count, argumentsVar, true);
            }

            // Initialize the EventInterceptionArgs

            WriteAspectInit(_invokerMethod, _invokerMethod.Body.Instructions.Count);

            WriteBindingInit(_invokerMethod, _invokerMethod.Body.Instructions.Count);

            VariableDefinition eiaVar;
            WriteEiaInit(_invokerMethod, _invokerMethod.Body.Instructions.Count, argumentsVar, out eiaVar);

            // The remaining work is handed off to a helper method since the code is not type-specific.

            il.Emit(OpCodes.Ldloc, handlerVar);
            il.Emit(OpCodes.Ldsfld, _aspectField);
            il.Emit(OpCodes.Ldloc, eiaVar);
            il.Emit(OpCodes.Call, invokeEventMethod);
            il.Emit(OpCodes.Ret);

            _invokerMethod.Body.RemoveNops();
            _invokerMethod.Body.OptimizeMacros();

            // Create a field that will be used to cache the invoker's delegate in the future
        }

        private void RewriteEventBackingFieldReferences(MethodDefinition method)
        {
            bool isStatic = _evtBackingField.IsStatic;

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
                if (!fr.IsSimilar(_evtBackingField) || fr.Resolve() != _evtBackingField)
                    continue;
                
                // Replace with reference to new field
                ins.Operand = _invokerDelegateField;

                // Lazily initialize some stuff the first time work needs to be done
                if (newinsc == null)
                {
                    newinsc = new Collection<Ins>();
                    existingNops = new HashSet<Ins>(insc.Where(ir => ir.OpCode == OpCodes.Nop));

                    MethodDefinition delegateCtorDef = _evt.EventType.Resolve().Methods.Single(m => m.IsConstructor);
                    delegateCtor = _mwc.SafeImport(delegateCtorDef);
                    if (_evt.EventType.IsGenericInstance)
                        delegateCtor = delegateCtor.WithGenericDeclaringType((GenericInstanceType) _evt.EventType);
                }

                // Insert delegate initializer BEFORE the current instruction
                Ins notNullLabel = Ins.Create(OpCodes.Nop);
                if (isStatic)
                {
                    newinsc.Add(Ins.Create(OpCodes.Ldsfld, _invokerDelegateField));
                    newinsc.Add(Ins.Create(OpCodes.Brtrue, notNullLabel));
                    newinsc.Add(Ins.Create(OpCodes.Ldnull));
                    newinsc.Add(Ins.Create(OpCodes.Ldftn, _invokerMethod));
                    newinsc.Add(Ins.Create(OpCodes.Newobj, delegateCtor));
                    newinsc.Add(Ins.Create(OpCodes.Stsfld, _invokerDelegateField));
                    newinsc.Add(notNullLabel);

                    method.Body.InsertInstructions(i, newinsc);
                }
                else
                {
                    newinsc.Add(Ins.Create(OpCodes.Ldarg_0));
                    newinsc.Add(Ins.Create(OpCodes.Ldfld, _invokerDelegateField));
                    newinsc.Add(Ins.Create(OpCodes.Brtrue, notNullLabel));
                    newinsc.Add(Ins.Create(OpCodes.Ldarg_0));
                    newinsc.Add(Ins.Create(OpCodes.Dup));
                    newinsc.Add(Ins.Create(OpCodes.Ldftn, _invokerMethod));
                    newinsc.Add(Ins.Create(OpCodes.Newobj, delegateCtor));
                    newinsc.Add(Ins.Create(OpCodes.Stfld, _invokerDelegateField));
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