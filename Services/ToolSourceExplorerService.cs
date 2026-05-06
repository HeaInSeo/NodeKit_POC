using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using NodeKit_POC.Models;

namespace NodeKit_POC.Services
{
    public sealed class ToolSourceExplorerService
    {
        private static readonly IReadOnlyList<ToolSourceCandidate> FixtureCandidates = LoadFixtureCandidates();

        public IReadOnlyList<ToolSourceCandidate> SearchCandidates(
            ToolConnectivityMode mode,
            ToolSourceStartPoint startPoint,
            string? query)
        {
            var normalizedQuery = query?.Trim() ?? string.Empty;
            IEnumerable<ToolSourceCandidate> candidates = FixtureCandidates
                .Where(candidate => MatchesStartPoint(candidate.Route, startPoint));

            if (mode == ToolConnectivityMode.Disconnected)
            {
                candidates = candidates.Where(candidate => !candidate.RequiresExternalConnection);
            }

            if (!string.IsNullOrEmpty(normalizedQuery))
            {
                candidates = candidates.Where(candidate =>
                    candidate.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || candidate.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || candidate.SourceLabel.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
            }

            return candidates
                .OrderByDescending(candidate => candidate.Recommended)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<ToolSourceCandidate> LoadFixtureCandidates()
        {
            var candidates = new List<ToolSourceCandidate>();
            candidates.AddRange(LoadCandidatesFromDirectory("seeds"));
            candidates.AddRange(LoadCandidatesFromDirectory("local-mirror"));
            candidates.AddRange(BuildInlineCandidates());
            return new ReadOnlyCollection<ToolSourceCandidate>(candidates);
        }

        private static IEnumerable<ToolSourceCandidate> LoadCandidatesFromDirectory(string relativeDirectory)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", relativeDirectory);
            if (!Directory.Exists(directory))
            {
                return Enumerable.Empty<ToolSourceCandidate>();
            }

            return Directory
                .GetFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(LoadCandidateFromFile)
                .ToList();
        }

        private static ToolSourceCandidate LoadCandidateFromFile(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();

                if (key.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase))
                {
                    metadata[key["metadata.".Length..]] = value;
                }
                else
                {
                    values[key] = value;
                }
            }

            return new ToolSourceCandidate(
                Enum.Parse<ToolSourceRoute>(GetRequired(values, "route")),
                GetRequired(values, "name"),
                GetRequired(values, "version"),
                GetRequired(values, "displayName"),
                GetRequired(values, "sourceLabel"),
                GetOptional(values, "packageName"),
                GetOptional(values, "channel"),
                GetOptional(values, "platform"),
                bool.Parse(GetRequired(values, "requiresExternalConnection")),
                bool.Parse(GetRequired(values, "recommended")),
                metadata);
        }

