// spaarkeai-word-add-in-r1 task 030 (FR-13): the shared server-side record-creation service.
//
// Placement Justification (bff-extensions.md) and the CLAUDE.md §11 three-question answers are in
// projects/spaarkeai-word-add-in-r1/notes/030-creation-service-decisions.md §3-§4. In short: it runs inline in
// POST /api/office/quickcreate/{entityType} (a user is waiting; ADR-001 rules out Functions), it composes the
// existing IGenericEntityService + IFieldMappingDataverseService seams (no new Dataverse client), and it is the
// one implementation task 031 (Project) and the post-r1 wizard-migration evaluation are meant to call.
//
// NUMBERING (task 076, owner decisions 2026-10-02): sprk_matternumber / sprk_projectnumber — each table's PRIMARY NAME —
// are assigned by Dataverse's platform autonumber (MAT-{SEQNUM:6} / PRJ-{SEQNUM:6}), set up per environment by
// scripts/Set-RecordNumberingSchema.ps1. It is INTERIM: "the dataverse auto numbering is just an interim solution until
// we build the numbering function". This service never writes the number (the platform fills it only when the create
// leaves it empty); it retries a create the number's alternate key refuses, and warns when no number came back.
// See projects/spaarkeai-word-add-in-r1/notes/076-record-numbering.md.

using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Office;
// Models.Office declares its own EntityReference (the add-in's association DTO); every reference here is the SDK's.
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Input to <see cref="RecordCreationService.CreateAsync"/>.
/// </summary>
public sealed record RecordCreationRequest
{
    /// <summary>The entity to create. <b>Matter</b> (task 030) or <b>Project</b> (task 031); Invoice stays on the minimal path.</summary>
    public required QuickCreateEntityType EntityType { get; init; }

    /// <summary>Record name (required; trimmed).</summary>
    public required string Name { get; init; }

    /// <summary>Optional description (trimmed; ignored when blank).</summary>
    public string? Description { get; init; }

    /// <summary>The caller's Entra object id — for logs only; the owner comes from <see cref="OwnerSystemUserId"/>'s business unit.</summary>
    public required string CallerUserId { get; init; }

    /// <summary>
    /// The caller's Dataverse <c>systemuserid</c>, resolved server-side (never from the client). <b>Load-bearing</b>:
    /// when it is absent or unparseable the service refuses rather than creating an app-owned record. It is NOT the
    /// owner: the record is owned by this user's business-unit default owner team (task 080).
    /// </summary>
    public string? OwnerSystemUserId { get; init; }

    /// <summary>
    /// The <c>sprk_mattertype_ref</c> id. The pane will always send it (owner decision 2026-09-11; the client task
    /// adding the required field is pending). A type that resolves sets the <c>sprk_mattertype</c> lookup. A missing,
    /// empty or unknown type is NEVER a rejection: the matter is created without the lookup, with a warning.
    /// </summary>
    public Guid? MatterTypeId { get; init; }

    /// <summary>
    /// The <c>sprk_practicearea_ref</c> id (Matter only; task 100). Same posture as <see cref="MatterTypeId"/>: set
    /// once verified to exist; a missing, empty or unknown id is never a rejection (an unknown one warns).
    /// </summary>
    public Guid? PracticeAreaId { get; init; }

    /// <summary>
    /// The <c>sprk_projecttype_ref</c> id (Project only; task 100). Optional; set once verified to exist, an unknown
    /// id warns and is dropped.
    /// </summary>
    public Guid? ProjectTypeId { get; init; }

    /// <summary>
    /// The contact to write into <c>sprk_assignedtointernal</c> (task 100). When supplied it wins over a
    /// field-mapping rule and over the maker default; when absent or empty the existing default applies
    /// (a mapped value is kept, else the maker's linked contact — unified-access-control-r2 task 152).
    /// </summary>
    /// <remarks>
    /// 🔴 Written as given, without a read: the CALLER's Read right on the contact MUST already have been established
    /// upstream — <c>QuickCreateSourceAccessFilter</c> does that for the quick-create route. A new caller of this
    /// service must do the same, or it can attach any contact id to a record the caller's team owns.
    /// </remarks>
    public Guid? AssignedToContactId { get; init; }

    /// <summary>
    /// Optional record-context entity logical name (e.g. <c>sprk_project</c>) — the Field Mapping Framework source.
    /// </summary>
    /// <remarks>
    /// 🔴 The source record is read <b>app-only</b>. The CALLER'S Read right on it MUST already have been
    /// established upstream — <c>QuickCreateSourceAccessFilter</c> does that for the quick-create route. A new
    /// caller of this service must do the same, or it turns this into a way to copy fields out of any record.
    /// </remarks>
    public string? SourceEntityLogicalName { get; init; }

    /// <summary>Optional record-context id; see <see cref="SourceEntityLogicalName"/>.</summary>
    public Guid? SourceRecordId { get; init; }
}

/// <summary>Why a creation was refused. Every kind means <b>no row was written</b>.</summary>
public enum RecordCreationFailureKind
{
    /// <summary>The request cannot be honoured as given (wrong entity type, blank name).</summary>
    InvalidInput,

    /// <summary>No owner could be determined — the caller has no resolvable Dataverse user, or their business unit
    /// has no single default owner team (task 080) — so nothing was created.</summary>
    OwnerUnresolved,

    /// <summary>Every create attempt was refused by the number's alternate key: the platform kept issuing numbers that
    /// rows already hold (typed in ahead of the sequence). Nothing was created; the operator re-seeds with
    /// <c>scripts/Set-RecordNumberingSchema.ps1 -Apply</c> (task 076).</summary>
    NumberUnavailable,

    /// <summary>
    /// Task 158 r1 (owner round 31): a record filed under a SECURE record is created secure, and the caller may not create it
    /// so — on the No Access list of the secure record or the new one, or without the rights to create it there. 403.
    /// </summary>
    SecureFilingRefused,

    /// <summary>
    /// Task 158 r1: whether the caller may create it secure could not be checked, or the secure create could not be
    /// completed and was removed again. Nothing was created. 500.
    /// </summary>
    SecureFilingFailed,
}

/// <summary>A structured refusal. <see cref="Code"/> is a stable identifier; <see cref="Detail"/> is user-safe text.</summary>
public sealed record RecordCreationFailure(RecordCreationFailureKind Kind, string Code, string Detail);

/// <summary>
/// Outcome of <see cref="RecordCreationService.CreateAsync"/>: the created record, or a
/// <see cref="RecordCreationFailure"/>. A business outcome is never an exception.
/// </summary>
public sealed record RecordCreationResult
{
    /// <summary>The created record id (empty on failure).</summary>
    public Guid RecordId { get; init; }

    /// <summary>The created record's entity logical name.</summary>
    public string LogicalName { get; init; } = string.Empty;

    /// <summary>The name actually written (a field-mapping rule may have replaced the requested one).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Non-fatal diagnostics (field-mapping skips, no or unknown matter type, BU defaults unavailable, the
    /// created record came back without a number — task 076).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Set when the creation was refused; <see langword="null"/> on success.</summary>
    public RecordCreationFailure? Failure { get; init; }

    /// <summary>True when a record was created.</summary>
    public bool Succeeded => Failure is null;

    internal static RecordCreationResult Failed(RecordCreationFailure failure) => new() { Failure = failure };
}

