// -----------------------------------------------------------------------------
// IDataverseAppUserVerifier.cs
//
// T2 SILENT-FAIL TRAP OWNER (spec.md FR-33 + design.md §4B): independently
// re-queries `systemusers?$filter=applicationid eq {applicationId}` AFTER
// IDataverseAppUserCreator reports success, so a silent registration failure
// (creator believes it succeeded, but Dataverse never actually persisted the
// row — e.g. a write that returned 200 against a stale/unreplicated env) is
// caught as a DISTINCT post-condition rather than trusted implicitly. Parity
// with H3's IAdminConsentVerifier being independent of IEntraAppRegProvisioner,
// and H4's IArmKeyVaultRefProbe being independent of the KV-writer PATCH.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Verifies the T2 post-condition: exactly one <c>systemusers</c> row exists
/// for the given <c>applicationId</c>.
/// </summary>
public interface IDataverseAppUserVerifier
{
    /// <summary>
    /// Queries <c>systemusers?$filter=applicationid eq {applicationId}</c> and
    /// returns the observed row count + (if exactly one) its systemuserid.
    /// </summary>
    Task<DataverseAppUserVerificationResult> VerifyAsync(
        string environmentUrl,
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IDataverseAppUserVerifier.VerifyAsync"/>
/// invocation. Exhaustive: <see cref="Verified"/> | <see cref="CountMismatch"/>.
/// </summary>
public abstract record DataverseAppUserVerificationResult
{
    private DataverseAppUserVerificationResult() { }

    /// <summary>Exactly one App User row found — T2 cleared.</summary>
    public sealed record Verified(string SystemUserId) : DataverseAppUserVerificationResult;

    /// <summary>
    /// Observed count != 1 (zero — registration silently did not persist; or
    /// more than one — duplicate registration, also a data-integrity concern).
    /// </summary>
    public sealed record CountMismatch(int ObservedCount) : DataverseAppUserVerificationResult;
}
