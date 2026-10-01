using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 075 — the ONE record-aware SharePoint Embedded container mapping, in both
/// directions. All of the logic that DECIDES anything lives in <see cref="SecureContainerDecision"/>; this
/// type is the data-fetching half plus the reverse lookup.
///
/// <para><b>Forward</b> (<see cref="ResolveForRecordAsync"/>): <i>which container does this record's content
/// belong in?</i> A secure record resolves to its own <c>sprk_containerid</c> or FAILS CLOSED. A CHILD record
/// (to-do, event, invoice) that is not itself secure but is filed under a SECURE project, matter or work
/// assignment resolves to that ROOT's own container or fails closed (task 155, owner C10 part 2). Everything
/// else resolves to the non-secure default: the RECORD's own <c>owningbusinessunit</c> container when the caller
/// supplies no fallback, <c>Communication:ArchiveContainerId</c> for server-side ingest.</para>
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
/// answer read as not-secure is the same isolation failure with an extra step. The same holds for the entity
/// NAME: an alias is mapped to its logical name, and a name that is not a real entity is refused rather than
/// read as "not securable" (task 151, #1038). Error codes:
/// <c>secure_record_container_missing</c> (409), <c>container_record_not_found</c> (404),
/// <c>container_entity_unknown</c> (400), <c>container_ownership_ambiguous</c> (409),
/// <c>container_ownership_indeterminate</c> (409), and for child records (task 155)
/// <c>container_ancestor_unresolved</c> (409; 503 when the child's own row or its root could not be read),
/// <c>container_ancestor_ambiguous</c> (409) and <c>container_ancestor_unverifiable</c> (409).</para>
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

    /// <summary>The Dataverse user entity, for the no-record case (<see cref="ResolveForActingUserAsync"/>).</summary>
    private const string SystemUserEntity = "systemuser";

    /// <summary>
    /// The <c>systemuser</c> column carrying the user's Entra object id.
    /// </summary>
    /// <remarks>
    /// This is a LOOKUP KEY, not a comparison. The Entra <c>oid</c> is matched against the column
    /// Dataverse stores it in — which is what makes the no-record path free of the id-space defect
    /// <c>CallerIdentityGuardTests.Rule2</c> exists to catch. Nothing here compares an <c>oid</c> to a
    /// <c>systemuserid</c>; the query TRANSLATES one into the other.
    /// </remarks>
    private const string AzureAdObjectIdColumn = "azureactivedirectoryobjectid";

    /// <summary>The <c>systemuser</c> column carrying the user's business unit.</summary>
    private const string BusinessUnitLookupColumn = "businessunitid";

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
    /// subtree while secure records are owned in <c>Secure Record</c>, so acting-user resolution
    /// writes a secure record's content into the general Operations container. Ownership is a
    /// property of the record, so the container follows the record.</para>
    ///
    /// <para><b>Cost</b>: <c>owningbusinessunit</c> rides along on the record read that already
    /// happens, so a SECURE record costs zero extra round trips (its own container wins and the
    /// business unit is never consulted). A non-secure record costs one additional read of the
    /// business unit row. A CHILD record (task 155) adds one read per linked root that can be secure —
    /// one in practice — and skips the business unit when that root is secure.</para>
    ///
    /// <para><b>Holds for EVERY entity as of task 155.</b> Before it, a non-securable entity (to-do, event,
    /// contact) returned Unresolved without reading the record, so this overload's promise was true only for
    /// securable entities and the record-keyed upload routes answered "No storage container is configured" for
    /// records whose container was derivable.</para>
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

        // ALIASES FIRST (task 151, #1038). A caller may name an association type by its friendly alias
        // ("project") rather than its logical name ("sprk_project"); the securable registry is keyed on logical
        // names only, so before this mapping "project" read as "not securable" and a SECURE project's content
        // resolved to a shared container. DocumentAssociationMap is THE alias table — the record-keyed upload
        // filter's EntitySetByType is held in lockstep with it (pinned key-by-key and entity-by-entity by
        // AssociationTypeLockstepTests) — so the record the route AUTHORIZED and the record this resolver READS
        // are the same record. A name that is not an alias passes through lower-cased, exactly as before. The
        // alias table WINS for a name that is both an alias and a real logical name ("invoice") — see
        // NormalizeEntityName.
        var normalizedEntity = NormalizeEntityName(entityLogicalName);

        // ONE registry question, ONE catalog lookup (task 151 review): "is this an entity at all, and can it be
        // secure?" answered together. Until task 151 the only question was "is it securable?", and "no" was
        // also the answer for a name that is NOT AN ENTITY AT ALL — a misspelling, an entity SET name
        // ("sprk_projects"), an unmapped alias — so a secure record named any of those ways got a non-secure
        // decision. Asking the two halves as two calls fixed that but fetched the catalog twice, so with Redis
        // down every ordinary non-securable upload paid for two full-org metadata round trips. Keep it one call.
        //
        // A metadata failure here PROPAGATES rather than being read as "not an entity" or "not securable" — see
        // ISecurableEntityRegistry's fail-closed contract.
        var securability = await _securableEntities
            .ClassifyEntityAsync(normalizedEntity, ct)
            .ConfigureAwait(false);

        if (securability == EntitySecurability.NotAnEntity)
        {
            // REFUSE rather than guess: an unknown answer is never "not secure".
            throw UnknownEntity(entityLogicalName);
        }

        var isSecurable = securability == EntitySecurability.Securable;

        // CHILD records (task 155, owner C10 part 2: "every child of a secure record is secure"). A to-do, event
        // or invoice filed under a SECURE project / matter / work assignment must store its content in that
        // root's OWN container — whether or not the child's entity can carry sprk_issecure itself. Until task
        // 155 a non-securable entity short-circuited here with no record read, so a to-do's upload resolved
        // Unresolved (the misleading "No storage container is configured" 409 on the record-keyed routes) and
        // an Office save to it fell through to a SHARED default container: the #1038 class, reached through
        // the child instead of the root.
        var ancestorLinks = CoreAncestorResolver.IsChildRecordEntity(normalizedEntity)
            ? ChildAncestorLinks.For(normalizedEntity)
            : null;

        if (CoreAncestorResolver.IsChildRecordEntity(normalizedEntity) && ancestorLinks is null)
        {
            // A CHILD entity whose link to its root this component does not know (task 155 escalation trigger
            // 1). Guessing "no ancestor" is how a secure root's content would reach a shared container, so the
            // answer is a refusal until the entity's links are verified and added to ChildAncestorLinks.
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                $"'{normalizedEntity}' is a child record type whose link to its project, matter or work "
                + "assignment is not known to the storage resolver, so it cannot be determined whether it sits "
                + "under a secure record.",
                statusCode: 409);
        }

        if (!isSecurable && ancestorLinks is null)
        {
            // An entity that cannot carry sprk_issecure AND has no ancestor concept (contact, account, …) cannot
            // be secure by any route, so there is no securability question to answer.
            if (!string.IsNullOrWhiteSpace(nonSecureFallbackContainerId))
            {
                // Explicit fallback (server-side ingest): unchanged — ZERO record round trips.
                return SecureContainerDecision.Decide(
                    isSecure: false, ownContainerId: null, fallbackContainerId: nonSecureFallbackContainerId);
            }

            // Two-argument overload: the documented contract — the non-secure default comes from the RECORD's
            // own owningbusinessunit. Before task 155 this branch returned Unresolved without reading the record,
            // so the overload's documentation was true only for SECURABLE entities, and every upload to a
            // contact answered "No storage container is configured" even when its business unit had one.
            var plainRecord = await ReadRecordAsync(
                normalizedEntity, recordId, [OwningBusinessUnitColumn], ct).ConfigureAwait(false);

            var plainFallback = await ResolveOwningBusinessUnitContainerAsync(
                plainRecord, normalizedEntity, recordId, ct).ConfigureAwait(false);

            return SecureContainerDecision.Decide(
                isSecure: false, ownContainerId: null, fallbackContainerId: plainFallback);
        }

        // Securable, or a CHILD whose root must be checked — and, deliberately, any value not handled above: the
        // secure path READS the record's own flag, which is the fail-closed direction (an entity without
        // sprk_issecure makes that read fault rather than resolve to a shared container).
        var columns = new List<string>(capacity: 16) { OwningBusinessUnitColumn };
        if (isSecurable)
        {
            columns.Add(SecurableEntityRegistry.SecureFlagAttribute);
            columns.Add(ContainerColumn);
        }

        if (ancestorLinks is not null)
        {
            // ONE record read carries the child's links too (task 155 constraint: no extra round trip for them).
            columns.AddRange(ancestorLinks.AllColumns);
        }

        Entity record;
        try
        {
            record = await ReadRecordAsync(normalizedEntity, recordId, [.. columns], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ancestorLinks is not null && ex is not SdapProblemException and not OperationCanceledException)
        {
            // For a CHILD this read IS its link to its root, so an unreadable row is an unreadable ancestor link
            // (task 155 AC4). It already failed closed — nothing below ran — but as a raw fault the record-keyed
            // routes rendered it as a generic 500 "Upload failed" carrying the exception text. Same posture and
            // code as an unreadable ROOT: unknown never becomes "not secure", and 503 because a retry may succeed.
            // Not-found (404) and the resolver's own refusals pass through untouched; a non-child keeps the
            // original propagate-raw contract.
            _logger.LogError(ex,
                "[SECURE-CONTAINER] Could not read {Entity} {RecordId}, whose row carries its link to its project, "
                + "matter or work assignment. Refusing rather than treating it as having no secure root ({Code}).",
                normalizedEntity, recordId, AncestorUnresolvedCode);

            throw AncestorUnresolved(
                normalizedEntity, recordId,
                $"This {normalizedEntity} could not be read, so its link to a project, matter or work assignment — "
                + "and whether its content belongs in a secure container — cannot be determined. Try again.",
                statusCode: 503,
                logDetail: "child row read failed");
        }

        var isSecure = false;
        string? ownContainerId = null;

        if (isSecurable)
        {
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

            isSecure = record.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute);
            ownContainerId = record.GetAttributeValue<string>(ContainerColumn);
        }

        // A record that is not itself secure but hangs off a SECURE root takes the root's own container — or
        // refuses. Consulted BEFORE any fallback is derived, for the same reason the business unit is skipped for
        // a secure record: a usable shared container must never be in scope at the point a secure root decides.
        if (!isSecure && ancestorLinks is not null)
        {
            var viaAncestor = await ResolveSecureAncestorAsync(
                record, normalizedEntity, recordId, ancestorLinks, ct).ConfigureAwait(false);

            if (viaAncestor is not null)
            {
                return viaAncestor;
            }
        }

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
    /// Read the record being resolved, normalizing "does not exist" to the documented 404.
    /// </summary>
    /// <remarks>
    /// A read failure means the answer is UNKNOWN, and unknown must never resolve to a shared fallback.
    /// <c>IGenericEntityService.RetrieveAsync</c> returns a non-nullable Entity and the production implementation
    /// THROWS a FaultException on not-found rather than returning null — so the null branch is defensive only,
    /// and the not-found case is normalized here so callers get the documented 404 instead of a raw SDK fault
    /// surfacing as a 500 or an unwinnable Service Bus retry. Every other failure PROPAGATES.
    /// </remarks>
    private async Task<Entity> ReadRecordAsync(
        string normalizedEntity,
        Guid recordId,
        string[] columns,
        CancellationToken ct)
    {
        // Guid.Empty cannot identify a record. A record with an unusable id is an indeterminate case, so it
        // refuses rather than falling through to the fallback.
        if (recordId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "container_record_not_found",
                title: "Cannot resolve a storage container",
                detail: $"An empty record id was supplied for entity '{normalizedEntity}', so it cannot be "
                        + "determined whether the record is secure. Refusing rather than using a shared container.",
                statusCode: 404);
        }

        Entity? record;
        try
        {
            record = await _entityService
                .RetrieveAsync(normalizedEntity, recordId, columns, ct)
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

        return record ?? throw new SdapProblemException(
            code: "container_record_not_found",
            title: "Cannot resolve a storage container",
            detail: $"Record '{recordId}' of type '{normalizedEntity}' was not found, so it cannot be "
                    + "determined whether it is secure. Refusing rather than using a shared container.",
            statusCode: 404);
    }

    // =============================================================================================
    // CHILD RECORDS — the secure ROOT decides (task 155, owner C10 part 2)
    // =============================================================================================

    /// <summary>Problem code: a child's root could not be read or is not known (task 155).</summary>
    internal const string AncestorUnresolvedCode = "container_ancestor_unresolved";

    /// <summary>Problem code: a child links to MORE than one secure root (task 155).</summary>
    internal const string AncestorAmbiguousCode = "container_ancestor_ambiguous";

    /// <summary>
    /// Problem code: the child is filed under another CHILD record, so its root comes from a DENORMALIZED stamp
    /// that can be stale (task 155 escalation trigger 2 — see <see cref="ResolveSecureAncestorAsync"/>).
    /// </summary>
    internal const string AncestorUnverifiableCode = "container_ancestor_unverifiable";

    /// <summary>
    /// For a record that is not itself secure, decide whether a SECURE root (project / matter / work
    /// assignment) owns its content. Returns the root's own container (<see cref="ContainerDecisionOutcome.ResolvedSecure"/>),
    /// or <see langword="null"/> when no linked root is secure — the caller then derives the non-secure default
    /// exactly as before. Every indeterminate answer THROWS; none returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a child filed under another CHILD is refused rather than resolved (escalation trigger 2,
    /// recorded in <c>notes/task-155-child-record-container-resolution.md</c>).</b> When a to-do's regarding is a
    /// project, its <c>sprk_regardingproject</c> IS the link the user chose. When its regarding is a
    /// communication, event, invoice, document or analysis, its <c>sprk_regarding{core}</c> value is a
    /// DENORMALIZED copy of that intermediate's root, written once by <see cref="CoreAncestorResolver"/> and never
    /// refreshed: nothing re-stamps a to-do when its communication is re-filed (one-hop by design, ADR-034), and a
    /// native form clear leaves the copy behind (task 051 F-051-6). Re-file the communication under a SECURE
    /// matter and the to-do still reads "non-secure root" — its bytes would go to a shared container, the exact
    /// #1038 leak this task closes. Resolving from that copy would be papering over it, so this refuses, with a
    /// code of its own so the owner's eventual choice (read the intermediate live, cascade re-stamps on
    /// re-file, or accept) can replace exactly this branch.</para>
    ///
    /// <para><b>Cost</b> (task 155 constraint): the child's links rode on the record read the caller already made;
    /// each linked root that CAN be secure costs one read of its flag and container — one in practice. A root
    /// type that cannot carry <c>sprk_issecure</c> (e.g. <c>sprk_servicerequest</c> in dev) is never read. The
    /// classification it asks is the registry's scope-memoized catalog, so no extra metadata round trip.</para>
    /// </remarks>
    private async Task<ContainerDecision?> ResolveSecureAncestorAsync(
        Entity record,
        string normalizedEntity,
        Guid recordId,
        ChildAncestorLinks links,
        CancellationToken ct)
    {
        var intermediates = links.IntermediateColumns
            .Where(column => record.GetAttributeValue<EntityReference>(column) is { } r && r.Id != Guid.Empty)
            .ToList();

        if (intermediates.Count > 0)
        {
            _logger.LogWarning(
                "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: it is filed under another child record ({Columns}), "
                + "so its project/matter/work-assignment link is a denormalized stamp that is not refreshed when "
                + "that record is re-filed. It cannot be verified that it does not sit under a SECURE record "
                + "({Code}).",
                normalizedEntity, recordId, string.Join(", ", intermediates), AncestorUnverifiableCode);

            throw new SdapProblemException(
                code: AncestorUnverifiableCode,
                title: "Cannot resolve a storage container",
                detail: $"This {normalizedEntity} is filed under another record ({string.Join(", ", intermediates)}), "
                        + "so whether it belongs to a secure project, matter or work assignment cannot be verified. "
                        + "Its content is not stored in a shared container on an unverified answer. File it directly "
                        + "against its project, matter or work assignment to upload to it.",
                statusCode: 409);
        }

        var secureRoots = new List<(string Entity, Guid Id, string? Container)>();

        foreach (var (ancestorEntity, linkColumn) in links.RootLinks)
        {
            if (record.GetAttributeValue<EntityReference>(linkColumn) is not { } link || link.Id == Guid.Empty)
            {
                continue;
            }

            var classification = await _securableEntities
                .ClassifyEntityAsync(ancestorEntity, ct)
                .ConfigureAwait(false);

            if (classification == EntitySecurability.NotSecurable)
            {
                // The root's entity cannot carry sprk_issecure (sprk_servicerequest in dev), so it cannot be
                // secure; reading the flag would fault. Derived from metadata, so a root type that GAINS the flag
                // is picked up without a code change.
                continue;
            }

            if (classification != EntitySecurability.Securable)
            {
                throw AncestorUnresolved(
                    normalizedEntity, recordId,
                    $"Its '{linkColumn}' link names '{ancestorEntity}', which this environment does not know as an "
                    + "entity, so whether that record is secure cannot be determined.",
                    statusCode: 409);
            }

            Entity? root;
            try
            {
                root = await _entityService
                    .RetrieveAsync(
                        ancestorEntity, link.Id,
                        [SecurableEntityRegistry.SecureFlagAttribute, ContainerColumn], ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRecordNotFound(ex))
            {
                // The ids go to the log (via AncestorUnresolved's reason), never to the response body.
                throw AncestorUnresolved(
                    normalizedEntity, recordId,
                    $"It is linked to a {ancestorEntity} that does not exist, so whether its content belongs in a "
                    + "secure container cannot be determined.",
                    statusCode: 409,
                    logDetail: $"{linkColumn} -> {ancestorEntity} {link.Id} (not found)");
            }
            catch (Exception ex) when (ex is not SdapProblemException and not OperationCanceledException)
            {
                // Unreadable is UNKNOWN, and unknown never becomes "not secure" (ADR-003). 503: retryable.
                _logger.LogError(ex,
                    "[SECURE-CONTAINER] Could not read {Ancestor} {AncestorId}, the root of {Entity} {RecordId}. "
                    + "Refusing rather than treating it as non-secure ({Code}).",
                    ancestorEntity, link.Id, normalizedEntity, recordId, AncestorUnresolvedCode);

                throw AncestorUnresolved(
                    normalizedEntity, recordId,
                    $"Its {ancestorEntity} could not be read, so whether its content belongs in a secure container "
                    + "cannot be determined. Try again.",
                    statusCode: 503,
                    logDetail: $"{linkColumn} -> {ancestorEntity} {link.Id} (read failed)");
            }

            if (root is null)
            {
                throw AncestorUnresolved(
                    normalizedEntity, recordId,
                    $"Its {ancestorEntity} returned no row, so whether its content belongs in a secure container "
                    + "cannot be determined.",
                    statusCode: 409,
                    logDetail: $"{linkColumn} -> {ancestorEntity} {link.Id} (null row)");
            }

            if (!root.Contains(SecurableEntityRegistry.SecureFlagAttribute))
            {
                // Same posture as the record path: NULL flags are legitimate and common (live dev 2026-10-01: 9
                // projects and 18 matters carry NULL sprk_issecure — a Two Options column is not back-filled), so
                // absent reads as non-secure, but distinguishably.
                _logger.LogWarning(
                    "[SECURE-CONTAINER] '{Attribute}' was ABSENT (not false) on {Ancestor} {AncestorId}, the root of "
                    + "{Entity} {RecordId}. Treating the root as non-secure.",
                    SecurableEntityRegistry.SecureFlagAttribute, ancestorEntity, link.Id, normalizedEntity, recordId);
            }

            if (root.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute))
            {
                secureRoots.Add((ancestorEntity, link.Id, root.GetAttributeValue<string>(ContainerColumn)));
            }
        }

        if (secureRoots.Count == 0)
        {
            return null;
        }

        if (secureRoots.Count > 1)
        {
            // Consistent with CommunicationContainerResolver's ambiguity refusal: two secure roots means no single
            // correct destination, and either choice places the content where the other root's members can read it.
            var roots = string.Join(", ", secureRoots.Select(r => $"{r.Entity}:{r.Id}"));

            _logger.LogError(
                "[SECURE-CONTAINER] {Entity} {RecordId} links to {Count} SECURE records [{Roots}]. There is no single "
                + "correct container for its content. Refusing ({Code}).",
                normalizedEntity, recordId, secureRoots.Count, roots, AncestorAmbiguousCode);

            throw new SdapProblemException(
                code: AncestorAmbiguousCode,
                title: "Ambiguous secure destination",
                detail: $"This {normalizedEntity} is linked to more than one secure record, so its content has no "
                        + "single correct storage container.",
                statusCode: 409);
        }

        var secureRoot = secureRoots[0];
        var decision = SecureContainerDecision.Decide(
            isSecure: true, ownContainerId: secureRoot.Container, fallbackContainerId: null);

        if (decision.Outcome == ContainerDecisionOutcome.FailClosed)
        {
            _logger.LogError(
                "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: its root {Ancestor} {AncestorId} is SECURE but has no "
                + "sprk_containerid. The child's content belongs with the secure root, and no shared container may "
                + "stand in for it.",
                normalizedEntity, recordId, secureRoot.Entity, secureRoot.Id);

            throw new SdapProblemException(
                code: "secure_record_container_missing",
                title: "Secure record has no storage container",
                detail: $"This {normalizedEntity} belongs to a secure {secureRoot.Entity}, which has no container of "
                        + "its own. Its content cannot be stored in a shared container, so this operation is refused. "
                        + "Provision the secure record's container first.",
                statusCode: 409);
        }

        _logger.LogInformation(
            "[SECURE-CONTAINER] Resolved {Entity} {RecordId} to the OWN container of its secure root {Ancestor} "
            + "{AncestorId} (not a business-unit container).",
            normalizedEntity, recordId, secureRoot.Entity, secureRoot.Id);

        return decision;
    }

    /// <param name="reason">Response-safe: names entity TYPES only, never another record's id.</param>
    /// <param name="logDetail">Ids and columns for the operator; logged, never returned to the caller.</param>
    private SdapProblemException AncestorUnresolved(
        string normalizedEntity, Guid recordId, string reason, int statusCode, string? logDetail = null)
    {
        _logger.LogWarning(
            "[SECURE-CONTAINER] REFUSED {Entity} {RecordId} ({Code}): {Reason} {Detail}",
            normalizedEntity, recordId, AncestorUnresolvedCode, reason, logDetail ?? string.Empty);

        return new SdapProblemException(
            code: AncestorUnresolvedCode,
            title: "Cannot resolve a storage container",
            detail: $"{reason} Refusing rather than using a shared container.",
            statusCode: statusCode);
    }

    /// <summary>
    /// How each CHILD record type links to its root, and which of its regarding columns name ANOTHER child.
    /// </summary>
    /// <remarks>
    /// <para><b>Verified against live metadata (spaarkedev1, read-only, 2026-10-01).</b> <c>sprk_todo</c> and
    /// <c>sprk_event</c> carry all four <c>sprk_regarding{core}</c> columns (<see cref="CoreAncestorResolver.CoreAncestorLookups"/>,
    /// referenced rather than restated); <c>sprk_invoice</c> carries none of them and links through its typed
    /// <c>sprk_matter</c> / <c>sprk_project</c> lookups instead. The intermediate columns are each entity's
    /// regarding lookups whose target is itself in <see cref="CoreAncestorResolver.ChildRecordEntities"/>.</para>
    ///
    /// <para><b>A child type NOT listed is refused</b> (<see cref="AncestorUnresolvedCode"/>), never read as
    /// "no ancestor" — task 155 escalation trigger 1. <c>sprk_communication</c>, <c>sprk_document</c> and
    /// <c>sprk_analysis</c> reach no caller of this resolver today (the communication pipeline has its own
    /// adapter, <c>CommunicationContainerResolver</c>, which asks only about securable regardings). Add an entry —
    /// verified against live metadata — before routing one of them here. A column missing from the live entity
    /// makes the record read FAULT, which fails closed.</para>
    /// </remarks>
    internal sealed class ChildAncestorLinks
    {
        private static readonly IReadOnlyList<(string AncestorEntity, string LinkColumn)> RegardingCoreLinks =
            CoreAncestorResolver.CoreAncestorLookups.Select(l => (l.EntityType, l.LookupAttribute)).ToArray();

        private static readonly IReadOnlyDictionary<string, ChildAncestorLinks> ByEntity =
            new Dictionary<string, ChildAncestorLinks>(StringComparer.Ordinal)
            {
                ["sprk_todo"] = new(
                    RegardingCoreLinks,
                    ["sprk_regardinganalysis", "sprk_regardingcommunication", "sprk_regardingdocument",
                     "sprk_regardingevent", "sprk_regardinginvoice"]),
                ["sprk_event"] = new(
                    RegardingCoreLinks,
                    ["sprk_regardinganalysis", "sprk_regardingcommunication", "sprk_regardingevent",
                     "sprk_regardinginvoice"]),
                ["sprk_invoice"] = new(
                    [("sprk_project", "sprk_project"), ("sprk_matter", "sprk_matter")],
                    []),
            };

        private ChildAncestorLinks(
            IReadOnlyList<(string AncestorEntity, string LinkColumn)> rootLinks,
            IReadOnlyList<string> intermediateColumns)
        {
            RootLinks = rootLinks;
            IntermediateColumns = intermediateColumns;
        }

        /// <summary>Root entity → the column on the child that links to it.</summary>
        public IReadOnlyList<(string AncestorEntity, string LinkColumn)> RootLinks { get; }

        /// <summary>Regarding columns that point at ANOTHER child record (whose root is only a denormalized stamp here).</summary>
        public IReadOnlyList<string> IntermediateColumns { get; }

        /// <summary>Every column the record read must carry for the decision.</summary>
        public IEnumerable<string> AllColumns => RootLinks.Select(l => l.LinkColumn).Concat(IntermediateColumns);

        /// <summary>The links for <paramref name="childEntity"/>, or <see langword="null"/> when they are not known.</summary>
        public static ChildAncestorLinks? For(string childEntity)
            => ByEntity.TryGetValue(childEntity, out var links) ? links : null;

        /// <summary>The child entities with known links — for the pin test.</summary>
        internal static IEnumerable<string> KnownChildEntities => ByEntity.Keys;
    }

    /// <summary>
    /// The stable problem code for a name that is neither a <see cref="DocumentAssociationMap"/> alias nor a
    /// real entity logical name (task 151, #1038). 400, distinct from <c>container_record_not_found</c> (404)
    /// and <c>secure_record_container_missing</c> (409), which existing clients branch on: a name that is not
    /// an entity is a malformed request, not a missing record or a missing container.
    /// </summary>
    internal const string UnknownEntityCode = "container_entity_unknown";

    /// <summary>
    /// The logical name for <paramref name="entityName"/>: the <see cref="DocumentAssociationMap"/> alias
    /// mapping when it is one, otherwise the trimmed, lower-cased input. Deliberately NOT a second alias table
    /// (CLAUDE.md §11) — anything the shared map does not know passes through and is then checked against the
    /// org's real entities.
    /// </summary>
    /// <remarks>
    /// <para><b>THE ALIAS TABLE WINS — the one stated exception to "a real logical name resolves byte-for-byte as
    /// before".</b> The alias lookup runs FIRST and is not second-guessed by the org's entity catalog. For every
    /// alias but one that is moot, because the alias is not also a real logical name (<c>project</c>,
    /// <c>matter</c>, <c>workassignment</c>, <c>event</c>, <c>todo</c>) or maps to itself (<c>contact</c>). The
    /// exception is the bare name <c>invoice</c>: it is ALSO the logical name of the out-of-the-box Dynamics 365
    /// Sales Invoice entity, which exists in any org with Sales installed. Here it resolves to
    /// <c>sprk_invoice</c>, never to the OOB entity.</para>
    ///
    /// <para>That is correct, not merely tolerated: every Spaarke caller that says <c>invoice</c> means
    /// <c>sprk_invoice</c> (it is the Office save / association wire's friendly type, and the record-keyed route
    /// filter's <c>EntityAccessFilter.EntitySetByType</c> authorizes <c>invoice</c> against <c>sprk_invoices</c>
    /// — so resolving it anywhere else would read a DIFFERENT record from the one the caller was authorized
    /// on). No caller passes the OOB Sales invoice: it has no <c>sprk_document</c> lookup and no container
    /// semantics. spaarkedev1 has no <c>invoice</c> entity at all (read-only metadata check, 2026-09-30:
    /// <c>EntityDefinitions(LogicalName='invoice')</c> → 404). Pinned by
    /// <c>RecordContainerResolverTests.BareInvoice_ResolvesAsSprkInvoice_EvenWhenTheOrgHasTheOobInvoiceEntity</c>.
    /// Any NEW alias that collides with a real OOB logical name joins this exception and needs the same
    /// justification.</para>
    /// </remarks>
    private static string NormalizeEntityName(string entityName)
        => DocumentAssociationMap.ToLogicalName(entityName) ?? entityName.Trim().ToLowerInvariant();

    private SdapProblemException UnknownEntity(string suppliedName)
    {
        var safeName = SanitizeForMessage(suppliedName);

        // Warning, not Error: the content was REFUSED, so nothing was mis-stored — but a caller passing a name
        // that is not an entity is a defect at that call site, and the supplied spelling is what finds it.
        _logger.LogWarning(
            "[SECURE-CONTAINER] REFUSED to resolve a storage container: '{SuppliedEntity}' is neither a known "
            + "association alias nor an entity logical name in this org ({Code}). Not read as 'not "
            + "securable' — a secure record named this way would otherwise land in a shared container.",
            safeName, UnknownEntityCode);

        return new SdapProblemException(
            code: UnknownEntityCode,
            title: "Unknown entity",
            detail: $"'{safeName}' is not an entity this service recognises, so it cannot be determined whether "
                    + "the record is secure. Name the record's entity by its logical name (for example "
                    + "'sprk_project') or a supported association type (for example 'project').",
            statusCode: 400);
    }

    /// <summary>
    /// The supplied name is caller-controlled (a route segment on the record-keyed upload routes), so it is
    /// reduced to identifier characters and bounded before it reaches a log line or a response body.
    /// </summary>
    private static string SanitizeForMessage(string value)
    {
        const int maxLength = 64;

        var trimmed = value.Trim();
        var chars = trimmed
            .Take(maxLength)
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '?')
            .ToArray();

        return trimmed.Length > maxLength ? new string(chars) + "…" : new string(chars);
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

        var container = await ReadBusinessUnitContainerAsync(buId, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Read one business unit's stamped container. Shared by the record path
    /// (<see cref="ResolveOwningBusinessUnitContainerAsync"/>) and the no-record path
    /// (<see cref="ResolveForActingUserAsync"/>) so there is ONE business-unit → container read.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> when the business unit has no container stamped — a legitimate and
    /// common state (verified live 2026-08-27: three of six business units have <c>sprk_containerid</c>
    /// unset). <b>Read failures PROPAGATE</b>: an unreadable business unit means the container is unknown,
    /// and unknown must not become "no container".
    /// </remarks>
    private async Task<string?> ReadBusinessUnitContainerAsync(Guid buId, CancellationToken ct)
    {
        var businessUnit = await _entityService
            .RetrieveAsync(BusinessUnitEntity, buId, [ContainerColumn], ct)
            .ConfigureAwait(false);

        return businessUnit?.GetAttributeValue<string>(ContainerColumn);
    }

    /// <summary>
    /// The NO-RECORD case: which container does content belong in when there is no owning record yet?
    /// Derives the acting user's business-unit container, server-side.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists in the component whose own docs argue against acting-user resolution.</b>
    /// <see cref="ResolveForRecordAsync"/>'s remarks are emphatic that the container must follow the
    /// RECORD, not the uploader, and that remains correct wherever a record exists — this method is not
    /// an alternative to it. It answers a different question, the one that method structurally cannot:
    /// content created BEFORE its owning record exists. Task 076's escalation established that twelve
    /// client sites resolve a container before the record exists, and
    /// <c>composeEditor.registration.ts</c> shows matter-less drafting is a DESIGNED flow, not an edge
    /// case. Those callers need a server-side answer or they keep supplying their own container id —
    /// which is the defect class this project exists to remove.</para>
    ///
    /// <para><b>It cannot be misused for a record.</b> The signature takes no entity or record, so it is
    /// structurally incapable of answering "which container for this record". A secure record cannot
    /// reach it either, because a secure record IS a record.</para>
    ///
    /// <para><b>Fail-closed, with one legitimate null.</b> A caller with no Dataverse user, or an
    /// ambiguous one, is an indeterminate answer and THROWS — it must never become "no container", which
    /// a call site could read as "carry on". The single non-throwing empty result is a business unit with
    /// no container stamped, which mirrors the record path's identical case.</para>
    ///
    /// <para>🔴 <b>Known residual, not solved here.</b> Content placed in a business-unit container this
    /// way and LATER associated to a secure record is already in the shared container, and SPE
    /// permissions are additive-only, so nothing retracts it. That is the gap written up in
    /// <c>notes/finding-secure-transition-container-migration.md</c> — filed as its own project by owner
    /// direction 2026-08-31. It is the price of supporting create-before-the-record-exists at all, and it
    /// is strictly smaller than today's behaviour, where the CLIENT names the container.</para>
    /// </remarks>
    /// <param name="actingUserObjectId">
    /// The caller's Entra object id (<c>oid</c>). Used as a lookup key on
    /// <see cref="AzureAdObjectIdColumn"/> — never compared against a <c>systemuserid</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ContainerDecision> ResolveForActingUserAsync(
        string? actingUserObjectId,
        CancellationToken ct = default)
    {
        // An unusable caller id cannot identify a business unit. Refusing beats resolving to whatever a
        // null filter would match.
        if (string.IsNullOrWhiteSpace(actingUserObjectId)
            || !Guid.TryParse(actingUserObjectId.Trim(), out var oid)
            || oid == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "acting_user_not_resolvable",
                title: "Cannot resolve a storage container",
                detail: "No usable caller identity was supplied, so the storage container for content with "
                        + "no owning record cannot be determined. Refusing rather than using a shared "
                        + "container.",
                statusCode: 403);
        }

        // TOP 2, not TOP 1: one row is the answer, two rows means the oid maps to more than one Dataverse
        // user and the business unit is ambiguous. Asking for one would silently pick a winner.
        var query = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet(BusinessUnitLookupColumn),
            TopCount = 2,
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(AzureAdObjectIdColumn, ConditionOperator.Equal, oid)
                }
            }
        };

        var users = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var rows = users?.Entities;
        var rowCount = rows?.Count ?? 0;

        if (rowCount == 0)
        {
            _logger.LogWarning(
                "[SECURE-CONTAINER] No Dataverse user matches caller oid {Oid} on '{Column}', so no "
                + "business-unit container can be derived for record-less content. Refusing.",
                oid, AzureAdObjectIdColumn);

            throw new SdapProblemException(
                code: "acting_user_not_resolvable",
                title: "Cannot resolve a storage container",
                detail: "Your account is not provisioned as a Dataverse user in this environment, so the "
                        + "storage location for this content cannot be determined. Ask an administrator to "
                        + "provision your user account.",
                statusCode: 403);
        }

        if (rowCount > 1)
        {
            _logger.LogError(
                "[SECURE-CONTAINER] Caller oid {Oid} matches {Count}+ Dataverse users on '{Column}'. The "
                + "owning business unit is ambiguous; refusing rather than choosing one.",
                oid, rowCount, AzureAdObjectIdColumn);

            throw new SdapProblemException(
                code: "acting_user_ambiguous",
                title: "Cannot resolve a storage container",
                detail: "Your Entra account maps to more than one Dataverse user, so the storage location "
                        + "for this content is ambiguous. Refusing rather than choosing one. Ask an "
                        + "administrator to resolve the duplicate user records.",
                statusCode: 409);
        }

        if (rows![0].GetAttributeValue<EntityReference>(BusinessUnitLookupColumn) is not { Id: var buId }
            || buId == Guid.Empty)
        {
            _logger.LogError(
                "[SECURE-CONTAINER] The Dataverse user for caller oid {Oid} carries no '{Column}'. "
                + "Refusing — a user with no business unit has no derivable container.",
                oid, BusinessUnitLookupColumn);

            throw new SdapProblemException(
                code: "acting_user_not_resolvable",
                title: "Cannot resolve a storage container",
                detail: "Your Dataverse user has no business unit, so the storage location for this "
                        + "content cannot be determined. Ask an administrator to check your user record.",
                statusCode: 409);
        }

        var container = await ReadBusinessUnitContainerAsync(buId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(container))
        {
            // The record path's identical case: a legitimate, common state. Yields Unresolved rather than
            // an exception so the caller reports "no storage configured" honestly instead of an error.
            _logger.LogInformation(
                "[SECURE-CONTAINER] The acting user's business unit {BusinessUnitId} has no '{Column}' "
                + "stamped, so no container could be derived for record-less content.",
                buId, ContainerColumn);
        }

        // isSecure: false is a statement of fact, not an assumption — there is no record, so there is
        // nothing that CAN be secure. FailClosed is therefore unreachable on this path by construction.
        return SecureContainerDecision.Decide(
            isSecure: false, ownContainerId: null, fallbackContainerId: container);
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
    ///
    /// <para><c>internal</c>, not private: the document-identity resolver (spaarkeai-word-add-in-r1 task 012)
    /// needs the same "absent vs. indeterminate" split, and a second copy of this predicate is how the two
    /// would drift.</para>
    /// </summary>
    internal static bool IsRecordNotFound(Exception ex)
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
