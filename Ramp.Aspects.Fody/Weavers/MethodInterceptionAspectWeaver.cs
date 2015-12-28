using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Ramp.Aspects.Fody.Utilities;

namespace Ramp.Aspects.Fody.Weavers
{
    /// <summary>
    /// Applies the method interception aspect to a method.
    /// </summary>
    internal class MethodInterceptionAspectWeaver : AspectWeaver
    {
        internal static void Weave(
            ModuleDefinition alm,
            MethodDefinition method,
            TypeDefinition aspectType,
            int aspectIndex)
        {

            string originalName = ExtractOriginalName(method.Name);

            string cacheFieldName = $"<{originalName}>z__CachedAspect" + aspectIndex;
            string bindingClassName = $"<{originalName}>z__MethodBinding" + aspectIndex;
            
            MethodDefinition original = DuplicateOriginalMethod(method, aspectIndex);
            
            TypeDefinition bindingType;
            CreateMethodBindingClass(method, alm, bindingClassName, original, out bindingType);

            FieldReference aspectField;
            CreateAspectCacheField(method.DeclaringType, aspectType, cacheFieldName, out aspectField);

            // Clear the target method body as it needs entirely new code

            method.Body.InitLocals = false;
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();

            ILProcessor il = method.Body.GetILProcessor();
            var lp = new LabelProcessor(il);
            
            //WriteOutArgumentInit(il);

            VariableDefinition argumentsVariable;
            WriteArgumentContainerInit(method, alm, il, out argumentsVariable);

            WriteCopyArgumentsToContainer(method, alm, il, argumentsVariable, true);
            
            WriteAspectInit(method, aspectType, aspectField, il, lp);

            WriteBindingInit(il, lp, bindingType);
            
            FieldReference valueField;
            VariableDefinition iaVariable;
            WriteMiaInit(method, alm, il, argumentsVariable, bindingType, out iaVariable, out valueField);

            WriteCallAdvice("OnInvoke", method, aspectType, il, aspectField, iaVariable);
            
            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(method, alm, il, argumentsVariable, false, true);

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
            MethodDefinition method,
            ModuleDefinition aspectLibraryModule,
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
                TypeDefinition miaTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs");
                miaType = module.Import(miaTypeDef);

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef);

                returnValueField = null;
            }
            else
            {
                TypeDefinition miaTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs`1");
                GenericInstanceType genericMiaType = module.Import(miaTypeDef).MakeGenericInstanceType(method.ReturnType);
                miaType = genericMiaType;

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef).WithGenericDeclaringType(genericMiaType);

                FieldDefinition returnValueFieldDef = miaTypeDef.Fields.Single(f => f.Name == "TypedReturnValue");
                returnValueField = module.Import(returnValueFieldDef).WithGenericDeclaringType(genericMiaType);
            }

            miaVariable = new VariableDefinition("mia", miaType);
            il.Body.Variables.Add(miaVariable);

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
            il.Emit(OpCodes.Stloc, miaVariable);
        }

        protected static void WriteCallAdvice(
            string name,
            MethodDefinition method,
            TypeDefinition aspectType,
            ILProcessor il,
            FieldReference aspectField,
            VariableDefinition iaVariable)
        {
            MethodDefinition adviceDef = aspectType.Methods.Single(m => m.Name == name);
            MethodReference advice = method.Module.Import(adviceDef);

            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, iaVariable);
            il.Emit(OpCodes.Callvirt, advice);
        }

        /// <summary>
        /// 
        /// </summary>
        protected static void WriteBindingInit(ILProcessor il, LabelProcessor lp, TypeDefinition bindingType)
        {
            // Initialize the binding instance
            FieldReference instanceField = bindingType.Fields.Single(f => f.Name == "Instance");
            MethodReference constructor = bindingType.Methods.Single(f => f.IsConstructor && !f.IsStatic);

            Label notNullLabel = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Brtrue, notNullLabel.Instruction);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stsfld, instanceField);
            lp.MarkLabel(notNullLabel);
        }

        protected static void CreateMethodBindingClass(
            MethodDefinition method,
            ModuleDefinition aspectLibraryModule,
            string bindingClassName,
            MethodReference original,
            out TypeDefinition bindingTypeDef)
        {
            ModuleDefinition module = method.Module;

            TypeReference baseType;

            if (method.ReturnType == module.TypeSystem.Void)
            {
                baseType = module.Import(aspectLibraryModule.GetType("Ramp.Aspects.Internal.MethodBinding"));
            }
            else
            {
                TypeDefinition baseTypeDef = aspectLibraryModule.GetType("Ramp.Aspects.Internal.MethodBinding`1");
                baseType = module.Import(baseTypeDef).MakeGenericInstanceType(method.ReturnType);
            }

