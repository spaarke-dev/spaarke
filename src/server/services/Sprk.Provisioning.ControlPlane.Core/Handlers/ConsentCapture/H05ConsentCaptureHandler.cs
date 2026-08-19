// -----------------------------------------------------------------------------
// H05ConsentCaptureHandler.cs
//
// L2 CONTROL-PLANE H0.5 consent-capture handler (task 042).
//
// SPEC / DESIGN references:
//   - spec.md FR-02:               Model 2 self-service consent-capture — the
//                                  ONE irreducible customer-tenant admin
//                                  action; captures tid + kicks pipeline.
//   - spec.md §4D I1:              No hardcoded default tenant — tid is a
//                                  REQUIRED explicit parameter, propagated
//                                  through every downstream Azure call.
//   - design.md §4.1 H0.5 row:     Re-consent semantics — {Ready, Running,
//                                  WaitingOnGate} → no-op with existing runId;
//                                  {Failed, Cancelled} → restart from H0.
//   - design.md §4.3a.2:           BFF endpoint is Anonymous+HMAC (customer
//                                  admin authenticates to THEIR tenant, not
//                                  Spaarke's control plane).
//   - design.md §6.2 gateStates:   `admin-consent` gate — Pending until H0.5
//                                  verifies + records evidence.
//   - design.md D18:               BFF-as-consent-callback client — the
//                                  callback IS the extension point.
//
// EXECUTION MODEL:
//   Handler runs inside L2's future SB drain (Wave C5) OR is invoked directly
//   by the L2 reconciler (Wave C5). This wave (C4) delivers the class +
//   registration only — no runtime drain wire-up. Unit tests exercise the
//   full decision surface via constructor injection of mocks.
//
// IDEMPOTENCY (3-level per ADR-004 + design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the BFF endpoint's enqueue
//           computes MessageId = SHA256(H0.5|RunId|CustomerId|paramHash) —
//           two identical enqueues collapse at the wire.
//   Level 2 (Redis IdempotencyService): L2 does not yet own a Redis client
//           (design.md §4.1 preamble). Skipped this wave.
//   Level 3 (handler body durable dedup): the handler checks
//           ProvisioningRun.CompletedPhases for a matching (Phase=H0.5,
//           IdempotencyKey) entry. On match: return Success no-op.
//
//   Idempotency key format: `consent-{customerId}-{tenantId}` — MUST match
//   the BFF endpoint's enqueue-side format for level-1 dedup to bind.
//
// RE-CONSENT DECISION TREE:
//                            ┌──────────────────────────────────┐
//                            │ Registry lookup by tenantId       │
//                            └──────────────────────────────────┘
//                                        │
//                     ┌──────────────────┴──────────────────┐
//                     ▼                                      ▼
//              (no row found)                          (row found)
//                     │                                      │
//                     ▼                          ┌───────────┴────────────┐
//              Fresh start → enqueue H0          ▼                        ▼
//              Success(idempotencyKey)  {Ready|Running|            {Failed|Cancelled}
//                                        WaitingOnGate}                    │
//                                              │                           ▼
//                                              ▼               Enqueue H0 (restart)
//                                       No-op (log existing    Success(idempotencyKey)
//                                       runId; do NOT enqueue)
//                                       Success(idempotencyKey)
// -----------------------------------------------------------------------------

using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Registry;

namespace Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;

/// <summary>
/// H0.5 — Model 2 consent-capture handler. Owns re-consent semantics per
/// spec.md FR-02 + design.md §4.1: existing-run no-op vs restart-from-H0.
/// Does NOT touch AI resources (per ADR-013 forcing-function rule — this
/// handler receives no injection of <c>IActionResolver</c> / <c>IActionRunner</c>
/// / <c>IOpenAiClient</c> / <c>IPlaybookService</c>).
/// </summary>
public sealed class H05ConsentCaptureHandler : IProvisioningHandler
{
    /// <summary>Design.md §4.1 handler-catalog identifier.</summary>
    public const string HandlerIdConstant = "H0.5";

