namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Boundary normalization for entity-type names received in <see cref="ChatHostContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b> (R7 Wave 12 task 150 / audit 120 Gap A):
/// SpaarkeAi clients launched from Power Apps pass the raw Dataverse logical name
/// (e.g. <c>sprk_matter</c>, <c>sprk_project</c>, <c>sprk_invoice</c>) as the chat
/// host context EntityType, because the client reads URL params directly without
/// invoking <c>useEntityResolver</c>. Multiple BFF surfaces — entity-enrichment in
/// the system prompt, matter-memory injection, parent-entity-scoped RAG search —
/// however operate on the canonical short form (<c>matter</c>, <c>project</c>,
/// <c>invoice</c>, <c>account</c>, <c>contact</c>) used by the document-index
/// writer (<see cref="Sprk.Bff.Api.Api.Ai.RagEndpoints"/>) and by
/// <see cref="ParentEntityContext.EntityTypes"/>. The split caused every
/// matter-scoped UAT scenario to silently no-op ("nothing fixed").
/// </para>
/// <para>
/// <b>Fix shape</b>: normalize once at the BFF chat-session boundary
/// (in <see cref="ChatHostContext"/>'s constructor) so every downstream consumer
/// sees the canonical form unchanged. This is the smallest blast radius — five
/// downstream surfaces stop branching on convention without any signature change.
/// </para>
/// <para>
/// <b>Backward compatibility</b>: clients may continue sending either form. Raw
/// <c>sprk_matter</c> normalizes to <c>matter</c>; already-canonical
/// <c>matter</c> passes through unchanged. Non-parent-business types (e.g.
/// <c>sprk_analysisoutput</c>, used as a HostContext slot for analysis sessions
/// per ChatEndpoints.cs:911) are NOT normalized — they pass through verbatim.
/// </para>
/// <para>
/// <b>Out of scope</b>: <see cref="Sprk.Bff.Api.Services.Ai.Chat.StandaloneChatContextProvider"/>
/// and <see cref="Sprk.Bff.Api.Services.Ai.Chat.Tools.DataverseQueryTools"/> still
/// operate on raw logical names (a different bounded context — pre-session probe
/// and tool-allow-listing). Audit 120 disposition §A: minimum-touch — normalize
/// at the chat-session boundary only.
/// </para>
/// </remarks>
internal static class EntityTypeNormalizer
{
    /// <summary>
    /// Returns the canonical short form for a parent business entity type when the
    /// input matches a known raw Dataverse logical name; otherwise returns the input
    /// unchanged (with whitespace and casing preserved).
    /// </summary>
    /// <param name="entityType">
    /// Inbound EntityType value from a chat host context. May be canonical
    /// (<c>matter</c>), raw Dataverse (<c>sprk_matter</c>), null, empty,
    /// or an unrelated value (e.g. <c>sprk_analysisoutput</c>).
    /// </param>
    /// <returns>
    /// Canonical form when known; the original input otherwise (so pre-existing
    /// raw-form-only consumers continue to receive their raw inputs unchanged).
    /// </returns>
    public static string? Normalize(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return entityType;
        }

        // Case-insensitive match on the trimmed input; preserve the original on miss.
        var trimmed = entityType.Trim();

        return trimmed.ToLowerInvariant() switch
        {
            "sprk_matter" => ParentEntityContext.EntityTypes.Matter,
            "sprk_project" => ParentEntityContext.EntityTypes.Project,
            "sprk_invoice" => ParentEntityContext.EntityTypes.Invoice,
            // Already-canonical forms — return canonical (lowercased) for stability.
            "matter" => ParentEntityContext.EntityTypes.Matter,
            "project" => ParentEntityContext.EntityTypes.Project,
            "invoice" => ParentEntityContext.EntityTypes.Invoice,
            "account" => ParentEntityContext.EntityTypes.Account,
            "contact" => ParentEntityContext.EntityTypes.Contact,
            // Unknown / non-parent-business types (e.g. sprk_analysisoutput) pass
            // through unchanged so non-chat HostContext consumers (analysis sessions,
            // dataverse-query tool allow-list) continue to function.
            _ => entityType
        };
    }
}
