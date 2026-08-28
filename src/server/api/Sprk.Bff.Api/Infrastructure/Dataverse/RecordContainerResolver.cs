using System.ServiceModel;
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
    /// The record's owning business unit. Dataverse populates this system column on every user- or
    /// team-owned row, so it needs no extra round trip — it comes back with the record read that
    /// <see cref="ResolveForRecordAsync"/> already performs.
    /// </summary>
    private const string OwningBusinessUnitColumn = "owningbusinessunit";

    /// <summary>The business unit entity, whose <c>sprk_containerid</c> is the non-secure default.</summary>
    private const string BusinessUnitEntity = "businessunit";

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

    /// <summary>
    /// Resolve the container for a record with NO caller-supplied fallback — the server derives the
    /// non-secure default from the record's OWN owning business unit.
    ///
    /// <para>This is the overload the upload path uses (task 076). It is what makes "the client stops
    /// deciding" literally true: the authorization key and the container both derive from
    /// <c>(entityLogicalName, recordId)</c>, so no code path lets them disagree.</para>
    ///
    /// <para><b>Why the RECORD's business unit and not the ACTING USER's.</b> Every client upload site
    /// resolves <c>getUserId() → systemuser.businessunitid → businessunit.sprk_containerid</c> — the
    /// person uploading, not the thing being uploaded to. Two users uploading to the same matter put
    /// its documents in two different containers. Worse for isolation specifically: per
    /// <c>notes/secure-project-workflow-review-2026-08-24.md</c> §A, users sit in the Operations
    /// subtree while secure records are owned in <c>Secure Projects</c>, so acting-user resolution
    /// writes a secure record's content into the general Operations container. Ownership is a
    /// property of the record, so the container follows the record.</para>
    ///
    /// <para><b>Cost</b>: <c>owningbusinessunit</c> rides along on the record read that already
    /// happens, so a SECURE record costs zero extra round trips (its own container wins and the
    /// business unit is never consulted). A non-secure record costs one additional read of the
    /// business unit row.</para>
    /// </summary>
    public Task<ContainerDecision> ResolveForRecordAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
        => ResolveForRecordAsync(entityLogicalName, recordId, nonSecureFallbackContainerId: null, ct);

    /// <summary>
    /// Resolve the container for a record, with an explicit non-secure fallback.
    ///
    /// <para>Server-side ingest uses this overload to pass <c>Communication:ArchiveContainerId</c>,
    /// which has no owning record to derive a business unit from. When
    /// <paramref name="nonSecureFallbackContainerId"/> is null the resolver derives the fallback from
    /// the record's own <c>owningbusinessunit</c> — see the parameterless overload.</para>
    /// </summary>
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
                    [SecurableEntityRegistry.SecureFlagAttribute, ContainerColumn, OwningBusinessUnitColumn],
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

        // Derive the non-secure default from the RECORD's owning business unit when the caller did not
        // supply one (task 076). Deliberately skipped for a secure record: its own container wins, so
        // the read would be wasted, and — more importantly — a secure record must never have a usable
        // fallback in scope at the decision point. Skipping it means the fail-closed path cannot
        // accidentally acquire one.
        var fallbackContainerId = nonSecureFallbackContainerId;
        if (!isSecure && string.IsNullOrWhiteSpace(fallbackContainerId))
        {
            fallbackContainerId = await ResolveOwningBusinessUnitContainerAsync(
                record, normalizedEntity, recordId, ct).ConfigureAwait(false);
        }

        var decision = SecureContainerDecision.Decide(isSecure, ownContainerId, fallbackContainerId);

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

    /// <summary>
    /// The non-secure default: the container stamped on the record's OWNING BUSINESS UNIT.
    ///
    /// <para>Returns <see langword="null"/> when the business unit has no container stamped, which is a
    /// legitimate and common state — verified live 2026-08-27, three of six business units have
    /// <c>sprk_containerid</c> unset. Null flows into
    /// <see cref="SecureContainerDecision.Decide"/> as "no fallback", which for a NON-SECURE record
    /// yields <see cref="ContainerDecisionOutcome.Unresolved"/> — the benign
    /// caller-keeps-its-existing-behaviour case. It can never soften a secure record's refusal,
    /// because this method is not called for secure records at all.</para>
    ///
    /// <para><b>Read failures PROPAGATE.</b> An unreadable business unit means the container is unknown,
    /// and unknown must not become "no fallback" — that would silently turn a resolvable upload into
    /// an <c>Unresolved</c> skip. Same fail-closed posture as the rest of this component.</para>
    /// </summary>
    private async Task<string?> ResolveOwningBusinessUnitContainerAsync(
        Entity record,
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct)
    {
        // owningbusinessunit is an EntityReference on a user/team-owned row. Absent means the entity is
        // organization-owned (no owning BU exists) — a real answer, not a failure.
        if (record.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn) is not { Id: var buId }
            || buId == Guid.Empty)
        {
            _logger.LogInformation(
                "[SECURE-CONTAINER] {Entity} {RecordId} has no owning business unit, so there is no "
                + "business-unit container to fall back to.",
                entityLogicalName, recordId);
            return null;
        }

        var businessUnit = await _entityService
            .RetrieveAsync(BusinessUnitEntity, buId, [ContainerColumn], ct)
            .ConfigureAwait(false);

        var container = businessUnit?.GetAttributeValue<string>(ContainerColumn);

        if (string.IsNullOrWhiteSpace(container))
        {
            _logger.LogInformation(
                "[SECURE-CONTAINER] The owning business unit {BusinessUnitId} of {Entity} {RecordId} has "
                + "no '{Column}' stamped, so no container could be derived for its non-secure content.",
                buId, entityLogicalName, recordId, ContainerColumn);
            return null;
        }

        return container;
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

        // The LIKE pattern is built ONCE. It is trim-tolerant (leading '%' catches a stored value with
        // leading whitespace) and selective (the container id itself is in the pattern), and every
        // LIKE-significant character in the id is bracket-escaped so an SPE drive id cannot act as a
        // wildcard — see EscapeForLike. The code-side exact-after-trim compare below remains the AUTHORITY;
        // the filter only narrows what has to be inspected.
        var containerPattern = $"%{EscapeForLike(normalizedContainer)}%";

        // PASS 1 — who, among the SECURE records, claims this container?
        //
        // Both conditions are load-bearing and for different reasons:
        //   * `sprk_issecure == true` means shared-container noise cannot crowd the signal out of the page.
        //     Three live projects already share the root business unit's container id, so at BU-container
        //     scale the noise is hundreds of rows.
        //   * the container filter makes the probe SELECTIVE. Without it the query returns "any N secure
        //     records" rather than "claimants of THIS container", the page fills once the org simply HOLDS
        //     N secure records — the intended steady state, each with its own container — and the
        //     truncation guard below then fires on every call, for every container, including the correct
        //     owner's. That is a hard availability cliff at N, and it kills tasks 073 and 078 outright.
        foreach (var entityLogicalName in securableEntities)
        {
            var secureQuery = new QueryExpression(entityLogicalName)
            {
                // SELECTED, not merely filtered on, so the match can be re-confirmed in code.
                ColumnSet = new ColumnSet(ContainerColumn),
                TopCount = ClaimantProbeLimit,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SecurableEntityRegistry.SecureFlagAttribute, ConditionOperator.Equal, true),
                        new ConditionExpression(ContainerColumn, ConditionOperator.Like, containerPattern)
                    }
                }
            };

            // Propagates on failure — an unanswerable ownership question must not read as "unowned", which a
            // caller would treat as "an ordinary shared container".
            var secureResults = await _entityService.RetrieveMultipleAsync(secureQuery, ct).ConfigureAwait(false);

            var secureRows = secureResults?.Entities?.ToList() ?? [];

            foreach (var row in secureRows)
            {
                if (row is null || row.Id == Guid.Empty)
                {
                    continue;
                }

                // THE MATCH IS MADE IN CODE, NOT BY THE FILTER.
                //
                // The forward direction normalizes with Trim(), so a record stamped "  b!x  " stores its
                // content in b!x. A Dataverse `Equal` filter does not trim the stored value, so filtering on
                // the trimmed input alone would MISS that row — zero secure claimants, and the fail-open
                // "this is a shared container" answer. LIKE is deliberately WIDER than the answer (it also
                // matches a superstring such as b!xyz); this compare is what narrows it back to exact.
                if (!IsSameContainer(row.GetAttributeValue<string>(ContainerColumn), normalizedContainer))
                {
                    continue;
                }

                secureClaimants.Add(new OwningSecureRecord(entityLogicalName, row.Id));
            }

            // Truncation is DETECTABLE and fail-closed. `TopCount` does not populate
            // `EntityCollection.MoreRecords` (only PageInfo does), so a full page is the only available
            // signal that a claimant may lie beyond it. With the selective filter above, a full page means
            // ClaimantProbeLimit-plus SECURE records match this one container — pathological co-mingling in
            // its own right — so refusing is both honest and the correct answer.
            if (secureRows.Count >= ClaimantProbeLimit)
            {
                _logger.LogError(
                    "[SECURE-CONTAINER] The secure-claimant probe on '{Entity}' filled its page of {Limit} "
                    + "rows for container '{Container}'. That many secure records matching one container is "
                    + "itself co-mingling, and a further claimant may lie beyond the page, so ownership "
                    + "cannot be established. Refusing rather than answering.",
                    entityLogicalName, ClaimantProbeLimit, normalizedContainer);

                throw new SdapProblemException(
                    code: "container_ownership_indeterminate",
                    title: "Container ownership could not be established",
                    detail: "Too many secure records match this container for ownership to be determined.",
                    statusCode: 409);
            }
        }

        if (secureClaimants.Count == 0)
        {
            // No secure record claims this container, so it is a shared business-unit or archive container.
            // That is an ANSWER, not a failure: the caller decides what it means for them.
            //
            // Returning HERE, before pass 2, is deliberate. The co-mingling question only means anything
            // once a secure claimant exists, and a shared BU container legitimately has hundreds of
            // non-secure claimants — probing it would fill the page and turn the ordinary shared-container
            // case into a refusal, breaking task 078 for every normal container.
            return null;
        }

        // PASS 2 — does any NON-secure record ALSO claim this container? Only asked when a secure claimant
        // exists, where the expected answer is zero, so a full page here really is co-mingling.
        foreach (var entityLogicalName in securableEntities)
        {
            var coMingleQuery = new QueryExpression(entityLogicalName)
            {
                // Same reason as pass 1: the filter is wider than the answer, so the column must come back
                // for the code-side compare to be possible at all.
                ColumnSet = new ColumnSet(ContainerColumn),
                TopCount = ClaimantProbeLimit,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(ContainerColumn, ConditionOperator.Like, containerPattern)
                    },
                    Filters =
                    {
                        // `sprk_issecure != true` ALONE IS WRONG, and this nested Or is the fix.
                        //
                        // NotEqual is SQL `<> 1`, and `NULL <> 1` evaluates to UNKNOWN, so a row whose flag
                        // is NULL is EXCLUDED by it. Those rows are legitimate and expected — Dataverse does
                        // not back-fill a Two Options column on existing rows, and field-level security
                        // returns the row with the attribute masked rather than erroring (the same fact the
                        // absent-flag warning in ResolveForRecordAsync exists to surface). Excluding them
                        // makes a NULL-flagged non-secure claimant invisible, so co-mingling goes undetected
                        // and the secure record is reported as sole owner of a shared container.
                        new FilterExpression(LogicalOperator.Or)
                        {
                            Conditions =
                            {
                                new ConditionExpression(
                                    SecurableEntityRegistry.SecureFlagAttribute,
                                    ConditionOperator.NotEqual,
                                    true),
                                new ConditionExpression(
                                    SecurableEntityRegistry.SecureFlagAttribute, ConditionOperator.Null)
                            }
                        }
                    }
                }
            };

            var coMingleResults = await _entityService.RetrieveMultipleAsync(coMingleQuery, ct).ConfigureAwait(false);

            var coMingleRows = coMingleResults?.Entities?.ToList() ?? [];

            nonSecureClaimantCount += coMingleRows.Count(row =>
                row is not null
                && IsSameContainer(row.GetAttributeValue<string>(ContainerColumn), normalizedContainer));

            if (coMingleRows.Count >= ClaimantProbeLimit)
            {
                _logger.LogError(
                    "[SECURE-CONTAINER] The co-mingling probe on '{Entity}' filled its page of {Limit} rows "
                    + "for container '{Container}', which a secure record claims. Refusing rather than "
                    + "under-reporting co-mingling.",
                    entityLogicalName, ClaimantProbeLimit, normalizedContainer);

                throw new SdapProblemException(
                    code: "container_ownership_indeterminate",
                    title: "Container ownership could not be established",
                    detail: "Too many records match a container claimed by a secure record for co-mingling "
                            + "to be ruled out.",
                    statusCode: 409);
            }
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
    /// Dataverse error code <c>0x80040217 ObjectDoesNotExist</c> as a signed 32-bit integer, which is how
    /// <see cref="OrganizationServiceFault.ErrorCode"/> exposes it.
    /// </summary>
    private const int ObjectDoesNotExistErrorCode = -2147220969;

    /// <summary>
    /// Whether an exception from a Dataverse retrieve means "the row does not exist", as opposed to a
    /// transient, schema, or authorization failure. Only the former may be normalized to a 404: mapping a
    /// timeout to "not found" would turn a retryable condition into a permanent one, and the ingest path
    /// treats the 404 as permanent (it skips rather than retrying).
    ///
    /// <para><b>Typed, not substring-matched.</b> Matching <c>ex.Message</c> for "does not exist" / "was not
    /// found" fails on two counts. Dataverse fault messages are LOCALIZED, so on a non-English org the
    /// classification silently stops working and the raw fault escapes — which is the very condition the
    /// normalization exists to prevent. And it is over-broad: <i>"Attribute sprk_issecure was not found"</i>
    /// is a schema or field-level-security error, and reporting it to an operator as "the record does not
    /// exist" misdiagnoses precisely the masked-attribute case the absent-flag warning exists to surface.
    /// The error code is stable and locale-independent.</para>
    /// </summary>
    private static bool IsRecordNotFound(Exception ex)
        => ex is FaultException<OrganizationServiceFault> fault
           && fault.Detail?.ErrorCode == ObjectDoesNotExistErrorCode;

    /// <summary>
    /// Escapes the LIKE-significant characters so a container id cannot behave as a pattern.
    ///
    /// <para>Dataverse <see cref="ConditionOperator.Like"/> maps to T-SQL <c>LIKE</c>, where <c>%</c>,
    /// <c>_</c> and <c>[</c> are significant. <c>_</c> matters in practice rather than in theory: SPE drive
    /// ids are base64url-ish and routinely contain it, so an unescaped id would match unrelated containers.
    /// T-SQL's bracket form escapes all three — <c>_</c> → <c>[_]</c>, <c>%</c> → <c>[%]</c>, and <c>[</c> →
    /// <c>[[]</c> (which must be applied first, or it would re-escape the brackets just introduced).</para>
    /// </summary>
    private static string EscapeForLike(string value)
        => value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);

    /// <summary>
    /// The single definition of container equality on the reverse path: exact after trimming, matching the
    /// forward direction's <c>Trim()</c> normalization. A blank stored value never matches anything.
    /// </summary>
    private static bool IsSameContainer(string? storedContainer, string normalizedContainer)
        => !string.IsNullOrWhiteSpace(storedContainer)
           && string.Equals(storedContainer.Trim(), normalizedContainer, StringComparison.Ordinal);
}
