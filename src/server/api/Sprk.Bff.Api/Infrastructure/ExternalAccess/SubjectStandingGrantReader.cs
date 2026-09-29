// teams-app-r1 Task 022 (2026-08-04) — Standing-grant flag reader (the task-051 seam).
// unified-access-control-r2 Task 042 (2026-09-10) — made LEVEL-BEARING and extended to organizations.
//
// design.md §5 composes a CONTACT principal's accessible set as:
//     sprk_externalrecordaccess grants  ∪  (standing-grant runtime membership IFF the contact
//                                            holds a standing grant)
// This reader is the SEAM that gates the standing-grant term. Task 022 answered one yes/no question
// ("does this contact hold a standing grant?"); FR-25 makes the term LEVEL-BEARING, so the reader now
// answers "does the subject hold one, and at what baseline level?" in the SAME single read (NFR-02).
//
// ⚠️ RENAMED from IContactStandingGrantReader / ContactStandingGrantReader by task 042. It reads
// ORGANIZATIONS as well as contacts now, so "Contact" in the name would have been false — the exact
// docs-vs-reality drift this project keeps repairing. The POML sanctions the supersede
// ("a superseding ISubjectStandingGrantReader").
//
// Broker-only (ADR-028 A2 NFR-02): reads Dataverse APP-ONLY via the already-registered
// IDataverseService (IGenericEntityService.RetrieveAsync). No caller-token exchange (no OBO),
// no Graph SDK types, no AI-internal types.

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// A subject's standing-grant policy state: whether they hold one, and the baseline access level it
/// confers (FR-25).
/// </summary>
/// <param name="Held">
/// <c>true</c> iff the subject's <c>sprk_standinggrant</c> flag is set. Fail-closed on any ambiguity.
/// </param>
/// <param name="Baseline">
/// The subject's <c>sprk_accesspermissiongrant</c> level, or <c>null</c> when the field is not set.
/// <para>🔴 <b><c>null</c> means the standing term contributes NOTHING</b> — see
/// <see cref="StandingGrantState.Rights"/>. That is an owner decision (2026-09-10), not an
/// implementation default; the alternatives were a View Only floor and a Collaborate floor, and both
/// were rejected because a level nobody chose must not confer access. Recorded in
/// <c>notes/task-042-standing-grant-levels.md</c> §4.</para>
/// </param>
public readonly record struct StandingGrantState(bool Held, ExternalAccessLevel? Baseline)
{
    /// <summary>The fail-closed value: no standing grant, no baseline.</summary>
    public static StandingGrantState NotHeld => new(false, null);

    /// <summary>
    /// What this standing grant contributes to the evaluator — <see cref="AccessRights.None"/> unless
    /// the subject BOTH holds the flag AND carries a recognised baseline.
    /// </summary>
    /// <remarks>
    /// <para>Routed through <see cref="ExternalAccessLevels.ToAccessRights"/> — task 032's SINGLE
    /// mapping, never a second copy. That mapping already fails closed on <c>null</c> and on any value
    /// outside the enum, so the owner's "empty baseline contributes nothing" decision needed no
    /// special-case code: it is what the existing mapping already does. The work was to NOT
    /// special-case it.</para>
    ///
    /// <para>⚠️ Callers MUST still skip records when this is <see cref="AccessRights.None"/> rather
    /// than accumulate them. <c>AccumulateTerm</c> would otherwise enter the record id with zero
    /// rights — present in the accessible set while conferring nothing, which is a different and
    /// worse answer than absent.</para>
    /// </remarks>
    public AccessRights Rights => Held ? ExternalAccessLevels.ToAccessRights(Baseline) : AccessRights.None;
}

