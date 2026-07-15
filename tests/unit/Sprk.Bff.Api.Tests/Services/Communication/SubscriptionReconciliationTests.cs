using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Unit tests for the FR-24 subscription-resilience surface:
/// - MailboxDeltaReconciliationService: delta-query reconciliation feeds missed messages into the
///   IncomingCommunication job path, is idempotent (exactly-once per message id), and is non-fatal.
/// - GraphSubscriptionManager.HandleLifecycleNotificationAsync: the three lifecycle events dispatch
///   to renew/recreate/reconcile without human intervention.
///
/// Graph is mocked at the GraphMailFolderDeltaReader.QueryDeltaAsync virtual seam (the Graph SDK
/// sealed types are unmockable — see InboundPipelineTests), and Service Bus at the
/// JobSubmissionService boundary.
/// </summary>
public class SubscriptionReconciliationTests
{
    private const string Mailbox = "shared@contoso.com";

    // ── Builders ────────────────────────────────────────────────────────────────

    private static CommunicationAccount CreateReceiveAccount(
        Guid? id = null,
        string email = Mailbox,
        string? subscriptionId = null,
        string? monitorFolder = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Test Account",
            EmailAddress = email,
            AccountType = AccountType.SharedAccount,
            ReceiveEnabled = true,
            SubscriptionId = subscriptionId,
            MonitorFolder = monitorFolder
        };

    private static DeltaMessage Msg(string id, bool removed = false)
        => new(MessageId: id, InternetMessageId: $"<{id}@contoso.com>", Subject: $"Subject {id}", IsRemoved: removed);

    /// <summary>
    /// GraphMailFolderDeltaReader is a concrete class (ADR-010 — no interface); its <c>QueryDeltaAsync</c>
    /// is virtual, so tests substitute it via a loose mock over the concrete type.
    /// </summary>
    private static Mock<GraphMailFolderDeltaReader> MockReader()
        => new(MockBehavior.Loose,
            Mock.Of<IGraphClientFactory>(),
            Mock.Of<ILogger<GraphMailFolderDeltaReader>>());

    private static JobSubmissionService MockJobSubmission(List<JobContract> captured, Func<int, bool>? throwOnCall = null)
    {
        var optionsMock = new Mock<IOptions<ServiceBusOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new ServiceBusOptions());

