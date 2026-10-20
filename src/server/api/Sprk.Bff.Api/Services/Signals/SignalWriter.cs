using System.Collections.Frozen;
using System.Globalization;
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Workspace;
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
public sealed record SignalEvidenceRef(string Kind, string Ref, string Tier, double? Confidence = null)
{
    /// <summary>Task 031 (FR-48, D-23): the witness's display value (for a communication, its subject). Never copied into
    /// <c>sprk_sentence</c>.</summary>
    public string? Text { get; init; }

    /// <summary>Task 031 (FR-48): where the witness came from (for a communication, its sender).</summary>
    public string? Source { get; init; }

    /// <summary>Task 031 (FR-48): when the witness was true, ISO 8601 UTC (the clause's date field on the witness row).</summary>
    public string? AsOf { get; init; }

    /// <summary>Task 031 (FR-48): the clause the witness satisfied (<c>all[N]</c>, task 026's clause id). Null marks a context
    /// line, which the UI shows as "context, not tested".</summary>
    public string? Clause { get; init; }
}

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
/// <c>sprk_communication</c>. MUST be one of <see cref="SignalWriter.VerifiedSubjects"/> (a subset of
/// <see cref="SignalWriter.RegardingLookupByEntity"/>'s 10 keys). It is also the subject segment of the dedupe key.</param>
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
    IReadOnlyList<SignalEvidenceRef>? EvidenceRefs = null)
{
    /// <summary>Task 031 (D-13, FR-03): the episode this write belongs to, the last segment of the dedupe key. 1 for a
    /// subject's first Signal under the policy; the evaluator passes n+1 to re-raise after episode n was resolved.</summary>
    public int Episode { get; init; } = 1;

    /// <summary>Task 031 (R13, ADR-024's fifth resolver field): the subject's record number, read by the evaluator from the
    /// field the subject table's <c>sprk_recordtype_ref</c> catalog row names (<c>sprk_regardingrecordnumberfield</c>).
    /// Written to <c>sprk_regardingrecordnumber</c> on create; null when the table has no number field.</summary>
    public string? SubjectRecordNumber { get; init; }

    /// <summary>Task 143 (#1783 / D-135): the person who asked for the Signal, recorded as <c>sprk_createdbyperson</c> with the
    /// owner (<see cref="RecordOwnershipContext.RequestedBy"/>); it decides nothing about the owner. <c>null</c> for every Signal
    /// raised today: the nightly job and the event triggers (one evaluate-plan-write path, <see cref="SignalReevaluationJob"/>) act
    /// for nobody, so the row records no person. Server-derived only, never bound from a request body.</summary>
    public RecordRequester? RequestedBy { get; init; }
}

/// <summary>The result of one <see cref="SignalWriter.WriteAsync"/> call.</summary>
/// <param name="SignalId">The (possibly pre-existing) <c>sprk_signal</c> row id.</param>
/// <param name="Created"><c>true</c> on first detection; <c>false</c> when re-evaluation matched an existing
/// row's dedupe key and only <c>sprk_lastevaluated</c> was refreshed (CM-9's create-then-reconcile contract).</param>
/// <param name="GroupingMatterId">The Signal's <c>sprk_matter</c>: the core record when it is a matter, otherwise
/// <see cref="Guid.Empty"/> (a project, work assignment, service request or no core record, task 037/D-36).</param>
/// <param name="OwningBusinessUnitId">The business unit read back from the created row (it derives from the owning team the
/// resolver chose, task 143 / D-130; the writer never sends it). <c>Guid.Empty</c> on a re-evaluation update, where ownership is
/// not touched, and on a skip.</param>
public sealed record SignalWriteResult(Guid SignalId, bool Created, Guid GroupingMatterId, Guid OwningBusinessUnitId)
{
    /// <summary>Task 039 (D-33): the Secure Record Owners team that owns the row when the create set it as owner;
    /// <c>null</c> for an ordinary (business-unit team) owner, on a re-evaluation update and on a skip.</summary>
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
/// The core record a subject groups under (task 037/031; D-34, D-36), as <see cref="SignalWriter.DeriveCoreRecordAsync"/>
/// derives it: <see cref="Entity"/> and <see cref="Id"/> name the core record, <see cref="TypeRefId"/> its
/// <c>sprk_recordtype_ref</c> catalog row (what <c>sprk_corerecordtype</c> holds). For a no-core Do item (D-35)
/// <see cref="Entity"/> and <see cref="TypeRefId"/> are null, <see cref="Id"/> is empty and <see cref="NoCoreOwner"/> is the
/// team the Signal is owned by: <see cref="IRecordOwnershipResolver"/>'s answer for the item, in D-134's order (task 143), never
/// a user and never the item's raw <c>ownerid</c>.
/// </summary>
public sealed record SignalCoreRecord(string? Entity, Guid Id, Guid? TypeRefId, EntityReference? NoCoreOwner)
{
    /// <summary>True for a D-35 item with no core record.</summary>
    public bool IsNoCore => Entity is null;
}

/// <summary>What <see cref="SignalWriter.CloseAsync"/> did.</summary>
public enum SignalCloseOutcome
{
    /// <summary>The Signal was closed.</summary>
    Closed,

    /// <summary>The Signal changed after it was read (its row version moved), so nothing was written.</summary>
    ChangedSinceRead,
}

/// <summary>What <see cref="SignalWriter.CloseActedByDecisionAsync"/> did (ebilling-r1 tasks 049, 100 and 105; D-122).</summary>
public enum SignalAutomatedCloseOutcome
{
    /// <summary>The Signal was closed as Acted and linked to the Decision Record.</summary>
    Closed,

    /// <summary>The Signal is already Resolved and names this Decision Record (a resumed run); nothing was written.</summary>
    AlreadyClosedByThisRecord,

    /// <summary>A person resolved the Signal, another record closed it, or the system closed it before the set was read; nothing was
    /// written. A person's resolution is never overwritten.</summary>
    LeftAsIs,

    /// <summary>The Signal changed between the read and the write twice (its row version moved); nothing was written. The caller retries.</summary>
    ChangedSinceRead,
}

/// <summary>What <see cref="SignalWriter.LinkConditionClearedRecordAsync"/> did (task 153).</summary>
public enum SignalRecordLinkOutcome
{
    /// <summary>The Signal now names the record (and, when known, the person in <c>sprk_resolvedby</c>).</summary>
    Linked,

    /// <summary>The Signal already named this record; nothing was written.</summary>
    AlreadyLinked,

    /// <summary>The Signal is no longer a ConditionCleared closure (a person's decision re-closed it); nothing was written.</summary>
    NotApplicable,

    /// <summary>The Signal changed between the read and the write (its row version moved); nothing was written. The next pass retries.</summary>
    ChangedSinceRead,

    /// <summary>Adversarial pass 1 F2: the Signal names a Decision Record other than the one it named when the job closed it; nothing was
    /// overwritten. Logged at Warning and counted on <c>ontology.writer.skips</c> (permanent: not retried).</summary>
    ForeignLink,
}

/// <summary>
/// Thrown when the writer's binding escalation trigger fires: a subject whose core record or owner cannot be derived, or a
/// row whose owner cannot be verified. The caller MUST stop rather than write a Signal it cannot group or leave it silently
/// with the wrong owner (task 030 <c>&lt;escalation&gt;</c>; <c>security-roles.md</c> §4; task 143, D-130).
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
    /// owner read-back itself throwing) as an escalation, so it funnels through the SAME
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
/// The stable refusal codes of <see cref="SignalWriter.PreflightRefusal"/> (ebilling-r1 task 049): one per distinct request check
/// <see cref="SignalWriter.WriteAsync"/> makes before its first Dataverse call, in the order it makes them. Snake_case and bounded,
/// like <see cref="OntologyWriterFailureReason"/>; the two checks that already have a writer reason there
/// (<see cref="SentenceTemplateInvalid"/>, <see cref="SubjectNotVerified"/>) use that exact value, so a preflight code and the
/// <c>reason</c> a refused write logs and meters agree.
/// </summary>
public static class SignalWriteRefusal
{
    /// <summary>The request is null.</summary>
    public const string RequestMissing = "request_missing";

    /// <summary><c>SubjectEntityLogicalName</c> is blank or not a Dataverse logical name (<c>^[a-z][a-z0-9_]*$</c>).</summary>
    public const string SubjectEntityInvalid = "subject_entity_invalid";

    /// <summary><c>SubjectId</c> is <see cref="Guid.Empty"/>.</summary>
    public const string SubjectIdEmpty = "subject_id_empty";

    /// <summary><c>PolicyCode</c> is blank (it is part of the dedupe key).</summary>
    public const string PolicyCodeMissing = "policy_code_missing";

    /// <summary><c>PolicyCode</c> exceeds <c>sprk_signal.sprk_policycode</c>'s 50 characters.</summary>
    public const string PolicyCodeTooLong = "policy_code_too_long";

    /// <summary><c>ShortHeadline</c> is blank (<c>sprk_name</c> is NOT NULL).</summary>
    public const string HeadlineMissing = "headline_missing";

    /// <summary><c>ShortHeadline</c> exceeds <c>sprk_signal.sprk_name</c>'s 200 characters.</summary>
    public const string HeadlineTooLong = "headline_too_long";

    /// <summary><c>Lane</c> is not a <c>sprk_signal.sprk_lane</c> option value.</summary>
    public const string LaneInvalid = "lane_invalid";

    /// <summary><c>Severity</c> is set and is not a <c>sprk_signal.sprk_severity</c> option value.</summary>
    public const string SeverityInvalid = "severity_invalid";

    /// <summary><c>Episode</c> is not a whole number from 1 (D-13).</summary>
    public const string EpisodeInvalid = "episode_invalid";

    /// <summary><c>MessageTemplate</c> is null or blank.</summary>
    public const string TemplateMissing = "template_missing";

    /// <summary><c>FactValues</c> is null.</summary>
    public const string FactValuesMissing = "fact_values_missing";

    /// <summary>The template cannot be rendered strictly from the fact snapshot (a malformed placeholder, a token that is not a key, a
    /// null fact, or a leftover placeholder; §0.3). The value of <see cref="OntologyWriterFailureReason.SentenceTemplateInvalid"/>.</summary>
    public const string SentenceTemplateInvalid = OntologyWriterFailureReason.SentenceTemplateInvalid;

    /// <summary>The rendered sentence exceeds <c>sprk_signal.sprk_sentence</c>'s 2000 characters.</summary>
    public const string SentenceTooLong = "sentence_too_long";

