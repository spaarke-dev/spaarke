// -----------------------------------------------------------------------------
// ISolutionVerifier.cs
//
// L2 abstraction over the post-import check. Called by H6 AFTER
// <see cref="ISolutionImporter"/> reports Success: independently confirms the
// environment holds SpaarkeMaster of the run's package type (T218b — present
// AND ismanaged as requested) and returns the record H6 writes to
// interStepState.ImportedSolutions.
//
// WHY A SEPARATE VERIFIER (not folded into ISolutionImporter): "importer
// reported success but the environment disagrees" (out-of-band mutation, an
// import that silently did nothing) is a distinct operator diagnosis from an
// import failure, and it is classified QuarantineRequired.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Verifies the target Dataverse env holds SpaarkeMaster of the run's package type after
/// <see cref="ISolutionImporter"/> reports Success. Returns the installed version + solutionId used by H6 to build
/// the Cosmos interStepState.ImportedSolutions manifest.
/// </summary>
public interface ISolutionVerifier
{
    /// <summary>
    /// Reads the target env's SpaarkeMaster solution row and checks presence and type. Returns a typed
    /// <see cref="SolutionVerificationOutcome"/> — AllPresent carries the installed record, Missing names what is
    /// absent or of the wrong type. Domain failures do NOT throw; infrastructure faults MAY throw.
    /// </summary>
    /// <param name="request">Verification inputs (target env URL + package type + auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SolutionVerificationOutcome> VerifyAsync(
        SolutionVerificationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single verifier invocation. The verifier signs in as the same identity the importer used.
/// </summary>
/// <param name="TargetDataverseUrl">Target env URL — must match the URL the importer just imported to.</param>
/// <param name="TenantId">Entra tenant id (§4D I1).</param>
/// <param name="ClientId">BFF Entra app registration id.</param>
/// <param name="Managed">T218b — <c>true</c> when the run imported the managed package; the installed SpaarkeMaster's
/// <c>ismanaged</c> must match.</param>
/// <param name="ClientSecret">
/// Resolved BFF app-reg client secret. NEVER logged. The stateless Web API verifier acquires its OWN bearer token
/// and therefore needs this value independently. A44.5 (task 205i):
/// MAY be <c>null</c>/empty on secret-free environments — the verifier then
/// resolves its credential from the FR-39 ordered chain
/// (<see cref="Credentials.WorkerDataverseCredentialFactory"/>, MI-FIC
/// first). Empty is the SIGNAL (auth-v4 §9.1); never pass a sentinel value.
/// </param>
public sealed record SolutionVerificationRequest(
    string TargetDataverseUrl,
    string TenantId,
    string ClientId,
    bool Managed,
    string? ClientSecret);

/// <summary>
/// Discriminated result of <see cref="ISolutionVerifier.VerifyAsync"/>.
/// Exhaustive: <see cref="AllPresent"/> | <see cref="Missing"/>.
/// </summary>
public abstract record SolutionVerificationOutcome
{
    private SolutionVerificationOutcome() { }

    /// <summary>SpaarkeMaster is installed with the requested type. <paramref name="ImportedRecords"/> carries its version + solutionId + type — written into Cosmos interStepState.</summary>
    /// <param name="ImportedRecords">The SpaarkeMaster record (one entry).</param>
    public sealed record AllPresent(ImmutableArray<ImportedSolutionRecord> ImportedRecords) : SolutionVerificationOutcome;

    /// <summary>SpaarkeMaster is absent, or installed with the other type. <paramref name="Diagnostic"/> says which.</summary>
    /// <param name="MissingUniqueNames">The unique names not found as requested (SpaarkeMaster).</param>
    /// <param name="Diagnostic">Operator-facing message including the response tail.</param>
    public sealed record Missing(
        ImmutableArray<string> MissingUniqueNames,
        string Diagnostic) : SolutionVerificationOutcome;
}
