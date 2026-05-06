using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NodeKit_POC.Models;

namespace NodeKit_POC.Services
{
    public static class ToolImageRecipeGenerator
    {
        public static GeneratedToolImageRecipe Generate(ToolInstallRecipe recipe)
        {
            var environmentYaml = recipe.InstallMethod == InstallMethod.Micromamba
                ? BuildEnvironmentYaml(recipe)
                : null;
            var dockerfile = BuildDockerfile(recipe, environmentYaml != null);
            var environmentYamlHash = environmentYaml != null ? ComputeHash(environmentYaml) : null;
            var dockerfileHash = ComputeHash(dockerfile);
            var recipeFingerprint = ComputeHash(
                string.Join(
                    "|",
                    new[]
                    {
                        recipe.ReproducibilitySeed,
                        recipe.RecipeVersion,
                        environmentYamlHash ?? "no-env",
                        dockerfileHash,
                    }));
            var lockMetadata = BuildLockMetadata(recipe, environmentYamlHash, dockerfileHash, recipeFingerprint);

            return new GeneratedToolImageRecipe(
                recipe.Name,
                recipe.Version,
                recipe.StableRef,
                recipe.RecipeVersion,
                recipe.SourceRoute,
                dockerfile,
                environmentYaml,
                lockMetadata,
                recipeFingerprint,
                environmentYamlHash,
                dockerfileHash,
                recipe.ReproducibilitySeed);
        }

        private static string BuildEnvironmentYaml(ToolInstallRecipe recipe)
        {
            var channels = ResolveChannels(recipe);
            var dependencyLines = string.Join(
                "\n",
                recipe.Packages.Select(package => $"  - {package.Name}={package.Version}"));
            var channelLines = string.Join(
                "\n",
                channels.Select(channel => $"  - {channel}"));

            return
$""""
name: {recipe.Name}
channels:
{channelLines}
dependencies:
{dependencyLines}
"""";
        }

        private static string BuildDockerfile(ToolInstallRecipe recipe, bool hasEnvironmentYaml)
        {
            if (recipe.InstallMethod == InstallMethod.Micromamba && hasEnvironmentYaml)
            {
                return
$""""
FROM {recipe.Runtime.BuilderImage} AS builder

COPY environment.yml /tmp/environment.yml
RUN micromamba create -y -n tool -f /tmp/environment.yml \
    && micromamba clean -a -y

FROM {recipe.Runtime.BaseImage} AS runtime

# This image is a reusable tool runtime seed.
# It intentionally does not pin an execution entrypoint.
# Actual command/script/entrypoint is defined in a later wrapper or DAG node image.
ENV PATH=/opt/conda/envs/tool/bin:/opt/conda/bin:$PATH
COPY --from=builder /opt/conda /opt/conda

RUN useradd -m -s /bin/bash appuser
USER appuser
WORKDIR /work
CMD ["bash"]
"""";
            }

            return
$""""
FROM {recipe.Runtime.BaseImage} AS runtime

# Route: {recipe.SourceRoute}
# InstallMethod: {recipe.InstallMethod}
# This image is a runtime recipe draft, not an execution node contract.
# Actual command/script/entrypoint is defined in a later wrapper or DAG node image.

RUN useradd -m -s /bin/bash appuser
USER appuser
WORKDIR /work
CMD ["bash"]
"""";
        }

        private static string BuildLockMetadata(
            ToolInstallRecipe recipe,
            string? environmentYamlHash,
            string dockerfileHash,
            string recipeFingerprint)
        {
            return
$""""
stableRef: {recipe.StableRef}
recipeVersion: {recipe.RecipeVersion}
sourceRoute: {recipe.SourceRoute}
installMethod: {recipe.InstallMethod}
reproducibilitySeed: {recipe.ReproducibilitySeed}
builderImage: {recipe.Runtime.BuilderImage}
builderImageDigest: {recipe.Runtime.BuilderImageDigest ?? "resolve-required"}
baseImage: {recipe.Runtime.BaseImage}
baseImageDigest: {recipe.Runtime.BaseImageDigest ?? "resolve-required"}
environmentYamlHash: {environmentYamlHash ?? "not-generated"}
dockerfileHash: {dockerfileHash}
recipeFingerprint: {recipeFingerprint}
lockState: preview-only
dependencyResolution: preview-only
"""";
        }

        private static IReadOnlyList<string> ResolveChannels(ToolInstallRecipe recipe)
        {
            return recipe.SourceRoute switch
            {
                ToolSourceRoute.ExternalConda => BuildChannelList(
                    recipe,
                    recipe.Packages.Select(package => package.Channel).Where(channel => !string.IsNullOrWhiteSpace(channel)).Select(channel => channel!),
                    "conda-forge"),
                ToolSourceRoute.LocalPackageMirror => BuildChannelList(
                    recipe,
                    recipe.Packages.Select(package => package.Channel).Where(channel => !string.IsNullOrWhiteSpace(channel)).Select(channel => channel!),
                    "local-conda-forge"),
                ToolSourceRoute.InternalSeed => BuildChannelList(
                    recipe,
                    recipe.Packages.Select(package => package.Channel).Where(channel => !string.IsNullOrWhiteSpace(channel)).Select(channel => channel!),
                    "local-conda-forge"),
                _ => Array.Empty<string>(),
            };
        }

        private static IReadOnlyList<string> BuildChannelList(
            ToolInstallRecipe recipe,
            IEnumerable<string> primaryChannels,
            string fallbackSecondaryChannel)
        {
            var channels = new List<string>();
            channels.AddRange(primaryChannels.Distinct(StringComparer.OrdinalIgnoreCase));

            if (recipe.SourceMetadata.TryGetValue("secondaryChannel", out var secondaryChannel)
                && !string.IsNullOrWhiteSpace(secondaryChannel))
            {
                channels.Add(secondaryChannel);
            }
            else
            {
                channels.Add(fallbackSecondaryChannel);
            }

            return channels
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
