namespace RuntimeEvidence;

// Evidence feasibility POC for Console Decision 39 "Runtime Requirement Discovery"
// (NodeKit_POC issue #1). This is NOT the canonical Tool Runtime Requirements schema.

/// <summary>One observed fact, with where it came from and how it was obtained.</summary>
/// <param name="Kind">executable-candidate, binary-format, architecture, elf-type, elf-interpreter,
/// elf-needed, elf-runpath, elf-rpath, elf-linkage, shebang-interpreter, shebang-argument, unknown.</param>
/// <param name="Value">The observed value, verbatim (or the reason, for unknown).</param>
/// <param name="Source">Where in the artifact the value was read, e.g. "elf:PT_INTERP".</param>
/// <param name="Method">How it was collected; always a read-only static method.</param>
public sealed record Evidence(string Kind, string Value, string Source, string Method);

/// <summary>Evidence for one file, keyed by its path relative to the analysed root ('/' separators).</summary>
public sealed record FileEvidence(string Path, IReadOnlyList<Evidence> Items);

/// <summary>The evidence for a whole local root. Contains no host path, time or environment data,
/// so the same artifact gives the same report wherever and whenever it is analysed.</summary>
public sealed record EvidenceReport(string SchemaNote, IReadOnlyList<FileEvidence> Files);
