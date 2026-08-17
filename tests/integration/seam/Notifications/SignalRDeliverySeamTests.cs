using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Notifications;

/// <summary>
/// Vertical-slice seam (ADR-038) for the Layer-C SignalR delivery leg (task 020): the
/// write-before-ping ordering contract, the signal-only payload shape (NFR-02/03), the
/// <c>Clients.User(oid)</c> targeting, and the ADR-032 Null-Object degrade path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Live-Azure boundary — read this.</b> Azure SignalR <b>Serverless</b> real delivery to a
/// connected client requires a PROVISIONED Azure SignalR resource + connection string, which is not
/// available in the local/CI test process. So this file does NOT fake an HTTP transport (ADR-038 bans
/// <c>Mock&lt;HttpMessageHandler&gt;</c>). Instead it exercises the REAL
/// <see cref="SignalRDeliveryService"/> logic (arg validation, signal construction, best-effort catch,
/// user-vs-group targeting) against a doubled <see cref="IServiceHubContext"/> MODULE BOUNDARY
/// (ADR-038 "mock at module boundaries, not the HTTP-handler level") — proving everything up to, but
/// not including, the SDK's network glue. A true end-to-end delivery variant
/// (<see cref="PingUserAsync_AgainstLiveResource_NegotiatesRealConnection"/>) is GATED on a
/// connection-string env var and no-ops with a clear reason when absent.
/// </para>
/// <para>
/// The Null-Object degrade scenario (b) runs fully LIVE locally — it needs no resource.
/// </para>
/// </remarks>
public sealed class SignalRDeliverySeamTests
{
    private const string LiveConnStringEnvVar = "SPAARKE_SIGNALR_TEST_CONNSTRING";
    private const string SignalMethod = "notification";

    private static CommunicationEnvelope BuildEnvelope() => new CommunicationEnvelope
    {
        Kind = NotificationKind.CommunicationArrived,
        CommunicationId = Guid.NewGuid(),
        ThreadId = Guid.NewGuid(),
        Channel = "email",
        Direction = "inbound",
        RegardingRecordId = Guid.NewGuid().ToString(),
        SenderDisplay = "Jane Doe",
        BadgeDelta = 1
    }.Validate();

    private static OutboxService BuildOutbox(FakeGenericEntityService store)
        => new(store, NullLogger<OutboxService>.Instance, new FixedTimeProvider(DateTimeOffset.Parse("2026-07-21T12:00:00Z")));

    // ─────────────────────────────────────────────────────────────────────────
    // (a) Real delivery reaches Clients.User(oid) — boundary double, runs locally.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PingUserAsync_AfterOutboxWrite_DispatchesSignalOnlyMessageToTargetUser()
    {
        // Arrange — write the durable outbox row FIRST (real OutboxService; Dataverse boundary faked),
        // so the ping is issued with a genuinely post-write row id (write-before-ping vertical slice).
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store);
        var userId = Guid.NewGuid();               // the recipient's Dataverse systemuserid (outbox OwnerId)
        var userOid = Guid.NewGuid().ToString();   // the Azure AD oid the resolver maps it to
        var outboxRowId = await outbox.WriteAsync(userId, NotificationKind.CommunicationArrived, BuildEnvelope());

        // The resolver maps the recipient's systemuserid → the oid SignalR is keyed by (ADR-028).
        var resolver = ResolverMapping(userId, userOid);

        // Doubled ServiceHubContext boundary — captures what the real service dispatches.
        string? targetedUser = null;
        string? capturedMethod = null;
        object?[]? capturedArgs = null;

        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((m, a, _) => { capturedMethod = m; capturedArgs = a; })
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients
            .Setup(c => c.User(It.IsAny<string>()))
            .Callback<string>(u => targetedUser = u)
            .Returns(clientProxy.Object);

        var hub = new Mock<IServiceHubContext>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        var sut = new BoundaryDoubleDeliveryService(hub.Object, resolver);

        // Act — ping AFTER the write, passing the recipient systemuserid (resolved server-side to the oid).
        await sut.PingUserAsync(outboxRowId, userId, NotificationKind.CommunicationArrived);

        // Assert — dispatched at-most-once to Clients.User(oid) with the signal-only payload.
        targetedUser.Should().Be(userOid, "delivery must target the caller's own oid");
        capturedMethod.Should().Be(SignalMethod);
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Should().HaveCount(1);

