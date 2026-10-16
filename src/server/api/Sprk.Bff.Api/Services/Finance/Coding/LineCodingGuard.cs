using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Coding;

/// <summary>
/// The server-side control of FR-22 / D-46: turns what the model returned for one line into what may be stored. Pure (no I/O).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The Action tells the model to use only the allowed codes, but nothing in the platform enforces it:
/// <c>$choices</c> enrichment only reaches top-level string properties, not <c>lines[].taskCode</c>, and a model can still
/// return a code it knows from the UTBMS standard or copied from the line. So every code is checked against the line's resolved
/// <see cref="ResolvedCodeSet"/> here. A code outside the set, or of a kind the line type does not take, is dropped and
/// reported as a <see cref="RefusedCode"/>; it is never stored and never corrected to a "near" code.
/// </para>
/// <para>
/// <b>Observations</b> are kept only when their quoted text appears in the line's narrative (a finding needs evidence the
/// reviewer can see), with at most one per type (the highest-confidence one), matching the Action's contract.
/// </para>
/// <para>
/// <b>Confidence.</b> The model's confidence describes the codes it returned. When any returned code was refused it no longer
/// describes what is stored, so it is reported as 0 and the line goes to the reviewer.
/// </para>
/// </remarks>
internal static class LineCodingGuard
{
    /// <summary>Guards the model's entry for a line that was sent to it.</summary>
    public static LineCodingResult Apply(InvoiceLineToCode line, InvoiceLineCoding returned, ResolvedCodeSet set)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(set);

        var refused = new List<RefusedCode>();
        var taskTakes = line.LineType == InvoiceLineType.Fee;
        var task = Check(BillingCodeKind.Task, returned.TaskCode, taskTakes, set, refused);
        var activity = Check(BillingCodeKind.Activity, returned.ActivityCode, taskTakes, set, refused);
        var expense = Check(BillingCodeKind.Expense, returned.ExpenseCode, !taskTakes, set, refused);

        var (observations, dropped) = GroundObservations(line.Narrative, returned.Observations);

        return new LineCodingResult
        {
            LineKey = line.LineKey,
            Status = LineCodingStatus.Coded,
            Task = Slot(task, line.BilledTaskCode),
            Activity = Slot(activity, line.BilledActivityCode),
            Expense = Slot(expense, line.BilledExpenseCode),
            Confidence = refused.Count > 0 ? 0m : returned.Confidence,
            Observations = observations,
            RefusedCodes = refused,
            DroppedObservations = dropped,
            CodeSetId = set.CodeSetId,
            CodeSetName = set.CodeSetName,
            CodeSetSource = set.Source,
        };
    }

    /// <summary>A line that was not coded (<paramref name="status"/> says why): no AI codes, the firm's codes retained, no mismatch.</summary>
    public static LineCodingResult Uncoded(InvoiceLineToCode line, LineCodingStatus status, ResolvedCodeSet set)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(set);

        return new LineCodingResult
        {
            LineKey = line.LineKey,
            Status = status,
            Task = Slot(null, line.BilledTaskCode),
            Activity = Slot(null, line.BilledActivityCode),
            Expense = Slot(null, line.BilledExpenseCode),
            Confidence = 0m,
            Observations = [],
            RefusedCodes = [],
            CodeSetId = set.CodeSetId,
            CodeSetName = set.CodeSetName,
            CodeSetSource = set.Source,
        };
    }

    /// <summary>The AI code to store (the set's own spelling), or <c>null</c> when none was returned or it was refused.</summary>
    private static string? Check(
        BillingCodeKind kind,
        string? returnedCode,
        bool lineTypeTakesKind,
        ResolvedCodeSet set,
        List<RefusedCode> refused)
    {
        var code = Normalize(returnedCode);
        if (code is null)
        {
            return null;
        }

        if (!lineTypeTakesKind)
        {
            refused.Add(new RefusedCode(kind, code, CodeRefusalReason.NotApplicableToLineType));
            return null;
        }

        if (!set.TryGetCode(kind, code, out var canonical))
        {
            refused.Add(new RefusedCode(kind, code, CodeRefusalReason.OutsideCodeSet));
            return null;
        }

        return canonical;
    }

    /// <summary>The AI code next to the firm's. A mismatch needs both and a real difference (case and padding do not count).</summary>
    private static LineCodeValue Slot(string? aiCode, string? firmCode)
    {
        var firm = Normalize(firmCode);
        var mismatch = aiCode is not null
            && firm is not null
            && !string.Equals(aiCode, firm, StringComparison.OrdinalIgnoreCase);
        return new LineCodeValue(aiCode, firm, mismatch);
    }

    private static (List<InvoiceLineObservation> Kept, int Dropped) GroundObservations(
        string? narrative,
        IReadOnlyList<InvoiceLineObservation> observations)
    {
        var kept = new Dictionary<InvoiceLineObservationType, InvoiceLineObservation>();
        var dropped = 0;
        var haystack = CollapseWhitespace(narrative);

        foreach (var observation in observations)
        {
            var quote = CollapseWhitespace(observation.QuotedText);
            if (quote.Length == 0 || haystack.Length == 0 || !haystack.Contains(quote, StringComparison.OrdinalIgnoreCase))
            {
                dropped++;
                continue;
            }

            if (kept.TryGetValue(observation.Type, out var existing))
            {
                dropped++;
                if (observation.Confidence <= existing.Confidence)
                {
                    continue;
                }
            }

            kept[observation.Type] = observation;
        }

        // Stable order: the enum order, not the model's.
        return (kept.OrderBy(k => k.Key).Select(k => k.Value).ToList(), dropped);
    }

    private static string? Normalize(string? code)
    {
        var trimmed = code?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
