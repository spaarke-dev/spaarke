namespace Sprk.Bff.Api.Services.Ai.Insights.Composition;

/// <summary>
/// Reusable helper that composes / merges retrieval-result text blocks from multiple
/// substrate tiers (operational + derived) into a single prompt-ready block.
/// </summary>
/// <remarks>
/// <para>
/// <b>Background (Q5 audit, 2026-05-28)</b>: <see cref="Sprk.Bff.Api.Services.Ai.Nodes.AiAnalysisNodeExecutor"/>
/// already implements a three-tier merge (L1 reference knowledge + L2 customer documents +
/// L3 entity context) via a private <c>MergeKnowledgeContext</c> helper. The Q5 audit
/// recommendation for task 022 (D-P12 IndexRetrieveNode) was: <b>do not re-implement the
/// composition</b> — extract it into a shared helper consumable by both the existing
/// <c>AiAnalysisNodeExecutor</c> and the new <c>IndexRetrieveNode</c>.
/// </para>
/// <para>
/// <b>Behavior preservation</b>: this helper is a literal lift of <c>AiAnalysisNodeExecutor</c>'s
/// merge logic — null-safe, whitespace-trim-respecting, separator-aware. Existing callers
/// (<c>AiAnalysisNodeExecutor</c>) delegate to <see cref="Merge(string?, string?)"/> instead
/// of carrying their own private copy. The visible behavior is identical.
/// </para>
/// <para>
/// <b>Why not just one method?</b> The class is the natural extension point for the upcoming
/// (D-A35, Phase 1.5) operational+derived three-tier RRF / rank-fusion composer. Keeping the
/// helper class today lets the IndexRetrieveNode + AiAnalysisNodeExecutor consume the same
/// API regardless of how the merge logic evolves.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Insights/Composition/</c>.
/// </para>
/// </remarks>
public static class MultiIndexComposer
{
    /// <summary>
    /// Merges two knowledge-context text blocks, preserving the conventional ordering
    /// (reference / retrieved knowledge first, scope / inline knowledge second).
    /// </summary>
    /// <param name="referenceKnowledge">
    /// Reference / RAG / index-retrieval-derived knowledge (L1 / L2 / L3 / Observations / Precedents).
    /// May be <c>null</c> or whitespace.
    /// </param>
    /// <param name="scopeKnowledge">
    /// Scope / inline knowledge attached to the playbook node. May be <c>null</c> or whitespace.
    /// </param>
    /// <returns>
    /// Combined knowledge context string separated by a blank line, or <c>null</c> if
    /// both inputs are <c>null</c>/whitespace. When exactly one input is present, returns
    /// it unchanged (no leading or trailing separator).
    /// </returns>
    public static string? Merge(string? referenceKnowledge, string? scopeKnowledge)
    {
        if (string.IsNullOrWhiteSpace(referenceKnowledge) && string.IsNullOrWhiteSpace(scopeKnowledge))
            return null;

        if (string.IsNullOrWhiteSpace(referenceKnowledge))
            return scopeKnowledge;

        if (string.IsNullOrWhiteSpace(scopeKnowledge))
            return referenceKnowledge;

        return $"{referenceKnowledge}\n\n{scopeKnowledge}";
    }

    /// <summary>
    /// Merges an ordered set of knowledge-context text blocks into a single block.
    /// Null / whitespace entries are silently skipped. Order is preserved.
    /// </summary>
    /// <param name="blocks">Knowledge blocks in priority order (highest priority first).</param>
    /// <returns>Concatenated block separated by blank lines, or <c>null</c> if all entries are empty.</returns>
    public static string? Merge(params string?[] blocks)
    {
        if (blocks is null || blocks.Length == 0)
            return null;

        string? merged = null;
        foreach (var block in blocks)
            merged = Merge(merged, block);

        return merged;
    }
}
