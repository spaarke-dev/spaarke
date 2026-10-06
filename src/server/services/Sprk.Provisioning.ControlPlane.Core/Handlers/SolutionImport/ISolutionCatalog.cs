// -----------------------------------------------------------------------------
// ISolutionCatalog.cs
//
// L2 abstraction over the canonical list of 9 authoritative Spaarke managed
// solutions H6 imports (spec.md §11.1a + FR-09). The catalog IS a C#-side
// mirror of scripts/Deploy-DataverseSolutions.ps1's $SolutionImportOrder
// hashtable per task 008 R5 binding:
//
//   > "H6 handler MUST source its input list from $SolutionImportOrder,
//     never from src/solutions/ folder enumeration"
//
// Why the mirror is BINDING-safe (not a MUST-NOT-hardcode violation):
//   - The PS script REMAINS the runtime authority — the H6 importer shell-out
//     does NOT pass -SolutionsToImport, so the script's own $SolutionImportOrder
//     drives WHICH solutions get pushed to Dataverse.
//   - The C# catalog exists ONLY for (a) post-import verification (knowing
//     the 8 SolutionName strings to look up in `pac solution list`) and
//     (b) building the Cosmos interStepState.ImportedSolutions manifest.
//   - A unit test in H6SolutionImportHandlerTests parses the PS script's
//     $SolutionImportOrder block and asserts the C# catalog matches — any
//     drift between the two fails CI, so the PS script remains the single
//     source of truth of record.
//   - Any list-editing occurs in the PS script FIRST; catalog + test are
//     updated as the consequence. This is the same discipline H2b uses for
//     ICanonicalIndexCatalog vs Deploy-AllIndexes.ps1 (parity per task 045).
//
// ADR-039 GUARD:
//   The catalog exposes RetiredSolutionUniqueNames — a defense-in-depth list
//   of solution unique-names that MUST NOT appear in the 9 authoritative
//   solutions per ADR-039 amendment 2026-07-05 ("MUST NOT land new capability
//   on the frozen node-graph engine"). If any of the 8 catalog entries
//   overlaps this list, H6 emits RetiredSolutionReintroduction at pre-check
//   time BEFORE the PS script fires — parity with H2b's retired-index
//   pre-check pattern.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="CanonicalSolutionCatalog"/> — hardcodes the
//       8 canonical solutions (mirror of the PS script).
//     - Test: stubs injected per unit test that return curated catalogs
//       (retired-reintroduction test uses a catalog that violates ADR-039).
//   Interface earns its keep — no NIH.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Canonical list of the 9 authoritative Spaarke managed solutions (Tier 1
/// SpaarkeCore, Tier 2 webresources, Tier 3 six feature solutions).
/// C#-side mirror of <c>scripts/Deploy-DataverseSolutions.ps1</c>'s
/// <c>$SolutionImportOrder</c> per task 008 R5 binding.
/// </summary>
public interface ISolutionCatalog
{
    /// <summary>
    /// The 9 authoritative solutions in dependency order (Tier 1 first, Tier
    /// 2 second, Tier 3 members in enumeration order). Consumers MUST NOT
    /// re-order; the tier ordering is dependency-load-bearing per spec.md
    /// §11.1a. Empty is a compile / config error.
    /// </summary>
    ImmutableArray<CanonicalSolutionEntry> Solutions { get; }

    /// <summary>
    /// Solution unique-names whose presence in <see cref="Solutions"/> is a
    /// deployment-time defense-in-depth violation per ADR-039 (single AI
    /// routing surface; retired dispatcher / embeddings surface MUST NOT be
    /// re-introduced). Handler emits
    /// <see cref="SolutionImportRejectionCodes.RetiredSolutionReintroduction"/>
    /// at pre-check time when any catalog entry matches this list.
    /// </summary>
    ImmutableArray<string> RetiredSolutionUniqueNames { get; }

    /// <summary>
    /// Deterministic SHA-256 hash of the ordered catalog contents (each
    /// entry's <c>SolutionName|Tier</c> concatenated + hashed). Used as the
    /// suffix of H6's idempotency key: <c>solimport-{customerId}-{catalogHash}</c>.
    /// Bumping the catalog (adding a solution, changing a Tier) invalidates
    /// existing idempotency records + forces re-import (Package Deployer
    /// handles per-solution version dedup idempotently).
    /// </summary>
    string CatalogHash { get; }
}

/// <summary>
/// Single entry in <see cref="ISolutionCatalog.Solutions"/>.
/// </summary>
/// <param name="FolderName">
/// Solution folder name under <c>src/solutions/</c> (e.g. <c>SpaarkeCore</c>,
/// <c>webresources</c>). Matches the key in the PS script's
/// <c>$SolutionImportOrder</c> hashtable.
/// </param>
/// <param name="SolutionUniqueName">
/// Dataverse solution unique-name (e.g. <c>SpaarkeCore</c>,
/// <c>SpaarkeWebResources</c>, <c>CalendarSidePane</c>). Used by the verifier
/// to look the solution up in <c>pac solution list</c> output. Note:
/// FolderName ≠ SolutionUniqueName for <c>webresources</c> →
/// <c>SpaarkeWebResources</c> (matches PS script mapping).
/// </param>
/// <param name="DisplayName">Human-readable display name for logs + operator diagnostics.</param>
/// <param name="Tier">
/// Dependency tier: 1 (SpaarkeCore — base), 2 (webresources — depends on
/// Tier 1), 3 (six feature solutions — depend on Tier 1 + 2). Enforced by
/// the PS script's per-tier gate.
/// </param>
public sealed record CanonicalSolutionEntry(
    string FolderName,
    string SolutionUniqueName,
    string DisplayName,
    int Tier);
