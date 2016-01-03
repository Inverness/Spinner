using System.Collections.Generic;
using System.Diagnostics;
using Mono.Cecil;

namespace Spinner.Fody
{
    internal static class ReferenceExtensions
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
            if (ReferenceEquals(self, other))
                return true;

            Debug.Assert(self.IsGenericParameter == other.IsGenericParameter,
                         "comparing generic parameter to non-generic paramter");

            return self.Name == other.Name && self.Namespace == other.Namespace;
        }

        //internal static bool IsSame(this MethodReference self, MethodReference other)
        //{
        //    if (ReferenceEquals(self, other))
        //        return true;

        //    if (self.Name != other.Name)
        //        return false;

        //    if (!IsSame(self.DeclaringType, other.DeclaringType))
        //        return false;
            
        //    if (self.HasThis != other.HasThis)
        //        return false;
            
        //    if (self.HasParameters != other.HasParameters)
        //        return false;

        //    if (self.HasParameters && (self.Parameters.Count != other.Parameters.Count))
        //        return false;

        //    if (!IsSame(self.ReturnType, other.ReturnType))
        //        return false;

        //    for (int i = 0; i < self.Parameters.Count; i++)
        //    {
        //        if (!self.Parameters[i].ParameterType.IsSame(other.Parameters[i].ParameterType))
        //            return false;
        //    }

        //    return true;
        //}
    }
}