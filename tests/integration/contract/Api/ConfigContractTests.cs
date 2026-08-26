using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// Contract tests for <c>GET /api/config</c> — the public runtime config bundle
/// introduced by customer-provisioning-orchestration-r1 task 087 per spec.md
/// FR-36 + §7.9 close-pattern.
///
/// PATH: <c>tests/integration/contract/Api/</c> — this file lives at the KEEP
/// path for endpoint contract tests per <c>tests/CLAUDE.md</c> §KEEP Paths.
///
/// COVERAGE:
///   1. Anonymous access — no bearer token required (browsers bootstrap before login)
///   2. Response shape stability — camelCase { bffUrl, msalClientId, tenantId, featureFlags }
///   3. Zero-secrets invariant — grep of body for KV refs / connection strings / client secrets
///   4. Cache semantics — Cache-Control: public, max-age=60 + strong ETag
///   5. Conditional GET — If-None-Match returns 304 Not Modified
/// </summary>
[Trait("status", "repaired")]
public class ConfigContractTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebAppFactory _factory;

    public ConfigContractTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetConfig_Anonymous_Returns200()
    {
        // Anonymous — no Authorization header. External-spa + code-pages fetch this
        // BEFORE MSAL init, so requiring a bearer token would break the bootstrap.
        var response = await _client.GetAsync("/api/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetConfig_ResponseShape_ContainsFR36Fields()
    {
        var response = await _client.GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // FR-36 canonical field set — wire names are camelCase (browser convention).
        doc.RootElement.TryGetProperty("bffUrl", out var bffUrl).Should().BeTrue();
        doc.RootElement.TryGetProperty("msalClientId", out var msalClientId).Should().BeTrue();
        doc.RootElement.TryGetProperty("tenantId", out var tenantId).Should().BeTrue();
        doc.RootElement.TryGetProperty("featureFlags", out var featureFlags).Should().BeTrue();

        bffUrl.GetString().Should().Be("https://spaarke-bff-test.example.com");
        msalClientId.GetString().Should().Be("test-app-id");
        tenantId.GetString().Should().Be("test-tenant-id");
        featureFlags.ValueKind.Should().Be(JsonValueKind.Object);
        featureFlags.GetProperty("testFeatureEnabled").GetBoolean().Should().BeTrue();
        featureFlags.GetProperty("testFeatureDisabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetConfig_ResponseBody_ContainsNoSecrets()
    {
        // Binding invariant (POML acceptance): the response body MUST NEVER include
        // secrets, KV references, or connection strings. This guards against future
        // additions accidentally leaking any of those through the options binding.
        var response = await _client.GetAsync("/api/config");
        var body = await response.Content.ReadAsStringAsync();

        // Key Vault reference syntax
        body.Should().NotContain("@Microsoft.KeyVault",
            "the anonymous endpoint MUST NOT surface KV references");

        // Common secret-shaped keys (case-insensitive contains)
        body.ToLowerInvariant().Should().NotContain("clientsecret",
            "'clientSecret' should never appear in the public config bundle");
        body.ToLowerInvariant().Should().NotContain("connectionstring",
            "'connectionString' should never appear in the public config bundle");
        body.ToLowerInvariant().Should().NotContain("password",
            "'password' should never appear in the public config bundle");
        body.ToLowerInvariant().Should().NotContain("apikey",
            "'apiKey' should never appear in the public config bundle");
        body.ToLowerInvariant().Should().NotContain("sharedaccesskey",
            "'sharedAccessKey' should never appear in the public config bundle");
    }

    [Fact]
    public async Task GetConfig_Response_HasCacheControlHeader()
    {
        var response = await _client.GetAsync("/api/config");

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue(
            "browser + CDN caches should share the response (values are public)");
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(60),
            "POML task 087 specifies a 60s short cache");
    }

    [Fact]
    public async Task GetConfig_Response_HasStrongEtag()
    {
        var response = await _client.GetAsync("/api/config");

        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.IsWeak.Should().BeFalse(
            "the body is byte-stable for a given options snapshot, so a strong ETag is correct");
        response.Headers.ETag.Tag.Should().StartWith("\"").And.EndWith("\"",
            "RFC 7232 opaque-string quoting");
    }

    [Fact]
    public async Task GetConfig_WithMatchingIfNoneMatch_Returns304()
    {
        // First request captures the ETag.
        var first = await _client.GetAsync("/api/config");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull();

        // Second request revalidates — expect 304 Not Modified.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/config");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.Tag));

        var second = await _client.SendAsync(request);
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // 304 responses re-emit the ETag + Cache-Control so intermediaries can
        // refresh their caches without a body transfer.
        second.Headers.ETag.Should().NotBeNull();
        second.Headers.CacheControl.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConfig_WithMismatchedIfNoneMatch_Returns200WithBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/config");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"stale-etag-value\""));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeEmpty();
    }
}
