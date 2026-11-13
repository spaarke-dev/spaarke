using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 015 (B-5; spec FR-11, FR-05, FR-04): the slim list row of the Decide lane.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: WorklistItemRow (SignalReadService.cs), 1-2 KB per row with its evidence lines and, on a closed item, its Decision
//     Record steps. No type named WorklistItemSummary exists.
//   Extension: it is a projection OF that row, not a new model. Extending WorklistItemRow itself would change the Console's REST
//     response, which task 011's golden baselines pin byte for byte (FR-09).
//   Cost of doing nothing: 30-50 full rows exceed Cowork's 64 KiB inline cap, so the list tool cannot return page 1; and FR-04
//     cannot be met, because the list never carried the privilege flag.
// =====================================================================================================================

/// <summary>
/// One Work Item of the Decide lane as a list shows it (FR-11): <see cref="WorklistItemRow"/> without its evidence lines
/// (<c>EvidenceRefs</c>), its rule sentence and its Decision Record (the steps), plus its group's core record id (FR-12) and the
/// privilege flag (FR-04). At most
/// <see cref="MaxJsonBytes"/> bytes as JSON, whatever the stored text lengths: the free-text members are clipped (<see cref="Clip"/>).
/// </summary>
/// <remarks>
/// <para><b>What is left out, and why.</b> <c>EvidenceRefs</c>, the Decision Record and the closed-set members
/// (<c>ResolvedOn</c>, <c>ResolutionType</c>, <c>SuppressedUntil</c>) are never present on a Decide-lane row of the lanes set, and the
/// Do-lane subject reference is Do-lane only; the rule <c>Sentence</c> (up to 2,000 characters) cannot share a 600-byte row with the
/// ids and dates, and the full row, sentence included, arrives with the decision plan on open (spec FR-11).</para>
/// <para><b>Clipped text.</b> <see cref="Title"/>, <see cref="SubjectName"/> and <see cref="RuleShortName"/> are cut at a character
/// boundary to fit their byte budget and end in an ellipsis when cut. The widget's headline is the subject name, else the title.</para>
/// <para><b>Nulls.</b> A null member is not written, whatever the host's serializer options, so the size bound holds for every host.</para>
/// </remarks>
/// <param name="CoreRecordId">The id of the core record whose group the item sits in (015 review F4; FR-12: the widget groups rows by
/// matter). Its name and number are on the page once per group, in <see cref="DecidePageResult.Cores"/>, not on every row.</param>
/// <param name="PrivilegeFlagged">FR-04 as amended 2026-11-08 (task 018): true when the item's subject communication or any witness or
/// evidence communication is Privileged or Potentially Privileged, read as the caller and failing closed (<see cref="WorkItemPrivilege"/>;
/// never <c>sprk_signal.sprk_privilegeflagged</c>, which nothing writes). ADR-015: flagged, never decided. The tool layer reduces a
/// flagged item to a count for the model (task 022); nothing here branches on it.</param>
public sealed record WorklistItemSummary(
    Guid SignalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CoreRecordId,
    string Lane,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubjectName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RuleShortName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkType,
    string PastDueWording,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? RaisedOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DueDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PolicyVersionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorklistInvoiceReview? InvoiceReview,
    bool PrivilegeFlagged)
{
    /// <summary>The most a summary serializes to (FR-11, NFR-02): 50 of them are 30 KB, well under Cowork's 64 KiB inline cap.</summary>
    public const int MaxJsonBytes = 600;

    /// <summary>Byte budgets (as default-encoded JSON, the worst case: every non-ASCII character is a six-byte escape) of the clipped
    /// members. With every other member present, a billing item's <c>invoiceReview</c> included, they leave the row at most
    /// <see cref="MaxJsonBytes"/> (pinned by <c>WorklistItemSummaryTests</c>). <see cref="CoreRecordId"/>'s 54 bytes came out of them
    /// (015 review: 48/56/32 before); the subject name, the widget's headline, keeps the most.</summary>
    internal const int TitleBytes = 28;
    internal const int SubjectNameBytes = 36;
    internal const int RuleShortNameBytes = 18;

    /// <summary>The slim row for <paramref name="row"/> in the group of <paramref name="core"/>, with the given privilege flag.</summary>
    internal static WorklistItemSummary From(WorklistItemRow row, WorklistCoreRecord? core, bool privilegeFlagged)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new WorklistItemSummary(
            row.SignalId,
            core?.RecordId,
            row.Lane,
            row.Status,
            Clip(row.Title, TitleBytes),
            Clip(row.SubjectName, SubjectNameBytes),
            Clip(row.RuleShortName, RuleShortNameBytes),
            row.WorkType,
            row.PastDueWording,
            row.RaisedOn,
            row.DueDate,
            row.PolicyVersionId,
            row.InvoiceReview,
            privilegeFlagged);
    }

    private const string Ellipsis = "…";

    /// <summary>
    /// <paramref name="text"/> cut, at a character boundary and with a trailing ellipsis, to at most <paramref name="maxJsonBytes"/> bytes
    /// as default-encoded JSON string content (<see cref="JsonEncodedText"/>, so a quote, a backslash or any non-ASCII character is
    /// counted at its escaped length). Unchanged when it already fits; null stays null.
    /// </summary>
    internal static string? Clip(string? text, int maxJsonBytes)
    {
        if (text is null)
        {
            return null;
        }

        var ellipsisBytes = JsonEncodedText.Encode(Ellipsis).EncodedUtf8Bytes.Length;
        var total = 0;
        var fits = true;
        var cutAt = 0;
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var size = JsonEncodedText.Encode(rune.ToString()).EncodedUtf8Bytes.Length;
            total += size;
            if (total > maxJsonBytes)
            {
                fits = false;
                break;
            }

            if (total + ellipsisBytes <= maxJsonBytes)
            {
                cutAt = used + rune.Utf16SequenceLength;
            }

            used += rune.Utf16SequenceLength;
        }

        return fits ? text : text[..cutAt] + Ellipsis;
    }
}

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), 015 review F4.
//   Existing: WorklistCoreRecord (SignalReadService.cs), the REST group's core identity, with the name and number unclipped.
//   Extension: reusing it would put clipped text in the type the 011 golden baselines pin, and carry the record type and type label the
//     page does not need; this is the clipped name and number of that same caller-trimmed record, nothing else.
//   Cost of doing nothing: rows of a page cannot be grouped under a named matter (FR-12, task 031), and without a page-level map the
//     name would cost every row its bytes under the 600-byte cap (FR-11).

/// <summary>
/// A core record as a Decide-lane page names it once for all its rows (<see cref="DecidePageResult.Cores"/>, keyed by
/// <see cref="WorklistItemSummary.CoreRecordId"/>): the name and number of the group's <see cref="WorklistCoreRecord"/>, clipped like the
/// row's text (<see cref="WorklistItemSummary.Clip"/>). A null member is not written.
/// </summary>
public sealed record WorklistCoreSummary(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Number)
{
    /// <summary>Byte budgets (escaped JSON, as for the row). 50 entries at most are about 10 KB.</summary>
    internal const int NameBytes = 120;
    internal const int NumberBytes = 40;

    /// <summary>The page's entry for <paramref name="core"/>.</summary>
    internal static WorklistCoreSummary From(WorklistCoreRecord core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return new WorklistCoreSummary(WorklistItemSummary.Clip(core.Name, NameBytes), WorklistItemSummary.Clip(core.Number, NumberBytes));
    }
}
