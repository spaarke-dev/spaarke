// -----------------------------------------------------------------------------
// ExchangePolicySidecarClient.cs
//
// Production <see cref="IExchangePolicyApplier"/> — task 161 (Wave G-6, Option
// D hybrid) sidecar client wiring. Replaces
// <see cref="ExchangePolicyScriptApplier"/> (RETIRED, kept on disk unregistered
// — see that file's retirement banner). Posts to the sitecontainer-private
// sidecar (task 114's `Listener.ps1` on http://127.0.0.1:8091/apply-policy)
// per DS-1b §3's exact message contract; the sidecar owns the
// ExchangeOnlineManagement module + Connect-ExchangeOnline / Get- /
// New-ApplicationAccessPolicy. This client maps the sidecar's 4-outcome wire
// envelope (Success | AlreadyCompliant | Drift | Failure — see Listener.ps1
// .DESCRIPTION envelope-mapping rules; Drift is preserved as a distinct wire
// state to close the T4 silent-fail regression H14a exists to close) onto the
// existing 3-case <see cref="ExchangePolicyApplyOutcome"/> that the retired
// script applier's exit-code mapper produced: wire Success and
// AlreadyCompliant both collapse to <see cref="ExchangePolicyApplyOutcome.Applied"/>
// (differentiated by CreatedCount).
//
// GROUND-TRUTHED WIRE CONTRACT (task 114 Listener.ps1 line 30-44 authoritative
// per POML escalation trigger — where the POML's own 3-outcome shorthand
// disagrees with Listener.ps1's 4-outcome shape, Listener.ps1 wins because it
// is the deployable artifact):
//
//   REQUEST  POST /apply-policy
//     Headers:
//       Content-Type:   application/json
//       X-Sidecar-Auth: <per-boot shared secret from platform KV>
//     Body:
//       { tenantId, expectedAppIds[2], policyScopeGroupId,
//         descriptionPrefix?, correlationId, timeoutSeconds? }
//
//   RESPONSE 200
//     { outcome: "Success"|"AlreadyCompliant"|"Drift"|"Failure",
//       createdCount:    <int 0..2>,
//       expectedAppIds:  [<string>, <string>],
//       observedAppIds:  [<string>, ...],
//       policiesApplied: [<string>, ...],   // subset newly created this call
//       diagnostic:      <string> }
//
//   RESPONSE 400 body-parse or validation error (permanent, no retry)
//   RESPONSE 401 missing/mismatched X-Sidecar-Auth (permanent, no retry)
//   RESPONSE 404 unknown route (permanent, no retry — routing bug)
//   RESPONSE 502 cert fetch failure (potentially transient — one retry)
//   RESPONSE 5xx server-side transient (one retry with backoff per DS-1b §3)
//
// RETRY DISCIPLINE (DS-1b §3): "One retry with backoff inside the client for
// transient EXO throttling; everything else defers to the run-level §4C retry
// machinery, exactly like every other collaborator." Implemented as: one
// retry with SidecarTransientRetryDelay backoff on {HTTP 5xx, HTTP 200 +
// outcome=Failure}; NO in-client retry on {HTTP 4xx, HTTP 200 + Success /
// AlreadyCompliant / Drift, transport exceptions (HttpRequestException /
// TimeoutException — those propagate to a Failure outcome that H14a
// classifies as Resumable, letting the reconciler re-enqueue at the run
// level, which is exactly what DS-1b calls out for connection-refused /
// timeout).
//
// SHARED-SECRET DEFENSE-IN-DEPTH: DS-1b §3 mandates "localhost-only binding +
// a per-boot shared-secret header sourced from the platform KV." The sidecar
// reads the SAME secret from its own SIDECAR_SHARED_SECRET env var (an
// App-Service-resolved KeyVault reference to the same platform KV secret);
// this client reads it via <see cref="IKvSecretReader"/> (task 160's
// SecretClientKvReader) — so neither side trusts the other's env exposure.
// The header is sent on EVERY request per the acceptance-criterion test.
//
// LOUD-FAIL DISCIPLINE (silent-fail-audit, waves G-4/G-5 pattern): every
// error condition that could otherwise silently manifest as an
// unauthenticated call or a swallowed exception returns an explicit
// <see cref="ExchangePolicyApplyOutcome.Failure"/> with a diagnostic that
// names what to check:
//   - shared-secret config missing (empty vault/subscription/name) -> Failure
//     "SidecarSharedSecret* config missing — L2 not configured to
//     authenticate to sidecar" (never proceed with empty header).
//   - shared-secret KV NotFound -> Failure "platform KV secret 'X' not found
//     on vault 'Y'".
//   - shared-secret KV Failure -> Failure passes through KV diagnostic.
//   - transport exception -> Failure with type+message, "reconciler will
//     re-enqueue" (DS-1b maps this to InfraFault=Resumable).
//   - HTTP 401 -> Failure naming both the KV secret name and vault to check.
//   - HTTP 200 + malformed JSON -> Failure with parse error + body tail.
//   - HTTP 200 + unknown outcome value -> Failure naming the outcome.
//   - CorrelationId empty (contract violation by caller) -> Failure.
//
// SEAM JUSTIFICATION (ADR-010): satisfies the ≥2-impl gate — production
// sidecar client + per-unit-test fakes (H14a's own FakeApplier + this file's
// own contract test) + the retired-on-disk script applier. Same posture as
// the retired reader (task 160): fake-transport unit tests only (a
// hand-rolled HttpMessageHandler; NEVER Mock<HttpMessageHandler>, ADR-038
// path #1). No live sidecar in the CI unit suite — that is task 162's
// end-to-end verification.
//
// PLACEMENT / COMPONENT JUSTIFICATION (CLAUDE.md §11): Existing —
// IExchangePolicyApplier already exists as the seam ExchangePolicyScriptApplier
// implemented; this task provides a second implementation behind the SAME
// interface, not a new one. Extension — the sidecar's message contract is
// entirely DS-1b's own design (§3), authored specifically for this reuse.
// Cost-of-doing-nothing — H14a (the T4 trap owner, the ONE real
// security-hardening step in the fleet with no SDK equivalent per DS-1b §0's
// verified finding) has no way to reach the sidecar's functionality at all;
// the whole point of building the sidecar (task 114) is unrealized without
// this client.
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// HTTP client for the H14a Exchange ApplicationAccessPolicy sidecar
/// (Listener.ps1, task 114). Maps its 4-outcome wire envelope onto the
/// 3-case <see cref="ExchangePolicyApplyOutcome"/> preserving the T4
/// silent-fail-trap-closing semantics (Drift is a distinct outcome; wire
/// Success + AlreadyCompliant both collapse to <see cref="ExchangePolicyApplyOutcome.Applied"/>).
/// </summary>
public sealed class ExchangePolicySidecarClient : IExchangePolicyApplier
{
    /// <summary>Wire header name for the per-boot shared secret (Listener.ps1 line 17 / 345).</summary>
    public const string SharedSecretHeaderName = "X-Sidecar-Auth";

