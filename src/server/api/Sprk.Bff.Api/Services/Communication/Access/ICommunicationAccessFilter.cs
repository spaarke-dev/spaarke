using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// The BFF read-path filter for the two Spaarke message business rules Dataverse impersonation does NOT cover:
/// INTERNAL-ONLY and PRIVILEGE (FR-08 / NFR-06). It is applied ON TOP of an ALREADY-IMPERSONATED message set —
/// the rows Dataverse returned to the calling user (the endpoint issues the record-read query with the
/// <c>MSCRMCallerID</c> impersonation header = <see cref="CommunicationAccessContext.CallerSystemUserId"/>, so
/// record-level access — ownership, role depth, BU, teams, sharing, hierarchy — is enforced NATIVELY by
/// Dataverse in one query, per the owner decision 2026-07-16, <c>notes/access-model-decision.md</c>).
///
/// <para>Reworked from task 042 (superseded): the hand-computed <c>MembershipResolver(anchor) ∪ overlay grants</c>
/// union + the point-forward privacy switch are RETIRED for reads — they mis-modeled effective access for an
/// app-only BFF (missed role depth / BU / hierarchy) and rebuilt platform security (design §5). Record access is
/// now Dataverse's job via impersonation; this filter shrinks to two per-message rules:</para>
///
/// <list type="number">
///   <item><b>Internal-only</b> — an <c>sprk_isinternalonly</c> message is hidden from a non-internal caller (D-05).
///     Default-deny: if the flag cannot be evaluated, a non-internal caller is DENIED (fail closed, NFR-06).</item>
///   <item><b>Privilege</b> — classification metadata composed onto the decision; it NEVER gates the read on its own
///     and the filter NEVER calls AI at read time (ADR-015 — the AI may FLAG upstream; a human owns the field;
///     enforcement only reads it). Owner decision 2026-07-16: privilege never gates a read.</item>
/// </list>
///
/// The filter makes NO I/O and takes NO Dataverse / membership / grant / AI dependency — "no AI at read time" and
/// "no re-computed access at read time" are both STRUCTURAL guarantees. Discrete authorization gates ("can user X
/// open / post to thread Y") are NOT this filter's job — use Dataverse <c>RetrievePrincipalAccess</c> (Web API,
/// app-only; the documented mechanism for one-principal-one-record effective-rights checks — see task 050 / 043).
/// </summary>
public interface ICommunicationAccessFilter
{
    /// <summary>
    /// Decides a single IMPERSONATED <c>sprk_communication</c> row against the caller's internal-only + privilege
    /// rules. Pure, synchronous, no I/O, no AI. The unit of the security tests.
    /// </summary>
    /// <param name="message">
    /// A <c>sprk_communication</c> row Dataverse already returned to the impersonated caller, carrying at least
    /// <c>sprk_isinternalonly</c> and <c>sprk_privilegeclassification</c>.
    /// </param>
    CommunicationAccessDecision EvaluateMessage(CommunicationAccessContext caller, Entity message);

    /// <summary>
    /// The endpoint entry point (task 050): filters an already-impersonated thread message set to the caller's
    /// visible subset (applies internal-only; carries privilege as metadata) and returns every per-row decision
    /// (reused for the internal-only-filtered unread count). Input rows are assumed Dataverse-access-scoped already.
    /// </summary>
    CommunicationAccessResult FilterMessages(
        CommunicationAccessContext caller,
        IReadOnlyCollection<Entity> messages);
}
