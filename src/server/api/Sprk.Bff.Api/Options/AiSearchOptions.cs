namespace Sprk.Bff.Api.Configuration;

public class AiSearchOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKeySecretName { get; init; } = string.Empty;
    public string KnowledgeIndexName { get; init; } = "spaarke-knowledge-index-v2";
    public string DiscoveryIndexName { get; init; } = "discovery-index";
    public string RagReferencesIndexName { get; init; } = "spaarke-rag-references";
    public string SemanticConfigName { get; init; } = "semantic-config";
}
