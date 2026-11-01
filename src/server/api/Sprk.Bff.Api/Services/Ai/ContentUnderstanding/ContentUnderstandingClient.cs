using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.ContentUnderstanding;

/// <summary>
/// Thin REST client for Azure AI Content Understanding (ebilling-r1 task 043; FR-21, FR-47, D-49, D-81). Serves the
/// GA (<c>2025-11-01</c>) and preview (<c>2026-06-01-preview</c>) APIs from configuration alone; the request and
/// result shapes it uses are the same on both (measured on dev 2026-10-15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why REST, not the SDK (D-81 condition 3).</b> The GA SDK <c>Azure.AI.ContentUnderstanding</c> 1.1.0 does not
/// cover the preview API, which D-81 makes primary, and a prerelease SDK is not allowed. The two calls needed here
/// (analyze a page range, poll the operation; and GET the analyzer for the health check) are small, so the
/// approved package is not added (NFR-12 delta 0).
/// </para>
/// <para>
/// <b>Auth (ADR-028, FR-47).</b> Every request carries a managed-identity bearer token from the DI
/// <see cref="TokenCredential"/> (scope <see cref="TokenScope"/>); there is no key path. The token is only ever sent to
/// the configured endpoint's host: an <c>Operation-Location</c> on another host is refused.
/// </para>
/// <para>
/// <b>Page ranges and the page limit.</b> A document is analyzed in consecutive ranges of
/// <see cref="ContentUnderstandingOptions.PageRangeSize"/> pages (one call when it fits), with original-file page
/// numbers in every result (verified on both API versions). Input truncation is never enabled: a document longer than
/// <see cref="ContentUnderstandingOptions.MaxPages"/> is refused as <see cref="ContentUnderstandingFailure.PageLimit"/>.
/// The preview reports the page count in <c>metadata.pageCount</c>, so a long document is refused after its first
/// range; GA does not, so on GA the client learns the length as it reads and, on reaching the limit, asks for page
/// MaxPages + 1 (refused at once when it does not exist; when it does, that one-page operation is abandoned unread).
/// </para>
/// <para>
/// <b>Logs (ADR-015).</b> Document id, analyzer, api-version, page range, page count, HTTP status, the service's error
/// CODE and timings only — never document content, field values or service error messages.
/// </para>
/// </remarks>
public sealed class ContentUnderstandingClient
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client.</summary>
    public const string HttpClientName = "ContentUnderstanding";

    /// <summary>Entra scope for Cognitive Services data-plane calls.</summary>
    public const string TokenScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>Retry-After values above this are capped.</summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private const int MaxSubmitAttempts = 3;

    // Content Understanding inner error codes meaning "the document itself cannot be opened". The first two were
    // measured on dev (encrypted and corrupt PDFs -> FailedToExtractPageCount; zero bytes -> ContentEmpty); the other
    // two are the documented content errors, mapped the same way.
    private static readonly HashSet<string> UnreadableCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FailedToExtractPageCount",
        "ContentEmpty",
        "UnsupportedContent",
        "InvalidContent",
    };

    private const string PageCountExceededCode = "InputPageCountExceeded";
    private const string PagesOutOfRangeCode = "InvalidPagesOutOfRange";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly ContentUnderstandingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContentUnderstandingClient> _logger;

    public ContentUnderstandingClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IOptions<ContentUnderstandingOptions> options,
        TimeProvider timeProvider,
        ILogger<ContentUnderstandingClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when <c>ContentUnderstanding:Endpoint</c> is set.</summary>
    public bool IsConfigured => _options.IsConfigured;

    /// <summary>The configured analyzer id.</summary>
    public string AnalyzerId => _options.AnalyzerId;

    /// <summary>The configured api-version.</summary>
    public string ApiVersion => _options.ApiVersion;

    /// <summary>
    /// Analyze one document with the configured analyzer, in page ranges, and return each range's document content.
    /// </summary>
    /// <exception cref="ContentUnderstandingException">The document cannot be opened, is over the page limit, or the service call failed.</exception>
    /// <exception cref="InvalidOperationException">The client is not configured (check <see cref="IsConfigured"/> first).</exception>
    internal async Task<ContentUnderstandingAnalysis> AnalyzeDocumentAsync(
        ReadOnlyMemory<byte> content,
        string contentType,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var endpoint = RequireEndpoint();
        var started = _timeProvider.GetTimestamp();
        var contents = new List<JsonElement>();
        int? totalPages = null;
        var lastPage = 0;
        var start = 1;

        while (true)
        {
            var end = Math.Min(start + _options.PageRangeSize - 1, _options.MaxPages);
            var range = await AnalyzeRangeAsync(endpoint, content, contentType, documentId, start, end, cancellationToken)
                .ConfigureAwait(false);
            if (range is null)
            {
                // The range starts past the last page: the previous range ended exactly on the document's last page.
                break;
            }

            totalPages ??= range.ReportedPageCount;
            if (totalPages > _options.MaxPages)
            {
                throw PageLimit(documentId, totalPages.Value);
            }

            contents.Add(range.Content);
            lastPage = Math.Max(lastPage, range.EndPage ?? end);

            var documentEnded = totalPages is int total ? lastPage >= total : lastPage < end;
            if (documentEnded)
            {
                break;
            }

            if (end >= _options.MaxPages)
            {
                // Read up to the limit without seeing the end (GA reports no page count): does one more page exist?
                if (await PageExistsAsync(endpoint, content, contentType, documentId, _options.MaxPages + 1, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw PageLimit(documentId, pageCount: null);
                }

                break;
            }

            start = end + 1;
        }

        var pageCount = totalPages ?? lastPage;
        _logger.LogInformation(
            "Content Understanding analyzed document {DocumentId}: analyzer={AnalyzerId} apiVersion={ApiVersion} " +
            "pages={PageCount} ranges={RangeCount} elapsedMs={ElapsedMs}",
            documentId, _options.AnalyzerId, _options.ApiVersion, pageCount, contents.Count,
            (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds);

        return new ContentUnderstandingAnalysis(contents, pageCount, _options.AnalyzerId, _options.ApiVersion);
    }

    /// <summary>
    /// GET the configured analyzer (health check). Returns the HTTP status and the analyzer's <c>status</c> when found;
    /// a transport or token failure is reported as <c>null</c> status with the exception type name.
    /// </summary>
    internal async Task<AnalyzerLookup> GetAnalyzerAsync(CancellationToken cancellationToken)
    {
        var endpoint = RequireEndpoint();
        var uri = new Uri(endpoint, $"contentunderstanding/analyzers/{_options.AnalyzerId}?api-version={_options.ApiVersion}");
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, uri, cancellationToken).ConfigureAwait(false);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            string? analyzerStatus = null;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var body = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
                analyzerStatus = body is not null && body.RootElement.ValueKind == JsonValueKind.Object
                    && body.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                        ? s.GetString()
                        : null;
            }

            return new AnalyzerLookup((int)response.StatusCode, analyzerStatus, Failure: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new AnalyzerLookup(StatusCode: null, AnalyzerStatus: null, Failure: ex.GetType().Name);
        }
    }

    // ── one page range ─────────────────────────────────────────────────────────────────────────────────────

    private async Task<RangeResult?> AnalyzeRangeAsync(
        Uri endpoint,
        ReadOnlyMemory<byte> content,
        string contentType,
        Guid documentId,
        int start,
        int end,
        CancellationToken cancellationToken)
    {
        var rangeText = start == end
            ? start.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}";
        var started = _timeProvider.GetTimestamp();

        var operation = await SubmitAsync(endpoint, content, contentType, documentId, rangeText, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
        {
            if (start == 1)
            {
                // A first range can only be "out of range" for a document with no pages.
                throw Fail(ContentUnderstandingFailure.Unreadable, documentId, PagesOutOfRangeCode, httpStatus: 400, rangeText);
            }

            return null;
        }

        using var result = await PollAsync(operation, documentId, rangeText, cancellationToken).ConfigureAwait(false);
        var range = ReadRange(result, documentId, rangeText);

        _logger.LogInformation(
            "Content Understanding range done: document={DocumentId} range={Range} endPage={EndPage} reportedPages={ReportedPages} elapsedMs={ElapsedMs}",
            documentId, rangeText, range.EndPage, range.ReportedPageCount,
            (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds);
        return range;
    }

    /// <summary>POST the analyze request for one range. Returns the operation URI, or null when the range is past the last page.</summary>
    private async Task<Uri?> SubmitAsync(
        Uri endpoint,
        ReadOnlyMemory<byte> content,
        string contentType,
        Guid documentId,
        string rangeText,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            endpoint,
            $"contentunderstanding/analyzers/{_options.AnalyzerId}:analyzeBinary?api-version={_options.ApiVersion}&range={rangeText}");

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = await CreateRequestAsync(HttpMethod.Post, uri, cancellationToken).ConfigureAwait(false);
                request.Content = new ReadOnlyMemoryContent(content);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not ContentUnderstandingException
                                       && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            {
                throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, ex.GetType().Name, httpStatus: null, rangeText, ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    return RequireOperationLocation(response, endpoint, documentId, rangeText);
                }

                if (IsTransient(response.StatusCode) && attempt < MaxSubmitAttempts)
                {
                    await DelayAsync(RetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var code = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
                if (string.Equals(code, PagesOutOfRangeCode, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                throw Fail(Classify(response.StatusCode, code), documentId, code, (int)response.StatusCode, rangeText);
            }
        }
    }

    /// <summary>Does page <paramref name="page"/> exist? A refused range answers no at once; an accepted one is abandoned.</summary>
    private async Task<bool> PageExistsAsync(
        Uri endpoint,
        ReadOnlyMemory<byte> content,
        string contentType,
        Guid documentId,
        int page,
        CancellationToken cancellationToken)
    {
        var operation = await SubmitAsync(
                endpoint, content, contentType, documentId, page.ToString(CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);
        return operation is not null;
    }

    private async Task<JsonDocument> PollAsync(Uri operation, Guid documentId, string rangeText, CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(_options.OperationTimeoutSeconds);
        var delay = TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds);

        while (true)
        {
            if (_timeProvider.GetUtcNow() > deadline)
            {
                throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, "OperationTimedOut", httpStatus: null, rangeText);
            }

            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);

            HttpResponseMessage response;
            try
            {
                using var request = await CreateRequestAsync(HttpMethod.Get, operation, cancellationToken).ConfigureAwait(false);
                response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, ex.GetType().Name, httpStatus: null, rangeText, ex);
            }

            using (response)
            {
                if (IsTransient(response.StatusCode))
                {
                    delay = RetryDelay(response, attempt: 1, fallback: TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds));
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var code = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
                    throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, code, (int)response.StatusCode, rangeText);
                }

                var body = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
                if (body is null || body.RootElement.ValueKind != JsonValueKind.Object)
                {
                    body?.Dispose();
                    throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, "MalformedOperation", 200, rangeText);
                }

                var status = body.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;

                if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    return body;
                }

                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))
                {
                    var code = body.RootElement.TryGetProperty("error", out var error) ? DeepestErrorCode(error) : null;
                    body.Dispose();
                    throw Fail(
                        status!.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
                            ? ContentUnderstandingFailure.ServiceFailed
                            : Classify(HttpStatusCode.OK, code),
                        documentId, code ?? status, 200, rangeText);
                }

                body.Dispose();
                delay = RetryDelay(response, attempt: 1, fallback: TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds));
            }
        }
    }

    // ── result ─────────────────────────────────────────────────────────────────────────────────────────────

    private RangeResult ReadRange(JsonDocument result, Guid documentId, string rangeText)
    {
        var root = result.RootElement;
        if (!root.TryGetProperty("result", out var analyzeResult) || analyzeResult.ValueKind != JsonValueKind.Object)
        {
            throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, "MalformedResult", 200, rangeText);
        }

        // One input, no routing: the document content is the first content of kind "document" (or the first content).
        JsonElement? document = null;
        if (analyzeResult.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in contents.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                document ??= c;
                if (c.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                    && string.Equals(kind.GetString(), "document", StringComparison.OrdinalIgnoreCase))
                {
                    document = c;
                    break;
                }
            }
        }

        // A succeeded operation always returns a document content with a fields object, even for a blank page (each
        // field then carries only its type; dev evidence 2026-10, both api-versions). A result without one is a
        // service fault, not "no invoice data": reading it as empty would report a real invoice as not an invoice.
        if (document is not JsonElement content
            || !content.TryGetProperty("fields", out var fields)
            || fields.ValueKind != JsonValueKind.Object)
        {
            throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, "MalformedResult", 200, rangeText);
        }

        return new RangeResult(content.Clone(), ReadInt(content, "endPageNumber"), ReadReportedPageCount(content));
    }

    private static int? ReadReportedPageCount(JsonElement content)
    {
        // Preview only: metadata.pageCount, returned as a string ("26") on 2026-06-01-preview.
        if (!content.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("pageCount", out var pageCount))
        {
            return null;
        }

        return pageCount.ValueKind switch
        {
            JsonValueKind.Number when pageCount.TryGetInt32(out var n) && n > 0 => n,
            JsonValueKind.String when int.TryParse(pageCount.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 => n,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)
            ? n
            : null;

    // ── HTTP helpers ───────────────────────────────────────────────────────────────────────────────────────

    private Uri RequireEndpoint()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("ContentUnderstanding:Endpoint is not configured.");
        }

        // Validated at startup (absolute https). A trailing slash makes relative paths resolve under it.
        var text = _options.Endpoint!.Trim();
        return new Uri(text.EndsWith('/') ? text : text + "/", UriKind.Absolute);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, Uri uri, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { TokenScope }), cancellationToken)
            .ConfigureAwait(false);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);

    private Uri RequireOperationLocation(HttpResponseMessage response, Uri endpoint, Guid documentId, string rangeText)
    {
        var value = response.Headers.TryGetValues("Operation-Location", out var values) ? values.FirstOrDefault() : null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var operation)
            || operation.Scheme != Uri.UriSchemeHttps
            || !string.Equals(operation.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            // Never send the managed-identity token anywhere but the configured endpoint.
            throw Fail(ContentUnderstandingFailure.ServiceFailed, documentId, "UnexpectedOperationLocation", 202, rangeText);
        }

        return operation;
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
            or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError;

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt, TimeSpan? fallback = null)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? delay = retryAfter?.Delta;
        if (delay is null && retryAfter?.Date is DateTimeOffset date)
        {
            delay = date - _timeProvider.GetUtcNow();
        }

        delay ??= fallback ?? TimeSpan.FromSeconds(2 * attempt);
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        return delay > MaxRetryDelay ? MaxRetryDelay : delay.Value;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, _timeProvider, cancellationToken);

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var body = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return body is not null && body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("error", out var error)
                ? DeepestErrorCode(error)
                : null;
    }

    /// <summary>The innermost <c>code</c> of an error / innererror chain (the specific one, e.g. FailedToExtractPageCount).</summary>
    private static string? DeepestErrorCode(JsonElement error)
    {
        string? code = null;
        var current = error;
        for (var depth = 0; depth < 8 && current.ValueKind == JsonValueKind.Object; depth++)
        {
            if (current.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
            {
                code = c.GetString();
            }

            if (!current.TryGetProperty("innererror", out current))
            {
                break;
            }
        }

        return code;
    }

    private static ContentUnderstandingFailure Classify(HttpStatusCode status, string? code)
    {
        if (string.Equals(code, PageCountExceededCode, StringComparison.OrdinalIgnoreCase))
        {
            return ContentUnderstandingFailure.PageLimit;
        }

        // 413: the file itself is over the service's request size, so the failure is the document's, not the service's.
        if (status is HttpStatusCode.UnsupportedMediaType or HttpStatusCode.RequestEntityTooLarge
            || (code is not null && UnreadableCodes.Contains(code)))
        {
            return ContentUnderstandingFailure.Unreadable;
        }

        return ContentUnderstandingFailure.ServiceFailed;
    }

    private ContentUnderstandingException PageLimit(Guid documentId, int? pageCount)
    {
        _logger.LogWarning(
            "Content Understanding refused document {DocumentId}: pages={PageCount} exceed MaxPages={MaxPages}",
            documentId, pageCount, _options.MaxPages);
        return new ContentUnderstandingException(
            ContentUnderstandingFailure.PageLimit, PageCountExceededCode, httpStatus: null,
            $"The document has more than {_options.MaxPages} pages.");
    }

    private ContentUnderstandingException Fail(
        ContentUnderstandingFailure failure,
        Guid documentId,
        string? serviceCode,
        int? httpStatus,
        string rangeText,
        Exception? inner = null)
    {
        _logger.LogWarning(
            "Content Understanding call failed: document={DocumentId} analyzer={AnalyzerId} apiVersion={ApiVersion} " +
            "range={Range} failure={Failure} httpStatus={HttpStatus} serviceCode={ServiceCode}",
            documentId, _options.AnalyzerId, _options.ApiVersion, rangeText, failure, httpStatus, serviceCode);
        return new ContentUnderstandingException(
            failure, serviceCode, httpStatus,
            $"Content Understanding {failure} (HTTP {httpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"}, code {serviceCode ?? "-"}).",
            inner);
    }

    private sealed record RangeResult(JsonElement Content, int? EndPage, int? ReportedPageCount);
}

