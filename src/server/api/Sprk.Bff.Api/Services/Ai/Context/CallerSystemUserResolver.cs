// spaarke-ai-architecture-redesign-r2 F-2 (D3, envelope-convergence task): Caller-systemuser
// resolution — deterministic claims→Dataverse-systemuser mapping, server-side only, keying the
// ContextEnvelope User slice's user-memory RECALL fragment (operator ruling 2026-07-09 (c):
// canonical User-scope key = Dataverse systemuserid).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — oid→systemuserid IS resolved elsewhere (MembershipEndpoints.ResolveSystemUserIdAsync)
//       but that helper is PRIVATE to its endpoint file and not reusable as an injectable service. The
//       sibling ICallerContactResolver (task 055) resolves oid→CONTACT, a DIFFERENT key — user memory is
//       keyed by systemuserid, not contactid, so it cannot be reused.
//   (2) Extension — the AAD oid claim is the key on BOTH sides: systemuser.azureactivedirectoryobjectid
//       (directory-synced) here, and contact.sprk_externalobjectid (the oid binding, task 141) for
//       ICallerContactResolver. This resolver mirrors ICallerContactResolver's one-hop QueryExpression
//       pattern (same IDataverseService facade, same oid-claim extraction, same fail-honestly posture) —
//       additive, no second Dataverse client.
//       Issue #1576: the Dataverse query itself is now DELEGATED to the shared, cached
//       ISystemUserIdentityResolver.ResolveSystemUserIdAsync, which excludes disabled systemusers. The private
//       query this class carried had no isdisabled filter, so a disabled caller still resolved.
//   (3) Cost-of-doing-nothing — without a systemuserid for the caller, F-2 user-scope memory has NO recall
//       key: an AI-captured user preference (memory.write scope=user) is persisted but never read back into
//       any prompt (the exact capture-only gap this task closes).
//
// Placement Justification (bff-extensions.md): lives in Services/Ai/Context/ (ADR-013 in-zone AI code)
// alongside the Context Binder — its ONE consumer. Latency-coupled to the per-turn bind
// (runs inside ContextBinder.BindAsync), so it belongs in the BFF, not a separate service.

using System.Security.Claims;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Deterministic claims→Dataverse-systemuser resolver (F-2 user-memory recall). Server-side ONLY: the
/// systemuserid is always the output of this Dataverse cross-reference (AAD oid claim →
/// <c>systemuser.azureactivedirectoryobjectid</c>, ADR-028), never a value from client Args or an LLM
/// completion. Consumed by <see cref="ContextBinder"/> to key the User slice's user-memory fragment.
/// </summary>
/// <remarks>
/// <b>Known limit (issue #1576): a newly disabled user still resolves for up to 10 minutes.</b> The lookup is delegated
/// to <see cref="ISystemUserIdentityResolver.ResolveSystemUserIdAsync"/>, which excludes disabled systemusers but caches
/// each oid → systemuserid mapping for 10 minutes. A user disabled at time T therefore still resolves until about
/// T + 10 min on every caller of this resolver — including the authorization checks in
/// <c>CommunicationRecordAuthorizationFilter</c> (caller resolution) and <c>DecisionRouteCores</c>, not only memory
/// recall. The same limit is stated on <see cref="ISystemUserIdentityResolver"/>.
/// </remarks>
public interface ICallerSystemUserResolver
{
    /// <summary>
    /// Resolves <paramref name="caller"/>'s Dataverse <c>systemuserid</c> deterministically via the AAD
    /// oid claim → <c>systemuser.azureactivedirectoryobjectid</c> cross-reference (ADR-028). Returns an
    /// explicit <see cref="CallerSystemUserResolution.Unresolved"/> result — never a guess — when the
    /// caller has no resolvable claims, no oid claim, or no matching ACTIVE systemuser row (a disabled
    /// systemuser does not resolve, issue #1576).
    /// </summary>
    Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="ICallerSystemUserResolver.ResolveAsync"/> — an honest resolved/unresolved union
/// (fail-honestly). <see cref="UnresolvedReason"/> is an identifier only (NFR-07), never free text.
/// </summary>
public sealed record CallerSystemUserResolution
{
    /// <summary>True when <see cref="SystemUserId"/> was deterministically resolved.</summary>
    public required bool IsResolved { get; init; }

