using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Outcome class of a correction attempt. The endpoint maps each to one HTTP status and one stable code.</summary>
public enum CorrectionStatus
{
    Applied,
    InvalidRequest,
    CodeNotInSet,
    InvoiceNotFound,
    LineNotFound,
    NotInReview,
    LineNotCorrectable,
    NoProvenance,
    CodeSetUnavailable,
    Changed,
    OwnerUnresolved,

    /// <summary>A read or a write failed. The correction row may already be written; repeating the same request completes the value write
    /// without a second row.</summary>
    Unavailable,

    /// <summary>The correction row and the value are written but the follow-through after them (the <c>OnValueWrittenAsync</c> seam) did not
    /// finish. Repeating the same request writes no second row and runs the follow-through again.</summary>
    Incomplete,
}

/// <summary>Stable ProblemDetails codes of the correction routes (ADR-019). Callers branch on these, never on a message.</summary>
public static class CorrectionCodes
{
    public const string InvalidRequest = LineDecisionCodes.InvalidRequest;
    public const string NotInReview = LineDecisionCodes.NotInReview;
    public const string LinePending = LineDecisionCodes.LinePending;
    public const string LineChanged = LineDecisionCodes.LineChanged;
    public const string OwnerUnresolved = LineDecisionCodes.OwnerUnresolved;
    public const string Unavailable = "ebilling.correction.unavailable";

    /// <summary>The correction is saved but the step after it did not finish; repeat the same request to complete it (503, with <c>failedStep</c>).</summary>
    public const string Incomplete = "ebilling.correction.incomplete";

    /// <summary>The <c>failedStep</c> of an <see cref="Incomplete"/> answer: the re-evaluation that follows a saved value.</summary>
    public const string ReevaluationStep = "reevaluation";
    public const string ReviewerUnresolved = LineDecisionCodes.DeciderUnresolved;

    /// <summary>FR-22: the code is not in the line's resolved code set; nothing was written.</summary>
    public const string CodeNotInSet = "ebilling.code.not_in_set";

    public const string FieldUnknown = "ebilling.correction.field_unknown";
    public const string FieldNotApplicable = "ebilling.correction.field_not_applicable";

    /// <summary>B2 item: a timekeeper or date correction needs the rate re-resolved, which a later task does. Refused with this stable code.</summary>
    public const string FieldNotSupportedYet = "ebilling.correction.field_not_supported_yet";

    public const string ValueRequired = "ebilling.correction.value_required";
    public const string ValueInvalid = "ebilling.correction.value_invalid";
    public const string NothingToConfirm = "ebilling.correction.nothing_to_confirm";
    public const string LineNotCorrectable = "ebilling.correction.line_not_correctable";
    public const string NoProvenance = "ebilling.correction.no_provenance";
    public const string CodeSetUnavailable = "ebilling.correction.code_set_unavailable";
    public const string InvoiceChanged = "ebilling.invoice.changed";
}

/// <summary>
/// One correction request. <paramref name="LineId"/> is the route's line for a line field and null for a header field. The reviewer is the
/// caller's <c>systemuserid</c> from their own token, never the body.
/// </summary>
/// <param name="Value">The final value as text (an invariant number, a <c>yyyy-MM-dd</c> date, a code or free text). Absent when <paramref name="Confirm"/>.</param>
/// <param name="Confirm">Keep the value as read: one Confirmed individually row (D-64) when it still equals what the AI read.</param>
public sealed record CorrectionCommand(Guid InvoiceId, Guid? LineId, string? Field, string? Value, bool Confirm, Guid ReviewerSystemUserId);

/// <summary>One correction row as the History pane and the calibration report read it. Values are the caller's to see (ADR-015: never logged).</summary>
public sealed record CorrectionDto(
    Guid Id,
    Guid? LineId,
    string Field,
    string? AiValue,
    string? FinalValue,
    string Outcome,
    bool IsLabel,
    decimal? AiConfidence,
    string? SourceCode,
    string? SourceVersion,
    string? ReviewerRole,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedOn);

public sealed record CorrectionResult(
    CorrectionStatus Status, string Code, string? Detail = null, CorrectionDto? Correction = null, bool AlreadyApplied = false, string? FailedStep = null)
{
    public bool IsApplied => Status == CorrectionStatus.Applied;
}

/// <summary>
/// What a successful correction changed, handed to <see cref="InvoiceCorrectionService.OnValueWrittenAsync"/>. It carries ids, the canonical
/// field name and the outcome only (ADR-015: no value).
/// </summary>
public sealed record CorrectionApplied(Guid InvoiceId, Guid? LineId, string Field, string Outcome, bool ValueChanged);

