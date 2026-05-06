namespace NodeKit_POC.Models
{
    public enum ToolSourceRoute
    {
        InternalSeed,
        LocalPackageMirror,
        ExternalConda,
        ExternalGitHubRelease,
        ExternalOciRegistry,
        RecipeBundle,
        LegacyDockerfile,
        InternalOciRegistry,
    }
}