        var signal = capturedArgs![0].Should().BeOfType<NotificationSignal>().Subject;
        signal.OutboxRowId.Should().Be(outboxRowId, "the signal carries the outbox row id so the client can re-fetch");
        signal.Kind.Should().Be(NotificationKind.CommunicationArrived);
    }

    [Fact]
    public void NotificationSignal_CarriesOnlyRoutingInfo_NoBodyOrTokenFields()
    {
        // Structural proof of "signal-only" (NFR-02/03): the wire payload type has EXACTLY the two
        // routing fields — no body, no snippet, no content, no action token can ride along.
        var props = typeof(NotificationSignal).GetProperties().Select(p => p.Name);
        props.Should().BeEquivalentTo(new[] { nameof(NotificationSignal.OutboxRowId), nameof(NotificationSignal.Kind) });
    }

    [Fact]
    public async Task PingUserAsync_WithEmptyOutboxRowId_ThrowsBeforeAnyDispatch()
    {
        // The write-before-ping ordering is STRUCTURAL: a caller cannot ping without a post-write row id.
        var hub = new Mock<IServiceHubContext>(MockBehavior.Strict); // Strict → any dispatch would fail the test.
        // Strict resolver too — the write-before-ping guard must throw BEFORE any identity resolution.
        var resolver = new Mock<ISystemUserIdentityResolver>(MockBehavior.Strict).Object;
        var sut = new BoundaryDoubleDeliveryService(hub.Object, resolver);

        var act = async () => await sut.PingUserAsync(Guid.Empty, Guid.NewGuid(), NotificationKind.CommunicationArrived);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("outboxRowId");
    }

    [Fact]
    public async Task PingGroupAsync_AfterOutboxWrite_DispatchesToTargetGroup()
    {
        // The Group(...) primitive task 023 builds fan-out on top of — same signal-only contract.
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store);
        var outboxRowId = await outbox.WriteAsync(Guid.NewGuid(), NotificationKind.CommunicationArrived, BuildEnvelope());

        string? targetedGroup = null;
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>()))
            .Callback<string>(g => targetedGroup = g)
            .Returns(clientProxy.Object);
        var hub = new Mock<IServiceHubContext>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);
        var sut = new BoundaryDoubleDeliveryService(hub.Object, Mock.Of<ISystemUserIdentityResolver>());

        await sut.PingGroupAsync(outboxRowId, "thread:abc", NotificationKind.CommunicationArrived);

        targetedGroup.Should().Be("thread:abc");
        clientProxy.Verify(p => p.SendCoreAsync(SignalMethod, It.Is<object?[]>(a => a.Length == 1 && a[0] is NotificationSignal), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PingUserAsync_WhenDispatchThrows_SwallowsSoProducerSucceeds()
    {
        // Best-effort at-most-once: a transient SignalR failure must NOT fail the producer's own path.
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store);
        var recipientId = Guid.NewGuid();
        var outboxRowId = await outbox.WriteAsync(recipientId, NotificationKind.CommunicationArrived, BuildEnvelope());

        // Recipient IS mapped (so dispatch is attempted and the transient failure is what gets swallowed).
        var resolver = ResolverMapping(recipientId, Guid.NewGuid().ToString());

        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR unreachable"));
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);
        var hub = new Mock<IServiceHubContext>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);
        var sut = new BoundaryDoubleDeliveryService(hub.Object, resolver);

        var act = async () => await sut.PingUserAsync(outboxRowId, recipientId, NotificationKind.CommunicationArrived);

        await act.Should().NotThrowAsync("a live-signal failure is not caller-visible error state (NFR-04)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (b) Null-Object degrade path — runs fully LIVE locally, no resource needed.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PingUserAsync_WhenSignalRDisabled_ReturnsWithoutThrowingOrBlocking()
    {
        // With SignalR OFF (Null-Object registered), a ping AFTER an outbox write is a quiet no-op —
        // it returns without throwing and without blocking the producer's success path (ADR-032 P2).
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store);
        var outboxRowId = await outbox.WriteAsync(Guid.NewGuid(), NotificationKind.CommunicationArrived, BuildEnvelope());

        SignalRDeliveryService nullService = new NullSignalRDeliveryService(NullLogger<SignalRDeliveryService>.Instance);

        var act = async () => await nullService.PingUserAsync(outboxRowId, Guid.NewGuid(), NotificationKind.CommunicationArrived);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PingUserAsync_WhenRecipientHasNoOidMapping_IsQuietNoOpWithoutDispatch()
    {
        // A recipient systemuserid with no azureactivedirectoryobjectid mapping (unmapped/disabled user):
        // the resolver returns null → the ping is a quiet no-op. No dispatch, no throw — the outbox row +
        // poll fallback (task 022) carry the durable truth.
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store);
        var recipientId = Guid.NewGuid();
        var outboxRowId = await outbox.WriteAsync(recipientId, NotificationKind.CommunicationArrived, BuildEnvelope());

        var resolver = new Mock<ISystemUserIdentityResolver>();
        resolver.Setup(r => r.ResolveOidAsync(recipientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

        // Strict hub — ANY access to Clients (let alone a dispatch) would fail the test.
        var hub = new Mock<IServiceHubContext>(MockBehavior.Strict);
        var sut = new BoundaryDoubleDeliveryService(hub.Object, resolver.Object);

        var act = async () => await sut.PingUserAsync(outboxRowId, recipientId, NotificationKind.CommunicationArrived);

        await act.Should().NotThrowAsync("an unmapped recipient is a quiet no-op, not caller-visible error state");
        resolver.Verify(r => r.ResolveOidAsync(recipientId, It.IsAny<CancellationToken>()), Times.Once);
        hub.Verify(h => h.Clients, Times.Never, "no SignalR dispatch when the recipient has no oid mapping");
    }

    [Fact]
    public async Task NegotiateAsync_WhenSignalRDisabled_ThrowsFeatureDisabledForPollFallback()
    {
        // Negotiate under the Null-Object is P3 fail-fast: it throws FeatureDisabledException so the
        // endpoint returns 503 and the client falls back to the poll endpoint (FR-06) — not a
        // misleading "connected" response.
        SignalRDeliveryService nullService = new NullSignalRDeliveryService(NullLogger<SignalRDeliveryService>.Instance);

        var act = async () => await nullService.NegotiateAsync(Guid.NewGuid().ToString());

        (await act.Should().ThrowAsync<FeatureDisabledException>())
            .Which.ErrorCode.Should().Be("notifications.signalr.disabled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Live end-to-end variant — GATED on a provisioned Azure SignalR resource.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PingUserAsync_AgainstLiveResource_NegotiatesRealConnection()
    {
        var connString = Environment.GetEnvironmentVariable(LiveConnStringEnvVar);
        if (string.IsNullOrWhiteSpace(connString))
        {
            // SKIPPED (documented): Serverless real delivery needs a PROVISIONED Azure SignalR
            // resource. Set env var SPAARKE_SIGNALR_TEST_CONNSTRING to run this against a real resource.
            // The boundary-double tests above cover the delivery logic up to the SDK network glue.
            Assert.True(true, $"Live SignalR test skipped: {LiveConnStringEnvVar} not set.");
            return;
        }

        var options = Microsoft.Extensions.Options.Options.Create(new SignalRDeliveryOptions
        {
            Enabled = true,
            ConnectionString = connString,
            HubName = "notifications"
        });
        var oid = Guid.NewGuid().ToString();
        var recipientId = Guid.NewGuid();
        var resolver = ResolverMapping(recipientId, oid);
        var sut = new SignalRDeliveryService(options, resolver, NullLoggerFactory.Instance);

        var info = await sut.NegotiateAsync(oid);

        info.Url.Should().NotBeNullOrWhiteSpace();
        info.AccessToken.Should().NotBeNullOrWhiteSpace();

        // A real ping must not throw against a live resource (delivery is at-most-once/best-effort).
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store);
        var outboxRowId = await outbox.WriteAsync(recipientId, NotificationKind.CommunicationArrived, BuildEnvelope());
        var ping = async () => await sut.PingUserAsync(outboxRowId, recipientId, NotificationKind.CommunicationArrived);
        await ping.Should().NotThrowAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Real <see cref="SignalRDeliveryService"/> subclass whose ONLY override is the module-boundary
    /// accessor — it returns a test-supplied <see cref="IServiceHubContext"/> double instead of building
    /// a live Serverless hub. All ping logic (validation, signal construction, targeting, best-effort
    /// catch) runs UNMODIFIED. This is a module-boundary double per ADR-038, not an HTTP-handler mock.
    /// </summary>
    private sealed class BoundaryDoubleDeliveryService : SignalRDeliveryService
    {
        private readonly IServiceHubContext _hub;

        public BoundaryDoubleDeliveryService(IServiceHubContext hub, ISystemUserIdentityResolver identityResolver)
            : base(identityResolver, NullLogger<SignalRDeliveryService>.Instance)
            => _hub = hub;

        protected override Task<IServiceHubContext> GetHubContextAsync(CancellationToken ct)
            => Task.FromResult(_hub);
    }

    /// <summary>Builds an <see cref="ISystemUserIdentityResolver"/> double mapping one systemuserid → oid.</summary>
    private static ISystemUserIdentityResolver ResolverMapping(Guid systemUserId, string? oid)
    {
        var mock = new Mock<ISystemUserIdentityResolver>();
        mock.Setup(r => r.ResolveOidAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(oid);
        return mock.Object;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Minimal in-memory <see cref="IGenericEntityService"/> — the Dataverse boundary. Only
    /// <see cref="CreateAsync"/> is exercised here (via <see cref="OutboxService.WriteAsync{TEnvelope}"/>
    /// to obtain a genuine post-write row id); everything else throws.
    /// </summary>
    private sealed class FakeGenericEntityService : IGenericEntityService
    {
        public async Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default)
            => (await CreateAsync(entity, ct), true); // compose-r7 task 013: interface member; fake delegates to create
        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());

        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
