using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// #2257 (owner round 123, "run as the user"): the Update Record node writes AS the person who started the run
/// (<c>MSCRMCallerID</c>), so Dataverse decides the write on that person's own rights — including a <c>recordId</c> written
/// into the node's config, which no run filter checks. Only a system run (scheduled, a background job) writes app-only. Both
/// are asked <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> (fix round 1): the node's payload is the playbook
/// author's configuration or a model's, never the runner's. A person's run whose user is unknown writes nothing and
/// never falls back to app-only. A refusal by Dataverse is a failed node with a stable code, never an unhandled exception.
/// </summary>
public class UpdateRecordNodeExecutorRunIdentityTests
{
    private static readonly Guid Person = Guid.Parse("22570000-0000-4000-8000-00000000a001");
    private static readonly Guid Matter = Guid.Parse("22570000-0000-4000-8000-00000000b001");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();
    private readonly Mock<IServiceScopeFactory> _scopes = new(MockBehavior.Strict);
    private readonly Mock<ITemplateEngine> _templates = new();
    private readonly List<(string Table, Dictionary<string, object?> Fields, Guid? As)> _written = [];

    public UpdateRecordNodeExecutorRunIdentityTests()
    {
        _templates
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) => template);
        _writes
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>(
                (table, _, fields, _, person) => _written.Add((table, fields, person)))
            .Returns(Task.CompletedTask);
    }

    private UpdateRecordNodeExecutor Executor() =>
        new(_templates.Object, _writes.Object, _scopes.Object, NullLogger<UpdateRecordNodeExecutor>.Instance);

    /// <summary>Dataverse answers 403 to a write made AS <paramref name="person"/> (no Write / no AppendTo).</summary>
    private void DataverseRefuses(Guid person) =>
        _writes
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                person))
            .ThrowsAsync(new HttpRequestException("403 (Forbidden)", inner: null, HttpStatusCode.Forbidden));

    private static string LegacyConfig(string table, Guid recordId, string field, string value) =>
        JsonSerializer.Serialize(new
        {
            entityLogicalName = table,
            recordId = recordId.ToString(),
            fields = new Dictionary<string, string> { [field] = value },
        });

    private static NodeExecutionContext Context(string configJson, PlaybookRunStartedBy startedBy, Guid? userId) => new()
    {
        RunId = Guid.NewGuid(),
        PlaybookId = Guid.NewGuid(),
        Node = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid(),
            Name = "persistEnvelope",
            ExecutionOrder = 1,
            OutputVariable = "persistResult",
            ConfigJson = configJson,
            IsActive = true,
        },
        Action = new AnalysisAction { Id = Guid.NewGuid(), Name = "Update Record" },
        ExecutorType = ExecutorType.UpdateRecord,
        Scopes = new ResolvedScopes([], [], []),
        TenantId = "test-tenant",
        StartedBy = startedBy,
        UserId = userId,
    };

    // ── Must fire: a person's run writes AS that person ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task APersonsRun_WritesAsThatPerson()
    {
        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "sprk_performancesummary", "x"), PlaybookRunStartedBy.Person, Person),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _written.Should().ContainSingle().Which.As.Should().Be(Person, "a person's run writes AS that person, never app-only");
    }

    // ── Must fire (fix round 1): a person's run is still asked the deny-lists ──────────────────────────────────────────
    // The payload is the playbook's configuration (its author, who may not be the runner) or a model's, never the runner's,
    // so InheritedAccessPermission.RefuseAppOnlyWrite is asked whatever identity the write runs as (main-session decision).

    [Fact]
    public async Task APersonsRun_ANodeAskedToWriteTheOwner_IsRefused_WithTheOwnerCode_AndNothingIsWritten()
    {
        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "ownerid@odata.bind", $"/systemusers({Person:D})"),
                PlaybookRunStartedBy.Person, Person),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("a configured owner write is refused before anything is written, even as the person");
    }

    [Theory]
    [InlineData("sprk_accesspermission", "100000000", "sdap.access.document_access_server_only")]
    [InlineData("sprk_accessinheritance", "{}", "sdap.access.document_access_server_only")]
    [InlineData("sprk_parentdocument@odata.bind", "/sprk_documents(22570000-0000-4000-8000-00000000f001)", "sdap.access.document_link_server_only")]
    [InlineData("sprk_graphitemid", "x", "sdap.access.document_bff_column_server_only")]
    [InlineData("sprk_createdbyperson@odata.bind", "/systemusers(22570000-0000-4000-8000-00000000a001)", "sdap.access.creator_server_only")]
    public async Task APersonsRun_ANodeThatWritesADocumentsServerOwnedColumn_IsRefused_AndNothingIsWritten(
        string column, string value, string code)
    {
        // The issue's scenario: user A publishes a node that sets sprk_document.sprk_accesspermission = Standard on
        // {{document.id}}; user B, who holds Write, runs it. B's Restricted document must not be lowered, even as B.
        var document = Guid.Parse("22570000-0000-4000-8000-00000000f002");

        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_document", document, column, value), PlaybookRunStartedBy.Person, Person),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(code);
        _writes.Invocations.Should().BeEmpty();
    }

    // ── Must fire (master merge after #2253): a person's run is asked #2251's and #2253's sets too ───────────────────────
    // #2253 added WriteIdentity.Caller, which skips AccessTables, SecureRootColumns and AccessColumns for a write that runs AS
    // a person (the field-mapping push). A playbook run that writes AS its person is not that: the node asks as
    // WriteIdentity.AppOnly, so every set applies. Fails if UpdateRecordActionCore passes WriteIdentity.Caller.

    [Theory]
    [InlineData("sprk_communicationaccount", "sprk_name", "x", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode)]
    [InlineData("sprk_communication", "sprk_isinternalonly", "false", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode)]
    [InlineData("sprk_matter", "sprk_containerid", "b!forged", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode)]
    [InlineData("systemuser", "sprk_primarycontact@odata.bind", "/contacts(22570000-0000-4000-8000-00000000c0c0)",
        InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode)]
    public async Task APersonsRun_ANodeThatWritesAnAccessTableOrColumn_IsRefused_AndNothingIsWritten(
        string table, string column, string value, string code)
    {
        // The last row is the issue's own route (a hard-coded systemuser record, its task-141 link to a contact).
        var record = Guid.Parse("22570000-0000-4000-8000-00000000c002");

        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig(table, record, column, value), PlaybookRunStartedBy.Person, Person),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain($"({code})", "a person's run is asked every set, as WriteIdentity.AppOnly");
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written, even AS the person");
    }

    // ── The shipped matter-health-single playbook (Update Record on sprk_matter.sprk_performancesummary) ────────────────

    [Fact]
    public async Task MatterHealthSingle_ACallerWithWriteOnTheMatter_WritesTheEnvelopeAsThemself()
    {
        var result = await Executor().ExecuteAsync(
            Context(ShippedPersistEnvelopeConfig(Matter), PlaybookRunStartedBy.Person, Person), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var write = _written.Should().ContainSingle().Subject;
        write.Table.Should().Be("sprk_matter");
        write.Fields.Keys.Should().Equal("sprk_performancesummary");
        write.As.Should().Be(Person);
    }

    [Fact]
    public async Task MatterHealthSingle_ACallerWithoutWriteOnTheMatter_FailsCleanly_AndIsNeverRetriedAppOnly()
    {
        // The Insights route checks Write on the subject first, so this cannot happen through it; the writer is safe anyway.
        DataverseRefuses(Person);

        var result = await Executor().ExecuteAsync(
            Context(ShippedPersistEnvelopeConfig(Matter), PlaybookRunStartedBy.Person, Person), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(PlaybookRunWriteIdentity.WriteAccessDeniedCode);
        result.ErrorMessage.Should().StartWith(PlaybookRunWriteIdentity.WriteAccessDeniedCode);
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null),
            Times.Never, "a refused person's write is never repeated app-only");
    }

    [Fact]
    public async Task AHardCodedRecordId_ThePersonCannotWrite_IsRefused_AndNothingIsWrittenAppOnly()
    {
        // The issue's route: the node's config names a record the run filter never checked. Master merge after #2253: the
        // issue's own column (systemuser.sprk_primarycontact) is now an access column, refused before Dataverse is asked
        // (pinned with the other #2251/#2253 sets in the person's-run must-fire tests); an ordinary column on the same
        // hard-coded record still reaches Dataverse AS the person, which refuses it.
        var self = Guid.Parse("22570000-0000-4000-8000-00000000c001");
        DataverseRefuses(Person);

        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("systemuser", self, "jobtitle", "Partner"),
                PlaybookRunStartedBy.Person, Person),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(PlaybookRunWriteIdentity.WriteAccessDeniedCode);
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null),
            Times.Never);
    }

    [Fact]
    public async Task ALookupBind_ThePersonMayNotAppendTo_FailsTheNodeWithTheCode_NotAnException()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("contact", It.IsAny<CancellationToken>())).ReturnsAsync("contacts");
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IGenericEntityService))).Returns(entities.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        _scopes.Setup(f => f.CreateScope()).Returns(scope.Object);
        DataverseRefuses(Person);
        var config = JsonSerializer.Serialize(new
        {
            entityLogicalName = "sprk_matter",
            recordId = Matter.ToString(),
            fields = new Dictionary<string, string> { ["sprk_performancesummary"] = "x" },
            lookups = new Dictionary<string, object>
            {
                ["sprk_ClientContact"] = new { targetEntity = "contact", targetId = "22570000-0000-4000-8000-00000000c0c0" },
            },
        });

        var result = await Executor().ExecuteAsync(Context(config, PlaybookRunStartedBy.Person, Person), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(PlaybookRunWriteIdentity.WriteAccessDeniedCode);
    }

    // ── Must fire: a person's run whose user is unknown writes nothing (never app-only) ───────────────────────────────

    public static TheoryData<Guid?> UnresolvedUsers() => new() { null, Guid.Empty };

    [Theory]
    [MemberData(nameof(UnresolvedUsers))]
    public async Task APersonsRun_WhoseUserIsUnknown_RefusesTheWrite_AndNeverWritesAppOnly(Guid? userId)
    {
        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "sprk_performancesummary", "x"), PlaybookRunStartedBy.Person, userId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(PlaybookRunWriteIdentity.RunUserUnresolvedCode);
        _writes.Invocations.Should().BeEmpty("nothing is read or written for a run whose user is unknown");
    }

    [Fact]
    public async Task ANodeContextThatSaysNothing_IsAPersonsRun_AndRefusesToWriteWithoutAUser()
    {
        // The default is fail closed: a context built without StartedBy is a person's run.
        var context = Context(LegacyConfig("sprk_matter", Matter, "sprk_performancesummary", "x"), PlaybookRunStartedBy.Person, null)
            with { StartedBy = default };

        var result = await Executor().ExecuteAsync(context, CancellationToken.None);

        context.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        result.ErrorCode.Should().Be(PlaybookRunWriteIdentity.RunUserUnresolvedCode);
        _writes.Invocations.Should().BeEmpty();
    }

    // ── Must not fire: a system run writes app-only, behind the deny-lists ─────────────────────────────────────────────

    [Fact]
    public async Task ASystemRun_WritesAppOnly_EvenWhenItTargetsAUser()
    {
        // A scheduler's per-user notification run targets a user (UserId, for eq-userid); it still writes as no one.
        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "sprk_performancesummary", "x"), PlaybookRunStartedBy.System, Person),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _written.Should().ContainSingle().Which.As.Should().BeNull();
    }

    [Fact]
    public async Task ASystemRun_StillMeetsTheAppOnlyDenyList_AndWritesNothing()
    {
        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "ownerid@odata.bind", $"/systemusers({Person:D})"),
                PlaybookRunStartedBy.System, null),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAppOnlyWrite_ThatDataverseRefuses_IsNotReportedAsThePersonsRefusal()
    {
        _writes
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new HttpRequestException("403 (Forbidden)", inner: null, HttpStatusCode.Forbidden));

        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "sprk_performancesummary", "x"), PlaybookRunStartedBy.System, null),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError);
    }

    [Fact]
    public async Task APersonsWrite_ThatFailsForAnotherReason_IsNotReportedAsAnAccessRefusal()
    {
        _writes
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), Person))
            .ThrowsAsync(new HttpRequestException("500", inner: null, HttpStatusCode.InternalServerError));

        var result = await Executor().ExecuteAsync(
            Context(LegacyConfig("sprk_matter", Matter, "sprk_performancesummary", "x"), PlaybookRunStartedBy.Person, Person),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError);
    }

    /// <summary>
    /// The shipped <c>matter-health-single</c> playbook's <c>persistEnvelope</c> node config, with <c>{{matterId}}</c> already
    /// substituted (the orchestrator renders run parameters into ConfigJson before the node runs).
    /// </summary>
    private static string ShippedPersistEnvelopeConfig(Guid matterId)
    {
        var path = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Ai", "Insights", "Playbooks",
            "matter-health-single.playbook.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var node = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("name").GetString() == "persistEnvelope");
        node.GetProperty("actionType").GetInt32().Should().Be((int)ExecutorType.UpdateRecord);
        return node.GetProperty("configJson").GetRawText().Replace("{{matterId}}", matterId.ToString());
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
