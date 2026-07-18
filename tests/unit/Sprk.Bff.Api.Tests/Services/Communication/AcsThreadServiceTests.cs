using System.Reflection;
using Azure;
using Azure.Communication;
using Azure.Communication.Chat;
using Azure.Communication.Identity;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="AcsThreadService"/> — the ACS thread + membership plane (FR-15 / task 011). These
/// protect branched logic that would regress silently: (1) the 30-day auto-delete retention set AT CREATE
/// TIME (a design-§8.7 invariant — a dropped/loosened retention would let ACS become a shadow record store),
/// (2) idempotent membership mutations (add-existing / remove-absent are safe no-ops — the reconcile job 041
/// relies on this), (3) the batch-chunk shape that keeps the 041 reconcile inside the ACS 10/10s + 30/min
/// per-thread budget, (4) a 429 surfacing as a retryable <see cref="AcsRateLimitException"/>, and (5) the
/// MRI guard that stops a raw Entra id ever reaching ACS. The ACS Chat SDK is mocked because no live ACS
/// resource is provisioned (the task's explicit constraint) — the branches have no other observable surface.
/// </summary>
public class AcsThreadServiceTests
{
    private const string ThreadId = "19:acs:thread_00000000000000000000000000000001@thread.v2";
    private const string Mri = "8:acs:res_00000000-0000-0000-0000-000000000001";

    private static Response<T> Resp<T>(T value) => Response.FromValue(value, Mock.Of<Response>());

    private static AcsThreadService BuildSut(Mock<ChatClient> chatClient)
        => new(new Lazy<ChatClient>(() => chatClient.Object), Mock.Of<ILogger<AcsThreadService>>());

    private static CreateChatThreadResult ThreadResult(string threadId) =>
        ChatModelFactory.CreateChatThreadResult(
            ChatModelFactory.ChatThreadProperties(
                id: threadId,
                topic: "topic",
                createdOn: DateTimeOffset.UtcNow,
                createdBy: new CommunicationUserIdentifier(Mri),
                deletedOn: default),
            invalidParticipants: Array.Empty<ChatError>());

