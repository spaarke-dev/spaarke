// -----------------------------------------------------------------------------
// BapRestEnvironmentCreator.cs
//
// Production <see cref="IDataverseEnvCreator"/> implementation — pure
// HttpClient + DefaultAzureCredential port of the collaborator that used to
// shell out to the pac CLI's `admin create-environment` command (task 048,
// PacAdminDataverseEnvCreator — RETIRED, kept on disk unregistered). Task 140
// (Wave G-4, Option D hybrid, DS-1b §1 H5 row) ports the exact BAP
// (Business Application Platform) admin REST create + async-operation-
// polling sequence that this project's OWN scripts/Provision-Customer.ps1
// STEP 5 ("Creating Dataverse environment via Power Platform Admin API") /
// STEP 6 ("Waiting for Dataverse environment provisioning") already
// implement — the script itself had already abandoned `pac` for this exact
// REST sequence; this class ports that sequence to C#, not a newly-designed
// one.
//
// GROUND-TRUTHING (per Wave G-1..G-3 discipline — verify, don't guess):
//   The script and this project's OWN task-120 BAP-REST precedent
//   (BapRestEnvironmentRateProbe.cs, Handlers/Preflight/) DISAGREE on two
//   details. Both cannot be right; live-web verification (WebSearch against
//   Microsoft Learn / MicrosoftDocs/power-platform + community docs,
//   2026-08-20) resolved the conflict in favor of task 120's values:
//     1. RESOURCE PROVIDER NAMESPACE: the script uses
//        "Microsoft.BusinessAppsPlatform" (plural "Apps") throughout its
//        list/create/poll URLs. Task 120's probe + Microsoft's own
//        MicrosoftDocs/power-platform "list-environments.md" reference doc
//        both use "Microsoft.BusinessAppPlatform" (singular "App", no
//        trailing s). This class uses the SINGULAR form — the script's
//        plural spelling is a latent typo, not an intentional divergent
//        endpoint. (A malformed provider namespace segment would 404 at the
//        ARM-style routing layer, not silently succeed.)
//     2. TOKEN AUDIENCE: the script requests
//        "https://api.bap.microsoft.com/.default". Task 120's probe (already
//        landed + code-reviewed) uses "https://service.powerapps.com/.default".
//        Community + Microsoft Q&A sources confirm
//        "https://service.powerapps.com/.default" (equivalently
//        "https://api.powerplatform.com/.default" on newer tooling) is the
//        documented resource for BAP admin REST calls — this class follows
//        task 120's audience, not the script's.
//   The api-version ("2023-06-01") and the actual create+poll SHAPE (POST
//   .../environments then GET .../environments/{envId} until
//   provisioningState is terminal — NOT a Location-header async pattern) ARE
//   ported verbatim from the script, corroborated by Microsoft's own
//   documented "API v2.8 and earlier: poll the Get Environments endpoint"
//   async-operations model (the dispatch context's generic "202 + Location
//   header" framing does not apply to this specific BAP endpoint family —
//   documented deviation, not an oversight).
//
// SEQUENCE (ported from Provision-Customer.ps1 STEP 5 + STEP 6):
//   1. Acquire a BAP-admin-scoped token via DefaultAzureCredential pinned to
//      request.TenantId (§4D I1/I5 — never a default tenant).
//   2. Idempotent existing-environment check: GET the admin environments
//      list and look for a match on domainName OR displayName (script's
//      STEP 5 "already exists" branch). This is NOT redundant with the
//      handler's own Level-3 Cosmos idempotency (H5DataverseEnvCreationHandler
//      CompletedPhases scan) — it covers the narrower window where BAP
//      already acknowledged a create request but the run's CompletedPhase
//      was never durably recorded (crash-after-create-before-write). Without
//      this check, a resume would re-POST the SAME deterministic domain
//      (customerId) and hit a DomainAlreadyExists conflict it can never
//      naturally recover from.
//        - Match found + already Succeeded with an instanceUrl -> Success
//          immediately (no re-create).
//        - Match found but still provisioning -> skip the CREATE POST,
//          proceed straight to polling using the existing envId.
//        - No match (or the list call itself fails) -> proceed to CREATE.
//   3. CREATE: POST .../environments with the STEP 5 body shape
//      (displayName/description/environmentSku/azureRegion/
//      linkedEnvironmentMetadata{baseLanguage,domainName,currency}).
//      environmentSku is populated from request.Tier (the H5 envelope's
//      Sandbox/Production/Trial equivalent of pac's `--type`) rather than
//      the script's hardcoded "Production" literal — the script's hardcode
//      is a script-level simplification, not a BAP API constraint; H5's
//      own request contract already carries Tier and the field is named
//      exactly "environmentSku" in the BAP schema, so this is a faithful
//      completion of the port, not a new design.
//   4. POLL: GET .../environments/{envId} every
//      DataverseEnvCreationOptions.AsyncOperationPollInterval (default 30s,
//      matches STEP 6's $pollIntervalSeconds) until
//      properties.provisioningState is "Succeeded" (-> Success with
//      properties.linkedEnvironmentMetadata.instanceUrl),
//      "Failed"/"Deleted" (-> Failure(ProvisioningFailed) — distinct from
//      Timeout), or DataverseEnvCreationOptions.CreationTimeout elapses
//      (-> Failure(Timeout)). Uses TimeProvider (not Stopwatch/Task.Delay
//      wall-clock) per docs/standards/TEST-ARCHITECTURE.md so the timeout
//      path is deterministically unit-testable.
//
// FAILURE CLASSIFICATION: HTTP-response-shape adaptation of
// PacAdminDataverseEnvCreator.ClassifyStderr's heuristics (status code +
// response body text), PLUS one new distinct classification this REST port
// surfaces that the pac CLI's textual stderr never cleanly did:
// DomainAlreadyExists (create POST rejected because the requested domain is
// taken — see DataverseEnvCreationFailureKind.DomainAlreadyExists doc for
// why this is NOT folded into PartialProvisioning).
//
// ADR-038 TEST-BOUNDARY DESIGN: typed-HttpClient injection (constructor
// takes HttpClient directly, matching BapRestEnvironmentRateProbe's
// established pattern) + an internal Func<string,TokenCredential> test seam
// so unit tests never invoke the real DefaultAzureCredential chain. Tests
// use a hand-rolled HttpMessageHandler subclass — never Mock<HttpMessageHandler>
// (banned per testing.md / ADR-038).
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// <see cref="IDataverseEnvCreator"/> implementation that creates + polls a
/// Dataverse environment via the Power Platform Admin (BAP) REST API,
/// porting Provision-Customer.ps1 STEP 5/6 verbatim (endpoint shape,
/// request-body shape, async-operation-polling pattern).
/// </summary>
public sealed class BapRestEnvironmentCreator : IDataverseEnvCreator
{
    /// <summary>
    /// BAP admin REST token audience. Ground-truthed against task 120's
    /// already-landed BapRestEnvironmentRateProbe + live-web verification
    /// (see file header) — NOT the script's unverified
    /// "https://api.bap.microsoft.com/.default".
    /// </summary>
    internal const string BapScope = "https://service.powerapps.com/.default";

