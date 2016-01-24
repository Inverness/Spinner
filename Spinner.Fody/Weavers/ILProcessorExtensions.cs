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
        /// Loads a variable, instance field, or null.
        /// </summary>
        internal static void EmitLoadOrNull(
            this ILProcessorEx il,
            VariableDefinition varOpt,
            FieldReference ifield)
        {
            if (varOpt != null)
            {
                il.Emit(OpCodes.Ldloc, varOpt);
            }
            else if (ifield != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ifield);
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