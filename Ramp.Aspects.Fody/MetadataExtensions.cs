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

        /// <summary>
        /// Replaces all occurances of an instruction as an operand.
        /// </summary>
        /// <param name="self"></param>
        /// <param name="oldValue"></param>
        /// <param name="newValue"></param>
        internal static void ReplaceOperands(this Collection<Instruction> self, Instruction oldValue, Instruction newValue)
        {
            foreach (Instruction instruction in self)
                if (ReferenceEquals(instruction.Operand, oldValue))
                    instruction.Operand = newValue;
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
                if (eh.FilterStart != null && newInstructions.TryGetValue(eh.FilterStart, out nins))
                    eh.FilterStart = nins;
                if (newInstructions.TryGetValue(eh.HandlerStart, out nins))
                    eh.HandlerStart = nins;
                if (newInstructions.TryGetValue(eh.HandlerEnd, out nins))
                    eh.HandlerEnd = nins;
            }
        }
    }
}