    /// <summary>
    /// BAP admin REST resource-provider namespace. Ground-truthed SINGULAR
    /// "App" (see file header) — the script's "Microsoft.BusinessAppsPlatform"
    /// (plural) is a latent typo, corrected here.
    /// </summary>
    private const string ProviderNamespace = "Microsoft.BusinessAppPlatform";

    /// <summary>
    /// api-version ported verbatim from Provision-Customer.ps1 STEP 5/6.
    /// </summary>
    private const string ApiVersion = "2023-06-01";

    private const string EnvironmentsListUri =
        $"https://api.bap.microsoft.com/providers/{ProviderNamespace}/scopes/admin/environments?api-version={ApiVersion}";

    private const string EnvironmentsCreateUri =
        $"https://api.bap.microsoft.com/providers/{ProviderNamespace}/environments?api-version={ApiVersion}";

    private static string EnvironmentStatusUri(string envId)
        => $"https://api.bap.microsoft.com/providers/{ProviderNamespace}/scopes/admin/environments/{envId}?api-version={ApiVersion}";

    private readonly HttpClient _httpClient;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly DataverseEnvCreationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BapRestEnvironmentCreator> _logger;

    /// <summary>Constructs the creator with a typed <see cref="HttpClient"/> (production via <c>services.AddHttpClient</c>).</summary>
    public BapRestEnvironmentCreator(
        HttpClient httpClient,
        IOptions<DataverseEnvCreationOptions> options,
        ILogger<BapRestEnvironmentCreator> logger)
        : this(
            httpClient,
            tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),
            options,
            logger,
            TimeProvider.System)
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// (so tests never invoke the real DefaultAzureCredential chain)
    /// alongside a fake-transport <see cref="HttpClient"/> + <see cref="TimeProvider"/>.
    /// </summary>
    internal BapRestEnvironmentCreator(
        HttpClient httpClient,
        Func<string, TokenCredential> credentialFactory,
        IOptions<DataverseEnvCreationOptions> options,
        ILogger<BapRestEnvironmentCreator> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClient = httpClient;
        _credentialFactory = credentialFactory;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<DataverseEnvCreationOutcome> CreateEnvironmentAsync(
        DataverseEnvCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Tier);

        var deadline = _timeProvider.GetUtcNow() + _options.CreationTimeout;
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? $"{request.CustomerId} (Spaarke)"
            : request.DisplayName!;
        var domainName = request.CustomerId;
        var credential = _credentialFactory(request.TenantId);

        _logger.LogInformation(
            "H5 BAP-REST create-environment starting: customerId={CustomerId} region={Region} tier={Tier}",
            request.CustomerId, request.Region, request.Tier);

        // (1) Idempotent existing-environment check — ports STEP 5's "already
        // exists" branch. Covers the resume-after-partial-success window the
        // handler's own CompletedPhases idempotency does not (see file header).
        string? envId;
        try
        {
            var existing = await FindExistingEnvironmentAsync(credential, domainName, displayName, cancellationToken)
                .ConfigureAwait(false);
            if (existing is { InstanceUrl: not null } found
                && string.Equals(found.ProvisioningState, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "H5 BAP-REST environment already exists (idempotent): customerId={CustomerId} envId={EnvId}",
                    request.CustomerId, found.Name);
                return new DataverseEnvCreationOutcome.Success(found.InstanceUrl);
            }
            envId = existing?.Name;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Parity with the script's own catch-and-proceed on the list
            // check (STEP 5: "Could not list environments... Proceeding with
            // creation attempt..."). A list-check failure is not itself
            // fatal — the CREATE call is the authoritative next step.
            _logger.LogWarning(ex,
                "H5 BAP-REST existing-environment list check failed — proceeding with create attempt: customerId={CustomerId}",
                request.CustomerId);
            envId = null;
        }

        // (2) CREATE — only if no existing (or still-provisioning) environment was found.
        if (envId is null)
        {
            var createOutcome = await CreateAsync(
                credential, request, displayName, domainName, cancellationToken).ConfigureAwait(false);
            if (createOutcome is CreateResult.Failed failed)
            {
                return new DataverseEnvCreationOutcome.Failure(failed.Kind, failed.Diagnostic);
            }
            envId = ((CreateResult.Started)createOutcome).EnvId;
        }

        // (3) POLL until terminal state or deadline.
        return await PollUntilTerminalAsync(credential, envId, deadline, request.CustomerId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ExistingEnvironment?> FindExistingEnvironmentAsync(
        TokenCredential credential, string domainName, string displayName, CancellationToken cancellationToken)
    {
        var token = await AcquireTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, EnvironmentsListUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"BAP admin environments list failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body, 400)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var env in valueArray.EnumerateArray())
        {
            var record = ParseEnvironmentRecord(env);
            if (record is null)
            {
                continue;
            }
            if (string.Equals(record.DomainName, domainName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }
        return null;
    }

    private async Task<CreateResult> CreateAsync(
        TokenCredential credential,
        DataverseEnvCreationRequest request,
        string displayName,
        string domainName,
        CancellationToken cancellationToken)
    {
        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CreateResult.Failed(
                DataverseEnvCreationFailureKind.AuthFailure,
                $"BAP admin API token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        // Body shape ported verbatim from STEP 5's $createBody, with
        // environmentSku sourced from request.Tier (see file header —
        // faithful completion of the port, not a new design).
        var createBody = new
        {
            properties = new
            {
                displayName,
                description = $"Spaarke customer environment for {displayName}",
                environmentSku = request.Tier,
                azureRegion = request.Region,
                linkedEnvironmentMetadata = new
                {
                    baseLanguage = 1033,
                    domainName,
                    currency = new { code = "USD" },
                },
            },
            location = request.Region,
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, EnvironmentsCreateUri)
        {
            Content = JsonContent.Create(createBody),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CreateResult.Failed(
                DataverseEnvCreationFailureKind.Timeout,
                $"BAP admin create-environment POST timed out for customerId '{request.CustomerId}'.");
        }
        catch (HttpRequestException ex)
        {
            return new CreateResult.Failed(
                DataverseEnvCreationFailureKind.UnknownInvocationFailure,
                $"BAP admin create-environment POST infrastructure error: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var kind = ClassifyHttpFailure(response.StatusCode, body);
                var diagnostic =
                    $"BAP admin create-environment POST failed for customerId '{request.CustomerId}': " +
                    $"{(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body, 800)}";
                return new CreateResult.Failed(kind, diagnostic);
            }

            var envId = TryParseEnvId(body);
            if (string.IsNullOrWhiteSpace(envId))
            {
                return new CreateResult.Failed(
                    DataverseEnvCreationFailureKind.UnknownInvocationFailure,
                    $"BAP admin create-environment POST returned {(int)response.StatusCode} but the response body had no " +
                    $"'name' (envId) field for customerId '{request.CustomerId}'. Body: {Truncate(body, 400)}");
            }

            _logger.LogInformation(
                "H5 BAP-REST environment creation initiated: customerId={CustomerId} envId={EnvId}",
                request.CustomerId, envId);
            return new CreateResult.Started(envId);
        }
    }

    private async Task<DataverseEnvCreationOutcome> PollUntilTerminalAsync(
        TokenCredential credential, string envId, DateTimeOffset deadline, string customerId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EnvironmentRecordBase? status = null;
            try
            {
                var token = await AcquireTokenAsync(credential, cancellationToken).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Get, EnvironmentStatusUri(envId));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    status = ParseEnvironmentRecord(doc.RootElement);
                }
                else
                {
                    _logger.LogWarning(
                        "H5 BAP-REST poll returned {StatusCode} for envId={EnvId} — retrying: {Body}",
                        (int)response.StatusCode, envId, Truncate(body, 200));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Parity with STEP 6's catch-and-retry (poll errors are
                // transient; only a confirmed terminal state or the deadline
                // stops the loop).
                _logger.LogWarning(ex, "H5 BAP-REST poll error for envId={EnvId} — retrying", envId);
            }

            if (status is not null)
            {
                if (string.Equals(status.ProvisioningState, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    return new DataverseEnvCreationOutcome.Success(status.InstanceUrl ?? string.Empty);
                }
                if (string.Equals(status.ProvisioningState, "Failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status.ProvisioningState, "Deleted", StringComparison.OrdinalIgnoreCase))
                {
                    return new DataverseEnvCreationOutcome.Failure(
                        DataverseEnvCreationFailureKind.ProvisioningFailed,
                        $"BAP reported terminal provisioningState='{status.ProvisioningState}' for customerId '{customerId}' envId '{envId}'.");
                }
                // Any other state (e.g. "Provisioning", "LinkedMetadataProvisioning") -> keep polling.
            }

            var now = _timeProvider.GetUtcNow();
            if (now >= deadline)
            {
                return new DataverseEnvCreationOutcome.Failure(
                    DataverseEnvCreationFailureKind.Timeout,
                    $"BAP-REST env creation for customerId '{customerId}' (envId '{envId}') did not reach a terminal " +
                    $"provisioningState within {_options.CreationTimeout}.");
            }

            var remaining = deadline - now;
            var delay = _options.AsyncOperationPollInterval < remaining ? _options.AsyncOperationPollInterval : remaining;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<AccessToken> AcquireTokenAsync(TokenCredential credential, CancellationToken cancellationToken)
        => await credential.GetTokenAsync(new TokenRequestContext(new[] { BapScope }), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Heuristic HTTP-response-shape classifier — adapts
    /// PacAdminDataverseEnvCreator.ClassifyStderr's text-based heuristics to
    /// the BAP REST create call's status code + response body. Exposed
    /// internal for unit testing.
    /// </summary>
    internal static DataverseEnvCreationFailureKind ClassifyHttpFailure(HttpStatusCode statusCode, string body)
    {
        var lower = (body ?? string.Empty).ToLowerInvariant();

        // Domain conflict is checked FIRST and independently of status code
        // text-matching below — it is the acceptance-criterion-mandated
        // distinct classification (not folded into PartialProvisioning).
        if ((statusCode == HttpStatusCode.Conflict || statusCode == HttpStatusCode.BadRequest)
            && lower.Contains("domain", StringComparison.Ordinal)
            && (lower.Contains("already", StringComparison.Ordinal)
                || lower.Contains("taken", StringComparison.Ordinal)
                || lower.Contains("conflict", StringComparison.Ordinal)
                || lower.Contains("exists", StringComparison.Ordinal)))
        {
            return DataverseEnvCreationFailureKind.DomainAlreadyExists;
        }

        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden
            || lower.Contains("unauthorized", StringComparison.Ordinal)
            || lower.Contains("not authorized", StringComparison.Ordinal)
            || lower.Contains("forbidden", StringComparison.Ordinal)
            || lower.Contains("access denied", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.AuthFailure;
        }

        if ((int)statusCode == 429
            || lower.Contains("rate limit", StringComparison.Ordinal)
            || lower.Contains("throttle", StringComparison.Ordinal)
            || lower.Contains("too many requests", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.RateLimited;
        }

        if (lower.Contains("quota", StringComparison.Ordinal)
            || lower.Contains("capacity", StringComparison.Ordinal)
            || lower.Contains("no more environments", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.QuotaExhausted;
        }

        if (lower.Contains("already exists", StringComparison.Ordinal)
            || lower.Contains("conflicting", StringComparison.Ordinal)
            || lower.Contains("partial", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.PartialProvisioning;
        }

        return DataverseEnvCreationFailureKind.UnknownInvocationFailure;
    }

    private static string? TryParseEnvId(string createResponseBody)
    {
        if (string.IsNullOrWhiteSpace(createResponseBody))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(createResponseBody);
            return doc.RootElement.TryGetProperty("name", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a single environment JSON element (from either the list
    /// response's <c>value[]</c> array or a direct-GET status response) into
    /// the fields this creator needs: <c>name</c> (envId),
    /// <c>properties.displayName</c>, <c>properties.provisioningState</c>,
    /// <c>properties.linkedEnvironmentMetadata.domainName</c>, and
    /// <c>properties.linkedEnvironmentMetadata.instanceUrl</c>.
    /// </summary>
    internal static ExistingEnvironment? ParseEnvironmentRecord(JsonElement env)
    {
        if (!env.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var name = nameEl.GetString()!;

        string? displayName = null;
        string? provisioningState = null;
        string? domainName = null;
        string? instanceUrl = null;

        if (env.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            if (props.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
            {
                displayName = dn.GetString();
            }
            if (props.TryGetProperty("provisioningState", out var ps) && ps.ValueKind == JsonValueKind.String)
            {
                provisioningState = ps.GetString();
            }
            if (props.TryGetProperty("linkedEnvironmentMetadata", out var lem) && lem.ValueKind == JsonValueKind.Object)
            {
                if (lem.TryGetProperty("domainName", out var dom) && dom.ValueKind == JsonValueKind.String)
                {
                    domainName = dom.GetString();
                }
                if (lem.TryGetProperty("instanceUrl", out var iu) && iu.ValueKind == JsonValueKind.String)
                {
                    instanceUrl = iu.GetString();
                }
            }
        }

        return new ExistingEnvironment(name, displayName, provisioningState, domainName, instanceUrl);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    /// <summary>Base shape shared by list-entries and direct status-poll responses.</summary>
    internal abstract record EnvironmentRecordBase(string Name, string? ProvisioningState, string? InstanceUrl);

    /// <summary>A parsed BAP admin environment record — used for both the existing-check and the poll loop.</summary>
    internal sealed record ExistingEnvironment(
        string Name, string? DisplayName, string? ProvisioningState, string? DomainName, string? InstanceUrl)
        : EnvironmentRecordBase(Name, ProvisioningState, InstanceUrl);

    private abstract record CreateResult
    {
        private CreateResult() { }
        public sealed record Started(string EnvId) : CreateResult;
        public sealed record Failed(DataverseEnvCreationFailureKind Kind, string Diagnostic) : CreateResult;
    }
}
