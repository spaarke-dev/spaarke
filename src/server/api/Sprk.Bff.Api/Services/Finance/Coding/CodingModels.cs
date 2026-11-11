using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Coding;

// ─────────────────────────────────────────────────────────────────────────────
// Contracts of UTBMS line coding (ebilling-r1 task 047; spec FR-22, D-05, D-46, D-51).
//
// Services/Finance/Coding turns invoice lines into coded lines in three steps: CodeSetResolver picks the line's
// code set (FR-22 order), LineCodingService sends the lines in chunks to IInvoiceAi.CodeLinesAsync with that set's
// codes, and LineCodingGuard refuses any code the model returned that is not in the set. The model is never
// trusted for a code; the guard is the FR-22 control.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which code list a UTBMS code belongs to. The numeric values are the <c>sprk_codetype</c> option values (task 018).</summary>
public enum BillingCodeKind
{
    Task = 100000000,
    Activity = 100000001,
    Expense = 100000002,
}

/// <summary>
/// The step of the FR-22 resolution order that selected a line's code set. Declared in the order the steps run.
/// </summary>
public enum CodeSetSource
{
    /// <summary>Step 1: the set implied by the firm-supplied task codes on the invoice (all of them belong to one set).</summary>
    FirmCodes,

    /// <summary>Step 2: the default code set mapped on the core record's matter type.</summary>
    MatterType,

    /// <summary>Step 3: the default code set mapped on the core record's practice area.</summary>
    PracticeArea,

    /// <summary>Step 4: a set proposed by <see cref="ICodeSetSuggestionProvider"/>. Flagged for review; never persisted as a mapping.</summary>
    AiSuggestion,

    /// <summary>Step 5: the settings default code set (<c>sprk_ebillingsetting.sprk_defaultcodeset</c>, else "UTBMS Litigation").</summary>
    SettingsDefault,
}

/// <summary>
/// A core record (FR-05) whose matter type and practice area select the default code set. Matter, Project and Work
/// Assignment all carry <c>sprk_mattertype</c> and <c>sprk_practicearea</c> lookups.
/// </summary>
public sealed record CodingCoreRecord(string EntityLogicalName, Guid Id)
{
    public const string Matter = "sprk_matter";
    public const string Project = "sprk_project";
    public const string WorkAssignment = "sprk_workassignment";

    /// <summary>True for the three core-record tables.</summary>
    public static bool IsSupported(string? entityLogicalName) =>
        entityLogicalName is Matter or Project or WorkAssignment;
}

/// <summary>
/// The code set a coding call resolved: which set, which FR-22 step chose it, and the closed lists of allowed codes. One
/// resolution serves every line of one invoice (one core record, one set of firm codes).
/// </summary>
/// <remarks>
/// <b>Activity and expense codes.</b> The seeded UTBMS sets keep activity and expense codes in their own sets
/// ("UTBMS Activity", "UTBMS Expense", task 018), not in the task-code set. The task list is the resolved set's Task
/// codes; the activity and expense lists are the resolved set's own Activity / Expense codes when it has any, otherwise
/// those of the shared standalone set. A returned code is "in the line's code set" when it is in the matching list.
/// </remarks>
public sealed class ResolvedCodeSet
{
    private readonly Dictionary<string, string>[] _canonical;

    public ResolvedCodeSet(
        Guid codeSetId,
        string codeSetName,
        CodeSetSource source,
        IReadOnlyList<InvoiceCodeOption> taskCodes,
        IReadOnlyList<InvoiceCodeOption> activityCodes,
        IReadOnlyList<InvoiceCodeOption> expenseCodes)
    {
        CodeSetId = codeSetId;
        CodeSetName = codeSetName ?? throw new ArgumentNullException(nameof(codeSetName));
        Source = source;
        TaskCodes = taskCodes ?? throw new ArgumentNullException(nameof(taskCodes));
        ActivityCodes = activityCodes ?? throw new ArgumentNullException(nameof(activityCodes));
        ExpenseCodes = expenseCodes ?? throw new ArgumentNullException(nameof(expenseCodes));

        _canonical = [Index(taskCodes), Index(activityCodes), Index(expenseCodes)];
    }

    public Guid CodeSetId { get; }

    public string CodeSetName { get; }

    public CodeSetSource Source { get; }

    /// <summary>True when the set was chosen by the AI (step 4) and a reviewer should confirm it (FR-22).</summary>
    public bool NeedsReview => Source == CodeSetSource.AiSuggestion;

    public IReadOnlyList<InvoiceCodeOption> TaskCodes { get; }

    public IReadOnlyList<InvoiceCodeOption> ActivityCodes { get; }

    public IReadOnlyList<InvoiceCodeOption> ExpenseCodes { get; }

    /// <summary>
    /// Whether <paramref name="code"/> is in the allowed list for <paramref name="kind"/>. Matching ignores case and
    /// surrounding whitespace; <paramref name="canonical"/> is the code exactly as the set spells it, which is what
    /// must be stored.
    /// </summary>
    public bool TryGetCode(BillingCodeKind kind, string? code, out string canonical)
    {
        var key = code?.Trim();
        if (!string.IsNullOrEmpty(key) && _canonical[Slot(kind)].TryGetValue(key, out var found))
        {
            canonical = found;
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    private static Dictionary<string, string> Index(IReadOnlyList<InvoiceCodeOption> options)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            map.TryAdd(option.Code, option.Code);
        }

        return map;
    }

