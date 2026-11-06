using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;

namespace Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;

/// <summary>
/// Test doubles at the Content Understanding HTTP boundary (ebilling-r1 task 043). The only doubled boundary is the
/// wire: a hand-written <see cref="HttpMessageHandler"/> that answers like the service (recorded bodies, measured error
/// codes), a fixed <see cref="TokenCredential"/> and a <see cref="FakeTimeProvider"/>. Everything from
/// <c>InvoiceAi.ReadInvoiceAsync</c> down to the HTTP request is production code.
/// </summary>
internal static class ContentUnderstandingTestHost
{
    public const string Endpoint = "https://cu-test.cognitiveservices.azure.com/";
    public const string Token = "test-mi-token";

    public static ContentUnderstandingOptions PreviewOptions(Action<ContentUnderstandingOptions>? configure = null)
    {
        var options = new ContentUnderstandingOptions
        {
            Endpoint = Endpoint,
            ApiVersion = "2026-06-01-preview",
            AnalyzerId = "sprk_invoice_v2p",
            // Below the validator's 250 ms floor on purpose: these options are built directly (no ValidateOnStart),
            // and a zero delay keeps the polling tests from waiting on the fake clock.
            PollIntervalMilliseconds = 0,
        };
        configure?.Invoke(options);
        return options;
    }

    public static ContentUnderstandingOptions GaOptions(Action<ContentUnderstandingOptions>? configure = null)
    {
        var options = PreviewOptions();
        options.ApiVersion = "2025-11-01";
        options.AnalyzerId = "sprk_invoice_v2";
        configure?.Invoke(options);
        return options;
    }

    public static ContentUnderstandingClient Client(
        ContentUnderstandingOptions options,
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null) =>
        new(
            new SingleHandlerFactory(handler),
            new FixedTokenCredential(),
            Options.Create(options),
            timeProvider ?? new FakeTimeProvider(),
            NullLogger<ContentUnderstandingClient>.Instance);

    /// <summary>A client with no endpoint: the reader is "not configured". Any HTTP call fails the test.</summary>
    public static ContentUnderstandingClient Unconfigured() =>
        Client(new ContentUnderstandingOptions(), new FakeContentUnderstandingService());

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>An error body as the service returns it: top-level code, the specific code in innererror.</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, string code, string? innerCode = null) =>
        JsonResponse(status, innerCode is null
            ? $$$"""{"error":{"code":"{{{code}}}","message":"m"}}"""
            : $$$$"""{"error":{"code":"{{{{code}}}}","message":"m","innererror":{"code":"{{{{innerCode}}}}","message":"m"}}}""");

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(Token, DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            if (requestContext.Scopes.Length != 1 || requestContext.Scopes[0] != ContentUnderstandingClient.TokenScope)
            {
                throw new InvalidOperationException("Unexpected token scope.");
            }

            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }
}

/// <summary>One request the fake service saw (no body kept, only its length).</summary>
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, string? Authorization, int BodyLength, string? ContentType);

/// <summary>
/// A scripted Content Understanding service. <see cref="OnAnalyze"/> answers each analyze POST (given the requested
/// range text, for example "1-20"); returning null answers 202 with an operation on the endpoint's host whose result is
/// <see cref="OnResult"/> for that range. <see cref="OnPoll"/>, when set, answers polls before the result is served.
/// Unscripted requests answer 418, so a test fails loudly on a call it did not expect.
/// </summary>
internal sealed class FakeContentUnderstandingService : HttpMessageHandler
{
    private readonly Dictionary<string, string> _operations = new(StringComparer.Ordinal);

    public List<SeenRequest> Requests { get; } = new();

    public Func<string, HttpResponseMessage?> OnAnalyze { get; set; } = _ => null;

    public Func<string, HttpResponseMessage> OnResult { get; set; } =
        _ => new HttpResponseMessage((HttpStatusCode)418);

    public Func<string, int, HttpResponseMessage?>? OnPoll { get; set; }

    /// <summary>Operation-Location host override (to test the same-host rule).</summary>
    public string OperationHost { get; set; } = new Uri(ContentUnderstandingTestHost.Endpoint).Host;

    public IEnumerable<SeenRequest> Analyzes => Requests.Where(r => r.Method == HttpMethod.Post);

    public IEnumerable<string> AnalyzedRanges => Analyzes.Select(r => RangeOf(r.Uri));

    private readonly Dictionary<string, int> _polls = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? 0 : (await request.Content.ReadAsByteArrayAsync(cancellationToken)).Length;
        Requests.Add(new SeenRequest(
            request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body,
            request.Content?.Headers.ContentType?.MediaType));

        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && path.EndsWith(":analyzeBinary", StringComparison.Ordinal))
        {
            var range = RangeOf(request.RequestUri);
            var scripted = OnAnalyze(range);
            if (scripted is not null)
            {
                return scripted;
            }

            var id = $"op-{_operations.Count + 1}";
            _operations[id] = range;
            var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
            accepted.Headers.Add(
                "Operation-Location",
                $"https://{OperationHost}/contentunderstanding/analyzerResults/{id}?api-version=x");
            return accepted;
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/contentunderstanding/analyzerResults/", StringComparison.Ordinal))
        {
            var id = path[(path.LastIndexOf('/') + 1)..];
            if (!_operations.TryGetValue(id, out var range))
            {
                return ContentUnderstandingTestHost.Error(HttpStatusCode.NotFound, "NotFound", "OperationNotFound");
            }

            _polls[id] = _polls.GetValueOrDefault(id) + 1;
            var early = OnPoll?.Invoke(range, _polls[id]);
            return early ?? OnResult(range);
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/contentunderstanding/analyzers/", StringComparison.Ordinal)
            && OnGetAnalyzer is not null)
        {
            return OnGetAnalyzer(path["/contentunderstanding/analyzers/".Length..]);
        }

        return new HttpResponseMessage((HttpStatusCode)418);
    }

    /// <summary>Answers the analyzer GET (health check), given the analyzer id in the path.</summary>
    public Func<string, HttpResponseMessage>? OnGetAnalyzer { get; set; }

    public static string RangeOf(Uri uri)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith("range=", StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(part["range=".Length..]);
            }
        }

        return string.Empty;
    }
}
