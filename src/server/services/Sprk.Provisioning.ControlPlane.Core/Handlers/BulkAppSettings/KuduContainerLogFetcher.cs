// -----------------------------------------------------------------------------
// KuduContainerLogFetcher.cs
//
// Task 201 — production impl of IContainerLogFetcher. Fetches
// `https://{appServiceName}.scm.azurewebsites.net/api/logs/docker` (returns a
// text array of recent docker log entries). Bearer token acquired via the
// shared TokenCredential singleton (ADR-028 UAMI-outbound) — scope is
// `https://management.azure.com/.default`, the same audience the Kudu ARM-
// proxied endpoints accept.
//
// NOTE: The exact Kudu endpoint response shape can vary slightly by App
// Service SKU / OS. The primary path is `/api/logs/docker` which returns
// JSON metadata listing log files; the impl also falls back to listing
// `/api/vfs/LogFiles/` and fetching the most recent `*_docker.log` file
// when the primary path returns no useful text (defensive — the F20/F20a
// evidence pattern that motivated this class was observed via the CLI-
// convenience `az webapp log tail`, whose transport uses similar Kudu
// endpoints).
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Core;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <inheritdoc cref="IContainerLogFetcher"/>
public sealed class KuduContainerLogFetcher : IContainerLogFetcher
{
    private static readonly string[] KuduTokenScopes = ["https://management.azure.com/.default"];

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly ILogger<KuduContainerLogFetcher> _logger;

    /// <summary>Constructs the fetcher.</summary>
    public KuduContainerLogFetcher(
        HttpClient httpClient,
        TokenCredential credential,
        ILogger<KuduContainerLogFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _credential = credential;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> FetchDockerLogsAsync(string appServiceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appServiceName);

        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(KuduTokenScopes), cancellationToken).ConfigureAwait(false);

        var kuduBase = new Uri($"https://{appServiceName}.scm.azurewebsites.net");

        // (1) Primary path — /api/logs/docker returns JSON metadata array of
        //     log entries (each carries an href to the log file).
        try
        {
            var primary = await FetchViaLogsDockerAsync(kuduBase, token.Token, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "H4b KuduContainerLogFetcher: primary /api/logs/docker path failed for '{AppService}'; falling back to /api/vfs/LogFiles",
                appServiceName);
        }

        // (2) Fallback — enumerate /api/vfs/LogFiles/ and fetch the newest
        //     *_docker.log file's content.
        return await FetchViaVfsLogFilesAsync(kuduBase, token.Token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> FetchViaLogsDockerAsync(Uri kuduBase, string bearerToken, CancellationToken ct)
    {
        var url = new Uri(kuduBase, "/api/logs/docker");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        // The response is a JSON array of log-file metadata objects with an "href"
        // pointing at the log content. Concatenate the newest ~3 files' bodies
        // (best-effort — the parser degrades gracefully if the shape isn't as
        // expected).
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return body;  // Non-array shape — just return whatever the server sent.
            }

            var sink = new System.Text.StringBuilder();
            var count = 0;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (count++ >= 3) break;
                if (!entry.TryGetProperty("href", out var hrefEl) || hrefEl.ValueKind != JsonValueKind.String) continue;
                var href = hrefEl.GetString();
                if (string.IsNullOrWhiteSpace(href)) continue;
                var content = await FetchAsync(new Uri(href), bearerToken, ct).ConfigureAwait(false);
                sink.Append(content);
                sink.Append('\n');
            }
            return sink.ToString();
        }
        catch (JsonException)
        {
            return body;  // Return the raw payload verbatim on parse failure.
        }
    }

    private async Task<string> FetchViaVfsLogFilesAsync(Uri kuduBase, string bearerToken, CancellationToken ct)
    {
        // /api/vfs/LogFiles/ returns a directory listing; find the newest *_docker.log
        // file + fetch it. Best-effort: on any error return empty (H4b caller
        // handles the parse-failure branch with a generic diagnostic).
        try
        {
            var url = new Uri(kuduBase, "/api/vfs/LogFiles/");
            var body = await FetchAsync(url, bearerToken, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;

            (Uri Href, DateTimeOffset Mtime)? newest = null;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) continue;
                var name = nameEl.GetString();
                if (name is null || !name.EndsWith("_docker.log", StringComparison.OrdinalIgnoreCase)) continue;

                if (!entry.TryGetProperty("href", out var hrefEl) || hrefEl.ValueKind != JsonValueKind.String) continue;
                var href = hrefEl.GetString();
                if (string.IsNullOrWhiteSpace(href)) continue;

                DateTimeOffset mtime = DateTimeOffset.MinValue;
                if (entry.TryGetProperty("mtime", out var mtimeEl) && mtimeEl.ValueKind == JsonValueKind.String)
                {
                    _ = DateTimeOffset.TryParse(mtimeEl.GetString(), out mtime);
                }

                if (newest is null || mtime > newest.Value.Mtime)
                {
                    newest = (new Uri(href), mtime);
                }
            }

            return newest is null
                ? string.Empty
                : await FetchAsync(newest.Value.Href, bearerToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "H4b KuduContainerLogFetcher: fallback /api/vfs/LogFiles enumeration failed.");
            return string.Empty;
        }
    }

    private async Task<string> FetchAsync(Uri url, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
