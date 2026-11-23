using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

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

    private static ProcessorHarness WithOverSizeSnapshot() => new()
    {
        Channel = InvoiceSubmissionChannel.Internal,
        EvaluationEdit = result => result with
        {
            ProcedureBasis = ProcedureSnapshotPatchKit.Padded(result.ProcedureBasis ?? new BillingProcedureBasis([], [], []), ProcedureSnapshotPatchKit.OverTheColumn),
        },
    };

    [Fact(DisplayName = "082d fix round 3: a re-run whose findings are over the column only because of the Procedure snapshot drops the member (JSON null) and writes, not refuses")]
    public async Task ARerun_WhoseFindingsAreOverTheColumnOnlyBecauseOfTheSnapshot_DropsTheMember_AndWrites()
    {
        var world = await World.SeedAsync();
        world.Processor = WithOverSizeSnapshot();

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed, result.Code);
        world.Applied.Should().ContainSingle("control: the commit was sent");
        var written = world.Row(SubmissionId).GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        written.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength);
        var stored = world.Findings();
        stored.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        stored[ProcedureSnapshot.Member].Should().BeNull("JSON null: readers treat it as no snapshot");
        stored[InvoiceAssignFirmService.StoredDecisionMember]!["rulesRerun"]!.GetValue<string>().Should().Be(InvoiceAssignFirmService.RulesRerunCompleted, "control: the rest of the re-run is written");
    }

    [Fact(DisplayName = "082d fix round 3: a re-run whose findings are still over the column without the snapshot is refused TooLarge, with nothing written")]
    public async Task ARerun_WhoseFindingsAreStillOverTheColumnWithoutTheSnapshot_IsRefusedTooLarge_AndWritesNothing()
    {
        var world = await World.SeedAsync();
        world.EditFindings(f => f["padding"] = new string('x', InvoiceSubmissionPersister.FindingsJsonMaxLength + 1));
        world.Processor = WithOverSizeSnapshot();
        var before = world.Snapshot();

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Refused);
        result.Code.Should().Be(InvoiceSubmissionRerunCodes.TooLarge);
        result.Permanent.Should().BeTrue();
        world.Applied.Should().BeEmpty();
        world.Attempts.Should().Be(0);
        world.Snapshot().Should().BeEquivalentTo(before, "nothing was written");
    }
}
