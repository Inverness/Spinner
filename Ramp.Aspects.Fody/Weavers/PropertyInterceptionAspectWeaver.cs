using System.Diagnostics;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Ramp.Aspects.Fody.Utilities;

namespace Ramp.Aspects.Fody.Weavers
{
    internal class PropertyInterceptionAspectWeaver : MethodInterceptionAspectWeaver
    {
        internal static void Weave(
            ModuleDefinition alm,
            PropertyDefinition property,
            TypeDefinition aspectType,
            int aspectIndex)
        {

            string originalName = ExtractOriginalName(property.Name);

            string cacheFieldName = $"<{originalName}>z__CachedAspect" + aspectIndex;
            string bindingClassName = $"<{originalName}>z__PropertyBinding" + aspectIndex;

            MethodDefinition getter = property.GetMethod;
            MethodDefinition setter = property.SetMethod;

            MethodDefinition originalGetter = getter != null ? DuplicateOriginalMethod(getter, aspectIndex) : null;
            MethodDefinition originalSetter = setter != null ? DuplicateOriginalMethod(setter, aspectIndex) : null;

            TypeDefinition bindingType;
            CreatePropertyBindingClass(property, alm, bindingClassName, originalGetter, originalSetter, out bindingType);

            FieldReference aspectField;
            CreateAspectCacheField(property.DeclaringType, aspectType, cacheFieldName, out aspectField);

            if (getter != null)
                WeavePropertyMethod(alm, property, getter, aspectType, aspectField, bindingType);
            if (setter != null)
                WeavePropertyMethod(alm, property, setter, aspectType, aspectField, bindingType);
        }

        private static void WeavePropertyMethod(
            ModuleDefinition alm,
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

            ILProcessor il = method.Body.GetILProcessor();
            var lp = new LabelProcessor(il);

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(method, alm, il, out argumentsVariable);

            WriteCopyArgumentsToContainer(method, alm, il, argumentsVariable, true);
            
            WriteAspectInit(method, aspectType, aspectField, il, lp);

            WriteBindingInit(il, lp, bindingType);
            
            FieldReference valueField;
            VariableDefinition iaVariable;
            WritePiaInit(method, property, alm, il, argumentsVariable, bindingType, out iaVariable, out valueField);
            
            if (method.IsSetter)
            {
                Debug.Assert(method.Parameters.Count >= 1);
                int last = method.Parameters.Count - 1 + (method.IsStatic ? 0 : 1);

                il.Emit(OpCodes.Ldloc, iaVariable);
                il.Emit(OpCodes.Ldarg, last);
                il.Emit(OpCodes.Stfld, valueField);
            }

            string adviceName = method.IsGetter ? "OnGetValue" : "OnSetValue";
            WriteCallAdvice(adviceName, method, aspectType, il, aspectField, iaVariable);

            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(method, alm, il, argumentsVariable, false, true);
            
            if (method.IsGetter)
            {
                il.Emit(OpCodes.Ldloc, iaVariable);
                il.Emit(OpCodes.Ldfld, valueField);
            }

            il.Emit(OpCodes.Ret);

            // Fix labels and optimize

            lp.Finish();
            il.Body.OptimizeMacros();
        }

        protected static void CreatePropertyBindingClass(
            PropertyDefinition property,
            ModuleDefinition alm,
            string bindingClassName,
            MethodReference originalGetter,
            MethodReference originalSetter,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = property.Module;

            TypeDefinition baseTypeDef = alm.GetType("Ramp.Aspects.Internal.PropertyBinding`1");
            TypeReference baseType = module.Import(baseTypeDef).MakeGenericInstanceType(property.PropertyType);

            var tattrs = TypeAttributes.NestedPrivate |
                         TypeAttributes.Class |
                         TypeAttributes.Sealed;

            bindingTypeDef = new TypeDefinition(null, bindingClassName, tattrs, baseType)
            {
                DeclaringType = property.DeclaringType
            };

            property.DeclaringType.NestedTypes.Add(bindingTypeDef);

            MethodDefinition constructorDef = MakeDefaultConstructor(bindingTypeDef);

            bindingTypeDef.Methods.Add(constructorDef);

            // Add the static instance field

            var instanceAttrs = FieldAttributes.Public | FieldAttributes.Static;
            var instanceField = new FieldDefinition("Instance", instanceAttrs, bindingTypeDef);

            bindingTypeDef.Fields.Add(instanceField);

            // Override the GetValue method
            {
                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition("GetValue", mattrs, property.PropertyType);

                bindingTypeDef.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
                TypeReference argumentsBaseType =
                    module.Import(alm.GetType("Ramp.Aspects.Arguments"));

                bmethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
                bmethod.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, argumentsBaseType));

                ILProcessor bil = bmethod.Body.GetILProcessor();