/// <summary>
/// Reviewer corrections of extracted values and AI codes (ebilling-r1 task 101 part B1; FR-31, FR-22, D-64, EB-D-170, EB-D-171, NFR-09).
/// Writes one D-64 correction row per field (Corrected or Confirmed individually, both labels), then the corrected value; reads the rows back
/// for the History pane and the calibration report. Kept strictly separate from Reduce (task 100): a correction never touches a line's
/// status, approved amount or decision fields.
/// </summary>
/// <remarks>
/// <para><b>Order and exactly-once.</b> Validate everything, then (1) the correction row, a deterministic-id upsert, then (2) the value, a
/// row-version-conditional update of the line (or the invoice) at the version read in the same request: a stale version is 409
/// <c>ebilling.line.changed</c> / <c>ebilling.invoice.changed</c>. The outcome is derived, never sent: the AI value is the
/// <b>earliest prior correction row's</b> AI value for the field, else the value stored now; the outcome is Corrected when the final value
/// differs from it and Confirmed when equal. A retry therefore writes no second row: when the latest Corrected / Confirmed row of the field
/// already carries the requested final value, only a still-missing value write is done (the repeat after a failed step 2 completes it).
/// Header and line rows are separate scopes (a header row has no line).</para>
/// <para><b>Codes.</b> A code correction is accepted only if the code is in the line's resolved code set, which is the set 047 recorded in the
/// line's <c>coding.codeSetId</c> (loaded through <see cref="CodeSetResolver.LoadRecordedAsync"/>; no second resolution order). The corrected
/// code is stored in the line's <c>coding.&lt;kind&gt;.code</c> JSON, which the read model reads (<c>mismatch</c> and <c>firmCodeMismatch</c>
/// follow it); the firm's billed code columns and the code lookups are not written.</para>
/// <para><b>Source and version (EB-D-170).</b> The row names the analyzer id and API version (extracted fields) or the Action code and version
/// (codes) from the <c>provenance</c> the persister stored. A field whose AI value cannot be attributed to a source version (a line or
/// submission stored before EB-D-170) answers 409 <c>ebilling.correction.no_provenance</c> rather than writing a blank version (the task's escalation
/// trigger); a header field's AI value is the one stored in the submission's header provenance.</para>
/// <para><b>Reviewer role (D-64, directional).</b> <c>eligible-approver</c> when the caller's OWN contact
/// (<see cref="IIdentityNormalizationService.ResolveAsync"/>, EB-D-171) is in the live eligible set of the invoice's single core record
/// (<c>EligibleApproverResolver.SingleCoreOf</c>, #2455); otherwise <c>invoice-reviewer</c>, also when the lookup fails (logged).</para>
/// <para><b>Not here (part B2).</b> Re-running the billing rules, the Signal reconcile, <c>findings[]</c> / <c>signalPlan[]</c> in the stored
/// findings, line status and the totals follow-through. They hook in at <see cref="OnValueWrittenAsync"/>. Timekeeper and date corrections
/// (the rate must be re-resolved) are refused with <see cref="CorrectionCodes.FieldNotSupportedYet"/>.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceLineDecisionService"/> writes the bulk / Unreviewed
/// correction rows and owns decisions; nothing writes a Corrected or Confirmed row or an extracted value. Extension: that service is task 100's
/// and decides lines, the rows here are a different write (a value, not a decision), so the row builder is repeated in miniature and
/// the shared helpers (single core, deterministic ids, field names) are reused. Cost of doing nothing: a misread value cannot be fixed and the
/// D-47 calibration report has no labels (FR-31).</para>
/// </remarks>
public sealed class InvoiceCorrectionService
{
    public const string ReviewerRole = InvoiceLineDecisionService.DeciderRole;
    public const string ApproverRole = "eligible-approver";

    internal const int OutcomeCorrected = 100000000;
    internal const int OutcomeConfirmed = 100000001;
    internal const string CorrectedLabel = "Corrected";
    internal const string ConfirmedLabel = "Confirmed";

    private const int ValueMaxLength = 4000;
    private const decimal AbsoluteLimit = 1_000_000_000m;

    internal delegate Task<EligibleApproverSet> ResolveApprovers(string coreEntity, Guid coreId, CancellationToken ct);

    private enum Kind { Decimal, Money, Text, Date, Currency, Code }

    private sealed record Spec(
        string Name, Kind Kind, string? Column, bool Header, bool FeeOnly = false, bool ExpenseOnly = false, string? CodeMember = null,
        int MaxLength = ValueMaxLength, bool NonNegative = false);

    private static readonly Spec[] Specs =
    [
        new(ResolutionFieldNames.Hours, Kind.Decimal, InvoiceSubmissionPersister.LineQuantity, Header: false, FeeOnly: true),
        new(ResolutionFieldNames.Rate, Kind.Money, InvoiceSubmissionPersister.LineRate, Header: false, FeeOnly: true, NonNegative: true),
        new(ResolutionFieldNames.Amount, Kind.Money, InvoiceTotalsService.LineAmount, Header: false),
        new(BillingFieldNames.Narrative, Kind.Text, InvoiceSubmissionPersister.LineDescription, Header: false, MaxLength: 2000), // sprk_billingevent.sprk_description is Multiline Text 2000
        new(BillingFieldNames.TaskCode, Kind.Code, null, Header: false, FeeOnly: true, CodeMember: "task"),
        new(BillingFieldNames.ActivityCode, Kind.Code, null, Header: false, FeeOnly: true, CodeMember: "activity"),
        new(BillingFieldNames.ExpenseCode, Kind.Code, null, Header: false, ExpenseOnly: true, CodeMember: "expense"),
        new(BillingFieldNames.FirmInvoiceNumber, Kind.Text, InvoiceReviewService.InvInvoiceNumber, Header: true, MaxLength: 100),
        new(BillingFieldNames.BillingPeriodStart, Kind.Date, InvoiceSubmissionPersister.InvoiceBillingPeriodStart, Header: true),
        new(BillingFieldNames.BillingPeriodEnd, Kind.Date, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd, Header: true),
        new(BillingFieldNames.Currency, Kind.Currency, InvoiceSubmissionPersister.InvoiceCurrency, Header: true),
        new(ResolutionFieldNames.TotalDue, Kind.Money, InvoiceReviewService.InvTotalAmount, Header: true),
    ];