    private static int Slot(BillingCodeKind kind) => kind switch
    {
        BillingCodeKind.Task => 0,
        BillingCodeKind.Activity => 1,
        BillingCodeKind.Expense => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// One invoice's lines to code. All lines belong to ONE core record; an invoice whose lines span core records is coded
/// with one call per core record.
/// </summary>
public sealed record LineCodingRequest
{
    /// <summary>The core record whose matter type / practice area map to a code set.</summary>
    public required CodingCoreRecord CoreRecord { get; init; }

    /// <summary>The lines to code. <see cref="InvoiceLineToCode.LineKey"/> must be unique within the request.</summary>
    public required IReadOnlyList<InvoiceLineToCode> Lines { get; init; }

    /// <summary>Tenant id for the AI run context. Not sent to the model.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Whether the line went to the model, and if not why not.</summary>
public enum LineCodingStatus
{
    /// <summary>The line was sent to the model and its entry came back.</summary>
    Coded,

    /// <summary>The line has no narrative, so it was not sent (an observation needs quoted text); it has no codes.</summary>
    NoNarrative,

    /// <summary>The line was sent but the model returned no usable entry for its key (none, or more than one); it has no codes.</summary>
    NotReturned,
}

/// <summary>Why a code the model returned was refused.</summary>
public enum CodeRefusalReason
{
    /// <summary>The code is not in the allowed list for its kind in the line's resolved code set (FR-22).</summary>
    OutsideCodeSet,

    /// <summary>The code is in the set but of a kind the line type does not take (a task or activity code on an Expense line, an expense code on a Fee line).</summary>
    NotApplicableToLineType,
}

/// <summary>A returned code that was discarded: the finding input "code refused" (FR-22). The code is never stored.</summary>
public sealed record RefusedCode(BillingCodeKind Kind, string Code, CodeRefusalReason Reason);

/// <summary>
/// One code slot of a line: the AI's validated code next to the firm's as billed (D-05). <see cref="Mismatch"/> is true
/// when both are present and differ.
/// </summary>
public sealed record LineCodeValue(string? Code, string? FirmCode, bool Mismatch);

/// <summary>The coding of one line.</summary>
public sealed record LineCodingResult
{
    public required string LineKey { get; init; }

    public required LineCodingStatus Status { get; init; }

    /// <summary>The task code from the resolved set, or none.</summary>
    public required LineCodeValue Task { get; init; }

    /// <summary>The activity code from the resolved set, or none.</summary>
    public required LineCodeValue Activity { get; init; }

    /// <summary>The expense code from the resolved set, or none.</summary>
    public required LineCodeValue Expense { get; init; }

    /// <summary>The model's confidence in the codes, 0-1; 0 when the line was not coded or a code was refused.</summary>
    public required decimal Confidence { get; init; }

    /// <summary>Block-billing / vague-description observations whose quoted text is in the line's narrative.</summary>
    public required IReadOnlyList<InvoiceLineObservation> Observations { get; init; }

    /// <summary>Codes the model returned that were refused; each is a "code refused" finding input.</summary>
    public required IReadOnlyList<RefusedCode> RefusedCodes { get; init; }

    /// <summary>
    /// Observations the model returned that were dropped, not stored: the quoted text is not in the narrative, or another
    /// observation of the same type was kept (one per type).
    /// </summary>
    public int DroppedObservations { get; init; }

    public required Guid CodeSetId { get; init; }

    public required string CodeSetName { get; init; }

    /// <summary>The FR-22 step that chose the code set. Recorded per line.</summary>
    public required CodeSetSource CodeSetSource { get; init; }

    /// <summary>True when the code set was an AI suggestion (step 4): a reviewer should confirm it.</summary>
    public bool CodeSetNeedsReview => CodeSetSource == CodeSetSource.AiSuggestion;

    /// <summary>True when a firm-supplied code differs from the AI code on any slot (D-05 mismatch → finding).</summary>
    public bool HasFirmCodeMismatch => Task.Mismatch || Activity.Mismatch || Expense.Mismatch;
}

/// <summary>The result of coding one invoice: the resolved code set and one <see cref="LineCodingResult"/> per input line, in input order.</summary>
public sealed record InvoiceCodingResult
{
    public required ResolvedCodeSet CodeSet { get; init; }

    public required IReadOnlyList<LineCodingResult> Lines { get; init; }

    /// <summary>Calls made to <see cref="IInvoiceAi.CodeLinesAsync"/>.</summary>
    public required int ChunkCount { get; init; }
}

/// <summary>
/// A failure of line coding that is not an AI failure (those surface as <see cref="InvoiceAiException"/> or
/// <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/>, unchanged): the reference data the coding depends on
/// is missing. The caller (the submission job) marks the work Failed with <see cref="ReasonCode"/>.
/// </summary>
public sealed class LineCodingException : Exception
{
    /// <summary>No code set could be resolved: no mapping, no AI suggestion and no usable settings default.</summary>
    public const string NoCodeSet = "ebilling.coding.no_code_set";

    public LineCodingException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
    }

    public string ReasonCode { get; }
}
