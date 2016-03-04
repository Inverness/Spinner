using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Spinner.Fody.Utilities;

namespace Spinner.Fody.Weavers.Prototype
{
    internal sealed class AspectInitWeaver : AdviceWeaver
    {
        private const string MulticastAttributePropertyPrefix = "Attribute";

        public AspectInitWeaver(AspectWeaver2 p, MethodDefinition adviceMethod)
            : base(p, adviceMethod)
        {
        }

        protected override void WeaveCore(MethodDefinition method, MethodDefinition stateMachine, int offset, ICollection<AdviceWeaver> previous)
        {
            var fieldWeaver = previous.OfType<AspectFieldWeaver>().First();

            // Figure out if the attribute has any arguments that need to be initialized
            CustomAttribute attr = P.MulticastInstance.Attribute;

            int argCount = attr.HasConstructorArguments ? attr.ConstructorArguments.Count : 0;
            int propCount = attr.HasProperties ? attr.Properties.Count : 0;
            int fieldCount = attr.HasFields ? attr.Fields.Count : 0;

            Instruction jtNotNull = Instruction.Create(OpCodes.Nop);

            var il = new ILProcessorEx();
            il.Emit(OpCodes.Ldsfld, fieldWeaver.Field);
            il.Emit(OpCodes.Brtrue, jtNotNull);

            MethodReference ctor;
            if (argCount == 0)
            {
                // NOTE: Aspect type can't be generic since its declared by an attribute
                ctor = P.Context.SafeImport(P.AspectType.GetConstructor(0));
            }
            else
            {
                List<TypeReference> argTypes = attr.ConstructorArguments.Select(c => c.Type).ToList();

                ctor = P.Context.SafeImport(P.AspectType.GetConstructor(argTypes));

                for (int i = 0; i < attr.ConstructorArguments.Count; i++)
                    EmitAttributeArgument(il, attr.ConstructorArguments[i]);
            }

            il.Emit(OpCodes.Newobj, ctor);

            if (fieldCount != 0)
            {
                for (int i = 0; i < attr.Fields.Count; i++)
                {
                    CustomAttributeNamedArgument na = attr.Fields[i];

                    FieldReference field = P.Context.SafeImport(P.AspectType.GetField(na.Name, true));

                    il.Emit(OpCodes.Dup);
                    EmitAttributeArgument(il, na.Argument);
                    il.Emit(OpCodes.Stfld, field);
                }
            }

            if (propCount != 0)
            {
                for (int i = 0; i < attr.Properties.Count; i++)
                {
                    CustomAttributeNamedArgument na = attr.Properties[i];

                    // Skip attribute properties that are not needed at runtime
                    if (na.Name.StartsWith(MulticastAttributePropertyPrefix))
                        continue;

                    MethodReference setter = P.Context.SafeImport(P.AspectType.GetProperty(na.Name, true).SetMethod);

                    il.Emit(OpCodes.Dup);
                    EmitAttributeArgument(il, na.Argument);
                    il.Emit(OpCodes.Callvirt, setter);
                }
            }

            il.Emit(OpCodes.Stsfld, fieldWeaver.Field);
            il.Append(jtNotNull);

            method.Body.InsertInstructions(offset, true, il.Instructions);
        }

        private void EmitAttributeArgument(ILProcessorEx il, CustomAttributeArgument arg)
        {
            EmitLiteral(il, arg.Type, arg.Value);
        }

        private void EmitLiteral(ILProcessorEx il, TypeReference type, object value)
        {
            if (value == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else if (value is bool)
            {
                il.Emit((bool) value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            }
            else if (value is char)
            {
                il.Emit(OpCodes.Ldc_I4, (char) value);
            }
            else if (value is byte)
            {
                il.Emit(OpCodes.Ldc_I4, (byte) value);
            }
            else if (value is sbyte)
            {
                il.Emit(OpCodes.Ldc_I4, (sbyte) value);
            }
            else if (value is short)
            {
                il.Emit(OpCodes.Ldc_I4, (short) value);
            }
            else if (value is ushort)
            {
                il.Emit(OpCodes.Ldc_I4, (ushort) value);
            }
            else if (value is int)
            {
                il.Emit(OpCodes.Ldc_I4, (int) value);
            }
            else if (value is uint)
            {
                il.Emit(OpCodes.Ldc_I4, (int) (uint) value);
            }
            else if (value is long)
            {
                il.Emit(OpCodes.Ldc_I8, (long) value);
            }
            else if (value is ulong)
            {
                il.Emit(OpCodes.Ldc_I8, (long) (ulong) value);
            }
            else if (value is float)
            {
                il.Emit(OpCodes.Ldc_R4, (float) value);
            }
            else if (value is double)
            {
                il.Emit(OpCodes.Ldc_R8, (double) value);
            }
            else if (value is string)
            {
                il.Emit(OpCodes.Ldstr, (string) value);
            }
            else if (value is TypeReference)
            {
                MethodReference getTypeFromHandle = P.Context.SafeImport(P.Context.Framework.Type_GetTypeFromHandle);
                il.Emit(OpCodes.Ldtoken, (TypeReference) value);
                il.Emit(OpCodes.Call, getTypeFromHandle);
            }
            else if (value is CustomAttributeArgument[])
            {
                var caaArray = (CustomAttributeArgument[]) value;
                TypeReference elementType = P.Context.SafeImport(type.GetElementType());
                TypeReference objectType = elementType.Module.TypeSystem.Object;
                OpCode stelemOpCode = GetStelemOpCode(elementType);

                il.Emit(OpCodes.Ldc_I4, caaArray.Length);
                il.Emit(OpCodes.Newarr, elementType);

                for (int i = 0; i < caaArray.Length; i++)
                {
                    CustomAttributeArgument caa = caaArray[i];

                    if (caa.Value is CustomAttributeArgument)
                        caa = (CustomAttributeArgument) caa.Value;

                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);

                    EmitLiteral(il, caa.Type, caa.Value);

                    if (elementType.IsSame(objectType) && caa.Type.IsValueType)
                        il.Emit(OpCodes.Box, caa.Type);

                    il.Emit(stelemOpCode);
                    //il.Emit(OpCodes.Stelem_Any, elementType);
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(value), value.GetType().Name);
            }
        }

        private OpCode GetStelemOpCode(TypeReference type)
        {
            TypeSystem ts = type.Module.TypeSystem;

            if (type == ts.Boolean || type == ts.Byte || type == ts.SByte)
                return OpCodes.Stelem_I1;
            if (type == ts.Char || type == ts.Int16 || type == ts.UInt16)
                return OpCodes.Stelem_I2;
            if (type == ts.Int32 || type == ts.UInt32)
                return OpCodes.Stelem_I4;
            if (type == ts.Int64 || type == ts.UInt64)
                return OpCodes.Stelem_I8;
            if (type == ts.Single)
                return OpCodes.Stelem_R4;
            if (type == ts.Double)
                return OpCodes.Stelem_R8;
            return OpCodes.Stelem_Ref;
        }
    }
}