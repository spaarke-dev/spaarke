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
/// Valid values: matter, project, invoice, account, contact.
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
public sealed record ChatHostContext(
    string EntityType,
    string EntityId,
    string? EntityName = null,
    string? WorkspaceType = null)
{
    /// <summary>
    /// Validates that the host context has valid required fields.
    /// </summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(EntityType) &&
        !string.IsNullOrWhiteSpace(EntityId) &&
        ParentEntityContext.EntityTypes.IsValid(EntityType);
}