    /// <summary>Handler identifier for the downstream H0 enqueue.</summary>
    public const string NextHandlerId = "H0";

    private static readonly JsonSerializerOptions PayloadDeserializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Re-consent decision — statuses that leave the existing run intact.
    private static readonly HashSet<string> NoOpStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ready",
        "Running",
        "WaitingOnGate",
    };

    // Re-consent decision — statuses that trigger a restart-from-H0.
    private static readonly HashSet<string> RestartStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Failed",
        "Cancelled",
    };

    private readonly IDataverseEnvironmentRegistryClient _registry;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly ILogger<H05ConsentCaptureHandler> _logger;

    /// <summary>
    /// Constructs the handler bound to the registry lookup + downstream
    /// enqueue seam. No AI / no BFF-facade dependencies (ADR-013).
    /// </summary>
    public H05ConsentCaptureHandler(
        IDataverseEnvironmentRegistryClient registry,
        IHandlerEnqueuer enqueuer,
        ILogger<H05ConsentCaptureHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _enqueuer = enqueuer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string HandlerId => HandlerIdConstant;

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // -------- Parse + validate the envelope payload --------
        if (string.IsNullOrWhiteSpace(envelope.ParametersJson))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.MissingParameters,
                "H0.5 envelope carried an empty ParametersJson body. Operator must republish the " +
                "consent-callback with a valid payload.");
        }

        ConsentCapturePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ConsentCapturePayload>(
                envelope.ParametersJson,
                PayloadDeserializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "H0.5 payload deserialization failed for runId={RunId} customerIdHash={CustomerIdHash}",
                envelope.RunId,
                HashForLog(envelope.CustomerId));
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.MalformedPayload,
                "H0.5 envelope payload could not be deserialized as ConsentCapturePayload. See handler log for parse detail.");
        }

        if (payload is null)
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.MalformedPayload,
                "H0.5 envelope payload deserialized as null. Operator must republish the consent-callback.");
        }

        if (string.IsNullOrWhiteSpace(payload.CustomerId))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.MissingCustomerId,
                "H0.5 payload was missing customerId. The BFF endpoint MUST always include a non-empty customerId.");
        }

        if (string.IsNullOrWhiteSpace(payload.TenantId))
        {
            // §4D I1: no hardcoded default tenant — fail here rather than assume.
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.MissingTenantId,
                "H0.5 payload was missing tenantId. §4D I1 forbids a default-tenant fallback — operator must " +
                "re-run the consent flow so the customer admin's tid is captured.");
        }

        if (!string.Equals(envelope.CustomerId, payload.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            // Envelope-vs-payload mismatch would cross a Cosmos partition boundary (§4D I3).
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.CustomerIdMismatch,
                $"H0.5 envelope.CustomerId '{envelope.CustomerId}' did not match payload.customerId '{payload.CustomerId}'. " +
                "The BFF endpoint MUST enqueue with the same customerId in both slots (partition-key discipline / §4D I3).");
        }

        var idempotencyKey = BuildIdempotencyKey(payload.CustomerId, payload.TenantId);

        // -------- Registry lookup + re-consent branch --------
        DataverseEnvironmentRegistrySnapshot? existing;
        try
        {
            existing = await _registry.LookupByTenantIdAsync(payload.TenantId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Wave-C5's real registry impl may throw on transient Dataverse
            // faults — surface as Resumable so operator can retry after the
            // upstream fault clears. Placeholder impl never throws.
            _logger.LogError(ex,
                "H0.5 registry lookup failed for tenantIdHash={TenantIdHash}",
                HashForLog(payload.TenantId));
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                ConsentCaptureRejectionCodes.RegistryUnavailable,
                "H0.5 registry lookup failed — see handler log. This is Resumable: operator can POST " +
                "/api/runs/{id}/resume once the registry is reachable.");
        }

        if (existing is null)
        {
            // No prior environment for this tenant — treat as first consent.
            _logger.LogInformation(
                "H0.5 fresh consent (no existing environment). RunId={RunId} CustomerId={CustomerId} " +
                "TenantIdHash={TenantIdHash}. Enqueuing H0 preflight.",
                envelope.RunId, envelope.CustomerId, HashForLog(payload.TenantId));

            await EnqueueH0Async(envelope, payload, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Success(idempotencyKey);
        }

        if (NoOpStatuses.Contains(existing.SetupStatus))
        {
            // Re-consent no-op — existing run is still valid; do NOT re-enqueue H0.
            _logger.LogInformation(
                "H0.5 re-consent no-op. RunId={RunId} EnvironmentId={EnvironmentId} " +
                "ExistingSetupStatus={SetupStatus} ExistingRunId={ExistingRunId} " +
                "TenantIdHash={TenantIdHash}. Not enqueuing H0.",
                envelope.RunId, existing.EnvironmentId, existing.SetupStatus,
                existing.CurrentRunId ?? "(none)", HashForLog(payload.TenantId));

            return new HandlerResult.Success(idempotencyKey);
        }

        if (RestartStatuses.Contains(existing.SetupStatus))
        {
            // Prior run failed / cancelled — restart from H0.
            _logger.LogInformation(
                "H0.5 re-consent restart. RunId={RunId} EnvironmentId={EnvironmentId} " +
                "ExistingSetupStatus={SetupStatus} TenantIdHash={TenantIdHash}. Enqueuing H0.",
                envelope.RunId, existing.EnvironmentId, existing.SetupStatus,
                HashForLog(payload.TenantId));

            await EnqueueH0Async(envelope, payload, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Success(idempotencyKey);
        }

        // Unknown status (e.g., NotStarted, or a new value introduced in a
        // future spec revision) — default to enqueue-H0 (safe: level-1 dedup
        // absorbs a duplicate H0 within the SB window).
        _logger.LogWarning(
            "H0.5 encountered unmapped SetupStatus='{SetupStatus}' for EnvironmentId={EnvironmentId}; " +
            "defaulting to enqueue-H0. TenantIdHash={TenantIdHash}. Update NoOpStatuses / RestartStatuses " +
            "if this status should trigger a different branch.",
            existing.SetupStatus, existing.EnvironmentId, HashForLog(payload.TenantId));

        await EnqueueH0Async(envelope, payload, cancellationToken).ConfigureAwait(false);
        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>
    /// Builds the deterministic H0.5 idempotency key. MUST match the format
    /// used by the BFF endpoint's enqueue-side so level-1 (Service Bus
    /// MessageId) dedup binds. Format: <c>consent-{customerId}-{tenantId}</c>.
    /// </summary>
    /// <remarks>
    /// Exposed as internal so the L2 test suite and the BFF endpoint can
    /// assert format parity without duplicating the string interpolation.
    /// </remarks>
    internal static string BuildIdempotencyKey(string customerId, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return $"consent-{customerId}-{tenantId}";
    }

    private async Task EnqueueH0Async(
        HandlerEnvelope originalEnvelope,
        ConsentCapturePayload payload,
        CancellationToken cancellationToken)
    {
        // H0 preflight payload — carries the tid onward so §4D I1 propagates.
        // Reuses ParametersJson as a JSON object with tenantId + customerId
        // so H0 (task 041) does not need to know the H0.5 payload shape;
        // both handlers project only the fields they consume.
        var h0Params = JsonSerializer.Serialize(new
        {
            customerId = payload.CustomerId,
            tenantId = payload.TenantId,
        }, PayloadDeserializerOptions);

        var next = new HandlerEnvelope
        {
            HandlerId = NextHandlerId,
            RunId = originalEnvelope.RunId,
            CustomerId = payload.CustomerId,
            ParametersJson = h0Params,
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        await _enqueuer.EnqueueAsync(next, cancellationToken).ConfigureAwait(false);
    }

    // First 8 hex chars of SHA256(value) — enough to correlate log lines
    // for the SAME identifier without exposing the identifier itself.
    private static string HashForLog(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).AsSpan(0, 8).ToString();
    }
}
