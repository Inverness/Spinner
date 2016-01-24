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
        /// Loads a variable, instance field, or null
        /// </summary>
        internal static void EmitLoadVarOrField(
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
    }
}