    /// <summary>The resolved Dataverse <c>systemuserid</c> (string GUID, "D" format). Null when unresolved.</summary>
    public string? SystemUserId { get; init; }

    /// <summary>Identifier-only reason for a non-resolution (e.g. <c>no-claims-principal</c>, <c>no-oid-claim</c>, <c>no-matching-systemuser</c>, <c>lookup-failed</c>). Null when resolved.</summary>
    public string? UnresolvedReason { get; init; }

    public static CallerSystemUserResolution Resolved(string systemUserId) =>
        new() { IsResolved = true, SystemUserId = systemUserId };

    public static CallerSystemUserResolution Unresolved(string reason) =>
        new() { IsResolved = false, UnresolvedReason = reason };
}

/// <summary>Default <see cref="ICallerSystemUserResolver"/> — see file header for Component/Placement Justification.</summary>
public sealed class CallerSystemUserResolver : ICallerSystemUserResolver
{
    private readonly ISystemUserIdentityResolver _identity;
    private readonly ILogger<CallerSystemUserResolver> _logger;

    /// <param name="identity">
    /// The shared, cached oid ⇄ systemuserid resolver (singleton, registered unconditionally by
    /// <c>NotificationsModule</c>). Issue #1576: the Dataverse query is delegated to it so disabled systemusers are
    /// excluded in ONE place; this class keeps only the claims extraction and the honest-outcome mapping.
    /// </param>
    public CallerSystemUserResolver(ISystemUserIdentityResolver identity, ILogger<CallerSystemUserResolver> logger)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct)
    {
        if (caller is null)
        {
            return CallerSystemUserResolution.Unresolved("no-claims-principal");
        }

        // Reuse the SAME oid-claim extraction chain as the sibling contact resolver (ADR-028).
        var oid = CallerContactResolver.ExtractAadObjectId(caller);
        if (oid is null)
        {
            _logger.LogDebug(
                "CallerSystemUserResolver: caller principal carries no usable oid claim — honest " +
                "no-systemuser result (fail-honestly; never guesses).");
            return CallerSystemUserResolution.Unresolved("no-oid-claim");
        }

        try
        {
            var systemUserId = await _identity
                .ResolveSystemUserIdAsync(oid.Value.ToString("D"), ct)
                .ConfigureAwait(false);
            if (systemUserId is null || systemUserId.Value == Guid.Empty)
            {
                _logger.LogInformation(
                    "CallerSystemUserResolver: no active systemuser cross-reference for caller oid={CallerOid} — " +
                    "honest no-systemuser result (fail-honestly; never guesses).",
                    oid);
                return CallerSystemUserResolution.Unresolved("no-matching-systemuser");
            }

            return CallerSystemUserResolution.Resolved(systemUserId.Value.ToString("D"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-safe, never fail-open-to-a-guess: a Dataverse error surfaces as an honest unresolved
            // result (the User slice's user-memory fragment stays absent), not a fallback guess.
            _logger.LogWarning(ex,
                "CallerSystemUserResolver: Dataverse lookup failed for caller oid={CallerOid}; honest " +
                "no-systemuser result (fail-honestly).",
                oid);
            return CallerSystemUserResolution.Unresolved("lookup-failed");
        }
    }
}

/// <summary>
/// ADR-032 Null-Object default for <see cref="ICallerSystemUserResolver"/> — used by
/// <see cref="ContextBinder"/> when no real resolver is DI-registered (or a caller/test constructs
/// <see cref="ContextBinder"/> directly without supplying one). Always returns an honest
/// <see cref="CallerSystemUserResolution.Unresolved"/> result so the User slice's user-memory fragment
/// degrades to absent rather than the Binder throwing.
/// </summary>
public sealed class NullCallerSystemUserResolver : ICallerSystemUserResolver
{
    /// <summary>Shared stateless instance (the resolver holds no per-call state).</summary>
    public static readonly NullCallerSystemUserResolver Instance = new();

    public Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct) =>
        Task.FromResult(CallerSystemUserResolution.Unresolved("resolver-not-registered"));
}
