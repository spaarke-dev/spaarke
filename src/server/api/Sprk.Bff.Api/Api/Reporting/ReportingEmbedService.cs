using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using Microsoft.Rest;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Authenticates to the Power BI REST API via MSAL ConfidentialClientApplication (service principal /
/// App Owns Data pattern) and provides embed token generation, report management, and export operations.
///
/// Follows ADR-001 (Minimal API — registered in DI, thin facade),
/// ADR-007 (no SDK types leak above this facade — all public methods return DTOs),
/// ADR-009 (embed tokens cached in Redis with TTL matching token expiry, proactive refresh at 80% TTL),
/// ADR-010 (counts as 1 of ≤2 DI registrations for the Reporting module).
///
/// MSAL caches the SP access token in-process; no custom distributed cache is needed for the raw
/// access token. Embed tokens are cached in Redis with the key format
/// <c>pbi:embed:{workspaceId}:{reportId}:{userId}</c> to reduce Power BI API round-trips.
/// </summary>
// unified-access-control-r2 task 166 r1: UNSEALED and the five methods the endpoints call are VIRTUAL — a permitted
// ADR-010 test seam (POML constraint), so the endpoint contract tests can substitute the Power BI boundary. No behaviour
// change; nothing derives from it in production.
public class ReportingEmbedService
{
    /// <summary>
    /// Power BI OAuth 2.0 scope for client-credentials / App Owns Data.
    /// Using <c>.default</c> ensures all API.Read / Tenant.Read.All permissions granted via admin
    /// consent in the app registration are included automatically (follows oauth-scopes.md pattern).
    /// </summary>
    private static readonly string[] PowerBiScopes = ["https://analysis.windows.net/.default"];

    /// <summary>
    /// How long to wait between export status polls.
    /// Power BI export jobs typically complete within 10-30 seconds for small reports.
    /// </summary>
    private static readonly TimeSpan ExportPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Maximum number of export status polls before throwing <see cref="TimeoutException"/>.</summary>
    private const int ExportMaxPolls = 30;

    /// <summary>
    /// Fraction of a token's remaining lifetime that must still be available for a cached token
    /// to be considered fresh. Tokens with less than 20% of their original TTL remaining are
    /// treated as near-expiry and regenerated proactively (ADR-009 proactive refresh rule).
    /// </summary>
    private const double FreshThresholdFraction = 0.20;

    /// <summary>
    /// The client should call <c>report.setAccessToken()</c> once 80% of the token's original
    /// lifetime has elapsed — i.e. at 80% of total TTL from the issue time.
    /// </summary>
    private const double ClientRefreshFraction = 0.80;

    // Resource identifier for ITenantCache (FR-05). Embed tokens are user-bound Power BI
    // tokens, not authorization decisions — caching is permitted by ADR-009.
    private const string ReportingEmbedResource = "reporting-embed";
    private const int CacheVersion = 1;

    private readonly PowerBiOptions _options;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ReportingEmbedService> _logger;
    private readonly IConfidentialClientApplication _cca;

    public ReportingEmbedService(
        IOptions<PowerBiOptions> options,
        ITenantCache cache,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ReportingEmbedService> logger)
    {
        _options = options.Value;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;

        // Build MSAL ConfidentialClientApplication once; MSAL manages the in-process token cache.
        _cca = ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority(_options.GetEffectiveAuthorityUrl())
            .WithClientSecret(_options.ClientSecret)
            .Build();
    }

    // -----------------------------------------------------------------------------------------
    // Embed token
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns a Power BI embed configuration for the specified report.
    ///
    /// Implements a cache-aside pattern (ADR-009):
    /// <list type="bullet">
    ///   <item>Cache key: <c>pbi:embed:{workspaceId}:{reportId}:{userId}</c></item>
    ///   <item>Cache HIT and token fresh (&gt;20% TTL remaining): return cached value immediately.</item>
    ///   <item>Cache HIT but token near-expiry (&lt;20% TTL remaining): regenerate proactively.</item>
    ///   <item>Cache MISS: generate token, cache with TTL matching token expiry, return result.</item>
    /// </list>
    ///
    /// The returned <see cref="EmbedConfig.RefreshAfter"/> tells the client when to call
    /// <c>report.setAccessToken()</c> — at 80% of the token's total lifetime — before the token
    /// expires in the browser.
    /// </summary>
    /// <param name="workspaceId">Power BI workspace (group) GUID containing the report.</param>
    /// <param name="reportId">Report GUID to embed.</param>
    /// <param name="username">
    ///   RLS username (typically the user's UPN or BU identifier). Pass <c>null</c> to skip RLS.
    /// </param>
    /// <param name="roles">
    ///   RLS role names defined in the dataset. Pass <c>null</c> or empty to skip RLS.
    ///   Only set these when the dataset has RLS roles configured — Power BI will reject the
    ///   token request if RLS is not enabled on the dataset.
    /// </param>
    /// <param name="profileId">
    ///   Optional service principal profile ID for multi-workspace isolation (task PBI-003).
    ///   When supplied, the <c>X-PowerBI-Profile-Id</c> header is added to scope the call.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="EmbedConfig"/> containing the embed token, URL, report ID, expiry, and refresh hint.</returns>
    public virtual async Task<EmbedConfig> GetEmbedConfigAsync(
        Guid workspaceId,
        Guid reportId,
        string? username,
        IList<string>? roles,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        var tenantId = ExtractTenantId();
        var idComponent = BuildEmbedCacheId(workspaceId, reportId, username);

        // --- Cache-aside: check Redis first (only when tenant claim is available) ---
        if (!string.IsNullOrEmpty(tenantId))
        {
            try
            {
                var entry = await _cache.GetAsync<CachedEmbedEntry>(
                    tenantId, ReportingEmbedResource, idComponent, CacheVersion, ct: ct);
                if (entry != null)
                {
                    var remaining = entry.Expiry - DateTimeOffset.UtcNow;
                    var totalTtl = entry.Expiry - entry.IssuedAt;

                    // Token is considered fresh when more than 20% of its original lifetime remains.
                    var isFresh = totalTtl.TotalSeconds > 0 &&
                                  remaining.TotalSeconds / totalTtl.TotalSeconds > FreshThresholdFraction;

                    if (isFresh)
                    {
                        _logger.LogDebug(
                            "Embed token cache HIT for report {ReportId} (remaining TTL: {RemainingSeconds:F0}s)",
                            reportId, remaining.TotalSeconds);
                        return entry.ToEmbedConfig();
                    }

                    _logger.LogInformation(
                        "Embed token for report {ReportId} is near expiry ({RemainingSeconds:F0}s remaining, " +
                        "threshold {ThresholdPct}%) — regenerating proactively",
                        reportId, remaining.TotalSeconds, (int)(FreshThresholdFraction * 100));
                }
                else
                {
                    _logger.LogDebug("Embed token cache MISS for report {ReportId}", reportId);
                }
            }
            catch (Exception ex)
            {
                // Cache read failure must never block token generation (ADR-009: graceful degradation).
                _logger.LogWarning(ex, "Redis read failed for embed token (report {ReportId}); falling through to PBI API", reportId);
            }
        }

        // --- Cache MISS or near-expiry: generate a fresh token from Power BI ---
        var config = await GenerateFreshEmbedConfigAsync(workspaceId, reportId, username, roles, profileId, ct);

        // --- Store fresh token in Redis ---
        if (!string.IsNullOrEmpty(tenantId))
        {
            try
            {
                var ttl = config.Expiry - DateTimeOffset.UtcNow;
                if (ttl > TimeSpan.Zero)
                {
                    var entry = CachedEmbedEntry.FromEmbedConfig(config);
                    await _cache.SetAsync(
                        tenantId, ReportingEmbedResource, idComponent, CacheVersion,
                        entry, ttl, ct: ct);

                    _logger.LogDebug(
                        "Cached embed token for report {ReportId} with TTL {TtlSeconds:F0}s",
                        reportId, ttl.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                // Cache write failure must never surface to the caller (ADR-009: graceful degradation).
                _logger.LogWarning(ex, "Redis write failed for embed token (report {ReportId}); token will not be cached", reportId);
            }
        }

        return config;
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the current HttpContext.
    /// Returns null when no claim is present (in which case caching is skipped).
    /// </summary>
    private string? ExtractTenantId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return null;
        return user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
    }

    /// <summary>
    /// Calls the Power BI REST API to generate a fresh embed token and constructs a full
    /// <see cref="EmbedConfig"/> including the proactive refresh hint (<see cref="EmbedConfig.RefreshAfter"/>).
    /// This method bypasses the cache entirely and is called on cache misses or near-expiry hits.
    /// </summary>
    private async Task<EmbedConfig> GenerateFreshEmbedConfigAsync(
        Guid workspaceId,
        Guid reportId,
        string? username,
        IList<string>? roles,
        Guid? profileId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating embed token for report {ReportId} in workspace {WorkspaceId} (RLS user: {Username})",
            reportId, workspaceId, username ?? "<none>");

        var client = await GetPowerBIClientAsync(profileId, ct);

        // Fetch the report to get the dataset binding and canonical embed URL.
        Report report;
        try
        {
            report = await client.Reports.GetReportInGroupAsync(workspaceId, reportId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch report {ReportId} from workspace {WorkspaceId}",
                reportId, workspaceId);
            throw;
        }

        var tokenRequest = BuildGenerateTokenRequest(report, username, roles);

        EmbedToken embedToken;
        try
        {
            embedToken = await client.Reports.GenerateTokenInGroupAsync(
                workspaceId, reportId, tokenRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embed token for report {ReportId}", reportId);
            throw;
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiry = new DateTimeOffset(embedToken.Expiration, TimeSpan.Zero);
        var totalTtl = expiry - issuedAt;

        // Tell the client to refresh at 80% of the token lifetime to avoid browser-side expiry.
        var refreshAfter = issuedAt + TimeSpan.FromSeconds(totalTtl.TotalSeconds * ClientRefreshFraction);

        _logger.LogInformation(
            "Embed token generated for report {ReportId}, expires {Expiry}, client refresh at {RefreshAfter}",
            reportId, expiry, refreshAfter);

        return new EmbedConfig(
            Token: embedToken.Token!,
            EmbedUrl: report.EmbedUrl!,
            ReportId: reportId,
            Expiry: expiry,
            RefreshAfter: refreshAfter);
    }

    // -----------------------------------------------------------------------------------------
    // Cache helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the ITenantCache id component for an embed token.
    /// Format: <c>{workspaceId}:{reportId}:{userId}</c> (tenant prefix is added by the wrapper).
    /// Uses "anonymous" when no username is provided (unauthenticated / no-RLS scenario).
    /// </summary>
    private static string BuildEmbedCacheId(Guid workspaceId, Guid reportId, string? username) =>
        $"{workspaceId}:{reportId}:{username ?? "anonymous"}";

    /// <summary>
    /// Internal cache entry that stores the full embed config plus the token issue time so that
    /// the proactive refresh fraction can be recomputed correctly on subsequent cache hits.
    /// </summary>
    private sealed record CachedEmbedEntry(
        string Token,
        string EmbedUrl,
        Guid ReportId,
        DateTimeOffset Expiry,
        DateTimeOffset IssuedAt,
        DateTimeOffset RefreshAfter)
    {
        public EmbedConfig ToEmbedConfig() =>
            new(Token, EmbedUrl, ReportId, Expiry, RefreshAfter);

        public static CachedEmbedEntry FromEmbedConfig(EmbedConfig config) =>
            new(config.Token, config.EmbedUrl, config.ReportId, config.Expiry,
                IssuedAt: DateTimeOffset.UtcNow,
                RefreshAfter: config.RefreshAfter);
    }

    // -----------------------------------------------------------------------------------------
    // Report list / get
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns all reports in the specified Power BI workspace.
    /// </summary>
    /// <param name="workspaceId">Power BI workspace (group) GUID.</param>
    /// <param name="profileId">Optional service principal profile ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of <see cref="PowerBiReport"/> DTOs. Never null; may be empty.</returns>
    public async Task<IReadOnlyList<PowerBiReport>> GetReportsAsync(
        Guid workspaceId,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Listing reports in workspace {WorkspaceId}", workspaceId);

        var client = await GetPowerBIClientAsync(profileId, ct);

        Reports reports;
        try
        {
            reports = await client.Reports.GetReportsInGroupAsync(workspaceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list reports in workspace {WorkspaceId}", workspaceId);
            throw;
        }

        return (reports.Value ?? [])
            .Select(MapToDto)
            .ToList();
    }

    // GetReportAsync DELETED 2026-10-04 by unified-access-control-r2 task 166 f1 (verifier item 14): its only caller was
    // the Save-As registration branch, removed by owner round 23 item 2. A Power BI report is never looked up by an id a
    // client names — every report id comes from a catalog row read as the caller.

    // -----------------------------------------------------------------------------------------
    // Report create / delete
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a new report in the workspace by cloning a template report and binding it to the
    /// specified dataset.
    ///
    /// The Power BI REST API does not expose a "create blank report" operation — new reports must
    /// either be imported from a .pbix file or cloned from an existing template. Callers are
    /// expected to provide a <paramref name="templateReportId"/> that serves as the canvas scaffold.
    /// </summary>
    /// <param name="workspaceId">Target Power BI workspace GUID.</param>
    /// <param name="name">Display name for the new report.</param>
    /// <param name="datasetId">Dataset GUID the cloned report will be rebound to.</param>
    /// <param name="templateReportId">Source report GUID to clone the canvas from.</param>
    /// <param name="profileId">Optional service principal profile ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created <see cref="PowerBiReport"/> DTO.</returns>
    public virtual async Task<PowerBiReport> CreateReportAsync(
        Guid workspaceId,
        string name,
        Guid datasetId,
        Guid templateReportId,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating report '{Name}' (clone of {TemplateReportId}) bound to dataset {DatasetId} in workspace {WorkspaceId}",
            name, templateReportId, datasetId, workspaceId);

        var client = await GetPowerBIClientAsync(profileId, ct);

        // Clone into the same workspace and rebind to the specified dataset.
        var cloneRequest = new CloneReportRequest(
            name: name,
            targetWorkspaceId: workspaceId,
            targetModelId: datasetId.ToString());

        Report created;
        try
        {
            created = await client.Reports.CloneReportInGroupAsync(
                workspaceId, templateReportId, cloneRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create report '{Name}' in workspace {WorkspaceId}", name, workspaceId);
            throw;
        }

        _logger.LogInformation("Created report {ReportId} ('{Name}')", created.Id, name);
        return MapToDto(created);
    }

    /// <summary>
    /// Deletes a report from the specified Power BI workspace.
    /// </summary>
    /// <param name="workspaceId">Power BI workspace GUID.</param>
    /// <param name="reportId">Report GUID to delete.</param>
    /// <param name="profileId">Optional service principal profile ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task DeleteReportAsync(
        Guid workspaceId,
        Guid reportId,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deleting report {ReportId} from workspace {WorkspaceId}", reportId, workspaceId);

        var client = await GetPowerBIClientAsync(profileId, ct);

        try
        {
            await client.Reports.DeleteReportInGroupAsync(workspaceId, reportId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete report {ReportId}", reportId);
            throw;
        }

        _logger.LogInformation("Deleted report {ReportId}", reportId);
    }

    // -----------------------------------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Exports a report to the specified file format (PDF or PPTX).
    /// Triggers an async export job on the Power BI service, polls until completion, then streams
    /// the result. The caller is responsible for disposing the returned stream.
    /// </summary>
    /// <param name="workspaceId">Power BI workspace GUID.</param>
    /// <param name="reportId">Report GUID to export.</param>
    /// <param name="format">Output format: <see cref="ExportFormat.PDF"/> or <see cref="ExportFormat.PPTX"/>.</param>
    /// <param name="username">
    ///   The row-level-security username (the caller's business unit id). Required: an export is the same data path
    ///   as an embed, so it carries the same effective identity (unified-access-control-r2 task 166 r2).
    /// </param>
    /// <param name="roles">The dataset RLS role(s) <paramref name="username"/> is evaluated under. Required.</param>
    /// <param name="profileId">Optional service principal profile ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   A <see cref="Stream"/> containing the exported file bytes. The caller must dispose it.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when no RLS identity is supplied — an export is never unfiltered.</exception>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when the Power BI export job fails, or when the report's dataset (which the identity must name) cannot
    ///   be determined.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///   Thrown when the export job does not complete within the polling timeout
    ///   (<see cref="ExportMaxPolls"/> × <see cref="ExportPollInterval"/>).
    /// </exception>
    public virtual async Task<Stream> ExportReportAsync(
        Guid workspaceId,
        Guid reportId,
        ExportFormat format,
        string username,
        IList<string> roles,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        // An export runs as the service principal, so without an effective identity Power BI would return every
        // business unit's rows (or fail on an RLS dataset). Refuse rather than export unfiltered (task 166 r2).
        if (string.IsNullOrWhiteSpace(username) || roles is not { Count: > 0 })
        {
            throw new ArgumentException("An export requires the caller's row-level-security identity.", nameof(username));
        }

        _logger.LogInformation(
            "Exporting report {ReportId} from workspace {WorkspaceId} as {Format} (RLS user: {Username})",
            reportId, workspaceId, format, username);

        var client = await GetPowerBIClientAsync(profileId, ct);

        // The identity must name the report's dataset — read it from Power BI, as the embed path does.
        Report report;
        try
        {
            report = await client.Reports.GetReportInGroupAsync(workspaceId, reportId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch report {ReportId} for export", reportId);
            throw;
        }

        // Step 1: Trigger the async export job — WITH the caller's effective identity.
        var exportRequest = BuildExportRequest(format, report.DatasetId, username, roles);
        Export exportJob;
        try
        {
            exportJob = await client.Reports.ExportToFileInGroupAsync(
                workspaceId, reportId, exportRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start export job for report {ReportId}", reportId);
            throw;
        }

        var exportId = exportJob.Id!;
        _logger.LogDebug("Export job {ExportId} started for report {ReportId}", exportId, reportId);

        // Step 2: Poll until the export job completes.
        Export? completedExport = null;
        for (var poll = 0; poll < ExportMaxPolls; poll++)
        {
            await Task.Delay(ExportPollInterval, ct);

            Export status;
            try
            {
                status = await client.Reports.GetExportToFileStatusInGroupAsync(
                    workspaceId, reportId, exportId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to poll export status for job {ExportId}", exportId);
                throw;
            }

            _logger.LogDebug(
                "Export job {ExportId} status: {Status} (poll {Poll}/{MaxPolls})",
                exportId, status.Status, poll + 1, ExportMaxPolls);

            if (status.Status == ExportState.Succeeded)
            {
                completedExport = status;
                break;
            }

            if (status.Status == ExportState.Failed)
            {
                throw new InvalidOperationException(
                    $"Power BI export job {exportId} failed for report {reportId}.");
            }
        }

        if (completedExport is null)
        {
            throw new TimeoutException(
                $"Power BI export job {exportId} did not complete within " +
                $"{ExportMaxPolls * ExportPollInterval.TotalSeconds} seconds.");
        }

        // Step 3: Retrieve and return the file stream.
        Stream fileStream;
        try
        {
            fileStream = await client.Reports.GetFileOfExportToFileInGroupAsync(
                workspaceId, reportId, exportId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve export file for job {ExportId}", exportId);
            throw;
        }

        _logger.LogInformation(
            "Export complete for report {ReportId}, job {ExportId}", reportId, exportId);

        return fileStream;
    }

    // -----------------------------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Acquires a service principal access token via MSAL and creates a <see cref="PowerBIClient"/>.
    /// MSAL's in-process token cache returns the cached token on repeat calls until it expires,
    /// minimising round-trips to Entra ID.
    ///
    /// When <paramref name="profileId"/> is provided, the <c>X-PowerBI-Profile-Id</c> header is
    /// added to every outgoing request so the call is scoped to that service principal profile
    /// (task PBI-003 multi-workspace isolation pattern).
    /// </summary>
    /// <remarks>
    /// <c>protected internal virtual</c> returning the SDK's own <see cref="IPowerBIClient"/> interface (unified-access-control-r2
    /// task 166 f1, verifier item 7): the ONE seam below which a test substitutes the Power BI service, so the REAL
    /// <see cref="ExportReportAsync"/> body runs — and a regression that drops the export's row-level-security identity
    /// fails a test instead of shipping. The SDK type never leaves this class (ADR-007).
    /// </remarks>
    protected internal virtual async Task<IPowerBIClient> GetPowerBIClientAsync(
        Guid? profileId,
        CancellationToken ct)
    {
        AuthenticationResult result;
        try
        {
            result = await _cca
                .AcquireTokenForClient(PowerBiScopes)
                .ExecuteAsync(ct);
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex,
                "MSAL service error acquiring Power BI token: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (MsalClientException ex)
        {
            _logger.LogError(ex,
                "MSAL client error acquiring Power BI token: {ErrorCode}", ex.ErrorCode);
            throw;
        }

        var credentials = new TokenCredentials(result.AccessToken, "Bearer");
        var client = new PowerBIClient(new Uri(_options.ApiUrl), credentials);

        // Wire service principal profile header for multi-workspace isolation (task PBI-003).
        if (profileId.HasValue)
        {
            client.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-PowerBI-Profile-Id", profileId.Value.ToString());
        }

        return client;
    }

    /// <summary>
    /// Builds a <see cref="GenerateTokenRequest"/> for the given report, optionally including
    /// an <see cref="EffectiveIdentity"/> for Row-Level Security enforcement.
    ///
    /// RLS is only applied when both <paramref name="username"/> and <paramref name="roles"/>
    /// are non-null and non-empty. If the underlying dataset does not have RLS configured,
    /// Power BI will return an error if EffectiveIdentity is supplied — callers must only pass
    /// RLS parameters when the dataset actually defines roles.
    /// </summary>
    private static GenerateTokenRequest BuildGenerateTokenRequest(
        Report report,
        string? username,
        IList<string>? roles)
    {
        if (!string.IsNullOrWhiteSpace(username) && roles is { Count: > 0 })
        {
            var identity = new EffectiveIdentity
            {
                Username = username,
                Datasets = [report.DatasetId],
                Roles = roles
            };

            return new GenerateTokenRequest(
                accessLevel: TokenAccessLevel.View,
                identity: identity);
        }

        return new GenerateTokenRequest(accessLevel: TokenAccessLevel.View);
    }

    /// <summary>
    /// Maps a Power BI SDK <see cref="Report"/> to the public <see cref="PowerBiReport"/> DTO.
    /// Shields callers from SDK types (ADR-007).
    /// </summary>
    private static PowerBiReport MapToDto(Report report) =>
        new(
            Id: report.Id,
            Name: report.Name ?? string.Empty,
            EmbedUrl: report.EmbedUrl ?? string.Empty,
            DatasetId: Guid.TryParse(report.DatasetId, out var dsId) ? dsId : Guid.Empty);

    /// <summary>
    /// The Power BI export request for <paramref name="format"/>, carrying the row-level-security
    /// <see cref="EffectiveIdentity"/> (<paramref name="username"/> under <paramref name="roles"/> on
    /// <paramref name="datasetId"/>) — the same identity <see cref="BuildGenerateTokenRequest"/> puts in an embed token
    /// (unified-access-control-r2 task 166 r2). No identity, no request: a blank username, no roles or no dataset throw,
    /// so an export can never run unfiltered as the service principal.
    /// </summary>
    internal static ExportReportRequest BuildExportRequest(
        ExportFormat format, string? datasetId, string username, IList<string> roles)
    {
        if (string.IsNullOrWhiteSpace(username) || roles is not { Count: > 0 })
        {
            throw new ArgumentException("An export requires a row-level-security identity.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new InvalidOperationException(
                "The report's dataset could not be determined, so the export's row-level-security identity cannot be applied.");
        }

        return new ExportReportRequest
        {
            Format = MapExportFormat(format),
            PowerBIReportConfiguration = new PowerBIReportExportConfiguration
            {
                Identities =
                [
                    new EffectiveIdentity
                    {
                        Username = username,
                        Datasets = [datasetId],
                        Roles = roles,
                    },
                ],
            },
        };
    }

    /// <summary>Maps the public <see cref="ExportFormat"/> enum to the Power BI API <see cref="FileFormat"/>.</summary>
    private static FileFormat MapExportFormat(ExportFormat format) => format switch
    {
        ExportFormat.PDF => FileFormat.PDF,
        ExportFormat.PPTX => FileFormat.PPTX,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
    };
}