/// <summary>
/// Creates Dataverse records server-side with the completeness the client <c>Create*Wizard</c> components give
/// them in the browser — a load-bearing owner, business-unit defaults, and the Field Mapping Framework. r1 scope is
/// <b>Matter</b> (task 030) and <b>Project</b> (task 031); Invoice stays on the minimal generic path.
/// </summary>
/// <remarks>
/// <para><b>Matter pipeline</b> (mirrors <c>matterService.createMatter</c>, except that the number comes from the
/// platform, not the client):</para>
/// <list type="number">
///   <item><description>Name / description; the <c>sprk_mattertype</c> and <c>sprk_practicearea</c> lookups when the
///   supplied ids resolve (one existence read each — an unknown id is dropped with a warning, never rejected; task 100
///   added the practice area).</description></item>
///   <item><description>BU defaults from the OWNER's business unit — <c>sprk_searchindexname</c> (INV-5 guarded) and
///   the <c>sprk_ai_search_index</c> lookup — mirroring <c>EntityCreationService.applyUserBuDefaults</c>.
///   <c>sprk_containerid</c> is deliberately NOT written (unified-access-control-r2 task 076, W1).</description></item>
///   <item><description>Field Mapping Framework — one profile read, one source read, rules applied by
///   <see cref="CreateTimeFieldMapping"/>; a missing profile is a silent no-op.</description></item>
///   <item><description>Assigned To Internal — the contact the request names, else a mapped value, else the maker's
///   linked contact (task 152; task 100 added the request's contact).</description></item>
///   <item><description>Owner — <c>ownerid</c> = the caller's business-unit DEFAULT OWNER TEAM (task 080, invariant
///   I-6); refused when the caller or the team is unresolved.</description></item>
///   <item><description>Creator — <c>sprk_createdbyperson</c> = the caller (unified-access-control-r2 task 133, owner
///   round 7 item 2): this create is app-only, so <c>createdby</c> is the BFF application user and this column is what
///   records the person. Protected from field mapping like the owner.</description></item>
/// </list>
/// <para><b><c>sprk_matternumber</c> is never written here</b> — not directly, and not through a field-mapping rule of
/// any type (the protected-attribute check is case-insensitive). It is the platform's autonumber (task 076, INTERIM until
/// the numbering function): Dataverse fills it only when the create leaves it empty, so a value sent here would pre-empt
/// the sequence. The create is retried when the number's alternate key refuses it (<see cref="CreateNumberedAsync"/>),
/// and a record that came back without a number is reported (<see cref="WarnIfNumberMissingAsync"/>).</para>
/// <para><b>Deliberate deviations from the client engine</b> (notes/030 §8): mapping may not touch
/// <c>sprk_matternumber</c>, <c>ownerid</c> or <c>sprk_containerid</c>; a mapping that blanks the name or writes a
/// non-matter-type value into <c>sprk_mattertype</c> is reverted with a warning.</para>
/// <para><b>ADR-044</b>: every GUID handled here is a <see cref="Guid"/> value — canonical by construction. The one
/// string GUID, <see cref="RecordCreationRequest.OwnerSystemUserId"/>, is parsed with
/// <see cref="Guid.TryParse(string?, out Guid)"/>. Body GUIDs (<c>matterTypeId</c>, <c>sourceRecordId</c>) are bound
/// by System.Text.Json, which accepts only the bare "D" form (either case) and fails a brace-wrapped value closed with a
/// 400 before this service runs — so clients must send <c>cleanGuid</c> output. Reads are typed SDK calls; no GUID is
/// interpolated into an OData string.</para>
/// <para><b>ADR-010</b>: concrete, one registration (<c>OfficeModule</c>). No interface — there is one
/// implementation, and the contract test substitutes the Dataverse boundary, not this class.</para>
/// </remarks>
public sealed class RecordCreationService
{
    internal const string MatterEntity = "sprk_matter";
    internal const string MatterNameAttribute = "sprk_mattername";
    internal const string MatterDescriptionAttribute = "sprk_matterdescription";
    internal const string MatterNumberAttribute = "sprk_matternumber";
    internal const string MatterTypeAttribute = "sprk_mattertype";
    internal const string MatterTypeEntity = "sprk_mattertype_ref";
    internal const string MatterTypeIdAttribute = "sprk_mattertype_refid";

    // Task 100 (owner UAT round 5 item 3) — live metadata, spaarkedev1 2026-10-05: sprk_matter.sprk_practicearea →
    // sprk_practicearea_ref; sprk_project.sprk_projecttype_ref → sprk_projecttype_ref (the lookup column carries the
    // table's name).
    internal const string MatterPracticeAreaAttribute = "sprk_practicearea";
    internal const string PracticeAreaEntity = "sprk_practicearea_ref";
    internal const string PracticeAreaIdAttribute = "sprk_practicearea_refid";
    internal const string ProjectTypeAttribute = "sprk_projecttype_ref";
    internal const string ProjectTypeEntity = "sprk_projecttype_ref";
    internal const string ProjectTypeIdAttribute = "sprk_projecttype_refid";

    internal const string ContactEntity = "contact";
    internal const string ContactNameAttribute = "fullname";
    internal const string ContactEmailAttribute = "emailaddress1";

    // Project (task 031). sprk_projectnumber is the sprk_project PRIMARY NAME attribute and is NEVER written on this
    // path — the platform's autonumber assigns it (PRJ-{SEQNUM:6}, task 076, interim). It is named here so it can be
    // PROTECTED from field-mapping rules and read back after the create; see ProjectProtectedAttributes.
    internal const string ProjectEntity = "sprk_project";
    internal const string ProjectNameAttribute = "sprk_projectname";
    internal const string ProjectDescriptionAttribute = "sprk_projectdescription";
    internal const string ProjectNumberAttribute = "sprk_projectnumber";

    internal const string OwnerAttribute = "ownerid";
    internal const string ContainerAttribute = "sprk_containerid";
    internal const string SystemUserEntity = "systemuser";
    internal const string UserBusinessUnitAttribute = "businessunitid";
    internal const string BusinessUnitEntity = "businessunit";
    internal const string SearchIndexNameAttribute = "sprk_searchindexname";
    internal const string SearchIndexLookupAttribute = "sprk_ai_search_index";
    internal const string SearchIndexEntity = "sprk_aisearchindex";

