using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Field-mapping-server-write-path-r1 task 049 Part B (UAC-r2's conditions 1-4): a created root's "Assigned *" pass runs
/// on <see cref="AssignedAccessCreateGrantJobHandler"/> after the create returned. The REAL materializer runs over
/// <see cref="Harness"/>, so each test asserts the access and ledger that resulted. The create side is driven through the
/// chat create's owned path, which calls <c>RecordCreationService</c> (the same setup as
/// <see cref="AssignedAccessWriterTriggerTests"/>). KEEP path: <c>tests/integration/data-mutation/**</c>.
/// </summary>
public sealed class AssignedAccessCreateGrantJobTests : TypedToolHandlerTestFixture
{
    private const string Attorney1 = "sprk_assignedattorney1";

    private readonly Harness _h = new();

    /// <summary>The deployment's tenant (<c>AzureAd:TenantId</c>) the handler's host is configured with; <c>null</c> = unset.</summary>
    private string? DeploymentTenant { get; set; } = TestTenant;
    private readonly ProcessLocalScheduledJobLease _lease = new();
    private readonly List<JobContract> _queued = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // The create: queued, nothing granted before the job; a failed enqueue grants inline
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOfARoot_QueuesOneJobNamingOnlyTheRoot_AndGrantsNothingUntilTheJobRuns()
    {
        var contact = _h.Contact();

        var (result, createdId) = await ChatCreateProjectAsync(contact, Scopes(QueueThatRecords()));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.TotalWrites.Should().Be(0, "the create returned before the grant: the assignee has no access from it yet");

        var job = _queued.Should().ContainSingle().Subject;
        job.JobType.Should().Be(AssignedAccessCreateGrantJobHandler.JobTypeName);
        job.IdempotencyKey.Should().Be($"assigned-access-create:{createdId:N}");
        var payload = job.Payload!.RootElement;
        payload.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["RootType", "RootId", "GrantorOid", "CacheTenants"], "UAC-r2 condition 2: the root, the grantor and the cache tenants only");
        payload.GetProperty("RootType").GetString().Should().Be("Project");
        payload.GetProperty("RootId").GetGuid().Should().Be(createdId);
        payload.GetProperty("GrantorOid").ValueKind.Should().Be(JsonValueKind.Null, "the chat create names no grantor (task 017 r3)");
        payload.GetProperty("CacheTenants").EnumerateArray().Select(t => t.GetString()).Should().Equal(TestTenant);
        job.Payload.RootElement.GetRawText().Should().NotContain(contact.ToString("D"), "no assignee travels in the message");

        var outcome = await Handler().ProcessAsync(Delivered(job), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, outcome.ErrorMessage);
        _h.Grants.ActiveRowsOf(createdId, contact).Should().ContainSingle("the job grants the assignee");
    }

    [Fact]
    public async Task CreateOfARoot_WhenTheJobCannotBeQueued_GrantsInline_AsBefore()
    {
        var contact = _h.Contact();
        var jobs = Queue();
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Simulated send failure.", ServiceBusFailureReason.ServiceCommunicationProblem));

        var (result, createdId) = await ChatCreateProjectAsync(contact, Scopes(jobs));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(createdId, contact).Should().ContainSingle("an enqueue failure runs the pass inline");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The job: idempotent, re-reads the current assignment, L1 semantics, serialized per root
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Job_TwoDeliveries_LeaveTheSameLedger()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var handler = Handler();

        (await handler.ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None))
            .Status.Should().Be(JobStatus.Completed);
        var ledger = LedgerJson();
        var writes = _h.TotalWrites;
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();

        (await handler.ProcessAsync(Job(ExternalGrantRootType.Matter, matter, delivery: 2), CancellationToken.None))
            .Status.Should().Be(JobStatus.Completed);

        _h.TotalWrites.Should().Be(writes, "a second delivery writes nothing");
        LedgerJson().Should().Be(ledger);
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task Job_AReassignBetweenTheCreateAndTheJob_GrantsOnlyTheNewAssignee()
    {
        var matter = Guid.NewGuid();
        var created = _h.Contact();
        var reassigned = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, created); // the row as created
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, reassigned); // changed before the job ran

        (await Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None))
            .Status.Should().Be(JobStatus.Completed);

        _h.Grants.ActiveRowsOf(matter, reassigned).Should().ContainSingle();
        _h.Grants.ActiveRowsOf(matter, created).Should().BeEmpty();
        _h.Store.RowsOf(matter, created).Should().BeEmpty("the job never saw the earlier assignee");
    }

    [Fact]
    public async Task Job_AReassignAfterAnEarlierPassGranted_RemovesTheEarlierAutoGrant_AsTheL1PassDoes()
    {
        var matter = Guid.NewGuid();
        var earlier = _h.Contact();
        var later = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, earlier);
        (await _h.SyncAsync(ExternalGrantRootType.Matter, matter)).Complete.Should().BeTrue(); // e.g. the wizard's sync call
        _h.Grants.ActiveRowsOf(matter, earlier).Should().ContainSingle();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, later);

        (await Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None))
            .Status.Should().Be(JobStatus.Completed);

        _h.Grants.ActiveRowsOf(matter, later).Should().ContainSingle();
        _h.Grants.ActiveRowsOf(matter, earlier).Should().BeEmpty(
            "the job keeps L1 semantics (revoke-on-change on), unlike the reconciliation job's report-only default");
    }

    [Fact]
    public async Task Job_TwoConcurrentRunsOnOneRoot_AreSerialized()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var gate = new GatedOwnership(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble());
        _h.Ownership = gate;

        var first = Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); // the first pass is writing its grant
        var second = Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, matter, delivery: 2), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        second.IsCompleted.Should().BeFalse("the second run waits for the root's lease");
        gate.MaxInFlight.Should().Be(1);

        gate.Release();
        var outcomes = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        outcomes.Select(o => o.Status).Should().AllBeEquivalentTo(JobStatus.Completed);
        gate.MaxInFlight.Should().Be(1, "the two passes never overlapped");
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
        _h.Store.RowsOf(matter, contact).Should().ContainSingle();
    }

    /// <summary>
    /// Adversarial review F1: an update path's inline pass takes no lease. An update that re-assigns the root while the
    /// job's pass runs finds no earlier grant to revoke, and the job's pass then grants the earlier assignee from its stale
    /// read. The job's next pass removes it, so the earlier assignee ends with no access.
    /// </summary>
    [Fact]
    public async Task Job_AnUpdateThatReassignsDuringThePass_LeavesOnlyTheNewAssigneeWithAccess()
    {
        var matter = Guid.NewGuid();
        var created = _h.Contact();
        var reassigned = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, created);
        var gate = new GatedOwnership(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble());
        _h.Ownership = gate;

        var job = Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); // the job read the root as created, and is granting

        // An update path re-assigns the root and runs its own inline pass (the L1 trigger, no lease).
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, reassigned);
        await _h.Materializer.AfterWriteAsync("sprk_matter", matter, [Attorney1], grantorOid: null, CancellationToken.None);
        _h.Grants.ActiveRowsOf(matter, reassigned).Should().ContainSingle();

        gate.Release();
        var outcome = await job.WaitAsync(TimeSpan.FromSeconds(10));

        outcome.Status.Should().Be(JobStatus.Completed, outcome.ErrorMessage);
        _h.Grants.ActiveRowsOf(matter, created).Should().BeEmpty("a grant written from a stale read never outlives the job");
        _h.Grants.ActiveRowsOf(matter, reassigned).Should().ContainSingle();
    }

    /// <summary>
    /// Task 049 fix round 1 (K2): the same re-assign during the pass, on a root whose second assignee cannot be shared (an
    /// internal user; every share write fails). The pass that grants the earlier assignee from its stale read is therefore
    /// incomplete. The job settles before it reports the failure, so the earlier assignee ends with no grant.
    /// </summary>
    [Fact]
    public async Task Job_AReassignDuringAPassThatAlsoFails_LeavesTheEarlierAssigneeWithNoGrant()
    {
        const string Attorney2 = "sprk_assignedattorney2";
        var matter = Guid.NewGuid();
        var created = _h.Contact();
        var reassigned = _h.Contact();
        var (unshareable, _) = _h.LinkedContact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, created);
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney2, unshareable);
        _h.Shares.WriteFailure = new HttpRequestException("Simulated share write failure.");
        var gate = new GatedOwnership(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble());
        _h.Ownership = gate;
        var passEnds = new PassEndLogger();
        _h.Logger = passEnds;

        var job = Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); // the job read the root as created, and is granting

        // An update path re-assigns the root and runs its own inline pass (the L1 trigger, no lease).
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, reassigned);
        await _h.Materializer.AfterWriteAsync("sprk_matter", matter, [Attorney1], grantorOid: null, CancellationToken.None);
        _h.Grants.ActiveRowsOf(matter, reassigned).Should().ContainSingle();

        passEnds.OnNextPassEnd = () => { }; // count the job's passes from here
        gate.Release();
        var outcome = await job.WaitAsync(TimeSpan.FromSeconds(10));

        passEnds.FirstPassEnd.Should().NotContain(" 0 failure(s)", "the precondition: the stale pass failed")
            .And.NotContain(" 0 write(s)", "and wrote the earlier assignee's grant");
        passEnds.PassesEnded.Should().BeGreaterThan(1, "a failing pass that wrote is followed by another");
        outcome.Status.Should().Be(JobStatus.Failed, "the share still fails, so the delivery is retried");
        _h.Grants.ActiveRowsOf(matter, created).Should().BeEmpty("a grant written from a stale read never outlives the job");
        _h.Grants.ActiveRowsOf(matter, reassigned).Should().ContainSingle();
    }

    [Fact]
    public async Task Job_ForARootDeletedSinceTheCreate_CompletesAndWritesNothing()
    {
        var outcome = await Handler().ProcessAsync(Job(ExternalGrantRootType.Matter, Guid.NewGuid()), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, "a root that no longer exists is an answer, not a failure");
        _h.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task CreateOfARoot_WhenTheEnqueueOutlastsItsBound_GrantsInline()
    {
        var contact = _h.Contact();
        var jobs = Queue();
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct)); // a Service Bus that never answers

        var (result, createdId) = await ChatCreateProjectAsync(contact, Scopes(jobs));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(createdId, contact).Should().ContainSingle(
            $"an enqueue still pending after {AssignedAccessCreateGrantJobHandler.EnqueueTimeout.TotalSeconds:0} s runs the pass inline");
    }

    [Fact]
    public async Task Job_WhenTheRootsLeaseStaysHeld_IsRedelivered_AndGrantsNothing()
    {
        var matter = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, _h.Contact());
        var held = await _lease.TryAcquireAsync(
            NoAccessShareEnforcer.RecordLockId("sprk_matter", matter), null, TimeSpan.FromMinutes(5), CancellationToken.None);
        held.Status.Should().Be(ScheduledJobLeaseStatus.Granted);

        var outcome = await Handler(lockWait: TimeSpan.FromMilliseconds(150))
            .ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        outcome.ErrorMessage.Should().Be(AssignedAccessCreateGrantJobHandler.RecordBusyKind);
        _h.TotalWrites.Should().Be(0);
    }

    [Fact]
    public async Task Job_APassThatFails_IsRetried_ThenDeadLettered_LoggingTheRootAndKindOnly()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Names[contact] = "Avery Example";
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        _h.Store.FailLedgerRead = true;
        var logger = new CapturingLogger();
        var handler = Handler(logger);

        var retried = await handler.ProcessAsync(Job(ExternalGrantRootType.Matter, matter), CancellationToken.None);
        var last = await handler.ProcessAsync(
            Job(ExternalGrantRootType.Matter, matter, delivery: JobContract.MaxDeliveryCount), CancellationToken.None);

        retried.Status.Should().Be(JobStatus.Failed);
        last.Status.Should().Be(JobStatus.Poisoned);
        last.ErrorMessage.Should().Be("failed:ledger-unreadable");
        _h.TotalWrites.Should().Be(0, "nothing is granted by a pass that failed");

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains("[ASSIGNED-ACCESS]") && e.Message.Contains(matter.ToString("D"))
            && e.Message.Contains("failed:ledger-unreadable"));
        logger.Entries.Should().OnlyContain(e => !e.Message.Contains(contact.ToString("D"))
            && !e.Message.Contains("Avery") && !e.Message.Contains('@'), "no name, email or assignee in the job's own lines");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fix round 3 (K3): the message is not trusted for the grantor or the cache tenant
    // ─────────────────────────────────────────────────────────────────────────────

    private static AssignedAccessCreateGrantPayload PayloadFor(string? grantorOid, params string[] tenants) =>
        new(ExternalGrantRootType.Matter, Guid.NewGuid(), grantorOid, tenants);

    [Fact]
    public async Task Job_AGrantorWhoIsAnActiveSystemUser_IsUsed()
    {
        var oid = Guid.NewGuid();
        _h.Grants.SystemUserIdsByOid[oid] = Guid.NewGuid();

        var request = await Handler().RequestForAsync(PayloadFor(oid.ToString("D")), CancellationToken.None);

        request.GrantorOid.Should().Be(oid.ToString("D"));
        request.RevokeOnChange.Should().BeTrue();
        request.Trigger.Should().Be(AssignedAccessTrigger.Inline);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("not-a-guid")]
    [InlineData("empty-guid")]
    [InlineData("lookup-fails")]
    public async Task Job_AGrantorWhoIsNotAnActiveSystemUser_RunsWithNoGrantor_WarnsOnce_AndStillGrants(string kind)
    {
        var oid = Guid.NewGuid();
        var payloadOid = kind switch
        {
            "not-a-guid" => "not-a-guid",
            "empty-guid" => Guid.Empty.ToString("D"),
            _ => oid.ToString("D"),
        };
        if (kind == "disabled")
        {
            _h.Grants.SystemUserIdsByOid[oid] = Guid.NewGuid();
            _h.Grants.DisabledSystemUserOids[oid] = true;
        }

        if (kind == "lookup-fails")
            _h.Grants.FailSystemUserQueries = true;

        var logger = new CapturingLogger();
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var payload = new AssignedAccessCreateGrantPayload(ExternalGrantRootType.Matter, matter, payloadOid, [TestTenant]);

        var request = await Handler(logger).RequestForAsync(payload, CancellationToken.None);
        var outcome = await Handler(logger).ProcessAsync(
            Delivered(AssignedAccessCreateGrantJobHandler.NewJob(payload, Guid.NewGuid().ToString("D"))), CancellationToken.None);

        request.GrantorOid.Should().BeNull("an oid that is not an active systemuser is not used");
        outcome.Status.Should().Be(JobStatus.Completed, "the grant is never failed for the grantor");
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("runs with no grantor"));
        logger.Entries.Should().OnlyContain(e => !e.Message.Contains(oid.ToString("D")), "the oid is not logged");
    }

    [Fact]
    public async Task Job_NoGrantorInTheMessage_NeedsNoLookup_AndLogsNoWarning()
    {
        _h.Grants.FailSystemUserQueries = true;
        var logger = new CapturingLogger();

        var request = await Handler(logger).RequestForAsync(PayloadFor(null), CancellationToken.None);

        request.GrantorOid.Should().BeNull();
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Job_TheCacheTenant_IsTheDeploymentsOwn_NeverTheMessages()
    {
        var request = await Handler().RequestForAsync(PayloadFor(null, "11111111-1111-1111-1111-111111111111", "another"),
            CancellationToken.None);

        request.CacheTenants.Should().Equal(TestTenant);
    }

    [Fact]
    public async Task Job_WithNoDeploymentTenantConfigured_ClearsNoTenant_WhateverTheMessageNames()
    {
        DeploymentTenant = null;

        var request = await Handler().RequestForAsync(PayloadFor(null, "11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        request.CacheTenants.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_ThatNamesNoRoot_IsDeadLettered_AndWritesNothing()
    {
        var job = new JobContract
        {
            JobType = AssignedAccessCreateGrantJobHandler.JobTypeName,
            Payload = JsonDocument.Parse("""{ "RootType": "Matter", "RootId": "00000000-0000-0000-0000-000000000000" }"""),
        };

        var outcome = await Handler().ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        _h.TotalWrites.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────────

    private AssignedAccessCreateGrantJobHandler Handler(ILogger<AssignedAccessCreateGrantJobHandler>? logger = null, TimeSpan? lockWait = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.Materializer);
        services.AddSingleton<DataverseWebApiClient>(_h.Grants);
        if (DeploymentTenant is not null)
        {
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = DeploymentTenant }).Build());
        }

        return new AssignedAccessCreateGrantJobHandler(
            services.BuildServiceProvider(), _lease, logger ?? NullLogger<AssignedAccessCreateGrantJobHandler>.Instance)
        {
            LockWait = lockWait ?? TimeSpan.FromSeconds(10),
            LockRetryInterval = TimeSpan.FromMilliseconds(20),
        };
    }

    private static JobContract Job(ExternalGrantRootType type, Guid rootId, string? grantorOid = null, int delivery = 1) =>
        Delivered(AssignedAccessCreateGrantJobHandler.NewJob(
            new AssignedAccessCreateGrantPayload(type, rootId, grantorOid, [TestTenant]), Guid.NewGuid().ToString("D")), delivery);

    /// <summary>The job as the processor hands it over: read back from its JSON, with the broker's delivery count.</summary>
    private static JobContract Delivered(JobContract job, int delivery = 1)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var received = JsonSerializer.Deserialize<JobContract>(JsonSerializer.Serialize(job, options), options)!;
        received.DeliveryCount = delivery;
        return received;
    }

    private string LedgerJson() => JsonSerializer.Serialize(_h.Store.Ledger.OrderBy(r => r.LedgerKey).ToList());

    private static Mock<JobSubmissionService> Queue() => new(
        MockBehavior.Strict,
        Options.Create(new ServiceBusOptions { QueueName = "sdap-jobs" }),
        Mock.Of<ILogger<JobSubmissionService>>(),
        new Mock<ServiceBusClient>().Object);

    private Mock<JobSubmissionService> QueueThatRecords()
    {
        var jobs = Queue();
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => _queued.Add(job))
            .Returns(Task.CompletedTask);
        return jobs;
    }

    /// <summary>The host's scopes: the harness's materializer, the job queue and the configuration (the cache tenant).</summary>
    private IServiceScopeFactory Scopes(Mock<JobSubmissionService> jobs)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.Materializer);
        services.AddSingleton(jobs.Object);
        services.AddSingleton(_h.Configuration);
        services.AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// A project created from chat on the owned path (<c>RecordCreationService</c>, as the caller), whose created row names
    /// <paramref name="contact"/> in an "Assigned *" column — <see cref="AssignedAccessWriterTriggerTests"/>' owned-path setup.
    /// </summary>
    private async Task<(Sprk.Bff.Api.Services.Ai.ToolResult Result, Guid CreatedId)> ChatCreateProjectAsync(
        Guid contact, IServiceScopeFactory scopes)
    {
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
                It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, id, _, _, _) =>
            {
                createdId = id;
                _h.Store.Assign(ExternalGrantRootType.Project, id, Attorney1, contact);
            })
            .Returns(Task.CompletedTask);
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
            Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), appOnly.Object,
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), scopes),
            scopes);

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_project","item":{"sprk_projectname":"New"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        createdId.Should().NotBeNull("the application created the root (the owned path)");
        return (result, createdId!.Value);
    }

    /// <summary>
    /// The grant core's owner question (a new grant row's team), held at a gate: it shows when a pass is writing a grant,
    /// and how many passes are inside it at once.
    /// </summary>
    private sealed class GatedOwnership(IRecordOwnershipResolver inner) : IRecordOwnershipResolver
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _maxInFlight;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public void Release() => _release.TrySetResult();

        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct) =>
            inner.ResolveOwningTeamAsync(context, ct);

        public async Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while ((seen = Volatile.Read(ref _maxInFlight)) < now
                   && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
            {
            }

            try
            {
                // Only the FIRST grant write waits at the gate; every later one passes (and is still counted in flight).
                if (Entered.TrySetResult())
                    await _release.Task.ConfigureAwait(false);
                return await inner.ResolveOwnerAsync(context, ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(request, applyChange, ct);
    }

    /// <summary>
    /// The materializer's logger: counts the passes that end once <see cref="OnNextPassEnd"/> is set, and runs that action
    /// at the first of them (its "assigned subject(s)" summary line).
    /// </summary>
    private sealed class PassEndLogger : ILogger<AssignedAccessMaterializer>
    {
        private int _passesEnded;

        public Action? OnNextPassEnd { get; set; }

        public int PassesEnded => Volatile.Read(ref _passesEnded);

        /// <summary>The summary line of the first pass that ended once armed.</summary>
        public string? FirstPassEnd { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (OnNextPassEnd is not { } armed || !message.Contains("assigned subject(s)", StringComparison.Ordinal))
                return;

            if (Interlocked.Increment(ref _passesEnded) == 1)
            {
                FirstPassEnd = message;
                armed();
            }
        }
    }

    private sealed class CapturingLogger : ILogger<AssignedAccessCreateGrantJobHandler>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
