// -----------------------------------------------------------------------------
// BapRestEnvironmentRateProbe.cs
//
// Production <see cref="IPreflightQuotaProbe"/> implementation — SDK/REST port
// of scripts/preflight/Test-DataverseEnvCreationRate.ps1 (task 120, Wave G-2,
// Option D hybrid per DS-1b §1 H0 row). Replaces the `pac admin list --json`
// shell-out with a raw BAP (Business Application Platform) admin REST call:
//
//   GET https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform
//       /scopes/admin/environments?api-version=2021-04-01
//   Authorization: Bearer <token, audience https://service.powerapps.com/>
//
// This is the same endpoint the PAC CLI + Microsoft.PowerApps.Administration
// .PowerShell's Get-AdminEnvironment cmdlet call internally — `pac admin
// list` is a client-side reformatting of this REST response, not a distinct
// data source.
//
// *** LIVE-VERIFICATION-PENDING (Wave G-1 🟡 convention — see project
// current-task.md "Live Ceremony Backlog") ***
// The POML's escalation trigger requires verifying the response shape
// against a LIVE BAP call before trusting the field mapping (the pwsh
// script parses PAC's OWN reformatted output, which does not by itself
// prove the raw REST envelope's field names). This sandbox has no live
// Azure/BAP credentials to make that call. Per DS-1b's documented BAP
// admin environments-list schema (the same schema
// Microsoft.PowerApps.Administration.PowerShell's REST layer + community
// tooling have used unchanged for years):
//     { "value": [ { "name": "...", "properties": { "createdTime": "<ISO8601>", ... } } ], "nextLink": ... }
// <see cref="BapEnvironmentReader"/> reads `properties.createdTime` as the
// primary field, with a defensive fallback chain (`properties.CreatedTime`,
// `createdTime`, `CreatedOn`) mirroring the PS script's own multi-field
// probe (`CreatedOn`,`created`,`CreatedTime`,`createdOn`) so a minor casing
// drift does not silently break the check. THIS ASSUMPTION MUST BE
// CONFIRMED against a live tenant during the Wave G-1/G-2 live ceremony
// before H0 gates a production customer run — flagged in task 120's
// completion report for owner sign-off.
//
// THRESHOLD LOGIC: ported verbatim from Test-DataverseEnvCreationRate.ps1
// (see <see cref="Evaluate"/>):
//   - MinSlotsRequired > RateLimit -> immediate Fail (arg sanity).
//   - Zero environments -> trivial Pass (full rate-limit headroom free).
//   - Count environments created within RateWindowHours of "now"; slotsFree
//     = RateLimit - observedRecent; Pass iff slotsFree >= MinSlotsRequired.
//
// DEFAULTS: MinSlotsRequired=1, RateWindowHours=1, RateLimit=4 (Microsoft-
// documented soft limit ~4 env/hr per tenant) — match the PS script's
// parameter defaults exactly. Overridable via NonSecretParameters.
//
// ADR-038 TEST-BOUNDARY DESIGN: typed-HttpClient injection, matching
// GraphRestB2BConsentVerifier's established pattern elsewhere in
// Handlers/** exactly (raw HttpClient + DefaultAzureCredential, no SDK
// wrapper). Unit tests construct <c>new HttpClient(fakeHandler)</c> with a
// hand-rolled <see cref="HttpMessageHandler"/> subclass and pass it to the
// probe directly — never a Mock&lt;HttpMessageHandler&gt; (banned per
// testing.md), never a wrapper mock. <see cref="ParseCreationTimes"/> is
// additionally exposed for pure-parsing unit tests against canned response
// bodies with no HTTP involved at all.
//
// AUTH: DefaultAzureCredential scoped to the TARGET tenant (§4D I5 — never a
// default/fallback tenant), matching the same pattern as
// GraphRestB2BConsentVerifier / DataverseWebApiHealthProbe elsewhere in
// Handlers/**. This differs from the other 3 H0 probes (which query the
// SPAARKE PLATFORM subscription) because Dataverse env-creation rate is a
// property of the TARGET tenant (Model 1 = Spaarke tenant, Model 2 =
// customer tenant).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// REST-backed <see cref="IPreflightQuotaProbe"/> for Dataverse environment-
/// creation rate headroom. Reads <c>minSlotsRequired</c>, <c>rateWindowHours</c>,
/// <c>rateLimit</c> overrides (all optional) from
/// <see cref="PreflightProbeInput.NonSecretParameters"/>; tenant comes from
/// <see cref="PreflightProbeInput.TenantId"/> (already §4D I1-guarded by H0
/// before any probe fires).
/// </summary>
public sealed class BapRestEnvironmentRateProbe : IPreflightQuotaProbe
{
    /// <summary>Optional run-parameter override for minimum required free slots. Default 1.</summary>
    public const string MinSlotsRequiredParameterKey = "minSlotsRequired";

