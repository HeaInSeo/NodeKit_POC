namespace NodeKit_POC.Models
{
    public sealed record PackageSpec(
        string Name,
        string Version,
        string? Channel,
        string? Platform);
}
