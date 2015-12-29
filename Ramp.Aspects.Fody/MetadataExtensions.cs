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
            int last = instructions.Count - 1;

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
                if (newInstructions.TryGetValue(eh.FilterStart, out nins))
                    eh.FilterStart = nins;
                if (newInstructions.TryGetValue(eh.HandlerStart, out nins))
                    eh.HandlerStart = nins;
                if (newInstructions.TryGetValue(eh.HandlerEnd, out nins))
                    eh.HandlerEnd = nins;
            }
        }
    }
}
