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
    /// <para><b>Concurrent re-files (#2143).</b> The owner is written CONDITIONALLY, at the row version read after the change,
    /// from the parents the row holds at that version, re-derived on every attempt (a row another writer re-filed, or whose
    /// parent was made Secure, in between gets the owner its parents give now), a changed version is read again (bounded),
    /// and a re-derivation that is refused — or that would take a Secure-held row out of isolation on a filing this call's
    /// F3 check did not cover — writes no owner. Attempts that run out toward the Secure team end in one unconditional write
    /// of the Secure team (over-restriction is the safe failure). The put-back is conditional too, and never overwrites a
    /// newer filing or owner.</para>
    /// </remarks>
    /// <exception cref="RecordOwnerUnsettledException">(#2143) The owner could not be settled — the row kept changing under
    /// concurrent writers, its current parents resolve to no owner, or settling it would take it out of the Secure Record
    /// business unit on a filing this call did not make — and no owner that disagrees with its current parents was written.
    /// The exception's remarks say which state the row is left in.</exception>
    /// <exception cref="InvalidOperationException">The reassignment did not read back as applied (the change was rolled
    /// back first), or the row carries no version to condition the owner write on.</exception>
    /// <exception cref="ArgumentException">(#2143 round 5) A <see cref="RecordReparent.AttachColumns"/> entry is a lineage-only
    /// lookup (<see cref="RecordReparent.LineageOnlyTargetOf"/>) that <see cref="RecordReparent.ParentChanges"/> does not name;
    /// thrown before anything is read or written.</exception>
    Task<RecordOwnerResolution> ReparentAsync(
        RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct);
}

/// <summary>One parent record a child is filed under: a lookup value the child carries.</summary>
/// <remarks>
/// #2143 (F3): <see cref="EntityLogicalName"/> is held trimmed and lower-case — Dataverse logical names are, but a writer
/// may pass a lookup's table as it was given (<c>sprk_Matter</c>). Normalised here, at construction and on <c>with</c>, so
/// two references to the same row are equal however they were spelled: the re-file compares the parents it resolved from
/// with the parents it reads back, and a spelling difference must never read as another writer's re-filing.
/// </remarks>
public sealed record RecordOwnershipParent(string EntityLogicalName, Guid RecordId)
{
    private readonly string _entityLogicalName = Normalize(EntityLogicalName);

    /// <summary>The parent's table, trimmed and lower-case.</summary>
    public string EntityLogicalName
    {
        get => _entityLogicalName;
        init => _entityLogicalName = Normalize(value);
    }

    private static string Normalize(string? entityLogicalName) =>
        entityLogicalName?.Trim().ToLowerInvariant() ?? string.Empty;

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
/// from the row being written with <see cref="ParentsOf(IEnumerable{KeyValuePair{string, object}}, string?)"/> so a lookup
/// cannot be forgotten.
/// </para>
/// </remarks>
/// <summary>
/// UAC task 194b V-fix: one reconciler pass's cache of the per-pass-constant ownership lookups
/// (<see cref="RecordOwnershipContext.LookupCache"/>). Only answers are kept: a read that faults propagates and is asked again.
/// Not thread-safe: a pass decides its rows one at a time.
/// </summary>
public sealed class RecordOwnerLookupCache
{
    /// <summary>The Secure Record business unit, once read.</summary>
    internal (Guid? Id, bool Ambiguous)? SecureBusinessUnit { get; set; }

    /// <summary>Each business unit's default owner team answer, once read.</summary>
    internal Dictionary<Guid, RecordOwnerResolution> DefaultOwnerTeams { get; } = new();

    /// <summary>R4b-2c: each answer business unit's restricted-content owner team outcome (the walk to the customer's top unit
    /// and the team there), once read.</summary>
    internal Dictionary<Guid, Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity> RestrictedContentByAnswerBusinessUnit { get; } = new();

    /// <summary>R4b-2c: each customer top business unit's restricted-content owner team identification, once read.</summary>
    internal Dictionary<Guid, Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity> RestrictedContentByCustomerBusinessUnit { get; } = new();

    /// <summary>R4b-2c: every restricted-content owner team outcome this pass met — one per customer top business unit, and
    /// one per refusal of the walk (the pass reports them); empty when no hard-deny row asked.</summary>
    public IReadOnlyList<Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity> RestrictedContentOwnerTeams =>
        RestrictedContentByAnswerBusinessUnit.Values.Distinct().ToList();
}

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

    /// <summary>
    /// UAC task 194b V-fix 3 (coordinator ruling): the parents that count for SECURE-IF-ANY only
    /// (<see cref="RecordOwnershipResolver.IsSecureOnlyParent"/>: an editable copy's <c>sprk_canonicaldocument</c>). Any one of
    /// them under a Secure record makes the row Secure, and one that cannot be read refuses, as any parent does. They are never
    /// the primary and never decide an ordinary row's business unit: a row with no other parent takes the unfiled answer.
    /// Filled by <see cref="ForRow"/> (and so <see cref="ForChild"/>) and <see cref="ForExistingRowAsync"/>, never by hand.
    /// </summary>
    public IReadOnlyList<RecordOwnershipParent> SecureOnlyParents { get; init; } = Array.Empty<RecordOwnershipParent>();

    /// <summary>
    /// UAC task 194b R4b-2: the table of the row this question is about. A hard-deny table
    /// (<see cref="RecordOwnershipResolver.IsRestrictedContentTable"/>) takes the restricted-content owner team where its
    /// ordinary answer would be a business unit's default team. Filled by <see cref="ForRow"/> (and so <see cref="ForChild"/>),
    /// <see cref="ForExistingRowAsync"/> and the re-file, never by hand; <c>null</c> leaves every answer as it was.
    /// </summary>
    public string? ChildEntityLogicalName { get; init; }

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

    /// <summary>
    /// UAC task 194b V-fix (verifier B, the app user's request budget): the lookups that are the same for every row of ONE
    /// reconciler pass — the Secure Record business unit and each business unit's default owner team — read once and kept
    /// here for the rest of the pass. The pass creates it and drops it with itself, so a business-unit or team change is
    /// seen by the next pass. <c>null</c> everywhere else (each question reads them, as before).
    /// </summary>
    public RecordOwnerLookupCache? LookupCache { get; init; }

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
    /// from the row itself, never from a guess). #2084: a column of <paramref name="table"/> that CITES rather than files
    /// (<see cref="RecordOwnershipResolver.IsCitation"/>) is left out, so a row is never filed under the record it cites.
    /// <paramref name="table"/> is the row's own table (required, so no caller can skip the exclusion by omission);
    /// <c>null</c> only where the fields belong to no one table.
    /// </summary>
    public static IReadOnlyList<RecordOwnershipParent> ParentsOf(IEnumerable<KeyValuePair<string, object>> fields, string? table)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var parents = new List<RecordOwnershipParent>();
        foreach (var (column, value) in fields)
        {
            if (RecordOwnershipResolver.IsCitation(table, column))
                continue;

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
    /// #2084: <see cref="ParentsOf(IEnumerable{KeyValuePair{string, object}}, string?)"/> for a row, keyed by its own table, so
    /// its citation lookups (<see cref="RecordOwnershipResolver.IsCitation"/>) are left out: a lookup to an ownership-parent table
    /// that names what the row cites, not what it is filed under. Used for a row being created (<see cref="ForChild"/>) and for an
    /// existing row the reconciler re-owns (<c>ContextFor</c>).
    /// </summary>
    public static IReadOnlyList<RecordOwnershipParent> ParentsOf(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ParentsOf(row.Attributes, row.LogicalName);
    }

    /// <summary>
    /// UAC task 194b: <see cref="ParentsOf(Entity)"/> in a FIXED order, so the first parent (the PRIMARY, whose business unit
    /// decides an ordinary row's owner) never depends on the order Dataverse returns the row's attributes in. The order is the
    /// row's table's lookup order in <c>SecureChildLineage</c> (<see cref="Sprk.Bff.Api.Services.Access.SecureChildLineage.Table.LookupOrder"/>,
    /// which matches each table's writer); a lookup that order does not name comes after, by column name. Secure-if-any reads
    /// every parent, so the order never decides whether the row is secure.
    /// </summary>
    public static IReadOnlyList<RecordOwnershipParent> OrderedParentsOf(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return PrimaryOrdered(row.LogicalName, LookupsOf(row), PairIdOf(row));
    }

    /// <summary>
    /// UAC task 194b V-fix 3: the row's Secure-only parents (<see cref="SecureOnlyParents"/>), which
    /// <see cref="OrderedParentsOf"/> leaves out — the other half of the question the reconciler asks about the written row.
    /// </summary>
    public static IReadOnlyList<RecordOwnershipParent> SecureOnlyParentsOf(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return SecureOnlyOf(row.LogicalName, LookupsOf(row));
    }

    /// <summary>The ownership parents held by <paramref name="table"/>'s Secure-only columns, distinct.</summary>
    private static IReadOnlyList<RecordOwnershipParent> SecureOnlyOf(
        string table, IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> lookups) =>
        lookups
            .Where(l => l.Value is { IsSpecified: true } parent
                        && RecordOwnershipResolver.IsOwnershipParent(parent.EntityLogicalName)
                        && RecordOwnershipResolver.IsSecureOnlyParent(table, l.Key))
            .Select(l => l.Value! with { EntityLogicalName = l.Value!.EntityLogicalName.Trim().ToLowerInvariant() })
            .Distinct()
            .ToArray();

    /// <summary>Every lookup <paramref name="row"/> holds, column → the record it names (any table; the core filters).</summary>
    internal static IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> LookupsOf(Entity row) =>
        row.Attributes
            .Where(a => a.Value is EntityReference)
            .Select(a => new KeyValuePair<string, RecordOwnershipParent?>(
                a.Key, new RecordOwnershipParent(((EntityReference)a.Value).LogicalName, ((EntityReference)a.Value).Id)));

    /// <summary>The row's regarding pair id (<c>sprk_regardingrecordid</c>), as written.</summary>
    internal static string? PairIdOf(Entity row) =>
        row.Attributes.TryGetValue(CoreAncestorResolver.RegardingRecordIdColumn, out var pair) ? pair as string : null;

    /// <summary>
    /// UAC task 194b fix round R (B3): THE primary-order rule, shared by the reconciler's question about an existing row
    /// (<see cref="OrderedParentsOf"/>) and every writer's question about a row it writes (<see cref="ForRow"/>): the lookups
    /// that file the row (an ownership-parent target, a real id, not a citation of <paramref name="table"/>) in
    /// <paramref name="table"/>'s primary order (<see cref="InPrimaryOrder{TValue}"/>, the subject named by
    /// <paramref name="pairId"/> first on a regarding host table), de-duplicated.
    /// </summary>
    private static IReadOnlyList<RecordOwnershipParent> PrimaryOrdered(
        string table, IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> lookups, string? pairId)
    {
        var filed = lookups
            .Where(l => l.Value is { IsSpecified: true } parent
                        && RecordOwnershipResolver.IsOwnershipParent(parent.EntityLogicalName)
                        && !RecordOwnershipResolver.IsCitation(table, l.Key)
                        // V-fix 3: a Secure-only parent is never in the primary order (SecureOnlyParents carries it).
                        && !RecordOwnershipResolver.IsSecureOnlyParent(table, l.Key))
            .Select(l => new KeyValuePair<string, RecordOwnershipParent>(l.Key, l.Value!))
            .ToArray();
        var subject = SubjectColumnOf(
            table, pairId,
            column => filed.FirstOrDefault(l => string.Equals(l.Key, column, StringComparison.OrdinalIgnoreCase)).Value?.RecordId);
        return InPrimaryOrder(table, filed, subject).Select(l => l.Value).Distinct().ToArray();
    }

    /// <summary>
    /// UAC task 194b: <paramref name="byColumn"/> in <paramref name="table"/>'s primary order (see <see cref="OrderedParentsOf"/>).
    /// For a host table of the shared regarding builder (<c>TodoRegardingBuilder.RegardingLookupsByHostTable</c>) the row's
    /// SUBJECT — the regarding lookup that carries <c>sprk_regardingrecordid</c>, <paramref name="subjectColumn"/>
    /// (<see cref="SubjectColumnOf(Entity)"/>) — is what its writer files it under, not a core-ancestor stamp in another
    /// <c>sprk_regarding*</c> column:
    /// <list type="bullet">
    /// <item><c>sprk_todo</c> (fix round F1-a): the subject first, then the document carrier, then the email carrier, then the
    /// lineage order — the Office writer's "the record regarding first, then the document or email"
    /// (<c>OfficeService.ResolveTodoOwnerTeamAsync</c>), and <c>TodoGenerationService</c> / <c>InquiryReplyTodoCreator</c>,
    /// which name the regarding as the primary.</item>
    /// <item><c>sprk_signal</c> / <c>sprk_decisionrecord</c>: the typed core lookups first, then the subject, then the lineage
    /// order (<c>SignalWriter.OwnershipContextOf</c>: core, subject, stamps).</item>
    /// </list>
    /// Every other table: the lineage order. A lookup that order does not name comes after, by column name.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, TValue>> InPrimaryOrder<TValue>(
        string? table, IEnumerable<KeyValuePair<string, TValue>> byColumn, string? subjectColumn = null)
    {
        Sprk.Bff.Api.Services.Access.SecureChildLineage.Table? lineage = null;
        if (table is not null)
            Sprk.Bff.Api.Services.Access.SecureChildLineage.Children.TryGetValue(table, out lineage);
        var order = lineage?.LookupOrder
                    ?? (table is not null && Sprk.Bff.Api.Services.Access.SecureChildLineage.RootLookupOrder.TryGetValue(table, out var rootOrder)
                        ? rootOrder
                        : Array.Empty<string>());
        var regarding = table is not null
                        && Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder.RegardingLookupsByHostTable.TryGetValue(table, out var map)
            ? new HashSet<string>(map.Values, StringComparer.OrdinalIgnoreCase)
            : null;
        var corePair = table is not null && CorePairTables.Contains(table);

        int Lineage(string column)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], column, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return int.MaxValue;
        }

        bool Is(string column, string? other) => other is not null && string.Equals(column, other, StringComparison.OrdinalIgnoreCase);

        int Group(string column)
        {
            if (regarding is null)
                return 0;
            if (corePair)
            {
                // The typed core lookups (a lookup to a core record that is not a regarding lookup), then the subject.
                if (!regarding.Contains(column)
                    && lineage is not null
                    && lineage.Lookups.TryGetValue(column, out var target)
                    && CoreAncestorResolver.CoreRecordEntities.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    return 0;
                }

                return Is(column, subjectColumn) ? 1 : 2;
            }

            return Is(column, subjectColumn) ? 0
                : Is(column, TodoDocumentCarrierColumn) ? 1
                : Is(column, TodoEmailCarrierColumn) ? 2
                : 3;
        }

        return byColumn
            .OrderBy(c => Group(c.Key))
            .ThenBy(c => Lineage(c.Key))
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The To Do's document carrier (the open Word document it was created from) — <c>OfficeService.TodoRegardingMap</c>.</summary>
    private const string TodoDocumentCarrierColumn = "sprk_regardingdocument";

    /// <summary>The To Do's email carrier (the Outlook email it was created from) — <c>OfficeService.TodoRegardingMap</c>.</summary>
    private const string TodoEmailCarrierColumn = "sprk_regardingcommunication";

    /// <summary>
    /// Fix round F1-a: the column of <paramref name="row"/>'s SUBJECT — the regarding lookup whose id repeats the pair id
    /// <c>sprk_regardingrecordid</c> (<c>SignalWriter.SubjectEntityOf</c>, the one agreement rule) — for a host table of the
    /// shared regarding builder; <c>null</c> for any other table, a row with no pair, or a pair no single lookup repeats.
    /// </summary>
    internal static string? SubjectColumnOf(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return SubjectColumnOf(
            row.LogicalName,
            row.Attributes.TryGetValue(CoreAncestorResolver.RegardingRecordIdColumn, out var pair) ? pair as string : null,
            column => row.Attributes.TryGetValue(column, out var value) && value is EntityReference reference ? reference.Id : null);
    }

    /// <summary><see cref="SubjectColumnOf(Entity)"/> from a table, its pair id as written, and the id each lookup column carries.</summary>
    internal static string? SubjectColumnOf(string? table, string? pairId, Func<string, Guid?> lookupIdOf)
    {
        ArgumentNullException.ThrowIfNull(lookupIdOf);
        if (table is null
            || !Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder.RegardingLookupsByHostTable.TryGetValue(table, out var lookups)
            || !Guid.TryParse(pairId?.Trim(), out var pair))
        {
            return null;
        }

        return Sprk.Bff.Api.Services.Signals.SignalWriter.SubjectEntityOf(table, pair, lookupIdOf) is { } subject
               && lookups.TryGetValue(subject, out var column)
            ? column
            : null;
    }

    /// <summary>The tables whose rows name a CORE record in a pair (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>) and a
    /// subject (the regarding lookup carrying <c>sprk_regardingrecordid</c>): their writers ask about the core first.</summary>
    internal static readonly IReadOnlySet<string> CorePairTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_signal", "sprk_decisionrecord",
    };

    /// <summary>UAC task 194b fix round R (A5): why <see cref="ReparentAsync"/> refuses a Signal or a Decision Record.</summary>
    internal const string WriterOwnedFilingRefusal =
        "a Signal's or a Decision Record's filing is written only by its own writer, whose ownership question covers the core "
        + "record and the Signals it resolved; a generic re-file does not ask that question, so nothing was written (194b A5)";

    private const string CoreRecordTypeColumn = "sprk_corerecordtype";
    private const string CoreRecordIdColumn = "sprk_corerecordid";

    /// <summary>
    /// UAC task 194b: the ownership question for an EXISTING row, with the parents in the order its writer asks them, so the
    /// reconciler (<c>SecureChildReconciler</c>) and the writer give one answer whatever order the row's attributes come back in.
    /// <list type="bullet">
    /// <item><c>sprk_signal</c> and <c>sprk_decisionrecord</c>: the core record (<c>sprk_corerecordtype</c>, read through the
    /// record-type catalog, + <c>sprk_corerecordid</c>), then the subject (the regarding lookup that carries
    /// <c>sprk_regardingrecordid</c>), then every core ancestor stamp <see cref="CoreAncestorResolver"/> derives from that subject
    /// (the list <c>SignalWriter.OwnershipContextOf</c> and <c>DecisionRecordWriter.OwnershipContextOf</c> build), then — for a
    /// Decision Record — the subject of every Signal it resolved (the writer's <c>SignalSubjects</c>, asked again from the ids its
    /// <c>sprk_steps.resolvedSignalIds</c> lists: <c>DecisionCommitService.ResolvedSignalSubjectsOfRecordAsync</c>, fix round R),
    /// then the row's other lookups (<see cref="OrderedParentsOf"/>). A Decision Record whose Signal subjects cannot all be
    /// known is <see cref="ExistingRowOwnership.Incomplete"/>: decided over what is known, never released from isolation
    /// (<see cref="DecisionRecordHeldInIsolation"/>). A row with NO core is decided over the same list without the core, as its
    /// writer decides it, EXCEPT one whose subject is a To Do that <c>TodoAssigneeOwnerReconciliationJob</c> keeps
    /// (<c>TodoAssigneeOwnerReconciliationJob.SelectsAsync</c>, the job's own predicate): its owner is its personal To Do
    /// assignee's team (D-134), and <see cref="ExistingRowOwnership.NoCoreOwnerRule"/> says so (fix round F1-c).</item>
    /// <item>Every other table: <see cref="OrderedParentsOf"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="row">The row, with every column (the reconciler reads the whole row).</param>
    /// <param name="reads">What the question reads, once per pass (<see cref="ExistingRowReads"/>): the record-type catalog,
    /// the core-ancestor derivation (absent: a Signal or Decision Record with a core is
    /// <see cref="ExistingRowOwnership.Undetermined"/> — fail closed, a stamp could be the secure parent) and the To Do
    /// assignee links (absent: a no-core row about a To Do is undetermined).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>A data condition (a malformed pair, an unknown core type, a stamp derivation that failed) is
    /// <see cref="ExistingRowOwnership.Undetermined"/>, never a throw. A Dataverse fault reading the core record propagates; one
    /// reading what decides a no-core row's rule is undetermined; one reading a Decision Record's Signal subjects leaves its
    /// question <see cref="ExistingRowOwnership.Incomplete"/>.</returns>
    public static async Task<ExistingRowOwnership> ForExistingRowAsync(Entity row, ExistingRowReads reads, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(reads);
        var rest = OrderedParentsOf(row);
        // V-fix 3: the Secure-only parents ride with every question built here (only sprk_document holds one today).
        var secureOnly = SecureOnlyParentsOf(row);
        if (!CorePairTables.Contains(row.LogicalName))
            return new ExistingRowOwnership(ForParents(rest) with { SecureOnlyParents = secureOnly, ChildEntityLogicalName = row.LogicalName });

        var undetermined = new ExistingRowOwnership(ForParents(rest) with { SecureOnlyParents = secureOnly, ChildEntityLogicalName = row.LogicalName });
        var typeRef = row.GetAttributeValue<EntityReference>(CoreRecordTypeColumn)?.Id is { } t && t != Guid.Empty ? t : (Guid?)null;
        var coreText = row.GetAttributeValue<string>(CoreRecordIdColumn)?.Trim();
        Guid? coreId = Guid.TryParse(coreText, out var parsed) && parsed != Guid.Empty ? parsed : null;
        if ((typeRef is null) != (coreId is null) || (!string.IsNullOrWhiteSpace(coreText) && coreId is null))
            return undetermined with { Undetermined = "its core record pair is malformed" };

        var subject = Guid.TryParse(row.GetAttributeValue<string>(CoreAncestorResolver.RegardingRecordIdColumn)?.Trim(), out var pairId)
                      && Sprk.Bff.Api.Services.Signals.SignalWriter.SubjectEntityOf(
                          row.LogicalName, pairId, lookup => row.GetAttributeValue<EntityReference>(lookup)?.Id) is { } subjectTable
            ? new RecordOwnershipParent(subjectTable, pairId)
            : null;

        // Fix round R (ontology answer (c)): a Decision Record's writer also asks about the subject of every Signal it resolved
        // (SignalSubjects), so its question does too — from the ids its sprk_steps lists, by the commit's own rule. What cannot be
        // known leaves the question INCOMPLETE (decided over what is known; never released from isolation), not undetermined.
        IReadOnlyList<RecordOwnershipParent> signalSubjects = Array.Empty<RecordOwnershipParent>();
        string? incomplete = null;
        if (string.Equals(row.LogicalName, DecisionRecordTable, StringComparison.OrdinalIgnoreCase))
        {
            var (subjects, why) = await Sprk.Bff.Api.Services.Signals.DecisionCommitService.ResolvedSignalSubjectsOfRecordAsync(
                row.GetAttributeValue<string>(DecisionRecordStepsColumn), reads.Entities, ct).ConfigureAwait(false);
            signalSubjects = subjects.Select(s => new RecordOwnershipParent(s.Entity, s.Id)).ToList();
            incomplete = why;
        }

        ExistingRowOwnership Asked(ExistingRowOwnership question) =>
            question.Undetermined is null ? question with { Incomplete = incomplete } : question;

        if (coreId is null)
        {
            // Fix round F1-c: the assignee rule is the To Do job's, and only for the rows that job keeps (its own predicate);
            // every other no-core row is decided over its parents, as its writer decided it ([subject, ..SignalSubjects]).
            var noCore = ForParents(new[] { subject }.Concat(signalSubjects).Concat(rest)) with { SecureOnlyParents = secureOnly, ChildEntityLogicalName = row.LogicalName };
            if (subject is null || !string.Equals(subject.EntityLogicalName, TodoTable, StringComparison.OrdinalIgnoreCase))
                return Asked(new ExistingRowOwnership(noCore));
            if (reads.Links is null || reads.CoreAncestors is null)
            {
                return new ExistingRowOwnership(noCore)
                {
                    Undetermined = "it has no core record and is about a To Do, whose assignee rule cannot be checked here",
                };
            }

            bool keptByToDoJob;
            try
            {
                keptByToDoJob = await Sprk.Bff.Api.Services.Signals.TodoAssigneeOwnerReconciliationJob.SelectsAsync(
                    row, reads.Entities, reads.Links, reads.CoreAncestors, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new ExistingRowOwnership(noCore)
                {
                    Undetermined = $"it has no core record, and whether its To Do's assignee rule keeps it could not be read ({ex.Message})",
                };
            }

            return Asked(keptByToDoJob
                ? new ExistingRowOwnership(noCore)
                {
                    NoCoreOwnerRule = "it has no core record: its owner is its To Do assignee's business-unit team (D-134), kept by "
                                      + "TodoAssigneeOwnerReconciliationJob; reported, not moved",
                }
                : new ExistingRowOwnership(noCore));
        }

        var coreTable = await reads.CoreTableOfRecordTypeAsync(typeRef!.Value, ct).ConfigureAwait(false);
        if (coreTable is null)
            return undetermined with { Undetermined = "its core record type is not a core record table in the record-type catalog" };

        var parents = new List<RecordOwnershipParent?> { new(coreTable, coreId.Value), subject };
        if (subject is not null)
        {
            if (reads.CoreAncestors is null)
                return undetermined with { Undetermined = "its subject's core ancestors cannot be derived here" };

            var stamps = await reads.StampsOfAsync(subject, ct).ConfigureAwait(false);
            if (stamps.Status is not (CoreAncestorStatus.CoreTarget or CoreAncestorStatus.Derived or CoreAncestorStatus.NoAncestor))
                return undetermined with { Undetermined = $"its subject's core ancestors could not be derived ({stamps.Status})" };

            parents.AddRange(stamps.Stamps.Select(stamp => new RecordOwnershipParent(stamp.EntityType, stamp.RecordId)));
        }

        return Asked(new ExistingRowOwnership(
            ForParents(parents.Concat(signalSubjects).Concat(rest)) with { SecureOnlyParents = secureOnly, ChildEntityLogicalName = row.LogicalName }));
    }

    internal const string DecisionRecordTable = "sprk_decisionrecord";

    /// <summary>R4b-2b (rule 1): why a re-file leaves an author-owned row of a hard-deny table with its author.</summary>
    internal const string AuthorKeptRestrictedContentReason =
        "an author-owned row of a restricted-content table keeps its author when its answer is ordinary; the restricted-content "
        + "owner team replaces only a business unit's default team (R4b-2 rule 1)";

    /// <summary>The Decision Record's step envelope, which lists the Signals it resolved (<c>resolvedSignalIds</c>).</summary>
    internal const string DecisionRecordStepsColumn = "sprk_steps";
    private const string TodoTable = "sprk_todo";

    /// <summary>
    /// UAC task 194b fix round (F1-d, narrowed in fix round R by ontology answer (c)): THE never-out rule for a Decision Record —
    /// the ONE predicate <see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler"/> asks, in its secure pass over a root's
    /// children (Make Secure and Unsecure, and the pass after a re-file) and in its ordinary pass (a held record is reported).
    /// <see cref="IRecordOwnershipResolver.ReparentAsync"/> never asks it: it refuses a Decision Record outright, before
    /// anything is read or written (fix round R, A5: <see cref="WriterOwnedFilingRefusal"/>). A Decision Record owned in isolation (the Secure Record owner
    /// team, or any team in the Secure Record business unit) whose answer now is an ordinary team is held where it is and
    /// reported when its question is INCOMPLETE (<see cref="ExistingRowOwnership.Incomplete"/>: the Signals it resolved cannot
    /// all be known), since an ordinary answer over part of its commit-time parents cannot prove it may leave. A Decision Record
    /// whose question is whole leaves isolation as any other row does. A move INTO isolation is never held.
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="ownedInIsolation">Whether the row is owned now by the Secure Record owner team or in the Secure Record
    /// business unit (an ambiguous Secure Record business unit counts as owned there: fail closed).</param>
    /// <param name="answer">The rule's answer for the row now.</param>
    /// <param name="incomplete">Why the question the answer came from is incomplete, or <c>null</c> when it is whole.</param>
    /// <returns>Why it is held (a sentence), or <c>null</c> when the rule does not hold it.</returns>
    public static string? DecisionRecordHeldInIsolation(
        string? table, bool ownedInIsolation, RecordOwnerResolution answer, string? incomplete)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return string.Equals(table, DecisionRecordTable, StringComparison.OrdinalIgnoreCase)
               && ownedInIsolation && answer.IsOwned && !answer.IsSecureOwner && incomplete is not null
            ? $"a Decision Record owned in isolation whose question is incomplete ({incomplete}): an ordinary answer over part of "
              + "its commit-time parents cannot prove it may leave, so it is held in isolation and reported (194b F1-d)"
            : null;
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
    /// UAC task 194b fix round R (B3): THE ownership question for a CHILD row a writer is about to write — its table and the
    /// lookups it will hold (column → record; nulls, non-parent targets and citations are dropped), in the table's primary
    /// order (<see cref="PrimaryOrdered"/>: the order the reconciler asks the written row in, so the writer's PRIMARY — an
    /// ordinary row's business unit — is the reconciler's by construction). <paramref name="pairId"/> is the row's
    /// <c>sprk_regardingrecordid</c> as written, which names the subject first on a regarding host table. A writer that needs
    /// another primary has found a table whose order is wrong: that is changed in <c>SecureChildLineage</c>, with a reason,
    /// never at the call site. The one opt-out is <see cref="ForParentsInWriterOrder"/> (a closed list).
    /// </summary>
    public static RecordOwnershipContext ForRow(
        string table, IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> lookups, string? pairId = null,
        Guid? callerObjectId = null, Guid? callerSystemUserId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(lookups);
        var name = table.Trim().ToLowerInvariant();
        var held = lookups.ToArray();
        // V-fix 3: a Secure-only lookup (an editable copy's canonical) joins secure-if-any only, never the primary order.
        return ForParents(PrimaryOrdered(name, held, pairId), callerObjectId, callerSystemUserId) with
        {
            SecureOnlyParents = SecureOnlyOf(name, held),
            ChildEntityLogicalName = name, // R4b-2
        };
    }

    /// <summary>
    /// <see cref="ForRow"/> for a child row built as an entity: its table, every lookup it holds and its pair id. Fix round R
    /// (B3): no caller-named primary any more — the table's primary order decides, as the reconciler's does.
    /// </summary>
    public static RecordOwnershipContext ForChild(Entity child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return ForRow(child.LogicalName, LookupsOf(child), PairIdOf(child));
    }

    /// <summary>
    /// UAC task 194b fix round R (B3): the ONE opt-out from <see cref="ForRow"/> — <paramref name="parents"/> in the order the
    /// writer names them, for a writer whose own order IS its table's rule: the Signal writer (core, subject, stamps) and the
    /// Decision Record writer (its Signal's core, subject, derived cores, every resolved Signal's subject), whose existing-row
    /// question <see cref="ForExistingRowAsync"/> builds with the same list. A CLOSED list: only
    /// <c>SignalWriter.OwnershipContextOf</c> and <c>DecisionRecordWriter.OwnershipContextOf</c> may call it
    /// (<c>WriterOrderOptOutAllowListTests</c>).
    /// </summary>
    internal static RecordOwnershipContext ForParentsInWriterOrder(IEnumerable<RecordOwnershipParent?> parents) =>
        ForParents(parents);

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

    /// <summary>
    /// The owner is the given user's business-unit team; used when a row is owned on behalf of a person who is not the
    /// caller (D-134: a personal To Do's Assigned To). No parent is named, so the resolver answers through its unfiled
    /// path (<see cref="UnfiledOwnership.ActingUserTeam"/>) with <paramref name="systemUserId"/> as the user.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="systemUserId"/> is <see cref="Guid.Empty"/>.</exception>
    public static RecordOwnershipContext ForUserTeam(Guid systemUserId)
    {
        if (systemUserId == Guid.Empty)
            throw new ArgumentException("A user-team owner needs a systemuserid.", nameof(systemUserId));

        return new RecordOwnershipContext
        {
            CallerSystemUserId = systemUserId,
            WhenUnfiled = UnfiledOwnership.ActingUserTeam,
        };
    }
}

