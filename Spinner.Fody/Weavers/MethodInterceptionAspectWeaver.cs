using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Spinner.Fody.Utilities;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    /// <summary>
    /// Applies the method interception aspect to a method.
    /// </summary>
    internal sealed class MethodInterceptionAspectWeaver : AspectWeaver
    {
        private const string InvokeMethodName = "Invoke";
        private const string OnInvokeAdviceName = "OnInvoke";

        internal static void Weave(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex)
        {
            MethodDefinition original = DuplicateOriginalMethod(mwc, method, aspectIndex);
            
            TypeDefinition bindingType;
            CreateMethodBindingClass(mwc, method, aspectIndex, original, out bindingType);

            FieldReference aspectField;
            CreateAspectCacheField(mwc, method, aspectType, aspectIndex, out aspectField);

            // Clear the target method body as it needs entirely new code

            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();
            
            Collection<Ins> insc = method.Body.Instructions;
            
            //WriteOutArgumentInit(il);

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(mwc, method, insc.Count, out argumentsVariable);

            WriteCopyArgumentsToContainer(mwc, method, insc.Count, argumentsVariable, true);
            
            WriteAspectInit(mwc, method, insc.Count, aspectType, aspectField);

            WriteBindingInit(method, insc.Count, bindingType);
            
            FieldReference valueField;
            VariableDefinition iaVariable;
            WriteMiaInit(mwc, method, insc.Count, argumentsVariable, bindingType, out iaVariable, out valueField);

            WriteCallAdvice(mwc, method, insc.Count, OnInvokeAdviceName, aspectType, aspectField, iaVariable);
            
            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(mwc, method, insc.Count, argumentsVariable, false, true);

            if (valueField != null)
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, iaVariable));
                insc.Add(Ins.Create(OpCodes.Ldfld, valueField));
            }
            insc.Add(Ins.Create(OpCodes.Ret));

            // Fix labels and optimize

            method.Body.OptimizeMacros();
            method.Body.RemoveNops();
        }

        /// <summary>
        /// Writes the MethodInterceptionArgs initialization.
        /// </summary>
        private static void WriteMiaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int offset,
            VariableDefinition argumentsVariable,
            TypeDefinition bindingType,
            out VariableDefinition miaVariable,
            out FieldReference returnValueField)
        {
            ModuleDefinition module = method.Module;
            
            TypeReference miaType;
            MethodReference constructor;
            
            if (method.ReturnType == module.TypeSystem.Void)
            {
                TypeDefinition miaTypeDef = mwc.Spinner.BoundMethodInterceptionArgs;
                miaType = mwc.SafeImport(miaTypeDef);

                MethodDefinition constructorDef = mwc.Spinner.BoundMethodInterceptionArgs_ctor;
                constructor = mwc.SafeImport(constructorDef);

                returnValueField = null;
            }
            else
            {
                TypeDefinition miaTypeDef = mwc.Spinner.BoundMethodInterceptionArgsT1;
                GenericInstanceType genericMiaType = mwc.SafeImport(miaTypeDef).MakeGenericInstanceType(method.ReturnType);
                miaType = genericMiaType;

                MethodDefinition constructorDef = mwc.Spinner.BoundMethodInterceptionArgsT1_ctor;
                constructor = mwc.SafeImport(constructorDef).WithGenericDeclaringType(genericMiaType);

                FieldDefinition returnValueFieldDef = mwc.Spinner.BoundMethodInterceptionArgsT1_TypedReturnValue;
                returnValueField = mwc.SafeImport(returnValueFieldDef).WithGenericDeclaringType(genericMiaType);
            }

            miaVariable = method.Body.AddVariableDefinition(miaType);
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

            insc.Add(argumentsVariable == null
                ? Ins.Create(OpCodes.Ldnull)
                : Ins.Create(OpCodes.Ldloc, argumentsVariable));

            // the method binding
            insc.Add(bindingType == null
                ? Ins.Create(OpCodes.Ldnull)
                : Ins.Create(OpCodes.Ldsfld, bindingType.Fields.Single(f => f.Name == BindingInstanceFieldName)));

            insc.Add(Ins.Create(OpCodes.Newobj, constructor));
            insc.Add(Ins.Create(OpCodes.Stloc, miaVariable));

            method.Body.InsertInstructions(offset, insc);
        }

        private static void CreateMethodBindingClass(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int aspectIndex,
            MethodReference original,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = method.Module;

            TypeReference baseType;

            if (method.ReturnType == module.TypeSystem.Void)
            {
                baseType = mwc.SafeImport(mwc.Spinner.MethodBinding);
            }
            else
            {
                baseType = mwc.SafeImport(mwc.Spinner.MethodBindingT1).MakeGenericInstanceType(method.ReturnType);
            }

            string name = NameGenerator.MakeMethodBindingName(method.Name, aspectIndex);

            CreateBindingClass(mwc, method.DeclaringType, baseType, name, out bindingTypeDef);

            // Override the invoke method

            var invokeAttrs = MethodAttributes.Public |
                              MethodAttributes.Virtual |
                              MethodAttributes.Final |
                              MethodAttributes.HideBySig |
                              MethodAttributes.ReuseSlot;

            var invokeMethod = new MethodDefinition(InvokeMethodName, invokeAttrs, method.ReturnType);

            bindingTypeDef.Methods.Add(invokeMethod);

            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = mwc.SafeImport(mwc.Spinner.Arguments);

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            ILProcessor bil = invokeMethod.Body.GetILProcessor();

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(mwc, method, out argumentContainerType, out argumentContainerFields);

            // Case the arguments container from its base type to the generic instance type
            VariableDefinition argsContainer = null;
            if (method.Parameters.Count != 0)
            {
                argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                bil.Emit(OpCodes.Ldarg_2);
                bil.Emit(OpCodes.Castclass, argumentContainerType);
                bil.Emit(OpCodes.Stloc, argsContainer);
            }

            // Load the instance for the method call
            if (!method.IsStatic)
            {
                // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                bil.Emit(OpCodes.Ldarg_1);
                bil.Emit(OpCodes.Ldind_Ref);
                bil.Emit(method.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, method.DeclaringType);
            }

            // Load arguments or addresses directly from the arguments container
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                bool byRef = method.Parameters[i].ParameterType.IsByReference;

                bil.Emit(OpCodes.Ldloc, argsContainer);
                bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
            }

            if (method.IsStatic || method.DeclaringType.IsValueType)
                bil.Emit(OpCodes.Call, original);
            else
                bil.Emit(OpCodes.Callvirt, original);

            bil.Emit(OpCodes.Ret);
        }
    }
}
