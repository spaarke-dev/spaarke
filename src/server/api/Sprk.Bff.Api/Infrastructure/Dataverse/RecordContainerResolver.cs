using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// Dataverse-backed <see cref="IRecordContainerResolver"/>. All of the logic that decides anything lives in
/// <see cref="SecureContainerDecision"/>; this type is the data-fetching half plus the reverse lookup.
///
/// <para>Registered <b>Scoped</b> and <b>unconditionally</b> (Program.cs, beside
/// <see cref="IDocumentStorageResolver"/>). Unconditional registration is deliberate: a feature-gated
/// isolation seam would be absent exactly when the gate is off, and an absent resolver means callers fall
/// back to the shared container — so there is no ADR-032 Null-Object question to answer here, because there
/// is no acceptable null object.</para>
/// </summary>
public sealed class RecordContainerResolver : IRecordContainerResolver
{
    /// <summary>The stamped container column, on both the securable records and the business unit.</summary>
    private const string ContainerColumn = "sprk_containerid";

    /// <summary>
    /// How many claimants of one container id to fetch when answering the reverse question. Only needs to be
    /// enough to DISTINGUISH one from many; bounded so a shared business-unit container with thousands of
    /// rows cannot turn an authorization check into a table scan. Three live projects currently share the
    /// root business unit's container id, so "many" is the normal case, not the exotic one.
    /// </summary>
    private const int ClaimantProbeLimit = 25;

    private readonly ISecurableEntityRegistry _securableEntities;
    private readonly IGenericEntityService _entityService;
    private readonly ILogger<RecordContainerResolver> _logger;

    public RecordContainerResolver(
        ISecurableEntityRegistry securableEntities,
        IGenericEntityService entityService,
        ILogger<RecordContainerResolver> logger)
    {
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContainerDecision> ResolveForRecordAsync(
        string entityLogicalName,
        Guid recordId,
        string? nonSecureFallbackContainerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            throw new ArgumentException("Entity logical name is required.", nameof(entityLogicalName));
        }

        var normalizedEntity = entityLogicalName.Trim().ToLowerInvariant();

        // An entity that cannot carry sprk_issecure cannot be secure, so there is nothing to read and no
        // decision to make beyond the fallback. Note this also means a non-securable entity costs ZERO extra
        // Dataverse round trips, which is what keeps the seam cheap enough to put on every upload path.
        //
        // A metadata failure here PROPAGATES rather than being read as "not securable" — see
        // ISecurableEntityRegistry's fail-closed contract.
        if (!await _securableEntities.IsSecurableAsync(normalizedEntity, ct).ConfigureAwait(false))
        {
            return SecureContainerDecision.Decide(
                isSecure: false, ownContainerId: null, fallbackContainerId: nonSecureFallbackContainerId);
        }

        // Guid.Empty cannot identify a record. A securable entity with an unusable id is an
        // indeterminate-securability case, so it refuses rather than falling through to the fallback.
        if (recordId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "container_record_not_found",
                title: "Cannot resolve a storage container",
                detail: $"An empty record id was supplied for securable entity '{normalizedEntity}', so it "
                        + "cannot be determined whether the record is secure. Refusing rather than using a "
                        + "shared container.",
                statusCode: 404);
        }

        // Any exception from here propagates: a read failure means securability is UNKNOWN, and unknown must
        // never resolve to the shared fallback.
        var record = await _entityService
            .RetrieveAsync(normalizedEntity, recordId, [SecurableEntityRegistry.SecureFlagAttribute, ContainerColumn], ct)
            .ConfigureAwait(false);

        if (record is null)
        {
            throw new SdapProblemException(
                code: "container_record_not_found",
                title: "Cannot resolve a storage container",
                detail: $"Record '{recordId}' of type '{normalizedEntity}' was not found, so it cannot be "
                        + "determined whether it is secure. Refusing rather than using a shared container.",
                statusCode: 404);
        }

