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

            if (self.HasParameters)
                foreach (ParameterDefinition parameter in self.Parameters)
                    reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            if (self.HasGenericParameters)
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

        /// <summary>
        /// Does a fast check if two references are for the same definition.
        /// </summary>
        internal static bool IsSame(this TypeReference self, TypeReference other)
        {
            if (ReferenceEquals(self, other))
                return true;

            if (self.Name != other.Name || self.Namespace != other.Namespace)
                return false;

            if (self.HasGenericParameters != other.HasGenericParameters)
                return false;

            if (self.HasGenericParameters && (self.GenericParameters.Count != other.GenericParameters.Count))
                return false;

            return self.Resolve() == other.Resolve();
        }

        /// <summary>
        /// Does a fast check if two references are for the same definition.
        /// </summary>
        internal static bool IsSame(this FieldReference self, FieldReference other)
        {
            if (ReferenceEquals(self, other))
                return true;

            if (self.Name != other.Name)
                return false;

            if (!self.FieldType.IsSame(other.FieldType))
                return false;

            if (!self.DeclaringType.IsSame(other.DeclaringType))
                return false;

            return self.Resolve() == other.Resolve();
        }

        /// <summary>
        /// Shortcut for MetadataResolver.GetMethod() to get a matching method reference.
        /// </summary>
        internal static MethodDefinition GetMethod(this TypeDefinition self, MethodReference reference, bool inherited)
        {
            if (!inherited)
                return self.HasMethods ? MetadataResolver.GetMethod(self.Methods, reference) : null;

            TypeDefinition current = self;
            while (current != null)
            {
                MethodDefinition result;
                if (current.HasMethods && (result = MetadataResolver.GetMethod(current.Methods, reference)) != null)
                    return result;

                current = current.BaseType?.Resolve();
            }

            return null;
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

        //    if (self.HasGenericParameters != other.HasGenericParameters)
        //        return false;

        //    if (self.HasParameters && (self.Parameters.Count != other.Parameters.Count))
        //        return false;

        //    if (self.HasGenericParameters && (self.GenericParameters.Count != other.GenericParameters.Count))
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