using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// unified-access-control-r2 task 075, strategy 2 — the communication pipeline's entry into the record-aware
/// container decision (<see cref="RecordContainerResolver"/>).
///
/// <para><b>Why an adapter is needed.</b> The email/communication ingest path has a <c>sprk_communication</c> id and
/// a single global <c>Communication:ArchiveContainerId</c>, and <c>sprk_communication</c> does not carry
/// <c>sprk_issecure</c>. Its content belongs in a SECURE record's own container when the communication sits under
/// one (owner C10 part 2), and in the archive container otherwise.</para>
///
/// <para><b>This is the call site the build plan says is easiest to forget</b> — no client, no wizard, no UI.
/// If it is missed, a secure matter's inbound email attachments land in the shared archive container, and
/// because SPE permissions are additive-only that cannot be undone by any later permission change.</para>
///
/// <para><b>The SAME child resolution as the record path (task 155 f4).</b> Until f4 this adapter asked the
/// resolver about each SECURABLE typed regarding (project / matter / work assignment / invoice) one at a time, so
/// a communication linked to a secure matter only by its polymorphic regarding pair (161 of 276 live rows carry the
/// pair), or filed under a service request / event / analysis / budget / report card — none securable — went to the
/// shared archive container. Now the communication ITSELF is the record: <c>sprk_communication</c> has an entry in
/// <c>RecordContainerResolver.ChildAncestorLinks</c> (from a live sweep of its 24 lookups), so the pair, the held
/// path for intermediates and the transitive root walk are exactly the record path's. Every set regarding is
/// still considered and any secure root wins — that was always this adapter's rule ("storing it in the secure
/// matter's container is merely conservative") and it is the record path's rule too; two DIFFERENT secure roots
/// refuse as ambiguous.</para>
///
/// <para><b>What is preserved.</b> A communication whose row names no root by any route keeps the global archive
/// container (or "none — skip" when the archive is unconfigured; the business unit is never derived here — see
/// <c>RecordContainerResolver.ResolveForRecordWithFixedFallbackAsync</c>). That includes the shape the OUTBOUND sender
/// writes for an email regarding a person, organization or account — the typed party lookup plus the same id in
/// <c>sprk_regardingrecordid</c>, and no <c>sprk_regardingrecordtype</c> (<c>CommunicationService.MapAssociationFieldsAsync</c>
/// never writes it): the pair's id is the row's own typed party, so it names a party (task 155 f5). An invoice regarding
/// is still resolved LIVE through the invoice's links, as since task 155 r0 — but no longer through the invoice's own
/// flag or container: since task 150 an invoice follows its matter (owner round 10 item 11;
/// <c>SecurableEntityRegistry.FlagIsNotASecurityInput</c>). An empty
/// securable-entity set is still refused before anything is read.</para>
///
/// <para><b>What is NOT the archive any more (f4, by design).</b> A pair id naming a record whose type nothing on the row
/// states — the sender's UNMAPPED primaries (<c>sprk_todo</c>, <c>sprk_document</c>: no typed column, no type) — is
/// refused <c>container_ancestor_unresolved</c>: a to-do or a document can belong to a secure matter, so a root CAN be
/// involved and the archive would be a guess.</para>
/// </summary>
public sealed class CommunicationContainerResolver
{
    /// <summary>The communication entity — the record this adapter resolves.</summary>
    internal const string CommunicationEntity = "sprk_communication";

    private readonly RecordContainerResolver _containerResolver;
    private readonly ISecurableEntityRegistry _securableEntities;
    private readonly ILogger<CommunicationContainerResolver> _logger;

