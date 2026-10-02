using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r2 (verifier items 2 and 6) — the chat tools that write Dataverse AS THE USER, driven
/// through their REAL handlers and the REAL <see cref="RecordOwnershipResolver"/> over <see cref="Directory"/>:
/// <list type="bullet">
/// <item><c>dataverse.create_record</c> and <c>email.draft</c> (owner decision S1 option (1), G5 refined): a CHILD row
/// filed under a record is checked AS THE CALLER (Create/Append, AppendTo on every record named, no owner or field-secured
/// column), then created by the APPLICATION owned by the team the resolver names — the named Secure team for a secure
/// record — never by the caller; refusals create nothing.</item>
/// <item><c>dataverse.update_record</c>: a re-file of a child re-derives its owner (<c>ReparentAsync</c>): the caller's own
/// PATCH runs only after the owner is decided, then the owner is assigned and read back.</item>
/// </list>
/// The caller's Dataverse is a scripted <see cref="IDataverseUserClient"/> (the module boundary these handlers are tested at,
/// ADR-038); the app-only create seam records what it is asked to write.
/// </summary>
[Trait("status", "new")]
public sealed partial class SecureChildOwnershipAiToolTests : TypedToolHandlerTestFixture
{
    private static readonly Guid SecureMatter = Guid.Parse("a2460000-0000-4000-8000-000000000001");
    private static readonly Guid OrdinaryMatter = Guid.Parse("a2460000-0000-4000-8000-000000000002");
    private static readonly Guid FlaggedProject = Guid.Parse("a2460000-0000-4000-8000-000000000003");
    private static readonly Guid OrdinaryDocument = Guid.Parse("a2460000-0000-4000-8000-000000000004");
    private static readonly Guid Caller = Guid.Parse("a2460000-0000-4000-8000-0000000000aa");

    private readonly Directory _world = Directory.Standard()
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithRecord("sprk_project", FlaggedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam)
        .WithRecord("sprk_document", OrdinaryDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam);

