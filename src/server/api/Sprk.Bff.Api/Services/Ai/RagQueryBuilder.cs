using AiModels = Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Builds metadata-aware RAG queries from DocumentAnalysisResult.
/// Replaces the naive first-500-characters approach with structured queries that
/// extract entities, key phrases, document type, and section titles from analysis metadata.
/// </summary>
/// <remarks>
/// Query construction strategy:
/// 1. Entities (top 5 by relevance): organizations, people, references — highest signal
/// 2. Key phrases (top 10 from Keywords field): domain-specific vocabulary
/// 3. Document type: anchors query in the document's semantic domain
/// 4. Summary prefix: provides semantic context for vector search
///
/// Filter construction:
/// - Always includes tenantId (ADR-014: tenant isolation required)
/// - Includes documentType filter when strongly typed (not "other")
///
/// Registered as: builder.Services.AddSingleton&lt;RagQueryBuilder&gt;() per ADR-010.
/// </remarks>
public class RagQueryBuilder
{
    private const int MaxEntities = 5;
    private const int MaxKeyPhrases = 10;
    private const string FallbackSearchText = "document analysis knowledge retrieval";

    // Document types considered "strongly typed" — warrant an index-level filter
    private static readonly HashSet<string> StronglyTypedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "contract", "agreement", "nda", "lease", "invoice", "proposal",
        "report", "letter", "memo", "sla", "statement", "policy"
    };

    /// <summary>
    /// Builds a metadata-aware <see cref="AiModels.RagQuery"/> from a document analysis result.
    /// </summary>
    /// <param name="result">
    /// The <see cref="AiModels.DocumentAnalysisResult"/> produced by the AI analysis pipeline.
    /// Used to extract entities, key phrases, document type, and summary.
    /// </param>
    /// <param name="tenantId">
    /// The tenant identifier. Included in the filter expression for index-level isolation
    /// per ADR-014. Must not be null or empty.
    /// </param>
    /// <returns>
    /// A <see cref="AiModels.RagQuery"/> with a composite search text, tenant-scoped filter,
    /// and default result limits. Returns a safe fallback query if metadata is empty.
    /// </returns>
    public AiModels.RagQuery BuildQuery(AiModels.DocumentAnalysisResult result, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var searchText = BuildSearchText(result);
        var filterExpression = BuildFilterExpression(result, tenantId);

        return new AiModels.RagQuery(
            SearchText: searchText,
            FilterExpression: filterExpression);
    }

    // === Private Helpers ===

    /// <summary>
    /// Constructs the composite search text from analysis metadata.
    /// Priority order: entities → key phrases → document type → summary prefix.
    /// </summary>
    private static string BuildSearchText(AiModels.DocumentAnalysisResult result)
    {
        var parts = new List<string>();

        // 1. Extract top entities (highest retrieval signal — named entities match indexed content directly)
        var entityTerms = ExtractEntityTerms(result.Entities);
        if (entityTerms.Count > 0)
        {
            parts.Add(string.Join(" ", entityTerms.Take(MaxEntities)));
        }

        // 2. Extract key phrases from the Keywords field (comma-separated string)
        var keyPhrases = ExtractKeyPhrases(result.Keywords);
        if (keyPhrases.Count > 0)
        {
            parts.Add(string.Join(" ", keyPhrases.Take(MaxKeyPhrases)));
        }

        // 3. Include document type as a semantic anchor term
        if (!string.IsNullOrWhiteSpace(result.Entities.DocumentType) &&
            !result.Entities.DocumentType.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(result.Entities.DocumentType);
        }

        // 4. Prepend a summary fragment for semantic vector context (first 200 chars)
        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            var summaryFragment = result.Summary.Length > 200
                ? result.Summary[..200]
                : result.Summary;
            // Insert summary at beginning for vector search relevance
            parts.Insert(0, summaryFragment);
        }

        if (parts.Count == 0)
        {
            return FallbackSearchText;
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Builds an OData filter expression that always scopes by tenant,
    /// and optionally adds a document type filter for strongly typed documents.
    /// </summary>
    private static string BuildFilterExpression(AiModels.DocumentAnalysisResult result, string tenantId)
    {
        var filters = new List<string>
        {
            // ADR-014: Tenant isolation is mandatory on all index queries
            $"tenantId eq '{EscapeODataValue(tenantId)}'"
        };

        // Add document type filter for strongly typed documents only.
        // "other" is the catch-all — filtering on it would unnecessarily restrict results.
        var documentType = result.Entities.DocumentType;
        if (!string.IsNullOrWhiteSpace(documentType) &&
            StronglyTypedDocumentTypes.Contains(documentType))
        {
            filters.Add($"documentType eq '{EscapeODataValue(documentType)}'");
        }

        return string.Join(" and ", filters);
    }

    /// <summary>
    /// Extracts prioritized entity terms from the <see cref="AiModels.ExtractedEntities"/> structure.
    /// Order: Organizations → People → References → Amounts → Dates.
    /// Organizations and references are highest signal for legal/business document retrieval.
    /// </summary>
    private static List<string> ExtractEntityTerms(AiModels.ExtractedEntities entities)
    {
        var terms = new List<string>();

        // Organizations first — strongest retrieval signal for contract/legal documents
        foreach (var org in entities.Organizations)
        {
            if (!string.IsNullOrWhiteSpace(org))
                terms.Add(org.Trim());
        }

        // People — parties to agreements, signatories
        foreach (var person in entities.People)
        {
            if (!string.IsNullOrWhiteSpace(person))
                terms.Add(person.Trim());
        }

        // References — contract numbers, matter IDs, invoice numbers
        foreach (var reference in entities.References)
        {
            if (!string.IsNullOrWhiteSpace(reference))
                terms.Add(reference.Trim());
        }

        // Amounts — relevant for invoice/financial document retrieval
        foreach (var amount in entities.Amounts)
        {
            if (!string.IsNullOrWhiteSpace(amount))
                terms.Add(amount.Trim());
        }

        return terms;
    }

    /// <summary>
    /// Parses the comma-separated Keywords string into individual phrases.
    /// Trims whitespace and removes empty entries.
    /// </summary>
    private static List<string> ExtractKeyPhrases(string keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            return [];

        return keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
    }

    /// <summary>
    /// Escapes single quotes in OData filter values to prevent filter injection.
    /// </summary>
    private static string EscapeODataValue(string value) => value.Replace("'", "''");
}
