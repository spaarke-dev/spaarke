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
/// belong in?</i> A secure record resolves to its own <c>sprk_containerid</c> or FAILS CLOSED. A record that is
/// not itself secure but whose own row names a SECURE project, matter or work assignment — a CHILD (to-do, event,
/// invoice, communication), or a work assignment / project / contact whose columns can name one (task 155 f3 live
/// sweep) — resolves to that ROOT's own container or fails closed (task 155, owner C10 part 2). Since f4 "names"
/// is TRANSITIVE: a secure root anywhere above the record decides, however many roots sit between them, and two
/// different secure roots anywhere above it refuse as ambiguous. Everything else resolves
/// to the non-secure default: the RECORD's own <c>owningbusinessunit</c> container when the caller supplies no
/// fallback, <c>Communication:ArchiveContainerId</c> for server-side ingest.</para>
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
/// <c>container_ownership_indeterminate</c> (409), <c>securable_entities_unknown</c> (409, the registry answered
/// outside its contract — task 155 f3), and for records with root links (task 155)
/// <c>container_ancestor_unresolved</c> (409 — a missing row above the record, or a chain longer than the walk
/// follows; 503 when the child's own row, a regarding type or any row above it could not be read),
/// <c>container_ancestor_ambiguous</c> (409: two different secure roots anywhere above it, or a row's typed and
/// polymorphic regarding disagree) and <c>container_ancestor_unverifiable</c> (409).</para>
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

    /// <summary>
    /// <c>Communication:ArchiveContainerId</c> — the one shared container that is not a business unit's; the
    /// document-pointer check (task 166 r1) accepts it as an environment container.
    /// </summary>
    private readonly string? _archiveContainerId;

    public RecordContainerResolver(
        ISecurableEntityRegistry securableEntities,
        IGenericEntityService entityService,
        ILogger<RecordContainerResolver> logger,
        Microsoft.Extensions.Options.IOptions<Sprk.Bff.Api.Configuration.CommunicationOptions>? communicationOptions = null)
    {
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _archiveContainerId = communicationOptions?.Value?.ArchiveContainerId;
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
    /// business unit row. A record with root links (task 155) adds one read per root ABOVE it — the root's flag,
    /// container and own links ride on that one read, and the walk continues from it (f4; bounded at
    /// <see cref="MaxRootChainDepth"/> deep and <see cref="MaxRootReads"/> rows) — plus, on any row where the
    /// polymorphic regarding pair is the sole thing naming a record, one read of its <c>sprk_recordtype_ref</c> row
    /// (task 155 f3); it skips the business unit when a root is secure. The full per-shape table is in
    /// <c>notes/task-155-child-record-container-resolution.md</c> "Round f3" / "Round f4".</para>
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
    public Task<ContainerDecision> ResolveForRecordAsync(
        string entityLogicalName,
        Guid recordId,
        string? nonSecureFallbackContainerId,
        CancellationToken ct = default)
        => ResolveCoreAsync(
            entityLogicalName, recordId, nonSecureFallbackContainerId, deriveBusinessUnitFallback: true, ct);

    /// <summary>
    /// Resolve the container for a record whose non-secure default is FIXED by the caller — the explicit
    /// <paramref name="nonSecureFallbackContainerId"/>, or nothing at all — and never derived from the record's
    /// business unit (task 155 f4).
    /// </summary>
    /// <remarks>
    /// <para>For <see cref="Sprk.Bff.Api.Services.Communication.Engine.CommunicationContainerResolver"/>, whose
    /// non-secure default is <c>Communication:ArchiveContainerId</c>. When that is unconfigured the adapter's contract
    /// has always been "no container — skip", never "the communication's business-unit container"; the public
    /// four-argument overload reads a null fallback as "derive it from the business unit", which would have changed
    /// that. Everything that DECIDES — the secure answer, every refusal, the transitive root walk — is the same code
    /// as <see cref="ResolveForRecordAsync(string, Guid, string?, CancellationToken)"/>.</para>
    /// </remarks>
    internal Task<ContainerDecision> ResolveForRecordWithFixedFallbackAsync(
        string entityLogicalName,
        Guid recordId,
        string? nonSecureFallbackContainerId,
        CancellationToken ct = default)
        => ResolveCoreAsync(
            entityLogicalName, recordId, nonSecureFallbackContainerId, deriveBusinessUnitFallback: false, ct);

    private async Task<ContainerDecision> ResolveCoreAsync(
        string entityLogicalName,
        Guid recordId,
        string? nonSecureFallbackContainerId,
        bool deriveBusinessUnitFallback,
        CancellationToken ct)
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

        if (securability is not (EntitySecurability.NotSecurable or EntitySecurability.Securable))
        {
            // Any value outside the registry's contract (task 155 f3). Before f3 such a value computed
            // isSecurable = false and — for an entity with no ancestor concept — took the NOT-securable branch
            // straight to a shared container, while the comment below claimed it took the secure path. An
            // answer the code does not understand is an unknown answer, and unknown is refused.
            throw SecurabilityIndeterminate(normalizedEntity, recordId, securability);
        }

        var isSecurable = securability == EntitySecurability.Securable;

        // CHILD records (task 155, owner C10 part 2: "every child of a secure record is secure"). A to-do, event
        // or invoice filed under a SECURE project / matter / work assignment must store its content in that
        // root's OWN container — whether or not the child's entity can carry sprk_issecure itself. Until task
        // 155 a non-securable entity short-circuited here with no record read, so a to-do's upload resolved
        // Unresolved (the misleading "No storage container is configured" 409 on the record-keyed routes) and
        // an Office save to it fell through to a SHARED default container: the #1038 class, reached through
        // the child instead of the root.
        //
        // Task 155 f3: the links table now covers EVERY entity this resolver can be asked about whose own row can
        // name a project / matter / work assignment, directly or through another record — not only the child
        // taxonomy. A work assignment filed regarding a matter, a project carrying the polymorphic regarding pair
        // and a contact carrying an invoice lookup are read the same way (live sweep: ChildAncestorLinks).
        //
        // Task 155 f4: the walk is TRANSITIVE — every root above the record is read with its own links in turn
        // (ResolveSecureAncestorAsync) — and sprk_communication has an entry, so the communication pipeline's
        // container decision is this same resolution (CommunicationContainerResolver).
        var isChild = CoreAncestorResolver.IsChildRecordEntity(normalizedEntity);
        var ancestorLinks = ChildAncestorLinks.For(normalizedEntity);

        if (isChild && ancestorLinks is null)
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
            // An entity that cannot carry sprk_issecure AND whose own row names no project / matter / work
            // assignment by any column (account, …) cannot be secure by any route, so there is no securability
            // question to answer. (contact is NOT in this set as of f3: its sprk_invoice lookup names a record that
            // can belong to a matter — ChildAncestorLinks.)
            if (!string.IsNullOrWhiteSpace(nonSecureFallbackContainerId))
            {
                // Explicit fallback (server-side ingest): unchanged — ZERO record round trips.
                return SecureContainerDecision.Decide(
                    isSecure: false, ownContainerId: null, fallbackContainerId: nonSecureFallbackContainerId);
            }

            if (!deriveBusinessUnitFallback)
            {
                // Fixed-fallback caller with no fallback: nothing to derive, nothing secure to find — Unresolved.
                return SecureContainerDecision.Decide(isSecure: false, ownContainerId: null, fallbackContainerId: null);
            }

            // Two-argument overload: the documented contract — the non-secure default comes from the RECORD's
            // own owningbusinessunit. Before task 155 this branch returned Unresolved without reading the record,
            // so the overload's documentation was true only for SECURABLE entities, and every upload to such a
            // record answered "No storage container is configured" even when its business unit had one.
            var plainRecord = await ReadRecordAsync(
                normalizedEntity, recordId, [OwningBusinessUnitColumn], ct).ConfigureAwait(false);

            var plainFallback = await ResolveOwningBusinessUnitContainerAsync(
                plainRecord, normalizedEntity, recordId, ct).ConfigureAwait(false);

            return SecureContainerDecision.Decide(
                isSecure: false, ownContainerId: null, fallbackContainerId: plainFallback);
        }

        // Securable (its own flag is read), or an entity whose own row can name a root (its links are read). Only
        // those two classifications reach here: NotAnEntity and every value outside the registry's contract were
        // refused above, and a NotSecurable entity with no links returned above.
        var columns = new List<string>(capacity: 20) { OwningBusinessUnitColumn };
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
            isChild && ex is not SdapProblemException && !IsCallerCancellation(ex, ct))
        {
            // For a CHILD this read IS its link to its root, so an unreadable row is an unreadable ancestor link
            // (task 155 AC4). It already failed closed — nothing below ran — but as a raw fault the record-keyed
            // routes rendered it as a generic 500 "Upload failed" carrying the exception text. Same posture and
            // code as an unreadable ROOT: unknown never becomes "not secure", and 503 because a retry may succeed.
            // Not-found (404) and the resolver's own refusals pass through untouched; a non-child (a root, a
            // contact — including those that gained links in f3) keeps the original propagate-raw contract, which
            // is equally fail-closed: nothing below runs.
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
        if (!isSecure && string.IsNullOrWhiteSpace(fallbackContainerId) && deriveBusinessUnitFallback)
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
    /// The record's OWN container — and ONLY its own: <see cref="ContainerDecisionOutcome.ResolvedSecure"/> when the
    /// record itself is secure (<c>sprk_issecure</c> = true) and stamps a container; <see cref="ContainerDecisionOutcome.FailClosed"/>
    /// when it is secure with no container, or when its flag is ABSENT (unknown is never "not secure");
    /// <see cref="ContainerDecisionOutcome.Unresolved"/> when the record is not secure or its entity cannot be secure.
    /// Never an ancestor's container, never a business-unit fallback (unified-access-control-r2 task 166 r1).
    /// </summary>
    /// <remarks>
    /// <para><b>Why a second question.</b> <see cref="ResolveForRecordAsync(string, Guid, CancellationToken)"/> answers
    /// "where does this record's CONTENT go?", and since task 155 a non-secure record filed under a secure root answers
    /// with the ROOT's container (<c>ResolvedSecure</c>). That is right for storing content and wrong for REMOVING
    /// access: project closure and the single-grant revoke strip the revoked grantees' permissions from "the record's
    /// own container", and used against the content answer they would strip them from the secure ANCESTOR's container
    /// — where the same people may still hold a grant on the ancestor itself. Container permissions are justified by
    /// grants on the record that OWNS the container; this method answers exactly that.</para>
    /// <para>Refusals that mean "this name is not an entity" propagate as in the forward resolution; a row read failure
    /// propagates too (the callers fold any exception into "could not be determined").</para>
    /// </remarks>
    internal async Task<ContainerDecision> ResolveOwnContainerAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            throw new ArgumentException("Entity logical name is required.", nameof(entityLogicalName));
        }

        var normalizedEntity = NormalizeEntityName(entityLogicalName);
        var securability = await _securableEntities.ClassifyEntityAsync(normalizedEntity, ct).ConfigureAwait(false);

        if (securability == EntitySecurability.NotAnEntity)
        {
            throw UnknownEntity(entityLogicalName);
        }

        if (securability == EntitySecurability.NotSecurable)
        {
            // An entity that cannot carry sprk_issecure owns no isolated container.
            return SecureContainerDecision.Decide(isSecure: false, ownContainerId: null, fallbackContainerId: null);
        }

        if (securability != EntitySecurability.Securable)
        {
            throw SecurabilityIndeterminate(normalizedEntity, recordId, securability);
        }

        var record = await ReadRecordAsync(
            normalizedEntity, recordId, [SecurableEntityRegistry.SecureFlagAttribute, ContainerColumn], ct).ConfigureAwait(false);

        if (!record.Contains(SecurableEntityRegistry.SecureFlagAttribute))
        {
            _logger.LogWarning(
                "[SECURE-CONTAINER] '{Attribute}' was ABSENT on {Entity} {RecordId}; whether it owns an isolated container "
                + "cannot be determined, so its own-container decision FAILS CLOSED.",
                SecurableEntityRegistry.SecureFlagAttribute, normalizedEntity, recordId);
            return new ContainerDecision(ContainerDecisionOutcome.FailClosed, null);
        }

        return SecureContainerDecision.Decide(
            isSecure: record.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute),
            ownContainerId: record.GetAttributeValue<string>(ContainerColumn),
            fallbackContainerId: null);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    // The document-pointer check — unified-access-control-r2 task 166 r1 (owner round 21 item 1, part b)
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The code every app-only download refusal of an unverifiable document pointer carries.</summary>
    public const string DocumentStorageUnverifiedCode = "document_storage_unverified";

    /// <summary>An email attachment is a child document of the email document; its file lives where the email's does.</summary>
    private const string ParentDocumentColumn = "sprk_parentdocument";

    /// <summary>
    /// Throws <see cref="SdapProblemException"/> (<see cref="DocumentStorageUnverifiedCode"/>, 409) unless
    /// <see cref="IsDocumentPointerContainerAllowedAsync"/> accepts the pointer. Call it BEFORE every app-only download
    /// that follows a <c>sprk_document</c> row's <c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c>.
    /// </summary>
    public async Task EnsureDocumentPointerContainerAsync(Guid documentId, string? pointerDriveId, CancellationToken ct = default)
    {
        if (await IsDocumentPointerContainerAllowedAsync(documentId, pointerDriveId, ct).ConfigureAwait(false))
        {
            return;
        }

        throw new SdapProblemException(
            code: DocumentStorageUnverifiedCode,
            title: "Document storage could not be verified",
            detail: "This document's file is not in a storage container its record may use, so it is not served. "
                    + "An administrator must repair the document's storage location.",
            statusCode: 409);
    }

    /// <summary>
    /// May the BFF follow this document row's SharePoint Embedded pointer AS THE APPLICATION? (task 166 r1)
    /// </summary>
    /// <remarks>
    /// <para><b>The threat.</b> The BFF downloads a document's bytes as the managed identity from the drive the row's
    /// <c>sprk_graphdriveid</c> names. Until the pointer columns are field-secured (scripts/Set-DocumentPointerFieldSecurity.ps1)
    /// any Write holder can re-point a row — and rows forged before that lock stay forged. This check runs before the
    /// download and refuses a pointer into a container the document has no business in.</para>
    /// <para><b>The rule.</b> (1) A pointer into a SECURE record's own container is honoured only for a document that
    /// hangs off that very record — named directly by one of its root lookups, or through a related record that this
    /// resolver resolves to that container, or through its parent document (one level). (2) Any other pointer must name
    /// one of THIS environment's shared containers: a business unit's <c>sprk_containerid</c>, or the configured
    /// communication archive container. A container this environment does not own (another customer's) is refused.
    /// (3) Anything that cannot be decided — an indeterminate owner, a read fault — refuses.</para>
    /// <para><b>Why not "must equal the container derived for the row" for every document.</b> Live dev data
    /// (2026-10-04): 447 of 530 active documents were uploaded before task 076 into the UPLOADER's business-unit
    /// container, not their record's. A strict derived-container comparison would refuse ~85% of legitimate downloads;
    /// that remaining tightening needs those files moved first (task note §8). What a forged pointer can reach is
    /// already narrowed to the environment's own non-secure containers, and the field-security lock stops new
    /// forgeries.</para>
    /// </remarks>
    public async Task<bool> IsDocumentPointerContainerAllowedAsync(
        Guid documentId, string? pointerDriveId, CancellationToken ct = default)
    {
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(pointerDriveId))
        {
            return false;
        }

        var drive = pointerDriveId.Trim();
        try
        {
            var secureOwner = await ResolveOwningRecordAsync(drive, ct).ConfigureAwait(false);
            if (secureOwner is not null)
            {
                var belongs = await DocumentHangsOffAsync(documentId, secureOwner, drive, depth: 0, ct).ConfigureAwait(false);
                if (!belongs)
                {
                    _logger.LogWarning(
                        "[DOCUMENT-POINTER] REFUSED: document {DocumentId} points into the OWN container of secure {Entity} "
                        + "{RecordId}, but the document does not belong to that record. Not served app-only.",
                        documentId, secureOwner.EntityLogicalName, secureOwner.RecordId);
                }

                return belongs;
            }

            if (!string.IsNullOrWhiteSpace(_archiveContainerId) && IsSameContainer(_archiveContainerId, drive))
            {
                return true;
            }

            if (await IsBusinessUnitContainerAsync(drive, ct).ConfigureAwait(false))
            {
                return true;
            }

            _logger.LogWarning(
                "[DOCUMENT-POINTER] REFUSED: document {DocumentId} points into a container that is neither a secure "
                + "record's own nor one of this environment's business-unit / archive containers. Not served app-only.",
                documentId);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DOCUMENT-POINTER] REFUSED: the container of document {DocumentId}'s pointer could not be verified "
                + "(fail closed).", documentId);
            return false;
        }
    }

    /// <summary>
    /// <see cref="IsDocumentPointerContainerAllowedAsync(Guid, string?, CancellationToken)"/> for callers holding the
    /// document id as text (<see cref="DocumentEntity.Id"/>, job payloads). An id that is not a GUID names no
    /// <c>sprk_document</c> row, so its pointer cannot be verified — refused.
    /// </summary>
    public Task<bool> IsDocumentPointerContainerAllowedAsync(
        string? documentId, string? pointerDriveId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(documentId, out var id))
        {
            _logger.LogWarning(
                "[DOCUMENT-POINTER] REFUSED: '{DocumentId}' is not a document id, so the pointer it carries cannot be "
                + "verified (fail closed).", documentId);
            return Task.FromResult(false);
        }

        return IsDocumentPointerContainerAllowedAsync(id, pointerDriveId, ct);
    }

    private async Task<bool> DocumentHangsOffAsync(
        Guid documentId, OwningSecureRecord owner, string drive, int depth, CancellationToken ct)
    {
        // The document's record links are the ONE canonical vocabulary (DocumentLinkFields — every sprk_document
        // record-link lookup, verified against live metadata). A link whose target this resolver cannot resolve to a
        // secure container simply does not match; it never widens what is honoured.
        var columns = DocumentLinkFields.LogicalNames.Append(ParentDocumentColumn).ToArray();
        var row = await _entityService.RetrieveAsync("sprk_document", documentId, columns, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        foreach (var (column, target) in DocumentLinkFields.All.Select(f => (f.LogicalName, f.TargetEntityLogicalName)))
        {
            if (row.GetAttributeValue<EntityReference>(column) is not { Id: var linkedId } || linkedId == Guid.Empty)
            {
                continue;
            }

            if (string.Equals(target, owner.EntityLogicalName, StringComparison.Ordinal) && linkedId == owner.RecordId)
            {
                return true;
            }

            try
            {
                var decision = await ResolveForRecordAsync(target, linkedId, ct).ConfigureAwait(false);
                if (decision.Outcome == ContainerDecisionOutcome.ResolvedSecure && IsSameContainer(decision.ContainerId, drive))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One unresolvable link is not a match; the others may still be.
                _logger.LogInformation(ex,
                    "[DOCUMENT-POINTER] Link {Column} -> {Target} {LinkedId} of document {DocumentId} could not be resolved.",
                    column, target, linkedId, documentId);
            }
        }

        if (depth == 0 && row.GetAttributeValue<EntityReference>(ParentDocumentColumn) is { Id: var parentId }
            && parentId != Guid.Empty && parentId != documentId)
        {
            return await DocumentHangsOffAsync(parentId, owner, drive, depth: 1, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Does a business unit of THIS environment stamp <paramref name="drive"/> as its container?</summary>
    private async Task<bool> IsBusinessUnitContainerAsync(string drive, CancellationToken ct)
    {
        var query = new QueryExpression(BusinessUnitEntity)
        {
            ColumnSet = new ColumnSet(ContainerColumn),
            TopCount = ClaimantProbeLimit,
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions = { new ConditionExpression(ContainerColumn, ConditionOperator.Like, $"%{EscapeForLike(drive)}%") }
            }
        };

        var results = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return results?.Entities?.Any(e => IsSameContainer(e.GetAttributeValue<string>(ContainerColumn), drive)) == true;
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
    /// How many root hops ABOVE the record the walk follows (task 155 f4). The deepest live shape spaarkedev1 can hold
    /// is three — a to-do → its work assignment → that work assignment's project → (through the project's polymorphic
    /// pair) a matter — so four leaves one hop of slack. A chain that needs more is REFUSED, never truncated into "no
    /// secure root above it".
    /// </summary>
    internal const int MaxRootChainDepth = 4;

    /// <summary>
    /// How many distinct rows above the record one resolution may read (task 155 f4): the cost bound across BRANCHES,
    /// where <see cref="MaxRootChainDepth"/> bounds each branch's length. A to-do can name a project, a matter and a work
    /// assignment at once, and that work assignment a project and a matter of its own, so breadth is real; eight is well
    /// above any live shape (the commonest costs one). Exceeding it is refused, like the depth bound.
    /// </summary>
    internal const int MaxRootReads = 8;

    /// <summary>
    /// A record the walk follows: its entity and id, and the column path that named it. <see cref="Via"/> holds
    /// column and entity NAMES only, so it may reach a response body; ids go to the log.
    /// </summary>
    private readonly record struct RootHop(string Entity, Guid Id, string Via);

    /// <summary>
    /// For a record that is not itself secure, decide whether a SECURE root (project / matter / work assignment) owns
    /// its content — ANYWHERE above it. Returns that root's own container
    /// (<see cref="ContainerDecisionOutcome.ResolvedSecure"/>), or <see langword="null"/> when no root above it is
    /// secure — the caller then derives the non-secure default exactly as before. Every indeterminate answer THROWS;
    /// none returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The walk is transitive (task 155 f4).</b> Until f4 a root link was followed ONE hop: a to-do regarding
    /// a work assignment read only that work assignment's own flag. But f3 made a NON-secure work assignment that is
    /// filed regarding a SECURE matter store its own files in the matter's container (interpretation iii), so its to-do
    /// — which read "work assignment, not secure" — went to the shared business-unit container: the chain disagreed
    /// with itself, and the disagreement was a fail-open (live: event <c>a30254d0</c> under work assignment
    /// <c>9c0254d0</c>). Now every row the walk reads names the records above IT, through the same table the record's
    /// own row uses (<see cref="ChildAncestorLinks"/>), and those are read in turn:</para>
    /// <list type="bullet">
    /// <item>ANY secure root in the chain → the content goes to that root's own container (or refuses if it has none);
    /// never a shared container while a secure root is anywhere above.</item>
    /// <item>Two DIFFERENT secure roots anywhere in the chain → <see cref="AncestorAmbiguousCode"/>. The walk continues
    /// PAST a secure root to find out (a secure work assignment filed regarding a different secure matter is two). The
    /// same record reached twice (a diamond) is one root, not two.</item>
    /// <item>An unreadable hop → <see cref="AncestorUnresolvedCode"/> 503 (retryable); a missing hop, a null row or a
    /// type the org does not know → 409.</item>
    /// <item>A hop whose own row names an intermediate is the held path (<see cref="AncestorUnverifiableCode"/>): the
    /// record cannot be placed, exactly as the hop itself cannot.</item>
    /// <item>Bounded (<see cref="MaxRootChainDepth"/>, <see cref="MaxRootReads"/>) and cycle-guarded (a record already
    /// on the walk, including the record being resolved, is not read again). A chain past the bound refuses, 409.</item>
    /// </list>
    ///
    /// <para><b>Why a child filed under another CHILD is refused rather than resolved (escalation trigger 2,
    /// recorded in <c>notes/task-155-child-record-container-resolution.md</c>).</b> When a to-do's regarding is a
    /// project, its <c>sprk_regardingproject</c> IS the link the user chose. When its regarding is a
    /// communication, event, invoice, document or analysis, its <c>sprk_regarding{core}</c> value is a
    /// DENORMALIZED copy of that intermediate's root, written once by <see cref="CoreAncestorResolver"/> and never
    /// refreshed: nothing re-stamps a to-do when its communication is re-filed (one-hop by design, ADR-034), and a
    /// native form clear leaves the copy behind (task 051 F-051-6). Re-file the communication under a SECURE
    /// matter and the to-do still reads "non-secure root" — its bytes would go to a shared container, the exact
    /// #1038 leak this task closes. Resolving from that copy would be papering over it, so this refuses, with a
    /// code of its own so the owner's eventual choice (task 156: option b, cascade re-stamps on re-file) can replace
    /// exactly this branch. The walk above follows only ROOTS (and an entry's explicitly followed records — the
    /// communication's invoice); it never reads an intermediate live, which would be option (a).</para>
    ///
    /// <para>The same refusal covers every other record that belongs to a root without the row carrying that root
    /// (task 155 f2/f3/f4): an AGREEMENT, BUDGET or REPORT CARD and a SERVICE REQUEST (each hangs off a matter /
    /// project / work assignment, none is a CHILD that gets stamped — a service request is a CORE record that
    /// cannot carry <c>sprk_issecure</c>), an invoice's <c>sprk_regardingagreement</c>, a work assignment's
    /// communication / event / invoice regarding, a contact's <c>sprk_invoice</c>, a communication's service request /
    /// event / analysis / budget / report card regarding, and a polymorphic regarding pair that names any of those
    /// types. Resolving one would read "no root" and pick a shared container even when the root is secure.</para>
    ///
    /// <para><b>The polymorphic regarding pair</b> (<c>sprk_regardingrecordid</c> + <c>sprk_regardingrecordtype</c>,
    /// task 155 f3) is read on every row that carries it — the record's and every hop's: see
    /// <see cref="ResolvePolymorphicRegardingAsync"/>. A root it names joins the walk like a typed link.</para>
    ///
    /// <para><b>Cost</b> (task 155 constraint): the record's links rode on the record read the caller already made;
    /// each row above it costs ONE read carrying its flag, its container AND its own links — so a to-do under a project
    /// or matter still costs one root read (neither carries a link live); a to-do under a work assignment that regards
    /// a matter costs two. The pair costs one <c>sprk_recordtype_ref</c> read per row only when it is the sole thing
    /// on that row naming a record. A root type that cannot carry <c>sprk_issecure</c> AND names nothing above it is
    /// never read. The classification it asks is the registry's scope-memoized catalog, so no extra metadata round
    /// trip.</para>
    /// </remarks>
    private async Task<ContainerDecision?> ResolveSecureAncestorAsync(
        Entity record,
        string normalizedEntity,
        Guid recordId,
        ChildAncestorLinks links,
        CancellationToken ct)
    {
        // The record itself is on the walk: a root that names it back (a project whose pair names a work assignment
        // that regards that project) is a cycle, not another root.
        var visited = new HashSet<(string Entity, Guid Id)> { (normalizedEntity, recordId) };
        var secureRoots = new List<(RootHop Hop, string? Container)>();
        var rootReads = 0;

        // Level 0: the record's OWN row (already read by the caller) names the first hops. A held column here refuses
        // before any root is read.
        var frontier = await NextHopsAsync(
                record, normalizedEntity, links, viaPrefix: null, normalizedEntity, recordId, ct)
            .ConfigureAwait(false);

        for (var depth = 1; frontier.Count > 0; depth++)
        {
            var next = new List<RootHop>();

            foreach (var hop in frontier)
            {
                if (!visited.Add((hop.Entity, hop.Id)))
                {
                    // Reached twice — a diamond (the to-do names matter M directly AND through its work assignment) or a
                    // cycle. That record is already on the walk, so it adds nothing; it is not a second root.
                    continue;
                }

                var classification = await _securableEntities
                    .ClassifyEntityAsync(hop.Entity, ct)
                    .ConfigureAwait(false);

                var hopLinks = ChildAncestorLinks.For(hop.Entity);

                if (classification == EntitySecurability.NotSecurable && hopLinks is null)
                {
                    // Cannot carry sprk_issecure in this org (reading the flag would fault) AND its row names nothing
                    // above it, so it can be neither a secure root nor the way to one: never read. Derived from
                    // metadata, so a root type that GAINS the flag is read without a code change. (Live, all three
                    // roots carry it.)
                    continue;
                }

                if (classification is not (EntitySecurability.NotSecurable or EntitySecurability.Securable))
                {
                    throw AncestorUnresolved(
                        normalizedEntity, recordId,
                        $"It is filed under a '{hop.Entity}', which this environment does not know as an entity, so "
                        + "whether that record is secure cannot be determined.",
                        statusCode: 409,
                        logDetail: $"{hop.Via} -> {hop.Entity} {hop.Id} (classified {(int)classification})");
                }

                if (depth > MaxRootChainDepth || rootReads >= MaxRootReads)
                {
                    throw AncestorUnresolved(
                        normalizedEntity, recordId,
                        "The chain of records it is filed under is longer than the storage resolver follows, so "
                        + "whether a secure project, matter or work assignment sits above it cannot be determined.",
                        statusCode: 409,
                        logDetail: $"{hop.Via} -> {hop.Entity} {hop.Id} (depth {depth}, {rootReads} rows read; limits "
                                   + $"{MaxRootChainDepth} deep, {MaxRootReads} rows)");
                }

                var isSecurableHop = classification == EntitySecurability.Securable;
                var columns = new List<string>(capacity: 12);
                if (isSecurableHop)
                {
                    columns.Add(SecurableEntityRegistry.SecureFlagAttribute);
                    columns.Add(ContainerColumn);
                }

                if (hopLinks is not null)
                {
                    // ONE read of the hop carries its own links too — the same rule as the record's own read.
                    columns.AddRange(hopLinks.AllColumns);
                }

                rootReads++;
                var row = await ReadHopAsync(hop, [.. columns], normalizedEntity, recordId, ct).ConfigureAwait(false);

                if (isSecurableHop)
                {
                    if (!row.Contains(SecurableEntityRegistry.SecureFlagAttribute))
                    {
                        // Same posture as the record path: NULL flags are legitimate and common (live dev 2026-10-01: 9
                        // projects and 18 matters carry NULL sprk_issecure — a Two Options column is not back-filled),
                        // so absent reads as non-secure, but distinguishably. The walk still continues above it.
                        _logger.LogWarning(
                            "[SECURE-CONTAINER] '{Attribute}' was ABSENT (not false) on {Ancestor} {AncestorId}, above "
                            + "{Entity} {RecordId} ({Via}). Treating it as non-secure.",
                            SecurableEntityRegistry.SecureFlagAttribute, hop.Entity, hop.Id, normalizedEntity, recordId,
                            hop.Via);
                    }

                    if (row.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute))
                    {
                        secureRoots.Add((hop, row.GetAttributeValue<string>(ContainerColumn)));
                    }
                }

                if (hopLinks is not null)
                {
                    // Followed even when THIS hop is secure: a different secure root above it is an ambiguity (there
                    // is no single correct container), and the only way to see one is to keep walking.
                    next.AddRange(await NextHopsAsync(
                            row, hop.Entity, hopLinks, hop.Via, normalizedEntity, recordId, ct)
                        .ConfigureAwait(false));
                }
            }

            frontier = next;
        }

        if (secureRoots.Count == 0)
        {
            return null;
        }

        if (secureRoots.Count > 1)
        {
            // Consistent with the communication pipeline's ambiguity refusal: two secure roots means no single correct
            // destination, and either choice places the content where the other root's members can read it.
            var secureRootList = string.Join(
                ", ", secureRoots.Select(r => $"{r.Hop.Entity}:{r.Hop.Id} via {r.Hop.Via}"));

            _logger.LogError(
                "[SECURE-CONTAINER] {Entity} {RecordId} has {Count} SECURE records above it [{Roots}]. There is no "
                + "single correct container for its content. Refusing ({Code}).",
                normalizedEntity, recordId, secureRoots.Count, secureRootList, AncestorAmbiguousCode);

            throw new SdapProblemException(
                code: AncestorAmbiguousCode,
                title: "Ambiguous secure destination",
                detail: $"This {normalizedEntity} is linked to more than one secure record, so its content has no "
                        + "single correct storage container.",
                statusCode: 409);
        }

        var (secureRoot, secureContainer) = secureRoots[0];
        var decision = SecureContainerDecision.Decide(
            isSecure: true, ownContainerId: secureContainer, fallbackContainerId: null);

        if (decision.Outcome == ContainerDecisionOutcome.FailClosed)
        {
            _logger.LogError(
                "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: {Ancestor} {AncestorId} above it ({Via}) is SECURE but "
                + "has no sprk_containerid. Its content belongs with the secure root, and no shared container may stand "
                + "in for it.",
                normalizedEntity, recordId, secureRoot.Entity, secureRoot.Id, secureRoot.Via);

            throw new SdapProblemException(
                code: "secure_record_container_missing",
                title: "Secure record has no storage container",
                detail: $"This {normalizedEntity} belongs to a secure {secureRoot.Entity}, which has no container of "
                        + "its own. Its content cannot be stored in a shared container, so this operation is refused. "
                        + "Provision the secure record's container first.",
                statusCode: 409);
        }

        _logger.LogInformation(
            "[SECURE-CONTAINER] Resolved {Entity} {RecordId} to the OWN container of the secure {Ancestor} {AncestorId} "
            + "above it ({Via}; not a business-unit container).",
            normalizedEntity, recordId, secureRoot.Entity, secureRoot.Id, secureRoot.Via);

        return decision;
    }

    /// <summary>
    /// The records one row names ABOVE it, to be read next (task 155 f4): its followed links that are set — every
    /// root link, plus the records its entry follows live (the communication's invoice) — and, when the row carries
    /// it, the record its polymorphic regarding pair names. Used for the record's own row (level 0) and for every row
    /// the walk reads.
    /// </summary>
    /// <remarks>
    /// A held column (<see cref="ChildAncestorLinks.IntermediateColumns"/>) THROWS
    /// <see cref="AncestorUnverifiableCode"/>, and so does every answer the pair cannot give. These MUST ride on the
    /// row's read: Dataverse returns only the requested columns, so a column left out reads as "not set".
    /// </remarks>
    /// <param name="viaPrefix"><see langword="null"/> for the record's own row; the path that reached the row otherwise.</param>
    private async Task<List<RootHop>> NextHopsAsync(
        Entity row,
        string rowEntity,
        ChildAncestorLinks rowLinks,
        string? viaPrefix,
        string normalizedEntity,
        Guid recordId,
        CancellationToken ct)
    {
        var held = rowLinks.IntermediateColumns
            .Where(column => row.GetAttributeValue<EntityReference>(column) is { } r && r.Id != Guid.Empty)
            .ToList();

        if (held.Count > 0)
        {
            throw Unverifiable(
                normalizedEntity, recordId, Via(viaPrefix, rowEntity, string.Join(", ", held)),
                heldBy: viaPrefix is null ? null : rowEntity);
        }

        var hops = rowLinks.FollowedLinks
            .Select(l => (l.Target, l.LinkColumn,
                Id: row.GetAttributeValue<EntityReference>(l.LinkColumn)?.Id ?? Guid.Empty))
            .Where(l => l.Id != Guid.Empty)
            .Select(l => new RootHop(l.Target, l.Id, Via(viaPrefix, rowEntity, l.LinkColumn)))
            .ToList();

        if (rowLinks.HasPolymorphicRegarding)
        {
            var named = await ResolvePolymorphicRegardingAsync(
                    row, rowEntity, rowLinks, viaPrefix, hops, normalizedEntity, recordId, ct)
                .ConfigureAwait(false);

            if (named is { } pairHop)
            {
                hops.Add(pairHop);
            }
        }

        return hops;
    }

    /// <summary>The column path for logs and the response: names only, never ids.</summary>
    private static string Via(string? viaPrefix, string rowEntity, string columns)
        => viaPrefix is null ? columns : $"{viaPrefix} > {rowEntity}.{columns}";

    /// <summary>
    /// Read one row above the record — its flag and container when its type can be secure, and its own links —
    /// normalizing every failure to the typed refusal (missing / null row 409, unreadable 503).
    /// </summary>
    private async Task<Entity> ReadHopAsync(
        RootHop hop, string[] columns, string normalizedEntity, Guid recordId, CancellationToken ct)
    {
        Entity? row;
        try
        {
            row = await _entityService
                .RetrieveAsync(hop.Entity, hop.Id, columns, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            // The ids go to the log (via AncestorUnresolved's logDetail), never to the response body.
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                $"It is linked to a {hop.Entity} that does not exist, so whether its content belongs in a secure "
                + "container cannot be determined.",
                statusCode: 409,
                logDetail: $"{hop.Via} -> {hop.Entity} {hop.Id} (not found)");
        }
        catch (Exception ex) when (ex is not SdapProblemException && !IsCallerCancellation(ex, ct))
        {
            // Unreadable is UNKNOWN, and unknown never becomes "not secure" (ADR-003). 503: retryable.
            _logger.LogError(ex,
                "[SECURE-CONTAINER] Could not read {Ancestor} {AncestorId}, above {Entity} {RecordId} ({Via}). Refusing "
                + "rather than treating it as non-secure ({Code}).",
                hop.Entity, hop.Id, normalizedEntity, recordId, hop.Via, AncestorUnresolvedCode);

            throw AncestorUnresolved(
                normalizedEntity, recordId,
                $"Its {hop.Entity} could not be read, so whether its content belongs in a secure container "
                + "cannot be determined. Try again.",
                statusCode: 503,
                logDetail: $"{hop.Via} -> {hop.Entity} {hop.Id} (read failed)");
        }

        return row ?? throw AncestorUnresolved(
            normalizedEntity, recordId,
            $"Its {hop.Entity} returned no row, so whether its content belongs in a secure container "
            + "cannot be determined.",
            statusCode: 409,
            logDetail: $"{hop.Via} -> {hop.Entity} {hop.Id} (null row)");
    }

    /// <summary>
    /// True only when the CALLER cancelled (task 155 f2). A Dataverse HTTP timeout also surfaces as a
    /// <see cref="TaskCanceledException"/>, but with the caller's token still live — that is an unreadable row, and
    /// it gets the typed, retryable 503 rather than escaping as a generic 500.
    /// </summary>
    private static bool IsCallerCancellation(Exception ex, CancellationToken ct)
        => ex is OperationCanceledException && ct.IsCancellationRequested;

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
    /// The held-path refusal (escalation trigger 2): the record — or a record above it — is filed under another record
    /// that belongs to a root, and that row cannot say which root: a denormalized stamp at best, nothing at all at worst.
    /// </summary>
    /// <param name="via">The column path that named the intermediate — names only, for the log and the response.</param>
    /// <param name="heldBy">
    /// <see langword="null"/> when the record's OWN row is filed under the intermediate; otherwise the type of the record
    /// ABOVE it that is (task 155 f4) — the advice then names that record, since the record itself is filed correctly.
    /// </param>
    private SdapProblemException Unverifiable(string normalizedEntity, Guid recordId, string via, string? heldBy = null)
    {
        _logger.LogWarning(
            "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: {Holder} is filed under another record that belongs to a "
            + "project/matter/work assignment ({Via}), so that row carries at most a denormalized stamp of that root "
            + "(not refreshed when the record is re-filed) and, for an agreement, budget, report card or service "
            + "request, no root link at all. It cannot be verified that it does not sit under a SECURE record ({Code}).",
            normalizedEntity, recordId, heldBy is null ? "it" : $"the {heldBy} above it", via, AncestorUnverifiableCode);

        var detail = heldBy is null
            ? $"This {normalizedEntity} is filed under another record ({via}), so whether it belongs to a secure "
              + "project, matter or work assignment cannot be verified. Its content is not stored in a shared "
              + "container on an unverified answer. File it directly against its project, matter or work "
              + "assignment to upload to it."
            : $"This {normalizedEntity} belongs to a {heldBy} that is itself filed under another record ({via}), so "
              + "whether it belongs to a secure project, matter or work assignment cannot be verified. Its content is "
              + $"not stored in a shared container on an unverified answer. File the {heldBy} directly against its "
              + "project, matter or work assignment to upload to this record.";

        return new SdapProblemException(
            code: AncestorUnverifiableCode,
            title: "Cannot resolve a storage container",
            detail: detail,
            statusCode: 409);
    }

    /// <summary>The polymorphic regarding pair's record id column (a STRING, no referential integrity).</summary>
    internal const string RegardingRecordIdColumn = "sprk_regardingrecordid";

    /// <summary>The polymorphic regarding pair's type column (Lookup → <c>sprk_recordtype_ref</c>).</summary>
    internal const string RegardingRecordTypeColumn = "sprk_regardingrecordtype";

    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string RecordTypeLogicalNameColumn = "sprk_recordlogicalname";

    /// <summary>
    /// Read the polymorphic regarding pair on one row (task 155 f3; on every row the walk reads since f4, ADR-024
    /// resolver fields). Returns the record above it that the pair alone names — to be followed like a typed link —
    /// or <see langword="null"/> when the pair adds nothing. Every answer the pair cannot give THROWS.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is read at all.</b> Live spaarkedev1 (read-only, 2026-10-01) holds events whose typed regarding
    /// lookups are all NULL but whose pair names a <c>sprk_matter</c>; 161 of 276 communications carry it (f4). A
    /// resolver that reads only the typed columns sees "no root" on such a row and picks a shared container — even when
    /// the matter is secure.</para>
    ///
    /// <para><b>The rules</b>, in order:</para>
    /// <list type="number">
    /// <item>No record id → the pair names no record and adds nothing. (A TYPE with no id — 21 events and 1 to-do
    /// live — names no record either.)</item>
    /// <item>An id that is not a GUID → refused (<see cref="AncestorUnresolvedCode"/>, 409).</item>
    /// <item>The id is the id of a followed link set on the same row → the pair names THAT record: agreement by
    /// identity, nothing new, no read. The typed lookup is referentially enforced, so it — not the pair's type
    /// label — says what the record is; a mislabelled type (1 live event) cannot hide a second record. The same
    /// holds for the row's typed PARTY regarding lookups (<see cref="ChildAncestorLinks.PartyRegardingColumns"/>,
    /// task 155 f5): an id equal to the row's own <c>sprk_regardingperson</c> / <c>…contact</c> /
    /// <c>…organization</c> / <c>…account</c> names that person or organization, which is not ownership, so the pair
    /// adds nothing. This is the shape the OUTBOUND sender writes for every email regarding a person, organization or
    /// account (<c>CommunicationService.MapAssociationFieldsAsync</c> sets the typed lookup and the pair id, and never
    /// the pair's type); without it rule 5 refused every one of them as "no type" and lost its <c>.eml</c> archive.</item>
    /// <item>A followed link is set and the pair names a DIFFERENT record → the row says two things about what it
    /// is filed under → refused as ambiguous (<see cref="AncestorAmbiguousCode"/>, 409). Neither is picked.</item>
    /// <item>The pair is the only thing naming a record → its type decides, read from <c>sprk_recordtype_ref</c>: a
    /// type the row's entry FOLLOWS (a root; the communication's invoice) is returned and walked like a typed link (a
    /// record that does not exist refuses there, 409); an INTERMEDIATE is the held path
    /// (<see cref="AncestorUnverifiableCode"/>); a PARTY (contact, account, organization) is not ownership and adds
    /// nothing, exactly like the typed party columns; a missing type, an unreadable type row (503) or a type this
    /// table does not classify → refused (<see cref="AncestorUnresolvedCode"/>).</item>
    /// </list>
    /// </remarks>
    /// <param name="followedHops">The followed links already set on the same row (rules 3 and 4).</param>
    private async Task<RootHop?> ResolvePolymorphicRegardingAsync(
        Entity row,
        string rowEntity,
        ChildAncestorLinks rowLinks,
        string? viaPrefix,
        IReadOnlyList<RootHop> followedHops,
        string normalizedEntity,
        Guid recordId,
        CancellationToken ct)
    {
        // Response-safe phrasing: the record's own row ("Its"), or the TYPE of the row above it — never an id.
        var possessive = viaPrefix is null ? "Its" : $"Its {rowEntity}'s";
        var subject = viaPrefix is null ? "It" : $"Its {rowEntity}";
        var via = Via(viaPrefix, rowEntity, RegardingRecordIdColumn);

        var rawId = row.GetAttributeValue<string>(RegardingRecordIdColumn);

        if (string.IsNullOrWhiteSpace(rawId))
        {
            return null;
        }

        if (!Guid.TryParse(rawId.Trim(), out var pairId) || pairId == Guid.Empty)
        {
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                $"{possessive} '{RegardingRecordIdColumn}' does not hold a valid record identifier, so the record it is "
                + "filed under cannot be determined.",
                statusCode: 409,
                logDetail: $"{via} = '{SanitizeForMessage(rawId)}'");
        }

        // Rule 3: agreement by identity — with a followed link, or (f5) with the row's own typed PARTY regarding. Both
        // are referentially enforced lookups on the same row, so the pair names that record; a party is not ownership.
        if (followedHops.Any(h => h.Id == pairId) || NamesTypedParty(row, rowLinks, pairId))
        {
            return null;
        }

        if (followedHops.Count > 0)
        {
            var typed = string.Join(", ", followedHops.Select(h => $"{h.Via} -> {h.Entity}:{h.Id}"));

            _logger.LogError(
                "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: the typed link(s) [{Typed}] and the polymorphic "
                + "regarding ({Via} = {PairId}) on the same {RowEntity} row name DIFFERENT records. Neither is picked "
                + "({Code}).",
                normalizedEntity, recordId, typed, via, pairId, rowEntity, AncestorAmbiguousCode);

            throw new SdapProblemException(
                code: AncestorAmbiguousCode,
                title: "Ambiguous secure destination",
                detail: viaPrefix is null
                    ? $"This {normalizedEntity}'s regarding fields disagree about which record it is filed under, so "
                      + "its content has no single correct storage container."
                    : $"The regarding fields of the {rowEntity} this {normalizedEntity} belongs to disagree about which "
                      + "record it is filed under, so its content has no single correct storage container.",
                statusCode: 409);
        }

        if (row.GetAttributeValue<EntityReference>(RegardingRecordTypeColumn) is not { } typeRef
            || typeRef.Id == Guid.Empty)
        {
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                $"{subject} names the record it is filed under without that record's type, so whether that record is "
                + "secure cannot be determined.",
                statusCode: 409,
                logDetail: $"{via} = {pairId}, {RegardingRecordTypeColumn} not set");
        }

        var pairEntity = await ReadRegardingTypeAsync(typeRef.Id, normalizedEntity, recordId, pairId, ct)
            .ConfigureAwait(false);

        if (rowLinks.Follows(pairEntity))
        {
            return new RootHop(pairEntity, pairId, via);
        }

        switch (ChildAncestorLinks.KindOf(pairEntity))
        {
            case ChildAncestorLinks.RecordKind.Intermediate:
                throw Unverifiable(
                    normalizedEntity, recordId, $"{via} -> {pairEntity}",
                    heldBy: viaPrefix is null ? null : rowEntity);

            case ChildAncestorLinks.RecordKind.Party:
                return null;

            default:
                throw AncestorUnresolved(
                    normalizedEntity, recordId,
                    $"It is filed under a '{SanitizeForMessage(pairEntity)}', a record type the storage resolver does "
                    + "not classify, so whether it belongs to a secure project, matter or work assignment cannot be "
                    + "determined.",
                    statusCode: 409,
                    logDetail: $"{via} = {pairId}, type {pairEntity}");
        }
    }

    /// <summary>
    /// Whether the pair's id is the id of one of the row's typed PARTY regarding lookups (rule 3, task 155 f5). Those
    /// columns ride on the row's read whenever the row carries the pair (<see cref="ChildAncestorLinks.AllColumns"/>).
    /// Identity only: a party that is set with a DIFFERENT id leaves the pair to rules 4 and 5.
    /// </summary>
    private static bool NamesTypedParty(Entity row, ChildAncestorLinks rowLinks, Guid pairId)
        => rowLinks.PartyRegardingColumns.Any(column =>
            row.GetAttributeValue<EntityReference>(column) is { } party && party.Id == pairId);

    /// <summary>The entity logical name a <c>sprk_recordtype_ref</c> row stands for — or a refusal.</summary>
    private async Task<string> ReadRegardingTypeAsync(
        Guid typeRefId, string normalizedEntity, Guid recordId, Guid pairId, CancellationToken ct)
    {
        Entity? typeRow;
        try
        {
            typeRow = await _entityService
                .RetrieveAsync(RecordTypeRefEntity, typeRefId, [RecordTypeLogicalNameColumn], ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                "The type of the record it is filed under does not exist, so whether that record is secure cannot be "
                + "determined.",
                statusCode: 409,
                logDetail: $"{RegardingRecordTypeColumn} -> {RecordTypeRefEntity} {typeRefId} (not found); pair id {pairId}");
        }
        catch (Exception ex) when (ex is not SdapProblemException && !IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex,
                "[SECURE-CONTAINER] Could not read {TypeEntity} {TypeId}, the regarding type of {Entity} {RecordId}. "
                + "Refusing rather than treating the record as having no root ({Code}).",
                RecordTypeRefEntity, typeRefId, normalizedEntity, recordId, AncestorUnresolvedCode);

            throw AncestorUnresolved(
                normalizedEntity, recordId,
                "The type of the record it is filed under could not be read, so whether that record is secure cannot "
                + "be determined. Try again.",
                statusCode: 503,
                logDetail: $"{RegardingRecordTypeColumn} -> {RecordTypeRefEntity} {typeRefId} (read failed)");
        }

        var logicalName = typeRow?.GetAttributeValue<string>(RecordTypeLogicalNameColumn);

        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                "The type of the record it is filed under could not be determined, so whether that record is secure "
                + "cannot be determined.",
                statusCode: 409,
                logDetail: $"{RegardingRecordTypeColumn} -> {RecordTypeRefEntity} {typeRefId} "
                           + (typeRow is null ? "(null row)" : "(no logical name)"));
        }

        return logicalName.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// The registry answered with a value outside its contract (neither NotAnEntity, NotSecurable nor Securable).
    /// Unknown is refused — the same code the communication pipeline uses when securability cannot be determined.
    /// </summary>
    private SdapProblemException SecurabilityIndeterminate(
        string normalizedEntity, Guid recordId, EntitySecurability securability)
    {
        _logger.LogError(
            "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: the securable-entity registry classified the entity as "
            + "'{Securability}', which is not a defined answer. Refusing rather than treating it as not securable.",
            normalizedEntity, recordId, (int)securability);

        return new SdapProblemException(
            code: "securable_entities_unknown",
            title: "Securability could not be determined",
            detail: $"Whether a {normalizedEntity} can be secure could not be determined, so its content is not stored "
                    + "in a shared container. Refusing rather than guessing.",
            statusCode: 409);
    }

    /// <summary>
    /// How each record type this resolver can be asked about names its ROOT (project / matter / work assignment),
    /// directly or through another record — from the task 155 f3 live metadata sweep, extended in f4 to
    /// <c>sprk_communication</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>The sweep (spaarkedev1, read-only, 2026-10-01; communication 2026-10-02).</b> For every entity in this
    /// table AND every entity the record-keyed upload routes, the Office save path, Compose and the communication
    /// adapter can resolve (<c>sprk_todo</c>, <c>sprk_event</c>, <c>sprk_invoice</c>, <c>sprk_workassignment</c>,
    /// <c>sprk_project</c>, <c>sprk_matter</c>, <c>contact</c>, <c>sprk_communication</c>), EVERY Lookup / Customer /
    /// Owner column was enumerated with its targets
    /// (<c>EntityDefinitions(LogicalName='x')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata</c>), and
    /// every target was classified by its OWN lookups. The full table is in
    /// <c>notes/task-155-child-record-container-resolution.md</c> "Round f3" / "Round f4", and the literal list is
    /// pinned by <c>ChildRecordContainerResolutionTests.ChildRecordRead_RequestsEveryLinkAndIntermediateColumn</c>.</para>
    ///
    /// <para><b>The rule.</b> Every column whose target IS a root is FOLLOWED (read, flag- and container-checked, and
    /// its own row walked in turn — f4); every column whose target can HANG OFF a root
    /// (<see cref="RecordKind.Intermediate"/>) is the held path, unless the entry explicitly follows that target (only
    /// the communication's invoice); party targets (contact, account, organization) and reference / principal /
    /// grouping targets (systemuser, team, businessunit, currency, the <c>*_ref</c> tables, eventset, AI search index,
    /// triage category, communication thread) are not ownership and are not FOLLOWED. A <c>sprk_regardingrecordid</c>
    /// column means the row carries the polymorphic pair, which is read too — and with it the row's typed PARTY
    /// REGARDING columns (<see cref="PartyRegardingColumns"/>, f5), on the same read, for one purpose only: the pair's
    /// rule 3 (an id equal to the row's own typed party names that party). They never move content.</para>
    ///
    /// <para><b>The same table serves every row the walk reads</b> (task 155 f4): the record's own row AND each root
    /// above it — so a work assignment's links are read whether the work assignment is the record or the root of a
    /// to-do. A root type with no entry (<c>sprk_matter</c>) names nothing above itself.</para>
    ///
    /// <para><b>An entity the child taxonomy names but this table does not is refused</b> (task 155 escalation
    /// trigger 1): <c>sprk_document</c> and <c>sprk_analysis</c> reach no caller of this resolver today. Add an entry —
    /// swept against live metadata — before routing one of them here. A column missing from the live entity makes the
    /// row read FAULT, which fails closed.</para>
    /// </remarks>
    internal sealed class ChildAncestorLinks
    {
        /// <summary>What a record of a given type is, for the question "can it be or hang off a root?".</summary>
        internal enum RecordKind
        {
            /// <summary>A project, matter or work assignment: it carries <c>sprk_issecure</c> and its own container.</summary>
            Root,

            /// <summary>A record that itself has (or can have) a root: following it needs a read this resolver does not make.</summary>
            Intermediate,

            /// <summary>A person or organization: referenced, never an owner of content.</summary>
            Party,
        }

        /// <summary>
        /// Every record type a link — typed or polymorphic — can name, classified from the live sweep. A type not in
        /// this table is UNKNOWN, and a polymorphic regarding naming one is refused.
        /// </summary>
        /// <remarks>
        /// Intermediate, each with its live evidence: analysis / communication / document / event / invoice / to-do are
        /// the CHILD taxonomy (stamped once, never refreshed — trigger 2); agreement and report card carry
        /// <c>sprk_regardingmatter</c> / <c>sprk_regardingproject</c>; budget carries typed <c>sprk_matter</c> /
        /// <c>sprk_project</c>; service request carries <c>sprk_regarding{matter,project,workassignment}</c> and cannot
        /// carry <c>sprk_issecure</c> itself. Party: contact, account and sprk_organization have no lookup to a root (the
        /// <c>sprk_invoice</c> lookup on contact and organization is a reference to an invoice, not an owner of the party;
        /// a contact RESOLVED here still has that column read — see the contact entry below).
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, RecordKind> KindByEntity =
            new Dictionary<string, RecordKind>(StringComparer.Ordinal)
            {
                ["sprk_project"] = RecordKind.Root,
                ["sprk_matter"] = RecordKind.Root,
                ["sprk_workassignment"] = RecordKind.Root,

                ["sprk_analysis"] = RecordKind.Intermediate,
                ["sprk_communication"] = RecordKind.Intermediate,
                ["sprk_document"] = RecordKind.Intermediate,
                ["sprk_event"] = RecordKind.Intermediate,
                ["sprk_invoice"] = RecordKind.Intermediate,
                ["sprk_todo"] = RecordKind.Intermediate,
                ["sprk_agreement"] = RecordKind.Intermediate,
                ["sprk_budget"] = RecordKind.Intermediate,
                ["sprk_reportcard"] = RecordKind.Intermediate,
                ["sprk_servicerequest"] = RecordKind.Intermediate,

                ["contact"] = RecordKind.Party,
                ["account"] = RecordKind.Party,
                ["sprk_organization"] = RecordKind.Party,
            };

        /// <summary>
        /// The regarding columns <c>sprk_todo</c> and <c>sprk_event</c> share whose target is a root or can hang off
        /// one. <c>sprk_regardingservicerequest</c> is here as an INTERMEDIATE (f3): a service request is a core record
        /// for ACCESS, but it cannot carry <c>sprk_issecure</c> and it hangs off a matter / project / work assignment
        /// — and because it is core, <see cref="CoreAncestorResolver"/> stamps nothing above it on the child.
        /// </summary>
        private static readonly (string Column, string Target)[] SharedRegardingLinks =
        [
            ("sprk_regardingproject", "sprk_project"),
            ("sprk_regardingmatter", "sprk_matter"),
            ("sprk_regardingworkassignment", "sprk_workassignment"),
            ("sprk_regardingservicerequest", "sprk_servicerequest"),
            ("sprk_regardinganalysis", "sprk_analysis"),
            ("sprk_regardingcommunication", "sprk_communication"),
            ("sprk_regardingevent", "sprk_event"),
            ("sprk_regardinginvoice", "sprk_invoice"),
            ("sprk_regardingagreement", "sprk_agreement"),
            ("sprk_regardingbudget", "sprk_budget"),
            ("sprk_regardingreportcard", "sprk_reportcard"),
        ];

        /// <summary>
        /// The typed PARTY regarding lookups (f5), from the f3 / f4 live sweeps. Read only on a row that carries the
        /// pair, and only for the pair's rule 3: the regarding builders write the typed regarding column and the pair id
        /// together, and the outbound sender writes NO type (<c>CommunicationService.MapAssociationFieldsAsync</c>), so
        /// for a party the typed column is the only thing on the row that says what the pair id is. sprk_invoice,
        /// sprk_workassignment and sprk_project have no regarding-party lookup (their party columns are assignees and
        /// vendors, which no builder pairs with the pair id), so they have none here.
        /// </summary>
        private static readonly (string Column, string Target)[] TodoPartyRegarding =
        [
            ("sprk_regardingcontact", "contact"), ("sprk_regardingorganization", "sprk_organization"),
        ];

        private static readonly (string Column, string Target)[] EventPartyRegarding =
        [
            ("sprk_regardingcontact", "contact"), ("sprk_regardingorganization", "sprk_organization"),
            ("sprk_regardingaccount", "account"),
        ];

        private static readonly (string Column, string Target)[] CommunicationPartyRegarding =
        [
            ("sprk_regardingperson", "contact"), ("sprk_regardingorganization", "sprk_organization"),
            ("sprk_regardingaccount", "account"),
        ];

        private static readonly IReadOnlyDictionary<string, ChildAncestorLinks> ByEntity =
            new Dictionary<string, ChildAncestorLinks>(StringComparer.Ordinal)
            {
                ["sprk_todo"] = new(polymorphic: true,
                    [.. SharedRegardingLinks, ("sprk_regardingdocument", "sprk_document")],
                    parties: TodoPartyRegarding),
                ["sprk_event"] = new(polymorphic: true, SharedRegardingLinks, parties: EventPartyRegarding),
                // Typed sprk_project / sprk_matter are its OWN root links; sprk_regardingagreement is an intermediate
                // (f3: the f2 table gave the invoice no intermediates at all, so an invoice regarding an agreement of a
                // SECURE matter resolved a shared container).
                ["sprk_invoice"] = new(polymorphic: true,
                [
                    ("sprk_project", "sprk_project"), ("sprk_matter", "sprk_matter"),
                    ("sprk_regardingagreement", "sprk_agreement"),
                ]),
                // A ROOT that is itself filed regarding a matter / project (live: 9 of 22 work assignments) or a
                // communication / event / invoice (live: 1). Its own sprk_issecure still decides first when it is the
                // record; as a root above a to-do / event / invoice its links are walked too (f4).
                ["sprk_workassignment"] = new(polymorphic: true,
                [
                    ("sprk_regardingproject", "sprk_project"), ("sprk_regardingmatter", "sprk_matter"),
                    ("sprk_regardingcommunication", "sprk_communication"), ("sprk_regardingevent", "sprk_event"),
                    ("sprk_regardinginvoice", "sprk_invoice"),
                ]),
                // A ROOT whose only root-capable column is the polymorphic pair (live: 0 projects carry it).
                // sprk_matter has a sprk_regardingrecordtype but NO sprk_regardingrecordid, so its row can name no
                // record and it has no entry.
                ["sprk_project"] = new(polymorphic: true, []),
                // A PARTY whose own sprk_invoice lookup names a record that can belong to a matter (live: 0 contacts
                // set it). Read and held rather than trusted as "a person is never under a matter" (f2's reading).
                ["contact"] = new(polymorphic: false, [("sprk_invoice", "sprk_invoice")]),
                // f4: the communication pipeline's own row (live sweep 2026-10-02, 24 lookups). Roots are followed;
                // service request / event / analysis / budget / report card are HELD (live: 1 event, 1 analysis);
                // the pair is read (live: 161 of 276). The INVOICE is FOLLOWED live — read for its own flag, container
                // and links — because that is what the communication path has done since task 155 r0 (it resolved
                // the invoice as a record), and an invoice's typed sprk_project / sprk_matter are its OWN links, not
                // a CoreAncestorResolver stamp. Not ownership, not read: sprk_regardingperson / organization /
                // account (party — not FOLLOWED; read for the pair's rule 3, f5); sprk_communicationthread (a GROUPING
                // whose anchor is COPIED from its messages' regarding — IThreadResolver — and which every message gets
                // by the 3-tier ladder, so holding on it would refuse every threaded message); sprk_triagecategory,
                // sprk_sentby and the system columns.
                ["sprk_communication"] = new(polymorphic: true,
                [
                    ("sprk_regardingproject", "sprk_project"), ("sprk_regardingmatter", "sprk_matter"),
                    ("sprk_regardingworkassignment", "sprk_workassignment"),
                    ("sprk_regardinginvoice", "sprk_invoice"),
                    ("sprk_regardingservicerequest", "sprk_servicerequest"), ("sprk_regardingevent", "sprk_event"),
                    ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingbudget", "sprk_budget"),
                    ("sprk_regardingreportcard", "sprk_reportcard"),
                ], followed: ["sprk_invoice"], parties: CommunicationPartyRegarding),
            };

        private readonly IReadOnlySet<string> _followedTargets;

        private ChildAncestorLinks(
            bool polymorphic,
            IReadOnlyList<(string Column, string Target)> links,
            IReadOnlyCollection<string>? followed = null,
            IReadOnlyList<(string Column, string Target)>? parties = null)
        {
            HasPolymorphicRegarding = polymorphic;
            _followedTargets = new HashSet<string>(followed ?? [], StringComparer.Ordinal);
            FollowedLinks = links
                .Where(l => Follows(l.Target))
                .Select(l => (l.Target, l.Column))
                .ToArray();
            IntermediateColumns = links
                .Where(l => !Follows(l.Target) && KindOf(l.Target) == RecordKind.Intermediate)
                .Select(l => l.Column)
                .ToArray();

            // A party column is only ever an identity witness for the pair, never an ownership link: a non-party target
            // here would let rule 3 swallow a pair that names a ROOT, so it is a defect in this table, not data.
            var partyLinks = parties ?? [];
            var notParty = partyLinks.Where(p => KindOf(p.Target) != RecordKind.Party).Select(p => p.Column).ToArray();
            if (notParty.Length > 0 || (partyLinks.Count > 0 && !polymorphic))
            {
                throw new ArgumentException(
                    "Party regarding columns must target a party and belong to a row that carries the polymorphic pair: "
                    + string.Join(", ", partyLinks.Select(p => p.Column)),
                    nameof(parties));
            }

            PartyRegardingColumns = partyLinks.Select(p => p.Column).ToArray();
        }

        /// <summary>
        /// Target entity → the column on this row that names it, for every link the walk FOLLOWS: each root link, and
        /// each link to a type this entry explicitly follows (the communication's invoice).
        /// </summary>
        public IReadOnlyList<(string Target, string LinkColumn)> FollowedLinks { get; }

        /// <summary>
        /// Columns that point at a record which ITSELF belongs (or can belong) to a root — another child (whose root
        /// is only a denormalized stamp here) or a root-owned non-child record (agreement, budget, report card,
        /// service request: no stamp at all). Any of them set means this row cannot answer "which root?", so the
        /// record is refused. These MUST ride on the row's read: Dataverse returns only the requested columns, so a
        /// column left out of <see cref="AllColumns"/> reads as "not set" and silently disables the refusal.
        /// </summary>
        public IReadOnlyList<string> IntermediateColumns { get; }

        /// <summary>The row carries the polymorphic regarding pair (<c>sprk_regardingrecordid</c> + type).</summary>
        public bool HasPolymorphicRegarding { get; }

        /// <summary>
        /// The row's typed PARTY regarding lookups (task 155 f5): read with the pair, used only by its rule 3 — an id
        /// equal to one of them names that party, which is not ownership. Never followed.
        /// </summary>
        public IReadOnlyList<string> PartyRegardingColumns { get; }

        /// <summary>Every column the row's read must carry for the decision.</summary>
        public IEnumerable<string> AllColumns => FollowedLinks.Select(l => l.LinkColumn)
            .Concat(IntermediateColumns)
            .Concat(HasPolymorphicRegarding
                ? PolymorphicColumns.Concat(PartyRegardingColumns)
                : Array.Empty<string>());

        /// <summary>
        /// Whether a link from this row to <paramref name="target"/> is walked: every root, plus a type this entry
        /// explicitly follows. Also decides the polymorphic pair's rule 5.
        /// </summary>
        public bool Follows(string target)
            => KindOf(target) == RecordKind.Root || _followedTargets.Contains(target);

        private static readonly string[] PolymorphicColumns = [RegardingRecordIdColumn, RegardingRecordTypeColumn];

        /// <summary>The links for <paramref name="entity"/>, or <see langword="null"/> when its row names no root.</summary>
        public static ChildAncestorLinks? For(string entity)
            => ByEntity.TryGetValue(entity, out var links) ? links : null;

        /// <summary>The kind of record <paramref name="entity"/> is, or <see langword="null"/> when it is not classified.</summary>
        internal static RecordKind? KindOf(string entity)
            => KindByEntity.TryGetValue(entity, out var kind) ? kind : null;

        /// <summary>The entities with known links — for the pin test.</summary>
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
