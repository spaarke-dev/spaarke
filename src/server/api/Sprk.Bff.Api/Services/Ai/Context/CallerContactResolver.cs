// spaarke-ai-architecture-redesign-r2 task AIR2-055 (FR-B-06): Caller-contact self-assignment
// resolution — deterministic claims→Dataverse-contact mapping, server-side only, feeding the
// ContextEnvelope User slice's CallerContactId (ADR-039 "no runtime judgment for what should be
// data" — "assign it to me" stops being model guesswork).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — the caller's Entra oid is the binding key. Since unified-access-control-r2 task 141 the
//       ONE binding column is contact.sprk_externalobjectid (both planes); the shared read is
//       ContactBindingDecision.ContactsBoundToQuery + DecideBoundContact, also used by IdentityNormalizationService.
//   (2) Extension — this resolver runs that shared query for the caller's oid claim. It used to query
//       contact.azureactivedirectoryobjectid, a column that does not exist in dev, so every "assign it to me"
//       returned lookup-failed (task 141 background item 3).
//   (3) Cost-of-doing-nothing — without this resolver, "assign it to me" / "my tasks" has no
//       deterministic contact id to bind to; the ONLY alternative is letting the LLM guess a
//       contact from conversational context, which is exactly the unreliable behavior FR-B-06
//       exists to remove (R17/R18 backlog).
//
// Placement Justification (bff-extensions.md): lives in Services/Ai/Context/ (ADR-013 in-zone AI
// code) alongside the Context Binder (task 053 / ADR-043 Move 1) — its ONE consumer. It is
// latency-coupled to the per-turn bind (runs inside ContextBinder.BindAsync), so it belongs in the
// BFF, not a separate service.

using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Deterministic claims→Dataverse-contact resolver (FR-B-06). Server-side ONLY: no code path in
/// this type (or its ONE consumer, <see cref="ContextBinder"/>) accepts a model- or client-supplied
/// contact id for "me" — the contact id is always the output of THIS Dataverse cross-reference,
/// never a value read out of dispatch <c>Args</c> or an LLM completion.
/// </summary>
public interface ICallerContactResolver
{
    /// <summary>
    /// Resolves <paramref name="caller"/>'s Dataverse contact deterministically: the ONE contact bound to
    /// the caller's Entra oid (<c>contact.sprk_externalobjectid</c>, task 141), when it is active. Returns an
    /// explicit <see cref="CallerContactResolution.Unresolved"/> result — never a guessed/nearest contact — when
    /// the caller has no resolvable claims, no oid claim, no bound contact, an ambiguous binding (two contacts
    /// in any state), or an inactive bound contact.
    /// </summary>
    Task<CallerContactResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="ICallerContactResolver.ResolveAsync"/> — an honest resolved/unresolved
/// union (FR-B-06 "fail honestly"). <see cref="UnresolvedReason"/> is an identifier only (NFR-07
/// counts/identifiers-only posture), never free text derived from claim values.
/// </summary>
public sealed record CallerContactResolution
{
    /// <summary>True when <see cref="ContactId"/> was deterministically resolved.</summary>
    public required bool IsResolved { get; init; }

    /// <summary>The resolved Dataverse <c>contactid</c> (string GUID, "D" format). Null when unresolved.</summary>
    public string? ContactId { get; init; }

    /// <summary>
    /// Identifier-only reason for a non-resolution (e.g. <c>no-claims-principal</c>,
    /// <c>no-oid-claim</c>, <c>no-matching-contact</c>, <c>ambiguous-binding</c>, <c>inactive-contact</c>,
    /// <c>lookup-failed</c>).
    /// Null when resolved.
    /// </summary>
    public string? UnresolvedReason { get; init; }

    public static CallerContactResolution Resolved(string contactId) =>
        new() { IsResolved = true, ContactId = contactId };

    public static CallerContactResolution Unresolved(string reason) =>
        new() { IsResolved = false, UnresolvedReason = reason };
}

/// <summary>Default <see cref="ICallerContactResolver"/> — see file header for Component/Placement Justification.</summary>
public sealed class CallerContactResolver : ICallerContactResolver
{
    private readonly IDataverseService _dataverse;
    private readonly ILogger<CallerContactResolver> _logger;

