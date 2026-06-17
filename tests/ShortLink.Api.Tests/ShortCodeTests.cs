extern alias ShortLinkApi;

using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class ShortCodeTests
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Generate_returns_7_char_string()
    {
        var code = ShortCode.Generate();

        Assert.Equal(7, code.Length);
    }

    [Fact]
    public void Generate_all_chars_are_in_alphabet()
    {
        var code = ShortCode.Generate();

        foreach (var ch in code)
        {
            Assert.Contains(ch, Alphabet);
        }
    }

    [Fact]
    public void Generate_1000_codes_are_mostly_unique()
    {
        var codes = new HashSet<string>(capacity: 1000);

        for (var i = 0; i < 1000; i++)
        {
            codes.Add(ShortCode.Generate());
        }

        Assert.True(codes.Count >= 990,
            $"Expected at least 990 distinct codes out of 1000, but got {codes.Count}.");
    }
}
