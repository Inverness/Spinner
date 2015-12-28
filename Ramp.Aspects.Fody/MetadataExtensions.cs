using System;
using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;

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

        //internal static TypeReference SafeImport(this ModuleDefinition self, Type type)
        //{
        //    lock (self)
        //        return self.Import(type);
        //}

        //internal static TypeReference SafeImport(this ModuleDefinition self, TypeReference type)
        //{
        //    lock (self)
        //        return self.Import(type);
        //}

        //internal static MethodReference SafeImport(this ModuleDefinition self, MethodReference method)
        //{
        //    lock (self)
        //        return self.Import(method);
        //}

        //internal static FieldReference SafeImport(this ModuleDefinition self, FieldReference field)
        //{
        //    lock (self)
        //        return self.Import(field);
        //}

        //internal static TypeDefinition SafeGetType(this ModuleDefinition self, string fullName)
        //{
        //    lock (self)
        //        return self.GetType(fullName);
        //}
    }
}