                if (property.GetMethod != null)
                {
                    GenericInstanceType argumentContainerType;
                    FieldReference[] argumentContainerFields;
                    GetArgumentContainerInfo(property.GetMethod, alm, out argumentContainerType, out argumentContainerFields);

                    // Case the arguments container from its base type to the generic instance type
                    VariableDefinition argsContainer = null;
                    if (property.GetMethod.Parameters.Count != 0)
                    {
                        argsContainer = new VariableDefinition("castedIndex", argumentContainerType);
                        bil.Body.Variables.Add(argsContainer);
                        bil.Body.InitLocals = true;

                        bil.Emit(OpCodes.Ldarg_2);
                        bil.Emit(OpCodes.Castclass, argumentContainerType);
                        bil.Emit(OpCodes.Stloc, argsContainer);
                    }

                    // Load the instance for the method call
                    if (!property.GetMethod.IsStatic)
                    {
                        if (property.GetMethod.DeclaringType.IsValueType)
                        {
                            bil.Emit(OpCodes.Ldarg_1);
                            bil.Emit(OpCodes.Ldind_Ref);
                            bil.Emit(OpCodes.Unbox, property.GetMethod.DeclaringType);
                        }
                        else
                        {
                            bil.Emit(OpCodes.Ldarg_1);
                            bil.Emit(OpCodes.Ldind_Ref);
                            bil.Emit(OpCodes.Castclass, property.GetMethod.DeclaringType);
                        }
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

                bil.Emit(OpCodes.Ret);
            }

            // Override the SetValue Method
            {
                var mattrs = MethodAttributes.Public |
                             MethodAttributes.Virtual |
                             MethodAttributes.Final |
                             MethodAttributes.HideBySig |
                             MethodAttributes.ReuseSlot;

                var bmethod = new MethodDefinition("SetValue", mattrs, module.TypeSystem.Void);

                bindingTypeDef.Methods.Add(bmethod);

                TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
                TypeReference argumentsBaseType =
                    module.Import(alm.GetType("Ramp.Aspects.Arguments"));

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
                    GetArgumentContainerInfo(property.SetMethod, alm, out argumentContainerType, out argumentContainerFields);

                    // Case the arguments container from its base type to the generic instance type
                    VariableDefinition argsContainer = null;
                    if (property.SetMethod.Parameters.Count != 1)
                    {
                        argsContainer = new VariableDefinition("castedIndex", argumentContainerType);
                        bil.Body.Variables.Add(argsContainer);
                        bil.Body.InitLocals = true;

                        bil.Emit(OpCodes.Ldarg_2);
                        bil.Emit(OpCodes.Castclass, argumentContainerType);
                        bil.Emit(OpCodes.Stloc, argsContainer);
                    }

                    // Load the instance for the method call
                    if (!property.SetMethod.IsStatic)
                    {
                        if (property.SetMethod.DeclaringType.IsValueType)
                        {
                            bil.Emit(OpCodes.Ldarg_1);
                            bil.Emit(OpCodes.Ldind_Ref);
                            bil.Emit(OpCodes.Unbox, property.SetMethod.DeclaringType);
                        }
                        else
                        {
                            bil.Emit(OpCodes.Ldarg_1);
                            bil.Emit(OpCodes.Ldind_Ref);
                            bil.Emit(OpCodes.Castclass, property.SetMethod.DeclaringType);
                        }
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
        protected static void WritePiaInit(
            MethodDefinition method,
            PropertyDefinition property,
            ModuleDefinition aspectLibraryModule,
            ILProcessor il,
            VariableDefinition argumentsVariable,
            TypeDefinition bindingType,
            out VariableDefinition iaVariable,
            out FieldReference valueField)
        {
            ModuleDefinition module = method.Module;

            TypeDefinition piaTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundPropertyInterceptionArgs`1");
            GenericInstanceType genericPiaType = module.Import(piaTypeDef).MakeGenericInstanceType(property.PropertyType);
            TypeReference piaType = genericPiaType;

            MethodDefinition constructorDef = piaTypeDef.GetConstructors().Single(m => m.HasThis);
            MethodReference constructor = module.Import(constructorDef).WithGenericDeclaringType(genericPiaType);

            FieldDefinition valueFieldDef = piaTypeDef.Fields.Single(f => f.Name == "TypedValue");
            valueField = module.Import(valueFieldDef).WithGenericDeclaringType(genericPiaType);

            iaVariable = new VariableDefinition("pia", piaType);
            il.Body.InitLocals = true;
            il.Body.Variables.Add(iaVariable);

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

            if (argumentsVariable == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, argumentsVariable);
            }

            // the method binding
            if (bindingType == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, bindingType.Fields.Single(f => f.Name == "Instance"));
            }

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, iaVariable);
        }
    }
}