    private readonly ScriptedUserClient _user = new(Caller);
    private readonly List<(string Table, Guid Id, Dictionary<string, object?> Fields)> _appCreates = new();
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);

    public SecureChildOwnershipAiToolTests()
    {
        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, id, f, _, _) => _appCreates.Add((t, id, f)))
            .Returns(Task.CompletedTask);
    }

    // =====================================================================================
    // dataverse.create_record — S1 / G5
    // =====================================================================================

    [Fact]
    public async Task CreateRecord_AToDoFiledToASecureMatter_IsCreatedByTheAppOwnedByTheNamedTeam_NeverAsTheUser()
    {
        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty("the row is never created owned by the caller");
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_todo");
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        Owner(fields).Should().NotBe(Directory.SecureDefaultTeam);
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})");
        CreatedRecordId(result).Should().Be(id);
    }

    [Fact]
    public async Task CreateRecord_AToDoFiledToAnOrdinaryMatter_IsOwnedByThatMattersBusinessUnitTeam()
    {
        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam);
    }

    [Fact]
    public async Task CreateRecord_FiledToAFlaggedButNotIsolatedProject_IsRefusedWithTheStableCode_AndCreatesNothing()
    {
        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingproject", "sprk_project", FlaggedProject));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_WhenTheCallerLacksCreateOnTheTable_IsDeniedAsTheUser_AndCreatesNothing()
    {
        // G5: the app creates only what the caller could have created themselves.
        _user.Held.Remove("prvCreatesprk_todo");

        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_WhenTheCallerLacksAppendToOnTheMatter_IsDenied_BeforeTheOwnerIsDecided()
    {
        _user.NoAppendTo.Add(FlaggedProject);

        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingproject", "sprk_project", FlaggedProject));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "a caller who may not file under the record learns nothing about it — not even that it is secure");
        result.ErrorMessage.Should().NotContain(RecordOwnerRefusal.SecureParentNotIsolated);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_SettingTheOwnerColumn_IsRefused_AndCreatesNothing()
    {
        var result = await CreateRecord("sprk_todo",
            Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter),
            ("ownerid", JsonSerializer.SerializeToElement(new { relatedTable = "systemuser", recordId = Caller })));

        result.Success.Should().BeFalse();
        _appCreates.Should().BeEmpty("the server owns the owner on the app-only path");
    }

    [Fact]
    public async Task CreateRecord_SettingAFieldSecuredColumn_IsRefused_AndCreatesNothing()
    {
        _user.SecuredColumns.Add("sprk_notes");

        var result = await CreateRecord("sprk_todo",
            Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter),
            ("sprk_notes", JsonSerializer.SerializeToElement("privileged")));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _appCreates.Should().BeEmpty("an app-only write would pass the caller's column security");
    }

    [Fact]
    public async Task CreateRecord_AnUnfiledToDo_IsStillCreatedAsTheUser()
    {
        var result = await CreateRecord("sprk_todo", ("sprk_name", JsonSerializer.SerializeToElement("Call back")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().ContainSingle();
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_ATableOutsideTheOwnershipSetFiledToASecureMatter_IsRefused_AndNothingIsCreated()
    {
        // A task activity regarding a secure matter cannot be re-owned here and must not sit in the caller's unit.
        var result = await CreateRecord("task", Lookup("regardingobjectid", "sprk_matter", SecureMatter));

        result.Success.Should().BeFalse();
        _user.Posts.Should().BeEmpty();
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_ATableOutsideTheOwnershipSetFiledToAnOrdinaryMatter_IsStillCreatedAsTheUser()
    {
        var result = await CreateRecord("task", Lookup("regardingobjectid", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateRecord_WhenResolvingTheOwnerFaults_FailsAsAnError_AndCreatesNothing()
    {
        var handler = CreateRecordHandler(_world.Resolver(fault: new TimeoutException("throttled")));

        var result = await handler.ExecuteChatAsync(
            CreateContext("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError, "a fault is the request's failure, never a refusal");
        _appCreates.Should().BeEmpty();
    }

    // =====================================================================================
    // email.draft — S1 / G5
    // =====================================================================================

    [Fact]
    public async Task EmailDraft_RegardingASecureMatter_IsCreatedByTheAppOwnedByTheNamedTeam_WithTheDrafterAsSender()
    {
        var handler = new EmailDraftToolHandler(
            _user, Mock.Of<IEmailDraftAi>(), CoreAncestorResolverFixtures.Inert(), CreateLogger<EmailDraftToolHandler>(),
            _world.Resolver(), _appOnly.Object);
        var args = JsonSerializer.Serialize(new
        {
            subject = "Settlement",
            body = "Draft for review.",
            to = new[] { "counsel@other.example" },
            regarding = new { table = "sprk_matter", recordId = SecureMatter, name = "Acme v Beta" },
        });

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: args) with { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(EmailDraftToolHandler)), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty();
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_communication").Subject.Fields;
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})");
        Bind(fields, "sprk_SentBy@odata.bind").Should().Be($"/systemusers({Caller:D})",
            "S1: Created By is the application, so the drafting user is recorded as the sender");
        ((JsonElement)fields["statuscode"]!).GetInt32().Should().Be(1, "still a DRAFT");
    }

    // =====================================================================================
    // dataverse.update_record — a re-file re-derives the owner (verifier item 2)
    // =====================================================================================

    [Fact]
    public async Task UpdateRecord_RefilingADocumentUnderASecureMatter_ReassignsItToTheNamedTeam_ReadBack()
    {
        var result = await UpdateRecord("sprk_document", OrdinaryDocument, Lookup("sprk_matter", "sprk_matter", SecureMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle("the caller's own PATCH is applied — Dataverse authorizes it");
        _world.Assignments.Should().ContainSingle().Which.Should().Be(("sprk_document", OrdinaryDocument, Directory.SecureNamedTeam));
        _world.Row("sprk_document", OrdinaryDocument).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task UpdateRecord_RefilingOntoAFlaggedButNotIsolatedProject_IsRefused_AndTheCallersPatchIsNeverSent()
    {
        var result = await UpdateRecord("sprk_document", OrdinaryDocument, Lookup("sprk_project", "sprk_project", FlaggedProject));

        result.ErrorCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_RefilingWithoutAppendToOnTheTarget_IsDenied_AndNothingIsWritten()
    {
        _user.NoAppendTo.Add(SecureMatter);

        var result = await UpdateRecord("sprk_document", OrdinaryDocument, Lookup("sprk_matter", "sprk_matter", SecureMatter));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_WhenDataverseRefusesTheCallersPatch_AssignsNoOwner()
    {
        _user.PatchStatus = 403;

        var result = await UpdateRecord("sprk_document", OrdinaryDocument, Lookup("sprk_matter", "sprk_matter", SecureMatter));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _world.Assignments.Should().BeEmpty("the owner follows only a change that was written");
    }

    [Fact]
    public async Task UpdateRecord_OfARowTheCallerCannotSee_FailsAsTheirOwnNotFound_BeforeAnyOwnerDecision()
    {
        _user.InvisibleRows.Add(OrdinaryDocument);

        var result = await UpdateRecord("sprk_document", OrdinaryDocument, Lookup("sprk_project", "sprk_project", FlaggedProject));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        result.ErrorMessage.Should().NotContain(RecordOwnerRefusal.SecureParentNotIsolated);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_ATableOutsideTheOwnershipSetMovedUnderASecureMatter_IsRefused_AndNotWritten()
    {
        var result = await UpdateRecord("task", Guid.NewGuid(), Lookup("regardingobjectid", "sprk_matter", SecureMatter));

        result.Success.Should().BeFalse();
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_AChangeThatFilesNothing_IsAnOrdinaryUpdateAsTheUser()
    {
        var result = await UpdateRecord("sprk_document", OrdinaryDocument, ("sprk_documentname", JsonSerializer.SerializeToElement("Renamed")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
        _world.Assignments.Should().BeEmpty();
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private DataverseCreateRecordHandler CreateRecordHandler(IRecordOwnershipResolver? resolver = null) =>
        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            resolver ?? _world.Resolver(), _appOnly.Object);

    private Task<ToolResult> CreateRecord(string table, params (string Column, JsonElement Value)[] item) =>
        CreateRecordHandler().ExecuteChatAsync(
            CreateContext(table, item), BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

    private static ChatInvocationContext CreateContext(string table, params (string Column, JsonElement Value)[] item) =>
        BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
        {
            tablename = table,
            item = item.ToDictionary(i => i.Column, i => i.Value),
        })) with { UserId = Guid.NewGuid().ToString() };

    private Task<ToolResult> UpdateRecord(string table, Guid id, params (string Column, JsonElement Value)[] item) =>
        new DataverseUpdateRecordHandler(_user, CreateLogger<DataverseUpdateRecordHandler>(), _world.Resolver())
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = table,
                    recordId = id,
                    item = item.ToDictionary(i => i.Column, i => i.Value),
                })) with { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)), CancellationToken.None);

    private static (string Column, JsonElement Value) Lookup(string column, string table, Guid id) =>
        (column, JsonSerializer.SerializeToElement(new { relatedTable = table, recordId = id }));

    private static Guid Owner(IReadOnlyDictionary<string, object?> fields) =>
        fields.TryGetValue("ownerid@odata.bind", out var bind) && bind is string path
        && Guid.TryParse(path["/teams(".Length..^1], out var team)
            ? team
            : Guid.Empty;

    private static string? Bind(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) && value is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString()
            : null;

    private static Guid? CreatedRecordId(ToolResult result) =>
        result.Metadata?.TryGetValue(ToolResultMetadataKeys.CreatedRecord, out var created) == true
        && created is ToolCreatedRecord record
            ? record.RecordId
            : null;

    /// <summary>
    /// The caller's Dataverse, scripted: table metadata and lookups for the handful of tables these tests use, the
    /// caller's held privileges, AppendTo per record, field-secured columns, rows the caller can see, and a record of
    /// every POST and PATCH made as the caller.
    /// </summary>
    private sealed partial class ScriptedUserClient(Guid me) : IDataverseUserClient
    {
        private static readonly Dictionary<string, string> EntitySets = new()
        {
            ["sprk_todo"] = "sprk_todos", ["sprk_matter"] = "sprk_matters", ["sprk_project"] = "sprk_projects",
            ["sprk_document"] = "sprk_documents", ["sprk_communication"] = "sprk_communications",
            ["task"] = "tasks", ["systemuser"] = "systemusers",
        };

        /// <summary>table → (lookup column, target table, navigation property).</summary>
        private static readonly Dictionary<string, (string Column, string Target, string Navigation)[]> Lookups = new()
        {
            ["sprk_todo"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_regardingproject", "sprk_project", "sprk_RegardingProject"),
                ("ownerid", "systemuser", "ownerid"),
            },
            ["sprk_communication"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_sentby", "systemuser", "sprk_SentBy"),
            },
            ["sprk_document"] = new[]
            {
                ("sprk_matter", "sprk_matter", "sprk_Matter"),
                ("sprk_project", "sprk_project", "sprk_Project"),
            },
            ["task"] = new[] { ("regardingobjectid", "sprk_matter", "regardingobjectid_sprk_matter") },
        };

        public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "prvCreatesprk_todo", "prvAppendsprk_todo", "prvCreatesprk_communication", "prvAppendsprk_communication",
            "prvCreateActivity", "prvAppendActivity",
        };

        public HashSet<Guid> NoAppendTo { get; } = new();
        public HashSet<string> SecuredColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<Guid> InvisibleRows { get; } = new();
        public int PatchStatus { get; set; } = 204;
        public List<(string Path, string Body)> Posts { get; } = new();
        public List<(string Path, string Body)> Patches { get; } = new();

        [GeneratedRegex(@"^EntityDefinitions\(LogicalName='(?<t>[a-z_]+)'\)(?<rest>.*)$")]
        private static partial Regex Definition();

        [GeneratedRegex(@"@odata\.id"":""(?<set>\w+)\((?<id>[0-9a-fA-F-]{36})\)")]
        private static partial Regex Target();

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Get(Uri.UnescapeDataString(relativePath)));

        private DataverseUserResponse Get(string path)
        {
            if (path == "WhoAmI()")
                return Ok(new { UserId = me });

            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
            {
                var id = Guid.Parse(Target().Match(path).Groups["id"].Value);
                return Ok(new { AccessRights = NoAppendTo.Contains(id) ? "ReadAccess,WriteAccess" : "ReadAccess,WriteAccess,AppendAccess,AppendToAccess" });
            }

            if (path.Contains("RetrieveUserSetOfPrivilegesByNames", StringComparison.Ordinal))
            {
                var asked = JsonSerializer.Deserialize<string[]>(path[(path.IndexOf("@p1=", StringComparison.Ordinal) + 4)..])!;
                return Ok(new { RolePrivileges = asked.Where(Held.Contains).Select(n => new { Depth = "Basic", PrivilegeName = n }) });
            }

            if (Definition().Match(path) is { Success: true } definition)
            {
                var table = definition.Groups["t"].Value;
                var rest = definition.Groups["rest"].Value;
                if (rest.StartsWith("/Attributes", StringComparison.Ordinal))
                    return Ok(new { value = SecuredColumns.Select(c => new { LogicalName = c, IsSecured = true }) });
                if (rest.Contains("Privileges", StringComparison.Ordinal))
                {
                    var schema = table is "task" ? "Activity" : table;
                    return Ok(new
                    {
                        LogicalName = table,
                        Privileges = new[]
                        {
                            new { Name = $"prvCreate{schema}", PrivilegeType = "Create" },
                            new { Name = $"prvAppend{schema}", PrivilegeType = "Append" },
                        },
                    });
                }

                if (rest.Contains("ManyToOneRelationships", StringComparison.Ordinal))
                {
                    return Ok(new
                    {
                        LogicalName = table,
                        ManyToOneRelationships = Lookups.GetValueOrDefault(table, Array.Empty<(string Column, string Target, string Navigation)>())
                            .Select(l => new
                            {
                                ReferencingAttribute = l.Column,
                                ReferencingEntityNavigationPropertyName = l.Navigation,
                                ReferencedEntity = l.Target,
                            }),
                    });
                }

                return Ok(new { EntitySetName = EntitySets[table], PrimaryIdAttribute = table == "task" ? "activityid" : table + "id" });
            }

            // A row read as the caller: "{set}({id})?$select=…".
            var rowId = Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);
            return InvisibleRows.Contains(rowId)
                ? DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found.")
                : Ok(new { id = rowId });
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            PostAsync(absoluteApiPath, jsonBody, preferRepresentation: false, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        {
            Posts.Add((absoluteApiPath, jsonBody));
            return Task.FromResult(Ok(new { sprk_todoid = Guid.NewGuid(), activityid = Guid.NewGuid() }, 201));
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            if (PatchStatus >= 400)
                return Task.FromResult(DataverseUserResponse.Fail(PatchStatus, DataverseUserClientErrorCodes.AccessDenied, "Denied."));

            Patches.Add((relativePath, jsonBody));
            return Task.FromResult(DataverseUserResponse.Ok(204, null));
        }

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static DataverseUserResponse Ok(object body, int status = 200) =>
            DataverseUserResponse.Ok(status, JsonSerializer.SerializeToElement(body));
    }
}
