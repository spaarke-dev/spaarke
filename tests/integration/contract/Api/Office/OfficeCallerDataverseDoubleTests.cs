using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Tests.Auth.Office;
using Sprk.Bff.Api.Tests.Regression;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 018b-1b: the Office quick-create host's caller-Dataverse double answers each question
/// <c>RecordCreationService</c> asks as the caller with the fixture world's default, honours a per-test override, throws on a
/// question it was not scripted for, and passes the #2188 mask read to <see cref="MappingSourceCallerReadDouble"/> unchanged.
/// The write capture views the step-11 body as the old SDK entity.
/// </summary>
public class OfficeCallerDataverseDoubleTests
{
    private static readonly Guid Caller = OfficeQuickCreateWorld.ContractCaller;

    private readonly OfficeCallerDataverseDouble _world =
        new(new MappingSourceCallerReadDouble(_ => null), () => Caller);

    // ── Defaults, one per scripted question ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhoAmI_ByDefault_NamesTheSystemUserTheHostsResolverResolved()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(OfficeQuickCreateWorld.FieldsCaller.ToString("D")));
        await factory.CallerResolver.Object.ResolveAsync(new ClaimsPrincipal(), CancellationToken.None); // the endpoint's resolve

        var answer = await factory.CallerDataverse.GetAsync("WhoAmI()", CancellationToken.None);

