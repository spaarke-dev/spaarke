using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 r3 (UAC-r2 review of PR #2486, F1): who the "Assigned *" step records as the
/// person behind a root's create. On master the chat create passed NO grantor (<c>grantorOid: null</c>): the contact got its
/// grant, but no invitation went out and no user was recorded in <c>sprk_grantedby</c>; the child-records route passed the
/// caller's oid (the invitation, attributed to them). Moving the chat create onto <see cref="RecordCreationService"/> had
/// silently switched it to the caller — a user confirming a chat-proposed matter, project or work assignment would have
/// emailed an external Assigned To an invitation in their name. <see cref="RecordCreationRequest.Grantor"/> restores it.
/// The REAL materializer, grant core and invite service run over the task-142 harness; assertions are on what resulted
/// (the invite email, the CIAM call, the stamped marker, the grant row's <c>sprk_grantedby</c>), not on a mock call.
/// KEEP path: <c>tests/integration/data-mutation/**</c> (writes and their side effects).
/// </summary>
public sealed class AssignedAccessGrantorTests : TypedToolHandlerTestFixture
{
    private const string AssignedTo = "sprk_assignedtointernal";
    private const string ContactEmail = "assignee@outside.test";

    private readonly Harness _h = new();

    /// <summary>The confirming user's Entra oid and the systemuser it resolves to (an INTERNAL user: the invite rule applies).</summary>
    private readonly Guid _callerOid = Guid.NewGuid();
    private readonly Guid _callerSystemUser = Guid.NewGuid();

    public AssignedAccessGrantorTests() => _h.Grants.SystemUserIdsByOid[_callerOid] = _callerSystemUser;

    /// <summary>A scope factory whose scope resolves the harness's materializer — what the host's DI provides.</summary>
    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.Materializer);
        services.AddSingleton(EntitySetNameFixtures.Service());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>An active contact typed in as Assigned To: an email, no CIAM account bound (so an invitation is owed).</summary>
    private Guid ExternalContact()
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = ContactEmail;
        return contact;
    }

    private static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static ExternalGrantRootType RootOf(string table) => table switch
    {
        "sprk_matter" => ExternalGrantRootType.Matter,
        "sprk_project" => ExternalGrantRootType.Project,
        "sprk_workassignment" => ExternalGrantRootType.WorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    private static string EntitySetOf(string table) => table + "s";

    /// <summary>
    /// The caller's Dataverse for an owned create of <paramref name="table"/>: the table's metadata (user-owned, so the
    /// create takes the owned path), WhoAmI, and the Create privilege the caller holds.
    /// </summary>
    private Mock<IDataverseUserClient> CallerDataverse(string table)
    {
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"EntityDefinitions(LogicalName='{table}')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json(
                $$"""{ "EntitySetName": "{{EntitySetOf(table)}}", "PrimaryIdAttribute": "{{table}}id", "OwnershipType": "UserOwned" }""")));
        dataverse.Setup(d => d.GetAsync("WhoAmI()", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json($$"""{ "UserId": "{{_callerSystemUser:D}}" }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"EntityDefinitions(LogicalName='{table}')?$select=LogicalName,Privileges")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json(
                $$"""{ "LogicalName": "{{table}}", "Privileges": [ { "PrivilegeType": "Create", "Name": "prvCreate{{table}}" } ] }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.Contains("RetrieveUserSetOfPrivilegesByNames")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json($$"""{ "RolePrivileges": [ { "PrivilegeName": "prvCreate{{table}}" } ] }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"EntityDefinitions(LogicalName='{table}')/Attributes")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "value": [] }""")));
        return dataverse;
    }

    /// <summary>
    /// The application's create of the root: the created row names <paramref name="contact"/> in its Assigned To (what the
    /// materializer reads back). <paramref name="createdId"/> is the row's id once created.
    /// </summary>
    private Mock<IFieldMappingDataverseService> AppOnlyCreate(string table, Guid contact, Action<Guid> created)
    {
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly.Setup(a => a.UpdateRecordFieldsAsync(
                table, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, id, _, _, _) =>
            {
                created(id);
                _h.Store.Assign(RootOf(table), id, AssignedTo, contact);
                _h.Grants.RootNames[id] = "Acme";
            })
            .Returns(Task.CompletedTask);
        return appOnly;
    }

    private DataverseCreateRecordHandler ChatHandler(Mock<IDataverseUserClient> dataverse, Mock<IFieldMappingDataverseService> appOnly) =>
        new(dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new RecordOwnershipResolverDouble(),
            IdentityNormalizationFixtures.NoLinkedContact(),
            Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(
                new RecordOwnershipResolverDouble(), appOnly.Object, SecureRootFilingGateFixtures.NothingSecure(), Scopes()),
            Scopes());

    // ── The chat create: no grantor ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_project", "sprk_projectname")]
    [InlineData("sprk_matter", "sprk_mattername")]
    [InlineData("sprk_workassignment", "sprk_name")]
    public async Task ChatOwnedCreate_OfARoot_WithAnExternalAssignedTo_GrantsAccess_ButSendsNoInvitation_AndRecordsNoGrantor(
        string table, string nameColumn)
    {
        var contact = ExternalContact();
        Guid? createdId = null;
        var dataverse = CallerDataverse(table);
        var handler = ChatHandler(dataverse, AppOnlyCreate(table, contact, id => createdId = id));

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$$"""{"tablename":"{{{table}}}","item":{"{{{nameColumn}}}":"New"}}""")
                with { UserId = _callerOid.ToString("D") },
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        createdId.Should().NotBeNull("the application created the root (the owned path)");
        dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "a run-as-user POST would mean the owned path was not taken");

        // As on master: the contact has its access ...
        _h.Grants.ActiveRowsOf(createdId!.Value, contact).Should().ContainSingle("the grant is made");
        // ... but nobody invited it, and nobody is recorded as having granted it.
        _h.Emails.InviteUrls.Should().BeEmpty("no grantor, so no invitation is sent in the confirming user's name");
        _h.Ciam.Attempts.Should().BeEmpty("no CIAM account is provisioned");
        _h.Identities.Contacts[contact].InvitedOn.Should().BeNull("no invite marker is stamped");
        GrantPayload().Should().NotContain("sprk_GrantedBy@odata.bind", "sprk_grantedby names no user: the chat create has no grantor");
    }

    // ── The route create: the caller stays the grantor (must-not-fire: route behaviour is unchanged) ─────────────────────

    [Fact]
    public async Task ChildRecordsRoute_CreateOfAWorkAssignment_WithAnExternalAssignedTo_StillInvitesInTheCallersName()
    {
        const string table = "sprk_workassignment";
        var contact = ExternalContact();
        Guid? createdId = null;
        var dataverse = CallerDataverse(table);
        var appOnly = AppOnlyCreate(table, contact, id => createdId = id);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", _callerOid.ToString("D"))], "test")),
        };

        var result = await ChildRecordEndpoints.CreateAsync(
            table,
            JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["sprk_name"] = "Review the lease" }),
            dataverse.Object,
            Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(
                new RecordOwnershipResolverDouble(), appOnly.Object, SecureRootFilingGateFixtures.NothingSecure(), Scopes()),
            http, NullLogger<Program>.Instance, CancellationToken.None);

        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status201Created, (result as IValueHttpResult)?.Value?.ToString());
        createdId.Should().NotBeNull();
        _h.Grants.ActiveRowsOf(createdId!.Value, contact).Should().ContainSingle();
        _h.Emails.InviteUrls.Should().ContainSingle("the route's grantor is the caller: the contact is invited (unchanged)");
        _h.Identities.Contacts[contact].InvitedOn.Should().NotBeNull();
        GrantPayload().Should().Contain("sprk_GrantedBy@odata.bind").And.Contain($"/systemusers({_callerSystemUser:D})",
            "sprk_grantedby names the caller (unchanged)");
    }

    /// <summary>The one grant row the materializer created, as written (its <c>sprk_GrantedBy@odata.bind</c> is the grantor).</summary>
    private string GrantPayload() => _h.Grants.Creates.Should().ContainSingle("one grant row was written").Subject.Payload;

    // ── The request: the default is the caller, None is nobody (the service's own contract) ───────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TheRequestsGrantor_DecidesWhetherTheAssignedToIsInvited(bool noGrantor, bool invited)
    {
        const string table = "sprk_project";
        var contact = ExternalContact();
        Guid? createdId = null;
        var service = Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(
            new RecordOwnershipResolverDouble(), AppOnlyCreate(table, contact, id => createdId = id).Object,
            SecureRootFilingGateFixtures.NothingSecure(), Scopes());
        var dataverse = CallerDataverse(table);
        var item = await DataverseWriteItemMapper.MapAsync(
            dataverse.Object, table, JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["sprk_projectname"] = "New" }),
            CancellationToken.None);

        var outcome = await service.CreateAsync(
            new RecordCreationRequest(table, new RecordCreationPayload.Mapped(item.Item!)) { Grantor = noGrantor ? AssignedAccessGrantor.None : AssignedAccessGrantor.Caller },
            RecordCreationPrincipal.ForCaller(dataverse.Object, _callerOid), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _h.Grants.ActiveRowsOf(createdId!.Value, contact).Should().ContainSingle("the grant is made either way");
        _h.Emails.InviteUrls.Should().HaveCount(invited ? 1 : 0);
    }

    /// <summary>
    /// Counts the scopes the "Assigned *" step opens: <c>RunAfterWriteAsync</c> opens one for a root only, so a child table's
    /// create that never reaches the materializer leaves the count at zero.
    /// </summary>
    internal sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int Opened { get; private set; }

        public IServiceScope CreateScope()
        {
            Opened++;
            return inner.CreateScope();
        }
    }

    /// <summary>A counting scope factory over the harness's materializer, for the child-table pins.</summary>
    internal static (CountingScopeFactory Scopes, Harness Harness) CountingScopes()
    {
        var harness = new Harness();
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Materializer);
        services.AddSingleton(EntitySetNameFixtures.Service());
        return (new CountingScopeFactory(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()), harness);
    }
}
