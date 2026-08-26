using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 075 — the ONE record-aware SharePoint Embedded container mapping, in both
/// directions. All of the logic that DECIDES anything lives in <see cref="SecureContainerDecision"/>; this
/// type is the data-fetching half plus the reverse lookup.
///
/// <para><b>Forward</b> (<see cref="ResolveForRecordAsync"/>): <i>which container does this record's content
/// belong in?</i> A secure record resolves to its own <c>sprk_containerid</c> or FAILS CLOSED; everything
/// else resolves to the caller's existing default (the business-unit cascade on the client per INV-7,
/// <c>Communication:ArchiveContainerId</c> for server-side ingest).</para>
///
/// <para><b>Reverse</b> (<see cref="ResolveOwningRecordAsync"/>): <i>which record owns this container?</i>
/// The authorization subject for the container-keyed routes (tasks 073 / 078). Both directions come from
/// this one component so there is exactly one mapping in the codebase.</para>
///
/// <para><b>Why this exists at all.</b> Provisioning creates a per-project container and stamps its id on the
/// project row (task 021), and until this landed <b>nothing read it</b>. Uploads resolved from the acting
/// user's business unit or one global archive container, so a secure project's documents went into a shared
/// container. SharePoint Embedded permissions are additive-only — inheritance cannot be broken on an
/// individual file — so no later per-item permission can retract that. The per-project container is the only
/// isolation mechanism available, and this is what makes the stamp mean something.</para>
///
/// <para><b>Fail-closed contract.</b> Any failure to DETERMINE securability (metadata unavailable, record
/// read failed, empty id, indeterminate ownership) throws rather than defaulting to "not secure". An unknown
/// answer read as not-secure is the same isolation failure with an extra step. Error codes:
/// <c>secure_record_container_missing</c> (409), <c>container_record_not_found</c> (404),
/// <c>container_ownership_ambiguous</c> (409), <c>container_ownership_indeterminate</c> (409).</para>
///
/// <para>Registered <b>Scoped</b> and <b>unconditionally</b> (Program.cs, beside
/// <see cref="IDocumentStorageResolver"/>). Unconditional registration is deliberate: a feature-gated
/// isolation seam would be absent exactly when the gate is off, and an absent resolver means callers fall
/// back to the shared container — so there is no ADR-032 Null-Object question to answer here, because there
/// is no acceptable null object.</para>
/// </summary>
public sealed class RecordContainerResolver
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

        // A read failure means securability is UNKNOWN, and unknown must never resolve to the shared
        // fallback. `IGenericEntityService.RetrieveAsync` returns a non-nullable Entity and the production
        // implementation THROWS a FaultException on not-found rather than returning null — so the null branch
        // below is defensive only, and the not-found case is normalized here so callers get the documented
        // 404 instead of a raw SDK fault surfacing as a 500 or an unwinnable Service Bus retry.
        Entity? record;
        try
        {
            record = await _entityService
                .RetrieveAsync(
                    normalizedEntity,
                    recordId,
                    [SecurableEntityRegistry.SecureFlagAttribute, ContainerColumn],
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            _logger.LogWarning(
                ex,
                "[SECURE-CONTAINER] {Entity} {RecordId} does not exist; refusing to resolve a container for it.",
                normalizedEntity, recordId);

            throw new SdapProblemException(
                code: "container_record_not_found",
                title: "Cannot resolve a storage container",
                detail: $"Record '{recordId}' of type '{normalizedEntity}' does not exist, so it cannot be "
                        + "determined whether it is secure. Refusing rather than using a shared container.",
                statusCode: 404);
        }

        if (record is null)
        {
            throw new SdapProblemException(
                code: "container_record_not_found",
                title: "Cannot resolve a storage container",
                detail: $"Record '{recordId}' of type '{normalizedEntity}' was not found, so it cannot be "
                        + "determined whether it is secure. Refusing rather than using a shared container.",
                statusCode: 404);
        }

        // ABSENT is not the same as FALSE, and the distinction is worth a log line even though it is not
        // (yet) an error. Dataverse omits null-valued properties from Web API responses, and FIELD-LEVEL
        // SECURITY on sprk_issecure returns the row with the attribute masked out rather than failing — both
        // yield "absent", and GetAttributeValue<bool> maps absent to false, i.e. the shared container. A
        // blanket throw would be wrong (a securable entity legitimately has NULL rows and that must not fail
        // every upload), so this is logged distinguishably and the live assertion that sprk_issecure is
        // neither field-secured nor NULL on any securable row belongs with task 047.
        if (!record.Contains(SecurableEntityRegistry.SecureFlagAttribute))
        {
            _logger.LogWarning(
                "[SECURE-CONTAINER] '{Attribute}' was ABSENT (not false) on {Entity} {RecordId}. Treating as "
                + "non-secure. Absent means either an unset column or FIELD-LEVEL SECURITY masking the value "
                + "for this caller — the latter would silently route content to the shared container.",
                SecurableEntityRegistry.SecureFlagAttribute, normalizedEntity, recordId);
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
            // TWO SEPARATE QUERIES, and the split is load-bearing rather than tidiness.
            //
            // A single container-filtered query with a TopCount could return the shared-container noise and
            // push the ONE secure claimant outside the page — and `TopCount` does not populate
            // `EntityCollection.MoreRecords` (only PageInfo does), so that truncation is UNDETECTABLE by
            // construction. The failure would be silent and fail-OPEN: zero secure claimants found, which
            // this method reports as "an ordinary shared container". The premise is not exotic — three live
            // projects already share the root business unit's container id, so many claimants is normal.
            //
            // Filtering on sprk_issecure == true first means the signal cannot be crowded out by the noise,
            // whatever the noise volume.
            var secureQuery = new QueryExpression(entityLogicalName)
            {
                // The container column is SELECTED, not just filtered on, so the exact match can be
                // re-confirmed in code — see the trim note below.
                ColumnSet = new ColumnSet(ContainerColumn),
                TopCount = ClaimantProbeLimit,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SecurableEntityRegistry.SecureFlagAttribute, ConditionOperator.Equal, true),
                        new ConditionExpression(ContainerColumn, ConditionOperator.NotNull)
                    }
                }
            };

            // Propagates on failure — an unanswerable ownership question must not read as "unowned", which a
            // caller would treat as "an ordinary shared container".
            var secureResults = await _entityService.RetrieveMultipleAsync(secureQuery, ct).ConfigureAwait(false);

            IEnumerable<Entity> secureRows = secureResults?.Entities ?? Enumerable.Empty<Entity>();
            var secureRowCount = 0;

            foreach (var row in secureRows)
            {
                secureRowCount++;

                if (row is null || row.Id == Guid.Empty)
                {
                    continue;
                }

                // THE MATCH IS MADE IN CODE, NOT IN THE FILTER, and this is the C-1 fix.
                //
                // The forward direction normalizes with Trim(), so a record stamped "  b!x  " stores its
                // content in b!x. A Dataverse `Equal` filter does NOT trim the stored value, so filtering on
                // the trimmed input would MISS that row — yielding zero secure claimants and the fail-open
                // "shared container" answer. Comparing trimmed-to-trimmed here makes the two directions agree
                // on exactly one definition of equality. (A `Like '%…%'` filter was rejected: SPE drive ids
                // routinely contain '_', which is a LIKE single-character wildcard, so it would over-match
                // and re-introduce the truncation risk this restructure just removed.)
                var storedContainer = row.GetAttributeValue<string>(ContainerColumn);

                if (!string.Equals(storedContainer?.Trim(), normalizedContainer, StringComparison.Ordinal))
                {
                    continue;
                }

                secureClaimants.Add(new OwningSecureRecord(entityLogicalName, row.Id));
            }

            // Truncation is now DETECTABLE and fail-closed. Reaching the cap means there are more secure
            // records than the probe can see, so "no further claimant exists" is no longer something this
            // method knows — and answering anyway is how a co-mingled container reads as unowned.
            if (secureRowCount >= ClaimantProbeLimit)
            {
                _logger.LogError(
                    "[SECURE-CONTAINER] Secure-record probe on '{Entity}' hit its bound of {Limit} rows while "
                    + "resolving ownership of container '{Container}'. Ownership cannot be established without "
                    + "possibly missing a claimant, so this is a refusal rather than an answer.",
                    entityLogicalName, ClaimantProbeLimit, normalizedContainer);

                throw new SdapProblemException(
                    code: "container_ownership_indeterminate",
                    title: "Container ownership could not be established",
                    detail: "There are more secure records than the ownership probe can inspect, so it cannot "
                            + "be determined whether this container is owned by one of them.",
                    statusCode: 409);
            }

            // The co-mingling probe. Only ever asked whether AT LEAST ONE non-secure record claims the same
            // container, so it is bounded at 1 row and cannot be crowded out either.
            var coMingleQuery = new QueryExpression(entityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(ContainerColumn, ConditionOperator.Equal, normalizedContainer),
                        new ConditionExpression(
                            SecurableEntityRegistry.SecureFlagAttribute, ConditionOperator.NotEqual, true)
                    }
                }
            };

            var coMingleResults = await _entityService.RetrieveMultipleAsync(coMingleQuery, ct).ConfigureAwait(false);

            nonSecureClaimantCount += coMingleResults?.Entities?.Count ?? 0;
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

    /// <summary>
    /// Whether an exception from a Dataverse retrieve means "the row does not exist" as opposed to a
    /// transient or authorization failure. Only the former may be normalized to a 404 — mapping a timeout to
    /// "not found" would turn a retryable condition into a permanent one.
    /// </summary>
    private static bool IsRecordNotFound(Exception ex)
        => ex.Message.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase);
}
