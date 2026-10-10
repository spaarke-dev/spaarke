using System.Collections.Frozen;
using System.Globalization;
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// One evidence reference on a Signal (<c>sprk_signal.sprk_evidencerefs</c>, spec FR-03/FR-14; schema-draft.md
/// §1). <c>Tier</c> drives epistemic rendering downstream (<c>Fact</c> flat, <c>Observation</c> hedged) — this
/// writer does not interpret it.
/// </summary>
/// <param name="Kind">What the reference points at, e.g. <c>"snapshot"</c>, <c>"communication"</c>.</param>
/// <param name="Ref">A typed reference string, e.g. <c>"sprk_communication:{guid}"</c>.</param>
/// <param name="Tier"><c>"Fact"</c> or <c>"Observation"</c>.</param>
/// <param name="Confidence">Only meaningful (and typically present) for <c>Observation</c>-tier refs.</param>
public sealed record SignalEvidenceRef(string Kind, string Ref, string Tier, double? Confidence = null);

/// <summary>
/// Everything <see cref="SignalWriter.WriteAsync"/> needs to write ONE <c>sprk_signal</c> row (task 030; spec
/// FR-03, FR-14; design.md CM-9). The caller (the evaluator, task 031/032 — not yet built) is responsible for
/// having already evaluated the predicate; this type carries only what gets WRITTEN.
/// </summary>
/// <param name="PolicyId">The firing policy (<c>sprk_signal.sprk_policy</c>).</param>
/// <param name="PolicyCode">Denormalized onto the Signal (<c>sprk_policy.sprk_policycode</c>) and part of the
/// dedupe key — survives policy deletion and renders without a join. Max 50 chars (schema limit).</param>
/// <param name="PolicyVersionId">The immutable version that fired (<c>sprk_signal.sprk_policyversion</c>).</param>
/// <param name="SubjectEntityLogicalName">The subject's Dataverse logical name, e.g. <c>sprk_matter</c> or
/// <c>sprk_communication</c>. MUST be one of <see cref="SignalWriter.RegardingLookupByEntity"/>'s 9 verified
/// keys.</param>
/// <param name="SubjectId">The subject record's id.</param>
/// <param name="SubjectDisplayName">For the denormalized <c>sprk_regardingrecordname</c> — display only.</param>
/// <param name="ShortHeadline">The Signal's <c>sprk_name</c> (required, NOT NULL). Max 200 chars (schema
/// limit) — prototype finding 14: the worklist row cannot show a full sentence, so this is the short headline,
/// not <see cref="MessageTemplate"/>'s rendering.</param>
/// <param name="Lane"><see cref="SignalWriter.LaneDecide"/> or <see cref="SignalWriter.LaneDo"/>.</param>
/// <param name="Severity">One of <see cref="SignalWriter.SeverityInfo"/> / <see cref="SignalWriter.SeverityWarning"/>
/// / <see cref="SignalWriter.SeverityCritical"/>, or <c>null</c> (the column is optional on the live schema).</param>
/// <param name="MessageTemplate">The firing policy version's <c>sprk_messagetemplate</c>. Tokens of the shape
/// <c>{{fieldName}}</c> are substituted from <paramref name="FactValues"/>; see
/// <see cref="SignalWriter.RenderSentence"/> for the binding §0.3 rule this enforces. The RENDERED result must
/// fit <c>sprk_sentence</c>'s 2000-character limit.</param>
/// <param name="FactValues">The detection-time facts the predicate actually read — becomes
/// <c>sprk_factsnapshot</c> verbatim (JSON) AND is the only source <see cref="MessageTemplate"/> may draw on.
/// A null value for a referenced key is refused (§0.3 — a null fact is not evidence). <b>What the evaluator
/// (task 031) puts here for a template token</b> — every token is in
/// <see cref="CompiledPredicate.TemplateEligibleFields"/> (enforced at validation, task 022 rework round 2,
/// finding F3), and its value is: for a field read by the subject's own <c>when</c> filter, the firing
/// subject's own value of that field (which may be null, e.g. under a <c>&lt;&gt;</c> filter — a null fact is
/// refused at render, round 3 finding L4); for a field read by an <c>exists</c> clause, <b>the clause's pinned
/// literal</b> (the one value the clause's <c>eq</c> / single-element <c>in</c> condition fixes it to). For a
/// number, boolean or id pin every matching related row carries exactly that value. <b>For a string pin, only
/// up to collation</b> (round 3 finding L3): Dataverse string <c>eq</c> is case- and accent-insensitive, so a
/// matching row may store "FEE" where the clause pinned "Fee" — the pinned literal is what the predicate
/// TESTED, and is the value to render; the row's stored spelling may differ. Never a value from a
/// <c>notExists</c> clause, and never a value picked from one of several matching related rows.</param>
/// <param name="EvidenceRefs">Optional typed evidence refs — becomes <c>sprk_evidencerefs</c> (JSON array,
/// <c>[]</c> when omitted).</param>
public sealed record SignalWriteRequest(
    Guid PolicyId,
    string PolicyCode,
    Guid PolicyVersionId,
    string SubjectEntityLogicalName,
    Guid SubjectId,
    string? SubjectDisplayName,
    string ShortHeadline,
    int Lane,
    int? Severity,
    string MessageTemplate,
    IReadOnlyDictionary<string, object?> FactValues,
    IReadOnlyList<SignalEvidenceRef>? EvidenceRefs = null);

/// <summary>The result of one <see cref="SignalWriter.WriteAsync"/> call.</summary>
/// <param name="SignalId">The (possibly pre-existing) <c>sprk_signal</c> row id.</param>
/// <param name="Created"><c>true</c> on first detection; <c>false</c> when re-evaluation matched an existing
/// row's dedupe key and only <c>sprk_lastevaluated</c> was refreshed (CM-9's create-then-reconcile contract).</param>
/// <param name="GroupingMatterId">The Signal's <c>sprk_matter</c>: the core record when it is a matter, otherwise
/// <see cref="Guid.Empty"/> (a project, work assignment, service request or no core record, task 037/D-36).</param>
/// <param name="OwningBusinessUnitId">The business unit <c>sprk_signal.owningbusinessunit</c> was set to at
/// create (FR-14), or, for a Secure-team-owned create, the business unit read back from the row (it derives from
/// the team). <c>Guid.Empty</c> on a re-evaluation update, where ownership is not touched, and on a skip.</param>
public sealed record SignalWriteResult(Guid SignalId, bool Created, Guid GroupingMatterId, Guid OwningBusinessUnitId)
{
    /// <summary>Task 039 (D-33): the Secure Record Owners team that owns the row when the create set it as owner;
    /// <c>null</c> on the FR-14 path, on a re-evaluation update and on a skip.</summary>
    public Guid? SecureOwnerTeamId { get; init; }

    /// <summary>Task 037 (D-34/D-36): the table of the core record the Signal groups under (one of
    /// <c>CoreAncestorResolver.CoreRecordEntities</c>); <c>null</c> for a no-core Do item (D-35).</summary>
    public string? CoreRecordEntity { get; init; }

    /// <summary>Task 037: the core record's id; <see cref="Guid.Empty"/> for a no-core item.</summary>
    public Guid CoreRecordId { get; init; }

    /// <summary>Task 039 (D-33): the <see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal"/> code when the
    /// ownership resolver refused and NOTHING was written; <c>null</c> otherwise.</summary>
    public string? SkippedRefusalCode { get; init; }

