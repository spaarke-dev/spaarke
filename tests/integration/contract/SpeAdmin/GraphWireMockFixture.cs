using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Stands up a fake Microsoft Graph endpoint on localhost and hands out a real
/// <see cref="GraphServiceClient"/> pointed at it, so SpeAdmin production code can be exercised
/// end-to-end over HTTP with no tenant, no credentials, and no network.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The 359 tests across the 14 SpeAdmin test files make no HTTP call and
/// stand up no host, so the Graph interaction — which is the substance of the app — had zero
/// automated coverage. Most of <c>SpeAdminGraphService</c> is request-building and response-mapping;
/// mapping is exactly what an HTTP-boundary fake can check and what a mock of the service cannot.
/// </para>
/// <para>
/// <b>The seam.</b> No production change was needed. 47 methods on <c>SpeAdminGraphService</c> already
/// take <c>GraphServiceClient graphClient</c> as their first parameter (the DI-held client is built by
/// private <c>CreateGraphClient*</c> helpers that hardcode <c>https://graph.microsoft.com/beta</c>, but
/// those are only used by the <c>GetClientForConfigAsync</c> path). A test constructs the client here
/// and passes it straight in. Whether the production base address should become configurable is
/// task 021's decision, deliberately not pre-empted here.
/// </para>
/// <para>
/// <b>Both halves of the exchange are assertable.</b> <see cref="RequestsFor"/> exposes the outgoing
/// request (path + raw query, so a <c>$select</c> field set can be asserted exactly), and the canned
/// response drives production's mapping code. The §3.2 defect class — a property name that does not
/// exist on the real API — is a defect in the REQUEST, so asserting only the response mapping would
/// miss it. That is why both directions are first-class here.
/// </para>
/// <para>
/// <b>Authoring canned responses.</b> Write the JSON with the property names Microsoft documents for
/// the endpoint, never with the names our code happens to use. The whole point is to fail when those
/// two disagree — a fixture authored from our own code would agree with our bugs.
/// </para>
/// <para>
/// <b>Prerequisite this fixture depends on.</b> WireMock.Net 1.5.45 loads <c>MimeKitLite</c> at runtime
/// while parsing request bodies. The test project previously carried
/// <c>&lt;ExcludeAssets&gt;all&lt;/ExcludeAssets&gt;</c> on that package, which stripped the DLL from the
/// output and made every WireMock request fail with 500 inside its GlobalExceptionMiddleware. That is
/// the real reason the six tests in <c>Integration/GraphApiWireMockTests.cs</c> were skipped as a
/// "path matching" problem. The csproj now excludes only the <c>compile</c> asset. If WireMock ever
/// starts returning a blanket 500 again, check that first.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var graph = new GraphWireMockFixture();
/// graph.StubGet("/storage/fileStorage/containers", """{"value":[{"id":"c1","displayName":"Matters"}]}""");
///
/// var result = await sut.ListContainersAsync(graph.CreateGraphClient(), containerTypeId);
///
/// graph.SelectFieldsFor("/storage/fileStorage/containers").Should().BeEquivalentTo("id", "displayName");
/// result.Single().DisplayName.Should().Be("Matters");
/// </code>
/// </example>
public sealed class GraphWireMockFixture : IDisposable
{
    private readonly WireMockServer _server;

    public GraphWireMockFixture()
    {
        _server = WireMockServer.Start();
    }

    /// <summary>Root URL of the fake Graph endpoint, e.g. <c>http://localhost:51234</c>.</summary>
    public string BaseUrl => _server.Urls[0];