    /// <summary>The subject table is not in <see cref="SignalWriter.VerifiedSubjects"/>. The value of
    /// <see cref="OntologyWriterFailureReason.MatterDerivationUnverified"/>.</summary>
    public const string SubjectNotVerified = OntologyWriterFailureReason.MatterDerivationUnverified;
}

/// <summary>
/// Writes <c>sprk_signal</c> rows idempotently via the dedupe key, populates the subject through the ADR-024
/// polymorphic pattern, and owns every row by the team uac-r2's <see cref="IRecordOwnershipResolver"/> returns (task 030; spec
/// FR-03, FR-14, NFR-08; task 143, D-129/D-130).
/// </summary>
/// <remarks>
/// <para><b>Ownership is the resolver's answer, as returned (task 143; D-129, D-130, D-131 contract C3).</b> Every Signal's
/// <c>ownerid</c> is the team <see cref="IRecordOwnershipResolver"/> answers, written IN the create by its own
/// <see cref="RecordOwnerResolution.ApplyTo"/>: a secure parent → the Secure Record Owners team; an ordinary core record → that
/// record's business-unit default team; a no-core Do item → the item's answer in D-134's order (below). The writer identity never
/// owns a row, no owner is picked here, and <c>owningbusinessunit</c> is never sent: it derives from the team (the former FR-14
/// path that set it directly, leaving the writer as owner, is gone). The ownership question names EVERY parent the Signal sits
/// under (<see cref="OwnershipContextOf"/>): the core record (primary: it decides an ordinary row's business unit), the subject,
/// and every core record the subject's stamps name (secure-if-any, the same set the Decision Record's owner is asked about,
/// task 142). A refusal — or an answer that names no team — writes nothing at all: it is logged at Warning
/// (<see cref="OntologyWriterEvents.WriteSkippedOwnerRefused"/>), metered as <see cref="OntologyWriterFailureReason.OwnerRefused"/>
/// and returned as a skipped result. (In the nightly job a no-core item's refusal is met first, by
/// <see cref="DeriveCoreRecordAsync"/> while the evaluator plans: the job logs it at Error as
/// <see cref="OntologyWriterEvents.WriteRefused"/> with this writer's escalation attached, and nothing is written.) After CREATE the row is read back: a secure row by <c>owningteam</c>
/// (<see cref="OntologyWriterFailureReason.SecureOwnerMismatch"/>), an ordinary one by <c>ownerid</c>
/// (<see cref="OntologyWriterFailureReason.OwnerMismatch"/>, or <see cref="OntologyWriterFailureReason.NoCoreOwnerMismatch"/>
/// for a no-core row); a reconcile re-checks the existing row the same way and escalates rather than touch a mis-owned row.
/// <b>F2 (open owner decision, 0x80040299):</b> Dataverse refuses a team owner whose role lacks Read on <c>sprk_signal</c>
/// ("Read Privilege Check For Owner failed"); on spaarkedev1 some business-unit default teams lack it (notes/043-bu-team-read-check.md).
/// This writer does not work around it with a user or writer owner: the create fails and is surfaced as
/// <see cref="OntologyWriterFailureReason.DataverseAccessDenied"/> until the owner decides F2. The person who asked
/// (<see cref="SignalWriteRequest.RequestedBy"/>, #1783 / D-135) is stamped as <c>sprk_createdbyperson</c> by the same
/// <c>ApplyTo</c>; every Signal raised today is job-raised and records nobody.</para>
/// <para><b>No-core items (D-35 as amended by D-129 and D-134).</b> A Do-lane item with no core record is asked about as its own
/// parent (<see cref="RecordOwnershipContext.ForParents"/> over the subject). When that answer is ordinary and the item is a
/// PERSONAL To Do whose Assigned To names one eligible user (<see cref="PersonalToDoAssignee"/>, the helper the Decision Record
/// uses), the owner is that user's business-unit team (<see cref="RecordOwnershipContext.ForUserTeam"/>), still the resolver's
/// answer; a secure parent wins, and no single user keeps the item's own answer — exactly the Decision Record's order, so a no-core
/// Signal and its Decision Record agree. A Dataverse fault reading the assignee fails closed
/// (<see cref="OntologyWriterFailureReason.NoCoreOwnerUnresolved"/>) unless the parents' answer is secure; so does a resolver
/// fault on this path. When the item's answer changes after the Signal was written, the nightly job re-owns it through uac-r2's
/// <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/> (<see cref="ReassignNoCoreOwnerAsync"/>), which never moves a row into
/// or out of the Secure Record business unit.</para>
/// <para><b>Create-first, not Upsert (F1/F4/F5/F6).</b> <see cref="WriteAsync"/> always attempts
/// <see cref="OntologyWriterDataverseClient.CreateAsync"/> with the FULL payload (status, firstdetected,
/// owner, policyversion, factsnapshot, sentence, …) — Dataverse's NOT NULL constraints on this
/// table make a partial create impossible, which is why Upsert (a prior design) could not work: an Upsert
/// payload narrow enough to be safe on re-evaluation is too narrow to satisfy a first create. On
/// <c>sprk_dedupekey</c>'s unique-index conflict (<see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>,
/// the SAME classifier <c>CreateCommunicationRaceProofAsync</c> already uses for the identical fault), the
/// writer reconciles to the EXISTING row and updates ONLY <c>sprk_lastevaluated</c> — never re-sending
/// <c>sprk_signalstatus</c>, <c>sprk_firstdetected</c>, <c>ownerid</c>, <c>sprk_policyversion</c>,
/// <c>sprk_factsnapshot</c> or <c>sprk_sentence</c>. Those are DETECTION-TIME facts (the POML's own rationale):
/// re-sending them on every re-evaluation would silently re-open a Signal a human already resolved or dismissed,
/// and would re-write "what the data said" with whatever the data says NOW, defeating the whole reason
/// <c>sprk_factsnapshot</c> exists. <c>sprk_firstdetected</c> is therefore set ATOMICALLY in the same Create
/// call that sets everything else — no second round-trip, no window where a half-written row exists.
/// Re-fire-after-resolution semantics and <c>Superseded</c>-on-version-change belong to the EVALUATOR (task
/// 031), not this writer: this class's only job on a duplicate is "touch lastevaluated", never "decide whether
/// to reopen".</para>
/// <para><b>Subject lookup + ADR-024 pair: the shared builder (task 141, D-132, D-133).</b> The subject's typed
/// <c>sprk_regarding{entity}</c> lookup and the pair (<c>sprk_regardingrecordid</c>, <c>name</c>, <c>url</c> and
/// <c>sprk_regardingrecordtype</c>) are written by <c>TodoRegardingBuilder.ApplySubjectLookupAndPairAsync</c>, the same
/// code that files To Dos, never by a copy here. Since task 140, <c>sprk_signal.sprk_regardingrecordtype</c> is a Lookup to
/// <c>sprk_recordtype_ref</c> (D-132), as on <c>sprk_todo</c>: the builder writes the subject table's catalog row there, and
/// a subject table with no catalog row (<c>sprk_communication</c> on spaarkedev1) gets the untyped pair (id, name, url),
/// which is valid, never an error. The builder writes no core-ancestor stamp for this host: the grouping core below is this
/// writer's own (D-36), and the Signal's live stamp belongs to the restamper registration (task 144). The dedupe key is
/// composed from the subject logical name this writer holds (<see cref="BuildDedupeKey"/>), never read back from the pair
/// type. The schema-agreement guard <c>SignalPairTypeSchemaAgreementTests</c> fails the build if the pair type the writer
/// emits stops matching the type the SpaarkeMaster solution declares.</para>
/// <para><b>Core record (task 037; D-34, D-36, D-37, D-35).</b> <see cref="VerifiedSubjects"/> is the closed set of
/// subjects the writer accepts (matter, communication, and the Do-lane event, To Do and work assignment); it is a
/// subset of <see cref="RegardingLookupByEntity"/>'s 10 schema keys. The grouping CORE record (any type
/// <c>CoreAncestorResolver</c> knows) comes only from <c>CoreAncestorResolver.ResolveStampsAsync</c>: the subject itself
/// for a core subject, its single stamp, or, for several stamps, the record the subject is directly filed under, then
/// matter over project, else a refusal. It is written as <c>sprk_corerecordtype</c> (catalog row) +
/// <c>sprk_corerecordid</c>, and to <c>sprk_matter</c> as well when the core is a matter. A Do-lane item with no core
/// record is owned as described above (D-35, D-134). The Signal's <c>sprk_duedate</c> copies the subject's date and is
/// refreshed on reconcile only while the Signal is Open or Acknowledged.</para>
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
/// <para><b>Secure records (task 039, owner D-33).</b> A secure answer is the Secure Record Owners team, in the create. The
/// writer grants no shares: uac-r2's 2-minute secure-child reconcile mirrors the root's sharees onto the row
/// (<c>SecureChildLineage</c> lists <c>sprk_signal</c>), and the create succeeds only where the live Secure Record Owner role
/// holds Read on <c>sprk_signal</c> (<c>config/secure-record-owner-role.json</c>; otherwise 403 <c>0x80040299</c>, surfaced as
/// <see cref="OntologyWriterFailureReason.DataverseAccessDenied"/>).</para>
/// </remarks>
public sealed partial class SignalWriter
{
    // ── Option-set values, verified against describe('tables/sprk_signal'), 2026-10-04. ──────────────────────
    public const int LaneDecide = 100000000;
    public const int LaneDo = 100000001;
    public const int SeverityInfo = 100000000;
    public const int SeverityWarning = 100000001;
    public const int SeverityCritical = 100000002;
    // sprk_signalstatus and sprk_resolutiontype, verified against the live schema (spaarkedev1, 2026-10-10, task 031).
    public const int SignalStatusOpen = 100000000;
    public const int SignalStatusAcknowledged = 100000001;
    public const int SignalStatusResolved = 100000002;
    public const int ResolutionActed = 100000000;
    public const int ResolutionDismissed = 100000001;
    public const int ResolutionConditionCleared = 100000002;
    public const int ResolutionSuperseded = 100000003;
    public const int ResolutionPolicyRetired = 100000004;

    /// <summary>The system closures (FR-16): written by the evaluator, never by a human, so no Decision Record.</summary>
    private static readonly FrozenSet<int> SystemResolutions =
        new[] { ResolutionConditionCleared, ResolutionSuperseded, ResolutionPolicyRetired }.ToFrozenSet();

    // Optimistic-concurrency refusal (ConcurrencyVersionMismatch): the row changed since the evaluator read it.
    private const int ConcurrencyVersionMismatch = unchecked((int)0x80060882);

    // ── Schema length limits (F21). ────────────────────────────────────────────────────────────────────────
    private const int PolicyCodeMaxLength = 50;
    private const int NameMaxLength = 200;
    private const int SentenceMaxLength = 2000;

    private static readonly FrozenSet<int> ValidLanes = new[] { LaneDecide, LaneDo }.ToFrozenSet();
    private static readonly FrozenSet<int> ValidSeverities =
        new[] { SeverityInfo, SeverityWarning, SeverityCritical }.ToFrozenSet();

    /// <summary>
    /// The 10 entity-specific regarding lookups on <c>sprk_signal</c>: the shared regarding builder's own map for this host
    /// table (<c>TodoRegardingBuilder.RegardingLookupsByHostTable["sprk_signal"]</c>, task 141), not a copy. ADR-024: AT MOST
    /// ONE is populated per row.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RegardingLookupByEntity { get; } =
        TodoRegardingBuilder.RegardingLookupsByHostTable[SignalEntity];

    private const string SignalEntity = "sprk_signal";

    /// <summary>
    /// A Signal row's SUBJECT table, read from its filing, never from the pair's type (task 141): the one subject lookup in
    /// <see cref="RegardingLookupByEntity"/> whose id equals the pair id <paramref name="pairId"/>
    /// (<c>sprk_regardingrecordid</c>). <paramref name="lookupIdOf"/> returns the id a lookup column carries on the row, or
    /// null. Returns null when no lookup, or more than one, carries the pair id: a missing or ambiguous subject, which every
    /// caller treats as "no subject" (fail closed).
    /// </summary>
    /// <remarks>
    /// Why not <c>sprk_regardingrecordtype</c>: since task 140 it is a lookup to <c>sprk_recordtype_ref</c> (D-132), so its
    /// value is a catalog row id, not a table name, and it is absent on an untyped pair (a <c>sprk_communication</c> subject on
    /// spaarkedev1). The writer always writes the subject lookup and the pair id together (the shared regarding builder), so
    /// the lookup that repeats the pair id names the subject for typed and untyped pairs alike, with no catalog read. It is the
    /// same agreement rule uac-r2 applies to an untyped pair (a lookup carrying the pair id). A later core-ancestor stamp in
    /// another <c>sprk_regarding*</c> column (task 144) carries a different id and so never matches.
    /// </remarks>
    internal static string? SubjectEntityOf(Guid pairId, Func<string, Guid?> lookupIdOf)
    {
        ArgumentNullException.ThrowIfNull(lookupIdOf);
        if (pairId == Guid.Empty)
        {
            return null;
        }

        string? subject = null;
        foreach (var (entity, lookup) in RegardingLookupByEntity)
        {
            if (lookupIdOf(lookup) != pairId)
            {
                continue;
            }

            if (subject is not null)
            {
                return null;
            }

            subject = entity;
        }

        return subject;
    }

