namespace NodeKit_POC.Models
{
    public sealed record GeneratedToolImageRecipe(
        string Name,
        string Version,
        string StableRef,
        string RecipeVersion,
        ToolSourceRoute SourceRoute,
        string DockerfileContent,
        string? EnvironmentYaml,
        string? LockMetadata,
        string RecipeFingerprint,
        string? EnvironmentYamlHash,
        string DockerfileHash,
        string ReproducibilityFingerprintSeed);
}
