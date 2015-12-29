using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Ramp.Aspects.Fody.Utilities
{
    /// <summary>
    /// Supports label writing with ILProcessor. Call Finish() to remove no-ops used for labeling.
    /// </summary>
    internal class LabelProcessor
    {
        private readonly Collection<Instruction> _instructions;
        private readonly List<Label> _labels = new List<Label>();

        internal LabelProcessor(ILProcessor il)
        {
            _instructions = il.Body.Instructions;
        }

        internal LabelProcessor(Collection<Instruction> instructions)
        {
            _instructions = instructions;
        }

        public Label DefineLabel()
        {
            var l = new Label(Instruction.Create(OpCodes.Nop));
            _labels.Add(l);
            return l;
        }

        public void MarkLabel(Label label)
        {
            if (label.Marked)
                throw new InvalidOperationException();
            _instructions.Add(label.Instruction);
            label.Marked = true;
        }

        /// <summary>
        /// Removes no-ops that were used for labeling.
        /// </summary>
        public void Finish()
        {
            if (_labels.Count == 0)
                return;

            Collection<Instruction> instructions = _instructions;
            List<Instruction> breaks = new List<Instruction>();
            Dictionary<Instruction, Instruction> newInstructions = new Dictionary<Instruction, Instruction>();
            int last = instructions.Count - 1;

            // Find break that reference a label, and instructions that follow the no-ops
            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction ins = instructions[i];
                
                if (ins.OpCode.OperandType == OperandType.InlineBrTarget || ins.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                {
                    Label label = GetLabelForInstruction((Instruction) ins.Operand);
                    if (label == null)
                        continue;

                    breaks.Add(ins);
                }
                else if (ins.OpCode == OpCodes.Nop)
                {
                    Label label = GetLabelForInstruction(ins);
                    if (label == null)
                        continue;

                    if (i == last)
                        throw new InvalidOperationException("marked label at the end of instruction list; missing ret?");
                    
                    newInstructions.Add(ins, instructions[i + 1]);
                    
                    instructions.RemoveAt(i);
                    i--;
                }
            }

            // Update breaks to point to instructions that follow the no-ops
            foreach (Instruction ins in breaks)
                ins.Operand = newInstructions[(Instruction) ins.Operand];

            _labels.Clear();
        }

        private Label GetLabelForInstruction(Instruction instruction)
        {
            return _labels.Find(l => l.Instruction == instruction);
        }
    }

    internal sealed class Label
    {
        internal readonly Instruction Instruction;
        internal bool Marked;

        internal Label(Instruction i)
        {
            Instruction = i;
        }

        public static implicit operator Instruction(Label label)
        {
            return label?.Instruction;
        }
    }
}
