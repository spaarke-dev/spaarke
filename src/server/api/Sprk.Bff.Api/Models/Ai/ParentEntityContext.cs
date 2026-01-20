namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents the parent business entity that owns a document.
/// Used for entity-scoped semantic search operations.
/// </summary>
/// <remarks>
/// <para>
/// Parent entity context enables filtering search results by the business entity
/// (Matter, Project, Invoice, Account, or Contact) that owns the documents.
/// </para>
/// <para>
/// All properties are required when a parent entity context is provided.
/// For documents without parent entity association, pass null instead of
/// creating an incomplete context.
/// </para>
/// </remarks>
/// <param name="EntityType">
/// The type of parent entity. Valid values: matter, project, invoice, account, contact.
/// </param>
/// <param name="EntityId">
/// The unique identifier (GUID) of the parent entity in Dataverse.
/// </param>
/// <param name="EntityName">
/// The display name of the parent entity for search result presentation.
/// </param>
public sealed record ParentEntityContext(
    string EntityType,
    string EntityId,
    string EntityName)
{
    /// <summary>
    /// Valid parent entity types for semantic search scoping.
    /// </summary>
    public static class EntityTypes
    {
        /// <summary>Matter entity type (legal matter, case, or project).</summary>
        public const string Matter = "matter";

        /// <summary>Project entity type.</summary>
        public const string Project = "project";

        /// <summary>Invoice entity type.</summary>
        public const string Invoice = "invoice";

        /// <summary>Account entity type (organization, company).</summary>
        public const string Account = "account";

        /// <summary>Contact entity type (person).</summary>
        public const string Contact = "contact";

        /// <summary>
        /// All valid entity types for validation.
        /// </summary>
        public static readonly string[] All = [Matter, Project, Invoice, Account, Contact];

        /// <summary>
        /// Validates whether the given type is a valid parent entity type.
        /// </summary>
        /// <param name="type">The entity type to validate.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public static bool IsValid(string? type) =>
            !string.IsNullOrWhiteSpace(type) && All.Contains(type.ToLowerInvariant());
    }
}
