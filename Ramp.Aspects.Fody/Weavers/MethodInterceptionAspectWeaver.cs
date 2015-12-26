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

            // Duplicate the target method under a new name: <Name>z__Original

            string originalName = $"<{Method.Name}>z__Original";

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

            // Begin writing the new body

            FieldReference aspectField = WriteAspectInit(il, lp);

            VariableDefinition argumentsVariable = WriteArgumentsInit(il);

            VariableDefinition miaVariable = WriteMiaInit(il, argumentsVariable, null);

            WriteCallOnInvoke(il, aspectField, miaVariable);

            WriteReturn(il, null);

            // Fix labels and optimize

            lp.Finish();
            il.Body.OptimizeMacros();
        }

        /// <summary>
        /// Writes code to initialize the MethodInterceptionArgs
        /// </summary>
        /// <param name="il">IL processor</param>
        /// <param name="argumentsVariable">The arguments container</param>
        /// <param name="bindingField"></param>
        /// <returns></returns>
        private VariableDefinition WriteMiaInit(ILProcessor il, VariableDefinition argumentsVariable, FieldReference bindingField)
        {
            ModuleDefinition module = Method.Module;
            TypeSystem typeSystem = module.TypeSystem;
            
            TypeReference miaType;
            MethodReference constructor;
            
            if (Method.ReturnType == typeSystem.Void)
            {
                TypeDefinition miaTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs");
                miaType = module.Import(miaTypeDef);

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef);

            }
            else
            {
                TypeDefinition miaTypeDef = AspectLibraryModule.GetType("Ramp.Aspects.Internal.BoundMethodInterceptionArgs`1");
                miaType = module.Import(miaTypeDef).MakeGenericInstanceType(Method.ReturnType);

                MethodDefinition constructorDef = miaTypeDef.GetConstructors().Single(m => m.HasThis);
                constructor = module.Import(constructorDef.MakeGenericDeclaringType(Method.ReturnType));
            }

            VariableDefinition miaVariable = new VariableDefinition("mia", miaType);
            il.Body.Variables.Add(miaVariable);

            if (Method.IsStatic)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (Method.DeclaringType.IsValueType)
                    il.Emit(OpCodes.Box, typeSystem.Object);
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

            return miaVariable;
        }

        private FieldReference WriteAspectInit(ILProcessor il, LabelProcessor lp)
        {
            FieldDefinition aspectField = CacheClassBuilder.AddAspectCacheField(AspectType);

            MethodDefinition constructorDef = AspectType.GetConstructors().Single(m => m.HasThis && m.Parameters.Count == 0);
            MethodReference constructor = Method.Module.Import(constructorDef);

            Label brTarget = lp.DefineLabel();
            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Brtrue, brTarget.Instruction);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stsfld, aspectField);
            lp.MarkLabel(brTarget);

            return aspectField;
        }

        private void WriteCallOnInvoke(ILProcessor il, FieldReference aspectField, VariableDefinition miaVariable)
        {
            MethodDefinition onInvokeMethodDef = AspectType.Methods.Single(m => m.Name == "OnInvoke");
            MethodReference onInvokeMethod = Method.Module.Import(onInvokeMethodDef);

            il.Emit(OpCodes.Ldsfld, aspectField);
            il.Emit(OpCodes.Ldloc, miaVariable);
            il.Emit(OpCodes.Callvirt, onInvokeMethod);
        }

        private void WriteReturn(ILProcessor il, FieldReference bindingDefinition)
        {
            il.Emit(OpCodes.Ldc_I4_0); // TEMP!
            il.Emit(OpCodes.Ret);
        }
    }
}
