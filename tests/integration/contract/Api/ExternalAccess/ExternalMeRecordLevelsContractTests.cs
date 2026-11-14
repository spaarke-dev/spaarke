using System.Net;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 008 (uac-r2 review 2026-11-13, conditions A, A1, A2, A3): <c>GET /api/v1/external/me</c>
/// lists the matters and work assignments the caller can READ as <c>records[{recordType, recordId, accessLevel}]</c>, beside the
/// unchanged <c>projects</c>.
/// </summary>
/// <remarks>
/// <para>Driven through the REAL route, filter and handler on <see cref="ExternalTodoScopeTestFixture"/>. The principal is stubbed at
/// <see cref="ICallerPrincipalResolver"/> (the seam that exists for it); the caller's Dataverse access probe and the data service are
/// recording doubles, so "no new read" is asserted, not assumed.</para>
/// <para><b>Parity (A1).</b> Every emitted level must equal <see cref="ExternalAccessLevels.ToDisplayLevel"/> of the rights the read
/// routes decide on (<c>CallerPrincipal.RightsOn</c>, the dispatch <c>RightsForRoot</c> mirrors), and each emitted record must be one
/// the record's own read route admits (200), while a None key and a vetoed key are neither listed nor admitted (403). Placement:
/// <c>tests/integration/contract/**</c> (ADR-038, endpoint contract).</para>
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalMeRecordLevelsContractTests : IClassFixture<ExternalTodoScopeTestFixture>
{
    private static readonly Guid ContactId = Guid.Parse("a0080000-0000-4000-8000-0000000000c1");
    private static readonly Guid SystemUserId = Guid.Parse("a0080000-0000-4000-8000-000000000051");

    // One record per (kind, level), plus the keys that must never be emitted.
    private static readonly Guid ProjectView = Guid.Parse("a0080000-0000-4000-8000-000000000101");
    private static readonly Guid ProjectCollaborate = Guid.Parse("a0080000-0000-4000-8000-000000000102");
    private static readonly Guid ProjectFull = Guid.Parse("a0080000-0000-4000-8000-000000000103");
    private static readonly Guid MatterView = Guid.Parse("a0080000-0000-4000-8000-000000000201");
    private static readonly Guid MatterCollaborate = Guid.Parse("a0080000-0000-4000-8000-000000000202");
    private static readonly Guid MatterFull = Guid.Parse("a0080000-0000-4000-8000-000000000203");
    private static readonly Guid MatterOffGrid = Guid.Parse("a0080000-0000-4000-8000-000000000204");
    private static readonly Guid WorkAssignmentView = Guid.Parse("a0080000-0000-4000-8000-000000000301");
    private static readonly Guid WorkAssignmentCollaborate = Guid.Parse("a0080000-0000-4000-8000-000000000302");
    private static readonly Guid WorkAssignmentFull = Guid.Parse("a0080000-0000-4000-8000-000000000303");

    /// <summary>A grant row with no level: the key is in the map with None (task 136's first live path).</summary>
    private static readonly Guid MatterNoneKey = Guid.Parse("a0080000-0000-4000-8000-0000000002ee");
    private static readonly Guid WorkAssignmentNoneKey = Guid.Parse("a0080000-0000-4000-8000-0000000003ee");

    /// <summary>
    /// A vetoed record: a Read veto (deny list / Restricted) removes the key in composition; a construction path that forgot to
    /// prune would leave the other rights without Read. Both views (/me and the read routes) must refuse it alike.
    /// </summary>
    private static readonly Guid MatterVetoedKey = Guid.Parse("a0080000-0000-4000-8000-0000000002dd");
    private static readonly Guid WorkAssignmentVetoedKey = Guid.Parse("a0080000-0000-4000-8000-0000000003dd");

    private const AccessRights ReadVetoed = AccessRights.Write | AccessRights.Create | AccessRights.Delete;

    private readonly ExternalTodoScopeTestFixture _fixture;

    public ExternalMeRecordLevelsContractTests(ExternalTodoScopeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static CallerPrincipal EveryLevelPrincipal(bool dataverseDecides = false) => new()
    {
        Plane = dataverseDecides ? CallerPrincipalPlane.Workforce : CallerPrincipalPlane.CiamContact,
        ContactId = ContactId,
        SystemUserId = dataverseDecides ? SystemUserId : null,
        Email = "caller@outside.test",
        ProjectAccess = new[]
        {
            CallerProjectAccess.FromLevel(ProjectView, ExternalAccessLevel.ViewOnly),
            CallerProjectAccess.FromLevel(ProjectCollaborate, ExternalAccessLevel.Collaborate),
            CallerProjectAccess.FromLevel(ProjectFull, ExternalAccessLevel.FullAccess),
        },
        MatterAccess = new Dictionary<Guid, AccessRights>
        {
            [MatterView] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly),
            [MatterCollaborate] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
            [MatterFull] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.FullAccess),
            // Off the three level points: the display projection under-states it (Read|Write without Create shows ViewOnly).
            [MatterOffGrid] = AccessRights.Read | AccessRights.Write,
            [MatterNoneKey] = AccessRights.None,
            [MatterVetoedKey] = ReadVetoed,
        },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>
        {
            [WorkAssignmentView] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly),
            [WorkAssignmentCollaborate] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
            [WorkAssignmentFull] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.FullAccess),
            [WorkAssignmentNoneKey] = AccessRights.None,
            [WorkAssignmentVetoedKey] = ReadVetoed,
        },
        DataverseConfirmsWrites = dataverseDecides,
    };

    private async Task<(string Raw, JsonElement Root)> GetMeAsync()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync("/api/v1/external/me");
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        using var doc = JsonDocument.Parse(raw);
        return (raw, doc.RootElement.Clone());
    }

    private static List<(string RecordType, Guid RecordId, string AccessLevel)> RecordsOf(JsonElement root) =>
        root.GetProperty("records").EnumerateArray()
            .Select(r => (r.GetProperty("recordType").GetString()!, r.GetProperty("recordId").GetGuid(), r.GetProperty("accessLevel").GetString()!))
            .ToList();

    private static ExternalGrantRootType RootTypeOf(string wire) => wire switch
    {
        "matter" => ExternalGrantRootType.Matter,
        "workassignment" => ExternalGrantRootType.WorkAssignment,
        _ => throw new InvalidOperationException($"Unexpected recordType '{wire}'."),
    };

    private static string SegmentOf(ExternalGrantRootType type) => type switch
    {
        ExternalGrantRootType.Project => "projects",
        ExternalGrantRootType.Matter => "matters",
        _ => "workassignments",
    };

    // ── A: the matters and work assignments the caller can read, each with its level ──────────────────────────────────

    [Fact]
    public async Task Me_ForACallerHoldingMattersAndWorkAssignments_ListsEachReadableRecordWithItsDisplayLevel()
    {
        _fixture.Principal = EveryLevelPrincipal();

        var (_, root) = await GetMeAsync();

        RecordsOf(root).Should().BeEquivalentTo(new[]
        {
            ("matter", MatterView, "ViewOnly"),
            ("matter", MatterCollaborate, "Collaborate"),
            ("matter", MatterFull, "FullAccess"),
            ("matter", MatterOffGrid, "ViewOnly"),
            ("workassignment", WorkAssignmentView, "ViewOnly"),
            ("workassignment", WorkAssignmentCollaborate, "Collaborate"),
            ("workassignment", WorkAssignmentFull, "FullAccess"),
        });
    }

    [Fact]
    public async Task Me_RecordEntries_CarryOnlyTypeIdAndLevel()
    {
        _fixture.Principal = EveryLevelPrincipal();

        var (_, root) = await GetMeAsync();

        foreach (var entry in root.GetProperty("records").EnumerateArray())
            entry.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("recordType", "recordId", "accessLevel");
    }

    [Fact]
    public async Task Me_ForACallerWithNoMatterOrWorkAssignment_ReturnsAnEmptyRecordsList()
    {
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = ContactId,
            Email = "caller@outside.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(ProjectView, ExternalAccessLevel.ViewOnly) },
        };

        var (_, root) = await GetMeAsync();

        root.GetProperty("records").GetArrayLength().Should().Be(0, "no matter or work assignment is readable — an empty list, not a guess");
    }

    // ── A1: parity with what the read routes decide; a None key and a vetoed key never appear ────────────────────────────

    [Fact]
    public async Task Me_EveryEmittedLevel_EqualsTheDisplayLevelOfTheRightsTheReadRoutesDecideOn()
    {
        var principal = EveryLevelPrincipal();
        _fixture.Principal = principal;

        var (_, root) = await GetMeAsync();

        // Records: the label is ToDisplayLevel(RightsForRoot(kind, id)) for every entry.
        var records = RecordsOf(root);
        records.Should().NotBeEmpty();
        foreach (var (recordType, recordId, accessLevel) in records)
        {
            var expected = ExternalAccessLevels.ToDisplayLevel(principal.RightsOn(RootTypeOf(recordType), recordId));
            expected.Should().NotBeNull($"{recordType} {recordId} was emitted, so the routes must admit it");
            accessLevel.Should().Be(expected!.Value.ToString(), $"{recordType} {recordId}");
        }

        // Projects: unchanged, and on the same projection (A3: additive only).
        foreach (var project in root.GetProperty("projects").EnumerateArray())
        {
            var projectId = project.GetProperty("projectId").GetGuid();
            project.GetProperty("accessLevel").GetString().Should().Be(
                ExternalAccessLevels.ToDisplayLevel(principal.RightsOn(ExternalGrantRootType.Project, projectId))!.Value.ToString());
        }

        root.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("projectId").GetGuid())
            .Should().BeEquivalentTo(new[] { ProjectView, ProjectCollaborate, ProjectFull });
    }

    [Fact]
    public async Task Me_EveryEmittedRecord_IsOneItsReadRouteAdmits()
    {
        _fixture.Principal = EveryLevelPrincipal();

        var (_, root) = await GetMeAsync();

        using var client = _fixture.CreateAuthenticatedClient();
        foreach (var (recordType, recordId, _) in RecordsOf(root))
        {
            using var read = await client.GetAsync($"/api/v1/external/{SegmentOf(RootTypeOf(recordType))}/{recordId}/events");
            read.StatusCode.Should().Be(HttpStatusCode.OK, $"/me labelled {recordType} {recordId}, so its read route must admit it");
        }
    }

    [Theory]
    [InlineData("matters", "a0080000-0000-4000-8000-0000000002ee")]
    [InlineData("workassignments", "a0080000-0000-4000-8000-0000000003ee")]
    [InlineData("matters", "a0080000-0000-4000-8000-0000000002dd")]
    [InlineData("workassignments", "a0080000-0000-4000-8000-0000000003dd")]
    public async Task Me_ANoneKeyOrAVetoedKey_AppearsNowhereInTheBody_AndItsReadRouteRefusesIt(string segment, string id)
    {
        _fixture.Principal = EveryLevelPrincipal();

        var (raw, _) = await GetMeAsync();

        raw.Should().NotContainEquivalentOf(id, "a record the caller cannot read is never disclosed, not even its id");

        using var client = _fixture.CreateAuthenticatedClient();
        using var read = await client.GetAsync($"/api/v1/external/{segment}/{id}/events");
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the read route refuses what /me does not list");
    }

    // ── A2: a caller whose access Dataverse decides gets the composed READ view, as for projects ──────────────────────────

    [Fact]
    public async Task Me_ForADataverseDecidedCaller_ListsTheComposedReadView_AndFlagsWriteAccessPerRecord()
    {
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = ContactId,
            SystemUserId = SystemUserId,
            Email = "colleague@spaarke.test",
            // The impersonated set confers Read only (task 036): projects and records alike read as ViewOnly.
            ProjectAccess = new[] { new CallerProjectAccess { ProjectId = ProjectView, Rights = AccessRights.Read } },
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterView] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [WorkAssignmentView] = AccessRights.Read },
            DataverseConfirmsWrites = true,
        };

        var (_, root) = await GetMeAsync();

        root.GetProperty("writeAccessPerRecord").GetBoolean().Should().BeTrue();
        root.GetProperty("projects")[0].GetProperty("accessLevel").GetString().Should().Be("ViewOnly");
        RecordsOf(root).Should().BeEquivalentTo(new[]
        {
            ("matter", MatterView, "ViewOnly"),
            ("workassignment", WorkAssignmentView, "ViewOnly"),
        }, "the label is the composed READ view; write affordances come from each list's canCreate");

        // A3: nothing was asked of Dataverse to build the labels — not the caller's rights, not a privilege.
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty();
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty();
        _fixture.Probe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Me_ForAContactCaller_DoesNotFlagWriteAccessPerRecord()
    {
        _fixture.Principal = EveryLevelPrincipal();

        var (_, root) = await GetMeAsync();

        root.TryGetProperty("writeAccessPerRecord", out _).Should().BeFalse("omitted for every caller Dataverse does not decide");
    }

    // ── A3: additive; no new read ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_BuildingTheRecordLevels_ReadsNothing()
    {
        _fixture.Principal = EveryLevelPrincipal(dataverseDecides: true);

        await GetMeAsync();

        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("the levels come from the principal alone");
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty();
        _fixture.Probe.Calls.Should().BeEmpty();
        _fixture.Data.ListCallCount.Should().Be(0);
    }
}