    /// <summary>Wire route for the apply-policy endpoint (Listener.ps1 line 14 / 337).</summary>
    public const string ApplyPolicyPath = "/apply-policy";

    /// <summary>Wire outcome value: 2 policies created this call.</summary>
    public const string WireOutcomeSuccess = "Success";

    /// <summary>Wire outcome value: 2 policies already present, no create needed.</summary>
    public const string WireOutcomeAlreadyCompliant = "AlreadyCompliant";

    /// <summary>Wire outcome value: T4 drift — 2+ policies present but AppIds mismatch expected set.</summary>
    public const string WireOutcomeDrift = "Drift";

    /// <summary>Wire outcome value: infra-level failure inside sidecar (script exception, cert fetch fail, etc).</summary>
    public const string WireOutcomeFailure = "Failure";

    /// <summary>
    /// Advisory value sent on the wire's <c>timeoutSeconds</c> field —
    /// matches Listener.ps1's own default (line 27 / 374). Listener currently
    /// enforces caller-side (HttpClient.Timeout is the real bound); this is
    /// preserved as a constant referencing the source-of-truth default so
    /// pinning the wire value + the HTTP-level timeout stays audit-traceable.
    /// </summary>
    public const int ListenerAdvisoryTimeoutSeconds = 300;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IKvSecretReader _kvSecretReader;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<ExchangePolicySidecarClient> _logger;