/// <summary>
/// UAC task 194b fix round (K5): what one pass reads to ask its rows' ownership questions
/// (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>) — the record-type catalog by type, and the core-ancestor stamps by
/// subject, each read once per pass and kept in a plain dictionary (no expiry, no sharing across passes: a new pass reads
/// again). A fault is not kept, so the next row asks again.
/// </summary>
public sealed class ExistingRowReads
{
    private readonly Dictionary<Guid, string?> _coreTables = new();
    private readonly Dictionary<RecordOwnershipParent, CoreAncestorResult> _stamps = new();

    /// <param name="entities">Reads the catalog, the To Do and the Signals a Decision Record resolved.</param>
    /// <param name="coreAncestors">The ONE core-ancestor derivation; <c>null</c> leaves a cored Signal or Decision Record
    /// undetermined.</param>
    /// <param name="links">The To Do assignee links (the To Do job's); <c>null</c> leaves a no-core row about a To Do
    /// undetermined.</param>
    public ExistingRowReads(
        IGenericEntityService entities, CoreAncestorResolver? coreAncestors,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessStore? links)
    {
        Entities = entities ?? throw new ArgumentNullException(nameof(entities));
        CoreAncestors = coreAncestors;
        Links = links;
    }

    /// <summary>Reads the catalog, the To Do and the Signals a Decision Record resolved.</summary>
    public IGenericEntityService Entities { get; }

    /// <summary>The core-ancestor derivation, when given.</summary>
    public CoreAncestorResolver? CoreAncestors { get; }

    /// <summary>The To Do assignee links, when given.</summary>
    public Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessStore? Links { get; }

    /// <summary>How many catalog reads this pass made (one per record type).</summary>
    public int CatalogReads { get; private set; }

    /// <summary>How many stamp derivations this pass made (one per subject).</summary>
    public int StampDerivations { get; private set; }

    /// <summary>The core table of a record type (<see cref="CoreAncestorResolver.CoreTableOfRecordTypeAsync"/>), once per type.</summary>
    internal async Task<string?> CoreTableOfRecordTypeAsync(Guid typeRef, CancellationToken ct)
    {
        if (_coreTables.TryGetValue(typeRef, out var known))
            return known;
        CatalogReads++;
        var table = await CoreAncestorResolver.CoreTableOfRecordTypeAsync(Entities, typeRef, ct).ConfigureAwait(false);
        _coreTables[typeRef] = table;
        return table;
    }

    /// <summary>A subject's core ancestor stamps (read-only intent), once per subject. Requires <see cref="CoreAncestors"/>.</summary>
    internal async Task<CoreAncestorResult> StampsOfAsync(RecordOwnershipParent subject, CancellationToken ct)
    {
        if (_stamps.TryGetValue(subject, out var known))
            return known;
        StampDerivations++;
        var stamps = await CoreAncestors!.ResolveStampsAsync(
            subject.EntityLogicalName, subject.RecordId, ct, CoreAncestorIntent.ReadOnly).ConfigureAwait(false);
        _stamps[subject] = stamps;
        return stamps;
    }
}

/// <summary>
/// UAC task 194b: the ownership question for an existing row (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>).
/// <see cref="Context"/> names its parents, primary first, and is always set (a caller that needs only the set of parents reads
/// it even when the answer is undetermined).
/// </summary>
/// <param name="Context">The row's parents in its writer's order (<see cref="RecordOwnershipContext.ForParents"/>).</param>
public sealed record ExistingRowOwnership(RecordOwnershipContext Context)
{
    /// <summary>Set for a Signal or Decision Record with no core record that <c>TodoAssigneeOwnerReconciliationJob</c> keeps (its
    /// own predicate, fix round F1-c): its owner comes from another rule (its To Do assignee's team), not from its parents, so a
    /// reconciler never moves it out of isolation nor between ordinary teams (a move INTO isolation still holds: secure wins).
    /// Why, as a sentence.</summary>
    public string? NoCoreOwnerRule { get; init; }

    /// <summary>Set when the question cannot be asked from what the row holds (a malformed core pair, an unknown core type, a
    /// stamp derivation that failed). Fail closed: the row is not decided. Why, as a sentence.</summary>
    public string? Undetermined { get; init; }

    /// <summary>UAC task 194b fix round R (ontology answer (c)): set for a Decision Record whose Signal subjects cannot all be
    /// known (no <c>resolvedSignalIds</c> in its <c>sprk_steps</c>, a listed Signal unreadable or gone, a subject table that
    /// cannot be derived). The question holds what is known, so it is still decided — a secure answer is secure over every
    /// parent — but an ordinary answer never takes it out of isolation
    /// (<see cref="RecordOwnershipContext.DecisionRecordHeldInIsolation"/>). Why, as a sentence.</summary>
    public string? Incomplete { get; init; }
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
    /// knowing which of them are lookups). #2110 (ontology K3): an entry for a LINEAGE lookup that files the row under no
    /// ownership parent (<see cref="LineageOnlyTargetOf"/> — a message's thread) decides no owner either, but is compared
    /// with the row: a change that moves one is no <see cref="RecordOwnerResolution.ParentsUnchanged"/>
    /// (<see cref="WithLineageLookupsIn"/> adds them).
    /// </summary>
    public required IReadOnlyDictionary<string, EntityReference?> ParentChanges { get; init; }

    /// <summary>
    /// Parents the row takes on from something it is being ATTACHED to rather than from one of its own columns — an invoice
    /// confirmed onto a matter. Added to the row's current parents. #2143 round 6: a message's record thread is NOT passed
    /// here — the resolver reads the thread the row sits in, or the one <see cref="ParentChanges"/> names, and adds its filing
    /// itself (<c>RecordThreadFiling</c>, the one rule the reconciler applies too).
    /// </summary>
    public IReadOnlyList<RecordOwnershipParent> InheritedParents { get; init; } = Array.Empty<RecordOwnershipParent>();

