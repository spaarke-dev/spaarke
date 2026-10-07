// spaarkeai-word-add-in-r1 task 080 — record ownership assignment.
//
// Owner decision 2026-09-22: "the records should be owned by the acting user's BU default owner team.
// So if testuser1 is in spaarke business unit 1 team/business unit, then the record they create is
// assigned to that team (not the user)."
//
// REFINED 2026-09-25, same owner, after they asked whether a BFF-created record even HAS an accurate
// "user BU": the source is the TARGET record's business unit first, the acting user's only as a fallback.
// Two reasons, both verified — some BFF creates have no acting user at all (EmailAttachmentProcessor,
// inbound email), and where there is one their BU is often the wrong scope (a root-BU user filing to a
// child-BU matter would put the document where the matter's own team cannot see it). This matches
// RecordContainerResolver's already-sanctioned order for SPE containers. Nothing about the team-ownership
// convention changed; only where the business unit is read from.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — nothing resolves "business unit → default owner team". The two neighbours both do
//       something else: UserOrgContextReader (Services/Ai/Context/) reads BU and team NAMES to bind AI
//       prompt context, and CommunicationEnrichmentService.ResolveTeamIdByNameAsync (:738) resolves a team
//       by NAME from an email-category routing gate. Neither answers "which team owns what this caller
//       creates".
//   (2) Extension — No. UserOrgContextReader is ADR-013 in-zone AI code; having CRUD create paths inject it
//       would be precisely the CRUD→AI dependency root CLAUDE.md §10 bullet 3 forbids. The Communication
//       resolver keys off an email category, not the caller's business unit, so it cannot be generalized
//       without changing what it means.
//   (3) Cost-of-doing-nothing — every record the BFF creates app-only defaults to the calling application
//       user, which lives in the ROOT business unit: measured 2026-09-22, ALL 512 sprk_document rows sit in
//       root. Users sit in child BUs and Dataverse Deep depth traverses DOWNWARD, so they reach none of
//       them. Concretely: task 063's Run Index and task 064's create-To-Do both return 403 for every
//       ordinary user, and every future per-record gate on a BFF-created entity fails the same way.
//
// Placement Justification (bff-extensions.md): lives in Services/Dataverse/ beside CoreAncestorResolver —
// it is a Dataverse lookup with no AI concern. It deliberately does NOT live in Spaarke.Dataverse: that
// shared library must not depend on BFF services, so create paths there receive an already-resolved team id
// as a parameter (mirroring RecordCreationRequest.OwnerSystemUserId, the shipped precedent).
//
// unified-access-control-r2 task 144 (C10 part 1, #967), 2026-10-01: ONE business unit is an exception. The
// Secure Record BU's records are owned by its NAMED, non-default owner team (SecureRecordOwnerTeam), never its
// default team — the default team's membership follows every user placed in the BU and cannot be curated, which
// is the hole this task closes. So "business unit → team" now means: the Secure Record BU → its named team (or
// REFUSE when that team cannot be resolved); every other business unit → its default team, exactly as before.
//
// unified-access-control-r2 task 146 (C10 part 2, #1034), 2026-10-01: "every child of a secure record is secure,
// server-side". Three things changed, all IN PLACE (owner D-1: one owner for the owner invariant, no second
// resolver):
//   (a) A child names EVERY parent it has, not one. sprk_document alone carries six root lookups (live metadata
//       2026-10-01: sprk_project, sprk_relatedproject, sprk_matter, sprk_relatedmatter, sprk_workassignment,
//       sprk_relatedworkassignment). A writer that passed only its first lookup would miss a secure parent named
//       in another, so RecordOwnershipContext.Parents carries all of them and the resolver applies SECURE-IF-ANY:
//       any parent in the Secure Record business unit makes the child the named Secure team's.
//   (b) A root FLAGGED sprk_issecure but not owned in the Secure Record business unit (an interrupted or failed
//       provisioning, C11) REFUSES. Record-first reads ownership, and such a root's ownership says "ordinary", so
//       without this check its children would be handed an ordinary team (ADR-003: fail closed).
//   (c) Refusals carry a stable code and a reason (RecordOwnerResolution), so each writer can refuse in its own
//       error contract with a message that names WHY — and a reparent re-derives the owner through the same rule
//       (ReparentAsync), assigning it in a separate update and reading it back.
//   Contact access is unaffected: contacts reach children through app-only reads keyed on the parent.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Resolves the team that should own a newly created record: the <b>default owner team</b> of the relevant
/// business unit — the TARGET record's BU where the record is being filed against something, otherwise the
/// acting user's. See <see cref="RecordOwnershipContext"/> for why that order and not the reverse.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Secure Record business unit is the one exception (task 144).</b> A record whose business unit is the
/// Secure Record BU is owned by that BU's NAMED owner team (<see cref="SecureRecordOwnerTeam"/>) — the team that owns
/// the secure records themselves — and the resolver REFUSES (null) when that team is missing or ambiguous. It never
/// answers with the Secure Record BU's default team.
/// </para>
/// <para>
/// <b>Secure-if-any (task 146).</b> A child names every parent it has (<see cref="RecordOwnershipContext.Parents"/>).
/// If ANY of them is owned in the Secure Record business unit, the child is the named Secure team's. If any root
/// parent is FLAGGED <c>sprk_issecure</c> but is not owned there, the resolver refuses — never an ordinary team. A parent
/// that is itself a child and is NOT team-owned (a run-as-user, client-created or pre-146 row) is looked through to the
/// records it is filed under, so a grandchild of a secure root is secure even while its parent awaits backfill (r2).
/// </para>
/// <para>
/// Setting <c>ownerid</c> to that team makes <c>owningbusinessunit</c> <b>derive</b> from it —
/// <c>owningbusinessunit</c> is never assigned directly. This is the shape <c>sprk_matter</c> rows already
/// have in every environment checked, which is why it is the target rather than an invention.
/// </para>
/// <para>
/// <b>Fail-closed.</b> An unresolvable team returns <c>null</c> (a <see cref="RecordOwnerOutcome.Refused"/>
/// resolution) and callers MUST refuse the create. Falling back to app-only ownership is exactly the defect this
/// type exists to remove, and a silent fallback would reintroduce it invisibly — a record that looks created but
/// is unreachable by the person who created it.
/// </para>
/// <para>
/// <b>An answer versus a fault.</b> <c>null</c> means the data says there is no team (a missing or unowned
/// target, no such user, an ambiguous user, no single default team). A Dataverse fault is NOT that answer and
/// PROPAGATES — a throttled read must not become a permanent refusal telling the user to fix their setup.
/// </para>
/// </remarks>
public interface IRecordOwnershipResolver
{
    /// <summary>
    /// Resolves the team that should own a new record, applying the priority order in ONE place so no call
    /// site can get it wrong: <b>the target record's business unit first, the acting user's second</b>.
    /// Returns <c>null</c> when neither resolves — callers MUST then refuse.
    /// </summary>
    /// <remarks>The pre-146 shape, kept for the Office writers. It is <see cref="ResolveOwnerAsync"/> without the
    /// reason: every rule (secure-if-any, the flagged-not-isolated refusal) applies to it too.</remarks>
    Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct);

    /// <summary>
    /// The same decision as <see cref="ResolveOwningTeamAsync"/>, with the outcome named: the team, or a refusal
    /// carrying a stable <see cref="RecordOwnerRefusal"/> code and a reason a writer can put in its own error
    /// contract, or — only when the context opts in — <see cref="RecordOwnerOutcome.Unchanged"/>.
    /// </summary>
    Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct);

    /// <summary>
    /// Re-derives the owner of an EXISTING row whose parent lookups are changing (a reparent), BEFORE the change
    /// is written: reads the row's current parents, overlays <see cref="RecordReparent.ParentChanges"/>, resolves
    /// through the same rule as a create, and only then runs <paramref name="applyChange"/>. When the resolved team
    /// differs from the row's owner it is assigned in a SEPARATE update (never folded into the field update — an
    /// owner change is its own Dataverse operation) and read back.
    /// </summary>
    /// <returns>
    /// A refusal WITHOUT having run <paramref name="applyChange"/> — the caller refuses in its own contract.
    /// Otherwise the resolution after the change and any reassignment succeeded.
    /// </returns>
    /// <remarks>
    /// <para><b>A failed assignment is rolled back, never left half-done</b> (task 146 b1/b2). When the owner assignment
    /// (or its read-back) fails AFTER <paramref name="applyChange"/> wrote the change — a Secure Record Owner role still
    /// missing Read on the table is the expected cause until gate G146-1 runs — the owner is read again: when it IS the
    /// resolved team the assignment landed and the re-file stands; otherwise the columns the change moved (its parent
    /// lookups and <see cref="RecordReparent.AttachColumns"/>) are written back to their previous values, so the row's
    /// filing matches the owner it still has, and the failure is logged CRITICAL (<c>ReparentOwnerAssignmentFailed</c>).
    /// When the owner cannot be read at all, the safe direction is taken (put back for a move into a secure record, kept
    /// otherwise) and logged <c>ReparentLeftInconsistent</c>, as is a restore that also fails. The original failure then
    /// propagates.</para>
    /// <para>The "already owned" check reads the owner AFTER the change, so an owner another writer set in between is
    /// never mistaken for the resolved one.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The reassignment did not read back as applied (the change was rolled
    /// back first).</exception>
    Task<RecordOwnerResolution> ReparentAsync(
        RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct);
}

/// <summary>One parent record a child is filed under: a lookup value the child carries.</summary>
public sealed record RecordOwnershipParent(string EntityLogicalName, Guid RecordId)
{
    /// <summary>True when the reference names a real row.</summary>
    public bool IsSpecified => !string.IsNullOrWhiteSpace(EntityLogicalName) && RecordId != Guid.Empty;
}

/// <summary>
/// What a create does when it names NO parent at all. The default is the owner's I-6 convention (the acting
/// user's business-unit team); the alternative exists for writers whose unfiled rows carry an existing contract
/// an owner team would break (task 146 escalation E1 — see <see cref="KeepCreator"/>).
/// </summary>
public enum UnfiledOwnership
{
    /// <summary>The acting user's business-unit team (owner decision 2026-09-25, §6.5 Path A recorded by the peer);
    /// refuse when there is no acting user either.</summary>
    ActingUserTeam = 0,

    /// <summary>
    /// Leave <c>ownerid</c> unset — the creating identity keeps the row — and answer
    /// <see cref="RecordOwnerOutcome.Unchanged"/>. Used ONLY for unfiled <c>sprk_communication</c> rows (and their
    /// content rows): escalation E1, ACCEPTED by owner round 10 item 8 (2026-10-03, "unfiled communications (inbound,
    /// chat, outbound naming no record) keep their creator as owner; filed ones are routed secure-if-any"). Why:
    /// inbound mail has no acting user and must never be dropped
    /// (constraint vs owner contract, escalation trigger 7); <c>ThreadResolver</c>'s per-user master thread is
    /// keyed on the message's OWNING USER; Direct-thread privacy rests on per-participant shares of an
    /// application-owned row. A team owner would break all three. A FILED communication never takes this branch.
    /// </summary>
    KeepCreator = 1,
}

/// <summary>
/// What is known about a record being created, in the order that decides its owner.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why record-first rather than creator-first.</b> The acting user's business unit is the wrong answer more
/// often than it is the right one:
/// </para>
/// <list type="number">
/// <item><b>Some creates have no acting user at all.</b> <c>EmailAttachmentProcessor</c> creates documents from
/// inbound email — no human initiated it, so "the acting user's BU" is undefined. Creator-first would have to
/// refuse and would break inbound attachment processing outright.</item>
/// <item><b>Even with an acting user, their BU is often the wrong scope.</b> A root-BU paralegal filing a
/// document to a child-BU matter would, creator-first, put the document in ROOT — where the very team that
/// owns the matter cannot see it. The document belongs with its matter, not with whoever uploaded it.</item>
/// </list>
/// <para>
/// This mirrors <c>RecordContainerResolver</c> (task 076, owner-sanctioned), which derives an SPE container the
/// same way — <c>ResolveForRecordAsync</c> from the target record, <c>ResolveForActingUserAsync</c> only when
/// there is no record. Same question, same answer shape; reusing the established order rather than inventing a
/// second one. It is also the same instinct as FR-26's <c>CoreAncestorResolver</c>, which exists precisely
/// because access for a server-created child has to be inherited from its core ancestor.
/// </para>
/// <para>
/// <b>Every parent, not the first (task 146).</b> <see cref="TargetEntityLogicalName"/>/<see cref="TargetRecordId"/>
/// is the PRIMARY parent — it decides the business unit of an ordinary child. <see cref="Parents"/> carries every
/// OTHER parent lookup value the child has; together they are the set secure-if-any is applied to. Build them
/// from the row being written with <see cref="ParentsOf(IEnumerable{KeyValuePair{string, object}})"/> so a lookup
/// cannot be forgotten.
/// </para>
/// </remarks>
public sealed record RecordOwnershipContext
{
    /// <summary>Logical name of the record this one is being filed against (e.g. <c>sprk_matter</c>). Null when unfiled.</summary>
    public string? TargetEntityLogicalName { get; init; }

