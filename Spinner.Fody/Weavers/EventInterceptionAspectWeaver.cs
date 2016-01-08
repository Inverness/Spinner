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
        internal static void Weave(
            ModuleWeavingContext mwc,
            EventDefinition xevent,
            TypeDefinition aspectType,
            int aspectIndex)
        {
            TypeDefinition dtype = xevent.DeclaringType;
            string originalName = ExtractOriginalName(xevent.Name);

            string cacheFieldName = $"<{originalName}>z__CachedAspect" + aspectIndex;
            string bindingClassName = $"<{originalName}>z__EventBinding" + aspectIndex;

            MethodDefinition adder = xevent.AddMethod;
            MethodDefinition remover = xevent.RemoveMethod;

            MethodDefinition originalAdder = adder != null ? DuplicateOriginalMethod(adder, aspectIndex) : null;
            MethodDefinition originalRemover = remover != null ? DuplicateOriginalMethod(remover, aspectIndex) : null;

            FieldReference aspectField;
            CreateAspectCacheField(mwc, dtype, aspectType, cacheFieldName, out aspectField);

            TypeDefinition bindingClass;
            CreateEventBindingClass(mwc, xevent, bindingClassName, originalAdder, originalRemover, out bindingClass);

            //FieldDefinition backingField = GetEventBackingField(xevent);
            // TODO: Replace backing field invocations in a class with a call to a generated method that will
            //       initialize the aspect and call OnInvoke() for each handler in the backing field

            if (adder != null)
                WeaveMethod(mwc, xevent, adder, aspectType, aspectField, bindingClass);
            if (remover != null)
                WeaveMethod(mwc, xevent, remover, aspectType, aspectField, bindingClass);
        }

        private static void WeaveMethod(
            ModuleWeavingContext mwc,
            EventDefinition xevent,
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

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(mwc, method, insc.Count, out argumentsVariable);

            WriteCopyArgumentsToContainer(mwc, method, insc.Count, argumentsVariable, true);

            WriteAspectInit(mwc, method, insc.Count, aspectType, aspectField);

            WriteBindingInit(method, insc.Count, bindingType);

            VariableDefinition eiaVariable = null;

            string adviceName = method.IsRemoveOn ? "OnRemoveHandler" : "OnAddHandler";
            WriteCallAdvice(mwc, method, insc.Count, adviceName, aspectType, aspectField, eiaVariable);

            insc.Add(Ins.Create(OpCodes.Ret));

            method.Body.OptimizeMacros();
            method.Body.RemoveNops();
        }

        private static void CreateEventBindingClass(
            ModuleWeavingContext mwc,
            EventDefinition xevent,
            string name,
            MethodReference originalAdder,
            MethodReference originalRemover,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = xevent.Module;

            TypeReference baseDelegateType = mwc.SafeImport(mwc.Framework.Delegate);
            TypeReference argumentsBaseType = mwc.SafeImport(mwc.Spinner.Arguments);
            TypeDefinition delegateTypeDef = xevent.EventType.Resolve();
            TypeReference delegateType = mwc.SafeImport(delegateTypeDef);
            MethodDefinition delegateInvokeMethodDef = delegateTypeDef.Methods.Single(m => m.Name == "Invoke");
            MethodReference delegateInvokeMethod = mwc.SafeImport(delegateInvokeMethodDef);

            TypeReference baseType = mwc.SafeImport(mwc.Spinner.EventBinding);
            CreateBindingClass(mwc, xevent.DeclaringType, baseType, name, out bindingTypeDef);

            for (int i = 0; i < 2; i++)
            {
                string methodName = i == 0 ? "AddHandler" : "RemoveHandler";
                MethodReference original = i == 0 ? originalAdder : originalRemover;
                MethodDefinition eventMethod = i == 0 ? xevent.AddMethod : xevent.RemoveMethod;

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
                        bil.Emit(xevent.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 xevent.DeclaringType);
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

                var bmethod = new MethodDefinition("InvokeHandler", mattrs, module.TypeSystem.Object);

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

                    bil.Emit(OpCodes.Ldarg_2);
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
        /// Gets the backing field used to store an event's handler. This only exists for events without custom add
        /// and remove methods.
        /// </summary>
        private static FieldDefinition GetEventBackingField(EventDefinition xevent)
        {
            if (!xevent.DeclaringType.HasFields)
                return null;

            return xevent.DeclaringType.Fields.SingleOrDefault(f => f.Name == xevent.Name &&
                                                                    f.FieldType == xevent.EventType);
        }
    }
}