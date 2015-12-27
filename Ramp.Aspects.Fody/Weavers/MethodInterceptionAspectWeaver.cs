using System;
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
    internal sealed class MethodInterceptionAspectWeaver : AspectWeaver
    {
        internal MethodInterceptionAspectWeaver(
            ModuleDefinition aspectLibraryModule,
            CacheClassBuilder ccb,
            MethodDefinition method,
            TypeDefinition aspectType)
            : base(aspectLibraryModule, ccb, method, aspectType)
        {
        }

        internal void Weave()
        {
            const MethodAttributes preservedAttributes =
                MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.PInvokeImpl |
                MethodAttributes.UnmanagedExport | MethodAttributes.HasSecurity | MethodAttributes.RequireSecObject;

            // Duplicate the target method under a new name: <Name>z__OriginalMethod

            string originalName = $"<{ExtractOriginalName(Method.Name)}>z__OriginalMethod" + Method.DeclaringType.Methods.Count;

            MethodAttributes originalAttributes = Method.Attributes & preservedAttributes |
                                                  MethodAttributes.Private;

            var original = new MethodDefinition(originalName, originalAttributes, Method.ReturnType);
            
            original.Parameters.AddRange(Method.Parameters);
            original.GenericParameters.AddRange(Method.GenericParameters.Select(p => p.Clone(original)));
            original.ImplAttributes = Method.ImplAttributes;
            original.SemanticsAttributes = Method.SemanticsAttributes;

            if (Method.IsPInvokeImpl)
            {
                original.PInvokeInfo = Method.PInvokeInfo;
                Method.PInvokeInfo = null;
                Method.IsPreserveSig = false;
                Method.IsPInvokeImpl = false;
            }
            else
            {
                original.Body.InitLocals = Method.Body.InitLocals;
                original.Body.Instructions.AddRange(Method.Body.Instructions);
                original.Body.Variables.AddRange(Method.Body.Variables);
                original.Body.ExceptionHandlers.AddRange(Method.Body.ExceptionHandlers);
            }

            Method.DeclaringType.Methods.Add(original);

            // Clear the target method body as it needs entirely new code

            Method.Body.InitLocals = true;
            Method.Body.Instructions.Clear();
            Method.Body.Variables.Clear();
            Method.Body.ExceptionHandlers.Clear();

            ILProcessor il = Method.Body.GetILProcessor();
            var lp = new LabelProcessor(il);
            
            //WriteOutArgumentInit(il);

            VariableDefinition argumentsVariable;
            FieldReference[] argumentFields;
            WriteArgumentContainerInit(il, out argumentsVariable, out argumentFields);

            WriteCopyArgumentsToContainer(il, argumentsVariable, argumentFields, true);
            
            FieldReference aspectField;
            WriteAspectInit(il, lp, out aspectField);

            FieldReference bindingField;
            WriteBindingInit(il, lp, original, argumentFields, out bindingField);

            FieldReference returnValueField;
            VariableDefinition miaVariable;
            WriteMiaInit(il, argumentsVariable, bindingField, out miaVariable, out returnValueField);

            WriteCallOnInvoke(il, aspectField, miaVariable);

            // Copy out and ref arguments from container
            WriteCopyArgumentsFromContainer(il, argumentsVariable, argumentFields, false, true);

            WriteReturn(il, miaVariable, returnValueField);

            // Fix labels and optimize

            lp.Finish();
            il.Body.OptimizeMacros();
        }
        
        /// <summary>
        /// Writes the MethodInterceptionArgs initialization.
        /// </summary>
        /// <param name="il">An IL processor</param>
        /// <param name="argumentsVariable">A local variable containing the initialized arguments container.</param>
        /// <param name="bindingField">A reference to the field containing the MethodBinding implementation.</param>
        /// <param name="miaVariable">A local variable containing the initialized MethodInterceptionArgs.</param>
        /// <param name="returnValueField">A reference the typed field on the MIA that holds the return value.</param>
        private void WriteMiaInit(ILProcessor il, VariableDefinition argumentsVariable, FieldReference bindingField, out VariableDefinition miaVariable, out FieldReference returnValueField)
        {
            ModuleDefinition module = Method.Module;
            
            TypeReference miaType;
            MethodReference constructor;
            
            if (ReturnsVoid)
            {
                TypeDefinition miaTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs");
                miaType = module.Import(miaTypeDef);

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef);

                returnValueField = null;
            }
            else
            {
                TypeDefinition miaTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs`1");
                GenericInstanceType genericMiaType = module.Import(miaTypeDef).MakeGenericInstanceType(Method.ReturnType);
                miaType = genericMiaType;

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef).WithGenericDeclaringType(genericMiaType);

                FieldDefinition returnValueFieldDef = miaTypeDef.Fields.Single(f => f.Name == "TypedReturnValue");
                returnValueField = module.Import(returnValueFieldDef).WithGenericDeclaringType(genericMiaType);
            }

            miaVariable = new VariableDefinition("mia", miaType);
            il.Body.Variables.Add(miaVariable);

            if (Method.IsStatic)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (Method.DeclaringType.IsValueType)
                {
                    il.Emit(OpCodes.Ldobj, Method.DeclaringType);
                    il.Emit(OpCodes.Box, Method.DeclaringType);
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
            if (bindingField == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, bindingField);
            }

            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, miaVariable);
        }

        /// <summary>
        /// Write aspect initialization.
        /// </summary>
        /// <param name="il">An IL processor</param>
        /// <param name="lp">A label processor</param>
        /// <param name="aspectField">A reference to the static field containing the aspect</param>
        private void WriteAspectInit(ILProcessor il, LabelProcessor lp, out FieldReference aspectField)
        {
            var fattrs = FieldAttributes.Private |
                         FieldAttributes.Static;

            string fname = $"<{ExtractOriginalName(Method.Name)}>z__CachedAspect" + Method.DeclaringType.Fields.Count;

            var aspectFieldDef = new FieldDefinition(fname, fattrs, Method.Module.Import(AspectType));
            Method.DeclaringType.Fields.Add(aspectFieldDef);

            aspectField = aspectFieldDef;

            // NOTE: Aspect type can't be generic since its declared by an attribute
            MethodDefinition ctorDef = AspectType.GetConstructors().Single(m => m.HasThis && m.Parameters.Count == 0);
            MethodReference ctor = Method.Module.Import(ctorDef);
            
            Label notNullLabel = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Brtrue, notNullLabel.Instruction);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stsfld, aspectField);
            lp.MarkLabel(notNullLabel);
        }

        /// <summary>
        /// Write a call to OnInvoke().
        /// </summary>
        /// <param name="il">An IL processor</param>
        /// <param name="aspectField">The field containing the aspect instance</param>
        /// <param name="miaVariable">The MethodInterceptionArgs to pass to OnInvoke()</param>
        private void WriteCallOnInvoke(ILProcessor il, FieldReference aspectField, VariableDefinition miaVariable)
        {
            MethodDefinition onInvokeMethodDef = AspectType.Methods.Single(m => m.Name == "OnInvoke");
            MethodReference onInvokeMethod = Method.Module.Import(onInvokeMethodDef);

            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, miaVariable);
            il.Emit(OpCodes.Callvirt, onInvokeMethod);
        }

        /// <summary>
        /// Write the return statement that uses the TypedReturnValue from the MethodInterceptionArgs if applicable.
        /// </summary>
        /// <param name="il"></param>
        /// <param name="miaVariable"></param>
        /// <param name="returnValueField"></param>
        private void WriteReturn(ILProcessor il, VariableDefinition miaVariable, FieldReference returnValueField)
        {
            if (returnValueField != null)
            {
                il.Emit(OpCodes.Ldloc, miaVariable);
                il.Emit(OpCodes.Ldfld, returnValueField);
            }
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Generate the MethodBinding implementation and write its initialization.
        /// </summary>
        /// <param name="il"></param>
        /// <param name="lp"></param>
        /// <param name="original"></param>
        /// <param name="argumentFields"></param>
        /// <param name="bindingField"></param>
        private void WriteBindingInit(
            ILProcessor il,
            LabelProcessor lp,
            MethodReference original,
            FieldReference[] argumentFields,
            out FieldReference bindingField)
        {
            ModuleDefinition module = Method.Module;

            TypeReference baseType;

            if (ReturnsVoid)
            {
                baseType = module.Import(AspectLibraryModule.GetType("Ramp.Aspects.Internal.MethodBinding"));
            }
            else
            {
                TypeDefinition baseTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.MethodBinding`1");
                baseType = module.Import(baseTypeDef).MakeGenericInstanceType(Method.ReturnType);
            }

            string className = $"<{ExtractOriginalName(Method.Name)}>z__MethodBinding" +
                               Method.DeclaringType.NestedTypes.Count;

            var tattrs = TypeAttributes.NestedPrivate |
                         TypeAttributes.Class |
                         TypeAttributes.Sealed;

            var bindingTypeDef = new TypeDefinition(null, className, tattrs, baseType)
            {
                DeclaringType = Method.DeclaringType
            };

            Method.DeclaringType.NestedTypes.Add(bindingTypeDef);

            MethodDefinition constructor = MakeDefaultConstructor(bindingTypeDef);

            bindingTypeDef.Methods.Add(constructor);

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

            var invokeMethod = new MethodDefinition("Invoke", invokeAttrs, Method.ReturnType);

            bindingTypeDef.Methods.Add(invokeMethod);

            TypeReference instanceType = module.TypeSystem.Object.MakeByReferenceType();
            TypeReference argumentsBaseType = module.Import(AspectLibraryModule.GetType("Ramp.Aspects.Arguments"));

            invokeMethod.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, instanceType));
            invokeMethod.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, argumentsBaseType));

            ILProcessor bil = invokeMethod.Body.GetILProcessor();

            // Case the arguments container from its base type to the generic instance type
            VariableDefinition argsContainer = null;
            if (Method.Parameters.Count != 0)
            {
                argsContainer = new VariableDefinition("castedArgs", argumentFields[0].DeclaringType);
                bil.Body.Variables.Add(argsContainer);
                bil.Body.InitLocals = true;

                bil.Emit(OpCodes.Ldarg_2);
                bil.Emit(OpCodes.Castclass, argumentFields[0].DeclaringType);
                bil.Emit(OpCodes.Stloc, argsContainer);
            }

            // Load the instance for the method call
            if (!Method.IsStatic)
            {
                if (Method.DeclaringType.IsValueType)
                {
                    bil.Emit(OpCodes.Ldarg_1);
                    bil.Emit(OpCodes.Ldind_Ref);
                    bil.Emit(OpCodes.Unbox, Method.DeclaringType);
                }
                else
                {
                    bil.Emit(OpCodes.Ldarg_1);
                    bil.Emit(OpCodes.Ldind_Ref);
                    bil.Emit(OpCodes.Castclass, Method.DeclaringType);
                }
            }

            // Load arguments or addresses directly from the arguments container
            for (int i = 0; i < Method.Parameters.Count; i++)
            {
                bool byRef = Method.Parameters[i].ParameterType.IsByReference;

                bil.Emit(OpCodes.Ldloc, argsContainer);
                bil.Emit(byRef ? OpCodes.Ldflda : OpCodes.Ldfld, argumentFields[i]);
            }

            if (Method.IsStatic || Method.DeclaringType.IsValueType)
                bil.Emit(OpCodes.Call, original);
            else
                bil.Emit(OpCodes.Callvirt, original);

            bil.Emit(OpCodes.Ret);

            // Initialize the binding instance

            Label notNullLabel = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Brtrue, notNullLabel.Instruction);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stsfld, instanceField);
            lp.MarkLabel(notNullLabel);

            bindingField = instanceField;
        }

        private static MethodDefinition MakeDefaultConstructor(TypeDefinition type)
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

        private static string ExtractOriginalName(string name)
        {
            const StringComparison comparison = StringComparison.InvariantCulture;

            int endBracketIndex;
            if (name.StartsWith("<", comparison) && (endBracketIndex = name.IndexOf(">", comparison)) != -1)
            {
                return name.Substring(1, endBracketIndex - 1);
            }
            else
            {
                return name;
            }
        }
    }
}
