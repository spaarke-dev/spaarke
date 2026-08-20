// -----------------------------------------------------------------------------
// ISolutionVerifier.cs
//
// L2 abstraction over the post-import solution verification. Called by H6
// AFTER <see cref="ISolutionImporter"/> reports Success to build the Cosmos
// interStepState.ImportedSolutions manifest (POML acceptance criterion 6)
// AND independently confirm the 8 authoritative solutions are installed.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="PacCliSolutionVerifier"/> — shells out to
//       `pac solution list --environment {envUrl}` and parses the tabular
//       output for the 8 solution unique-names + installed versions +
//       solutionIds.
//     - Test: stubs injected per unit test that return canned catalogs.
//   Interface earns its keep — no NIH.
//
// WHY A SEPARATE VERIFIER (not folded into ISolutionImporter):
//   - The PS script's own Test-TierImport verifies presence, but returns
//     nothing about version or solutionId to C#. The verifier reads
//     `pac solution list` from C# to extract the actual version + solutionId
//     values needed for the interStepState manifest.
//   - Splits fail-modes: script-side per-tier gate failure vs verifier-side
//     "script reported success but list disagrees" (bug or out-of-band
//     mutation) are distinct operator diagnoses.
//   - Same trade-off as H5's IDataverseEnvCreator + IDataverseHealthProbe
//     split (parity per task 048 pattern).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Verifies the 8 authoritative solutions are installed on a target Dataverse
/// env after <see cref="ISolutionImporter"/> reports Success. Returns per-
/// solution installed version + solutionId used by H6 to build the Cosmos
/// interStepState.ImportedSolutions manifest.
/// </summary>
public interface ISolutionVerifier
{
    /// <summary>
    /// Reads the target env's installed solutions (via
    /// <c>pac solution list --environment {envUrl}</c> in the production
    /// impl) and cross-references against the expected catalog. Returns a
    /// typed <see cref="SolutionVerificationOutcome"/> — AllPresent carries
    /// the per-entry version + solutionId map, Missing carries the list of
    /// solutions the verifier could not find. Domain failures do NOT throw;
    /// infrastructure faults (pac binary missing, auth chain broken) MAY throw.
    /// </summary>
    /// <param name="request">Verification inputs (target env URL + expected catalog + auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SolutionVerificationOutcome> VerifyAsync(
        SolutionVerificationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single verifier invocation. Auth is shared with the importer
/// so the verifier reuses the same PAC auth profile the importer just
/// established.
/// </summary>
/// <param name="TargetDataverseUrl">Target env URL — must match the URL the importer just imported to.</param>
/// <param name="TenantId">Entra tenant id (§4D I1).</param>
/// <param name="ClientId">BFF Entra app registration id.</param>
/// <param name="ExpectedCatalog">The 8 authoritative solutions the verifier must confirm are installed.</param>
/// <param name="ClientSecret">
/// Resolved BFF app-reg client secret. NEVER logged. Added by task 141 (Wave
/// G-4) for <see cref="DataverseWebApiSolutionVerifier"/> — unlike the retired
/// PacCliSolutionVerifier (which reused the importer's already-created pac
/// auth profile), the stateless Web API verifier acquires its OWN bearer
/// token via <c>Azure.Identity.ClientSecretCredential</c> and therefore needs
/// this value independently.
/// </param>
public sealed record SolutionVerificationRequest(
    string TargetDataverseUrl,
    string TenantId,
    string ClientId,
    ImmutableArray<CanonicalSolutionEntry> ExpectedCatalog,
    string ClientSecret);

/// <summary>
/// Discriminated result of <see cref="ISolutionVerifier.VerifyAsync"/>.
/// Exhaustive: <see cref="AllPresent"/> | <see cref="Missing"/>.
/// </summary>
public abstract record SolutionVerificationOutcome
{
    private SolutionVerificationOutcome() { }

    /// <summary>All 8 expected solutions were found. <paramref name="ImportedRecords"/> carries per-entry version + solutionId — written into Cosmos interStepState.</summary>
    /// <param name="ImportedRecords">One record per expected catalog entry in the same order.</param>
    public sealed record AllPresent(ImmutableArray<ImportedSolutionRecord> ImportedRecords) : SolutionVerificationOutcome;

    /// <summary>At least one expected solution was NOT found. <paramref name="MissingUniqueNames"/> lists the absent solution unique-names + <paramref name="Diagnostic"/> carries operator context.</summary>
    /// <param name="MissingUniqueNames">Solution unique-names that were NOT found in the pac solution list output.</param>
    /// <param name="Diagnostic">Operator-facing message including the raw pac output tail.</param>
    public sealed record Missing(
        ImmutableArray<string> MissingUniqueNames,
        string Diagnostic) : SolutionVerificationOutcome;
}
