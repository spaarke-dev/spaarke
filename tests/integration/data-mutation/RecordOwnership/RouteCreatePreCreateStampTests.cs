using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017, Phase 2d-1 (owner decision 2a): POST /api/v1/child-records/{table} through the
/// REAL route and the REAL <see cref="RecordCreationService"/>, whose step 10 now derives the I-1 stamp before the create. The
/// two new refusals pass the route's edge (<see cref="ChildRecordEndpoints.RefusalResult"/>) unchanged, through the shared map:
/// 400 <c>record_creation.stamp_underivable</c> with the service's words, and 503 <c>record_creation.stamp_unavailable</c> with
/// the map's fixed words. Nothing is created on either. Every existing route row (017b's NFR-07 table) is pinned elsewhere and
/// unchanged.
/// </summary>
/// <remarks>A partial of <see cref="SecureChildOwnershipAiToolTests"/> (its route harness), as RouteDecisionCreateParityTests is.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The route's create over a stamp world this test controls (the harness's own is empty).</summary>
    private Task<IResult> CreateChildOverStamps(string table, Dictionary<string, object?> payload, StampWorld stamps) =>
        ChildRecordEndpoints.CreateAsync(
            table, JsonSerializer.SerializeToElement(payload), _user,
            RouteRecordCreation.Over(
                _world.Resolver(), _appOnly.Object, stamps.Restamper, Shares(), SecureRootFilingGateFixtures.NothingSecure(),
                null, _routeMembership.Object),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

    private Dictionary<string, object?> AnEventUnderTheEvent() => new()
    {
        ["sprk_eventname"] = "Follow-up",
        ["sprk_RegardingEvent@odata.bind"] = $"/sprk_events({Event:D})",
    };

    private void TheEventIsFiledUnderTheOrdinaryMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }

    [Fact]
    public async Task RouteCreate_AnEventUnderAnEvent_CarriesItsMattersStampInTheCreatePayload()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_matter", OrdinaryMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle().Which.Fields["sprk_RegardingMatter@odata.bind"]
            .Should().Be($"/sprk_matters({OrdinaryMatter:D})", "step 10 derives the stamp into the one create");
    }

    // =====================================================================================================================
    // field-mapping-server-write-path-r1 task 042a: characterize the #2483 / DI-009 window (design.md section 15.1, 15.6).
    // Tests only. These pin TODAY's owner decision, which is wrong; task 042b derives the stamp before step 7 and INVERTS them.
    // =====================================================================================================================

    /// <summary>
    /// The team-owned Event a new event is filed under, itself filed under the SECURE matter but still owned by an ordinary
    /// team: the Make Secure pass has not reached it yet, or ended incomplete. The stamp world derives its root, the Secure
    /// matter, for the I-1 stamp.
    /// </summary>
    private StampWorld TheTeamOwnedEventIsFiledUnderTheSecureMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter) });
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        return new StampWorld().Row("sprk_matter", SecureMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", SecureMatter)]);
    }

    /// <summary>The row as Dataverse stores it after the create: every lookup bind in the create body as the column it sets.</summary>
    private static Entity TheWrittenRow(string table, Guid id, IReadOnlyDictionary<string, object?> body)
    {
        var row = new Entity(table, id);
        foreach (var (key, value) in body)
        {
            if (!key.EndsWith("@odata.bind", StringComparison.Ordinal) || key == "ownerid@odata.bind")
                continue;
            var path = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;
            if (path is null)
                continue;
            var set = path.TrimStart('/')[..path.TrimStart('/').IndexOf('(')];
            var target = Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);
            if (set == "systemusers")
                continue;
            row[key[..^"@odata.bind".Length].ToLowerInvariant()] = new EntityReference(set.TrimEnd('s'), target);
        }

        return row;
    }

    /// <summary>
    /// CHARACTERIZES #2483 (DI-009); task 042b INVERTS this test. A create filed under a TEAM-OWNED event of a Secure matter is
    /// today owned by the event's ordinary business-unit team (<see cref="Directory.ChildTeam"/>): step 7 decides the owner over
    /// the item's own lookups ([the event]); the resolver reads nothing above a team-owned parent; and step 10 derives the I-1
    /// stamp (<c>sprk_regardingmatter</c> = the Secure matter) into the create BODY only. After 042b step 7 sees the derived
    /// root, the owner is <see cref="Directory.SecureNamedTeam"/> and the sharee mirror is written at create.
    /// </summary>
    [Fact]
    public async Task RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window()
    {
        var stamps = TheTeamOwnedEventIsFiledUnderTheSecureMatter();

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        fields["ownerid@odata.bind"].Should().Be($"/teams({Directory.ChildTeam:D})",
            "CHARACTERIZES #2483: today the owner ignores the derived stamp's root; 042b inverts this to SecureNamedTeam");
        Owner(fields).Should().NotBe(Directory.SecureNamedTeam, "the window: an ordinary team can read a row of a Secure family");
        System.Console.Error.WriteLine("DUMP " + string.Join(";", fields.Select(kv => kv.Key + "=" + kv.Value)));
        fields["sprk_RegardingMatter@odata.bind"].Should().Be($"/sprk_matters({SecureMatter:D})",
            "step 10 still derives the Secure matter into the create body");
        _shareTable.MaskOf("sprk_event", id, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "CHARACTERIZES #2483: an ordinary-owned row gets no sharee mirror at create; 042b writes it at create");
    }

    /// <summary>
    /// CHARACTERIZES #2483 (DI-009); task 042b INVERTS the disagreement (A2 then agrees). The reconciler asks the written row's
    /// question, which includes the stamp the create bound, so it owns the same row to the Secure team: the create and the
    /// reconciler disagree, and the reconciler moves the row 2-4 minutes later.
    /// </summary>
    [Fact]
    public async Task RouteCreate_TheReconcilersQuestionOverTheWrittenEvent_OwnsItToTheSecureTeam_WhereTheCreateDidNot_DI009Window()
    {
        var stamps = TheTeamOwnedEventIsFiledUnderTheSecureMatter();
        await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        var written = TheWrittenRow("sprk_event", id, fields);
        var reads = new ExistingRowReads(Mock.Of<IGenericEntityService>(), null, null);

        var question = await RecordOwnershipContext.ForExistingRowAsync(written, reads, CancellationToken.None);
        var answer = await _world.Resolver().ResolveOwnerAsync(question.Context, CancellationToken.None);

        question.Undetermined.Should().BeNull();
        Owner(fields).Should().Be(Directory.ChildTeam, "what the create gave (CHARACTERIZES #2483)");
        answer.OwningTeamId.Should().Be(Directory.SecureNamedTeam,
            "CHARACTERIZES #2483: the reconciler's answer for the written row is the Secure team; 042b makes the create agree");
        answer.IsSecureOwner.Should().BeTrue();
    }

    [Fact]
    public async Task RouteCreate_WhenTheParentsRootCannotBeDetermined_Is400StampUnderivable_ThroughTheSharedMap_AndCreatesNothing()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld(); // the event is gone by the time its root is read: NotFound, a data condition

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("record_creation.stamp_underivable");
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("record_creation.stamp_underivable");
        problem.ProblemDetails.Detail.Should().StartWith("The event was not saved: the core record it belongs to cannot be determined");
        _appCreates.Should().BeEmpty("nothing is created");
        _user.Posts.Should().BeEmpty("and there is no caller-side fallback");
    }

    [Fact]
    public async Task RouteCreate_WhenTheParentsRootCannotBeReadRightNow_Is503StampUnavailable_ThroughTheSharedMap_AndCreatesNothing()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
            .FailRead("sprk_event", Event);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("record_creation.stamp_unavailable");
        problem.ProblemDetails.Detail.Should().Be(
            "The event could not be saved right now, so nothing was created. Try again in a few minutes.");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }
}
