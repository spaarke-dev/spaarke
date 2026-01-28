using Sprk.Bff.Api.Services.Ai.Prompts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Fallback scope catalog when Dataverse scopes are unavailable or empty.
/// Based on KNW-BUILDER-001-ScopeCatalog design artifact.
/// </summary>
/// <remarks>
/// This catalog provides the AI with awareness of available scope types
/// even when Dataverse has not been populated with scope data.
/// Enables queries like "what skills are available?" to be answered.
/// </remarks>
public static class FallbackScopeCatalog
{
    /// <summary>
    /// Get fallback action entries when Dataverse returns empty.
    /// </summary>
    public static IReadOnlyList<ScopeCatalogEntry> GetActions() =>
    [
        new ScopeCatalogEntry
        {
            Name = "SYS-ACT-001",
            DisplayName = "Entity Extraction",
            Description = "Extract named entities (parties, dates, amounts) from document text. Works with all document types.",
            ScopeType = "action"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-ACT-002",
            DisplayName = "Document Summary",
            Description = "Generate TL;DR summary of document content with key points. Works with all document types.",
            ScopeType = "action"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-ACT-003",
            DisplayName = "Clause Analysis",
            Description = "Analyze and categorize document clauses. Best for contracts, leases, and agreements.",
            ScopeType = "action"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-ACT-004",
            DisplayName = "Risk Detection",
            Description = "Identify potential risks and issues in documents. Returns risk level and mitigation suggestions.",
            ScopeType = "action"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-ACT-005",
            DisplayName = "Financial Term Extraction",
            Description = "Extract monetary values, payment terms, and financial provisions from contracts and leases.",
            ScopeType = "action"
        }
    ];

    /// <summary>
    /// Get fallback skill entries when Dataverse returns empty.
    /// </summary>
    public static IReadOnlyList<ScopeCatalogEntry> GetSkills() =>
    [
        new ScopeCatalogEntry
        {
            Name = "SYS-SKL-001",
            DisplayName = "Real Estate Domain",
            Description = "Domain expertise for real estate documents including leases, deeds, and easements.",
            ScopeType = "skill"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-SKL-002",
            DisplayName = "Contract Law Basics",
            Description = "Basic contract law principles and terminology for analyzing agreements.",
            ScopeType = "skill"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-SKL-003",
            DisplayName = "Financial Analysis",
            Description = "Financial document analysis expertise for loans, invoices, and financial statements.",
            ScopeType = "skill"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-SKL-004",
            DisplayName = "Insurance Expertise",
            Description = "Insurance document and certificate of insurance (COI) analysis expertise.",
            ScopeType = "skill"
        }
    ];

    /// <summary>
    /// Get fallback knowledge entries when Dataverse returns empty.
    /// </summary>
    public static IReadOnlyList<ScopeCatalogEntry> GetKnowledge() =>
    [
        new ScopeCatalogEntry
        {
            Name = "SYS-KNW-001",
            DisplayName = "Standard Contract Terms",
            Description = "Reference database of standard contract clauses for comparison and analysis.",
            ScopeType = "knowledge"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-KNW-002",
            DisplayName = "Company Policies",
            Description = "Organization-specific policies and guidelines for compliance checking.",
            ScopeType = "knowledge"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-KNW-003",
            DisplayName = "Regulatory Requirements",
            Description = "Industry regulatory compliance requirements and standards.",
            ScopeType = "knowledge"
        }
    ];

    /// <summary>
    /// Get fallback tool entries when Dataverse returns empty.
    /// </summary>
    public static IReadOnlyList<ScopeCatalogEntry> GetTools() =>
    [
        new ScopeCatalogEntry
        {
            Name = "SYS-TL-001",
            DisplayName = "Entity Extractor Handler",
            Description = "Structured entity extraction with validation. Outputs validated entity objects.",
            ScopeType = "tool"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-TL-002",
            DisplayName = "Clause Analyzer Handler",
            Description = "Clause detection and classification with category tagging.",
            ScopeType = "tool"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-TL-003",
            DisplayName = "Document Classifier Handler",
            Description = "Document type classification with confidence scoring.",
            ScopeType = "tool"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-TL-004",
            DisplayName = "Summary Handler",
            Description = "Configurable document summarization with multiple output formats.",
            ScopeType = "tool"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-TL-005",
            DisplayName = "Risk Detector Handler",
            Description = "Risk scoring and categorization with severity levels.",
            ScopeType = "tool"
        },
        new ScopeCatalogEntry
        {
            Name = "SYS-TL-009",
            DisplayName = "Generic Analysis Handler",
            Description = "Highly configurable analysis handler for custom use cases.",
            ScopeType = "tool"
        }
    ];

    /// <summary>
    /// Merge Dataverse results with fallback entries when Dataverse returns empty.
    /// </summary>
    /// <param name="dataverseEntries">Entries loaded from Dataverse.</param>
    /// <param name="fallbackEntries">Fallback entries to use if Dataverse is empty.</param>
    /// <returns>Merged list prioritizing Dataverse entries.</returns>
    public static IReadOnlyList<ScopeCatalogEntry> MergeWithFallback(
        IReadOnlyList<ScopeCatalogEntry> dataverseEntries,
        IReadOnlyList<ScopeCatalogEntry> fallbackEntries)
    {
        // If Dataverse has entries, use them (they are authoritative)
        if (dataverseEntries.Count > 0)
        {
            return dataverseEntries;
        }

        // Otherwise, use fallback
        return fallbackEntries;
    }
}
