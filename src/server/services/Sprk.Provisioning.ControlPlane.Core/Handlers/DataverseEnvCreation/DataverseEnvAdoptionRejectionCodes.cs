// -----------------------------------------------------------------------------
// DataverseEnvAdoptionRejectionCodes.cs — machine-stable H5 rejection codes (T228: H5 adopts the operator's
// environment; the creation codes went with environment creation). Every code is Resumable — H5 creates nothing.
// Failure gates are `h5-{code}`.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>Rejection codes for <see cref="H5DataverseEnvAdoptionHandler"/>.</summary>
public static class DataverseEnvAdoptionRejectionCodes
{
    /// <summary>The run's <c>tenantId</c> is missing (§4D I1).</summary>
    public const string MissingTenantId = "missing-tenant-id";

    /// <summary>The ProvisioningRun was not found in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// <c>dataverseEnvUrl</c> is missing, malformed or not named for this customer
    /// (<see cref="Sprk.Provisioning.ControlPlane.Core.Models.DataverseEnvironmentUrlRule"/>). Nothing was written.
    /// </summary>
    public const string EnvUrlInvalid = "env-url-invalid";

    /// <summary>
    /// WhoAmI answered 401/403 for the L2 Worker identity: it is not an application user of the environment
    /// (prereqs.yaml PRQ-C-09). The operator adds it with the System Administrator role and resumes.
    /// </summary>
    public const string WorkerNotAppUser = "worker-not-app-user";

    /// <summary>WhoAmI failed for another reason, or did not answer within the polling window.</summary>
    public const string EnvHealthCheckFailed = "env-health-check-failed";

    /// <summary>The success write lost an optimistic-concurrency race; resume short-circuits on idempotency.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>The run was deleted while H5 was in flight.</summary>
    public const string RunDeletedDuringAdoption = "run-deleted-during-adoption";
}
