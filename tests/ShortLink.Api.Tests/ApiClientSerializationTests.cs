using System.Net;
using ShortLink.Api.Client;
using ShortLink.Contracts;
#pragma warning disable JSON002 // raw JSON string literals are intentional mock data in this test file

namespace ShortLink.Api.Tests;

/// <summary>
/// Verifies that SliceApiClient serializes CreateLinkRequest with camelCase property names,
/// matching the server's JsonKnownNamingPolicy.CamelCase (AotJsonContext).
///
/// Regression test for: client sending PascalCase "TargetUrl" which the server's camelCase
/// binding treats as null, producing "required" + "not a valid URL" errors on valid input.
/// </summary>
public sealed class ApiClientSerializationTests
{
    [Fact]
    public async Task CreatePublicLinkAsync_SendsRequest_WithCamelCaseJson()
    {
        // Capture the raw request body sent by the generated client.
        string? capturedBody = null;

        var fakeHandler = new CapturingHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();

            // Return a valid 201 so the client does not throw.
            const string responseJson =
                """{"id":1,"code":"abc1234","shortUrl":"http://localhost/r/abc1234","targetUrl":"https://example.com","createdAt":"2024-01-01T00:00:00Z"}""";
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var http = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://localhost") };
        var client = new SliceApiClient(http);
        var ct = TestContext.Current.CancellationToken;

        await client.Links.CreatePublicLinkAsync(
            new CreateLinkRequest { TargetUrl = "https://example.com" }, ct);

        Assert.NotNull(capturedBody);

        // Must be camelCase so the server's camelCase JsonNamingPolicy accepts it.
        Assert.Contains("\"targetUrl\"", capturedBody, StringComparison.Ordinal);

        // Must not be PascalCase — that was the regression.
        Assert.DoesNotContain("\"TargetUrl\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePublicLinkAsync_EmptyTargetUrl_ServerRejectsWithBothValidationErrors()
    {
        // Documents the server contract: empty targetUrl → "required" AND "not a valid URL"
        // (both DataAnnotations run on the empty string, not just Required).
        // This is why two messages appear simultaneously — not just one.
        string? capturedBody = null;

        var fakeHandler = new CapturingHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();

            const string errorJson =
                """{"status":400,"errors":{"TargetUrl":["The TargetUrl field is required.","The TargetUrl field is not a valid fully-qualified http, https, or ftp URL."]}}""";
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var http = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://localhost") };
        var client = new SliceApiClient(http);
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<SliceApiClient.SliceApiException>(
            () => client.Links.CreatePublicLinkAsync(new CreateLinkRequest { TargetUrl = "" }, ct));

        // The client sends the empty value via camelCase, which the server rejects with field errors.
        Assert.NotNull(capturedBody);
        Assert.Contains("\"targetUrl\"", capturedBody, StringComparison.Ordinal);

        // Verify both error messages are surfaced via the exception's Errors dictionary.
        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors.ContainsKey("TargetUrl"));
        Assert.Equal(2, ex.Errors["TargetUrl"].Length);
    }

    // Minimal DelegatingHandler to intercept outgoing requests.
    private sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
