using System;
using System.Collections.Generic;
using System.Linq;
using NodeKit_POC.Models;

namespace NodeKit_POC.Services
{
    public static class ToolInstallRecipeNormalizer
    {
        public static ToolInstallRecipe Normalize(ToolSourceCandidate candidate)
        {
            var installMethod = ResolveInstallMethod(candidate.Route);
            var packages = BuildPackages(candidate, installMethod);
            var metadata = candidate.Metadata
                .Concat(new Dictionary<string, string?>
                {
                    ["sourceLabel"] = candidate.SourceLabel,
                    ["packageName"] = candidate.PackageName,
                    ["channel"] = candidate.Channel,
                    ["platform"] = candidate.Platform,
                    ["secondaryChannel"] = ResolveSecondaryChannel(candidate.Route),
                }
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => new KeyValuePair<string, string>(item.Key, item.Value!)))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            metadata["recipeVersion"] = "0.1.0";

            var reproducibilitySeed = string.Join(
                "|",
                new[]
                {
                    candidate.StableRef,
                    metadata["recipeVersion"],
                    candidate.Route.ToString(),
                    installMethod.ToString(),
                    string.Join(",", packages.Select(package => $"{package.Name}={package.Version}@{package.Channel}")),
                });

            return new ToolInstallRecipe(
                candidate.Name,
                candidate.Version,
                candidate.StableRef,
                metadata["recipeVersion"],
                candidate.Route,
                installMethod,
                new RuntimeImageSpec(
                    "mambaorg/micromamba:1.5.6",
                    null,
                    "ubuntu:22.04",
                    null),
                packages,
                metadata,
                reproducibilitySeed);
        }

        private static InstallMethod ResolveInstallMethod(ToolSourceRoute route)
        {
            return route switch
            {
                ToolSourceRoute.ExternalConda => InstallMethod.Micromamba,
                ToolSourceRoute.LocalPackageMirror => InstallMethod.Micromamba,
                ToolSourceRoute.InternalSeed => InstallMethod.Micromamba,
                ToolSourceRoute.ExternalGitHubRelease => InstallMethod.BinaryDownload,
                ToolSourceRoute.ExternalOciRegistry => InstallMethod.ExistingOciImage,
                ToolSourceRoute.InternalOciRegistry => InstallMethod.ExistingOciImage,
                ToolSourceRoute.RecipeBundle => InstallMethod.ImportedRecipe,
                ToolSourceRoute.LegacyDockerfile => InstallMethod.ImportedRecipe,
                _ => InstallMethod.ImportedRecipe,
            };
        }

        private static IReadOnlyList<PackageSpec> BuildPackages(ToolSourceCandidate candidate, InstallMethod installMethod)
        {
            if (installMethod != InstallMethod.Micromamba)
            {
                return Array.Empty<PackageSpec>();
            }

            return new[]
            {
                new PackageSpec(
                    candidate.PackageName ?? candidate.Name,
                    candidate.Version,
                    candidate.Channel,
                    candidate.Platform),
            };
        }

        private static string? ResolveSecondaryChannel(ToolSourceRoute route)
        {
            return route switch
            {
                ToolSourceRoute.ExternalConda => "conda-forge",
                ToolSourceRoute.LocalPackageMirror => "local-conda-forge",
                ToolSourceRoute.InternalSeed => "local-conda-forge",
                _ => null,
            };
        }
    }
}
