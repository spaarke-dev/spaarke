// -----------------------------------------------------------------------------
// IExchangePolicyReadClient.cs
//
// Task 180 (Wave G-7 -- H13 T4 acceptance-gate probe, sidecar read-route).
// Seam abstraction over the sidecar's NEW GET /policies read-only route
// (Listener.ps1 extension, this task) -- pure enumeration of the customer
// tenant's Exchange ApplicationAccessPolicy list. Used by the T4 probe
// (ExchangePolicyCountT4Probe) to re-verify H14a's post-condition
// INDEPENDENTLY of H14a's own action-and-verify result (per H13's R7
// "assert EFFECTS not intentions" principle).
//
// SEAM JUSTIFICATION (ADR-010):
//   >=2 implementations exist from day 1:
//     - Production (this task): <see cref="ExchangePolicySidecarReadClient"/>
//       -- HttpClient GETting to the sitecontainer-private sidecar (task 114's
//       Listener.ps1 on http://127.0.0.1:8091/policies) with the SAME
//       per-boot X-Sidecar-Auth shared-secret header
//       ExchangePolicySidecarClient (task 161) already uses for the write
//       route. One sidecar, one auth model, one KV secret; two disjoint
//       routes serving disjoint semantics (write vs read).
//     - Test: per-unit-test fakes returning canned outcomes.
//
// WHY A SIBLING SEAM RATHER THAN EXTENDING IExchangePolicyApplier:
//   IExchangePolicyApplier is action-and-verify (may create policies); this
//   is pure read (never mutates). Overloading one interface with two verbs
//   whose contracts differ on the fundamental "does this mutate observable
//   state?" axis would defeat the whole point of the standalone probe (a
//   probe that mutates what it verifies invalidates R7). Separate seams keep
//   the T4 probe provably non-mutating at the compile-time type level, not
//   just at the runtime hope-nobody-passes-the-wrong-flag level.
//
// PLACEMENT / COMPONENT JUSTIFICATION (CLAUDE.md s11):
//   Existing -- the sidecar HTTP transport, shared-secret KV read, typed
//     HttpClient DI, and X-Sidecar-Auth header contract all already exist
//     (task 161's ExchangePolicySidecarClient). This task adds a second READ
//     route to the same sidecar + a second thin client behind a NEW
//     interface, per POML: "a thin extension of ExchangePolicySidecarClient
//     (or a sibling read-client)".
//   Extension -- the sibling-client choice keeps the applier's action-and-
//     verify contract narrowly-typed and satisfies the "probe must not
//     mutate" invariant at the type level.
//   Cost-of-doing-nothing -- H13's T4 branch returns InfraFault
//     UNCONDITIONALLY (Resumable) without this seam + probe -- the ONE H13
//     probe that cannot be pure .NET per DS-4 s6, so there is no SDK-only
//     fallback path. H13's acceptance gate cannot go green for T4 without
//     this route + client + probe.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Read-only enumeration of a customer tenant's Exchange
/// <c>ApplicationAccessPolicy</c> entries via the sidecar's
/// <c>GET /policies</c> route (Listener.ps1, task 114 + read-route
/// extension task 180). Never mutates state; used by the H13 T4 probe.
/// </summary>
public interface IExchangePolicyReadClient
{
    /// <summary>
    /// Enumerates every ApplicationAccessPolicy visible to the sidecar's
    /// app-only Exchange Online connection under the customer tenant.
    /// Returns a discriminated outcome; MUST NOT throw for transport or
    /// remote-error conditions (returns <see cref="ExchangePolicyReadOutcome.Failure"/>
    /// so the caller can classify Resumable).
    /// </summary>
    Task<ExchangePolicyReadOutcome> ReadAsync(
        ExchangePolicyReadRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single sidecar read invocation. Immutable record.
/// </summary>
/// <param name="TenantId">
/// Customer Entra tenant id (sent to the sidecar so its EXO connection is
/// scoped to the right tenant; also logged with the sidecar's structured
/// stdout line for run-tracing). MUST NOT be empty per s4D I1.
/// </param>
/// <param name="CorrelationId">
/// ProvisioningRun id (H13 handler's <c>envelope.RunId</c>) -- forwarded on
/// the wire as <c>correlationId</c> so sidecar stdout log lines interleave
/// with Worker logs by RunId in Log Analytics (parity with task 161's
/// ApplyAsync CorrelationId contract).
/// </param>
public sealed record ExchangePolicyReadRequest(
    string TenantId,
    string CorrelationId);

/// <summary>
/// Single ApplicationAccessPolicy row projected from
/// <c>Get-ApplicationAccessPolicy</c>. Matches Listener.ps1's per-policy
/// projection field-for-field.
/// </summary>
public sealed record ExchangePolicyEntry(
    string AppId,
    string Description,
    string PolicyScopeGroupId);

/// <summary>
/// Discriminated outcome of <see cref="IExchangePolicyReadClient.ReadAsync"/>.
/// </summary>
public abstract record ExchangePolicyReadOutcome
{
    private ExchangePolicyReadOutcome() { }

    /// <summary>
    /// Sidecar read succeeded; <paramref name="ObservedAppIds"/> is the
    /// distinct AppId set across every enumerated policy;
    /// <paramref name="Policies"/> is the full per-row projection.
    /// </summary>
    public sealed record Success(
        IReadOnlyList<string> ObservedAppIds,
        IReadOnlyList<ExchangePolicyEntry> Policies) : ExchangePolicyReadOutcome;

    /// <summary>
    /// Sidecar read could not run to a conclusive answer (transport, auth,
    /// KV, cert-fetch, EXO connect, or Get-ApplicationAccessPolicy failure).
    /// Caller classifies as Resumable per H13 s4C.
    /// </summary>
    public sealed record Failure(string Diagnostic) : ExchangePolicyReadOutcome;
}