            var tattrs = TypeAttributes.NestedPrivate |
                         TypeAttributes.Class |
                         TypeAttributes.Sealed;

            bindingTypeDef = new TypeDefinition(null, bindingClassName, tattrs, baseType)
            {
                DeclaringType = method.DeclaringType
            };

            method.DeclaringType.NestedTypes.Add(bindingTypeDef);

            MethodDefinition constructorDef = MakeDefaultConstructor(bindingTypeDef);

            bindingTypeDef.Methods.Add(constructorDef);

            // Add the static instance field

            var instanceAttrs = FieldAttributes.Public | FieldAttributes.Static;
            var instanceField = new FieldDefinition("Instance", instanceAttrs, bindingTypeDef);

            bindingTypeDef.Fields.Add(instanceField);

            // Override the invoke method

            var invokeAttrs = MethodAttributes.Public |
                              MethodAttributes.Virtual |
                              MethodAttributes.Final |
                              MethodAttributes.HideBySig |
                              MethodAttributes.ReuseSlot;

            var invokeMethod = new MethodDefinition("Invoke", invokeAttrs, method.ReturnType);

            bindingTypeDef.Methods.Add(invokeMethod);

            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = module.Import(aspectLibraryModule.GetType("Ramp.Aspects.Arguments"));

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            ILProcessor bil = invokeMethod.Body.GetILProcessor();

            GenericInstanceType argumentContainerType;
            FieldReference[] argumentContainerFields;
            GetArgumentContainerInfo(method, aspectLibraryModule, out argumentContainerType, out argumentContainerFields);

            // Case the arguments container from its base type to the generic instance type
            VariableDefinition argsContainer = null;
            if (method.Parameters.Count != 0)
            {
                argsContainer = new VariableDefinition("castedArgs", argumentContainerType);
                bil.Body.Variables.Add(argsContainer);
                bil.Body.InitLocals = true;

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

        protected static MethodDefinition MakeDefaultConstructor(TypeDefinition type)
        {
            TypeDefinition baseTypeDef = type.BaseType.Resolve();
            MethodDefinition baseCtorDef = baseTypeDef.Methods.Single(m => m.HasThis && m.Parameters.Count == 0);

            MethodReference baseCtor;
            if (type.BaseType.IsGenericInstance)
            {
                var baseType = (GenericInstanceType) type.BaseType;

                baseCtor = type.Module.Import(baseCtorDef).WithGenericDeclaringType(baseType);
            }
            else
            {
                baseCtor = type.Module.Import(baseCtorDef);
            }

            var attrs = MethodAttributes.Public |
                        MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName |
                        MethodAttributes.RTSpecialName;

            var method = new MethodDefinition(".ctor", attrs, type.Module.TypeSystem.Void);

            Collection<Instruction> i = method.Body.Instructions;
            i.Add(Instruction.Create(OpCodes.Ldarg_0));
            i.Add(Instruction.Create(OpCodes.Call, baseCtor));
            i.Add(Instruction.Create(OpCodes.Ret));

            return method;
        }
    }
}
