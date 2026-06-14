namespace ShortLink.Api.Infrastructure;

public interface IShortLinkSettings
{
    string BaseUrl { get; }
}

public sealed class ShortLinkSettings : IShortLinkSettings
{
    public string BaseUrl { get; init; } = "http://localhost:5200";
}