/// <summary>
/// Reads a subject's standing-grant policy flag and baseline level — <c>sprk_standinggrant</c> +
/// <c>sprk_accesspermissiongrant</c> — for a <b>contact</b> or an <b>organization</b> (FR-12, FR-25).
/// </summary>
/// <remarks>
/// <para>Interface exists as an ADR-010 testing seam (mirrors <see cref="IWorkforcePrincipalResolver"/>):
/// the composition service is unit-tested against a substitute reader so the standing-grant gate is
/// exercised on every branch (not held / held with each baseline / held with none) without a live
/// Dataverse.</para>
///
/// <para><b>Both entities carry the SAME two logical names.</b> Design §10 said the organization field
/// was <c>sprk_accesspermissions</c>; live metadata says <c>sprk_accesspermissiongrant</c>, identical
/// to the contact field (verified 2026-09-10 — notes/task-042-standing-grant-levels.md §2). One
/// attribute constant therefore serves both, and the two methods differ only in entity + id.</para>
/// </remarks>
public interface ISubjectStandingGrantReader
{
    /// <summary>
    /// Reads a CONTACT's standing-grant state. Fail-closed: any read fault (contact missing,
    /// field-level-security denial, transport error) returns <see cref="StandingGrantState.NotHeld"/>
    /// — a contact NEVER receives automatic (standing) membership on an ambiguous or failed read.
    /// Over-granting here would be an over-exposure defect; under-granting on a fault is the safe
    /// direction for a security gate.
    /// </summary>
    /// <param name="contactId">Dataverse <c>contactid</c>. MUST NOT be <see cref="Guid.Empty"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StandingGrantState> ReadForContactAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Reads an ORGANIZATION's standing-grant state, with the identical fail-closed contract.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Built and unit-tested by task 042; NOT consumed by the evaluator yet.</b> The org
    /// EXPANSION term (organization standing grant → its members' accessible records) is task 043's,
    /// and wiring it here would ship an untested access path. Task 042's own acceptance criteria
    /// require that composition output contains no org-derived entries.
    /// </remarks>
    /// <param name="organizationId">Dataverse <c>sprk_organizationid</c>. MUST NOT be <see cref="Guid.Empty"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StandingGrantState> ReadForOrganizationAsync(Guid organizationId, CancellationToken ct);
}

/// <inheritdoc />
public sealed class SubjectStandingGrantReader : ISubjectStandingGrantReader
{
    // Field contract — projects/teams-app-r1/notes/050-standing-grant-field-schema.md §1, extended by
    // task 042's live verification (notes/task-042-standing-grant-levels.md §1).
    internal const string ContactEntity = "contact";
    internal const string OrganizationEntity = "sprk_organization";
    internal const string StandingGrantAttribute = "sprk_standinggrant";
    internal const string BaselineAttribute = "sprk_accesspermissiongrant";

    private readonly IDataverseService _dataverse;
    private readonly ILogger<SubjectStandingGrantReader> _logger;

    public SubjectStandingGrantReader(
        IDataverseService dataverse,
        ILogger<SubjectStandingGrantReader> logger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(logger);
        _dataverse = dataverse;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<StandingGrantState> ReadForContactAsync(Guid contactId, CancellationToken ct)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("contactId must not be empty.", nameof(contactId));
        }

