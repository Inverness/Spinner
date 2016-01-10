using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Ins = Mono.Cecil.Cil.Instruction;

namespace Spinner.Fody.Weavers
{
    internal sealed class PropertyInterceptionAspectWeaver : AspectWeaver
    {
        private const string GetValueMethodName = "GetValue";
        private const string SetValueMethodName = "SetValue";

        internal static void Weave(
            ModuleWeavingContext mwc,
            PropertyDefinition property,
            TypeDefinition aspectType,
            int aspectIndex)
        {
            Debug.Assert(property.GetMethod != null || property.SetMethod != null);

            MethodDefinition getter = property.GetMethod;
            MethodDefinition setter = property.SetMethod;

            MethodDefinition originalGetter = getter != null ? DuplicateOriginalMethod(mwc, getter, aspectIndex) : null;
            MethodDefinition originalSetter = setter != null ? DuplicateOriginalMethod(mwc, setter, aspectIndex) : null;

            TypeDefinition bindingType;
            CreatePropertyBindingClass(mwc, property, aspectIndex, originalGetter, originalSetter, out bindingType);

            FieldReference aspectField;
            CreateAspectCacheField(mwc, property, aspectType, aspectIndex, out aspectField);

            if (getter != null)
                WeaveMethod(mwc, property, getter, aspectType, aspectField, bindingType);
            if (setter != null)
                WeaveMethod(mwc, property, setter, aspectType, aspectField, bindingType);
        }

        private static void WeaveMethod(
            ModuleWeavingContext mwc,
            PropertyDefinition property,
            MethodDefinition method,
            TypeDefinition aspectType,
            FieldReference aspectField,
            TypeDefinition bindingType)
        {
            Debug.Assert(method.IsGetter || method.IsSetter);

            // Clear the target method body as it needs entirely new code
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
            
            FieldReference valueField;
            VariableDefinition iaVariable;
            WritePiaInit(mwc, method, insc.Count, property, argumentsVariable, bindingType, out iaVariable, out valueField);
            
            if (method.IsSetter)
            {
                Debug.Assert(method.Parameters.Count >= 1);

                insc.Add(Ins.Create(OpCodes.Ldloc, iaVariable));
                insc.Add(Ins.Create(OpCodes.Ldarg, method.Parameters.Last()));
                insc.Add(Ins.Create(OpCodes.Stfld, valueField));
            }

            MethodReference adviceBase = method.IsGetter
                ? mwc.Spinner.IPropertyInterceptionAspect_OnGetValue
                : mwc.Spinner.IPropertyInterceptionAspect_OnSetValue;

            WriteCallAdvice(mwc, method, insc.Count, adviceBase, aspectType, aspectField, iaVariable);

            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(mwc, method, insc.Count, argumentsVariable, false, true);
            
            if (method.IsGetter)
            {
                insc.Add(Ins.Create(OpCodes.Ldloc, iaVariable));
                insc.Add(Ins.Create(OpCodes.Ldfld, valueField));
            }

            insc.Add(Ins.Create(OpCodes.Ret));

            // Fix labels and optimize
            
            method.Body.RemoveNops();
            method.Body.OptimizeMacros();
        }

