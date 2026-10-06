using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
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
    private static readonly Guid Sharee = Guid.Parse("a2470000-0000-4000-8000-0000000000b1");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    /// <summary>
    /// The REAL task 149 synchronizer's world (the same team / business-unit ids as <see cref="Directory"/>): the secure
    /// matter, shared with <see cref="Sharee"/>; every row the application creates lands here as written, and every owner
    /// assignment the resolver makes is mirrored here, so the route's inline mirror is asserted end to end.
    /// </summary>
    private SecureChildShareWorld? _shareWorldField;
    private readonly FakeRecordShareTable _shareTable = new();

    private SecureChildShareWorld ShareWorld
    {
        get
        {
            if (_shareWorldField is null)
            {
                _shareWorldField = SecureChildShareWorld.Standard()
                    .SecureRoot("sprk_matter", SecureMatter)
                    .OrdinaryRoot("sprk_matter", OrdinaryMatter);
                _shareTable.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
                _world.OnAssign = (entity, id, team) =>
                    _shareWorldField.MoveOwner(entity, id, DataversePrincipalRef.Team(team));
            }

            return _shareWorldField;
        }
    }

    /// <summary>The app-only create, as Dataverse would store it: the owner bind and every lookup bind as columns.</summary>
    private void MaterializeInShareWorld(string table, Guid id, Dictionary<string, object?> fields)
    {
        if (!SecureChildLineage.IsChild(table))
            return;

        var columns = new List<(string Column, object Value)>();
        foreach (var (key, value) in fields)
        {
            if (!key.EndsWith("@odata.bind", StringComparison.Ordinal))
                continue;
            var path = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;
            if (path is null)
                continue;
            var set = path.TrimStart('/')[..path.TrimStart('/').IndexOf('(')];
            var target = Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);
            var navigation = key[..^"@odata.bind".Length];
            if (navigation == "ownerid")
                columns.Add(("owningteam", new Microsoft.Xrm.Sdk.EntityReference("team", target)));
            else if (set != "systemusers")
                columns.Add((navigation.ToLowerInvariant(), new Microsoft.Xrm.Sdk.EntityReference(set.TrimEnd('s'), target)));
        }

        ShareWorld.Add(table, id, columns.ToArray());
    }

    private SecureChildShareSynchronizer Shares() => ShareWorld.Synchronizer(_shareTable);

    /// <summary>
    /// Task 147 r1c (round 36): the REAL task 148 reconciler over the same share world — the after-re-file step every re-file
    /// writer runs (the row's mirror, then the pass over what is filed under it). A pass below a CHILD row never reads the
    /// platform-cascade rows, so it needs no Web API client.
    /// </summary>
    private SecureChildReconciler Children() => SecureChildShareWorld.ReconcilerOver(() => ShareWorld, _shareTable, null!);

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
        _shareTable.MaskOf("sprk_todo", id, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the secure matter's sharee sees the new to-do at once (task 149's mirror, run inline by the route)");
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
        var created = _appCreates.Should().ContainSingle().Subject;
        Owner(created.Fields).Should().Be(Directory.ChildTeam);
        _shareTable.WriteLog.Should().BeEmpty("an ordinary record's child is never mirrored or shared");
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
        var (table, memoId, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_memo");
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        _shareTable.MaskOf("sprk_memo", memoId, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind", "task 147 r1 added sprk_memo to the stamped child tables");
    }

    [Fact]
    public async Task ChildCreate_ABudgetUnderASecureMatter_FromTheRibbonsNewBudget_IsOwnedByTheNamedTeam()
    {
        // Owner round 28 item 2 (E2): the native subgrid "+ New" under a secure matter is replaced by a command that creates
        // the budget through this route and then opens it.
        var result = await CreateChild("sprk_budget", new()
        {
            ["sprk_name"] = "New budget",
            ["sprk_Matter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created);
        _user.Posts.Should().BeEmpty();
        var (table, budgetId, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_budget");
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        _shareTable.MaskOf("sprk_budget", budgetId, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind", "task 147 r1 added sprk_budget to the stamped child tables");
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

    [Theory]
    [InlineData("ownerid@odata.bind", "/systemusers(a2460000-0000-4000-8000-0000000000aa)")]
    [InlineData("sprk_CreatedByPerson@odata.bind", "/systemusers(a2460000-0000-4000-8000-0000000000ee)")]
    public async Task ChildRefile_NamingAServerOwnedColumn_IsRefused403_AndNothingIsWritten(string key, string value)
    {
        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            [key] = value,
        });

        Status(result).Should().Be(StatusCodes.Status403Forbidden, "a re-file never sets the owner or the creator person");
        ReasonCode(result).Should().Be(ChildRecordEndpoints.DeniedCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
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

    // ── Per census table (task 147 r1c, AC13): secure / ordinary / refusal for EVERY table the route creates ────

    /// <summary>Every create table, with the lookup a writer names a matter through (live navigation properties).</summary>
    public static TheoryData<string, string> CensusCreateTablesUnderAMatter => new()
    {
        { "sprk_todo", "sprk_RegardingMatter" },
        { "sprk_event", "sprk_RegardingMatter" },
        { "sprk_memo", "sprk_RegardingMatter" },
        { "sprk_invoice", "sprk_Matter" },
        { "sprk_reportcard", "sprk_RegardingMatter" },
        { "sprk_analysis", "sprk_RegardingMatter" },
        { "sprk_document", "sprk_Matter" },
        { "sprk_budget", "sprk_Matter" },
        { "sprk_kpiassessment", "sprk_Matter" },
        { "sprk_billingevent", "sprk_matter" },
    };

    [Fact]
    public void TheCensusTheory_CoversEveryCreateTable()
    {
        CensusCreateTablesUnderAMatter.Select(row => (string)row[0]).Should()
            .BeEquivalentTo(ChildRecordEndpoints.CreateTables, "a table added to the route gets its secure/ordinary/refusal cases");
    }

    [Theory]
    [MemberData(nameof(CensusCreateTablesUnderAMatter))]
    public async Task ChildCreate_EveryCensusTable_UnderASecureMatter_IsCreatedByTheApp_OwnedByTheNamedTeam_AndMirrored(
        string table, string navigation)
    {
        var result = await CreateChild(table, new()
        {
            ["sprk_name"] = "x",
            [$"{navigation}@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _user.Posts.Should().BeEmpty("a browser child is never created as, or owned by, the user");
        var (created, id, fields) = _appCreates.Should().ContainSingle().Subject;
        created.Should().Be(table);
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        _shareTable.MaskOf(table, id, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the secure matter's sharee sees the new row at once");
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind",
            "createdby is the application; the person who asked is recorded (RecordCreatorPerson)");
    }

    [Theory]
    [MemberData(nameof(CensusCreateTablesUnderAMatter))]
    public async Task ChildCreate_EveryCensusTable_UnderAnOrdinaryMatter_IsOwnedByThatMattersBusinessUnitTeam_AndNotShared(
        string table, string navigation)
    {
        var result = await CreateChild(table, new()
        {
            ["sprk_name"] = "x",
            [$"{navigation}@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam,
            "round 35 item 6 / I-6: a child of a non-secure parent is owned by that parent's business-unit team");
        _shareTable.WriteLog.Should().BeEmpty("an ordinary record's child is never mirrored or shared");
    }

    [Theory]
    [MemberData(nameof(CensusCreateTablesUnderAMatter))]
    public async Task ChildCreate_EveryCensusTable_UnderAMatterTheCallerCannotAppendTo_IsTheUniformNotFound_AndCreatesNothing(
        string table, string navigation)
    {
        _user.NoAppendTo.Add(SecureMatter);

        var result = await CreateChild(table, new()
        {
            ["sprk_name"] = "x",
            [$"{navigation}@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    // ── Round 34 item 6: CreateEventWizard's documents bind the event through sprk_relatedevent ────────────

    [Fact]
    public async Task ChildCreate_ADocumentFiledUnderAnEvent_BindsSprkRelatedEvent()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await CreateChild("sprk_document", new()
        {
            ["sprk_documentname"] = "brief.pdf",
            ["sprk_RelatedEvent@odata.bind"] = $"/sprk_events({Event:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Bind(fields, "sprk_RelatedEvent@odata.bind").Should().Be($"/sprk_events({Event:D})",
            "sprk_document's event lookup is sprk_relatedevent (navigation property sprk_RelatedEvent)");
        fields.Keys.Should().NotContain(k => k.Equals("sprk_Event@odata.bind", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChildCreate_ADocumentBindingTheNonexistentSprkEventColumn_IsRefused400_AndCreatesNothing()
    {
        // The defect round 34 item 6 names: `sprk_Event@odata.bind` on sprk_document — no such column. The mapper resolves
        // every bind from the table's own metadata, so the wrong key is refused rather than written.
        var result = await CreateChild("sprk_document", new()
        {
            ["sprk_documentname"] = "brief.pdf",
            ["sprk_Event@odata.bind"] = $"/sprk_events({Event:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    // ── Re-file (the ONE core) ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildRefile_AToDoMovedUnderASecureMatter_IsPatchedAsTheCaller_ThenOwnedByTheNamedTeam()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        // The share world holds the row as the caller's PATCH leaves it: filed under the secure matter (its owner follows
        // the resolver's assignment through OnAssign).
        ShareWorld.OrdinaryChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            ["sprk_regardingrecordname"] = "Secure matter",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle().Which.Path.Should().Be($"sprk_todos({Todo:D})");
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "a child moved under a secure record is shared with its sharees in the same request");
    }

    [Fact]
    public async Task ChildRefile_AToDoMovedOutOfASecureMatter_ByAFullAccessHolder_ReturnsToTheBusinessUnitTeam_AndLosesTheMirror()
    {
        // AC3: re-parenting OUT of every secure record restores the new parent's business-unit team and takes the mirrored
        // shares off (owner round 22: only a row that WAS isolated carries the mirror).
        _world.WithRecord("sprk_todo", Todo, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        ShareWorld.SecureChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        _shareTable.Seed("sprk_todo", Todo, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[Todo] = Directory.SecureNamedTeam;
        ShareWorld.Set("sprk_todo", Todo, "sprk_regardingmatter", new EntityReference("sprk_matter", OrdinaryMatter));

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.ChildTeam));
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the secure record's sharees no longer reach a child that left it");
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

    // ── PATCH /api/v1/events/{id}/filing and PATCH /api/communications/{id}/filing (round 36) ───────────────────

    [Fact]
    public async Task EventFiling_AnEventMovedUnderASecureMatter_IsOwnedByTheNamedTeam()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        ShareWorld.UserOwnedChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", SecureMatter));

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _world.Assignments.Should().Equal(("sprk_event", Event, Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_event", Event, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task EventFiling_TheRegardingLookupsAndTheResolverFields_AreTheFiling_AndAreWrittenAsTheCaller()
    {
        // The RegardingResolver's saved-host payload: the chosen lookup, the cleared siblings, the ADR-024 resolver fields.
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        var result = await RefileEvent(new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_RegardingProject@odata.bind"] = null,
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["sprk_regardingrecordname"] = "Smith v. Jones",
            ["sprk_regardingrecordurl"] = "/main.aspx?etn=sprk_matter",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var patch = _user.Patches.Should().ContainSingle().Subject;
        patch.Path.Should().Be($"sprk_events({Event:D})");
        // Task 147 r1c-v1 (verifier item 1): the body Dataverse receives. The pre-cleared sibling stays a NAVIGATION
        // PROPERTY's null bind — its logical name (sprk_regardingproject) is not a Web API property, and a body naming it
        // is refused (0x80060888), which is what broke every resolver selection on a saved host.
        var body = BodyOf(patch.Body);
        body.Keys.Should().BeEquivalentTo(
            "sprk_RegardingMatter@odata.bind", "sprk_RegardingProject@odata.bind",
            "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl");
        body["sprk_RegardingProject@odata.bind"].ValueKind.Should().Be(JsonValueKind.Null);
        body["sprk_RegardingMatter@odata.bind"].GetString().Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        _user.RefusedBodies.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_name")]
    [InlineData("statuscode")]
    [InlineData("sprk_CompletedBy@odata.bind")]
    [InlineData("ownerid@odata.bind")]
    public async Task EventFiling_APayloadNamingAnythingButTheFiling_Is400_AndNothingIsWritten(string property)
    {
        // Owner round 36: "it changes only the filing" — every other change to an event is the caller's own update.
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileEvent(new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            [property] = property.EndsWith("@odata.bind", StringComparison.Ordinal) ? $"/systemusers({Caller:D})" : "x",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFilingCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task EventFiling_MovedUnderASecureMatter_TakesTheToDoFiledUnderTheEventIntoIsolation_AndMirrorsIt()
    {
        // Round 36: "then 148/149's child pass runs when the event moves under ... a secure parent". Before r1c a to-do of the
        // event stayed readable by the business unit under the secure matter until the next sweep.
        var todoOfEvent = Guid.Parse("a2470000-0000-4000-8000-000000000011");
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        ShareWorld.OrdinaryChild("sprk_todo", todoOfEvent, ("sprk_regardingevent", "sprk_event", Event));

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.OwnerOf("sprk_todo", todoOfEvent).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam),
            "the to-do follows its event into isolation in the same request");
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "and the secure matter's sharee sees it at once");
    }

    [Fact]
    public async Task EventFiling_ATransientFailureMovingTheToDo_IsCompletedByTheSecondPass()
    {
        var todoOfEvent = Guid.Parse("a2470000-0000-4000-8000-000000000016");
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        ShareWorld.OrdinaryChild("sprk_todo", todoOfEvent, ("sprk_regardingevent", "sprk_event", Event));
        // The first re-own of the to-do is refused; the fault has cleared by the second.
        var writes = 0;
        ShareWorld.RefusingOwnerWritesOf(todoOfEvent);
        ShareWorld.Sequence = () =>
        {
            if (++writes == 2)
                ShareWorld.ClearOwnerWriteFaults();
            return writes;
        };

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.OwnerWrites.Where(w => w.Id == todoOfEvent).Should().HaveCount(2, "one pass, then the one retry");
        ShareWorld.OwnerOf("sprk_todo", todoOfEvent).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task EventFiling_WhenTheToDoCannotBeMoved_TheReFileStands_AndTheToDoIsLeftAsItWas()
    {
        // ADR-003: the pass never fails the re-file it follows, and never guesses — after the retry the to-do is reported
        // (an ERROR) and left as it was; the two-minute recent-changes pass moves it in (it sees the event's modification).
        var todoOfEvent = Guid.Parse("a2470000-0000-4000-8000-000000000017");
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        ShareWorld.OrdinaryChild("sprk_todo", todoOfEvent, ("sprk_regardingevent", "sprk_event", Event));
        ShareWorld.RefusingOwnerWritesOf(todoOfEvent);

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _world.Assignments.Should().Equal(("sprk_event", Event, Directory.SecureNamedTeam));
        ShareWorld.OwnerWrites.Where(w => w.Id == todoOfEvent).Should().HaveCount(Sprk.Bff.Api.Services.Access.SecureChildReconciler.RefileChildPassAttempts);
        ShareWorld.OwnerOf("sprk_todo", todoOfEvent).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam));
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    [Fact]
    public async Task EventFiling_MovedOutOfASecureMatter_ByAFullAccessHolder_ReleasesTheToDoFiledUnderIt_AndTakesItsMirrorOff()
    {
        // The release rides the event's own F3 (Full Access on the matter left): the to-do was isolated only through it.
        var todoOfEvent = Guid.Parse("a2470000-0000-4000-8000-000000000012");
        MovedOutOfTheSecureMatter(todoOfEvent, alsoUnderTheSecureMatter: false);

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _world.Assignments.Should().Equal(("sprk_event", Event, Directory.ChildTeam));
        ShareWorld.OwnerOf("sprk_todo", todoOfEvent).Should().Be(DataversePrincipalRef.Team(Directory.ChildTeam),
            "it follows the event out to the event's business unit team");
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "owner round 22: its mirror of the secure matter's sharees goes with it");
    }

    [Fact]
    public async Task EventFiling_MovedOutOfASecureMatter_KeepsAToDoAlsoFiledUnderThatMatterIsolated()
    {
        var todoOfEvent = Guid.Parse("a2470000-0000-4000-8000-000000000013");
        MovedOutOfTheSecureMatter(todoOfEvent, alsoUnderTheSecureMatter: true);

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.OwnerOf("sprk_todo", todoOfEvent).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam),
            "secure-if-any: it is still filed under the secure matter itself");
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task EventFiling_OfAnEventThatWasNeverIsolated_ReleasesNothingFiledUnderIt()
    {
        // Owner round 24 item 2: no F3 was asked (the event left no secure record), so an isolated to-do under it — held
        // for an F3 holder's act — is not this re-file's to release.
        var heldTodo = Guid.Parse("a2470000-0000-4000-8000-000000000014");
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        ShareWorld.SecureChild("sprk_todo", heldTodo, ("sprk_regardingevent", "sprk_event", Event));
        _shareTable.Seed("sprk_todo", heldTodo, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.OwnerOf("sprk_todo", heldTodo).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_todo", heldTodo, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        ShareWorld.OwnerWrites.Should().NotContain(w => w.Id == heldTodo);
    }

    [Fact]
    public async Task EventFiling_MovedBetweenSecureMatters_MovesTheToDosMirrorToTheNewMattersSharees()
    {
        var secondMatter = Guid.Parse("a2470000-0000-4000-8000-0000000000c1");
        var secondSharee = Guid.Parse("a2470000-0000-4000-8000-0000000000b2");
        var todoOfEvent = Guid.Parse("a2470000-0000-4000-8000-000000000015");
        _world.WithSecureRoot("sprk_matter", secondMatter);
        _world.WithRecord("sprk_event", Event, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[Event] = Directory.SecureNamedTeam;
        ShareWorld.SecureRoot("sprk_matter", secondMatter);
        _shareTable.Seed("sprk_matter", secondMatter, DataversePrincipalRef.User(secondSharee), CollaborateMask);
        ShareWorld.SecureChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", secondMatter));
        ShareWorld.SecureChild("sprk_todo", todoOfEvent, ("sprk_regardingevent", "sprk_event", Event));
        _shareTable.Seed("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var result = await RefileEvent(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({secondMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.OwnerOf("sprk_todo", todoOfEvent).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the first matter's sharee loses the to-do with the event");
        _shareTable.MaskOf("sprk_todo", todoOfEvent, DataversePrincipalRef.User(secondSharee)).Should().Be(CollaborateMask,
            "and the second matter's sharee gains it");
    }

    [Fact]
    public async Task CommunicationFiling_ACommunicationMovedUnderASecureMatter_IsPatchedAsTheCaller_ThenOwnedByTheNamedTeam_AndMirrored()
    {
        // PATCH /api/communications/{id}/filing (the communications family, round 28/36) — the Connections writers' route.
        var communication = Guid.Parse("a2470000-0000-4000-8000-000000000005");
        _world.WithRecord("sprk_communication", communication, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.UserOwnedChild("sprk_communication", communication, ("sprk_regardingmatter", "sprk_matter", SecureMatter));

        var result = await RefileCommunication(
            communication, new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle().Which.Path.Should().Be($"sprk_communications({communication:D})");
        _world.Assignments.Should().Equal(("sprk_communication", communication, Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_communication", communication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task CommunicationFiling_UnderARecordTheCallerCannotAppendTo_IsTheUniformNotFound_AndNothingIsWritten()
    {
        var communication = Guid.Parse("a2470000-0000-4000-8000-000000000005");
        _world.WithRecord("sprk_communication", communication, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.NoAppendTo.Add(SecureMatter);

        var result = await RefileCommunication(
            communication, new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task CommunicationFiling_WithTheAssociationStatus_Is400_TheStatusIsTheCallersOwnUpdate()
    {
        var communication = Guid.Parse("a2470000-0000-4000-8000-000000000005");
        _world.WithRecord("sprk_communication", communication, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileCommunication(communication, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = null,
            ["sprk_associationstatus"] = null,
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFilingCode);
        _user.Patches.Should().BeEmpty();
    }

    public static TheoryData<string, bool> FilingShapes => new()
    {
        { "{}", false },
        { "[]", false },
        { "\"x\"", false },
        { "{\"sprk_regarding\":null}", false },
        { "{\"@odata.etag\":\"W/1\"}", false },
        { "{\"sprk_regardingrecordid\":\"x\",\"sprk_name\":\"y\"}", false },
        { "{\"sprk_RegardingMatter@odata.bind\":\"/sprk_matters(00000000-0000-0000-0000-000000000001)\"}", true },
        { "{\"sprk_RegardingRecordType@odata.bind\":null,\"sprk_regardingrecordid\":null}", true },
        { "{\"sprk_regardingrecordnumber\":\"MAT-1\",\"sprk_regardingrecordurl\":null,\"sprk_regardingrecordname\":\"A\"}", true },
    };

    [Theory]
    [MemberData(nameof(FilingShapes))]
    public void FilingShape_AcceptsOnlyTheRegardingLookupsAndTheResolverFields(string json, bool accepted)
    {
        using var document = JsonDocument.Parse(json);

        var problem = ChildRecordEndpoints.FilingShapeProblem(document.RootElement.Clone(), "sprk_event");

        if (accepted)
        {
            problem.Should().BeNull();
        }
        else
        {
            Status(problem!).Should().Be(StatusCodes.Status400BadRequest);
            ReasonCode(problem!).Should().Be(ChildRecordEndpoints.NotFilingCode);
        }
    }

    /// <summary>
    /// An event filed under the secure matter (isolated, with a to-do under it isolated and mirrored), that a Full Access
    /// holder moves to the ordinary matter. The share world sees the PATCH's result (the event under the ordinary matter);
    /// the event's business unit team is known to it so the released to-do's owner can be derived.
    /// </summary>
    private void MovedOutOfTheSecureMatter(Guid todoOfEvent, bool alsoUnderTheSecureMatter)
    {
        _world.WithRecord("sprk_event", Event, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[Event] = Directory.SecureNamedTeam;
        ShareWorld.Add("businessunit", Directory.ChildBu, ("name", "Spaarke Business Unit 1"));
        ShareWorld.Team(Directory.ChildTeam, Directory.ChildBu, "Spaarke Business Unit 1", isDefault: true, teamType: 0);
        ShareWorld.SecureChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        var lookups = alsoUnderTheSecureMatter
            ? new[] { ("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", SecureMatter) }
            : new[] { ("sprk_regardingevent", "sprk_event", Event) };
        ShareWorld.SecureChild("sprk_todo", todoOfEvent, lookups);
        _shareTable.Seed("sprk_todo", todoOfEvent, DataversePrincipalRef.User(Sharee), CollaborateMask);
    }

    /// <summary>Every table re-filed through <c>PATCH /api/v1/child-records/{table}/{id}</c>, with its matter lookup.</summary>
    public static TheoryData<string, string, string> CensusRefileTablesUnderAMatter => new()
    {
        { "sprk_todo", "sprk_RegardingMatter", "sprk_regardingmatter" },
        { "sprk_memo", "sprk_RegardingMatter", "sprk_regardingmatter" },
        { "sprk_invoice", "sprk_Matter", "sprk_matter" },
        { "sprk_reportcard", "sprk_RegardingMatter", "sprk_regardingmatter" },
        { "sprk_analysis", "sprk_RegardingMatter", "sprk_regardingmatter" },
    };

    [Fact]
    public void TheRefileTheory_CoversEveryRefileTable()
    {
        CensusRefileTablesUnderAMatter.Select(row => (string)row[0]).Should()
            .BeEquivalentTo(ChildRecordEndpoints.RefileTables, "a table added to the re-file route gets its cases");
    }

    [Theory]
    [MemberData(nameof(CensusRefileTablesUnderAMatter))]
    public async Task ChildRefile_EveryCensusTable_MovedUnderASecureMatter_IsOwnedByTheNamedTeam_AndMirrored(
        string table, string navigation, string column)
    {
        var row = Guid.Parse("a2470000-0000-4000-8000-000000000007");
        _world.WithRecord(table, row, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild(table, row, (column, "sprk_matter", SecureMatter));

        var result = await RefileChild(table, row, new() { [$"{navigation}@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
        _world.Assignments.Should().Equal((table, row, Directory.SecureNamedTeam));
        _shareTable.MaskOf(table, row, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Theory]
    [MemberData(nameof(CensusRefileTablesUnderAMatter))]
    public async Task ChildRefile_EveryCensusTable_MovedOutOfASecureMatter_ByAFullAccessHolder_ReturnsToTheBusinessUnitTeam_AndLosesTheMirror(
        string table, string navigation, string column)
    {
        var row = Guid.Parse("a2470000-0000-4000-8000-000000000008");
        _world.WithRecord(table, row, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            [column] = new EntityReference("sprk_matter", SecureMatter),
        });
        ShareWorld.SecureChild(table, row, (column, "sprk_matter", SecureMatter));
        _shareTable.Seed(table, row, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[row] = Directory.SecureNamedTeam;
        ShareWorld.Set(table, row, column, new EntityReference("sprk_matter", OrdinaryMatter));

        var result = await RefileChild(table, row, new() { [$"{navigation}@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _world.Assignments.Should().Equal((table, row, Directory.ChildTeam));
        _shareTable.MaskOf(table, row, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(CensusRefileTablesUnderAMatter))]
    public async Task ChildRefile_EveryCensusTable_UnderAMatterTheCallerCannotAppendTo_IsTheUniformNotFound_AndNothingIsWritten(
        string table, string navigation, string column)
    {
        _ = column;
        var row = Guid.Parse("a2470000-0000-4000-8000-000000000009");
        _world.WithRecord(table, row, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.NoAppendTo.Add(SecureMatter);

        var result = await RefileChild(table, row, new() { [$"{navigation}@odata.bind"] = $"/sprk_matters({SecureMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    // ── Task 147 r1c: the chat update tool runs the SAME after-re-file mirror (one implementation) ──────────────

    [Fact]
    public async Task UpdateTool_MovingAToDoUnderASecureMatter_SharesItWithTheMattersSharees_Inline()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));

        var result = await UpdateRecordWithShares(
            "sprk_todo", Todo, Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact]
    public async Task UpdateTool_MovingAToDoOutOfASecureMatter_ByAFullAccessHolder_TakesTheMirroredSharesOff()
    {
        // Before r1c the chat tool's move out left the secure record's sharees on the row for good: the two-minute share
        // job looks only at Secure-team-owned rows, so nothing ever took them off.
        _world.WithRecord("sprk_todo", Todo, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        ShareWorld.SecureChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        _shareTable.Seed("sprk_todo", Todo, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        ShareWorld.Set("sprk_todo", Todo, "sprk_regardingmatter", new EntityReference("sprk_matter", OrdinaryMatter));

        var result = await UpdateRecordWithShares(
            "sprk_todo", Todo, Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.ChildTeam));
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateTool_ReFilingANeverIsolatedToDo_KeepsItsOwnShares()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        _shareTable.Seed("sprk_todo", Todo, DataversePrincipalRef.User(Sharee), 1);

        var result = await UpdateRecordWithShares(
            "sprk_todo", Todo, Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().Be(1,
            "owner round 22: a share on a never-isolated row is its user's own intent");
    }

    /// <summary>The chat update tool with a scope that resolves the REAL reconciler (and its synchronizer) over this test's
    /// share world.</summary>
    private Task<Sprk.Bff.Api.Services.Ai.ToolResult> UpdateRecordWithShares(
        string table, Guid id, params (string Column, JsonElement Value)[] item)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => Children());
        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider);

        return new Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler(
                _user,
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                NullLogger<Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler>.Instance,
                _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
                scopes)
            .ExecuteChatAsync(
                BuildChatInvocationContext(
                    toolArgumentsJson: JsonSerializer.Serialize(new
                    {
                        tablename = table,
                        recordId = id,
                        item = item.ToDictionary(i => i.Column, i => i.Value),
                    })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    // ── Task 147 r1c-v1 (verifier item 1): the body Dataverse RECEIVES. A lookup is set AND cleared through its navigation
    //    property (`{Nav}@odata.bind`); its logical name is not a Web API property. The scripted Dataverse refuses a body
    //    that names one (`RefusedBodies`, 400 0x80060888), as the real one does — before r1c-v1 every clear reached it as
    //    `"<logical name>": null`, so every re-file that cleared a lookup (the resolver's pre-clear, Connections' unlink
    //    and clear, the side pane's clear, the chat tool's clear) failed live while these tests passed. ─────────────────

    [Fact]
    public async Task ChildRefile_TheResolversSetAndPreClear_ReachDataverseAsNavigationPropertyBinds_NeverALogicalName()
    {
        // A RegardingResolver selection on a saved to-do: the chosen lookup, the pre-cleared sibling, a resolver field.
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            ["sprk_RegardingProject@odata.bind"] = null,
            ["sprk_regardingrecordname"] = "Secure matter",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.RefusedBodies.Should().BeEmpty();
        var body = BodyOf(_user.Patches.Should().ContainSingle().Subject.Body);
        body.Keys.Should().BeEquivalentTo(
            "sprk_RegardingMatter@odata.bind", "sprk_RegardingProject@odata.bind", "sprk_regardingrecordname");
        body["sprk_RegardingProject@odata.bind"].ValueKind.Should().Be(JsonValueKind.Null);
        body["sprk_RegardingMatter@odata.bind"].GetString().Should().Be($"/sprk_matters({SecureMatter:D})");
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.SecureNamedTeam));
    }

    [Fact]
    public async Task ChildRefile_AClearOnlyMoveOutOfASecureMatter_ClearsThroughTheNavigationProperty_ReturnsTheRowToItsBusinessUnit_AndTakesTheMirrorOff()
    {
        // AC3 through a CLEAR (the resolver's "clear", a move out of every secure record) by a Full Access holder.
        _world.WithRecord("sprk_todo", Todo, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        ShareWorld.SecureChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        _shareTable.Seed("sprk_todo", Todo, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[Todo] = Directory.SecureNamedTeam;
        ShareWorld.Set("sprk_todo", Todo, "sprk_regardingmatter", null); // what the caller's PATCH leaves

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_RegardingMatter@odata.bind"] = null });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.RefusedBodies.Should().BeEmpty();
        _user.Patches.Should().ContainSingle().Which.Body.Should().Be("{\"sprk_RegardingMatter@odata.bind\":null}");
        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.ChildTeam));
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the secure record's sharees no longer reach a child cleared out of it");
    }

    [Fact]
    public async Task CommunicationFiling_AConnectionsUnlink_ClearsThroughTheNavigationProperty()
    {
        var communication = Guid.Parse("a2470000-0000-4000-8000-000000000006");
        _world.WithRecord("sprk_communication", communication, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["createdby"] = new EntityReference("systemuser", Caller),
        });

        var result = await RefileCommunication(communication, new() { ["sprk_RegardingMatter@odata.bind"] = null });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.RefusedBodies.Should().BeEmpty();
        _user.Patches.Should().ContainSingle().Which.Body.Should().Be("{\"sprk_RegardingMatter@odata.bind\":null}");
    }

    [Fact]
    public async Task UpdateTool_ClearingALookupByItsLogicalName_ClearsThroughItsNavigationProperty()
    {
        // The chat tool's item is keyed by logical name (the GA MCP contract); the body is not.
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
        });

        var result = await UpdateRecord("sprk_todo", Todo, ("sprk_regardingmatter", JsonNull));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.RefusedBodies.Should().BeEmpty();
        _user.Patches.Should().ContainSingle().Which.Body.Should().Be("{\"sprk_RegardingMatter@odata.bind\":null}");
    }

    [Fact]
    public async Task UpdateTool_ClearingAPolymorphicLookup_ClearsThroughOneNavigationProperty_TheFirstByName()
    {
        var task = Guid.Parse("a2470000-0000-4000-8000-000000000021");

        var result = await UpdateRecord("task", task, ("regardingobjectid", JsonNull), ("description", JsonNull));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.RefusedBodies.Should().BeEmpty();
        _user.Patches.Should().ContainSingle().Which.Body.Should().Be(
            "{\"regardingobjectid_sprk_matter@odata.bind\":null,\"description\":null}",
            "every navigation property of a polymorphic lookup binds the same column; a plain column's null stays its own");
    }

    [Fact]
    public async Task ChildCreate_APayloadClearingALookup_CreatesWithTheNavigationPropertysNullBind()
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_RegardingProject@odata.bind"] = null,
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey("sprk_RegardingProject@odata.bind")
            .WhoseValue.Should().BeOfType<JsonElement>().Which.ValueKind.Should().Be(JsonValueKind.Null);
        fields.Keys.Should().NotContain(k => k.Equals("sprk_regardingproject", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("{\"sprk_RegardingMatter@odata.bind\":\"/sprk_matters(a2460000-0000-4000-8000-000000000002)\",\"sprk_regardingmatter@odata.bind\":null}")]
    [InlineData("{\"sprk_regardingmatter\":null,\"sprk_RegardingMatter@odata.bind\":\"/sprk_matters(a2460000-0000-4000-8000-000000000002)\"}")]
    [InlineData("{\"sprk_RegardingMatter@odata.bind\":\"/sprk_matters(a2460000-0000-4000-8000-000000000002)\",\"sprk_regardingmatter@odata.bind\":\"/sprk_matters(a2460000-0000-4000-8000-000000000001)\"}")]
    [InlineData("{\"sprk_RegardingMatter@odata.bind\":null,\"sprk_regardingMatter@odata.bind\":null}")]
    public async Task ChildRefile_APayloadNamingOneLookupTwice_IsRefused400_AndNothingIsWritten(string json)
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        using var document = JsonDocument.Parse(json);

        var result = await ChildRecordEndpoints.RefileAsync(
            "sprk_todo", Todo, document.RootElement.Clone(), _user, _world.Resolver(), Restamper(), Children(),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    // ── Task 147 r1c-v1 (verifier item 2, AC9): a row the caller may not READ answers exactly as a row that does not
    //    exist, on every route that runs the ONE re-file (Dataverse answers the first 403, the second 404) ─────────────

    public static TheoryData<string> RowReadRoutes => new() { "child-records", "event-filing", "communication-filing" };

    [Theory]
    [MemberData(nameof(RowReadRoutes))]
    public async Task ARowTheCallerMayNotRead_AnswersExactlyAsARowThatDoesNotExist(string route)
    {
        var unreadable = Guid.Parse("a2470000-0000-4000-8000-0000000000f1");
        var absent = Guid.Parse("a2470000-0000-4000-8000-0000000000f2");
        _user.UnreadableRows.Add(unreadable);
        _user.InvisibleRows.Add(absent);
        var payload = new Dictionary<string, object?> { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})" };

        var denied = await RefileThrough(route, unreadable, payload);
        var missing = await RefileThrough(route, absent, payload);

        Status(denied).Should().Be(StatusCodes.Status404NotFound, "an existing row the caller cannot read is not disclosed");
        ProblemOf(denied).Should().Be(ProblemOf(missing),
            "the same status, title, detail and reason code as a row that does not exist (round 9, AC9)");
        Detail(denied).Should().NotContain("privilege").And.NotContain(unreadable.ToString("D"));
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    private Task<IResult> RefileThrough(string route, Guid id, Dictionary<string, object?> payload) => route switch
    {
        "child-records" => RefileChild("sprk_todo", id, payload),
        "event-filing" => Sprk.Bff.Api.Api.Events.EventEndpoints.RefileEventAsync(
            id, Payload(payload), _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None),
        _ => RefileCommunication(id, payload),
    };

    private static (int? Status, string? Title, string? Detail, string? ReasonCode) ProblemOf(IResult result) =>
        (Status(result), (result as ProblemHttpResult)?.ProblemDetails.Title, Detail(result), ReasonCode(result));

    private static readonly JsonElement JsonNull = JsonSerializer.SerializeToElement<object?>(null);

    private static Dictionary<string, JsonElement> BodyOf(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    private Task<IResult> CreateChild(string table, Dictionary<string, object?> payload) =>
        ChildRecordEndpoints.CreateAsync(
            table, Payload(payload), _user, _world.Resolver(), _appOnly.Object, Restamper(), Shares(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

    private Task<IResult> RefileChild(string table, Guid id, Dictionary<string, object?> payload) =>
        ChildRecordEndpoints.RefileAsync(
            table, id, Payload(payload), _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

    /// <summary>PATCH /api/v1/events/{id}/filing's REAL handler (the route's filters are pinned through the real mapper by
    /// EventEndpointsAuthorizationContractTests).</summary>
    private Task<IResult> RefileEvent(Dictionary<string, object?> payload) =>
        Sprk.Bff.Api.Api.Events.EventEndpoints.RefileEventAsync(
            Event, Payload(payload), _user, _world.Resolver(), Restamper(), Children(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

    /// <summary>PATCH /api/communications/{id}/filing's handler: the ONE re-file core, filing columns only.</summary>
    private Task<IResult> RefileCommunication(Guid communication, Dictionary<string, object?> payload) =>
        ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", communication, Payload(payload), _user, _world.Resolver(), Restamper(), Children(),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None, filingOnly: true);

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