    /// <summary>Id of the record this one is being filed against. Null when unfiled.</summary>
    public Guid? TargetRecordId { get; init; }

    /// <summary>
    /// Every OTHER parent the child is filed under (task 146) — both project lookups of a document, the FR-26
    /// core-ancestor stamps, a reply's source communication. Entries whose type is not an ownership parent
    /// (<see cref="RecordOwnershipResolver.IsOwnershipParent"/> — a contact, an organization, a record-type ref)
    /// are ignored: they are relationships, not parents.
    /// </summary>
    public IReadOnlyList<RecordOwnershipParent> Parents { get; init; } = Array.Empty<RecordOwnershipParent>();

    /// <summary>The acting user's Dataverse systemuserid, when known (HTTP paths that already resolved it).</summary>
    public Guid? CallerSystemUserId { get; init; }

    /// <summary>
    /// The acting user's Entra object id, when that is all the path has. Background paths have only this:
    /// <c>UploadFinalizationWorker</c> runs from a queue message carrying <c>payload.UserId</c> with no
    /// <see cref="System.Security.Claims.ClaimsPrincipal"/> to hand to <c>ICallerSystemUserResolver</c>. The
    /// cross-reference is <c>systemuser.azureactivedirectoryobjectid</c> (ADR-028) — the same key that
    /// resolver uses.
    /// </summary>
    public Guid? CallerObjectId { get; init; }

    /// <summary>What happens when no parent at all is named. Default: the acting user's team (I-6).</summary>
    public UnfiledOwnership WhenUnfiled { get; init; } = UnfiledOwnership.ActingUserTeam;

    /// <summary>
    /// The PERSON who asked for the row, when the BFF creates it as the application (task 146 c1-r1, owner round 13 item 9:
    /// "children the BFF creates as the application record the person who asked"). The resolver turns it into a
    /// <c>systemuserid</c> (<see cref="RecordOwnerResolution.CreatedByPerson"/>) and the writer stamps it with the owner
    /// (<see cref="RecordOwnerResolution.ApplyTo"/>), so F3's "or the creator" branch can admit that person later. It
    /// decides NOTHING about the owner. <c>null</c>: the writer acts for nobody (inbound mail, a background job), and the
    /// row records no person.
    /// </summary>
    public RecordRequester? RequestedBy { get; init; }

    /// <summary>
    /// For a CONTENT row hanging off its primary target (a review log, a participant row, an attachment row of a
    /// communication). When the target IS team-owned, the row resolves record-first from it like any child. When the
    /// target is NOT team-owned (it kept a user or application owner), the target's OWN parent lookups decide:
    /// <list type="bullet">
    /// <item>the target names no parent — an UNFILED row that kept its creator under
    /// <see cref="UnfiledOwnership.KeepCreator"/> (E1) — so the content row keeps its creator too and the answer is
    /// <see cref="RecordOwnerOutcome.Unchanged"/>;</item>
    /// <item>the target IS filed (a run-as-user or client-created row, or one written before task 146) — so the content
    /// row resolves from the records the TARGET is filed under, secure-if-any. A user-owned communication filed to a
    /// secure matter must not hand its content to its creator in an ordinary business unit (task 146 verifier item 3).</item>
    /// </list>
    /// </summary>
    public bool KeepCreatorUnlessTargetIsTeamOwned { get; init; }

    /// <summary>
    /// unified-access-control-r2 task 148 — the ONE root an unsecure transition is taking out of isolation, named by the
    /// only caller allowed to name it (<c>SecureChildReconciler</c>, after <c>/unsecure-project</c> has moved the root off
    /// the Secure Record owner team and read the move back). Its <c>sprk_issecure</c> flag is still <c>true</c> — the
    /// endpoint clears it LAST, and keeps it when the child pass does not complete — so without this the flag-but-not-
    /// isolated refusal (C11) would refuse every one of its children, and the transition could never re-own them. For
    /// THAT root only, the flag is not read as "a provisioning that did not complete": its ownership decides, exactly as
    /// for any ordinary parent. Every other parent keeps every rule (secure-if-any, the refusal). <c>null</c> everywhere
    /// else.
    /// </summary>
    public RecordOwnershipParent? UnsecuringRoot { get; init; }

    /// <summary>
    /// unified-access-control-r2 task 148 r2 — the owners a REPORT-ONLY pass of <c>SecureChildReconciler</c> has planned
    /// for rows it does not write: row → the owning team it would give that row. A parent named here is read as owned by
    /// that team (its owning business unit is the team's) instead of by its stored owner; its existence and its
    /// <c>sprk_issecure</c> flag are still read. So a dry run decides a grandchild against the owner its parent WOULD have
    /// — the plan a writing pass carries out — and lists every change a writing pass would make, not only the first level.
    /// The rule itself is unchanged: only the facts it reads for a planned row are the planned ones. A planned team whose
    /// row cannot be read refuses (<see cref="RecordOwnerRefusal.ParentUnresolved"/>), as any unreadable parent does.
    /// Set by nothing that writes; <c>null</c> everywhere else.
    /// </summary>
    public IReadOnlyDictionary<RecordOwnershipParent, Guid>? PlannedOwningTeams { get; init; }

    /// <summary>True when a target record was supplied — i.e. the preferred source is available.</summary>
    public bool HasTarget =>
        !string.IsNullOrWhiteSpace(TargetEntityLogicalName)
        && TargetRecordId is { } id && id != Guid.Empty;

    /// <summary>True when ANY parent (the target, or an ownership-parent entry of <see cref="Parents"/>) is named.</summary>
    public bool HasParent =>
        HasTarget || Parents.Any(p => p.IsSpecified && RecordOwnershipResolver.IsOwnershipParent(p.EntityLogicalName));

    /// <summary>
    /// Every ownership-parent reference among <paramref name="fields"/> — the attributes of a row being written, or
    /// an update payload. Reads <see cref="EntityReference"/> values only, keyed by whatever column holds them, so
    /// a child table's every parent lookup is picked up without a per-table column list (the set of lookups comes
    /// from the row itself, never from a guess).
    /// </summary>
    public static IReadOnlyList<RecordOwnershipParent> ParentsOf(IEnumerable<KeyValuePair<string, object>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var parents = new List<RecordOwnershipParent>();
        foreach (var (_, value) in fields)
        {
            if (value is EntityReference reference
                && reference.Id != Guid.Empty
                && RecordOwnershipResolver.IsOwnershipParent(reference.LogicalName))
            {
                parents.Add(new RecordOwnershipParent(reference.LogicalName, reference.Id));
            }
        }

        return parents.Distinct().ToArray();
    }

    /// <summary>
    /// The context for a row filed under <paramref name="parents"/> — nulls and non-parent types dropped, the first
    /// remaining parent primary — with the acting user (when the path has one) for a row left with no parent.
    /// </summary>
    public static RecordOwnershipContext ForParents(
        IEnumerable<RecordOwnershipParent?> parents, Guid? callerObjectId = null, Guid? callerSystemUserId = null)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var specified = parents
            .Where(p => p is { IsSpecified: true } && RecordOwnershipResolver.IsOwnershipParent(p.EntityLogicalName))
            .Select(p => p!)
            .Distinct()
            .ToArray();
        var primary = specified.FirstOrDefault();
        return new RecordOwnershipContext
        {
            TargetEntityLogicalName = primary?.EntityLogicalName,
            TargetRecordId = primary?.RecordId,
            Parents = specified,
            CallerObjectId = callerObjectId,
            CallerSystemUserId = callerSystemUserId,
        };
    }

    /// <summary>
    /// The context for a child row about to be created: every ownership-parent lookup on <paramref name="child"/>
    /// becomes a parent. The first parent in attribute order is not meaningful, so an ordinary child's business unit
    /// comes from <paramref name="primaryParent"/> when it is an ownership parent (a contact regarding is not), else
    /// the first parent found.
    /// </summary>
    public static RecordOwnershipContext ForChild(Entity child, RecordOwnershipParent? primaryParent = null)
    {
        ArgumentNullException.ThrowIfNull(child);
        var parents = ParentsOf(child.Attributes);
        var primary = primaryParent is { IsSpecified: true } p && RecordOwnershipResolver.IsOwnershipParent(p.EntityLogicalName)
            ? p
            : parents.FirstOrDefault();
        return new RecordOwnershipContext
        {
            TargetEntityLogicalName = primary?.EntityLogicalName,
            TargetRecordId = primary?.RecordId,
            Parents = parents,
        };
    }

    /// <summary>
    /// The context for a CONTENT row created under <paramref name="parentEntityLogicalName"/> — an attachment, a
    /// participant, a review log, an archived document of a communication (task 146). Record-first from the parent
    /// when it is team-owned (so a secure parent's content is the named Secure team's); from the records the parent is
    /// filed under when the parent kept a user or application owner but IS filed; and
    /// <see cref="RecordOwnerOutcome.Unchanged"/> — the creator — only while the parent is an UNFILED row that kept its
    /// own creator (E1). See <see cref="KeepCreatorUnlessTargetIsTeamOwned"/>.
    /// </summary>
    public static RecordOwnershipContext ContentOf(string parentEntityLogicalName, Guid parentId) => new()
    {
        TargetEntityLogicalName = parentEntityLogicalName,
        TargetRecordId = parentId,
        KeepCreatorUnlessTargetIsTeamOwned = true,
    };
}

/// <summary>
/// The person who asked for a row the BFF creates as the application (task 146 c1-r1) — by <c>systemuserid</c> when the
/// path already resolved it, else by Entra object id (the resolver looks the user up, exactly as it does for an unfiled
/// row's acting user). Server-derived only: never bound from a request body.
/// </summary>
public sealed record RecordRequester(Guid? SystemUserId, Guid? ObjectId)
{
    /// <summary>A requester from whatever the path holds; <c>null</c> when it holds neither id (the row records nobody).</summary>
    public static RecordRequester? Of(Guid? systemUserId = null, Guid? objectId = null) =>
        systemUserId is { } s && s != Guid.Empty
            ? new RecordRequester(s, objectId is { } o && o != Guid.Empty ? o : null)
            : objectId is { } oid && oid != Guid.Empty
                ? new RecordRequester(null, oid)
                : null;

    /// <summary>A requester from an Entra object id held as text (a claim, a queued payload); <c>null</c> when it is not one.</summary>
    public static RecordRequester? OfObjectId(string? objectId) =>
        Guid.TryParse(objectId, out var oid) ? Of(objectId: oid) : null;

    /// <summary>The signed-in caller of an HTTP request (their <c>oid</c> claim, the same one every route resolves).</summary>
    public static RecordRequester? OfCaller(System.Security.Claims.ClaimsPrincipal? user) =>
        OfObjectId(Sprk.Bff.Api.Infrastructure.Authentication.CallerResolution.ResolveObjectId(user));
}

/// <summary>A reparent: an existing row whose parent lookups are about to change.</summary>
public sealed record RecordReparent
{
    /// <summary>The row's table.</summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>The row.</summary>
    public required Guid RecordId { get; init; }

    /// <summary>
    /// The lookups the change writes: column → new parent, or <c>null</c> for a column the change CLEARS. Entries
    /// whose value is not an ownership parent are ignored (the row's other columns are not parents). A <c>null</c>
    /// entry for a column that holds no ownership parent on the row is not a parent change either; when the change
    /// neither sets a parent nor clears one the row holds, <see cref="IRecordOwnershipResolver.ReparentAsync"/> writes
    /// the change and makes no ownership decision (verifier item 8 — a writer may pass every null it writes without
    /// knowing which of them are lookups).
    /// </summary>
    public required IReadOnlyDictionary<string, EntityReference?> ParentChanges { get; init; }

