// -----------------------------------------------------------------------------
// DispatcherOptionsBudgetTests.cs
//
// T186 review: MaxHandlerDuration is both the Service Bus lock-renewal ceiling and
// the Redis dispatch-lock TTL. The longest handler, H6, can wait out Entra FIC
// propagation and the new app user's Dataverse propagation before its import. If the
// sum outgrows MaxHandlerDuration, the lock expires mid-import and a redelivery can
// start a second import. This pins the defaults together.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class DispatcherOptionsBudgetTests
{
    [Fact]
    public void MaxHandlerDuration_CoversH6sPropagationWaitsPlusItsImport_WithMargin()
    {
        var dispatcher = new DispatcherOptions();
        var import = new SolutionImportOptions();

        var h6WorstCase = import.Credentials.EntraPropagationTimeout
                          + import.Credentials.DataverseAppUserPropagationTimeout
                          + import.ImportTimeout;

        dispatcher.MaxHandlerDuration.Should().BeGreaterThanOrEqualTo(h6WorstCase + TimeSpan.FromMinutes(5),
            "the lock must outlive H6's longest legitimate run, or a redelivery starts a second import");
    }
}
