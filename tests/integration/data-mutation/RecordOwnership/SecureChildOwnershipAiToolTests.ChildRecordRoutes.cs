using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 147 r1 (owner round 28 item 1, E1 = A1): the BFF routes every BROWSER child writer now
/// sends its Web API payload to — <c>POST /api/v1/child-records/{table}</c> (G5 create) and the ONE re-file core behind
/// <c>PATCH /api/v1/child-records/{table}/{id}</c>, <c>PATCH /api/v1/events/{id}/filing</c> and
/// <c>PATCH /api/communications/{id}/filing</c>. Driven through the REAL handlers, the REAL OwnedChildWrite
/// core, the REAL resolver over <see cref="Directory"/> and the caller's scripted Dataverse (the same harness as the chat
/// tools: one G5 core, one harness).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid Event = Guid.Parse("a2470000-0000-4000-8000-000000000001");
    private static readonly Guid Todo = Guid.Parse("a2470000-0000-4000-8000-000000000003");

    // ── Create (G5) ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildCreate_AToDoUnderASecureMatter_IsCreatedByTheApp_OwnedByTheNamedTeam_ForTheCaller()
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created);
        _user.Posts.Should().BeEmpty("a browser child is never created as, or owned by, the user");
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_todo");
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})",
            "the server rebuilt the bind from the lookup's own metadata");
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue.Should().Be($"/systemusers({Caller:D})", "createdby is the application; the person is recorded");
        ((IValueHttpResult)result).Value.Should().BeEquivalentTo(new { id });
    }

    [Fact]
    public async Task ChildCreate_AToDoUnderAnOrdinaryMatter_IsOwnedByThatMattersBusinessUnitTeam()
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created);
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam);
    }

    [Fact]
    public async Task ChildCreate_AMemoUnderASecureMatter_IsOwnedByTheNamedTeam()
    {
        var result = await CreateChild("sprk_memo", new()
        {
            ["sprk_name"] = "Untitled",
            ["sprk_memobody"] = "",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created);
        var (table, _, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_memo");
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind", "task 147 r1 added sprk_memo to the stamped child tables");
    }

    [Fact]
    public async Task ChildCreate_UnderAFlaggedButNotIsolatedProject_IsRefused409_WithTheStableCode_AndCreatesNothing()
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingProject@odata.bind"] = $"/sprk_projects({FlaggedProject:D})",
        });

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        Detail(result).Should().StartWith("The to-do was not saved");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_UnderARecordTheCallerCannotAppendTo_AndUnderOneThatDoesNotExist_GetTheSameNotFound()
    {
        var missing = Guid.Parse("a2470000-0000-4000-8000-0000000000ff");
        _user.NoAppendTo.Add(SecureMatter);
        _user.Missing.Add(missing);

        var denied = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
        });
        var absent = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({missing:D})",
        });

        Status(denied).Should().Be(StatusCodes.Status404NotFound);
        Status(absent).Should().Be(StatusCodes.Status404NotFound);
        (ReasonCode(denied), Detail(denied)).Should().Be((ReasonCode(absent), Detail(absent)),
            "a record the caller may not file under answers exactly as a record that does not exist (round 9)");
        Detail(denied).Should().NotContain("secure");
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_WithoutTheTablesCreatePrivilege_IsRefused403_AndCreatesNothing()
    {
        _user.Held.Remove("prvCreatesprk_todo");

        var result = await CreateChild("sprk_todo", new() { ["sprk_name"] = "x" });

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.DeniedCode);
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ownerid@odata.bind", "/systemusers(a2460000-0000-4000-8000-0000000000aa)")]
    [InlineData("sprk_CreatedByPerson@odata.bind", "/systemusers(a2460000-0000-4000-8000-0000000000ee)")]
    public async Task ChildCreate_NamingAServerOwnedColumn_IsRefused_AndCreatesNothing(string key, string value)
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            [key] = value,
        });

        Status(result).Should().Be(StatusCodes.Status403Forbidden, "the client never sets the owner or the creator person");
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_SettingAFieldSecuredColumn_IsRefused_AndCreatesNothing()
    {
        _user.SecuredColumns.Add("sprk_notes");

        var result = await CreateChild("sprk_todo", new() { ["sprk_name"] = "x", ["sprk_notes"] = "privileged" });

        Status(result).Should().Be(StatusCodes.Status403Forbidden, "an app-only create must never pass field security");
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_RegardingMatter@odata.bind", "/sprk_projects(a2460000-0000-4000-8000-000000000001)")] // re-pointed set
    [InlineData("sprk_RegardingMatter@odata.bind", "/sprk_matters(not-a-guid)")]
    [InlineData("sprk_Unknown@odata.bind", "/sprk_matters(a2460000-0000-4000-8000-000000000001)")]
    [InlineData("sprk_name@OData.Community.Display.V1.FormattedValue", "x")]
    public async Task ChildCreate_AMalformedOrRepointedBind_IsRefused400_AndCreatesNothing(string key, string value)
    {
        var result = await CreateChild("sprk_todo", new() { ["sprk_name"] = "x", [key] = value });

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_communication")]
    [InlineData("account")]
    public async Task ChildCreate_OfATableOutsideTheCensus_IsRefused400(string table)
    {
        var result = await CreateChild(table, new() { ["sprk_name"] = "x" });

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.UnsupportedTableCode);
        _appCreates.Should().BeEmpty();
    }

    // ── Re-file (the ONE core) ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildRefile_AToDoMovedUnderASecureMatter_IsPatchedAsTheCaller_ThenOwnedByTheNamedTeam()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            ["sprk_regardingrecordname"] = "Secure matter",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle().Which.Path.Should().Be($"sprk_todos({Todo:D})");
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.SecureNamedTeam));
    }

    [Fact]
    public async Task ChildRefile_MovingADocumentOutOfASecureMatter_ByAWriteOnlyHolder_IsRefusedByF3_AndNothingIsWritten()
    {
        // The document's re-file route is PUT /api/v1/documents/{id}; the shared core is exercised through the to-do's
        // twin: a to-do filed under the secure matter, moved to an ordinary one by someone who is neither a Full Access
        // holder nor its creator.
        _world.WithRecord("sprk_todo", Todo, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
            ["createdby"] = new EntityReference("systemuser", Guid.Parse("a2460000-0000-4000-8000-0000000000ee")),
        });

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be("sdap.unsecure.not_permitted");
        _user.Patches.Should().BeEmpty("refused before the caller's PATCH");
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_ARowTheCallerCannotRead_AndOneThatDoesNotExist_GetTheSameNotFound()
    {
        var absent = Guid.Parse("a2470000-0000-4000-8000-0000000000fe");
        _user.InvisibleRows.Add(Todo);
        _user.InvisibleRows.Add(absent);

        var hidden = await RefileChild("sprk_todo", Todo, new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });
        var missing = await RefileChild("sprk_todo", absent, new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(hidden).Should().Be(StatusCodes.Status404NotFound);
        (ReasonCode(hidden), Status(hidden)).Should().Be((ReasonCode(missing), Status(missing)));
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_UnderARecordTheCallerCannotAppendTo_IsTheUniformNotFound_AndNothingIsWritten()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.NoAppendTo.Add(SecureMatter);

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnUpdateThatFilesNothing_IsTheCallersOwnPatch_AndReownsNothing()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_name"] = "renamed" });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle();
        _world.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_communication")]
    [InlineData("sprk_document")]
    public async Task ChildRefile_OfATableReFiledThroughItsOwnFamily_IsRefused400_OnTheChildRecordsRoute(string table)
    {
        var result = await RefileChild(table, Guid.NewGuid(), new() { ["sprk_name"] = "x" });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, "one re-file route per table (round 28)");
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task EventFiling_AnEventMovedUnderASecureMatter_IsOwnedByTheNamedTeam()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await ChildRecordEndpoints.UpdateAsync(
            "sprk_event", Event, Payload(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" }),
            _user, _world.Resolver(), Restamper(), HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _world.Assignments.Should().Equal(("sprk_event", Event, Directory.SecureNamedTeam));
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    private Task<IResult> CreateChild(string table, Dictionary<string, object?> payload) =>
        ChildRecordEndpoints.CreateAsync(
            table, Payload(payload), _user, _world.Resolver(), _appOnly.Object, Restamper(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

    private Task<IResult> RefileChild(string table, Guid id, Dictionary<string, object?> payload) =>
        ChildRecordEndpoints.RefileAsync(
            table, id, Payload(payload), _user, _world.Resolver(), Restamper(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

    private static CoreAncestorRestamper Restamper() =>
        new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper;

    private static JsonElement Payload(Dictionary<string, object?> payload) => JsonSerializer.SerializeToElement(payload);

    private static HttpContext HttpContextOfCaller() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("oid", CallerOid.ToString()),
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", CallerOid.ToString()),
        }, "test")),
    };

    private static int? Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private static string? ReasonCode(IResult result) =>
        result is ProblemHttpResult problem && problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code)
            ? code?.ToString()
            : null;

    private static string? Detail(IResult result) => (result as ProblemHttpResult)?.ProblemDetails.Detail;
}
