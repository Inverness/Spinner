namespace Spinner.Fody.Multicasting
{
    /// <summary>
    /// Enumerates the types of objects that provide metadata tokens.
    /// </summary>
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