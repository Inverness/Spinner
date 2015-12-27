using System.Diagnostics;
using Mono.Cecil;

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
    }
}
