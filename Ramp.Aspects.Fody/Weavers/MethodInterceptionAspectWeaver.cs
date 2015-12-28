using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Ramp.Aspects.Fody.Utilities;

namespace Ramp.Aspects.Fody.Weavers
{
    /// <summary>
    /// Applies the method interception aspect to a method.
    /// </summary>
    internal class MethodInterceptionAspectWeaver : AspectWeaver
    {
        protected const string InvokeMethodName = "Invoke";
        protected const string OnInvokeAdviceName = "OnInvoke";

        internal static void Weave(
            ImportContext ic,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex)
        {
            string originalName = ExtractOriginalName(method.Name);

            string cacheFieldName = $"<{originalName}>z__CachedAspect" + aspectIndex;
            string bindingClassName = $"<{originalName}>z__MethodBinding" + aspectIndex;
            
            MethodDefinition original = DuplicateOriginalMethod(method, aspectIndex);
            
            TypeDefinition bindingType;
            CreateMethodBindingClass(ic, method, bindingClassName, original, out bindingType);

            FieldReference aspectField;
            CreateAspectCacheField(ic, method.DeclaringType, aspectType, cacheFieldName, out aspectField);

            // Clear the target method body as it needs entirely new code

            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            ILProcessor il = method.Body.GetILProcessor();
            var lp = new LabelProcessor(il);
            
            //WriteOutArgumentInit(il);

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(ic, method, il, out argumentsVariable);

            WriteCopyArgumentsToContainer(ic, method, il, argumentsVariable, true);
            
            WriteAspectInit(ic, method, aspectType, aspectField, il, lp);

            WriteBindingInit(il, lp, bindingType);
            
            FieldReference valueField;
            VariableDefinition iaVariable;
            WriteMiaInit(ic, method, il, argumentsVariable, bindingType, out iaVariable, out valueField);

            WriteCallAdvice(ic, OnInvokeAdviceName, aspectType, il, aspectField, iaVariable);
            
            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(ic, method, il, argumentsVariable, false, true);

            if (valueField != null)
            {
                il.Emit(OpCodes.Ldloc, iaVariable);
                il.Emit(OpCodes.Ldfld, valueField);
            }
            il.Emit(OpCodes.Ret);

            // Fix labels and optimize

            lp.Finish();
            il.Body.OptimizeMacros();
        }

        /// <summary>
        /// Writes the MethodInterceptionArgs initialization.
        /// </summary>
        protected static void WriteMiaInit(
            ImportContext ic,
            MethodDefinition method,
            ILProcessor il,
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
                TypeDefinition miaTypeDef = ic.Library.BoundMethodInterceptionArgs;
                miaType = ic.SafeImport(miaTypeDef);

                MethodDefinition constructorDef = ic.Library.BoundMethodInterceptionArgs_ctor;
                constructor = ic.SafeImport(constructorDef);

                returnValueField = null;
            }
            else
            {
                TypeDefinition miaTypeDef = ic.Library.BoundMethodInterceptionArgsT1;
                GenericInstanceType genericMiaType = ic.SafeImport(miaTypeDef).MakeGenericInstanceType(method.ReturnType);
                miaType = genericMiaType;

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = ic.SafeImport(constructorDef).WithGenericDeclaringType(genericMiaType);

                FieldDefinition returnValueFieldDef = ic.Library.BoundMethodInterceptionArgsT1_TypedReturnValue;
                returnValueField = ic.SafeImport(returnValueFieldDef).WithGenericDeclaringType(genericMiaType);
            }

            miaVariable = il.Body.AddVariableDefinition(miaType);

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
                il.Emit(OpCodes.Ldsfld, bindingType.Fields.Single(f => f.Name == BindingInstanceFieldName));
            }

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, miaVariable);
        }

        protected static void CreateMethodBindingClass(
            ImportContext ic,
            MethodDefinition method,
            string bindingClassName,
            MethodReference original,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = method.Module;

            TypeReference baseType;

            if (method.ReturnType == module.TypeSystem.Void)
            {
                baseType = ic.SafeImport(ic.Library.MethodBinding);
            }
            else
            {
                baseType = ic.SafeImport(ic.Library.MethodBindingT1).MakeGenericInstanceType(method.ReturnType);
            }

            var tattrs = TypeAttributes.NestedPrivate |
                         TypeAttributes.Class |
                         TypeAttributes.Sealed;

            bindingTypeDef = new TypeDefinition(null, bindingClassName, tattrs, baseType)
            {
                DeclaringType = method.DeclaringType
            };

            method.DeclaringType.NestedTypes.Add(bindingTypeDef);

            MethodDefinition constructorDef = MakeDefaultConstructor(ic, bindingTypeDef);

            bindingTypeDef.Methods.Add(constructorDef);

            // Add the static instance field

            var instanceAttrs = FieldAttributes.Public | FieldAttributes.Static;
            var instanceField = new FieldDefinition(BindingInstanceFieldName, instanceAttrs, bindingTypeDef);

            bindingTypeDef.Fields.Add(instanceField);

            // Override the invoke method

            var invokeAttrs = MethodAttributes.Public |
                              MethodAttributes.Virtual |
                              MethodAttributes.Final |
                              MethodAttributes.HideBySig |
                              MethodAttributes.ReuseSlot;

            var invokeMethod = new MethodDefinition(InvokeMethodName, invokeAttrs, method.ReturnType);

            bindingTypeDef.Methods.Add(invokeMethod);

            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = ic.SafeImport(ic.Library.ArgumentContainerBase);

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            ILProcessor bil = invokeMethod.Body.GetILProcessor();

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(ic, method, out argumentContainerType, out argumentContainerFields);

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
                if (method.DeclaringType.IsValueType)
                {
                    bil.Emit(OpCodes.Ldarg_1);
                    bil.Emit(OpCodes.Ldind_Ref);
                    bil.Emit(OpCodes.Unbox, method.DeclaringType);
                }
                else
                {
                    bil.Emit(OpCodes.Ldarg_1);
                    bil.Emit(OpCodes.Ldind_Ref);
                    bil.Emit(OpCodes.Castclass, method.DeclaringType);
                }
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
