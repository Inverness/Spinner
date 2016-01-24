using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Spinner.Fody
{
    internal static class MethodBodyExtensions
    {
        /// <summary>
        /// Replaces all occurances of an instruction as an operand and in exception handlers.
        /// </summary>
        internal static void ReplaceInstructionOperands(
            this MethodBody self,
            Instruction oldValue,
            Instruction newValue,
            int excludeStart,
            int excludeCount)
        {
            int excludeEnd = excludeStart + excludeCount;
            for (int i = 0; i < self.Instructions.Count; i++)
            {
                if (i >= excludeStart && i < excludeEnd)
                    continue;

                Instruction ins = self.Instructions[i];
                OperandType ot = ins.OpCode.OperandType;
                if (ot == OperandType.InlineBrTarget || ot == OperandType.ShortInlineBrTarget)
                {
                    if (ReferenceEquals(ins.Operand, oldValue))
                        ins.Operand = newValue;
                }
                else if (ot == OperandType.InlineSwitch)
                {
                    var operand = (Instruction[]) ins.Operand;
                    for (int s = 0; s < operand.Length; s++)
                        if (ReferenceEquals(operand[s], oldValue))
                            operand[s] = newValue;
                }
            }

            if (!self.HasExceptionHandlers)
                return;

            // Update exception handlers since they point to instructions

            foreach (ExceptionHandler eh in self.ExceptionHandlers)
            {
                int index;
                if (eh.TryStart == oldValue &&
                    ((index = self.Instructions.IndexOf(eh.TryStart)) < excludeStart || index >= excludeEnd))
                    eh.TryStart = newValue;
                if (eh.TryEnd == oldValue &&
                    ((index = self.Instructions.IndexOf(eh.TryEnd)) < excludeStart || index >= excludeEnd))
                    eh.TryEnd = newValue;
                if (eh.HandlerStart == oldValue &&
                    ((index = self.Instructions.IndexOf(eh.HandlerStart)) < excludeStart || index >= excludeEnd))
                    eh.HandlerStart = newValue;
                if (eh.HandlerEnd == oldValue &&
                    ((index = self.Instructions.IndexOf(eh.HandlerEnd)) < excludeStart || index >= excludeEnd))
                    eh.HandlerEnd = newValue;
                if (eh.FilterStart == oldValue &&
                    ((index = self.Instructions.IndexOf(eh.FilterStart)) < excludeStart || index >= excludeEnd))
                    eh.FilterStart = newValue;
            }
        }

        internal static void UpdateOffsets(this MethodBody self)
        {
            int offset = 0;
            foreach (Instruction ins in self.Instructions)
            {
                ins.Offset = offset;
                offset += ins.GetSize();
            }
        }

        internal static int IndexOfOrEnd(this MethodBody self, Instruction ins)
        {
            return ins != null ? self.Instructions.IndexOf(ins) : self.Instructions.Count;
        }

        /// <summary>
        /// Inserts instructions while fixing branch targets for the insertion index.
        /// </summary>
        internal static int InsertInstructions(this MethodBody self, int index, bool fixOffsets, params Instruction[] instructions)
        {
            return InsertInstructions(self, index, fixOffsets, (IEnumerable<Instruction>) instructions);
        }

        /// <summary>
        /// Inserts instructions while fixing branch targets for the insertion index.
        /// </summary>
        internal static int InsertInstructions(this MethodBody self, int index, bool fixOffsets, IEnumerable<Instruction> instructions)
        {
            Collection<Instruction> insc = self.Instructions;

            if (index == insc.Count)
                return insc.AddRange(instructions);

            Instruction oldIns = insc[index];

            int count = insc.InsertRange(index, instructions);

            Instruction newIns = insc[index];

            if (fixOffsets)
                self.ReplaceInstructionOperands(oldIns, newIns, index, count);

            return count;
        }

        internal static void ReplaceInstruction(this MethodBody self, int index, Instruction ins)
        {
            Instruction oldIns = self.Instructions[index];

            self.Instructions[index] = ins;

            self.ReplaceInstructionOperands(oldIns, ins, index, 1);
        }

        /// <summary>
        /// Helper to add a variable definition to the body and set InitLocals to true.
        /// </summary>
        internal static VariableDefinition AddVariableDefinition(this MethodBody self, TypeReference type)
        {
            var def = new VariableDefinition(type);
            self.Variables.Add(def);
            self.InitLocals = true;
            return def;
        }

        /// <summary>
        /// Helper to add a variable definition to the body and set InitLocals to true.
        /// </summary>
        internal static VariableDefinition AddVariableDefinition(this MethodBody self, string name, TypeReference type)
        {
            var def = new VariableDefinition(name, type);
            self.Variables.Add(def);
            self.InitLocals = true;
            return def;
        }

        /// <summary>
        /// Removes all Nop's from the body's instructions and fixes up instruction operands and exception handlers.
        /// </summary>
        internal static void RemoveNops(this MethodBody self, ICollection<Instruction> excluded = null)
        {
            Collection<Instruction> instructions = self.Instructions;

            if (instructions.Count == 0)
                return;

            var breaks = new HashSet<Instruction>();
            var newInstructions = new Dictionary<Instruction, Instruction>();

            // Find break that reference a label, and instructions that follow the no-ops
            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction ins = instructions[i];
                OperandType ot = ins.OpCode.OperandType;

                if (ot == OperandType.InlineBrTarget || ot == OperandType.ShortInlineBrTarget)
                {
                    var operand = (Instruction) ins.Operand;
                    if (operand.OpCode == OpCodes.Nop)
                        breaks.Add(ins);
                }
                else if (ot == OperandType.InlineSwitch)
                {
                    var operand = (Instruction[]) ins.Operand;
                    for (int s = 0; s < operand.Length; s++)
                    {
                        if (operand[s].OpCode == OpCodes.Nop)
                        {
                            breaks.Add(ins);
                            break;
                        }
                    }
                }
                else if (ins.OpCode == OpCodes.Nop)
                {
                    if (excluded != null && excluded.Contains(ins))
                        continue;

                    // Need to skip any following Nop's
                    Instruction next = null;

                    for (int n = i + 1; n < instructions.Count; n++)
                    {
                        Instruction maybeNext = instructions[n];
                        if (maybeNext.OpCode != OpCodes.Nop)
                        {
                            next = maybeNext;
                            break;
                        }
                    }

                    // Next can be null if there are no more Nop's in the instruction list.
                    newInstructions.Add(ins, next);

                    if (next == null)
                        continue;

                    instructions.RemoveAt(i);
                    i--;
                }
            }

            // Update breaks to point to instructions that follow the no-ops
            foreach (Instruction ins in breaks)
            {
                if (ins.OpCode.OperandType == OperandType.InlineSwitch)
                {
                    var operands = (Instruction[]) ins.Operand;
                    for (int i = 0; i < operands.Length; i++)
                    {
                        Instruction next = newInstructions[operands[i]];
                        if (next != null)
                            operands[i] = next;
                    }
                }
                else
                {
                    Instruction next = newInstructions[(Instruction) ins.Operand];
                    if (next != null)
                        ins.Operand = next;
                }
            }

            if (!self.HasExceptionHandlers)
                return;

            // Update exception handlers since they point to instructions

            foreach (ExceptionHandler eh in self.ExceptionHandlers)
            {
                Instruction nins;
                if (newInstructions.TryGetValue(eh.TryStart, out nins))
                    eh.TryStart = nins;
                if (newInstructions.TryGetValue(eh.TryEnd, out nins))
                    eh.TryEnd = nins;
                if (newInstructions.TryGetValue(eh.HandlerStart, out nins))
                    eh.HandlerStart = nins;
                if (newInstructions.TryGetValue(eh.HandlerEnd, out nins))
                    eh.HandlerEnd = nins;
                if (eh.FilterStart != null && newInstructions.TryGetValue(eh.FilterStart, out nins))
                    eh.FilterStart = nins;
            }
        }
    }
}
