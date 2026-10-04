using RuntimeEvidence;

// Usage: dotnet run --project RuntimeEvidence.Cli -- <unpacked-tool-root>
// Prints the deterministic evidence report as JSON. Read-only: nothing is executed or written.
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: RuntimeEvidence.Cli <unpacked-tool-root>");
    return 2;
}
Console.WriteLine(EvidenceCollector.ToJson(EvidenceCollector.Collect(args[0])));
return 0;
