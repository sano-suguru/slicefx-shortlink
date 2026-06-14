namespace ShortLink.Api.Infrastructure;

public interface ICurrentApiKey
{
    string? OwnerId { get; set; }
}

public sealed class CurrentApiKey : ICurrentApiKey
{
    public string? OwnerId { get; set; }
}
