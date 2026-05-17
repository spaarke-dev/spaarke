using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.PromptLibrary;

/// <summary>
/// Cosmos DB document representation of a prompt template.
///
/// Partition key: <c>/ownerId</c>
///   - Personal tier  → userId
///   - Team tier      → teamId
///
/// The <c>id</c> field doubles as the templateId returned to callers.
/// Properties are serialised with camelCase names to match the CosmosClient serialiser options
/// configured in <c>AiPersistenceModule</c>.
/// </summary>
internal sealed class CosmosPromptDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Partition key value — userId (Personal) or teamId (Team).</summary>
    public string OwnerId { get; set; } = string.Empty;

    public PromptOwnership Ownership { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Body { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public List<CosmosTemplateVariable> Variables { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Converts to the public API record.</summary>
    public PromptTemplate ToPromptTemplate() => new(
        Id: Id,
        TenantId: TenantId,
        Name: Name,
        Description: Description,
        Body: Body,
        Ownership: Ownership,
        OwnerId: OwnerId,
        Tags: Tags.AsReadOnly(),
        Variables: Variables.Select(v => v.ToTemplateVariable()).ToList().AsReadOnly(),
        CreatedAt: CreatedAt,
        UpdatedAt: UpdatedAt);

    /// <summary>Builds a Cosmos document from a public API record.</summary>
    public static CosmosPromptDocument FromPromptTemplate(PromptTemplate template) => new()
    {
        Id = template.Id,
        TenantId = template.TenantId,
        OwnerId = template.OwnerId ?? string.Empty,
        Ownership = template.Ownership,
        Name = template.Name,
        Description = template.Description,
        Body = template.Body,
        Tags = [.. template.Tags],
        Variables = template.Variables.Select(CosmosTemplateVariable.FromTemplateVariable).ToList(),
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt
    };
}

/// <summary>Cosmos-serialisable representation of <see cref="TemplateVariable"/>.</summary>
internal sealed class CosmosTemplateVariable
{
    public string Name { get; set; } = string.Empty;
    public TemplateVariableType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; } = true;

    public TemplateVariable ToTemplateVariable() => new(Name, Type, Description, Required);

    public static CosmosTemplateVariable FromTemplateVariable(TemplateVariable v) => new()
    {
        Name = v.Name,
        Type = v.Type,
        Description = v.Description,
        Required = v.Required
    };
}
