using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Engine;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Shared factory for the task-015 auto-file gate + status mapper. Keeps the four
/// <see cref="IncomingAssociationResolver"/> construction sites (and the mapper unit tests) on one
/// source of truth for the default gate wiring.
/// </summary>
internal static class AssociationTestSupport
{
    public static AutoFileGate Gate(
        bool enabled = true,
        double threshold = 0.85,
        Dictionary<string, AutoFileTenantOverride>? tenants = null)
    {
        var options = new AutoFileOptions
        {
            Enabled = enabled,
            Threshold = threshold,
            Tenants = tenants ?? new Dictionary<string, AutoFileTenantOverride>(StringComparer.OrdinalIgnoreCase),
        };
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        return new AutoFileGate(monitor);
    }

    public static AssociationStatusMapper Mapper(bool enabled = true, double threshold = 0.85) =>
        new(Gate(enabled, threshold), NullLogger<AssociationStatusMapper>.Instance);
}