/// <summary>Where a Content Understanding read failed.</summary>
internal enum ContentUnderstandingFailure
{
    /// <summary>The document cannot be opened (encrypted, corrupt, empty).</summary>
    Unreadable,

    /// <summary>The document is longer than the page limit.</summary>
    PageLimit,

    /// <summary>The call to the service failed.</summary>
    ServiceFailed,
}

/// <summary>A failed Content Understanding read. Carries the service's error CODE only, never its message or content.</summary>
internal sealed class ContentUnderstandingException : Exception
{
    public ContentUnderstandingException(
        ContentUnderstandingFailure failure, string? serviceCode, int? httpStatus, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        ServiceCode = serviceCode;
        HttpStatus = httpStatus;
    }

    public ContentUnderstandingFailure Failure { get; }

    public string? ServiceCode { get; }

    public int? HttpStatus { get; }
}

/// <summary>The document contents of every analyzed range, in page order.</summary>
internal sealed record ContentUnderstandingAnalysis(
    IReadOnlyList<JsonElement> Contents,
    int PageCount,
    string AnalyzerId,
    string ApiVersion);

/// <summary>Result of the analyzer GET used by the health check.</summary>
internal sealed record AnalyzerLookup(int? StatusCode, string? AnalyzerStatus, string? Failure);
