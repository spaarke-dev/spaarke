using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Controls how knowledge retrieval behaves for an individual action node.
/// Stored in the node's <c>ConfigJson</c> under the <c>knowledgeRetrieval</c> property.
/// </summary>
/// <remarks>
/// <para>
/// When absent from ConfigJson, defaults are applied (mode = Auto, topK = 5),
/// preserving backward compatibility with nodes created before this feature.
/// </para>
/// <para>
/// Mode semantics:
/// <list type="bullet">
///   <item><c>Auto</c> — retrieve only when RagIndex knowledge sources are linked (existing behavior).</item>
///   <item><c>Always</c> — retrieve using domain matching even without explicit knowledge source links.</item>
///   <item><c>Never</c> — skip retrieval entirely, even if knowledge sources are linked.</item>
/// </list>
/// </para>
/// </remarks>
public sealed record KnowledgeRetrievalConfig
{
    /// <summary>
    /// Retrieval mode controlling when knowledge retrieval is executed.
    /// Default: <see cref="KnowledgeRetrievalMode.Auto"/>.
    /// </summary>
    public KnowledgeRetrievalMode Mode { get; init; } = KnowledgeRetrievalMode.Auto;

    /// <summary>
    /// Maximum number of reference chunks to retrieve.
    /// Default: 5, Max: 20. Passed to <see cref="ReferenceSearchOptions.TopK"/>.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// When true, include document-level context in retrieval (L2 — future).
    /// Default: false.
    /// </summary>
    public bool IncludeDocumentContext { get; init; }

    /// <summary>
    /// When true, include entity-level context in retrieval (L3 — future).
    /// Default: false.
    /// </summary>
    public bool IncludeEntityContext { get; init; }

    /// <summary>
    /// Returns the default configuration (Auto mode, TopK=5, no L2/L3).
    /// </summary>
    public static KnowledgeRetrievalConfig Default { get; } = new();

    /// <summary>
    /// Clamps <see cref="TopK"/> to the valid range [1, 20].
    /// </summary>
    public int EffectiveTopK => Math.Clamp(TopK, 1, 20);
}

/// <summary>
/// Controls when knowledge retrieval is executed for an action node.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KnowledgeRetrievalMode
{
    /// <summary>
    /// Retrieve only when RagIndex knowledge sources are linked to the node (default, backward compatible).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always retrieve, using domain matching even without explicit knowledge source links.
    /// </summary>
    Always = 1,

    /// <summary>
    /// Never retrieve knowledge, even if knowledge sources are linked.
    /// </summary>
    Never = 2
}
