namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Describes WHERE SprkChat is embedded, enabling entity-scoped search and
/// playbook discovery without coupling SprkChat to any specific host workspace.
///
/// When provided, the host context:
///   - Scopes <see cref="Services.Ai.Chat.Tools.DocumentSearchTools.SearchDiscoveryAsync"/>
///     to the parent entity boundary (prevents cross-workspace data leakage).
///   - Enables playbook discovery filtered by entity type and workspace.
///
/// All properties except <see cref="EntityType"/> and <see cref="EntityId"/> are optional.
/// Reuses validation from <see cref="ParentEntityContext.EntityTypes.IsValid"/>.
/// </summary>
/// <param name="EntityType">
/// Business entity type where the chat is embedded.
/// <para>
/// Accepts either the canonical short form (<c>matter</c>, <c>project</c>,
/// <c>invoice</c>, <c>account</c>, <c>contact</c>) OR the raw Dataverse logical
/// name (<c>sprk_matter</c>, <c>sprk_project</c>, <c>sprk_invoice</c>). The
/// boundary normalizer (<see cref="EntityTypeNormalizer"/>) is applied
/// unconditionally in the constructor, so every downstream consumer reads the
/// canonical short form regardless of which convention the client used. See
/// R7 Wave 12 task 150 / audit 120 Gap A for the disposition.
/// </para>
/// <para>
/// Non-parent-business types (e.g. <c>sprk_analysisoutput</c> used by the
/// analysis-session HostContext slot per ChatEndpoints.cs SendMessageAsync)
/// pass through unchanged.
/// </para>
/// </param>
/// <param name="EntityId">
/// GUID of the parent entity record in Dataverse.
/// </param>
/// <param name="EntityName">
/// Display name of the parent entity (used for logging and UI, not filtering).
/// </param>
/// <param name="WorkspaceType">
/// The workspace hosting SprkChat, e.g. "LegalWorkspace", "AnalysisWorkspace", "FinanceWorkspace".
/// Used for telemetry and future workspace-specific playbook filtering.
/// </param>
/// <param name="PageType">
/// The page or view within the workspace where SprkChat is embedded,
/// e.g. "MainForm", "AnalysisView", "DocumentPanel".
/// Used for context-aware prompt selection and telemetry.
/// </param>
public sealed record ChatHostContext(
    string EntityType,
    string EntityId,
    string? EntityName = null,
    string? WorkspaceType = null,
    string? PageType = null)
{
    private readonly string _entityType = EntityTypeNormalizer.Normalize(EntityType)!;

    /// <summary>
    /// Inbound entity type, normalized via <see cref="EntityTypeNormalizer"/> at
    /// every construction path (primary constructor, <c>with</c> expressions,
    /// System.Text.Json deserialization). See R7 Wave 12 task 150 / audit 120
    /// Gap A: SpaarkeAi client passes raw <c>sprk_matter</c>; BFF system-prompt
    /// enrichment, matter-memory, and parent-entity-scoped RAG search expect
    /// canonical <c>matter</c>. Normalizing here is the single point that fixes
    /// all five downstream surfaces without changing any of their signatures.
    /// </summary>
    /// <remarks>
    /// The init-accessor (not constructor-body) pattern is load-bearing: positional
    /// records also synthesize a clone method for <c>with</c> expressions that
    /// assigns to init setters directly without re-running constructor logic. By
    /// normalizing in the init setter we cover the constructor path, the <c>with</c>
    /// path, and System.Text.Json deserialization (which uses init setters).
    /// The non-nullable suppression (<c>!</c>) on the backing field initializer is
    /// correct: <see cref="EntityTypeNormalizer.Normalize"/> only returns null for
    /// null input, which the non-nullable parameter type disallows.
    /// </remarks>
    public string EntityType
    {
        get => _entityType;
        init => _entityType = EntityTypeNormalizer.Normalize(value)!;
    }

    /// <summary>
    /// Validates that the host context has valid required fields.
    /// </summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(EntityType) &&
        !string.IsNullOrWhiteSpace(EntityId) &&
        ParentEntityContext.EntityTypes.IsValid(EntityType);
}
