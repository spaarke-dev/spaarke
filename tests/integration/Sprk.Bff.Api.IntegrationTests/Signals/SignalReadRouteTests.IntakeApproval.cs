// spaarke-intake-pipeline-r1 task 017 (review F4): the LIST path of the reader gating. The single-item read is pinned in
// tests/integration/auth/Intake/IntakeApprovalReaderGatingTests.cs through SignalCoreRecordAccess; the worklist reads the core
// record itself (SignalReadService.ReadReadableCoresAsync, a batched read as the caller), so the list needs its own rows. They live
// here because the fake world that answers "as each user" belongs to this fixture (it is internal to this test project).
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    /// <summary>An intake approval Work Item as the evaluation writes it: Decide lane, the service request is its own core record.
    /// Alice reads the request; Bob is in the same business unit, so he reads the Signal, but has no Read on the request.</summary>
    private static (World World, Guid Signal, Guid Request) IntakeApprovalWorld()
    {
        var world = new World();
        var request = world.Record("sprk_servicerequest", readers: [Alice]);
        var signal = world.Signal(Decide, core: (ServiceRequestType, request), shortName: "Intake approval");
        return (world, signal, request);
    }

    [Fact]
    public async Task IntakeApproval_ACallerWithoutReadOnTheRequest_DoesNotGetTheItem_InTheWorklist()
    {
        var (world, signal, request) = IntakeApprovalWorld();

        var result = await ReadAsync(world, Bob, WorklistSet.Lanes);

        result.Outcome.Should().Be(SignalReadOutcome.Ok, result.Reason);
        RowsOf(result).Should().BeEmpty("a caller who cannot read the service request is not shown its approval item");
        Json(result.Response).Should().NotContain(signal.ToString("D")).And.NotContain(request.ToString("D"));
    }

    [Fact]
    public async Task IntakeApproval_ACallerWithoutReadOnTheRequest_GetsNothing_FromTheSingleRowListRead_Too()
    {
        var (world, signal, _) = IntakeApprovalWorld();

        var one = await ReadOneAsync(world, Bob, signal);

        one.Outcome.Should().Be(SignalReadOutcome.Ok, one.Reason);
        RowsOf(one).Should().BeEmpty();
    }

    [Fact]
    public async Task IntakeApproval_ACallerWithReadOnTheRequest_GetsTheItem_InTheWorklist()
    {
        var (world, signal, request) = IntakeApprovalWorld();

        var result = await ReadAsync(world, Alice, WorklistSet.Lanes);

        result.Outcome.Should().Be(SignalReadOutcome.Ok, result.Reason);
        RowsOf(result).Should().ContainSingle().Which.SignalId.Should().Be(signal);
        var group = result.Response!.Lanes.Single(l => l.Lane == "Decide").Groups.Should().ContainSingle().Subject;
        group.Core!.RecordId.Should().Be(request);
        group.Core.RecordType.Should().Be("sprk_servicerequest");
    }
}