        return ReadAsync(ContactEntity, contactId, isFlsSecured: true, ct);
    }

    /// <inheritdoc />
    public Task<StandingGrantState> ReadForOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("organizationId must not be empty.", nameof(organizationId));
        }

        // isFlsSecured: false — sprk_organization.sprk_standinggrant is IsSecured=FALSE in live
        // metadata, unlike contact.sprk_standinggrant (IsSecured=TRUE). Verified 2026-09-10. That
        // asymmetry is a real finding filed separately; it changes the LOG WORDING here (an absent
        // org attribute means "not set", not "FLS stripped it") and nothing about the fail direction.
        return ReadAsync(OrganizationEntity, organizationId, isFlsSecured: false, ct);
    }

    private async Task<StandingGrantState> ReadAsync(
        string entityName,
        Guid subjectId,
        bool isFlsSecured,
        CancellationToken ct)
    {
        try
        {
            // ONE read for BOTH attributes (NFR-02: exactly one round trip per subject).
            var entity = await _dataverse
                .RetrieveAsync(entityName, subjectId, new[] { StandingGrantAttribute, BaselineAttribute }, ct)
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
            if (isFlsSecured && entity is not null && !entity.Contains(StandingGrantAttribute))
            {
                _logger.LogWarning(
                    "[WF-STANDING] {Entity} {SubjectId}: secured attribute '{Attribute}' absent from an " +
                    "otherwise-successful app-only retrieve. Most likely the BFF Dataverse Application " +
                    "User lacks field-level-security READ on {Entity}.{Attribute} (grant via the " +
                    "'Standing Grant Administrators' FSP). Treating as NO standing grant (fail-closed).",
                    entityName, subjectId, StandingGrantAttribute, entityName, StandingGrantAttribute);
            }

            var held = entity?.GetAttributeValue<bool>(StandingGrantAttribute) ?? false;

            if (!held)
            {
                _logger.LogDebug(
                    "[WF-STANDING] {Entity} {SubjectId} standing-grant flag = false", entityName, subjectId);
                return StandingGrantState.NotHeld;
            }

            var baseline = ReadBaseline(entity);

            if (baseline is null)
            {
                // 🔴 OWNER DECISION 2026-09-10 (option B): a standing grant with NO baseline contributes
                // NOTHING. This is deliberately loud rather than debug — it is a MISCONFIGURATION, and
                // the whole point of choosing B over a floor was that a level nobody chose must not
                // confer access. The subject looks configured (flag set) and is receiving nothing, which
                // is exactly the state an operator needs told about instead of discovering by report.
                // Fix is to populate sprk_accesspermissiongrant; spec Prerequisites already make that an
                // environment obligation.
                _logger.LogWarning(
                    "[WF-STANDING] {Entity} {SubjectId} holds a standing grant but '{Baseline}' is NOT SET, " +
                    "so the standing term contributes NO access. Populate {Entity}.{Baseline} (View Only / " +
                    "Collaborate / Full Access).",
                    entityName, subjectId, BaselineAttribute, entityName, BaselineAttribute);

                return new StandingGrantState(Held: true, Baseline: null);
            }

            _logger.LogDebug(
                "[WF-STANDING] {Entity} {SubjectId} standing-grant flag = true, baseline = {Baseline}",
                entityName, subjectId, baseline);

            return new StandingGrantState(Held: true, Baseline: baseline);
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
            // profile) for a true value to be returned.
            _logger.LogWarning(ex,
                "[WF-STANDING] Failed to read the standing-grant state for {Entity} {SubjectId}; " +
                "treating as NO standing grant (fail-closed).", entityName, subjectId);
            return StandingGrantState.NotHeld;
        }
    }

    /// <summary>
    /// Reads the baseline picklist, returning <c>null</c> when it is unset OR carries a value outside
    /// the three FR-25 levels.
    /// </summary>
    /// <remarks>
    /// An unrecognised integer is treated as unset rather than passed through, so it cannot reach
    /// <see cref="ExternalAccessLevels.ToAccessRights"/> as an out-of-range enum value. That mapping
    /// would already fail it closed to <see cref="AccessRights.None"/> — this is belt-and-braces at the
    /// boundary where the untyped platform value enters, and it keeps the WARNING above accurate for
    /// both causes.
    /// </remarks>
    internal static ExternalAccessLevel? ReadBaseline(Entity? entity)
    {
        var raw = entity?.GetAttributeValue<OptionSetValue>(BaselineAttribute)?.Value;

        return raw switch
        {
            (int)ExternalAccessLevel.ViewOnly => ExternalAccessLevel.ViewOnly,
            (int)ExternalAccessLevel.Collaborate => ExternalAccessLevel.Collaborate,
            (int)ExternalAccessLevel.FullAccess => ExternalAccessLevel.FullAccess,
            _ => null
        };
    }
}
