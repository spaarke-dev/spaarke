// teams-app-r1 Task 022 (2026-08-04) — Standing-grant flag reader (the task-051 seam).
//
// design.md §5 composes a CONTACT principal's accessible set as:
//     sprk_externalrecordaccess grants  ∪  (standing-grant runtime membership IFF the contact
//                                            holds a standing grant)
// This reader is the SEAM that gates the standing-grant term: it answers the single yes/no
// question "does this contact hold a standing grant?" by reading the field-security-secured
// boolean contact.sprk_standinggrant (schema contract: projects/teams-app-r1/notes/050-standing-
// grant-field-schema.md §1). Task 022 wires the term as `HasStandingGrantAsync(contactId) ?
// ResolveByContactAsync(...) : nothing`; task 051 finalizes/verifies the runtime UNION WITHOUT
// restructuring the composition — it only has to harden THIS reader (see the SEAM NOTES below).
//
// Broker-only (ADR-028 A2 NFR-02): reads Dataverse APP-ONLY via the already-registered
// IDataverseService (IGenericEntityService.RetrieveAsync). No caller-token exchange (no OBO),
// no Graph SDK types, no AI-internal types.

using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Reads a contact's subject-level <b>standing grant</b> policy flag
/// (<c>contact.sprk_standinggrant</c>, FR-12). This is the composition seam the accessible-record-set
/// service (task 022) uses to gate a contact's standing-grant runtime membership term; task 051
/// finalizes the runtime union by hardening this reader (FLS read grant / caching) without changing
/// the composition shape.
/// </summary>
/// <remarks>
/// Interface exists as an ADR-010 testing seam (mirrors <see cref="IWorkforcePrincipalResolver"/>):
/// the composition service is unit-tested against a substitute reader so the standing-grant gate is
/// exercised on both branches (held / not held) without a live Dataverse.
/// </remarks>
public interface IContactStandingGrantReader
{
    /// <summary>
    /// Returns <c>true</c> iff the contact holds a standing grant
    /// (<c>contact.sprk_standinggrant == true</c>). Fail-closed: any read fault (contact missing,
    /// field-level-security denial, transport error) returns <c>false</c> — a contact NEVER receives
    /// automatic (standing) membership on an ambiguous/failed read. Over-granting here would be an
    /// over-exposure defect; under-granting on a fault is the safe direction for a security gate and
    /// is the documented seam behavior task 051 hardens.
    /// </summary>
    /// <param name="contactId">Dataverse <c>contactid</c>. MUST NOT be <see cref="Guid.Empty"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> HasStandingGrantAsync(Guid contactId, CancellationToken ct);
}

/// <inheritdoc />
public sealed class ContactStandingGrantReader : IContactStandingGrantReader
{
    // Field contract — projects/teams-app-r1/notes/050-standing-grant-field-schema.md §1.
    internal const string ContactEntity = "contact";
    internal const string StandingGrantAttribute = "sprk_standinggrant";

    private readonly IDataverseService _dataverse;
    private readonly ILogger<ContactStandingGrantReader> _logger;

    public ContactStandingGrantReader(
        IDataverseService dataverse,
        ILogger<ContactStandingGrantReader> logger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(logger);
        _dataverse = dataverse;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasStandingGrantAsync(Guid contactId, CancellationToken ct)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("contactId must not be empty.", nameof(contactId));
        }

        try
        {
            // App-only read of the single secured boolean. GetAttributeValue<bool> returns false
            // when the attribute is absent (e.g., value not set, or FLS strips it from the payload)
            // — the fail-closed default is therefore structural, not just exception handling.
            var entity = await _dataverse
                .RetrieveAsync(ContactEntity, contactId, new[] { StandingGrantAttribute }, ct)
                .ConfigureAwait(false);

            // Task 051 hardening — make the FLS-denial case OBSERVABLE. A successful retrieve whose
            // payload does NOT contain the secured attribute is the exact signature of the BFF
            // Dataverse Application User lacking FLS read on contact.sprk_standinggrant (050 schema §2:
            // IsSecured=true). Without a grant (via the "Standing Grant Administrators" or System
            // Administrator field-security profile) the platform silently strips the column, so EVERY
            // contact would read as "no standing grant" — a whole-feature-dark misconfiguration that a
            // debug-level "flag = false" line would hide. Surface it at WARNING so the operator-gated
            // FLS grant (see report / 050 notes §5) is detectable in logs. This does NOT change the
            // fail-closed decision — an unreadable flag still means NO standing grant.
            if (entity is not null && !entity.Contains(StandingGrantAttribute))
            {
                _logger.LogWarning(
                    "[WF-STANDING] Contact {ContactId}: secured attribute '{Attribute}' absent from an " +
                    "otherwise-successful app-only retrieve. Most likely the BFF Dataverse Application " +
                    "User lacks field-level-security READ on contact.{Attribute} (grant via the " +
                    "'Standing Grant Administrators' FSP). Treating as NO standing grant (fail-closed).",
                    contactId, StandingGrantAttribute, StandingGrantAttribute);
            }

            var held = entity?.GetAttributeValue<bool>(StandingGrantAttribute) ?? false;

            _logger.LogDebug(
                "[WF-STANDING] Contact {ContactId} standing-grant flag = {Held}", contactId, held);

            return held;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SEAM NOTE (task 051): a fault here is treated as "no standing grant" (fail-closed).
            // The most likely non-error cause is field-level security: contact.sprk_standinggrant is
            // IsSecured=true (050 schema §2), so the BFF Dataverse Application User must hold read on
            // it (via the System Administrator FLS profile or the "Standing Grant Administrators"
            // profile) for a true value to be returned. Task 051 verifies/*grants* that FLS read and
            // MAY add caching; it does NOT need to change the composition (task 022) to do so.
            _logger.LogWarning(ex,
                "[WF-STANDING] Failed to read standing-grant flag for Contact {ContactId}; " +
                "treating as NO standing grant (fail-closed).", contactId);
            return false;
        }
    }
}
