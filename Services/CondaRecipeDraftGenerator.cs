using NodeKit_POC.Models;

namespace NodeKit_POC.Services
{
    public static class CondaRecipeDraftGenerator
    {
        public static GeneratedCondaRecipeDraft Generate(ToolSourceCandidate candidate)
        {
            var packageName = candidate.PackageName ?? candidate.Name;

            var environmentYaml =
$""""
name: {candidate.Name}
channels:
  - bioconda
  - conda-forge
dependencies:
  - {packageName}={candidate.Version}
"""";

            var dockerfile =
$""""
FROM mambaorg/micromamba:1.5.6 AS builder

COPY environment.yml /tmp/environment.yml
RUN micromamba create -y -n tool -f /tmp/environment.yml \
    && micromamba clean -a -y

FROM ubuntu:22.04 AS runtime

ENV PATH=/opt/conda/envs/tool/bin:/opt/conda/bin:$PATH
COPY --from=builder /opt/conda /opt/conda

RUN useradd -m -s /bin/bash appuser
USER appuser
WORKDIR /work
CMD ["{candidate.Name}"]
"""";

            return new GeneratedCondaRecipeDraft(environmentYaml, dockerfile);
        }
    }
}
