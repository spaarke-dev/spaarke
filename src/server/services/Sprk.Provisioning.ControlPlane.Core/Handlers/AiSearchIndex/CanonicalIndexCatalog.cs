// -----------------------------------------------------------------------------
// CanonicalIndexCatalog.cs
//
// Production <see cref="ICanonicalIndexCatalog"/> implementation — hardcoded
// constant table sourced from:
//
//   - scripts/ai-search/Deploy-AllIndexes.ps1 $Catalog (task 002 audit § 1)
//     for the CANONICAL 7.
//   - projects/customer-provisioning-orchestration-r1/spec.md SC #10 +
//     projects/customer-provisioning-orchestration-r1/notes/
//     ai-search-catalog-audit-2026-08.md § 2 for the RETIRED lineage
//     (spaarke-playbook-embeddings + spaarke-knowledge-index-v1/v2/shared).
//
// Kept as a constant literal (rather than reading the PS script $Catalog block
// at runtime) so a template drift produces a compile-time visible diff on this
// file — the paired PR against Deploy-AllIndexes.ps1 MUST update this list too
// or the H2b test suite fails. This is a deliberate two-place edit that acts
// as a forcing function to keep documentation + code + script consistent.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <inheritdoc/>
public sealed class CanonicalIndexCatalog : ICanonicalIndexCatalog
{
    /// <inheritdoc/>
    public ImmutableArray<string> CanonicalIndexNames { get; }
        = ImmutableArray.Create(
            "spaarke-files-index",
            "spaarke-discovery-index",
            "spaarke-records-index",
            "spaarke-rag-references",
            "spaarke-insights-index",
            "spaarke-session-files",
            "spaarke-invoices-index");

    /// <inheritdoc/>
    public ImmutableHashSet<string> RetiredIndexNames { get; }
        = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            // ADR-039 dispatcher-stack retirement + spaarke-ai-architecture-
            // redesign-r1 task 035 / FR-P2-06.
            "spaarke-playbook-embeddings",
            // Also the historical unprefixed variant per audit § 2.1 —
            // AI-SEARCH-INDEX-CATALOG.md §1 forbids unprefixed defect names.
            "playbook-embeddings",
            // spec.md FR-05 SC #10 + audit § 2.2 — full retired lineage per
            // AI-SEARCH-INDEX-CATALOG.md §5.
            "spaarke-knowledge-index",
            "spaarke-knowledge-index-v2",
            "spaarke-knowledge-shared",
            "knowledge-index",
            // Historical unprefixed variant per audit § 4 (catalog §1 rule).
            "discovery-index");

    /// <inheritdoc/>
    public bool IsRetired(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return false;
        }
        return RetiredIndexNames.Contains(indexName.Trim());
    }
}