    /// <summary>
    /// Columns the change writes to ATTACH the row to what it joins — a message's thread lookup when it joins a record
    /// thread. They are not parent lookups, so the resolver cannot see them in
    /// <see cref="ParentChanges"/>; naming them lets <see cref="IRecordOwnershipResolver.ReparentAsync"/> put them back,
    /// with the parent columns, when the owner assignment that must follow the change fails (task 146 b1).
    /// #2143 round 5: a lineage-only lookup named here (<see cref="LineageOnlyTargetOf"/> — a message's thread) must also be
    /// named, with the row it writes, in <see cref="ParentChanges"/>, or the reparent throws <see cref="ArgumentException"/>.
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
    /// Every parent change a generic update payload to <paramref name="entityLogicalName"/> makes, keyed by the column's
    /// logical name (<c>@odata.bind</c> and <c>_x_value</c> spellings normalized): an <see cref="EntityReference"/> onto an
    /// ownership parent; an <c>@odata.bind</c> (<c>"sprk_RegardingMatter@odata.bind": "/sprk_matters(&lt;id&gt;)"</c>) on one
    /// of the table's lookups to an ownership parent (<see cref="FilingParentOf"/>) — the ONLY shape a Web API write can set
    /// a lookup in (#2091 round 3); and every column the payload
    /// CLEARS. A <c>null</c> value is entered as a <c>null</c> change, because a generic update cannot tell a cleared lookup
    /// from a cleared text column — the row can (<see cref="IRecordOwnershipResolver.ReparentAsync"/> reads it, and a null
    /// for a column that holds no parent is no parent change). Task 146 verifier item 8: a child moved OUT of its secure
    /// parent by clearing the lookup must be re-owned like one moved by setting another.
    /// </summary>
    /// <exception cref="RecordOwnerUnresolvedException">A filing lookup is bound to something that does not name one row of
    /// its target table (<c>/&lt;target entity set&gt;(&lt;id&gt;)</c>): the new parent cannot be read, so the write is refused
    /// (fail closed) — written, it would move the row with no owner re-derived.</exception>
    public static IReadOnlyDictionary<string, EntityReference?> ParentChangesWithClearsIn<TValue>(
        string entityLogicalName, IEnumerable<KeyValuePair<string, TValue>> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityLogicalName);
        ArgumentNullException.ThrowIfNull(fields);
        var table = entityLogicalName.Trim().ToLowerInvariant();
        var changes = new Dictionary<string, EntityReference?>(StringComparer.OrdinalIgnoreCase);
        var clears = new List<string>();
        foreach (var (key, raw) in fields)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var column = Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(key);
            object? value = raw is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined }
                ? null
                : raw;
            if (value is EntityReference reference)
            {
                if (RecordOwnershipResolver.IsOwnershipParent(reference.LogicalName))
                    changes[column] = reference.Id == Guid.Empty ? null : reference;
            }
            else if (value is null)
            {
                clears.Add(column);
            }
            else if (key.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase) && FilingParentOf(table, column) is { } target)
            {
                changes[column] = new EntityReference(target, BoundIdOf(table, column, target, value));
            }
        }

        foreach (var column in clears)
            changes.TryAdd(column, null);

        return changes;
    }

    /// <summary>
    /// #2091 round 3: whether an update to <paramref name="entityLogicalName"/> that makes <paramref name="parentChanges"/>
    /// could be a RE-FILE — a lookup set onto an ownership parent, or a CLEARED filing lookup (<see cref="FilingParentOf"/>),
    /// on a reparentable child table (<see cref="RecordOwnershipResolver.IsReparentableChild"/>). The ONE predicate every
    /// re-file writer uses to decide whether it reads isolation before the write and runs
    /// <c>SecureChildReconciler.AfterRefileAsync</c> after it. A cleared text column is no re-file (round 4); the resolver
    /// still receives every null.
    /// </summary>
    public static bool MayRefile(string entityLogicalName, IReadOnlyDictionary<string, EntityReference?> parentChanges)
    {
        ArgumentNullException.ThrowIfNull(parentChanges);
        return MayRefile(entityLogicalName, parentChanges.Values.OfType<EntityReference>().Select(r => r.LogicalName),
            parentChanges.Where(c => c.Value is null).Select(c => c.Key));
    }

    /// <summary><see cref="MayRefile(string, IReadOnlyDictionary{string, EntityReference?})"/> over the lookup targets an
    /// update sets and the columns it clears.</summary>
    public static bool MayRefile(string entityLogicalName, IEnumerable<string> lookupTargets, IEnumerable<string> clearedColumns)
    {
        ArgumentNullException.ThrowIfNull(lookupTargets);
        ArgumentNullException.ThrowIfNull(clearedColumns);
        if (!RecordOwnershipResolver.IsReparentableChild(entityLogicalName))
            return false;

        var table = entityLogicalName.Trim().ToLowerInvariant();
        // #2143 round 7c: a message's thread set or cleared re-files it (IsThreadFilingColumn).
        var threadFiles = RecordThreadFiling.AppliesTo(table);
        return lookupTargets.Any(RecordOwnershipResolver.IsOwnershipParent)
            || (threadFiles && lookupTargets.Any(t => string.Equals(t, RecordThreadFiling.ThreadTable, StringComparison.OrdinalIgnoreCase)))
            || clearedColumns.Any(c => FilingParentOf(table, Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(c)) is not null
                                       || IsThreadFilingColumn(table, c));
    }

    /// <summary>
    /// #2143 round 7c: whether <paramref name="column"/> of <paramref name="table"/> is a lineage-only lookup that nonetheless
    /// RE-FILES the row — a message's thread (<c>sprk_communication.sprk_communicationthread</c>), whose own filing the message
    /// takes (<see cref="RecordThreadFiling"/>). Setting or clearing it moves what the message is filed under, so every writer
    /// routes it through <see cref="IRecordOwnershipResolver.ReparentAsync"/> (F3 included) even when it is the only change.
    /// Every other lineage-only lookup (a document's current version) still never makes a change a re-file on its own.
    /// </summary>
    /// <remarks>Component Justification (CLAUDE.md §11): (1) Existing — <see cref="LineageOnlyTargetOf"/> names the lineage-only
    /// lookups; <see cref="RecordThreadFiling"/> owns the thread rule. (2) Extension — a predicate over the two, beside them, used
    /// by the existing helpers (<see cref="WithLineageLookupsIn"/>, <see cref="MayRefile(string, IEnumerable{string}, IEnumerable{string})"/>)
    /// and <c>OwnedChildWrite</c>'s gate; no new surface. (3) Cost of doing nothing — a thread-only PATCH moved a message off a
    /// secure record's thread with no F3 and no owner decided (#2143 round 7 F2).</remarks>
    internal static bool IsThreadFilingColumn(string? table, string? column) =>
        RecordThreadFiling.AppliesTo(table)
        && column is not null
        && string.Equals(
            Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(column), RecordThreadFiling.ThreadColumn,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The ONE map of a child's lookups to an ownership parent, for the columns a generic payload names (a bind's key, a
    /// clear): the table's lineage lookups whose target is an ownership parent
    /// (<see cref="Sprk.Bff.Api.Services.Access.SecureChildLineage"/>), and its service-request lookup (the solution's
    /// 1:N relationships from <c>sprk_servicerequest</c>, <c>ExternalDataService.ServiceRequestRelations</c>, pinned to the
    /// solution XML) — a service request is an ownership parent the lineage map leaves out on purpose (round 4). The
    /// target table, or <c>null</c> for a column that files the row under no ownership parent.
    /// </summary>
    internal static string? FilingParentOf(string table, string column)
    {
        if (Sprk.Bff.Api.Services.Access.SecureChildLineage.Children.TryGetValue(table, out var lineage)
            && lineage.Lookups.TryGetValue(column, out var target)
            && RecordOwnershipResolver.IsOwnershipParent(target))
        {
            return target;
        }

        return Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService.ServiceRequestRelations.Any(r =>
                string.Equals(r.EntityLogicalName, table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.LookupAttribute, column, StringComparison.OrdinalIgnoreCase))
            ? ServiceRequest
            : null;
    }

    private const string ServiceRequest = "sprk_servicerequest";

    private const string BindSuffix = "@odata.bind";

    /// <summary>
    /// #2110 (ontology K3): the target of a LINEAGE lookup of <paramref name="table"/>
    /// (<see cref="Sprk.Bff.Api.Services.Access.SecureChildLineage"/>) that files the row under NO ownership parent — a message's
    /// thread (<c>sprk_communication.sprk_communicationthread</c>), a document's current version. It decides no owner (a thread
    /// is deliberately not an ownership parent: the remarks of <see cref="RecordOwnershipResolver.OwnershipParentEntities"/>),
    /// but the step after a re-file walks it (the mirror's secure roots), so a change that moves it is no
    /// <see cref="RecordOwnerResolution.ParentsUnchanged"/>. <c>null</c> for any other column.
    /// </summary>
    /// <remarks>Component Justification (CLAUDE.md §11): (1) Existing — <see cref="FilingParentOf"/> maps the lineage lookups
    /// that ARE ownership parents. (2) Extension — its complement over the same map, beside it, not a second map. (3) Cost of
    /// doing nothing — the same-parent skip read a change of a message's thread alongside a re-bind of its held matter as
    /// "nothing moved", and the step after a re-file did not run.</remarks>
    internal static string? LineageOnlyTargetOf(string? table, string? column) =>
        table is not null && column is not null
        && Sprk.Bff.Api.Services.Access.SecureChildLineage.Children.TryGetValue(table.Trim(), out var lineage)
        && lineage.Lookups.TryGetValue(column, out var target)
        && !RecordOwnershipResolver.IsOwnershipParent(target)
            ? target
            : null;

    /// <summary>
    /// #2110 (ontology K3): <paramref name="parentChanges"/> plus every lineage lookup <paramref name="fields"/> SETS that files
    /// the row under no ownership parent (<see cref="LineageOnlyTargetOf"/>), as an <see cref="EntityReference"/> value or an
    /// <c>@odata.bind</c> naming one row of its target — so the reparent can tell whether the change moved it. A clear is in
    /// <paramref name="parentChanges"/> already (every null is). They decide no owner, and — but for a message's thread — never make
    /// a change a reparent on their own: an empty map gains nothing else. #2143 round 7c: a message's thread
    /// (<see cref="IsThreadFilingColumn"/>) is added to an empty map too, so a thread-only change is a re-file. A bind that
    /// names no row of the lookup's target is left out (Dataverse refuses it).
    /// </summary>
    public static IReadOnlyDictionary<string, EntityReference?> WithLineageLookupsIn<TValue>(
        string entityLogicalName,
        IReadOnlyDictionary<string, EntityReference?> parentChanges,
        IEnumerable<KeyValuePair<string, TValue>> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityLogicalName);
        ArgumentNullException.ThrowIfNull(parentChanges);
        ArgumentNullException.ThrowIfNull(fields);
        var table = entityLogicalName.Trim().ToLowerInvariant();
        if (parentChanges.Count == 0 && !RecordThreadFiling.AppliesTo(table))
            return parentChanges;

        Dictionary<string, EntityReference?>? merged = null;
        foreach (var (key, raw) in fields)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var column = Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(key);
            if (LineageOnlyTargetOf(table, column) is not { } target || parentChanges.ContainsKey(column))
                continue;

            // #2143 round 7c: on its own, only a message's thread makes the change a re-file.
            if (parentChanges.Count == 0 && !IsThreadFilingColumn(table, column))
                continue;

            var reference = raw switch
            {
                EntityReference set when set.Id != Guid.Empty => set,
                _ when key.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase) && LineageBoundIdOf(target, raw) is { } id
                    => new EntityReference(target, id),
                _ => null,
            };
            if (reference is not null)
                (merged ??= new Dictionary<string, EntityReference?>(parentChanges, StringComparer.OrdinalIgnoreCase))[column] = reference;
        }

        return merged ?? parentChanges;
    }

    /// <summary>The row a lineage lookup's <c>@odata.bind</c> names — its last path segment, <c>&lt;target set&gt;(&lt;id&gt;)</c>
    /// (a relative or an absolute URI) — or <c>null</c>.</summary>
    private static Guid? LineageBoundIdOf(string target, object? value)
    {
        var path = (value switch
        {
            string text => text,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
            _ => null,
        })?.Trim();
        if (path is null || EntitySetOf(target) is not { } entitySet)
            return null;

        var segment = path[(path.LastIndexOf('/') + 1)..];
        var open = segment.IndexOf('(');
        return open > 0 && segment.EndsWith(')')
               && string.Equals(segment[..open], entitySet, StringComparison.OrdinalIgnoreCase)
               && Guid.TryParse(segment[(open + 1)..^1], out var id) && id != Guid.Empty
            ? id
            : null;
    }

    /// <summary>The id an <c>@odata.bind</c> on a filing lookup names: exactly <c>/&lt;target entity set&gt;(&lt;id&gt;)</c>
    /// (the leading slash optional). Anything else refuses the write.</summary>
    private static Guid BoundIdOf(string table, string column, string target, object value)
    {
        var entitySet = EntitySetOf(target);
        var path = (value switch
        {
            string text => text,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
            _ => null,
        })?.Trim();
        if (path is not null && entitySet is not null)
        {
            var start = path.StartsWith('/') ? 1 : 0;
            var open = path.IndexOf('(', start);
            if (open > start && path.EndsWith(')')
                && string.Equals(path[start..open], entitySet, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(path[(open + 1)..^1], out var id) && id != Guid.Empty)
            {
                return id;
            }
        }

        throw new RecordOwnerUnresolvedException(table, RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved,
            $"the {column} lookup is bound to something that does not name one {target} row (expected /{entitySet ?? target}(<id>)), "
            + "so the record it would be filed under cannot be read and the update was not written"));
    }

    /// <summary>A filing target's Web API entity set: a secure root's from the ONE root table, a child's from the lineage
    /// map, the service request's from the ONE addressable-set list (<c>RegardingNameFields</c>). <c>null</c> for a table
    /// none knows (the bind is then refused).</summary>
    private static string? EntitySetOf(string target) =>
        Sprk.Bff.Api.Services.Access.SecureChildLineage.IsRoot(target)
            ? Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.EntitySetFor(target)
            : Sprk.Bff.Api.Services.Access.SecureChildLineage.Children.TryGetValue(target, out var child)
                ? child.EntitySet
                : Sprk.Bff.Api.Services.Communication.RegardingNameFields.EntitySetName(target);

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

    /// <summary>UAC task 194b R4b-2/R4b-2c: a hard-deny row's restricted-content owner team cannot be used — the answer is in
    /// the root itself or under the Secure Record unit, the parent chain cannot be read or loops, or the team in the customer's
    /// top business unit is missing, ambiguous or has a member
    /// (<see cref="Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamStatus"/>).</summary>
    public const string RestrictedContentOwnerTeamUnresolved = "record_owner_restricted_content_team_unresolved";
}

/// <summary>
/// A re-file's F3 permission to leave secure roots (<see cref="RecordOwnerResolution.SecureExitPermitted"/>): every secure
/// root the change took the row out from under, the decision that permitted it (who, and on what basis), and (PR #2161
/// round 4) every secure root the row is filed under AFTER the change, from the same F3 walk — the filing that decision
/// approved. A step that acts on the permission acts only toward one of <see cref="RootsAfter"/>: a filing another writer
/// made after the walk (a move to a third secure record S3) was never F3-checked, so the permission does not cover it.
/// </summary>
/// <remarks>
/// Component Justification (CLAUDE.md §11): (1) Existing — <see cref="Sprk.Bff.Api.Services.Access.SecureRemovalDecision"/>
/// carries the caller and basis, but not the roots left, and is surfaced only on a refusal. (2) Extension — it is reused
/// as-is inside this record; the roots are known only inside the F3 check, so this record carries them out. (3) Cost of doing
/// nothing — a step after the re-file cannot tell the F3-permitted move S1 to S2 from the same edit made outside the BFF, so
/// an isolated document's file stays in S1's container for good (#2159 A1) or would move without F3.
/// </remarks>
public sealed record SecureExitPermit(
    IReadOnlyList<RecordOwnershipParent> RootsLeft, Sprk.Bff.Api.Services.Access.SecureRemovalDecision Decision,
    IReadOnlyList<RecordOwnershipParent> RootsAfter);

/// <summary>The answer to an ownership question: a team, a reasoned refusal, or (opted-in only) unchanged.</summary>
public sealed record RecordOwnerResolution(
    RecordOwnerOutcome Outcome, Guid? OwningTeamId, string? RefusalCode, string? Reason)
{
    /// <summary>True when a team was resolved.</summary>
    public bool IsOwned => Outcome == RecordOwnerOutcome.Owned && OwningTeamId is { } id && id != Guid.Empty;

    /// <summary>True when the writer must not write.</summary>
    public bool IsRefused => Outcome == RecordOwnerOutcome.Refused;

    /// <summary>
    /// #2091 round 4: set by <see cref="IRecordOwnershipResolver.ReparentAsync"/> when it read the row and the change moves
    /// none of its parents (every parent it sets is the one the column already holds, and every null clears a column that
    /// holds none) — nor any lineage lookup it names that is no ownership parent (#2110: a message's thread,
    /// <see cref="RecordReparent.LineageOnlyTargetOf"/>). The change was written and nothing was re-filed, so a writer runs no
    /// step after a re-file. <c>false</c> otherwise — including on any resolution a reparent did not decide, so a writer that
    /// cannot tell runs the step.
    /// </summary>
    public bool ParentsUnchanged { get; init; }

    /// <summary>
    /// True when the team is the Secure Record business unit's NAMED owner team — the row is a child of a secure record
    /// (task 146 r2). Lets a writer that may not re-own a row itself (a run-as-user tool writing a table outside the
    /// ownership set) refuse rather than leave a secure record's child in an ordinary business unit, without computing a
    /// team of its own.
    /// </summary>
    public bool IsSecureOwner { get; init; }

    /// <summary>
    /// UAC task 194b R4b-2: on a hard-deny row's answer (<see cref="RecordOwnershipResolver.IsRestrictedContentTable"/>), the
    /// business unit's default team the restricted-content owner team replaced — the ordinary answer the row would otherwise
    /// have had, so a caller comparing business units (the ordinary pass's report on a row it never moves) compares as before.
    /// <c>null</c> on every other answer.
    /// </summary>
    public Guid? InPlaceOfDefaultTeamId { get; init; }

    /// <summary>
    /// Set on a refused RE-FILE that would have taken the row out of a secure record without F3's permission (owner round
    /// 10 item 7, task 146 c1). <see cref="RefusalCode"/> is then one of the unsecure endpoint's two reason codes, and an
    /// HTTP writer answers with that endpoint's 403 ProblemDetails shape
    /// (<see cref="Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution, string, string?)"/>).
    /// </summary>
    public Sprk.Bff.Api.Services.Access.SecureRemovalDecision? SecureRemovalRefusal { get; init; }

    /// <summary>
    /// Set on a re-file WRITTEN after it took the row out from under at least one secure root with the caller's F3 permission
    /// (owner round 10 item 7): the roots it left and the permitting decision. <c>null</c> when the re-file left no secure
    /// root, and on every refusal. A step after the re-file may act on that permission for the row only — the resolver
    /// knows nothing of what (#2159 slice 2: the document's file follows it from S1's container into S2's).
    /// </summary>
    public SecureExitPermit? SecureExitPermitted { get; init; }

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

    /// <summary>
    /// A refusal that is not about the owner, thrown on the same "throw" contract so every writer that already catches this
    /// type reports it unchanged: <paramref name="message"/> is used verbatim (it must be safe to surface) and is also the
    /// <see cref="Reason"/>. ebilling-r1 task 026 fix-round-2: the invoice-family guard on the generic update paths.
    /// </summary>
    public RecordOwnerUnresolvedException(string entityLogicalName, string refusalCode, string message)
        : base(message)
    {
        EntityLogicalName = entityLogicalName;
        RefusalCode = refusalCode;
        Reason = message;
    }

    /// <summary>The resolution's reason — what is wrong, for the caller's ProblemDetails detail.</summary>
    public string? Reason { get; }

    /// <summary>The table the refused row belongs to.</summary>
    public string EntityLogicalName { get; }

    /// <summary>The stable <see cref="RecordOwnerRefusal"/> code.</summary>
    public string RefusalCode { get; }
}

/// <summary>
/// #2143: thrown by <see cref="IRecordOwnershipResolver.ReparentAsync"/> when a re-filed row's owner could not be settled
/// against the parents it holds — other writers kept changing the row, re-filed it under parents that resolve to no owner,
/// or re-filed it so that the owner its parents now give would take it out of the Secure Record business unit on a filing
/// this re-file did not make. A conflict with concurrent writers, not a server fault: the HTTP writers answer it 409 with
/// <see cref="ReasonCode"/> (reload the record and retry); queue and AI writers let it propagate as the retryable failure
/// their contracts already give a Dataverse fault.
/// </summary>
/// <remarks>
/// <para><b>The row's state when this is thrown.</b> The change was written (every column it set). Then, after the
/// recovery <c>ReparentAsync</c> runs before rethrowing:</para>
/// <list type="bullet">
/// <item>When the row was still filed as this re-file left it and still owned by its previous owner, its FILING columns
/// (the parent lookups the change moved and its <see cref="RecordReparent.AttachColumns"/>) were written back, so it is
/// filed where the owner it kept belongs. Columns the change set that file nothing stay written.</item>
/// <item>When another writer re-filed or re-owned the row: its newer filing stands, and the owner was re-checked
/// against it.</item>
/// <item>When the owner could not be read back at all: the filing is put back if the resolved owner was the Secure team,
/// and kept otherwise (the safe direction; logged for repair).</item>
/// <item>When this call wrote the Secure team unconditionally (attempts exhausted toward it) and another writer had moved the
/// row out from under its secure record first: the owner its parents now give was written back where the Secure owner was
/// this call's own; otherwise the row is held in the Secure Record business unit and logged
/// <see cref="RecordOwnershipResolver.ReparentLeftInconsistent"/> (#2143 round 2).</item>
/// </list>
/// <para>No ordinary owner that disagrees with the row's current parents was written by this call, and no step after a re-file
/// runs on a row this is thrown for. A row held in the Secure Record business unit stays there (over-restricted, never
/// exposed). No sweep releases such a row (the sweep and the recent-changes pass only move rows INTO isolation); a re-file
/// its F3 check admits, or manual repair from the <see cref="RecordOwnershipResolver.ReparentLeftInconsistent"/> log,
/// settles it. The message names only the row (table and id).</para>
/// <para>CLAUDE.md §11. <i>Existing</i> — <see cref="RecordOwnerUnresolvedException"/> is the resolver's other typed owner
/// failure; it is not reused because every catcher of it (the review-memo and document-operation routes, the action seam,
/// the communication and thread writers, the incoming-communication job's hold) reads it as "refused, nothing was written",
/// and here the change WAS written. <i>Extension</i> — it derives from <see cref="InvalidOperationException"/>, so every
/// writer that does not name it (the queue and AI writers, and those that swallow a filing failure) keeps the handling it
/// already gives a Dataverse fault; only the HTTP writers that answer 409 catch it by name. <i>Cost of doing nothing</i> — a concurrent re-file whose owner cannot be settled
/// reaches the global handler as a 500 "server_error", telling the client the server failed when the record changed
/// under it and a reload and retry settles it.</para>
/// </remarks>
public sealed class RecordOwnerUnsettledException : InvalidOperationException
{
    /// <summary>The ProblemDetails <c>reasonCode</c> an HTTP writer answers this with (409): reload the record and retry.</summary>
    public const string ReasonCode = "record_owner_unsettled";

    public RecordOwnerUnsettledException(string entityLogicalName, Guid recordId, string message)
        : base(message)
    {
        EntityLogicalName = entityLogicalName;
        RecordId = recordId;
    }

    /// <summary>The table of the row whose owner could not be settled.</summary>
    public string EntityLogicalName { get; }

