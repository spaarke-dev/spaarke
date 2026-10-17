using System.Globalization;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// Ontology platform R1 task 045 (D-47; #1959 item 2): the Decision Record a closed worklist item carries.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: SignalReadService is the one reader of Signals for the Console (D-15, FR-24); DecisionRecordWriter owns the shape of
//     sprk_steps / sprk_followons it writes.
//   Extension: the Decision Record rides on the item row the route already returns, read AS THE CALLER like every other row-level
//     fact there; no new route, so the route-authorization ledger (uac-r2's) is unchanged. These records are the row's DTO and the
//     tolerant reader of the stored envelope, kept out of the 1,400-line service (cohesion: parsing, not access decisions).
//   Cost of doing nothing: a decided item opens read-only with no replayed steps, and the Console has no Decision Record tab (D-47).
// =====================================================================================================================

/// <summary>
/// One Decision Record as a closed item carries it (task 045): the record number, who decided and when, the class and outcome, the
/// note (<c>sprk_reason</c>) and the steps the wizard replays. Present only when the CALLER's own read returned the record (a secure
/// child reaches only its family; an owner-only record only its owner): a record the caller cannot read is omitted, never withheld by
/// name. The fact snapshot is deliberately NOT returned (it holds witness values the Signal's read-time witness rule governs).
/// </summary>
/// <param name="RecordId">The <c>sprk_decisionrecord</c> id.</param>
/// <param name="Number">The autonumber, for example <c>DR-00042</c>.</param>
/// <param name="RecordClass"><c>Judgement</c>, <c>Routine</c> or <c>Dismissal</c>; null for an option value this build does not know.</param>
/// <param name="Outcome"><c>Authorized</c>, <c>Denied</c> or <c>Dismissed</c>; null when unknown.</param>
/// <param name="DecidedOn">When it was decided (<c>sprk_decidedon</c>).</param>
/// <param name="DecidedByName">The confirming person's name, from the record's OWN <c>sprk_confirmedby</c> (never the owner, D-129);
/// null when the record names none (a system decision) or the caller cannot read the user.</param>
/// <param name="Reason">The note the decider gave (<c>sprk_reason</c>); null when none.</param>
/// <param name="Steps">Every action the rule offered, in order; null when the stored envelope is missing or unreadable (the wizard then
/// opens the item read-only with no replay rather than showing every action as Skipped).</param>
/// <param name="FollowOns">The Next steps the decision created (<c>sprk_followons</c>); empty when none or unreadable.</param>
public sealed record WorklistDecisionRecord(
    Guid RecordId,
    string? Number,
    string? RecordClass,
    string? Outcome,
    DateTimeOffset? DecidedOn,
    string? DecidedByName,
    string? Reason,
    IReadOnlyList<WorklistDecisionStep>? Steps,
    IReadOnlyList<WorklistDecisionFollowOn> FollowOns);

/// <summary>One offered action and what became of it. <paramref name="Values"/> is what was sent (null for a skipped step).</summary>
public sealed record WorklistDecisionStep(string Code, bool Taken, JsonElement? Values, string? Status);

/// <summary>A Next step the decision created.</summary>
public sealed record WorklistDecisionFollowOn(string Code, string? Entity, string? Id);

/// <summary>Reads one <c>sprk_decisionrecord</c> row (as the worklist's FetchXML returns it through the Web API) tolerantly.</summary>
internal static class WorklistDecisionRecordReader
{
    /// <summary>The columns the route selects (plus the <c>by</c> link's <c>fullname</c>); the fact snapshot is not among them.</summary>
    internal static readonly string[] Columns =
    [
        "sprk_decisionrecordid", "sprk_decisionnumber", "sprk_recordclass", "sprk_decisionoutcome", "sprk_reason",
        "sprk_decidedon", "sprk_confirmedby", "sprk_steps", "sprk_followons",
    ];

    internal static bool TryParse(JsonElement raw, out WorklistDecisionRecord record)
    {
        record = null!;
        if (raw.ValueKind != JsonValueKind.Object
            || !raw.TryGetProperty("sprk_decisionrecordid", out var idValue)
            || idValue.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idValue.GetString(), out var id)
            || id == Guid.Empty)
        {
            return false;
        }

        record = new WorklistDecisionRecord(
            id,
            Text(raw, "sprk_decisionnumber"),
            Int(raw, "sprk_recordclass") switch
            {
                (int)RecordedDecisionClass.Judgement => "Judgement",
                (int)RecordedDecisionClass.Routine => "Routine",
                (int)RecordedDecisionClass.Dismissal => "Dismissal",
                _ => null,
            },
            Int(raw, "sprk_decisionoutcome") switch
            {
                100000000 => "Authorized",
                100000001 => "Denied",
                100000002 => "Dismissed",
                _ => null,
            },
            Text(raw, "sprk_decidedon") is { } at
            && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var decidedOn)
                ? decidedOn
                : null,
            Text(raw, "by.fullname") ?? Text(raw, "by_x002e_fullname"),
            Text(raw, "sprk_reason"),
            ParseSteps(Text(raw, "sprk_steps")),
            ParseFollowOns(Text(raw, "sprk_followons")));
        return true;
    }

    /// <summary>The <c>steps</c> array of the envelope <see cref="DecisionRecordWriter"/> writes; null when absent or not that shape.</summary>
    internal static IReadOnlyList<WorklistDecisionStep>? ParseSteps(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stored);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("steps", out var steps)
                || steps.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<WorklistDecisionStep>();
            foreach (var step in steps.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object || Text(step, "code") is not { } code)
                {
                    continue;
                }

                var taken = step.TryGetProperty("taken", out var t) && t.ValueKind == JsonValueKind.True;
                JsonElement? values = taken && step.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
                    ? v.Clone()
                    : null;
                list.Add(new WorklistDecisionStep(code, taken, values, Text(step, "outcome")));
            }

            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<WorklistDecisionFollowOn> ParseFollowOns(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(stored);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<WorklistDecisionFollowOn>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && Text(entry, "actionCode") is { } code)
                {
                    list.Add(new WorklistDecisionFollowOn(code, Text(entry, "entity"), Text(entry, "id")));
                }
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static int? Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
}