    /// <summary>
    /// Constructs the production client. Registered by
    /// <see cref="IntegrationWiringModule.AddH14IntegrationWiringHandler"/> via
    /// <c>AddHttpClient&lt;IExchangePolicyApplier, ExchangePolicySidecarClient&gt;()</c>
    /// so <paramref name="httpClient"/> comes from <c>IHttpClientFactory</c>'s
    /// pool (parity with GraphRestSubscriptionCreator's own typed-HttpClient
    /// registration). Configures BaseAddress + Timeout from bound options on
    /// each construction — safe because typed-client instances are Transient
    /// and each gets a fresh HttpClient underneath (which itself is pooled via
    /// the factory's message-handler cache).
    /// </summary>
    public ExchangePolicySidecarClient(
        HttpClient httpClient,
        IKvSecretReader kvSecretReader,
        IOptions<IntegrationWiringOptions> options,
        ILogger<ExchangePolicySidecarClient> logger)
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
        // has neither pre-set — treats an already-configured BaseAddress as
        // the "test seam owns HttpClient config" signal. In production the
        // typed-HttpClient factory hands us a fresh HttpClient with default
        // (null) BaseAddress + default 100 s Timeout, so we always configure
        // it. In tests the HttpClient carries a pre-set BaseAddress pointing
        // at the fake transport's synthetic host + (optionally) a custom
        // Timeout the test uses to exercise the timeout branch (AC-9).
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.SidecarBaseUrl);
            _httpClient.Timeout = _options.SidecarRequestTimeout;
        }
    }

    /// <inheritdoc/>
    public async Task<ExchangePolicyApplyOutcome> ApplyAsync(
        ExchangePolicyApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PolicyScopeGroupId);
        if (request.ExpectedAppIds is not { Count: 2 })
        {
            return new ExchangePolicyApplyOutcome.Failure(
                $"ExpectedAppIds must contain exactly 2 entries (BFF app-reg + UAMI); got {request.ExpectedAppIds?.Count ?? 0}.");
        }
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            return new ExchangePolicyApplyOutcome.Failure(
                "CorrelationId is required — H14a MUST pass envelope.RunId per POML acceptance criterion 3 " +
                "(so sidecar stdout logs interleave with Worker logs by RunId in Log Analytics).");
        }

        // Resolve the per-boot shared secret from platform KV (defense-in-depth
        // per DS-1b §3). Empty/whitespace config values, KV NotFound, KV
        // Failure all short-circuit to explicit Failure outcomes — NEVER
        // proceed with an empty X-Sidecar-Auth header (would silently 401
        // from the sidecar, which is a silent-fail-adjacent trap).
        var secretResolution = await ResolveSharedSecretAsync(cancellationToken).ConfigureAwait(false);
        if (secretResolution.Failure is not null)
        {
            return secretResolution.Failure;
        }
        var sharedSecret = secretResolution.Secret!;

        var wireRequest = BuildWireRequest(request);

        // Two-attempt loop. First attempt: TrySendOnceAsync returns either a
        // terminal outcome (success or non-retryable failure) OR a
        // retry-eligible failure. If retry-eligible AND we have retries left,
        // wait and try once more; second retry-eligible failure surfaces as
        // Failure (H14a classifies as Resumable — reconciler re-enqueues).
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var attemptResult = await TrySendOnceAsync(wireRequest, sharedSecret, request.CorrelationId, attempt, cancellationToken)
                .ConfigureAwait(false);

            switch (attemptResult)
            {
                case AttemptResult.Terminal terminal:
                    return terminal.Outcome;

                case AttemptResult.RetryEligible retry:
                    if (attempt == 2)
                    {
                        _logger.LogWarning(
                            "H14a sidecar returned retry-eligible failure on retry attempt (correlationId={CorrelationId}): {Diagnostic}",
                            request.CorrelationId, retry.Diagnostic);
                        return new ExchangePolicyApplyOutcome.Failure(
                            $"Sidecar returned retry-eligible failure after one retry with {_options.SidecarTransientRetryDelay} backoff: " +
                            $"{retry.Diagnostic} (correlationId={request.CorrelationId}). Reconciler will re-enqueue.");
                    }
                    _logger.LogWarning(
                        "H14a sidecar returned retry-eligible failure on first attempt (correlationId={CorrelationId}); " +
                        "backing off {Delay} before retry: {Diagnostic}",
                        request.CorrelationId, _options.SidecarTransientRetryDelay, retry.Diagnostic);
                    // Task.Delay's own OperationCanceledException on caller-cancel is the correct
                    // exception here — propagate as-is (no try/catch shim needed; the token
                    // Task.Delay observes IS `cancellationToken`).
                    await Task.Delay(_options.SidecarTransientRetryDelay, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    // Unreachable — every AttemptResult subtype is handled above.
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(AttemptResult)} subtype '{attemptResult.GetType().Name}'.");
            }
        }

        // Unreachable — the loop either returns or throws.
        return new ExchangePolicyApplyOutcome.Failure(
            $"ExchangePolicySidecarClient retry loop exited unexpectedly (correlationId={request.CorrelationId}).");
    }

    private async Task<AttemptResult> TrySendOnceAsync(
        SidecarApplyPolicyRequest wireRequest,
        string sharedSecret,
        string correlationId,
        int attempt,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response;
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApplyPolicyPath);
            httpRequest.Headers.TryAddWithoutValidation(SharedSecretHeaderName, sharedSecret);
            httpRequest.Content = JsonContent.Create(wireRequest, options: SerializerOptions);

            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled — propagate.
            throw;
        }
        catch (OperationCanceledException oce)
        {
            // HttpClient.Timeout elapsed (surfaces as OperationCanceledException
            // in .NET 6+; distinct from caller-cancellation which we let
            // propagate above). DS-1b §3: connection-refused/timeout ->
            // InfraFault (Resumable, reconciler re-enqueues). NO in-client
            // retry per that rule.
            _logger.LogWarning(oce,
                "H14a sidecar POST {Path} timed out after {Timeout} (attempt={Attempt}, correlationId={CorrelationId})",
                ApplyPolicyPath, _httpClient.Timeout, attempt, correlationId);
            return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar POST {ApplyPolicyPath} timed out after {_httpClient.Timeout} " +
                $"(correlationId={correlationId}). DS-1b §3 InfraFault — reconciler will re-enqueue."));
        }
        catch (HttpRequestException hre)
        {
            // Connection refused, DNS failure, TCP reset before the response
            // arrived, etc. Same DS-1b §3 InfraFault semantics — no in-client
            // retry; reconciler re-enqueues at the run level.
            _logger.LogWarning(hre,
                "H14a sidecar POST {Path} transport failure (attempt={Attempt}, correlationId={CorrelationId})",
                ApplyPolicyPath, attempt, correlationId);
            return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar POST {ApplyPolicyPath} transport failure: {hre.GetType().Name}: {hre.Message} " +
                $"(correlationId={correlationId}). DS-1b §3 InfraFault — reconciler will re-enqueue."));
        }

        try
        {
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // ---- HTTP 5xx: server-side transient, retry-eligible ----
            if (status >= 500)
            {
                return AttemptResult.RetryEligible.Of(
                    $"HTTP {status} from sidecar: {Truncate(body, 400)}");
            }

            // ---- HTTP 401: permanent auth misconfiguration ----
            if (status == 401)
            {
                _logger.LogError(
                    "H14a sidecar rejected X-Sidecar-Auth (HTTP 401, correlationId={CorrelationId}) — " +
                    "Worker's platform-KV shared-secret does not match sidecar's SIDECAR_SHARED_SECRET env var value.",
                    correlationId);
                return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                    $"Sidecar rejected X-Sidecar-Auth (HTTP 401, correlationId={correlationId}) — " +
                    $"verify platform KV secret '{_options.SidecarSharedSecretName}' on vault " +
                    $"'{_options.SidecarSharedSecretVaultName}' matches the sidecar container's " +
                    "SIDECAR_SHARED_SECRET env-var-KeyVault-reference value. Body: " +
                    Truncate(body, 300)));
            }

            // ---- HTTP 404: routing bug ----
            if (status == 404)
            {
                return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                    $"Sidecar returned HTTP 404 for POST {ApplyPolicyPath} (correlationId={correlationId}) — " +
                    $"route mismatch. Verify Listener.ps1 accepts POST {ApplyPolicyPath}. Body: {Truncate(body, 300)}"));
            }

            // ---- HTTP 400: request-body validation error ----
            if (status == 400)
            {
                return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                    $"Sidecar rejected request body (HTTP 400, correlationId={correlationId}): {Truncate(body, 400)}"));
            }

            // ---- HTTP 200: parse envelope ----
            if (status == 200)
            {
                return MapWireResponse(body, correlationId);
            }

            // ---- Any other HTTP status: unexpected, treat as terminal Failure ----
            return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar returned unexpected HTTP {status} (correlationId={correlationId}): {Truncate(body, 400)}"));
        }
        finally
        {
            response.Dispose();
        }
    }

    private static AttemptResult MapWireResponse(string body, string correlationId)
    {
        SidecarApplyPolicyResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SidecarApplyPolicyResponse>(body, SerializerOptions);
        }
        catch (JsonException jex)
        {
            // Malformed JSON — LOUD-FAIL per silent-fail-audit discipline.
            // Never return a null-mapped outcome or silently retry.
            return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar returned HTTP 200 with unparseable JSON body (correlationId={correlationId}): " +
                $"{jex.Message}. Body: {Truncate(body, 400)}"));
        }

        if (parsed is null)
        {
            return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar returned HTTP 200 with null-deserialized body (correlationId={correlationId}). " +
                $"Raw: {Truncate(body, 400)}"));
        }

        var observedAppIds = parsed.ObservedAppIds ?? Array.Empty<string>();
        var expectedAppIds = parsed.ExpectedAppIds ?? Array.Empty<string>();
        var diagnostic = parsed.Diagnostic ?? string.Empty;

        // ----- Wire outcome mapping (Listener.ps1 .DESCRIPTION line 32-47) -----
        // Wire Success            + createdCount>0 -> Applied(createdCount, observedAppIds)  (this call created 1-2)
        // Wire AlreadyCompliant   + createdCount=0 -> Applied(0, observedAppIds)             (both already present)
        // Wire Drift                                -> Drift(expected, observed)             (T4 silent-fail trap)
        // Wire Failure                              -> RetryEligible (surfaces as Failure on 2nd attempt)
        switch (parsed.Outcome)
        {
            // Wire Success (createdCount>0) + wire AlreadyCompliant (createdCount=0)
            // both collapse to the 3-case Applied outcome — the wire distinguishes them
            // for T4-log auditability; the C# seam preserves the distinction via
            // CreatedCount.
            case WireOutcomeSuccess:
            case WireOutcomeAlreadyCompliant:
                return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Applied(parsed.CreatedCount, observedAppIds));

            case WireOutcomeDrift:
                return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Drift(expectedAppIds, observedAppIds));

            case WireOutcomeFailure:
                // DS-1b §3: retry ONCE with backoff for transient EXO
                // throttling. We treat all wire Failures as retry-eligible on
                // first attempt (a 5s backoff often clears throttling); if it
                // repeats, the retry loop surfaces the final Failure to H14a.
                return AttemptResult.RetryEligible.Of(
                    $"sidecar wire Failure: {(string.IsNullOrEmpty(diagnostic) ? "(no diagnostic)" : diagnostic)}");

            default:
                // Unknown outcome value — LOUD-FAIL per silent-fail-audit.
                // Never silently treat as Success or fall through.
                return AttemptResult.Terminal.Of(new ExchangePolicyApplyOutcome.Failure(
                    $"Sidecar returned HTTP 200 with unknown outcome '{parsed.Outcome ?? "(null)"}' " +
                    $"(correlationId={correlationId}). Expected one of: {WireOutcomeSuccess}, " +
                    $"{WireOutcomeAlreadyCompliant}, {WireOutcomeDrift}, {WireOutcomeFailure}. " +
                    $"Diagnostic: {(string.IsNullOrEmpty(diagnostic) ? "(none)" : diagnostic)}"));
        }
    }

    private static SidecarApplyPolicyRequest BuildWireRequest(ExchangePolicyApplyRequest request)
    {
        // descriptionPrefix is omitted from the wire when empty (Listener.ps1
        // defaults it server-side to "Spaarke-Provisioning-AppAccessPolicy").
        // timeoutSeconds is advisory on the wire (Listener.ps1 comment: the
        // caller-side HttpClient.Timeout is the real bound); we still send it
        // so the sidecar's log line records the requested budget.
        return new SidecarApplyPolicyRequest
        {
            TenantId = request.TenantId,
            ExpectedAppIds = request.ExpectedAppIds.ToArray(),
            PolicyScopeGroupId = request.PolicyScopeGroupId,
            DescriptionPrefix = string.IsNullOrEmpty(request.DescriptionPrefix) ? null : request.DescriptionPrefix,
            CorrelationId = request.CorrelationId,
            TimeoutSeconds = ListenerAdvisoryTimeoutSeconds,
        };
    }

    private async Task<SharedSecretResolution> ResolveSharedSecretAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SidecarSharedSecretVaultName)
            || string.IsNullOrWhiteSpace(_options.SidecarSharedSecretSubscriptionId)
            || string.IsNullOrWhiteSpace(_options.SidecarSharedSecretName))
        {
            return SharedSecretResolution.Fail(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar shared secret config missing: " +
                $"SidecarSharedSecretVaultName='{_options.SidecarSharedSecretVaultName}', " +
                $"SidecarSharedSecretSubscriptionId='{_options.SidecarSharedSecretSubscriptionId}', " +
                $"SidecarSharedSecretName='{_options.SidecarSharedSecretName}'. " +
                "L2 cannot authenticate to sidecar without a shared secret — bind all three " +
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
                    return SharedSecretResolution.Fail(new ExchangePolicyApplyOutcome.Failure(
                        $"Platform KV secret '{_options.SidecarSharedSecretName}' not found on vault " +
                        $"'{_options.SidecarSharedSecretVaultName}'. Sidecar's SIDECAR_SHARED_SECRET " +
                        "env-var-KeyVault-reference must resolve to the same secret name — verify the " +
                        "platform KV secret has been provisioned + the L2 UAMI holds the 'Key Vault " +
                        "Secrets User' RBAC role on this vault."));

                case KvSecretReadResult.Failure kvf:
                    return SharedSecretResolution.Fail(new ExchangePolicyApplyOutcome.Failure(
                        $"Platform KV read failed for shared secret '{_options.SidecarSharedSecretName}' " +
                        $"on vault '{_options.SidecarSharedSecretVaultName}': {kvf.Diagnostic}"));

                default:
                    return SharedSecretResolution.Fail(new ExchangePolicyApplyOutcome.Failure(
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
            // Defense in depth: SecretClientKvReader.ReadSecretAsync catches
            // its own RequestFailedException + generic exceptions and maps to
            // KvSecretReadResult.Failure — the reader is contract-bound to
            // NOT throw for the "secret doesn't exist yet" case, only for
            // genuine infra faults. This catch handles ANY other unexpected
            // exception (ArgumentException from mis-configured empty strings
            // if the reader's own guards fired, e.g., though we pre-guard
            // above) as a LOUD-FAIL — never silently proceed with an empty
            // header.
            return SharedSecretResolution.Fail(new ExchangePolicyApplyOutcome.Failure(
                $"Unexpected exception resolving sidecar shared secret from platform KV: " +
                $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...[truncated]");

    // -------------------------------------------------------------------------
    // Wire DTOs — internal so the contract test can construct/assert them
    // directly without reflecting off the JSON.
    // -------------------------------------------------------------------------

    /// <summary>Wire request DTO for POST /apply-policy — Listener.ps1 line 19-29.</summary>
    internal sealed class SidecarApplyPolicyRequest
    {
        [JsonPropertyName("tenantId")]
        public string TenantId { get; init; } = default!;

        [JsonPropertyName("expectedAppIds")]
        public string[] ExpectedAppIds { get; init; } = default!;

        [JsonPropertyName("policyScopeGroupId")]
        public string PolicyScopeGroupId { get; init; } = default!;

        [JsonPropertyName("descriptionPrefix")]
        public string? DescriptionPrefix { get; init; }

        [JsonPropertyName("correlationId")]
        public string CorrelationId { get; init; } = default!;

        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; init; }
    }

    /// <summary>Wire response DTO for POST /apply-policy — Listener.ps1 line 31-38.</summary>
    internal sealed class SidecarApplyPolicyResponse
    {
        [JsonPropertyName("outcome")]
        public string? Outcome { get; init; }

        [JsonPropertyName("createdCount")]
        public int CreatedCount { get; init; }

        [JsonPropertyName("expectedAppIds")]
        public string[]? ExpectedAppIds { get; init; }

        [JsonPropertyName("observedAppIds")]
        public string[]? ObservedAppIds { get; init; }

        [JsonPropertyName("policiesApplied")]
        public string[]? PoliciesApplied { get; init; }

        [JsonPropertyName("diagnostic")]
        public string? Diagnostic { get; init; }
    }

    // -------------------------------------------------------------------------
    // Internal helper types
    // -------------------------------------------------------------------------

    /// <summary>Discriminated per-attempt result. Terminal outcomes propagate; RetryEligible triggers one retry.</summary>
    private abstract record AttemptResult
    {
        private AttemptResult() { }

        public sealed record Terminal(ExchangePolicyApplyOutcome Outcome) : AttemptResult
        {
            public static AttemptResult Of(ExchangePolicyApplyOutcome outcome) => new Terminal(outcome);
        }

        public sealed record RetryEligible(string Diagnostic) : AttemptResult
        {
            public static AttemptResult Of(string diagnostic) => new RetryEligible(diagnostic);
        }
    }

    /// <summary>Discriminated shared-secret resolution result.</summary>
    private readonly struct SharedSecretResolution
    {
        public string? Secret { get; }
        public ExchangePolicyApplyOutcome.Failure? Failure { get; }

        private SharedSecretResolution(string? secret, ExchangePolicyApplyOutcome.Failure? failure)
        {
            Secret = secret;
            Failure = failure;
        }

        public static SharedSecretResolution Succeed(string secret) => new(secret, null);
        public static SharedSecretResolution Fail(ExchangePolicyApplyOutcome.Failure failure) => new(null, failure);
    }
}
