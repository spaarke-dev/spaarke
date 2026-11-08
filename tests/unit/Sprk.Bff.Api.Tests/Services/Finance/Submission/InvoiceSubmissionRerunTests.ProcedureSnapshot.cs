using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>ebilling-r1 082d: the re-run writes the 7.4 snapshot through the persister's builder (no copy), and never keeps an older one.</summary>
public sealed partial class InvoiceSubmissionRerunTests
{
    private static readonly JsonObject StaleSnapshot = new()
    {
        ["version"] = ProcedureSnapshot.Version,
        ["chain"] = new JsonArray(),
        ["overrides"] = new JsonArray(),
        ["resolutions"] = new JsonArray(),
        ["applications"] = new JsonArray(new JsonObject
        {
            ["policyVersionId"] = Guid.Parse("082d0000-0000-4000-8000-00000000057a").ToString("D"),
            ["source"] = "Procedure",
            ["applied"] = new JsonObject { ["invoice"] = true, ["lines"] = new JsonArray() },
        }),
    };

    [Fact(DisplayName = "082d: a re-run that re-evaluates writes the snapshot of ITS evaluation over the stored one")]
    public async Task ARerun_WritesTheSnapshotOfItsEvaluation()
    {
        var world = await World.SeedAsync();
        world.EditFindings(f => f[ProcedureSnapshot.Member] = StaleSnapshot.DeepClone());

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var stored = ProcedureSnapshot.Read(world.Findings());
        stored.Should().NotBeNull("the re-run's evaluation stored its snapshot");
        stored!.AppliedPolicyVersionIds.Should().NotContain(Guid.Parse("082d0000-0000-4000-8000-00000000057a"), "the older snapshot is replaced");
    }

    [Fact(DisplayName = "082d: a re-run that ends without an evaluation (unresolved Procedures, final delivery) leaves NO snapshot, never the older one")]
    public async Task ARerun_WithNoEvaluation_LeavesNoSnapshot()
    {
        var world = await World.SeedAsync();
        world.EditFindings(f => f[ProcedureSnapshot.Member] = StaleSnapshot.DeepClone());
        ProcedureSnapshot.Read(world.Findings()).Should().NotBeNull("control: the stale snapshot is stored");
        world.Processor = Unresolved();

        var result = await world.RunAsync(finalDelivery: true);

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        world.Findings()[ProcedureSnapshot.Member].Should().BeNull();
        ProcedureSnapshot.Read(world.Findings()).Should().BeNull();
    }
}
