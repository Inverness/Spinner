using Mono.Cecil;
using Mono.Cecil.Rocks;

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

        internal static MethodReference MakeGenericDeclaringType(this MethodReference self, params TypeReference[] args)
        {
            return MakeGenericDeclaringType(self, self.DeclaringType.MakeGenericInstanceType(args));
        }

        internal static MethodReference MakeGenericDeclaringType(this MethodReference self, GenericInstanceType type)
        {
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

        internal static FieldReference MakeGenericDeclaringType(this FieldReference self, params TypeReference[] args)
        {
            return MakeGenericDeclaringType(self, self.DeclaringType.MakeGenericInstanceType(args));
        }

        internal static FieldReference MakeGenericDeclaringType(this FieldReference self, GenericInstanceType type)
        {
            return new FieldReference(self.Name, self.FieldType, type);
        }
    }
}