        var mock = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            optionsMock.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);

        var calls = 0;
        mock.Setup(s => s.SubmitCommunicationJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>((job, _) =>
            {
                calls++;
                if (throwOnCall is not null && throwOnCall(calls))
                    throw new InvalidOperationException("simulated Service Bus failure");
                captured.Add(job);
                return Task.CompletedTask;
            });

        return mock.Object;
    }

    private static MailboxDeltaReconciliationService CreateSut(
        GraphMailFolderDeltaReader reader,
        JobSubmissionService jobSubmission,
        CommunicationAccountService? accountService = null)
        => new(
            reader,
            accountService ?? CreateAccountService(),
            jobSubmission,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<MailboxDeltaReconciliationService>>());

    private static CommunicationAccountService CreateAccountService(params CommunicationAccount[] accounts)
    {
        var dataverseMock = new Mock<IDataverseService>();

        var entities = accounts.Select(a =>
        {
            var e = new DataverseEntity("sprk_communicationaccount") { Id = a.Id };
            e["sprk_emailaddress"] = a.EmailAddress;
            e["sprk_name"] = a.Name;
            e["sprk_accounttype"] = new OptionSetValue(100000000);
            e["sprk_receiveenabled"] = a.ReceiveEnabled;
            e["sprk_graphsubscriptionid"] = a.SubscriptionId;
            e["sprk_monitorfolder"] = a.MonitorFolder;
            return e;
        }).ToArray();

        dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.Is<string>(f => f.Contains("sprk_receiveenabled")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);

        return new CommunicationAccountService(
            dataverseMock.Object,
            dataverseMock.Object,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
    }

    // ── MailboxDeltaReconciliationService ───────────────────────────────────────

    [Fact]
    public async Task ReconcileAccountAsync_WhenDeltaReturnsMissedMessage_EnqueuesIncomingCommunicationJob()
    {
        var captured = new List<JobContract>();
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reader = MockReader();
        reader.Setup(r => r.QueryDeltaAsync(Mailbox, "Inbox", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailFolderDeltaResult(new[] { Msg("AAA") }, "delta-cursor-1"));

        var sut = CreateSut(reader.Object, MockJobSubmission(captured));

        var enqueued = await sut.ReconcileAccountAsync(account, CancellationToken.None);

        enqueued.Should().Be(1);
        captured.Should().HaveCount(1);
        var job = captured[0];
        job.JobType.Should().Be("IncomingCommunication");
        job.IdempotencyKey.Should().Be("Communication:AAA:Process",
            "reconciled messages must share the canonical idempotency key with webhook + polling deliveries");
        job.SubjectId.Should().Be("AAA");

        // The resource path feeds IncomingCommunicationProcessor via the job handler.
        using var payload = job.Payload!;
        payload.RootElement.GetProperty("Resource").GetString()
            .Should().Be($"users/{Mailbox}/mailFolders/Inbox/messages/AAA");
        payload.RootElement.GetProperty("TriggerSource").GetString().Should().Be("DeltaReconciliation");
    }

    [Fact]
    public async Task ReconcileAccountAsync_WhenSameMessageAppearsTwice_EnqueuesExactlyOnce()
    {
        // Idempotency (exactly once): a message id repeated within a delta batch enqueues one job,
        // and that job's stable idempotency key lets downstream dedup collapse cross-source duplicates.
        var captured = new List<JobContract>();
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reader = MockReader();
        reader.Setup(r => r.QueryDeltaAsync(Mailbox, "Inbox", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailFolderDeltaResult(new[] { Msg("DUP"), Msg("DUP") }, "delta-cursor-1"));

        var sut = CreateSut(reader.Object, MockJobSubmission(captured));

        var enqueued = await sut.ReconcileAccountAsync(account, CancellationToken.None);

        enqueued.Should().Be(1, "the duplicate message id must be captured exactly once");
        captured.Should().ContainSingle();
        captured[0].IdempotencyKey.Should().Be("Communication:DUP:Process");
    }

    [Fact]
    public async Task ReconcileAccountAsync_WhenMessageIsRemoved_DoesNotEnqueue()
    {
        var captured = new List<JobContract>();
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reader = MockReader();
        reader.Setup(r => r.QueryDeltaAsync(Mailbox, "Inbox", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailFolderDeltaResult(new[] { Msg("DEL", removed: true) }, "delta-cursor-1"));

        var sut = CreateSut(reader.Object, MockJobSubmission(captured));

        var enqueued = await sut.ReconcileAccountAsync(account, CancellationToken.None);

        enqueued.Should().Be(0);
        captured.Should().BeEmpty("removed/deleted messages are not inbound communications");
    }

    [Fact]
    public async Task ReconcileAccountAsync_WhenDeltaReaderThrows_IsNonFatalAndReturnsZero()
    {
        // NFR-06: a reconciliation failure must not crash the caller.
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reader = MockReader();
        reader.Setup(r => r.QueryDeltaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Graph transient failure"));

        var sut = CreateSut(reader.Object, MockJobSubmission(new List<JobContract>()));

        var act = async () => await sut.ReconcileAccountAsync(account, CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await sut.ReconcileAccountAsync(account, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAccountAsync_WhenOneEnqueueThrows_ContinuesWithRemainingMessages()
    {
        // NFR-06: a per-message enqueue failure must not abort the rest of the batch.
        var captured = new List<JobContract>();
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reader = MockReader();
        reader.Setup(r => r.QueryDeltaAsync(Mailbox, "Inbox", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailFolderDeltaResult(new[] { Msg("M1"), Msg("M2") }, "delta-cursor-1"));

        var sut = CreateSut(reader.Object, MockJobSubmission(captured, throwOnCall: call => call == 1));

        var enqueued = await sut.ReconcileAccountAsync(account, CancellationToken.None);

        enqueued.Should().Be(1);
        captured.Should().ContainSingle().Which.SubjectId.Should().Be("M2");
    }

    [Fact]
    public async Task ReconcileAllAsync_WhenAccountReconciliationThrows_DoesNotThrow()
    {
        // NFR-06: one account failing must not abort the whole cycle.
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reader = MockReader();
        reader.Setup(r => r.QueryDeltaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = CreateSut(reader.Object, MockJobSubmission(new List<JobContract>()), CreateAccountService(account));

        var act = async () => await sut.ReconcileAllAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── GraphSubscriptionManager lifecycle handling ─────────────────────────────

    private static Mock<MailboxDeltaReconciliationService> MockReconciliation()
        => new(
            MockBehavior.Loose,
            MockReader().Object,
            CreateAccountService(),
            MockJobSubmission(new List<JobContract>()),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<MailboxDeltaReconciliationService>>());

    private static GraphSubscriptionManager CreateManager(
        MailboxDeltaReconciliationService reconciliation,
        params CommunicationAccount[] accounts)
        => new(
            CreateAccountService(accounts),
            Mock.Of<IGraphClientFactory>(),
            Mock.Of<IGenericEntityService>(),
            reconciliation,
            Options.Create(new CommunicationOptions
            {
                ApprovedSenders = new[] { new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true } },
                WebhookNotificationUrl = "https://test.example.com/api/communications/incoming-webhook",
                WebhookClientState = "state",
                WebhookSigningKey = "key"
            }),
            Mock.Of<ILogger<GraphSubscriptionManager>>());

    [Fact]
    public async Task HandleLifecycleNotificationAsync_WhenMissedWithKnownSubscription_ReconcilesThatAccount()
    {
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reconciliation = MockReconciliation();
        var manager = CreateManager(reconciliation.Object, account);

        await manager.HandleLifecycleNotificationAsync("missed", "sub-1", "resource", CancellationToken.None);

        reconciliation.Verify(r => r.ReconcileAccountAsync(
            It.Is<CommunicationAccount>(a => a.Id == account.Id), It.IsAny<CancellationToken>()), Times.Once);
        reconciliation.Verify(r => r.ReconcileAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleLifecycleNotificationAsync_WhenMissedWithUnknownSubscription_ReconcilesAllAccounts()
    {
        var account = CreateReceiveAccount(subscriptionId: "sub-1");
        var reconciliation = MockReconciliation();
        var manager = CreateManager(reconciliation.Object, account);

        await manager.HandleLifecycleNotificationAsync("missed", "sub-does-not-exist", "resource", CancellationToken.None);

        reconciliation.Verify(r => r.ReconcileAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        reconciliation.Verify(r => r.ReconcileAccountAsync(It.IsAny<CommunicationAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleLifecycleNotificationAsync_WhenReauthorizationRequiredWithNoMatchingAccount_IsNonFatalAndDoesNotReconcile()
    {
        var reconciliation = MockReconciliation();
        var manager = CreateManager(reconciliation.Object); // no accounts

        var act = async () => await manager.HandleLifecycleNotificationAsync(
            "reauthorizationRequired", "sub-1", "resource", CancellationToken.None);

        await act.Should().NotThrowAsync();
        reconciliation.Verify(r => r.ReconcileAccountAsync(It.IsAny<CommunicationAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleLifecycleNotificationAsync_WhenUnknownEvent_IsNoOpAndDoesNotThrow()
    {
        var reconciliation = MockReconciliation();
        var manager = CreateManager(reconciliation.Object, CreateReceiveAccount(subscriptionId: "sub-1"));

        var act = async () => await manager.HandleLifecycleNotificationAsync(
            "someFutureEvent", "sub-1", "resource", CancellationToken.None);

        await act.Should().NotThrowAsync();
        reconciliation.Verify(r => r.ReconcileAccountAsync(It.IsAny<CommunicationAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        reconciliation.Verify(r => r.ReconcileAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
