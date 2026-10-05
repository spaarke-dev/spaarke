using System.Collections.Frozen;
using System.Globalization;
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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
/// <param name="GroupingMatterId">The resolved <c>sprk_matter</c> — always populated (D-3).</param>
/// <param name="OwningBusinessUnitId">The business unit <c>sprk_signal.owningbusinessunit</c> was set to at
/// create (FR-14). <c>Guid.Empty</c> on a re-evaluation update, where ownership is not touched.</param>
public sealed record SignalWriteResult(Guid SignalId, bool Created, Guid GroupingMatterId, Guid OwningBusinessUnitId);

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
/// <para><b>Grouping matter — a CLOSED, VERIFIED set, like <c>PredicateCompiler.VerifiedJoins</c>.</b>
/// <see cref="RegardingLookupByEntity"/> lists all 9 subject types <c>sprk_signal</c> can point at (it is a
/// schema fact — every lookup exists today). <see cref="VerifiedMatterDerivation"/> is deliberately smaller: it
/// names, for each subject type, HOW to derive the grouping matter, and today that is verified for exactly two
/// — <c>sprk_matter</c> (the subject IS the matter) and <c>sprk_communication</c>
/// (<c>sprk_communication.sprk_regardingmatter</c>, the same lookup <c>PredicateCompiler.VerifiedJoins</c>
/// already relies on). Any other subject type ESCALATES (<see cref="SignalWriterEscalationException"/>) rather
/// than guessing a lookup field name that might not exist or might target something else entirely.</para>
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
    /// Verified grouping-matter derivation per subject type. <c>null</c> = the subject IS the matter. See the
    /// class remarks for why this is intentionally a SUBSET of <see cref="RegardingLookupByEntity"/>'s keys.
    /// </summary>
    public static readonly FrozenDictionary<string, string?> VerifiedMatterDerivation =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = null,
            ["sprk_communication"] = "sprk_regardingmatter",
        }.ToFrozenDictionary();

    private static readonly JsonSerializerOptions EvidenceRefJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OntologyWriterDataverseClient _writerClient;
    private readonly IGenericEntityService _sysadminClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalWriter> _logger;

    public SignalWriter(
        OntologyWriterDataverseClient writerClient,
        IGenericEntityService sysadminClient,
        TimeProvider timeProvider,
        ILogger<SignalWriter> logger)
    {
        _writerClient = writerClient ?? throw new ArgumentNullException(nameof(writerClient));
        _sysadminClient = sysadminClient ?? throw new ArgumentNullException(nameof(sysadminClient));
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

            var matterId = await ResolveGroupingMatterIdAsync(subjectEntity, request.SubjectId, ct).ConfigureAwait(false);
            var owningBusinessUnitId = await ResolveOwningBusinessUnitIdAsync(matterId, ct).ConfigureAwait(false);

            var nowUtc = _timeProvider.GetUtcNow();
            var dedupeKey = BuildDedupeKey(request.PolicyCode, subjectEntity, cleanSubjectId);
            var entity = BuildEntity(request, subjectEntity, cleanSubjectId, matterId, owningBusinessUnitId, sentence, nowUtc);
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
                        .RetrieveByAlternateKeyAsync("sprk_signal", keyAttributes, new[] { "sprk_signalid", "owningbusinessunit" }, ct)
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
                EnsureOwningBusinessUnitMatches(existing, owningBusinessUnitId, signalId);

                await _writerClient.UpdateAsync(
                    "sprk_signal",
                    signalId,
                    new Dictionary<string, object> { ["sprk_lastevaluated"] = nowUtc.UtcDateTime },
                    ct).ConfigureAwait(false);
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
                        .RetrieveAsync("sprk_signal", signalId, new[] { "owningbusinessunit" }, ct)
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

                EnsureOwningBusinessUnitMatches(verifyRow, owningBusinessUnitId, signalId);
            }
            else
            {
                // Ownership is NOT touched on a re-evaluation update -- report Guid.Empty rather than a value that
                // would wrongly imply this write just (re-)assigned it.
                resultOwningBu = Guid.Empty;
            }

            _logger.LogInformation(
                "Signal {SignalId} {Action} for policy {PolicyCode}, subject {SubjectEntity} {SubjectId}, matter {MatterId}.",
                signalId, created ? "created" : "reconciled (re-evaluation)", request.PolicyCode, subjectEntity, cleanSubjectId, matterId);

            return new SignalWriteResult(signalId, created, matterId, resultOwningBu);
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

    private async Task<Guid> ResolveGroupingMatterIdAsync(string subjectEntity, Guid subjectId, CancellationToken ct)
    {
        if (!VerifiedMatterDerivation.TryGetValue(subjectEntity, out var matterLookupField))
        {
            throw new SignalWriterEscalationException(
                $"Cannot derive a grouping matter for subject type '{subjectEntity}': no verified matter-" +
                "derivation path is registered for it (SignalWriter.VerifiedMatterDerivation). Refusing to " +
                "write a Signal with a null sprk_matter (task 030 escalation trigger). Widening this set is a " +
                "schema check plus a code change with review.",
                OntologyWriterFailureReason.MatterDerivationUnverified);
        }

        if (matterLookupField is null)
        {
            // The subject IS the matter (today's only PredicateCompiler-produced case: subject=sprk_matter).
            return subjectId;
        }

        if (!PredicateCompiler.EvaluatorGlobalReadableEntities.Contains(subjectEntity))
        {
            // Defense in depth: VerifiedMatterDerivation must never name an entity outside the writer's
            // verified Global-read allow-list. If it ever did, a Basic-depth read below would be silently
            // trimmed and look identical to "this row has no matter" (task 006 finding, security-roles.md §9).
            throw new SignalWriterEscalationException(
                $"Subject type '{subjectEntity}' is not in PredicateCompiler.EvaluatorGlobalReadableEntities; " +
                "refusing to read its grouping-matter lookup, which Dataverse would silently trim.",
                OntologyWriterFailureReason.MatterDerivationUnverified);
        }

        var subjectRow = await _writerClient
            .RetrieveAsync(subjectEntity, subjectId, new[] { matterLookupField }, ct)
            .ConfigureAwait(false);
        var matterRef = subjectRow.GetAttributeValue<EntityReference>(matterLookupField);

        if (matterRef is null || matterRef.Id == Guid.Empty)
        {
            throw new SignalWriterEscalationException(
                $"Cannot derive a grouping matter: {subjectEntity} {subjectId:D}'s '{matterLookupField}' " +
                "lookup is empty. Refusing to write a Signal with a null sprk_matter (task 030 escalation trigger).",
                OntologyWriterFailureReason.MatterLookupEmpty);
        }

        return matterRef.Id;
    }

    /// <summary>
    /// Reads the grouping matter's <c>owningbusinessunit</c> via the SHARED sysadmin client (F25) — a metadata
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
    private async Task<Guid> ResolveOwningBusinessUnitIdAsync(Guid matterId, CancellationToken ct)
    {
        var matterRow = await _sysadminClient
            .RetrieveAsync("sprk_matter", matterId, new[] { "owningbusinessunit" }, ct)
            .ConfigureAwait(false);
        var buRef = matterRow.GetAttributeValue<EntityReference>("owningbusinessunit");

        if (buRef is null || buRef.Id == Guid.Empty)
        {
            throw new SignalWriterEscalationException(
                $"Cannot resolve an owning business unit for grouping matter {matterId:D}: the matter has no " +
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

    private static Entity BuildEntity(
        SignalWriteRequest request,
        string subjectEntity,
        string cleanSubjectId,
        Guid matterId,
        Guid owningBusinessUnitId,
        string sentence,
        DateTimeOffset nowUtc)
    {
        if (!RegardingLookupByEntity.TryGetValue(subjectEntity, out var specificLookup))
        {
            // Unreachable given VerifiedMatterDerivation's keys are a subset of RegardingLookupByEntity's
            // (asserted by SignalWriterTests) — guarded anyway so a future edit that breaks that invariant
            // fails loudly here instead of writing an un-trimmed row.
            throw new InvalidOperationException(
                $"'{subjectEntity}' has a verified matter-derivation path but no entry in RegardingLookupByEntity. " +
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

        entity["sprk_matter"] = new EntityReference("sprk_matter", matterId);

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

        // FR-14 (F3/F22): owner is left as the writer (the default — NOT set here); owningbusinessunit is set
        // to the grouping matter's BU. Never ownerid=team — confirmed unworkable live (403 0x80040299).
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
