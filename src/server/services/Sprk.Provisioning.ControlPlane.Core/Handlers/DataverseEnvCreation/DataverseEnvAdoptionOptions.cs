// -----------------------------------------------------------------------------
// DataverseEnvAdoptionOptions.cs — H5's WhoAmI polling (T228; bound from the
// `DataverseEnvAdoptionOptions` configuration section). The creation settings
// (pac path, creation timeout, BAP poll interval) went with environment creation.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>Options for <see cref="H5DataverseEnvAdoptionHandler"/> and <see cref="DataverseWebApiHealthProbe"/>.</summary>
public sealed class DataverseEnvAdoptionOptions
{
    /// <summary>Interval between WhoAmI probes while the environment answers "not ready yet". Default 15 s.</summary>
    public TimeSpan HealthProbeInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Total time H5 waits for a terminal WhoAmI answer. Default 10 minutes — an environment the operator created is
    /// normally ready at once; one still being prepared gets this long before H5 fails Resumable.
    /// </summary>
    public TimeSpan HealthProbeTotalTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Per-request timeout of one <c>GET /WhoAmI</c>. Default 30 s.</summary>
    public TimeSpan HealthProbeRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