    /// <summary>The row whose owner could not be settled.</summary>
    public Guid RecordId { get; }
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
    private const string OwningUserColumn = "owninguser";
    private const string OwnerColumn = "ownerid";
    private const string IsSecureColumn = "sprk_issecure";
    private const string DocumentEntity = "sprk_document";

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
        // unified-access-control-r2 #2084 (owner round 102 item 4): a rule scoped to a matter is that matter's child, and the
        // parent of its versions. sprk_policyversion is NOT listed: it is a lineage child only (SecureChildLineage), like
        // sprk_signal, because Signals and Decision Records name the version they cite, which files nothing.
        "sprk_policy",
        "sprk_reportcard",
        "sprk_spendsignal",
        "sprk_spendsnapshot",
        "sprk_todo",
    ];

    private static readonly HashSet<string> ParentSet = new(OwnershipParentEntities, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// #2084: lookups to an ownership-parent table that CITE rather than file — (table, column). A Signal names the rule that
    /// raised it (<c>sprk_signal.sprk_policy</c>); it is filed under its subject and core record, never under the rule
    /// (SecureChildLineage leaves the lookup out). Read as a parent, an all-matters rule owned in its author's business unit
    /// would decide an ordinary Signal's team on an Unsecure, and a rule held by a Secure matter would pull a Signal about
    /// another matter into isolation. The one predicate every parent derivation applies: <see cref="RecordOwnershipContext.ParentsOf(IEnumerable{KeyValuePair{string, object}}, string?)"/>
    /// (and through it <see cref="RecordOwnershipContext.ParentsOf(Entity)"/> and <see cref="RecordOwnershipContext.ForChild"/>) and
    /// <c>OwnedChildWrite.ParentsOf</c>.
    /// </summary>
    private static readonly HashSet<(string Table, string Column)> CitationLookups = new(
        [("sprk_signal", "sprk_policy")], CitationComparer.Instance);

    /// <summary>True when <paramref name="column"/> of <paramref name="table"/> cites a record rather than files the row under it.</summary>
    public static bool IsCitation(string? table, string? column) =>
        table is not null && column is not null && CitationLookups.Contains((table, column));

    /// <summary>
    /// UAC task 194b V-fix 3 (coordinator ruling, 2026-10-28): lookups to an ownership-parent table that count for
    /// SECURE-IF-ANY only — (table, column). The opposite rule to <see cref="CitationLookups"/>: a citation is left out of
    /// secure-if-any; one of these is IN secure-if-any and OUT of the primary order. An editable copy's link to the
    /// byte-identical canonical it was saved as (<c>sprk_document.sprk_canonicaldocument</c>, Compose and Office): a copy of
    /// Secure content is Secure, but an ordinary canonical never decides the copy's business unit — following it would hand
    /// the saver's own document to another record's team (task 146). The one definition the writers' question
    /// (<see cref="RecordOwnershipContext.ForRow"/>), the reconciler's (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>),
    /// the re-file's (<see cref="ReparentAsync"/>) and the resolver's look-through read. The link is cut when the copy diverges
    /// (graduate); a graduated copy keeps its owner until a re-file asks again (a known limit, task-194 notes).
    /// </summary>
    private static readonly HashSet<(string Table, string Column)> SecureOnlyLookups = new(
        [("sprk_document", "sprk_canonicaldocument")], CitationComparer.Instance);

    /// <summary>True when <paramref name="column"/> of <paramref name="table"/> is a Secure-only parent
    /// (<see cref="SecureOnlyLookups"/>): in secure-if-any, never the primary.</summary>
    public static bool IsSecureOnlyParent(string? table, string? column) =>
        table is not null && column is not null && SecureOnlyLookups.Contains((table, column));

    /// <summary>
    /// UAC task 194b R4b-2 (owner round 131, the 086 ruling): the HARD-DENY tables. A row of one of these whose ordinary answer
    /// would be a business unit's default team goes instead to the restricted-content owner team
    /// (<see cref="Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam"/>: in the customer's top business unit above
    /// the answer — the root's direct child, owner round 136 — no members, Read only at Basic on exactly these tables), so no
    /// business-unit depth reaches it. A Secure answer is unchanged, and so is
    /// every answer that keeps the creator (the reconciler's unfiled rows, a writer's KeepCreator). Read through
    /// <see cref="IsRestrictedContentTable"/> at the resolver's ordinary exits, for the table
    /// <see cref="RecordOwnershipContext.ChildEntityLogicalName"/> names.
    /// </summary>
    private static readonly HashSet<string> RestrictedContentTableSet = new(
        ["sprk_storedfile", "sprk_procedureassignment", "sprk_ruleset", "sprk_policy", "sprk_policyversion"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The hard-deny tables (<see cref="RestrictedContentTableSet"/>).</summary>
    public static IReadOnlyCollection<string> RestrictedContentTables => RestrictedContentTableSet;

    /// <summary>True when <paramref name="table"/> is a hard-deny table (<see cref="RestrictedContentTableSet"/>).</summary>
    public static bool IsRestrictedContentTable(string? table) =>
        !string.IsNullOrWhiteSpace(table) && RestrictedContentTableSet.Contains(table.Trim());

    private sealed class CitationComparer : IEqualityComparer<(string Table, string Column)>
    {
        public static readonly CitationComparer Instance = new();

        public bool Equals((string Table, string Column) x, (string Table, string Column) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Table, y.Table) && StringComparer.OrdinalIgnoreCase.Equals(x.Column, y.Column);

        public int GetHashCode((string Table, string Column) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
    }

    /// <summary>True when a lookup to <paramref name="entityLogicalName"/> files a child under a parent for ownership.</summary>
    public static bool IsOwnershipParent(string? entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && ParentSet.Contains(entityLogicalName);

    /// <summary>
    /// True for a CHILD table whose owner a generic re-filing must re-derive: an ownership parent that is not itself a
    /// root. A root's own ownership (the three secure-flagged roots and the service request) is not re-derived by a generic
    /// re-filing: Make Secure and Remove Secure stay provisioning's — a secure root is provisioned into isolation through task
    /// 144's endpoint, never by a bare re-own (owner S6) — so a generic update that changes a root's lookups does not reassign
    /// it. Owner amendment 2026-10-29 ("the family members have the same ownership"): in ORDINARY (non-secure) mode a FILED
    /// work assignment or project follows its family's answer, and an unfiled person-owned one is owned by its person's
    /// business-unit team — <c>SecureRootInheritance.FollowParentsPassAsync</c>'s root-owner step, through
    /// <c>SecureChildReconciler.ReownFiledRootAsync</c> (matters are not in it). Write-side only: the owner question's read
    /// side also looks through a work assignment or project with no owning team (<see cref="IsLookThroughParent"/>).
    /// </summary>
    public static bool IsReparentableChild(string? entityLogicalName) =>
        IsOwnershipParent(entityLogicalName)
        && !SecureFlaggedRoots.Contains(entityLogicalName!)
        && !string.Equals(entityLogicalName, "sprk_servicerequest", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A CORE record: a root (the three secure-flagged roots) or the service request. Never content of a row asked on the
    /// content path (<see cref="RecordOwnershipContext.KeepCreatorUnlessTargetIsTeamOwned"/>): it stays in the secure check.
    /// </summary>
    private static bool IsCoreRecord(string? entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName)
        && (SecureFlaggedRoots.Contains(entityLogicalName)
            || string.Equals(entityLogicalName, "sprk_servicerequest", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A parent whose filing answers for it when it is not team-owned (the owner question's look-through and the secure
    /// check's lineage): every reparentable child and, owner decision 2026-10-29, the work assignment and the project — each
    /// belongs to its core record, so a person-owned, filed work assignment takes its matter's (then project's) business unit,
    /// and a person-owned project filed by its BFF-managed pair takes the business unit of the record the pair names
    /// (<see cref="ReadPairParentAsync"/>). A project whose pair is empty, or names a record that no longer exists, is filed
    /// under nothing and keeps its own business unit (fix round B-K1/K2/K3: never looked through, never its other lookups).
    /// Read-side only: the write-side re-file gates ask <see cref="IsReparentableChild"/>, under which both stay roots.
    /// </summary>
    private static bool IsLookThroughParent(string? entityLogicalName) =>
        IsReparentableChild(entityLogicalName)
        || string.Equals(entityLogicalName, Sprk.Bff.Api.Services.Access.SecureRootInheritance.WorkAssignment,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(entityLogicalName, Sprk.Bff.Api.Services.Access.SecureRootInheritance.Project,
            StringComparison.OrdinalIgnoreCase);

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
        var resolution = await ResolveOwnerOnlyAsync(context, committedParentReads: false, ct).ConfigureAwait(false);
        return resolution.IsOwned ? resolution.OwningTeamId : null;
    }

    /// <inheritdoc />
    public async Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await ResolveOwnerOnlyAsync(context, committedParentReads: false, ct).ConfigureAwait(false);
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
        // Issue #1576: a disabled systemuser keeps its oid; it is nobody's creator person.
        query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

        var users = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        if (users.Count == 1 && users[0].Id != Guid.Empty)
            return users[0].Id;

        _logger.LogWarning(
            "The person who asked for a new row (Entra object id {ObjectId}) matches {Count} Dataverse users; the row records "
            + "no creator person (task 146, owner round 13 item 9).", objectId, users.Count);
        return null;
    }

    /// <summary>The owner decision alone — <see cref="ResolveOwnerAsync"/> without the person who asked.</summary>
    /// <param name="committedParentReads">#2143 round 3 (K1): <c>true</c> on the re-file path only — every parent fact
    /// (owning business unit, owning team, <c>sprk_issecure</c>, a parent's own filing) is read committed
    /// (<c>NoLock = false</c>). The create paths keep their <c>NoLock</c> reads.</param>
    private async Task<RecordOwnerResolution> ResolveOwnerOnlyAsync(
        RecordOwnershipContext context, bool committedParentReads, CancellationToken ct)
    {
        var parents = CollectParents(context);

        // #2468: a DOCUMENT whose primary parent is a communication filed under nothing. Such a communication keeps its
        // creating identity (E1: the application user, or a root team) — a technical owner, never the family the document
        // belongs to — so it does not name the document's owner: it counts for secure-if-any only, and the document takes
        // the question's unfiled answer (an Office save: the saver's team, task 080; the reconciler: the owner it has). A
        // FILED communication still names it (the family rule). Decided from the communication's filing alone, never from
        // its owner kind; ContentOf questions (no ChildEntityLogicalName) are unchanged.
        if (parents.Count > 0
            && string.Equals(context.ChildEntityLogicalName, DocumentEntity, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parents[0].EntityLogicalName, RecordThreadFiling.CommunicationTable, StringComparison.OrdinalIgnoreCase))
        {
            var filed = await IsCommunicationFiledAsync(parents[0], ct).ConfigureAwait(false);
            if (filed is null)
            {
                return RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the parent {parents[0].EntityLogicalName} {parents[0].RecordId:D} or the thread it sits in could not be "
                    + "read, so whether it is filed cannot be decided");
            }

            if (filed == false)
            {
                var rest = parents.Skip(1).ToArray();
                var demoted = context with
                {
                    TargetEntityLogicalName = rest.FirstOrDefault()?.EntityLogicalName,
                    TargetRecordId = rest.FirstOrDefault()?.RecordId,
                    Parents = rest,
                    SecureOnlyParents = context.SecureOnlyParents.Append(parents[0]).Distinct().ToArray(),
                };
                return await ResolveOwnerOnlyAsync(demoted, committedParentReads, ct).ConfigureAwait(false);
            }
        }

        // ── 1. PREFERRED: the parents' business units (record-first, secure-if-any) ─────────────────────
        // A filed record belongs with what it is filed against, not with whoever uploaded it. This is also
        // the only source available when there is no acting user at all (inbound email).
        if (parents.Count > 0)
        {
            return await ResolveFromParentsAsync(parents, context, committedParentReads, ct).ConfigureAwait(false);
        }

        // ── 2. Nothing named ──────────────────────────────────────────────────────────────────────────────
        // V-fix 3: a row filed under nothing but a Secure-only parent (an unfiled copy of a canonical) is Secure when that
        // parent is, and otherwise takes the unfiled answer below — never the Secure-only parent's business unit.
        if (context.SecureOnlyParents.Count > 0
            && await SecureDecisionAsync(Array.Empty<ParentFacts>(), context, committedParentReads, ct).ConfigureAwait(false)
                is { } secureOnlyDecided)
        {
            return secureOnlyDecided;
        }

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
            return await ResolveFromUserAsync("systemuserid", systemUserId, context, ct).ConfigureAwait(false);
        }

        if (context.CallerObjectId is { } objectId && objectId != Guid.Empty)
        {
            return await ResolveFromUserAsync("azureactivedirectoryobjectid", objectId, context, ct).ConfigureAwait(false);
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

        // #2143 round 5: the lineage overlay below reads this call's own lineage-only changes from ParentChanges, the only
        // place a value is given. A lineage-only lookup named only in AttachColumns would read as ANOTHER writer's move on
        // every call, so it is a programming error, refused before anything is read or written.
        foreach (var column in request.AttachColumns)
        {
            if (RecordReparent.LineageOnlyTargetOf(request.EntityLogicalName, column) is not null
                && !request.ParentChanges.Keys.Any(key => string.Equals(key, column, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"AttachColumns names the lineage-only lookup '{column}' of {request.EntityLogicalName}, but ParentChanges "
                    + "does not: name it there with the row the change writes (#2143).",
                    nameof(request));
            }
        }

        // UAC task 194b fix round R (A5): a Signal's or a Decision Record's filing is its WRITER's (SignalWriter,
        // DecisionCommitService), whose question names the core first and, for a Decision Record, the Signals it resolved
        // (ForExistingRowAsync). No update path re-files them through here; the generic writers that could (an Update Record
        // node, a playbook's output mapping, a field-mapping push) would ask only the lookups they write. Refused before
        // anything is read or written (fail closed).
        if (RecordOwnershipContext.CorePairTables.Contains(request.EntityLogicalName.Trim()))
        {
            _logger.LogWarning(
                "Refusing to re-file {Entity} {RecordId}: its filing is written only by its own writer (194b A5). The change was "
                + "NOT written.", request.EntityLogicalName, request.RecordId);
            return RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined, RecordOwnershipContext.WriterOwnedFilingRefusal);
        }

        var current = await ReadWholeRowAsync(request, ct).ConfigureAwait(false);

        if (current is null)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.RecordMissing,
                $"the {request.EntityLogicalName} {request.RecordId:D} being re-filed does not exist");
        }

        // Current parents, keyed by column, overlaid with the change.
        var byColumn = OwnershipParentColumnsOf(current);

        // #2143 round 6: a message also takes its record thread's filing (RecordThreadFiling, the reconciler's rule) — before
        // the change from the thread it sits in, after it from the thread the change puts it in (the one it names in
        // ParentChanges, else the same). Read committed; a thread that cannot be read fails the call before anything is written.
        var threadBefore = await RecordThreadFiling.ParentsAsync(
            _dataverse, request.EntityLogicalName, RecordThreadFiling.ThreadOf(current), ct).ConfigureAwait(false);
        var threadAfter = threadBefore;
        var namesAnotherThread = false;
        if (RecordThreadFiling.AppliesTo(request.EntityLogicalName)
            && request.ParentChanges.FirstOrDefault(change =>
                    string.Equals(change.Key, RecordThreadFiling.ThreadColumn, StringComparison.OrdinalIgnoreCase))
                is { Key: not null } threadChange
            && (threadChange.Value?.Id ?? Guid.Empty) != (RecordThreadFiling.ThreadOf(current) ?? Guid.Empty))
        {
            namesAnotherThread = true;
            threadAfter = await RecordThreadFiling.ParentsAsync(
                _dataverse, request.EntityLogicalName, threadChange.Value?.Id, ct).ConfigureAwait(false);
        }

        // Every parent the row is filed under BEFORE the change — what a move OUT of a secure root is measured against
        // (owner round 10 item 7, task 146 c1).
        var filedUnderBefore = byColumn.Values.Concat(threadBefore).Distinct().ToArray();

        // #2143 round 7 (F1): the parents the row's OWN columns hold before the change — F3 is asked about every secure record
        // the change takes out of them, even while the row's thread still files it under that record.
        var ownBefore = byColumn.Values.Distinct().ToArray();

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
        var movesALineageLookup = false;
        foreach (var (column, value) in request.ParentChanges)
        {
            if (RecordReparent.LineageOnlyTargetOf(request.EntityLogicalName, column) is not null)
            {
                // #2110 (ontology K3): a lineage lookup that is no ownership parent (a message's thread) decides no owner,
                // but one the change moves — set to another row, or cleared while the row holds one — is a change the step
                // after a re-file must see.
                var held = current.GetAttributeValue<EntityReference>(column)?.Id ?? Guid.Empty;
                if ((value?.Id ?? Guid.Empty) != held)
                {
                    movesALineageLookup = true;
                    // #2143 round 7 (F4): put back with the rest of the change when the owner assignment fails — a thread move
                    // the change makes is part of what the row is filed under (RecordThreadFiling).
                    Remember(column);
                }

                continue;
            }

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
                // #2091 round 4: re-binding the parent the column already holds moves nothing (a push from the parent it
                // files its children under writes exactly that).
                var parent = new RecordOwnershipParent(value.LogicalName, value.Id);
                if (byColumn.TryGetValue(column, out var held)
                    && string.Equals(held.EntityLogicalName, parent.EntityLogicalName, StringComparison.OrdinalIgnoreCase)
                    && held.RecordId == parent.RecordId)
                {
                    continue;
                }

                byColumn[column] = parent;
                changesAParent = true;
                Remember(column);
            }
        }

        foreach (var column in request.AttachColumns.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            Remember(column);
        }

        // #2143 round 6: a change that moves the message to a thread filed under other parents changes what it is filed under.
        changesAParent |= !threadBefore.ToHashSet().SetEquals(threadAfter);

        if (!changesAParent)
        {
            // Nothing the row is filed under changes, so its owner does not either: write the change and decide nothing
            // (task 146 verifier item 8 — the writer passed every null it writes; none of them was a parent).
            await applyChange(ct).ConfigureAwait(false);
            var unchanged = RecordOwnerResolution.Unchanged(
                $"the change to {request.EntityLogicalName} {request.RecordId:D} sets no parent and clears none it holds");
            return unchanged with { ParentsUnchanged = !movesALineageLookup };
        }

        // UAC task 194b: in the table's primary order (RecordOwnershipContext.OrderedParentsOf), never the attribute order.
        // V-fix 3: a Secure-only column (a copy's canonical) is asked for secure-if-any only, never in the primary order.
        var parents = WithInheritedParents(
            RecordOwnershipContext.InPrimaryOrder(
                    request.EntityLogicalName, FilingColumns(request.EntityLogicalName, byColumn),
                    RecordOwnershipContext.SubjectColumnOf(
                        request.EntityLogicalName,
                        current.Attributes.TryGetValue(CoreAncestorResolver.RegardingRecordIdColumn, out var pairText) ? pairText as string : null,
                        column => byColumn.TryGetValue(column, out var heldParent) ? heldParent.RecordId : null))
                .Select(c => c.Value).Concat(threadAfter),
            request);
        var secureOnlyAfter = SecureOnlyColumns(request.EntityLogicalName, byColumn);
        var resolution = await ResolveReparentOwnerAsync(request, parents, secureOnlyAfter, ct).ConfigureAwait(false);

        // R4b-2c (rule 1, owner round 137 "author wins"): the restricted-content team is asked for only in place of a default
        // team, so an author-owned row keeps its author whether or not that team can be used — never a refusal for it.
        if (AuthorKeepsRestrictedContent(resolution, current.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id))
        {
            resolution = RecordOwnerResolution.Unchanged(RecordOwnershipContext.AuthorKeptRestrictedContentReason);
        }

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
        // #2143 round 7b: a change to the message's OWN filing that leaves its thread as it is will be followed by the thread
        // reconcile (ThreadResolver.ReconcileThreadForFilingAsync), which moves the message off a thread anchored to a record it
        // is no longer filed to. That move is this re-file's: the thread's parents are what it leaves.
        var ownAfter = byColumn.Values.Distinct().ToArray();
        var threadTheReconcileMayLeave = RecordThreadFiling.AppliesTo(request.EntityLogicalName) && !namesAnotherThread
                                         && !ownBefore.ToHashSet().SetEquals(ownAfter)
            ? threadBefore
            : Array.Empty<RecordOwnershipParent>();
        var (exitRefusal, permittedToLeaveSecure, exitPermit) = await RefuseUnlessPermittedToLeaveSecureRootsAsync(
            // V-fix 3: filed under, after the change, includes the Secure-only parents it still holds (as filedUnderBefore does),
            // so keeping a copy's canonical is never read as leaving the canonical's secure root.
            request, current, filedUnderBefore, parents.Concat(secureOnlyAfter).Distinct().ToArray(), ownBefore, ownAfter,
            threadTheReconcileMayLeave, resolution, ct)
            .ConfigureAwait(false);
        if (exitRefusal is not null)
        {
            _logger.LogWarning(
                "Refusing to re-file {Entity} {RecordId} out of a secure record: {Reason} ({Code}). The change was NOT "
                + "written.", request.EntityLogicalName, request.RecordId, exitRefusal.Reason, exitRefusal.RefusalCode);
            return exitRefusal;
        }

        // #2159 slice 2: an answer of this written re-file carries the F3 permission it was given (the roots it left) — the
        // answer resolved before the change, and the one the settle derived from the filing THIS call made (PR #2161 round 4,
        // F1-a: never one it re-derived from another writer's filing, which this call's F3 check never saw).
        RecordOwnerResolution WithExitPermit(RecordOwnerResolution answer) =>
            exitPermit is null ? answer : answer with { SecureExitPermitted = exitPermit };

        // #2143 round 2 (F3c): the row's owner as read BEFORE the change, captured before the change is written. `current`
        // is the SDK's own copy of the row, so the change cannot alter it in production; capturing it here keeps "before"
        // true by construction rather than by that property of the client.
        // #2143 round 3 (F1): with the lineage-only lookups this change itself moves applied, so only ANOTHER writer's move
        // of one reads as a change. Round 5 (F1): only ParentChanges carries a value, so a writer that sets a lineage-only
        // lookup names it there too (a thread join: ThreadResolver), never only in AttachColumns (refused at the top of this
        // method). Enforced by: Reparent_WhenAnAttachColumnIsALineageOnlyLookupNotNamedInParentChanges_ThrowsArgumentException_BeforeAnyReadOrWrite.
        var lineageAfterThisChange = LineageOnlyLookupsOf(request.EntityLogicalName, current);
        foreach (var (column, value) in request.ParentChanges)
        {
            if (RecordReparent.LineageOnlyTargetOf(request.EntityLogicalName, column) is null)
                continue;
            if (value is { } set && set.Id != Guid.Empty)
                lineageAfterThisChange[column] = set.Id;
            else
                lineageAfterThisChange.Remove(column);
        }

        var before = new RowBeforeChange(
            current.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id,
            current.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id,
            permittedToLeaveSecure,
            lineageAfterThisChange);

        await applyChange(ct).ConfigureAwait(false);

        if (!resolution.IsOwned)
        {
            return WithExitPermit(resolution); // Unchanged — the row keeps its owner.
        }

        // Ownership is assigned on its own, not folded into the field update: Dataverse treats an owner change as a
        // distinct operation, and combining them is a documented way to have one of the two quietly not happen
        // (the ProvisionProjectEndpoint.AssignOwnerToSecureTeamAsync rationale). #2143: it is assigned CONDITIONALLY, at the
        // version read after the change, from the parents the row holds at that version (SettleOwnerAsync).
        var observed = new AfterChangeObservation();
        try
        {
            var (settled, filingIsOwn) = await SettleOwnerAsync(request, parents, before, observed, ct).ConfigureAwait(false);
            if (filingIsOwn)
            {
                return WithExitPermit(settled);
            }

            // PR #2161 round 4 (F1-a): the settle answered from a filing another writer made after this call's change (its
            // parents or lineage-only lookups are not the ones this call wrote). This call's F3 check approved leaving the
            // roots for THIS call's filing only, so no step after the re-file may act on it for that one: no permit, and a
            // file held in the root it left stays held (fail closed).
            if (exitPermit is not null)
            {
                _logger.LogWarning(
                    "Re-file of {Entity} {RecordId}: its owner was settled from a filing another writer made after this change; "
                    + "the F3 permission to leave {Count} secure record(s) is not carried to it.",
                    request.EntityLogicalName, request.RecordId, exitPermit.RootsLeft.Count);
            }

            return settled;
        }
        catch (Exception assignFailure)
        {
            // The change is already written and the owner may not be: the row would be filed under its NEW parents while
            // still owned for its OLD ones — for a move under a secure record, readable in an ordinary business unit
            // (ADR-003 fail-open, task 146 b1/b2 verifier item). Recover first, then surface the failure.
            if (await RecoverFromFailedAssignmentAsync(request, restore, parents, resolution, before, observed, assignFailure)
                    .ConfigureAwait(false) is { } landed)
            {
                // The assignment landed despite the failure report: the re-file stands.
                if (landed.FilingIsOwn)
                {
                    return WithExitPermit(landed.Answer); // filing and owner agree, on the filing this call made
                }

                // PR #2161 round 5 (K1): the filing could not be read back, so the answer stands (#2150) on a filing nobody
                // confirmed as this call's own. The F3 permission rides only on a confirmed one (round 4's FilingIsOwn rule):
                // no permit, and a file held in the root it left stays held (fail closed).
                if (exitPermit is not null)
                {
                    _logger.LogWarning(
                        "Re-file of {Entity} {RecordId}: its owner assignment landed but its filing could not be read back, so "
                        + "the filing could not be confirmed as this re-file's own; the F3 permission to leave {Count} secure "
                        + "record(s) is not carried to it.",
                        request.EntityLogicalName, request.RecordId, exitPermit.RootsLeft.Count);
                }

                return landed.Answer;
            }

            throw;
        }
    }

    /// <summary>
    /// R4b-2c (rule 1): whether a re-file's answer for a row owned by <paramref name="owningUser"/> is the restricted-content
    /// team that cannot be used — asked only in place of a default team, which never replaces a kept author — so the row
    /// keeps its author rather than refusing. A row owned by a team (a default team included) still refuses.
    /// </summary>
    private static bool AuthorKeepsRestrictedContent(RecordOwnerResolution answer, Guid? owningUser) =>
        owningUser is { } user && user != Guid.Empty
        && answer.IsRefused
        && answer.RefusalCode == RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved;

    /// <summary>
    /// #2143: how many times a re-file's owner write is tried against a row that keeps changing — the attempt count of the
    /// existing 412 pattern (<c>InvoiceReviewService.MaxConditionalWriteAttempts</c>). The compensating put-back uses it too.
    /// </summary>
    internal const int MaxOwnerWriteAttempts = 3;

    private const string VersionNumberColumn = "versionnumber";

    /// <summary>
    /// The row as the first read AFTER the change found it (#2143): the version a compensating put-back is conditioned on,
    /// and whether the row was still filed under the parents the owner was resolved from — when it was not, another writer
    /// re-filed it first and a put-back would overwrite that newer filing.
    /// </summary>
    private sealed class AfterChangeObservation
    {
        public bool Read { get; set; }

        public string? Version { get; set; }

        public bool ParentsAsResolved { get; set; }

        /// <summary>The team that owned the row at that read (K2: a put-back requires it to be the pre-change owner).</summary>
        public Guid? OwningTeam { get; set; }

        /// <summary>#2143 round 8: the row's lineage-only lookups at that read (a put-back requires them to be this call's).</summary>
        public IReadOnlyDictionary<string, Guid> LineageLookups { get; set; } = new Dictionary<string, Guid>();

        /// <summary>
        /// The owner the settle loop last decided to write (F1-a re-derives it on every attempt, so it can differ from the
        /// answer resolved before the change): what a recovery checks "the assignment landed" against.
        /// </summary>
        public RecordOwnerResolution? LastTarget { get; set; }
    }

    /// <summary>
    /// The row's owner as read BEFORE the change (#2143): its owning team — what a put-back restores the filing for — its
    /// owning business unit, and whether this call's F3 check (<see cref="RefuseUnlessPermittedToLeaveSecureRootsAsync"/>)
    /// was asked and PERMITTED the row to leave a secure record. The last two together say whether this call's own F3 check
    /// covered taking the row out of the Secure Record business unit (<see cref="WouldLeaveSecureBusinessUnitAsync"/>).
    /// <c>LineageLookups</c> (#2143 round 3) is the row's lineage-only lookups as read before the change WITH this call's own
    /// changes to them applied — what the row holds after the change when no other writer moved one.
    /// </summary>
    private sealed record RowBeforeChange(
        Guid? OwningTeam, Guid? OwningBusinessUnit, bool PermittedToLeaveSecure,
        IReadOnlyDictionary<string, Guid> LineageLookups);

    /// <summary>
    /// A row's version, owner and ownership parents, from one read (#2143) — and (round 3) its lineage-only lookups
    /// (<see cref="RecordReparent.LineageOnlyTargetOf"/>: a message's thread), column → row id.
    /// </summary>
    /// <remarks>
    /// The lineage-only lookups decide no owner here: <see cref="Parents"/> holds the row's own ownership-parent columns, this
    /// request's <see cref="RecordReparent.InheritedParents"/> and (#2143 round 6) the filing of the record thread the row sits
    /// in as of this read (<c>RecordThreadFiling</c>), so a join — already on the row or landing between reads — is a parent the
    /// settle, the release, K4 and the guard all see. A changed lineage-only lookup stays the cheap first check (round 3):
    /// neither the put-back of this call's Secure write (<see cref="ReleaseOwnSecureWriteAsync"/>) nor the guard's F3 exemption
    /// (<see cref="WouldLeaveSecureBusinessUnitAsync"/>) is taken over another writer's move of one.
    /// </remarks>
    private sealed record RowOwnership(
        string? Version, Guid? OwningTeam, Guid? OwningBusinessUnit, IReadOnlyCollection<RecordOwnershipParent> Parents,
        IReadOnlyDictionary<string, Guid> LineageLookups, IReadOnlyList<RecordOwnershipParent> SecureOnlyParents)
    {
        /// <summary>R4b-2b: the user who owns the row (its author), when a user does.</summary>
        public Guid? OwningUser { get; init; }
    }

    /// <summary>Every lineage-only lookup (<see cref="RecordReparent.LineageOnlyTargetOf"/>) <paramref name="row"/> holds,
    /// column → row id (#2143 round 3). An empty lookup is absent, as the every-column read returns it.</summary>
    private static Dictionary<string, Guid> LineageOnlyLookupsOf(string table, Entity row)
    {
        var lookups = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in row.Attributes)
        {
            if (value is EntityReference reference && reference.Id != Guid.Empty
                && RecordReparent.LineageOnlyTargetOf(table, column) is not null)
            {
                lookups[column] = reference.Id;
            }
        }

        return lookups;
    }

    /// <summary>Whether two reads hold the same lineage-only lookups (#2143 round 3).</summary>
    private static bool SameLineage(IReadOnlyDictionary<string, Guid> left, IReadOnlyDictionary<string, Guid> right) =>
        left.Count == right.Count && left.All(lookup => right.TryGetValue(lookup.Key, out var id) && id == lookup.Value);

    /// <summary>
    /// The row's current state — every column, because a child's parents are whatever lookups it carries, and a per-table
    /// column list is exactly the guess task 146 forbids. A reparent is an explicit filing action, not a hot path, so the
    /// wide read is the honest price. <c>null</c> when the row does not exist.
    /// </summary>
    /// <remarks>
    /// #2143 round 2: a COMMITTED read (<c>NoLock = false</c>). Every settle and put-back decision is taken from this read —
    /// "already owned, write nothing", the version a conditional write is conditioned on, the parents the owner is derived
    /// from — and a dirty read could answer "already owned" from another writer's uncommitted owner write that then rolls
    /// back, leaving the row with no owner write at all. The pre-#2143 owner check was a committed <c>RetrieveAsync</c>.
    /// Round 3 (K1): the facts of those parents — their owning business unit and team, <c>sprk_issecure</c>, a parent's own
    /// filing — are read committed on this path too (<see cref="ResolveReparentOwnerAsync"/>, the F3 walk
    /// <see cref="SecureRootsAboveAsync"/>); only the create paths keep <c>NoLock</c> parent reads.
    /// </remarks>
    private async Task<Entity?> ReadWholeRowAsync(RecordReparent request, CancellationToken ct)
    {
        var query = new QueryExpression(request.EntityLogicalName)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = false,
        };
        query.Criteria.AddCondition($"{request.EntityLogicalName}id", ConditionOperator.Equal, request.RecordId);
        return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    /// <summary>Every ownership-parent lookup <paramref name="row"/> holds, keyed by column.</summary>
    private static Dictionary<string, RecordOwnershipParent> OwnershipParentColumnsOf(Entity row)
    {
        var byColumn = new Dictionary<string, RecordOwnershipParent>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in row.Attributes)
        {
            if (value is EntityReference reference && reference.Id != Guid.Empty
                && IsOwnershipParent(reference.LogicalName))
            {
                byColumn[column] = new RecordOwnershipParent(reference.LogicalName, reference.Id);
            }
        }

        return byColumn;
    }

    /// <summary>V-fix 3: the columns of <paramref name="byColumn"/> that file the row (every one but a Secure-only parent).</summary>
    private static IEnumerable<KeyValuePair<string, RecordOwnershipParent>> FilingColumns(
        string table, IReadOnlyDictionary<string, RecordOwnershipParent> byColumn) =>
        byColumn.Where(c => !IsSecureOnlyParent(table, c.Key));

    /// <summary>V-fix 3: the Secure-only parents of <paramref name="byColumn"/> (<see cref="IsSecureOnlyParent"/>), distinct.</summary>
    private static RecordOwnershipParent[] SecureOnlyColumns(
        string table, IReadOnlyDictionary<string, RecordOwnershipParent> byColumn) =>
        byColumn.Where(c => IsSecureOnlyParent(table, c.Key)).Select(c => c.Value).Distinct().ToArray();

    /// <summary><paramref name="ownParents"/> plus the request's <see cref="RecordReparent.InheritedParents"/>, distinct.</summary>
    private static RecordOwnershipParent[] WithInheritedParents(
        IEnumerable<RecordOwnershipParent> ownParents, RecordReparent request) =>
        ownParents
            .Concat(request.InheritedParents.Where(p => p.IsSpecified && IsOwnershipParent(p.EntityLogicalName)))
            .Distinct()
            .ToArray();

    /// <summary>The owner a re-filed row takes from <paramref name="parents"/>, for the request's caller — from parent facts
    /// read committed (#2143 round 3, K1: the settle and put-back decisions are taken from them).</summary>
    private Task<RecordOwnerResolution> ResolveReparentOwnerAsync(
        RecordReparent request, IReadOnlyList<RecordOwnershipParent> parents, IReadOnlyList<RecordOwnershipParent> secureOnly,
        CancellationToken ct) =>
        ResolveOwnerOnlyAsync(
            new RecordOwnershipContext
            {
                Parents = parents,
                SecureOnlyParents = secureOnly, // V-fix 3: secure-if-any only
                ChildEntityLogicalName = request.EntityLogicalName, // R4b-2
                CallerSystemUserId = request.CallerSystemUserId,
                CallerObjectId = request.CallerObjectId,
                WhenUnfiled = request.WhenUnfiled,
            },
            committedParentReads: true,
            ct);

    /// <summary>Whether <paramref name="state"/>'s parents are exactly <paramref name="resolvedFrom"/> (record equality; the
    /// table names are normalised by <see cref="RecordOwnershipParent"/>).</summary>
    private static bool FiledAsResolved(RowOwnership state, IReadOnlyCollection<RecordOwnershipParent> resolvedFrom) =>
        state.Parents.Count == resolvedFrom.Count && state.Parents.All(resolvedFrom.Contains);

    /// <summary>The row's version, owner and ownership parents in ONE read (#2143); <c>null</c> when it no longer exists.</summary>
    private async Task<RowOwnership?> ReadRowOwnershipAsync(RecordReparent request, CancellationToken ct)
    {
        var row = await ReadWholeRowAsync(request, ct).ConfigureAwait(false);
        if (row is null)
            return null;

        var version = row.RowVersion
            ?? (row.GetAttributeValue<long?>(VersionNumberColumn) is { } number
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null);
        // #2143 round 6: with its record thread's filing, as the thread reads NOW (RecordThreadFiling) — so a join already on
        // the row, or one landing between reads, is a parent the settle, the release, K4 and the guard all see.
        var threadParents = await RecordThreadFiling.ParentsAsync(
            _dataverse, request.EntityLogicalName, RecordThreadFiling.ThreadOf(row), ct).ConfigureAwait(false);
        return new RowOwnership(
            string.IsNullOrWhiteSpace(version) ? null : version,
            row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id,
            row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id,
            WithInheritedParents(
                RecordOwnershipContext.InPrimaryOrder(
                        request.EntityLogicalName, FilingColumns(request.EntityLogicalName, OwnershipParentColumnsOf(row)),
                        RecordOwnershipContext.SubjectColumnOf(row))
                    .Select(c => c.Value).Concat(threadParents),
                request),
            LineageOnlyLookupsOf(request.EntityLogicalName, row),
            SecureOnlyColumns(request.EntityLogicalName, OwnershipParentColumnsOf(row)))
        {
            OwningUser = row.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id is { } user && user != Guid.Empty ? user : null,
        };
    }

    /// <summary>
    /// #2143: makes the re-filed row's owner the team its CURRENT parents resolve to, never one resolved from parents it no
    /// longer holds or from parent facts that have since changed. Each attempt reads the row once (version, owner, parents)
    /// and RE-DERIVES the owner from the parents it holds (the parents' own facts are read live, so a parent made Secure in
    /// between answers the Secure team); when the row is already owned by that team nothing is written; otherwise the owner
    /// is written CONDITIONALLY at the version read, and a row changed in between
    /// (<see cref="System.Data.DBConcurrencyException"/>) is read again, up to <see cref="MaxOwnerWriteAttempts"/> attempts.
    /// </summary>
    /// <remarks>
    /// <para><b>Fail closed.</b> A re-derivation that is REFUSED writes no owner and throws. An ORDINARY owner is not written
    /// to a row held in the Secure Record business unit (<see cref="WouldLeaveSecureBusinessUnitAsync"/>) unless this call's
    /// own F3 check covered that move — the row is still filed exactly as this call resolved it, and it was held there BEFORE
    /// the change or this call's F3 check permitted it to leave a secure record; otherwise the filing it answers was not this
    /// call's, the row stays over-restricted, and the call throws at once. Attempts that run out toward an ordinary owner
    /// throw; attempts that run out toward the SECURE team end in one unconditional write of the Secure team (F1-b): leaving a
    /// row filed under a secure record with an ordinary owner until the reconcile is the exposure. That write is then checked
    /// against a fresh read (<see cref="ConfirmUnconditionalSecureWriteAsync"/>): a concurrent writer may already have moved
    /// the row out and settled its own owner, and nothing re-owns it after that, so when the fresh parents no longer resolve
    /// to the Secure team this call puts back the owner they give where it can (<see cref="ReleaseOwnSecureWriteAsync"/>),
    /// logs the row for repair where it cannot, and throws. Every throw but a missing row version is
    /// <see cref="RecordOwnerUnsettledException"/>, naming only the row.</para>
    /// <para>The owner is read AFTER the change, never taken from the row read before it (task 146 b2): another writer
    /// may have re-owned the row in between, and a stale "already owned" answer would leave the re-filed row with that
    /// writer's owner. A written owner is read back (task 146).</para>
    /// </remarks>
    /// <returns>
    /// The resolution the row's owner now matches, and (PR #2161 round 4, F1-a) whether it was derived from the filing THIS
    /// call made: the row read was still filed exactly as this call resolved it, with the lineage-only lookups this call left
    /// it (<see cref="RowBeforeChange.LineageLookups"/>) — the same test the Secure-unit guard and the recovery apply. A
    /// caller carries this call's F3 permission only on such an answer.
    /// </returns>
    private async Task<(RecordOwnerResolution Answer, bool FilingIsOwn)> SettleOwnerAsync(
        RecordReparent request, IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RowBeforeChange before,
        AfterChangeObservation? observed, CancellationToken ct)
    {
        RecordOwnerResolution? lastTarget = null;
        RowOwnership? lastState = null;
        for (var attempt = 1; attempt <= MaxOwnerWriteAttempts; attempt++)
        {
            var state = await ReadRowOwnershipAsync(request, ct).ConfigureAwait(false)
                ?? throw new RecordOwnerUnsettledException(
                    request.EntityLogicalName, request.RecordId,
                    $"The {request.EntityLogicalName} {request.RecordId:D} being re-filed no longer exists, so its owner could "
                    + "not be settled (#2143).");
            lastState = state;

            var parentsAsResolved = FiledAsResolved(state, resolvedFrom);
            var filingIsOwn = parentsAsResolved && SameLineage(state.LineageLookups, before.LineageLookups);
            if (observed is { Read: false })
            {
                observed.Read = true;
                observed.Version = state.Version;
                observed.ParentsAsResolved = parentsAsResolved;
                observed.OwningTeam = state.OwningTeam;
                observed.LineageLookups = state.LineageLookups;
            }

            // F1-a: re-derived on EVERY attempt, from the parents read now — never the answer from before the change, which a
            // parent made Secure since (SecureChildReconciler re-owning the row first) would turn into a stale ordinary owner.
            var target = await ResolveReparentOwnerAsync(request, state.Parents.ToArray(), state.SecureOnlyParents, ct)
                .ConfigureAwait(false);
            if (!parentsAsResolved)
            {
                _logger.LogWarning(
                    "Re-file of {Entity} {RecordId}: its parents changed after the change was written (a concurrent writer); "
                    + "the owner is re-derived from the parents it holds now: {Outcome} {TeamId} (attempt {Attempt}, #2143).",
                    request.EntityLogicalName, request.RecordId, target.Outcome, target.OwningTeamId, attempt);
            }

            // R4b-2c (rule 1): an author-owned row whose answer is now an unusable restricted-content team keeps its author.
            if (AuthorKeepsRestrictedContent(target, state.OwningUser))
            {
                return (RecordOwnerResolution.Unchanged(RecordOwnershipContext.AuthorKeptRestrictedContentReason), filingIsOwn);
            }

            if (target.IsRefused)
            {
                throw new RecordOwnerUnsettledException(
                    request.EntityLogicalName, request.RecordId,
                    $"The {request.EntityLogicalName} {request.RecordId:D} is filed under parents that now resolve to no owner "
                    + $"({target.RefusalCode}); no owner was written (#2143).");
            }

            if (!target.IsOwned)
            {
                return (target, filingIsOwn); // Unchanged — the row keeps its owner.
            }

            lastTarget = target;
            if (observed is not null)
                observed.LastTarget = target;
            var teamId = target.OwningTeamId!.Value;
            if (state.OwningTeam == teamId)
            {
                return (target, filingIsOwn); // already owned by the team its current parents resolve to
            }

            // R4b-2b (rule 1): the restricted-content team replaces a default-team answer only, never a kept creator. An
            // author-owned row of a hard-deny table whose answer is ordinary keeps its author, as the ordinary pass keeps it
            // (SecureChildReconciler never writes a user-owned row there); a Secure answer still takes it into isolation.
            if (state.OwningUser is not null && target.InPlaceOfDefaultTeamId is not null)
            {
                return (RecordOwnerResolution.Unchanged(RecordOwnershipContext.AuthorKeptRestrictedContentReason), filingIsOwn);
            }

            if (!target.IsSecureOwner
                && await WouldLeaveSecureBusinessUnitAsync(state, parentsAsResolved, before, ct).ConfigureAwait(false))
            {
                _logger.LogWarning(
                    "Re-file of {Entity} {RecordId}: the owner its parents give now is ordinary, but the row is held in the "
                    + "Secure Record business unit on a filing this re-file's F3 check did not cover, so no owner is written "
                    + "(attempt {Attempt}, #2143).",
                    request.EntityLogicalName, request.RecordId, attempt);
                throw new RecordOwnerUnsettledException(
                    request.EntityLogicalName, request.RecordId,
                    $"The owner of {request.EntityLogicalName} {request.RecordId:D} was not settled: the owner its parents give "
                    + "now would take it out of the Secure Record business unit on a filing this re-file did not make, so it "
                    + "keeps its secure owner until that filing is settled (#2143).");
            }

            if (state.Version is not { } version)
            {
                throw new InvalidOperationException(
                    $"The {request.EntityLogicalName} {request.RecordId:D} carries no row version, so its owner cannot be "
                    + "written conditionally; no owner was written (#2143).");
            }

            try
            {
                await _dataverse.UpdateIfRowVersionMatchesAsync(
                    request.EntityLogicalName,
                    request.RecordId,
                    version,
                    new Dictionary<string, object> { [OwnerColumn] = new EntityReference(TeamEntity, teamId) },
                    ct).ConfigureAwait(false);
            }
            catch (System.Data.DBConcurrencyException)
            {
                _logger.LogInformation(
                    "Re-file of {Entity} {RecordId}: the row changed between its read and the owner write (attempt {Attempt} "
                    + "of {MaxAttempts}); reading it again (#2143).",
                    request.EntityLogicalName, request.RecordId, attempt, MaxOwnerWriteAttempts);
                continue;
            }

            // Written conditionally at the version read, so the filing it answers is the one read at that version.
            return (await ConfirmOwnerAsync(request, target, ct).ConfigureAwait(false), filingIsOwn);
        }

        if (lastTarget is { IsSecureOwner: true } secure)
        {
            // F1-b: the row's parents last resolved to the Secure team and it kept changing. One UNCONDITIONAL write of the
            // Secure team: over-restricted is the safe failure; ordinary-owned under a secure record is not.
            _logger.LogWarning(
                "Re-file of {Entity} {RecordId}: its owner could not be settled conditionally in {MaxAttempts} attempts and its "
                + "parents last resolved to the Secure team, so the Secure team is written unconditionally (#2143).",
                request.EntityLogicalName, request.RecordId, MaxOwnerWriteAttempts);
            await _dataverse.UpdateAsync(
                request.EntityLogicalName,
                request.RecordId,
                new Dictionary<string, object> { [OwnerColumn] = new EntityReference(TeamEntity, secure.OwningTeamId!.Value) },
                ct).ConfigureAwait(false);
            return await ConfirmUnconditionalSecureWriteAsync(request, resolvedFrom, secure, before, lastState!, ct)
                .ConfigureAwait(false);
        }

        _logger.LogError(
            "Re-file of {Entity} {RecordId}: its owner could not be settled in {MaxAttempts} attempts — the row kept changing "
            + "under concurrent writers. No owner that disagrees with its current parents was written (#2143).",
            request.EntityLogicalName, request.RecordId, MaxOwnerWriteAttempts);
        throw new RecordOwnerUnsettledException(
            request.EntityLogicalName, request.RecordId,
            $"The owner of {request.EntityLogicalName} {request.RecordId:D} could not be settled in {MaxOwnerWriteAttempts} "
            + "attempts: the row kept changing under concurrent writers. No owner that disagrees with its current parents was "
            + "written (#2143).");
    }

    /// <summary>
    /// Reads a written owner back (task 146): <paramref name="target"/> when it is the team, else throws
    /// <see cref="RecordOwnerUnsettledException"/> (#2143 round 2: the write succeeded, so a different owner read back is
    /// another writer's — a conflict the HTTP writers answer 409, not a server fault).
    /// </summary>
    private async Task<RecordOwnerResolution> ConfirmOwnerAsync(
        RecordReparent request, RecordOwnerResolution target, CancellationToken ct)
    {
        var teamId = target.OwningTeamId!.Value;
        if (await ReadOwningTeamAsync(request, ct).ConfigureAwait(false) != teamId)
        {
            throw new RecordOwnerUnsettledException(
                request.EntityLogicalName, request.RecordId,
                $"The owner of {request.EntityLogicalName} {request.RecordId:D} did not read back as the team it was assigned "
                + "after the reassignment; the re-filed row may be readable outside its new parent's team (task 146).");
        }

        _logger.LogInformation(
            "Re-filed {Entity} {RecordId}: owner reassigned to team {TeamId}.",
            request.EntityLogicalName, request.RecordId, teamId);
        return target;
    }

    /// <summary>
    /// #2143 round 2 (F2): what F1-b's UNCONDITIONAL Secure write is checked against. That write is conditioned on nothing,
    /// so another writer may have moved the row out from under its secure record between this call's last read
    /// (<paramref name="lastRead"/>) and the write — a mover whose own settle already wrote the ordinary owner its new parents
    /// give, and whose call answered success. The row is read again (committed): while its parents still resolve to the Secure
    /// team, the owner is read back as before (<see cref="ConfirmOwnerAsync"/>) and the re-file stands; otherwise the Secure
    /// owner just written belongs to no parent the row holds, the steps after a re-file must not run on it (a descendant pass
    /// would pull everything filed under it into isolation), and the call throws <see cref="RecordOwnerUnsettledException"/>
    /// after ONE settle decision from the fresh parents (<see cref="ReleaseOwnSecureWriteAsync"/>).
    /// </summary>
    /// <remarks>
    /// #2143 round 3 (K4): one case is not a move by another writer. When the row is still filed exactly as this call resolved
    /// it (<paramref name="resolvedFrom"/>) — its secure parent was un-secured meanwhile, so the same filing now gives an
    /// ordinary owner — and the release wrote that owner (<see cref="SecureWriteRelease.Released"/>), filing and owner agree
    /// and the filing is this call's own: the re-file is settled, and the owner it now holds is returned (the steps after a
    /// re-file run as for any settled re-file). Every other case throws as above.
    /// Round 5 (F2): "this call's own" includes its lineage-only lookups (a message's thread) — the row holds the ones it held
    /// before the change with this call's own changes applied (<paramref name="before"/>), so another writer's join is never
    /// answered as this call's settled filing.
    /// </remarks>
    private async Task<(RecordOwnerResolution Answer, bool FilingIsOwn)> ConfirmUnconditionalSecureWriteAsync(
        RecordReparent request, IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RecordOwnerResolution secure,
        RowBeforeChange before, RowOwnership lastRead, CancellationToken ct)
    {
        var fresh = await ReadRowOwnershipAsync(request, ct).ConfigureAwait(false)
            ?? throw new RecordOwnerUnsettledException(
                request.EntityLogicalName, request.RecordId,
                $"The {request.EntityLogicalName} {request.RecordId:D} being re-filed no longer exists, so its owner could "
                + "not be settled (#2143).");

        var freshTarget = await ResolveReparentOwnerAsync(request, fresh.Parents.ToArray(), fresh.SecureOnlyParents, ct)
            .ConfigureAwait(false);
        var filingIsOwn = FiledAsResolved(fresh, resolvedFrom) && SameLineage(before.LineageLookups, fresh.LineageLookups);
        if (freshTarget.IsSecureOwner)
        {
            return (await ConfirmOwnerAsync(request, secure, ct).ConfigureAwait(false), filingIsOwn);
        }

        var outcome = await ReleaseOwnSecureWriteAsync(request, secure, freshTarget, fresh, before, lastRead, ct)
            .ConfigureAwait(false);
        if (outcome == SecureWriteRelease.Released && filingIsOwn)
        {
            _logger.LogInformation(
                "Re-filed {Entity} {RecordId}: its parents stopped resolving to the Secure team while its owner was settled (a "
                + "secure parent was un-secured); the filing is this re-file's own and its owner is the one those parents now "
                + "give, team {TeamId} (#2143).",
                request.EntityLogicalName, request.RecordId, freshTarget.OwningTeamId);
            return (freshTarget, true);
        }

        if (outcome == SecureWriteRelease.Held)
        {
            _logger.LogCritical(
                ReparentLeftInconsistent,
                "Re-file of {Entity} {RecordId} needs checking: its owner was written as the Secure team {SecureTeamId} "
                + "unconditionally, but another writer had moved it out from under its secure record first; its parents now "
                + "give {Outcome} {TeamId}, and that owner could not be restored by this call. It is held in the Secure Record "
                + "business unit (over-restricted, not exposed). No sweep releases it: a re-file that its F3 check admits (the "
                + "row's creator), or manual repair, settles it (#2143).",
                request.EntityLogicalName, request.RecordId, secure.OwningTeamId, freshTarget.Outcome, freshTarget.OwningTeamId);
        }
        else
        {
            _logger.LogWarning(
                "Re-file of {Entity} {RecordId}: its owner was written as the Secure team unconditionally, but another writer "
                + "had moved it out from under its secure record first; its owner is the one its parents now give ({Outcome} "
                + "{TeamId}, {Release}). This re-file did not stand (#2143).",
                request.EntityLogicalName, request.RecordId, freshTarget.Outcome, freshTarget.OwningTeamId, outcome);
        }

        throw new RecordOwnerUnsettledException(
            request.EntityLogicalName, request.RecordId,
            filingIsOwn
                ? $"The {request.EntityLogicalName} {request.RecordId:D} is still filed as this re-file left it, but its parents "
                    + "stopped resolving to the Secure team while its owner was settled and that owner could not be settled "
                    + "by this call; reload it and retry (#2143)."
                : $"The {request.EntityLogicalName} {request.RecordId:D} was moved out from under its secure record by another "
                    + "writer while this re-file settled its owner, so this re-file did not stand; reload it and retry (#2143).");
    }

    /// <summary>What <see cref="ReleaseOwnSecureWriteAsync"/> did (#2143 round 2).</summary>
    private enum SecureWriteRelease
    {
        /// <summary>The Secure owner this call wrote was replaced, conditionally, with the owner the fresh parents give.</summary>
        Released,

        /// <summary>The row already holds the owner its fresh parents give (the mover's write landed after this call's).</summary>
        AlreadySettled,

        /// <summary>Nothing was written: the row stays held in the Secure Record business unit (logged for repair).</summary>
        Held,
    }

    /// <summary>
    /// #2143 round 2 (F2): ONE settle decision for a row this call just wrote the Secure team to, unconditionally, although its
    /// fresh parents (<paramref name="fresh"/>) no longer resolve to it. The owner they give is written back — CONDITIONALLY, at
    /// the fresh read's version — only when the Secure owner the row holds is the one this call wrote AND the row was outside
    /// the Secure Record business unit at this call's last read before that write (<paramref name="lastRead"/>): its isolation
    /// is then this call's own write, not a hold another writer's guard or the reconcile placed (which only an F3-checked move
    /// may lift, so it is left). Every other case writes nothing: a refused or unchanged answer, another writer's owner, a
    /// lineage-only lookup (a message's thread) that changed between <paramref name="lastRead"/> and <paramref name="fresh"/>
    /// (round 3, F1: another writer's filing; since round 6 the fresh parents include the thread's filing too), an ambiguous or absent Secure Record business
    /// unit, no version, a row changed again (412), or an owner that does not read back. Never writes an owner that disagrees
    /// with the fresh parents. Round 5 (F2): the lineage-only lookups are also compared with the ones the row held before the
    /// change with this call's own changes applied (<paramref name="before"/>): a join that landed before
    /// <paramref name="lastRead"/>, whose settle wrote the Secure team after it, leaves the thread the same at both reads, and
    /// that Secure owner is the joiner's, not this call's.
    /// </summary>
    private async Task<SecureWriteRelease> ReleaseOwnSecureWriteAsync(
        RecordReparent request, RecordOwnerResolution secure, RecordOwnerResolution freshTarget, RowOwnership fresh,
        RowBeforeChange before, RowOwnership lastRead, CancellationToken ct)
    {
        if (!freshTarget.IsOwned || freshTarget.IsSecureOwner)
            return SecureWriteRelease.Held;

        var ordinaryTeam = freshTarget.OwningTeamId!.Value;
        if (fresh.OwningTeam == ordinaryTeam)
            return SecureWriteRelease.AlreadySettled;
        if (fresh.OwningTeam != secure.OwningTeamId || fresh.Version is not { } version)
            return SecureWriteRelease.Held;
        if (!SameLineage(lastRead.LineageLookups, fresh.LineageLookups)
            || !SameLineage(before.LineageLookups, fresh.LineageLookups))
        {
            return SecureWriteRelease.Held;
        }

        var secureBu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct, _logger).ConfigureAwait(false);
        if (secureBu.Ambiguous || secureBu.Id is not { } secureBuId
            || lastRead.OwningBusinessUnit is not { } lastBu || lastBu == secureBuId)
        {
            return SecureWriteRelease.Held;
        }

        try
        {
            await _dataverse.UpdateIfRowVersionMatchesAsync(
                request.EntityLogicalName,
                request.RecordId,
                version,
                new Dictionary<string, object> { [OwnerColumn] = new EntityReference(TeamEntity, ordinaryTeam) },
                ct).ConfigureAwait(false);
        }
        catch (System.Data.DBConcurrencyException)
        {
            return SecureWriteRelease.Held;
        }

        return await ReadOwningTeamAsync(request, ct).ConfigureAwait(false) == ordinaryTeam
            ? SecureWriteRelease.Released
            : SecureWriteRelease.Held;
    }

    /// <summary>
    /// Whether writing an ORDINARY owner to <paramref name="state"/>'s row would take it out of the Secure Record business
    /// unit on a filing this call's F3 check did not cover (#2143). <c>false</c> when the row is not held there, or when it
    /// is still filed exactly as this call resolved it AND either it was held there before the change or this call's F3 check
    /// was asked and permitted it to leave a secure record (round 2, F1: a row another writer's Secure write moved into the
    /// unit AFTER this call read it is still the move out its own F3 check permitted). Round 3 (F1): the F3 exemption is not
    /// taken when a lineage-only lookup (a message's thread) changed since the read before the change, other than by this
    /// call's own change: the F3 check covered the secure roots of the parents this call left, not a secure record another
    /// writer's join made the row inherit. Round 4: the same holds for the before-unit arm — a row held there before the
    /// change whose secure parent was un-secured is not exempted when another writer joined it to a secure thread meanwhile,
    /// so the lineage check guards the whole exemption. An ambiguous Secure Record business unit, or a row whose owning
    /// business unit did not come back, counts as held there (fail closed); no Secure Record business unit at all answers
    /// <c>false</c>.
    /// </summary>
    private async Task<bool> WouldLeaveSecureBusinessUnitAsync(
        RowOwnership state, bool parentsAsResolved, RowBeforeChange before, CancellationToken ct)
    {
        var secureBu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct, _logger).ConfigureAwait(false);
        if (secureBu.Ambiguous)
            return true;
        if (secureBu.Id is not { } secureBuId)
            return false;
        if (state.OwningBusinessUnit is { } held && held != secureBuId)
            return false;
        return !(parentsAsResolved
            && SameLineage(state.LineageLookups, before.LineageLookups)
            && (before.OwningBusinessUnit == secureBuId || before.PermittedToLeaveSecure));
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
    /// root and the caller may not, or <c>null</c> when it may proceed — with <c>PermittedToLeave</c> <c>true</c> exactly
    /// when the F3 decision was asked and permitted the move out (#2143 round 2: the settle's Secure-unit guard keys on it),
    /// and <c>Permit</c> the F3 permission when that permitted change leaves at least one secure root
    /// (<see cref="RecordOwnerResolution.SecureExitPermitted"/>, #2159 slice 2). Nothing is written here.
    /// </summary>
    /// <remarks>
    /// <para><b>Which secure roots the row leaves.</b> The secure roots above the row's parents BEFORE the change, less
    /// those above its parents AFTER it (<see cref="SecureRootsAboveAsync"/>). Leaving one root for another secure root
    /// still leaves the first ("or to a different root"); adding a parent, or moving between ordinary records, leaves
    /// none and asks nothing.</para>
    /// <para><b>Out of the row's own columns</b> (#2143 round 7, F1). Also every secure root above the parents the row's OWN
    /// columns hold before the change, less those above what they hold after it (<paramref name="ownBefore"/>,
    /// <paramref name="ownAfter"/>) — even while the row's record thread still files it under that root: the thread follows
    /// the filing after the write, so the move out is this re-file's.</para>
    /// <para><b>Off the message's thread</b> (#2143 round 7b). A change to a message's own filing that names no other thread is
    /// followed by the thread reconcile, which moves it off a thread anchored to a record it is no longer filed to: every
    /// secure root above <paramref name="threadTheReconcileMayLeave"/>, less those above <paramref name="ownAfter"/>, is left
    /// too. Asked whatever the reconcile then decides (a private thread keeps the message): the safe side.</para>
    /// <para><b>Who may.</b> <see cref="Sprk.Bff.Api.Services.Access.SecureDesignationRemoval"/> — the unsecure endpoint's
    /// F3 check — with Full Access asked on EVERY secure root left and the creator counted on the ROW
    /// (<c>sprk_createdbyperson</c> on a table the BFF stamps, else <c>createdby</c>; #2273). A writer that acts for no person
    /// (<see cref="RecordReparent.SecureExitCaller"/> null) is refused: there is nobody whose rights could be checked.</para>
    /// <para><b>Fail closed.</b> A row held in the Secure Record business unit that would leave its isolation although no
    /// secure root above it can be named (its secure parent is not one of its own columns — a message secured by the
    /// record thread it joined) admits only its creator. A filing deeper than <see cref="MaxLineageDepth"/> refuses, as
    /// the owner decision does.</para>
    /// </remarks>
    private async Task<(RecordOwnerResolution? Refusal, bool PermittedToLeave, SecureExitPermit? Permit)>
        RefuseUnlessPermittedToLeaveSecureRootsAsync(
        RecordReparent request, Entity current, IReadOnlyCollection<RecordOwnershipParent> filedUnderBefore,
        IReadOnlyCollection<RecordOwnershipParent> filedUnderAfter, IReadOnlyCollection<RecordOwnershipParent> ownBefore,
        IReadOnlyCollection<RecordOwnershipParent> ownAfter, IReadOnlyCollection<RecordOwnershipParent> threadTheReconcileMayLeave,
        RecordOwnerResolution resolution, CancellationToken ct)
    {
        // Two cheap facts first, so the many re-files that leave nothing (a thread JOIN, an invoice link, additive inbound
        // filing) cost no read: a change that removes none of the row's parents leaves no root (both walks would read the
        // same parents), and an answer that keeps the row secure or keeps its owner cannot take it out of isolation.
        var removesAParent = filedUnderBefore.Any(parent => !filedUnderAfter.Contains(parent));
        // #2143 round 7 (F1): a parent taken out of the row's OWN columns, though another source (its thread) still files the
        // row under it — the move a re-file makes; the thread follows it after the write.
        var removesAnOwnParent = ownBefore.Any(parent => !ownAfter.Contains(parent));
        var mayLeaveIsolation = !resolution.IsSecureOwner && resolution.Outcome != RecordOwnerOutcome.Unchanged;
        if (!removesAParent && !removesAnOwnParent && threadTheReconcileMayLeave.Count == 0 && !mayLeaveIsolation)
        {
            return (null, false, null);
        }

        var secureBu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct, _logger).ConfigureAwait(false);
        if (secureBu.Ambiguous)
        {
            return (RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
                "more than one business unit carries the Secure Record name, so whether the change leaves a secure record "
                + "cannot be decided"), false, null);
        }

        (IReadOnlyList<RecordOwnershipParent> Roots, RecordOwnershipParent? TooDeepAt) none =
            (Array.Empty<RecordOwnershipParent>(), null);
        var before = removesAParent ? await SecureRootsAboveAsync(filedUnderBefore, secureBu.Id, ct).ConfigureAwait(false) : none;
        var after = removesAParent ? await SecureRootsAboveAsync(filedUnderAfter, secureBu.Id, ct).ConfigureAwait(false) : none;
        if ((before.TooDeepAt ?? after.TooDeepAt) is { } tooDeep)
        {
            return (RecordOwnerResolution.Refused(
                RecordOwnerRefusal.ParentUnresolved,
                $"the record's filing runs through more than {MaxLineageDepth} levels of records (still unresolved at "
                + $"{tooDeep.EntityLogicalName} {tooDeep.RecordId:D}), so whether the change leaves a secure record cannot "
                + "be decided"), false, null);
        }

        var left = before.Roots.Where(root => !after.Roots.Contains(root)).ToList();

        // #2143 round 7 (F1): every secure root the row's OWN columns leave, even while its thread keeps the row under it — the
        // thread follows the filing after the write (ThreadResolver.ReconcileThreadForFilingAsync), and that move asks the same
        // caller. A caller without Full Access on the record is refused here, before anything is written.
        if (removesAnOwnParent)
        {
            var ownRootsBefore = await SecureRootsAboveAsync(ownBefore, secureBu.Id, ct).ConfigureAwait(false);
            var ownRootsAfter = await SecureRootsAboveAsync(ownAfter, secureBu.Id, ct).ConfigureAwait(false);
            if ((ownRootsBefore.TooDeepAt ?? ownRootsAfter.TooDeepAt) is { } ownTooDeep)
            {
                return (RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the record's filing runs through more than {MaxLineageDepth} levels of records (still unresolved at "
                    + $"{ownTooDeep.EntityLogicalName} {ownTooDeep.RecordId:D}), so whether the change leaves a secure record "
                    + "cannot be decided"), false, null);
            }

            left.AddRange(ownRootsBefore.Roots.Where(root => !ownRootsAfter.Roots.Contains(root) && !left.Contains(root)));
        }

        // #2143 round 7b: every secure root above the thread the reconcile will move the message off, less those its own new
        // parents keep it under — asked here, before anything is written, of the person whose re-file the move follows (the
        // thread move acts for the same person, round 7a). A caller without Full Access on it is refused.
        if (threadTheReconcileMayLeave.Count > 0)
        {
            var threadRoots = await SecureRootsAboveAsync(threadTheReconcileMayLeave, secureBu.Id, ct).ConfigureAwait(false);
            var keptByOwn = await SecureRootsAboveAsync(ownAfter, secureBu.Id, ct).ConfigureAwait(false);
            if ((threadRoots.TooDeepAt ?? keptByOwn.TooDeepAt) is { } threadTooDeep)
            {
                return (RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the record's filing runs through more than {MaxLineageDepth} levels of records (still unresolved at "
                    + $"{threadTooDeep.EntityLogicalName} {threadTooDeep.RecordId:D}), so whether the change leaves a secure "
                    + "record cannot be decided"), false, null);
            }

            left.AddRange(threadRoots.Roots.Where(root => !keptByOwn.Roots.Contains(root) && !left.Contains(root)));
        }

        // Held in the Secure Record business unit, about to leave its isolation, yet no secure root above it can be named.
        var unidentified = left.Count == 0
            && mayLeaveIsolation
            && secureBu.Id is { } secureBuId
            && current.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id == secureBuId;

        if (left.Count == 0 && !unidentified)
        {
            return (null, false, null); // no secure root is left, and the row is not taken out of secure isolation
        }

        // #2273: sprk_createdbyperson is evidence of who created the row ONLY on a table the BFF stamps — there it is
        // field-secured, written by the BFF alone (the schema scripts' -Verify gates the deploy). A table that merely carries
        // the column (created unsecured, or not yet stamped) lets any Write holder name themselves, so its value is "not
        // recorded": no value, no separate read, and only createdby or Full Access can admit (fail closed).
        var creatorPersonIsTrusted = RecordCreatorPerson.IsStamped(request.EntityLogicalName);
        var decision = await Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.DecideAsync(
            new Sprk.Bff.Api.Services.Access.SecureRemovalQuestion
            {
                Caller = request.SecureExitCaller,
                SecuredRecords = left
                    .Select(root => new Sprk.Bff.Api.Services.Access.SecuredRecordRef(root.EntityLogicalName, root.RecordId))
                    .ToArray(),
                IncludesUnidentifiedSecureRecord = unidentified,
                CreatedBy = current.GetAttributeValue<EntityReference>(CreatedByColumn)?.Id,
                CreatedByPerson = creatorPersonIsTrusted
                    ? current.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id
                    : null,
                // c1-r1 (owner round 13 item 9): a child table now carries the person too. The every-column read cannot
                // tell "nobody recorded" from "this environment lacks the column", so when the row came back without it,
                // the column is read on its own — an absent column is "could not tell" (unverifiable), never "allowed"
                // (main-session condition 2, now for children as for roots). Asked only when nothing else admitted.
                ReadCreatedByPersonAsync =
                    creatorPersonIsTrusted
                    && !current.Attributes.ContainsKey(RecordCreatorPerson.Column)
                        ? token => ReadCreatorPersonAsync(request, token)
                        : null,
            },
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "F3 on re-filing {Entity} {RecordId} out of {Count} secure record(s): {Outcome} ({Basis}), caller {CallerId}.",
            request.EntityLogicalName, request.RecordId, left.Count, decision.Outcome, decision.Basis,
            decision.CallerSystemUserId);

        if (!decision.IsPermitted)
        {
            return (RecordOwnerResolution.SecureRemovalRefused(decision, ChildNoun(request.EntityLogicalName)), false, null);
        }

        // #2143 round 2 (F1): a PERMITTED decision is also what the settle's Secure-unit guard keys on — the move out of the
        // Secure Record business unit this check covered, even when another writer's Secure write lands after this read.
        if (left.Count == 0)
        {
            return (null, true, null);
        }

        // PR #2161 round 4 (F1-b): the permit names the filing this decision approved — every secure root above the parents
        // the row holds AFTER the change (the walk above, when it ran; read here otherwise). A step after the re-file acts on
        // the permit only toward one of them (DocumentContainerRelocator.RecordReleaseCoreAsync). A filing too deep to walk
        // names none, so no such step acts on it (held: fail closed); the re-file itself was already decided above.
        var rootsAfter = after.Roots;
        if (!removesAParent)
        {
            var walked = await SecureRootsAboveAsync(filedUnderAfter, secureBu.Id, ct).ConfigureAwait(false);
            rootsAfter = walked.TooDeepAt is null ? walked.Roots : Array.Empty<RecordOwnershipParent>();
        }

        return (null, true, new SecureExitPermit(left, decision, rootsAfter));
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
                    var fact = await ReadParentAsync(node, plannedOwningTeams: null, committedReads: true, ct)
                        .ConfigureAwait(false);
                    if (fact is not null && (fact.FlaggedSecure || (secureBuId is { } sbu && fact.BusinessUnitId == sbu)))
                        roots.Add(node);
                }
                else if (IsReparentableChild(node.EntityLogicalName))
                {
                    var filing = await ReadOwnershipParentsOfAsync(node, committedReads: true, ct).ConfigureAwait(false);
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
    /// failure, which the writer reports in its own contract. #2143: "the resolved team" is the one the settle loop last
    /// decided to write (<see cref="AfterChangeObservation.LastTarget"/>), or <paramref name="resolution"/> when it decided
    /// none.
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
    /// <returns>
    /// The resolution the row's owner turned out to match (the assignment landed), or <c>null</c>. With it (PR #2161 round 5,
    /// K1), whether the filing was CONFIRMED as this call's own — read back filed exactly as this call resolved it, with the
    /// lineage-only lookups it left (<see cref="StillFiledAsResolvedAsync"/> answered <c>true</c>): the same notion as
    /// <see cref="SettleOwnerAsync"/>'s. A filing that could not be read back keeps the answer (#2150) but is not confirmed.
    /// </returns>
    private async Task<(RecordOwnerResolution Answer, bool FilingIsOwn)?> RecoverFromFailedAssignmentAsync(
        RecordReparent request, IReadOnlyDictionary<string, object> restore,
        IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RecordOwnerResolution resolution, RowBeforeChange before,
        AfterChangeObservation observed, Exception assignFailure)
    {
        var target = observed.LastTarget ?? resolution;
        var teamId = target.OwningTeamId!.Value;

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

        var filedAsResolved = landed == true
            ? await StillFiledAsResolvedAsync(request, resolvedFrom, before).ConfigureAwait(false)
            : null;
        if (filedAsResolved == false)
        {
            // #2143: the owner is the resolved team, but the row is no longer filed under the parents it was resolved from —
            // another writer re-filed it. Filing and owner do NOT agree; the filing is left to that writer below.
            // Round 5 (F2): nor when another writer moved a lineage-only lookup (a thread join) — that writer's Secure owner
            // reads the same as this call's, and is never answered as this call's settled re-file.
            landed = false;
        }

        if (landed == true)
        {
            _logger.LogWarning(assignFailure,
                "Re-file of {Entity} {RecordId}: the owner assignment to team {TeamId} reported a failure, but the owner reads "
                + "back as that team — the assignment landed (lost response); the re-file stands (task 146).",
                request.EntityLogicalName, request.RecordId, teamId);
            return (target, filedAsResolved == true);
        }

        if (landed is null && !target.IsSecureOwner)
        {
            _logger.LogCritical(
                ReparentLeftInconsistent, assignFailure,
                "Re-file of {Entity} {RecordId} needs checking: the owner assignment to the ORDINARY team {TeamId} failed and "
                + "the owner could not be read back. The new filing is kept (it names no secure record), so the row is owned "
                + "either by that team or still by its previous owner — re-run the re-file to settle it (task 146).",
                request.EntityLogicalName, request.RecordId, teamId);
            return null;
        }

        await RestoreFilingAsync(request, restore, resolvedFrom, target, before, observed, assignFailure, ownerUnknown: landed is null)
            .ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// #2143: whether the row is still filed under <paramref name="resolvedFrom"/> — and (round 5, F2) still holds the
    /// lineage-only lookups of <paramref name="before"/>, so no other writer joined it elsewhere — <c>null</c> when it cannot
    /// be read (the caller keeps its earlier answer). App-only, no cancellation (recovery).
    /// </summary>
    private async Task<bool?> StillFiledAsResolvedAsync(
        RecordReparent request, IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RowBeforeChange before)
    {
        try
        {
            var state = await ReadRowOwnershipAsync(request, CancellationToken.None).ConfigureAwait(false);
            return state is not null && FiledAsResolved(state, resolvedFrom)
                && SameLineage(before.LineageLookups, state.LineageLookups);
        }
        catch (Exception readFailure)
        {
            _logger.LogWarning(readFailure,
                "Re-file of {Entity} {RecordId}: after a failed owner assignment its filing could not be read back.",
                request.EntityLogicalName, request.RecordId);
            return null;
        }
    }

    /// <summary>
    /// #2143: after a put-back was NOT applied — a newer writer re-filed or re-owned the row
    /// (<see cref="PutBackOutcome.LeftToNewerWriter"/>), or the put-back could not be sent safely
    /// (<see cref="PutBackOutcome.Unsettled"/>: the row kept changing, could not be read, or was never read toward an ordinary
    /// owner) — checks that the row's owner matches the parents it holds now, the same settle loop as the re-file
    /// (<see cref="SettleOwnerAsync"/>), and logs the outcome. Never throws (recovery): a check that cannot settle is logged
    /// <see cref="ReparentLeftInconsistent"/>.
    /// </summary>
    private async Task RecheckOwnerAfterNewerFilingAsync(
        RecordReparent request, IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RowBeforeChange before,
        Exception assignFailure, PutBackOutcome putBack)
    {
        // K3 (round 2): say WHY the filing was not put back. Only LeftToNewerWriter saw another writer's filing or owner; an
        // Unsettled put-back may be leaving this call's own filing in place.
        var whyNotPutBack = putBack == PutBackOutcome.LeftToNewerWriter
            ? "another writer re-filed or re-owned the row"
            : "the put-back could not be sent safely (the row kept changing or could not be read), so the filing may be this "
              + "re-file's own";
        try
        {
            var (settled, _) = await SettleOwnerAsync(request, resolvedFrom, before, observed: null, CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogWarning(
                assignFailure,
                "Re-file of {Entity} {RecordId}: its owner assignment failed and its filing was NOT put back ({WhyNotPutBack}); "
                + "its owner matches the parents it holds now ({Outcome} {TeamId}) (#2143).",
                request.EntityLogicalName, request.RecordId, whyNotPutBack, settled.Outcome, settled.OwningTeamId);
        }
        catch (Exception recheckFailure)
        {
            _logger.LogCritical(
                ReparentLeftInconsistent, recheckFailure,
                "Re-file of {Entity} {RecordId} needs checking: its owner assignment failed ({AssignFailure}), its filing was "
                + "NOT put back ({WhyNotPutBack}), and its owner could not be confirmed against the parents it holds now "
                + "(#2143).",
                request.EntityLogicalName, request.RecordId, assignFailure.GetType().Name, whyNotPutBack);
        }
    }

    /// <summary>
    /// Writes back what the row was filed under before the change — the columns the change moved
    /// (<paramref name="restore"/>: the parent lookups it set or cleared, and its <see cref="RecordReparent.AttachColumns"/>)
    /// — so the row's filing matches the owner it still has. Every outcome is a CRITICAL log naming the row; a restore
    /// that itself fails is logged under <see cref="ReparentLeftInconsistent"/> for manual repair. Never throws.
    /// </summary>
    /// <remarks>
    /// #2143: the put-back is CONDITIONAL — at the version the first read after the change observed, and only while the row
    /// is still filed under the parents the owner was resolved from and owned by its pre-change owner (K2). A row another
    /// writer re-filed or re-owned (its parents or owner differ, or its version moved and a fresh read shows different
    /// parents or owner) is left to that writer, and its owner is re-checked
    /// against the parents it holds (<see cref="RecheckOwnerAfterNewerFilingAsync"/>). A version that moved while the
    /// filing is still ours (an unrelated column written) is read again, up to <see cref="MaxOwnerWriteAttempts"/>
    /// attempts. Only when the row's version was never read at all — every read after the change failed, so there is no
    /// sign of a newer writer — AND the owner this re-file resolved is the Secure team is the put-back sent unconditionally,
    /// as before #2143 (exposure is the worse failure). Toward an ORDINARY owner it is never sent blind (round 2, K1): the
    /// filing it would restore can be a secure record's, written over a newer writer's ordinary owner; the row is left filed
    /// under this re-file's ordinary parents and logged, and its owner re-checked.
    /// </remarks>
    private async Task RestoreFilingAsync(
        RecordReparent request, IReadOnlyDictionary<string, object> restore,
        IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RecordOwnerResolution resolution, RowBeforeChange before,
        AfterChangeObservation observed, Exception assignFailure, bool ownerUnknown)
    {
        var teamId = resolution.OwningTeamId!.Value;
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
            var putBack = await PutBackFilingAsync(request, restore, resolvedFrom, resolution, before, observed)
                .ConfigureAwait(false);
            if (putBack != PutBackOutcome.Applied)
            {
                if (putBack == PutBackOutcome.Unsettled)
                {
                    _logger.LogCritical(
                        ReparentLeftInconsistent, assignFailure,
                        "Re-file of {Entity} {RecordId} needs checking: the owner could not be assigned to team {TeamId}, and "
                        + "its filing columns ({Columns}) were NOT put back — the row kept changing, or could not be read, and a "
                        + "put-back is never sent blind over a newer write (nor ever blind toward an ordinary owner) (#2143).",
                        request.EntityLogicalName, request.RecordId, teamId, string.Join(", ", restore.Keys));
                }

                await RecheckOwnerAfterNewerFilingAsync(request, resolvedFrom, before, assignFailure, putBack)
                    .ConfigureAwait(false);
                return;
            }

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
                + "to team {TeamId} ({AssignFailure}, code {AssignErrorCode}) and restoring its filing columns ({Columns}) also "
                + "failed. It is filed under its new parents while owned for its old ones (task 146).",
                request.EntityLogicalName, request.RecordId, teamId, assignFailure.GetType().Name, ErrorCodeOf(assignFailure),
                string.Join(", ", restore.Keys));
        }
    }

    /// <summary>
    /// K4 (#2143): the Dataverse fault code anywhere in <paramref name="failure"/>'s chain (<c>0x…</c>), or <c>null</c> — what a
    /// log names instead of the failure's message, which can carry user or record detail.
    /// </summary>
    private static string? ErrorCodeOf(Exception failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is System.ServiceModel.FaultException<OrganizationServiceFault> { Detail: { } detail })
                return $"0x{detail.ErrorCode:X8}";
        }

        return null;
    }

    /// <summary>What <see cref="PutBackFilingAsync"/> did (#2143).</summary>
    private enum PutBackOutcome
    {
        /// <summary>The filing columns were written back.</summary>
        Applied,

        /// <summary>Another writer re-filed the row: nothing was written over its filing.</summary>
        LeftToNewerWriter,

        /// <summary>The row kept changing, or could not be read after it changed: nothing was written.</summary>
        Unsettled,
    }

    /// <summary>
    /// #2143: writes <paramref name="restore"/> back conditionally — see <see cref="RestoreFilingAsync"/>'s remarks. The first
    /// attempt uses the version the first read after the change observed; a later one reads the row again. App-only, no
    /// cancellation (recovery). A failure other than a version mismatch propagates to <see cref="RestoreFilingAsync"/>.
    /// K2: the filing is put back only while the row is still owned by its PRE-change owner (<paramref name="before"/>) — the
    /// owner the restored filing belongs to; a row another writer (or the reconcile) re-owned is left to it. Round 8: and only
    /// while it holds the lineage-only lookups this call left (<see cref="RowBeforeChange.LineageLookups"/>) — another
    /// writer's move to a different thread with the same parents is that writer's filing, and is never reverted. Round 9
    /// (F1): that lineage test applies only toward an ORDINARY target. Toward the SECURE team the row's filing is a secure
    /// record's while its owner is still the ordinary one before the change, so leaving it to the other writer would leave
    /// a secure filing on an ordinary owner; it is put back, as before round 8.
    /// </summary>
    private async Task<PutBackOutcome> PutBackFilingAsync(
        RecordReparent request, IReadOnlyDictionary<string, object> restore,
        IReadOnlyCollection<RecordOwnershipParent> resolvedFrom, RecordOwnerResolution target, RowBeforeChange before,
        AfterChangeObservation observed)
    {
        for (var attempt = 1; attempt <= MaxOwnerWriteAttempts; attempt++)
        {
            string? version;
            bool filedAsResolved;
            if (attempt == 1 && observed.Read)
            {
                version = observed.Version;
                filedAsResolved = observed.ParentsAsResolved && observed.OwningTeam == before.OwningTeam
                    && (target.IsSecureOwner || SameLineage(before.LineageLookups, observed.LineageLookups));
            }
            else
            {
                RowOwnership? state;
                try
                {
                    state = await ReadRowOwnershipAsync(request, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception readFailure)
                {
                    _logger.LogWarning(readFailure,
                        "Re-file of {Entity} {RecordId}: the row could not be read before its filing was put back (#2143).",
                        request.EntityLogicalName, request.RecordId);
                    if (attempt > 1)
                        return PutBackOutcome.Unsettled; // it changed since the change, and cannot be read now

                    if (!target.IsSecureOwner)
                    {
                        // #2143 round 2 (K1): never sent blind toward an ORDINARY target. The filing it restores is the one
                        // before a move to ordinary parents — possibly a secure record's — and a newer writer may since have
                        // given the row an ordinary owner: restoring the secure filing over it is the exposure. Left as it is,
                        // the row stays filed under this re-file's ordinary parents with whatever owner it holds (its
                        // previous owner, or a newer writer's), and is logged for repair by the caller.
                        return PutBackOutcome.Unsettled;
                    }

                    // Never read since the change, and not now, toward the SECURE team (a move into a secure record): no
                    // sign of a newer writer, and leaving the row filed under the secure record with an owner the failed
                    // assignment may not have changed is the exposure this put-back exists to undo. As before #2143.
                    await _dataverse.UpdateAsync(
                        request.EntityLogicalName, request.RecordId, new Dictionary<string, object>(restore),
                        CancellationToken.None).ConfigureAwait(false);
                    return PutBackOutcome.Applied;
                }

                version = state?.Version;
                filedAsResolved = state is not null
                    && FiledAsResolved(state, resolvedFrom)
                    && state.OwningTeam == before.OwningTeam
                    && (target.IsSecureOwner || SameLineage(before.LineageLookups, state.LineageLookups));
            }

            if (!filedAsResolved)
            {
                return PutBackOutcome.LeftToNewerWriter;
            }

            if (version is null)
            {
                // The row is filed as this re-file left it but carries no version to condition on: as before #2143.
                await _dataverse.UpdateAsync(
                    request.EntityLogicalName, request.RecordId, new Dictionary<string, object>(restore),
                    CancellationToken.None).ConfigureAwait(false);
                return PutBackOutcome.Applied;
            }

            try
            {
                await _dataverse.UpdateIfRowVersionMatchesAsync(
                    request.EntityLogicalName, request.RecordId, version, new Dictionary<string, object>(restore),
                    CancellationToken.None).ConfigureAwait(false);
                return PutBackOutcome.Applied;
            }
            catch (System.Data.DBConcurrencyException)
            {
                _logger.LogInformation(
                    "Re-file of {Entity} {RecordId}: the row changed before its filing was put back (attempt {Attempt} of "
                    + "{MaxAttempts}); reading it again (#2143).",
                    request.EntityLogicalName, request.RecordId, attempt, MaxOwnerWriteAttempts);
            }
        }

        return PutBackOutcome.Unsettled;
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
        RecordOwnershipParent Parent, Guid BusinessUnitId, bool HasOwningTeam, bool FlaggedSecure)
    {
        /// <summary>Fix round (B-K2): a project whose ADR-024 pair is EMPTY — filed under nothing, so it is never looked
        /// through (its own business unit decides, and no look-through depth is spent on it).</summary>
        public bool UnpairedProject { get; init; }
    }

    /// <summary>
    /// Secure-if-any over every parent. Order of the checks is the point: an unreadable parent refuses before any
    /// answer (it might be the secure one); a flagged-but-not-isolated root refuses before the secure branch (its
    /// children must not inherit the ordinary BU its ownership still shows); any parent in the Secure Record BU wins
    /// over every ordinary parent; only then does the primary parent's business unit decide an ordinary child.
    /// </summary>
    /// <remarks>UAC task 194b (I-6, "a content row of a user-owned but FILED parent follows the parent's filing, not its
    /// creator"): when the PRIMARY parent is a child-table row (<see cref="IsReparentableChild"/>) that is not team-owned and is
    /// filed, its own filing answers in its place, through the same look-through a content row of such a parent already took
    /// (<see cref="RecordOwnershipContext.KeepCreatorUnlessTargetIsTeamOwned"/>), followed for at most
    /// <see cref="MaxLineageDepth"/> levels (<paramref name="lookThroughDepth"/>), never twice through the same primary
    /// (<paramref name="lookedThrough"/>: two such rows filed under each other). Fix round R (A3): at a revisit or at the bound
    /// it STOPS looking through and decides over the parents it holds — the lineage walk and the secure check, which have
    /// their own bound and their own seen set — and refuses only when that answer would be ordinary (the primary's business
    /// unit is then unknown), never "ordinary". A core record (a root, a service request) is never content, and is never
    /// looked through except a work assignment (owner decision 2026-10-29, <see cref="IsLookThroughParent"/>): a person-owned
    /// work assignment that is filed takes its matter's business unit, then its project's (an invoice's or event's matter or
    /// project when it holds neither), on every path; so does a person-owned project filed by its BFF-managed pair (the record
    /// the pair names; an unresolvable pair refuses; an empty pair, or one naming a deleted record, files it under nothing).
    /// Any other user-owned core record keeps its own business unit. An
    /// UNFILED user-owned parent keeps its own business unit too, as before.</remarks>
    private async Task<RecordOwnerResolution> ResolveFromParentsAsync(
        IReadOnlyList<RecordOwnershipParent> parents, RecordOwnershipContext context, bool committedReads, CancellationToken ct,
        int lookThroughDepth = 0, IReadOnlySet<RecordOwnershipParent>? lookedThrough = null)
    {
        var facts = new List<ParentFacts>(parents.Count);
        foreach (var parent in parents)
        {
            var fact = await ReadParentAsync(parent, context.PlannedOwningTeams, committedReads, ct).ConfigureAwait(false);
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
        // UAC task 194b (I-6): the same look-through for ANY row whose PRIMARY parent is a child-table row that is not
        // team-owned and IS filed: its filing (in its own primary order) decides the business unit, not its creator's. The
        // parent itself stays among the parents, so secure-if-any reads the same set as before. An unfiled one keeps its own
        // business unit (below), and a root is never looked through except a work assignment (owner decision 2026-10-29).
        // Owner decision 2026-10-29: a CORE record (a root, a service request) is never content. Asked first on the content
        // path (the gate writer, a review), it stays among the parents (secure-if-any reads it) and is never answered over its
        // own filing alone; a work assignment that is not team-owned is looked through as below, an unfiled one keeps its own
        // business unit, and any other core record keeps its own business unit.
        var contentRow = context.KeepCreatorUnlessTargetIsTeamOwned
                         && !IsCoreRecord(facts[0].Parent.EntityLogicalName);
        var lookThrough = !facts[0].HasOwningTeam && !facts[0].UnpairedProject
                          && (contentRow || IsLookThroughParent(facts[0].Parent.EntityLogicalName));
        string? primaryUndecided = null;
        if (lookThrough && (lookThroughDepth >= MaxLineageDepth || lookedThrough?.Contains(facts[0].Parent) == true))
        {
            // Fix round R (A3): the look-through stops here — a primary already looked through (a loop) or the bound. Whether
            // the row is secure is still decided below over every parent it holds (secure-if-any); only an ordinary answer,
            // which would need this primary's business unit, refuses.
            primaryUndecided = lookThroughDepth >= MaxLineageDepth
                ? $"the record's primary parent runs through more than {MaxLineageDepth} levels of records that are not "
                  + $"team-owned (still unresolved at {facts[0].Parent.EntityLogicalName} {facts[0].Parent.RecordId:D}), so its "
                  + "business unit cannot be decided"
                : $"the record's primary parent {facts[0].Parent.EntityLogicalName} {facts[0].Parent.RecordId:D} is not "
                  + "team-owned and its filing loops back to it, so its business unit cannot be decided";
        }
        else if (lookThrough)
        {
            var visited = new HashSet<RecordOwnershipParent>(lookedThrough ?? new HashSet<RecordOwnershipParent>())
            {
                facts[0].Parent,
            };
            var (parentFiling, unverifiable) = await ReadLookThroughFilingAsync(facts[0].Parent, committedReads, ct)
                .ConfigureAwait(false);
            if (parentFiling is null)
            {
                return RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    unverifiable is null
                        ? $"the parent {facts[0].Parent.EntityLogicalName} {facts[0].Parent.RecordId:D} could not be read"
                        : $"what the parent {facts[0].Parent.EntityLogicalName} {facts[0].Parent.RecordId:D} is filed under "
                          + $"could not be told ({unverifiable})");
            }

            if (contentRow)
            {
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
                    inherited, context with { KeepCreatorUnlessTargetIsTeamOwned = false }, committedReads, ct,
                    lookThroughDepth + 1, visited).ConfigureAwait(false);
            }

            if (parentFiling.Count > 0)
            {
                // The primary stays among the parents (secure-if-any). Asked as a plain parent from here on: a core record
                // looked through on the content path never hands a row further down the content answer (owner decision
                // 2026-10-29).
                var filedUnder = parentFiling.Concat(facts.Select(f => f.Parent)).Distinct().ToList();
                return await ResolveFromParentsAsync(
                        filedUnder, context with { KeepCreatorUnlessTargetIsTeamOwned = false }, committedReads, ct,
                        lookThroughDepth + 1, visited)
                    .ConfigureAwait(false);
            }
        }

        // V-fix 3: the secure-if-any decision (with the Secure-only parents) is the one helper the no-parent path shares.
        if (await SecureDecisionAsync(facts, context, committedReads, ct).ConfigureAwait(false) is { } decided)
            return decided;

        if (primaryUndecided is not null)
        {
            _logger.LogWarning(
                "Refusing to resolve an owning team: no parent is secure, and the primary parent {Entity} {RecordId} could not be "
                + "looked through (a loop or the depth bound), so the ordinary business unit cannot be decided (194b A3).",
                facts[0].Parent.EntityLogicalName, facts[0].Parent.RecordId);
            return RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, primaryUndecided);
        }

        return await OrdinaryExitAsync(
            await ResolveDefaultOwnerTeamAsync(facts[0].BusinessUnitId, ct, context.LookupCache).ConfigureAwait(false),
            facts[0].BusinessUnitId, context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Secure-if-any over <paramref name="facts"/> and <paramref name="context"/>'s Secure-only parents
    /// (<see cref="RecordOwnershipContext.SecureOnlyParents"/>, V-fix 3), with the lineage above every parent that is a child
    /// and not team-owned: a REFUSAL (an unreadable parent or ancestor, an ambiguous Secure Record business unit, a parent
    /// flagged secure but not isolated) or the Secure team's answer; <c>null</c> when nothing is secure, so the ordinary rule
    /// (the primary's business unit, never a Secure-only parent's) or the unfiled answer decides.
    /// </summary>
    private async Task<RecordOwnerResolution?> SecureDecisionAsync(
        IReadOnlyList<ParentFacts> facts, RecordOwnershipContext context, bool committedReads, CancellationToken ct)
    {
        var all = new List<ParentFacts>(facts);
        foreach (var secureOnly in context.SecureOnlyParents)
        {
            if (!secureOnly.IsSpecified || !IsOwnershipParent(secureOnly.EntityLogicalName))
                continue;
            var parent = secureOnly with { EntityLogicalName = secureOnly.EntityLogicalName.Trim().ToLowerInvariant() };
            if (all.Any(f => f.Parent == parent))
                continue;

            var fact = await ReadParentAsync(parent, context.PlannedOwningTeams, committedReads, ct).ConfigureAwait(false);
            if (fact is null)
            {
                // ⛔ REFUSE, as for any named parent: the one that cannot be read might be the secure one.
                _logger.LogWarning(
                    "Refusing to resolve an owning team: the Secure-only parent {ParentEntity} {ParentId} was named but its "
                    + "business unit could not be read (V-fix 3).",
                    parent.EntityLogicalName, parent.RecordId);
                return RecordOwnerResolution.Refused(
                    RecordOwnerRefusal.ParentUnresolved,
                    $"the parent {parent.EntityLogicalName} {parent.RecordId:D} does not exist or has no owning business unit");
            }

            all.Add(fact);
        }

        // A parent that is itself a CHILD and is NOT team-owned (a run-as-user, client-created or pre-146 row) says nothing
        // about secrecy through its own business unit — a user-owned document filed to a secure matter sits in its
        // creator's ordinary unit. Its filing is read, transitively, so a secure ancestor makes THIS row secure and a
        // flagged-but-not-isolated one refuses (task 146 r2, verifier item 10 — the look-through ContentOf already did,
        // now for every context: an analysis of such a document, a Compose promote, a grandchild of any writer). The
        // ancestors only take part in the secure decision; an ordinary row still takes the PRIMARY parent's unit, so
        // nothing changes for a child of ordinary records.
        var lineage = await ReadUnownedChildLineageAsync(all, context.PlannedOwningTeams, committedReads, ct)
            .ConfigureAwait(false);
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

        var decisive = all.Concat(lineage.Facts).ToList();

        var secureBu = await SecureBusinessUnitAsync(context.LookupCache, ct).ConfigureAwait(false);
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

        return null;
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
        IReadOnlyList<ParentFacts> facts, IReadOnlyDictionary<RecordOwnershipParent, Guid>? plannedOwningTeams,
        bool committedReads, CancellationToken ct)
    {
        var lineage = new List<ParentFacts>();
        var seen = new HashSet<RecordOwnershipParent>(facts.Select(f => f.Parent));
        var frontier = facts.Where(IsUnownedChild).Select(f => f.Parent).ToList();

        for (var depth = 0; depth < MaxLineageDepth && frontier.Count > 0; depth++)
        {
            var next = new List<RecordOwnershipParent>();
            foreach (var child in frontier)
            {
                var filing = await ReadOwnershipParentsOfAsync(child, committedReads, ct).ConfigureAwait(false);
                if (filing is null)
                {
                    return (lineage, child, false);
                }

                foreach (var ancestor in filing.Select(p => p with { EntityLogicalName = p.EntityLogicalName.ToLowerInvariant() }))
                {
                    if (!seen.Add(ancestor))
                        continue;

                    var fact = await ReadParentAsync(ancestor, plannedOwningTeams, committedReads, ct).ConfigureAwait(false);
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

        // Owner decision 2026-10-29: a work assignment that is not team-owned is read through too — it can only add parents, so
        // the answer can only become more secure. Fix round (B-K2/K3): a project with an EMPTY pair is filed under nothing (a
        // project is filed only through its pair), so it is not read through.
        static bool IsUnownedChild(ParentFacts fact) =>
            !fact.HasOwningTeam && !fact.UnpairedProject && IsLookThroughParent(fact.Parent.EntityLogicalName);
    }

    /// <summary>
    /// #2468: whether <paramref name="communication"/> is filed under anything — its own ownership-parent lookups (the
    /// parents its own question names, <see cref="RecordOwnershipContext.OrderedParentsOf"/>) or its record thread's filing
    /// (<see cref="RecordThreadFiling"/>: a Direct thread files nothing). Read committed. <c>null</c> when the communication or
    /// its thread cannot be read (the caller refuses); a Dataverse fault propagates, as every resolver read does.
    /// </summary>
    private async Task<bool?> IsCommunicationFiledAsync(RecordOwnershipParent communication, CancellationToken ct)
    {
        var query = new QueryExpression(RecordThreadFiling.CommunicationTable)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = false,
        };
        query.Criteria.AddCondition($"{RecordThreadFiling.CommunicationTable}id", ConditionOperator.Equal, communication.RecordId);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (row is null)
            return null;
        if (RecordOwnershipContext.OrderedParentsOf(row).Count > 0)
            return true;
        if (RecordThreadFiling.ThreadOf(row) is not { } threadId)
            return false;

        try
        {
            var thread = await RecordThreadFiling.ReadThreadAsync(_dataverse, threadId, ct).ConfigureAwait(false);
            return RecordThreadFiling.ParentsOf(thread).Count > 0;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "The thread {ThreadId} of communication {CommunicationId} could not be read (#2468).",
                threadId, communication.RecordId);
            return null;
        }
    }

    /// <summary>
    /// The ownership parents <paramref name="row"/> is itself filed under — every ownership-parent lookup value it
    /// carries (task 146 verifier item 3). Every column is read, for the reason <see cref="ReparentAsync"/> gives: a
    /// row's parents are whatever lookups it carries, and a per-table column list is the guess this task forbids. The
    /// read happens only for a CONTENT row whose parent is not team-owned. <c>null</c> when the row cannot be read; a
    /// Dataverse fault propagates.
    /// </summary>
    /// <remarks>#2143 round 3 (K1): <paramref name="committedReads"/> is <c>true</c> on the re-file path only (a committed
    /// read); the create paths read with <c>NoLock</c>.</remarks>
    private async Task<IReadOnlyList<RecordOwnershipParent>?> ReadOwnershipParentsOfAsync(
        RecordOwnershipParent row, bool committedReads, CancellationToken ct)
    {
        var entity = await ReadFilingRowAsync(row, committedReads, ct).ConfigureAwait(false);
        // #2084 round 2: no citation exclusion here. Every row read here is an ownership parent — a reparentable child on a
        // lineage walk, or a ContentOf / DecisionRecordWriter target (ForParents keeps ownership parents only) — and no citing
        // table (RecordOwnershipResolver.IsCitation: sprk_signal) is one, so the exclusion could never fire.
        // UAC task 194b: in the table's primary order; the first is the parent's own primary, which a look-through follows.
        // V-fix 3: then its Secure-only parents (its canonical), for the walks that decide whether it sits under a secure record
        // (the lineage, the secure roots above) — never for the look-through (ReadLookThroughFilingAsync), which follows the
        // primary order alone.
        if (entity is null)
            return null;
        // Owner decision 2026-10-29: a project's BFF-managed pair is its filing; one that cannot be told is unread (refused).
        var pair = await ReadPairParentAsync(row, entity, committedReads, ct).ConfigureAwait(false);
        if (pair.Undetermined is not null)
            return null;
        return OrderedWithPair(row, entity, pair.Parent)
            .Concat(RecordOwnershipContext.SecureOnlyParentsOf(entity)).Distinct().ToArray();
    }

    /// <summary>
    /// Owner decision 2026-10-29: the parent a row's ADR-024 pair names ON ITS OWN — only on a table whose pair is BFF-managed
    /// (a project: <c>SecureRootInheritance.PairNamesAParentOnItsOwn</c>; the one rule creation reads it by,
    /// <c>SecureRootInheritance.PairParentAsync</c>). A work assignment's pair is never followed (text any user with Write can
    /// set). <c>Undetermined</c>: the pair names a record that cannot be told — the caller refuses, never "unfiled". Fix
    /// round (B-K1): a pair naming a record that no longer exists names no parent (the project is no longer filed), as the
    /// secure-root plan reads a missing parent (<c>SecureRootInheritance.ExistingPairParentAsync</c>). A Dataverse fault
    /// propagates.
    /// </summary>
    private Task<(RecordOwnershipParent? Parent, string? Undetermined)> ReadPairParentAsync(
        RecordOwnershipParent row, Entity entity, bool committedReads, CancellationToken ct) =>
        Sprk.Bff.Api.Services.Access.SecureRootInheritance.ExistingPairParentAsync(
            _dataverse, row.EntityLogicalName, RecordOwnershipContext.PairIdOf(entity),
            entity.GetAttributeValue<EntityReference>(Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairTypeColumn)?.Id,
            committedReads, ct);

    /// <summary>
    /// <paramref name="entity"/>'s ownership parents in its table's primary order, with <paramref name="pairParent"/> (the
    /// record its BFF-managed pair names) keyed under the pair column — <c>SecureChildLineage.RootLookupOrder</c>'s key for a
    /// project, so it ranks first.
    /// </summary>
    private static IReadOnlyList<RecordOwnershipParent> OrderedWithPair(
        RecordOwnershipParent row, Entity entity, RecordOwnershipParent? pairParent) =>
        pairParent is null
            ? RecordOwnershipContext.OrderedParentsOf(entity)
            : RecordOwnershipContext.ForRow(
                row.EntityLogicalName,
                RecordOwnershipContext.LookupsOf(entity).Append(new KeyValuePair<string, RecordOwnershipParent?>(
                    Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairIdColumn, pairParent)),
                RecordOwnershipContext.PairIdOf(entity)).Parents;

    /// <summary>
    /// The filing a look-through follows for <paramref name="row"/>: its ownership parents in its table's primary order
    /// (no Secure-only parents). Owner decision 2026-10-29: a row filed only under an invoice or event (a work assignment with
    /// no matter or project) names the matters and projects that record belongs to FIRST, the invoice or event kept after
    /// them — <c>SecureRootInheritance.OwnerLookupsThroughIntermediatesAsync</c>, the derivation creation and the un-secure
    /// cascade ask, so every path ranks the same parent first. <c>Filing</c> is <c>null</c> when the row cannot be read, or
    /// (with <c>Unverifiable</c>) when what its invoice or event belongs to cannot be told (fail closed); a Dataverse fault
    /// reading the row itself, or reading its invoice or event, propagates.
    /// </summary>
    private async Task<(IReadOnlyList<RecordOwnershipParent>? Filing, string? Unverifiable)> ReadLookThroughFilingAsync(
        RecordOwnershipParent row, bool committedReads, CancellationToken ct)
    {
        var entity = await ReadFilingRowAsync(row, committedReads, ct).ConfigureAwait(false);
        if (entity is null)
            return (null, null);

        // Owner decision 2026-10-29: a project's BFF-managed pair is its filing (a work assignment's never is); a pair that
        // cannot be told refuses (ParentUnresolved), never "unfiled".
        var pair = await ReadPairParentAsync(row, entity, committedReads, ct).ConfigureAwait(false);
        if (pair.Undetermined is { } undetermined)
            return (null, undetermined);
        if (pair.Parent is not null)
            return (OrderedWithPair(row, entity, pair.Parent), null);
        // Fix round (B-K3): a project is filed only through its pair. With no pair parent (an empty pair, or one naming a
        // record that no longer exists) it is filed under nothing — never its other lookups (OrderedParentsOf).
        if (Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairNamesAParentOnItsOwn(row.EntityLogicalName))
            return (Array.Empty<RecordOwnershipParent>(), null);

        var lookups = RecordOwnershipContext.LookupsOf(entity).ToList();
        // #2143 round 3 (K1): on the re-file path what the invoice or event belongs to is a committed read too.
        var through = await Sprk.Bff.Api.Services.Access.SecureRootInheritance
            .OwnerLookupsThroughIntermediatesAsync(_dataverse, _logger, row.EntityLogicalName, lookups, ct, committedReads)
            .ConfigureAwait(false);
        // A faulted read of the invoice or event (throttled, failed) is not an answer: it propagates, as a fault reading the row
        // itself does (ReadFilingRowAsync), so the caller retries rather than reporting a refusal. Creation refuses on it
        // instead (fail closed, OwnerFilingThroughIntermediates.Unverifiable).
        if (through.Fault is { } fault)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
        if (through.Unverifiable is { } unverifiable)
            return (null, unverifiable);
        if (through.Lookups.Count == 0)
            return (RecordOwnershipContext.OrderedParentsOf(entity), null);

        // The one builder ranks them (RecordOwnershipContext.ForRow: the table's primary order, matter then project first).
        return (RecordOwnershipContext.ForRow(
                row.EntityLogicalName, through.Lookups.Concat(lookups), RecordOwnershipContext.PairIdOf(entity)).Parents,
            null);
    }

    /// <summary>
    /// The row <paramref name="row"/> names, every column (task 146 verifier item 3): a row's parents are whatever lookups it
    /// carries, and a per-table column list is the guess this task forbids. <c>null</c> when it cannot be read; a Dataverse
    /// fault propagates.
    /// </summary>
    /// <remarks>#2143 round 3 (K1): <paramref name="committedReads"/> is <c>true</c> on the re-file path only (a committed
    /// read); the create paths read with <c>NoLock</c>.</remarks>
    private async Task<Entity?> ReadFilingRowAsync(RecordOwnershipParent row, bool committedReads, CancellationToken ct)
    {
        var query = new QueryExpression(row.EntityLogicalName)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = !committedReads,
        };
        query.Criteria.AddCondition($"{row.EntityLogicalName}id", ConditionOperator.Equal, row.RecordId);
        return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
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
    /// #2143 round 3 (K1): <paramref name="committedReads"/> is <c>true</c> on the re-file path only — the settle and put-back
    /// decisions are taken from these facts, so they are read committed (<c>NoLock = false</c>) there; the create paths, hot
    /// and shared, keep <c>NoLock</c>.
    /// </remarks>
    private async Task<ParentFacts?> ReadParentAsync(
        RecordOwnershipParent parent, IReadOnlyDictionary<RecordOwnershipParent, Guid>? plannedOwningTeams, bool committedReads,
        CancellationToken ct)
    {
        var isSecureFlaggedRoot = SecureFlaggedRoots.Contains(parent.EntityLogicalName);
        var columns = isSecureFlaggedRoot
            ? new ColumnSet(OwningBusinessUnitColumn, OwningTeamColumn, IsSecureColumn)
            : new ColumnSet(OwningBusinessUnitColumn, OwningTeamColumn);
        // Fix round (B-K2): a project's pair id rides in the same read, so a project filed under nothing is never looked through.
        var pairedTable = Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairNamesAParentOnItsOwn(parent.EntityLogicalName);
        if (pairedTable)
            columns.AddColumn(Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairIdColumn);

        var query = new QueryExpression(parent.EntityLogicalName)
        {
            ColumnSet = columns,
            TopCount = 1,
            NoLock = !committedReads
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
        var unpairedProject = pairedTable
                              && string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(
                                  Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairIdColumn));

        if (plannedOwningTeams is not null
            && plannedOwningTeams.TryGetValue(
                parent with { EntityLogicalName = parent.EntityLogicalName.Trim().ToLowerInvariant() }, out var plannedTeam))
        {
            var teamQuery = new QueryExpression(TeamEntity)
            {
                ColumnSet = new ColumnSet(BusinessUnitColumn),
                TopCount = 1,
                NoLock = !committedReads
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

            return new ParentFacts(parent, plannedBu.Value, HasOwningTeam: true, flaggedSecure) { UnpairedProject = unpairedProject };
        }

        return new ParentFacts(parent, businessUnitId.Value, hasOwningTeam, flaggedSecure) { UnpairedProject = unpairedProject };
    }

    /// <summary>
    /// Resolves the acting user's business unit, then that BU's owner team.
    /// <paramref name="userKeyColumn"/> selects which identity the caller is known by.
    /// </summary>
    private async Task<RecordOwnerResolution> ResolveFromUserAsync(
        string userKeyColumn, Guid userKey, RecordOwnershipContext context, CancellationToken ct)
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
        // Issue #1576: a disabled systemuser keeps its oid and id; its business unit must not own a new record, so it
        // resolves as "no user" and the save refuses (ActingUserUnresolved) rather than filing under that BU.
        userQuery.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

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
        var secureBu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct, _logger).ConfigureAwait(false);
        if (secureBu.Ambiguous)
        {
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
                "more than one business unit carries the Secure Record name, so the acting user's business unit cannot be classified");
        }

        return secureBu.Id == businessUnitId
            ? await ResolveNamedSecureOwnerTeamAsync(businessUnitId.Value, ct).ConfigureAwait(false)
            : await OrdinaryExitAsync(
                await ResolveDefaultOwnerTeamAsync(businessUnitId.Value, ct).ConfigureAwait(false), businessUnitId.Value, context, ct)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// UAC task 194b R4b-2 (owner round 131) and R4b-2c (owner round 136): the ordinary exit. A business unit's default team
    /// answer for a row of a hard-deny table (<see cref="IsRestrictedContentTable"/>, the table
    /// <see cref="RecordOwnershipContext.ChildEntityLogicalName"/> names) becomes the restricted-content owner team of the
    /// CUSTOMER's top business unit above <paramref name="answerBusinessUnitId"/> (the root's direct child it sits under) —
    /// the walk and the team kept for the pass when the question carries the pass's cache — recording the default team it
    /// replaced (<see cref="RecordOwnerResolution.InPlaceOfDefaultTeamId"/>). Every other answer passes through unchanged: a
    /// Secure answer, a refusal, and an answer that keeps the creator never reach a default team. FAIL CLOSED: an answer in the
    /// root itself, a top unit that is the Secure Record unit, a parent chain that cannot be read or loops, and a team that
    /// cannot be used each refuse (<see cref="RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved"/>), never the default team.
    /// </summary>
    private async Task<RecordOwnerResolution> OrdinaryExitAsync(
        RecordOwnerResolution defaultTeam, Guid answerBusinessUnitId, RecordOwnershipContext context, CancellationToken ct)
    {
        if (!defaultTeam.IsOwned || defaultTeam.IsSecureOwner || !IsRestrictedContentTable(context.ChildEntityLogicalName))
            return defaultTeam;

        var identity = await RestrictedContentOwnerTeamForAsync(answerBusinessUnitId, context.LookupCache, ct).ConfigureAwait(false);
        if (!identity.IsResolved)
        {
            _logger.LogError(
                "Refusing to resolve an owning team for a {Table} row: the restricted-content owner team cannot be used "
                + "({Status}; answer business unit {AnswerBusinessUnitId}, customer business unit {CustomerBusinessUnitId}; "
                + "{MembersFound} member(s) read). A hard-deny row is never owned by a business unit's default team "
                + "(UAC task 194b R4b-2c).",
                context.ChildEntityLogicalName, identity.Status, answerBusinessUnitId, identity.CustomerBusinessUnitId,
                identity.MembersFound);
            return RecordOwnerResolution.Refused(
                RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved,
                $"the restricted-content owner team cannot be used ({identity.Status})");
        }

        return RecordOwnerResolution.Owned(identity.OwnerTeamId!.Value) with { InPlaceOfDefaultTeamId = defaultTeam.OwningTeamId };
    }

    /// <summary>
    /// R4b-2c: the restricted-content owner team for an answer in <paramref name="answerBusinessUnitId"/>: the walk to the
    /// customer's top business unit, then the team there — each kept in the pass's cache when there is one. Only answers are
    /// kept; a read that faults propagates and is asked again.
    /// </summary>
    private async Task<Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity> RestrictedContentOwnerTeamForAsync(
        Guid answerBusinessUnitId, RecordOwnerLookupCache? cache, CancellationToken ct)
    {
        if (cache is not null && cache.RestrictedContentByAnswerBusinessUnit.TryGetValue(answerBusinessUnitId, out var known))
            return known;

        Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity identity;
        var (top, walk) = await Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam
            .CustomerTopBusinessUnitAsync(_dataverse, answerBusinessUnitId, ct).ConfigureAwait(false);
        if (top is not { } customerBu)
        {
            identity = new(walk, null, null, 0);
        }
        else if (cache is not null && cache.RestrictedContentByCustomerBusinessUnit.TryGetValue(customerBu, out var team))
        {
            identity = team;
        }
        else
        {
            var secureBu = await SecureBusinessUnitAsync(cache, ct).ConfigureAwait(false);
            identity = secureBu.Ambiguous
                ? new(Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamStatus.SecureBusinessUnitAmbiguous, null, customerBu, 0)
                : await Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam
                    .IdentifyAsync(_dataverse, _configuration, customerBu, secureBu.Id, ct).ConfigureAwait(false);
            if (cache is not null)
                cache.RestrictedContentByCustomerBusinessUnit[customerBu] = identity;
        }

        if (cache is not null)
            cache.RestrictedContentByAnswerBusinessUnit[answerBusinessUnitId] = identity;
        return identity;
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
    private async Task<RecordOwnerResolution> ResolveDefaultOwnerTeamAsync(
        Guid businessUnitId, CancellationToken ct, RecordOwnerLookupCache? cache = null)
    {
        if (cache is not null && cache.DefaultOwnerTeams.TryGetValue(businessUnitId, out var known))
            return known;
        var answer = await ReadDefaultOwnerTeamAsync(businessUnitId, ct).ConfigureAwait(false);
        if (cache is not null)
            cache.DefaultOwnerTeams[businessUnitId] = answer;
        return answer;
    }

    /// <summary>The Secure Record business unit: the pass's cached answer when it has one, else the one lookup
    /// (<see cref="SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync"/>), kept for the pass.</summary>
    private async Task<(Guid? Id, bool Ambiguous)> SecureBusinessUnitAsync(RecordOwnerLookupCache? cache, CancellationToken ct)
    {
        if (cache?.SecureBusinessUnit is { } known)
            return known;
        var answer = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct, _logger)
            .ConfigureAwait(false);
        if (cache is not null)
            cache.SecureBusinessUnit = answer;
        return answer;
    }

    private async Task<RecordOwnerResolution> ReadDefaultOwnerTeamAsync(Guid businessUnitId, CancellationToken ct)
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