        private static void CreatePropertyBindingClass(
            ModuleWeavingContext mwc,
            PropertyDefinition property,
            int aspectIndex,
            MethodReference originalGetter,
            MethodReference originalSetter,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = property.Module;

            string name = NameGenerator.MakePropertyBindingName(property.Name, aspectIndex);
            TypeReference baseType = mwc.SafeImport(mwc.Spinner.PropertyBindingT1).MakeGenericInstanceType(property.PropertyType);

            CreateBindingClass(mwc, property.DeclaringType, baseType, name, out bindingTypeDef);

            // Override the GetValue method
            {
                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(GetValueMethodName, mattrs, property.PropertyType);

                bindingTypeDef.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
                TypeReference argumentsBaseType = mwc.SafeImport(mwc.Spinner.Arguments);

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, argumentsBaseType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                if (property.GetMethod != null)
                {
                    GenericInstanceType argumentContainerType;
                    FieldReference[] argumentContainerFields;
                    GetArgumentContainerInfo(mwc,
                                             property.GetMethod,
                                             out argumentContainerType,
                                             out argumentContainerFields);

                    // Case the arguments container from its base type to the generic instance type
                    VariableDefinition argsContainer = null;
                    if (property.GetMethod.Parameters.Count != 0)
                    {
                        argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                        bil.Emit(OpCodes.Ldarg_2);
                        bil.Emit(OpCodes.Castclass, argumentContainerType);
                        bil.Emit(OpCodes.Stloc, argsContainer);
                    }

                    // Load the instance for the method call
                    if (!property.GetMethod.IsStatic)
                    {
                        // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                        bil.Emit(OpCodes.Ldarg_1);
                        bil.Emit(OpCodes.Ldind_Ref);
                        bil.Emit(property.GetMethod.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 property.GetMethod.DeclaringType);
                    }

                    // Load arguments or addresses directly from the arguments container
                    for (int i = 0; i < property.GetMethod.Parameters.Count; i++)
                    {
                        bool byRef = property.GetMethod.Parameters[i].ParameterType.IsByReference;

                        bil.Emit(OpCodes.Ldloc, argsContainer);
                        bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
                    }

                    if (property.GetMethod.IsStatic || property.GetMethod.DeclaringType.IsValueType)
                        bil.Emit(OpCodes.Call, originalGetter);
                    else
                        bil.Emit(OpCodes.Callvirt, originalGetter);
                }
                else
                {
                    if (property.PropertyType.IsValueType)
                    {
                        VariableDefinition returnVar = bil.Body.AddVariableDefinition(property.PropertyType);

                        bil.Emit(OpCodes.Ldloca, returnVar);
                        bil.Emit(OpCodes.Initobj, property.PropertyType);
                        bil.Emit(OpCodes.Ldloc, returnVar);
                    }
                    else
                    {
                        bil.Emit(OpCodes.Ldnull);
                    }
                }

                bil.Emit(OpCodes.Ret);
            }

            // Override the SetValue Method
            {
                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition(SetValueMethodName, mattrs, module.TypeSystem.Void);

                bindingTypeDef.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
                TypeReference argumentsBaseType = mwc.SafeImport(mwc.Spinner.Arguments);

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, argumentsBaseType));
                bmethod.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                    property.PropertyType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                if (property.SetMethod != null)
                {
                    Debug.Assert(property.SetMethod.Parameters.Count >= 1);

                    GenericInstanceType argumentContainerType;
                    FieldReference[] argumentContainerFields;
                    GetArgumentContainerInfo(mwc,
                                             property.SetMethod,
                                             out argumentContainerType,
                                             out argumentContainerFields);

                    // Case the arguments container from its base type to the generic instance type
                    VariableDefinition argsContainer = null;
                    if (property.SetMethod.Parameters.Count != 1)
                    {
                        argsContainer = bil.Body.AddVariableDefinition(argumentContainerType);

                        bil.Emit(OpCodes.Ldarg_2);
                        bil.Emit(OpCodes.Castclass, argumentContainerType);
                        bil.Emit(OpCodes.Stloc, argsContainer);
                    }

                    // Load the instance for the method call
                    if (!property.SetMethod.IsStatic)
                    {
                        // Must use unbox instead of unbox.any here so that the call is made on the value inside the box.
                        bil.Emit(OpCodes.Ldarg_1);
                        bil.Emit(OpCodes.Ldind_Ref);
                        bil.Emit(property.SetMethod.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                                 property.SetMethod.DeclaringType);
                    }

                    // Load arguments or addresses directly from the arguments container
                    for (int i = 0; i < property.SetMethod.Parameters.Count - 1; i++)
                    {
                        bool byRef = property.SetMethod.Parameters[i].ParameterType.IsByReference;

                        bil.Emit(OpCodes.Ldloc, argsContainer);
                        bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentContainerFields[i]);
                    }

                    // Load new property value
                    bil.Emit(OpCodes.Ldarg_3);

                    if (property.SetMethod.IsStatic || property.SetMethod.DeclaringType.IsValueType)
                        bil.Emit(OpCodes.Call, originalSetter);
                    else
                        bil.Emit(OpCodes.Callvirt, originalSetter);
                }

                bil.Emit(OpCodes.Ret);
            }
        }

        /// <summary>
        /// Writes the PropertyInterceptionArgs initialization.
        /// </summary>
        private static void WritePiaInit(
            ModuleWeavingContext mwc,
            MethodDefinition method,
            int offset,
            PropertyDefinition property,
            VariableDefinition argumentsVariable,
            TypeDefinition bindingType,
            out VariableDefinition iaVariable,
            out FieldReference valueField)
        {
            TypeDefinition piaTypeDef = mwc.Spinner.BoundPropertyInterceptionArgsT1;
            GenericInstanceType genericPiaType = mwc.SafeImport(piaTypeDef).MakeGenericInstanceType(property.PropertyType);
            TypeReference piaType = genericPiaType;

            MethodDefinition constructorDef = mwc.Spinner.BoundPropertyInterceptionArgsT1_ctor;
            MethodReference constructor = mwc.SafeImport(constructorDef).WithGenericDeclaringType(genericPiaType);

            FieldDefinition valueFieldDef = mwc.Spinner.BoundPropertyInterceptionArgsT1_TypedValue;
            valueField = mwc.SafeImport(valueFieldDef).WithGenericDeclaringType(genericPiaType);

            iaVariable = method.Body.AddVariableDefinition(piaType);

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

            insc.Add(Ins.Create(OpCodes.Ldsfld, bindingType.Fields.Single(f => f.Name == BindingInstanceFieldName)));

            insc.Add(Ins.Create(OpCodes.Newobj, constructor));
            insc.Add(Ins.Create(OpCodes.Stloc, iaVariable));

            method.Body.InsertInstructions(offset, insc);
        }
    }
}