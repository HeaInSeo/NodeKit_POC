namespace NodeKit_POC.Models
{
    public sealed record RuntimeImageSpec(
        string BuilderImage,
        string? BuilderImageDigest,
        string BaseImage,
        string? BaseImageDigest);
}
