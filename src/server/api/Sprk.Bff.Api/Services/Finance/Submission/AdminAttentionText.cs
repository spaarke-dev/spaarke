using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The text of <c>sprk_invoicesubmission.sprk_adminattention</c> ("Needs Admin Attention", D-133): why a submission needs an
/// administrator's review, shown in the model-driven app's "Submissions needing attention" view. The marker is set when a billing Signal
/// the submission should have could not be created (<see cref="InvoiceSubmissionPersister"/> for a plan entry the writer would refuse,
/// <see cref="InvoiceSubmissionAfterSteps"/> for a permanent write refusal) and is not cleared from code: the administrator clears it
/// after review. The ONE exception (review fix R7, notes 14c): when the after-steps complete the submission they withdraw the
/// "Processing stopped" segment, which has become false (<see cref="WithoutStopped"/>); every other segment and any text an
/// administrator typed stays. Three sources write it, each as its own segment of the one text (joined by <see cref="Separator"/>, always in the order
/// signals, system policy, processing stopped, so a merge is deterministic): (1) Signal plan and write refusals; (2) the system billing
/// policy is missing, ambiguous, without a version in force or of the wrong type (review fix R5: the system Signals were not created;
/// "disabled" is the administrator's own choice, D-125, and writes nothing); (3) processing stopped on a submission that links its invoice
/// (review fix R3: the handler left it Processing, never Failed); (4) ebilling-r1 task 094: the commit was undone after the commit gate
/// failed (<see cref="UndoneNote"/>; it replaces a "Processing stopped" segment, which has become false). Notes: section 14c.
/// </summary>
/// <remarks>
/// <para><b>Codes only (ADR-015).</b> The text names the refusal code, the policy code and the subject (line or invoice, with an id prefix);
/// it never carries an extracted value. The full detail stays in <c>sprk_findingsjson</c> (<c>signalPlanRefusals</c>,
/// <c>signalWriteRefusals</c>).</para>
/// <para><b>Idempotent.</b> The Signal segment is a pure function of the refusals stored in the findings JSON (the plan refusals first, then
/// the write refusals, in stored order, each distinct entry once), so the same refusals always give the same text and a resume writes nothing
/// new. Capped at <see cref="InvoiceSubmissionPersister.AdminAttentionMaxLength"/> characters: entries that do not fit are replaced by
/// "… (+N more)".</para>
/// <para><b>Merge, never overwrite.</b> <see cref="Merge"/> replaces only the segment of its own kind in the current value and keeps the
/// others (and any text an administrator typed), so a Signal refusal recorded later does not erase the policy note and the other way round.
/// The Signal segment is rebuilt from the stored refusals within the room the other segments leave. <see cref="NoteAsync"/> reads the
/// current value and writes only when the merged text differs, so a redelivery that finds the same cause writes nothing.</para>
/// </remarks>
internal static class AdminAttentionText
{
    /// <summary>One Signal that could not be created: its policy, subject (entity logical name and id) and the refusal code.</summary>
    internal readonly record struct Item(string? PolicyCode, string? SubjectEntity, string? SubjectId, string? Code);

    private const int IdPrefixLength = 8;

    /// <summary>Between the segments of one text.</summary>
    internal const string Separator = " | ";

    private const string PolicyPrefix = "System billing policy ";
    private const string StoppedPrefix = "Processing stopped: ";
    private const string UndonePrefix = "Submission undone: ";
    private const int MaxCodeLength = 80;

    /// <summary>The room the Signal segment is never squeezed below (its header and the "… (+N more)" tail).</summary>
    private const int MinSignalsLength = 80;

