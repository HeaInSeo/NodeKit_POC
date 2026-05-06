using System.Linq;
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
            var lockMetadata = BuildLockMetadata(recipe);

            return new GeneratedToolImageRecipe(
                recipe.Name,
                recipe.Version,
                recipe.StableRef,
                recipe.SourceRoute,
                dockerfile,
                environmentYaml,
                lockMetadata,
                recipe.ReproducibilitySeed);
        }

        private static string BuildEnvironmentYaml(ToolInstallRecipe recipe)
        {
            var dependencyLines = string.Join(
                "\n",
                recipe.Packages.Select(package => $"  - {package.Name}={package.Version}"));

            return
$""""
name: {recipe.Name}
channels:
  - bioconda
  - conda-forge
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

RUN useradd -m -s /bin/bash appuser
USER appuser
WORKDIR /work
CMD ["bash"]
"""";
        }

        private static string BuildLockMetadata(ToolInstallRecipe recipe)
        {
            return
$""""
stableRef: {recipe.StableRef}
sourceRoute: {recipe.SourceRoute}
installMethod: {recipe.InstallMethod}
reproducibilitySeed: {recipe.ReproducibilitySeed}
lockState: preview-only
"""";
        }
    }
}
