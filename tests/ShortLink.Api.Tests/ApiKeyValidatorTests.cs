extern alias ShortLinkApi;

using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

/// <summary>
/// Pure unit tests for <see cref="ApiKeyValidator.Hash"/>. No database required.
/// </summary>
public sealed class ApiKeyValidatorHashTests
{
    [Fact]
    public void Hash_is_deterministic()
    {
        var first = ApiKeyValidator.Hash("my-secret-key");
        var second = ApiKeyValidator.Hash("my-secret-key");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_is_64_lowercase_hex_chars()
    {
        var hash = ApiKeyValidator.Hash("any-key");

        Assert.Equal(64, hash.Length);
        Assert.All(hash, ch => Assert.True(char.IsAsciiDigit(ch) || (ch >= 'a' && ch <= 'f'),
            $"Expected lowercase hex char, got '{ch}'."));
    }

    [Fact]
    public void Hash_differs_for_different_inputs()
    {
        var hash1 = ApiKeyValidator.Hash("key-one");
        var hash2 = ApiKeyValidator.Hash("key-two");

        Assert.NotEqual(hash1, hash2);
    }
}

/// <summary>
/// Integration tests for <see cref="ApiKeyValidator.ValidateAsync"/>. Requires a live Postgres.
/// </summary>
public sealed class ApiKeyValidatorDbTests : DbBackedTest
{
    [Fact]
    public async Task ValidateAsync_returns_hash_for_valid_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var validator = new ApiKeyValidator(Ds);
        var expectedHash = ApiKeyValidator.Hash(TestDb.SeedApiKey);

        var result = await validator.ValidateAsync(TestDb.SeedApiKey, ct);

        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public async Task ValidateAsync_returns_null_for_invalid_key()
    {
        var ct = TestContext.Current.CancellationToken;
        // Build a fresh data source just for this test to confirm the DB path
        await using var ds = TestDb.Build();
        var validator = new ApiKeyValidator(ds);

        var result = await validator.ValidateAsync("this-key-does-not-exist", ct);

        Assert.Null(result);
    }
}