    /// <summary>Fields a later task corrects: the rate must be re-resolved from them (the roster timekeeper and the date drive the approved rate).</summary>
    private static readonly HashSet<string> NotSupportedYet = new(StringComparer.OrdinalIgnoreCase)
    {
        ResolutionFieldNames.Date, ResolutionFieldNames.TimekeeperAsBilled, BillingFieldNames.InvoiceDate,
    };

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.VersionNumber,
        InvoiceReviewService.InvInvoiceNumber, InvoiceSubmissionPersister.InvoiceBillingPeriodStart, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd,
        InvoiceSubmissionPersister.InvoiceCurrency, InvoiceReviewService.InvTotalAmount,
        "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
        CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
        "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
    ];

    private static readonly string[] CorrectionColumns =
    [
        "sprk_fieldname", "sprk_aivalue", "sprk_finalvalue", "sprk_aiconfidence", "sprk_outcometype", "sprk_islabel",
        "sprk_sourcecode", "sprk_sourceversion", "sprk_reviewerrole", "sprk_reviewedon", "sprk_reviewedby", "sprk_invoiceline", "createdon",
    ];

    private readonly IGenericEntityService _entities;
    private readonly CodeSetResolver _codeSets;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IIdentityNormalizationService _identity;
    private readonly ResolveApprovers _approvers;
    private readonly ILogger<InvoiceCorrectionService> _logger;
    private readonly TimeProvider _time;

    public InvoiceCorrectionService(
        IGenericEntityService entities,
        CodeSetResolver codeSets,
        IRecordOwnershipResolver ownership,
        IIdentityNormalizationService identity,
        EligibleApproverResolver approvers,
        ILogger<InvoiceCorrectionService> logger)
        : this(entities, codeSets, ownership, identity,
            (approvers ?? throw new ArgumentNullException(nameof(approvers))).ResolveAsync, logger)
    {
    }

    /// <summary>The seam the unit tests use: the approver resolver is concrete, so its live resolution is passed as its call.</summary>
    internal InvoiceCorrectionService(
        IGenericEntityService entities,
        CodeSetResolver codeSets,
        IRecordOwnershipResolver ownership,
        IIdentityNormalizationService identity,
        ResolveApprovers approvers,
        ILogger<InvoiceCorrectionService> logger,
        TimeProvider? time = null,
        Func<CorrectionApplied, CancellationToken, Task>? onValueWritten = null)
    {
        _onValueWritten = onValueWritten;
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _codeSets = codeSets ?? throw new ArgumentNullException(nameof(codeSets));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _approvers = approvers ?? throw new ArgumentNullException(nameof(approvers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    // ── The B2 seam ──

    /// <summary>
    /// PART B2 HOOKS IN HERE. Called once per successful request after the correction row and the value are written (also on a repeat that
    /// finds both already written, so a re-evaluation that failed after the first attempt is run again). B2 re-evaluates the billing rules
    /// for the affected line(s), reconciles the Signals, patches <c>findings[]</c> / <c>signalPlan[]</c> in <c>sprk_findingsjson</c>, sets the
    /// line status Clean or Flagged and recomputes the totals. B1 leaves it empty: a correction in B1 does not change a line's status or Signals.
    /// </summary>
    internal Task OnValueWrittenAsync(CorrectionApplied applied, CancellationToken ct) =>
        _onValueWritten is null ? Task.CompletedTask : _onValueWritten(applied, ct);

    /// <summary>B2a: the seam's body is passed in (the unit-test seam, and where B2b's re-evaluation is wired); null is the no-op of B1.</summary>
    private readonly Func<CorrectionApplied, CancellationToken, Task>? _onValueWritten;

    // ── The write ──

    public async Task<CorrectionResult> CorrectAsync(CorrectionCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Validate(command, out var spec) is { } refused)
        {
            return refused;
        }

        return spec!.Header
            ? await CorrectHeaderAsync(command, spec, ct).ConfigureAwait(false)
            : await CorrectLineAsync(command, spec, ct).ConfigureAwait(false);
    }

    private static CorrectionResult Bad(string code, string detail) => new(CorrectionStatus.InvalidRequest, code, detail);

    /// <summary>The request-shape rules that need no data (400). Returns the refusal, or null with the resolved field spec when the shape is valid.</summary>
    private static CorrectionResult? Validate(CorrectionCommand command, out Spec? spec)
    {
        spec = null;
        if (command.InvoiceId == Guid.Empty || command.ReviewerSystemUserId == Guid.Empty || command.LineId == Guid.Empty)
        {
            return Bad(CorrectionCodes.InvalidRequest, "An invoice id is required.");
        }

        var name = command.Field?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Bad(CorrectionCodes.FieldUnknown, "A field is required.");
        }

        if (NotSupportedYet.Contains(name))
        {
            return Bad(CorrectionCodes.FieldNotSupportedYet, "This field cannot be corrected yet.");
        }

        spec = Specs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (spec is null || spec.Header == (command.LineId is not null))
        {
            return Bad(CorrectionCodes.FieldUnknown, "This is not a field that can be corrected here.");
        }

        if (command.Confirm && command.Value is not null)
        {
            return Bad(CorrectionCodes.InvalidRequest, "Send a value or confirm, not both.");
        }

        if (!command.Confirm && string.IsNullOrWhiteSpace(command.Value))
        {
            return Bad(CorrectionCodes.ValueRequired, "A value is required unless the value is confirmed as read.");
        }

        if (!command.Confirm && spec.Kind != Kind.Code && !TryNormalize(spec, command.Value!, out _, out var detail))
        {
            return Bad(CorrectionCodes.ValueInvalid, detail);
        }

        return null;
    }

    private async Task<CorrectionResult> CorrectLineAsync(CorrectionCommand command, Spec spec, CancellationToken ct)
    {
        Entity? invoice;
        Entity? line;
        List<Entity> prior;
        try
        {
            invoice = await ReadInvoiceAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new CorrectionResult(CorrectionStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            if (invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
            {
                return new CorrectionResult(CorrectionStatus.NotInReview, CorrectionCodes.NotInReview, "Values can be corrected only while the invoice is In Review.");
            }

            line = await ReadLineAsync(command.InvoiceId, command.LineId!.Value, ct).ConfigureAwait(false);
            if (line is null)
            {
                return new CorrectionResult(CorrectionStatus.LineNotFound, "ebilling.line.not_found");
            }

            prior = await ReadPriorAsync(command.InvoiceId, command.LineId, spec.Name, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        if (InvoiceLineDecisionService.SingleCoreOf(invoice) is not { } core)
        {
            return NoSingleCore(command.InvoiceId);
        }

        var type = line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value;
        var status = line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
        if (type is not (int)InvoiceLineCostType.Fee and not (int)InvoiceLineCostType.Expense)
        {
            return new CorrectionResult(CorrectionStatus.LineNotCorrectable, CorrectionCodes.LineNotCorrectable, "An Adjustment line is computed, not read.");
        }

        if (status == (int)InvoiceLineStatus.Pending)
        {
            return new CorrectionResult(CorrectionStatus.LineNotCorrectable, CorrectionCodes.LinePending, "A line still being processed cannot be corrected.");
        }

        if (status is not ((int)InvoiceLineStatus.Clean or (int)InvoiceLineStatus.Flagged))
        {
            return new CorrectionResult(CorrectionStatus.LineNotCorrectable, CorrectionCodes.LineNotCorrectable, "Only a line that is not yet decided can be corrected.");
        }

        if ((spec.FeeOnly && type != (int)InvoiceLineCostType.Fee) || (spec.ExpenseOnly && type != (int)InvoiceLineCostType.Expense))
        {
            return new CorrectionResult(CorrectionStatus.InvalidRequest, CorrectionCodes.FieldNotApplicable, "This field does not apply to this kind of line.");
        }

        if (!line.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
        {
            return new CorrectionResult(CorrectionStatus.Unavailable, CorrectionCodes.Unavailable, "The line version could not be read; nothing was changed.");
        }

        var extractionJson = line.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence);
        JsonNode? extraction = null;
        if (!string.IsNullOrWhiteSpace(extractionJson))
        {
            try
            {
                extraction = JsonNode.Parse(extractionJson);
            }
            catch (JsonException)
            {
                extraction = null;
            }
        }

        // The value stored now, as text, and (for a code) the set it must belong to.
        var current = spec.Kind == Kind.Code ? CodeOf(extraction, spec.CodeMember!) : CurrentText(spec, line);
        string? finalText;
        if (spec.Kind == Kind.Code)
        {
            if (command.Confirm)
            {
                finalText = current;
            }
            else
            {
                var checkedCode = await CheckCodeAsync(spec, command.Value!, extraction, ct).ConfigureAwait(false);
                if (checkedCode.Failure is { } failure)
                {
                    return failure;
                }

                finalText = checkedCode.Canonical;
            }
        }
        else if (command.Confirm)
        {
            finalText = current;
        }
        else
        {
            TryNormalize(spec, command.Value!, out finalText, out _);
        }

        if (finalText is null)
        {
            return Bad(CorrectionCodes.NothingToConfirm, "There is no stored value to confirm.");
        }

        // Where the AI value came from (EB-D-170). The attributed source is required whenever the AI produced a value, or the analyzer read the field.
        var source = LineSource(spec, extraction, extractionJson, current);
        if (source is null)
        {
            return new CorrectionResult(
                CorrectionStatus.NoProvenance, CorrectionCodes.NoProvenance,
                "This line was stored before the AI source was recorded, so its value cannot be attributed to a version; nothing was changed.");
        }

        return await ApplyAsync(
            command, spec, invoice, core, line.Id, LineName(spec, line), line, version, InvoiceReviewReadService.LineEntity,
            current, finalText, source, prior, extraction, ct).ConfigureAwait(false);
    }

    private async Task<CorrectionResult> CorrectHeaderAsync(CorrectionCommand command, Spec spec, CancellationToken ct)
    {
        Entity? invoice;
        List<Entity> prior;
        string? findingsJson;
        try
        {
            invoice = await ReadInvoiceAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new CorrectionResult(CorrectionStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            if (invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
            {
                return new CorrectionResult(CorrectionStatus.NotInReview, CorrectionCodes.NotInReview, "Values can be corrected only while the invoice is In Review.");
            }

            prior = await ReadPriorAsync(command.InvoiceId, null, spec.Name, ct).ConfigureAwait(false);
            findingsJson = await ReadFindingsJsonAsync(command.InvoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        if (InvoiceLineDecisionService.SingleCoreOf(invoice) is not { } core)
        {
            return NoSingleCore(command.InvoiceId);
        }

        if (!invoice.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
        {
            return new CorrectionResult(CorrectionStatus.Unavailable, CorrectionCodes.Unavailable, "The invoice version could not be read; nothing was changed.");
        }

        var provenance = ExtractionProvenance.ParseSubmission(findingsJson);
        if (provenance is not { HasExtraction: true })
        {
            return new CorrectionResult(
                CorrectionStatus.NoProvenance, CorrectionCodes.NoProvenance,
                "This invoice was stored before the AI source was recorded, so its value cannot be attributed to a version; nothing was changed.");
        }

        // The AI value of a header field is the one the analyzer read (kept in the header provenance, because a correction overwrites the column).
        var stored = CurrentText(spec, invoice);
        string? read = stored;
        decimal? confidence = null;
        if (provenance.Header.TryGetValue(spec.Name, out var field))
        {
            read = field.Value;
            confidence = field.Combined ?? field.Confidence;
        }

        string? finalText;
        if (command.Confirm)
        {
            finalText = stored;
        }
        else
        {
            TryNormalize(spec, command.Value!, out finalText, out _);
        }

        if (finalText is null)
        {
            return Bad(CorrectionCodes.NothingToConfirm, "There is no stored value to confirm.");
        }

        var source = new Source(provenance.AnalyzerId, provenance.ApiVersion, confidence);
        return await ApplyAsync(
            command, spec, invoice, core, lineId: null, $"{spec.Name} - invoice", invoice, version, InvoiceReviewReadService.InvoiceEntity,
            stored, finalText, source, prior, extraction: null, ct, headerAiValue: read).ConfigureAwait(false);
    }

    private sealed record Source(string? Code, string? Version, decimal? Confidence);

    /// <summary>The line field's source: the analyzer pair for an extracted field, the Action pair for a code. Null when the AI value cannot be attributed.</summary>
    private static Source? LineSource(Spec spec, JsonNode? extraction, string? extractionJson, string? currentCode)
    {
        var provenance = ExtractionProvenance.ParseLine(extractionJson);
        if (spec.Kind == Kind.Code)
        {
            var confidence = DecimalOf(extraction?["coding"]?["confidence"]);
            if (currentCode is null)
            {
                // The AI offered no code: nothing to attribute.
                return new Source(null, null, null);
            }

            return provenance is { ActionCode: { Length: > 0 } code, ActionVersion: { } version }
                ? new Source(code, version.ToString(CultureInfo.InvariantCulture), confidence)
                : null;
        }

        if (provenance is not { HasExtraction: true })
        {
            return null;
        }

        var field = extraction?["fields"]?[spec.Name];
        return new Source(provenance.AnalyzerId, provenance.ApiVersion, DecimalOf(field?["combined"]) ?? DecimalOf(field?["confidence"]));
    }

    private async Task<CorrectionResult> ApplyAsync(
        CorrectionCommand command, Spec spec, Entity invoice, (string Entity, Guid Id) core, Guid? lineId, string rowName, Entity target, long version,
        string targetEntity, string? current, string finalText, Source source, List<Entity> prior, JsonNode? extraction, CancellationToken ct,
        string? headerAiValue = null)
    {
        // The AI value: the earliest prior row's, else what the AI read (a header field: the stored header provenance), else the value stored now.
        var ordered = prior.OrderBy(WhenOf).ThenBy(r => r.GetAttributeValue<DateTime?>("createdon")).ToList();
        string? aiText;
        if (ordered.Count > 0)
        {
            aiText = ordered[0].GetAttributeValue<string>("sprk_aivalue");
        }
        else
        {
            aiText = spec.Header ? headerAiValue : current;
        }

        var outcome = aiText is not null && Same(spec.Kind, aiText, finalText) ? OutcomeConfirmed : OutcomeCorrected;
        var valueChanged = current is null || !Same(spec.Kind, current, finalText);

        // A retry: the latest Corrected / Confirmed row of this field already has this final value, so no second row; a value write still missing is done.
        var latest = ordered.LastOrDefault(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value is OutcomeCorrected or OutcomeConfirmed);
        var rowExists = latest is not null && latest.GetAttributeValue<string>("sprk_finalvalue") is { } latestFinal && Same(spec.Kind, latestFinal, finalText);

        // The correction row (when this request writes one) and the value update go in ONE transaction: row first, then the line / invoice update
        // conditioned on the row version read in this request. A stale version (409) rolls the row back, so no label is ever left for a value that
        // was not applied (D-64 rows are append-only). The row id is deterministic (field, version read, final value), so a repeat upserts the same row.
        var requests = new List<OrganizationRequest>(2);
        CorrectionDto dto;
        if (rowExists)
        {
            dto = ToDto(latest!);
        }
        else
        {
            var role = await RoleOfAsync(command.InvoiceId, command.ReviewerSystemUserId, core, ct).ConfigureAwait(false);
            var reviewedOn = _time.GetUtcNow();
            var row = BuildRow(
                command, spec, invoice, core, lineId, rowName, version, aiText, finalText, outcome, source, role, reviewedOn.UtcDateTime);
            if (await StampOwnerAsync(row, command.InvoiceId, command.ReviewerSystemUserId, ct).ConfigureAwait(false) is { } ownerFailure)
            {
                return ownerFailure;
            }

            requests.Add(new UpsertRequest { Target = row });
            dto = ToDto(row);
        }

        if (valueChanged)
        {
            requests.Add(ConditionalValueUpdate(targetEntity, target.Id, version, ValueFields(spec, finalText, extraction)));
        }

        if (requests.Count > 0)
        {
            try
            {
                await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (valueChanged && DataverseServiceClientImpl.IsRowVersionMismatch(ex))
            {
                var changed = lineId is null ? CorrectionCodes.InvoiceChanged : CorrectionCodes.LineChanged;
                return new CorrectionResult(
                    CorrectionStatus.Changed, changed, "The record changed while the correction was prepared. Reload and correct again.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // All or nothing; a timeout can land after the commit (outcome unknown): the same request again writes no second row and finishes any missing value.
                _logger.LogError("Correction of {Field} on invoice {InvoiceId}: the write failed ({ExceptionType}); the outcome may be unknown.",
                    spec.Name, command.InvoiceId, ex.GetType().Name);
                return new CorrectionResult(
                    CorrectionStatus.Unavailable, CorrectionCodes.Unavailable, "The correction could not be saved. Repeat the same request.");
            }
        }

        _logger.LogInformation("Correction of {Field} on invoice {InvoiceId} line {LineId}: outcome {Outcome}, row written {RowWritten}, value changed {ValueChanged}.",
            spec.Name, command.InvoiceId, lineId, dto.Outcome, !rowExists, valueChanged);

        // The follow-through runs on EVERY successful pass, a repeat that wrote no second row included: a re-evaluation that failed after the first
        // attempt is run again by the same request. A failure is never reported as success (503 Incomplete); the repeat completes it.
        try
        {
            await OnValueWrittenAsync(new CorrectionApplied(command.InvoiceId, lineId, spec.Name, dto.Outcome, valueChanged), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Correction of {Field} on invoice {InvoiceId}: saved, but the step after it failed ({ExceptionType}); the caller repeats the same request to finish it.",
                spec.Name, command.InvoiceId, ex.GetType().Name);
            return new CorrectionResult(
                CorrectionStatus.Incomplete, CorrectionCodes.Incomplete,
                "The correction was saved but a later step did not finish. Repeat the same request to complete it.",
                FailedStep: CorrectionCodes.ReevaluationStep);
        }

        return new CorrectionResult(CorrectionStatus.Applied, "ok", Correction: dto, AlreadyApplied: rowExists && !valueChanged);
    }

    // ── Values ──

    /// <summary>Validates and normalizes a typed value to the canonical text the rows keep. Code values are checked against the set elsewhere.</summary>
    private static bool TryNormalize(Spec spec, string raw, out string? canonical, out string detail)
    {
        canonical = null;
        detail = "The value is not valid for this field.";
        var text = raw.Trim();
        switch (spec.Kind)
        {
            case Kind.Decimal:
                if (!TryDecimal(text, out var number))
                {
                    return false;
                }

                canonical = CanonicalDecimal(decimal.Round(number, 2, MidpointRounding.AwayFromZero)); // sprk_quantity is Decimal precision 2
                return true;

            case Kind.Money:
                if (!TryDecimal(text, out var money) || (spec.NonNegative && money < 0m))
                {
                    return false;
                }

                canonical = decimal.Round(money, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
                return true;

            case Kind.Date:
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year < 1900 || date.Year > 2100)
                {
                    detail = "The date must be yyyy-MM-dd.";
                    return false;
                }

                canonical = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;

            case Kind.Currency:
                if (text.Length != 3 || !text.All(char.IsAsciiLetter))
                {
                    detail = "The currency must be a three-letter code.";
                    return false;
                }

                canonical = text.ToUpperInvariant();
                return true;

            default:
                if (text.Length == 0 || text.Length > spec.MaxLength)
                {
                    detail = $"The value is required and at most {spec.MaxLength} characters.";
                    return false;
                }

                canonical = text;
                return true;
        }
    }

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
        && Math.Abs(value) <= AbsoluteLimit;

    private static string CanonicalDecimal(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The value stored in the row's column as the canonical text, or null when it holds none.</summary>
    private static string? CurrentText(Spec spec, Entity row)
    {
        if (spec.Column is null || !row.Contains(spec.Column) || row[spec.Column] is not { } raw)
        {
            return null;
        }

        var text = (spec.Kind, raw) switch
        {
            (Kind.Money, Money m) => m.Value.ToString("0.00", CultureInfo.InvariantCulture),
            (Kind.Date, DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (Kind.Decimal, decimal d) => CanonicalDecimal(decimal.Round(d, 4, MidpointRounding.AwayFromZero)),
            (Kind.Decimal, double d) => CanonicalDecimal(decimal.Round((decimal)d, 4, MidpointRounding.AwayFromZero)),
            (Kind.Decimal, float f) => CanonicalDecimal(decimal.Round((decimal)f, 4, MidpointRounding.AwayFromZero)),
            (_, string s) => s.Trim(),
            _ => raw.ToString(),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool Same(Kind kind, string a, string b) => kind switch
    {
        Kind.Decimal or Kind.Money => TryDecimal(a, out var x) && TryDecimal(b, out var y) && x == y,
        Kind.Code or Kind.Currency => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal),
    };

    private static object ColumnValue(Spec spec, string canonical) => spec.Kind switch
    {
        Kind.Money => new Money(decimal.Parse(canonical, CultureInfo.InvariantCulture)),
        Kind.Decimal => decimal.Parse(canonical, CultureInfo.InvariantCulture),
        Kind.Date => DateTime.SpecifyKind(DateTime.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc),
        _ => canonical,
    };

    /// <summary>The value fields of the write: the column, or (a code) the extraction JSON with the corrected code. Never status, approved amount or decision fields.</summary>
    private static Dictionary<string, object> ValueFields(Spec spec, string finalText, JsonNode? extraction)
    {
        if (spec.Kind != Kind.Code)
        {
            return new Dictionary<string, object> { [spec.Column!] = ColumnValue(spec, finalText) };
        }

        var coding = extraction!["coding"]!.AsObject();
        var slot = coding[spec.CodeMember!] as JsonObject ?? new JsonObject();
        slot["code"] = finalText;
        var firm = slot["firmCode"] is JsonValue firmValue && firmValue.TryGetValue<string>(out var f) ? f : null;
        slot["mismatch"] = !string.IsNullOrWhiteSpace(firm) && !string.Equals(firm.Trim(), finalText.Trim(), StringComparison.OrdinalIgnoreCase);
        coding[spec.CodeMember!] = slot;
        coding["firmCodeMismatch"] = new[] { "task", "activity", "expense" }
            .Any(member => coding[member] is JsonObject o && o["mismatch"] is JsonValue m && m.TryGetValue<bool>(out var flag) && flag);
        return new Dictionary<string, object> { [InvoiceSubmissionPersister.LineExtractionConfidence] = extraction.ToJsonString() };
    }

    /// <summary>The value update conditioned on the row version read in this request (<c>IfRowVersionMatches</c>), the shape the bulk conditional update sends.</summary>
    private static UpdateRequest ConditionalValueUpdate(string entityLogicalName, Guid id, long rowVersion, Dictionary<string, object> fields)
    {
        var entity = new Entity(entityLogicalName, id) { RowVersion = rowVersion.ToString(CultureInfo.InvariantCulture) };
        foreach (var (name, value) in fields)
        {
            entity[name] = value is DBNull ? null : value;
        }

        return new UpdateRequest { Target = entity, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches };
    }

    // ── Codes (FR-22) ──

    private sealed record CheckedCode(string? Canonical, CorrectionResult? Failure);

    private async Task<CheckedCode> CheckCodeAsync(Spec spec, string raw, JsonNode? extraction, CancellationToken ct)
    {
        var coding = extraction?["coding"];
        if (coding?["codeSetId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var idText) || !Guid.TryParse(idText, out var setId))
        {
            return new CheckedCode(null, new CorrectionResult(
                CorrectionStatus.CodeSetUnavailable, CorrectionCodes.CodeSetUnavailable, "This line was not coded, so it has no code set to check a code against."));
        }

        var sourceName = coding["codeSetSource"] is JsonValue sv && sv.TryGetValue<string>(out var s) ? s : null;
        var source = Enum.TryParse<CodeSetSource>(sourceName, out var parsed) ? parsed : CodeSetSource.SettingsDefault;
        ResolvedCodeSet? set;
        try
        {
            set = await _codeSets.LoadRecordedAsync(setId, source, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Correction of {Field}: the code set {CodeSetId} could not be read ({ExceptionType}); nothing changed.", spec.Name, setId, ex.GetType().Name);
            return new CheckedCode(null, new CorrectionResult(CorrectionStatus.Unavailable, CorrectionCodes.Unavailable, "The code set could not be read; nothing was changed. Try again."));
        }

        if (set is null)
        {
            return new CheckedCode(null, new CorrectionResult(
                CorrectionStatus.CodeSetUnavailable, CorrectionCodes.CodeSetUnavailable, "The code set this line was coded with is no longer in use."));
        }

        var kind = spec.CodeMember switch { "task" => BillingCodeKind.Task, "activity" => BillingCodeKind.Activity, _ => BillingCodeKind.Expense };
        return set.TryGetCode(kind, raw, out var canonical)
            ? new CheckedCode(canonical, null)
            : new CheckedCode(null, new CorrectionResult(CorrectionStatus.CodeNotInSet, CorrectionCodes.CodeNotInSet, "The code is not in this line's code set."));
    }

    private static string? CodeOf(JsonNode? extraction, string member) =>
        extraction?["coding"]?[member]?["code"] is JsonValue v && v.TryGetValue<string>(out var code) && !string.IsNullOrWhiteSpace(code) ? code.Trim() : null;

    private static decimal? DecimalOf(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    // ── Rows ──

    private Entity BuildRow(
        CorrectionCommand command, Spec spec, Entity invoice, (string Entity, Guid Id) core, Guid? lineId, string name, long version,
        string? aiText, string finalText, int outcome, Source source, string role, DateTime reviewedOnUtc)
    {
        var scope = lineId ?? command.InvoiceId;
        var id = InvoiceLineDecisionService.IdOf($"ebilling-correction-edit-{scope:D}-{spec.Name}-{version}-{finalText}");
        var row = new Entity(InvoiceLineDecisionService.CorrectionEntity, id)
        {
            ["sprk_name"] = name.Length > 200 ? name[..200] : name,
            ["sprk_fieldname"] = spec.Name,
            ["sprk_finalvalue"] = Capped(finalText),
            ["sprk_outcometype"] = new OptionSetValue(outcome),
            ["sprk_islabel"] = true, // D-64: Corrected and Confirmed individually are labels.
            ["sprk_reviewerrole"] = role,
            ["sprk_reviewedon"] = reviewedOnUtc,
            ["sprk_reviewedby"] = new EntityReference(InvoiceLineDecisionService.SystemUserEntity, command.ReviewerSystemUserId),
            ["sprk_invoice"] = new EntityReference(InvoiceReviewReadService.InvoiceEntity, command.InvoiceId),
        };
        if (aiText is not null)
        {
            row["sprk_aivalue"] = Capped(aiText);
        }

        if (lineId is { } line)
        {
            row["sprk_invoiceline"] = new EntityReference(InvoiceReviewReadService.LineEntity, line);
        }

        if (source.Code is not null)
        {
            row["sprk_sourcecode"] = source.Code.Length > 100 ? source.Code[..100] : source.Code;
        }

        if (source.Version is not null)
        {
            row["sprk_sourceversion"] = source.Version.Length > 20 ? source.Version[..20] : source.Version;
        }

        if (source.Confidence is { } confidence)
        {
            row["sprk_aiconfidence"] = Math.Round(confidence, 4, MidpointRounding.AwayFromZero);
        }

        // 013: exactly one core-record lookup (SingleCoreOf, #2455) plus the resolver fields, copied from the invoice.
        row[CoreAncestorResolver.StampColumnFor(core.Entity)] = new EntityReference(core.Entity, core.Id);
        foreach (var column in new[]
        {
            CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
            "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
        })
        {
            if (invoice.Contains(column) && invoice[column] is { } carried)
            {
                row[column] = carried;
            }
        }

        return row;
    }

    private static string Capped(string value) => value.Length > ValueMaxLength ? value[..ValueMaxLength] : value;

    private static string LineName(Spec spec, Entity line) =>
        $"{spec.Name} - line {(line.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Files the correction row (013): its owner asked through the row's own question, the creating person, and 013's guard. Null when stamped.</summary>
    private async Task<CorrectionResult?> StampOwnerAsync(Entity row, Guid invoiceId, Guid creatorSystemUserId, CancellationToken ct)
    {
        RecordOwnerResolution answer;
        try
        {
            answer = await _ownership.ResolveOwnerAsync(RecordOwnershipContext.ForChild(row), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(invoiceId, ex);
        }

        if (!answer.IsOwned)
        {
            _logger.LogWarning("Correction on invoice {InvoiceId}: no owner for the correction row ({Code}); nothing changed.", invoiceId, answer.RefusalCode);
            return new CorrectionResult(CorrectionStatus.OwnerUnresolved, CorrectionCodes.OwnerUnresolved, "The correction has no owner; nothing was changed.");
        }

        answer.ApplyTo(row);
        RecordCreatorPerson.Stamp(row, creatorSystemUserId);
        if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } refused)
        {
            // A builder defect, never sent.
            throw new InvalidOperationException($"The correction row names no single core record ({refused.RefusalCode}).");
        }

        return null;
    }

    /// <summary>D-64 reviewer role (directional): an eligible approver of the invoice's core record by the caller's OWN contact (EB-D-171), else an invoice reviewer.</summary>
    private async Task<string> RoleOfAsync(Guid invoiceId, Guid reviewer, (string Entity, Guid Id) core, CancellationToken ct)
    {
        try
        {
            var identity = await _identity.ResolveAsync(reviewer, ct).ConfigureAwait(false);
            if (identity.ContactId is { } contact && contact != Guid.Empty)
            {
                var set = await _approvers(core.Entity, core.Id, ct).ConfigureAwait(false);
                if (set.ForContact(contact) is not null)
                {
                    return ApproverRole;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Correction on invoice {InvoiceId}: the reviewer role could not be resolved ({ExceptionType}); recorded as {Role}.", invoiceId, ex.GetType().Name, ReviewerRole);
        }

        return ReviewerRole;
    }

    private static DateTime WhenOf(Entity row) =>
        row.GetAttributeValue<DateTime?>("sprk_reviewedon") ?? row.GetAttributeValue<DateTime?>("createdon") ?? DateTime.MinValue;

    private static CorrectionDto ToDto(Entity row)
    {
        var outcome = row.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value;
        return new CorrectionDto(
            row.Id,
            row.GetAttributeValue<EntityReference>("sprk_invoiceline")?.Id,
            row.GetAttributeValue<string>("sprk_fieldname") ?? string.Empty,
            row.GetAttributeValue<string>("sprk_aivalue"),
            row.GetAttributeValue<string>("sprk_finalvalue"),
            outcome switch
            {
                OutcomeCorrected => CorrectedLabel,
                OutcomeConfirmed => ConfirmedLabel,
                100000002 => "AcceptedInBulk",
                100000003 => "Unreviewed",
                _ => "Unknown",
            },
            row.GetAttributeValue<bool?>("sprk_islabel") ?? false,
            row.GetAttributeValue<decimal?>("sprk_aiconfidence"),
            row.GetAttributeValue<string>("sprk_sourcecode"),
            row.GetAttributeValue<string>("sprk_sourceversion"),
            row.GetAttributeValue<string>("sprk_reviewerrole"),
            row.GetAttributeValue<EntityReference>("sprk_reviewedby")?.Id,
            row.GetAttributeValue<DateTime?>("sprk_reviewedon") is { } when ? new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc)) : null);
    }

    // ── The read ──

    /// <summary>
    /// The correction rows of ONE invoice, newest first (the History pane; task 161 reads the same rows per Action / analyzer code and version).
    /// <paramref name="sourceCode"/> and <paramref name="sourceVersion"/> filter on the stored columns. Rows of another invoice never match.
    /// </summary>
    public async Task<IReadOnlyList<CorrectionDto>?> ListAsync(Guid invoiceId, string? sourceCode, string? sourceVersion, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceLineDecisionService.CorrectionEntity)
        {
            ColumnSet = new ColumnSet(CorrectionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            query.Criteria.AddCondition("sprk_sourcecode", ConditionOperator.Equal, sourceCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sourceVersion))
        {
            query.Criteria.AddCondition("sprk_sourceversion", ConditionOperator.Equal, sourceVersion.Trim());
        }

        query.AddOrder("sprk_reviewedon", OrderType.Descending);
        query.AddOrder($"{InvoiceLineDecisionService.CorrectionEntity}id", OrderType.Ascending);
        try
        {
            var rows = await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false);
            return rows.OrderByDescending(WhenOf).ThenBy(r => r.Id).Select(ToDto).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Corrections of invoice {InvoiceId}: the read failed ({ExceptionType}).", invoiceId, ex.GetType().Name);
            return null;
        }
    }

    private async Task<Entity?> ReadInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    /// <summary>The line, only if it belongs to this invoice (a foreign line id is the same null as an absent one: uniform 404).</summary>
    private async Task<Entity?> ReadLineAsync(Guid invoiceId, Guid lineId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.LineEntity)
        {
            ColumnSet = new ColumnSet(InvoiceReviewReadService.LineColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.LineEntity}id", ConditionOperator.Equal, lineId);
        query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    /// <summary>The earlier correction rows of one field (of one line, or of the header when <paramref name="lineId"/> is null), any outcome.</summary>
    private async Task<List<Entity>> ReadPriorAsync(Guid invoiceId, Guid? lineId, string field, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceLineDecisionService.CorrectionEntity)
        {
            ColumnSet = new ColumnSet(CorrectionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
        query.Criteria.AddCondition("sprk_fieldname", ConditionOperator.Equal, field);
        if (lineId is { } line)
        {
            query.Criteria.AddCondition("sprk_invoiceline", ConditionOperator.Equal, line);
        }
        else
        {
            query.Criteria.AddCondition("sprk_invoiceline", ConditionOperator.Null);
        }

        query.AddOrder($"{InvoiceLineDecisionService.CorrectionEntity}id", OrderType.Ascending);
        return await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false);
    }

    /// <summary>The latest submission's stored findings JSON (it carries the header provenance), or null.</summary>
    private async Task<string?> ReadFindingsJsonAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionFindingsJson, "createdon"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        query.AddOrder("createdon", OrderType.Descending);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault()
            ?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
    }

    private CorrectionResult ReadFailed(Guid invoiceId, Exception ex)
    {
        _logger.LogWarning("Correction on invoice {InvoiceId}: a read failed ({ExceptionType}); nothing changed.", invoiceId, ex.GetType().Name);
        return new CorrectionResult(CorrectionStatus.Unavailable, CorrectionCodes.Unavailable, "The invoice could not be read; nothing was changed.");
    }

    private CorrectionResult NoSingleCore(Guid invoiceId)
    {
        _logger.LogWarning("Correction on invoice {InvoiceId}: the invoice names no single core record; nothing changed.", invoiceId);
        return new CorrectionResult(CorrectionStatus.OwnerUnresolved, CorrectionCodes.OwnerUnresolved, "The invoice names no single core record.");
    }
}