    /// <summary>
    /// Builds a real <see cref="GraphServiceClient"/> whose base address is this fake endpoint.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="AnonymousAuthenticationProvider"/> deliberately: it attaches no token and
    /// performs no host validation, so nothing here can reach Entra ID or a real tenant even by
    /// accident. Production's own auth providers are out of scope for a mapping test — what is under
    /// test is the request the SDK emits and the mapping of the response, not how a token was obtained.
    /// </remarks>
    public GraphServiceClient CreateGraphClient()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return new GraphServiceClient(httpClient, new AnonymousAuthenticationProvider(), BaseUrl);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Stubbing — the RESPONSE half
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serves <paramref name="jsonBody"/> for any GET whose path starts with <paramref name="pathPrefix"/>.
    /// </summary>
    /// <param name="pathPrefix">
    /// Path only, no query string — Graph query options are matched separately so a test can assert
    /// them rather than having to reproduce them to get a match.
    /// </param>
    /// <param name="jsonBody">Canned response, authored from the documented Graph schema.</param>
    /// <param name="statusCode">HTTP status to return. Use 4xx/5xx to exercise error translation.</param>
    public GraphWireMockFixture StubGet(string pathPrefix, string jsonBody, int statusCode = 200)
    {
        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody));

        return this;
    }

    /// <summary>Serves <paramref name="jsonBody"/> for a PATCH — used to cover update-property paths.</summary>
    public GraphWireMockFixture StubPatch(string pathPrefix, string jsonBody, int statusCode = 200)
    {
        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingPatch())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody));

        return this;
    }

    /// <summary>
    /// Serves <paramref name="jsonBody"/> for a POST — used for <c>/search/query</c> and other
    /// action endpoints where the REQUEST BODY is the thing worth asserting.
    /// </summary>
    /// <remarks>
    /// Pair with <see cref="RecordedGraphRequest.BodyAsJson"/>. Graph's search API takes its entity
    /// types, query, and field list in the body rather than the query string, so for those endpoints
    /// the body is where the wrong-property-name defect class lives.
    /// </remarks>
    public GraphWireMockFixture StubPost(string pathPrefix, string jsonBody, int statusCode = 200)
    {
        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody));

        return this;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Observation — the REQUEST half
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Every request the fake endpoint received, in order.</summary>
    public IReadOnlyList<RecordedGraphRequest> AllRequests =>
        _server.LogEntries
            .Select(e => new RecordedGraphRequest(
                e.RequestMessage.Method,
                e.RequestMessage.Path,
                e.RequestMessage.RawQuery ?? string.Empty,
                e.RequestMessage.Body))
            .ToList();

    /// <summary>Requests whose path starts with <paramref name="pathPrefix"/>, in order.</summary>
    public IReadOnlyList<RecordedGraphRequest> RequestsFor(string pathPrefix) =>
        AllRequests.Where(r => r.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// The <c>$select</c> field set from the first request to <paramref name="pathPrefix"/>.
    /// </summary>
    /// <remarks>
    /// This is the assertion that catches the §3.2 defect class. It returns the fields as an
    /// unordered set so a test asserts WHICH fields were requested, not the order the SDK happened to
    /// serialize them in — order is not part of the OData contract and pinning it would produce a
    /// brittle test that fails on SDK upgrades without any real defect.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No request reached <paramref name="pathPrefix"/>.</exception>
    public IReadOnlyList<string> SelectFieldsFor(string pathPrefix)
    {
        var matching = RequestsFor(pathPrefix);
        if (matching.Count == 0)
        {
            var seen = AllRequests.Count == 0
                ? "(the fake endpoint received no requests at all)"
                : string.Join(", ", AllRequests.Select(r => $"{r.Method} {r.Path}"));
            throw new InvalidOperationException(
                $"No request was made to '{pathPrefix}'. Requests seen: {seen}");
        }

        return ParseSelect(matching[0].RawQuery);
    }

    /// <summary>Extracts and splits the <c>$select</c> value out of a raw query string.</summary>
    internal static IReadOnlyList<string> ParseSelect(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return Array.Empty<string>();
        }

        foreach (var pair in rawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separator]);
            if (!key.Equals("$select", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(pair[(separator + 1)..])
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return Array.Empty<string>();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }
}

/// <summary>One request observed by <see cref="GraphWireMockFixture"/>.</summary>
/// <param name="Method">HTTP method, e.g. <c>GET</c>.</param>
/// <param name="Path">Path with no query string, e.g. <c>/storage/fileStorage/containers</c>.</param>
/// <param name="RawQuery">Raw query string including the leading <c>?</c>, or empty.</param>
/// <param name="Body">Raw request body, or null for bodyless requests.</param>
public sealed record RecordedGraphRequest(string Method, string Path, string RawQuery, string? Body)
{
    /// <summary>The request body parsed as JSON. Use to assert PATCH property names.</summary>
    /// <exception cref="InvalidOperationException">The request had no body.</exception>
    public JsonElement BodyAsJson() => string.IsNullOrWhiteSpace(Body)
        ? throw new InvalidOperationException($"{Method} {Path} had no request body to parse.")
        : JsonDocument.Parse(Body).RootElement.Clone();
}
