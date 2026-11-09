using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 042c fix round 4 (owner decision 2026-11-04, ontology D-204): a Signal, a Decision
/// Record and a Budget Revision are created only by their writers. The general create refuses the three tables
/// (<see cref="RecordTableRule.CreatedOnlyByItsWriter"/>) at the start of its pipeline, for the caller and the system principal,
/// with the refusal of a table it does not create (400 <c>record_creation.table_unsupported</c>), before anything is read,
/// mapped, checked or written. Driven through this harness's journal, which records every dependency call as it happens.
/// </summary>
public sealed partial class RecordCreationServicePipelineSeamTests
{
    /// <summary>The three tables and the three ways in: a caller's Web API body (the route's shape), a caller's mapped item (the
    /// chat's and the decision executors' shape) and a system principal's mapped item.</summary>
    public static readonly TheoryData<string, string> WriterOnlyCreates = new()
    {
        { "sprk_signal", "caller-webapi" }, { "sprk_signal", "caller-mapped" }, { "sprk_signal", "system" },
        { "sprk_decisionrecord", "caller-webapi" }, { "sprk_decisionrecord", "caller-mapped" }, { "sprk_decisionrecord", "system" },
        { "sprk_budgetrevision", "caller-webapi" }, { "sprk_budgetrevision", "caller-mapped" }, { "sprk_budgetrevision", "system" },
    };

    [Theory(DisplayName = "042c fix round 4 (D-204, must fire): a create of a table only its writer creates is refused before any read, for the caller and the system principal")]
    [MemberData(nameof(WriterOnlyCreates))]
    public async Task CreateAsync_ATableOnlyItsWriterCreates_IsRefused_BeforeAnyReadOrWrite(string table, string way)
    {
        var (request, principal) = WriterOnlyRequest(table, way);

        var outcome = await Service().CreateAsync(request, principal, CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Invalid);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.TableUnsupportedCode);
        outcome.Refusal.Detail.Should().Be($"'{table}' records are not created here.");
        var problem = Problem(outcome, table);
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.TableUnsupportedCode);

        // Nothing written, and nothing needed: no question as the caller (not even WhoAmI), no owner, no pre-write rule, no
        // app-only read or write, no re-stamp, no sharee mirror, no event.
        _createAttempts.Should().BeEmpty();
        _creates.Should().BeEmpty();
        _journal.Should().BeEmpty("refused before the pipeline asks anything");
        _restampReads.Should().BeEmpty();
        _appOnly.Invocations.Should().BeEmpty();
        _readBack.Invocations.Should().BeEmpty();
        _scripted.Posts.Should().BeEmpty();
        _scripted.Patches.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
        outcome.Steps.Should().BeEmpty("no step began");
    }

    [Theory(DisplayName = "042c fix round 4 (D-204): the refusal is a table the service does not create, word for word")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_budgetrevision")]
    public async Task CreateAsync_ATableOnlyItsWriterCreates_IsRefusedAsAnUnregisteredTableIs(string table)
    {
        const string unregistered = "sprk_notregisteredfixture";
        var refused = await Service().CreateAsync(WriterOnlyRequest(table, "system").Request, RecordCreationPrincipal.ForSystem(),
            CancellationToken.None);
        var unknown = await Service().CreateAsync(WriterOnlyRequest(unregistered, "system").Request, RecordCreationPrincipal.ForSystem(),
            CancellationToken.None);

        RecordTableRules.Find(unregistered).Should().BeNull("the fixture table is unregistered");
        refused.Refusal!.Kind.Should().Be(unknown.Refusal!.Kind);
        refused.Refusal.Code.Should().Be(unknown.Refusal.Code);
        refused.Refusal.Detail.Should().Be(unknown.Refusal.Detail.Replace(unregistered, table, StringComparison.Ordinal));
        refused.Steps.Should().Equal(unknown.Steps);
    }

    [Fact(DisplayName = "042c fix round 4 (D-204, must not fire): another child table, an event, is still created")]
    public async Task CreateAsync_AnEvent_IsStillCreated_TheRefusalDoesNotFire()
    {
        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        RecordTableRules.Get("sprk_event").CreatedOnlyByItsWriter.Should().BeFalse();
        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Table.Should().Be("sprk_event");
        Owner(_creates[0].Fields).Should().Be(Directory.ChildTeam);
    }

    /// <summary>A create of <paramref name="table"/> filed under the Secure matter, the way <paramref name="way"/> names.</summary>
    private (RecordCreationRequest Request, RecordCreationPrincipal Principal) WriterOnlyRequest(string table, string way)
    {
        if (way == "caller-webapi")
        {
            return (WebApiRequest(table, new { sprk_name = "Written by hand", sprk_Matter_bind = $"/sprk_matters({SecureMatter:D})" }),
                AsCaller());
        }

        var item = new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sprk_name"] = "Written by hand",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            }),
            ["sprk_name", "sprk_matter"])
        {
            Lookups = [new DataverseWriteItemMapper.MappedLookup("sprk_matter", "sprk_matter", "sprk_matters", SecureMatter)],
        };
        var request = new RecordCreationRequest(table, new RecordCreationPayload.Mapped(item)) { CorrelationId = FixedCorrelation };
        return (request, way == "system" ? RecordCreationPrincipal.ForSystem() : AsCaller());
    }
}
