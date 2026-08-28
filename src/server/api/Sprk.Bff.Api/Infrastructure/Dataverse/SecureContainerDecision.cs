namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 075 — THE decision: which SharePoint Embedded container does a record's
/// content belong in? Pure, dependency-free, and the only place in the C# codebase where this rule lives.
///
/// <para><b>Why the rule is worth isolating.</b> SharePoint Embedded permissions are <b>additive-only</b>:
/// <i>"SharePoint Embedded content inherits permissions from its container hierarchy. You can't break
/// inheritance on arbitrary files or folders."</i> A secure record's document placed in a shared container is
/// therefore readable by every member of that container and <b>no later per-item permission can retract
/// it</b>. There is no per-item alternative and no after-the-fact repair, which is why an absent secure
/// container is a refusal rather than a fallback.</para>
///
/// <para><b>The three outcomes, and the invariant between them.</b>
/// <see cref="ContainerDecisionOutcome.ResolvedSecure"/> uses the record's own container.
/// <see cref="ContainerDecisionOutcome.ResolvedFallback"/> uses the caller's non-secure default (the
/// business-unit cascade on the client per INV-7; <c>Communication:ArchiveContainerId</c> on the server).
/// <see cref="ContainerDecisionOutcome.Unresolved"/> means no container is available AND the record is not
/// secure — the benign config-absence case that callers may skip on.
/// <see cref="ContainerDecisionOutcome.FailClosed"/> means refuse.</para>
///
/// <para><b>The load-bearing invariant</b>: there is NO input for which a secure record yields
/// <see cref="ContainerDecisionOutcome.Unresolved"/>. A secure record either resolves to its own container or
/// fails closed. If that were not so, a secure record could reach the "log a warning and skip" path that
/// exists for unconfigured non-secure archives, and a skip is indistinguishable from success at the call
/// site. Pinned by test.</para>
///
/// <para><b>A non-secure record's own stamp is deliberately ignored.</b> Only the fallback is consulted for
/// a non-secure record. Reading a non-secure record's <c>sprk_containerid</c> would silently redirect content
/// for any record carrying a stale stamp — and stale stamps demonstrably exist, because the creation
/// wizard's business-unit cascade writes that column today (which is what task 076 removes). Non-secure
/// behaviour is unchanged by this task, on purpose.</para>
///
/// <para><b>Drift</b>: the equivalent TypeScript decision is <c>decideContainer</c> in
/// <c>src/client/shared/Spaarke.UI.Components/src/services/RecordContainerResolver.ts</c>. Both are pinned to
/// <c>tests/fixtures/secure-container-decision-table.json</c>; see
/// <c>projects/unified-access-control-r2/notes/task-075-record-aware-container-resolver.md</c> §4 for why two
/// halves exist and what the residual risk is.</para>
/// </summary>
public static class SecureContainerDecision
{
    /// <summary>
    /// Apply the rule. Pure: no I/O, no logging, no clock, no configuration.
    /// </summary>
    /// <param name="isSecure">
    /// Whether the record is secure (<c>sprk_issecure</c>). The CALLER is responsible for having actually
    /// determined this — passing <c>false</c> because the answer could not be obtained is the silent
    /// isolation failure this component exists to prevent, so
    /// <see cref="RecordContainerResolver"/> throws rather than defaulting when securability is unknown.
    /// </param>
    /// <param name="ownContainerId">The record's own <c>sprk_containerid</c>, if any.</param>
    /// <param name="fallbackContainerId">
    /// The caller's non-secure default — the business-unit cascade on the client, or
    /// <c>Communication:ArchiveContainerId</c> for server-side ingest. Consulted ONLY when the record is not
    /// secure.
    /// </param>
    public static ContainerDecision Decide(bool isSecure, string? ownContainerId, string? fallbackContainerId)
    {
        if (isSecure)
        {
            var own = Normalize(ownContainerId);

            // FAIL CLOSED, and note what is deliberately NOT reachable from here: the fallback. It may well
            // be non-empty and usable. Using it is the defect.
            return own is null
                ? new ContainerDecision(ContainerDecisionOutcome.FailClosed, null)
                : new ContainerDecision(ContainerDecisionOutcome.ResolvedSecure, own);
        }

        var fallback = Normalize(fallbackContainerId);

        return fallback is null
            ? new ContainerDecision(ContainerDecisionOutcome.Unresolved, null)
            : new ContainerDecision(ContainerDecisionOutcome.ResolvedFallback, fallback);
    }

    /// <summary>
    /// Blank is blank: null, empty and whitespace all mean "not set". Dataverse returns an empty string as
    /// readily as null for an unset <c>NVARCHAR</c>, and an unbound configuration option binds as empty
    /// rather than null — so a check for <c>null</c> alone would treat both as "set" and resolve to a blank
    /// container id, surfacing as a confusing Graph error instead of a refusal.
    /// Trimming is shared with the TypeScript half so the same record cannot resolve to two different ids.
    /// </summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Outcome of <see cref="SecureContainerDecision.Decide"/>.</summary>
public enum ContainerDecisionOutcome
{
    /// <summary>Use the secure record's own container.</summary>
    ResolvedSecure,

    /// <summary>Use the caller's non-secure fallback (business-unit cascade / archive container).</summary>
    ResolvedFallback,

    /// <summary>
    /// No container available and the record is NOT secure. Callers keep their existing skip/warn
    /// behaviour. Unreachable for a secure record by construction.
    /// </summary>
    Unresolved,

    /// <summary>Refuse. Never substitute a shared container for a secure record.</summary>
    FailClosed,
}

/// <summary>
/// The decision plus the container it resolved to. <see cref="ContainerId"/> is non-null exactly when
/// <see cref="Outcome"/> is <see cref="ContainerDecisionOutcome.ResolvedSecure"/> or
/// <see cref="ContainerDecisionOutcome.ResolvedFallback"/>.
/// </summary>
public sealed record ContainerDecision(ContainerDecisionOutcome Outcome, string? ContainerId);