        private static IReadOnlyList<ToolSourceCandidate> BuildInlineCandidates()
        {
            return new ReadOnlyCollection<ToolSourceCandidate>(
                new List<ToolSourceCandidate>
                {
                    new(
                        ToolSourceRoute.ExternalConda,
                        "bwa",
                        "0.7.17",
                        "BWA 0.7.17",
                        "Bioconda",
                        "bwa",
                        "bioconda",
                        "linux-64",
                        true,
                        true,
                        new Dictionary<string, string>
                        {
                            ["channel"] = "bioconda",
                            ["subdir"] = "linux-64",
                        }),
                    new(
                        ToolSourceRoute.ExternalConda,
                        "samtools",
                        "1.19",
                        "Samtools 1.19",
                        "Bioconda",
                        "samtools",
                        "bioconda",
                        "linux-64",
                        true,
                        true,
                        new Dictionary<string, string>
                        {
                            ["channel"] = "bioconda",
                            ["subdir"] = "linux-64",
                        }),
                    new(
                        ToolSourceRoute.ExternalConda,
                        "fastqc",
                        "0.12.1",
                        "FastQC 0.12.1",
                        "Bioconda",
                        "fastqc",
                        "bioconda",
                        "linux-64",
                        true,
                        false,
                        new Dictionary<string, string>
                        {
                            ["channel"] = "bioconda",
                            ["subdir"] = "linux-64",
                        }),
                    new(
                        ToolSourceRoute.ExternalGitHubRelease,
                        "bwa",
                        "0.7.17",
                        "lh3/bwa v0.7.17",
                        "GitHub Release",
                        "bwa",
                        null,
                        "linux-amd64",
                        true,
                        false,
                        new Dictionary<string, string>
                        {
                            ["repository"] = "lh3/bwa",
                            ["tag"] = "v0.7.17",
                        }),
                    new(
                        ToolSourceRoute.ExternalGitHubRelease,
                        "gatk",
                        "4.1.2.0",
                        "broadinstitute/gatk v4.1.2.0",
                        "GitHub Release",
                        "gatk",
                        null,
                        "linux-amd64",
                        true,
                        false,
                        new Dictionary<string, string>
                        {
                            ["repository"] = "broadinstitute/gatk",
                            ["tag"] = "4.1.2.0",
                        }),
                    new(
                        ToolSourceRoute.ExternalOciRegistry,
                        "bwa",
                        "0.7.17",
                        "bwa:0.7.17",
                        "OCI Registry",
                        "bwa",
                        null,
                        "linux-amd64",
                        true,
                        false,
                        new Dictionary<string, string>
                        {
                            ["image"] = "docker.io/biocontainers/bwa:0.7.17",
                            ["pull"] = "public",
                        }),
                    new(
                        ToolSourceRoute.ExternalOciRegistry,
                        "fastqc",
                        "0.11.9",
                        "fastqc:0.11.9",
                        "OCI Registry",
                        "fastqc",
                        null,
                        "linux-amd64",
                        true,
                        false,
                        new Dictionary<string, string>
                        {
                            ["image"] = "docker.io/biocontainers/fastqc:0.11.9",
                            ["pull"] = "public",
                        }),
                    new(
                        ToolSourceRoute.RecipeBundle,
                        "bwa",
                        "0.7.17",
                        "bwa-recipe-bundle",
                        "Imported Recipe Bundle",
                        "bwa",
                        null,
                        "linux-64",
                        false,
                        false,
                        new Dictionary<string, string>
                        {
                            ["bundle"] = "bwa-recipe-bundle",
                            ["evidence"] = "pre-imported",
                        }),
                    new(
                        ToolSourceRoute.RecipeBundle,
                        "samtools",
                        "1.19",
                        "samtools-recipe-bundle",
                        "Imported Recipe Bundle",
                        "samtools",
                        null,
                        "linux-64",
                        false,
                        false,
                        new Dictionary<string, string>
                        {
                            ["bundle"] = "samtools-recipe-bundle",
                            ["evidence"] = "pre-imported",
                        }),
                    new(
                        ToolSourceRoute.InternalOciRegistry,
                        "bwa",
                        "0.7.17",
                        "bwa-tool-runtime@sha256:preview",
                        "Internal Harbor",
                        "bwa",
                        null,
                        "linux-amd64",
                        false,
                        true,
                        new Dictionary<string, string>
                        {
                            ["image"] = "harbor.local/tooling/bwa-tool-runtime@sha256:preview",
                            ["evidence"] = "internal-registry-preview",
                        }),
                });
        }

        private static string GetRequired(IReadOnlyDictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"Fixture file is missing required key '{key}'.");
        }

        private static string? GetOptional(IReadOnlyDictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static bool MatchesStartPoint(ToolSourceRoute route, ToolSourceStartPoint startPoint)
        {
            return startPoint switch
            {
                ToolSourceStartPoint.InternalSeed => route == ToolSourceRoute.InternalSeed,
                ToolSourceStartPoint.LocalPackageMirror => route == ToolSourceRoute.LocalPackageMirror,
                ToolSourceStartPoint.InternalOciRegistry => route == ToolSourceRoute.InternalOciRegistry,
                ToolSourceStartPoint.ExternalSearch => route == ToolSourceRoute.ExternalConda
                    || route == ToolSourceRoute.ExternalGitHubRelease
                    || route == ToolSourceRoute.ExternalOciRegistry,
                ToolSourceStartPoint.RecipeBundle => route == ToolSourceRoute.RecipeBundle,
                _ => false,
            };
        }
    }
}
