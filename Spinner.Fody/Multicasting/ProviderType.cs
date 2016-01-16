namespace Spinner.Fody.Multicasting
{
    internal enum ProviderType
    {
        // AssemblyDefinition
        Assembly,
        // IMemberDefinition
        Type,
        Method,
        Property,
        Event,
        Field,
        // ParameterDefinition
        Parameter,
        // ReturnTypeDefinition
        MethodReturn
    }
}