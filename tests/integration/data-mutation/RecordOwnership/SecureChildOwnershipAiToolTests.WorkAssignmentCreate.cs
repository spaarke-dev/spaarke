using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 046 (D-21; uac-r2's answer D-113): the ONE server create of a work assignment —
/// <c>POST /api/v1/child-records/sprk_workassignment</c>, the REAL <see cref="ChildRecordEndpoints.CreateAsync"/> over the
/// REAL <c>OwnedChildWrite</c> core with the secure-create plan, the REAL resolver over <see cref="Directory"/> and the
/// caller's scripted Dataverse. The payload is the one the Create Work Assignment wizard builds
/// (<c>workAssignmentService.ts</c> <c>createWorkAssignment</c>). The isolated create under a SECURE matter is driven through
/// the same route in <c>SecureRootInheritanceWriterTests.ChildRecordRoute.cs</c> (provisioning's fixture).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid WaRecordTypeMatter = Guid.Parse("a0460000-0000-4000-8000-000000000001");
    private static readonly Guid WaLawFirm = Guid.Parse("a0460000-0000-4000-8000-000000000002");

    /// <summary>The wizard's create payload for a work assignment regarding <paramref name="matter"/>.</summary>
    private static Dictionary<string, object?> WizardWorkAssignment(Guid matter) => new()
    {
        ["sprk_name"] = "Review the lease",
        ["sprk_priority"] = 100000001,
        ["sprk_description"] = "Second draft",
        ["sprk_responseduedate"] = "2026-10-20",
        ["sprk_searchindexname"] = "spaarke-files-index",
        ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({WaRecordTypeMatter:D})",
        ["sprk_regardingrecordid"] = matter.ToString("D"),
        ["sprk_regardingrecordname"] = "Lease dispute",
        ["sprk_regardingrecordurl"] = $"https://org/main.aspx?etn=sprk_matter&id={matter:D}",
        ["sprk_AssignedLawFirm1@odata.bind"] = $"/sprk_organizations({WaLawFirm:D})",
    };

    [Fact]
    public async Task WorkAssignmentCreate_TheWizardsPayloadUnderAnOrdinaryMatter_IsCreatedByTheApp_OwnedByTheMattersTeam_ForTheCaller()
    {
        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _user.Posts.Should().BeEmpty("a work assignment is never created as, or owned by, the user (WP-3)");
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_workassignment");
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first (I-6): the regarding matter's business-unit team");
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue?.ToString().Should().Be($"/systemusers({Caller:D})",
            "createdby is the application; the person who asked is recorded");
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        Bind(fields, "sprk_RegardingRecordType@odata.bind").Should().Be($"/sprk_recordtype_refs({WaRecordTypeMatter:D})");
        Bind(fields, "sprk_AssignedLawFirm1@odata.bind").Should().Be($"/sprk_organizations({WaLawFirm:D})");
        foreach (var column in new[]
                 {
                     "sprk_name", "sprk_priority", "sprk_description", "sprk_responseduedate", "sprk_searchindexname",
                     "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl",
                 })
        {
            fields.Should().ContainKey(column, "every field the wizard sets is written as given (parity table)");
        }

        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedToInternal", StringComparison.OrdinalIgnoreCase),
            "wizard parity: the chat tool's 'for person' default is not added by this route");
        fields.Should().NotContainKey("sprk_issecure", "filed under an ordinary matter: an ordinary row");
        Created(result).Should().Be(id);
        CreatedBody(result).TryGetProperty("warnings", out _).Should().BeFalse("nothing was left undone");
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutAppendToOnTheRegardingMatter_IsTheSame404AsAMissingMatter_AndNothingIsCreated()
    {
        var missingMatter = Guid.Parse("a0460000-0000-4000-8000-0000000000f2");
        _user.NoAppendTo.Add(OrdinaryMatter);
        _user.Missing.Add(missingMatter);

        var denied = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter));
        var missing = await CreateChild("sprk_workassignment", WizardWorkAssignment(missingMatter));

        Status(denied).Should().Be(StatusCodes.Status404NotFound, "AppendTo on the regarding record is asked as the caller");
        ReasonCode(denied).Should().Be(ChildRecordEndpoints.NotFoundCode);
        (Status(denied), ReasonCode(denied), Detail(denied)).Should().Be((Status(missing), ReasonCode(missing), Detail(missing)),
            "a record the caller cannot append to is not disclosed");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutTheCreatePrivilege_Is403_AndNothingIsCreated()
    {
        _user.Held.Remove("prvCreatesprk_workassignment");

        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.DeniedCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// Provisioning's own column (the container) is not field-secured, so the route refuses it for a work assignment; the
    /// secure flag and the access record are refused by the secure-create plan (task 175) — D-113: neither the wizard nor a
    /// decision may send them. Every refusal writes nothing.
    /// </summary>
    [Theory]
    [InlineData("sprk_containerid", "b!container-of-someone-else", ChildRecordEndpoints.DeniedCode)]
    [InlineData("sprk_issecure", true, AccessFollowsParent.SecureFlagReasonCode)]
    [InlineData("sprk_accessinheritance", "{}", AccessInheritance.ServerOnlyReasonCode)]
    public async Task WorkAssignmentCreate_NamingAColumnSpaarkeOwnsOnAWorkAssignment_Is403_AndNothingIsCreated(
        string column, object value, string reasonCode)
    {
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload[column] = value;

        var result = await CreateChild("sprk_workassignment", payload);

        Status(result).Should().Be(StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(reasonCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_UnderAMatterWhoseSecureFlagCannotBeRead_Is409_AndNothingIsCreated()
    {
        // Task 158 (owner round 17 item 3): an EMPTY parent flag is never "not secure" — the plan refuses before any write.
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) =>
                q.EntityName == "sprk_matter"
                    ? new EntityCollection(new List<Entity> { new("sprk_matter", OrdinaryMatter) })
                    : new EntityCollection());

        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter),
            SecureRootFilingGateFixtures.Over(entities.Object));

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// NFR-10 / D-29: a caller Dataverse cannot name is #1312's single 403, before the payload or any record it binds is
    /// asked about. A to-do on the same route gets the same answer (one create route, one rule).
    /// </summary>
    [Theory]
    [InlineData("sprk_workassignment", 403, DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData("sprk_workassignment", 401, DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData("sprk_workassignment", 0, DataverseUserClientErrorCodes.UserContextRequired)]
    [InlineData("sprk_workassignment", 0, DataverseUserClientErrorCodes.OboExchangeFailed)]
    [InlineData("sprk_todo", 403, DataverseUserClientErrorCodes.AccessDenied)]
    public async Task Create_ForACallerWithNoDataverseIdentity_IsTheSingle403_BeforeAnythingElseIsAsked(
        string table, int whoAmIStatus, string errorCode)
    {
        var unresolved = UserClientAnsweringWhoAmI(DataverseUserResponse.Fail(whoAmIStatus, errorCode, "who?"));

        var result = await CreateChild(table, WizardWorkAssignment(OrdinaryMatter), user: unresolved.Object);

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be("sdap.access.deny.caller_unresolved");
        unresolved.Verify(u => u.GetAsync(It.Is<string>(p => p != "WhoAmI()"), It.IsAny<CancellationToken>()), Times.Never(),
            "nothing about the payload or its records is asked for a caller who cannot be named");
        _appCreates.Should().BeEmpty();
    }

    /// <summary>Control: throttling or a Dataverse fault says nothing about WHO the caller is — their own error, not the 403.</summary>
    [Theory]
    [InlineData(429, DataverseUserClientErrorCodes.RateLimited)]
    [InlineData(503, DataverseUserClientErrorCodes.ServiceError)]
    public async Task Create_WhenWhoAmIIsThrottledOrFaults_IsNotTheUnresolvedCaller403(int whoAmIStatus, string errorCode)
    {
        var faulting = UserClientAnsweringWhoAmI(DataverseUserResponse.Fail(whoAmIStatus, errorCode, "busy"));

        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter), user: faulting.Object);

        Status(result).Should().Be(whoAmIStatus);
        ReasonCode(result).Should().NotBe("sdap.access.deny.caller_unresolved");
        _appCreates.Should().BeEmpty();
    }

    /// <summary>
    /// AC 3 (task 043): a decision's Assign Work follow-on calls the same handler IN-PROCESS through
    /// <see cref="DecisionRouteCores.CreateChildAsync"/> with the commit route's own request, and gets the new work
    /// assignment's id back for <c>sprk_followons</c>.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_InProcessThroughDecisionRouteCores_ReturnsTheCreatedId()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataverseUserClient>(_user);
        services.AddSingleton<IRecordOwnershipResolver>(_world.Resolver());
        services.AddSingleton(_appOnly.Object);
        services.AddSingleton(Restamper());
        services.AddSingleton(Shares());
        services.AddSingleton(SecureRootFilingGateFixtures.NothingSecure());
        var http = HttpContextOfCaller();
        http.RequestServices = services.BuildServiceProvider();

        var reply = await new DecisionRouteCores().CreateChildAsync(
            http, "sprk_workassignment", JsonSerializer.SerializeToElement(WizardWorkAssignment(OrdinaryMatter)),
            CancellationToken.None);

        reply.Status.Should().Be(StatusCodes.Status201Created, reply.Detail);
        var created = _appCreates.Should().ContainSingle().Subject;
        created.Table.Should().Be("sprk_workassignment");
        reply.CreatedId.Should().Be(created.Id, "the decision records the new work assignment as its follow-on");
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A caller's Dataverse whose WhoAmI answers <paramref name="whoAmI"/>; any other question is recorded.</summary>
    private static Mock<IDataverseUserClient> UserClientAnsweringWhoAmI(DataverseUserResponse whoAmI)
    {
        var client = new Mock<IDataverseUserClient>(MockBehavior.Loose);
        client.Setup(u => u.GetAsync("WhoAmI()", It.IsAny<CancellationToken>())).ReturnsAsync(whoAmI);
        return client;
    }

    private static JsonElement CreatedBody(IResult result) =>
        JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);

    private static Guid Created(IResult result) => CreatedBody(result).GetProperty("id").GetGuid();
}