    public CommunicationContainerResolver(
        RecordContainerResolver containerResolver,
        ISecurableEntityRegistry securableEntities,
        ILogger<CommunicationContainerResolver> logger)
    {
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Decide which container a communication's content (attachments, <c>.eml</c>) belongs in.
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> id.</param>
    /// <param name="archiveContainerId">
    /// <c>Communication:ArchiveContainerId</c> — the existing global default, used only when no root can be involved:
    /// nothing the communication's row names leads to a secure project, matter or work assignment.
    /// </param>
    /// <returns>
    /// The container to write into, or <c>null</c> when none is available AND no root above the communication is
    /// secure — which preserves the existing "log a warning and skip" behaviour for an unconfigured archive container.
    /// </returns>
    /// <exception cref="Infrastructure.Exceptions.SdapProblemException">
    /// <para><c>secure_record_container_missing</c> — a root above the communication IS secure but has no container of
    /// its own. The caller MUST NOT write the bytes anywhere in response to this.</para>
    /// <para><c>container_ancestor_*</c> (task 155) — the record path's refusals: ambiguous (409 — two different secure
    /// roots anywhere above it, or the row's typed and polymorphic regarding disagree), unverifiable (409 — filed under
    /// a service request / event / analysis / budget / report card, or an invoice regarding an agreement), unresolved
    /// (409 — a missing record above it or an unclassifiable pair; 503 — a row could not be read). The inbound
    /// processor's permanent/transient split is <c>IncomingCommunicationProcessor.IsPermanentContainerRefusal</c>, and
    /// is unchanged.</para>
    /// <para><c>securable_entities_unknown</c> (409) — no entity carrying <c>sprk_issecure</c> is known.
    /// <c>container_record_not_found</c> (404) — the communication row does not exist.</para>
    /// </exception>
    public async Task<string?> ResolveContainerAsync(
        Guid communicationId,
        string? archiveContainerId,
        CancellationToken ct = default)
    {
        await EnsureSecurabilityKnownAsync(communicationId, ct).ConfigureAwait(false);

        var decision = await _containerResolver
            .ResolveForRecordWithFixedFallbackAsync(CommunicationEntity, communicationId, archiveContainerId, ct)
            .ConfigureAwait(false);

        if (decision.Outcome == ContainerDecisionOutcome.ResolvedSecure)
        {
            _logger.LogInformation(
                "[SECURE-CONTAINER] Communication {CommunicationId} routed to a SECURE record's own container "
                + "instead of the shared archive container.",
                communicationId);
        }

        // ResolvedFallback → the archive container; Unresolved → null (unconfigured archive: the caller skips).
        // FailClosed is never returned — the resolver throws secure_record_container_missing instead.
        return decision.ContainerId;
    }

    /// <summary>
    /// Refuse when no securable entity is known at all.
    /// </summary>
    /// <remarks>
    /// An empty set is legitimate in an org where <c>sprk_issecure</c> has never been added — and it is also exactly
    /// what a broken metadata query or an under-privileged identity looks like. On that answer the registry classifies
    /// every root as "cannot be secure", so the walk would find no secure root and the content would go to the shared
    /// archive container. This is the one place in the communication pipeline where the usual best-effort NFR-04
    /// degradation is wrong, so it refuses instead. <c>SecurableEntityRegistry</c> does NOT cache an empty answer, so
    /// this clears as soon as metadata answers properly.
    /// </remarks>
    private async Task EnsureSecurabilityKnownAsync(Guid communicationId, CancellationToken ct)
    {
        var securableEntities = await _securableEntities.GetSecurableEntitiesAsync(ct).ConfigureAwait(false);

        if (securableEntities.Count > 0)
        {
            return;
        }

        _logger.LogError(
            "[SECURE-CONTAINER] No securable entities are known, so it cannot be determined whether "
            + "communication {CommunicationId} sits under a secure record. Refusing rather than writing its "
            + "content to the shared archive container.",
            communicationId);

        throw new Infrastructure.Exceptions.SdapProblemException(
            code: "securable_entities_unknown",
            title: "Securability could not be determined",
            detail: "No entity carrying sprk_issecure is known, so it cannot be established whether this "
                    + "communication's content belongs in a secure container.",
            statusCode: 409);
    }
}
