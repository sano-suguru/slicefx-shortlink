extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class CorsOriginsTests
{
    private static readonly string[] DevDefault = ["http://localhost:5201"];
    private static readonly string[] SingleOrigin = ["https://x.pages.dev"];
    private static readonly string[] MultipleOrigins = ["https://x.pages.dev", "http://localhost:5201"];

    [Fact]
    public void Parse_null_returns_dev_default()
        => Assert.Equal(DevDefault, CorsOrigins.Parse(null));

    [Fact]
    public void Parse_empty_returns_dev_default()
        => Assert.Equal(DevDefault, CorsOrigins.Parse("   "));

    [Fact]
    public void Parse_single_origin()
        => Assert.Equal(SingleOrigin, CorsOrigins.Parse("https://x.pages.dev"));

    [Fact]
    public void Parse_multiple_trims_and_drops_empties()
        => Assert.Equal(
            MultipleOrigins,
            CorsOrigins.Parse(" https://x.pages.dev , , http://localhost:5201 "));
}
