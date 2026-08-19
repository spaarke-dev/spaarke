// -----------------------------------------------------------------------------
// ImportedSolutionRecord.cs
//
// POCO record written to ProvisioningRun.InterStepState.ImportedSolutions by
// H6 (task 049) after a successful solution import + verification. Satisfies
// POML acceptance criterion 6:
//
//   > "Cosmos interStepState contains manifest of 8 imported solutions with
//     name + version + solutionId"
//
// Consumed by H7 (env-var values — task 050) which needs the solution ids
// to bind option-set + config lookups, and by operator UI for solution-level
// status views.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// One entry in the H6-authored solution manifest persisted to
/// <see cref="Models.InterStepState.ImportedSolutions"/>.
/// </summary>
/// <param name="SolutionUniqueName">Dataverse solution unique-name (e.g. <c>SpaarkeCore</c>).</param>
/// <param name="Version">
/// Installed solution version as reported by <c>pac solution list</c>
/// post-import (e.g. <c>1.2.3.4</c>). Empty string means the verifier could
/// not parse a version but the solution IS present — treated as a soft
/// diagnostic; the handler still emits Success in this case since the PS
/// script's own per-tier gate confirmed the import.
/// </param>
/// <param name="SolutionId">
/// Dataverse-assigned solution GUID (as string, ADR-044 canonicalized).
/// Empty string when the verifier could not extract the id from
/// <c>pac solution list</c> output (some PAC CLI versions omit it from the
/// default output). Downstream H7 tolerates empty by falling back to a
/// unique-name lookup.
/// </param>
/// <param name="Tier">Dependency tier (1, 2, or 3) as declared in the catalog.</param>
public sealed record ImportedSolutionRecord(
    [property: JsonPropertyName("solutionUniqueName")] string SolutionUniqueName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("solutionId")] string SolutionId,
    [property: JsonPropertyName("tier")] int Tier);
