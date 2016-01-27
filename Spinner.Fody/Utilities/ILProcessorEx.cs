using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Spinner.Fody.Utilities
{
    public sealed class ILProcessorEx
    {
        private readonly Collection<Instruction> _instructions;
        //private int _offset;

        public ILProcessorEx()
        {
            _instructions = new Collection<Instruction>();
        }

        public ILProcessorEx(MethodBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            _instructions = body.Instructions;
            //_offset = _instructions.Count;
        }

        public ILProcessorEx(Collection<Instruction> instructions)
        {
            if (instructions == null)
                throw new ArgumentNullException(nameof(instructions));

            _instructions = instructions;
            //_offset = instructions.Count;
        }

        public Collection<Instruction> Instructions => _instructions;

        public int Count => _instructions.Count;

        //public int Offset
        //{
        //    get { return _offset; }

        //    set
        //    {
        //        if (value < 0)
        //            throw new ArgumentOutOfRangeException(nameof(value));
        //        _offset = Math.Min(value, _instructions.Count);
        //    }
        //}

        public Instruction Create(OpCode opcode)
        {
            return Instruction.Create(opcode);
        }

        public Instruction Create(OpCode opcode, TypeReference type)
        {
            return Instruction.Create(opcode, type);
        }

        public Instruction Create(OpCode opcode, CallSite site)
        {
            return Instruction.Create(opcode, site);
        }

        public Instruction Create(OpCode opcode, MethodReference method)
        {
            return Instruction.Create(opcode, method);
        }

        public Instruction Create(OpCode opcode, FieldReference field)
        {
            return Instruction.Create(opcode, field);
        }

        public Instruction Create(OpCode opcode, string value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, sbyte value)
        {
            return Instruction.Create(opcode, value);
        }

        //public Instruction Create(OpCode opcode, byte value)
        //{
        //    if (opcode.OperandType == OperandType.ShortInlineVar)
        //        return Instruction.Create(opcode, _body.Variables[value]);
        //    if (opcode.OperandType == OperandType.ShortInlineArg)
        //        return Instruction.Create(opcode, GetParameter(_body, value));
        //    return Instruction.Create(opcode, value);
        //}

        //public Instruction Create(OpCode opcode, int value)
        //{
        //    if (opcode.OperandType == OperandType.InlineVar)
        //        return Instruction.Create(opcode, _body.Variables[value]);
        //    if (opcode.OperandType == OperandType.InlineArg)
        //        return Instruction.Create(opcode, GetParameter(_body, value));
        //    return Instruction.Create(opcode, value);
        //}

        public Instruction Create(OpCode opcode, long value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, float value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, double value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, Instruction target)
        {
            return Instruction.Create(opcode, target);
        }

        public Instruction Create(OpCode opcode, Instruction[] targets)
        {
            return Instruction.Create(opcode, targets);
        }

        public Instruction Create(OpCode opcode, VariableDefinition variable)
        {
            return Instruction.Create(opcode, variable);
        }

        public Instruction Create(OpCode opcode, ParameterDefinition parameter)
        {
            return Instruction.Create(opcode, parameter);
        }

        public Instruction CreateNop()
        {
            return Instruction.Create(OpCodes.Nop);
        }

        public void Emit(OpCode opcode)
        {
            Append(Create(opcode));
        }

        public void Emit(OpCode opcode, TypeReference type)
        {
            Append(Create(opcode, type));
        }

        public void Emit(OpCode opcode, MethodReference method)
        {
            Append(Create(opcode, method));
        }

        public void Emit(OpCode opcode, CallSite site)
        {
            Append(Create(opcode, site));
        }

        public void Emit(OpCode opcode, FieldReference field)
        {
            Append(Create(opcode, field));
        }

        public void Emit(OpCode opcode, string value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, byte value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, sbyte value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, int value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, long value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, float value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, double value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, Instruction target)
        {
            Append(Create(opcode, target));
        }

        public void Emit(OpCode opcode, Instruction[] targets)
        {
            Append(Create(opcode, targets));
        }

        public void Emit(OpCode opcode, VariableDefinition variable)
        {
            Append(Create(opcode, variable));
        }

        public void Emit(OpCode opcode, ParameterDefinition parameter)
        {
            Append(Create(opcode, parameter));
        }

        public void InsertBefore(Instruction target, Instruction instruction)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (instruction == null)
                throw new ArgumentNullException(nameof(instruction));
            int index = _instructions.IndexOf(target);
            if (index == -1)
                throw new ArgumentOutOfRangeException(nameof(target));
            _instructions.Insert(index, instruction);
        }

        public void InsertAfter(Instruction target, Instruction instruction)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (instruction == null)
                throw new ArgumentNullException(nameof(instruction));
            int num = _instructions.IndexOf(target);
            if (num == -1)
                throw new ArgumentOutOfRangeException(nameof(target));
            _instructions.Insert(num + 1, instruction);
        }

        public void Append(Instruction instruction)
        {
            if (instruction == null)
                throw new ArgumentNullException(nameof(instruction));
            _instructions.Add(instruction);
        }

        public void Replace(Instruction target, Instruction instruction)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (instruction == null)
                throw new ArgumentNullException(nameof(instruction));
            InsertAfter(target, instruction);
            Remove(target);
        }

        public void Remove(Instruction instruction)
        {
            if (instruction == null)
                throw new ArgumentNullException(nameof(instruction));
            if (!_instructions.Remove(instruction))
                throw new ArgumentOutOfRangeException(nameof(instruction));
        }

        //private static ParameterDefinition GetParameter(MethodBody self, int index)
        //{
        //    MethodDefinition methodDefinition = self.Method;
        //    if (methodDefinition.HasThis)
        //    {
        //        if (index == 0)
        //            return self.ThisParameter;
        //        --index;
        //    }
        //    Collection<ParameterDefinition> parameters = methodDefinition.Parameters;
        //    if (index < 0 || index >= parameters.Count)
        //        return null;
        //    return parameters[index];
        //}
    }
}
