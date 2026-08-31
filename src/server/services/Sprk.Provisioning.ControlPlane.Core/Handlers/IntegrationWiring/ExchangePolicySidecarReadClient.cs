// -----------------------------------------------------------------------------
// ExchangePolicySidecarReadClient.cs
//
// Task 180 (Wave G-7 -- H13 T4 acceptance-gate probe, sidecar read-route).
// Production <see cref="IExchangePolicyReadClient"/>: typed HttpClient GETting
// to the sitecontainer-private sidecar's NEW GET /policies route (task 114's
// Listener.ps1 + this task's route extension). Sibling to task 161's
// <see cref="ExchangePolicySidecarClient"/>: SAME sidecar, SAME auth model,
// SAME shared-secret KV read path -- disjoint routes serving disjoint
// semantics (write vs read).
//
// GROUND-TRUTHED WIRE CONTRACT (Listener.ps1 line 65-96 authoritative --
// where any prose disagrees, Listener.ps1 wins because it is the deployable
// artifact):
//
//   REQUEST  GET /policies?tenantId=<id>&correlationId=<id>
//     Headers:
//       X-Sidecar-Auth: <per-boot shared secret from platform KV>
//
//   RESPONSE 200
//     { outcome:         "Success" | "Failure",
//       observedAppIds:  [<string>, ...],   // distinct AppIds
//       observedCount:   <int>,
//       policies:        [ { appId, description, policyScopeGroupId }, ... ],
//       diagnostic:      <string> }
//
//   RESPONSE 401 missing/mismatched X-Sidecar-Auth (permanent, no retry)
//   RESPONSE 404 route not served (permanent, no retry -- deployment gap)
//   RESPONSE 5xx server-side transient (surfaces as Failure -- H13 Resumable)
//
// RETRY DISCIPLINE:
//   Deliberately NO in-client retry. This is a READ probe -- H13's own
//   handler classifies transient InfraFault outcomes as Resumable and the
//   reconciler re-enqueues the H13 run. Adding client-side retry here would
//   double-count the wait budget against the acceptance-gate's own
//   retry-vs-quarantine timing (design.md s4C). Contrast with task 161's
//   write client which retries once inside the client for transient EXO
//   throttling because a re-enqueued H14a write costs more than a 5s pause
//   -- for a read, the reconciler path IS cheap enough.
//
// LOUD-FAIL DISCIPLINE (silent-fail-audit, task 161 pattern replicated):
//   - shared-secret config missing (empty vault/subscription/name) -> Failure
//     "SidecarSharedSecret* config missing -- L2 not configured to
//     authenticate to sidecar" (never proceed with empty header).
//   - shared-secret KV NotFound / Failure -> Failure with pass-through
//     diagnostic naming the secret + vault.
//   - transport exception -> Failure with "reconciler will re-enqueue".
//   - HTTP 401 -> Failure naming both the KV secret name AND vault to check.
//   - HTTP 200 + malformed JSON -> Failure with parse error + body tail.
//   - HTTP 200 + outcome=Failure -> Failure passing sidecar diagnostic
//     through.
//   - HTTP 200 + unknown outcome value -> Failure naming the expected set
//     (never silent-Success-fall-through).
//   - HTTP 200 + outcome=Success + null observedAppIds/policies array ->
//     TREATED AS EMPTY SET (defensive normalization; Listener.ps1 always
//     emits [] rather than null but downstream JSON.Net variation could
//     produce null).
//   - CorrelationId empty (contract violation by caller) -> Failure BEFORE
//     any HTTP call (parity with task 161's AC-15).
//   - TenantId empty (I1 no-hardcoded-tenant guard) -> Failure BEFORE any
//     HTTP call.
//
// PLACEMENT (CLAUDE.md s10 -- BFF hygiene): Sprk.Provisioning.ControlPlane.Core
// (L2, NOT BFF). Same folder as its sibling ExchangePolicySidecarClient.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// HTTP client for the H14a Exchange ApplicationAccessPolicy sidecar's
/// GET /policies read route (Listener.ps1, task 114 + task-180 extension).
/// </summary>
public sealed class ExchangePolicySidecarReadClient : IExchangePolicyReadClient
{
    /// <summary>Wire route for the read-policies endpoint (Listener.ps1 GET /policies).</summary>
    public const string ReadPoliciesPath = "/policies";

