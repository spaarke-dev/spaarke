using System.Text;
using System.Text.Json.Nodes;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The text of <c>sprk_invoicesubmission.sprk_adminattention</c> ("Needs Admin Attention", D-133): why a submission needs an
/// administrator's review, shown in the model-driven app's "Submissions needing attention" view. The marker is set when a billing Signal
/// the submission should have could not be created (<see cref="InvoiceSubmissionPersister"/> for a plan entry the writer would refuse,
/// <see cref="InvoiceSubmissionAfterSteps"/> for a permanent write refusal) and is NEVER cleared from code: the administrator clears it
/// after review.
/// </summary>
/// <remarks>
/// <para><b>Codes only (ADR-015).</b> The text names the refusal code, the policy code and the subject (line or invoice, with an id prefix);
/// it never carries an extracted value. The full detail stays in <c>sprk_findingsjson</c> (<c>signalPlanRefusals</c>,
/// <c>signalWriteRefusals</c>).</para>
/// <para><b>Idempotent.</b> The text is a pure function of the refusals stored in the findings JSON (the plan refusals first, then the
/// write refusals, in stored order, each distinct entry once), so the same refusals always give the same text and a resume writes nothing
/// new. Capped at <see cref="InvoiceSubmissionPersister.AdminAttentionMaxLength"/> characters: entries that do not fit are replaced by
/// "… (+N more)".</para>
/// </remarks>
internal static class AdminAttentionText
{
    /// <summary>One Signal that could not be created: its policy, subject (entity logical name and id) and the refusal code.</summary>
    internal readonly record struct Item(string? PolicyCode, string? SubjectEntity, string? SubjectId, string? Code);

    private const int IdPrefixLength = 8;

    /// <summary>
    /// The attention text for <paramref name="items"/> (distinct entries, first occurrence wins), or null when there are none: the caller
    /// then writes nothing, so an empty or null value is never stored.
    /// </summary>
    internal static string? Build(IEnumerable<Item> items)
    {
        var parts = items.Distinct().Select(Describe).ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var header = (parts.Count == 1 ? "1 Signal" : parts.Count + " Signals") + " could not be created: ";
        var full = header + string.Join(", ", parts);
        if (full.Length <= InvoiceSubmissionPersister.AdminAttentionMaxLength)
        {
            return full;
        }

        // Overflow: keep whole entries while the "… (+N more)" tail still fits; never cut an entry in half.
        var text = new StringBuilder(header);
        var kept = 0;
        foreach (var part in parts)
        {
            var remainingAfter = parts.Count - (kept + 1);
            var tail = remainingAfter > 0 ? $", … (+{remainingAfter} more)" : string.Empty;
            var candidate = text.Length + (kept > 0 ? 2 : 0) + part.Length + tail.Length;
            if (candidate > InvoiceSubmissionPersister.AdminAttentionMaxLength)
            {
                break;
            }

            if (kept > 0)
            {
                text.Append(", ");
            }

            text.Append(part);
            kept++;
        }

        var left = parts.Count - kept;
        return text.Append(kept == 0 ? $"… (+{left} more)" : $", … (+{left} more)").ToString();
    }

    /// <summary>
    /// The attention text for a stored findings JSON: its <c>signalPlanRefusals</c> then its <c>signalWriteRefusals</c>. Null when it
    /// stores neither.
    /// </summary>
    internal static string? FromFindings(JsonObject findings) =>
        Build(Read(findings, InvoiceSubmissionPersister.SignalPlanRefusalsMember).Concat(Read(findings, InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember)));

    private static IEnumerable<Item> Read(JsonObject findings, string member)
    {
        if (findings[member] is not JsonArray array)
        {
            yield break;
        }

        foreach (var entry in array.OfType<JsonObject>())
        {
            var subject = entry["subject"] as JsonObject;
            yield return new Item(Text(entry, "policyCode"), subject is null ? null : Text(subject, "entity"), subject is null ? null : Text(subject, "id"), Text(entry, "code"));
        }
    }

    private static string Describe(Item item)
    {
        var subject = item.SubjectEntity switch
        {
            InvoiceSubmissionPersister.LineEntity => "line",
            InvoiceReviewService.InvoiceEntity => "invoice",
            { Length: > 0 } other => other,
            _ => "record",
        };
        var id = item.SubjectId is { Length: > 0 } text ? " " + (text.Length > IdPrefixLength ? text[..IdPrefixLength] : text) : string.Empty;
        return $"{item.Code ?? "unknown"} ({item.PolicyCode ?? "unknown"} on {subject}{id})";
    }

    private static string? Text(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
}
