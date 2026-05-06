namespace NodeKit_POC.Models
{
    public sealed record RuntimeImageSpec(
        string BuilderImage,
        string BaseImage);
}
