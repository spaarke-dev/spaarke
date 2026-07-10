using System.Security.Claims;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Semantic-scope provider interface (FR-B-12) — the ContextEnvelope <see cref="SemanticSlice"/>'s
/// retrieval seam (spaarke-ai-architecture-redesign-r2 task AIR2-061).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — the Semantic-scope retrieval
/// implementation already ships as <see cref="RagService"/> (Azure AI Search hybrid search + SPE),
/// unlike the sibling Organizational slice which has no implementation yet. (2) <i>Extension</i> — this
/// interface does NOT reimplement retrieval; it is a thin, Binder-consumable seam over the existing
/// <see cref="IRagService"/>. (3) <i>Cost-of-doing-nothing</i> — without a stable provider-shaped
/// contract, the ContextEnvelope Semantic slice (task 015) has no formalized retrieval seam to bind
/// to, and any future consumer would either call <see cref="IRagService"/> directly (re-deriving the
/// tenant/ACL/topK plumbing per call site) or invent a second ad-hoc wrapper.
/// </para>
/// <para>
/// <b>WRAPS, does not fork</b>: the sole implementation (<see cref="SemanticScopeProvider"/>) delegates
/// every call to the existing shipped <see cref="IRagService"/>. No implementation of this interface may
/// call Azure AI Search directly or duplicate <c>RagService.BuildSearchOptions</c> logic.
/// </para>
/// <para>
/// <b>ACL preserved (AIPU2-027)</b>: <see cref="IRagService.SearchAsync(string, RagSearchOptions, CancellationToken)"/>
/// unconditionally appends the <see cref="Sprk.Bff.Api.Services.Ai.Security.PrivilegeFilterBuilder"/>
/// filter inside <c>RagService.BuildSearchOptions</c> for every non-session-scoped query — public-only
/// when no caller principal / no resolved groups (fail-closed). Because this interface only ever reaches
/// Azure AI Search THROUGH <see cref="IRagService"/>, that ACL can never be bypassed from this seam —
/// there is no alternate code path that constructs a <c>SearchClient</c>/<c>SearchOptions</c> itself.
/// </para>
/// <para>
/// <b>Semantic source is Azure AI Search + SPE, never Dataverse search</b> — honors the architecture
/// ruling that Dataverse is not a semantic-retrieval source (design D-M3). This interface and its sole
/// implementation carry no Dataverse dependency.
/// </para>
/// <para>
/// <b>Consumed by</b> the ContextEnvelope Semantic slice (task 015 <see cref="SemanticSlice"/> /
/// <see cref="RetrievalReference"/>) — <see cref="SemanticScopeResult.Retrieval"/> is exactly the
/// <see cref="RetrievalReference"/> shape the Context Binder (task 053) assembles into
/// <c>ContextEnvelope.Semantic.Retrieval</c>. Wiring the live per-turn Binder call site is intentionally
/// left to a follow-on integration step: task 060 (Organizational-scope provider) lands in the SAME
/// parallel wave and is likely to touch the same shared <c>ContextBinder.cs</c> /
/// <c>ContextBindingRequest</c> files — this task ships the bounded contract surface (interface + wrap)
/// without racing a concurrent agent on those files.
/// </para>
/// </remarks>
public interface ISemanticScopeProvider
{
    /// <summary>
    /// Resolves semantic retrieval context for a turn by delegating to <see cref="IRagService"/>. The
    /// privilege-filtered ACL is applied by <see cref="IRagService"/> itself — this method has no way
    /// to request an unfiltered search.
    /// </summary>
    Task<SemanticScopeResult> GetSemanticContextAsync(
        SemanticScopeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Request for <see cref="ISemanticScopeProvider.GetSemanticContextAsync"/>.</summary>
public sealed record SemanticScopeRequest
{
    /// <summary>Tenant identifier — forwarded unchanged to <see cref="RagSearchOptions.TenantId"/> (ADR-014 tenant isolation).</summary>
    public required string TenantId { get; init; }

    /// <summary>The semantic query text.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// The calling user's principal, forwarded unchanged to <see cref="RagSearchOptions.CallerPrincipal"/>.
    /// Null means "no caller" — <see cref="IRagService"/>'s privilege-group resolution treats that as
    /// public-only (fail-closed), never as "unfiltered"/"admin". This provider never substitutes or
    /// elevates the supplied principal.
    /// </summary>
    public ClaimsPrincipal? CallerPrincipal { get; init; }

    /// <summary>Maximum number of retrieval references to return. Default 5 (mirrors <see cref="RagSearchOptions.TopK"/> default).</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Optional parent-entity scoping (both must be set together — mirrors <see cref="RagSearchOptions"/>).</summary>
    public string? ParentEntityType { get; init; }

    /// <summary>Optional parent-entity scoping (both must be set together — mirrors <see cref="RagSearchOptions"/>).</summary>
    public string? ParentEntityId { get; init; }
}

/// <summary>Result of <see cref="ISemanticScopeProvider.GetSemanticContextAsync"/> — Binder-consumable retrieval references.</summary>
public sealed record SemanticScopeResult
{
    /// <summary>
    /// Retrieval references in the exact shape <c>ContextEnvelope.SemanticSlice.Retrieval</c> expects
    /// (task 015). Empty when the underlying <see cref="IRagService"/> search returned no results above
    /// its score threshold — never null.
    /// </summary>
    public IReadOnlyList<RetrievalReference> Retrieval { get; init; } = Array.Empty<RetrievalReference>();
}
