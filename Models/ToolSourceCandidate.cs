using System.Collections.Generic;
using System.Linq;

namespace NodeKit_POC.Models
{
    public sealed record ToolSourceCandidate(
        ToolSourceRoute Route,
        string Name,
        string Version,
        string DisplayName,
        string SourceLabel,
        string? PackageName,
        string? Channel,
        string? Platform,
        bool RequiresExternalConnection,
        bool Recommended,
        IReadOnlyDictionary<string, string> Metadata)
    {
        public string StableRef => $"{Name}@{Version}";

        public string RouteLabel => Route switch
        {
            ToolSourceRoute.InternalSeed => "Internal Seed",
            ToolSourceRoute.LocalPackageMirror => "Local Mirror",
            ToolSourceRoute.ExternalConda => "External Bioconda",
            ToolSourceRoute.ExternalGitHubRelease => "GitHub Release",
            ToolSourceRoute.ExternalOciRegistry => "OCI Registry",
            ToolSourceRoute.RecipeBundle => "Recipe Bundle",
            ToolSourceRoute.LegacyDockerfile => "Legacy Dockerfile",
            ToolSourceRoute.InternalOciRegistry => "Internal OCI Registry",
            _ => Route.ToString(),
        };

        public string ConnectivityLabel => RequiresExternalConnection
            ? "외부 연결 필요"
            : "내부만으로 가능";

        public string RecommendationLabel => Recommended ? "권장" : "선택 가능";

        public string MetadataSummary => string.Join(
            ", ",
            Metadata.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
    }
}