    /// <summary>True when the resolver refused and nothing was written (<see cref="SkippedRefusalCode"/>).</summary>
    public bool IsSkipped => SkippedRefusalCode is not null;
}

/// <summary>
/// Thrown when the writer's binding escalation trigger fires: a subject whose grouping matter (or that
/// matter's owning business unit, or the business-unit assignment itself) cannot be derived or verified. The
/// caller MUST stop rather than write a Signal with a null <c>sprk_matter</c> or leave it silently in the wrong
/// business unit (task 030 <c>&lt;escalation&gt;</c>; <c>security-roles.md</c> §4, extended by the task 030
/// rework's live findings F3/F22).
/// </summary>
public sealed class SignalWriterEscalationException : Exception
{
    /// <summary>One of <see cref="OntologyWriterFailureReason"/>'s bounded-cardinality constants — the same
    /// value logged on <see cref="OntologyWriterEvents.WriteRefused"/> and recorded on
    /// <see cref="OntologyWriterTelemetry.RecordFailure"/>, so a log query and a metric query always agree.</summary>
    public string Reason { get; }

    public SignalWriterEscalationException(string message, string reason) : base(message)
    {
        Reason = reason;
    }

    /// <summary>R1 (second independent review): wraps an underlying failure (e.g. the post-create
    /// owningbusinessunit read-back itself throwing) as an escalation, so it funnels through the SAME
    /// single logging/metering/rethrow point as every other escalation rather than needing a second one.</summary>
    public SignalWriterEscalationException(string message, string reason, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }
}

/// <summary>
/// Thrown when <see cref="SignalWriteRequest.MessageTemplate"/> cannot be rendered strictly from the
/// detection-time fact snapshot: a referenced token is absent, its value is null, or the rendered result still
/// contains an unsubstituted placeholder. Section 0.3 is binding: <c>sprk_sentence</c> may assert only what the
/// rule body actually evaluated.
/// </summary>
public sealed class SignalSentenceTemplateException : Exception
{
    public SignalSentenceTemplateException(string message) : base(message)
    {
    }
}

/// <summary>
/// Writes <c>sprk_signal</c> rows idempotently via the dedupe key, populates the subject through the ADR-024
/// polymorphic pattern, and sets ownership from the grouping matter's BUSINESS UNIT (task 030; spec FR-03,
/// FR-14, NFR-08 — rewritten per the task 030 rework's LIVE findings, 2026-10-04).
/// </summary>
/// <remarks>
/// <para><b>Ownership — team is REFUTED, owningbusinessunit is CONFIRMED (F3/F22, verified live as the writer
/// via MSCRMCallerID impersonation).</b> Setting <c>ownerid</c> to the grouping matter's default owner team
/// returns HTTP 403 <c>0x80040299</c> ("Read Privilege Check For Owner failed") for 59 of 62 live matters — team
/// ownership is unworkable, not merely undesirable. Setting <c>owningbusinessunit</c> directly (owner left as
/// the writer, the default) returns 204 and is READ by <c>Spaarke Console User</c> at Parent:Child BU depth
/// because the org has <c>EnableOwnershipAcrossBusinessUnits = true</c> — this is EXACTLY what FR-14 asks for
/// ("owner (or owning business unit) from its grouping matter"), using the alternative the requirement's own
/// wording names. The matter's BU is read via the SHARED sysadmin client (F25) — this is a metadata lookup, not
/// a Signal write, and does not need the writer's own identity. After CREATE, the row is read back and its
/// <c>owningbusinessunit</c> compared to the intended BU; a mismatch (e.g. an environment where
/// <c>EnableOwnershipAcrossBusinessUnits</c> is disabled) ESCALATES rather than leaving a Signal silently in the
/// wrong business unit. <b>Provisioning for a new environment must enable this org setting</b> — recorded here
/// and in the progress notes because it is now a deploy prerequisite, not an implementation detail.</para>
/// <para><b>Create-first, not Upsert (F1/F4/F5/F6).</b> <see cref="WriteAsync"/> always attempts
/// <see cref="OntologyWriterDataverseClient.CreateAsync"/> with the FULL payload (status, firstdetected,
/// owningbusinessunit, policyversion, factsnapshot, sentence, …) — Dataverse's NOT NULL constraints on this
/// table make a partial create impossible, which is why Upsert (a prior design) could not work: an Upsert
/// payload narrow enough to be safe on re-evaluation is too narrow to satisfy a first create. On
/// <c>sprk_dedupekey</c>'s unique-index conflict (<see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>,
/// the SAME classifier <c>CreateCommunicationRaceProofAsync</c> already uses for the identical fault), the
/// writer reconciles to the EXISTING row and updates ONLY <c>sprk_lastevaluated</c> — never re-sending
/// <c>sprk_signalstatus</c>, <c>sprk_firstdetected</c>, <c>owningbusinessunit</c>, <c>sprk_policyversion</c>,
/// <c>sprk_factsnapshot</c> or <c>sprk_sentence</c>. Those are DETECTION-TIME facts (the POML's own rationale):
/// re-sending them on every re-evaluation would silently re-open a Signal a human already resolved or dismissed,
/// and would re-write "what the data said" with whatever the data says NOW, defeating the whole reason
/// <c>sprk_factsnapshot</c> exists. <c>sprk_firstdetected</c> is therefore set ATOMICALLY in the same Create
/// call that sets everything else — no second round-trip, no window where a half-written row exists.
/// Re-fire-after-resolution semantics and <c>Superseded</c>-on-version-change belong to the EVALUATOR (task
/// 031), not this writer: this class's only job on a duplicate is "touch lastevaluated", never "decide whether
/// to reopen".</para>
/// <para><b>Mirrors, does not copy, <c>TodoRegardingBuilder</c>.</b> Same ADR-024 semantics (one specific
/// lookup + denormalized resolver fields, written atomically) but adapted to <c>sprk_signal</c>'s LIVE schema,
/// which differs from <c>sprk_todo</c> in one load-bearing way: <c>sprk_signal.sprk_regardingrecordtype</c> is
/// <c>NVARCHAR(100)</c> (plain text), NOT a lookup to <c>sprk_recordtype_ref</c> (verified via
/// <c>describe('tables/sprk_signal')</c>, 2026-10-04). This writer stores the subject's own logical name there
/// directly — simpler, and it is exactly the value spec FR-03 composes the dedupe key from
/// (<c>{policycode}|{regardingrecordtype}|{regardingrecordid}</c>), so there is no second representation to
/// keep in sync. The lookup's nav-property casing (<c>sprk_Matter</c> capitalized) is a Web-API-only concern —
/// irrelevant here because this writer goes through the SDK's typed <see cref="EntityReference"/>, which the
/// SDK resolves correctly regardless of case (F2, verified live and REFUTED as a risk to this code path).</para>
/// <para><b>Core record (task 037; D-34, D-36, D-37, D-35).</b> <see cref="VerifiedSubjects"/> is the closed set of
/// subjects the writer accepts (matter, communication, and the Do-lane event, To Do and work assignment); it is a
/// subset of <see cref="RegardingLookupByEntity"/>'s 9 schema keys. The grouping CORE record (any type
/// <c>CoreAncestorResolver</c> knows) comes only from <c>CoreAncestorResolver.ResolveStampsAsync</c>: the subject itself
/// for a core subject, its single stamp, or, for several stamps, the record the subject is directly filed under, then
/// matter over project, else a refusal. It is written as <c>sprk_corerecordtype</c> (catalog row) +
/// <c>sprk_corerecordid</c>, and to <c>sprk_matter</c> as well when the core is a matter. A Do-lane item with no core
/// record is owned by the subject's owner (D-35). The Signal's <c>sprk_duedate</c> copies the subject's date and is
/// refreshed on reconcile only while the Signal is Open or Acknowledged. Owning business unit (FR-14) is the core
/// record's.</para>
/// <para><b>§0.3 is enforced structurally (F8/F9) — here is EXACTLY what is checked, not a paraphrase.</b>
/// <see cref="RenderSentence"/> (1) requires every <c>{{token}}</c> in the template to be a KEY in the
/// detection-time fact snapshot, (2) requires that key's VALUE to be non-null (a null fact is not evidence —
/// rendering it blank would assert the field's absence as though it had been read), and (3) requires the fully
/// rendered string to contain NO remaining <c>{{…}}</c> placeholder. Any of the three failing throws
/// <see cref="SignalSentenceTemplateException"/> rather than writing a partially- or wrongly-rendered sentence.
/// <b>For 031</b>: <see cref="SignalWriteRequest.FactValues"/> must hold only values the predicate read (never a
/// superset gathered "just in case") — see that parameter's doc for exactly which value each token takes.
/// <see cref="PolicyVersionValidator"/> (task 022) refuses a template token outside
/// <see cref="CompiledPredicate.TemplateEligibleFields"/>, so a mismatch is caught when the policy version is
/// validated, not at the first evaluation that happens to exercise it.</para>
/// <para><b>Ids.</b> Every id written to a text resolver field is <c>Guid.ToString("D").ToLowerInvariant()</c> —
/// lowercase, no braces (the audit's §8.3 U1 finding: two divergent regexes were in circulation).</para>
/// <para><b>Secure records (task 039, owner D-33).</b> After the grouping matter is resolved, the uac-r2 ownership
/// resolver (<see cref="Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver"/>, invariant I-6) is asked with the
/// matter AND the subject as parents — task 146's shape for the spend-signal writer. A <b>Secure</b> answer (a parent
/// is owned in the Secure Record business unit) puts <c>ownerid</c> = the Secure Record Owners team in the create
/// itself, sends no <c>owningbusinessunit</c> (it derives from the team) and reads <c>owningteam</c> back. A <b>not
/// secure</b> answer keeps the FR-14 path above unchanged: a full I-6 switch to default-team ownership hits F3. A
/// <b>refusal</b> (for example a root flagged Secure but not isolated) writes nothing: it is logged at Warning
/// (<see cref="OntologyWriterEvents.WriteSkippedOwnerRefused"/>), metered as
/// <see cref="OntologyWriterFailureReason.OwnerRefused"/> and returned as a skipped result. The writer grants no
/// shares: uac-r2's 2-minute secure-child reconcile mirrors the root's sharees onto the row, but only once
/// <c>SecureChildLineage</c> lists <c>sprk_signal</c>. That entry, and the <c>prvReadsprk_Signal</c> entry in
/// <c>config/secure-record-owner-role.json</c>, are added by master PR #1390 (task 039), NOT by this branch.
/// <b>Whether the create itself succeeds does not depend on #1390</b>: it depends on the LIVE Secure Record Owner
/// role holding Read on <c>sprk_signal</c> in that environment (applied in dev directly by task 008; elsewhere by
/// <c>Set-SecureRecordOwnerRolePrivileges.ps1</c>, once #1390's config entry is present). Where the role lacks it, Dataverse refuses the
/// create with 403 <c>0x80040299</c> ("Read Privilege Check For Owner failed" — the refusal recorded for
/// <c>sprk_signal</c> by task 079, by Assign rather than create, and for <c>sprk_spendsignal</c> by uac-r2 task 146),
/// and this writer surfaces it as <see cref="OntologyWriterFailureReason.DataverseAccessDenied"/>. Where the role
/// holds it (spaarkedev1 since task 008; the create was proven live there on 2026-10-08) but #1390 is not deployed,
/// the row is Secure-team-owned and its sharees are never mirrored, so only the BFF can read it. Ship this writer
/// only after #1390 and the role edit are in the target environment.</para>
/// </remarks>
public sealed partial class SignalWriter
{
    // ── Option-set values, verified against describe('tables/sprk_signal'), 2026-10-04. ──────────────────────
    public const int LaneDecide = 100000000;
    public const int LaneDo = 100000001;
    public const int SeverityInfo = 100000000;
    public const int SeverityWarning = 100000001;
    public const int SeverityCritical = 100000002;
    private const int SignalStatusOpen = 100000000;
    private const int SignalStatusAcknowledged = 100000001;

