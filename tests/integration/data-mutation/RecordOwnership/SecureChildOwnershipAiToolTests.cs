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
/// <item><c>dataverse.create_record</c> and <c>email.draft</c> (owner decision S1 option (1), G5 refined; owner round 7
/// item 3, §6.5 path B): a create is checked AS THE CALLER (Create/Append, AppendTo on every record named, no owner or
/// field-secured column), then created by the APPLICATION owned by the team the resolver names — the named Secure team
/// for a secure record, the parent's (or, unfiled, the caller's) business-unit team otherwise — with the caller named in
/// the table's "for" column; never owned by the caller. The creator is kept only where the resolver keeps it (per-user
/// tables, unfiled communications — E1 — and tables with no user/team ownership). Refusals create nothing.</item>
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
    private static readonly Guid CallerOid = Guid.Parse("a2460000-0000-4000-8000-0000000000ab");

    /// <summary>The caller's LINKED contact (task 141) — the person a contact-typed "for" column names.</summary>
    private static readonly Guid CallerContact = Guid.Parse("a2460000-0000-4000-8000-0000000000ac");

    private readonly Directory _world = Directory.Standard()
        .WithUser(Caller, CallerOid, Directory.ChildBu) // the caller sits in the child business unit (owner round 5)
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithRecord("sprk_project", FlaggedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam)
        .WithRecord("sprk_document", OrdinaryDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam)
        // c1 (owner round 10 item 7): a document filed under the secure matter, created by someone else.
        .WithRecord("sprk_document", SecureDocument, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter),
                ["createdby"] = new EntityReference("systemuser", Guid.Parse("a2460000-0000-4000-8000-0000000000ee")),
            });

    private static readonly Guid SecureDocument = Guid.Parse("a2460000-0000-4000-8000-000000000005");

    private readonly ScriptedUserClient _user = new(Caller);
    private readonly List<(string Table, Guid Id, Dictionary<string, object?> Fields)> _appCreates = new();
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);

    public SecureChildOwnershipAiToolTests()
    {
        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, id, f, _, _) =>
            {
                // Task 147 r1c-v1 (verifier item 1): the app-only create is a Web API PATCH too — a body naming a lookup by
                // its logical name is refused (the DataverseWebApiService throws on the non-success status).
                if (ScriptedUserClient.UndeclaredPropertyIn(t, JsonSerializer.Serialize(f), "ownerid", "sprk_CreatedByPerson") is { } undeclared)
                {
                    throw new HttpRequestException(
                        $"0x80060888 Could not find a property named '{undeclared}' on type 'Microsoft.Dynamics.CRM.{t}'.",
                        null, System.Net.HttpStatusCode.BadRequest);
                }

                _appCreates.Add((t, id, f));
                MaterializeInShareWorld(t, id, f);
            })
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
        // c1-r1 (owner round 13 item 9): createdby is the application, so the caller is recorded as the person who asked.
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue.Should().Be($"/systemusers({Caller:D})");
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
    public async Task CreateRecord_NamingTheCreatorPersonColumn_IsRefused_AndCreatesNothing()
    {
        // c1-r1: who created a record is the server's to stamp, like its owner — a caller never chooses it.
        var result = await CreateRecord("sprk_todo",
            Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter),
            ("sprk_createdbyperson", JsonSerializer.SerializeToElement(new { relatedTable = "systemuser", recordId = Guid.NewGuid() })));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sprk_createdbyperson").And.Contain("set by the server");
        _appCreates.Should().BeEmpty();
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
    public async Task CreateRecord_AnUnfiledToDo_IsCreatedByTheApp_OwnedByTheCallersBusinessUnitTeam_ForTheCaller()
    {
        // Owner round 7 item 3 (G5 for every create) + round 5 (a record is assigned to the creating user's business-unit
        // team): the r2 "unfiled stays run-as-user" exception is superseded.
        var result = await CreateRecord("sprk_todo", ("sprk_name", JsonSerializer.SerializeToElement("Call back")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty("never owned by the individual");
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam, "the caller's business-unit default team");
        Bind(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({CallerContact:D})",
            "the person the to-do is FOR is the caller's linked contact (Created By is the application)");
    }

    [Fact]
    public async Task CreateRecord_AFiledToDo_NamesTheCallerInAssignedTo_WithoutAskingAppendToOnTheirContact()
    {
        // The "for" column is SERVER-set: it names the caller, so it costs no AppendTo check (the caller may lack it).
        _user.NoAppendTo.Add(CallerContact);

        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.SecureNamedTeam);
        Bind(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({CallerContact:D})");
    }

    [Fact]
    public async Task CreateRecord_ASuppliedAssignee_IsNeverOverwritten()
    {
        var colleague = Guid.NewGuid();

        var result = await CreateRecord("sprk_todo",
            Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter),
            Lookup("sprk_assignedto", "contact", colleague));

        result.Success.Should().BeTrue(result.ErrorMessage);
        Bind(_appCreates.Should().ContainSingle().Subject.Fields, "sprk_AssignedTo@odata.bind")
            .Should().Be($"/contacts({colleague:D})");
    }

    [Fact]
    public async Task CreateRecord_ACallerWithNoLinkedContact_IsStillCreated_TeamOwned_WithTheAssigneeBlank()
    {
        var handler = CreateRecordHandler(identity: IdentityNormalizationFixtures.NoLinkedContact());

        var result = await handler.ExecuteChatAsync(
            CreateContext("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam);
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.Ordinal),
            "never a team, never an email match — blank when there is no linked contact");
    }

    [Fact]
    public async Task CreateRecord_AnUnfiledMatter_IsOwnedByTheCallersBusinessUnitTeam_AndNamesTheCallerAsAssignedToInternal()
    {
        // A root created from chat follows the Office quick-create precedent (task 080 owner, A7 "for" column).
        var result = await CreateRecord("sprk_matter", ("sprk_mattername", JsonSerializer.SerializeToElement("Acme v Beta")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_matter").Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam);
        Bind(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({CallerContact:D})");

        // Batch 4 integration (task 133 x task 146): the creator person is stamped IN THE CREATE PAYLOAD — the one stamp
        // (task 133's interim app-only follow-up update is superseded and removed).
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue.Should().Be($"/systemusers({Caller:D})", "the application created the row for the caller");
    }

    [Fact]
    public async Task CreateRecord_AWorkAssignmentFiledToAnOrdinaryMatter_IsOwnedByThatMattersTeam_AndNamesTheCallerAsAssignedToInternal()
    {
        // Owner round 7 item 3 ("record the person in the table's Assigned-To / 'for' column where one exists"), b2-r2
        // (verifier b2-r1 item 4): the third root follows the matter/project precedent (A7), the column task 152 reads as
        // a work assignment's responsible internal person. Its separate sprk_assignedto is not defaulted.
        var result = await CreateRecord("sprk_workassignment", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty("never owned by the individual");
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_workassignment").Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first: the parent matter's business-unit team");
        Bind(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({CallerContact:D})");
        fields.Keys.Should().NotContain("sprk_AssignedTo@odata.bind", "one 'for' column per table");
    }

    [Fact]
    public async Task CreateRecord_AWorkAssignmentWithASuppliedAssignedToInternal_KeepsIt()
    {
        var colleague = Guid.NewGuid();

        var result = await CreateRecord("sprk_workassignment",
            ("sprk_name", JsonSerializer.SerializeToElement("Review lease")),
            Lookup("sprk_assignedtointernal", "contact", colleague));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_workassignment").Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam, "unfiled: the caller's business-unit default team");
        Bind(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({colleague:D})",
            "a supplied value is never overwritten");
    }

    [Fact]
    public async Task CreateRecord_AWorkAssignmentTheResolverPutsUnderTheSecureTeam_ButTheSecurePlanDoesNot_IsRefused_Task158r1()
    {
        // Task 158 r1 (owner round 31 item 2): a work assignment filed under a SECURE record is created INTO isolation, by the
        // plan SecureRootInheritance makes before the write (SecureRootInheritanceWriterTests drive it). This host's gate
        // reads a Dataverse with no rows — the plan finds no secure parent — while the ownership resolver's world says the
        // matter IS secure. The two cannot be reconciled at create time, so nothing is created (fail closed): never the
        // pre-r1 "ordinary row of the caller's unit", which left a business-unit-visible window.
        var result = await CreateRecord("sprk_workassignment", Lookup("sprk_regardingmatter", "sprk_matter", SecureMatter));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("NOT created").And.Contain("could not be decided consistently");
        _user.Posts.Should().BeEmpty("never owned by the individual");
        _appCreates.Should().BeEmpty("nothing is created when the two owners disagree");
    }

    [Fact]
    public async Task CreateRecord_AWorkAssignmentWhoseParentsFlagCannotBeRead_IsRefused_AndNothingIsCreated_Task158()
    {
        // Owner (task 158 constraint): "if the parent's flag cannot be read, refuse the create". The gate reads the matter
        // app-only and finds its flag EMPTY (owner round 17 item 3: empty is never "not secure").
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) =>
                q.EntityName == "sprk_matter"
                    ? new EntityCollection(new List<Entity> { new("sprk_matter", OrdinaryMatter) })
                    : new EntityCollection());

        var result = await CreateRecordHandler(rootFiling: SecureRootFilingGateFixtures.Over(entities.Object)).ExecuteChatAsync(
            CreateContext("sprk_workassignment", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)),
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        result.ErrorMessage.Should().Contain("NOT created");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    // ── Task 133 creator stamp, integrated with G5 (owner round 10: "superseded at integration by task 146's
    //    create-as-the-app, which writes the stamp in the create payload"). Batch 4 integration: the ONE stamp is task 146
    //    c1-r1's (RecordOwnerResolution.StampCreatorOn in OwnedChildWrite.CreateAsync, a sprk_CreatedByPerson bind after
    //    the caller's checks); task 149 r3's own WithCreatorPersonAsync re-map was dropped so the column is written once.

    [Theory]
    [InlineData("sprk_matter", "sprk_mattername")]
    [InlineData("sprk_project", "sprk_projectname")]
    [InlineData("sprk_workassignment", "sprk_name")]
    public async Task CreateRecord_ASecureRootTable_CarriesTheCallerAsItsCreatorPerson_InTheAppsOwnCreate(
        string table, string nameColumn)
    {
        // Live, the column is field-secured so that ONLY the BFF writes it (Set-RecordCreatorPersonSchema.ps1): the
        // server-set stamp is not the caller's column-security question.
        _user.SecuredColumns.Add(RecordCreatorPerson.Column);

        var result = await CreateRecord(table, (nameColumn, JsonSerializer.SerializeToElement("New")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty("created by the application, never as the user then patched");
        _user.Patches.Should().BeEmpty("there is no follow-up write: the stamp rides the create");
        var fields = _appCreates.Should().ContainSingle(c => c.Table == table).Subject.Fields;
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue.Should().Be($"/systemusers({Caller:D})",
                "createdby is the application, so the person who asked is recorded — the caller by WhoAmI, never the item");
        fields.Keys.Count(k => k.StartsWith("sprk_CreatedByPerson", StringComparison.OrdinalIgnoreCase)
                               || k.StartsWith("sprk_createdbyperson", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "exactly one creator stamp");
        Owner(fields).Should().Be(Directory.ChildTeam);
    }

    [Fact]
    public async Task CreateRecord_AChildTable_IsStampedWithItsCreatorPerson_Too()
    {
        // Batch 4 integration: task 149 r3 pinned "a child is never stamped" (task 133's three roots only). Task 146 c1-r1
        // (owner round 13 item 9, later than round 10) stamps the CHILD tables the application creates as well, so F3's
        // creator branch can admit the person who asked — this case now pins that, once.
        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue.Should().Be($"/systemusers({Caller:D})");
    }

    [Fact]
    public async Task CreateRecord_ARequestNamingAFieldSecuredColumnOnARoot_IsStillRefused_TheExemptionIsTheCreatorColumnOnly()
    {
        _user.SecuredColumns.Add(RecordCreatorPerson.Column);
        _user.SecuredColumns.Add("sprk_matterdescription");

        var result = await CreateRecord("sprk_matter",
            ("sprk_mattername", JsonSerializer.SerializeToElement("New")),
            ("sprk_matterdescription", JsonSerializer.SerializeToElement("privileged")));

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        result.ErrorMessage.Should().Contain("sprk_matterdescription");
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_workspacelayout")]   // per-user by design
    [InlineData("sprk_mattertype_ref")]    // organization-owned: no owner to decide
    public async Task CreateRecord_ATableThatKeepsItsCreator_IsStillCreatedAsTheUser(string table)
    {
        var result = await CreateRecord(table, ("sprk_name", JsonSerializer.SerializeToElement("x")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().ContainSingle();
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_AnUnfiledCommunication_IsStillCreatedAsTheUser_E1()
    {
        // The resolver keeps an unfiled communication's creator (escalation E1, pending the main session's reconciliation
        // with owner round 5) — G5 follows the resolver, it does not override it.
        var result = await CreateRecord("sprk_communication", ("sprk_name", JsonSerializer.SerializeToElement("Note")));

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
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed,
            "the Secure team may own only the ownership set's child tables (the codified role set)");
        _user.Posts.Should().BeEmpty();
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_ATableOutsideTheOwnershipSetFiledToAnOrdinaryMatter_IsCreatedByTheApp_OwnedByThatMattersTeam()
    {
        // Owner round 7 item 3: G5 for every create — an activity regarding an ordinary matter is that matter's team's.
        var result = await CreateRecord("task", Lookup("regardingobjectid", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().BeEmpty();
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam);
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

    [Fact]
    public async Task EmailDraft_Unfiled_IsStillCreatedAsTheDrafter_E1()
    {
        // An unfiled draft keeps its creator — the resolver's own answer for an unfiled communication (E1): a team-owned
        // draft would show one person's unsent email to their whole business unit.
        var handler = new EmailDraftToolHandler(
            _user, Mock.Of<IEmailDraftAi>(), CoreAncestorResolverFixtures.Inert(), CreateLogger<EmailDraftToolHandler>(),
            _world.Resolver(), _appOnly.Object);
        var args = JsonSerializer.Serialize(new { subject = "Hello", body = "Draft.", to = new[] { "a@b.example" } });

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: args) with { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(EmailDraftToolHandler)), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Posts.Should().ContainSingle();
        _appCreates.Should().BeEmpty();
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

    // ---- c1, owner round 10 item 7: moving a child OUT of a secure root is an un-secure (F3), asked AS THE CALLER ----

    [Fact]
    public async Task UpdateRecord_MovingADocumentOutOfASecureMatter_ByAFullAccessHolderOnIt_ReassignsItToTheNewMattersTeam()
    {
        _user.FullAccessOn.Add(SecureMatter);

        var result = await UpdateRecord("sprk_document", SecureDocument, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
        _world.Assignments.Should().Equal(("sprk_document", SecureDocument, Directory.ChildTeam));
    }

    [Fact]
    public async Task UpdateRecord_MovingADocumentOutOfASecureMatter_ByAWriteOnlyHolder_IsRefusedNotPermitted_AndThePatchIsNeverSent()
    {
        var result = await UpdateRecord("sprk_document", SecureDocument, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("sdap.unsecure.not_permitted");
        // c1-r1 (verifier c1 item 4): an F3 refusal is an AUTHORIZATION answer and is worded as one — the unsecure
        // endpoint's message, never the owner refusal's "the record's owner could not be decided".
        result.ErrorMessage.Should().StartWith(
            "The update was NOT written. Moving this document out of the secure record it is filed under ends its secure "
            + "protection. Only someone with Full Access")
            .And.NotContain("owner could not be decided");
        _user.Patches.Should().BeEmpty("refused before the caller's PATCH");
        _world.Assignments.Should().BeEmpty();
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    /// <remarks>Task 158: the secure-root gate defaults to one over a Dataverse with no rows (nothing is found filed under
    /// anything secure, so nothing is secured here); the securing itself is SecureRootInheritanceTests'.</remarks>
    private DataverseCreateRecordHandler CreateRecordHandler(
        IRecordOwnershipResolver? resolver = null,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService? identity = null,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate? rootFiling = null) =>
        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            resolver ?? _world.Resolver(), _appOnly.Object,
            identity ?? IdentityNormalizationFixtures.WithContact(CallerContact).Object,
            rootFiling ?? SecureRootFilingGateFixtures.NothingSecure());

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
        new DataverseUpdateRecordHandler(
                _user, new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                CreateLogger<DataverseUpdateRecordHandler>(), _world.Resolver(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure())
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
    /// <remarks>Internal (task 158): <c>SecureRootInheritanceWriterTests</c> drives the same chat tools over it.</remarks>
    internal sealed partial class ScriptedUserClient(Guid me) : IDataverseUserClient
    {
        private static readonly Dictionary<string, string> EntitySets = new()
        {
            ["sprk_todo"] = "sprk_todos", ["sprk_matter"] = "sprk_matters", ["sprk_project"] = "sprk_projects",
            ["sprk_document"] = "sprk_documents", ["sprk_communication"] = "sprk_communications",
            ["task"] = "tasks", ["systemuser"] = "systemusers", ["contact"] = "contacts",
            ["sprk_workassignment"] = "sprk_workassignments", ["sprk_workspacelayout"] = "sprk_workspacelayouts",
            ["sprk_mattertype_ref"] = "sprk_mattertype_refs",
            // Task 147 r1: the browser child-record routes' tables.
            ["sprk_memo"] = "sprk_memos", ["sprk_event"] = "sprk_events", ["sprk_budget"] = "sprk_budgets",
            ["sprk_invoice"] = "sprk_invoices", ["sprk_reportcard"] = "sprk_reportcards", ["sprk_analysis"] = "sprk_analysises",
            ["sprk_kpiassessment"] = "sprk_kpiassessments", ["sprk_billingevent"] = "sprk_billingevents",
            // Task 158 r1: the pair's type, and an org-typed lookup (a create's own No Access list).
            ["sprk_recordtype_ref"] = "sprk_recordtype_refs", ["sprk_organization"] = "sprk_organizations",
        };

        /// <summary>Tables whose metadata declares organization ownership (everything else is UserOwned).</summary>
        private static readonly HashSet<string> OrganizationOwned = new() { "sprk_mattertype_ref" };

        /// <summary>table → (lookup column, target table, navigation property).</summary>
        private static readonly Dictionary<string, (string Column, string Target, string Navigation)[]> Lookups = new()
        {
            ["sprk_todo"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_regardingproject", "sprk_project", "sprk_RegardingProject"),
                ("sprk_assignedto", "contact", "sprk_AssignedTo"),
                ("ownerid", "systemuser", "ownerid"),
                ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"), // task 146 c1-r1 schema step
            },
            ["sprk_matter"] = new[]
            {
                ("sprk_assignedtointernal", "contact", "sprk_AssignedToInternal"),
                ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"),
            },
            ["sprk_project"] = new[]
            {
                ("sprk_assignedtointernal", "contact", "sprk_AssignedToInternal"),
                ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"),
            },
            ["sprk_workassignment"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_assignedto", "contact", "sprk_AssignedTo"),
                ("sprk_assignedtointernal", "contact", "sprk_AssignedToInternal"),
                ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"),
                ("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_RegardingRecordType"),
                ("sprk_assignedlawfirm1", "sprk_organization", "sprk_AssignedLawFirm1"),
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
                // Task 147 r1c (round 34 item 6): the document's event lookup is sprk_relatedevent / sprk_RelatedEvent
                // (Spaarke.Dataverse.DocumentLinkFields, live metadata). There is NO sprk_event column on sprk_document.
                ("sprk_relatedevent", "sprk_event", "sprk_RelatedEvent"),
            },
            // Task 147 r1c: the remaining census create/re-file tables (live navigation-property casing).
            ["sprk_invoice"] = new[]
            {
                ("sprk_matter", "sprk_matter", "sprk_Matter"),
                ("sprk_project", "sprk_project", "sprk_Project"),
            },
            ["sprk_reportcard"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_regardingproject", "sprk_project", "sprk_RegardingProject"),
            },
            ["sprk_analysis"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_regardingproject", "sprk_project", "sprk_RegardingProject"),
            },
            // Task 147 r1c (E2, the live inventory of every main form; read-only metadata, spaarkedev1, 2026-10-04): the
            // KPI assessment's and the billing event's lookups. The billing event's matter navigation property is
            // lower-case (sprk_matter) live.
            ["sprk_kpiassessment"] = new[]
            {
                ("sprk_matter", "sprk_matter", "sprk_Matter"),
                ("sprk_project", "sprk_project", "sprk_Project"),
            },
            ["sprk_billingevent"] = new[]
            {
                ("sprk_matter", "sprk_matter", "sprk_matter"),
                ("sprk_project", "sprk_project", "sprk_project"),
            },
            // A polymorphic lookup: one navigation property per target table (task 147 r1c-v1: a clear goes through one).
            ["task"] = new[]
            {
                ("regardingobjectid", "sprk_matter", "regardingobjectid_sprk_matter"),
                ("regardingobjectid", "sprk_project", "regardingobjectid_sprk_project"),
            },
            // Task 147 r1 (live navigation-property casing: the schema name).
            ["sprk_memo"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_regardingevent", "sprk_event", "sprk_RegardingEvent"),
            },
            ["sprk_event"] = new[]
            {
                ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
                ("sprk_regardingproject", "sprk_project", "sprk_RegardingProject"),
            },
            // Task 147 r1 E2 (the secure-record ribbon's "New Budget"): sprk_Matter_Budget_1n, live 2026-10-04.
            ["sprk_budget"] = new[]
            {
                ("sprk_matter", "sprk_matter", "sprk_Matter"),
                ("sprk_project", "sprk_project", "sprk_Project"),
            },
        };

        public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "prvCreatesprk_todo", "prvAppendsprk_todo", "prvCreatesprk_communication", "prvAppendsprk_communication",
            "prvCreateActivity", "prvAppendActivity", "prvCreatesprk_matter", "prvAppendsprk_matter",
            "prvCreatesprk_workassignment", "prvAppendsprk_workassignment",
            "prvCreatesprk_project", "prvAppendsprk_project",
            "prvCreatesprk_memo", "prvAppendsprk_memo", "prvCreatesprk_event", "prvAppendsprk_event",
            "prvCreatesprk_budget", "prvAppendsprk_budget",
            "prvCreatesprk_document", "prvAppendsprk_document", "prvCreatesprk_invoice", "prvAppendsprk_invoice",
            "prvCreatesprk_reportcard", "prvAppendsprk_reportcard", "prvCreatesprk_analysis", "prvAppendsprk_analysis",
            "prvCreatesprk_kpiassessment", "prvAppendsprk_kpiassessment", "prvCreatesprk_billingevent", "prvAppendsprk_billingevent",
        };

        public HashSet<Guid> NoAppendTo { get; } = new();

        /// <summary>Task 147 r1: records that do not exist — RetrievePrincipalAccess on one fails (404).</summary>
        public HashSet<Guid> Missing { get; } = new();

        /// <summary>Records the caller holds Full Access on (Delete as well) — F3's question (task 146 c1).</summary>
        public HashSet<Guid> FullAccessOn { get; } = new();
        public HashSet<string> SecuredColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<Guid> InvisibleRows { get; } = new();

        /// <summary>
        /// Task 147 r1c-v1 (verifier item 2): rows that EXIST but the caller may not read — Dataverse answers a retrieve of
        /// one with 403 (the read privilege's depth does not reach it), where a row that does not exist answers 404.
        /// </summary>
        public HashSet<Guid> UnreadableRows { get; } = new();

        /// <summary>Task 147 r1: the owning team a row read as the caller reports (<c>_owningteam_value</c>).</summary>
        public Dictionary<Guid, Guid> OwningTeamOf { get; } = new();
        public int PatchStatus { get; set; } = 204;
        public List<(string Path, string Body)> Posts { get; } = new();
        public List<(string Path, string Body)> Patches { get; } = new();

        /// <summary>Task 147 r1c-v1: every write Dataverse refused because its body named a property the type does not declare.</summary>
        public List<(string Path, string Property)> RefusedBodies { get; } = new();

        /// <summary>
        /// Task 147 r1c-v1 (verifier item 1): the property of <paramref name="jsonBody"/> the Web API would REFUSE on
        /// <paramref name="table"/>, or <c>null</c>. A lookup is declared on the entity type only as its single-valued
        /// navigation property (case-sensitive, <c>sprk_RegardingMatter</c>) and the read-only <c>_…_value</c>; its logical
        /// name is not a property — live, spaarkedev1: <c>0x80060888 Could not find a property named 'sprk_regardingmatter'
        /// on type 'Microsoft.Dynamics.CRM.sprk_todo'</c>. So a body naming a lookup by its logical name (to set OR to
        /// clear it), or binding a navigation property the type does not declare, is refused, as Dataverse refuses it.
        /// <paramref name="alsoDeclared"/>: navigation properties the scripted table list leaves out (the server's own
        /// owner and creator-person binds on the app-only create).
        /// </summary>
        internal static string? UndeclaredPropertyIn(string table, string jsonBody, params string[] alsoDeclared)
        {
            var lookups = Lookups.GetValueOrDefault(table, Array.Empty<(string Column, string Target, string Navigation)>());
            using var document = JsonDocument.Parse(jsonBody);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.EndsWith("@odata.bind", StringComparison.Ordinal))
                {
                    var navigation = property.Name[..^"@odata.bind".Length];
                    if (!lookups.Any(l => string.Equals(l.Navigation, navigation, StringComparison.Ordinal))
                        && !alsoDeclared.Contains(navigation, StringComparer.Ordinal))
                    {
                        return property.Name;
                    }

                    continue;
                }

                if (property.Name.Contains('@', StringComparison.Ordinal))
                    continue; // an instance annotation

                if (lookups.Any(l => string.Equals(l.Column, property.Name, StringComparison.OrdinalIgnoreCase)))
                    return property.Name;
            }

            return null;
        }

        /// <summary>The table a Web API path addresses (<c>sprk_todos(…)</c> or <c>/api/data/v9.2/sprk_todos</c>).</summary>
        private static string? TableOf(string path)
        {
            var segment = path.TrimEnd('/');
            segment = segment[(segment.LastIndexOf('/') + 1)..];
            var set = segment.Contains('(', StringComparison.Ordinal) ? segment[..segment.IndexOf('(')] : segment;
            return EntitySets.FirstOrDefault(kv => kv.Value == set).Key;
        }

        /// <summary>Dataverse's answer to a body naming an undeclared property: 400, nothing written.</summary>
        private DataverseUserResponse? RefuseUndeclared(string path, string jsonBody)
        {
            if (TableOf(path) is not { } table || UndeclaredPropertyIn(table, jsonBody) is not { } property)
                return null;

            RefusedBodies.Add((path, property));
            var name = property.EndsWith("@odata.bind", StringComparison.Ordinal) ? property[..^"@odata.bind".Length] : property;
            return DataverseUserResponse.Fail(400, DataverseUserClientErrorCodes.BadRequest,
                $"0x80060888 Could not find a property named '{name}' on type 'Microsoft.Dynamics.CRM.{table}'.");
        }

        [GeneratedRegex(@"^EntityDefinitions\(LogicalName='(?<t>[a-z_]+)'\)(?<rest>.*)$")]
        private static partial Regex Definition();

        [GeneratedRegex(@"@odata\.id"":""(?<set>\w+)\((?<id>[0-9a-fA-F-]{36})\)")]
        private static partial Regex Target();

        [GeneratedRegex(@"LogicalName eq '(?<c>[a-z0-9_]+)'")]
        private static partial Regex AttributeNamed();

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Get(Uri.UnescapeDataString(relativePath)));

        private DataverseUserResponse Get(string path)
        {
            if (path == "WhoAmI()")
                return Ok(new { UserId = me });

            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
            {
                var id = Guid.Parse(Target().Match(path).Groups["id"].Value);
                if (Missing.Contains(id))
                    return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Does not exist.");
                var rights = NoAppendTo.Contains(id) ? "ReadAccess,WriteAccess" : "ReadAccess,WriteAccess,AppendAccess,AppendToAccess";
                return Ok(new { AccessRights = FullAccessOn.Contains(id) ? rights + ",DeleteAccess" : rights });
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
                {
                    // Only the columns the question names — as Dataverse answers the $filter.
                    var asked = AttributeNamed().Matches(rest).Select(m => m.Groups["c"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return Ok(new { value = SecuredColumns.Where(asked.Contains).Select(c => new { LogicalName = c, IsSecured = true }) });
                }
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

                return Ok(new
                {
                    EntitySetName = EntitySets[table],
                    PrimaryIdAttribute = table == "task" ? "activityid" : table + "id",
                    OwnershipType = OrganizationOwned.Contains(table) ? "OrganizationOwned" : "UserOwned",
                });
            }

            // A row read as the caller: "{set}({id})?$select=…".
            var rowId = Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);
            if (InvisibleRows.Contains(rowId))
                return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found.");
            if (UnreadableRows.Contains(rowId))
            {
                return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                    $"Principal user (Id={me:D}, type=8) is missing prvRead privilege on the record (Id={rowId:D}).");
            }
            var rowBody = new Dictionary<string, object?> { ["id"] = rowId };
            if (OwningTeamOf.TryGetValue(rowId, out var owningTeam))
                rowBody["_owningteam_value"] = owningTeam.ToString("D");
            return Ok(rowBody);
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            PostAsync(absoluteApiPath, jsonBody, preferRepresentation: false, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        {
            if (RefuseUndeclared(absoluteApiPath, jsonBody) is { } refused)
                return Task.FromResult(refused);

            Posts.Add((absoluteApiPath, jsonBody));
            return Task.FromResult(Ok(new { sprk_todoid = Guid.NewGuid(), activityid = Guid.NewGuid() }, 201));
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            if (PatchStatus >= 400)
                return Task.FromResult(DataverseUserResponse.Fail(PatchStatus, DataverseUserClientErrorCodes.AccessDenied, "Denied."));
            if (RefuseUndeclared(relativePath, jsonBody) is { } refused)
                return Task.FromResult(refused);

            Patches.Add((relativePath, jsonBody));
            return Task.FromResult(DataverseUserResponse.Ok(204, null));
        }

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static DataverseUserResponse Ok(object body, int status = 200) =>
            DataverseUserResponse.Ok(status, JsonSerializer.SerializeToElement(body));
    }
}
