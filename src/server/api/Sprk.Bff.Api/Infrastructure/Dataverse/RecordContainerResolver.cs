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
/// <c>container_ancestor_ambiguous</c> (409: two different secure roots anywhere above it, a row's typed and
/// polymorphic regarding disagree, or an Office carrier's root differs from its direct link while a secure record is
/// involved), <c>container_ancestor_unverifiable</c> (409: a service request, or an intermediate on a record that
/// carries no copy), — task 156 — <c>container_ancestor_stale</c> (409: a child's copy of its intermediate's root
/// differs from that intermediate's LIVE root; the stale row is enqueued for re-stamping); and
/// <c>secure_flag_unreadable</c> (503, task 150: <c>sprk_issecure</c> came back absent on the record or a root above
/// it — the field-secured value was masked from this identity).</para>
///
/// <para>Registered <b>Scoped</b> and <b>unconditionally</b> (Program.cs, beside
/// <see cref="IDocumentStorageResolver"/>). Unconditional registration is deliberate: a feature-gated
/// isolation seam would be absent exactly when the gate is off, and an absent resolver means callers fall
/// back to the shared container — so there is no ADR-032 Null-Object question to answer here, because there
/// is no acceptable null object.</para>
/// </summary>
public sealed partial class RecordContainerResolver
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
    private readonly CoreAncestorRestampQueue? _restampQueue;

    /// <summary>
    /// <c>Communication:ArchiveContainerId</c> — the one shared container that is not a business unit's; the
    /// document-pointer check (task 166 r1 / r2) accepts it only on the communication-archive path.
    /// </summary>
    private readonly string? _archiveContainerId;

    /// <summary>
    /// The app-only Graph read of an item's creator — the document-pointer check's ITEM evidence (task 166 r2, owner
    /// round 23 item 1). Null outside a host that registers SharePoint Embedded access: the check then refuses (an
    /// item that cannot be verified is never followed).
    /// </summary>
    private readonly Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations? _speFiles;

    /// <summary>
    /// Every Entra application (client) id the BFF authenticates as — "the BFF identity" of owner round 23 item 1
    /// (task 166 r2). See <see cref="BffApplicationIdsFrom"/>.
    /// </summary>
    private readonly IReadOnlySet<Guid> _bffApplicationIds;

    /// <param name="restampQueue">
    /// Where a <see cref="AncestorStaleCode"/> refusal enqueues the stale row (task 156). Registered unconditionally
    /// beside <see cref="CoreAncestorResolver"/>, so production always has it. Optional only so the many tests that never
    /// reach a stale copy keep their construction; without it the refusal is unchanged and the reconciliation job repairs
    /// the copy within one cycle.
    /// </param>
    public RecordContainerResolver(
        ISecurableEntityRegistry securableEntities,
        IGenericEntityService entityService,
        ILogger<RecordContainerResolver> logger,
        CoreAncestorRestampQueue? restampQueue = null,
        Microsoft.Extensions.Options.IOptions<Sprk.Bff.Api.Configuration.CommunicationOptions>? communicationOptions = null,
        Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations? speFiles = null,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        _securableEntities = securableEntities ?? throw new ArgumentNullException(nameof(securableEntities));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _restampQueue = restampQueue;
        _archiveContainerId = communicationOptions?.Value?.ArchiveContainerId;
        _speFiles = speFiles;
        _bffApplicationIds = BffApplicationIdsFrom(configuration);
        _strictDerivedContainer = StrictDerivedContainerFrom(configuration);
        _itemIdBoundBackfillComplete = ItemIdBoundBackfillCompleteFrom(configuration);
        _unfiledDefaultContainerId = configuration?[UnfiledDefaultContainerKey];
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
            // ABSENT is not FALSE, and since task 150 it REFUSES (owner decision, recorded in the task 150 note: the
            // standing fail-closed directive, ADR-003). The invariant: sprk_issecure is FIELD-SECURED on every
            // securable root, this service's application user holds Read on it through the "Spaarke BFF-Managed Field
            // Writers" profile (and every user through the readers profile on each business unit's default team), every
            // row holds true or false (the one-time NULL backfill, scripts/Repair-SecureFlagNulls.ps1, and the column's
            // No default), and the standing assertion SecureFlagFieldSecurityAssertion checks the grants. Under that
            // invariant an absent value means this identity LOST its field-level Read — Dataverse returns the row with
            // a secured column masked out rather than failing — and reading that as "not secure" would route a secure
            // record's content to shared storage, which SPE cannot take back. So it is refused, never guessed.
            if (!record.Contains(SecurableEntityRegistry.SecureFlagAttribute))
            {
                throw SecureFlagUnreadable(normalizedEntity, recordId, normalizedEntity, recordId);
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
                + "container (POST /api/v1/external-access/provision-project) before uploading to it.",
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

    // The document-pointer check (task 166 r1 / r2) lives in RecordContainerResolver.DocumentPointer.cs — the same type,
    // split by reason-to-change (CLAUDE.md §11.5): it answers an identity-and-tenancy question about a row's pointer,
    // not a record's container placement.

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
    /// Problem code (task 156): the record — or a record above it — carries a COPY of the root of the record it is filed
    /// under, and that copy differs from the record's LIVE root. 409; the stale row is enqueued for re-stamping, so a retry
    /// succeeds once <see cref="CoreAncestorRestamper"/> has refreshed it. Never resolves any container.
    /// </summary>
    internal const string AncestorStaleCode = "container_ancestor_stale";

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
    /// <para><b>The walk is transitive (task 155 f4).</b> Every row the walk reads names the records above IT, through the
    /// same table the record's own row uses (<see cref="ChildAncestorLinks"/>), and those are read in turn:</para>
    /// <list type="bullet">
    /// <item>ANY secure root in the chain → the content goes to that root's own container (or refuses if it has none);
    /// never a shared container while a secure root is anywhere above.</item>
    /// <item>Two DIFFERENT secure roots anywhere in the chain → <see cref="AncestorAmbiguousCode"/>. The walk continues
    /// PAST a secure root to find out. The same record reached twice (a diamond) is one root, not two.</item>
    /// <item>An unreadable hop → <see cref="AncestorUnresolvedCode"/> 503 (retryable); a missing hop, a null row or a
    /// type the org does not know → 409.</item>
    /// <item>Bounded (<see cref="MaxRootChainDepth"/>, <see cref="MaxRootReads"/>) and cycle-guarded. A chain past the
    /// bound refuses, 409.</item>
    /// </list>
    ///
    /// <para><b>A child filed under another record (task 156 — replaces task 155's held branch).</b> When a to-do's
    /// regarding is a communication, event, document, invoice, analysis, agreement, budget or report card, its
    /// <c>sprk_regarding{core}</c> value is a COPY of that record's root (<see cref="CoreAncestorResolver"/>). Task 155
    /// refused every such upload (<c>container_ancestor_unverifiable</c>), because nothing refreshed the copy. The owner
    /// chose option (b) — re-stamp the children on re-file (<see cref="CoreAncestorRestamper"/>, plus a 5-minute
    /// reconciliation job for writes outside the BFF) — and this is the fail-closed guard for the window between such a
    /// write and the job:</para>
    /// <list type="bullet">
    /// <item>The record's row says which record its copy comes from (<see cref="CoreAncestorResolver.ClassifyStampSource"/>
    /// — the SAME rule the cascade and the job apply). That record is read LIVE, once, and its own root compared with the
    /// copy, for the root types it can carry.</item>
    /// <item>Equal → the copy is followed exactly like a direct root link.</item>
    /// <item>Different → <see cref="AncestorStaleCode"/> (409), nothing resolved, and the stale row is enqueued for
    /// re-stamping (<see cref="CoreAncestorRestampQueue"/>), so a retry a moment later succeeds.</item>
    /// <item>The record above is unreadable → 503; missing → 409. Never "no root".</item>
    /// <item><b>Transitive.</b> The record above is read with its own links, so if IT is filed under another record its
    /// copy is compared in turn — a stale copy anywhere on the chain refuses, and THAT row is enqueued. A record above
    /// that can itself be secure (an invoice) is a secure root in its own right.</item>
    /// <item><b>The Office carrier to-do</b> (the user's direct choice of project / matter, plus the document or email it
    /// was created from): the pair names the direct root, so nothing is a copy. Each carrier is read live: its root equal
    /// to the direct link (or naming no root) → resolves through the direct link; different and ANY root on either
    /// branch secure → <see cref="AncestorAmbiguousCode"/>; different and none secure → the direct link (the user chose
    /// it), i.e. the record's non-secure default.</item>
    /// </list>
    /// <para>Still held (<see cref="AncestorUnverifiableCode"/>), unchanged from task 155: a <c>sprk_servicerequest</c>
    /// (a core record that cannot carry <c>sprk_issecure</c>, so nothing is ever copied from it), and an intermediate
    /// column on a record that carries no copy at all — a work assignment, a project, a contact, or an invoice / document /
    /// agreement filed under another record (their typed roots are their OWN links, so there is nothing to compare).</para>
    ///
    /// <para><b>The polymorphic regarding pair</b> is read on every row that carries it: see
    /// <see cref="ResolvePolymorphicRegardingAsync"/>.</para>
    ///
    /// <para><b>Cost</b>: a to-do under a project or matter still costs one root read. A to-do under a communication costs
    /// one more read (the communication, carrying its flag when it is securable and its links); an Office carrier costs
    /// one read per carrier. Every classification is the registry's scope-memoized catalog.</para>
    /// </remarks>
    private async Task<ContainerDecision?> ResolveSecureAncestorAsync(
        Entity record,
        string normalizedEntity,
        Guid recordId,
        ChildAncestorLinks links,
        CancellationToken ct)
    {
        var walk = new Walk(normalizedEntity, recordId);

        // Level 0: the record's OWN row (already read by the caller) names the first hops. A held column here refuses
        // before any root is read; a copy is compared with its source here.
        var level0 = await NextHopsAsync(
                record, normalizedEntity, recordId, links, viaPrefix: null, depth: 0, walk, ct)
            .ConfigureAwait(false);
        var frontier = level0.All;

        for (var depth = 1; frontier.Count > 0; depth++)
        {
            var next = new List<RootHop>();

            foreach (var hop in frontier)
            {
                if (!walk.Visited.Add((hop.Entity, hop.Id)))
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

                ThrowIfPastBounds(walk, depth, hop, normalizedEntity, recordId);

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

                walk.Reads++;
                var row = await ReadHopAsync(hop, [.. columns], normalizedEntity, recordId, ct).ConfigureAwait(false);

                if (isSecurableHop)
                {
                    NoteSecureFlag(walk, row, hop, normalizedEntity, recordId);
                }

                if (hopLinks is not null)
                {
                    // Followed even when THIS hop is secure: a different secure root above it is an ambiguity (there
                    // is no single correct container), and the only way to see one is to keep walking.
                    var above = await NextHopsAsync(
                            row, hop.Entity, hop.Id, hopLinks, hop.Via, depth, walk, ct)
                        .ConfigureAwait(false);
                    next.AddRange(above.All);
                }
            }

            frontier = next;
        }

        var secureRoots = walk.SecureRoots;

        if (walk.CarrierDisagreement is { } carrierVia && secureRoots.Count > 0)
        {
            // Task 156, the Office carrier shape: the record names its root directly AND is filed under a document or
            // email whose own root is a DIFFERENT record — and something on one of the two branches is secure. Either
            // container is a guess that can put one root's content where the other root's members read it.
            _logger.LogError(
                "[SECURE-CONTAINER] {Entity} {RecordId} names its root directly but is also filed under a record whose root "
                + "differs ({Via}), and a SECURE record is involved [{Roots}]. Refusing ({Code}).",
                normalizedEntity, recordId, carrierVia,
                string.Join(", ", secureRoots.Select(r => $"{r.Hop.Entity}:{r.Hop.Id} via {r.Hop.Via}")),
                AncestorAmbiguousCode);

            throw new SdapProblemException(
                code: AncestorAmbiguousCode,
                title: "Ambiguous secure destination",
                detail: $"This {normalizedEntity} names its project, matter or work assignment directly, but the record "
                        + $"it was created from ({carrierVia}) belongs to a different one, and a secure record is involved. "
                        + "Its content has no single correct storage container.",
                statusCode: 409);
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

    /// <summary>The state of one resolution's walk: what was read, what is secure, what is being verified.</summary>
    private sealed class Walk
    {
        public Walk(string recordEntity, Guid recordId)
        {
            RecordEntity = recordEntity;
            RecordId = recordId;

            // The record itself is on the walk: a root that names it back is a cycle, not another root, and an
            // intermediate whose filing leads back to it is a loop.
            Visited.Add((recordEntity, recordId));
            InProgress.Add((recordEntity, recordId));
        }

        /// <summary>The record being resolved — refusals name it.</summary>
        public string RecordEntity { get; }

        public Guid RecordId { get; }

        /// <summary>Roots already read (and the record itself).</summary>
        public HashSet<(string Entity, Guid Id)> Visited { get; } = [];

        /// <summary>Intermediates being verified on the current path — reaching one again is a filing loop.</summary>
        public HashSet<(string Entity, Guid Id)> InProgress { get; } = [];

        /// <summary>Intermediates already read and verified, with their OWN roots (a diamond reads each once).</summary>
        public Dictionary<(string Entity, Guid Id), RowHops> Verified { get; } = [];

        public List<(RootHop Hop, string? Container)> SecureRoots { get; } = [];

        /// <summary>Rows read above the record (roots AND intermediates) — bounded by <see cref="MaxRootReads"/>.</summary>
        public int Reads { get; set; }

        /// <summary>Set when an Office-carrier row's carrier names a root its direct link does not (task 156).</summary>
        public string? CarrierDisagreement { get; set; }
    }

    /// <summary>
    /// What one row names above it: <see cref="Own"/> — its typed root links, the root a child filed under it copies
    /// (exactly <see cref="CoreAncestorResolver.IntermediateRootColumns"/>) — and <see cref="Extra"/> — every other record
    /// that must still be WALKED for a secure flag (the record its polymorphic pair alone names, a disagreeing carrier's
    /// roots), but is not part of the copy.
    /// </summary>
    private sealed record RowHops(List<RootHop> Own, List<RootHop> Extra)
    {
        public List<RootHop> All => [.. Own, .. Extra];
    }

    private void NoteSecureFlag(Walk walk, Entity row, RootHop hop, string normalizedEntity, Guid recordId)
    {
        if (!row.Contains(SecurableEntityRegistry.SecureFlagAttribute))
        {
            // Same posture as the record path since task 150: an ABSENT flag on a root above the record refuses. (Before
            // it, NULL flags were common — live dev 2026-10-01: 9 projects, 18 matters and 11 work assignments, every one
            // created before the column existed — and were read as non-secure. The one-time backfill sets them to No and
            // the column defaults to No, so absent now means the field-secured value was masked from this identity.)
            throw SecureFlagUnreadable(hop.Entity, hop.Id, normalizedEntity, recordId, hop.Via);
        }

        if (row.GetAttributeValue<bool>(SecurableEntityRegistry.SecureFlagAttribute))
        {
            walk.SecureRoots.Add((hop, row.GetAttributeValue<string>(ContainerColumn)));
        }
    }

    private void ThrowIfPastBounds(Walk walk, int depth, RootHop hop, string normalizedEntity, Guid recordId)
    {
        if (depth > MaxRootChainDepth || walk.Reads >= MaxRootReads)
        {
            throw AncestorUnresolved(
                normalizedEntity, recordId,
                "The chain of records it is filed under is longer than the storage resolver follows, so whether a secure "
                + "project, matter or work assignment sits above it cannot be determined.",
                statusCode: 409,
                logDetail: $"{hop.Via} -> {hop.Entity} {hop.Id} (depth {depth}, {walk.Reads} rows read; limits "
                           + $"{MaxRootChainDepth} deep, {MaxRootReads} rows)");
        }
    }

    /// <summary>
    /// The records one row names ABOVE it (task 155 f4; task 156): its root links that are set, the record its polymorphic
    /// pair alone names, and — when the row is filed under another record — that record's verified root (a copy compared
    /// with the source, a carrier read live). Used for the record's own row (level 0) and for every row the walk reads.
    /// </summary>
    /// <remarks>
    /// Every column here MUST ride on the row's read: Dataverse returns only the requested columns, so a column left out
    /// reads as "not set".
    /// </remarks>
    /// <param name="viaPrefix"><see langword="null"/> for the record's own row; the path that reached the row otherwise.</param>
    private async Task<RowHops> NextHopsAsync(
        Entity row,
        string rowEntity,
        Guid rowId,
        ChildAncestorLinks rowLinks,
        string? viaPrefix,
        int depth,
        Walk walk,
        CancellationToken ct)
    {
        var walkRecordEntity = walk.RecordEntity;
        var walkRecordId = walk.RecordId;

        var setIntermediates = SetLinks(row, rowLinks.IntermediateColumns);
        var setCarriers = SetLinks(row, rowLinks.CarrierColumns);

        // HELD (task 155, unchanged): a column whose record cannot be compared from this row — a service request (core,
        // never copied), or any intermediate on a row that carries no copy at all (a work assignment, a project, a
        // contact, an invoice / document / agreement filed under another record).
        var sourceColumns = CoreAncestorResolver.StampSourceColumns.TryGetValue(rowEntity, out var sources)
            ? sources.Select(s => s.Column).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var held = setIntermediates.Where(i => !sourceColumns.Contains(i.Column)).Select(i => i.Column).ToList();
        if (held.Count > 0)
        {
            throw Unverifiable(
                walkRecordEntity, walkRecordId, Via(viaPrefix, rowEntity, string.Join(", ", held)),
                heldBy: viaPrefix is null ? null : rowEntity);
        }

        var own = rowLinks.FollowedLinks
            .Select(l => (l.Target, l.LinkColumn,
                Id: row.GetAttributeValue<EntityReference>(l.LinkColumn)?.Id ?? Guid.Empty))
            .Where(l => l.Id != Guid.Empty)
            .Select(l => new RootHop(l.Target, l.Id, Via(viaPrefix, rowEntity, l.LinkColumn)))
            .ToList();
        var extra = new List<RootHop>();

        if (rowLinks.HasPolymorphicRegarding)
        {
            var named = await ResolvePolymorphicRegardingAsync(
                    row, rowEntity, rowLinks, viaPrefix, own, [.. setIntermediates, .. setCarriers],
                    walkRecordEntity, walkRecordId, ct)
                .ConfigureAwait(false);

            if (named is { } pairHop)
            {
                // The pair ALONE names this record (nothing typed is set): walked, but not part of the row's copy —
                // CoreAncestorResolver derives a copy from typed root columns only.
                extra.Add(pairHop);
            }
        }

        if (setIntermediates.Count == 0 && setCarriers.Count == 0)
        {
            return new RowHops(own, extra);
        }

        // Task 156: the row is filed under another record. Which one its copy comes from — the one rule the cascade and
        // the reconciliation job also apply.
        var carriers = new List<StampSourceLink>(setCarriers);
        var decision = CoreAncestorResolver.ClassifyStampSource(
            rowEntity, row, CoreAncestorResolver.PartyRegardingColumnNames(rowEntity));

        switch (decision.Kind)
        {
            case StampSourceKind.Source:
                {
                    var source = decision.Source!;
                    var sourceHops = await IntermediateRootsAsync(
                            source, rowEntity, viaPrefix, depth, walk, ct)
                        .ConfigureAwait(false);

                    // The copy is compared with the source's TYPED root (what CoreAncestorResolver derives); anything else
                    // the source names (its pair alone, a disagreeing carrier of its own) is walked for a secure flag.
                    var carriable = CoreAncestorResolver.CarriableRootTypes(source.Intermediate);
                    var copy = own.Where(h => carriable.Contains(h.Entity)).Select(h => (h.Entity, h.Id)).ToHashSet();
                    var live = sourceHops.Own.Where(h => carriable.Contains(h.Entity)).Select(h => (h.Entity, h.Id)).ToHashSet();
                    extra.AddRange(sourceHops.Extra);

                    if (!copy.SetEquals(live))
                    {
                        throw await StaleAsync(
                                walkRecordEntity, walkRecordId, rowEntity, rowId,
                                Via(viaPrefix, rowEntity, source.Column), copy, live,
                                heldBy: viaPrefix is null ? null : rowEntity, ct)
                            .ConfigureAwait(false);
                    }

                    carriers.AddRange(decision.Carriers);
                    break;
                }

            case StampSourceKind.DirectRootLink:
            case StampSourceKind.NotFiledUnderAnIntermediate:
                carriers.AddRange(decision.Carriers);
                break;

            default:
                // AmbiguousSource: more than one record it is filed under and nothing says which one. InconsistentPair is
                // refused by the pair rules above; reaching it here is the same answer.
                throw Ambiguous(
                    walkRecordEntity, walkRecordId,
                    Via(viaPrefix, rowEntity, string.Join(", ", decision.Carriers.Select(c => c.Column))),
                    viaPrefix is null
                        ? $"This {walkRecordEntity} is filed under more than one record and does not say which one it "
                          + "belongs to, so its content has no single correct storage container."
                        : $"The {rowEntity} this {walkRecordEntity} belongs to is filed under more than one record and does "
                          + "not say which one, so its content has no single correct storage container.");
        }

        // Carriers (the Office to-do's document / email; an analysis's input document; an intermediate the row is filed
        // under besides its copy's source): read live. A carrier naming no root, or only roots the row already names,
        // agrees. Otherwise its roots are walked too, and a secure record on either branch refuses (ResolveSecure...).
        var rowRoots = own.Select(h => (h.Entity, h.Id)).ToHashSet();
        foreach (var carrier in carriers)
        {
            var carrierHops = await IntermediateRootsAsync(carrier, rowEntity, viaPrefix, depth, walk, ct)
                .ConfigureAwait(false);

            var disagreeing = carrierHops.All.Where(h => !rowRoots.Contains((h.Entity, h.Id))).ToList();
            if (disagreeing.Count > 0)
            {
                walk.CarrierDisagreement ??= Via(viaPrefix, rowEntity, carrier.Column);
                extra.AddRange(disagreeing);
            }
        }

        return new RowHops(own, extra);
    }

    private static List<StampSourceLink> SetLinks(Entity row, IReadOnlyList<(string Column, string Target)> columns)
        => columns
            .Select(c => (c.Column, c.Target, Id: row.GetAttributeValue<EntityReference>(c.Column)?.Id ?? Guid.Empty))
            .Where(c => c.Id != Guid.Empty)
            .Select(c => new StampSourceLink(c.Column, c.Target, c.Id))
            .ToList();

    /// <summary>
    /// Read the record a row is filed under (an intermediate) LIVE, once, and return its OWN root — the root a child copies
    /// from it — after verifying, through the same rules, everything IT is filed under (task 156; transitive). When the
    /// record is itself securable and secure (an invoice), it is a secure root in its own right.
    /// </summary>
    private async Task<RowHops> IntermediateRootsAsync(
        StampSourceLink link, string rowEntity, string? rowVia, int depth, Walk walk, CancellationToken ct)
    {
        var key = (link.Intermediate, link.Id);
        if (walk.Verified.TryGetValue(key, out var verified))
        {
            return verified;
        }

        var via = Via(rowVia, rowEntity, link.Column);
        var hop = new RootHop(link.Intermediate, link.Id, via);

        if (walk.InProgress.Contains(key))
        {
            throw AncestorUnresolved(
                walk.RecordEntity, walk.RecordId,
                "The records it is filed under lead back to themselves, so none of them says which project, matter or "
                + "work assignment it belongs to.",
                statusCode: 409,
                logDetail: $"{via} -> {link.Intermediate} {link.Id} (filing loop)");
        }

        var links = ChildAncestorLinks.For(link.Intermediate);
        var classification = await _securableEntities.ClassifyEntityAsync(link.Intermediate, ct).ConfigureAwait(false);

        if (links is null || classification is not (EntitySecurability.NotSecurable or EntitySecurability.Securable))
        {
            throw AncestorUnresolved(
                walk.RecordEntity, walk.RecordId,
                $"It is filed under a '{link.Intermediate}', whose own project, matter or work assignment the storage "
                + "resolver cannot read, so whether it belongs to a secure record cannot be determined.",
                statusCode: 409,
                logDetail: $"{via} -> {link.Intermediate} {link.Id} (classified {(int)classification}, links {(links is null ? "unknown" : "known")})");
        }

        ThrowIfPastBounds(walk, depth + 1, hop, walk.RecordEntity, walk.RecordId);

        var securable = classification == EntitySecurability.Securable;
        var columns = new List<string>(links.AllColumns);
        if (securable)
        {
            columns.Add(SecurableEntityRegistry.SecureFlagAttribute);
            columns.Add(ContainerColumn);
        }

        walk.Reads++;
        var row = await ReadHopAsync(hop, [.. columns], walk.RecordEntity, walk.RecordId, ct).ConfigureAwait(false);

        if (securable)
        {
            NoteSecureFlag(walk, row, hop, walk.RecordEntity, walk.RecordId);
        }

        walk.InProgress.Add(key);
        RowHops above;
        try
        {
            above = await NextHopsAsync(row, link.Intermediate, link.Id, links, via, depth + 1, walk, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            walk.InProgress.Remove(key);
        }

        // Two typed roots of one type naming DIFFERENT records (a document's sprk_matter and sprk_relatedmatter): the
        // root a child copies is not known — CoreAncestorResolver refuses to derive it, so no copy could ever match.
        var conflicting = above.Own
            .GroupBy(h => h.Entity, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Select(h => h.Id).Distinct().Count() > 1);
        if (conflicting is not null)
        {
            throw Ambiguous(
                walk.RecordEntity, walk.RecordId, via,
                $"The {link.Intermediate} it is filed under names two different {conflicting.Key} records, so its content "
                + "has no single correct storage container.");
        }

        // The caller walks both: Own as the row's copy (or a carrier's root), Extra for a secure flag only.
        walk.Verified[key] = above;
        return above;
    }

    /// <summary>
    /// The stale-copy refusal (task 156): <paramref name="staleEntity"/>'s copy of its source's root
    /// (<paramref name="copy"/>) differs from that source's live root (<paramref name="live"/>). The stale row is enqueued
    /// for re-stamping first; nothing is resolved. 409 — the user's retry succeeds once the re-stamp lands.
    /// </summary>
    private async Task<SdapProblemException> StaleAsync(
        string normalizedEntity,
        Guid recordId,
        string staleEntity,
        Guid staleId,
        string via,
        IReadOnlySet<(string Entity, Guid Id)> copy,
        IReadOnlySet<(string Entity, Guid Id)> live,
        string? heldBy,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: {Stale} {StaleId}'s copy of the root of the record it is "
            + "filed under ({Via}) is [{Copy}] but that record's LIVE root is [{Live}]. The copy is stale; enqueuing a "
            + "re-stamp of {Stale} {StaleId} ({Code}).",
            normalizedEntity, recordId, staleEntity, staleId, via,
            string.Join(", ", copy.Select(c => $"{c.Entity}:{c.Id}")),
            string.Join(", ", live.Select(c => $"{c.Entity}:{c.Id}")),
            staleEntity, staleId, AncestorStaleCode);

        if (_restampQueue is not null)
        {
            // Best effort and never thrown: the refusal stands either way, and the reconciliation job repairs the copy
            // within one cycle if the enqueue did not land.
            await _restampQueue.EnqueueAsync(staleEntity, staleId, ct).ConfigureAwait(false);
        }

        var detail = heldBy is null
            ? $"This {normalizedEntity}'s project, matter or work assignment is a copy taken from the record it is filed "
              + $"under ({via}), and that record has since moved, so the copy is out of date. It is being refreshed — try "
              + "again in a minute. Its content is not stored anywhere until then."
            : $"This {normalizedEntity} belongs to a {heldBy} whose project, matter or work assignment is a copy taken from "
              + $"the record it is filed under ({via}), and that record has since moved. It is being refreshed — try again "
              + "in a minute. Its content is not stored anywhere until then.";

        return new SdapProblemException(
            code: AncestorStaleCode,
            title: "Storage location is being refreshed",
            detail: detail,
            statusCode: 409);
    }

    private SdapProblemException Ambiguous(string normalizedEntity, Guid recordId, string via, string detail)
    {
        _logger.LogError(
            "[SECURE-CONTAINER] REFUSED {Entity} {RecordId}: {Detail} ({Via}; {Code}).",
            normalizedEntity, recordId, detail, via, AncestorAmbiguousCode);

        return new SdapProblemException(
            code: AncestorAmbiguousCode,
            title: "Ambiguous secure destination",
            detail: detail,
            statusCode: 409);
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

    /// <summary>
    /// Problem code (task 150): <c>sprk_issecure</c> came back ABSENT on a securable root — the record itself, or one
    /// above it — so whether its content belongs in a secure container cannot be told.
    /// </summary>
    internal const string SecureFlagUnreadableCode = "secure_flag_unreadable";

    /// <summary>
    /// The task 150 refusal for an ABSENT <c>sprk_issecure</c>. 503: the cause is this identity's field-level Read on
    /// the column (an administrator restores it), and an upload retried once it is restored succeeds.
    /// </summary>
    /// <param name="flagEntity">The securable root whose flag was absent — the record itself, or a root above it.</param>
    /// <param name="flagRecordId">That root's id: logged for the operator, never returned to the caller.</param>
    /// <param name="via">The column path to that root, when it is above the record.</param>
    private SdapProblemException SecureFlagUnreadable(
        string flagEntity, Guid flagRecordId, string normalizedEntity, Guid recordId, string? via = null)
    {
        _logger.LogError(
            "[SECURE-CONTAINER] REFUSED {Entity} {RecordId} ({Code}): '{Attribute}' was ABSENT on {FlagEntity} {FlagRecordId}"
            + "{Via}. Every securable row holds true or false once the NULL backfill has run, so absent means this "
            + "service cannot READ the field-secured column — its application user has lost the field security profile "
            + "(scripts/Set-SecureFlagFieldSecurity.ps1 -Verify; SecureFlagFieldSecurityAssertion). Refusing rather than "
            + "reading it as not secure, which would put a secure record's content in shared storage.",
            normalizedEntity, recordId, SecureFlagUnreadableCode, SecurableEntityRegistry.SecureFlagAttribute,
            flagEntity, flagRecordId, via is null ? string.Empty : $" (above it, via {via})");

        return new SdapProblemException(
            code: SecureFlagUnreadableCode,
            title: "Cannot resolve a storage container",
            detail: (via is null
                        ? $"Whether this {normalizedEntity} is secure could not be read"
                        : $"Whether the {flagEntity} this {normalizedEntity} belongs to is secure could not be read")
                    + ", so its content is not stored in a shared container on a guess. An administrator needs to check "
                    + "the secure-record setup; uploading again afterwards will work.",
            statusCode: 503);
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
    /// The held-path refusal: the record — or a record above it — is filed under another record that belongs to a root,
    /// and nothing on that row can be compared with it. Since task 156 that is only a service request (a core record that
    /// cannot carry <c>sprk_issecure</c>, from which nothing is ever copied) or an intermediate on a row that carries no
    /// copy at all (a work assignment, project, contact, invoice, document or agreement filed under another record); a
    /// to-do / event / communication / analysis filed under an intermediate is compared live instead
    /// (<see cref="AncestorStaleCode"/>).
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
            + "project/matter/work assignment ({Via}), and that row carries no copy of its root to compare (a service "
            + "request, or a row that is not a to-do / event / communication / analysis). It cannot be verified that it "
            + "does not sit under a SECURE record ({Code}).",
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
    /// is filed under → refused as ambiguous (<see cref="AncestorAmbiguousCode"/>, 409). Neither is picked. Since task
    /// 156 the same holds for the intermediates and carriers set on the row: the pair naming one of them is agreement by
    /// identity (it is the record the row is filed under — the copy's source), and naming none of them while they are
    /// set is a disagreement.</item>
    /// <item>The pair is the only thing naming a record → its type decides, read from <c>sprk_recordtype_ref</c>: a
    /// type the row's entry FOLLOWS (a root; the communication's invoice) is returned and walked like a typed link (a
    /// record that does not exist refuses there, 409); an INTERMEDIATE is the held path
    /// (<see cref="AncestorUnverifiableCode"/>); a PARTY (contact, account, organization) is not ownership and adds
    /// nothing, exactly like the typed party columns; a missing type, an unreadable type row (503) or a type this
    /// table does not classify → refused (<see cref="AncestorUnresolvedCode"/>).</item>
    /// </list>
    /// </remarks>
    /// <param name="followedHops">The followed links already set on the same row (rules 3 and 4).</param>
    /// <param name="typedOthers">
    /// The row's other typed links that are set — the intermediates it is filed under and its carriers (task 156). The pair
    /// naming one of them is agreement by identity (rule 3); naming none of them while they are set is a disagreement
    /// (rule 4), exactly like a followed link.
    /// </param>
    private async Task<RootHop?> ResolvePolymorphicRegardingAsync(
        Entity row,
        string rowEntity,
        ChildAncestorLinks rowLinks,
        string? viaPrefix,
        IReadOnlyList<RootHop> followedHops,
        IReadOnlyList<StampSourceLink> typedOthers,
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
        if (followedHops.Any(h => h.Id == pairId)
            || typedOthers.Any(o => o.Id == pairId)
            || NamesTypedParty(row, rowLinks, pairId))
        {
            return null;
        }

        if (followedHops.Count > 0 || typedOthers.Count > 0)
        {
            var typed = string.Join(", ", followedHops.Select(h => $"{h.Via} -> {h.Entity}:{h.Id}")
                .Concat(typedOthers.Select(o => $"{Via(viaPrefix, rowEntity, o.Column)} -> {o.Intermediate}:{o.Id}")));

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
    /// How each record type this resolver can be asked about — or can READ above a record — names its ROOT (project /
    /// matter / work assignment), directly or through another record. From the task 155 f3 live metadata sweep, extended
    /// in f4 to <c>sprk_communication</c> and in task 156 to the five intermediates a child can be filed under that f3
    /// classified but did not describe (<c>sprk_analysis</c>, <c>sprk_document</c>, <c>sprk_agreement</c>,
    /// <c>sprk_budget</c>, <c>sprk_reportcard</c>; spaarkedev1, read-only, 2026-10-02).
    /// </summary>
    /// <remarks>
    /// <para><b>The sweep.</b> For every entity in this table, EVERY Lookup / Customer / Owner column was enumerated with
    /// its targets, and every target was classified by its OWN lookups. The tables are in
    /// <c>notes/task-155-child-record-container-resolution.md</c> "Round f3" / "Round f4" and
    /// <c>notes/task-156-stamp-freshness.md</c>; the literal list is pinned by
    /// <c>ChildRecordContainerResolutionTests.ChildRecordRead_RequestsEveryLinkAndIntermediateColumn</c>.</para>
    ///
    /// <para><b>The rule.</b> Every column whose target IS a root is FOLLOWED (read, flag- and container-checked, and its
    /// own row walked in turn — f4). Every column whose target can HANG OFF a root (<see cref="RecordKind.Intermediate"/>)
    /// is, since task 156, either the record the row's COPY comes from — read live and compared — or a CARRIER — read
    /// live and checked for a disagreeing root — on a table that carries a copy
    /// (<see cref="CoreAncestorResolver.StampSourceColumns"/>), and the HELD path everywhere else (a service request; any
    /// intermediate on a work assignment, project, contact, invoice, document or agreement). Party targets and reference /
    /// principal / grouping targets are not ownership and are not FOLLOWED. A <c>sprk_regardingrecordid</c> column means
    /// the row carries the polymorphic pair, which is read too — with the row's typed PARTY REGARDING columns
    /// (<see cref="PartyRegardingColumns"/>, f5) for the pair's rule 3 only.</para>
    ///
    /// <para><b>The same table serves every row the walk reads</b>: the record's own row, each root above it and each
    /// intermediate it is filed under. A root type with no entry (<c>sprk_matter</c>) names nothing above itself.</para>
    ///
    /// <para><b>Lock-step with <see cref="CoreAncestorResolver"/></b> (task 156): for every intermediate, its followed root
    /// columns here ARE <see cref="CoreAncestorResolver.IntermediateRootColumns"/> (bar the service request, which is held
    /// here), and on every table that carries a copy its non-held intermediate columns ARE
    /// <see cref="CoreAncestorResolver.StampSourceColumns"/> — so the copy a child is compared with is the copy the
    /// restamper writes. Pinned by <c>CoreAncestorStampTopologyLockstepTests</c>.</para>
    /// </remarks>
    internal sealed class ChildAncestorLinks
    {
        /// <summary>What a record of a given type is, for the question "can it be or hang off a root?".</summary>
        internal enum RecordKind
        {
            /// <summary>A project, matter or work assignment: it carries <c>sprk_issecure</c> and its own container.</summary>
            Root,

            /// <summary>A record that itself has (or can have) a root.</summary>
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
        /// the CHILD taxonomy; agreement and report card carry <c>sprk_regardingmatter</c> / <c>sprk_regardingproject</c>;
        /// budget carries typed <c>sprk_matter</c> / <c>sprk_project</c>; service request carries
        /// <c>sprk_regarding{matter,project,workassignment}</c> and cannot carry <c>sprk_issecure</c> itself; an OOB
        /// <c>email</c> activity (a document's <c>sprk_email</c>, task 156) carries a <c>regardingobjectid</c> that can name
        /// a matter. Party: contact, account and sprk_organization have no lookup to a root.
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
                ["sprk_memo"] = RecordKind.Intermediate,
                ["sprk_agreement"] = RecordKind.Intermediate,
                ["sprk_budget"] = RecordKind.Intermediate,
                ["sprk_reportcard"] = RecordKind.Intermediate,
                ["sprk_servicerequest"] = RecordKind.Intermediate,
                ["email"] = RecordKind.Intermediate,

                ["contact"] = RecordKind.Party,
                ["account"] = RecordKind.Party,
                ["sprk_organization"] = RecordKind.Party,
                // Task 147 r1: a memo's sprk_regardingtimekeeper. A timekeeper is a person (a biller on an invoice line,
                // live 2026-10-04: lookups to contact, invoice and invoice line), like a contact, which also carries an
                // sprk_invoice lookup and is a party.
                ["sprk_timekeeper"] = RecordKind.Party,
            };

        /// <summary>
        /// The regarding columns <c>sprk_todo</c> and <c>sprk_event</c> share whose target is a root or can hang off
        /// one. <c>sprk_regardingservicerequest</c> is here as an INTERMEDIATE (f3): a service request is a core record
        /// for ACCESS, but it cannot carry <c>sprk_issecure</c> and it hangs off a matter / project / work assignment —
        /// and because it is core, nothing is copied from it, so it stays HELD (task 156).
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

        private static readonly IReadOnlyDictionary<string, ChildAncestorLinks> ByEntity =
            new Dictionary<string, ChildAncestorLinks>(StringComparer.Ordinal)
            {
                ["sprk_todo"] = new(polymorphic: true,
                    [.. SharedRegardingLinks, ("sprk_regardingdocument", "sprk_document")],
                    parties: CoreAncestorResolver.PartyRegardingColumns["sprk_todo"]),
                ["sprk_event"] = new(polymorphic: true, SharedRegardingLinks,
                    parties: CoreAncestorResolver.PartyRegardingColumns["sprk_event"]),
                // Task 147 r1 (live sweep of sprk_memo, read-only, 2026-10-04: 37 columns, 15 sprk_ lookups plus the pair).
                // The memo joined the CHILD taxonomy (owner round 2 item 6), so the resolver must READ it: until this
                // entry a memo was a child with unknown links and every container resolution of one refused 409
                // (verifier item 2). Its links are the to-do's, except that its report-card lookup is named
                // sprk_reportcard. Parties: contact, organization and timekeeper. sprk_memo is in StampSourceColumns, so
                // its intermediates are compared live like a to-do's (CoreAncestorStampTopologyLockstepTests).
                ["sprk_memo"] = new(polymorphic: true,
                [
                    ("sprk_regardingproject", "sprk_project"), ("sprk_regardingmatter", "sprk_matter"),
                    ("sprk_regardingworkassignment", "sprk_workassignment"),
                    ("sprk_regardingservicerequest", "sprk_servicerequest"),
                    ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingcommunication", "sprk_communication"),
                    ("sprk_regardingdocument", "sprk_document"), ("sprk_regardingevent", "sprk_event"),
                    ("sprk_regardinginvoice", "sprk_invoice"), ("sprk_regardingagreement", "sprk_agreement"),
                    ("sprk_regardingbudget", "sprk_budget"), ("sprk_reportcard", "sprk_reportcard"),
                ], parties: CoreAncestorResolver.PartyRegardingColumns["sprk_memo"]),
                // Typed sprk_project / sprk_matter are its OWN root links; sprk_regardingagreement is an intermediate
                // (f3). An invoice carries no copy, so a set sprk_regardingagreement stays HELD (task 156). Task 150 (owner
                // round 10 item 11): these links are the ONLY thing that decides an invoice — its own sprk_issecure is not
                // a security input (SecurableEntityRegistry.FlagIsNotASecurityInput), so it is never read for a flag or a
                // container.
                ["sprk_invoice"] = new(polymorphic: true,
                [
                    ("sprk_project", "sprk_project"), ("sprk_matter", "sprk_matter"),
                    ("sprk_regardingagreement", "sprk_agreement"),
                ]),
                // A ROOT that is itself filed regarding a matter / project (live: 9 of 22 work assignments) or a
                // communication / event / invoice (live: 1, held — a work assignment carries no copy).
                ["sprk_workassignment"] = new(polymorphic: true,
                [
                    ("sprk_regardingproject", "sprk_project"), ("sprk_regardingmatter", "sprk_matter"),
                    ("sprk_regardingcommunication", "sprk_communication"), ("sprk_regardingevent", "sprk_event"),
                    ("sprk_regardinginvoice", "sprk_invoice"),
                ]),
                // A ROOT whose only root-capable column is the polymorphic pair (live: 0 projects carry it).
                // sprk_matter has a sprk_regardingrecordtype but NO sprk_regardingrecordid column, so its row can name no
                // record and it has no entry.
                ["sprk_project"] = new(polymorphic: true, []),
                // A PARTY whose own sprk_invoice lookup names a record that can belong to a matter (live: 0 contacts set
                // it). Read and held (f3, interpretation iv).
                ["contact"] = new(polymorphic: false, [("sprk_invoice", "sprk_invoice")]),
                // f4: the communication pipeline's own row (24 lookups, live 2026-10-02). Task 156: its INVOICE is no
                // longer followed as if it were a root (f4 interpretation viii) — it is what the communication's copy
                // comes from, compared live like every other intermediate, and, being securable, a secure root in its
                // own right when its own flag is set. Service request stays held.
                ["sprk_communication"] = new(polymorphic: true,
                [
                    ("sprk_regardingproject", "sprk_project"), ("sprk_regardingmatter", "sprk_matter"),
                    ("sprk_regardingworkassignment", "sprk_workassignment"),
                    ("sprk_regardinginvoice", "sprk_invoice"),
                    ("sprk_regardingservicerequest", "sprk_servicerequest"), ("sprk_regardingevent", "sprk_event"),
                    ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingbudget", "sprk_budget"),
                    ("sprk_regardingreportcard", "sprk_reportcard"),
                ], parties: CoreAncestorResolver.PartyRegardingColumns["sprk_communication"]),
                // Task 156 (live sweep 2026-10-02, 36 lookups). Its copy comes from budget / communication / document /
                // invoice; its NOT NULL sprk_documentid (the document it ANALYSES) and sprk_outputfileid (the document it
                // produced) are read as CARRIERS — a root either names that the analysis does not, with a secure record
                // anywhere involved, refuses. Not ownership: sprk_actionid / sprk_playbook / sprk_agreementtype
                // (configuration), sprk_assigned* (parties), sprk_reviewerby and the system columns.
                ["sprk_analysis"] = new(polymorphic: true,
                [
                    ("sprk_regardingproject", "sprk_project"), ("sprk_regardingmatter", "sprk_matter"),
                    ("sprk_regardingworkassignment", "sprk_workassignment"),
                    ("sprk_regardingservicerequest", "sprk_servicerequest"),
                    ("sprk_regardingbudget", "sprk_budget"), ("sprk_regardingcommunication", "sprk_communication"),
                    ("sprk_regardingdocument", "sprk_document"), ("sprk_regardinginvoice", "sprk_invoice"),
                ], carriers: [("sprk_documentid", "sprk_document"), ("sprk_outputfileid", "sprk_document")]),
                // Task 156 (live sweep 2026-10-02, 50 lookups). Its record links are the CANONICAL document link
                // vocabulary (Spaarke.Dataverse.DocumentLinkFields — the one declaration, guarded by the ArchTests
                // DocumentLinkVocabularyGuardTests), classified by target through KindByEntity: a link to a root (typed
                // matter / project / work assignment and their related twins — 0 live rows set a twin) is FOLLOWED; a
                // link to an intermediate (the invoice pair, the related communication / event / agreement / service
                // request / to-do, the email it came from) is HELD — a document carries no copy, so nothing on it can be
                // compared. EXCLUDED, by name: the vocabulary's PARTY links (the related contact / organization / vendor
                // organization — referenced, never an owner of content); an unclassified target throws (constructor).
                // ADDED: the two document-to-document links the vocabulary does not hold — the email-attachment parent
                // and the canonical document it is a byte-identical copy of (the same content, so its filing matters
                // too). 131 of 531 live documents set an intermediate; 0 to-dos and 0 analyses are filed under a
                // document. The pair is an id with NO type column on this table: an id that is not one of its typed
                // links refuses.
                ["sprk_document"] = new(polymorphic: true,
                [
                    .. DocumentLinkFields.All
                        .Where(f => KindOf(f.TargetEntityLogicalName) != RecordKind.Party)
                        .Select(f => (f.LogicalName, f.TargetEntityLogicalName)),
                    ("sprk_parentdocument", "sprk_document"), ("sprk_canonicaldocument", "sprk_document"),
                ], pairType: false),
                // Task 156 (live 2026-10-02): roots sprk_regardingmatter / sprk_regardingproject; sprk_regardingdocument is
                // held (an agreement carries no copy; 0 live agreements set it). No pair.
                ["sprk_agreement"] = new(polymorphic: false,
                [
                    ("sprk_regardingmatter", "sprk_matter"), ("sprk_regardingproject", "sprk_project"),
                    ("sprk_regardingdocument", "sprk_document"),
                ]),
                // Task 156 (live 2026-10-02): typed sprk_matter / sprk_project only. No pair.
                ["sprk_budget"] = new(polymorphic: false,
                [
                    ("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project"),
                ]),
                // Task 156 (live 2026-10-02): sprk_regardingmatter / sprk_regardingproject and the pair (all 3 live report
                // cards set the pair to their typed matter). Its assignees and law firms are parties, not regardings.
                ["sprk_reportcard"] = new(polymorphic: true,
                [
                    ("sprk_regardingmatter", "sprk_matter"), ("sprk_regardingproject", "sprk_project"),
                ]),
            };

        private readonly IReadOnlySet<string> _followedTargets;

        private ChildAncestorLinks(
            bool polymorphic,
            IReadOnlyList<(string Column, string Target)> links,
            IReadOnlyCollection<string>? followed = null,
            IReadOnlyList<(string Column, string Target)>? parties = null,
            IReadOnlyList<(string Column, string Target)>? carriers = null,
            bool pairType = true)
        {
            // A link to a type KindByEntity does not classify would be neither followed nor held — silently ignored, which
            // is fail OPEN. It is a defect in this table (or a new column in the shared document link vocabulary), so it
            // stops the type from loading rather than being dropped.
            var unclassified = links.Where(l => KindOf(l.Target) is null).Select(l => $"{l.Column} -> {l.Target}").ToArray();
            if (unclassified.Length > 0)
            {
                throw new ArgumentException(
                    "Link columns must target a classified record type (KindByEntity): " + string.Join(", ", unclassified),
                    nameof(links));
            }

            HasPolymorphicRegarding = polymorphic;
            HasPairTypeColumn = polymorphic && pairType;
            _followedTargets = new HashSet<string>(followed ?? [], StringComparer.Ordinal);
            FollowedLinks = links
                .Where(l => Follows(l.Target))
                .Select(l => (l.Target, l.Column))
                .ToArray();
            IntermediateColumns = links
                .Where(l => !Follows(l.Target) && KindOf(l.Target) == RecordKind.Intermediate)
                .ToArray();

            // A carrier is always read live and never a copy's source: it must target an intermediate.
            var carrierLinks = carriers ?? [];
            if (carrierLinks.Any(c => KindOf(c.Target) != RecordKind.Intermediate))
            {
                throw new ArgumentException(
                    "Carrier columns must target an intermediate: " + string.Join(", ", carrierLinks.Select(c => c.Column)),
                    nameof(carriers));
            }

            CarrierColumns = carrierLinks.ToArray();

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
        /// each link to a type this entry explicitly follows (none since task 156).
        /// </summary>
        public IReadOnlyList<(string Target, string LinkColumn)> FollowedLinks { get; }

        /// <summary>
        /// Columns that point at a record which ITSELF belongs (or can belong) to a root. On a table that carries a copy
        /// (<see cref="CoreAncestorResolver.StampSourceColumns"/>) one of them is the copy's source (compared live) and the
        /// rest are carriers (read live); anywhere else, and for a service request, one set means the row cannot answer
        /// "which root?" and is HELD. These MUST ride on the row's read: Dataverse returns only the requested columns, so
        /// a column left out of <see cref="AllColumns"/> reads as "not set" and silently disables the check.
        /// </summary>
        public IReadOnlyList<(string Column, string Target)> IntermediateColumns { get; }

        /// <summary>
        /// Columns that are ALWAYS carriers (task 156): read live, never a copy's source, never held — an analysis's
        /// NOT NULL <c>sprk_documentid</c>, the document it analyses.
        /// </summary>
        public IReadOnlyList<(string Column, string Target)> CarrierColumns { get; }

        /// <summary>The row carries the polymorphic regarding pair's id (<c>sprk_regardingrecordid</c>).</summary>
        public bool HasPolymorphicRegarding { get; }

        /// <summary>The row also carries the pair's TYPE column (<c>sprk_document</c> does not — live 2026-10-02).</summary>
        public bool HasPairTypeColumn { get; }

        /// <summary>
        /// The row's typed PARTY regarding lookups (task 155 f5): read with the pair, used only by its rule 3 — an id
        /// equal to one of them names that party, which is not ownership. Never followed.
        /// </summary>
        public IReadOnlyList<string> PartyRegardingColumns { get; }

        /// <summary>Every column the row's read must carry for the decision.</summary>
        public IEnumerable<string> AllColumns => FollowedLinks.Select(l => l.LinkColumn)
            .Concat(IntermediateColumns.Select(i => i.Column))
            .Concat(CarrierColumns.Select(c => c.Column))
            .Concat(HasPolymorphicRegarding
                ? PairColumns().Concat(PartyRegardingColumns)
                : Array.Empty<string>());

        private IEnumerable<string> PairColumns() => HasPairTypeColumn
            ? [RegardingRecordIdColumn, RegardingRecordTypeColumn]
            : [RegardingRecordIdColumn];

        /// <summary>
        /// Whether a link from this row to <paramref name="target"/> is walked: every root, plus a type this entry
        /// explicitly follows. Also decides the polymorphic pair's rule 5.
        /// </summary>
        public bool Follows(string target)
            => KindOf(target) == RecordKind.Root || _followedTargets.Contains(target);

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
            // Task 171 (adversarial finding 2): the eligibility columns ride on the same read.
            ColumnSet = new ColumnSet(
                BusinessUnitLookupColumn, "domainname", "isdisabled", "accessmode", "applicationid", "sprk_isexternal"),
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

        // Task 171 (owner round 70, adversarial finding 2): content with no record is written APP-ONLY into the acting
        // user's BUSINESS-UNIT container, whose writers are exactly the round-70 population — an enabled, internal
        // person (blank sprk_isexternal is internal, round 67). The same rule SpeContainerMembershipSync keeps the
        // container's standing writers by; anyone else would be writing into a container they are not a member of.
        if (!Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync.IsStandingEligible(rows[0]))
        {
            _logger.LogWarning(
                "[SECURE-CONTAINER] The Dataverse user for caller oid {Oid} is not an enabled internal person (disabled, "
                + "not a person, or flagged external); record-less content is refused.",
                oid);

            throw new SdapProblemException(
                code: "acting_user_not_eligible",
                title: "Cannot store content without a record",
                detail: "Content that is not attached to a record is stored in your business unit's shared container, "
                        + "which only enabled internal users may write to. Attach the content to a record you have access to.",
                statusCode: 403);
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
                        // secure_flag_unreadable refusal in ResolveForRecordAsync fails closed on, task 150). Excluding them
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
    /// exist" misdiagnoses precisely the masked-attribute case the secure_flag_unreadable refusal (task 150)
    /// fails closed on.
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