    /// <summary>Wire outcome value: enumeration succeeded.</summary>
    public const string WireOutcomeSuccess = "Success";

    /// <summary>Wire outcome value: enumeration could not run (cert/EXO/read failure).</summary>
    public const string WireOutcomeFailure = "Failure";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IKvSecretReader _kvSecretReader;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<ExchangePolicySidecarReadClient> _logger;

    /// <summary>
    /// Production constructor. Registered via
    /// <c>AddHttpClient&lt;IExchangePolicyReadClient, ExchangePolicySidecarReadClient&gt;()</c>
    /// in <see cref="IntegrationWiringModule.AddH14IntegrationWiringHandler"/> --
    /// parity with the sibling write client's own typed-HttpClient registration.
    /// </summary>
    public ExchangePolicySidecarReadClient(
        HttpClient httpClient,
        IKvSecretReader kvSecretReader,
        IOptions<IntegrationWiringOptions> options,
        ILogger<ExchangePolicySidecarReadClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(kvSecretReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _kvSecretReader = kvSecretReader;
        _options = options.Value;
        _logger = logger;

        // Configure BaseAddress + Timeout ONLY when the injected HttpClient
        // has neither pre-set -- treats an already-configured BaseAddress as
        // the "test seam owns HttpClient config" signal (parity with sibling
        // ExchangePolicySidecarClient's constructor).
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.SidecarBaseUrl);
            _httpClient.Timeout = _options.SidecarRequestTimeout;
        }
    }

    /// <inheritdoc/>
    public async Task<ExchangePolicyReadOutcome> ReadAsync(
        ExchangePolicyReadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return new ExchangePolicyReadOutcome.Failure(
                "TenantId is required (s4D I1 -- explicit tenant scope; never rely on ambient default-tenant).");
        }
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            return new ExchangePolicyReadOutcome.Failure(
                "CorrelationId is required -- H13 T4 probe MUST pass envelope.RunId so sidecar stdout logs " +
                "interleave with Worker logs by RunId in Log Analytics.");
        }

        // Resolve the per-boot shared secret from platform KV (parity with
        // sibling write client's ResolveSharedSecretAsync).
        var secretResolution = await ResolveSharedSecretAsync(cancellationToken).ConfigureAwait(false);
        if (secretResolution.Failure is not null)
        {
            return secretResolution.Failure;
        }
        var sharedSecret = secretResolution.Secret!;

        // Build query string via Uri.EscapeDataString -- URL-encoding for
        // tenantId + correlationId is defense-in-depth (both are typically
        // GUIDs so encoding is a no-op, but hardcoding string concatenation
        // of caller-supplied strings without escaping is a maintenance
        // anti-pattern -- if either ever came from user input the escape
        // discipline is already correct).
        var relativeUri = $"{ReadPoliciesPath}?tenantId={Uri.EscapeDataString(request.TenantId)}"
            + $"&correlationId={Uri.EscapeDataString(request.CorrelationId)}";

        HttpResponseMessage? response;
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, relativeUri);
            httpRequest.Headers.TryAddWithoutValidation(
                ExchangePolicySidecarClient.SharedSecretHeaderName, sharedSecret);

            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException oce)
        {
            _logger.LogWarning(oce,
                "T4 read sidecar GET {Path} timed out after {Timeout} (correlationId={CorrelationId})",
                ReadPoliciesPath, _httpClient.Timeout, request.CorrelationId);
            return new ExchangePolicyReadOutcome.Failure(
                $"Sidecar GET {ReadPoliciesPath} timed out after {_httpClient.Timeout} " +
                $"(correlationId={request.CorrelationId}). Reconciler will re-enqueue.");
        }
        catch (HttpRequestException hre)
        {
            _logger.LogWarning(hre,
                "T4 read sidecar GET {Path} transport failure (correlationId={CorrelationId})",
                ReadPoliciesPath, request.CorrelationId);
            return new ExchangePolicyReadOutcome.Failure(
                $"Sidecar GET {ReadPoliciesPath} transport failure: {hre.GetType().Name}: {hre.Message} " +
                $"(correlationId={request.CorrelationId}). Reconciler will re-enqueue.");
        }

        try
        {
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (status == (int)HttpStatusCode.Unauthorized)
            {
                _logger.LogError(
                    "T4 read sidecar rejected X-Sidecar-Auth (HTTP 401, correlationId={CorrelationId})",
                    request.CorrelationId);
                return new ExchangePolicyReadOutcome.Failure(
                    $"Sidecar rejected X-Sidecar-Auth (HTTP 401, correlationId={request.CorrelationId}) -- " +
                    $"verify platform KV secret '{_options.SidecarSharedSecretName}' on vault " +
                    $"'{_options.SidecarSharedSecretVaultName}' matches the sidecar container's " +
                    "SIDECAR_SHARED_SECRET env-var-KeyVault-reference value. Body: " +
                    Truncate(body, 300));
            }
            if (status == (int)HttpStatusCode.NotFound)
            {
                return new ExchangePolicyReadOutcome.Failure(
                    $"Sidecar returned HTTP 404 for GET {ReadPoliciesPath} (correlationId={request.CorrelationId}) -- " +
                    $"the deployed Listener.ps1 does not serve this route (deployment gap: task 180 route not " +
                    $"live at the running sidecar). Body: {Truncate(body, 300)}");
            }
            if (status >= 500)
            {
                return new ExchangePolicyReadOutcome.Failure(
                    $"Sidecar returned HTTP {status} for GET {ReadPoliciesPath} " +
                    $"(correlationId={request.CorrelationId}): {Truncate(body, 400)}. Reconciler will re-enqueue.");
            }
            if (status != (int)HttpStatusCode.OK)
            {
                return new ExchangePolicyReadOutcome.Failure(
                    $"Sidecar returned unexpected HTTP {status} for GET {ReadPoliciesPath} " +
                    $"(correlationId={request.CorrelationId}): {Truncate(body, 400)}");
            }

            return MapWireResponse(body, request.CorrelationId);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static ExchangePolicyReadOutcome MapWireResponse(string body, string correlationId)
    {
        SidecarReadPoliciesResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SidecarReadPoliciesResponse>(body, SerializerOptions);
        }
        catch (JsonException jex)
        {
            return new ExchangePolicyReadOutcome.Failure(
                $"Sidecar returned HTTP 200 with unparseable JSON body (correlationId={correlationId}): " +
                $"{jex.Message}. Body: {Truncate(body, 400)}");
        }

        if (parsed is null)
        {
            return new ExchangePolicyReadOutcome.Failure(
                $"Sidecar returned HTTP 200 with null-deserialized body (correlationId={correlationId}). " +
                $"Raw: {Truncate(body, 400)}");
        }

        // Defensive normalization: Listener.ps1 always emits [] for empty
        // arrays but downstream JSON serializer variation (or a proxy) could
        // produce null -- treat as empty set rather than throwing.
        var observedAppIds = (IReadOnlyList<string>)(parsed.ObservedAppIds ?? Array.Empty<string>());
        var policies = (parsed.Policies ?? Array.Empty<SidecarPolicyEntry>())
            .Select(p => new ExchangePolicyEntry(
                AppId: p.AppId ?? string.Empty,
                Description: p.Description ?? string.Empty,
                PolicyScopeGroupId: p.PolicyScopeGroupId ?? string.Empty))
            .ToArray();

        switch (parsed.Outcome)
        {
            case WireOutcomeSuccess:
                return new ExchangePolicyReadOutcome.Success(observedAppIds, policies);

            case WireOutcomeFailure:
                var diag = string.IsNullOrEmpty(parsed.Diagnostic) ? "(no diagnostic)" : parsed.Diagnostic;
                return new ExchangePolicyReadOutcome.Failure(
                    $"Sidecar wire Failure (correlationId={correlationId}): {diag}");

            default:
                return new ExchangePolicyReadOutcome.Failure(
                    $"Sidecar returned HTTP 200 with unknown outcome '{parsed.Outcome ?? "(null)"}' " +
                    $"(correlationId={correlationId}). Expected one of: {WireOutcomeSuccess}, {WireOutcomeFailure}. " +
                    $"Diagnostic: {(string.IsNullOrEmpty(parsed.Diagnostic) ? "(none)" : parsed.Diagnostic)}");
        }
    }

    private async Task<SharedSecretResolution> ResolveSharedSecretAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SidecarSharedSecretVaultName)
            || string.IsNullOrWhiteSpace(_options.SidecarSharedSecretSubscriptionId)
            || string.IsNullOrWhiteSpace(_options.SidecarSharedSecretName))
        {
            return SharedSecretResolution.Fail(new ExchangePolicyReadOutcome.Failure(
                $"Sidecar shared secret config missing: " +
                $"SidecarSharedSecretVaultName='{_options.SidecarSharedSecretVaultName}', " +
                $"SidecarSharedSecretSubscriptionId='{_options.SidecarSharedSecretSubscriptionId}', " +
                $"SidecarSharedSecretName='{_options.SidecarSharedSecretName}'. " +
                "L2 cannot authenticate to sidecar without a shared secret -- bind all three " +
                "IntegrationWiring:SidecarSharedSecret* app-settings from platform KV references."));
        }

        try
        {
            var kvResult = await _kvSecretReader.ReadSecretAsync(
                _options.SidecarSharedSecretVaultName,
                _options.SidecarSharedSecretSubscriptionId,
                _options.SidecarSharedSecretName,
                cancellationToken).ConfigureAwait(false);

            switch (kvResult)
            {
                case KvSecretReadResult.Success s:
                    return SharedSecretResolution.Succeed(s.Value);

                case KvSecretReadResult.NotFound:
                    return SharedSecretResolution.Fail(new ExchangePolicyReadOutcome.Failure(
                        $"Platform KV secret '{_options.SidecarSharedSecretName}' not found on vault " +
                        $"'{_options.SidecarSharedSecretVaultName}'. Verify the platform KV secret has " +
                        "been provisioned + the L2 UAMI holds the 'Key Vault Secrets User' RBAC role."));

                case KvSecretReadResult.Failure kvf:
                    return SharedSecretResolution.Fail(new ExchangePolicyReadOutcome.Failure(
                        $"Platform KV read failed for shared secret '{_options.SidecarSharedSecretName}' " +
                        $"on vault '{_options.SidecarSharedSecretVaultName}': {kvf.Diagnostic}"));

                default:
                    return SharedSecretResolution.Fail(new ExchangePolicyReadOutcome.Failure(
                        $"Unhandled {nameof(KvSecretReadResult)} subtype '{kvResult.GetType().Name}' " +
                        "reading sidecar shared secret."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return SharedSecretResolution.Fail(new ExchangePolicyReadOutcome.Failure(
                $"Unexpected exception resolving sidecar shared secret from platform KV: " +
                $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...[truncated]");

    // -------------------------------------------------------------------------
    // Wire DTOs (internal so tests can construct/assert them directly).
    // -------------------------------------------------------------------------

    internal sealed class SidecarReadPoliciesResponse
    {
        [JsonPropertyName("outcome")]
        public string? Outcome { get; init; }

        [JsonPropertyName("observedAppIds")]
        public string[]? ObservedAppIds { get; init; }

        [JsonPropertyName("observedCount")]
        public int ObservedCount { get; init; }

        [JsonPropertyName("policies")]
        public SidecarPolicyEntry[]? Policies { get; init; }

        [JsonPropertyName("diagnostic")]
        public string? Diagnostic { get; init; }
    }

    internal sealed class SidecarPolicyEntry
    {
        [JsonPropertyName("appId")]
        public string? AppId { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("policyScopeGroupId")]
        public string? PolicyScopeGroupId { get; init; }
    }

    private readonly struct SharedSecretResolution
    {
        public string? Secret { get; }
        public ExchangePolicyReadOutcome.Failure? Failure { get; }

        private SharedSecretResolution(string? secret, ExchangePolicyReadOutcome.Failure? failure)
        {
            Secret = secret;
            Failure = failure;
        }

        public static SharedSecretResolution Succeed(string secret) => new(secret, null);
        public static SharedSecretResolution Fail(ExchangePolicyReadOutcome.Failure failure) => new(null, failure);
    }
}
