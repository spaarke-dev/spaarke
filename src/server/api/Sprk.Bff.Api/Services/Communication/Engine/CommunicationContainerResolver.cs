using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// unified-access-control-r2 task 075, strategy 2 — the adapter between the communication family's
/// polymorphic regarding (ADR-024, <see cref="RegardingFieldMap"/>) and the record-aware container decision
/// (<see cref="RecordContainerResolver"/>).
///
/// <para><b>Why an adapter is needed.</b> <see cref="RecordContainerResolver"/> answers about a record you
/// can name. The email/communication ingest path cannot name one: it has a <c>sprk_communication</c> id and a
/// single global <c>Communication:ArchiveContainerId</c>, and <c>sprk_communication</c> does not carry
/// <c>sprk_issecure</c>. Something has to decide WHICH record the decision is about, and that is
/// regarding-resolution logic which belongs beside <see cref="RegardingFieldMap"/> rather than inside a
/// generic Infrastructure seam.</para>
///
/// <para><b>This is the call site the build plan says is easiest to forget</b> — no client, no wizard, no UI.
/// If it is missed, a secure matter's inbound email attachments land in the shared archive container, and
/// because SPE permissions are additive-only that cannot be undone by any later permission change.</para>
///
/// <para><b>ANY securable regarding, not just the primary.</b> Deliberately different from
/// <see cref="RegardingParentEntityMapper"/>, which takes only the highest-priority regarding because
/// grounding a document to a non-primary parent would misfile it into the wrong RAG scope. The container
/// question has the opposite risk profile: if an email regards a secure matter AND an invoice, sending its
/// attachment to the invoice's shared container is a disclosure, whereas storing it in the secure matter's
/// container is merely conservative. So every set regarding is considered, and any secure one wins.</para>
/// </summary>
public sealed class CommunicationContainerResolver
{
    private readonly RecordContainerResolver _containerResolver;
    private readonly IGenericEntityService _entityService;
    private readonly ISecurableEntityRegistry _securableEntities;
    private readonly ILogger<CommunicationContainerResolver> _logger;

    public CommunicationContainerResolver(
        RecordContainerResolver containerResolver,
        IGenericEntityService entityService,
        ISecurableEntityRegistry securableEntities,
        ILogger<CommunicationContainerResolver> logger)
    {
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Decide which container a communication's content (attachments, <c>.eml</c>) belongs in.
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> id.</param>
    /// <param name="archiveContainerId">
    /// <c>Communication:ArchiveContainerId</c> — the existing global default, used only when no regarding is
    /// a secure record.
    /// </param>
    /// <returns>
    /// The container to write into, or <c>null</c> when none is available AND no regarding is secure — which
    /// preserves the existing "log a warning and skip" behaviour for an unconfigured archive container.
    /// </returns>
    /// <exception cref="Infrastructure.Exceptions.SdapProblemException">
    /// <para><c>secure_record_container_missing</c> — a regarding IS secure but has no container of its own.
    /// The caller MUST NOT write the bytes anywhere in response to this.</para>
    /// <para><c>communication_secure_container_ambiguous</c> — the communication regards two different secure
    /// records with different containers, so there is no single correct destination.</para>
    /// </exception>
    public async Task<string?> ResolveContainerAsync(
        Guid communicationId,
        string? archiveContainerId,
        CancellationToken ct = default)
    {
        var securableRegardings = await FindSecurableRegardingsAsync(communicationId, ct).ConfigureAwait(false);

        // No regarding on a securable entity → no secure record can be involved, so the existing global
        // archive container is the correct answer and behaviour is unchanged.
        if (securableRegardings.Count == 0)
        {
            return SecureContainerDecision
                .Decide(isSecure: false, ownContainerId: null, fallbackContainerId: archiveContainerId)
                .ContainerId;
        }

        var secureContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityLogicalName, recordId) in securableRegardings)
        {
            // Fallback deliberately null: this loop asks ONLY "is this regarding secure, and if so where does
            // its content live?". The global archive default is applied once, afterwards, and only if nothing
            // secure was found. Passing the archive container in here would let a non-secure regarding
            // "resolve" and mask a secure sibling.
            var decision = await _containerResolver
                .ResolveForRecordAsync(entityLogicalName, recordId, nonSecureFallbackContainerId: null, ct)
                .ConfigureAwait(false);

            if (decision.Outcome == ContainerDecisionOutcome.ResolvedSecure && decision.ContainerId is not null)
            {
                secureContainers.Add(decision.ContainerId);
            }
        }

        if (secureContainers.Count == 0)
        {
            // Securable regardings exist but none is actually marked secure — ordinary content.
            return SecureContainerDecision
                .Decide(isSecure: false, ownContainerId: null, fallbackContainerId: archiveContainerId)
                .ContainerId;
        }

        if (secureContainers.Count > 1)
        {
            _logger.LogError(
                "[SECURE-CONTAINER] Communication {CommunicationId} regards {Count} DIFFERENT secure records "
                + "with different containers. There is no single correct destination for its content, and "
                + "choosing one would place it where the other's members can read it. Refusing.",
                communicationId, secureContainers.Count);

            throw new Infrastructure.Exceptions.SdapProblemException(
                code: "communication_secure_container_ambiguous",
                title: "Ambiguous secure destination",
                detail: "This communication regards more than one secure record, each with its own storage "
                        + "container, so its content has no single correct destination.",
                statusCode: 409);
        }

        var target = secureContainers.Single();

        _logger.LogInformation(
            "[SECURE-CONTAINER] Communication {CommunicationId} routed to a SECURE record's own container "
            + "instead of the shared archive container.",
            communicationId);

        return target;
    }

    /// <summary>
    /// Every regarding set on the communication whose target entity can carry <c>sprk_issecure</c>.
    /// </summary>
    /// <remarks>
    /// Failures PROPAGATE. This is the one place in the communication pipeline where the usual best-effort
    /// NFR-04 degradation is wrong: degrading to "no securable regarding" would send a secure record's
    /// attachment to the shared archive container, which is exactly the irreversible outcome being prevented.
    /// Availability is traded for isolation here, deliberately.
    /// </remarks>
    private async Task<List<(string EntityLogicalName, Guid RecordId)>> FindSecurableRegardingsAsync(
        Guid communicationId,
        CancellationToken ct)
    {
        var securableEntities = await _securableEntities.GetSecurableEntitiesAsync(ct).ConfigureAwait(false);

        var found = new List<(string, Guid)>();

        if (securableEntities.Count == 0)
        {
            return found;
        }

        var communication = await _entityService
            .RetrieveAsync(
                "sprk_communication",
                communicationId,
                RegardingParentEntityMapper.RegardingColumns,
                ct)
            .ConfigureAwait(false);

        if (communication is null)
        {
            return found;
        }

        foreach (var (entityLogicalName, regardingField) in RegardingFieldMap.All)
        {
            if (!securableEntities.Contains(entityLogicalName.ToLowerInvariant()))
            {
                continue;
            }

            var reference = communication.GetAttributeValue<EntityReference>(regardingField);
            if (reference is null || reference.Id == Guid.Empty)
            {
                continue;
            }

            found.Add((entityLogicalName, reference.Id));
        }

        return found;
    }
}
