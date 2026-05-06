using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NodeKit_POC.Models;

namespace NodeKit_POC.Services
{
    public sealed class BiocondaSearchService
    {
        private static readonly Uri ChannelDataUri = new("https://conda.anaconda.org/bioconda/channeldata.json");
        private static readonly HttpClient HttpClient = new();

        private IReadOnlyList<ToolSourceCandidate>? _cachedCandidates;
        private DateTimeOffset _loadedAt;

        public async Task<IReadOnlyList<ToolSourceCandidate>> SearchAsync(string? query)
        {
            var candidates = await GetOrLoadCandidatesAsync().ConfigureAwait(false);
            var normalizedQuery = query?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(normalizedQuery))
            {
                return candidates.Take(20).ToList();
            }

            return candidates
                .Where(candidate =>
                    candidate.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || candidate.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || candidate.MetadataSummary.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => string.Equals(candidate.Name, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(candidate => candidate.Recommended)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();
        }

        private async Task<IReadOnlyList<ToolSourceCandidate>> GetOrLoadCandidatesAsync()
        {
            if (_cachedCandidates != null
                && DateTimeOffset.UtcNow - _loadedAt < TimeSpan.FromMinutes(15))
            {
                return _cachedCandidates;
            }

            using var stream = await HttpClient.GetStreamAsync(ChannelDataUri).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var packagesNode = document.RootElement.GetProperty("packages");

            var loaded = new List<ToolSourceCandidate>();
            foreach (var packageProperty in packagesNode.EnumerateObject())
            {
                var packageName = packageProperty.Name;
                var packageNode = packageProperty.Value;
                var version = ReadString(packageNode, "version");
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                loaded.Add(new ToolSourceCandidate(
                    ToolSourceRoute.ExternalConda,
                    packageName,
                    version,
                    $"{packageName} {version}",
                    "Bioconda",
                    packageName,
                    "bioconda",
                    ReadArray(packageNode, "subdirs").FirstOrDefault() ?? "linux-64",
                    true,
                    IsRecommended(packageName),
                    new Dictionary<string, string>
                    {
                        ["summary"] = ReadString(packageNode, "summary"),
                        ["license"] = ReadString(packageNode, "license"),
                        ["home"] = ReadString(packageNode, "home"),
                    }));
            }

            _cachedCandidates = loaded
                .OrderByDescending(candidate => candidate.Recommended)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _loadedAt = DateTimeOffset.UtcNow;
            return _cachedCandidates;
        }

        private static bool IsRecommended(string packageName)
        {
            return string.Equals(packageName, "bwa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(packageName, "samtools", StringComparison.OrdinalIgnoreCase)
                || string.Equals(packageName, "fastqc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(packageName, "gatk4", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(JsonElement node, string propertyName)
        {
            return node.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static IReadOnlyList<string> ReadArray(JsonElement node, string propertyName)
        {
            if (!node.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return property
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }
    }
}
