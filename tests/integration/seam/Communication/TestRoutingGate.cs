using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Engine;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// Test helper for the FR-E7 (task 057) <see cref="CategoryRoutingGate"/>. Builds a gate over an in-memory
/// <see cref="IOptionsMonitor{TOptions}"/> so seam tests that construct <c>CommunicationEnrichmentService</c>
/// can supply a routing gate. <see cref="Disabled"/> is the no-op default (routing off — the enrichment
/// behaves exactly as before task 057); <see cref="From"/> configures a live category→team map.
/// Visible to every seam test (they live under the <c>Sprk.Bff.Api.Tests</c> namespace root).
/// </summary>
internal static class TestRoutingGate
{
    public static CategoryRoutingGate Disabled() => From(new CategoryRoutingOptions());

    public static CategoryRoutingGate From(CategoryRoutingOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<CategoryRoutingOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new CategoryRoutingGate(monitor.Object);
    }
}