    private static readonly Regex SignalsSegment = new(@"^\d+ Signals? could not be created: ", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The attention text for <paramref name="items"/> (distinct entries, first occurrence wins), or null when there are none: the caller
    /// then writes nothing, so an empty or null value is never stored.
    /// </summary>
    internal static string? Build(IEnumerable<Item> items) => Build(items, InvoiceSubmissionPersister.AdminAttentionMaxLength);

    /// <summary><see cref="Build(IEnumerable{Item})"/> within <paramref name="maxLength"/> characters (never below <see cref="MinSignalsLength"/>).</summary>
    internal static string? Build(IEnumerable<Item> items, int maxLength)
    {
        maxLength = Math.Max(maxLength, MinSignalsLength);
        var parts = items.Distinct().Select(Describe).ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var header = (parts.Count == 1 ? "1 Signal" : parts.Count + " Signals") + " could not be created: ";
        var full = header + string.Join(", ", parts);
        if (full.Length <= maxLength)
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
            if (candidate > maxLength)
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
    internal static string? FromFindings(JsonObject findings) => FromFindings(findings, InvoiceSubmissionPersister.AdminAttentionMaxLength);

    private static string? FromFindings(JsonObject findings, int maxLength) =>
        Build(Read(findings, InvoiceSubmissionPersister.SignalPlanRefusalsMember).Concat(Read(findings, InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember)), maxLength);

    // ── Review fixes R3 and R5: the other sources, and the merge ──

    /// <summary>
    /// R3: the segment for a submission whose processing stopped while it links its invoice. Names the reason code only (ADR-015); the
    /// code is cut at <see cref="MaxCodeLength"/> characters.
    /// </summary>
    internal static string StoppedNote(string? code) =>
        $"{StoppedPrefix}{CodeOnly(code)}; the invoice is linked and the submission stays Processing: review it, then resume or correct";

    /// <summary>
    /// ebilling-r1 task 094 (procedure-and-file-model 9.1 step 5): the segment for a submission whose commit the gate undid
    /// (<see cref="SubmissionCommitGate"/>). Names the reason code only (ADR-015) and says what was kept: a filing of the source
    /// communication made by the from-document route is not undone.
    /// </summary>
    internal static string UndoneNote(string? code) =>
        $"{UndonePrefix}{CodeOnly(code)}; the invoice, its lines, approval and file were removed and the submission is Failed; a filing of its source communication made at submission is kept";

    /// <summary>
    /// R5: the segment for a system billing policy that is not usable. <paramref name="state"/> is
    /// <see cref="InvoiceSubmissionAfterSteps.ReadSystemPolicyAsync"/>'s; <c>disabled</c> (the administrator turned the policy off, D-125) and
    /// <c>in_force</c> give null: no attention.
    /// </summary>
    internal static string? PolicyNote(string policyCode, string state)
    {
        var description = state switch
        {
            "disabled" or "in_force" => null,
            "missing" => "missing",
            "ambiguous" => "ambiguous (more than one)",
            "without_a_version_in_force" => "without a version in force",
            "of_another_rule_type" => "of the wrong rule type",
            _ => $"not usable ({CodeOnly(state)})",
        };
        return description is null ? null : $"{PolicyPrefix}{CodeOnly(policyCode)} is {description}: system Signals were not created";
    }

    /// <summary>
    /// <paramref name="current"/> with the segments given here replacing their kind and every other segment kept. The Signal segment is
    /// rebuilt from <paramref name="findings"/> (the stored refusals) when it is given, within the room the others leave, otherwise the
    /// current one is kept. Deterministic: the order is always signals, policy, stopped, then any other text; the whole is capped at
    /// <see cref="InvoiceSubmissionPersister.AdminAttentionMaxLength"/> characters. Null when there is nothing to say.
    /// </summary>
    /// <remarks>Task 094: <paramref name="undoneNote"/> replaces the undone segment AND withdraws a "Processing stopped" one (the submission no
    /// longer stays Processing); it is placed after the stopped slot.</remarks>
    internal static string? Merge(
        string? current, JsonObject? findings = null, string? policyNote = null, string? stoppedNote = null, string? undoneNote = null)
    {
        string? signals = null, policy = null, stopped = null, undone = null;
        var others = new List<string>();
        foreach (var part in (current ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (SignalsSegment.IsMatch(part))
            {
                signals ??= part;
            }
            else if (part.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            {
                policy ??= part;
            }
            else if (part.StartsWith(StoppedPrefix, StringComparison.Ordinal))
            {
                stopped ??= part;
            }
            else if (part.StartsWith(UndonePrefix, StringComparison.Ordinal))
            {
                undone ??= part;
            }
            else
            {
                others.Add(part);
            }
        }

        policy = policyNote ?? policy;
        stopped = undoneNote is null ? stoppedNote ?? stopped : null;
        undone = undoneNote ?? undone;
        var rest = new[] { policy, stopped, undone }.Concat(others).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (findings is not null)
        {
            var room = InvoiceSubmissionPersister.AdminAttentionMaxLength - rest.Sum(p => p.Length + Separator.Length);
            signals = FromFindings(findings, room);
        }

        var all = new List<string>();
        if (signals is not null)
        {
            all.Add(signals);
        }

        all.AddRange(rest);
        if (all.Count == 0)
        {
            return null;
        }

        var text = string.Join(Separator, all);
        return text.Length <= InvoiceSubmissionPersister.AdminAttentionMaxLength
            ? text
            : text[..(InvoiceSubmissionPersister.AdminAttentionMaxLength - 1)] + "…";
    }

    /// <summary>
    /// R7: <paramref name="current"/> without its "Processing stopped" segment. <c>Changed</c> is false (and <c>Text</c> is
    /// <paramref name="current"/> as it is) when there is no such segment: the caller then writes nothing. When it is true, <c>Text</c> is
    /// every other segment in its stored order (the Signal, policy and any text an administrator typed are kept), or null when the stopped
    /// segment was all there was: the one case where code empties the column, because it is withdrawing its own statement that has become
    /// false (the submission completed), not clearing the administrator's review. Pure and total: it never throws.
    /// </summary>
    internal static (bool Changed, string? Text) WithoutStopped(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return (false, current);
        }

        var parts = current.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = parts.Where(p => !p.StartsWith(StoppedPrefix, StringComparison.Ordinal)).ToList();
        if (kept.Count == parts.Length)
        {
            return (false, current);
        }

        return (true, kept.Count == 0 ? null : string.Join(Separator, kept));
    }

    /// <summary>
    /// R3 / R5: merges <paramref name="policyNote"/> and/or <paramref name="stoppedNote"/> into the submission's <c>sprk_adminattention</c>:
    /// reads the current value and the stored findings, composes with <see cref="Merge"/> and writes ONE column only when the result differs
    /// (idempotent: a redelivery with the same cause writes nothing). This is not a status write. A fault - the read, the parse or the write -
    /// is logged (type name only) and swallowed: the caller's outcome never changes. Returns true when it wrote.
    /// </summary>
    internal static async Task<bool> NoteAsync(
        IGenericEntityService entities, Guid submissionId, string? policyNote, string? stoppedNote, ILogger logger, CancellationToken ct)
    {
        try
        {
            var row = await entities.RetrieveAsync(
                FinanceBaseValues.SubmissionEntity, submissionId,
                [InvoiceSubmissionPersister.SubmissionAdminAttention, InvoiceSubmissionPersister.SubmissionFindingsJson], ct).ConfigureAwait(false);
            var current = row?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
            var merged = Merge(current, ParseFindings(row?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson)), policyNote, stoppedNote);
            if (merged is null || string.Equals(merged, current, StringComparison.Ordinal))
            {
                return false;
            }

            await entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
            {
                [InvoiceSubmissionPersister.SubmissionAdminAttention] = merged,
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Invoice submission {SubmissionId}: the Needs Admin Attention text could not be written ({Fault}); the outcome is unchanged",
                submissionId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>The current <c>sprk_adminattention</c> of a submission (null when empty); a fault propagates.</summary>
    internal static async Task<string?> ReadCurrentAsync(IGenericEntityService entities, Guid submissionId, CancellationToken ct)
    {
        var row = await entities.RetrieveAsync(
            FinanceBaseValues.SubmissionEntity, submissionId, [InvoiceSubmissionPersister.SubmissionAdminAttention], ct).ConfigureAwait(false);
        return row?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
    }

    /// <summary>The findings JSON as an object, or null when it is empty or unreadable (the current Signal segment is then kept).</summary>
    private static JsonObject? ParseFindings(string? findingsJson)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(findingsJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CodeOnly(string? code)
    {
        var text = string.IsNullOrWhiteSpace(code) ? "unknown" : code.Trim();
        return text.Length > MaxCodeLength ? text[..MaxCodeLength] : text;
    }

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