    public CallerContactResolver(IDataverseService dataverse, ILogger<CallerContactResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CallerContactResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct)
    {
        if (caller is null)
        {
            return CallerContactResolution.Unresolved("no-claims-principal");
        }

        var oid = ExtractAadObjectId(caller);
        if (oid is null)
        {
            _logger.LogDebug(
                "CallerContactResolver: caller principal carries no usable oid claim — honest " +
                "no-contact result (FR-B-06 fail-honestly; never guesses).");
            return CallerContactResolution.Unresolved("no-oid-claim");
        }

        try
        {
            // The ONE binding read (shared with IdentityNormalizationService): every statecode, two rows, answered
            // by the binder's own oid step — so "assign it to me" never resolves a contact the binder would deny
            // (verifier finding 6: an active + an inactive contact on one oid is ambiguous, not "the active one").
            var results = await _dataverse
                .RetrieveMultipleAsync(ContactBindingDecision.ContactsBoundToQuery(oid.Value), ct)
                .ConfigureAwait(false);
            var decision = ContactBindingDecision.DecideBoundContact(
                ContactBindingDecision.BoundContactLookup(results.Entities));

            if (decision is null)
            {
                _logger.LogInformation(
                    "CallerContactResolver: no contact bound to caller oid={CallerOid} — " +
                    "honest no-contact result (FR-B-06 fail-honestly; never guesses a nearest/first contact).",
                    oid);
                return CallerContactResolution.Unresolved("no-matching-contact");
            }

            if (decision.Action == BindingAction.ResolveByOid && decision.ContactId is { } contactId)
            {
                return CallerContactResolution.Resolved(contactId.ToString("D"));
            }

            if (decision.DenyCode == ContactBindingDecision.DenyContactOidAmbiguous)
            {
                // Two contacts carrying one oid is an identity collision, not a choice to make here.
                _logger.LogWarning(
                    "CallerContactResolver: more than one contact is bound to caller oid={CallerOid} — honest " +
                    "unresolved result ({DenyCode}); never picks one.",
                    oid, decision.DenyCode);
                return CallerContactResolution.Unresolved("ambiguous-binding");
            }

            if (decision.DenyCode == ContactBindingDecision.DenyContactInactive)
            {
                // Deactivating a contact is how an operator removes a person (ADR-003): never "me".
                _logger.LogInformation(
                    "CallerContactResolver: the contact bound to caller oid={CallerOid} is inactive — honest " +
                    "unresolved result ({DenyCode}).",
                    oid, decision.DenyCode);
                return CallerContactResolution.Unresolved("inactive-contact");
            }

            _logger.LogWarning(
                "CallerContactResolver: caller oid={CallerOid} not resolved ({DenyCode}) — honest unresolved result.",
                oid, decision.DenyCode);
            return CallerContactResolution.Unresolved("lookup-failed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-safe, never fail-open-to-a-guess: a Dataverse error surfaces as an honest
            // unresolved result (the User slice's CallerContactId stays null), not a fallback guess.
            _logger.LogWarning(ex,
                "CallerContactResolver: Dataverse lookup failed for caller oid={CallerOid}; honest " +
                "no-contact result (FR-B-06 fail-honestly).",
                oid);
            return CallerContactResolution.Unresolved("lookup-failed");
        }
    }

    /// <summary>
    /// Extracts the Entra ID <c>oid</c> claim (AAD object id) from the supplied principal. Mirrors
    /// the extraction chain used elsewhere in the BFF for the same purpose (e.g.
    /// <c>MembershipEndpoints.ExtractAadObjectId</c>, <c>PrecedentAdminEndpoints</c>): the short-form
    /// <c>oid</c> claim, the long-form Microsoft schema URI, then <see cref="ClaimTypes.NameIdentifier"/>
    /// as a defensive fallback. Returns <c>null</c> when no usable identifier is present or the claim
    /// value does not parse as a non-empty <see cref="Guid"/>.
    /// </summary>
    internal static Guid? ExtractAadObjectId(ClaimsPrincipal user)
    {
        var oidString = CallerResolution.ResolveObjectId(user);

        if (string.IsNullOrWhiteSpace(oidString))
        {
            return null;
        }

        return Guid.TryParse(oidString, out var oid) && oid != Guid.Empty ? oid : null;
    }
}

/// <summary>
/// ADR-032 Null-Object default for <see cref="ICallerContactResolver"/> — used by
/// <see cref="ContextBinder"/> when no real resolver is DI-registered (or a caller/test constructs
/// <see cref="ContextBinder"/> directly without supplying one). Always returns an honest
/// <see cref="CallerContactResolution.Unresolved"/> result — never a guess — so the User slice's
/// <c>CallerContactId</c> degrades to null rather than the Binder throwing a null-reference failure.
/// </summary>
public sealed class NullCallerContactResolver : ICallerContactResolver
{
    /// <summary>Shared stateless instance (the resolver holds no per-call state).</summary>
    public static readonly NullCallerContactResolver Instance = new();

    public Task<CallerContactResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct) =>
        Task.FromResult(CallerContactResolution.Unresolved("resolver-not-registered"));
}
