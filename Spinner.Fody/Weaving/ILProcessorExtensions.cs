using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weaving
{
    /// <summary>
    /// Extensions specific to aspect weaving.
    /// </summary>
    internal static class ILProcessorExtensions
    {
        /// <summary>
        /// Emit call or callvirt appropriately.
        /// </summary>
        internal static void EmitCall(this ILProcessorEx il, MethodReference method)
        {
            // Callvirt is used for all instance method calls on reference types because it does a null check first.
            MethodDefinition def = method.Resolve();
            if (def.IsVirtual || (!def.IsStatic && !method.DeclaringType.IsValueType))
                il.Emit(OpCodes.Callvirt, method);
            else
                il.Emit(OpCodes.Call, method);
        }

        /// <summary>
        /// If type is a reference type, emit ldobj or ldind_ref depending on whether or not its a value type.
        /// </summary>
        internal static void EmitLoadValueIfRef(this ILProcessorEx il, TypeReference type)
        {
            if (type.IsByReference)
            {
                if (type.GetElementType().IsValueType)
                    il.Emit(OpCodes.Ldobj, type.GetElementType());
                else
                    il.Emit(OpCodes.Ldind_Ref);
            }
        }

        /// <summary>
        /// Emit an instance or static field load.
        /// </summary>
        internal static void EmitLoadFieldOrStaticField(this ILProcessorEx il, FieldDefinition field)
        {
            if (field.IsStatic)
            {
                il.Emit(OpCodes.Ldsfld, field);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
            }
        }

        /// <summary>
        /// Emit ldvirtftn or ldftn depending on the method type.
        /// </summary>
        internal static void EmitLoadPointerOrStaticPointer(this ILProcessorEx il, MethodDefinition method)
        {
            if (method.IsVirtual)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldvirtftn, method);
            }
            else
            {
                il.Emit(OpCodes.Ldftn, method);
            }
        }

        /// <summary>
        /// Loads a variable, instance field, or null.
        /// </summary>
        internal static void EmitLoadLocalOrFieldOrNull(
            this ILProcessorEx il,
            VariableDefinition varOpt,
            FieldReference instanceFieldOpt)
        {
            if (varOpt != null)
            {
                il.Emit(OpCodes.Ldloc, varOpt);
            }
            else if (instanceFieldOpt != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, instanceFieldOpt);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        /// <summary>
        /// Emits box if the type is a value type.
        /// </summary>
        internal static void EmitBoxIfValueType(this ILProcessorEx il, TypeReference type)
        {
            if (type.IsValueType)
                il.Emit(OpCodes.Box, type);
        }

        /// <summary>
        /// Emits castclass or unbox.any depending on whether or not the type is a value type.
        /// </summary>
        internal static void EmitCastOrUnbox(this ILProcessorEx il, TypeReference type)
        {
            il.Emit(type.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, type);
        }
    }
}