    /// <summary>
    /// Parents the row takes on from something it is being ATTACHED to rather than from one of its own columns — a
    /// message joining a record thread takes on that thread's regarding record (S6: a record thread's messages are
    /// children of its record). Added to the row's current parents.
    /// </summary>
    public IReadOnlyList<RecordOwnershipParent> InheritedParents { get; init; } = Array.Empty<RecordOwnershipParent>();

    /// <summary>
    /// Columns the change writes to ATTACH the row to its <see cref="InheritedParents"/> — a message's thread lookup when
    /// it joins a record thread. They are not parent lookups, so the resolver cannot see them in
    /// <see cref="ParentChanges"/>; naming them lets <see cref="IRecordOwnershipResolver.ReparentAsync"/> put them back,
    /// with the parent columns, when the owner assignment that must follow the change fails (task 146 b1).
    /// </summary>
    public IReadOnlyList<string> AttachColumns { get; init; } = Array.Empty<string>();

    /// <summary>The acting user, for a row left with no parent at all.</summary>
    public Guid? CallerSystemUserId { get; init; }

    /// <summary>The acting user's Entra object id, when that is all the path has.</summary>
    public Guid? CallerObjectId { get; init; }

    /// <summary>What a row left with no parent does. Communications keep their creator (E1).</summary>
    public UnfiledOwnership WhenUnfiled { get; init; } = UnfiledOwnership.ActingUserTeam;

    /// <summary>
    /// The person making the change, for owner round 10 item 7 (2026-10-03, BINDING): "moving a CHILD out of a secure
    /// root is an un-secure, so F3's limit applies". When the change would take the row out from under a secure root —
    /// to no secure root, or to a different one — <see cref="IRecordOwnershipResolver.ReparentAsync"/> asks
    /// <see cref="Sprk.Bff.Api.Services.Access.SecureDesignationRemoval"/> about THIS caller for every secure root the
    /// row leaves, before anything is written. <c>null</c> means the writer acts for no person (a background job, an
    /// app-only automation): such a move is refused (fail closed, ADR-003). Moves that leave no secure root never ask.
    /// </summary>
    public Sprk.Bff.Api.Services.Access.SecureRemovalCaller? SecureExitCaller { get; init; }

    /// <summary>
    /// The parent changes as <see cref="EntityReference"/> values from an update payload: every
    /// <see cref="EntityReference"/> value whose type is an ownership parent. A payload that names no parent yields
    /// an empty map — the caller then has no reparent to do.
    /// </summary>
    public static IReadOnlyDictionary<string, EntityReference?> ParentChangesIn<TValue>(IEnumerable<KeyValuePair<string, TValue>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var changes = new Dictionary<string, EntityReference?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in fields)
        {
            if (value is EntityReference reference && RecordOwnershipResolver.IsOwnershipParent(reference.LogicalName))
            {
                changes[column] = reference.Id == Guid.Empty ? null : reference;
            }
        }

        return changes;
    }

    /// <summary>
    /// <see cref="ParentChangesIn{TValue}"/> plus every column the payload CLEARS: a <c>null</c> value is entered as a
    /// <c>null</c> change, because a generic update cannot tell a cleared lookup from a cleared text column — the row
    /// can (<see cref="IRecordOwnershipResolver.ReparentAsync"/> reads it, and a null for a column that holds no parent
    /// is no parent change). Task 146 verifier item 8: a child moved OUT of its secure parent by clearing the lookup
    /// must be re-owned like one moved by setting another.
    /// </summary>
    public static IReadOnlyDictionary<string, EntityReference?> ParentChangesWithClearsIn<TValue>(
        IEnumerable<KeyValuePair<string, TValue>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var pairs = fields.ToArray();
        var changes = new Dictionary<string, EntityReference?>(ParentChangesIn(pairs), StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in pairs)
        {
            if (value is null && !string.IsNullOrWhiteSpace(column) && !changes.ContainsKey(column))
                changes[column] = null;
        }

        return changes;
    }

    /// <summary>
    /// The parent lookups a <c>sprk_document</c> update WRITES, as <c>sprk_document</c> columns — the same column/target
    /// pairs <c>DataverseServiceClientImpl.UpdateDocumentAsync</c> maps them to (an update only ever SETS a lookup;
    /// nothing in <see cref="UpdateDocumentRequest"/> clears one). <c>ContactLookup</c> is not an ownership parent.
    /// Empty when the update files the document under nothing new — the caller then has no reparent to do.
    /// </summary>
    public static IReadOnlyDictionary<string, EntityReference?> ParentChangesOf(UpdateDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var changes = new Dictionary<string, EntityReference?>(StringComparer.OrdinalIgnoreCase);
        void Set(string column, string target, Guid? id)
        {
            if (id is { } value && value != Guid.Empty)
                changes[column] = new EntityReference(target, value);
        }

        Set("sprk_parentdocument", "sprk_document", request.ParentDocumentLookup);
        Set("sprk_matter", "sprk_matter", request.MatterLookup);
        Set("sprk_project", "sprk_project", request.ProjectLookup);
        Set("sprk_invoice", "sprk_invoice", request.InvoiceLookup);
        Set("sprk_workassignment", "sprk_workassignment", request.WorkAssignmentLookup);
        Set("sprk_relatedevent", "sprk_event", request.EventLookup);
        Set("sprk_relatedtodo", "sprk_todo", request.TodoLookup);
        return changes;
    }
}

/// <summary>How an ownership question was answered.</summary>
public enum RecordOwnerOutcome
{
    /// <summary>A team owns the row: <see cref="RecordOwnerResolution.OwningTeamId"/>.</summary>
    Owned,

    /// <summary>No owner can be determined; the writer MUST NOT write the row.</summary>
    Refused,

    /// <summary>
    /// The context opted in (<see cref="UnfiledOwnership.KeepCreator"/> or
    /// <see cref="RecordOwnershipContext.KeepCreatorUnlessTargetIsTeamOwned"/>) and the row keeps its creating
    /// identity; the writer leaves <c>ownerid</c> unset. Never returned to a CREATE context that did not opt in. A
    /// reparent also answers it when the change touches no parent the row is filed under (the change was written, the
    /// owner was left as it was).
    /// </summary>
    Unchanged,
}

/// <summary>Stable refusal codes. HTTP writers return them as their ProblemDetails reason code.</summary>
public static class RecordOwnerRefusal
{
    /// <summary>A parent the child names does not exist, or has no owning business unit.</summary>
    public const string ParentUnresolved = "record_owner_parent_unresolved";

    /// <summary>A root is flagged <c>sprk_issecure</c> but is not owned in the Secure Record business unit.</summary>
    public const string SecureParentNotIsolated = "record_owner_secure_parent_not_isolated";

    /// <summary>The Secure Record business unit's named owner team is missing or ambiguous.</summary>
    public const string SecureOwnerTeamUnresolved = "record_owner_secure_team_unresolved";

    /// <summary>More than one business unit carries the Secure Record name.</summary>
    public const string SecureBusinessUnitAmbiguous = "record_owner_secure_bu_ambiguous";

    /// <summary>The business unit has no single default Owner team.</summary>
    public const string NoDefaultOwnerTeam = "record_owner_no_default_team";

    /// <summary>The acting user is unknown, or maps to more than one Dataverse user.</summary>
    public const string ActingUserUnresolved = "record_owner_acting_user_unresolved";

    /// <summary>No parent and no acting user: nothing to own the row from.</summary>
    public const string NoOwnerSource = "record_owner_no_source";

    /// <summary>The row being reparented does not exist.</summary>
    public const string RecordMissing = "record_owner_record_missing";

    /// <summary>
    /// The records a new row is filed under could not be DETERMINED — the association evaluation or a core-ancestor
    /// derivation failed before the owner could be asked. Inbound mail is HELD on it (owner amendment R3: "secure parent
    /// cannot be determined → held"), never created unfiled in its place (task 146 verifier item 4).
    /// </summary>
    public const string ParentUndetermined = "record_owner_parent_undetermined";
}

/// <summary>The answer to an ownership question: a team, a reasoned refusal, or (opted-in only) unchanged.</summary>
public sealed record RecordOwnerResolution(
    RecordOwnerOutcome Outcome, Guid? OwningTeamId, string? RefusalCode, string? Reason)
{
    /// <summary>True when a team was resolved.</summary>
    public bool IsOwned => Outcome == RecordOwnerOutcome.Owned && OwningTeamId is { } id && id != Guid.Empty;

    /// <summary>True when the writer must not write.</summary>
    public bool IsRefused => Outcome == RecordOwnerOutcome.Refused;

    /// <summary>
    /// True when the team is the Secure Record business unit's NAMED owner team — the row is a child of a secure record
    /// (task 146 r2). Lets a writer that may not re-own a row itself (a run-as-user tool writing a table outside the
    /// ownership set) refuse rather than leave a secure record's child in an ordinary business unit, without computing a
    /// team of its own.
    /// </summary>
    public bool IsSecureOwner { get; init; }

    /// <summary>
    /// Set on a refused RE-FILE that would have taken the row out of a secure record without F3's permission (owner round
    /// 10 item 7, task 146 c1). <see cref="RefusalCode"/> is then one of the unsecure endpoint's two reason codes, and an
    /// HTTP writer answers with that endpoint's 403 ProblemDetails shape
    /// (<see cref="Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution, string, string?)"/>).
    /// </summary>
    public Sprk.Bff.Api.Services.Access.SecureRemovalDecision? SecureRemovalRefusal { get; init; }

    /// <summary>True for a refusal the F3 check made (<see cref="SecureRemovalRefusal"/>): an AUTHORIZATION answer, not an
    /// owner refusal — HTTP writers answer it with the unsecure endpoint's ProblemDetails, not the owner refusal's 409.</summary>
    public bool IsForbidden => IsRefused && SecureRemovalRefusal is not null;

    /// <summary>
    /// The <c>systemuserid</c> of the person who asked for the row (<see cref="RecordOwnershipContext.RequestedBy"/>), when
    /// the context named one and it resolved to exactly one Dataverse user; <c>null</c> otherwise (task 146 c1-r1, owner
    /// round 13 item 9). Written by <see cref="ApplyTo"/> / <see cref="StampCreatorOn(Entity)"/> as
    /// <c>sprk_createdbyperson</c> on a table that carries it (<see cref="RecordCreatorPerson.IsStamped"/>).
    /// </summary>
    public Guid? CreatedByPerson { get; init; }

    /// <summary>
    /// Stamps <see cref="CreatedByPerson"/> on an SDK create payload when the row's table carries the column; does nothing
    /// otherwise (no person, or a table without the column). Never call it for a refusal.
    /// </summary>
    public void StampCreatorOn(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (CreatedByPerson is { } person && person != Guid.Empty && RecordCreatorPerson.IsStamped(row.LogicalName))
            RecordCreatorPerson.Stamp(row, person);
    }

    /// <summary>
    /// <see cref="StampCreatorOn(Entity)"/> for a Web API create payload of <paramref name="entityLogicalName"/>
    /// (<c>sprk_CreatedByPerson@odata.bind</c>).
    /// </summary>
    public void StampCreatorOn(IDictionary<string, object?> webApiFields, string entityLogicalName)
    {
        ArgumentNullException.ThrowIfNull(webApiFields);
        if (CreatedByPerson is { } person && person != Guid.Empty && RecordCreatorPerson.IsStamped(entityLogicalName))
            RecordCreatorPerson.Bind(webApiFields, person);
    }

    /// <summary>A resolved team.</summary>
    public static RecordOwnerResolution Owned(Guid teamId) => new(RecordOwnerOutcome.Owned, teamId, null, null);

    /// <summary>A refusal with its stable code and a reason that names what is wrong.</summary>
    public static RecordOwnerResolution Refused(string code, string reason) =>
        new(RecordOwnerOutcome.Refused, null, code, reason);

    /// <summary>The row keeps its creator (opted-in contexts only).</summary>
    public static RecordOwnerResolution Unchanged(string reason) =>
        new(RecordOwnerOutcome.Unchanged, null, null, reason);

    /// <summary>
    /// The F3 check refused the move out of a secure root (owner round 10 item 7): the decision's reason code, and the
    /// move-out message for <paramref name="childNoun"/> as the reason.
    /// </summary>
    internal static RecordOwnerResolution SecureRemovalRefused(
        Sprk.Bff.Api.Services.Access.SecureRemovalDecision decision, string childNoun) =>
        Refused(decision.ReasonCode!, decision.MoveOutDetail(childNoun)) with { SecureRemovalRefusal = decision };

    /// <summary>
    /// Writes this answer's owner onto a row about to be created: <c>ownerid</c> = the team when
    /// <see cref="IsOwned"/>; nothing when <see cref="RecordOwnerOutcome.Unchanged"/> (the row keeps its creator).
    /// Never call it for a refusal — the writer must not write at all. Task 146 c1-r1: also stamps the person who asked
    /// (<see cref="StampCreatorOn(Entity)"/>), whether or not the owner changes — who asked is a fact about the create.
    /// </summary>
    /// <exception cref="InvalidOperationException">The answer is a refusal.</exception>
    public void ApplyTo(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (IsRefused)
            throw new InvalidOperationException($"A refused owner ({RefusalCode}) cannot be applied to a new {row.LogicalName}.");
        if (IsOwned)
            row["ownerid"] = new EntityReference("team", OwningTeamId!.Value);
        StampCreatorOn(row);
    }
}

