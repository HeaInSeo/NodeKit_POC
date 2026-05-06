using System.Collections.Generic;

namespace NodeKit_POC.Models
{
    public sealed record ToolInstallRecipe(
        string Name,
        string Version,
        string StableRef,
        ToolSourceRoute SourceRoute,
        InstallMethod InstallMethod,
        RuntimeImageSpec Runtime,
        IReadOnlyList<PackageSpec> Packages,
        IReadOnlyDictionary<string, string> SourceMetadata,
        string ReproducibilitySeed);
}
