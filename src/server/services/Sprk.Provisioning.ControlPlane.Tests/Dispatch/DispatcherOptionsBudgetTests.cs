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
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
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

    [Fact]
    public void MaxHandlerDuration_LeavesH13HalfItsLockAfterTheKeylessProofRetry()
    {
        // ISS-034: H13 repeats the keyless proof for up to KeylessProofRetryWindow, then runs its trap, invariant, cost,
        // census and mailbox checks. The retry and its last attempt (two calls: the inner transient retry) may use at most
        // half of the lock.
        var dispatcher = new DispatcherOptions();
        var h13 = new H13AcceptanceOptions();

        (h13.KeylessProofRetryWindow + (2 * h13.KeylessProofTimeout)).Should().BeLessThanOrEqualTo(dispatcher.MaxHandlerDuration / 2,
            "the rest of H13 must still fit inside the Service Bus lock after the keyless-proof retry");
    }
}
