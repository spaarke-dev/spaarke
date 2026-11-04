// unified-access-control-r2 task 194b fix round R (R3b-2), rewritten by its V-fix round (item 7) — POST /api/v1/events asks the
// owner question for the sprk_event it writes over EVERY parent the row will hold (the regarding's typed lookup and its FR-26
// core stamps), in sprk_event's primary order (RecordOwnershipContext.ForRow) — the order the secure-child reconciler asks the
// written row in. Each test runs the real handler and compares the question it asked with the reconciler's question about the
// row it wrote (RecordOwnershipContext.OrderedParentsOf over the Web API body), in the style of TaskActionCoreOwnershipOrderTests:
// no hand-written parent list.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-194b-uac-r2")]
public class EventOwnershipContextTests
{
    private static readonly Guid RegardingId = Guid.Parse("194b0000-0000-4000-8000-0000000000e1");
    private static readonly Guid MatterId = Guid.Parse("194b0000-0000-4000-8000-0000000000e2");
    private static readonly Guid EventId = Guid.Parse("194b0000-0000-4000-8000-0000000000e3");

    private readonly RecordOwnershipResolverDouble _ownership = new();

    [Theory]
    [InlineData(RegardingRecordType.Project)]
    [InlineData(RegardingRecordType.Invoice)]
    [InlineData(RegardingRecordType.Analysis)]
    [InlineData(RegardingRecordType.WorkAssignment)]
    [InlineData(RegardingRecordType.Budget)]
    [InlineData(RegardingRecordType.Matter)]
    public async Task AnEventRegardingARecordStampedWithItsMatter_AsksWhatTheReconcilerAsksAboutTheWrittenRow(int regardingType)
    {
        var result = await RunAsync(regardingType, TheRegardingIsStampedWithItsMatter(regardingType));

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        var written = Written();
        var asked = _ownership.Requests.Should().ContainSingle().Subject;

        asked.Parents.Should().NotBeEmpty("the event is filed under its regarding (and its matter)");
        asked.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(written),
            "the writer asks exactly the question the reconciler asks about the written row");
        asked.TargetEntityLogicalName.Should().Be(asked.Parents[0].EntityLogicalName, "the primary is the table's first parent");
        asked.TargetRecordId.Should().Be(asked.Parents[0].RecordId);
    }

    [Fact]
    public async Task AnEventRegardingNothing_NamesNoParent_AsTheWrittenRowHolds_TheCallersOwnUnitDecides()
    {
        var result = await RunAsync(regardingType: null, new StampWorld());

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        var asked = _ownership.Requests.Should().ContainSingle().Subject;

        asked.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(Written()));
        asked.HasParent.Should().BeFalse();
    }

    // ── The site: the real handler over boundary fakes ──────────────────────────────────────────────────────────

    /// <summary>The test's entity sets: a logical name plus "s" (the inverse, in <see cref="Written"/>, strips it).</summary>
    private static string SetOf(string logicalName) => logicalName + "s";

    private readonly List<(string Table, Guid Id, Dictionary<string, object?> Fields)> _appCreates = new();

    /// <summary>Task 031: the stamp world of an event filed under a record that is itself stamped with its matter (the column the table holds it in).</summary>
    private static StampWorld TheRegardingIsStampedWithItsMatter(int regardingType)
    {
        var world = new StampWorld().Row("sprk_matter", MatterId);
        return regardingType switch
        {
            RegardingRecordType.Analysis => world.Row("sprk_analysis", RegardingId, [("sprk_regardingmatter", "sprk_matter", MatterId)]),
            RegardingRecordType.Invoice => world.Row("sprk_invoice", RegardingId, [("sprk_matter", "sprk_matter", MatterId)]),
            RegardingRecordType.Budget => world.Row("sprk_budget", RegardingId, [("sprk_matter", "sprk_matter", MatterId)]),
            _ => world,
        };
    }

    private Task<IResult> RunAsync(int? regardingType, StampWorld stamps)
    {
        // Task 031 (FR-19): the row is created by RecordCreationService, whose ONE owner question and ONE app-only create these
        // tests read; the endpoint no longer asks the owner itself.
        var events = new Mock<IEventDataverseService>(MockBehavior.Strict);
        events.Setup(s => s.CreateEventLogAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly.Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, id, f, _, _) => _appCreates.Add((t, id, f)))
            .Returns(Task.CompletedTask);

        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string logicalName, CancellationToken _) => SetOf(logicalName));
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string logicalName, Guid id, string[] _, CancellationToken __) => new Entity(logicalName, id));

        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no oid"));

        return EventEndpoints.CreateEventAsync(
            new ApiCreateEventRequest(
                "Hearing prep",
                RegardingRecordId: regardingType is null ? null : RegardingId,
                RegardingRecordType: regardingType),
            Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Over(
                _ownership,
                appOnly.Object,
                stamps.Restamper,
                Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard()
                    .Synchronizer(new Sprk.Bff.Api.Tests.AccessControl.FakeRecordShareTable()),
                SecureRootFilingGateFixtures.NothingSecure(),
                null,
                Mock.Of<IMembershipEventPublisher>(),
                entities.Object),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.SecureChildOwnershipAiToolTests.ScriptedUserClient(
                Guid.Parse("194b0000-0000-4000-8000-0000000000e9")),
            events.Object,
            entities.Object,
            callers.Object,
            Mock.Of<IIdentityNormalizationService>(),
            Mock.Of<ICommunicationDataverseService>(),
            new DefaultHttpContext(),
            NullLogger<Program>.Instance,
            CancellationToken.None);
    }

    /// <summary>The entity set a logical name has in the scripted caller's Dataverse (the inverse is in <see cref="Written"/>).</summary>
    private static readonly Dictionary<string, string> Sets = new()
    {
        ["sprk_matters"] = "sprk_matter", ["sprk_projects"] = "sprk_project", ["sprk_invoices"] = "sprk_invoice",
        ["sprk_analysises"] = "sprk_analysis", ["sprk_workassignments"] = "sprk_workassignment", ["sprk_budgets"] = "sprk_budget",
        ["sprk_events"] = "sprk_event", ["sprk_recordtype_refs"] = "sprk_recordtype_ref", ["contacts"] = "contact",
    };

    /// <summary>
    /// The row the create wrote, as the reconciler reads it: the Web API body of the ONE app-only create, each
    /// <c>{navigation}@odata.bind</c> read back as its lookup column naming its record, and the pair id. The owner bind is the
    /// answer, not a parent: left out.
    /// </summary>
    private Entity Written()
    {
        var (_, id, fields) = _appCreates.Should().ContainSingle("the event was created").Subject;
        var row = new Entity("sprk_event", id);
        foreach (var (key, value) in fields)
        {
            if (key.EndsWith("@odata.bind", StringComparison.Ordinal) && !key.StartsWith("ownerid", StringComparison.Ordinal))
            {
                var bind = value as string ?? ((System.Text.Json.JsonElement)value!).GetString()!;                                         // "/{set}({id})"
                var open = bind.IndexOf('(');
                var set = bind[1..open];
                if (!Sets.TryGetValue(set, out var table))
                    continue;                                                      // a person (systemusers): not a parent
                row[key[..^"@odata.bind".Length].ToLowerInvariant()] =
                    new EntityReference(table, Guid.Parse(bind[(open + 1)..^1]));
            }
            else if (key == "sprk_regardingrecordid")
            {
                row[key] = value;
            }
        }

        return row;
    }

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;
}
