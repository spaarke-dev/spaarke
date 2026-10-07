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
        Dictionary<string, AutoFileTenantOverride>? tenants = null,
        bool rung2And3AutoFileEnabled = false,
        List<string>? coreWritableEntities = null)
    {
        var options = new AutoFileOptions
        {
            Enabled = enabled,
            Threshold = threshold,
            Rung2And3AutoFileEnabled = rung2And3AutoFileEnabled,
            Tenants = tenants ?? new Dictionary<string, AutoFileTenantOverride>(StringComparer.OrdinalIgnoreCase),
        };
        if (coreWritableEntities is not null)
            options.CoreWritableEntities = coreWritableEntities;
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        return new AutoFileGate(monitor);
    }

    public static AssociationStatusMapper Mapper(
        bool enabled = true, double threshold = 0.85, bool rung2And3AutoFileEnabled = false,
        List<string>? coreWritableEntities = null) =>
        new(Gate(enabled, threshold, rung2And3AutoFileEnabled: rung2And3AutoFileEnabled, coreWritableEntities: coreWritableEntities),
            NullLogger<AssociationStatusMapper>.Instance);
}