    // ── Schema length limits (F21). ────────────────────────────────────────────────────────────────────────
    private const int PolicyCodeMaxLength = 50;
    private const int NameMaxLength = 200;
    private const int SentenceMaxLength = 2000;

    private static readonly FrozenSet<int> ValidLanes = new[] { LaneDecide, LaneDo }.ToFrozenSet();
    private static readonly FrozenSet<int> ValidSeverities =
        new[] { SeverityInfo, SeverityWarning, SeverityCritical }.ToFrozenSet();

    /// <summary>
    /// The 9 entity-specific regarding lookups on <c>sprk_signal</c> (verified live schema). ADR-024: AT MOST
    /// ONE is populated per row.
    /// </summary>
    public static readonly FrozenDictionary<string, string> RegardingLookupByEntity =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_communication"] = "sprk_regardingcommunication",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_servicerequest"] = "sprk_regardingservicerequest",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
            ["sprk_todo"] = "sprk_regardingtodo",
            ["sprk_event"] = "sprk_regardingevent",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_document"] = "sprk_regardingdocument",
        }.ToFrozenDictionary();

    /// <summary>
    /// The subject types the writer accepts: a CLOSED, VERIFIED set and deliberately a SUBSET of
    /// <see cref="RegardingLookupByEntity"/>'s keys. Task 037 (D-16/D-36) adds the three Do-lane subjects to matter and
    /// communication. <c>sprk_servicerequest</c> (and project, invoice, document) stay refused as a SUBJECT: no rule reads
    /// them in R1. Each subject's core record comes from <see cref="CoreAncestorResolver"/>, never from a lookup walk here.
    /// </summary>
    public static readonly FrozenSet<string> VerifiedSubjects = new[]
    {
        "sprk_matter", "sprk_communication", "sprk_event", "sprk_todo", "sprk_workassignment",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The subject column the Signal's <c>sprk_duedate</c> copies (task 037, D-27): an event's
    /// <c>sprk_duedate</c> (never <c>sprk_finalduedate</c>), a To Do's <c>sprk_duedate</c>, a work assignment's
    /// <c>sprk_responseduedate</c>. A subject type not listed carries no date.</summary>
    public static readonly FrozenDictionary<string, string> DueDateColumnBySubject =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sprk_event"] = "sprk_duedate",
            ["sprk_todo"] = "sprk_duedate",
            ["sprk_workassignment"] = "sprk_responseduedate",
        }.ToFrozenDictionary();

    private static readonly JsonSerializerOptions EvidenceRefJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OntologyWriterDataverseClient _writerClient;
    private readonly IGenericEntityService _sysadminClient;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalWriter> _logger;

    public SignalWriter(
        OntologyWriterDataverseClient writerClient,
        IGenericEntityService sysadminClient,
        CoreAncestorResolver coreAncestors,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        TimeProvider timeProvider,
        ILogger<SignalWriter> logger)
    {
        _writerClient = writerClient ?? throw new ArgumentNullException(nameof(writerClient));
        _sysadminClient = sysadminClient ?? throw new ArgumentNullException(nameof(sysadminClient));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Writes one <c>sprk_signal</c> row for <paramref name="request"/>. Re-running with the same
    /// (<c>PolicyCode</c>, subject) pair reconciles to the EXISTING row and refreshes only
    /// <c>sprk_lastevaluated</c> (CM-9's create-then-reconcile contract — see class remarks).
    /// </summary>
    /// <exception cref="ArgumentException">A required field is missing, exceeds its schema length limit, or
    /// <c>Lane</c>/<c>Severity</c> is not one of the live schema's option values.</exception>
    /// <exception cref="SignalWriterEscalationException">The grouping matter, its owning business unit, or the
    /// business-unit assignment itself cannot be derived or verified (the binding escalation trigger — see
    /// class remarks).</exception>
    /// <exception cref="SignalSentenceTemplateException"><paramref name="request"/>'s message template cannot
    /// be rendered strictly from the detection-time snapshot (§0.3).</exception>
    public async Task<SignalWriteResult> WriteAsync(SignalWriteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subjectEntity = RequireSubjectEntity(request.SubjectEntityLogicalName);

        if (request.SubjectId == Guid.Empty)
            throw new ArgumentException("SubjectId must not be Guid.Empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PolicyCode))
            throw new ArgumentException("PolicyCode is required (part of the dedupe key).", nameof(request));
        if (request.PolicyCode.Length > PolicyCodeMaxLength)
            throw new ArgumentException(
                $"PolicyCode is {request.PolicyCode.Length} characters; sprk_signal.sprk_policycode's limit is {PolicyCodeMaxLength}.",
                nameof(request));
        if (string.IsNullOrWhiteSpace(request.ShortHeadline))
            throw new ArgumentException("ShortHeadline is required (sprk_name is NOT NULL on sprk_signal).", nameof(request));
        if (request.ShortHeadline.Length > NameMaxLength)
            throw new ArgumentException(
                $"ShortHeadline is {request.ShortHeadline.Length} characters; sprk_signal.sprk_name's limit is {NameMaxLength}.",
                nameof(request));
        if (!ValidLanes.Contains(request.Lane))
            throw new ArgumentException($"Lane {request.Lane} is not a valid sprk_signal.sprk_lane option value.", nameof(request));
        if (request.Severity is { } sev && !ValidSeverities.Contains(sev))
            throw new ArgumentException($"Severity {sev} is not a valid sprk_signal.sprk_severity option value.", nameof(request));

        var cleanSubjectId = request.SubjectId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();

        try
        {
            // R2 (second independent review): rendering moved INSIDE the try so a SignalSentenceTemplateException
            // is caught by the dedicated clause below and logged/metered like every other refusal -- the owner's
            // no-silent-failure rule does not carve out an exception for a template authoring mistake.
            var sentence = RenderSentence(request.MessageTemplate, request.FactValues);
            if (sentence.Length > SentenceMaxLength)
            {
                throw new ArgumentException(
                    $"The rendered sentence is {sentence.Length} characters; sprk_signal.sprk_sentence's limit is {SentenceMaxLength}.",
                    nameof(request));
            }

            var core = await ResolveCoreAsync(subjectEntity, request.SubjectId, request.Lane, ct).ConfigureAwait(false);
            var matterId = core.Entity == "sprk_matter" ? core.Id : Guid.Empty;
            var dueDate = await ReadSubjectDueDateAsync(subjectEntity, request.SubjectId, ct).ConfigureAwait(false);

            Guid? secureTeamId = null;
            Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution? secureOwner = null;
            var owningBusinessUnitId = Guid.Empty;
            if (core.Entity is not null)
            {
                // Tasks 039/037 (D-33, D-36, D-38): the uac-r2 resolver decides whether the Signal is a secure child. The
                // CORE record (any core type) is the target and the subject a parent (secure-if-any). No skips. A refusal
                // writes nothing at all, not even a re-evaluation touch.
                var owner = await _ownership.ResolveOwnerAsync(
                    new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
                    {
                        TargetEntityLogicalName = core.Entity,
                        TargetRecordId = core.Id,
                        Parents = string.Equals(subjectEntity, core.Entity, StringComparison.Ordinal) && request.SubjectId == core.Id
                            ? Array.Empty<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent>()
                            : new[] { new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(subjectEntity, request.SubjectId) },
                    },
                    ct).ConfigureAwait(false);
                if (owner.IsRefused)
                {
                    _logger.LogWarning(OntologyWriterEvents.WriteSkippedOwnerRefused,
                        "Signal write SKIPPED for policy {PolicyCode}, subject {SubjectEntity} {SubjectId}, core record {CoreEntity} {CoreId}: " +
                        "no owner (reason={Reason}, code={RefusalCode}). Nothing was written.",
                        request.PolicyCode, subjectEntity, cleanSubjectId, core.Entity, core.Id,
                        OntologyWriterFailureReason.OwnerRefused, owner.RefusalCode);
                    OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.OwnerRefused);
                    return new SignalWriteResult(Guid.Empty, false, matterId, Guid.Empty)
                    {
                        SkippedRefusalCode = owner.RefusalCode ?? Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.NoOwnerSource,
                        CoreRecordEntity = core.Entity,
                        CoreRecordId = core.Id,
                    };
                }

                secureTeamId = owner.IsSecureOwner && owner.IsOwned ? owner.OwningTeamId : null;
                secureOwner = secureTeamId is null ? null : owner;
                // FR-14 path only: a Secure-team-owned row's business unit derives from the team and is never sent.
                if (secureTeamId is null)
                {
                    owningBusinessUnitId = await ResolveOwningBusinessUnitIdAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
                }
            }

            Guid? coreTypeRefId = core.Entity is null
                ? null
                : await ResolveRecordTypeRefIdAsync(core.Entity, ct).ConfigureAwait(false);

            var nowUtc = _timeProvider.GetUtcNow();
            var dedupeKey = BuildDedupeKey(request.PolicyCode, subjectEntity, cleanSubjectId);
            var entity = BuildEntity(request, subjectEntity, cleanSubjectId, core, coreTypeRefId, owningBusinessUnitId, secureOwner, dueDate, sentence, nowUtc);
            entity["sprk_dedupekey"] = dedupeKey;

            Guid signalId;
            bool created;
            try
            {
                signalId = await _writerClient.CreateAsync(entity, ct).ConfigureAwait(false);
                created = true;
            }
            catch (Exception ex) when (DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex))
            {
                // Re-evaluation: another run (this one or a concurrent one) already created this (policy, subject)
                // pair. Reconcile to the EXISTING row and refresh ONLY sprk_lastevaluated -- see class remarks for
                // why nothing else is re-sent. This is also the concurrent-create-becomes-an-update path: whichever
                // writer loses the race lands here instead of throwing. NOT a failure -- never logged/metered as one.
                _logger.LogInformation(
                    "Signal create lost the dedupe race for policy {PolicyCode}, subject {SubjectEntity} {SubjectId} " +
                    "(alternate-key duplicate on sprk_dedupekey) -- reconciling to the existing row.",
                    request.PolicyCode, subjectEntity, cleanSubjectId);

                var keyAttributes = new KeyAttributeCollection { ["sprk_dedupekey"] = dedupeKey };
                Entity existing;
                try
                {
                    // R1 (second independent review): owningbusinessunit is selected in THIS SAME retrieve --
                    // no extra round trip -- because the wrong-BU row was otherwise never re-checked on
                    // reconcile (only on the original create). A Signal's BU can drift after its first create
                    // (e.g. the matter itself was reassigned to a different business unit).
                    existing = await _writerClient
                        .RetrieveByAlternateKeyAsync("sprk_signal", keyAttributes, new[] { "sprk_signalid", "owningbusinessunit", "owningteam", "ownerid", "sprk_signalstatus", "sprk_duedate" }, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception readEx)
                {
                    throw new SignalWriterEscalationException(
                        $"Signal dedupekey '{dedupeKey}' hit a duplicate-key conflict, but the existing row could " +
                        "not be read back to reconcile.",
                        OntologyWriterFailureReason.OwningBusinessUnitReadBackFailed, readEx);
                }

                signalId = existing.Id;
                EnsureOwnershipMatches(existing, secureTeamId, owningBusinessUnitId, core.NoCoreOwner, signalId);

                var reconcileFields = new Dictionary<string, object> { ["sprk_lastevaluated"] = nowUtc.UtcDateTime };

                // Task 037 (D-13): the Signal carries the date it last saw. Refreshed while the Signal is Open or
                // Acknowledged; NEVER touched once Resolved (or when the status is unreadable), because D-13's date-change
                // re-raise rule compares the subject's date with exactly this value.
                var existingStatus = existing.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value;
                if (existingStatus is SignalStatusOpen or SignalStatusAcknowledged
                    && existing.GetAttributeValue<DateTime?>("sprk_duedate")?.Date != dueDate)
                {
                    reconcileFields["sprk_duedate"] = dueDate is { } d ? d : null!;
                }

                await _writerClient.UpdateAsync("sprk_signal", signalId, reconcileFields, ct).ConfigureAwait(false);
                created = false;
            }

            var resultOwningBu = owningBusinessUnitId;
            if (created)
            {
                // F1/F3/F22: verify the business-unit assignment landed, rather than trusting the 204. An
                // environment without EnableOwnershipAcrossBusinessUnits enabled would otherwise silently leave the
                // Signal owned (and BU-scoped) by the writer's own business unit -- invisible to the matter's BU
                // users, exactly the exposure FR-14 exists to prevent.
                Entity verifyRow;
                try
                {
                    verifyRow = await _writerClient
                        .RetrieveAsync("sprk_signal", signalId, new[] { "owningbusinessunit", "owningteam", "ownerid" }, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception readEx)
                {
                    // R1 (second independent review): a read-back that THROWS after a successful create is its
                    // OWN failure mode -- distinct from a successful read that disagrees (OwningBusinessUnitMismatch
                    // below) -- and must be just as visible, not swallowed as an unrelated raw exception.
                    throw new SignalWriterEscalationException(
                        $"Signal {signalId:D} was created but its owningbusinessunit could not be read back to verify.",
                        OntologyWriterFailureReason.OwningBusinessUnitReadBackFailed, readEx);
                }

                EnsureOwnershipMatches(verifyRow, secureTeamId, owningBusinessUnitId, core.NoCoreOwner, signalId);
                if (secureTeamId is not null)
                {
                    resultOwningBu = verifyRow.GetAttributeValue<EntityReference>("owningbusinessunit")?.Id ?? Guid.Empty;
                }
            }
            else
            {
                // Ownership is NOT touched on a re-evaluation update -- report Guid.Empty rather than a value that
                // would wrongly imply this write just (re-)assigned it.
                resultOwningBu = Guid.Empty;
            }

            _logger.LogInformation(
                "Signal {SignalId} {Action} for policy {PolicyCode}, subject {SubjectEntity} {SubjectId}, core record {CoreEntity} {CoreId}.",
                signalId, created ? "created" : "reconciled (re-evaluation)", request.PolicyCode, subjectEntity, cleanSubjectId,
                core.Entity ?? "(none)", core.Id);

            return new SignalWriteResult(signalId, created, matterId, resultOwningBu)
            {
                SecureOwnerTeamId = created ? secureTeamId : null,
                CoreRecordEntity = core.Entity,
                CoreRecordId = core.Id,
            };
        }
        catch (SignalWriterEscalationException ex)
        {
            // Owner directive (task 030 rework): a refusal must be loudly visible, never indistinguishable
            // from "no conditions found". Logged once, here, at the single point every escalation funnels
            // through -- not at each throw site -- so the EventId + reason + rethrow discipline cannot drift
            // per call site. NEVER logs fact values or the rendered sentence (matter detail); only the reason
            // tag and the policy code.
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Signal writer refused for policy {PolicyCode} (reason={Reason}).", request.PolicyCode, ex.Reason);
            OntologyWriterTelemetry.RecordFailure(ex.Reason);
            throw;
        }
        catch (SignalSentenceTemplateException ex)
        {
            // R2 (second independent review): a refused write under the owner's no-silent-failure rule too --
            // logged and metered WITHOUT any rendered content (the exception's own message no longer carries
            // the rendered string; see RenderSentence's remarks).
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Signal writer refused for policy {PolicyCode} (reason={Reason}).",
                request.PolicyCode, OntologyWriterFailureReason.SentenceTemplateInvalid);
            OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.SentenceTemplateInvalid);
            throw;
        }
        catch (Exception ex) when (IsDataverseAccessDenied(ex))
        {
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Signal writer refused for policy {PolicyCode} (reason={Reason}).",
                request.PolicyCode, OntologyWriterFailureReason.DataverseAccessDenied);
            OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.DataverseAccessDenied);
            throw;
        }
    }

    /// <summary>
    /// True when <paramref name="ex"/> (or any inner exception) is a Dataverse privilege-denied fault — 403 /
    /// <c>0x80040220</c> (generic access-check failure) / <c>0x80040299</c> ("Read Privilege Check For Owner
    /// failed", the F3 team-ownership fault this writer no longer triggers but which a future misconfiguration
    /// could reproduce for a different privilege). Mirrors <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>'s
    /// typed-first, message-fallback classification style.
    /// </summary>
    private static bool IsDataverseAccessDenied(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fault
                && (fault.Detail?.ErrorCode == unchecked((int)0x80040220)
                    || fault.Detail?.ErrorCode == unchecked((int)0x80040299)))
            {
                return true;
            }

            var m = e.Message;
            if (!string.IsNullOrEmpty(m) && (
                m.Contains("0x80040220", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("0x80040299", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("does not have", StringComparison.OrdinalIgnoreCase) && m.Contains("right", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("Privilege Check", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>spec FR-03: <c>{policycode}|{regardingrecordtype}|{regardingrecordid}</c>.</summary>
    internal static string BuildDedupeKey(string policyCode, string subjectEntity, string cleanSubjectId) =>
        $"{policyCode}|{subjectEntity}|{cleanSubjectId}";

    /// <summary>The core record a Signal groups under, or, when <see cref="Entity"/> is null, the owner of a
    /// no-core Do item (D-35): a system user, or the team that owns it (12 of the 25 no-core To Dos in dev are
    /// team-owned).</summary>
    private sealed record CorePlan(string? Entity, Guid Id, EntityReference? NoCoreOwner);

    /// <summary>
    /// Derives the subject's CORE record (task 037; D-34, D-36, D-37, D-35) with <see cref="CoreAncestorResolver"/>, the
    /// only way the writer derives one. No code here names a core type: the resolver's taxonomy decides, so a core type
    /// added there and in the <c>sprk_recordtype_ref</c> catalog is grouped with no change here (D-36).
    /// </summary>
    private async Task<CorePlan> ResolveCoreAsync(string subjectEntity, Guid subjectId, int lane, CancellationToken ct)
    {
        if (!VerifiedSubjects.Contains(subjectEntity))
        {
            throw new SignalWriterEscalationException(
                $"Cannot group a Signal for subject type '{subjectEntity}': it is not a verified subject " +
                "(SignalWriter.VerifiedSubjects). Refusing to write a Signal with no derivable core record " +
                "(task 030/037 escalation trigger). Widening this set is a schema check plus a code change with review.",
                OntologyWriterFailureReason.MatterDerivationUnverified);
        }

        var result = await _coreAncestors.ResolveStampsAsync(subjectEntity, subjectId, ct).ConfigureAwait(false);
        switch (result.Status)
        {
            case CoreAncestorStatus.CoreTarget:
                // The subject IS a core record (a matter, or a work assignment, D-36): it groups under itself.
                return new CorePlan(result.Stamps[0].EntityType, result.Stamps[0].RecordId, null);

            case CoreAncestorStatus.Derived:
                var stamp = await PickStampAsync(subjectEntity, subjectId, result.Stamps, ct).ConfigureAwait(false);
                return new CorePlan(stamp.EntityType, stamp.RecordId, null);

            case CoreAncestorStatus.Error:
                throw new SignalWriterEscalationException(
                    $"Cannot derive the core record of {subjectEntity} {subjectId:D}: {result.Error}",
                    OntologyWriterFailureReason.CoreRecordUnresolved);

            default:
                return await NoCorePlanAsync(subjectEntity, subjectId, lane, ct).ConfigureAwait(false);
        }
    }

    /// <summary>D-37: more than one stamp. The record the subject is DIRECTLY filed under (its regarding pair names one of
    /// the stamps, the DirectRootLink case of <c>CoreAncestorResolver.ClassifyStampSource</c>) wins; then matter over
    /// project; any other remaining tie stops the write.</summary>
    private async Task<CoreAncestorStamp> PickStampAsync(
        string subjectEntity, Guid subjectId, IReadOnlyList<CoreAncestorStamp> stamps, CancellationToken ct)
    {
        if (stamps.Count == 1)
        {
            return stamps[0];
        }

        var row = await _sysadminClient
            .RetrieveAsync(subjectEntity, subjectId, new[] { CoreAncestorResolver.RegardingRecordIdColumn }, ct)
            .ConfigureAwait(false);
        if (Guid.TryParse(row.GetAttributeValue<string>(CoreAncestorResolver.RegardingRecordIdColumn)?.Trim(), out var pairId)
            && stamps.Where(s => s.RecordId == pairId).ToList() is [var direct])
        {
            return direct;
        }

        var remaining = stamps.ToList();
        if (remaining.Any(s => s.EntityType == "sprk_matter"))
        {
            remaining.RemoveAll(s => s.EntityType == "sprk_project");
        }

        if (remaining.Count == 1)
        {
            return remaining[0];
        }

        throw new SignalWriterEscalationException(
            $"{subjectEntity} {subjectId:D} names {stamps.Count} core records ({string.Join(", ", stamps.Select(s => s.EntityType))}) " +
            "and neither its direct filed-under record nor matter-over-project decides between them (D-37). Refusing to guess.",
            OntologyWriterFailureReason.CoreRecordAmbiguous);
    }

    /// <summary>D-35: no core record. Only a Do-lane item may have none; its Signal is owned by the subject's owner.</summary>
    private async Task<CorePlan> NoCorePlanAsync(string subjectEntity, Guid subjectId, int lane, CancellationToken ct)
    {
        if (lane != LaneDo)
        {
            throw new SignalWriterEscalationException(
                $"Cannot derive a core record for {subjectEntity} {subjectId:D}, and only a Do-lane item may have none (D-35). " +
                "Refusing to write a Decide-lane Signal that no reader could see.",
                OntologyWriterFailureReason.MatterLookupEmpty);
        }

        var row = await _sysadminClient.RetrieveAsync(subjectEntity, subjectId, new[] { "ownerid" }, ct).ConfigureAwait(false);
        var owner = row.GetAttributeValue<EntityReference>("ownerid");
        if (owner is null || owner.Id == Guid.Empty
            || owner.LogicalName is not ("systemuser" or "team"))
        {
            throw new SignalWriterEscalationException(
                $"{subjectEntity} {subjectId:D} has no core record and no readable owner (a system user or a team), so a " +
                "no-core Signal cannot be owned by it (D-35). Refusing to leave the Signal owned by the writer.",
                OntologyWriterFailureReason.NoCoreOwnerUnresolved);
        }

        return new CorePlan(null, Guid.Empty, new EntityReference(owner.LogicalName, owner.Id));
    }

    /// <summary>The subject's date the Signal carries (D-27). Read with the shared client: a read of the subject, not a
    /// Signal write.</summary>
    private async Task<DateTime?> ReadSubjectDueDateAsync(string subjectEntity, Guid subjectId, CancellationToken ct)
    {
        if (!DueDateColumnBySubject.TryGetValue(subjectEntity, out var column))
        {
            return null;
        }

        var row = await _sysadminClient.RetrieveAsync(subjectEntity, subjectId, new[] { column }, ct).ConfigureAwait(false);
        return row.GetAttributeValue<DateTime?>(column)?.Date;
    }

    /// <summary>D-36: the <c>sprk_recordtype_ref</c> catalog row for the core record's table (exactly one active row).</summary>
    private async Task<Guid> ResolveRecordTypeRefIdAsync(string coreEntity, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_recordtype_ref")
        {
            ColumnSet = new ColumnSet("sprk_recordtype_refid"),
            TopCount = 2,
        };
        query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, coreEntity);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var rows = await _sysadminClient.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (rows.Entities.Count != 1)
        {
            throw new SignalWriterEscalationException(
                $"The core record type '{coreEntity}' has {rows.Entities.Count} active sprk_recordtype_ref catalog rows " +
                "(expected exactly 1), so the Signal's sprk_corerecordtype cannot be written (D-36).",
                OntologyWriterFailureReason.CoreRecordTypeNotCataloged);
        }

        return rows.Entities[0].Id;
    }

    /// <summary>
    /// Reads the core record's <c>owningbusinessunit</c> via the SHARED sysadmin client (F25) — a metadata
    /// lookup, not a Signal write, so the writer's own identity is not needed for it.
    /// </summary>
    /// <remarks>
    /// Component justification (CLAUDE.md §11): <b>existing</b> — <c>IRecordOwnershipResolver</c> resolves a
    /// TEAM, and team ownership is CONFIRMED unworkable for 59 of 62 live matters (F3: HTTP 403
    /// <c>0x80040299</c>, "Read Privilege Check For Owner failed", verified as the writer via MSCRMCallerID
    /// impersonation); <b>extension</b> — no, once the destination is a business-unit id rather than a team id,
    /// the resolver's team-selection logic (default vs. named secure-record team, user-fallback chains,
    /// ambiguity refusal) does not apply; <b>cost of doing nothing</b> — FR-14 cannot be satisfied correctly:
    /// the Signal would stay owned (and BU-scoped) by the writer's own business unit, invisible to 59 of 62
    /// matters' own BU users.
    /// </remarks>
    private async Task<Guid> ResolveOwningBusinessUnitIdAsync(string coreEntity, Guid coreId, CancellationToken ct)
    {
        var matterRow = await _sysadminClient
            .RetrieveAsync(coreEntity, coreId, new[] { "owningbusinessunit" }, ct)
            .ConfigureAwait(false);
        var buRef = matterRow.GetAttributeValue<EntityReference>("owningbusinessunit");

        if (buRef is null || buRef.Id == Guid.Empty)
        {
            throw new SignalWriterEscalationException(
                $"Cannot resolve an owning business unit for core record {coreEntity} {coreId:D}: it has no " +
                "owningbusinessunit. Refusing to write a Signal owned by the service identity's own business " +
                "unit (FR-14 / security-roles.md §4).",
                OntologyWriterFailureReason.OwningBusinessUnitNotFound);
        }

        return buRef.Id;
    }

    /// <summary>
    /// R1 (second independent review): the ONE place that compares a Signal row's <c>owningbusinessunit</c>
    /// against the grouping matter's business unit — called from BOTH the post-create verify AND the
    /// reconcile (re-evaluation) path, so the wrong-BU case can no longer go unchecked on reconcile the way
    /// the first rework left it. Pure (no I/O): the caller has already read <paramref name="signalRow"/>.
    /// </summary>
    private static void EnsureOwningBusinessUnitMatches(Entity signalRow, Guid expectedBuId, Guid signalId)
    {
        var actualBuId = signalRow.GetAttributeValue<EntityReference>("owningbusinessunit")?.Id;
        if (actualBuId != expectedBuId)
        {
            throw new SignalWriterEscalationException(
                $"Signal {signalId:D}'s owningbusinessunit " +
                $"({(actualBuId is { } id ? id.ToString("D") : "null")}) does not match the grouping matter's " +
                $"business unit ({expectedBuId:D}). This environment may not have EnableOwnershipAcrossBusinessUnits " +
                "enabled, or the row's business unit drifted after it was created. Refusing to leave a Signal " +
                "silently in the wrong business unit (FR-14).",
                OntologyWriterFailureReason.OwningBusinessUnitMismatch);
        }
    }

    /// <summary>
    /// Task 039 (D-33): the ONE ownership check for both the post-create verify and the reconcile path. A row the
    /// resolver gave to the Secure Record Owners team is checked by <c>owningteam</c> (its business unit derives from
    /// the team, and the grouping matter may be ordinary while the subject is secure); every other row keeps the FR-14
    /// business-unit check (<see cref="EnsureOwningBusinessUnitMatches"/>). A row the reconciler has not yet moved into
    /// isolation (≤ 2 minutes after a root is made secure) escalates here, loudly, rather than being touched.
    /// </summary>
    private static void EnsureOwnershipMatches(
        Entity signalRow, Guid? secureTeamId, Guid expectedBuId, EntityReference? noCoreOwner, Guid signalId)
    {
        if (noCoreOwner is { } expectedOwner)
        {
            // Task 037 (D-35): a no-core Do item's Signal is owned by the subject's owner. Nobody else may see it.
            var actualOwner = signalRow.GetAttributeValue<EntityReference>("ownerid");
            if (actualOwner?.Id != expectedOwner.Id || !string.Equals(actualOwner.LogicalName, expectedOwner.LogicalName, StringComparison.Ordinal))
            {
                throw new SignalWriterEscalationException(
                    $"Signal {signalId:D}'s ownerid ({(actualOwner is null ? "null" : actualOwner.Id.ToString("D"))}) is not the " +
                    $"subject's owner ({expectedOwner.Id:D}). Refusing to leave a no-core Signal with another owner (D-35).",
                    OntologyWriterFailureReason.NoCoreOwnerMismatch);
            }

            return;
        }

        if (secureTeamId is not { } teamId)
        {
            EnsureOwningBusinessUnitMatches(signalRow, expectedBuId, signalId);
            return;
        }

        var actualTeamId = signalRow.GetAttributeValue<EntityReference>("owningteam")?.Id;
        if (actualTeamId != teamId)
        {
            throw new SignalWriterEscalationException(
                $"Signal {signalId:D}'s owningteam ({(actualTeamId is { } id ? id.ToString("D") : "null")}) is not " +
                $"the Secure Record Owners team ({teamId:D}) the ownership resolver chose. Refusing to leave a secure " +
                "record's Signal with another owner (D-33).",
                OntologyWriterFailureReason.SecureOwnerMismatch);
        }
    }

    private static Entity BuildEntity(
        SignalWriteRequest request,
        string subjectEntity,
        string cleanSubjectId,
        CorePlan core,
        Guid? coreTypeRefId,
        Guid owningBusinessUnitId,
        Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution? secureOwner,
        DateTime? dueDate,
        string sentence,
        DateTimeOffset nowUtc)
    {
        if (!RegardingLookupByEntity.TryGetValue(subjectEntity, out var specificLookup))
        {
            // Unreachable given VerifiedSubjects is a subset of RegardingLookupByEntity's keys
            // (asserted by SignalWriterTests) — guarded anyway so a future edit that breaks that invariant
            // fails loudly here instead of writing an un-trimmed row.
            throw new InvalidOperationException(
                $"'{subjectEntity}' is a verified subject but has no entry in RegardingLookupByEntity. " +
                "This is a code-configuration defect, not a data problem.");
        }

        var entity = new Entity("sprk_signal");

        entity[specificLookup] = new EntityReference(subjectEntity, request.SubjectId);
        entity["sprk_regardingrecordid"] = cleanSubjectId;
        // TEXT column on sprk_signal (verified live schema) — NOT a lookup to sprk_recordtype_ref, unlike
        // sprk_todo/sprk_communication. The subject's own logical name is exactly the value FR-03's dedupe
        // key needs, so there is nothing else to resolve here.
        entity["sprk_regardingrecordtype"] = subjectEntity;
        entity["sprk_regardingrecordname"] = request.SubjectDisplayName ?? string.Empty;
        entity["sprk_regardingrecordurl"] = BuildRecordUrl(subjectEntity, cleanSubjectId);

        if (core.Entity is not null)
        {
            // D-36: the generic core-record pair (catalog type + id); sprk_matter as well only when the core is a matter.
            entity["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", coreTypeRefId!.Value);
            entity["sprk_corerecordid"] = core.Id.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
            if (core.Entity == "sprk_matter")
            {
                entity["sprk_matter"] = new EntityReference("sprk_matter", core.Id);
            }
        }

        if (dueDate is { } due)
        {
            entity["sprk_duedate"] = due;
        }

        entity["sprk_policy"] = new EntityReference("sprk_policy", request.PolicyId);
        entity["sprk_policyversion"] = new EntityReference("sprk_policyversion", request.PolicyVersionId);
        entity["sprk_policycode"] = request.PolicyCode;

        entity["sprk_lane"] = new OptionSetValue(request.Lane);
        if (request.Severity is { } severity)
        {
            entity["sprk_severity"] = new OptionSetValue(severity);
        }

        entity["sprk_sentence"] = sentence;
        entity["sprk_factsnapshot"] = JsonSerializer.Serialize(request.FactValues);
        entity["sprk_evidencerefs"] = JsonSerializer.Serialize(
            request.EvidenceRefs ?? Array.Empty<SignalEvidenceRef>(), EvidenceRefJsonOptions);

        entity["sprk_name"] = request.ShortHeadline;
        entity["sprk_signalstatus"] = new OptionSetValue(SignalStatusOpen);
        entity["sprk_firstdetected"] = nowUtc.UtcDateTime;
        entity["sprk_lastevaluated"] = nowUtc.UtcDateTime;

        if (core.NoCoreOwner is { } noCoreOwner)
        {
            // Task 037 (D-35): no core record, so the subject's owner owns the Signal (the writer holds Assign), set IN the
            // create. No owningbusinessunit is sent: it derives from the owner.
            entity["ownerid"] = noCoreOwner;
            return entity;
        }

        if (secureOwner is not null)
        {
            // Task 039 (D-33): a secure child — owned by the Secure Record Owners team IN the create, written by uac-r2's
            // own ApplyTo (the one censused owner write; the writer holds Assign, the team's role holds Read on
            // sprk_signal). owningbusinessunit is NOT sent: it derives from the team.
            secureOwner.ApplyTo(entity);
            return entity;
        }

        // FR-14 (F3/F22): owner is left as the writer (the default — NOT set here); owningbusinessunit is set
        // to the grouping matter's BU. Never ownerid=<a BU default team> — confirmed unworkable live (403 0x80040299).
        entity["owningbusinessunit"] = new EntityReference("businessunit", owningBusinessUnitId);

        return entity;
    }

    /// <summary>
    /// Substitutes <c>{{token}}</c> placeholders in <paramref name="template"/> ONLY from
    /// <paramref name="factValues"/>. Exactly three things are checked (F8/F9 — see class remarks for why this
    /// list, not a paraphrase, is the contract): every token is a KEY present in the snapshot; that key's VALUE
    /// is non-null; and the fully rendered string contains no leftover <c>{{…}}</c>.
    /// </summary>
    /// <exception cref="SignalSentenceTemplateException">Any of the three checks fails.</exception>
    internal static string RenderSentence(string template, IReadOnlyDictionary<string, object?> factValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(factValues);

        if (HasMalformedPlaceholder(template))
        {
            // Task 022 review finding #3: checked on the TEMPLATE, not the rendered output -- a malformed
            // placeholder (unbalanced braces, an empty token, a disallowed character in the field name) is
            // never matched by SentenceToken() at all, so before this fix it passed straight through to the
            // final rendered sentence as literal text, unflagged. Checking the template (rather than what
            // comes out the other end) also avoids a false positive if some SUBSTITUTED fact value happens to
            // itself contain a literal brace character.
            throw new SignalSentenceTemplateException(
                "sprk_sentence template contains a malformed {{...}} placeholder (unbalanced braces, an empty " +
                "token, a character outside [A-Za-z0-9_.] in the field name, or a lone literal or full-width " +
                "brace -- braces are reserved for {{field}} placeholders) that would otherwise render as " +
                "literal text instead of substituting.");
        }

        var rendered = SentenceToken().Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            if (!factValues.TryGetValue(token, out var value))
            {
                throw new SignalSentenceTemplateException(
                    $"sprk_sentence template references '{{{{{token}}}}}', which is not a key in the " +
                    "detection-time fact snapshot. Section 0.3 is binding: the sentence may assert only what " +
                    "the predicate actually read. Add the field to the fact snapshot, or remove it from the " +
                    "template.");
            }

            if (value is null)
            {
                throw new SignalSentenceTemplateException(
                    $"sprk_sentence template references '{{{{{token}}}}}', whose fact value is null. A null " +
                    "fact is not evidence — rendering it as an empty string would assert the field's ABSENCE " +
                    "as though it had been positively read, which is not what the predicate found.");
            }

            return FormatFactValue(value);
        });

        if (SentenceToken().IsMatch(rendered))
        {
            // R2 (second independent review): the rendered string is NOT included in this message -- it is
            // fact-derived matter detail (exactly what the owner's no-silent-failure logging must NOT carry).
            throw new SignalSentenceTemplateException(
                "The rendered sentence still contains an unsubstituted placeholder after substitution. " +
                "Refusing to write a sentence that was not fully rendered.");
        }

        return rendered;
    }

    /// <summary>
    /// Extracts every <c>{{token}}</c> name <paramref name="template"/> references, WITHOUT requiring a fact
    /// snapshot to render against. Shared by task 022's <see cref="PolicyVersionValidator"/> so "what counts as
    /// a token" has exactly one definition in this codebase — <see cref="RenderSentence"/>'s own
    /// <see cref="SentenceToken"/> regex — rather than a second copy that could silently drift from it.
    /// </summary>
    internal static IReadOnlyList<string> ExtractTemplateTokens(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return SentenceToken().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToArray();
    }

    /// <summary>
    /// True when <paramref name="template"/> contains a <c>{{</c>/<c>}}</c>-shaped placeholder attempt that
    /// <see cref="SentenceToken"/> does NOT recognize as well-formed — e.g. a disallowed character in the
    /// field name (<c>{{sprk-x}}</c>, <c>{{a b}}</c>), an empty token (<c>{{}}</c>), an unclosed brace pair
    /// (<c>{{x</c>), or an extra brace around an otherwise-valid token (<c>{{{x}}}</c>). Task 022 review
    /// finding #3: all five previously passed both <see cref="PolicyVersionValidator"/> and
    /// <see cref="RenderSentence"/> unflagged and would have rendered with stray literal brace text.
    /// </summary>
    /// <remarks>
    /// <para>Checks for ANY leftover single <c>{</c> or <c>}</c> after removing every well-formed match — not only a
    /// literal residual <c>{{</c>/<c>}}</c> pair — because <c>{{{x}}}</c> matches <see cref="SentenceToken"/>'s
    /// INNER <c>{{x}}</c> (regex is not anchored), leaving only single stray braces (<c>{</c> ... <c>}</c>)
    /// behind; a doubled-brace-only check would miss exactly that case.</para>
    /// <para><b>Authoring rule (task 022 rework round 2, finding F12): curly braces are RESERVED for
    /// <c>{{field}}</c> placeholders.</b> A single literal brace in prose — e.g. <c>"(see {policy})"</c> — is
    /// therefore refused as <c>template_malformed</c>, deliberately: it is indistinguishable from a mistyped
    /// placeholder, and a template has no escape syntax. Write the prose without braces. Full-width braces
    /// (U+FF5B <c>｛</c> / U+FF5D <c>｝</c>) are refused the same way, so a placeholder typed with an IME or pasted
    /// from a CJK document cannot pass as prose and render literally.</para>
    /// </remarks>
    internal static bool HasMalformedPlaceholder(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var withoutWellFormedTokens = SentenceToken().Replace(template, string.Empty);
        return withoutWellFormedTokens.AsSpan().IndexOfAny(ReservedBraces) >= 0;
    }

    /// <summary>The characters <see cref="HasMalformedPlaceholder"/> refuses outside a well-formed token: ASCII
    /// braces and their full-width forms U+FF5B / U+FF5D (finding F12).</summary>
    private static readonly System.Buffers.SearchValues<char> ReservedBraces =
        System.Buffers.SearchValues.Create("{}\uFF5B\uFF5D");

    private static string FormatFactValue(object value) => value switch
    {
        DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Mirrors <c>TodoRegardingBuilder.BuildRecordUrl</c> — a relative URL; the model-driven app
    /// resolves the host origin at click time. No org URL or tenant id hard-coded.</summary>
    internal static string BuildRecordUrl(string entityLogicalName, string recordId) =>
        $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";

    private static string RequireSubjectEntity(string? subjectEntityLogicalName)
    {
        if (string.IsNullOrWhiteSpace(subjectEntityLogicalName) || !LogicalName().IsMatch(subjectEntityLogicalName))
        {
            throw new ArgumentException(
                $"SubjectEntityLogicalName '{subjectEntityLogicalName}' is not a Dataverse logical name " +
                "(expected ^[a-z][a-z0-9_]*$).", nameof(subjectEntityLogicalName));
        }

        return subjectEntityLogicalName;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}")]
    private static partial Regex SentenceToken();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();
}
