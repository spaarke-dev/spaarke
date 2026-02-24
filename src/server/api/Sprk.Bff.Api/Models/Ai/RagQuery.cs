namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// A structured RAG query built from document analysis metadata.
/// Used by RagQueryBuilder to pass a rich, tenant-scoped query to RagService.
/// </summary>
/// <param name="SearchText">
/// The composite semantic search text built from entities, key phrases, document type,
/// and section titles extracted during analysis.
/// </param>
/// <param name="FilterExpression">
/// OData filter expression for Azure AI Search.
/// Always includes tenantId filter; optionally includes documentType filter.
/// </param>
/// <param name="Top">Maximum number of results to return. Default: 10.</param>
/// <param name="MinRelevanceScore">
/// Minimum relevance score threshold (0.0–1.0).
/// Results below this score are excluded. Default: 0.7.
/// </param>
public record RagQuery(
    string SearchText,
    string FilterExpression,
    int Top = 10,
    double MinRelevanceScore = 0.7);
