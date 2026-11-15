using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

/// <summary>
/// What the decision-plan and lookup tools (spaarkeai-mcp-bff-api-r1 task 023, FR-06, FR-07) share: the no-caller guard, the app-only
/// tool metadata, and the success result. Kept in one place so the two tools cannot disagree on any of them.
/// </summary>
/// <remarks>
/// <para><b>Success bodies are the REST JSON.</b> A success carries the same members, in the same case and with the same nulls, as the
/// REST route's answer (<c>JsonSerializerDefaults.Web</c>, which is what <c>Results.Ok</c> writes in this BFF), so the widget's
/// <c>ResponseFetchFn</c> shim can wrap a member of <c>structuredContent</c> in a <c>Response</c> unchanged (task 023 notes). Failures
/// are not built here: they go through <see cref="McpToolResults.FromError"/> (ADR-019).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="McpToolResults"/> (failures only) and the
/// capability's own caller guard. Extension: neither can build a success result or the app-only metadata, and the caller guard must
/// run in the tool before any capability call (FR-10, ADR-002 WP-6); this class adds only those three. Cost of doing nothing: each
/// tool would carry its own copy of the guard and the metadata, and one could drift to a caller-less read.</para>
/// </remarks>
internal static class PlanAndLookupToolSupport
{
    /// <summary>The REST routes' JSON: web defaults, nulls written (a candidate's <c>email</c> is <c>null</c>, not absent).</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>_meta.ui.visibility = ["app"]</c> (MCP Apps): the host lists the tool for the widget only and keeps it out of the model's tool
    /// set (design §7.1). A new node per tool, because a <see cref="JsonNode"/> has one parent.
    /// </summary>
    internal static JsonObject AppOnlyMeta() =>
        new() { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } };

    /// <summary>
    /// True when the tool can go on: the request's context exists and carries a signed-in caller (the same test the capability applies
    /// and <c>RequireAuthorization</c>'s default policy makes). False is <c>UserContextRequired</c>: nothing is read, and nothing falls
    /// back to an app-only client (FR-10 as amended 2026-11-07, option C).
    /// </summary>
    internal static bool HasCaller(HttpContext? http) => http is not null && DecisionCapability.HasSignedInCaller(http.User);

    /// <summary>A success: a one-line summary (counts only, never record text, ADR-015) and the body as <c>structuredContent</c>.</summary>
    internal static CallToolResult Success(string summary, object body) =>
        new()
        {
            Content = [new TextContentBlock { Text = summary }],
            StructuredContent = JsonSerializer.SerializeToElement(body, body.GetType(), Json),
        };

    // ---- the bound on the row (FR-05, FR-11: every tool result < 64 KiB) -----------------------------------------------------------
    //
    // COMPONENT JUSTIFICATION (CLAUDE.md section 11), added inside this existing file.
    //   Existing: WorklistItemSummary.Clip (the list tools' text clip) and the REST worklist row, which has no cap on evidenceRefs or sentence.
    //   Extension: the REST row must stay byte-for-byte unchanged (FR-09), so the bound cannot go in Services/Signals or the route; it is
    //     applied to the copy the MCP tool returns, reusing Clip.
    //   Cost of doing nothing: about 100 evidence lines (about 350 result bytes each) take spaarke_get_decision_plan past 64 KiB and the host
    //     drops the result, so the wizard cannot open that item from Copilot at all.

    /// <summary>The most evidence lines the plan tool returns (a rule's witnesses are a handful today).</summary>
    internal const int MaxEvidenceLines = 100;

    /// <summary>
    /// The most the <c>evidenceRefs</c> string may take IN THE RESULT: its JSON-escaped length, since it is a JSON array stored as a string
    /// and every quote in it is escaped again. 20,000 leaves the largest plan the closed catalog can build (28.7 KB, measured) and the rest of
    /// the row (about 5 KB at its caps) well under 64 KiB with more than 10 KB to spare. Lines are kept in stored order until the next would pass it.
    /// </summary>
    internal const int EvidenceBudgetBytes = 20_000;

    /// <summary>Per evidence line: the longest string member (JSON bytes), the most members kept, the longest member name and the
    /// largest nested value kept. With these, one line is at most about 7 KB, so the first line always fits <see cref="EvidenceBudgetBytes"/>.</summary>
    internal const int EvidenceFieldBytes = 400;
    internal const int EvidenceMaxMembers = 16;
    internal const int EvidenceMaxNameBytes = 64;

    /// <summary>The row's clipped free text, as default-encoded JSON bytes. A 2,000-character rule sentence (the stored maximum) fits unchanged
    /// unless it carries hundreds of escaped characters.</summary>
    /// <remarks>Title and subject allow 1,000 encoded bytes: the default encoder escapes non-ASCII as six bytes, so a lower bound would clip
    /// an ordinary non-Latin name (240 bytes is about 40 such characters). The worst case stays under the 64 KiB result limit.</remarks>
    internal const int RowTitleBytes = 1_000;
    internal const int RowSubjectNameBytes = 1_000;
    internal const int RowRuleShortNameBytes = 120;
    internal const int RowSentenceBytes = 4_000;
    internal const int RowShortTextBytes = 80;

    /// <summary>What <see cref="BoundRow"/> did: the row to return, how many evidence lines were stored, and whether any were left out.</summary>
    internal readonly record struct BoundedRow(WorklistItemRow? Row, int EvidenceTotal, bool EvidenceTruncated);

    /// <summary>
    /// <paramref name="row"/> as the plan tool returns it: every free-text member clipped (with an ellipsis, <see cref="WorklistItemSummary.Clip"/>)
    /// and <c>evidenceRefs</c> cut to at most <see cref="MaxEvidenceLines"/> lines and <see cref="EvidenceBudgetBytes"/>. A row inside every bound
    /// is returned unchanged (same text, same evidence string, byte for byte as the REST row), so only an oversized row differs from the REST route.
    /// The caller reports <see cref="BoundedRow.EvidenceTotal"/> and <see cref="BoundedRow.EvidenceTruncated"/> so the omission is never silent.
    /// </summary>
    internal static BoundedRow BoundRow(WorklistItemRow? row)
    {
        if (row is null)
        {
            return new BoundedRow(null, 0, false);
        }

        var (evidence, total, truncated) = BoundEvidence(row.EvidenceRefs);
        var bounded = row with
        {
            Status = WorklistItemSummary.Clip(row.Status, RowShortTextBytes),
            Title = WorklistItemSummary.Clip(row.Title, RowTitleBytes),
            SubjectName = WorklistItemSummary.Clip(row.SubjectName, RowSubjectNameBytes),
            RuleShortName = WorklistItemSummary.Clip(row.RuleShortName, RowRuleShortNameBytes),
            WorkType = WorklistItemSummary.Clip(row.WorkType, RowShortTextBytes),
            PastDueWording = WorklistItemSummary.Clip(row.PastDueWording, RowShortTextBytes)!,
            DueDate = WorklistItemSummary.Clip(row.DueDate, RowShortTextBytes),
            EvidenceRefs = evidence,
            Sentence = WorklistItemSummary.Clip(row.Sentence, RowSentenceBytes),
        };
        return new BoundedRow(bounded, total, truncated);
    }

    private static (string? Evidence, int Total, bool Truncated) BoundEvidence(string? evidenceRefs)
    {
        if (evidenceRefs is null)
        {
            return (null, 0, false);
        }

        JsonArray? lines;
        try
        {
            lines = JsonNode.Parse(evidenceRefs) as JsonArray;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            lines = null; // malformed (including an invalid surrogate escape): treated as a non-array value below
        }

        if (lines is null)
        {
            // Not an array: the REST route does not produce this (it returns null); keep it only while it fits.
            return EncodedBytes(evidenceRefs) <= EvidenceBudgetBytes ? (evidenceRefs, 0, false) : (null, 0, true);
        }

        var kept = new JsonArray();
        var changed = false;
        var used = 2; // "[" and "]"
        foreach (var node in lines)
        {
            var line = BoundEvidenceLine(node, out var lineChanged);
            var size = EncodedBytes(line?.ToJsonString() ?? "null") + (kept.Count > 0 ? 1 : 0);
            if (kept.Count >= MaxEvidenceLines || used + size > EvidenceBudgetBytes)
            {
                break;
            }

            kept.Add(line);
            used += size;
            changed |= lineChanged;
        }

        // Untouched only when nothing was cut AND the stored string itself fits: a stored string with extra whitespace could otherwise
        // pass every per-line bound and still exceed the budget.
        if (kept.Count == lines.Count && !changed && EncodedBytes(evidenceRefs) <= EvidenceBudgetBytes)
        {
            return (evidenceRefs, lines.Count, false); // inside every bound: the stored string, untouched
        }

        return (kept.ToJsonString(), lines.Count, kept.Count < lines.Count);
    }

    /// <summary>One evidence line with every string member clipped, nested values over the same size and members past the limits dropped.</summary>
    private static JsonNode? BoundEvidenceLine(JsonNode? node, out bool changed)
    {
        changed = false;
        if (node is not JsonObject source)
        {
            if (node is JsonValue)
            {
                return node.DeepClone();
            }

            changed = node is not null; // a nested array is dropped, which is a change (the stored string must not be returned as-is)
            return null;
        }

        var line = new JsonObject();
        foreach (var (name, value) in source)
        {
            if (line.Count >= EvidenceMaxMembers || EncodedBytes(name) > EvidenceMaxNameBytes)
            {
                changed = true;
                continue;
            }

            if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            {
                var clipped = WorklistItemSummary.Clip(text, EvidenceFieldBytes);
                changed |= clipped != text;
                line[name] = clipped;
            }
            else if (value is null || value is JsonValue || EncodedBytes(value.ToJsonString()) <= EvidenceFieldBytes)
            {
                line[name] = value?.DeepClone();
            }
            else
            {
                changed = true;
            }
        }

        return line;
    }

    private static int EncodedBytes(string text) => JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;

    /// <summary>The <c>{ reasonCode, message }</c> of a part of the plan result that failed after the plan itself was returned.</summary>
    internal sealed record PartError(string ReasonCode, string Message)
    {
        internal static PartError From(DecisionToolError error) => new(error.ReasonCode, error.Message);
    }

    /// <summary>
    /// <c>structuredContent</c> of <c>spaarke_get_decision_plan</c>. <see cref="Plan"/> is the REST decision-plan body and
    /// <see cref="Suppression"/> the REST suppression body, each exactly as its route writes it. <see cref="Row"/> is the item's full
    /// worklist row as the REST worklist writes it; null when the item is no longer in the caller's Decide or Do lane.
    /// <see cref="SuppressionError"/> and <see cref="RowError"/> are set (and the member is null) when that part could not be read; the
    /// plan, and the plan-read stamp, stand regardless.
    /// <para><see cref="Row"/> is the REST row bounded by <see cref="BoundRow"/> (text clipped with an ellipsis, evidence lines capped):
    /// <see cref="EvidenceTotal"/> is how many evidence lines the item stores and <see cref="EvidenceTruncated"/> is true when
    /// <c>row.evidenceRefs</c> holds fewer of them. Both are always present (0 and false when there is no row or no evidence).</para>
    /// </summary>
    internal sealed record PlanBody(
        DecisionPlanResponse Plan,
        SuppressionResponse? Suppression,
        WorklistItemRow? Row,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PartError? SuppressionError = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PartError? RowError = null,
        int EvidenceTotal = 0,
        bool EvidenceTruncated = false);
}
