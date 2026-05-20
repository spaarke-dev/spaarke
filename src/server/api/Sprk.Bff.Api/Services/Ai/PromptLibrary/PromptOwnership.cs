namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// Defines the ownership tier of a prompt template.
///
/// Storage mapping (ADR-015):
///   - Personal  → Cosmos DB <c>prompts</c> container, partition key = userId
///   - Team      → Cosmos DB <c>prompts</c> container, partition key = teamId
///   - Org       → Dataverse <c>sprk_prompttemplate</c> table (admin-managed, read-only via API)
///   - System    → Dataverse <c>sprk_prompttemplate</c> table (product-shipped, read-only via API)
/// </summary>
public enum PromptOwnership
{
    /// <summary>User-owned private template. Stored in Cosmos; only visible to the owning user.</summary>
    Personal = 0,

    /// <summary>Team-shared template. Stored in Cosmos; visible to all members of the owning team.</summary>
    Team = 1,

    /// <summary>Organisation-wide read-only template. Stored in Dataverse; managed by admins.</summary>
    Organization = 2,

    /// <summary>Product-shipped read-only template. Stored in Dataverse; managed by the Spaarke team.</summary>
    System = 3
}
