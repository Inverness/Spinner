using System.Collections.Generic;
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
        private static readonly Dictionary<string, OpCode> s_ldindOpcodes = new Dictionary<string, OpCode>
        {
            { nameof(System.SByte), OpCodes.Ldind_I1 },
            { nameof(System.Byte), OpCodes.Ldind_U1 },
            { nameof(System.Int16), OpCodes.Ldind_I2 },
            { nameof(System.UInt16), OpCodes.Ldind_U2 },
            { nameof(System.Int32), OpCodes.Ldind_I4 },
            { nameof(System.UInt32), OpCodes.Ldind_U4 },
            { nameof(System.Int64), OpCodes.Ldind_I8 },
            { nameof(System.Single), OpCodes.Ldind_R4 },
            { nameof(System.Double), OpCodes.Ldind_R8 },
        };

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
                TypeReference et = type.GetElementType();
                if (et.IsValueType)
                {
                    OpCode sc;
                    if (et.Namespace == "System" && s_ldindOpcodes.TryGetValue(et.Name, out sc))
                    {
                        il.Emit(sc);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldobj, et);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldind_Ref);
                }
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