    /// <summary>Optional run-parameter override for the rate-window size in hours. Default 1.</summary>
    public const string RateWindowHoursParameterKey = "rateWindowHours";

    /// <summary>Optional run-parameter override for the tenant hourly rate limit. Default 4.</summary>
    public const string RateLimitParameterKey = "rateLimit";

    private const int DefaultMinSlotsRequired = 1;
    private const int DefaultRateWindowHours = 1;
    private const int DefaultRateLimit = 4;

    private static readonly string[] BapScope = { "https://service.powerapps.com/.default" };
    private const string EnvironmentsListUri =
        "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2021-04-01";

    private readonly HttpClient _httpClient;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<BapRestEnvironmentRateProbe> _logger;
    private readonly TimeProvider _timeProvider;

    /// <inheritdoc/>
    public string CheckName => PreflightCheckNames.DataverseEnvCreationRate;

    /// <summary>Constructs the probe with a typed <see cref="HttpClient"/> (production via <c>services.AddHttpClient</c>; tests via a hand-rolled fake handler).</summary>
    public BapRestEnvironmentRateProbe(HttpClient httpClient, ILogger<BapRestEnvironmentRateProbe> logger)
        : this(
            httpClient,
            tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),
            logger,
            TimeProvider.System)
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/> (so tests never invoke the
    /// real DefaultAzureCredential chain) alongside the fake-transport <see cref="HttpClient"/> +
    /// <see cref="TimeProvider"/>.
    /// </summary>
    internal BapRestEnvironmentRateProbe(
        HttpClient httpClient,
        Func<string, TokenCredential> credentialFactory,
        ILogger<BapRestEnvironmentRateProbe> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClient = httpClient;
        _credentialFactory = credentialFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PreflightCheckResult> CheckAsync(PreflightProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var minSlotsRequired = ReadIntOverride(input, MinSlotsRequiredParameterKey, DefaultMinSlotsRequired);
        var rateWindowHours = ReadIntOverride(input, RateWindowHoursParameterKey, DefaultRateWindowHours);
        var rateLimit = ReadIntOverride(input, RateLimitParameterKey, DefaultRateLimit);

        if (minSlotsRequired > rateLimit)
        {
            var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["tenantId"] = input.TenantId,
                ["minSlotsRequired"] = minSlotsRequired,
                ["rateLimit"] = rateLimit,
                ["rateWindowHours"] = rateWindowHours,
            });
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.DataverseEnvCreationRate,
                Passed: false,
                Headroom: headroom,
                Diagnostic:
                    $"MinSlotsRequired ({minSlotsRequired}) exceeds tenant hourly RateLimit ({rateLimit} env/hr). " +
                    "Cannot satisfy in a single hourly bucket. File Dataverse env-creation rate quota bump (1-3 day lead time).");
        }

        _logger.LogInformation(
            "{CheckName} querying BAP admin environments: tenantId={TenantId}", CheckName, input.TenantId);

        var creationTimes = await GetEnvironmentCreationTimesAsync(input.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return Evaluate(input.TenantId, minSlotsRequired, rateWindowHours, rateLimit, _timeProvider.GetUtcNow(), creationTimes);
    }

    private async Task<IReadOnlyList<DateTimeOffset>> GetEnvironmentCreationTimesAsync(
        string tenantId, CancellationToken cancellationToken)
    {
        var credential = _credentialFactory(tenantId);
        var token = await credential.GetTokenAsync(new TokenRequestContext(BapScope), cancellationToken)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, EnvironmentsListUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"BAP admin environments list failed for tenant '{tenantId}': {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Body: {(text.Length > 500 ? text[..500] + "...[truncated]" : text)}");
        }

        return ParseCreationTimes(text);
    }

    /// <summary>
    /// Extracts each environment's creation timestamp from the BAP admin
    /// list response. Tries <c>properties.createdTime</c> first (the
    /// documented field per this file's header note), falling back to
    /// sibling casings — mirrors Test-DataverseEnvCreationRate.ps1's own
    /// defensive multi-field lookup. Exposed internal for unit testing
    /// against canned response bodies (no HTTP involved).
    /// </summary>
    internal static IReadOnlyList<DateTimeOffset> ParseCreationTimes(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Array.Empty<DateTimeOffset>();
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "BAP admin environments list response missing top-level 'value' array — API shape may have drifted. " +
                "Escalate per root CLAUDE.md §6.");
        }

        var results = new List<DateTimeOffset>();
        foreach (var env in valueArray.EnumerateArray())
        {
            var raw = TryReadCreatedTime(env);
            if (raw is null)
            {
                continue; // Skip un-parseable / missing entries — mirrors PS script's silent-skip behavior.
            }
            if (DateTimeOffset.TryParse(raw, out var parsed))
            {
                results.Add(parsed.ToUniversalTime());
            }
        }
        return results;
    }

    private static string? TryReadCreatedTime(JsonElement env)
    {
        if (env.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in new[] { "createdTime", "CreatedTime", "createdOn", "CreatedOn" })
            {
                if (props.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    return val.GetString();
                }
            }
        }

        // Defensive top-level fallback in case a future API revision flattens properties.
        foreach (var field in new[] { "createdTime", "CreatedTime", "createdOn", "CreatedOn" })
        {
            if (env.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString();
            }
        }
        return null;
    }

    /// <summary>
    /// Pure threshold-comparison logic — ported from Test-DataverseEnvCreationRate.ps1's
    /// rate-window counting block. Exposed internal so unit tests exercise the exact
    /// evaluation function the production path calls.
    /// </summary>
    internal static PreflightCheckResult Evaluate(
        string tenantId,
        int minSlotsRequired,
        int rateWindowHours,
        int rateLimit,
        DateTimeOffset now,
        IReadOnlyList<DateTimeOffset> environmentCreationTimes)
    {
        if (environmentCreationTimes.Count == 0)
        {
            var headroomEmpty = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["tenantId"] = tenantId,
                ["observedRecentEnvs"] = 0,
                ["rateLimit"] = rateLimit,
                ["rateWindowHours"] = rateWindowHours,
                ["slotsFree"] = rateLimit,
                ["minSlotsRequired"] = minSlotsRequired,
            });
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.DataverseEnvCreationRate,
                Passed: true,
                Headroom: headroomEmpty,
                Diagnostic: $"Dataverse env-creation rate has full headroom ({rateLimit}/hr slots free).");
        }

        var cutoff = now.AddHours(-rateWindowHours);
        var observedRecent = environmentCreationTimes.Count(t => t > cutoff);
        var slotsFree = rateLimit - observedRecent;

        var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["tenantId"] = tenantId,
            ["observedRecentEnvs"] = observedRecent,
            ["rateLimit"] = rateLimit,
            ["rateWindowHours"] = rateWindowHours,
            ["slotsFree"] = slotsFree,
            ["minSlotsRequired"] = minSlotsRequired,
        });

        if (slotsFree >= minSlotsRequired)
        {
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.DataverseEnvCreationRate,
                Passed: true,
                Headroom: headroom,
                Diagnostic:
                    $"Dataverse env-creation rate OK for tenant '{tenantId}': {observedRecent} envs created in last {rateWindowHours}h, " +
                    $"{slotsFree} slots free (need {minSlotsRequired}).");
        }

        return new PreflightCheckResult(
            CheckName: PreflightCheckNames.DataverseEnvCreationRate,
            Passed: false,
            Headroom: headroom,
            Diagnostic:
                $"Dataverse env-creation rate EXHAUSTED for tenant '{tenantId}': {observedRecent} env(s) created in last {rateWindowHours}h " +
                $"against rate limit {rateLimit}/hr → {slotsFree} slot(s) free (need {minSlotsRequired}). " +
                "Wait for hourly bucket rollover, OR file rate-quota bump via PPAC support (1-3 day lead time).");
    }

    private static int ReadIntOverride(PreflightProbeInput input, string key, int defaultValue)
        => input.NonSecretParameters.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed)
            ? parsed
            : defaultValue;
}
