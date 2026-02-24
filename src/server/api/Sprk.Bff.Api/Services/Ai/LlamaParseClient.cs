using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// HTTP client for the LlamaParse document-parsing REST API.
/// </summary>
/// <remarks>
/// LlamaParse is an optional enhancement for complex legal documents (contracts, leases,
/// heavily-scanned, multi-table). It is disabled by default
/// (<see cref="LlamaParseOptions.Enabled"/> = false).
///
/// Parse flow:
/// 1. POST /api/parsing/upload — upload document bytes; receive a job ID.
/// 2. GET  /api/parsing/job/{id} — poll until status is SUCCESS or ERROR.
/// 3. GET  /api/parsing/job/{id}/result/text — fetch plain-text result.
///
/// The caller (DocumentParserRouter) is responsible for checking
/// <see cref="LlamaParseOptions.Enabled"/> before calling this client.
///
/// ADR-007: document bytes arrive from SpeFileStore via the caller; this client never
/// accesses storage.
/// ADR-010: registered via AddHttpClient&lt;LlamaParseClient&gt; — IHttpClientFactory manages
/// the underlying HttpClient lifetime.
/// </remarks>
public class LlamaParseClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlamaParseOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlamaParseClient> _logger;

    // HTTP client name used for IHttpClientFactory registration.
    internal const string HttpClientName = "LlamaParseClient";

    // Polling interval between job-status checks.
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);

    // JSON options shared across serialisation calls.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Initialises a new <see cref="LlamaParseClient"/>.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create the named HTTP client.</param>
    /// <param name="options">LlamaParse feature options.</param>
    /// <param name="configuration">Application configuration (used to resolve the API key from Key Vault at runtime).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public LlamaParseClient(
        IHttpClientFactory httpClientFactory,
        IOptions<LlamaParseOptions> options,
        IConfiguration configuration,
        ILogger<LlamaParseClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Uploads a document to LlamaParse and polls until parsing completes, then returns
    /// the structured result.
    /// </summary>
    /// <param name="content">Raw document bytes.</param>
    /// <param name="fileName">
    /// File name (including extension) used by LlamaParse to determine the document type.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ParsedDocument"/> with the extracted text and metadata.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when LlamaParse returns a non-success job status or when the HTTP call fails.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when parsing does not complete within <see cref="LlamaParseOptions.ParseTimeoutSeconds"/>.
    /// </exception>
    public virtual async Task<ParsedDocument> ParseDocumentAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var apiKey = ResolveApiKey();

        _logger.LogDebug(
            "LlamaParseClient: uploading {FileName} ({Bytes} bytes) to LlamaParse",
            fileName, content.Length);

        var jobId = await UploadDocumentAsync(content, fileName, apiKey, cancellationToken);

        _logger.LogDebug(
            "LlamaParseClient: job {JobId} created for {FileName}; polling for completion",
            jobId, fileName);

        await WaitForJobCompletionAsync(jobId, apiKey, fileName, cancellationToken);

        var parsedDocument = await FetchResultAsync(jobId, apiKey, fileName, cancellationToken);

        _logger.LogInformation(
            "LlamaParseClient: successfully parsed {FileName} via LlamaParse (job {JobId}, {Chars} chars)",
            fileName, jobId, parsedDocument.Text.Length);

        return parsedDocument;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private string ResolveApiKey()
    {
        // The API key secret name is stored in options; the actual secret value is
        // injected into IConfiguration by Azure App Service / Key Vault at startup.
        var secretName = _options.ApiKeySecretName;
        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new InvalidOperationException(
                "LlamaParse API key secret name is not configured " +
                "(LlamaParse:ApiKeySecretName is empty).");
        }

        var apiKey = _configuration[secretName];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"LlamaParse API key not found in configuration under key '{secretName}'. " +
                "Ensure the secret is present in Key Vault and the App Service is configured " +
                "to expose it via Key Vault references.");
        }

        return apiKey;
    }

    private async Task<string> UploadDocumentAsync(
        byte[] content,
        string fileName,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(apiKey);

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveMimeType(fileName));
        form.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/parsing/upload", form, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"LlamaParse upload failed (HTTP {(int)response.StatusCode}): {body}");
        }

        var uploadResult = await response.Content.ReadFromJsonAsync<LlamaParseUploadResponse>(
            JsonOptions, cancellationToken);

        if (string.IsNullOrWhiteSpace(uploadResult?.Id))
        {
            throw new InvalidOperationException(
                "LlamaParse upload response did not contain a job ID.");
        }

        return uploadResult.Id;
    }

    private async Task WaitForJobCompletionAsync(
        string jobId,
        string apiKey,
        string fileName,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(apiKey);
        var timeout = TimeSpan.FromSeconds(_options.ParseTimeoutSeconds);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"LlamaParse did not complete parsing '{fileName}' (job {jobId}) " +
                    $"within {_options.ParseTimeoutSeconds} seconds.");
            }

            var response = await client.GetAsync(
                $"/api/parsing/job/{jobId}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"LlamaParse job status check failed (HTTP {(int)response.StatusCode}): {body}");
            }

            var status = await response.Content.ReadFromJsonAsync<LlamaParseJobStatus>(
                JsonOptions, cancellationToken);

            _logger.LogDebug(
                "LlamaParseClient: job {JobId} status = {Status}",
                jobId, status?.Status);

            switch (status?.Status?.ToUpperInvariant())
            {
                case "SUCCESS":
                    return;

                case "ERROR":
                case "PARTIAL_SUCCESS":
                    // PARTIAL_SUCCESS is treated as success — we still fetch whatever text
                    // was extracted; the caller will decide if quality is sufficient.
                    if (status.Status.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"LlamaParse job {jobId} ended with ERROR status for '{fileName}'.");
                    }
                    return;

                case "PENDING":
                case "IN_PROGRESS":
                default:
                    await Task.Delay(PollingInterval, cancellationToken);
                    break;
            }
        }
    }

    private async Task<ParsedDocument> FetchResultAsync(
        string jobId,
        string apiKey,
        string fileName,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(apiKey);

        // Fetch plain-text result
        var textResponse = await client.GetAsync(
            $"/api/parsing/job/{jobId}/result/text", cancellationToken);

        if (!textResponse.IsSuccessStatusCode)
        {
            var body = await textResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"LlamaParse result fetch failed (HTTP {(int)textResponse.StatusCode}): {body}");
        }

        var textResult = await textResponse.Content.ReadFromJsonAsync<LlamaParseTextResult>(
            JsonOptions, cancellationToken);

        // Attempt to fetch structured JSON result for tables (best-effort; tables may be absent)
        var tables = await TryFetchTablesAsync(client, jobId, cancellationToken);

        return new ParsedDocument
        {
            Text = textResult?.Text ?? string.Empty,
            Pages = textResult?.Pages ?? 0,
            Tables = tables,
            ExtractedAt = DateTimeOffset.UtcNow,
            ParserUsed = DocumentParser.LlamaParse
        };
    }

    private async Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>>> TryFetchTablesAsync(
        HttpClient client,
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var jsonResponse = await client.GetAsync(
                $"/api/parsing/job/{jobId}/result/json", cancellationToken);

            if (!jsonResponse.IsSuccessStatusCode)
            {
                return Array.Empty<IReadOnlyList<IReadOnlyList<string>>>();
            }

            var jsonResult = await jsonResponse.Content.ReadFromJsonAsync<LlamaParseJsonResult>(
                JsonOptions, cancellationToken);

            if (jsonResult?.Pages == null || jsonResult.Pages.Count == 0)
            {
                return Array.Empty<IReadOnlyList<IReadOnlyList<string>>>();
            }

            // Flatten tables across all pages into a single list of tables.
            var allTables = new List<IReadOnlyList<IReadOnlyList<string>>>();
            foreach (var page in jsonResult.Pages)
            {
                if (page.Tables == null) continue;
                foreach (var table in page.Tables)
                {
                    if (table.Rows != null)
                    {
                        allTables.Add(table.Rows);
                    }
                }
            }

            return allTables;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "LlamaParseClient: table extraction failed for job {JobId} (non-fatal)",
                jobId);
            return Array.Empty<IReadOnlyList<IReadOnlyList<string>>>();
        }
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        // Attach the API key on every request (Authorization: Bearer <key>).
        // The base address is configured on the named client registration.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static string ResolveMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".pdf"  => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc"  => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt"  => "text/plain",
            ".html" or ".htm" => "text/html",
            _       => "application/octet-stream"
        };
    }

    // -------------------------------------------------------------------------
    // LlamaParse API response models (private — not part of the public API)
    // -------------------------------------------------------------------------

    private sealed class LlamaParseUploadResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class LlamaParseJobStatus
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }

    private sealed class LlamaParseTextResult
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("pages")]
        public int Pages { get; init; }
    }

    private sealed class LlamaParseJsonResult
    {
        [JsonPropertyName("pages")]
        public List<LlamaParseJsonPage>? Pages { get; init; }
    }

    private sealed class LlamaParseJsonPage
    {
        [JsonPropertyName("tables")]
        public List<LlamaParseTable>? Tables { get; init; }
    }

    private sealed class LlamaParseTable
    {
        [JsonPropertyName("rows")]
        public List<List<string>>? Rows { get; init; }
    }
}
