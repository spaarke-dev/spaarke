// -----------------------------------------------------------------------------
// H14aExchangePolicySubHandler.cs
//
// L2 CONTROL-PLANE H14a Exchange mailbox-access sub-handler (task 073; RBAC for
// Applications since task 251, owner D26). T4 silent-fail trap owner.
//
// PURPOSE:
//   The first of H14's two in-process sub-steps (H14a, then H14m — task 263; spec.md FR-19). Grants the customer
//   stamp's managed identity the Exchange "Application Mail.*" roles, scoped to
//   the customer's mail-enabled security group, so the stamp's Graph mail calls
//   reach that group's mailboxes and no others (H10 no longer grants the Entra
//   mailbox roles, which would reach every mailbox in the tenant). Action-and-
//   verify via IExchangePolicyApplier (T4): any existing assignment that differs
//   from the expected set is Drift -- nothing is created or changed (NO silent
//   overwrite). Only the managed identity is granted: the BFF app registration
//   does no app-only mail (its Mail.Send is delegated), and Exchange RBAC for
//   Applications GRANTS access, so adding it would widen what it can reach.
//
// PARENT-OWNS-COSMOS DESIGN (Path C — pivot to comply with ADR-004's "one
// message one handler one outcome" in spirit, documented per CLAUDE.md §6.5):
//   Unlike every other H-series handler, this sub-handler does NOT read or
//   write ProvisioningRun state itself. H14 (H14IntegrationWiringHandler)
//   dispatches H14a in-process (H14b/H14c were removed, ISS-019, so H14a is now
//   the only sub-step; the single-writer design is kept: the parent's one
//   ETag-checked write is simpler than the sub-handler racing it). The
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
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H14aExchangePolicySubHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H14a;

    /// <summary>Sub-step token used in the idempotency key format h14-{customerId}-{subStep}-{hash}.</summary>
    public const string SubStep = "exchange";

    private const int MaxAssignmentNameLength = 64;

    private readonly IExchangePolicyApplier _applier;
    private readonly IGraphAppRolesRegistry _roles;
    private readonly ILogger<H14aExchangePolicySubHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14aExchangePolicySubHandler(
        IExchangePolicyApplier applier,
        IGraphAppRolesRegistry roles,
        ILogger<H14aExchangePolicySubHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(logger);
        _applier = applier;
        _roles = roles;
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
            || string.IsNullOrWhiteSpace(parameters.UamiClientId)
            || string.IsNullOrWhiteSpace(parameters.UamiObjectId))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14aRejections.ApplyFailed,
                "H14a ParametersJson missing one of tenantId/uamiClientId/uamiObjectId.");
        }

        if (string.IsNullOrWhiteSpace(parameters.ScopeGroupId))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14aRejections.MissingPolicyScopeGroupId,
                "Run parameter 'exchangePolicyScopeGroupId' is required by H14a — the operator must supply the Entra object id " +
                "of the customer's mail-enabled security group (the mailboxes the stamp may reach).");
        }

        var assignments = BuildAssignments(parameters.NamePrefix, envelope.CustomerId, _roles.GetExchangeScoped());
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, parameters.UamiClientId, parameters.ScopeGroupId, assignments);

        var outcome = await _applier.ApplyAsync(
            new ExchangePolicyApplyRequest(
                parameters.TenantId,
                AppId: parameters.UamiClientId,
                ServicePrincipalObjectId: parameters.UamiObjectId,
                DisplayName: $"{parameters.NamePrefix}-{envelope.CustomerId}-stamp-identity",
                ScopeGroupId: parameters.ScopeGroupId,
                Assignments: assignments,
                // CorrelationId = RunId so the sidecar's log lines interleave with the Worker's.
                CorrelationId: envelope.RunId),
            cancellationToken).ConfigureAwait(false);

        switch (outcome)
        {
            case ExchangePolicyApplyOutcome.Applied applied:
                _logger.LogInformation(
                    "H14a Exchange mailbox access applied: customerId={CustomerId} createdCount={CreatedCount} assignments={Assignments}",
                    envelope.CustomerId, applied.CreatedCount, string.Join(",", applied.AssignmentNames));
                return new HandlerResult.Success(idempotencyKey);

            case ExchangePolicyApplyOutcome.Drift drift:
                return new HandlerResult.Failure(FailureClass.QuarantineRequired, H14aRejections.TrapT4Drift,
                    "T4 drift detected (spec.md FR-33): the stamp identity's Exchange role assignments differ from the expected " +
                    $"group-scoped set. {string.Join(" ", drift.Conflicts)} Nothing was created or changed — the operator must " +
                    "inspect the tenant's Exchange role assignments (Get-ManagementRoleAssignment) directly.");

            case ExchangePolicyApplyOutcome.Failure failure:
                return new HandlerResult.Failure(
                    FailureClass.Resumable, H14aRejections.ApplyFailed,
                    $"Exchange mailbox access apply failed: {failure.Diagnostic}");

            default:
                throw new InvalidOperationException($"Unhandled {nameof(ExchangePolicyApplyOutcome)} type '{outcome.GetType().Name}'.");
        }
    }

    /// <summary>
    /// One assignment per Exchange-scoped Graph role, named <c>{prefix}-{customerId}-{role}</c>
    /// (e.g. <c>Spaarke-acme-MailSend</c>). The names are H14a's idempotency key in Exchange.
    /// </summary>
    internal static IReadOnlyList<ExchangeRoleAssignmentSpec> BuildAssignments(
        string namePrefix, string customerId, IReadOnlyList<GraphAppRoleEntry> exchangeScopedRoles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return exchangeScopedRoles
            .Select(r => new ExchangeRoleAssignmentSpec(
                BuildAssignmentName(namePrefix, customerId, r.Value),
                IGraphAppRolesRegistry.ToExchangeApplicationRole(r.Value)))
            .ToArray();
    }

    private static string BuildAssignmentName(string namePrefix, string customerId, string graphValue)
    {
        var name = $"{namePrefix}-{customerId}-{graphValue.Replace(".", string.Empty, StringComparison.Ordinal)}";
        if (name.Length <= MaxAssignmentNameLength)
        {
            return name;
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();
        return $"{name[..(MaxAssignmentNameLength - 9)]}-{hash}";
    }

    /// <summary>
    /// The key a run of this sub-handler records on success — H14 (parent) checks CompletedPhases for it
    /// before dispatching. Uses this handler's role catalog, so parent and sub-handler cannot disagree.
    /// </summary>
    internal string ExpectedIdempotencyKey(string customerId, string uamiClientId, string scopeGroupId, string namePrefix)
        => BuildIdempotencyKey(customerId, uamiClientId, scopeGroupId, BuildAssignments(namePrefix, customerId, _roles.GetExchangeScoped()));

    /// <summary>
    /// <c>h14-{customerId}-exchange-{hash}</c>; hash = SHA-256 over the app, the scope group and the
    /// sorted assignment set — a different group or role set is different work.
    /// </summary>
    internal static string BuildIdempotencyKey(
        string customerId, string appId, string scopeGroupId, IReadOnlyList<ExchangeRoleAssignmentSpec> assignments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(assignments);
        var payload = string.Join("|",
            new[] { appId.Trim().ToLowerInvariant(), scopeGroupId.Trim().ToLowerInvariant() }
                .Concat(assignments.Select(a => $"{a.Name}:{a.Role}").OrderBy(x => x, StringComparer.Ordinal)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"h14-{customerId}-{SubStep}-{hash}";
    }

    /// <summary>
    /// Builds the opaque ParametersJson payload H14 (parent) embeds in the sub-envelope. Shared by the
    /// parent and the tests so there is one serialization contract.
    /// </summary>
    internal static string BuildParametersJson(
        string tenantId, string uamiClientId, string uamiObjectId, string scopeGroupId, string namePrefix)
        => JsonSerializer.Serialize(new Parameters(tenantId, uamiClientId, uamiObjectId, scopeGroupId, namePrefix));

    /// <summary>H14a's typed ParametersJson shape (public so System.Text.Json binds the primary constructor).</summary>
    public sealed record Parameters(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("uamiClientId")] string UamiClientId,
        [property: JsonPropertyName("uamiObjectId")] string UamiObjectId,
        [property: JsonPropertyName("scopeGroupId")] string ScopeGroupId,
        [property: JsonPropertyName("namePrefix")] string NamePrefix);
}
