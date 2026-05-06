namespace NodeKit_POC.Models
{
    public sealed record GeneratedToolImageRecipe(
        string Name,
        string Version,
        string StableRef,
        ToolSourceRoute SourceRoute,
        string DockerfileContent,
        string? EnvironmentYaml,
        string? LockMetadata,
        string ReproducibilityFingerprintSeed);
}
