// -----------------------------------------------------------------------------
// ImportedSolutionRecord.cs
//
// POCO record written to ProvisioningRun.InterStepState.ImportedSolutions by
// H6 after a successful import + verification — since T218b one entry, the
// SpaarkeMaster package with its installed version, id and type. H13 turns it
// into the registry value sprk_solutionversion (ImportedSolutionSet).
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// One entry in the H6-authored solution manifest persisted to
/// <see cref="Models.InterStepState.ImportedSolutions"/>.
/// </summary>
/// <param name="SolutionUniqueName">Dataverse solution unique-name (<c>SpaarkeMaster</c>).</param>
/// <param name="Version">
/// Installed solution version read back after the import (e.g. <c>1.2.3.4</c>). Empty string means the verifier
/// could not read a version but the solution IS present.
/// </param>
/// <param name="SolutionId">
/// Dataverse-assigned solution GUID (as string, ADR-044 canonicalized).
/// Empty string when the response omitted it.
/// </param>
/// <param name="IsManaged">T218b — the installed solution's <c>ismanaged</c> (equals the run's package type).
/// <c>false</c> on records written before T218b, which carried a dependency tier instead.</param>
public sealed record ImportedSolutionRecord(
    [property: JsonPropertyName("solutionUniqueName")] string SolutionUniqueName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("solutionId")] string SolutionId,
    [property: JsonPropertyName("isManaged")] bool IsManaged);
