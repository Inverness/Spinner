using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers
{
    /// <summary>
    /// Extensions specific to aspect weaving.
    /// </summary>
    internal static class ILProcessorExtensions
    {
        /// <summary>
        /// Emits advice args load from a var, state machine field, or null if neither is provided.
        /// </summary>
        internal static void EmitLoadAdviceArgs(
            this ILProcessorEx il,
            VariableDefinition varOpt,
            FieldReference fieldOpt)
        {
            Debug.Assert(varOpt == null || fieldOpt == null);

            if (fieldOpt != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldOpt);
            }
            else if (varOpt != null)
            {
                il.Emit(OpCodes.Ldloc, varOpt);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        /// <summary>
        /// Emits box if the type is a value type.
        /// </summary>
        internal static void EmitValueTypeBox(this ILProcessorEx il, TypeReference type)
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