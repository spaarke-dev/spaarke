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
/// those are only used by the <c>GetClientForContainerAsync</c> path). A test constructs the client here
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
    /// Clears every stub and every recorded request — for a fixture shared by the tests of one class
    /// (unified-access-control-r2 task 165's host fixture keeps one fake Graph for its whole host).
    /// </summary>
    public void Reset() => _server.Reset();

    /// <summary>
    /// Builds a real <see cref="GraphServiceClient"/> whose base address is this fake endpoint.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="AnonymousAuthenticationProvider"/> deliberately: it attaches no token and
    /// performs no host validation, so nothing here can reach Entra ID or a real tenant even by
    /// accident. Production's own auth providers are out of scope for a mapping test — what is under
    /// test is the request the SDK emits and the mapping of the response, not how a token was obtained.
    /// </remarks>
    /// <param name="pipeline">A handler to send through, as production's <c>GraphApiClient</c> does (#1755: the Graph
    /// resilience handler). Its inner handler is set here. None by default: requests go straight to the fake.</param>
    public GraphServiceClient CreateGraphClient(DelegatingHandler? pipeline = null) =>
        CreateGraphClient(CreateHttpClient(pipeline));

    /// <summary>
    /// A <see cref="GraphServiceClient"/> over <paramref name="httpClient"/> — for a test that sends through the same
    /// pipeline before the client does (#1755 round 2: a request that opens the handler's circuit first).
    /// <paramref name="auth"/> replaces the anonymous provider (a token provider that fails, inside the SDK's send).
    /// </summary>
    public GraphServiceClient CreateGraphClient(HttpClient httpClient, IAuthenticationProvider? auth = null) =>
        new(httpClient, auth ?? new AnonymousAuthenticationProvider(), BaseUrl);

    /// <summary>An <see cref="HttpClient"/> on this fake, through <paramref name="pipeline"/> if given (its inner handler is set here).</summary>
    public HttpClient CreateHttpClient(DelegatingHandler? pipeline = null)
    {
        HttpClient httpClient;
        if (pipeline is null)
        {
            httpClient = new HttpClient();
        }
        else
        {
            pipeline.InnerHandler = new HttpClientHandler();
            httpClient = new HttpClient(pipeline);
        }

        httpClient.BaseAddress = new Uri(BaseUrl);
        return httpClient;
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

    /// <summary>
    /// Serves <paramref name="jsonBody"/> for a GET on EXACTLY <paramref name="path"/> (query string aside) — for a
    /// collection whose path is a prefix of its items' paths (<c>/containers</c> vs <c>/containers/{id}</c>), where
    /// <see cref="StubGet"/>'s prefix match would answer the items too.
    /// </summary>
    public GraphWireMockFixture StubGetExact(string path, string jsonBody, int statusCode = 200)
    {
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody));

        return this;
    }

    /// <summary>
    /// As <see cref="StubGetExact"/>, but REPLACES the previous <c>ReplaceGetExact</c> stub of the same path (one mapping
    /// per path, keyed by a GUID derived from it) — for a collection a test grows item by item, such as the recycle bin's
    /// <c>deletedContainers</c> list. Do not mix it with <see cref="StubGetExact"/> on the same path.
    /// </summary>
    public GraphWireMockFixture ReplaceGetExact(string path, string jsonBody, int statusCode = 200)
    {
        var key = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("GET-exact:" + path)));
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .WithGuid(key)
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody));

        return this;
    }

    /// <summary>
    /// Serves <paramref name="jsonBody"/> for a POST on EXACTLY <paramref name="path"/> (see <see cref="StubGetExact"/>).
    /// <paramref name="delay"/> holds the answer back, as on <see cref="StubPatch"/>. <paramref name="onReceived"/> runs
    /// when the request arrives, before the answer is sent: a test acts mid-request without racing a timer (#1755).
    /// </summary>
    /// <param name="retryAfterSeconds">Sent as a <c>Retry-After</c> header (a 429's backoff — #1755 round 2).</param>
    public GraphWireMockFixture StubPostExact(
        string path, string jsonBody, int statusCode = 200, TimeSpan? delay = null, Action? onReceived = null,
        int? retryAfterSeconds = null)
    {
        var response = onReceived is null
            ? Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(jsonBody)
            : Response.Create().WithCallback(_ =>
            {
                onReceived();
                return new WireMock.ResponseMessage
                {
                    StatusCode = statusCode,
                    Headers = new Dictionary<string, WireMock.Types.WireMockList<string>>
                    {
                        ["Content-Type"] = new("application/json")
                    },
                    BodyData = new WireMock.Util.BodyData
                    {
                        BodyAsString = jsonBody,
                        DetectedBodyType = WireMock.Types.BodyType.String,
                        Encoding = System.Text.Encoding.UTF8
                    }
                };
            });
        if (delay is { } d)
        {
            response = response.WithDelay(d);
        }

        if (retryAfterSeconds is { } retryAfter)
        {
            response = response.WithHeader("Retry-After", retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        _server
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(response);

        return this;
    }

    /// <summary>
    /// Serves a DIFFERENT response to each successive POST on EXACTLY <paramref name="path"/>, in order; the last one
    /// repeats. Used to show what a retrying client sends (#1755: a 429, then the create's 201).
    /// </summary>
    public GraphWireMockFixture StubPostExactSequence(string path, params (string Body, int StatusCode)[] responses)
    {
        if (responses.Length == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }

        var scenario = $"post-seq:{path}:{responses.Length}";
        for (var i = 0; i < responses.Length; i++)
        {
            var step = _server.Given(Request.Create().WithPath(path).UsingPost()).InScenario(scenario);
            if (i > 0)
            {
                step = step.WhenStateIs($"{scenario}#{i}");
            }

            if (i < responses.Length - 1)
            {
                step = step.WillSetStateTo($"{scenario}#{i + 1}");
            }

            step.RespondWith(Response.Create()
                .WithStatusCode(responses[i].StatusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responses[i].Body));
        }

        return this;
    }

    /// <summary>
    /// Serves a DIFFERENT response to each successive GET on <paramref name="pathPrefix"/>, in order.
    /// The final response repeats for any further calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required by any production path that reads a resource, mutates it, and re-reads to verify —
    /// which is the shape task 052's permanent delete is obliged to use, because Graph answers
    /// <c>204</c> whether it purged everything, some, or nothing. A single fixed stub would return the
    /// same bin contents before and after the delete, so a test built on <see cref="StubGet"/> could
    /// only ever assert "nothing was purged" and could never distinguish a working verification from
    /// a broken one.
    /// </para>
    /// <para>
    /// Implemented with WireMock scenarios (a state machine per stub), so ordering is enforced by the
    /// fake endpoint rather than by the test guessing how many calls production makes.
    /// </para>
    /// </remarks>
    /// <param name="responses">Response bodies with their status codes, in call order.</param>
    public GraphWireMockFixture StubGetSequence(
        string pathPrefix, params (string Body, int StatusCode)[] responses)
    {
        if (responses.Length == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }

        // Scenario names must be unique per stub or two sequences on different paths share state.
        var scenario = $"seq:{pathPrefix}:{responses.Length}";

        for (var i = 0; i < responses.Length; i++)
        {
            var step = _server
                .Given(Request.Create()
                    .WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*"))
                    .UsingGet())
                .InScenario(scenario);

            if (i > 0)
            {
                step = step.WhenStateIs($"{scenario}#{i}");
            }

            // The last response deliberately does NOT advance, so extra calls repeat it rather than
            // falling through to an unstubbed 404 that would look like a production defect.
            if (i < responses.Length - 1)
            {
                step = step.WillSetStateTo($"{scenario}#{i + 1}");
            }

            step.RespondWith(Response.Create()
                .WithStatusCode(responses[i].StatusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responses[i].Body));
        }

        return this;
    }

    /// <summary>
    /// Serves <paramref name="jsonBody"/> for a PATCH — used to cover update-property paths. <paramref name="delay"/>
    /// holds the answer back, to cover a caller that gives up while the PATCH is in flight.
    /// </summary>
    public GraphWireMockFixture StubPatch(string pathPrefix, string jsonBody, int statusCode = 200, TimeSpan? delay = null)
    {
        var response = Response.Create()
            .WithStatusCode(statusCode)
            .WithHeader("Content-Type", "application/json")
            .WithBody(jsonBody);
        if (delay is { } d)
        {
            response = response.WithDelay(d);
        }

        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingPatch())
            .RespondWith(response);

        return this;
    }

    /// <summary>
    /// Serves a DIFFERENT response to each successive PATCH on <paramref name="pathPrefix"/>, in order; the last one
    /// repeats. Used where two writes share a path — the business-unit stamp, then the ownership marker (#2032).
    /// </summary>
    public GraphWireMockFixture StubPatchSequence(string pathPrefix, params (string Body, int StatusCode)[] responses)
    {
        if (responses.Length == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }

        var scenario = $"patch-seq:{pathPrefix}:{responses.Length}";
        for (var i = 0; i < responses.Length; i++)
        {
            var step = _server
                .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingPatch())
                .InScenario(scenario);
            if (i > 0)
            {
                step = step.WhenStateIs($"{scenario}#{i}");
            }

            if (i < responses.Length - 1)
            {
                step = step.WillSetStateTo($"{scenario}#{i + 1}");
            }

            step.RespondWith(Response.Create()
                .WithStatusCode(responses[i].StatusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responses[i].Body));
        }

        return this;
    }

    /// <summary>
    /// Serves <paramref name="jsonBody"/> for a PUT — used for create-or-replace resources such as an
    /// <c>applicationPermissionGrant</c>, where the VERB is part of the contract (task 041 measured the
    /// POST alternative failing live).
    /// </summary>
    public GraphWireMockFixture StubPut(string pathPrefix, string jsonBody, int statusCode = 201)
    {
        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingPut())
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

    /// <summary>
    /// Serves a response for a DELETE — used where the thing worth asserting is that the right
    /// resource was targeted with the right verb.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>204 No Content</c>, which is what Graph actually returns for a successful
    /// revoke. That default matters: a stub returning 200-with-a-body would let a caller that
    /// mishandles an empty response pass here and fail in production.
    /// </remarks>
    public GraphWireMockFixture StubDelete(string pathPrefix, string jsonBody = "", int statusCode = 204)
    {
        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher($"{pathPrefix}*")).UsingDelete())
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

    /// <summary>
    /// PATCH requests whose path starts with <paramref name="pathPrefix"/>, in order.
    /// </summary>
    /// <remarks>
    /// A container-type settings write is a read-modify-write — it GETs the resource to obtain the
    /// <c>etag</c> that Graph's Update API requires in the body, then PATCHes. So
    /// <see cref="RequestsFor"/> legitimately returns two requests, and an assertion that means
    /// "the write we sent" has to say so. Without this, a test asserting <c>.Single()</c> fails on
    /// the GET, and — worse — a test asserting "no write was attempted" would pass or fail for the
    /// wrong reason.
    /// </remarks>
    public IReadOnlyList<RecordedGraphRequest> PatchRequestsFor(string pathPrefix) =>
        RequestsFor(pathPrefix)
            .Where(r => string.Equals(r.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
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

/// <summary>
/// An <see cref="Sprk.Bff.Api.Infrastructure.Graph.IGraphClientFactory"/> whose app-only client is one a test built on a
/// <see cref="GraphWireMockFixture"/> — for production classes that ask the factory for their client (#1755: the
/// provisioning container create). Delegated clients are refused: these paths are app-only.
/// </summary>
internal sealed class WireMockGraphClientFactory(Func<GraphServiceClient> appOnly)
    : Sprk.Bff.Api.Infrastructure.Graph.IGraphClientFactory
{
    public GraphServiceClient ForApp() => appOnly();

    public Task<GraphServiceClient> ForUserAsync(Microsoft.AspNetCore.Http.HttpContext ctx, CancellationToken ct = default) =>
        throw new InvalidOperationException("This path is app-only.");

    public Task<GraphServiceClient> ForUserBetaAsync(Microsoft.AspNetCore.Http.HttpContext ctx, CancellationToken ct = default) =>
        throw new InvalidOperationException("This path is app-only.");
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