        answer.IsSuccess.Should().BeTrue();
        answer.Body!.Value.GetProperty("UserId").GetGuid().Should().Be(OfficeQuickCreateWorld.FieldsCaller);
    }

    [Fact]
    public async Task TableMetadata_ByDefault_DeclaresOwnershipPrimaryIdAndSchemaNamedPrivileges()
    {
        var matter = await Body("EntityDefinitions(LogicalName='sprk_matter')?$select=LogicalName,Privileges");
        var reference = await Body(
            "EntityDefinitions(LogicalName='sprk_projecttype_ref')?$select=LogicalName,OwnershipType,PrimaryIdAttribute,Privileges");
        var user = await Body("EntityDefinitions(LogicalName='systemuser')?$select=LogicalName,OwnershipType,PrimaryIdAttribute,Privileges");

        matter.GetProperty("OwnershipType").GetString().Should().Be("UserOwned");
        PrivilegeNames(matter).Should().Contain(["prvCreatesprk_Matter", "prvAppendsprk_Matter", "prvAppendTosprk_Matter"]);
        reference.GetProperty("OwnershipType").GetString().Should().Be("OrganizationOwned");
        reference.GetProperty("PrimaryIdAttribute").GetString().Should().Be("sprk_projecttype_refid");
        PrivilegeNames(reference).Should().Contain("prvAppendTosprk_ProjectType_Ref");
        user.GetProperty("OwnershipType").GetString().Should().Be("BusinessOwned");
        PrivilegeNames(user).Should().Contain("prvAppendToUser");
    }

    [Fact]
    public async Task Privileges_ByDefault_HoldCreateAppendAndEveryReferenceAppendTo_AnchoredAtTheCallersBusinessUnit()
    {
        var asked = new[] { "prvCreatesprk_Project", "prvAppendsprk_Project", "prvAppendTosprk_MatterType_Ref", "prvDeletesprk_Matter" };

        var body = await Body(PrivilegesPath(Caller, asked));

        var grants = body.GetProperty("RolePrivileges").EnumerateArray().ToList();
        grants.Select(g => g.GetProperty("PrivilegeName").GetString()).Should()
            .Equal("prvCreatesprk_Project", "prvAppendsprk_Project", "prvAppendTosprk_MatterType_Ref");
        grants.Should().OnlyContain(g => g.GetProperty("BusinessUnitId").GetGuid() == OfficeQuickCreateWorld.ContractBusinessUnit);
    }

    [Fact]
    public async Task ColumnSecurity_ByDefault_NoDeclaredColumnIsSecured_AndAnUndeclaredOneIsNotAnswered()
    {
        var body = await Body(AttributesPath("sprk_project", "IsSecured", "sprk_projectname", "sprk_projecttype_ref", "sprk_nosuchcolumn"));

        body.GetProperty("value").EnumerateArray()
            .Select(a => (a.GetProperty("LogicalName").GetString(), a.GetProperty("IsSecured").GetBoolean()))
            .Should().BeEquivalentTo([("sprk_projectname", false), ("sprk_projecttype_ref", false)]);
    }

    [Fact]
    public async Task PrincipalAccess_ByDefault_GrantsAppendToOnAWorldRow_AndRefusesAnOrganizationOwnedTable()
    {
        var contact = await _world.GetAsync(AccessPath(Caller, "contacts", OfficeQuickCreateWorld.ChosenContact), CancellationToken.None);
        var reference = await _world.GetAsync(
            AccessPath(Caller, "sprk_mattertype_refs", OfficeQuickCreateWorld.ContractMatterType), CancellationToken.None);

        contact.Body!.Value.GetProperty("AccessRights").GetString().Should().Contain("AppendToAccess");
        reference.StatusCode.Should().Be(400, "live Dataverse refuses RetrievePrincipalAccess on an organization-owned table");
    }

    [Fact]
    public async Task ReferenceRowRead_ByDefault_FindsEveryFixtureReference_AsTheCaller()
    {
        foreach (var (set, id) in new[]
                 {
                     ("sprk_mattertype_refs", OfficeQuickCreateWorld.ContractMatterType),
                     ("sprk_mattertype_refs", OfficeQuickCreateWorld.FieldsMatterType),
                     ("sprk_practicearea_refs", OfficeQuickCreateWorld.PracticeArea),
                     ("sprk_projecttype_refs", OfficeQuickCreateWorld.ProjectType),
                 })
        {
            var primaryId = set[..^1] + "id";
            var body = await Body($"{set}({id:D})?$select={primaryId}");
            body.GetProperty(primaryId).GetGuid().Should().Be(id);
        }
    }

    [Fact]
    public async Task RecordTypeCatalog_ByDefault_NamesTheMatterAndProjectTables()
    {
        var matter = await Body($"sprk_recordtype_refs({RecordTypeCatalogSetup.MatterTypeId:D})?$select=sprk_recordlogicalname");
        var project = await Body($"sprk_recordtype_refs({RecordTypeCatalogSetup.ProjectTypeId:D})?$select=sprk_recordlogicalname");

        matter.GetProperty("sprk_recordlogicalname").GetString().Should().Be("sprk_matter");
        project.GetProperty("sprk_recordlogicalname").GetString().Should().Be("sprk_project");
    }

    [Fact]
    public async Task BusinessOwnedRowRead_ByDefault_ReportsTheUsersAndTeamsBusinessUnit()
    {
        var user = await Body($"systemusers({Caller:D})?$select=_businessunitid_value");
        var team = await Body($"teams({OfficeQuickCreateWorld.DefaultTeam:D})?$select=_businessunitid_value");

        user.GetProperty("_businessunitid_value").GetGuid().Should().Be(OfficeQuickCreateWorld.ContractBusinessUnit);
        team.GetProperty("_businessunitid_value").GetGuid().Should().Be(OfficeQuickCreateWorld.ContractBusinessUnit);
    }

    [Fact]
    public async Task BusinessUnitHierarchy_ByDefault_EachUnitIsItsOwnRoot()
    {
        var self = await Body(UnderPath(OfficeQuickCreateWorld.ContractBusinessUnit, OfficeQuickCreateWorld.ContractBusinessUnit));
        var other = await Body(UnderPath(OfficeQuickCreateWorld.ContractBusinessUnit, OfficeQuickCreateWorld.FieldsBusinessUnit));

        self.GetProperty("value").GetArrayLength().Should().Be(1);
        other.GetProperty("value").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task NavigationProperties_ByDefault_IncludeTheProjectTypeAndTheMappedClient()
    {
        var project = await Body(RelationshipsPath("sprk_project"));
        var matter = await Body(RelationshipsPath("sprk_matter"));

        Relationships(project).Should().Contain(("sprk_projecttype_ref", "sprk_ProjectType_Ref", "sprk_projecttype_ref"))
            .And.Contain(("sprk_regardingrecordtype", "sprk_RegardingRecordType", "sprk_recordtype_ref"));
        Relationships(matter).Should().Contain(("sprk_client", "sprk_Client", "account"))
            .And.Contain(("sprk_mattertype", "sprk_MatterType", "sprk_mattertype_ref"));
    }

    [Fact]
    public async Task EntitySetName_ByDefault_IsTheLiveEntitySet()
    {
        var body = await Body("EntityDefinitions(LogicalName='sprk_projecttype_ref')?$select=EntitySetName");

        body.GetProperty("EntitySetName").GetString().Should().Be("sprk_projecttype_refs");
    }

    [Fact]
    public async Task TargetColumns_ByDefault_AnswerTypeAndCreateValidity_ForDeclaredColumnsOnly()
    {
        var body = await Body(AttributesPath("sprk_matter", "AttributeType,IsValidForCreate", "sprk_client", "sprk_billingstatus", "sprk_notonsource"));

        body.GetProperty("value").EnumerateArray()
            .Select(a => (a.GetProperty("LogicalName").GetString(), a.GetProperty("AttributeType").GetString(), a.GetProperty("IsValidForCreate").GetBoolean()))
            .Should().BeEquivalentTo([("sprk_client", "Lookup", true), ("sprk_billingstatus", "Picklist", true)]);
    }

    [Fact]
    public async Task DateBehaviours_ByDefault_AreNone()
    {
        var body = await Body("EntityDefinitions(LogicalName='sprk_matter')/Attributes/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            + "?$select=LogicalName,DateTimeBehavior&$filter=" + Uri.EscapeDataString("LogicalName eq 'sprk_duedate'"));

        body.GetProperty("value").GetArrayLength().Should().Be(0);
    }

    // ── Overrides ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overrides_AreHonoured_DenyAppendTo_SecureAColumn_DropAPrivilege_MissOrHideARow_FaultOrAnswerARead()
    {
        _world.NoAppendTo.Add(("contact", OfficeQuickCreateWorld.ChosenContact));
        _world.SecuredColumns.Add(("sprk_matter", "sprk_matterdescription"));
        _world.Held.Remove("prvAppendTosprk_PracticeArea_Ref");
        _world.Missing.Add(("sprk_mattertype_ref", OfficeQuickCreateWorld.ContractMatterType));
        _world.Unreadable.Add(("sprk_practicearea_ref", OfficeQuickCreateWorld.PracticeArea));
        var projectTypeRead = $"sprk_projecttype_refs({OfficeQuickCreateWorld.ProjectType:D})?$select=sprk_projecttype_refid";
        _world.Respond = path => path == projectTypeRead
            ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "Scripted 503.")
            : null;
        _world.Fault = path => path == "WhoAmI()" ? new TimeoutException("scripted timeout") : null;

        (await Body(AccessPath(Caller, "contacts", OfficeQuickCreateWorld.ChosenContact)))
            .GetProperty("AccessRights").GetString().Should().Be("ReadAccess,WriteAccess");
        (await Body(AttributesPath("sprk_matter", "IsSecured", "sprk_matterdescription"))).GetProperty("value")[0]
            .GetProperty("IsSecured").GetBoolean().Should().BeTrue();
        (await Body(PrivilegesPath(Caller, ["prvAppendTosprk_PracticeArea_Ref"]))).GetProperty("RolePrivileges").GetArrayLength()
            .Should().Be(0);
        (await Get($"sprk_mattertype_refs({OfficeQuickCreateWorld.ContractMatterType:D})?$select=sprk_mattertype_refid")).StatusCode
            .Should().Be(404);
        (await Get($"sprk_practicearea_refs({OfficeQuickCreateWorld.PracticeArea:D})?$select=sprk_practicearea_refid")).StatusCode
            .Should().Be(403);
        (await Get(projectTypeRead)).StatusCode.Should().Be(503);
        await FluentActions.Awaiting(() => Get("WhoAmI()")).Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Overrides_WhoAmIFailureAndMe_AreHonoured()
    {
        _world.Me = OfficeQuickCreateWorld.Issue1079Caller;
        (await Body("WhoAmI()")).GetProperty("UserId").GetGuid().Should().Be(OfficeQuickCreateWorld.Issue1079Caller);

        _world.WhoAmIFailure = DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired, "No token.");
        (await Get("WhoAmI()")).ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    // ── Unknown input fails loudly ───────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> UnscriptedReads() => new()
    {
        "EntityDefinitions(LogicalName='sprk_unknowntable')?$select=EntitySetName",
        "EntityDefinitions(LogicalName='sprk_matter')?$select=LogicalName,SchemaName",
        $"sprk_unknowns({Guid.NewGuid():D})?$select=sprk_unknownid",
        $"contacts({Guid.NewGuid():D})?$select=contactid",
        AccessPath(Caller, "contacts", Guid.NewGuid()),
        PrivilegesPath(OfficeQuickCreateWorld.FieldsCaller, ["prvCreatesprk_Matter"]),
        "sprk_matters?$filter=sprk_mattername eq 'x'",
    };

    [Theory]
    [MemberData(nameof(UnscriptedReads))]
    public async Task AnUnscriptedQuestion_Throws_NamingTheRequest(string path)
    {
        var thrown = await FluentActions.Awaiting(() => Get(path)).Should().ThrowAsync<UnscriptedCallerRequestException>();

        thrown.Which.Request.Should().Be($"GET {Uri.UnescapeDataString(path)}");
    }

    [Fact]
    public async Task WhoAmI_WithNoCallerResolved_Throws()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();

        await FluentActions.Awaiting(() => factory.CallerDataverse.GetAsync("WhoAmI()", CancellationToken.None))
            .Should().ThrowAsync<UnscriptedCallerRequestException>().WithMessage("*WhoAmI()*");
    }

    [Fact]
    public async Task AWriteAsTheCaller_Throws_NamingTheRequest()
    {
        await FluentActions.Awaiting(() => _world.PostAsync("/api/data/v9.2/sprk_matters", "{}", CancellationToken.None))
            .Should().ThrowAsync<UnscriptedCallerRequestException>().WithMessage("*POST /api/data/v9.2/sprk_matters*");
        await FluentActions.Awaiting(() => _world.PatchAsync($"sprk_matters({Guid.NewGuid():D})", "{}", CancellationToken.None))
            .Should().ThrowAsync<UnscriptedCallerRequestException>().WithMessage("*PATCH sprk_matters*");
        await FluentActions.Awaiting(() => _world.DeleteAsync($"sprk_matters({Guid.NewGuid():D})", CancellationToken.None))
            .Should().ThrowAsync<UnscriptedCallerRequestException>().WithMessage("*DELETE sprk_matters*");
    }

    // ── The mask read is MappingSourceCallerReadDouble's answer, byte for byte ───────────────────────────────────

    private static readonly Guid SourceId = OfficeQuickCreateWorld.ContractSourceProject;

    public static TheoryData<string> MaskCases() => new()
    {
        "row", "secured-column", "absent-property", "missing-row", "denied-403", "no-user-context-0", "server-500", "unexpected-shape",
    };

    [Theory]
    [MemberData(nameof(MaskCases))]
    public async Task TheMaskRead_IsAnsweredExactlyAsMappingSourceCallerReadDoubleAnswersIt(string @case)
    {
        var expected = MaskDouble(@case);
        var routed = MaskDouble(@case);
        var world = new OfficeCallerDataverseDouble(routed, () => Caller);
        var path = @case is "missing-row" or "unexpected-shape" ? $"sprk_projects({Guid.NewGuid():D})" : $"sprk_projects({SourceId:D})";
        if (@case == "unexpected-shape")
        {
            // A $select read is not the mask read: the mask double answers it 400; the world does not route it there.
            (await expected.GetAsync(path + "?$select=sprk_name", CancellationToken.None)).StatusCode.Should().Be(400);
            await FluentActions.Awaiting(() => world.GetAsync(path + "?$select=sprk_name", CancellationToken.None))
                .Should().ThrowAsync<UnscriptedCallerRequestException>();
            return;
        }

        var want = await expected.GetAsync(path, CancellationToken.None);
        var got = await world.GetAsync(path, CancellationToken.None);

        (got.StatusCode, got.IsSuccess, got.ErrorCode, got.ErrorMessage).Should()
            .Be((want.StatusCode, want.IsSuccess, want.ErrorCode, want.ErrorMessage));
        got.Body?.GetRawText().Should().Be(want.Body?.GetRawText());
        routed.Paths.Should().Equal(path);
        world.Requests.Should().BeEmpty("a mask read is recorded only where it always was");
    }

    [Fact]
    public async Task TheMaskRead_KeepsItsThrowSwitch()
    {
        var mask = MaskDouble("row");
        mask.Throw = new TaskCanceledException("scripted timeout");
        var world = new OfficeCallerDataverseDouble(mask, () => Caller);

        await FluentActions.Awaiting(() => world.GetAsync($"sprk_projects({SourceId:D})", CancellationToken.None))
            .Should().ThrowAsync<TaskCanceledException>();
    }

    private static MappingSourceCallerReadDouble MaskDouble(string @case)
    {
        var row = new Entity("sprk_project", SourceId)
        {
            ["sprk_clientreference"] = "ACME-REF",
            ["sprk_client"] = new EntityReference("account", OfficeQuickCreateWorld.ContractAccount),
            ["sprk_feearrangement"] = "TOP-SECRET",
            ["sprk_budget"] = 12.5m,
            ["sprk_isbillable"] = true,
        };
        var mask = new MappingSourceCallerReadDouble(id => id == SourceId ? row : null);
        switch (@case)
        {
            case "secured-column": mask.Secured.Add("sprk_feearrangement"); break;
            case "absent-property": mask.Absent.Add("_sprk_client_value"); break;
            case "denied-403": mask.FailWithStatus = 403; break;
            case "no-user-context-0": mask.FailWithStatus = 0; break;
            case "server-500": mask.FailWithStatus = 500; break;
        }

        return mask;
    }

    // ── The world matches the suites' fixtures (drift guard) ─────────────────────────────────────────────────────

    public static TheoryData<Type, string, Guid> SuiteFixtureIds() => new()
    {
        { typeof(OfficeQuickCreateContractTests), "OwnerId", OfficeQuickCreateWorld.ContractCaller },
        { typeof(OfficeQuickCreateContractTests), "BusinessUnitId", OfficeQuickCreateWorld.ContractBusinessUnit },
        { typeof(OfficeQuickCreateContractTests), "MatterTypeId", OfficeQuickCreateWorld.ContractMatterType },
        { typeof(OfficeQuickCreateContractTests), "SourceProjectId", OfficeQuickCreateWorld.ContractSourceProject },
        { typeof(OfficeQuickCreateContractTests), "AccountId", OfficeQuickCreateWorld.ContractAccount },
        { typeof(OfficeQuickCreateProjectContractTests), "OwnerId", OfficeQuickCreateWorld.ContractCaller },
        { typeof(OfficeQuickCreateProjectContractTests), "BusinessUnitId", OfficeQuickCreateWorld.ContractBusinessUnit },
        { typeof(OfficeQuickCreateProjectContractTests), "MatterTypeId", OfficeQuickCreateWorld.ContractMatterType },
        { typeof(OfficeQuickCreateProjectContractTests), "SourceProjectId", OfficeQuickCreateWorld.ContractSourceProject },
        { typeof(OfficeQuickCreateProjectContractTests), "AccountId", OfficeQuickCreateWorld.ContractAccount },
        { typeof(OfficeQuickCreateFieldsContractTests), "CallerId", OfficeQuickCreateWorld.FieldsCaller },
        { typeof(OfficeQuickCreateFieldsContractTests), "BusinessUnitId", OfficeQuickCreateWorld.FieldsBusinessUnit },
        { typeof(OfficeQuickCreateFieldsContractTests), "MatterTypeId", OfficeQuickCreateWorld.FieldsMatterType },
        { typeof(OfficeQuickCreateFieldsContractTests), "PracticeAreaId", OfficeQuickCreateWorld.PracticeArea },
        { typeof(OfficeQuickCreateFieldsContractTests), "ProjectTypeId", OfficeQuickCreateWorld.ProjectType },
        { typeof(OfficeQuickCreateFieldsContractTests), "ChosenContact", OfficeQuickCreateWorld.ChosenContact },
        { typeof(OfficeQuickCreateFieldsContractTests), "MakersContact", OfficeQuickCreateWorld.MakersContact },
        { typeof(OfficeQuickCreateSourceFieldSecurityTests), "OwnerId", OfficeQuickCreateWorld.FieldSecurityCaller },
        { typeof(OfficeQuickCreateSourceFieldSecurityTests), "MatterTypeId", OfficeQuickCreateWorld.FieldSecurityMatterType },
        { typeof(OfficeQuickCreateSourceFieldSecurityTests), "SourceProjectId", OfficeQuickCreateWorld.FieldSecuritySourceProject },
        { typeof(OfficeQuickCreateSourceFieldSecurityTests), "AccountId", OfficeQuickCreateWorld.FieldSecurityAccount },
        { typeof(Issue1079_InvoiceQuickCreateNameTests), "CallerSystemUserId", OfficeQuickCreateWorld.Issue1079Caller },
    };

    [Theory]
    [MemberData(nameof(SuiteFixtureIds))]
    public void TheWorldsId_IsTheSuitesFixtureId(Type suite, string field, Guid world)
    {
        var constant = suite.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{suite.Name}.{field} no longer exists");

        constant.GetValue(null).Should().Be(world, $"{suite.Name}.{field} is the row the caller's Dataverse holds");
    }

    // ── The write capture ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheWriteCapture_ViewsTheStepElevenBodyAsTheOldSdkEntity()
    {
        var fieldMappings = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        var capture = RecordCreationWriteCapture.Attach(fieldMappings, _world);
        var id = Guid.NewGuid();
        var body = new Dictionary<string, object?>
        {
            ["sprk_mattername"] = Json("\"Acme v. Widget\""),
            ["sprk_MatterType@odata.bind"] = Json($"\"/sprk_mattertype_refs({OfficeQuickCreateWorld.ContractMatterType:D})\""),
            ["sprk_PracticeArea@odata.bind"] = null,
            ["ownerid@odata.bind"] = $"/teams({OfficeQuickCreateWorld.DefaultTeam:D})",
            ["sprk_billingstatus"] = Json("2"),
            ["sprk_issecure"] = false,
        };

        await fieldMappings.Object.UpdateRecordFieldsAsync("sprk_matter", id, body, CancellationToken.None);

        var matter = capture.Entity!;
        matter.LogicalName.Should().Be("sprk_matter");
        matter.Id.Should().Be(id);
        matter.GetAttributeValue<string>("sprk_mattername").Should().Be("Acme v. Widget");
        matter.GetAttributeValue<EntityReference>("sprk_mattertype").Should()
            .BeEquivalentTo(new EntityReference("sprk_mattertype_ref", OfficeQuickCreateWorld.ContractMatterType));
        matter.Attributes.Should().ContainKey("sprk_practicearea").WhoseValue.Should().BeNull();
        matter.GetAttributeValue<EntityReference>("ownerid").Should()
            .BeEquivalentTo(new EntityReference("team", OfficeQuickCreateWorld.DefaultTeam));
        matter.GetAttributeValue<OptionSetValue>("sprk_billingstatus").Value.Should().Be(2);
        matter.GetAttributeValue<bool>("sprk_issecure").Should().BeFalse();
    }

    [Fact]
    public async Task TheWriteCapture_FaultsTheChosenAttempt_SoTheNumberRetryCanBeDriven()
    {
        var fieldMappings = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        var capture = RecordCreationWriteCapture.Attach(fieldMappings, _world);
        capture.FailAttempt = attempt => attempt == 1
            ? new HttpRequestException("412", null, HttpStatusCode.PreconditionFailed)
            : null;
        var body = new Dictionary<string, object?> { ["sprk_mattername"] = "Retried" };

        await FluentActions.Awaiting(() => fieldMappings.Object.UpdateRecordFieldsAsync("sprk_matter", Guid.NewGuid(), body, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
        await fieldMappings.Object.UpdateRecordFieldsAsync("sprk_matter", Guid.NewGuid(), body, CancellationToken.None);

        capture.Attempts.Should().HaveCount(2);
        capture.Writes.Should().ContainSingle();
        capture.Entity!.GetAttributeValue<string>("sprk_mattername").Should().Be("Retried");
    }

    [Fact]
    public void TheWriteCapture_ThrowsOnABindTheWorldDoesNotKnow()
    {
        var capture = RecordCreationWriteCapture.Attach(new Mock<IFieldMappingDataverseService>(), _world);

        FluentActions.Invoking(() => capture.ToEntity("sprk_matter", Guid.NewGuid(),
                new Dictionary<string, object?> { ["sprk_Unknown@odata.bind"] = "/contacts(00000000-0000-0000-0000-000000000001)" }))
            .Should().Throw<UnscriptedCallerRequestException>().WithMessage("*sprk_Unknown*");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<DataverseUserResponse> Get(string path) => _world.GetAsync(path, CancellationToken.None);

    private async Task<JsonElement> Body(string path)
    {
        var answer = await Get(path);
        answer.IsSuccess.Should().BeTrue($"{path} → {answer.StatusCode} {answer.ErrorMessage}");
        return answer.Body!.Value;
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static IEnumerable<string?> PrivilegeNames(JsonElement metadata) =>
        metadata.GetProperty("Privileges").EnumerateArray().Select(p => p.GetProperty("Name").GetString());

    private static List<(string?, string?, string?)> Relationships(JsonElement metadata) =>
        metadata.GetProperty("ManyToOneRelationships").EnumerateArray()
            .Select(r => (r.GetProperty("ReferencingAttribute").GetString(),
                r.GetProperty("ReferencingEntityNavigationPropertyName").GetString(),
                r.GetProperty("ReferencedEntity").GetString()))
            .ToList();

    // The request shapes as production builds them (CallerWriteAuthorization, DataverseWriteItemMapper, the service's mapping).
    private static string AccessPath(Guid me, string set, Guid id) =>
        $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1="
        + Uri.EscapeDataString($"{{\"@odata.id\":\"{set}({id:D})\"}}");

    private static string PrivilegesPath(Guid me, string[] names) =>
        $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1="
        + Uri.EscapeDataString(JsonSerializer.Serialize(names));

    private static string AttributesPath(string table, string select, params string[] columns) =>
        $"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,{select}&$filter="
        + Uri.EscapeDataString(string.Join(" or ", columns.Select(c => $"LogicalName eq '{c}'")));

    private static string RelationshipsPath(string table) =>
        $"EntityDefinitions(LogicalName='{table}')?$select=LogicalName"
        + "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity)";

    private static string UnderPath(Guid candidate, Guid ancestor) =>
        "businessunits?$select=businessunitid&$filter=" + Uri.EscapeDataString(
            $"businessunitid eq {candidate:D} and Microsoft.Dynamics.CRM.UnderOrEqual(PropertyName='businessunitid',PropertyValue='{ancestor:D}')");
}
