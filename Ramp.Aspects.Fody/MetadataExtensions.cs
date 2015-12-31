using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Ramp.Aspects.Fody
{
    internal static class MetadataExtensions
    {
        internal static GenericParameter Clone(this GenericParameter genericParameter, MethodDefinition newOwner)
        {
            return new GenericParameter(newOwner)
            {
                Name = genericParameter.Name,
                Attributes = genericParameter.Attributes
            };
        }

        internal static MethodReference WithGenericDeclaringType(this MethodReference self, GenericInstanceType type)
        {
            Debug.Assert(type.Module == self.Module);

            var reference = new MethodReference(self.Name, self.ReturnType, type)
            {
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention
            };

            foreach (ParameterDefinition parameter in self.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (GenericParameter genericParam in self.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(genericParam.Name, reference));

            return reference;
        }

        internal static FieldReference WithGenericDeclaringType(this FieldReference self, GenericInstanceType type)
        {
            Debug.Assert(type.Module == self.Module);

            return new FieldReference(self.Name, self.FieldType, type);
        }

        internal static IEnumerable<MethodDefinition> GetInheritedMethods(this TypeDefinition type)
        {
            TypeDefinition current = type;
            while (current != null)
            {
                foreach (MethodDefinition m in current.Methods)
                    yield return m;

                current = current.BaseType?.Resolve();
            }
        }

        internal static bool IsSame(this TypeReference self, TypeReference other)
        {
            return self.Name == other.Name && self.Namespace == other.Namespace;
        }

        /// <summary>
        /// Replaces all occurances of an instruction as an operand and in exception handlers.
        /// </summary>
        /// <param name="self"></param>
        /// <param name="oldValue"></param>
        /// <param name="newValue"></param>
        internal static void ReplaceBranchTargets(this MethodBody self, Instruction oldValue, Instruction newValue, int excludeStart, int excludeCount)
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
                if (eh.TryStart == oldValue && ((index = self.Instructions.IndexOf(eh.TryStart)) < excludeStart || index > excludeEnd))
                    eh.TryStart = newValue;
                if (eh.TryEnd == oldValue && ((index = self.Instructions.IndexOf(eh.TryEnd)) < excludeStart || index > excludeEnd))
                    eh.TryEnd = newValue;
                if (eh.HandlerStart == oldValue && ((index = self.Instructions.IndexOf(eh.HandlerStart)) < excludeStart || index > excludeEnd))
                    eh.HandlerStart = newValue;
                if (eh.HandlerEnd == oldValue && ((index = self.Instructions.IndexOf(eh.HandlerEnd)) < excludeStart || index > excludeEnd))
                    eh.HandlerEnd = newValue;
                if (eh.FilterStart == oldValue && ((index = self.Instructions.IndexOf(eh.FilterStart)) < excludeStart || index > excludeEnd))
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

        internal static int InsertInstructions(this MethodBody self, int index, params Instruction[] instructions)
        {
            return InsertInstructions(self, index, (IEnumerable<Instruction>) instructions);
        }

        internal static int InsertInstructions(this MethodBody self, Instruction before, IEnumerable<Instruction> instructions)
        {
            int index = before != null ? self.Instructions.IndexOf(before) : self.Instructions.Count;
            return InsertInstructions(self, index, instructions);
        }

        /// <summary>
        /// Inserts instructions while fixing branch targets for the insertion index
        /// </summary>
        internal static int InsertInstructions(this MethodBody self, int index,  IEnumerable<Instruction> instructions)
        {
            Collection<Instruction> insc = self.Instructions;

            if (index == insc.Count)
                return insc.AddRange(instructions);

            Instruction oldIns = insc[index];

            int count = insc.InsertRange(index, instructions);

            Instruction newIns = insc[index];

            self.ReplaceBranchTargets(oldIns, newIns, index, count);

            return count;
        }

        internal static void ReplaceInstruction(this MethodBody self, int index, Instruction ins)
        {
            Instruction oldIns = self.Instructions[index];

            self.Instructions[index] = ins;

            self.ReplaceBranchTargets(oldIns, ins, index, 1);
        }

        internal static VariableDefinition AddVariableDefinition(this MethodBody self, TypeReference type)
        {
            var def = new VariableDefinition(type);
            self.Variables.Add(def);
            self.InitLocals = true;
            return def;
        }

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
        /// <param name="self"></param>
        internal static void RemoveNops(this MethodBody self)
        {
            Collection<Instruction> instructions = self.Instructions;

            if (instructions.Count == 0)
                return;

            HashSet<Instruction> breaks = new HashSet<Instruction>();
            Dictionary<Instruction, Instruction> newInstructions = new Dictionary<Instruction, Instruction>();

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
                else if (ins.OpCode == OpCodes.Nop)
                {
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
                Instruction next = newInstructions[(Instruction) ins.Operand];
                if (next != null)
                    ins.Operand = next;
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
