using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// SSE event emitted by the BFF to surface the top-N candidate playbooks to the chat
/// frontend after file-aware classification (chat-routing-redesign-r1 task 117a / FR-49).
/// Consumed by the chat frontend (task 117b) to render inline link buttons + an
/// "Open Library" call-to-action.
///
/// <para>
/// <b>Wire format</b>:
/// <code>
/// event: playbook_options
/// data: {
///   "type": "playbook_options",
///   "data": {
///     "candidates": [
///       { "playbookId": "...", "playbookCode": "...", "displayName": "...", "confidence": 0.92, "reason": "top-confidence" }
///     ],
///     "libraryModalCta": true,
///     "sessionAttachmentIds": ["..."],
///     "rerankInvoked": true,
///     "rerankReason": "ambiguous-top-2-within-margin"
///   }
/// }
///
/// </code>
/// </para>
///
/// <para>
/// <b>FR-48 invariant</b>: this event carries NO "auto-execute" flag. The user
/// MUST click a candidate (or open the library modal) to invoke a playbook.
/// </para>
///
/// <para>
/// <b>FR-51 invariant</b>: <see cref="PlaybookOptionsSseEventData.LibraryModalCta"/>
/// is ALWAYS <c>true</c>, regardless of candidate count (including the zero-candidate
/// no-match case). The library modal is the universal escape hatch.
/// </para>
///
/// <para>
/// <b>ADR-015 tier-1 audit safety (binding)</b>: the payload contains ONLY
/// deterministic identifiers + counts + admin-facing display names. The following
/// are FORBIDDEN in any payload field or any log emitted by the producer:
/// <list type="bullet">
///   <item><description>User message text (verbatim or paraphrased)</description></item>
///   <item><description>File content / extracted text / previews / filenames containing user data</description></item>
///   <item><description>Embedding vectors or per-file similarity scores</description></item>
///   <item><description>Raw LLM response strings (only short controlled-vocabulary reason tags)</description></item>
///   <item><description>Candidate descriptions beyond <see cref="PlaybookOptionCandidate.DisplayName"/></description></item>
/// </list>
/// <see cref="PlaybookOptionCandidate.Reason"/> MUST be a short controlled-vocabulary
/// label (e.g. <c>"top-confidence"</c>, <c>"llm-rerank-from-5"</c>,
/// <c>"timeout-graceful-degrade"</c>), NEVER free-form NL from an LLM.
/// </para>
///
/// <para>
/// <b>ADR-013 boundary</b>: this event is produced inside the
/// <c>Services/Ai/Chat/</c> internal orchestration boundary and emitted through the
/// existing chat SSE writer. It is NOT part of <c>Services/Ai/PublicContracts/</c>.
/// </para>
/// </summary>
public static class PlaybookOptionsSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client
    /// matches on. Bound by FR-49 — do NOT rename.
    /// </summary>
    public const string EventType = "playbook_options";
}

/// <summary>
/// Structured data payload for <c>playbook_options</c> SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Sprk.Bff.Api.Api.Ai.ChatSseEvent"/>.
/// Shape is locked by spec FR-49; tests in
/// <c>PlaybookOptionsEventBuilderTests</c> enforce the field set.
/// </summary>
/// <param name="Candidates">
/// Ordered top-N candidates (highest confidence first). May be empty when no playbook
/// crossed the secondary confidence threshold (graceful no-match path).
/// </param>
/// <param name="LibraryModalCta">
/// Always <c>true</c> per FR-51 — the frontend ALWAYS renders the "Open Library" CTA
/// alongside the candidate buttons (or alone in the no-match case).
/// </param>
/// <param name="SessionAttachmentIds">
/// Deterministic session attachment identifiers (file IDs assigned by the chat session
/// upload pipeline). Surfacing these lets the frontend correlate the candidates back to
/// the specific attachments the user uploaded this turn. ADR-015 tier-1: opaque IDs only —
/// NO filenames, NO MIME types, NO sizes, NO content.
/// </param>
/// <param name="RerankInvoked">
/// <c>true</c> when the upstream <c>IIntentRerankerService</c> was called to refine the
/// candidate list (i.e. <c>PlaybookCandidateSelection.RerankRecommended</c> was <c>true</c>
/// and the reranker executed). Useful telemetry signal for FR-46 budget verification on the
/// frontend.
/// </param>
/// <param name="RerankReason">
/// Telemetry tag explaining the rerank outcome when <see cref="RerankInvoked"/> is
/// <c>true</c>. Mirrors the controlled-vocabulary tags from
/// <c>IntentRerankerResult.Reason</c>. <c>null</c> when <see cref="RerankInvoked"/> is
/// <c>false</c>.
/// </param>
public sealed record PlaybookOptionsSseEventData(
    [property: JsonPropertyName("candidates")] IReadOnlyList<PlaybookOptionCandidate> Candidates,
    [property: JsonPropertyName("libraryModalCta")] bool LibraryModalCta,
    [property: JsonPropertyName("sessionAttachmentIds")] IReadOnlyList<string> SessionAttachmentIds,
    [property: JsonPropertyName("rerankInvoked")] bool RerankInvoked,
    [property: JsonPropertyName("rerankReason")] string? RerankReason);

/// <summary>
/// A single candidate playbook entry in the <c>playbook_options</c> SSE payload.
/// Shape locked by spec FR-49 — exactly five fields. Tests enforce that NO additional
/// fields are added (the contract is the integration surface for the frontend in task
/// 117b).
/// </summary>
/// <param name="PlaybookId">
/// Opaque immutable Dataverse PK (<c>sprk_aiplaybook</c> GUID, string form). The
/// frontend uses this as the click target identifier.
/// </param>
/// <param name="PlaybookCode">
/// Portable cross-environment short code (e.g. <c>"PB-013"</c>). Empty string when the
/// upstream selector / reranker did not supply a code — the orchestrator that wires this
/// builder into the chat streaming flow MAY enrich via <c>IPlaybookLookupService</c>
/// before emit.
/// </param>
/// <param name="DisplayName">
/// Admin-facing playbook name (<c>sprk_name</c>). ADR-015 tier-1 safe — this is
/// configuration content, not user-generated content.
/// </param>
/// <param name="Confidence">
/// Aggregated similarity score in the unit interval <c>[0, 1]</c>. Comes from the
/// upstream <c>PlaybookCandidate.Confidence</c> (MAX across files for the multi-file
/// case).
/// </param>
/// <param name="Reason">
/// Short controlled-vocabulary label explaining why this candidate was surfaced.
/// Examples: <c>"top-confidence"</c>, <c>"ambiguous-top-2-within-margin"</c>,
/// <c>"llm-rerank-from-5"</c>, <c>"timeout-graceful-degrade"</c>. NEVER free-form NL —
/// the controlled-vocabulary constraint is what makes the payload ADR-015 tier-1 safe
/// in the absence of an LLM-output filter.
/// </param>
public sealed record PlaybookOptionCandidate(
    [property: JsonPropertyName("playbookId")] string PlaybookId,
    [property: JsonPropertyName("playbookCode")] string PlaybookCode,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("reason")] string Reason);
