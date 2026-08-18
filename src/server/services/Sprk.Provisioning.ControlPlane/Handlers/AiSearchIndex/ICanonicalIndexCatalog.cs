// -----------------------------------------------------------------------------
// ICanonicalIndexCatalog.cs
//
// The AUTHORITATIVE 7-name canonical catalog + the retired-lineage rejection
// list. This seam exists to keep the retired-name guard testable without
// hard-coding string literals inside H2bAiSearchIndexHandler — tests inject a
// stub catalog to exercise the retired-index-in-requested-list path
// deterministically without depending on ambient production data.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-05: 7
//     canonical indexes per §11.2 catalog.
//   - projects/customer-provisioning-orchestration-r1/spec.md SC #10 +
//     projects/customer-provisioning-orchestration-r1/notes/
//     ai-search-catalog-audit-2026-08.md § 1 + § 2: retired lineage
//     enumerated verbatim from ADR-039 + AI-SEARCH-INDEX-CATALOG.md §5.
//   - scripts/ai-search/Deploy-AllIndexes.ps1 $Catalog: source-of-truth for
//     the canonical 7 names (task 002's audit confirmed this as the FR-05 /
//     FR-07 authority).
//   - ADR-039 (task 035 / FR-P2-06): `spaarke-playbook-embeddings` retired
//     with the dispatcher stack.
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11 extension test):
//   The retired-name guard is a design-intent violation check (structural,
//   not runtime). A stubbable seam lets the H2b tests inject a catalog whose
//   requested list contains a retired name, exercising the QuarantineRequired
//   path without a network call. Production impl (CanonicalIndexCatalog) is
//   a static constant table — trivially small but stable + auditable.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Authoritative catalog for the 7 canonical Spaarke AI Search indexes +
/// the retired/archived index-name lineage that H2b MUST NOT re-provision.
/// </summary>
public interface ICanonicalIndexCatalog
{
    /// <summary>
    /// The 7 canonical index names per <c>scripts/ai-search/Deploy-AllIndexes.ps1</c>
    /// <c>$Catalog</c> (task 002 audit § 1). Ordered by catalog row.
    /// </summary>
    ImmutableArray<string> CanonicalIndexNames { get; }

    /// <summary>
    /// The FULL retired / archived index-name lineage that H2b MUST refuse to
    /// provision. Includes: <c>spaarke-playbook-embeddings</c> (ADR-039 /
    /// FR-P2-06), <c>spaarke-knowledge-index</c> lineage (v1, v2, shared,
    /// unprefixed) per spec SC #10 + audit § 2.2 + § 2.3.
    /// </summary>
    ImmutableHashSet<string> RetiredIndexNames { get; }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="indexName"/> is in the retired
    /// lineage (case-insensitive per AI Search index naming rules — all
    /// lowercase). Returns <c>false</c> otherwise (including for the canonical 7).
    /// </summary>
    bool IsRetired(string indexName);
}