    /// <summary>
    /// The subject types the writer accepts: a CLOSED, VERIFIED set and deliberately a SUBSET of
    /// <see cref="RegardingLookupByEntity"/>'s keys. Task 037 (D-16/D-36) adds the three Do-lane subjects to matter and
    /// communication. Task 025b (owner decision D-122) adds <c>sprk_project</c>: a Threshold rule on a project's spend, budget or
    /// utilization raises a Signal about the project, which is a core record and groups under itself like a matter.
    /// ebilling-r1 task 026 (FR-15, D-31) adds <c>sprk_invoice</c> and <c>sprk_billingevent</c> (the Invoice Line): a billing
    /// finding is about an invoice or one of its lines. Their core record is the subject's OWN core lookup, derived by
    /// <see cref="CoreAncestorResolver"/> (a line is never resolved through its invoice, D-75), and exactly one is required
    /// (<see cref="RequireSingleInvoiceFamilyCore"/>). <c>sprk_servicerequest</c> and <c>sprk_document</c> stay refused as a
    /// SUBJECT: no rule reads them in R1. Each subject's core record comes from <see cref="CoreAncestorResolver"/>, never from a
    /// lookup walk here.
    /// </summary>
    public static readonly FrozenSet<string> VerifiedSubjects = new[]
    {
        "sprk_matter", "sprk_project", "sprk_communication", "sprk_event", "sprk_todo", "sprk_workassignment",
        "sprk_invoice", "sprk_billingevent",
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
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalWriter> _logger;

    /// <param name="recordTypes">The <c>sprk_recordtype_ref</c> lookup the shared regarding builder types the pair from
    /// (<c>QueryRecordTypeRefAsync</c>; the same source <c>TaskActionCore</c> and the To Do writers hand it). Task 141.</param>
    /// <param name="scopes">Task 143: the scope this singleton resolves two SCOPED, already-registered services from, per call
    /// (the <see cref="SignalSuppressionService"/> pattern): <see cref="AssignedAccessStore"/> for the D-134 assignee read
    /// (<see cref="PersonalToDoAssignee"/>) and <see cref="SecureChildReconciler"/> for the no-core re-own
    /// (<see cref="ReassignNoCoreOwnerAsync"/>). No new registration.</param>
    public SignalWriter(
        OntologyWriterDataverseClient writerClient,
        IGenericEntityService sysadminClient,
        CoreAncestorResolver coreAncestors,
        ICommunicationDataverseService recordTypes,
        IRecordOwnershipResolver ownership,
        IServiceScopeFactory scopes,
        TimeProvider timeProvider,
        ILogger<SignalWriter> logger)
    {
        _writerClient = writerClient ?? throw new ArgumentNullException(nameof(writerClient));
        _sysadminClient = sysadminClient ?? throw new ArgumentNullException(nameof(sysadminClient));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Writes one <c>sprk_signal</c> row for <paramref name="request"/>. Re-running with the same
    /// (<c>PolicyCode</c>, subject, <see cref="SignalWriteRequest.Episode"/>) reconciles to the EXISTING row and refreshes only
    /// <c>sprk_lastevaluated</c> (CM-9's create-then-reconcile contract — see class remarks).
    /// </summary>
    /// <exception cref="ArgumentException">A required field is missing, exceeds its schema length limit, or
    /// <c>Lane</c>/<c>Severity</c> is not one of the live schema's option values.</exception>
    /// <exception cref="SignalWriterEscalationException">The core record cannot be derived; a no-core item's owner cannot be
    /// resolved; or the resolver's owner does not read back on the created row, or on the existing row a reconcile meets (the
    /// binding escalation trigger — see class remarks). A refused owner is not thrown: it is a skipped result.</exception>
    /// <exception cref="SignalSentenceTemplateException"><paramref name="request"/>'s message template cannot
    /// be rendered strictly from the detection-time snapshot (§0.3).</exception>
    public async Task<SignalWriteResult> WriteAsync(SignalWriteRequest request, CancellationToken ct = default)
    {
        // Every check the writer makes before its first Dataverse call is ONE definition (CheckRequest), shared with PreflightRefusal.
        // An argument refusal is thrown as it always was, unlogged (a caller bug). The two refusals the writer logs and meters
        // (an unrenderable template, an unverified subject) are thrown inside the try, so the clauses below see them.
        var refusal = CheckRequest(request, out var renderedSentence);
        if (refusal is { Error: not (SignalSentenceTemplateException or SignalWriterEscalationException) })
        {
            throw refusal.Error;
        }

        var subjectEntity = request.SubjectEntityLogicalName;
        var cleanSubjectId = request.SubjectId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
        Guid? ownerTeamId = null; // named on the privilege refusal below: the team whose role lacks Read on sprk_signal

        try
        {
            if (refusal is not null)
            {
                throw refusal.Error;
            }

            var sentence = renderedSentence!;

            var core = await ResolveCoreAsync(subjectEntity, request.SubjectId, request.Lane, ct).ConfigureAwait(false);
            var matterId = core.Entity == "sprk_matter" ? core.Id : Guid.Empty;
            var dueDate = await ReadSubjectDueDateAsync(subjectEntity, request.SubjectId, ct).ConfigureAwait(false);

            // Task 143 (D-129/D-130): the owner is uac-r2's answer, as returned, for every parent the Signal sits under; a
            // no-core item's in D-134's order. A refusal (or an answer naming no team) writes nothing at all, not even a
            // re-evaluation touch.
            var owner = await ResolveOwnerAsync(core, subjectEntity, request.SubjectId, request.RequestedBy, ct).ConfigureAwait(false);
            ownerTeamId = owner.OwningTeamId;
            if (!owner.IsOwned)
            {
                var refusalCode = owner.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource;
                _logger.LogWarning(OntologyWriterEvents.WriteSkippedOwnerRefused,
                    "Signal write SKIPPED for policy {PolicyCode}, subject {SubjectEntity} {SubjectId}, core record {CoreEntity} {CoreId}: " +
                    "no owner (reason={Reason}, code={RefusalCode}). Nothing was written.",
                    request.PolicyCode, subjectEntity, cleanSubjectId, core.Entity ?? "(none)", core.Id,
                    OntologyWriterFailureReason.OwnerRefused, refusalCode);
                OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.OwnerRefused);
                return new SignalWriteResult(Guid.Empty, false, matterId, Guid.Empty)
                {
                    SkippedRefusalCode = refusalCode,
                    CoreRecordEntity = core.Entity,
                    CoreRecordId = core.Id,
                };
            }

            Guid? coreTypeRefId = core.Entity is null
                ? null
                : await ResolveRecordTypeRefIdAsync(core.Entity, ct).ConfigureAwait(false);

            var nowUtc = _timeProvider.GetUtcNow();
            var dedupeKey = BuildDedupeKey(request.PolicyCode, subjectEntity, cleanSubjectId, request.Episode);
            var entity = BuildEntity(request, core, coreTypeRefId, owner, dueDate, sentence, nowUtc);

            // ADR-024 subject lookup + pair through the SHARED regarding builder (task 141, D-133). No core-ancestor stamp (see
            // class remarks). A catalog read that FAULTS is not "no catalog row" (review F1): the builder throws, and the Signal
            // is refused as an escalation (logged, metered, counted by the evaluator) rather than written untyped.
            try
            {
                await new TodoRegardingBuilder(_recordTypes, _coreAncestors, _logger)
                    .ApplySubjectLookupAndPairAsync(entity, subjectEntity, request.SubjectId, request.SubjectDisplayName, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested) && !IsDataverseAccessDenied(ex))
            {
                throw new SignalWriterEscalationException(
                    $"The subject lookup and regarding pair for {subjectEntity} {cleanSubjectId} could not be built " +
                    $"({ex.GetType().Name}); the Signal is not written untyped.",
                    OntologyWriterFailureReason.SubjectPairUnresolved, ex);
            }
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
                    // The owner columns are selected in THIS SAME retrieve (no extra round trip): a row whose owner is not the
                    // resolver's answer now (its core record moved business unit, say) is re-checked on every reconcile, not
                    // only on the original create (R1, second independent review; task 143).
                    existing = await _writerClient
                        .RetrieveByAlternateKeyAsync("sprk_signal", keyAttributes, new[] { "sprk_signalid", "owningbusinessunit", "owningteam", "ownerid", "sprk_signalstatus", "sprk_duedate", "sprk_corerecordid" }, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception readEx)
                {
                    throw new SignalWriterEscalationException(
                        $"Signal dedupekey '{dedupeKey}' hit a duplicate-key conflict, but the existing row could " +
                        "not be read back to reconcile.",
                        OntologyWriterFailureReason.OwnerReadBackFailed, readEx);
                }

                signalId = existing.Id;

                // Task 037 (review F1): the subject moved between no-core and a core, was re-filed, or (no-core) its owner answer
                // changed since this Signal was written. Re-grouping and the no-core re-own are task 031's job; here the
                // reconcile is refused, loudly, never passed silently and never left to fail as an unrelated owner mismatch.
                if (CoreRecordChanged(existing, core, owner))
                {
                    _logger.LogWarning(OntologyWriterEvents.WriteSkippedCoreRecordChanged,
                        "Signal reconcile SKIPPED for policy {PolicyCode}, subject {SubjectEntity} {SubjectId}, signal {SignalId}: " +
                        "its core record or no-core owner changed since it was written (reason={Reason}). Nothing was updated.",
                        request.PolicyCode, subjectEntity, cleanSubjectId, signalId, OntologyWriterFailureReason.CoreRecordChanged);
                    OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.CoreRecordChanged);
                    return new SignalWriteResult(signalId, false, matterId, Guid.Empty)
                    {
                        SkippedRefusalCode = OntologyWriterFailureReason.CoreRecordChanged,
                        CoreRecordEntity = core.Entity,
                        CoreRecordId = core.Id,
                    };
                }

                EnsureOwnershipMatches(existing, owner, core.Entity is null, signalId);

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

            // Ownership is NOT touched on a re-evaluation update: report Guid.Empty rather than a value that would wrongly imply
            // this write just (re-)assigned it.
            var resultOwningBu = Guid.Empty;
            if (created)
            {
                // Verify the owner landed rather than trusting the 204: the row reads back with the resolver's team.
                Entity verifyRow;
                try
                {
                    verifyRow = await _writerClient
                        .RetrieveAsync("sprk_signal", signalId, new[] { "owningbusinessunit", "owningteam", "ownerid" }, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception readEx)
                {
                    // R1 (second independent review): a read-back that THROWS after a successful create is its OWN failure
                    // mode -- distinct from a successful read that disagrees (EnsureOwnershipMatches) -- and must be just as
                    // visible, not swallowed as an unrelated raw exception.
                    throw new SignalWriterEscalationException(
                        $"Signal {signalId:D} was created but its owner could not be read back to verify.",
                        OntologyWriterFailureReason.OwnerReadBackFailed, readEx);
                }

                EnsureOwnershipMatches(verifyRow, owner, core.Entity is null, signalId);
                resultOwningBu = verifyRow.GetAttributeValue<EntityReference>("owningbusinessunit")?.Id ?? Guid.Empty;
            }

            _logger.LogInformation(
                "Signal {SignalId} {Action} for policy {PolicyCode}, subject {SubjectEntity} {SubjectId}, core record {CoreEntity} {CoreId}.",
                signalId, created ? "created" : "reconciled (re-evaluation)", request.PolicyCode, subjectEntity, cleanSubjectId,
                core.Entity ?? "(none)", core.Id);

            return new SignalWriteResult(signalId, created, matterId, resultOwningBu)
            {
                SecureOwnerTeamId = created && owner.IsSecureOwner ? owner.OwningTeamId : null,
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
                "Signal writer refused for policy {PolicyCode} (reason={Reason}, ownerTeam={OwnerTeamId}): the team's role lacks a " +
                "privilege on sprk_signal (a redelivery cannot fix it).",
                request.PolicyCode, OntologyWriterFailureReason.DataverseAccessDenied, ownerTeamId);
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
    internal static bool IsDataverseAccessDenied(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fault
                && (fault.Detail?.ErrorCode == unchecked((int)0x80040220)
                    || fault.Detail?.ErrorCode == unchecked((int)0x80040299)))
            {
                return true;
            }

            // The shared Web API client throws a bare HttpRequestException (status only) for a 403 on an owner write.
            if (e is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden })
            {
                return true;
            }

            if (IsAccessDeniedMessage(e.Message))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// K1 (task 143 follow-up): true when <paramref name="ex"/> is a privilege refusal that a redelivery cannot change, so a caller
    /// counts it apart from the failures a retry can fix. It is <see cref="IsDataverseAccessDenied"/> EXCLUDING a
    /// <see cref="SignalWriterEscalationException"/>: the writer wraps reads and resolver faults (the owner read-back, the duplicate-key
    /// reconcile read, the no-core resolver, the assignee read) in an escalation whose inner fault can be a denial, but those are the
    /// writer's own refusals of an unknown state and stay retryable. The ONE definition for the job and the closure service.
    /// </summary>
    internal static bool IsDeterministicPrivilegeRefusal(Exception ex) =>
        ex is not SignalWriterEscalationException && IsDataverseAccessDenied(ex);

    /// <summary>The message fallback of <see cref="IsDataverseAccessDenied"/>, also used on a reconciler's refusal text.</summary>
    internal static bool IsAccessDeniedMessage(string? m) =>
        !string.IsNullOrEmpty(m) && (
            m.Contains("0x80040220", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("0x80040299", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("does not have", StringComparison.OrdinalIgnoreCase) && m.Contains("right", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Privilege Check", StringComparison.OrdinalIgnoreCase));

    /// <summary>spec FR-03 as amended by D-13 (task 031): <c>{policycode}|{subject logical name}|{subject id}|{episode}</c>.
    /// Task 007 re-keyed the existing dev rows as episode 1. D-132 (task 141): <paramref name="subjectEntity"/> is the subject
    /// logical name the writer already holds (the request's), never read back from <c>sprk_regardingrecordtype</c>, which is a
    /// lookup to <c>sprk_recordtype_ref</c> since task 140; the key's value format is unchanged.</summary>
    internal static string BuildDedupeKey(string policyCode, string subjectEntity, string cleanSubjectId, int episode) =>
        $"{policyCode}|{subjectEntity}|{cleanSubjectId}|{episode.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The begins-with prefix every episode key of one (policy, subject) pair shares (D-13's latest-episode
    /// lookup): <c>{policycode}|{type}|{id}|</c>. The trailing separator keeps <c>POL-A</c> from matching <c>POL-AB</c>.</summary>
    internal static string DedupeKeySubjectPrefix(string policyCode, string subjectEntity, string cleanSubjectId) =>
        $"{policyCode}|{subjectEntity}|{cleanSubjectId}|";

    /// <summary>The begins-with prefix of every episode key under one policy: <c>{policycode}|</c>.</summary>
    internal static string DedupeKeyPolicyPrefix(string policyCode) => $"{policyCode}|";

    /// <summary>
    /// Splits an episode-scoped dedupe key into its parts, reading from the RIGHT so a policy code is never split. False
    /// for a key that is not exactly <c>{code}|{type}|{id}|{episode}</c> with a lowercase id and an episode from 1.
    /// </summary>
    internal static bool TryParseDedupeKey(
        string? key, out string policyCode, out string subjectEntity, out Guid subjectId, out int episode)
    {
        policyCode = subjectEntity = string.Empty;
        subjectId = Guid.Empty;
        episode = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var episodeSep = key.LastIndexOf('|');
        var idSep = episodeSep > 0 ? key.LastIndexOf('|', episodeSep - 1) : -1;
        var typeSep = idSep > 0 ? key.LastIndexOf('|', idSep - 1) : -1;
        if (typeSep <= 0)
        {
            return false;
        }

        var idText = key.AsSpan(idSep + 1, episodeSep - idSep - 1);
        if (!int.TryParse(key.AsSpan(episodeSep + 1), NumberStyles.None, CultureInfo.InvariantCulture, out episode) || episode < 1
            || !Guid.TryParseExact(idText, "D", out subjectId)
            || !idText.SequenceEqual(subjectId.ToString("D", CultureInfo.InvariantCulture)))
        {
            episode = 0;
            return false;
        }

        subjectEntity = key.Substring(typeSep + 1, idSep - typeSep - 1);
        policyCode = key[..typeSep];
        return LogicalName().IsMatch(subjectEntity);
    }

    /// <summary>The core record a Signal groups under (<see cref="Entity"/> null for a no-core Do item, D-35), and every core
    /// record the subject's stamps name (<see cref="Stamps"/>: the subject itself for a core subject, empty for a no-core item),
    /// which are parents of the Signal's owner question as they are of its Decision Record's (task 143).</summary>
    private sealed record CorePlan(string? Entity, Guid Id, IReadOnlyList<CoreAncestorStamp> Stamps);

    /// <summary>
    /// Task 031 (D-34, D-36, D-35): the core record <see cref="WriteAsync"/> would group <paramref name="subjectId"/> under,
    /// derived exactly as the write derives it (<see cref="CoreAncestorResolver"/>, then the <c>sprk_recordtype_ref</c>
    /// catalog row), so the evaluator compares, suppresses and re-groups on the SAME answer the writer will write. Reads
    /// only: nothing is written and nothing is logged or metered here; the caller owns the refusal. For a no-core item it also
    /// resolves the owner the write would give it (task 143: <see cref="SignalCoreRecord.NoCoreOwner"/>, the resolver's team in
    /// D-134's order), so the job compares and re-owns on the same answer; a job-raised Signal names no person.
    /// </summary>
    /// <exception cref="SignalWriterEscalationException">The core record cannot be derived, or a no-core item's owner cannot be
    /// resolved (<see cref="OntologyWriterFailureReason.NoCoreOwnerUnresolved"/>) or is refused
    /// (<see cref="OntologyWriterFailureReason.OwnerRefused"/>): the writer's refusal.</exception>
    internal async Task<SignalCoreRecord> DeriveCoreRecordAsync(
        string subjectEntityLogicalName, Guid subjectId, int lane, CancellationToken ct = default)
    {
        var subjectEntity = RequireSubjectEntity(subjectEntityLogicalName);
        if (subjectId == Guid.Empty)
            throw new ArgumentException("subjectId must not be Guid.Empty.", nameof(subjectId));

        var core = await ResolveCoreAsync(subjectEntity, subjectId, lane, ct).ConfigureAwait(false);
        if (core.Entity is not null)
        {
            var typeRefId = await ResolveRecordTypeRefIdAsync(core.Entity, ct).ConfigureAwait(false);
            return new SignalCoreRecord(core.Entity, core.Id, typeRefId, null);
        }

        var owner = await ResolveOwnerAsync(core, subjectEntity, subjectId, requestedBy: null, ct).ConfigureAwait(false);
        if (!owner.IsOwned)
        {
            throw new SignalWriterEscalationException(
                $"The owner of the no-core {subjectEntity} {subjectId:D} was refused " +
                $"({owner.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource}); no Signal is written for it (D-130).",
                OntologyWriterFailureReason.OwnerRefused);
        }

        return new SignalCoreRecord(null, Guid.Empty, null, new EntityReference("team", owner.OwningTeamId!.Value));
    }

    /// <summary>
    /// Task 031 (FR-16, FR-15): closes ONE Signal as a system resolution (<see cref="ResolutionConditionCleared"/>,
    /// <see cref="ResolutionSuperseded"/> or <see cref="ResolutionPolicyRetired"/>). <c>sprk_decisionrecord</c> and
    /// <c>sprk_resolvedby</c> are not written by the close. Task 153 (D-145b): after a Do-lane ConditionCleared close the job writes a
    /// Routine Decision Record and links it with <see cref="LinkConditionClearedRecordAsync"/>; every other system closure (Superseded,
    /// PolicyRetired, a Decide-lane ConditionCleared) stays record-free. The update is conditional on <paramref name="rowVersion"/>
    /// (<c>sprk_signal</c> has optimistic concurrency enabled), so a Signal a person resolved after the evaluator read it is
    /// never closed a second time; that case returns <see cref="SignalCloseOutcome.ChangedSinceRead"/> and writes nothing.
    /// A failure is logged at Error (<see cref="OntologyWriterEvents.WriteRefused"/>), metered and rethrown.
    /// </summary>
    internal async Task<SignalCloseOutcome> CloseAsync(
        Guid signalId, string? rowVersion, int resolutionType, string policyCode, CancellationToken ct = default)
    {
        if (signalId == Guid.Empty)
            throw new ArgumentException("signalId must not be Guid.Empty.", nameof(signalId));
        if (!SystemResolutions.Contains(resolutionType))
            throw new ArgumentException(
                $"Resolution {resolutionType} is not a system resolution (ConditionCleared, Superseded, PolicyRetired); a person's " +
                "resolution is written by the commit route with its Decision Record (FR-16).", nameof(resolutionType));
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ArgumentException(
                "rowVersion is required: closing without it could re-close a Signal a person resolved since it was read (FR-16).",
                nameof(rowVersion));

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var fields = new Dictionary<string, object>
        {
            ["sprk_signalstatus"] = new OptionSetValue(SignalStatusResolved),
            ["sprk_resolutiontype"] = new OptionSetValue(resolutionType),
            ["sprk_resolvedon"] = nowUtc,
            ["sprk_lastevaluated"] = nowUtc,
        };

        try
        {
            await _writerClient.UpdateIfRowVersionMatchesAsync("sprk_signal", signalId, rowVersion, fields, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Signal {SignalId} closed for policy {PolicyCode} (resolution {ResolutionType}, no Decision Record).",
                signalId, policyCode, resolutionType);
            return SignalCloseOutcome.Closed;
        }
        catch (Exception ex) when (IsConcurrencyVersionMismatch(ex))
        {
            _logger.LogInformation(
                "Signal {SignalId} changed since the evaluator read it; NOT closed (policy {PolicyCode}). The next pass re-reads it.",
                signalId, policyCode);
            return SignalCloseOutcome.ChangedSinceRead;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = IsDataverseAccessDenied(ex) ? OntologyWriterFailureReason.DataverseAccessDenied : OntologyWriterFailureReason.SignalCloseFailed;
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Signal close refused for policy {PolicyCode}, signal {SignalId} (reason={Reason}).", policyCode, signalId, reason);
            OntologyWriterTelemetry.RecordFailure(reason);
            throw;
        }
    }

    private static readonly string[] AutomatedCloseColumns =
        ["sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_decisionrecord", "versionnumber"];

    /// <summary>
    /// ebilling-r1 task 049 (D-122): closes ONE Signal as <see cref="ResolutionActed"/>, resolved by the Decision Record
    /// <paramref name="decisionRecordId"/> of a billing decision: an AUTOMATED one (the automatic rejection of an invoice resolves every open
    /// Signal on it), or, with <paramref name="decidedByUserId"/>, a HUMAN one (a line accept / reduce / disallow, task 100; an override or a
    /// return of a blocked submission, task 105). A separate entry from <see cref="CloseAsync"/>, whose system-only guard stays (FR-16). The
    /// writer (app identity) does the close, with no impersonation, so a human decision's caller is authorized BEFORE any write (AppendTo on
    /// the core record and Write on each Signal's subject: the commit route's rule, <see cref="DecisionCallerRights"/>). An automated decider
    /// has no person, so <c>sprk_resolvedby</c> is written only when <paramref name="decidedByUserId"/> is given (none is invented);
    /// <c>sprk_resolutionnotes</c> is never written.
    /// <para><b>Reads, then decides</b> (the commit route's rule, as <c>DecisionCommitService.CloseSignalAsync</c> applies it):
    /// already Resolved and naming this record → <see cref="SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord"/>, no write; Open or
    /// Acknowledged → closed; closed by the SYSTEM (ConditionCleared, Superseded, PolicyRetired) at or after <paramref name="setReadAtUtc"/>,
    /// the moment the caller read the set it decided against → re-closed, so the Signal and the record agree; anything else (a person's
    /// Acted or Dismissed, another record, a system close before the set was read) → <see cref="SignalAutomatedCloseOutcome.LeftAsIs"/> and one
    /// Information line (ids and the policy code only).</para>
    /// <para><b>One write</b>, conditional on the row version just read: status Resolved, resolution Acted, <c>sprk_resolvedon</c> and
    /// <c>sprk_lastevaluated</c> the same instant (D-120), <c>sprk_decisionrecord</c>, and <c>sprk_resolvedby</c> (the systemuser
    /// <paramref name="decidedByUserId"/>) only when it is given. A version mismatch re-reads once and decides again; a
    /// second one is <see cref="SignalAutomatedCloseOutcome.ChangedSinceRead"/>. A Signal that no longer exists (ObjectDoesNotExist) is
    /// rethrown unchanged, logged at Debug only and not metered: the caller treats it as "nothing to close". Any other failure is logged
    /// at Error (<see cref="OntologyWriterEvents.WriteRefused"/>), metered and rethrown, as <see cref="CloseAsync"/> does.</para>
    /// </summary>
    /// <param name="decidedByUserId">The deciding person's <c>systemuserid</c> for a human decision, resolved by the caller from the
    /// on-behalf-of identity (never from request input); null for an automated decision, which writes no <c>sprk_resolvedby</c>.</param>
    /// <exception cref="ArgumentException">An id is empty (<paramref name="decidedByUserId"/> included, when given), or
    /// <paramref name="setReadAtUtc"/> is not UTC.</exception>
    internal async Task<SignalAutomatedCloseOutcome> CloseActedByDecisionAsync(
        Guid signalId, Guid decisionRecordId, DateTime setReadAtUtc, string policyCode, Guid? decidedByUserId = null, CancellationToken ct = default)
    {
        if (signalId == Guid.Empty)
            throw new ArgumentException("signalId must not be Guid.Empty.", nameof(signalId));
        if (decisionRecordId == Guid.Empty)
            throw new ArgumentException("decisionRecordId must not be Guid.Empty.", nameof(decisionRecordId));
        if (setReadAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("setReadAtUtc must be a UTC time (DateTimeKind.Utc).", nameof(setReadAtUtc));
        if (decidedByUserId == Guid.Empty)
            throw new ArgumentException("decidedByUserId must be null (an automated decision) or a user id, never Guid.Empty.", nameof(decidedByUserId));

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var row = await _writerClient.RetrieveAsync(SignalEntity, signalId, AutomatedCloseColumns, ct).ConfigureAwait(false);
                var status = row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value;
                var resolution = row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value;
                if (status == SignalStatusResolved && row.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id == decisionRecordId)
                {
                    return SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord;
                }

                var open = status is SignalStatusOpen or SignalStatusAcknowledged;
                var systemClosedSinceRead = status == SignalStatusResolved
                    && resolution is { } r && SystemResolutions.Contains(r)
                    && row.GetAttributeValue<DateTime?>("sprk_resolvedon") is { } resolvedOn
                    && DateTime.SpecifyKind(resolvedOn, DateTimeKind.Utc) >= setReadAtUtc;
                if (!open && !systemClosedSinceRead)
                {
                    _logger.LogInformation(
                        "Signal {SignalId} (policy {PolicyCode}) was resolved otherwise before Decision Record {RecordId} could close it; left as it is.",
                        signalId, policyCode, decisionRecordId);
                    return SignalAutomatedCloseOutcome.LeftAsIs;
                }

                var version = row.RowVersion ?? (row.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null);
                if (string.IsNullOrWhiteSpace(version))
                {
                    throw new InvalidOperationException($"Signal {signalId:D} was read without a row version, so it cannot be closed conditionally.");
                }

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var fields = new Dictionary<string, object>
                {
                    ["sprk_signalstatus"] = new OptionSetValue(SignalStatusResolved),
                    ["sprk_resolutiontype"] = new OptionSetValue(ResolutionActed),
                    ["sprk_resolvedon"] = nowUtc,
                    ["sprk_lastevaluated"] = nowUtc,
                    ["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", decisionRecordId),
                };
                if (decidedByUserId is { } person)
                {
                    // A human decision names its decider in the SAME conditional write; an automated one never does.
                    fields["sprk_resolvedby"] = new EntityReference("systemuser", person);
                }

                try
                {
                    await _writerClient.UpdateIfRowVersionMatchesAsync(SignalEntity, signalId, version, fields, ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Signal {SignalId} closed as Acted for policy {PolicyCode} by {Decider} Decision Record {RecordId}{Reclosed}.",
                        signalId, policyCode, decidedByUserId is null ? "automated" : "human", decisionRecordId,
                        open ? string.Empty : " (re-closed after a system resolution)");
                    return SignalAutomatedCloseOutcome.Closed;
                }
                catch (Exception ex) when (IsConcurrencyVersionMismatch(ex))
                {
                    if (attempt > 0)
                    {
                        _logger.LogInformation(
                            "Signal {SignalId} kept changing while Decision Record {RecordId} closed it; NOT closed (policy {PolicyCode}). The caller retries.",
                            signalId, decisionRecordId, policyCode);
                        return SignalAutomatedCloseOutcome.ChangedSinceRead;
                    }
                }
            }
        }
        catch (Exception ex) when (IsSignalGone(ex))
        {
            // The Signal row no longer exists (deleted after the Decision Record listed it). That is not a write failure: the caller
            // treats it as "nothing to close" (one Warning, then on), so it is neither logged at Error nor metered as signal_close_failed.
            // Rethrown unchanged so the caller keeps its behaviour.
            _logger.LogDebug(
                "Signal {SignalId} (policy {PolicyCode}) no longer exists; Decision Record {RecordId} has nothing to close. Rethrown for the caller.",
                signalId, policyCode, decisionRecordId);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = IsDataverseAccessDenied(ex) ? OntologyWriterFailureReason.DataverseAccessDenied : OntologyWriterFailureReason.SignalCloseFailed;
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Signal close refused for policy {PolicyCode}, signal {SignalId} (reason={Reason}).", policyCode, signalId, reason);
            OntologyWriterTelemetry.RecordFailure(reason);
            throw;
        }
    }

    /// <summary>Whether a Dataverse failure means the row does not exist (<c>0x80040217 ObjectDoesNotExist</c>) anywhere in the inner
    /// chain: the same test the caller applies (<c>InvoiceSubmissionAfterSteps.IsRecordNotFound</c>), so every exception the caller treats
    /// as "the Signal is gone" is also one this writer does not report as a failure.</summary>
    private static bool IsSignalGone(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Task 153 (D-145b): links a Do-lane Signal the job closed as ConditionCleared to its Routine Decision Record: <c>sprk_decisionrecord</c>
    /// and, when the record names the person who did the work, <c>sprk_resolvedby</c>. Nothing else. Reads the Signal first and writes only
    /// when it is still Resolved as ConditionCleared, conditional on the version just read, so a person's decision that re-closed it in
    /// between (with its own record) is never overwritten.
    /// <para><b>Replacing the link (adversarial pass 1 F2, D-128 Q18).</b> A D-128 reminder or inquiry leaves the Signal Acknowledged and
    /// linked to that record; the item stays open until the work is done, so the closing record REPLACES that link. The link replaces exactly
    /// <paramref name="expectedPriorRecordId"/>, the record the Signal named when the job closed it (null: none), and nothing else: any other
    /// record the Signal names now is a foreign link, left as it is, logged at Warning and counted on <c>ontology.writer.skips</c>
    /// (<see cref="SignalRecordLinkOutcome.ForeignLink"/>). The replaced record itself is not touched (records are append-only).</para>
    /// A failure is logged at Error, metered and thrown as the writer's escalation (already metered: the caller does not meter it again).
    /// </summary>
    internal async Task<SignalRecordLinkOutcome> LinkConditionClearedRecordAsync(
        Guid signalId, Guid recordId, Guid? expectedPriorRecordId, Guid? resolvedBy, string policyCode, CancellationToken ct = default)
    {
        if (signalId == Guid.Empty) throw new ArgumentException("signalId must not be Guid.Empty.", nameof(signalId));
        if (recordId == Guid.Empty) throw new ArgumentException("recordId must not be Guid.Empty.", nameof(recordId));

        try
        {
            var row = await _writerClient.RetrieveAsync(SignalEntity, signalId,
                new[] { "sprk_signalstatus", "sprk_resolutiontype", "sprk_decisionrecord", "versionnumber" }, ct).ConfigureAwait(false);
            var linked = row.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id;
            if (linked == recordId) return SignalRecordLinkOutcome.AlreadyLinked;
            if (row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value != SignalStatusResolved
                || row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value != ResolutionConditionCleared)
            {
                _logger.LogInformation(
                    "Signal {SignalId} (policy {PolicyCode}) is no longer a condition-cleared closure; Decision Record {RecordId} was not linked to it.",
                    signalId, policyCode, recordId);
                return SignalRecordLinkOutcome.NotApplicable;
            }

            if (linked is not null && linked != expectedPriorRecordId)
            {
                _logger.LogWarning(OntologyWriterEvents.WriteRefused,
                    "Signal {SignalId} (policy {PolicyCode}) names Decision Record {LinkedRecordId}, not the one it named when it was closed; " +
                    "its condition-cleared Decision Record {RecordId} was NOT linked (reason={Reason}). Nothing was overwritten.",
                    signalId, policyCode, linked, recordId, OntologyWriterFailureReason.ClosureRecordSkippedForeignLink);
                OntologyWriterTelemetry.RecordSkip(OntologyWriterFailureReason.ClosureRecordSkippedForeignLink);
                return SignalRecordLinkOutcome.ForeignLink;
            }

            var version = row.RowVersion ?? (row.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null);
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException($"Signal {signalId:D} was read without a row version, so it cannot be linked conditionally.");
            }

            var fields = new Dictionary<string, object> { ["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId) };
            if (resolvedBy is { } person && person != Guid.Empty)
            {
                fields["sprk_resolvedby"] = new EntityReference("systemuser", person);
            }

            await _writerClient.UpdateIfRowVersionMatchesAsync(SignalEntity, signalId, version, fields, ct).ConfigureAwait(false);
            return SignalRecordLinkOutcome.Linked;
        }
        catch (Exception ex) when (IsConcurrencyVersionMismatch(ex))
        {
            _logger.LogInformation(
                "Signal {SignalId} changed since it was read; Decision Record {RecordId} NOT linked (policy {PolicyCode}). The next pass retries.",
                signalId, recordId, policyCode);
            return SignalRecordLinkOutcome.ChangedSinceRead;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = IsDataverseAccessDenied(ex) ? OntologyWriterFailureReason.DataverseAccessDenied : OntologyWriterFailureReason.SignalRecordLinkFailed;
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Decision Record link refused for policy {PolicyCode}, signal {SignalId} (reason={Reason}).", policyCode, signalId, reason);
            OntologyWriterTelemetry.RecordFailure(reason);
            throw new SignalWriterEscalationException(
                $"Decision Record {recordId:D} could not be linked to Signal {signalId:D} ({ex.GetType().Name}).", reason, ex);
        }
    }

    /// <summary>
    /// Task 153 (D-134): the one enabled user a personal To Do's Assigned To names (<see cref="PersonalToDoAssignee"/>, through the scoped
    /// <see cref="AssignedAccessStore"/>), or null for no single user. A read fault propagates (the caller must not fall back).
    /// </summary>
    internal async Task<Guid?> PersonalToDoAssigneeAsync(Guid todoId, CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await PersonalToDoAssignee.ResolveAsync(
            _sysadminClient, scope.ServiceProvider.GetRequiredService<AssignedAccessStore>(), todoId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// D-120: records, ONCE, that the job observed a Threshold subject's condition FALSE after its Signal was resolved Acted, by
    /// stamping only <c>sprk_lastevaluated</c> on that resolved Signal (<see cref="SignalEpisodePlanner"/> reads
    /// <c>lastevaluated</c> later than <c>resolvedon</c> as "cleared observed"). Conditional on <paramref name="rowVersion"/>; a
    /// Signal changed since the read returns false and writes nothing (the next pass re-reads it). Never touches status,
    /// resolution, <c>sprk_resolvedon</c> or any other column. A failure is logged at Error, metered and rethrown.
    /// <para><b>Marker contract (D-120).</b> <c>sprk_lastevaluated</c> MORE THAN 1 minute after <c>sprk_resolvedon</c> on a resolved
    /// Signal means "cleared observed" (<see cref="ExistingSignal.ClearedMarkerTolerance"/>). Nothing but this stamp may write
    /// <c>sprk_lastevaluated</c> later than <c>sprk_resolvedon</c> on a resolved row; task 043's commit route, which resolves a
    /// Signal Acted, must stamp both columns together (within the tolerance) or leave <c>lastevaluated</c> alone.</para>
    /// </summary>
    internal async Task<bool> MarkClearedObservedAsync(Guid signalId, string? rowVersion, string policyCode, CancellationToken ct = default)
    {
        if (signalId == Guid.Empty)
            throw new ArgumentException("signalId must not be Guid.Empty.", nameof(signalId));
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ArgumentException("rowVersion is required: the stamp must not overwrite a Signal changed since it was read.", nameof(rowVersion));

        var fields = new Dictionary<string, object> { ["sprk_lastevaluated"] = _timeProvider.GetUtcNow().UtcDateTime };
        try
        {
            await _writerClient.UpdateIfRowVersionMatchesAsync("sprk_signal", signalId, rowVersion, fields, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Acted Signal {SignalId} (policy {PolicyCode}): condition observed cleared after the resolution (D-120); a later crossing re-raises.",
                signalId, policyCode);
            return true;
        }
        catch (Exception ex) when (IsConcurrencyVersionMismatch(ex))
        {
            _logger.LogInformation(
                "Signal {SignalId} changed since the evaluator read it; cleared-observed NOT recorded (policy {PolicyCode}).", signalId, policyCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = IsDataverseAccessDenied(ex) ? OntologyWriterFailureReason.DataverseAccessDenied : OntologyWriterFailureReason.SignalCloseFailed;
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Cleared-observed stamp refused for policy {PolicyCode}, signal {SignalId} (reason={Reason}).", policyCode, signalId, reason);
            OntologyWriterTelemetry.RecordFailure(reason);
            throw;
        }
    }

    /// <summary>
    /// Ontology task 145 (D-123, review F1): replaces a Signal's stored <c>sprk_evidencerefs</c> with
    /// <paramref name="redactedEvidenceJson"/>, the same lines with the detail that is no longer within the Signal's secure family
    /// removed (<see cref="SignalSuppressionService.StoredEvidenceToRedactAsync"/>). Writes ONLY that column, conditional on
    /// <paramref name="rowVersion"/>: a Signal changed since the read returns false and writes nothing (the next pass re-reads it), so
    /// a concurrent update is never overwritten. A failure is logged at Error, metered and rethrown.
    /// </summary>
    internal async Task<bool> RedactEvidenceAsync(
        Guid signalId, string? rowVersion, string redactedEvidenceJson, string policyCode, CancellationToken ct = default)
    {
        if (signalId == Guid.Empty)
            throw new ArgumentException("signalId must not be Guid.Empty.", nameof(signalId));
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ArgumentException("rowVersion is required: the redaction must not overwrite a Signal changed since it was read.", nameof(rowVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(redactedEvidenceJson);

        var fields = new Dictionary<string, object> { [SignalSuppressionService.EvidenceColumn] = redactedEvidenceJson };
        try
        {
            await _writerClient.UpdateIfRowVersionMatchesAsync("sprk_signal", signalId, rowVersion, fields, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Signal {SignalId} (policy {PolicyCode}): stored evidence detail outside the Signal's secure family cleared (D-123).",
                signalId, policyCode);
            return true;
        }
        catch (Exception ex) when (IsConcurrencyVersionMismatch(ex))
        {
            _logger.LogInformation(
                "Signal {SignalId} changed since the evaluator read it; its evidence redaction waits for the next pass (policy {PolicyCode}).",
                signalId, policyCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = IsDataverseAccessDenied(ex) ? OntologyWriterFailureReason.DataverseAccessDenied : OntologyWriterFailureReason.SignalEvidenceRedactFailed;
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Evidence redaction refused for policy {PolicyCode}, signal {SignalId} (reason={Reason}).", policyCode, signalId, reason);
            OntologyWriterTelemetry.RecordFailure(reason);
            throw;
        }
    }

    /// <summary>
    /// Task 031 (task 037 hand-off, D-35), task 143 (D-130, D-134): a no-core Signal follows its item's owner answer. When that
    /// answer changes after the Signal was written (the item's own team, or a personal To Do's Assigned To), the Signal is
    /// re-owned to <paramref name="owner"/> — the team <see cref="DeriveCoreRecordAsync"/> resolved, never a user — through
    /// uac-r2's <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/> (UAC task 183, owner round 96), the one place an ordinary
    /// Signal is re-owned: it refuses a row or a team in the Secure Record business unit (moving a row into or out of isolation
    /// is the secure pass's alone) and a user-owned row, and writes conditionally on the version it read. The writer identity
    /// writes no owner here. <see cref="OrdinaryRowReownOutcome.Reowned"/> is logged once (the next pass sees no drift);
    /// <see cref="OrdinaryRowReownOutcome.AlreadyOwned"/>, <see cref="OrdinaryRowReownOutcome.NotFound"/> and
    /// <see cref="OrdinaryRowReownOutcome.Conflict"/> are returned for the caller to count; a refusal is logged at Warning and
    /// metered as <see cref="OntologyWriterFailureReason.NoCoreOwnerMismatch"/>; a failure is logged at Error, metered as
    /// <see cref="OntologyWriterFailureReason.SignalReassignFailed"/> and thrown.
    /// </summary>
    /// <exception cref="InvalidOperationException">The re-own failed (<see cref="OrdinaryRowReownOutcome.Failed"/>, or a fault):
    /// already logged and metered here.</exception>
    internal async Task<OrdinaryRowReownOutcome> ReassignNoCoreOwnerAsync(
        Guid signalId, EntityReference owner, string policyCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (signalId == Guid.Empty)
            throw new ArgumentException("signalId must not be Guid.Empty.", nameof(signalId));
        if (owner.Id == Guid.Empty || owner.LogicalName != "team")
            throw new ArgumentException("owner must be the team the ownership resolver answered (D-130), never a user.", nameof(owner));

        OrdinaryRowReownResult result;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            result = await scope.ServiceProvider.GetRequiredService<SecureChildReconciler>()
                .ReownOrdinaryRowAsync(SignalEntity, signalId, owner.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var faultReason = IsDataverseAccessDenied(ex) ? OntologyWriterFailureReason.DataverseAccessDenied : OntologyWriterFailureReason.SignalReassignFailed;
            _logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "No-core Signal re-own failed for policy {PolicyCode}, signal {SignalId}, owner team {OwnerTeamId} (reason={Reason}).",
                policyCode, signalId, owner.Id, faultReason);
            OntologyWriterTelemetry.RecordFailure(faultReason);
            throw;
        }

        switch (result.Outcome)
        {
            case OrdinaryRowReownOutcome.Reowned:
                _logger.LogInformation(OntologyWriterEvents.NoCoreSignalReassigned,
                    "No-core Signal {SignalId} (policy {PolicyCode}) re-owned to its item's current owner team {OwnerId} (D-130, D-134).",
                    signalId, policyCode, owner.Id);
                break;

            case OrdinaryRowReownOutcome.RefusedSecure or OrdinaryRowReownOutcome.RefusedNotTeamOwned:
                _logger.LogWarning(OntologyWriterEvents.WriteRefused,
                    "No-core Signal {SignalId} (policy {PolicyCode}) was NOT re-owned to team {OwnerId}: {Outcome} (reason={Reason}). " +
                    "Nothing was written.", signalId, policyCode, owner.Id, result.Outcome, OntologyWriterFailureReason.NoCoreOwnerMismatch);
                OntologyWriterTelemetry.RecordFailure(OntologyWriterFailureReason.NoCoreOwnerMismatch);
                break;

            case OrdinaryRowReownOutcome.Failed:
                // Task 143 (review F1): the reconciler returns Dataverse's refusal as text, so it is classified on that text the way
                // IsDataverseAccessDenied classifies an exception's message (F2's 0x80040299 is metered as the access denial it is).
                // K1 (re-own, uac-r2 review): in production the owner write goes through the shared Web API client, which maps only
                // 412 and 404 and throws a bare HttpRequestException("... 403 (Forbidden).") for a privilege refusal: no Dataverse code
                // in the text. The reconciler therefore also returns the write's HTTP status; a 403 is the access denial.
                var forbidden = result.WriteFailureStatusCode == System.Net.HttpStatusCode.Forbidden;
                var failedReason = forbidden || IsAccessDeniedMessage(result.Detail)
                    ? OntologyWriterFailureReason.DataverseAccessDenied
                    : OntologyWriterFailureReason.SignalReassignFailed;
                _logger.LogError(OntologyWriterEvents.WriteRefused,
                    "No-core Signal re-own failed for policy {PolicyCode}, signal {SignalId}, owner team {OwnerTeamId}: {Detail} (reason={Reason}).",
                    policyCode, signalId, owner.Id, result.Detail, failedReason);
                OntologyWriterTelemetry.RecordFailure(failedReason);
                var reownFailure = $"The no-core Signal {signalId:D} could not be re-owned: {result.Detail}";
                throw forbidden
                    // The inner 403 is what IsDataverseAccessDenied / IsDeterministicPrivilegeRefusal read, so the job counts it AccessDenied.
                    ? new InvalidOperationException(reownFailure, new HttpRequestException(result.Detail, null, System.Net.HttpStatusCode.Forbidden))
                    : new InvalidOperationException(reownFailure);
        }

        return result.Outcome;
    }

    /// <summary>True when <see cref="WriteAsync"/> has already logged <paramref name="ex"/> at Error and metered it on
    /// <c>ontology.writer.failures</c> (an escalation, a sentence-template refusal, a privilege refusal), so a caller does not
    /// count it a second time.</summary>
    internal static bool IsMeteredByWriter(Exception ex) =>
        ex is SignalWriterEscalationException or SignalSentenceTemplateException || IsDataverseAccessDenied(ex);

    internal static bool IsConcurrencyVersionMismatch(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> fault && fault.Detail?.ErrorCode == ConcurrencyVersionMismatch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Derives the subject's CORE record (task 037; D-34, D-36, D-37, D-35) with <see cref="CoreAncestorResolver"/>, the
    /// only way the writer derives one. No code here names a core type: the resolver's taxonomy decides, so a core type
    /// added there and in the <c>sprk_recordtype_ref</c> catalog is grouped with no change here (D-36).
    /// </summary>
    private async Task<CorePlan> ResolveCoreAsync(string subjectEntity, Guid subjectId, int lane, CancellationToken ct)
    {
        if (UnverifiedSubject(subjectEntity) is { } unverified)
        {
            throw unverified;
        }

        var result = await _coreAncestors.ResolveStampsAsync(subjectEntity, subjectId, ct).ConfigureAwait(false);
        RequireSingleInvoiceFamilyCore(subjectEntity, subjectId, result);
        switch (result.Status)
        {
            case CoreAncestorStatus.CoreTarget:
                // The subject IS a core record (a matter, or a work assignment, D-36): it groups under itself.
                return new CorePlan(result.Stamps[0].EntityType, result.Stamps[0].RecordId, result.Stamps);

            case CoreAncestorStatus.Derived:
                var stamp = await PickStampAsync(subjectEntity, subjectId, result.Stamps, ct).ConfigureAwait(false);
                return new CorePlan(stamp.EntityType, stamp.RecordId, result.Stamps);

            case CoreAncestorStatus.Error:
                // ebilling-r1 task 049 (ontology F1-3 re-check F2): a FAULT (a throttle, a 503, a metadata or row read that failed;
                // CoreAncestorResult.IsPermanent false) is not a refusal of the subject, so the escalation wraps it. A caller that
                // classifies by the inner exception (InvoiceSubmissionAfterSteps.PermanentWriteRefusal) then retries instead of
                // dropping the Signal for good. A permanent Error (the row is gone, it names two roots) carries none. The reason,
                // the log and the metric are the same either way; the evaluator, the re-evaluation job and WriteAsync's catch read
                // only the type and Reason.
                var unresolvedMessage = $"Cannot derive the core record of {subjectEntity} {subjectId:D}: {result.Error}";
                throw result.IsPermanent
                    ? new SignalWriterEscalationException(unresolvedMessage, OntologyWriterFailureReason.CoreRecordUnresolved)
                    : new SignalWriterEscalationException(
                        unresolvedMessage, OntologyWriterFailureReason.CoreRecordUnresolved, new InvalidOperationException(result.Error));

            case CoreAncestorStatus.NoAncestor:
                return NoCorePlan(subjectEntity, subjectId, lane);

            default:
                // Unclassified (or a status added later): the resolver does not know this subject, which is not the same
                // as "filed under nothing". Never fall into the owner-only path on a status this code did not decide on.
                throw new SignalWriterEscalationException(
                    $"The core-record derivation for {subjectEntity} {subjectId:D} returned status '{result.Status}', which " +
                    "this writer does not map to a core record. Refusing to write.",
                    OntologyWriterFailureReason.CoreRecordUnresolved);
        }
    }

    /// <summary>
    /// ebilling-r1 task 026 (FR-05, FR-15, D-75): an invoice or invoice-line subject (a table of
    /// <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>) is filed under exactly ONE core record, its own. The
    /// resolver's answer for it is used only when it names one: no core record is refused here, never written as a no-core Do
    /// item (D-35), and more than one is refused, never picked by D-37's matter-over-project rule. "More than one" arrives two
    /// ways, and both give <see cref="OntologyWriterFailureReason.CoreRecordAmbiguous"/>: an invoice naming several core
    /// records is <see cref="CoreAncestorStatus.Derived"/> with several stamps; a line naming several is a permanent
    /// <see cref="CoreAncestorStatus.Error"/> with <see cref="CoreAncestorRefusalKind.MultipleRoots"/> (the resolver refuses a
    /// line, <see cref="CoreAncestorResolver.SingleRootIntermediates"/>), as is either one naming two records of one table. A
    /// subject that no longer exists (<see cref="CoreAncestorRefusalKind.NotFound"/>) or a fault
    /// (<see cref="CoreAncestorRefusalKind.Transient"/>) is left to the caller's Error case
    /// (<see cref="OntologyWriterFailureReason.CoreRecordUnresolved"/>), as before. Other subjects are untouched.
    /// </summary>
    /// <exception cref="SignalWriterEscalationException">The invoice-family subject names no core record
    /// (<see cref="OntologyWriterFailureReason.CoreRecordUnresolved"/>) or more than one
    /// (<see cref="OntologyWriterFailureReason.CoreRecordAmbiguous"/>).</exception>
    internal static void RequireSingleInvoiceFamilyCore(string subjectEntity, Guid subjectId, CoreAncestorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!InvoiceFamilyCoreRecordGuard.CoreRecordLookups.ContainsKey(subjectEntity))
        {
            return;
        }

        if (result.Status == CoreAncestorStatus.NoAncestor)
        {
            throw new SignalWriterEscalationException(
                $"{subjectEntity} {subjectId:D} names no core record of its own (FR-05); an invoice or invoice-line Signal is " +
                "never written without one. Refusing to write.",
                OntologyWriterFailureReason.CoreRecordUnresolved);
        }

        if (result.Status == CoreAncestorStatus.Derived && result.Stamps.Count != 1)
        {
            throw new SignalWriterEscalationException(
                $"{subjectEntity} {subjectId:D} names {result.Stamps.Count} core records " +
                $"({string.Join(", ", result.Stamps.Select(s => s.EntityType))}); FR-05 allows exactly one, so none is picked.",
                OntologyWriterFailureReason.CoreRecordAmbiguous);
        }

        if (result.Status == CoreAncestorStatus.Error && result.IsPermanent
            && result.RefusalKind == CoreAncestorRefusalKind.MultipleRoots)
        {
            throw new SignalWriterEscalationException(
                $"{subjectEntity} {subjectId:D} names more than one core record ({result.Error}); FR-05 allows exactly one, so " +
                "none is picked.",
                OntologyWriterFailureReason.CoreRecordAmbiguous);
        }
    }

    /// <summary>True when the existing Signal's core record (or, for a no-core Signal, its owner team) is not the one planned
    /// now (<paramref name="owner"/>: the resolver's answer for the item, task 143).</summary>
    private static bool CoreRecordChanged(Entity existing, CorePlan core, RecordOwnerResolution owner)
    {
        var existingCoreId = existing.GetAttributeValue<string>("sprk_corerecordid");
        if (core.Entity is not null)
        {
            return !string.Equals(existingCoreId?.Trim(), core.Id.ToString("D").ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(existingCoreId))
        {
            return true;
        }

        var current = existing.GetAttributeValue<EntityReference>("ownerid");
        return current is null || current.LogicalName != "team" || current.Id != owner.OwningTeamId;
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

    /// <summary>D-35: no core record. Only a Do-lane item may have none; its owner is resolved by <see cref="ResolveOwnerAsync"/>.</summary>
    private static CorePlan NoCorePlan(string subjectEntity, Guid subjectId, int lane)
    {
        if (lane != LaneDo)
        {
            throw new SignalWriterEscalationException(
                $"Cannot derive a core record for {subjectEntity} {subjectId:D}, and only a Do-lane item may have none (D-35). " +
                "Refusing to write a Decide-lane Signal that no reader could see.",
                OntologyWriterFailureReason.MatterLookupEmpty);
        }

        return new CorePlan(null, Guid.Empty, Array.Empty<CoreAncestorStamp>());
    }

    /// <summary>
    /// Task 143 (D-129, D-130; contract C3): the ownership question for a Signal, as ONE list of every parent it sits under — the
    /// core record first (the primary parent: it decides an ordinary Signal's business unit), then the subject, then every core
    /// record the subject's stamps name (the set the Decision Record's owner is asked about, task 142: secure-if-any). A no-core
    /// item is its own only parent. Duplicates collapse (<see cref="RecordOwnershipContext.ForParents"/>). The person who asked
    /// (<see cref="SignalWriteRequest.RequestedBy"/>, #1783 / D-135) is stamped with the owner and decides nothing about it; a
    /// job-raised Signal passes none.
    /// </summary>
    internal static RecordOwnershipContext OwnershipContextOf(
        string? coreEntity, Guid coreId, IReadOnlyList<CoreAncestorStamp> stamps, string subjectEntity, Guid subjectId,
        RecordRequester? requestedBy)
    {
        ArgumentNullException.ThrowIfNull(stamps);
        var parents = new List<RecordOwnershipParent?>();
        if (coreEntity is not null)
        {
            parents.Add(new RecordOwnershipParent(coreEntity, coreId));
        }

        parents.Add(new RecordOwnershipParent(subjectEntity, subjectId));
        parents.AddRange(stamps.Select(stamp => new RecordOwnershipParent(stamp.EntityType, stamp.RecordId)));
        return RecordOwnershipContext.ForParents(parents) with { RequestedBy = requestedBy };
    }

    /// <summary>
    /// Task 143: the Signal's owner, as uac-r2's resolver returns it (D-130), in the Decision Record's order (D-134). FIRST every
    /// parent (<see cref="OwnershipContextOf"/>): a refusal refuses and a secure answer stands (a secure parent wins). Only when
    /// that answer is ordinary AND there is no core record AND the subject is a PERSONAL To Do whose Assigned To names one eligible
    /// user (<see cref="PersonalToDoAssignee"/>, the helper the Decision Record uses), the owner is that user's business-unit team
    /// (<see cref="RecordOwnershipContext.ForUserTeam"/>). An answer that names no team (not refused, not owned) is returned as a
    /// <see cref="RecordOwnerRefusal.NoOwnerSource"/> refusal: no row is ever left with the writer identity as owner.
    /// </summary>
    /// <exception cref="SignalWriterEscalationException">No-core only: the resolver faulted, or the assignee could not be read
    /// while the parents' answer is ordinary (<see cref="OntologyWriterFailureReason.NoCoreOwnerUnresolved"/>: fail closed, it is
    /// unknown whether a user exists). On the core path a resolver fault propagates unchanged.</exception>
    private async Task<RecordOwnerResolution> ResolveOwnerAsync(
        CorePlan core, string subjectEntity, Guid subjectId, RecordRequester? requestedBy, CancellationToken ct)
    {
        var context = OwnershipContextOf(core.Entity, core.Id, core.Stamps, subjectEntity, subjectId, requestedBy);
        if (core.Entity is not null)
        {
            return Owned(await _ownership.ResolveOwnerAsync(context, ct).ConfigureAwait(false));
        }

        var owner = await ResolveNoCoreOwnerAsync(context, subjectEntity, subjectId, ct).ConfigureAwait(false);
        if (owner.IsRefused || owner.IsSecureOwner || !string.Equals(subjectEntity, PersonalToDoAssignee.Table, StringComparison.Ordinal))
        {
            return owner;
        }

        Guid? assignee;
        try
        {
            assignee = await PersonalToDoAssigneeAsync(subjectId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new SignalWriterEscalationException(
                $"The Assigned To of the personal To Do {subjectId:D} could not be read ({ex.GetType().Name}), and its own answer is " +
                "ordinary, so whether its assignee's team owns the Signal is unknown. Refusing rather than falling back (D-134).",
                OntologyWriterFailureReason.NoCoreOwnerUnresolved, ex);
        }

        return assignee is { } user && user != Guid.Empty
            ? await ResolveNoCoreOwnerAsync(
                RecordOwnershipContext.ForUserTeam(user) with { RequestedBy = requestedBy }, subjectEntity, subjectId, ct).ConfigureAwait(false)
            : owner;
    }

    /// <summary>One no-core ownership question: a resolver fault is the writer's escalation (the item's owner is unknown), never
    /// a raw fault that would end the evaluator's whole policy.</summary>
    private async Task<RecordOwnerResolution> ResolveNoCoreOwnerAsync(
        RecordOwnershipContext context, string subjectEntity, Guid subjectId, CancellationToken ct)
    {
        try
        {
            return Owned(await _ownership.ResolveOwnerAsync(context, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new SignalWriterEscalationException(
                $"The ownership resolver failed for the no-core {subjectEntity} {subjectId:D} ({ex.GetType().Name}), so its " +
                "Signal's owner is unknown. Refusing to write (D-130).",
                OntologyWriterFailureReason.NoCoreOwnerUnresolved, ex);
        }
    }

    /// <summary>An answer that names no team and is not a refusal (an opted-in "unchanged", or a secure answer with no team) is a
    /// refusal here: the Signal has no owner to take, and the writer identity never owns it.</summary>
    private static RecordOwnerResolution Owned(RecordOwnerResolution answer) =>
        answer.IsRefused || answer.IsOwned
            ? answer
            : RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "the ownership resolver named no owner team for the Signal");

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
    /// Task 039 (D-33), task 143 (D-130): the ONE ownership check for both the post-create verify and the reconcile path. A row
    /// the resolver gave to the Secure Record Owners team is checked by <c>owningteam</c>
    /// (<see cref="OntologyWriterFailureReason.SecureOwnerMismatch"/>); an ordinary row by <c>ownerid</c>, which must be the
    /// resolver's team (<see cref="OntologyWriterFailureReason.OwnerMismatch"/>, or
    /// <see cref="OntologyWriterFailureReason.NoCoreOwnerMismatch"/> for a no-core row). Pure (no I/O). A row whose owner is not
    /// the answer now (the reconciler has not yet moved it into isolation, its core record moved business unit, or it was written
    /// before task 143) escalates here, loudly, rather than being touched.
    /// </summary>
    private static void EnsureOwnershipMatches(Entity signalRow, RecordOwnerResolution owner, bool noCore, Guid signalId)
    {
        var teamId = owner.OwningTeamId!.Value;
        if (owner.IsSecureOwner)
        {
            var actualTeamId = signalRow.GetAttributeValue<EntityReference>("owningteam")?.Id;
            if (actualTeamId != teamId)
            {
                throw new SignalWriterEscalationException(
                    $"Signal {signalId:D}'s owningteam ({(actualTeamId is { } id ? id.ToString("D") : "null")}) is not " +
                    $"the Secure Record Owners team ({teamId:D}) the ownership resolver chose. Refusing to leave a secure " +
                    "record's Signal with another owner (D-33).",
                    OntologyWriterFailureReason.SecureOwnerMismatch);
            }

            return;
        }

        var actualOwner = signalRow.GetAttributeValue<EntityReference>("ownerid");
        if (actualOwner is null || actualOwner.LogicalName != "team" || actualOwner.Id != teamId)
        {
            throw new SignalWriterEscalationException(
                $"Signal {signalId:D}'s ownerid ({(actualOwner is null ? "null" : $"{actualOwner.LogicalName} {actualOwner.Id:D}")}) is " +
                $"not the team ({teamId:D}) the ownership resolver chose. Refusing to leave a Signal with another owner (D-130).",
                noCore ? OntologyWriterFailureReason.NoCoreOwnerMismatch : OntologyWriterFailureReason.OwnerMismatch);
        }
    }

    private static Entity BuildEntity(
        SignalWriteRequest request,
        CorePlan core,
        Guid? coreTypeRefId,
        RecordOwnerResolution owner,
        DateTime? dueDate,
        string sentence,
        DateTimeOffset nowUtc)
    {
        // The subject lookup and the ADR-024 pair are NOT written here: the shared regarding builder writes them on this
        // entity in WriteAsync (task 141, D-133).
        var entity = new Entity(SignalEntity);

        if (!string.IsNullOrWhiteSpace(request.SubjectRecordNumber))
        {
            // Task 031 (R13): ADR-024's fifth resolver field, from the field the subject's catalog row names.
            entity["sprk_regardingrecordnumber"] = request.SubjectRecordNumber;
        }

        if (core.Entity is not null)
        {
            // D-36: the generic core-record pair (catalog type + id); sprk_matter as well only when the core is a matter. Task 145:
            // sprk_matter is decision CONTEXT (the frozen core, an owner parent in the lineage), never the filing; the filing is the
            // subject lookup plus the live sprk_regarding{core} stamp, which follows the subject (144 review K1).
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

        // Task 143 (D-130): the resolver's team IN the create, on every path (secure, ordinary core, no-core), written by uac-r2's
        // own ApplyTo (the one censused owner write; the writer holds Assign), which also stamps the person who asked (D-135).
        // owningbusinessunit is never sent: it derives from the team. The caller passes only an owned answer.
        owner.ApplyTo(entity);
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
        // The SDK hands Money and Decimal columns back at scale 10 (live spaarkedev1: 25000.0000000000, 1.2500000000), so
        // trim the trailing zeros: a sentence reads "25000" and "94.5", not the stored scale.
        decimal number => Normalize(number).ToString(CultureInfo.InvariantCulture),
        Money money => Normalize(money.Value).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Dividing by 1.000...0 (28 places) drops a decimal's trailing zeros without changing its value.</summary>
    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    private static string RequireSubjectEntity(string? subjectEntityLogicalName) =>
        InvalidSubjectEntity(subjectEntityLogicalName) is { } invalid ? throw invalid : subjectEntityLogicalName!;

    /// <summary>The refusal for a subject name that is not a Dataverse logical name, or null when it is one. The one definition
    /// <see cref="RequireSubjectEntity"/> throws and <see cref="CheckRequest"/> returns.</summary>
    private static ArgumentException? InvalidSubjectEntity(string? subjectEntityLogicalName) =>
        string.IsNullOrWhiteSpace(subjectEntityLogicalName) || !LogicalName().IsMatch(subjectEntityLogicalName)
            ? new ArgumentException(
                $"SubjectEntityLogicalName '{subjectEntityLogicalName}' is not a Dataverse logical name " +
                "(expected ^[a-z][a-z0-9_]*$).", nameof(subjectEntityLogicalName))
            : null;

    /// <summary>The escalation for a subject table that is not in <see cref="VerifiedSubjects"/>, or null when it is. The one definition
    /// <see cref="ResolveCoreAsync"/> throws and <see cref="CheckRequest"/> returns.</summary>
    private static SignalWriterEscalationException? UnverifiedSubject(string subjectEntity) =>
        VerifiedSubjects.Contains(subjectEntity)
            ? null
            : new SignalWriterEscalationException(
                $"Cannot group a Signal for subject type '{subjectEntity}': it is not a verified subject " +
                "(SignalWriter.VerifiedSubjects). Refusing to write a Signal with no derivable core record " +
                "(task 030/037 escalation trigger). Widening this set is a schema check plus a code change with review.",
                OntologyWriterFailureReason.MatterDerivationUnverified);

    /// <summary>One request refusal: the stable <see cref="SignalWriteRefusal"/> code and the exception <see cref="WriteAsync"/> throws for it.</summary>
    private sealed record RequestRefusal(string Code, Exception Error);

    /// <summary>
    /// Whether <see cref="WriteAsync"/> would refuse <paramref name="request"/> before any Dataverse call: null when it would not,
    /// otherwise the stable <see cref="SignalWriteRefusal"/> code. Pure: no I/O, no logging, no metering, never throws for an invalid
    /// request (an invalid request is the answer). A caller that must not act on a Signal the writer would refuse (an automated
    /// decision) calls this instead of copying the checks.
    /// </summary>
    /// <remarks>
    /// It runs <see cref="CheckRequest"/>, the SAME method <see cref="WriteAsync"/> runs first, so the two cannot drift. Checked, in
    /// <see cref="WriteAsync"/>'s order: the request is not null; the subject name is a logical name; <c>SubjectId</c> is set;
    /// <c>PolicyCode</c> is set and at most 50 characters; <c>ShortHeadline</c> is set and at most 200; <c>Lane</c>, <c>Severity</c>
    /// and <c>Episode</c> are valid; the template is set, <c>FactValues</c> is set, the template renders strictly from the snapshot
    /// (§0.3), and the rendered sentence is at most 2000 characters; the subject table is in <see cref="VerifiedSubjects"/>.
    /// <para><b>Not checked here, because each needs I/O:</b> the core record is derivable and unambiguous (the core-ancestor
    /// resolver reads Dataverse); the subject has an owner (the ownership resolver); the core record's table has a catalog row; the
    /// subject lookup and regarding pair can be built (the catalog read); the owner reads back on the created row; the create or
    /// reconcile succeeds. A request this returns null for can still be refused by <see cref="WriteAsync"/> for one of those.</para>
    /// </remarks>
    internal static string? PreflightRefusal(SignalWriteRequest request) => CheckRequest(request, out _)?.Code;

    /// <summary>
    /// The writer's I/O-free request checks, the ONE definition <see cref="WriteAsync"/> throws from and
    /// <see cref="PreflightRefusal"/> maps to a code. Returns the first failing check in order, or null. On null,
    /// <paramref name="renderedSentence"/> is the rendered <c>sprk_sentence</c> (so the write does not render twice).
    /// </summary>
    private static RequestRefusal? CheckRequest(SignalWriteRequest? request, out string? renderedSentence)
    {
        renderedSentence = null;
        if (request is null)
            return new(SignalWriteRefusal.RequestMissing, new ArgumentNullException(nameof(request)));
        if (InvalidSubjectEntity(request.SubjectEntityLogicalName) is { } invalidSubject)
            return new(SignalWriteRefusal.SubjectEntityInvalid, invalidSubject);
        if (request.SubjectId == Guid.Empty)
            return new(SignalWriteRefusal.SubjectIdEmpty, new ArgumentException("SubjectId must not be Guid.Empty.", nameof(request)));
        if (string.IsNullOrWhiteSpace(request.PolicyCode))
            return new(SignalWriteRefusal.PolicyCodeMissing,
                new ArgumentException("PolicyCode is required (part of the dedupe key).", nameof(request)));
        if (request.PolicyCode.Length > PolicyCodeMaxLength)
            return new(SignalWriteRefusal.PolicyCodeTooLong, new ArgumentException(
                $"PolicyCode is {request.PolicyCode.Length} characters; sprk_signal.sprk_policycode's limit is {PolicyCodeMaxLength}.",
                nameof(request)));
        if (string.IsNullOrWhiteSpace(request.ShortHeadline))
            return new(SignalWriteRefusal.HeadlineMissing,
                new ArgumentException("ShortHeadline is required (sprk_name is NOT NULL on sprk_signal).", nameof(request)));
        if (request.ShortHeadline.Length > NameMaxLength)
            return new(SignalWriteRefusal.HeadlineTooLong, new ArgumentException(
                $"ShortHeadline is {request.ShortHeadline.Length} characters; sprk_signal.sprk_name's limit is {NameMaxLength}.",
                nameof(request)));
        if (!ValidLanes.Contains(request.Lane))
            return new(SignalWriteRefusal.LaneInvalid,
                new ArgumentException($"Lane {request.Lane} is not a valid sprk_signal.sprk_lane option value.", nameof(request)));
        if (request.Severity is { } sev && !ValidSeverities.Contains(sev))
            return new(SignalWriteRefusal.SeverityInvalid,
                new ArgumentException($"Severity {sev} is not a valid sprk_signal.sprk_severity option value.", nameof(request)));
        if (request.Episode < 1)
            return new(SignalWriteRefusal.EpisodeInvalid,
                new ArgumentException($"Episode {request.Episode} is not a whole number from 1 (D-13).", nameof(request)));

        // R2 (second independent review): an unrenderable template is a logged, metered refusal in WriteAsync, so it is returned here
        // as the exception the write's catch clause reads. RenderSentence's own blank-template / null-snapshot guards are argument
        // errors (ParamName names which).
        string sentence;
        try
        {
            sentence = RenderSentence(request.MessageTemplate, request.FactValues);
        }
        catch (SignalSentenceTemplateException ex)
        {
            return new(SignalWriteRefusal.SentenceTemplateInvalid, ex);
        }
        catch (ArgumentException ex)
        {
            return new(ex.ParamName == "factValues" ? SignalWriteRefusal.FactValuesMissing : SignalWriteRefusal.TemplateMissing, ex);
        }

        if (sentence.Length > SentenceMaxLength)
            return new(SignalWriteRefusal.SentenceTooLong, new ArgumentException(
                $"The rendered sentence is {sentence.Length} characters; sprk_signal.sprk_sentence's limit is {SentenceMaxLength}.",
                nameof(request)));
        if (UnverifiedSubject(request.SubjectEntityLogicalName) is { } unverified)
            return new(SignalWriteRefusal.SubjectNotVerified, unverified);

        renderedSentence = sentence;
        return null;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}")]
    private static partial Regex SentenceToken();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();
}
