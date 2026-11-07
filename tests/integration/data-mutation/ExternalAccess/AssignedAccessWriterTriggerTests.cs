using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 142, criterion 13 — every BFF writer in the census (notes §2) calls the Assigned-To
/// invariant owner AFTER its own write committed, with the root it created or updated, and a materializer fault never
/// fails the writer. The REAL materializer runs over <see cref="Harness"/>; each test asserts the access that resulted for
/// the written root (not merely that a mock was called), and a twin shows a write that touched no registry column
/// materializes nothing. KEEP path: <c>tests/integration/data-mutation/**</c> (writes and their side effects).
/// </summary>
public sealed class AssignedAccessWriterTriggerTests : TypedToolHandlerTestFixture
{
    private const string Attorney1 = "sprk_assignedattorney1";

    private readonly Harness _h = new();

    /// <summary>A scope factory whose scope resolves the harness's materializer — what the host's DI provides.</summary>
    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.Materializer);
        services.AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Office quick-create (matter, project) through the unified RecordCreationService — incl. owner A7: the maker is
    // assigned. Field mapping task 018c: driven over the Office route (the deleted Office core's tests, re-pointed).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Office contract host with the harness's materializer in DI (what step 12 resolves), every user's linked contact
    /// <paramref name="makersContact"/>, and the unified create's step-11 write landing in the harness's ledger — what
    /// Dataverse stores: the created row's Assigned column, as the materializer reads it back. Returns the created rows.
    /// </summary>
    private WebApplicationFactory<Program> OfficeHost(
        OfficeQuickCreateTestWebAppFactory factory, Guid makersContact, List<(string Table, Guid Id)> created,
        Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>? identityOverride = null,
        bool queueFails = false, Action<ExternalGrantRootType, Guid>? alsoAssign = null,
        Microsoft.Extensions.Logging.ILoggerProvider? logs = null)
    {
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Resolved(OfficeQuickCreateWorld.FieldsCaller.ToString("D")));
        factory.CallerDataverse.WithRow("contact", makersContact); // the maker default is AppendTo-checked as the caller
        factory.FieldMappings
            .Setup(m => m.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                created.Add((table, id));
                var root = table == "sprk_matter" ? ExternalGrantRootType.Matter : ExternalGrantRootType.Project;
                if (fields.TryGetValue("sprk_AssignedToInternal@odata.bind", out var bind)
                    && (bind is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : bind as string) is { } path
                    && Guid.TryParse(path.TrimEnd(')').Split('(').Last(), out var contact))
                {
                    _h.Store.Assign(root, id, "sprk_assignedtointernal", contact);
                }
                else
                {
                    _h.Store.Root(root, id);
                }

                alsoAssign?.Invoke(root, id); // task 049b: the other "Assigned *" columns the stored row carries
            })
            .Returns(Task.CompletedTask);
        factory.Entities
            .Setup(e => e.RetrieveAsync(
                It.Is<string>(t => t == "sprk_matter" || t == "sprk_project"), It.IsAny<Guid>(),
                It.Is<string[]>(columns => columns.Length == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] columns, CancellationToken _) => new Entity(table, id) { [columns[0]] = "NUM-142" });

        var identity = identityOverride ?? IdentityNormalizationFixtures.WithContact(makersContact);
        if (logs is not null)
        {
            _h.Logger = new Microsoft.Extensions.Logging.Logger<AssignedAccessMaterializer>(
                new Microsoft.Extensions.Logging.LoggerFactory(new[] { logs }));
        }

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (logs is not null)
                services.AddSingleton(logs);

            services.RemoveAll<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>();
            services.AddSingleton(identity.Object);
            services.RemoveAll<AssignedAccessMaterializer>();
            services.AddScoped(_ =>
            {
                // Resolved in the request only by an inline pass (a queued job is run by the test, over its own scope).
                Interlocked.Increment(ref _inlinePasses);
                return _h.Materializer;
            });
            // Task 049 Part B: the create queues its "Assigned *" pass; the jobs it queued are recorded here.
            services.RemoveAll<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>();
            services.AddSingleton(QueueThatRecords(queueFails));
        }));
    }

    /// <summary>How many times the Office host's request resolved the materializer: once per inline pass.</summary>
    private int _inlinePasses;

    /// <summary>A contact the request names in Assigned To: the caller may read it and append to it.</summary>
    private static void NamedAssignee(OfficeQuickCreateTestWebAppFactory factory, Guid contact)
    {
        factory.CallerDataverse.WithRow("contact", contact);
        factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", contact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
    }

    /// <summary>Task 049 Part B: the jobs the Office host's creates queued.</summary>
    private readonly List<Sprk.Bff.Api.Services.Jobs.JobContract> _queued = new();

    /// <summary>Task 049b fix round 2: how many sends a failing queue was asked for.</summary>
    private int _submitAttempts;

    private Sprk.Bff.Api.Services.Jobs.JobSubmissionService QueueThatRecords(bool fails = false)
    {
        var jobs = new Mock<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>(
            MockBehavior.Strict,
            Microsoft.Extensions.Options.Options.Create(new Sprk.Bff.Api.Configuration.ServiceBusOptions { QueueName = "sdap-jobs" }),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        if (fails)
        {
            jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
                .Callback<Sprk.Bff.Api.Services.Jobs.JobContract, CancellationToken>((_, _) => Interlocked.Increment(ref _submitAttempts))
                .ThrowsAsync(new Azure.Messaging.ServiceBus.ServiceBusException(
                    "Simulated send failure.", Azure.Messaging.ServiceBus.ServiceBusFailureReason.ServiceCommunicationProblem));
            return jobs.Object;
        }

        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<Sprk.Bff.Api.Services.Jobs.JobContract, CancellationToken>((job, _) => _queued.Add(job))
            .Returns(Task.CompletedTask);
        return jobs.Object;
    }

    /// <summary>Task 049 Part B: runs a queued create-grant job over the harness's materializer, as the queue would.</summary>
    private async Task RunQueuedGrantAsync(Sprk.Bff.Api.Services.Jobs.JobContract job)
    {
        var outcome = await RunQueuedJobAsync(job);
        outcome.Status.Should().Be(Sprk.Bff.Api.Services.Jobs.JobStatus.Completed, outcome.ErrorMessage);
    }

    private async Task<Sprk.Bff.Api.Services.Jobs.JobOutcome> RunQueuedJobAsync(Sprk.Bff.Api.Services.Jobs.JobContract job)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.Materializer);
        var handler = new Sprk.Bff.Api.Services.Jobs.Handlers.AssignedAccessCreateGrantJobHandler(
            services.BuildServiceProvider(), new Spaarke.Scheduling.ProcessLocalScheduledJobLease(),
            NullLogger<Sprk.Bff.Api.Services.Jobs.Handlers.AssignedAccessCreateGrantJobHandler>.Instance);
        return await handler.ProcessAsync(job, CancellationToken.None);
    }

    /// <summary>
    /// Task 049 fix round 1 (UAC-r2 ruling, item 1): a create whose Assigned-To is its maker's own linked contact grants the
    /// maker inline, so the maker's Assigned-To share is in place when the create returns. Task 049b: the full pass is then
    /// queued (one job), and finds the maker's share already there.
    /// </summary>
    [Theory]
    [InlineData("matter", "sprk_matter")]
    [InlineData("project", "sprk_project")]
    public async Task OfficeQuickCreate_ForItsMaker_SharesTheRecordWithThem_BeforeItReturns(string type, string table)
    {
        var (makersContact, makerUser) = _h.LinkedContact();
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        using var host = OfficeHost(factory, makersContact, created);

        var response = await host.CreateClient().PostAsJsonAsync($"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Quick" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var createdId = created.Should().ContainSingle().Subject.Id;
        _inlinePasses.Should().Be(1, "the maker's own subject is granted in the request");
        _h.Shares.MaskOf(table, createdId, DataversePrincipalRef.User(makerUser)).Should().NotBeNull(
            "owner A7: the maker is assigned, and the Assigned-To share is issued before the create returns");
        _h.Store.RowsOf(createdId, makersContact).Should().ContainSingle().Which.State.Should().Be(AssignedAccessState.Shared);

        var shareWrites = _h.Shares.Writes.Count;
        await RunQueuedGrantAsync(_queued.Should().ContainSingle("task 049b: the full pass is queued").Subject);
        _h.Shares.Writes.Count.Should().Be(shareWrites, "the queued pass finds the maker's share in place");
        _h.Store.RowsOf(createdId, makersContact).Should().ContainSingle().Which.State.Should().Be(AssignedAccessState.Shared);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Field-mapping-server-write-path-r1 task 049b (UAC-r2 ruling): when the maker is the created root's Assigned To,
    // only the maker's own subject is granted in the request; every other subject takes the queued path.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The other subjects a matter created for its maker also names: an internal attorney (a linked contact), an
    /// external contact (an unlinked one: a contact grant) and a law firm (an organization grant).</summary>
    private (Guid Attorney, Guid AttorneyUser, Guid External, Guid Firm, Action<ExternalGrantRootType, Guid> Assign) OthersOnTheMatter()
    {
        var (attorney, attorneyUser) = _h.LinkedContact();
        var external = _h.Contact();
        var firm = Guid.NewGuid();
        return (attorney, attorneyUser, external, firm, (root, id) =>
        {
            _h.Store.Assign(root, id, Attorney1, attorney);
            _h.Store.Assign(root, id, "sprk_assignedtoexternal", external);
            _h.Store.Assign(root, id, "sprk_assignedlawfirm1", firm);
        });
    }

    [Fact]
    public async Task OfficeQuickCreate_ForItsMaker_GrantsOnlyTheMakerInline_AndQueuesTheRest()
    {
        var (makersContact, makerUser) = _h.LinkedContact();
        var others = OthersOnTheMatter();
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        using var host = OfficeHost(factory, makersContact, created, alsoAssign: others.Assign);

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Quick" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var createdId = created.Should().ContainSingle().Subject.Id;
        _inlinePasses.Should().Be(1);
        _h.Shares.MaskOf("sprk_matter", createdId, DataversePrincipalRef.User(makerUser)).Should().NotBeNull();
        _h.Store.Ledger.Select(r => r.Subject).Distinct().Should().Equal(
            new AssignedSubject?[] { new AssignedSubject(AssignedSubjectKind.Contact, makersContact) },
            "exactly one subject, the maker's own, is granted in the request");
        _h.Shares.MaskOf("sprk_matter", createdId, DataversePrincipalRef.User(others.AttorneyUser)).Should().BeNull();
        _h.Grants.WriteCount.Should().Be(0);

        // The queued remainder: the full pass on the same path, idempotent for the maker.
        var job = _queued.Should().ContainSingle().Subject;
        job.JobType.Should().Be(Sprk.Bff.Api.Services.Jobs.Handlers.AssignedAccessCreateGrantJobHandler.JobTypeName);
        var shareWrites = _h.Shares.WriteLog.Count(w => w.Principal == DataversePrincipalRef.User(makerUser));
        await RunQueuedGrantAsync(job);

        _h.Shares.MaskOf("sprk_matter", createdId, DataversePrincipalRef.User(others.AttorneyUser)).Should().NotBeNull();
        _h.Grants.ActiveRowsOf(createdId, others.External).Should().ContainSingle();
        _h.Grants.ActiveRowsOf(createdId, organizationId: others.Firm).Should().ContainSingle();
        _h.Shares.WriteLog.Count(w => w.Principal == DataversePrincipalRef.User(makerUser)).Should().Be(shareWrites);
        _h.Store.RowsOf(createdId, makersContact).Should().ContainSingle().Which.State.Should().Be(AssignedAccessState.Shared);
    }

    [Fact]
    public async Task OfficeQuickCreate_ForItsMaker_NeverGrantsAnExternalSubjectInline()
    {
        var (makersContact, _) = _h.LinkedContact();
        var others = OthersOnTheMatter();
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        using var host = OfficeHost(factory, makersContact, created, alsoAssign: others.Assign);

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Quick" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var createdId = created.Should().ContainSingle().Subject.Id;
        _h.Grants.Creates.Should().BeEmpty("no contact or organization grant is written in the request");
        _h.Grants.ActiveRowsOf(createdId, others.External).Should().BeEmpty();
        _h.Grants.ActiveRowsOf(createdId, organizationId: others.Firm).Should().BeEmpty();
        _h.Store.RowsOf(createdId, others.External).Should().BeEmpty();
        _h.Store.RowsOf(createdId, others.Firm).Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();

        await RunQueuedGrantAsync(_queued.Should().ContainSingle().Subject);
        _h.Grants.ActiveRowsOf(createdId, others.External).Should().ContainSingle("the queued pass grants it");
    }

    /// <summary>
    /// Task 049b fix round 2 (UAC-r2 ruling b), condition (i): the remainder cannot be queued (the send throws). The FULL
    /// pass runs inline with the create's grantor, so the external subject gets its grant AND its invite, the maker is still
    /// granted, and one neutral line records the queue-down create (no contact id, name or email).
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_ForItsMaker_WhenTheQueueFails_RunsTheFullPassInline_GrantingAndInvitingTheExternalSubject()
    {
        var (makersContact, makerUser) = _h.LinkedContact();
        var others = OthersOnTheMatter();
        const string externalEmail = "partner@outside.test";
        _h.Identities.Contacts[others.External].Email = externalEmail;
        var grantorOid = Guid.NewGuid();
        _h.Grants.SystemUserIdsByOid[grantorOid] = Guid.NewGuid(); // an internal user: the invite rule applies
        var logs = new Sprk.Bff.Api.Tests.Auth.SpeAdmin.RecordingLoggerProvider();
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        using var host = OfficeHost(factory, makersContact, created, queueFails: true, alsoAssign: others.Assign, logs: logs);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", grantorOid.ToString("D"));

        var response = await client.PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Quick" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var createdId = created.Should().ContainSingle().Subject.Id;
        _queued.Should().BeEmpty("the queue failed");
        _inlinePasses.Should().Be(2, "the maker's own grant, then the full pass");
        _h.Shares.MaskOf("sprk_matter", createdId, DataversePrincipalRef.User(makerUser)).Should().NotBeNull("the maker is still granted");
        _h.Shares.MaskOf("sprk_matter", createdId, DataversePrincipalRef.User(others.AttorneyUser)).Should().NotBeNull();
        _h.Grants.ActiveRowsOf(createdId, others.External).Should().ContainSingle("the external subject is granted in the request");
        _h.Grants.ActiveRowsOf(createdId, organizationId: others.Firm).Should().ContainSingle();
        _h.Emails.InviteUrls.Should().ContainSingle("the external subject is invited, as the grantor's pass does");
        _h.Store.RowsOf(createdId, makersContact).Should().ContainSingle().Which.State.Should().Be(AssignedAccessState.Shared);

        var lines = logs.Lines.Where(l => l.Message.Contains(createdId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        var queueDown = lines.Where(l => l.Message.Contains("QueueDownInlinePass")).ToList();
        queueDown.Should().ContainSingle("one neutral line counts a queue-down create").Which.Message
            .Should().Contain("for 4 assigned subject(s)");
        queueDown[0].Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        logs.Lines.Should().NotContain(l => l.Message.Contains("reconciliation job completes it"));
        logs.Lines.Should().NotContain(l => l.Message.Contains("it runs inline"));
        queueDown[0].Message.Should().NotContainAny(externalEmail, others.External.ToString(), makersContact.ToString())
            .And.NotContainEquivalentOf(grantorOid.ToString("D"), "the line names no contact, email or person");
    }

    /// <summary>
    /// Task 049b fix round 2, condition (iii): the fallback runs once. One send is attempted, the full pass is not run twice
    /// (one full-pass line, one maker-only line) and nothing is queued again.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_ForItsMaker_WhenTheQueueFails_TheFullPassRunsOnce_AndIsNotQueuedAgain()
    {
        var (makersContact, _) = _h.LinkedContact();
        var others = OthersOnTheMatter();
        var logs = new Sprk.Bff.Api.Tests.Auth.SpeAdmin.RecordingLoggerProvider();
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        using var host = OfficeHost(factory, makersContact, created, queueFails: true, alsoAssign: others.Assign, logs: logs);

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Quick" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var createdId = created.Should().ContainSingle().Subject.Id;
        _submitAttempts.Should().Be(1, "one send, not retried");
        _queued.Should().BeEmpty();
        _inlinePasses.Should().Be(2, "the maker's pass and exactly one full pass");

        var lines = logs.Lines.Where(l => l.Message.Contains(createdId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        lines.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l.Message, @": \d+ assigned subject\(s\), \d+ write\(s\)"))
            .Should().Be(1, "the full pass ran once");
        lines.Count(l => l.Message.Contains("1 of 4 assigned subject(s) evaluated")).Should().Be(1);
        lines.Count(l => l.Message.Contains("QueueDownInlinePass")).Should().Be(1);
    }

    /// <summary>
    /// Task 049b, condition 3: the maker's inline grant meets a materializer fault (the ledger cannot be read). As before,
    /// the create still succeeds and nothing is written; the full pass is still queued.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_ForItsMaker_AFaultInTheMakersGrant_NeverFailsTheCreate()
    {
        var (makersContact, _) = _h.LinkedContact();
        _h.Store.FailLedgerRead = true;
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        using var host = OfficeHost(factory, makersContact, created);

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Quick" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the reconciliation job repairs it within minutes");
        created.Should().ContainSingle();
        _inlinePasses.Should().Be(1);
        _h.TotalWrites.Should().Be(0);
        _queued.Should().ContainSingle("the full pass is queued whatever the maker's grant answered");
    }

    /// <summary>Task 049 fix round 1 (item 1): a create assigned to someone other than its maker queues the pass.</summary>
    [Theory]
    [InlineData("matter", "sprk_matter")]
    [InlineData("project", "sprk_project")]
    public async Task OfficeQuickCreate_ForAnotherAssignee_QueuesThePass_AndGrantsNothingInline(string type, string table)
    {
        var (makersContact, _) = _h.LinkedContact();
        var (assignee, assigneeUser) = _h.LinkedContact();
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        NamedAssignee(factory, assignee);
        using var host = OfficeHost(factory, makersContact, created);

        var response = await host.CreateClient().PostAsJsonAsync($"/api/office/quickcreate/{type}",
            new QuickCreateRequest { Name = "Quick", AssignedToContactId = assignee });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var createdId = created.Should().ContainSingle().Subject.Id;
        _inlinePasses.Should().Be(0, "no pass runs in the request");
        _h.TotalWrites.Should().Be(0);
        await RunQueuedGrantAsync(_queued.Should().ContainSingle().Subject);
        _h.Shares.MaskOf(table, createdId, DataversePrincipalRef.User(assigneeUser)).Should().NotBeNull("the queued pass shares it");
    }

    /// <summary>
    /// Task 049 fix round 1 (item 1): when the maker's linked contact cannot be resolved, the create cannot tell that its
    /// Assigned-To is its maker, so it queues the pass (fail closed: no inline grant on a guess).
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheMakersContactLookupFaults_QueuesThePass()
    {
        var (makersContact, _) = _h.LinkedContact();
        var identity = new Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Simulated identity read failure."));
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        NamedAssignee(factory, makersContact);
        using var host = OfficeHost(factory, makersContact, created, identity);

        // The request names the maker's own contact, so the create does not need the lookup to fill it.
        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter",
            new QuickCreateRequest { Name = "Quick", AssignedToContactId = makersContact });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        created.Should().ContainSingle();
        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce(),
            "the create asked who its maker's contact is");
        _inlinePasses.Should().Be(0, "an unanswered lookup never grants inline");
        _h.TotalWrites.Should().Be(0);
        _queued.Should().ContainSingle();
    }

    /// <summary>
    /// Task 049 fix round 1 (F3): the queue fails, so the pass runs inline, and that pass meets a materializer fault (the
    /// ledger cannot be read). The create still succeeds, and nothing is shared.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_AMaterializerFault_NeverFailsTheCreate()
    {
        var (makersContact, _) = _h.LinkedContact();
        var (assignee, _) = _h.LinkedContact();
        _h.Store.FailLedgerRead = true;
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        NamedAssignee(factory, assignee);
        using var host = OfficeHost(factory, makersContact, created, queueFails: true);

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter",
            new QuickCreateRequest { Name = "Quick", AssignedToContactId = assignee });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the reconciliation job repairs it within minutes");
        created.Should().ContainSingle();
        _queued.Should().BeEmpty("the queue failed");
        _inlinePasses.Should().Be(1, "the failed enqueue ran the pass inline, over the fault");
        _h.Shares.Writes.Should().BeEmpty();
        _h.TotalWrites.Should().Be(0);
    }

    /// <summary>Task 049 fix round 1 (F3): the same fault in the queued job fails the job, and it writes nothing.</summary>
    [Fact]
    public async Task OfficeQuickCreate_AMaterializerFault_FailsItsQueuedJob_WithoutWriting()
    {
        var (makersContact, _) = _h.LinkedContact();
        var (assignee, _) = _h.LinkedContact();
        _h.Store.FailLedgerRead = true;
        var created = new List<(string Table, Guid Id)>();
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        NamedAssignee(factory, assignee);
        using var host = OfficeHost(factory, makersContact, created);

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter",
            new QuickCreateRequest { Name = "Quick", AssignedToContactId = assignee });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var outcome = await RunQueuedJobAsync(_queued.Should().ContainSingle().Subject);

        outcome.Status.Should().Be(Sprk.Bff.Api.Services.Jobs.JobStatus.Failed, "it is redelivered");
        outcome.ErrorMessage.Should().Be("failed:ledger-unreadable");
        _h.TotalWrites.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UpdateRecordActionCore (AI playbook UpdateRecord node + the Job B ActionSeam)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecordActionCore_ThatWritesAnAssignedLookup_MaterializesTheRoot()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var fieldMappings = new Mock<IFieldMappingDataverseService>();
        var core = new UpdateRecordActionCore(fieldMappings.Object, Scopes(), NullLogger.Instance);

        await core.UpdateAsync(new UpdateRecordActionInput(
            "sprk_matter", matter, null, null,
            new[] { new RenderedLookup(Attorney1, "contact", contact.ToString("D")) }), CancellationToken.None);

        fieldMappings.Verify(f => f.UpdateExistingRecordFieldsAsync("sprk_matter", matter, It.IsAny<Dictionary<string, object?>>(),
            null, It.IsAny<CancellationToken>()), Times.Once);
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateRecordActionCore_ThatWritesNoRegistryColumn_MaterializesNothing()
    {
        var matter = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, _h.Contact());
        var core = new UpdateRecordActionCore(new Mock<IFieldMappingDataverseService>().Object, Scopes(), NullLogger.Instance);

        await core.UpdateAsync(new UpdateRecordActionInput(
            "sprk_matter", matter, null, new Dictionary<string, string?> { ["sprk_mattername"] = "Renamed" }, null),
            CancellationToken.None);

        _h.TotalWrites.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The AI chat tools (dataverse.update_record / dataverse.create_record)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatUpdateRecord_OfAnAssignedColumn_MaterializesTheRoot_AfterThePatchCommitted()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_matters", "PrimaryIdAttribute": "sprk_matterid" }""")));
        dataverse.Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));
        var handler = new DataverseUpdateRecordHandler(dataverse.Object, new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$$"""{"tablename":"sprk_matter","recordId":"{{{matter:D}}}","item":{"{{{Attorney1}}}":"{{{contact:D}}}"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseUpdateRecordHandler), name: "SYS-Dataverse Update Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task ChatUpdateRecord_ThatTheUserIsRefused_MaterializesNothing()
    {
        var matter = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, _h.Contact());
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_matters", "PrimaryIdAttribute": "sprk_matterid" }""")));
        dataverse.Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, "0x80040220", "Principal user is missing prvWritesprk_matter"));
        var handler = new DataverseUpdateRecordHandler(dataverse.Object, new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$$"""{"tablename":"sprk_matter","recordId":"{{{matter:D}}}","item":{"{{{Attorney1}}}":"x"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseUpdateRecordHandler), name: "SYS-Dataverse Update Record"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        _h.TotalWrites.Should().Be(0, "nothing committed, so nothing is materialized");
    }

    [Fact]
    public async Task ChatCreateRecord_OfARoot_MaterializesTheCreatedRecord()
    {
        var createdId = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Project, createdId, Attorney1, contact);
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_projects", "PrimaryIdAttribute": "sprk_projectid" }""")));
        dataverse.Setup(d => d.PostAsync("/api/data/v9.2/sprk_projects", It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, Json($$"""{ "sprk_projectid": "{{createdId:D}}" }""")));
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), new Mock<IFieldMappingDataverseService>().Object, Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes()), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_project","item":{"sprk_projectname":"New"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(createdId, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task ChatCreateRecord_OfAChildTable_MaterializesNothing()
    {
        var createdId = Guid.NewGuid();
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_events", "PrimaryIdAttribute": "sprk_eventid" }""")));
        dataverse.Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, Json($$"""{ "sprk_eventid": "{{createdId:D}}" }""")));
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), new Mock<IFieldMappingDataverseService>().Object, Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes()), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_event","item":{"sprk_eventname":"Hearing"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.TotalWrites.Should().Be(0, "owner A6: a child entity's assignee gets no root grant");
    }

    /// <summary>
    /// Batch 4 integration (task 142 x task 146): since owner round 7 item 3 a root created from chat is created by the
    /// APPLICATION, owned by the resolver's team (<c>OwnedChildWrite</c>) — that path returns before the run-as-user tail,
    /// so it runs the Assigned-To materializer itself, after the create committed.
    /// </summary>
    [Fact]
    public async Task ChatCreateRecord_OfARoot_OnTheOwnedPath_MaterializesTheCreatedRecord()
    {
        var contact = _h.Contact();
        Guid? createdId = null;
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json(
                """{ "EntitySetName": "sprk_projects", "PrimaryIdAttribute": "sprk_projectid", "OwnershipType": "UserOwned" }""")));
        dataverse.Setup(d => d.GetAsync("WhoAmI()", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json($$"""{ "UserId": "{{Guid.NewGuid():D}}" }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')?$select=LogicalName,Privileges")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json(
                """{ "LogicalName": "sprk_project", "Privileges": [ { "PrivilegeType": "Create", "Name": "prvCreatesprk_project" } ] }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.Contains("RetrieveUserSetOfPrivilegesByNames")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "RolePrivileges": [ { "PrivilegeName": "prvCreatesprk_project" } ] }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')/Attributes")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "value": [] }""")));
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly.Setup(a => a.UpdateRecordFieldsAsync(
                "sprk_project", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>())) // task 166 (S-67): the push now writes AS the caller
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, id, _, _, _) =>
            {
                // The created row names the contact in an "Assigned *" column (what the materializer reads back).
                createdId = id;
                _h.Store.Assign(ExternalGrantRootType.Project, id, Attorney1, contact);
            })
            .Returns(Task.CompletedTask);
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), appOnly.Object, Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes()), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_project","item":{"sprk_projectname":"New"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        createdId.Should().NotBeNull("the application created the root (the owned path)");
        dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "a run-as-user POST would mean the owned path was not taken");
        _h.Grants.ActiveRowsOf(createdId!.Value, contact).Should().ContainSingle();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Field Mapping push (a parent's mapped values pushed to its children)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FieldMappingPush_ThatWritesAnAssignedColumnOfARootChild_MaterializesIt()
    {
        var project = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Project, project, Attorney1, contact);
        var dataverse = new Mock<IFieldMappingDataverseService>();

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            dataverse.Object,
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper,
            new[] { new FieldMappingRuleDto { SourceField = Attorney1, TargetField = Attorney1, SourceFieldType = "Text", TargetFieldType = "Text" } },
            new Dictionary<string, object?> { [Attorney1] = contact.ToString("D") },
            "sprk_project",
            new[] { project },
            // Task 166 (S-67): the push writes each child AS the caller (MSCRMCallerID).
            Guid.Parse("0000c166-0000-0000-0000-00000000ca11"),
            NullLogger.Instance,
            CancellationToken.None,
            Scopes());

        updated.Should().Be(1);
        failed.Should().Be(0);
        _h.Grants.ActiveRowsOf(project, contact).Should().ContainSingle();
    }
}
