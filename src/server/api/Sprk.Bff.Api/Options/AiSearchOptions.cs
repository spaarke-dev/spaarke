namespace Sprk.Bff.Api.Configuration;

public class AiSearchOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKeySecretName { get; init; } = string.Empty;
    public string KnowledgeIndexName { get; init; } = "knowledge-index";
    public string DiscoveryIndexName { get; init; } = "discovery-index";
    public string SemanticConfigName { get; init; } = "semantic-config";
}