/// <summary>
/// Thrown by a writer whose own error contract is "throw" when the owner is refused — background writers whose
/// caller turns it into a retry or a hold. Carries the stable code so the caller can report it.
/// </summary>
public sealed class RecordOwnerUnresolvedException : InvalidOperationException
{
    public RecordOwnerUnresolvedException(
        string entityLogicalName, RecordOwnerResolution resolution, Exception? innerException = null)
        : base($"No owner could be resolved for a new {entityLogicalName}, so it was not written: "
               + $"{resolution.Reason} ({resolution.RefusalCode}).", innerException)
    {
        EntityLogicalName = entityLogicalName;
        RefusalCode = resolution.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource;
        Reason = resolution.Reason;
    }

    /// <summary>The resolution's reason — what is wrong, for the caller's ProblemDetails detail.</summary>
    public string? Reason { get; }

    /// <summary>The table the refused row belongs to.</summary>
    public string EntityLogicalName { get; }

    /// <summary>The stable <see cref="RecordOwnerRefusal"/> code.</summary>
    public string RefusalCode { get; }
}

/// <inheritdoc cref="IRecordOwnershipResolver" />
public sealed class RecordOwnershipResolver : IRecordOwnershipResolver
{
    private const string SystemUserEntity = "systemuser";
    private const string TeamEntity = "team";
    private const string BusinessUnitEntity = "businessunit";
    private const string BusinessUnitColumn = "businessunitid";
    private const string OwningBusinessUnitColumn = "owningbusinessunit";
    private const string OwningTeamColumn = "owningteam";
    private const string OwnerColumn = "ownerid";
    private const string IsSecureColumn = "sprk_issecure";

    /// <summary>
    /// Owner-team teamtype. Dataverse defines 0 = Owner, 1 = Access. BOTH this and <c>isdefault</c> are
    /// required: dev contains non-default Owner teams (auto-created, GUID-shaped names) AND Access teams,
    /// so filtering on either predicate alone selects the wrong team.
    /// </summary>
    private const int OwnerTeamType = SecureRecordOwnerTeam.OwnerTeamType;

    /// <summary>
    /// The tables that carry <c>sprk_issecure</c> — the three secure-root types, from the ONE root table
    /// (<see cref="ExternalGrantRoot"/>), never re-listed. A parent of one of these is read with its flag.
    /// </summary>
    private static readonly HashSet<string> SecureFlaggedRoots = new(
        Enum.GetValues<ExternalGrantRootType>().Select(ExternalGrantRoot.LogicalNameFor),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The tables a child can be filed under for ownership: the three secure-flagged roots, the internal core root
    /// (<c>sprk_servicerequest</c>), and every Spaarke table that is itself a child of a root — enumerated from LIVE
    /// metadata (task 146 step 2, spaarkedev1 2026-10-01, <c>EntityDefinitions(sprk_project|sprk_matter|
    /// sprk_workassignment)/OneToManyRelationships</c>), all UserOwned. A child-of-a-child (a document filed to a
    /// communication) follows its parent's owner, which is how a grandchild of a secure root becomes secure.
    /// </summary>
    /// <remarks>
    /// Deliberately excluded, each with its reason: <c>sprk_externalrecordaccess</c> (a GRANT row, not a child);
    /// <c>sprk_communicationrule</c> (filing configuration); <c>sprk_communicationthread</c> (a grouping container —
    /// a per-user master or Direct thread is owned by a USER, and following it would hand a private message to that
    /// user's whole business unit; record threads carry their own regarding, which their messages are filed under);
    /// identity tables (<c>contact</c>, <c>account</c>, <c>sprk_organization</c>, <c>systemuser</c>, <c>team</c>) and
    /// organization-owned reference tables (<c>sprk_recordtype_ref</c>), which have no owning business unit at all.
    /// Pinned by test; a table added here must be UserOwned (a parent with no owning business unit refuses).
    /// </remarks>
    public static readonly IReadOnlyList<string> OwnershipParentEntities =
    [
        "sprk_project",
        "sprk_matter",
        "sprk_workassignment",
        "sprk_servicerequest",
        "sprk_agreement",
        "sprk_analysis",
        "sprk_billingevent",
        "sprk_budget",
        "sprk_communication",
        "sprk_document",
        "sprk_event",
        "sprk_invoice",
        "sprk_kpiassessment",
        "sprk_memo",
        "sprk_reportcard",
        "sprk_spendsignal",
        "sprk_spendsnapshot",
        "sprk_todo",
    ];

    private static readonly HashSet<string> ParentSet = new(OwnershipParentEntities, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a lookup to <paramref name="entityLogicalName"/> files a child under a parent for ownership.</summary>
    public static bool IsOwnershipParent(string? entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && ParentSet.Contains(entityLogicalName);

    /// <summary>
    /// True for a CHILD table whose owner a generic re-filing must re-derive: an ownership parent that is not itself a
    /// root. A root's own ownership (the three secure-flagged roots and the service request) is provisioning's — a
    /// secure root is provisioned into isolation through task 144's endpoint, never by a bare re-own (owner S6) — so a
    /// generic update that changes a root's lookups does not reassign it.
    /// </summary>
    public static bool IsReparentableChild(string? entityLogicalName) =>
        IsOwnershipParent(entityLogicalName)
        && !SecureFlaggedRoots.Contains(entityLogicalName!)
        && !string.Equals(entityLogicalName, "sprk_servicerequest", StringComparison.OrdinalIgnoreCase);

    // Read-only for creates: the narrowest seam that answers the queries below — the same one RecordContainerResolver
    // reads the same facts through. ReparentAsync is the one writer (an owner assignment, task 146): DI hands out the
    // one app-only IDataverseService behind it.
    private readonly IGenericEntityService _dataverse;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecordOwnershipResolver> _logger;

    public RecordOwnershipResolver(
        IGenericEntityService dataverse,
        IConfiguration configuration,
        ILogger<RecordOwnershipResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resolution = await ResolveOwnerOnlyAsync(context, ct).ConfigureAwait(false);
        return resolution.IsOwned ? resolution.OwningTeamId : null;
    }

    /// <inheritdoc />
    public async Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await ResolveOwnerOnlyAsync(context, ct).ConfigureAwait(false);
        if (resolution.IsRefused || context.RequestedBy is not { } requester)
            return resolution;

        // Task 146 c1-r1 (owner round 13 item 9): the person who asked, recorded with the owner. It never changes the owner,
        // and an unknown person refuses nothing — the row simply records nobody (F3 then admits Full Access holders only).
        var person = await ResolveRequesterAsync(requester, ct).ConfigureAwait(false);
        return person is { } id ? resolution with { CreatedByPerson = id } : resolution;
    }

    /// <summary>
    /// The <c>systemuserid</c> of the person who asked: the one given, or the ONE Dataverse user whose
    /// <c>azureactivedirectoryobjectid</c> is the object id given (TOP 2: two matches are ambiguous and name nobody). A
    /// Dataverse fault propagates, as every resolver read does.
    /// </summary>
    private async Task<Guid?> ResolveRequesterAsync(RecordRequester requester, CancellationToken ct)
    {
        if (requester.SystemUserId is { } systemUserId && systemUserId != Guid.Empty)
            return systemUserId;

        if (requester.ObjectId is not { } objectId || objectId == Guid.Empty)
            return null;

        var query = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet("systemuserid"),
            TopCount = 2,
            NoLock = true,
        };
        query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, objectId);

        var users = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        if (users.Count == 1 && users[0].Id != Guid.Empty)
            return users[0].Id;

        _logger.LogWarning(
            "The person who asked for a new row (Entra object id {ObjectId}) matches {Count} Dataverse users; the row records "
            + "no creator person (task 146, owner round 13 item 9).", objectId, users.Count);
        return null;
    }

    /// <summary>The owner decision alone — <see cref="ResolveOwnerAsync"/> without the person who asked.</summary>
    private async Task<RecordOwnerResolution> ResolveOwnerOnlyAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        var parents = CollectParents(context);

        // ── 1. PREFERRED: the parents' business units (record-first, secure-if-any) ─────────────────────
        // A filed record belongs with what it is filed against, not with whoever uploaded it. This is also
        // the only source available when there is no acting user at all (inbound email).
        if (parents.Count > 0)
        {
            return await ResolveFromParentsAsync(parents, context, ct).ConfigureAwait(false);
        }

        // ── 2. Nothing named ──────────────────────────────────────────────────────────────────────────────
        if (context.WhenUnfiled == UnfiledOwnership.KeepCreator)
        {
            return RecordOwnerResolution.Unchanged(
                "no parent is named; this writer's unfiled rows keep their creating identity (task 146 E1)");
        }

        // FALLBACK: the acting user's business unit.
        //
        // OWNER DECISION 2026-09-25, and a DELIBERATE divergence worth naming (CLAUDE.md §6.5 Path A —
        // project-scoped exception, not an oversight). The ADR-002 write-path review's gap G5 flags that
        // "the user's-BU fallback is the pattern task 076 removed elsewhere": for SPE containers, 076 refuses
        // rather than falling back to the acting user's business unit.
        //
        // Ownership keeps the fallback anyway, on the owner's call, because the two have different failure
        // costs. For a container, guessing wrong puts BYTES in the wrong place — 076 is right to refuse. For
        // ownership, refusing would block every legitimately UNASSOCIATED save (the Word ribbon quick-save
        // and any pane save with no "Related to" selected — per OfficeService's own analysis, roughly 80 of
        // the ~85 save bodies in the test corpus have no target). Refusing those is a save outage; assigning
        // them to the creator's own BU team is the correct answer for a record that genuinely belongs to
        // nobody else yet.
        //
        // The secure-record risk that motivates 076's stricter rule does NOT arise here, because this branch
        // is reached only when NO parent was named at all. A named-but-unresolvable parent refuses below.
        if (context.CallerSystemUserId is { } systemUserId && systemUserId != Guid.Empty)
        {
            return await ResolveFromUserAsync("systemuserid", systemUserId, ct).ConfigureAwait(false);
        }

        if (context.CallerObjectId is { } objectId && objectId != Guid.Empty)
        {
            return await ResolveFromUserAsync("azureactivedirectoryobjectid", objectId, ct).ConfigureAwait(false);
        }