        var isSecure = record.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute);
        var ownContainerId = record.GetAttributeValue<string>(ContainerColumn);

        var decision = SecureContainerDecision.Decide(isSecure, ownContainerId, nonSecureFallbackContainerId);

        if (decision.Outcome == ContainerDecisionOutcome.FailClosed)
        {
            // Loud, per the task's primary constraint. The log deliberately records that a fallback WAS
            // available and was NOT used, because "the upload failed" is otherwise indistinguishable from a
            // configuration problem, and the operator needs to know the refusal was the correct outcome.
            _logger.LogError(
                "[SECURE-CONTAINER] REFUSED to resolve a storage container for secure {Entity} {RecordId}: "
                + "sprk_issecure is true but sprk_containerid is not set. A non-secure fallback was "
                + "{FallbackState} and was deliberately NOT used — SPE permissions are additive-only, so "
                + "content written to a shared container cannot be retracted. Provision the record's own "
                + "container (POST /api/external/projects/provision) before uploading to it.",
                normalizedEntity,
                recordId,
                string.IsNullOrWhiteSpace(nonSecureFallbackContainerId) ? "absent" : "AVAILABLE");

            throw new SdapProblemException(
                code: "secure_record_container_missing",
                title: "Secure record has no storage container",
                detail: $"{normalizedEntity} '{recordId}' is marked secure but has no container of its own. "
                        + "Its content cannot be stored in a shared container, so this operation is refused. "
                        + "Provision the record's container first.",
                statusCode: 409);
        }

        if (decision.Outcome == ContainerDecisionOutcome.ResolvedSecure)
        {
            _logger.LogInformation(
                "[SECURE-CONTAINER] Resolved secure {Entity} {RecordId} to its OWN container (not the "
                + "shared fallback).",
                normalizedEntity, recordId);
        }

        return decision;
    }

    public async Task<OwningSecureRecord?> ResolveOwningRecordAsync(
        string containerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return null;
        }

        var normalizedContainer = containerId.Trim();

        var securableEntities = await _securableEntities.GetSecurableEntitiesAsync(ct).ConfigureAwait(false);

        var secureClaimants = new List<OwningSecureRecord>();
        var nonSecureClaimantCount = 0;

        foreach (var entityLogicalName in securableEntities)
        {
            var query = new QueryExpression(entityLogicalName)
            {
                ColumnSet = new ColumnSet(SecurableEntityRegistry.SecureFlagAttribute),
                TopCount = ClaimantProbeLimit,
                NoLock = true,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(ContainerColumn, ConditionOperator.Equal, normalizedContainer)
                    }
                }
            };

            // Propagates on failure — an unanswerable ownership question must not read as "unowned", which a
            // caller would treat as "an ordinary shared container".
            var results = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

            IEnumerable<Entity> rows = results?.Entities ?? Enumerable.Empty<Entity>();

            foreach (var row in rows)
            {
                if (row is null || row.Id == Guid.Empty)
                {
                    continue;
                }

                if (row.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute))
                {
                    secureClaimants.Add(new OwningSecureRecord(entityLogicalName, row.Id));
                }
                else
                {
                    nonSecureClaimantCount++;
                }
            }
        }

        if (secureClaimants.Count == 0)
        {
            // No secure record claims this container, so it is a shared business-unit or archive container.
            // That is an ANSWER, not a failure: the caller decides what it means for them.
            return null;
        }

        if (secureClaimants.Count > 1 || nonSecureClaimantCount > 0)
        {
            _logger.LogError(
                "[SECURE-CONTAINER] AMBIGUOUS container ownership for container '{Container}': "
                + "{SecureCount} secure claimant(s) [{Claimants}] and {NonSecureCount} non-secure "
                + "claimant(s). A secure record's container must be its own — sharing it means content is "
                + "co-mingled, and because SPE permissions are additive-only that cannot be undone by any "
                + "later permission change. Refusing to name an owner rather than authorizing against the "
                + "wrong record.",
                normalizedContainer,
                secureClaimants.Count,
                string.Join(", ", secureClaimants.Select(c => $"{c.EntityLogicalName}:{c.RecordId}")),
                nonSecureClaimantCount);

            throw new SdapProblemException(
                code: "container_ownership_ambiguous",
                title: "Container ownership is ambiguous",
                detail: "More than one record claims this container, or a secure record shares it with a "
                        + "non-secure record. Authorizing against one of them would be a guess, so this "
                        + "operation is refused.",
                statusCode: 409);
        }

        return secureClaimants[0];
    }
}