    // ── Retention (FR-15) ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateThreadAsync_SetsThirtyDayAutoDeleteRetention_AtCreateTime()
    {
        CreateChatThreadOptions? captured = null;
        var chatClient = new Mock<ChatClient>();
        chatClient
            .Setup(c => c.CreateChatThreadAsync(It.IsAny<CreateChatThreadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<CreateChatThreadOptions, CancellationToken>((o, _) => captured = o)
            .ReturnsAsync(Resp(ThreadResult(ThreadId)));

        var result = await BuildSut(chatClient).CreateThreadAsync("Matter 123", new[] { Mri });

        result.ChatThreadId.Should().Be(ThreadId);
        result.RetentionDays.Should().Be(30);
        captured!.RetentionPolicy.Should().BeOfType<ThreadCreationDateRetentionPolicy>()
            .Which.DeleteThreadAfterDays.Should().Be(30);
        captured.Participants.Should().ContainSingle();
    }

    [Theory]
    [InlineData(10, 30)]   // below the ACS floor → clamp up to 30
    [InlineData(45, 45)]   // in range → unchanged
    [InlineData(120, 90)]  // above the ACS ceiling → clamp down to 90
    public async Task CreateThreadAsync_ClampsRetentionToAcsRange(int requestedDays, int expectedDays)
    {
        CreateChatThreadOptions? captured = null;
        var chatClient = new Mock<ChatClient>();
        chatClient
            .Setup(c => c.CreateChatThreadAsync(It.IsAny<CreateChatThreadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<CreateChatThreadOptions, CancellationToken>((o, _) => captured = o)
            .ReturnsAsync(Resp(ThreadResult(ThreadId)));

        var result = await BuildSut(chatClient)
            .CreateThreadAsync("Matter 123", Array.Empty<string>(), TimeSpan.FromDays(requestedDays));

        result.RetentionDays.Should().Be(expectedDays);
        ((ThreadCreationDateRetentionPolicy)captured!.RetentionPolicy!).DeleteThreadAfterDays.Should().Be(expectedDays);
    }

    // ── Batch-friendly Add (rate-limit shape for task 041) ──────────────────

    [Fact]
    public async Task AddParticipantsAsync_WithMoreThanOneBatch_ChunksIntoPerBatchMutations()
    {
        var batchSizes = new List<int>();
        var thread = new Mock<ChatThreadClient>();
        thread
            .Setup(t => t.AddParticipantsAsync(It.IsAny<IEnumerable<ChatParticipant>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatParticipant>, CancellationToken>((ps, _) => batchSizes.Add(ps.Count()))
            .ReturnsAsync(Resp(ChatModelFactory.AddChatParticipantsResult(Array.Empty<ChatError>())));

        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        var ids = Enumerable.Range(0, 25).Select(i => $"8:acs:res_{i:D3}").ToArray();

        var change = await BuildSut(chatClient).AddParticipantsAsync(ThreadId, ids);

        // 25 → batches of 10, 10, 5 (MaxParticipantsPerBatch = 10 — the per-thread throttle unit).
        batchSizes.Should().Equal(AcsThreadService.MaxParticipantsPerBatch, AcsThreadService.MaxParticipantsPerBatch, 5);
        change.BatchCount.Should().Be(3);
        change.Requested.Should().Be(25);
        change.Applied.Should().Be(25);
        change.NoOped.Should().Be(0);
    }

    [Fact]
    public async Task AddParticipantsAsync_WhenAllInOneBatch_IssuesASingleMutation()
    {
        var callCount = 0;
        var thread = new Mock<ChatThreadClient>();
        thread
            .Setup(t => t.AddParticipantsAsync(It.IsAny<IEnumerable<ChatParticipant>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(Resp(ChatModelFactory.AddChatParticipantsResult(Array.Empty<ChatError>())));

        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        await BuildSut(chatClient).AddParticipantsAsync(ThreadId, new[] { "8:acs:res_a", "8:acs:res_b" });

        callCount.Should().Be(1);
    }

    // ── Idempotency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddParticipantsAsync_WhenParticipantAlreadyPresent_IsNoOpNotError()
    {
        var thread = new Mock<ChatThreadClient>();
        thread
            .Setup(t => t.AddParticipantsAsync(It.IsAny<IEnumerable<ChatParticipant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resp(ChatModelFactory.AddChatParticipantsResult(new[]
            {
                ChatModelFactory.ChatError("Conflict", "already a participant", Mri, Array.Empty<ChatError>(), null),
            })));

        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        var change = await BuildSut(chatClient).AddParticipantsAsync(ThreadId, new[] { Mri });

        change.NoOped.Should().Be(1);
        change.Applied.Should().Be(0);
    }

    [Fact]
    public async Task RemoveParticipantAsync_WhenParticipantAbsent_IsNoOpNotError()
    {
        var thread = new Mock<ChatThreadClient>();
        thread
            .Setup(t => t.RemoveParticipantAsync(It.IsAny<CommunicationIdentifier>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "participant not found"));

        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        var change = await BuildSut(chatClient).RemoveParticipantAsync(ThreadId, Mri);

        change.Applied.Should().Be(0);
        change.NoOped.Should().Be(1);
    }

    [Fact]
    public async Task RemoveParticipantAsync_WhenPresent_RemovesAndReportsApplied()
    {
        var thread = new Mock<ChatThreadClient>();
        thread
            .Setup(t => t.RemoveParticipantAsync(It.IsAny<CommunicationIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        var change = await BuildSut(chatClient).RemoveParticipantAsync(ThreadId, Mri);

        change.Applied.Should().Be(1);
        change.NoOped.Should().Be(0);
    }

    // ── 429 surfaces as retryable ───────────────────────────────────────────

    [Fact]
    public async Task AddParticipantsAsync_WhenThrottled_ThrowsRetryableRateLimitException()
    {
        var thread = new Mock<ChatThreadClient>();
        thread
            .Setup(t => t.AddParticipantsAsync(It.IsAny<IEnumerable<ChatParticipant>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(429, "Too Many Requests"));

        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        var act = () => BuildSut(chatClient).AddParticipantsAsync(ThreadId, new[] { Mri });

        await act.Should().ThrowAsync<AcsRateLimitException>();
    }

    // ── MRI guard: never pass a raw Entra id to ACS ─────────────────────────

    [Fact]
    public async Task AddParticipantsAsync_WithRawEntraId_ThrowsAndNeverCallsAcs()
    {
        var thread = new Mock<ChatThreadClient>(MockBehavior.Strict);
        var chatClient = new Mock<ChatClient>();
        chatClient.Setup(c => c.GetChatThreadClient(ThreadId)).Returns(thread.Object);

        // A bare Entra GUID is NOT an ACS MRI — the guard rejects it before any ACS call (strict mock throws
        // if AddParticipantsAsync is invoked).
        var act = () => BuildSut(chatClient).AddParticipantsAsync(ThreadId, new[] { Guid.NewGuid().ToString() });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── NFR-04 / ADR-045: no ACS type leaks the service boundary ────────────

    [Fact]
    public void ThreadServiceSeam_DoesNotExposeAnyAzureCommunicationType()
    {
        var offending = new List<string>();

        void Check(Type t, string where)
        {
            var probe = t.IsGenericType ? t.GetGenericArguments()[^1] : t;
            if (probe.Namespace?.StartsWith("Azure.Communication", StringComparison.Ordinal) == true)
                offending.Add($"{where}: {probe.FullName}");
        }

        foreach (var m in typeof(IAcsThreadService).GetMethods())
        {
            Check(m.ReturnType, $"{m.Name} return");
            foreach (var p in m.GetParameters())
                Check(p.ParameterType, $"{m.Name} param {p.Name}");
        }

        foreach (var model in new[] { typeof(AcsThreadCreateResult), typeof(AcsMembershipChange) })
            foreach (var p in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Check(p.PropertyType, $"{model.Name}.{p.Name}");

        offending.Should().BeEmpty(
            "the ACS thread seam + result models must not leak Azure.Communication types to callers (ADR-045 / NFR-04)");
    }
}

/// <summary>
/// Behavior of <see cref="AcsServiceChatCredential"/> — proves the ACS <c>ChatClient</c>'s auth is rooted in
/// the DI-injected central credential (ADR-028 / NFR-05) with NO credential <c>new</c>'d and NO ACS
/// connection-string key: the chat token is minted through task 010's <c>CommunicationIdentityClient</c>
/// (which task 010 built from the central <c>TokenCredential</c>), and the guarded logic under test is (1)
/// the lazy, cached service identity (created once, no live call at construction) and (2) chat-scope-only
/// token minting. The identity client is mocked (no live ACS resource — the task constraint).
/// </summary>
public class AcsServiceChatCredentialTests
{
    private const string ServiceMri = "8:acs:res_00000000-0000-0000-0000-0000000000ff";

    private static Response<T> Resp<T>(T value) => Response.FromValue(value, Mock.Of<Response>());

    [Fact]
    public async Task MintServiceTokenAsync_MintsChatScopeOnly_AndCreatesServiceIdentityLazilyOnce()
    {
        IEnumerable<CommunicationTokenScope>? requestedScopes = null;
        var identityClient = new Mock<Azure.Communication.Identity.CommunicationIdentityClient>();
        identityClient
            .Setup(c => c.CreateUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resp(new CommunicationUserIdentifier(ServiceMri)));
        identityClient
            .Setup(c => c.GetTokenAsync(It.IsAny<CommunicationUserIdentifier>(), It.IsAny<IEnumerable<CommunicationTokenScope>>(), It.IsAny<CancellationToken>()))
            .Callback<CommunicationUserIdentifier, IEnumerable<CommunicationTokenScope>, CancellationToken>((_, scopes, _) => requestedScopes = scopes)
            .ReturnsAsync(Resp(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(24))));

        var sut = new AcsServiceChatCredential(identityClient.Object, Mock.Of<ILogger<AcsServiceChatCredential>>());

        // No live call at construction (deferred until first mint).
        identityClient.Verify(c => c.CreateUserAsync(It.IsAny<CancellationToken>()), Times.Never);

        var t1 = await sut.MintServiceTokenAsync(default);
        var t2 = await sut.MintServiceTokenAsync(default);

        t1.Should().Be("tok");
        t2.Should().Be("tok");
        // Service identity created once (cached), rooted in the injected identity client; chat scope only.
        identityClient.Verify(c => c.CreateUserAsync(It.IsAny<CancellationToken>()), Times.Once);
        requestedScopes.Should().ContainSingle().Which.Should().Be(CommunicationTokenScope.Chat);
    }
}
