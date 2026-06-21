extern alias ShortLinkApi;

using ShortLink.Api.Tests.Helpers;
using ShortLinkApi::ShortLink.Api.Infrastructure;

namespace ShortLink.Api.Tests;

public sealed class PostgresLinkStoreTests : DbBackedTest
{
    private PostgresLinkStore Store => new(Ds);

    private static string Owner1Hash => ApiKeyValidator.Hash(TestDb.SeedApiKey);
    private static string Owner2Hash => ApiKeyValidator.Hash(TestDb.SecondApiKey);

    [Fact]
    public async Task CreateAsync_returns_link_with_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;

        var link = await store.CreateAsync("https://example.com", Owner1Hash, ShortCode.Generate(), ct);

        Assert.True(link.Id > 0);
        Assert.False(string.IsNullOrEmpty(link.Code));
        Assert.Equal("https://example.com", link.TargetUrl);
    }

    [Fact]
    public async Task FindByCodeAsync_returns_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;
        var created = await store.CreateAsync("https://example.com/find-by-code", Owner1Hash, ShortCode.Generate(), ct);

        var found = await store.FindByCodeAsync(created.Code, ct);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(created.Code, found.Code);
        Assert.Equal(created.TargetUrl, found.TargetUrl);
    }

    [Fact]
    public async Task FindByIdAsync_returns_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;
        var created = await store.CreateAsync("https://example.com/find-by-id", Owner1Hash, ShortCode.Generate(), ct);

        var found = await store.FindByIdAsync(created.Id, ct);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(created.TargetUrl, found.TargetUrl);
    }

    [Fact]
    public async Task FindByIdAsync_returns_null_for_unknown_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;

        var found = await store.FindByIdAsync(long.MaxValue, ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task ListByOwnerAsync_respects_page_boundaries()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;

        for (var i = 0; i < 5; i++)
        {
            await store.CreateAsync($"https://example.com/page/{i}", Owner1Hash, ShortCode.Generate(), ct);
        }

        var page1 = await store.ListByOwnerAsync(Owner1Hash, page: 1, pageSize: 2, ct);
        var page2 = await store.ListByOwnerAsync(Owner1Hash, page: 2, pageSize: 2, ct);
        var page3 = await store.ListByOwnerAsync(Owner1Hash, page: 3, pageSize: 2, ct);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Single(page3);
    }

    [Fact]
    public async Task CountByOwnerAsync_counts_only_own_links()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;

        await store.CreateAsync("https://example.com/owner1/a", Owner1Hash, ShortCode.Generate(), ct);
        await store.CreateAsync("https://example.com/owner1/b", Owner1Hash, ShortCode.Generate(), ct);
        await store.CreateAsync("https://example.com/owner2/a", Owner2Hash, ShortCode.Generate(), ct);

        var count1 = await store.CountByOwnerAsync(Owner1Hash, ct);
        var count2 = await store.CountByOwnerAsync(Owner2Hash, ct);

        Assert.Equal(2, count1);
        Assert.Equal(1, count2);
    }

    [Fact]
    public async Task DeleteAsync_removes_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;
        var created = await store.CreateAsync("https://example.com/delete-me", Owner1Hash, ShortCode.Generate(), ct);

        var deleted = await store.DeleteAsync(created.Id, Owner1Hash, ct);
        var found = await store.FindByIdAsync(created.Id, ct);

        Assert.True(deleted);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_wrong_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;
        var created = await store.CreateAsync("https://example.com/wrong-owner", Owner1Hash, ShortCode.Generate(), ct);

        var deleted = await store.DeleteAsync(created.Id, Owner2Hash, ct);
        var found = await store.FindByIdAsync(created.Id, ct);

        Assert.False(deleted);
        Assert.NotNull(found);
    }

    [Fact]
    public async Task CreateAsync_retries_on_unique_violation()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store;
        const string existingCode = "AAAAAAA";

        // Insert a row directly with the known code to force a unique violation on first attempt.
        await using var conn = await Ds.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO links (code, target_url, owner_key_hash)
            VALUES ($1, $2, $3)
            """;
        cmd.Parameters.AddWithValue(existingCode);
        cmd.Parameters.AddWithValue("https://existing.example.com");
        cmd.Parameters.AddWithValue(Owner1Hash);
        await cmd.ExecuteNonQueryAsync(ct);

        // CreateAsync with the same code should succeed by generating a different code on retry.
        var link = await store.CreateAsync("https://test.example.com", Owner1Hash, existingCode, ct);

        Assert.True(link.Id > 0);
        Assert.NotEqual(existingCode, link.Code);
        Assert.Equal("https://test.example.com", link.TargetUrl);
    }

    [Fact]
    public async Task CreateAsync_concurrent_same_initial_code_both_succeed_with_distinct_codes()
    {
        // Two callers race to insert with the same initial code.
        // One wins the unique constraint; the other catches SqlState 23505 and retries
        // with ShortCode.Generate(). Both should ultimately succeed with different codes.
        // Passes the SAME fixed code at the store level — HTTP-level calls use random codes
        // and cannot reliably reproduce the race condition.
        var ct = TestContext.Current.CancellationToken;
        const string sharedCode = "ZZZZZZZ";

        var task1 = Store.CreateAsync("https://concurrent1.example.com", Owner1Hash, sharedCode, ct);
        var task2 = Store.CreateAsync("https://concurrent2.example.com", Owner1Hash, sharedCode, ct);
        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(2, results.Length);
        Assert.All(results, r => Assert.True(r.Id > 0));
        Assert.NotEqual(results[0].Code, results[1].Code);

        var page = await Store.ListByOwnerAsync(Owner1Hash, page: 1, pageSize: 10, ct);
        Assert.Equal(2, page.Count);
    }
}