        // ── 3. Neither. Refuse upstream. ───────────────────────────────────────────────────────────────
        _logger.LogWarning(
            "Cannot resolve an owning team: no parent record and no acting-user identity were supplied. The "
            + "record must be refused rather than created app-owned (task 080).");
        return RecordOwnerResolution.Refused(
            RecordOwnerRefusal.NoOwnerSource,
            "the record names no parent record and there is no acting user to own it");
    }

    /// <inheritdoc />
    public async Task<RecordOwnerResolution> ReparentAsync(
        RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(applyChange);

        // The row's current state — every column, because a child's parents are whatever lookups it carries, and a
        // per-table column list is exactly the guess this task forbids. A reparent is an explicit filing action,
        // not a hot path, so the wide read is the honest price.
        var query = new QueryExpression(request.EntityLogicalName)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition($"{request.EntityLogicalName}id", ConditionOperator.Equal, request.RecordId);
        var current = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();

        if (current is null)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.RecordMissing,
                $"the {request.EntityLogicalName} {request.RecordId:D} being re-filed does not exist");
        }

        // Current parents, keyed by column, overlaid with the change.
        var byColumn = new Dictionary<string, RecordOwnershipParent>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in current.Attributes)
        {
            if (value is EntityReference reference && reference.Id != Guid.Empty
                && IsOwnershipParent(reference.LogicalName))
            {
                byColumn[column] = new RecordOwnershipParent(reference.LogicalName, reference.Id);
            }
        }

        // Every parent the row is filed under BEFORE the change — what a move OUT of a secure root is measured against
        // (owner round 10 item 7, task 146 c1).
        var filedUnderBefore = byColumn.Values.Distinct().ToArray();

        // What the row was filed under BEFORE the change, for every column the change moves — so a failed owner
        // assignment after the change can put the row back where its CURRENT owner belongs (CompensateAsync).
        var restore = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        void Remember(string column) =>
            restore[column] = current.Attributes.TryGetValue(column, out var before) && before is not null
                ? before
                : DBNull.Value;

        // A change that sets a parent, or clears one the row holds, is a parent change. A null for a column that holds
        // no parent (a cleared text column a generic writer could not tell from a lookup) is not.
        var changesAParent = request.InheritedParents.Any(p => p.IsSpecified && IsOwnershipParent(p.EntityLogicalName));
        foreach (var (column, value) in request.ParentChanges)
        {
            if (value is null || value.Id == Guid.Empty)
            {
                if (byColumn.Remove(column))
                {
                    changesAParent = true;
                    Remember(column);
                }
            }
            else if (IsOwnershipParent(value.LogicalName))
            {
                byColumn[column] = new RecordOwnershipParent(value.LogicalName, value.Id);
                changesAParent = true;
                Remember(column);
            }
        }

        foreach (var column in request.AttachColumns.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            Remember(column);
        }

        if (!changesAParent)
        {
            // Nothing the row is filed under changes, so its owner does not either: write the change and decide nothing
            // (task 146 verifier item 8 — the writer passed every null it writes; none of them was a parent).
            await applyChange(ct).ConfigureAwait(false);
            return RecordOwnerResolution.Unchanged(
                $"the change to {request.EntityLogicalName} {request.RecordId:D} sets no parent and clears none it holds");
        }

        var parents = byColumn.Values
            .Concat(request.InheritedParents.Where(p => p.IsSpecified && IsOwnershipParent(p.EntityLogicalName)))
            .Distinct()
            .ToArray();
        var resolution = await ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                Parents = parents,
                CallerSystemUserId = request.CallerSystemUserId,
                CallerObjectId = request.CallerObjectId,
                WhenUnfiled = request.WhenUnfiled,
            },
            ct).ConfigureAwait(false);

        if (resolution.IsRefused)
        {
            _logger.LogWarning(
                "Refusing to re-file {Entity} {RecordId}: {Reason} ({Code}). The change was NOT written.",
                request.EntityLogicalName, request.RecordId, resolution.Reason, resolution.RefusalCode);
            return resolution;
        }

        // Owner round 10 item 7 (2026-10-03, BINDING): "moving a CHILD out of a secure root is an un-secure, so F3's
        // limit applies". A change that takes the row out from under a secure root — to no secure root, or to a
        // different one — needs the caller's F3 rights on every secure root it leaves, decided BEFORE anything is
        // written, by the same check the unsecure endpoint makes (SecureDesignationRemoval).
        var exitRefusal = await RefuseUnlessPermittedToLeaveSecureRootsAsync(
            request, current, filedUnderBefore, parents, resolution, ct).ConfigureAwait(false);
        if (exitRefusal is not null)
        {
            _logger.LogWarning(
                "Refusing to re-file {Entity} {RecordId} out of a secure record: {Reason} ({Code}). The change was NOT "
                + "written.", request.EntityLogicalName, request.RecordId, exitRefusal.Reason, exitRefusal.RefusalCode);
            return exitRefusal;
        }

        await applyChange(ct).ConfigureAwait(false);

        if (!resolution.IsOwned)
        {
            return resolution; // Unchanged — the row keeps its owner.
        }

        var teamId = resolution.OwningTeamId!.Value;

        // Ownership is assigned on its own, not folded into the field update: Dataverse treats an owner change as a
        // distinct operation, and combining them is a documented way to have one of the two quietly not happen
        // (the ProvisionProjectEndpoint.AssignOwnerToSecureTeamAsync rationale).
        try
        {
            // The owner is read AFTER the change, never taken from the row read before it (task 146 b2): another writer
            // may have re-owned the row in between — FR-E7 category routing of an unfiled communication is one — and a
            // stale "already owned" answer would leave the re-filed row with that writer's owner.
            if (await ReadOwningTeamAsync(request, ct).ConfigureAwait(false) == teamId)
            {
                return resolution; // already owned by the resolved team
            }

            await _dataverse.UpdateAsync(
                request.EntityLogicalName,
                request.RecordId,
                new Dictionary<string, object> { [OwnerColumn] = new EntityReference(TeamEntity, teamId) },
                ct).ConfigureAwait(false);

            if (await ReadOwningTeamAsync(request, ct).ConfigureAwait(false) != teamId)
            {
                throw new InvalidOperationException(
                    $"The owner of {request.EntityLogicalName} {request.RecordId:D} did not read back as team {teamId:D} "
                    + "after the reassignment; the re-filed row may be readable outside its new parent's team (task 146).");
            }
        }
        catch (Exception assignFailure)
        {
            // The change is already written and the owner may not be: the row would be filed under its NEW parents while
            // still owned for its OLD ones — for a move under a secure record, readable in an ordinary business unit
            // (ADR-003 fail-open, task 146 b1/b2 verifier item). Recover first, then surface the failure.
            if (await RecoverFromFailedAssignmentAsync(request, restore, resolution, assignFailure).ConfigureAwait(false))
            {
                return resolution; // the assignment landed despite the failure report: filing and owner agree
            }

            throw;
        }

        _logger.LogInformation(
            "Re-filed {Entity} {RecordId}: owner reassigned to team {TeamId}.",
            request.EntityLogicalName, request.RecordId, teamId);
        return resolution;
    }

    /// <summary>The team that owns the row now (<c>owningteam</c>), or <c>null</c> when a user owns it.</summary>
    private async Task<Guid?> ReadOwningTeamAsync(RecordReparent request, CancellationToken ct)
    {
        var row = await _dataverse.RetrieveAsync(
            request.EntityLogicalName, request.RecordId, new[] { OwningTeamColumn }, ct).ConfigureAwait(false);
        return row?.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id;
    }

    /// <summary>Who sent the row's create — the F3 creator for a row a person created (task 146 c1).</summary>
    private const string CreatedByColumn = "createdby";

    /// <summary>
    /// Dataverse error <c>0x80041103</c> (QueryBuilderNoAttribute) as a signed 32-bit integer, which is how
    /// <see cref="OrganizationServiceFault.ErrorCode"/> exposes it: a query named an attribute this table does not have.
    /// Measured read-only against spaarkedev1 on 2026-10-03: a FetchXml query naming the column on a table without it
    /// answered this code. Typed on the code, not the (localized) message — the house idiom
    /// (<c>RecordContainerResolver.IsRecordNotFound</c>).
    /// </summary>
    private const int AttributeDoesNotExistErrorCode = unchecked((int)0x80041103);

    /// <summary>
    /// The row's recorded creator person (<see cref="RecordCreatorPerson.Column"/>), read on its own because the
    /// every-column read returned no value for it (task 146 c1-r1). A row with the column but no value is "nobody
    /// recorded" (a definite answer); a table WITHOUT the column (its schema step has not run here) is
    /// <see cref="Sprk.Bff.Api.Services.Access.CreatorPersonAnswer.ColumnAbsent"/> — "could not tell", never "allowed". Any other fault propagates to
    /// the F3 helper, which reads it as "could not be checked".
    /// </summary>
    private async Task<Sprk.Bff.Api.Services.Access.CreatorPersonAnswer> ReadCreatorPersonAsync(RecordReparent request, CancellationToken ct)
    {
        var query = new QueryExpression(request.EntityLogicalName)
        {
            ColumnSet = new ColumnSet(RecordCreatorPerson.Column),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition($"{request.EntityLogicalName}id", ConditionOperator.Equal, request.RecordId);

        try
        {
            var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            return Sprk.Bff.Api.Services.Access.CreatorPersonAnswer.Recorded(row?.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id);
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            when (fault.Detail?.ErrorCode == AttributeDoesNotExistErrorCode)
        {
            return Sprk.Bff.Api.Services.Access.CreatorPersonAnswer.ColumnAbsent;
        }
    }

    /// <summary>
    /// Owner round 10 item 7 (task 146 c1): the refusal to send when the re-file takes the row out from under a secure
    /// root and the caller may not, or <c>null</c> when it may proceed. Nothing is written here.
    /// </summary>
    /// <remarks>
    /// <para><b>Which secure roots the row leaves.</b> The secure roots above the row's parents BEFORE the change, less
    /// those above its parents AFTER it (<see cref="SecureRootsAboveAsync"/>). Leaving one root for another secure root
    /// still leaves the first ("or to a different root"); adding a parent, or moving between ordinary records, leaves
    /// none and asks nothing.</para>
    /// <para><b>Who may.</b> <see cref="Sprk.Bff.Api.Services.Access.SecureDesignationRemoval"/> — the unsecure endpoint's
    /// F3 check — with Full Access asked on EVERY secure root left and the creator counted on the ROW
    /// (<c>sprk_createdbyperson</c>, else <c>createdby</c>). A writer that acts for no person
    /// (<see cref="RecordReparent.SecureExitCaller"/> null) is refused: there is nobody whose rights could be checked.</para>
    /// <para><b>Fail closed.</b> A row held in the Secure Record business unit that would leave its isolation although no
    /// secure root above it can be named (its secure parent is not one of its own columns — a message secured by the
    /// record thread it joined) admits only its creator. A filing deeper than <see cref="MaxLineageDepth"/> refuses, as
    /// the owner decision does.</para>
    /// </remarks>
    private async Task<RecordOwnerResolution?> RefuseUnlessPermittedToLeaveSecureRootsAsync(
        RecordReparent request, Entity current, IReadOnlyCollection<RecordOwnershipParent> filedUnderBefore,
        IReadOnlyCollection<RecordOwnershipParent> filedUnderAfter, RecordOwnerResolution resolution, CancellationToken ct)
    {
        // Two cheap facts first, so the many re-files that leave nothing (a thread JOIN, an invoice link, additive inbound
        // filing) cost no read: a change that removes none of the row's parents leaves no root (both walks would read the
        // same parents), and an answer that keeps the row secure or keeps its owner cannot take it out of isolation.
        var removesAParent = filedUnderBefore.Any(parent => !filedUnderAfter.Contains(parent));
        var mayLeaveIsolation = !resolution.IsSecureOwner && resolution.Outcome != RecordOwnerOutcome.Unchanged;
        if (!removesAParent && !mayLeaveIsolation)
        {
            return null;
        }

        var secureBu = await ResolveSecureBusinessUnitAsync(ct).ConfigureAwait(false);
        if (secureBu.Ambiguous)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
                "more than one business unit carries the Secure Record name, so whether the change leaves a secure record "
                + "cannot be decided");
        }

        (IReadOnlyList<RecordOwnershipParent> Roots, RecordOwnershipParent? TooDeepAt) none =
            (Array.Empty<RecordOwnershipParent>(), null);
        var before = removesAParent ? await SecureRootsAboveAsync(filedUnderBefore, secureBu.Id, ct).ConfigureAwait(false) : none;
        var after = removesAParent ? await SecureRootsAboveAsync(filedUnderAfter, secureBu.Id, ct).ConfigureAwait(false) : none;
        if ((before.TooDeepAt ?? after.TooDeepAt) is { } tooDeep)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.ParentUnresolved,
                $"the record's filing runs through more than {MaxLineageDepth} levels of records (still unresolved at "
                + $"{tooDeep.EntityLogicalName} {tooDeep.RecordId:D}), so whether the change leaves a secure record cannot "
                + "be decided");
        }

        var left = before.Roots.Where(root => !after.Roots.Contains(root)).ToList();

        // Held in the Secure Record business unit, about to leave its isolation, yet no secure root above it can be named.
        var unidentified = left.Count == 0
            && mayLeaveIsolation
            && secureBu.Id is { } secureBuId
            && current.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id == secureBuId;

        if (left.Count == 0 && !unidentified)
        {
            return null; // no secure root is left, and the row is not taken out of secure isolation
        }

        var decision = await Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.DecideAsync(
            new Sprk.Bff.Api.Services.Access.SecureRemovalQuestion
            {
                Caller = request.SecureExitCaller,
                SecuredRecords = left
                    .Select(root => new Sprk.Bff.Api.Services.Access.SecuredRecordRef(root.EntityLogicalName, root.RecordId))
                    .ToArray(),
                IncludesUnidentifiedSecureRecord = unidentified,
                CreatedBy = current.GetAttributeValue<EntityReference>(CreatedByColumn)?.Id,
                CreatedByPerson = current.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id,
                // c1-r1 (owner round 13 item 9): a child table now carries the person too. The every-column read cannot
                // tell "nobody recorded" from "this environment lacks the column", so when the row came back without it,
                // the column is read on its own — an absent column is "could not tell" (unverifiable), never "allowed"
                // (main-session condition 2, now for children as for roots). Asked only when nothing else admitted.
                ReadCreatedByPersonAsync =
                    RecordCreatorPerson.IsStamped(request.EntityLogicalName)
                    && !current.Attributes.ContainsKey(RecordCreatorPerson.Column)
                        ? token => ReadCreatorPersonAsync(request, token)
                        : null,
            },
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "F3 on re-filing {Entity} {RecordId} out of {Count} secure record(s): {Outcome} ({Basis}), caller {CallerId}.",
            request.EntityLogicalName, request.RecordId, left.Count, decision.Outcome, decision.Basis,
            decision.CallerSystemUserId);

        return decision.IsPermitted
            ? null
            : RecordOwnerResolution.SecureRemovalRefused(decision, ChildNoun(request.EntityLogicalName));
    }

    /// <summary>The noun a move-out message names the row by ("document", "event", "to-do", …).</summary>
    private static string ChildNoun(string entityLogicalName) => entityLogicalName.Trim().ToLowerInvariant() switch
    {
        "sprk_todo" => "to-do",
        "sprk_communication" => "message",
        "sprk_communicationattachment" => "attachment",
        "sprk_emailreviewlog" => "review log",
        var table when table.StartsWith("sprk_", StringComparison.Ordinal) => table[5..],
        var table => table,
    };

    /// <summary>
    /// The secure ROOTS (the three <c>sprk_issecure</c> tables) that <paramref name="parents"/> sit under: each parent
    /// that is such a root and is owned in the Secure Record business unit or flagged <c>sprk_issecure</c>, and — for a
    /// parent that is itself a child — the secure roots above what IT is filed under, up to <see cref="MaxLineageDepth"/>
    /// levels (team-owned or not: a child under a secure-team-owned communication is still under that communication's
    /// root). A missing row is not a root to leave. <c>TooDeepAt</c> names a filing left unread at the limit; a Dataverse
    /// fault propagates.
    /// </summary>
    private async Task<(IReadOnlyList<RecordOwnershipParent> Roots, RecordOwnershipParent? TooDeepAt)> SecureRootsAboveAsync(
        IEnumerable<RecordOwnershipParent> parents, Guid? secureBuId, CancellationToken ct)
    {
        static RecordOwnershipParent Normalize(RecordOwnershipParent p) =>
            p with { EntityLogicalName = p.EntityLogicalName.Trim().ToLowerInvariant() };

        var roots = new List<RecordOwnershipParent>();
        var seen = new HashSet<RecordOwnershipParent>();
        var frontier = parents.Where(p => p.IsSpecified).Select(Normalize).Distinct().ToList();

        for (var depth = 0; depth <= MaxLineageDepth && frontier.Count > 0; depth++)
        {
            var next = new List<RecordOwnershipParent>();
            foreach (var node in frontier)
            {
                if (!seen.Add(node))
                    continue;

                if (SecureFlaggedRoots.Contains(node.EntityLogicalName))
                {
                    // Live facts only: F3 asks which secure roots the row leaves NOW (no planned-owner overlay).
                    var fact = await ReadParentAsync(node, plannedOwningTeams: null, ct).ConfigureAwait(false);
                    if (fact is not null && (fact.FlaggedSecure || (secureBuId is { } sbu && fact.BusinessUnitId == sbu)))
                        roots.Add(node);
                }
                else if (IsReparentableChild(node.EntityLogicalName))
                {
                    var filing = await ReadOwnershipParentsOfAsync(node, ct).ConfigureAwait(false);
                    if (filing is not null)
                        next.AddRange(filing.Select(Normalize));
                }
            }

            frontier = next.Where(n => !seen.Contains(n)).Distinct().ToList();
        }

        return (roots, frontier.Count > 0 ? frontier[0] : null);
    }

    /// <summary>Event id of a re-file whose owner assignment failed AFTER the change was written and whose filing was
    /// put back (task 146 b1/b2).</summary>
    public static readonly EventId ReparentOwnerAssignmentFailed = new(14601, nameof(ReparentOwnerAssignmentFailed));

    /// <summary>Event id of a re-file left needing manual repair after its owner assignment failed: the filing could not
    /// be put back, or the owner could not be confirmed (task 146 b1/b2). The log names the row and which way it is
    /// wrong.</summary>
    public static readonly EventId ReparentLeftInconsistent = new(14602, nameof(ReparentLeftInconsistent));

    /// <summary>
    /// After the owner assignment that must follow a written change fails, makes the row's filing and its owner agree
    /// again — and never in the direction that exposes a secure record's child (ADR-003). App-only, with no cancellation
    /// (the caller's token may be what failed). Returns <c>true</c> only when the row turns out to be owned by the
    /// resolved team after all (a lost response): the re-file stands. Otherwise the caller rethrows the original
    /// failure, which the writer reports in its own contract.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>Owner reads back as the resolved team</b> → the assignment landed; nothing to undo.</item>
    /// <item><b>Owner reads back as anything else</b> → the assignment did not land: the columns the change moved (its
    /// parent lookups and <see cref="RecordReparent.AttachColumns"/>) are written back to their previous values, so the
    /// row is filed where the owner it still has belongs (<see cref="ReparentOwnerAssignmentFailed"/>).</item>
    /// <item><b>Owner cannot be read</b> → which way the row is wrong is unknown, so the SAFE direction is taken. When the
    /// resolved team is the Secure team (a move INTO a secure record) the filing is put back: whether or not the
    /// assignment landed, the row is then never filed under the secure record while owned elsewhere (at worst it is owned
    /// by the Secure team under its old parents — over-restricted). When the resolved team is ordinary (a move out of, or
    /// between, ordinary records) the filing is kept: at worst the row keeps a stricter old owner. Either way it is logged
    /// <see cref="ReparentLeftInconsistent"/> for repair.</item>
    /// </list>
    /// A restore that itself fails is logged <see cref="ReparentLeftInconsistent"/>.
    /// </remarks>
    private async Task<bool> RecoverFromFailedAssignmentAsync(
        RecordReparent request, IReadOnlyDictionary<string, object> restore, RecordOwnerResolution resolution,
        Exception assignFailure)
    {
        var teamId = resolution.OwningTeamId!.Value;

        bool? landed;
        try
        {
            landed = await ReadOwningTeamAsync(request, CancellationToken.None).ConfigureAwait(false) == teamId;
        }
        catch (Exception readFailure)
        {
            _logger.LogWarning(readFailure,
                "Re-file of {Entity} {RecordId}: after a failed owner assignment its owner could not be read back.",
                request.EntityLogicalName, request.RecordId);
            landed = null;
        }

        if (landed == true)
        {
            _logger.LogWarning(assignFailure,
                "Re-file of {Entity} {RecordId}: the owner assignment to team {TeamId} reported a failure, but the owner reads "
                + "back as that team — the assignment landed (lost response); the re-file stands (task 146).",
                request.EntityLogicalName, request.RecordId, teamId);
            return true;
        }

        if (landed is null && !resolution.IsSecureOwner)
        {
            _logger.LogCritical(
                ReparentLeftInconsistent, assignFailure,
                "Re-file of {Entity} {RecordId} needs checking: the owner assignment to the ORDINARY team {TeamId} failed and "
                + "the owner could not be read back. The new filing is kept (it names no secure record), so the row is owned "
                + "either by that team or still by its previous owner — re-run the re-file to settle it (task 146).",
                request.EntityLogicalName, request.RecordId, teamId);
            return false;
        }

        await RestoreFilingAsync(request, restore, teamId, assignFailure, ownerUnknown: landed is null).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Writes back what the row was filed under before the change — the columns the change moved
    /// (<paramref name="restore"/>: the parent lookups it set or cleared, and its <see cref="RecordReparent.AttachColumns"/>)
    /// — so the row's filing matches the owner it still has. Every outcome is a CRITICAL log naming the row; a restore
    /// that itself fails is logged under <see cref="ReparentLeftInconsistent"/> for manual repair. Never throws.
    /// </summary>
    private async Task RestoreFilingAsync(
        RecordReparent request, IReadOnlyDictionary<string, object> restore, Guid teamId, Exception assignFailure,
        bool ownerUnknown)
    {
        if (restore.Count == 0)
        {
            _logger.LogCritical(
                ReparentLeftInconsistent, assignFailure,
                "Re-file of {Entity} {RecordId}: the change was written but the owner could not be assigned to team "
                + "{TeamId}, and the change named no column to put back. Unless the writer undoes its own change (the "
                + "invoice confirm does), the row may be readable outside its new parent's team until it is re-owned "
                + "(task 146).",
                request.EntityLogicalName, request.RecordId, teamId);
            return;
        }

        try
        {
            await _dataverse.UpdateAsync(
                request.EntityLogicalName, request.RecordId, new Dictionary<string, object>(restore),
                CancellationToken.None).ConfigureAwait(false);

            if (ownerUnknown)
            {
                _logger.LogCritical(
                    ReparentLeftInconsistent, assignFailure,
                    "Re-file of {Entity} {RecordId} ROLLED BACK, owner unconfirmed: the assignment to the Secure team {TeamId} "
                    + "failed and the owner could not be read back, so its filing columns ({Columns}) were restored. If the "
                    + "assignment did land, the row is owned by the Secure team under its previous parents (over-restricted, "
                    + "not exposed) — re-run the re-file (task 146).",
                    request.EntityLogicalName, request.RecordId, teamId, string.Join(", ", restore.Keys));
            }
            else
            {
                _logger.LogCritical(
                    ReparentOwnerAssignmentFailed, assignFailure,
                    "Re-file of {Entity} {RecordId} ROLLED BACK: the owner could not be assigned to team {TeamId} after the "
                    + "change, so its filing columns ({Columns}) were restored to match the owner it still has. The caller "
                    + "receives the failure (task 146).",
                    request.EntityLogicalName, request.RecordId, teamId, string.Join(", ", restore.Keys));
            }
        }
        catch (Exception restoreFailure)
        {
            _logger.LogCritical(
                ReparentLeftInconsistent, restoreFailure,
                "Re-file of {Entity} {RecordId} is INCONSISTENT and needs manual repair: the owner could not be assigned "
                + "to team {TeamId} ({AssignFailure}) and restoring its filing columns ({Columns}) also failed. It is filed "
                + "under its new parents while owned for its old ones (task 146).",
                request.EntityLogicalName, request.RecordId, teamId, assignFailure.Message, string.Join(", ", restore.Keys));
        }
    }

    /// <summary>
    /// A target type in either spelling a caller may hold. LOAD-BEARING for every filed Office save: the save
    /// endpoint accepts only the FRIENDLY form (<c>matter</c>, <c>project</c>, …), and so does the queued job's
    /// <c>AssociationType</c> — reading <c>matter</c> as an entity would fail. The alias table is
    /// <see cref="DocumentAssociationMap"/>'s, not a copy (the container resolver's call site uses the same one,
    /// GitHub #1038). A name outside it (a To Do's <c>sprk_document</c> / <c>sprk_communication</c> carrier, or a
    /// To Do regarding's <c>sprk_matter</c>) is already logical and passes through.
    /// </summary>
    internal static string ToTargetLogicalName(string entityTypeOrAlias) =>
        DocumentAssociationMap.ToLogicalName(entityTypeOrAlias) ?? entityTypeOrAlias.Trim().ToLowerInvariant();

    /// <summary>
    /// The primary target (as named, alias-normalized — the pre-146 contract) first, then every ownership-parent
    /// entry of <see cref="RecordOwnershipContext.Parents"/>, without duplicates.
    /// </summary>
    private static List<RecordOwnershipParent> CollectParents(RecordOwnershipContext context)
    {
        var parents = new List<RecordOwnershipParent>();
        if (context.HasTarget)
        {
            parents.Add(new RecordOwnershipParent(
                ToTargetLogicalName(context.TargetEntityLogicalName!), context.TargetRecordId!.Value));
        }

        foreach (var parent in context.Parents)
        {
            if (!parent.IsSpecified || !IsOwnershipParent(parent.EntityLogicalName))
                continue;

            var normalized = parent with { EntityLogicalName = parent.EntityLogicalName.Trim().ToLowerInvariant() };
            if (!parents.Contains(normalized))
                parents.Add(normalized);
        }

        return parents;
    }

    /// <summary>One parent's ownership facts.</summary>
    private sealed record ParentFacts(
        RecordOwnershipParent Parent, Guid BusinessUnitId, bool HasOwningTeam, bool FlaggedSecure);

    /// <summary>
    /// Secure-if-any over every parent. Order of the checks is the point: an unreadable parent refuses before any
    /// answer (it might be the secure one); a flagged-but-not-isolated root refuses before the secure branch (its
    /// children must not inherit the ordinary BU its ownership still shows); any parent in the Secure Record BU wins
    /// over every ordinary parent; only then does the primary parent's business unit decide an ordinary child.
    /// </summary>
    private async Task<RecordOwnerResolution> ResolveFromParentsAsync(
        IReadOnlyList<RecordOwnershipParent> parents, RecordOwnershipContext context, CancellationToken ct)
    {
        var facts = new List<ParentFacts>(parents.Count);
        foreach (var parent in parents)
        {
            var fact = await ReadParentAsync(parent, context.PlannedOwningTeams, ct).ConfigureAwait(false);
            if (fact is null)
            {
                // ⛔ REFUSE — do NOT fall back to the acting user when a parent WAS named but could not be
                // resolved. The secure-record case RecordContainerResolver documents (task 076,
                // notes/secure-project-workflow-review-2026-08-24.md §A): users sit in the Operations subtree while
                // SECURE records are owned in `Secure Record`. Falling back to the acting user's business unit would
                // assign a secure record's child to the general Operations team. An indeterminate answer read as
                // "not secure" is the same isolation failure with an extra step, so indeterminate must refuse.
                _logger.LogWarning(
                    "Refusing to resolve an owning team: parent {ParentEntity} {ParentId} was named but its "
                    + "business unit could not be read. NOT falling back to the acting user — for a secure record "
                    + "that would assign it to the caller's general business unit and defeat its isolation "
                    + "(task 076 / task 080 / task 146).",
                    parent.EntityLogicalName, parent.RecordId);
                return RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the parent {parent.EntityLogicalName} {parent.RecordId:D} does not exist or has no owning business unit");
            }

            facts.Add(fact);
        }

        // A content row of a parent that is not team-owned: the PARENT's own filing decides (task 146 verifier item 3).
        // Its owner says nothing about secrecy — a run-as-user, client-created or pre-146 communication is user-owned
        // even when it is filed to a secure matter — so the parent's own parent lookups are read. Filed → the content
        // row resolves from what the parent is filed under (secure-if-any), never the parent's creator. Unfiled → the
        // content row keeps its creator, as its parent did (E1).
        if (context.KeepCreatorUnlessTargetIsTeamOwned && !facts[0].HasOwningTeam)
        {
            var parentFiling = await ReadOwnershipParentsOfAsync(facts[0].Parent, ct).ConfigureAwait(false);
            if (parentFiling is null)
            {
                return RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the parent {facts[0].Parent.EntityLogicalName} {facts[0].Parent.RecordId:D} could not be read");
            }

            var inherited = parentFiling
                .Concat(facts.Skip(1).Select(f => f.Parent))
                .Where(p => p != facts[0].Parent)
                .Distinct()
                .ToList();
            if (inherited.Count == 0)
            {
                return RecordOwnerResolution.Unchanged(
                    $"the parent {facts[0].Parent.EntityLogicalName} {facts[0].Parent.RecordId:D} is not team-owned and is "
                    + "filed under nothing (an unfiled row that kept its creator, task 146 E1); its content row keeps its "
                    + "creator too");
            }

            return await ResolveFromParentsAsync(
                inherited, context with { KeepCreatorUnlessTargetIsTeamOwned = false }, ct).ConfigureAwait(false);
        }

        // A parent that is itself a CHILD and is NOT team-owned (a run-as-user, client-created or pre-146 row) says nothing
        // about secrecy through its own business unit — a user-owned document filed to a secure matter sits in its
        // creator's ordinary unit. Its filing is read, transitively, so a secure ancestor makes THIS row secure and a
        // flagged-but-not-isolated one refuses (task 146 r2, verifier item 10 — the look-through ContentOf already did,
        // now for every context: an analysis of such a document, a Compose promote, a grandchild of any writer). The
        // ancestors only take part in the secure decision; an ordinary row still takes the PRIMARY parent's unit, so
        // nothing changes for a child of ordinary records.
        var lineage = await ReadUnownedChildLineageAsync(facts, context.PlannedOwningTeams, ct).ConfigureAwait(false);
        if (lineage.Unresolved is { } unresolved)
        {
            return lineage.TooDeep
                ? RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the record's filing runs through more than {MaxLineageDepth} levels of records that are not team-owned "
                    + $"(still unresolved at {unresolved.EntityLogicalName} {unresolved.RecordId:D}), so whether it sits "
                    + "under a secure record cannot be decided")
                : RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the parent {unresolved.EntityLogicalName} {unresolved.RecordId:D} (an ancestor of this record) does not "
                    + "exist or has no owning business unit");
        }

        var decisive = facts.Concat(lineage.Facts).ToList();

        var secureBu = await ResolveSecureBusinessUnitAsync(ct).ConfigureAwait(false);
        if (secureBu.Ambiguous)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
                "more than one business unit carries the Secure Record name, so whether a parent is secure cannot be decided");
        }

        // A root flagged secure but not owned in the Secure Record BU is a failed or interrupted provisioning (C11).
        // Its ownership says "ordinary"; its flag says "secure". Fail closed: refuse, never the ordinary team.
        // Task 148: the one root an unsecure transition names (UnsecuringRoot) is mid-transition, not a failed provisioning —
        // its ownership has already been moved off the Secure team and read back, and its flag is cleared last.
        var unsecuring = context.UnsecuringRoot is { IsSpecified: true } u
            ? u with { EntityLogicalName = u.EntityLogicalName.Trim().ToLowerInvariant() }
            : null;
        var notIsolated = decisive.FirstOrDefault(f =>
            f.FlaggedSecure && f.BusinessUnitId != secureBu.Id && f.Parent != unsecuring);
        if (notIsolated is not null)
        {
            _logger.LogError(
                "Refusing to resolve an owning team: parent {ParentEntity} {ParentId} is flagged sprk_issecure but is "
                + "owned in business unit {BusinessUnitId}, not the Secure Record business unit — a failed or "
                + "interrupted provisioning (task 146 / C11). Its children are not given an ordinary owner.",
                notIsolated.Parent.EntityLogicalName, notIsolated.Parent.RecordId, notIsolated.BusinessUnitId);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureParentNotIsolated,
                $"the parent {notIsolated.Parent.EntityLogicalName} {notIsolated.Parent.RecordId:D} is marked secure "
                + "but is not isolated (its provisioning did not complete); re-run Make Secure on it, then retry");
        }

        if (secureBu.Id is { } secureBuId && decisive.Any(f => f.BusinessUnitId == secureBuId))
        {
            return await ResolveNamedSecureOwnerTeamAsync(secureBuId, ct).ConfigureAwait(false);
        }

        return await ResolveDefaultOwnerTeamAsync(facts[0].BusinessUnitId, ct).ConfigureAwait(false);
    }

    /// <summary>How many filing levels <see cref="ReadUnownedChildLineageAsync"/> follows above a parent.</summary>
    internal const int MaxLineageDepth = 4;

    /// <summary>
    /// The ancestors that decide whether a row is secure when one of its parents cannot say so itself: for every parent
    /// that is a CHILD table (<see cref="IsReparentableChild"/>) and is not team-owned, the records it is filed under —
    /// and, when one of those is again an un-team-owned child, the records IT is filed under, up to
    /// <see cref="MaxLineageDepth"/> levels (task 146 r2, verifier item 10). A team-owned parent is not followed: its
    /// owner already IS the resolver's answer for its own filing. <c>Unresolved</c> names an ancestor that could not be
    /// read; a Dataverse fault propagates.
    /// </summary>
    /// <remarks>
    /// <b>The depth limit refuses; it never answers "ordinary"</b> (task 146 b1, verifier LOW item). A chain still
    /// un-team-owned after <see cref="MaxLineageDepth"/> levels has an UNREAD filing above it, which might be secure —
    /// read as ordinary, it would hand a secure record's descendant an ordinary team (ADR-003). So a frontier left over
    /// at the limit comes back as <c>Unresolved</c> with <c>TooDeep</c>, and the resolver refuses
    /// (<see cref="RecordOwnerRefusal.ParentUnresolved"/>).
    /// </remarks>
    private async Task<(IReadOnlyList<ParentFacts> Facts, RecordOwnershipParent? Unresolved, bool TooDeep)> ReadUnownedChildLineageAsync(
        IReadOnlyList<ParentFacts> facts, IReadOnlyDictionary<RecordOwnershipParent, Guid>? plannedOwningTeams, CancellationToken ct)
    {
        var lineage = new List<ParentFacts>();
        var seen = new HashSet<RecordOwnershipParent>(facts.Select(f => f.Parent));
        var frontier = facts.Where(IsUnownedChild).Select(f => f.Parent).ToList();

        for (var depth = 0; depth < MaxLineageDepth && frontier.Count > 0; depth++)
        {
            var next = new List<RecordOwnershipParent>();
            foreach (var child in frontier)
            {
                var filing = await ReadOwnershipParentsOfAsync(child, ct).ConfigureAwait(false);
                if (filing is null)
                {
                    return (lineage, child, false);
                }

                foreach (var ancestor in filing.Select(p => p with { EntityLogicalName = p.EntityLogicalName.ToLowerInvariant() }))
                {
                    if (!seen.Add(ancestor))
                        continue;

                    var fact = await ReadParentAsync(ancestor, plannedOwningTeams, ct).ConfigureAwait(false);
                    if (fact is null)
                    {
                        return (lineage, ancestor, false);
                    }

                    lineage.Add(fact);
                    if (IsUnownedChild(fact))
                        next.Add(ancestor);
                }
            }

            frontier = next;
        }

        if (frontier.Count > 0)
        {
            _logger.LogWarning(
                "Refusing to resolve an owning team: the filing chain is still not team-owned after {MaxDepth} levels "
                + "(at {Entity} {RecordId}); whether it sits under a secure record cannot be decided (task 146).",
                MaxLineageDepth, frontier[0].EntityLogicalName, frontier[0].RecordId);
            return (lineage, frontier[0], true);
        }

        return (lineage, null, false);

        static bool IsUnownedChild(ParentFacts fact) =>
            !fact.HasOwningTeam && IsReparentableChild(fact.Parent.EntityLogicalName);
    }

    /// <summary>
    /// The ownership parents <paramref name="row"/> is itself filed under — every ownership-parent lookup value it
    /// carries (task 146 verifier item 3). Every column is read, for the reason <see cref="ReparentAsync"/> gives: a
    /// row's parents are whatever lookups it carries, and a per-table column list is the guess this task forbids. The
    /// read happens only for a CONTENT row whose parent is not team-owned. <c>null</c> when the row cannot be read; a
    /// Dataverse fault propagates.
    /// </summary>
    private async Task<IReadOnlyList<RecordOwnershipParent>?> ReadOwnershipParentsOfAsync(
        RecordOwnershipParent row, CancellationToken ct)
    {
        var query = new QueryExpression(row.EntityLogicalName)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition($"{row.EntityLogicalName}id", ConditionOperator.Equal, row.RecordId);
        var entity = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return entity is null ? null : RecordOwnershipContext.ParentsOf(entity.Attributes);
    }

    /// <summary>
    /// Reads one parent's <c>owningbusinessunit</c> (and <c>owningteam</c>; and <c>sprk_issecure</c> for the three
    /// secure-flagged roots). A missing row, or one with no owning business unit, is an ANSWER — "there is no team"
    /// — and the caller refuses. A Dataverse FAULT is not an answer: it propagates, so a throttled or timed-out read
    /// surfaces as the caller's retryable 5xx instead of a permanent refusal that tells the user to check the record.
    /// </summary>
    /// <remarks>
    /// Task 148 r2: a parent in <paramref name="plannedOwningTeams"/> (a report-only pass's planned owner) is read as owned
    /// by that team — its owning business unit is the team's, read from the team row — while its existence and flag are
    /// still read from the parent's own row. A planned team that cannot be found answers "no team" (the caller refuses).
    /// </remarks>
    private async Task<ParentFacts?> ReadParentAsync(
        RecordOwnershipParent parent, IReadOnlyDictionary<RecordOwnershipParent, Guid>? plannedOwningTeams, CancellationToken ct)
    {
        var isSecureFlaggedRoot = SecureFlaggedRoots.Contains(parent.EntityLogicalName);
        var columns = isSecureFlaggedRoot
            ? new ColumnSet(OwningBusinessUnitColumn, OwningTeamColumn, IsSecureColumn)
            : new ColumnSet(OwningBusinessUnitColumn, OwningTeamColumn);

        var query = new QueryExpression(parent.EntityLogicalName)
        {
            ColumnSet = columns,
            TopCount = 1,
            NoLock = true
        };
        query.Criteria.AddCondition($"{parent.EntityLogicalName}id", ConditionOperator.Equal, parent.RecordId);

        var results = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var row = results.Entities.FirstOrDefault();
        var businessUnitId = row?.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id;

        if (row is null || businessUnitId is null || businessUnitId == Guid.Empty)
        {
            _logger.LogWarning(
                "Parent {EntityLogicalName} {RecordId} does not exist or has no owning business unit.",
                parent.EntityLogicalName, parent.RecordId);
            return null;
        }

        var hasOwningTeam = row.GetAttributeValue<EntityReference>(OwningTeamColumn) is { } team && team.Id != Guid.Empty;

        // An EMPTY sprk_issecure fails CLOSED (task 150, round 17 item 3): since the task 150 backfill every row holds true or
        // false and the column is field-secured, so empty means the BFF lost its field-level Read and the real value was
        // masked. Read as flagged: a root that is not isolated is then refused (never an ordinary team); one that IS isolated
        // takes the secure branch by its BU anyway.
        var flaggedSecure = isSecureFlaggedRoot && (row.GetAttributeValue<bool?>(IsSecureColumn) ?? true);

        if (plannedOwningTeams is not null
            && plannedOwningTeams.TryGetValue(
                parent with { EntityLogicalName = parent.EntityLogicalName.Trim().ToLowerInvariant() }, out var plannedTeam))
        {
            var teamQuery = new QueryExpression(TeamEntity)
            {
                ColumnSet = new ColumnSet(BusinessUnitColumn),
                TopCount = 1,
                NoLock = true
            };
            teamQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, plannedTeam);
            var plannedBu = (await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false))
                .Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>(BusinessUnitColumn)?.Id;
            if (plannedBu is null || plannedBu == Guid.Empty)
            {
                _logger.LogWarning(
                    "The team {TeamId} planned for {EntityLogicalName} {RecordId} does not exist or has no business unit.",
                    plannedTeam, parent.EntityLogicalName, parent.RecordId);
                return null;
            }

            return new ParentFacts(parent, plannedBu.Value, HasOwningTeam: true, flaggedSecure);
        }

        return new ParentFacts(parent, businessUnitId.Value, hasOwningTeam, flaggedSecure);
    }

    /// <summary>
    /// Resolves the acting user's business unit, then that BU's owner team.
    /// <paramref name="userKeyColumn"/> selects which identity the caller is known by.
    /// </summary>
    private async Task<RecordOwnerResolution> ResolveFromUserAsync(string userKeyColumn, Guid userKey, CancellationToken ct)
    {
        // TOP 2, not TOP 1 — the same contract RecordContainerResolver.ResolveForActingUserAsync keeps for the
        // same fact. One row is the answer; two mean the key maps to more than one Dataverse user and the
        // business unit is AMBIGUOUS. TOP 1 would silently pick a winner, so this component and the
        // container resolver could put one save's bytes and its row in different business units.
        var userQuery = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet(BusinessUnitColumn),
            TopCount = 2,
            NoLock = true
        };
        userQuery.Criteria.AddCondition(userKeyColumn, ConditionOperator.Equal, userKey);

        var users = await _dataverse.RetrieveMultipleAsync(userQuery, ct).ConfigureAwait(false);
        if (users.Entities.Count > 1)
        {
            _logger.LogError(
                "Cannot resolve an owning team: {UserKeyColumn}={UserKey} matches more than one Dataverse "
                + "user, so the business unit is ambiguous. Refusing rather than choosing one.",
                userKeyColumn, userKey);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.ActingUserUnresolved,
                "the acting user matches more than one Dataverse user, so their business unit is ambiguous");
        }

        var businessUnitId = users.Entities.FirstOrDefault()
            ?.GetAttributeValue<EntityReference>(BusinessUnitColumn)?.Id;

        if (businessUnitId is null || businessUnitId == Guid.Empty)
        {
            _logger.LogWarning(
                "Cannot resolve an owning team: no systemuser with {UserKeyColumn}={UserKey}, or that user "
                + "has no business unit.",
                userKeyColumn, userKey);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.ActingUserUnresolved,
                "the acting user has no Dataverse user record with a business unit");
        }

        // As for a parent read: "no such user" / "no business unit" is an answer (refuse); a Dataverse
        // fault propagates as the caller's retryable 5xx, never as a refusal that blames the user's setup.
        //
        // The user path should never meet the Secure Record BU (it holds no users; provisioning and the census job
        // refuse and report otherwise), but if it does, the answer is still the named team or a refusal — never the
        // default team task 144 retired.
        var secureBu = await ResolveSecureBusinessUnitAsync(ct).ConfigureAwait(false);
        if (secureBu.Ambiguous)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
                "more than one business unit carries the Secure Record name, so the acting user's business unit cannot be classified");
        }

        return secureBu.Id == businessUnitId
            ? await ResolveNamedSecureOwnerTeamAsync(businessUnitId.Value, ct).ConfigureAwait(false)
            : await ResolveDefaultOwnerTeamAsync(businessUnitId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Which business unit is the Secure Record BU, decided by ID: the BU named by
    /// <c>SecureRecord:BusinessUnitName</c> (default <c>Secure Record</c>), looked up with TOP 2. Two matches are
    /// <c>Ambiguous</c> (refuse). No match means this environment has no Secure Record BU, so no BU can be it.
    /// </summary>
    /// <remarks>
    /// <para>No Secure Record BU is the right answer for an environment without secure records, and for a
    /// misconfigured name it is still not silent: provisioning refuses with <c>secure_bu_not_found</c>, the census
    /// job reports the BU missing, and once the setup-guide cutover has removed the role from the default team,
    /// Dataverse refuses any assignment to it. (A root FLAGGED secure in such an environment refuses here as not
    /// isolated — task 146.)</para>
    /// <para><b>Documented fail-open edge — until the live cutover.</b> With a misconfigured
    /// <c>SecureRecord:BusinessUnitName</c>, a child filed to a secure record resolves to the Secure Record BU's
    /// DEFAULT team. Of the mitigations above, only the last makes Dataverse refuse that assignment, and it exists
    /// only after guide §4.3 step 4 (the <c>Secure Record Owner</c> role removed from the default team). Before that
    /// step, the assignment succeeds and is visible only through the provisioning refusal and the census job's inert
    /// warning. Accepted by the task-144 verifier (round 2) on that condition; the PR names it.</para>
    /// </remarks>
    private async Task<(Guid? Id, bool Ambiguous)> ResolveSecureBusinessUnitAsync(CancellationToken ct)
    {
        var secureBuName = SecureRecordOwnerTeam.BusinessUnitName(_configuration);

        var buQuery = new QueryExpression(BusinessUnitEntity)
        {
            ColumnSet = new ColumnSet(BusinessUnitColumn),
            TopCount = 2,
            NoLock = true
        };
        buQuery.Criteria.AddCondition("name", ConditionOperator.Equal, secureBuName);

        var secureBus = await _dataverse.RetrieveMultipleAsync(buQuery, ct).ConfigureAwait(false);
        if (secureBus.Entities.Count > 1)
        {
            _logger.LogError(
                "Cannot resolve an owning team: more than one business unit is named '{SecureBuName}' "
                + "(SecureRecord:BusinessUnitName), so whether a business unit is the Secure Record business unit "
                + "cannot be decided. Refusing rather than guessing (task 144).",
                secureBuName);
            return (null, true);
        }

        return secureBus.Entities.Count == 1 && secureBus.Entities[0].Id != Guid.Empty
            ? (secureBus.Entities[0].Id, false)
            : (null, false);
    }

    /// <summary>
    /// The Secure Record business unit's NAMED owner team: the configured name, an Owner team, NOT the default team.
    /// Exactly one match, or a refusal — never the default team as a fallback.
    /// </summary>
    private async Task<RecordOwnerResolution> ResolveNamedSecureOwnerTeamAsync(Guid secureBusinessUnitId, CancellationToken ct)
    {
        var teamName = SecureRecordOwnerTeam.OwnerTeamName(_configuration);

        var teamQuery = new QueryExpression(TeamEntity)
        {
            ColumnSet = new ColumnSet("teamid"),
            TopCount = 2,
            NoLock = true
        };
        teamQuery.Criteria.AddCondition(BusinessUnitColumn, ConditionOperator.Equal, secureBusinessUnitId);
        teamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, teamName);
        teamQuery.Criteria.AddCondition("teamtype", ConditionOperator.Equal, OwnerTeamType);
        teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, false);

        var teams = await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false);
        if (teams.Entities.Count != 1 || teams.Entities[0].Id == Guid.Empty)
        {
            _logger.LogError(
                "Cannot resolve an owning team: the Secure Record business unit {BusinessUnitId} has {Count} "
                + "non-default Owner team(s) named '{TeamName}' (SecureRecord:OwnerTeamName); exactly one is "
                + "required. Refusing — a record in the Secure Record business unit is never owned by its default "
                + "team (task 144, #967).",
                secureBusinessUnitId, teams.Entities.Count, teamName);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureOwnerTeamUnresolved,
                $"the secure record's owner team '{teamName}' is missing or not unique in the Secure Record business unit");
        }

        return RecordOwnerResolution.Owned(teams.Entities[0].Id) with { IsSecureOwner = true };
    }

    /// <summary>
    /// A business unit's DEFAULT OWNER team. Both predicates are load-bearing — see <see cref="OwnerTeamType"/>.
    /// </summary>
    private async Task<RecordOwnerResolution> ResolveDefaultOwnerTeamAsync(Guid businessUnitId, CancellationToken ct)
    {
        // TOP 2 for the same reason as the user lookup. Dataverse keeps exactly one default team per business
        // unit, so a second row means the two predicates are not selecting what they claim to — refuse rather
        // than own the record by whichever row came back first.
        var teamQuery = new QueryExpression(TeamEntity)
        {
            ColumnSet = new ColumnSet("teamid"),
            TopCount = 2,
            NoLock = true
        };
        teamQuery.Criteria.AddCondition(BusinessUnitColumn, ConditionOperator.Equal, businessUnitId);
        teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
        teamQuery.Criteria.AddCondition("teamtype", ConditionOperator.Equal, OwnerTeamType);

        var teams = await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false);
        if (teams.Entities.Count > 1)
        {
            _logger.LogError(
                "Business unit {BusinessUnitId} returned more than one default owner team "
                + "(isdefault = true AND teamtype = {OwnerTeamType}). Refusing rather than choosing one.",
                businessUnitId, OwnerTeamType);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.NoDefaultOwnerTeam,
                $"business unit {businessUnitId:D} has more than one default owner team");
        }

        var teamId = teams.Entities.FirstOrDefault()?.Id;

        if (teamId is null || teamId == Guid.Empty)
        {
            _logger.LogWarning(
                "Business unit {BusinessUnitId} has no default owner team "
                + "(isdefault = true AND teamtype = {OwnerTeamType}).",
                businessUnitId, OwnerTeamType);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.NoDefaultOwnerTeam,
                $"business unit {businessUnitId:D} has no default owner team");
        }

        _logger.LogDebug(
            "Resolved owning team {TeamId} for business unit {BusinessUnitId}.", teamId, businessUnitId);
        return RecordOwnerResolution.Owned(teamId.Value);
    }
}
