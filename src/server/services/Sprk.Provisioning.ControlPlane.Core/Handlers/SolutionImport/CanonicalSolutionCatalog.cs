// -----------------------------------------------------------------------------
// CanonicalSolutionCatalog.cs
//
// Production <see cref="ISolutionCatalog"/> implementation — hardcodes the 9
// authoritative Spaarke managed solutions per spec.md §11.1a + FR-09. IS a
// C#-side mirror of scripts/Deploy-DataverseSolutions.ps1's $SolutionImportOrder
// hashtable per task 008 R5 binding.
//
// SESSION 19 MDA-GAP FIX (customer-provisioning-orchestration-r1, 2026-08-28):
// Added Tier 4 `SpaarkeCorporateCounselApp` (owns the sprk_MatterManagement MDA
// app-module + sitemap + 14 app-module-components per Build-SpaarkeMaster.ps1
// lines 87-89) after owner audit surfaced that H6 was importing entities +
// features but no MDA to sign into on the customer env. Solution unique-name
// verified by Assemble-SpaarkeMasterSolution.ps1 source-solution enumeration.
// Physical .zip publication to blob storage is a separate live-ceremony
// backlog item (documented in SolutionImportOptions.cs LIVE-CEREMONY GAP).
//
// The mapping (FolderName → SolutionUniqueName) MUST match the PS script's
// hashtable value → SolutionName mapping exactly. Any drift is caught by
// the H6SolutionImportHandlerTests.Catalog_MatchesPowerShellScriptSolutionImportOrder
// unit test which parses the PS script's block at test time + asserts
// structural equivalence.
//
// RETIRED-SOLUTION LIST (ADR-039 defense-in-depth):
//   Populated with unique-name patterns that ADR-039 amendment 2026-07-05
//   forbids reintroducing via any solution. Currently the retired surface
//   is the AI Search index (spaarke-playbook-embeddings) + PS orchestration
//   dispatcher — neither is currently packaged as a Dataverse solution, but
//   the guard exists to catch future accidents. The 9 authoritative solutions
//   do NOT overlap this list (unit-tested at CI time).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Hardcoded canonical catalog of the 9 authoritative Spaarke managed
/// solutions per spec.md §11.1a. C#-side mirror of
/// <c>scripts/Deploy-DataverseSolutions.ps1</c>'s <c>$SolutionImportOrder</c>.
/// </summary>
public sealed class CanonicalSolutionCatalog : ISolutionCatalog
{
    /// <summary>
    /// The 9 authoritative solutions in dependency-tier order. This is the
    /// C#-side mirror of the PS script's <c>$SolutionImportOrder</c>. Both
    /// MUST be updated in lockstep (unit test enforces).
    /// </summary>
    private static readonly ImmutableArray<CanonicalSolutionEntry> s_solutions =
        ImmutableArray.Create(
            // Tier 1 — base solution (entities, option sets, security roles)
            new CanonicalSolutionEntry(
                FolderName: "SpaarkeCore",
                SolutionUniqueName: "SpaarkeCore",
                DisplayName: "Spaarke Core",
                Tier: 1),

            // Tier 2 — web resources (JS used by forms + ribbons; depends on Tier 1)
            new CanonicalSolutionEntry(
                FolderName: "webresources",
                SolutionUniqueName: "SpaarkeWebResources",
                DisplayName: "Spaarke Web Resources",
                Tier: 2),

            // Tier 3 — feature solutions (independent of each other; depend on Tier 1+2)
            new CanonicalSolutionEntry(
                FolderName: "CalendarSidePane",
                SolutionUniqueName: "CalendarSidePane",
                DisplayName: "Calendar Side Pane",
                Tier: 3),
            new CanonicalSolutionEntry(
                FolderName: "DocumentUploadWizard",
                SolutionUniqueName: "DocumentUploadWizard",
                DisplayName: "Document Upload Wizard",
                Tier: 3),
            new CanonicalSolutionEntry(
                FolderName: "EventCommands",
                SolutionUniqueName: "EventRibbons",
                DisplayName: "Event Ribbon Commands",
                Tier: 3),
            new CanonicalSolutionEntry(
                FolderName: "EventDetailSidePane",
                SolutionUniqueName: "EventDetailSidePane",
                DisplayName: "Event Detail Side Pane",
                Tier: 3),
            new CanonicalSolutionEntry(
                FolderName: "EventsPage",
                SolutionUniqueName: "EventsPage",
                DisplayName: "Events Page",
                Tier: 3),
            new CanonicalSolutionEntry(
                FolderName: "LegalWorkspace",
                SolutionUniqueName: "LegalWorkspace",
                DisplayName: "Legal Workspace",
                Tier: 3),

            // Tier 4 — MDA app + sitemap + app-module components (SESSION 19 MDA-GAP FIX).
            // Depends on Tier 1 entities + Tier 3 feature forms/ribbons. Ships the
            // sprk_MatterManagement MDA (id 729afe6d-ca73-f011-b4cb-6045bdd8b757) —
            // without this, customer envs receive all entities but no MDA to sign into.
            new CanonicalSolutionEntry(
                FolderName: "SpaarkeCorporateCounselApp",
                SolutionUniqueName: "SpaarkeCorporateCounselApp",
                DisplayName: "Spaarke Corporate Counsel App (Matter Management MDA)",
                Tier: 4));

    /// <summary>
    /// Retired-artifact unique-name substrings whose presence in any catalog
    /// entry triggers <see cref="SolutionImportRejectionCodes.RetiredSolutionReintroduction"/>.
    /// Defense-in-depth against future accidental reintroduction of
    /// dispatcher / embeddings-index surfaces per ADR-039 amendment
    /// 2026-07-05.
    /// </summary>
    private static readonly ImmutableArray<string> s_retired =
        ImmutableArray.Create(
            "spaarke-playbook-embeddings",
            "PlaybookEmbeddings",
            "SpaarkePlaybookEmbeddings",
            "SprkDispatcher",
            "PlaybookDispatcher");

    /// <inheritdoc/>
    public ImmutableArray<CanonicalSolutionEntry> Solutions => s_solutions;

    /// <inheritdoc/>
    public ImmutableArray<string> RetiredSolutionUniqueNames => s_retired;

    /// <inheritdoc/>
    public string CatalogHash { get; } = ComputeCatalogHash(s_solutions);

    /// <summary>
    /// Computes the deterministic SHA-256 hex hash of the ordered catalog:
    /// concatenates <c>{SolutionName}|{Tier}</c> tuples with a semicolon
    /// separator + lowercases. Exposed <c>internal</c> so unit tests can
    /// verify determinism + non-empty.
    /// </summary>
    internal static string ComputeCatalogHash(ImmutableArray<CanonicalSolutionEntry> solutions)
    {
        var payload = string.Join(";", solutions.Select(s => $"{s.SolutionUniqueName}|{s.Tier}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
