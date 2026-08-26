// -----------------------------------------------------------------------------
// H14aExchangePolicySubHandler.cs
//
// L2 CONTROL-PLANE H14a Exchange ApplicationAccessPolicy sub-handler (task
// 073, wave C4 Batch 3F). T4 silent-fail trap owner.
//
// PURPOSE:
//   One of H14's 3 DAG-parallel sub-steps (spec.md FR-19). Applies +
//   verifies the 2 Exchange Online ApplicationAccessPolicy entries (BFF
//   app-reg + UAMI) via IExchangePolicyApplier's action-and-verify semantics
//   (T4): 0/1 present -> create missing; 2+ present -> verify AppIds match
//   the expected set exactly, else fail with a drift diagnostic (NO silent
//   overwrite).
//
// PARENT-OWNS-COSMOS DESIGN (Path C — pivot to comply with ADR-004's "one
// message one handler one outcome" in spirit, documented per CLAUDE.md §6.5):
//   Unlike every other H-series handler, this sub-handler does NOT read or
//   write ProvisioningRun state itself. H14 (H14IntegrationWiringHandler)
//   dispatches H14a/H14b/H14c via Task.WhenAll — 3 TRUE-parallel in-process
//   invocations against the SAME Cosmos document. If each sub independently
//   did its own read-modify-write with ETag optimistic concurrency (the
//   pattern every other handler uses), the 3 concurrent writers would race
//   on the SAME ETag and at least 2 of 3 would observe a Conflict on every
//   single invocation — not a rare race, a GUARANTEED one. Instead: the
//   PARENT reads the run ONCE, builds each sub's typed parameters as an
//   opaque ParametersJson payload (parity with the HandlerEnvelope's own
//   "opaque JSON, handler owns the schema" contract), invokes the 3 subs in
//   parallel as PURE external-system executors (this class touches ZERO
//   Cosmos state), collects their HandlerResult outcomes, and performs ONE
//   ReplaceRunAsync call aggregating all 3 CompletedPhase entries. This
//   sub-handler still implements IProvisioningHandler (uniformity +
//   independent unit-testability with a hand-built HandlerEnvelope) and is
//   registered in L2 DI per the POML acceptance criterion, but its
//   IdempotencyKey is a DETERMINISTIC PURE FUNCTION of its inputs (see
//   <see cref="BuildIdempotencyKey"/>) rather than a Cosmos-backed check —
//   the "have we already done this" DECISION is the parent's, made once,
//   before dispatch (skip re-invoking any sub whose expected key already
//   appears in ProvisioningRun.CompletedPhases).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-19 (H14
//     acceptance) + FR-33 T4 (silent-fail trap).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H14 row
//     + §4B T4 trap catalog.
//   - .claude/adr/ADR-004: IJobHandler-shape contract; idempotency key format
//     h14-{customerId}-{subStep}-{hash} per POML constraint.
//   - .claude/adr/ADR-036: 3-level idempotency stack (level 3 is the parent's
//     CompletedPhases scan, described above).
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Enqueue;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H14aExchangePolicySubHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H14a;

    /// <summary>Sub-step token used in the idempotency key format h14-{customerId}-{subStep}-{hash}.</summary>
    public const string SubStep = "exchange";

    private readonly IExchangePolicyApplier _applier;
    private readonly ILogger<H14aExchangePolicySubHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14aExchangePolicySubHandler(
        IExchangePolicyApplier applier,
        ILogger<H14aExchangePolicySubHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(logger);
        _applier = applier;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H14aExchangePolicySubHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        Parameters parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Parameters>(envelope.ParametersJson)
                ?? throw new JsonException("Deserialized to null.");
        }
        catch (JsonException ex)
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14aRejections.ApplyFailed,
                $"H14a ParametersJson deserialization failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(parameters.TenantId)
            || string.IsNullOrWhiteSpace(parameters.BffAppRegId)
            || string.IsNullOrWhiteSpace(parameters.UamiClientId))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14aRejections.ApplyFailed,
                "H14a ParametersJson missing one of tenantId/bffAppRegId/uamiClientId.");
        }

        if (string.IsNullOrWhiteSpace(parameters.PolicyScopeGroupId))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14aRejections.MissingPolicyScopeGroupId,
                "Run parameter 'exchangePolicyScopeGroupId' is required by H14a (New-ApplicationAccessPolicy " +
                "-PolicyScopeGroupId) — operator must supply the mail-enabled security group id for this tenant.");
        }

        var expectedAppIds = new[] { parameters.BffAppRegId, parameters.UamiClientId };
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, expectedAppIds);

        // Task 161 (Wave G-6): CorrelationId = envelope.RunId — placed on the
        // sidecar's `correlationId` wire field so its stdout log lines
        // interleave with this Worker's own correlationId=RunId logs in the
        // shared Log Analytics workspace (Listener.ps1 .OBSERVABILITY note).
        var outcome = await _applier.ApplyAsync(
            new ExchangePolicyApplyRequest(
                parameters.TenantId, expectedAppIds, parameters.PolicyScopeGroupId, parameters.DescriptionPrefix,
                CorrelationId: envelope.RunId),
            cancellationToken).ConfigureAwait(false);

        switch (outcome)
        {
            case ExchangePolicyApplyOutcome.Applied applied:
                _logger.LogInformation(
                    "H14a Exchange policy applied: customerId={CustomerId} createdCount={CreatedCount}",
                    envelope.CustomerId, applied.CreatedCount);
                return new HandlerResult.Success(idempotencyKey);

            case ExchangePolicyApplyOutcome.Drift drift:
                var diagnostic =
                    $"T4 drift detected (spec.md FR-33): Get-ApplicationAccessPolicy returned entries for the " +
                    $"expected AppId set but observed AppIds do not match. Expected: [{string.Join(", ", drift.ExpectedAppIds)}]. " +
                    $"Observed: [{string.Join(", ", drift.ObservedAppIds)}]. No policy was created or modified — " +
                    "operator must inspect the target Exchange Online tenant directly.";
                return new HandlerResult.Failure(FailureClass.QuarantineRequired, H14aRejections.TrapT4Drift, diagnostic);

            case ExchangePolicyApplyOutcome.Failure failure:
                return new HandlerResult.Failure(
                    FailureClass.Resumable, H14aRejections.ApplyFailed,
                    $"Exchange policy apply failed: {failure.Diagnostic}");

            default:
                throw new InvalidOperationException($"Unhandled {nameof(ExchangePolicyApplyOutcome)} type '{outcome.GetType().Name}'.");
        }
    }

    /// <summary>
    /// Computes the deterministic H14a idempotency key:
    /// <c>h14-{customerId}-exchange-{hash}</c> where hash is SHA-256 over the
    /// sorted expected AppId set. Exposed internal so the parent handler +
    /// unit tests can construct the expected key without duplicating the
    /// format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, IReadOnlyList<string> expectedAppIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(expectedAppIds);
        var payload = string.Join("|", expectedAppIds.OrderBy(a => a, StringComparer.Ordinal));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"h14-{customerId}-{SubStep}-{hash}";
    }

    /// <summary>
    /// Builds the opaque ParametersJson payload H14 (parent) embeds in the
    /// sub-envelope it hands to this sub-handler. Exposed internal so the
    /// parent + tests share one serialization contract.
    /// </summary>
    internal static string BuildParametersJson(
        string tenantId, string bffAppRegId, string uamiClientId, string policyScopeGroupId, string descriptionPrefix)
        => JsonSerializer.Serialize(new Parameters(tenantId, bffAppRegId, uamiClientId, policyScopeGroupId, descriptionPrefix));

    /// <summary>
    /// H14a's typed ParametersJson shape. Public (not private) so
    /// <see cref="System.Text.Json.JsonSerializer"/>'s reflection-based
    /// converter resolves the primary constructor without relying on
    /// non-public-type reflection support.
    /// </summary>
    public sealed record Parameters(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("bffAppRegId")] string BffAppRegId,
        [property: JsonPropertyName("uamiClientId")] string UamiClientId,
        [property: JsonPropertyName("policyScopeGroupId")] string PolicyScopeGroupId,
        [property: JsonPropertyName("descriptionPrefix")] string DescriptionPrefix);
}