    /// <summary>
    /// Attributes a field-mapping rule may not write when creating a MATTER: the number (the platform's autonumber
    /// assigns it only when the create leaves it empty, so a mapped value would pre-empt the sequence — and, copied
    /// from a parent, would collide on the number's alternate key; task 076), the load-bearing owner, and the storage
    /// container (server-derived only — unified-access-control-r2 task 076 W1). Case-insensitive, so a mis-cased or
    /// padded target is caught too.
    /// </summary>
    /// <remarks>
    /// Task 133 (unified-access-control-r2, owner round 7 item 2) adds <c>sprk_createdbyperson</c>: the
    /// person who created the record is the caller, stamped by this service — a Copy rule from a source record would
    /// otherwise name that record's creator as this one's.
    /// </remarks>
    private static readonly IReadOnlySet<string> MatterProtectedAttributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MatterNumberAttribute, OwnerAttribute, ContainerAttribute, Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Column
        };

    /// <summary>
    /// The same three protections for a PROJECT, with <see cref="ProjectNumberAttribute"/> in place of the matter
    /// number — for the same reason: the platform assigns it, and a mapped value would pre-empt the sequence.
    /// </summary>
    /// <remarks>
    /// The sets are deliberately PER ENTITY rather than one merged set. A merged set would newly skip-and-warn a rule
    /// targeting <c>sprk_projectnumber</c> on a MATTER create — a behaviour change to the path task 030 shipped,
    /// which task 031's acceptance criteria forbid.
    /// </remarks>
    private static readonly IReadOnlySet<string> ProjectProtectedAttributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ProjectNumberAttribute, OwnerAttribute, ContainerAttribute, Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Column,
            // Task 158 r1: sprk_issecure is the BFF's own (task 150 locks it; owner round 31 item 2 sets it IN the create of a
            // project filed under a secure record) — never a value a mapping rule copies.
            SecureFlagAttribute,
        };

    /// <summary>The secure flag (task 150: written only by the BFF).</summary>
    internal const string SecureFlagAttribute = "sprk_issecure";

    /// <summary>The Create privilege a project create filed under a secure record is checked against AS THE CALLER (G5).</summary>
    internal const string ProjectCreatePrivilege = "prvCreatesprk_project";

    internal const string TeamEntity = "team";

    /// <summary>
    /// Create attempts before a number collision is a refusal (task 076). The platform's sequence is unique only
    /// against itself; a number typed in ahead of it is refused by the alternate key (<c>0x80060892</c>, measured
    /// live 2026-10-02), and the next attempt draws the next number. One collision is rare; three in a row means a run
    /// of typed-ahead numbers, which <c>scripts/Set-RecordNumberingSchema.ps1 -Apply</c> clears by moving the seed.
    /// </summary>
    internal const int MaxNumberAttempts = 3;

    /// <summary>The refusal code when every attempt collided (task 076) — 409 via <c>OfficeService.MapCreationFailureStatus</c>.
    /// Snake case like this service's other Matter/Project refusal codes; only the quick-create route raises it.</summary>
    internal const string NumberUnavailableCode = "record_number_unavailable";

    /// <summary>
    /// The core record's internal Assigned-To contact column (matter and project). Owner decision A7 (round 3,
    /// reversed; unified-access-control-r2 task 152): a quick-created record names its MAKER here, editable.
    /// </summary>
    internal const string AssignedToInternalAttribute = "sprk_assignedtointernal";

    private readonly IGenericEntityService _entities;
    private readonly IFieldMappingDataverseService _fieldMappings;
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService _identity;
    private readonly Sprk.Bff.Api.Services.Access.SecureRootFilingGate _rootFiling;
    private readonly Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer _assignedAccess;
    private readonly ILogger<RecordCreationService> _logger;
    private readonly Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe? _callerAccess;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor? _http;

    /// <param name="callerAccess">Task 158 r1 (owner round 31 item 2): the OBO probe a project created INTO isolation is
    /// checked with AS THE CALLER first (G5: Create on the table, AppendTo on every secure parent). Optional so a host
    /// without the external-access module still composes this service; without it such a create is REFUSED (fail closed),
    /// never made unchecked.</param>
    /// <param name="http">The request whose bearer token the probe exchanges (same optionality).</param>
    public RecordCreationService(
        IGenericEntityService entities,
        IFieldMappingDataverseService fieldMappings,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        ILogger<RecordCreationService> logger,
        Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe? callerAccess = null,
        Microsoft.AspNetCore.Http.IHttpContextAccessor? http = null)
    {
        _callerAccess = callerAccess;
        _http = http;
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _fieldMappings = fieldMappings ?? throw new ArgumentNullException(nameof(fieldMappings));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        // Task 158 (owner round 6): a project the Field Mapping Framework files under a secure matter or project is secured
        // through provisioning's own steps. Registered by AddCoreAncestorResolver (unconditional, §10 F.1).
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
        _assignedAccess = assignedAccess ?? throw new ArgumentNullException(nameof(assignedAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Task 142 (L1, owner Q5 + R3 "immediate"): the created matter/project's "Assigned *" contacts — the maker's own
    /// contact included (owner A7, <see cref="ApplyMakerAssignedInternalAsync"/>), and any column field mapping copied —
    /// receive their Collaborate grant or share now, not at the next job tick. AFTER the create committed; the
    /// materializer never throws and never fails this create (a fault is logged and the job repairs it).
    /// </summary>
    private Task MaterializeAssignedAccessAsync(string logicalName, Guid createdId, string callerOid, CancellationToken ct)
        => _assignedAccess.AfterWriteAsync(logicalName, createdId, writtenColumns: null, grantorOid: callerOid, ct);

    /// <summary>
    /// Owner decision A7 (round 3, REVERSED; unified-access-control-r2 task 152, coordinated with 142 and
    /// word-add-in-r1): the quick-created matter/project names its MAKER in <c>sprk_assignedtointernal</c>. The create
    /// is app-only, so Created By is the BFF application user and cannot make the record "for" the maker in the Daily
    /// Briefing; this column does (ADR-034 A3 people-targeting surface). The maker already has ACCESS through the
    /// business-unit team; under task 142 this column also issues the Assigned-To share.
    /// </summary>
    /// <remarks>
    /// <para>Precedence (task 100, owner decision B — Assigned To is on the pane's form, prefilled, optional):</para>
    /// <list type="number">
    ///   <item><description>the contact the REQUEST names — the user's explicit choice on the form, so it wins over a
    ///   mapping rule and the default. Its Read right was authorized upstream (see
    ///   <see cref="RecordCreationRequest.AssignedToContactId"/>);</description></item>
    ///   <item><description>a value a field-mapping rule already wrote is kept (a supplied value is never overwritten
    ///   by the default);</description></item>
    ///   <item><description>the maker's linked contact (<see cref="ResolveMakerContactIdAsync"/>) — the server's
    ///   default, which the pane's prefill shows (<see cref="ResolveDefaultAssigneeAsync"/>).</description></item>
    /// </list>
    /// <para>The maker's contact comes only from task 141's link (<c>PersonIdentity.ContactId</c>) — never an email
    /// match. No link → the column stays blank and <c>assigned_internal_unset</c> is logged; the create proceeds.</para>
    /// </remarks>
    private async Task ApplyAssignedInternalAsync(
        Entity entity, Guid? requestedContactId, Guid makerSystemUserId, CancellationToken ct)
    {
        if (requestedContactId is { } requested && requested != Guid.Empty)
        {
            entity[AssignedToInternalAttribute] = new EntityReference(ContactEntity, requested);
            return;
        }

        if (entity.Attributes.TryGetValue(AssignedToInternalAttribute, out var existing)
            && existing is EntityReference supplied
            && supplied.Id != Guid.Empty)
        {
            return;
        }

        if (await ResolveMakerContactIdAsync(makerSystemUserId, ct).ConfigureAwait(false) is { } makerContact)
        {
            entity[AssignedToInternalAttribute] = new EntityReference(ContactEntity, makerContact);
            return;
        }

        _logger.LogWarning(
            "assigned_internal_unset: entity={Entity} maker={MakerId} reason={Reason} — the maker has no linked contact, so "
            + "{Column} is left blank (never an email match); the record is still owned by the business-unit team",
            entity.LogicalName, makerSystemUserId, "maker_has_no_linked_contact", AssignedToInternalAttribute);
    }

    /// <summary>
    /// The maker's linked contact id (task 141's link), or <see langword="null"/> when there is none or it could not
    /// be resolved. The ONE place the default assignee is decided — both the create
    /// (<see cref="ApplyAssignedInternalAsync"/>) and the pane's prefill (<see cref="ResolveDefaultAssigneeAsync"/>)
    /// read it, so the prefill can never name someone the server would not.
    /// </summary>
    private async Task<Guid?> ResolveMakerContactIdAsync(Guid makerSystemUserId, CancellationToken ct)
    {
        try
        {
            var contactId = (await _identity.ResolveAsync(makerSystemUserId, ct).ConfigureAwait(false)).ContactId;
            return contactId is { } id && id != Guid.Empty ? id : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] The maker's linked contact could not be resolved for {MakerId}", makerSystemUserId);
            return null;
        }
    }

    /// <summary>
    /// The contact the pane's "+ New" form prefills in Assigned To (task 100, owner decision B): the caller's own
    /// linked contact with its display name, or <see langword="null"/> when the caller is unresolved, has no linked
    /// contact, or the contact could not be read. For a Matter or Project it is exactly the contact the create assigns
    /// when the request names none; an Invoice has no server default, so there the pane sends it explicitly.
    /// </summary>
    /// <param name="callerSystemUserId">The caller's resolved <c>systemuserid</c> (never from the client).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The contact row is read app-only. That discloses nothing: it is the caller's OWN identity link, resolved from
    /// their own <c>systemuserid</c>; the route takes no id. Never throws for a Dataverse fault — no prefill is the
    /// worst case, and the user can still pick someone.
    /// </remarks>
    public async Task<QuickCreateContactOption?> ResolveDefaultAssigneeAsync(Guid callerSystemUserId, CancellationToken ct)
    {
        if (callerSystemUserId == Guid.Empty
            || await ResolveMakerContactIdAsync(callerSystemUserId, ct).ConfigureAwait(false) is not { } contactId)
        {
            return null;
        }

        Entity? contact;
        try
        {
            contact = await _entities
                .RetrieveAsync(ContactEntity, contactId, [ContactNameAttribute, ContactEmailAttribute], ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] The default assignee contact {ContactId} could not be read; no prefill.", contactId);
            return null;
        }

        var name = contact?.GetAttributeValue<string>(ContactNameAttribute)?.Trim();
        var email = contact?.GetAttributeValue<string>(ContactEmailAttribute)?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            email = null;
        }

        // A contact with neither a name nor an email gives the user nothing to recognise — no prefill.
        var display = string.IsNullOrEmpty(name) ? email : name;
        if (display is null)
        {
            return null;
        }

        return new QuickCreateContactOption { Id = contactId, Name = display, Email = email };
    }

    /// <summary>
    /// The owner of a new Matter or Project (task 080, write-path invariant I-6): the caller's business unit's DEFAULT
    /// OWNER TEAM — owner decision 2026-09-22, "owned by the acting user's BU default owner team … not the user".
    /// </summary>
    /// <remarks>
    /// <para>A new core record is filed against nothing, so the acting user's business unit is the answer. The
    /// optional field-mapping SOURCE is not a parent — it only supplies values — so it does not decide ownership.</para>
    /// <para>The caller's <c>systemuserid</c> is still load-bearing: an unresolved caller is refused before this runs,
    /// and the business-unit defaults above are read from the same user, so the team and those defaults always
    /// come from one business unit.</para>
    /// <para>Returns <see langword="null"/> when no team resolves; the caller refuses (no row written).</para>
    /// </remarks>
    private Task<Guid?> ResolveOwnerTeamAsync(Guid callerSystemUserId, CancellationToken ct) =>
        _ownership.ResolveOwningTeamAsync(
            new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext { CallerSystemUserId = callerSystemUserId },
            ct);

    private static RecordCreationResult OwnerTeamUnresolved(string entityLabel) =>
        RecordCreationResult.Failed(new RecordCreationFailure(
            RecordCreationFailureKind.OwnerUnresolved,
            // ONE code for "no owner team" across every Office create (save, To Do, quick-create) — ADR-019: the
            // client maps a condition by its code, so three spellings of one refusal would be three client entries.
            Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.RecordOwnerUnresolved,
            $"The new {entityLabel} could not be assigned to your business unit's team, so it was not created. Ask an "
            + "administrator to check that your user has a business unit and that the unit has its default team."));

    private static RecordCreationResult NumberUnavailable(string entityLabel) =>
        RecordCreationResult.Failed(new RecordCreationFailure(
            RecordCreationFailureKind.NumberUnavailable,
            NumberUnavailableCode,
            $"The new {entityLabel} could not be given a number because the next numbers are already in use, so it was "
            + "not created. Try again; if it keeps failing, ask an administrator to check record numbering."));

    /// <summary>
    /// Creates the record, leaving its number to the platform's autonumber, and retries when the number's alternate key
    /// refuses the create (task 076). Returns <see langword="null"/> — nothing written — after
    /// <see cref="MaxNumberAttempts"/> refusals.
    /// </summary>
    /// <remarks>
    /// Only the alternate-key duplicate (<c>0x80060892</c>, classified by
    /// <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>, the classifier the race-proof Communication
    /// create already uses) is retried: Dataverse rejects such a create whole, so a retry cannot double-create, and it
    /// draws the next number (measured live 2026-10-02: the refused attempt's number is consumed, the next attempt gets
    /// the one after). Every other fault propagates on the first attempt, as before — a timeout, for one, may have
    /// created the row, and retrying it could create a second.
    /// <para>Scope: the classifier matches ANY alternate-key duplicate. Today the number's key is the only alternate key
    /// on <c>sprk_matter</c> (<c>sprk_MatterNumber</c>) and on <c>sprk_project</c> (<c>sprk_ProjectNumber</c>), so that is
    /// exact; a key added later on another column would be retried and reported as a number collision — revisit this
    /// then. Each refused attempt is also logged as an error by <c>DataverseServiceClientImpl.CreateAsync</c> itself, so a
    /// retried create leaves one such error per refusal in telemetry; the warning below names it as a number collision.</para>
    /// </remarks>
    private async Task<Guid?> CreateNumberedAsync(Entity entity, string entityLabel, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _entities.CreateAsync(entity, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex))
            {
                if (attempt >= MaxNumberAttempts)
                {
                    _logger.LogError(ex,
                        "record_number_unavailable: entity={Entity} attempts={Attempts} — every number the platform issued "
                        + "was already held by a row; run scripts/Set-RecordNumberingSchema.ps1 -Apply to move the seed past them",
                        entity.LogicalName, attempt);
                    return null;
                }

                _logger.LogWarning(
                    "[RECORD-CREATE] The {EntityLabel}'s number was already held by another row (attempt {Attempt} of {Max}); "
                    + "retrying with the next number.", entityLabel, attempt, MaxNumberAttempts);
            }
        }
    }

    /// <summary>
    /// Reads the created record's number back and warns when it is blank (task 076): a blank number is a blank NAME in
    /// every lookup and list, and it means this environment has no autonumber on the column — the schema script was not
    /// run. The record is kept; the user is told and the condition is logged as <c>record_number_unassigned</c>.
    /// </summary>
    /// <remarks>
    /// Never fails or alarms a create that succeeded. The row already exists when this runs, so a read that faults — a
    /// cancelled request included — is logged and otherwise ignored: it says nothing about the number, and turning a
    /// committed create into an error would hide the record the user just made.
    /// </remarks>
    private async Task WarnIfNumberMissingAsync(
        string entityName, Guid recordId, string numberAttribute, string entityLabel, List<string> warnings, CancellationToken ct)
    {
        Entity? created;
        try
        {
            created = await _entities.RetrieveAsync(entityName, recordId, [numberAttribute], ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] The new {EntityLabel} {RecordId} could not be read back to confirm its number.", entityLabel, recordId);
            return;
        }

        // The real boundary never answers null; a substituted one (a loose test double) may — that says nothing either.
        if (created is null || !string.IsNullOrWhiteSpace(created.GetAttributeValue<string>(numberAttribute)))
        {
            return;
        }

        _logger.LogError(
            "record_number_unassigned: entity={Entity} id={RecordId} — created without a number, so it has a blank name; "
            + "{Column} has no autonumber in this environment (run scripts/Set-RecordNumberingSchema.ps1)",
            entityName, recordId, numberAttribute);
        warnings.Add(
            $"The new {entityLabel} was created without a number, so it shows without a name in lists. Ask an administrator "
            + "to check record numbering.");
    }

    /// <summary>
    /// Creates the record. Returns a structured <see cref="RecordCreationFailure"/> — never a partial write — when
    /// the record cannot be created with its invariants intact. Dataverse transport or rejection errors on the final
    /// create propagate (the caller's generic 500 path, unchanged from before).
    /// </summary>
    public async Task<RecordCreationResult> CreateAsync(RecordCreationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.EntityType switch
        {
            QuickCreateEntityType.Matter => await CreateMatterAsync(request, ct).ConfigureAwait(false),
            QuickCreateEntityType.Project => await CreateProjectAsync(request, ct).ConfigureAwait(false),
            _ => RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.InvalidInput,
                "entity_type_not_supported",
                $"Server-side creation supports Matter and Project only; '{request.EntityType}' is created by another path."))
        };
    }

    private async Task<RecordCreationResult> CreateMatterAsync(RecordCreationRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.InvalidInput, "name_required", "A matter requires a name."));
        }

        // Owner is LOAD-BEARING (task 030 step 4) — the old QuickCreate posture of "unresolved → app-owned" is
        // exactly how a pane-created matter ended up owned by nobody the user recognises.
        if (!TryParseGuid(request.OwnerSystemUserId, out var ownerId))
        {
            _logger.LogWarning(
                "[RECORD-CREATE] Refusing Matter create for caller {CallerUserId}: no resolved Dataverse systemuser, "
                + "so the record cannot be owned by the caller.",
                request.CallerUserId);

            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.OwnerUnresolved,
                "owner_unresolved",
                "Your account could not be matched to a Dataverse user, so the new matter could not be assigned to "
                + "you and was not created. Ask an administrator to check that your user is provisioned in this "
                + "environment."));
        }

        var warnings = new List<string>();
        var entity = new Entity(MatterEntity);
        entity[MatterNameAttribute] = name;
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            entity[MatterDescriptionAttribute] = request.Description.Trim();
        }

        // Guid.Empty is how some clients say "unset": treated exactly like an absent type (owner decision: never reject).
        // A supplied type is set only once it is VERIFIED to exist; not-found or an unanswerable check → no lookup + a
        // warning. The optional type never fails the create (a dangling lookup would fault it with a 500).
        EntityReference? requestedType = null;
        var typeWarningAdded = false;
        if (request.MatterTypeId is { } requestedTypeId && requestedTypeId != Guid.Empty)
        {
            if (await CheckReferenceAsync(MatterType, requestedTypeId, ct).ConfigureAwait(false) is { } typeWarning)
            {
                warnings.Add(typeWarning);
                typeWarningAdded = true;
            }
            else
            {
                requestedType = new EntityReference(MatterTypeEntity, requestedTypeId);
                entity[MatterTypeAttribute] = requestedType;
            }
        }

        // Task 100: Practice Area — required on the pane's form (owner decision B), but on the server the same
        // "never a rejection" posture as the matter type: verified to exist, else dropped with a warning.
        await ApplyReferenceAsync(entity, PracticeArea, request.PracticeAreaId, warnings, ct).ConfigureAwait(false);

        await ApplyBusinessUnitDefaultsAsync(entity, ownerId, "matter", warnings, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.SourceEntityLogicalName)
            && request.SourceRecordId is { } sourceRecordId
            && sourceRecordId != Guid.Empty)
        {
            await ApplyFieldMappingsAsync(
                    entity, request.SourceEntityLogicalName.Trim(), sourceRecordId, MatterProtectedAttributes, warnings, ct)
                .ConfigureAwait(false);

            KeepRequestedNameIfMappingBlankedIt(entity, MatterNameAttribute, name, "matter", warnings);
        }

        if (ResolveFinalMatterType(entity, requestedType, warnings) is null && !typeWarningAdded)
        {
            // Owner decision 2026-09-11: the type is required in the pane, but a missing one is NOT a rejection.
            warnings.Add("No matter type was supplied. Open the matter and set its type.");
        }

        // Owner A7 (task 152): the matter is FOR its maker — after field mapping, so a mapped value is kept — unless
        // the request names the assignee (task 100), which wins.
        await ApplyAssignedInternalAsync(entity, request.AssignedToContactId, ownerId, ct).ConfigureAwait(false);

        // Set LAST, after mapping, so nothing can overwrite it — owner attribution is load-bearing (task 030 step 4).
        // Task 080: the owner is the caller's business-unit default owner TEAM, not the caller.
        if (await ResolveOwnerTeamAsync(ownerId, ct).ConfigureAwait(false) is not { } ownerTeamId)
        {
            return OwnerTeamUnresolved("matter");
        }

        entity[OwnerAttribute] = new EntityReference(TeamEntity, ownerTeamId);

        // Task 133 (owner round 7 item 2): this create is APP-ONLY, so createdby is the BFF application user. The
        // person who asked for the matter is recorded here — set last, like the owner, so no mapping rule replaces it.
        Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Stamp(entity, ownerId);

        if (await CreateNumberedAsync(entity, "matter", ct).ConfigureAwait(false) is not { } createdId)
        {
            return NumberUnavailable("matter");
        }

        await WarnIfNumberMissingAsync(MatterEntity, createdId, MatterNumberAttribute, "matter", warnings, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[RECORD-CREATE] Matter {MatterId} created for caller {CallerUserId}: owner team={OwnerTeamId}, warnings={WarningCount}",
            createdId, request.CallerUserId, ownerTeamId, warnings.Count);

        await MaterializeAssignedAccessAsync(MatterEntity, createdId, request.CallerUserId, ct).ConfigureAwait(false);

        return new RecordCreationResult
        {
            RecordId = createdId,
            LogicalName = MatterEntity,
            Name = (string)entity[MatterNameAttribute],
            Warnings = warnings
        };
    }

    /// <summary>
    /// The Project path (task 031, FR-13). Deliberately NOT a copy of Matter: its optional type is
    /// <c>sprk_projecttype_ref</c> (task 100), there is no "no type supplied" warning, and <c>sprk_projectnumber</c> is
    /// never written.
    /// </summary>
    /// <remarks>
    /// <para><b><c>sprk_projectnumber</c> is the <c>sprk_project</c> PRIMARY NAME attribute</b>, and this path writes
    /// NOTHING to it. Since task 076 the platform's autonumber assigns <c>PRJ-######</c> on create (owner decision
    /// 2026-10-02, interim until the numbering function), so a pane-created Project is no longer blank. Unlike Matter,
    /// no client path supplies a project number at all (<c>projectService.ts</c> sends none), so every Project created
    /// without one — the Create Project wizard's included — gets the platform's number.</para>
    /// <para>Everything else is symmetric with Matter and reuses its implementation unchanged: a <b>load-bearing</b>
    /// owner (an unresolved caller is refused; task 030 left Project best-effort and named task 031 as the decider —
    /// notes/031 §4), business-unit defaults, and the Field Mapping Framework. <c>sprk_containerid</c> is never
    /// written (task 076 W1).</para>
    /// </remarks>
    private async Task<RecordCreationResult> CreateProjectAsync(RecordCreationRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.InvalidInput, "name_required", "A project requires a name."));
        }

        // Load-bearing, as for Matter: "unresolved → app-owned, silently" is the defect FR-13 exists to fix, and
        // AC2 requires a non-null ownerid. The refusal happens before any write, so nothing is created.
        if (!TryParseGuid(request.OwnerSystemUserId, out var ownerId))
        {
            _logger.LogWarning(
                "[RECORD-CREATE] Refusing Project create for caller {CallerUserId}: no resolved Dataverse systemuser, "
                + "so the record cannot be owned by the caller.",
                request.CallerUserId);

            return RecordCreationResult.Failed(new RecordCreationFailure(
                RecordCreationFailureKind.OwnerUnresolved,
                "owner_unresolved",
                "Your account could not be matched to a Dataverse user, so the new project could not be assigned to "
                + "you and was not created. Ask an administrator to check that your user is provisioned in this "
                + "environment."));
        }

        var warnings = new List<string>();
        var entity = new Entity(ProjectEntity);
        entity[ProjectNameAttribute] = name;
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            entity[ProjectDescriptionAttribute] = request.Description.Trim();
        }

        // Task 100: Project Type (optional) — verified to exist, else dropped with a warning; never a rejection.
        await ApplyReferenceAsync(entity, ProjectType, request.ProjectTypeId, warnings, ct).ConfigureAwait(false);

        await ApplyBusinessUnitDefaultsAsync(entity, ownerId, "project", warnings, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.SourceEntityLogicalName)
            && request.SourceRecordId is { } sourceRecordId
            && sourceRecordId != Guid.Empty)
        {
            await ApplyFieldMappingsAsync(
                    entity, request.SourceEntityLogicalName.Trim(), sourceRecordId, ProjectProtectedAttributes, warnings, ct)
                .ConfigureAwait(false);

            KeepRequestedNameIfMappingBlankedIt(entity, ProjectNameAttribute, name, "project", warnings);
        }

        // Owner A7 (task 152): the project is FOR its maker — after field mapping, so a mapped value is kept — unless
        // the request names the assignee (task 100), which wins.
        await ApplyAssignedInternalAsync(entity, request.AssignedToContactId, ownerId, ct).ConfigureAwait(false);

        // Set LAST, after mapping, so nothing can overwrite it — owner attribution is load-bearing.
        // Task 080: the owner is the caller's business-unit default owner TEAM, not the caller.
        if (await ResolveOwnerTeamAsync(ownerId, ct).ConfigureAwait(false) is not { } ownerTeamId)
        {
            return OwnerTeamUnresolved("project");
        }

        entity[OwnerAttribute] = new EntityReference(TeamEntity, ownerTeamId);

        // Task 133 (owner round 7 item 2): the app-only create's person, as for Matter.
        Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.Stamp(entity, ownerId);

        // Task 158 r1 (owner rounds 6 + 31): a project the field mapping filed under a SECURE matter or project (its
        // polymorphic pair) is created INTO isolation — owned by the named Secure Record Owners team, flagged in the create,
        // its sprk_createdbyperson the caller — never as an ordinary row of the caller's business unit first. Decided before
        // any write: an unreadable parent flag, or a caller walled off (or not checkable against) the No Access list of a
        // secure parent or of the record itself, refuses with nothing created; then the caller's own rights (G5).
        var plan = await _rootFiling.PlanCreateAsync(
                ProjectEntity, entity.Attributes.Select(a => new KeyValuePair<string, object?>(a.Key, a.Value)), ownerId, ct,
                CallerMayCreateUnderAsync)
            .ConfigureAwait(false);
        if (plan.Refusal is { } rootRefusal)
        {
            return RecordCreationResult.Failed(new RecordCreationFailure(
                KindFor(rootRefusal.RefusalCode),
                rootRefusal.RefusalCode ?? "record_owner_parent_undetermined",
                $"The project was not created: {rootRefusal.Reason}."));
        }

        if (plan.Isolated)
            IsolateProjectCreate(entity, plan);

        if (await CreateNumberedAsync(entity, "project", ct).ConfigureAwait(false) is not { } createdId)
        {
            return NumberUnavailable("project");
        }

        await WarnIfNumberMissingAsync(ProjectEntity, createdId, ProjectNumberAttribute, "project", warnings, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[RECORD-CREATE] Project {ProjectId} created for caller {CallerUserId}: owner team={OwnerTeamId}, isolated={Isolated}, " +
            "warnings={WarningCount}",
            createdId, request.CallerUserId, plan.Isolated ? plan.SecureOwnerTeamId : ownerTeamId, plan.Isolated, warnings.Count);

        if (plan.Isolated)
        {
            // Completed now through provisioning's own re-entry steps: the maker's share (read back), its own container, its
            // parents' sharees. A maker share that fails deletes the project again — nothing is left behind.
            var secured = await _rootFiling.CompleteIsolatedCreateAsync(ProjectEntity, createdId, ownerId, request.CallerUserId)
                .ConfigureAwait(false);
            if (secured.RowRemoved)
            {
                return RecordCreationResult.Failed(new RecordCreationFailure(
                    RecordCreationFailureKind.SecureFilingFailed,
                    secured.ReasonCode ?? "sdap.inherit.unexpected_result",
                    "The project is filed under a secure record, so it is created secure and shared to you, and that share could " +
                    "not be made, so the new project was removed again. Nothing was created. " +
                    // Task 158 r1c-v2 (round 47 item 4): one sentence of its own — never provisioning's ProblemDetails text
                    // appended (it ends in its own period and says "Nothing was changed." beside "Nothing was created.").
                    (secured.CompletesAutomatically
                        ? "Try again in a few minutes."
                        : $"Securing it was refused ({secured.ReasonCode}), and trying again will not change that: an " +
                          "administrator needs to review your access to the secure record it would be filed under.")));
            }

            // Task 158 r1c-v1 (verifier item 7): a self-heal is promised only when the job can deliver it — never after
            // provisioning REFUSED the maker (e.g. walled off between the plan and the provisioning): every run refuses again.
            if (secured.RowStranded)
            {
                // Not shared to the maker and not removable: it exists, and only an administrator can open it — never
                // reported as "shared to you".
                warnings.Add(
                    "The project was created as a secure record, but it could not be shared to you and could not be removed " +
                    $"again ({secured.ReasonCode}); " +
                    (secured.CompletesAutomatically
                        ? "it is shared to you automatically once that step succeeds (it is retried every few minutes)."
                        : "it will not be shared to you automatically — only an administrator can open it, and an administrator " +
                          "needs to review and remove it."));
            }
            else if (!secured.IsComplete)
            {
                // Shared to the maker: provisioning got past its refusals (all come before the creator's share), so what is
                // left is a fault the job retries.
                warnings.Add(
                    "The project was created as a secure record shared to you, but securing it could not be finished yet " +
                    $"({secured.ReasonCode}); it is completed automatically within a few minutes.");
            }
        }

        await MaterializeAssignedAccessAsync(ProjectEntity, createdId, request.CallerUserId, ct).ConfigureAwait(false);

        return new RecordCreationResult
        {
            RecordId = createdId,
            LogicalName = ProjectEntity,
            Name = (string)entity[ProjectNameAttribute],
            Warnings = warnings
        };
    }

    /// <summary>
    /// Task 158 r1 (owner round 31 item 2, G5): AS THE CALLER, before a project is created INTO isolation — Create on
    /// <c>sprk_project</c> and AppendTo on every secure parent it will be filed under, through the OBO probe on the request's
    /// own bearer token. Asked by the plan once the secure parents are known and before anything else is said about them
    /// (their No Access state); the app-only create then grants nothing the caller lacks. Any "could not answer" refuses.
    /// <c>null</c>: allowed.
    /// </summary>
    private async Task<Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution?> CallerMayCreateUnderAsync(
        IReadOnlyList<Sprk.Bff.Api.Services.Access.SecureFilingParent> parents, CancellationToken ct)
    {
        var token = _http?.HttpContext is { } context
            ? Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(context)
            : null;
        if (_callerAccess is null || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError(
                "[RECORD-CREATE] A project filed under a secure record cannot be checked against the caller's own rights here " +
                "(probe or token unavailable). Refused (fail closed).");
            return Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Refused(CallerRightsUnverifiable,
                "your permission to create a project under the secure record could not be checked");
        }

        if (!await _callerAccess.CallerHoldsPrivilegeAsync(token, ProjectCreatePrivilege, ct).ConfigureAwait(false))
        {
            return Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Refused(CallerCannotCreate,
                "you do not have permission to create projects");
        }

        foreach (var parent in parents)
        {
            var rights = await _callerAccess.GetCallerRightsAsync(
                    token, Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.EntitySetFor(parent.Table), parent.Id, ct)
                .ConfigureAwait(false);
            if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.AppendTo))
            {
                return Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Refused(CallerCannotFileUnderParent,
                    "you do not have permission to file a project under the secure record it names");
            }
        }

        return null;
    }

    /// <summary>
    /// Task 158 r1 (owner round 31 item 2): a project — a ROOT — filed under a secure record is created INTO isolation: owned
    /// by the named Secure Record Owners team provisioning's own topology names (the plan's), flagged in the create itself.
    /// A root's own ownership is provisioning's (task 144); its re-entry steps complete it after the create.
    /// </summary>
    private static void IsolateProjectCreate(Entity entity, Sprk.Bff.Api.Services.Access.SecureRootCreatePlan plan)
    {
        entity[OwnerAttribute] = new EntityReference(TeamEntity, plan.SecureOwnerTeamId!.Value);
        entity[SecureFlagAttribute] = true;
    }

    /// <summary>Task 158 r1: the caller lacks Create on <c>sprk_project</c> (G5). 403.</summary>
    internal const string CallerCannotCreate = "caller_cannot_create";

    /// <summary>Task 158 r1: the caller lacks AppendTo on a secure parent (G5). 403.</summary>
    internal const string CallerCannotFileUnderParent = "caller_cannot_file_under_parent";

    /// <summary>Task 158 r1: the caller's own rights could not be checked. 500.</summary>
    internal const string CallerRightsUnverifiable = "caller_rights_unverifiable";

    /// <summary>The failure kind of a planning refusal: a walled or refused caller is 403, an unverifiable check 500.</summary>
    private static RecordCreationFailureKind KindFor(string? code) => code switch
    {
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccess => RecordCreationFailureKind.SecureFilingRefused,
        CallerCannotCreate or CallerCannotFileUnderParent => RecordCreationFailureKind.SecureFilingRefused,
        CallerRightsUnverifiable => RecordCreationFailureKind.SecureFilingFailed,
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable => RecordCreationFailureKind.SecureFilingFailed,
        Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.SecureOwnerTeamUnresolved => RecordCreationFailureKind.SecureFilingFailed,
        _ => RecordCreationFailureKind.OwnerUnresolved,
    };

    /// <summary>
    /// A reference lookup the create form supplies by id: where it is written, the table it points at, and the words
    /// its warnings use. <paramref name="Label"/> names the selection ("matter type"), <paramref name="Noun"/> the
    /// missing value ("type"), <paramref name="EntityLabel"/> the record being created ("matter").
    /// </summary>
    private sealed record ReferenceLookup(
        string Attribute, string TargetEntity, string TargetIdAttribute, string Label, string Noun, string EntityLabel);

    private static readonly ReferenceLookup MatterType =
        new(MatterTypeAttribute, MatterTypeEntity, MatterTypeIdAttribute, "matter type", "type", "matter");

    private static readonly ReferenceLookup PracticeArea =
        new(MatterPracticeAreaAttribute, PracticeAreaEntity, PracticeAreaIdAttribute, "practice area", "practice area", "matter");

    private static readonly ReferenceLookup ProjectType =
        new(ProjectTypeAttribute, ProjectTypeEntity, ProjectTypeIdAttribute, "project type", "project type", "project");

    /// <summary>
    /// Sets <paramref name="lookup"/> to <paramref name="requestedId"/> once it is verified to exist. A missing or
    /// <see cref="Guid.Empty"/> id is a silent no-op; an unknown or unverifiable one is dropped with the warning from
    /// <see cref="CheckReferenceAsync"/>. Never a rejection (task 100, same posture as the matter type).
    /// </summary>
    private async Task ApplyReferenceAsync(
        Entity entity, ReferenceLookup lookup, Guid? requestedId, List<string> warnings, CancellationToken ct)
    {
        if (requestedId is not { } id || id == Guid.Empty)
        {
            return;
        }

        if (await CheckReferenceAsync(lookup, id, ct).ConfigureAwait(false) is { } warning)
        {
            warnings.Add(warning);
            return;
        }

        entity[lookup.Attribute] = new EntityReference(lookup.TargetEntity, id);
    }

    /// <summary>
    /// The one existence read for a supplied reference id — the matter type (owner decision 2026-09-11, "do not
    /// reject"; project CLAUDE.md Decisions), and since task 100 the practice area and project type. Returns
    /// <see langword="null"/> when the row exists — the lookup is then set. Otherwise returns the warning to report,
    /// and the record is created WITHOUT the lookup:    /// <list type="bullet">
    ///   <item><description>not found → "…was not found…";</description></item>
    ///   <item><description>the read itself failed (any other Dataverse fault) → a distinct "…could not be checked…".</description></item>
    /// </list>
    /// Neither is ever a 400 or a 500: the value is optional to the server, and a lookup that is not verified to exist
    /// would, if it dangled, fault the whole create.
    /// </summary>
    private async Task<string?> CheckReferenceAsync(ReferenceLookup lookup, Guid id, CancellationToken ct)
    {
        try
        {
            await _entities
                .RetrieveAsync(lookup.TargetEntity, id, [lookup.TargetIdAttribute], ct)
                .ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFound(ex))
        {
            _logger.LogWarning(
                "[RECORD-CREATE] {Reference} {ReferenceId} does not exist; creating the {EntityLabel} without it.",
                lookup.Label, id, lookup.EntityLabel);
            return $"The selected {lookup.Label} was not found, so the {lookup.EntityLabel} was created without a "
                   + $"{lookup.Noun}. Open the {lookup.EntityLabel} and set its {lookup.Noun}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] {Reference} {ReferenceId} could not be checked; creating the {EntityLabel} without it "
                + "rather than risk a dangling lookup failing the create.",
                lookup.Label, id, lookup.EntityLabel);
            return $"The selected {lookup.Label} could not be checked just now, so the {lookup.EntityLabel} was created "
                   + $"without a {lookup.Noun}. Open the {lookup.EntityLabel} and set its {lookup.Noun}.";
        }
    }

    /// <summary>
    /// A mapping rule may replace the name (client semantics), but may not leave the record without one — a
    /// Template whose placeholders all resolve empty, or a non-text value, reverts to the name the user entered.
    /// </summary>
    /// <param name="nameAttribute">The record's name attribute (<c>sprk_mattername</c> / <c>sprk_projectname</c>).</param>
    /// <param name="entityLabel">Lower-case noun for the user-facing warning ("matter" / "project").</param>
    private static void KeepRequestedNameIfMappingBlankedIt(
        Entity entity, string nameAttribute, string requestedName, string entityLabel, List<string> warnings)
    {
        if (entity.Attributes.TryGetValue(nameAttribute, out var value)
            && value is string mappedName
            && !string.IsNullOrWhiteSpace(mappedName))
        {
            return;
        }

        entity[nameAttribute] = requestedName;
        warnings.Add(
            $"A field-mapping rule produced an empty or non-text {entityLabel} name, so the name you entered was kept.");
    }

    /// <summary>
    /// The matter type the record will carry. A mapping rule that wrote something other than a
    /// <c>sprk_mattertype_ref</c> reference into <c>sprk_mattertype</c> is reverted (to the requested type, if any)
    /// rather than being sent to Dataverse, where it would fail the whole create.
    /// </summary>
    private static EntityReference? ResolveFinalMatterType(
        Entity entity,
        EntityReference? requestedType,
        List<string> warnings)
    {
        if (!entity.Attributes.TryGetValue(MatterTypeAttribute, out var value) || value is null)
        {
            return null;
        }

        if (value is EntityReference reference
            && reference.Id != Guid.Empty
            && string.Equals(reference.LogicalName, MatterTypeEntity, StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        warnings.Add("A field-mapping rule set the matter type to a value that is not a matter type, so it was not applied.");
        if (requestedType is not null)
        {
            entity[MatterTypeAttribute] = requestedType;
            return requestedType;
        }

        entity.Attributes.Remove(MatterTypeAttribute);
        return null;
    }

    // ------------------------------------------------------------------------------------------------------
    // Business-unit defaults
    // ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server-side <c>EntityCreationService.applyUserBuDefaults</c> + <c>resolveUserBuDefaults</c>: the owner's
    /// business unit → <c>sprk_searchindexname</c> (INV-5: an explicit value wins) and the
    /// <c>sprk_ai_search_index</c> lookup.
    /// </summary>
    /// <remarks>
    /// Non-fatal by design. These are search-index ROUTING hints, not a storage location or an access boundary,
    /// and <c>SearchIndexNameResolver</c> already walks the record's owning business unit at index time — which,
    /// with <c>ownerid</c> = the caller, is this same business unit. A failed read therefore degrades to that
    /// fallback rather than blocking the create.
    /// </remarks>
    /// <param name="entityLabel">Lower-case noun for the user-facing warning ("matter" / "project").</param>
    private async Task ApplyBusinessUnitDefaultsAsync(
        Entity entity,
        Guid ownerId,
        string entityLabel,
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            var user = await _entities
                .RetrieveAsync(SystemUserEntity, ownerId, [UserBusinessUnitAttribute], ct)
                .ConfigureAwait(false);

            if (user.GetAttributeValue<EntityReference>(UserBusinessUnitAttribute) is not { Id: var buId }
                || buId == Guid.Empty)
            {
                _logger.LogInformation(
                    "[RECORD-CREATE] Owner {OwnerId} has no business unit; no BU defaults applied.", ownerId);
                return;
            }

            var businessUnit = await _entities
                .RetrieveAsync(BusinessUnitEntity, buId, [SearchIndexNameAttribute, SearchIndexLookupAttribute], ct)
                .ConfigureAwait(false);

            var indexName = businessUnit.GetAttributeValue<string>(SearchIndexNameAttribute)?.Trim();
            if (!string.IsNullOrEmpty(indexName) && !HasExplicitValue(entity, SearchIndexNameAttribute))
            {
                entity[SearchIndexNameAttribute] = indexName;
            }

            if (businessUnit.GetAttributeValue<EntityReference>(SearchIndexLookupAttribute) is { Id: var indexId }
                && indexId != Guid.Empty
                && !HasExplicitValue(entity, SearchIndexLookupAttribute))
            {
                entity[SearchIndexLookupAttribute] = new EntityReference(SearchIndexEntity, indexId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] Business-unit defaults for owner {OwnerId} could not be read; the {EntityLabel} is "
                + "created without them and index routing falls back to its owning business unit.",
                ownerId, entityLabel);
            warnings.Add(
                "Business-unit search defaults could not be read, so they were not applied. Document indexing for "
                + $"this {entityLabel} falls back to its business unit's settings.");
        }
    }

    private static bool HasExplicitValue(Entity entity, string attribute)
        => entity.Attributes.TryGetValue(attribute, out var value)
           && value is not null
           && !(value is string text && string.IsNullOrWhiteSpace(text));

    // ------------------------------------------------------------------------------------------------------
    // Field Mapping Framework (create-time apply) — the I/O half; rule application is CreateTimeFieldMapping.
    // ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server-side <c>FieldMappingService.applyFieldMappings</c>: exactly one profile read (the existing
    /// <see cref="IFieldMappingDataverseService.GetFieldMappingProfileWithRulesAsync"/> reader, reused), at most one
    /// source read, then <see cref="CreateTimeFieldMapping.Apply"/>. Every failure is a warning — never a throw.
    /// No <c>source == target</c> guard (same-entity mapping is supported by the framework).
    /// </summary>
    private async Task ApplyFieldMappingsAsync(
        Entity target,
        string sourceEntity,
        Guid sourceRecordId,
        IReadOnlySet<string> protectedAttributes,
        List<string> warnings,
        CancellationToken ct)
    {
        FieldMappingProfileEntity? profile;
        try
        {
            profile = await _fieldMappings
                .GetFieldMappingProfileWithRulesAsync(sourceEntity, target.LogicalName, activeRulesOnly: true, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] Field-mapping profile lookup {Source} -> {Target} failed; treated as no profile.",
                sourceEntity, target.LogicalName);
            warnings.Add("Field-mapping configuration could not be read, so no fields were copied from the related record.");
            return;
        }

        if (profile is null)
        {
            // No profile for this pair: the graceful no-op (no warning), same as the client engine's 404.
            return;
        }

        var rules = profile.Rules ?? [];
        if (rules.Count == 0)
        {
            warnings.Add($"Field-mapping profile \"{profile.Name}\" has no rules; nothing was applied.");
            return;
        }

        // One source read spanning every Copy field and every Concat/Template placeholder — never one per rule.
        var sourceColumns = CreateTimeFieldMapping.CollectSourceColumns(rules);
        Entity? source = null;
        if (sourceColumns.Count > 0)
        {
            try
            {
                source = await _entities
                    .RetrieveAsync(sourceEntity, sourceRecordId, sourceColumns.ToArray(), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[RECORD-CREATE] Source record {Source}({SourceId}) could not be read for field mapping.",
                    sourceEntity, sourceRecordId);
                warnings.Add(
                    "The related record could not be read, so its Copy/Concat/Template field mappings were skipped.");
            }
        }

        CreateTimeFieldMapping.Apply(rules, target, source, protectedAttributes, warnings, _logger);
    }

    private static bool TryParseGuid(string? value, out Guid id)
        => Guid.TryParse(value?.Trim(), out id) && id != Guid.Empty;
}
