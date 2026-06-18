using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="MemoryCompositionService"/> (R6 Pillar 7 / task 067, D-C-20).
/// </summary>
/// <remarks>
/// Covers: kill-switch short-circuit, 4-layer assembly (recent / compressed / retrieved /
/// pinned), pinned grouped by pinType, recent-verbatim slicing, mid-window slicing,
/// retrieved-old skipping when matterId is null or conversation < midEnd, soft-failures
/// (compression returns null, recall throws, repository throws on one scope), budget
/// enforcement drop priority (retrieved-old → compressed-mid → recent oldest-first),
/// pinned NEVER dropped invariant (FR-42), cancellation propagates, constructor null-arg
/// defense.
/// </remarks>
public sealed class MemoryCompositionServiceTests
{
    private const string TenantA = "tenant-a";
    private const string UserAlice = "user-alice";
    private const string MatterX = "matter-x";

    private readonly Mock<ISummarizationCompressionService> _compression = new();
    private readonly Mock<IPinnedContextRepository> _repo = new();
    private readonly Mock<IPinnedContextRecallService> _recall = new();
    private readonly Mock<ILogger<MemoryCompositionService>> _logger = new();

    private static MemoryCompositionService CreateSut(
        Mock<ISummarizationCompressionService> compression,
        Mock<IPinnedContextRepository> repo,
        Mock<IPinnedContextRecallService> recall,
        Mock<ILogger<MemoryCompositionService>> logger,
        MemoryCompositionOptions? options = null)
    {
        options ??= new MemoryCompositionOptions { Enabled = true };
        return new MemoryCompositionService(
            compression.Object,
            repo.Object,
            recall.Object,
            Options.Create(options),
            logger.Object);
    }

    private static ChatMessage BuildMessage(int sequence, string content, ChatMessageRole role = ChatMessageRole.User)
    {
        return new ChatMessage(
            MessageId: $"msg-{sequence}",
            SessionId: "session-1",
            Role: role,
            Content: content,
            TokenCount: 0,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-100 + sequence),
            SequenceNumber: sequence);
    }

    private static PinnedContextItem BuildPin(
        string pinId,
        string content,
        PinType pinType = PinType.MatterFact,
        string? matterId = MatterX,
        string userId = UserAlice)
    {
        return new PinnedContextItem
        {
            Id = $"pinned-context_{TenantA}_{pinId}",
            DocumentType = "pinned-context",
            TenantId = TenantA,
            UserId = userId,
            PinType = pinType,
            Title = $"Pin {pinId}",
            Content = content,
            MatterId = matterId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };
    }

    private static MemoryCompositionRequest BuildRequest(
        IReadOnlyList<ChatMessage>? conversation = null,
        string? matterId = MatterX,
        string currentMessage = "current user message")
    {
        return new MemoryCompositionRequest(
            TenantId: TenantA,
            UserId: UserAlice,
            MatterId: matterId,
            Conversation: conversation ?? Array.Empty<ChatMessage>(),
            CurrentUserMessage: currentMessage);
    }

    // =========================================================================================
    // Kill switch
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_ReturnsEmpty_WhenKillSwitchOff()
    {
        var sut = CreateSut(_compression, _repo, _recall, _logger,
            new MemoryCompositionOptions { Enabled = false });

        var result = await sut.ComposeAsync(BuildRequest(
            conversation: new[] { BuildMessage(1, "hi") }));

        result.Should().BeSameAs(MemoryComposition.Empty);
        // Rationale: kill switch off short-circuits before any primitive is consulted.
        _compression.Verify(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repo.Verify(r => r.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _recall.Verify(r => r.RecallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================================
    // Argument validation
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_RejectsEmptyTenantId()
    {
        var sut = CreateSut(_compression, _repo, _recall, _logger);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.ComposeAsync(new MemoryCompositionRequest("", UserAlice, MatterX,
                Array.Empty<ChatMessage>(), "msg")));
    }

    [Fact]
    public async Task ComposeAsync_RejectsEmptyUserId()
    {
        var sut = CreateSut(_compression, _repo, _recall, _logger);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.ComposeAsync(new MemoryCompositionRequest(TenantA, "", MatterX,
                Array.Empty<ChatMessage>(), "msg")));
    }

    [Fact]
    public async Task ComposeAsync_RejectsNullRequest()
    {
        var sut = CreateSut(_compression, _repo, _recall, _logger);
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.ComposeAsync(null!));
    }

    // =========================================================================================
    // Layer: Recent verbatim — last N
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_RecentVerbatim_ReturnsAllMessages_WhenLessThanN()
    {
        var conv = Enumerable.Range(1, 5).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.RecentVerbatim.Should().HaveCount(5);
        result.CompressedMid.Should().BeNull("conversation is shorter than recent-verbatim cut-off — no mid window");
    }

    [Fact]
    public async Task ComposeAsync_RecentVerbatim_ReturnsLastNMessages_WhenMoreThanN()
    {
        var conv = Enumerable.Range(1, 12).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.RecentVerbatim.Should().HaveCount(10, because: "recent-verbatim default is 10");
        result.RecentVerbatim.First().SequenceNumber.Should().Be(3,
            because: "the last 10 of 12 messages start at sequence 3");
        result.RecentVerbatim.Last().SequenceNumber.Should().Be(12);
    }

    // =========================================================================================
    // Layer: Compressed mid — window [length - midEnd, length - recentN)
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_CompressedMid_CallsCompressionWithMidWindow()
    {
        var conv = Enumerable.Range(1, 20).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        IReadOnlyList<ChatMessage>? capturedMid = null;
        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, int, CancellationToken>((msgs, _, _) => capturedMid = msgs)
            .ReturnsAsync(BuildMessage(0, "Summary of earlier conversation: stuff", ChatMessageRole.System));

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.CompressedMid.Should().NotBeNull();
        result.CompressedMid!.Content.Should().StartWith("Summary of earlier conversation:");
        capturedMid.Should().NotBeNull();
        // Mid window for 20 msgs with recentN=10, midEnd=50: [max(0,-30), 10) = [0, 10).
        capturedMid!.Should().HaveCount(10);
        capturedMid.First().SequenceNumber.Should().Be(1);
        capturedMid.Last().SequenceNumber.Should().Be(10);
    }

    [Fact]
    public async Task ComposeAsync_CompressedMid_IsNull_WhenCompressionReturnsNull()
    {
        var conv = Enumerable.Range(1, 20).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        // Compression soft-fails (kill switch / circuit broken / empty).
        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage?)null);

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.CompressedMid.Should().BeNull(because: "compression returned null — layer omitted, others still compose");
        result.RecentVerbatim.Should().HaveCount(10, because: "recent layer still composes");
    }

    [Fact]
    public async Task ComposeAsync_CompressedMid_NotCalled_WhenConversationShorterThanRecentN()
    {
        var conv = Enumerable.Range(1, 8).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.CompressedMid.Should().BeNull();
        _compression.Verify(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================================
    // Layer: Retrieved old — recall, when conv >= midEnd and matterId set
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_RetrievedOld_CallsRecall_WhenConversationAtLeastMidEnd()
    {
        var conv = Enumerable.Range(1, 50).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        var recalled = new[]
        {
            BuildPin("p-recall-1", "recalled fact 1"),
            BuildPin("p-recall-2", "recalled fact 2")
        };
        _recall
            .Setup(r => r.RecallAsync(TenantA, MatterX, "current user message", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recalled);

        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage?)null);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.RetrievedOld.Should().HaveCount(2);
        result.RetrievedOld.Select(p => p.Id).Should().Contain(recalled.Select(r => r.Id));
    }

    [Fact]
    public async Task ComposeAsync_RetrievedOld_Empty_WhenMatterIdNull()
    {
        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage?)null);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv, matterId: null));

        result.RetrievedOld.Should().BeEmpty(because: "recall requires non-empty matterId");
        _recall.Verify(r => r.RecallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ComposeAsync_RetrievedOld_Empty_WhenConversationShorterThanMidEnd()
    {
        var conv = Enumerable.Range(1, 20).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage?)null);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.RetrievedOld.Should().BeEmpty();
        _recall.Verify(r => r.RecallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ComposeAsync_RetrievedOld_Empty_WhenRecallThrows()
    {
        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        _recall
            .Setup(r => r.RecallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated recall failure"));

        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage?)null);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.RetrievedOld.Should().BeEmpty(because: "recall failure is soft-fail; layer omitted");
        result.RecentVerbatim.Should().HaveCount(10, because: "other layers still compose");
    }

    // =========================================================================================
    // Layer: Pinned — ALL pins grouped by pinType
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_Pinned_GroupedByPinType_FromUserAndMatterScopes()
    {
        var userPins = new[]
        {
            BuildPin("p-pref", "be terse", PinType.UserPreference, matterId: null),
            BuildPin("p-rule", "no internal cost data", PinType.SystemRule, matterId: null)
        };
        var matterPins = new[]
        {
            BuildPin("p-fact-1", "clause Y", PinType.MatterFact),
            BuildPin("p-fact-2", "settlement Z", PinType.MatterFact)
        };

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userPins);
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterPins);

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest());

        result.Pinned.Should().HaveCount(3, because: "3 distinct pinTypes are present");
        result.Pinned[PinType.UserPreference].Should().ContainSingle().Which.Id.Should().Be(userPins[0].Id);
        result.Pinned[PinType.SystemRule].Should().ContainSingle().Which.Id.Should().Be(userPins[1].Id);
        result.Pinned[PinType.MatterFact].Should().HaveCount(2);
    }

    [Fact]
    public async Task ComposeAsync_Pinned_DeduplicatesById_AcrossScopes()
    {
        // Same pin returned from both GetByUserAsync and GetByMatterAsync — dedup by id.
        var shared = BuildPin("p-shared", "same content", PinType.MatterFact);

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { shared });
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { shared });

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest());

        result.Pinned[PinType.MatterFact].Should().ContainSingle(
            because: "same pin id from both scopes must dedupe");
    }

    [Fact]
    public async Task ComposeAsync_Pinned_Empty_WhenNoPinsExist()
    {
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest());

        result.Pinned.Should().BeEmpty();
    }

    [Fact]
    public async Task ComposeAsync_Pinned_SoftFailsOneScope_ReturnsOther()
    {
        var matterPins = new[] { BuildPin("p-fact", "clause Y", PinType.MatterFact) };

        // User-scope repo call throws; matter-scope returns successfully.
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user-scope failure"));
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterPins);

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest());

        result.Pinned[PinType.MatterFact].Should().ContainSingle(
            because: "matter scope succeeded; user scope failure must not nuke the whole tier");
    }

    [Fact]
    public async Task ComposeAsync_Pinned_SkipsMatterScope_WhenMatterIdNull()
    {
        var userPins = new[] { BuildPin("p-pref", "be terse", PinType.UserPreference, matterId: null) };

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userPins);

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(matterId: null));

        result.Pinned[PinType.UserPreference].Should().ContainSingle();
        _repo.Verify(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================================
    // Budget enforcement — drop priority
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_DropsRetrievedOldFirst_WhenOverBudget()
    {
        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();
        // Make one large recalled pin (~200 chars / 4 = ~50 tokens × inflated for budget).
        var largeRecalled = BuildPin("p-large", new string('a', 4000));

        _recall
            .Setup(r => r.RecallAsync(TenantA, MatterX, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { largeRecalled });

        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMessage(0, "Summary of earlier conversation: short", ChatMessageRole.System));

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        // Very tight budget — only the very small layers should survive.
        var sut = CreateSut(_compression, _repo, _recall, _logger,
            new MemoryCompositionOptions { Enabled = true, TotalTokenBudget = 1024 });

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.DroppedLayers.Should().Contain(MemoryCompositionService.LayerRetrievedOld);
        result.RetrievedOld.Should().BeEmpty(because: "retrieved-old is dropped first when over budget");
    }

    [Fact]
    public async Task ComposeAsync_DropsCompressedMidSecond_WhenStillOverAfterRetrievedDropped()
    {
        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();

        var largeRecalled = BuildPin("p-large", new string('r', 2000));
        var largeCompressed = BuildMessage(0,
            "Summary of earlier conversation: " + new string('s', 4000),
            ChatMessageRole.System);

        _recall
            .Setup(r => r.RecallAsync(TenantA, MatterX, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { largeRecalled });
        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(largeCompressed);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        // Budget tight enough that BOTH retrieved AND compressed need to drop.
        var sut = CreateSut(_compression, _repo, _recall, _logger,
            new MemoryCompositionOptions { Enabled = true, TotalTokenBudget = 1024 });

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.DroppedLayers.Should().ContainInOrder(
            MemoryCompositionService.LayerRetrievedOld,
            MemoryCompositionService.LayerCompressedMid);
        result.RetrievedOld.Should().BeEmpty();
        result.CompressedMid.Should().BeNull();
    }

    [Fact]
    public async Task ComposeAsync_DropsRecentOldestFirst_WhenStillOverAfterRetrievedAndCompressedDropped()
    {
        // Make every recent message big so dropping the oldest still helps.
        var conv = Enumerable.Range(1, 60)
            .Select(i => BuildMessage(i, new string('x', 500))) // ~125 tokens each
            .ToArray();

        var largeRecalled = BuildPin("p-large-r", new string('r', 2000));
        var largeCompressed = BuildMessage(0,
            "Summary: " + new string('s', 2000),
            ChatMessageRole.System);

        _recall
            .Setup(r => r.RecallAsync(TenantA, MatterX, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { largeRecalled });
        _compression
            .Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(largeCompressed);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        // Tight budget — recent layer also has to shed.
        var sut = CreateSut(_compression, _repo, _recall, _logger,
            new MemoryCompositionOptions { Enabled = true, TotalTokenBudget = 1024 });

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.DroppedLayers.Should().Contain(MemoryCompositionService.LayerRecentVerbatim);
        result.RecentVerbatim.Count.Should().BeLessThan(10,
            because: "oldest recent-verbatim items shed under budget pressure");
        // The KEPT recent messages must be the NEWEST — sequence numbers strictly increasing
        // and the last preserved must equal 60 (most-recent always survives).
        if (result.RecentVerbatim.Count > 0)
        {
            result.RecentVerbatim.Last().SequenceNumber.Should().Be(60,
                because: "oldest-first drop keeps the most-recent message intact");
        }
    }

    // =========================================================================================
    // FR-42 invariant: Pinned NEVER dropped
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_PinnedNeverDropped_EvenWhenAloneExceedsBudget()
    {
        // Build a pin set whose aggregate alone (~10K tokens via large content) exceeds
        // the 1024 budget. Other layers are empty so the only way to make the result fit
        // would be to drop pinned — which is forbidden.
        var hugePins = Enumerable.Range(0, 5)
            .Select(i => BuildPin($"p-huge-{i}", new string('z', 4000), PinType.MatterFact))
            .ToArray();

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hugePins);

        var sut = CreateSut(_compression, _repo, _recall, _logger,
            new MemoryCompositionOptions { Enabled = true, TotalTokenBudget = 1024 });

        var result = await sut.ComposeAsync(BuildRequest());

        // FR-42 INVARIANT — pinned tier must survive.
        result.Pinned.Should().NotBeEmpty();
        result.Pinned[PinType.MatterFact].Should().HaveCount(5,
            because: "pinned tier is NEVER dropped, even when alone exceeds budget (FR-42)");
        // DroppedLayers must NOT contain the pinned tag.
        result.DroppedLayers.Should().NotContain(MemoryCompositionService.LayerPinned);
        // The estimated token count is honest — it tells the caller pinned is over budget.
        result.EstimatedTokenCount.Should().BeGreaterThan(1024,
            because: "honest accounting — caller (task 068) applies the final hard guard");
    }

    [Fact]
    public async Task ComposeAsync_PinnedNeverDropped_WhenAllOtherLayersAreDroppable()
    {
        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, new string('m', 200))).ToArray();
        var recalled = new[] { BuildPin("p-r1", new string('r', 200)) };
        var pinned = new[]
        {
            BuildPin("p-keep-1", "must remain", PinType.UserPreference, matterId: null),
            BuildPin("p-keep-2", "must remain too", PinType.MatterFact)
        };

        _recall.Setup(r => r.RecallAsync(TenantA, MatterX, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recalled);
        _compression.Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMessage(0, new string('s', 200), ChatMessageRole.System));
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinned[0] });
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinned[1] });

        // Generous budget — everything fits; nothing dropped.
        var sut = CreateSut(_compression, _repo, _recall, _logger,
            new MemoryCompositionOptions { Enabled = true, TotalTokenBudget = 32_000 });

        var result = await sut.ComposeAsync(BuildRequest(conv));

        result.DroppedLayers.Should().BeEmpty();
        result.Pinned.Should().HaveCount(2);
        result.Pinned[PinType.UserPreference].Should().ContainSingle();
        result.Pinned[PinType.MatterFact].Should().ContainSingle();
    }

    // =========================================================================================
    // All 4 layers present together (FR-44 acceptance criterion)
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_ProducesAllFourLayers_WhenInputsSupportAll()
    {
        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();
        var recalled = new[] { BuildPin("p-recalled", "recalled content") };
        var compressed = BuildMessage(0, "Summary of earlier conversation: foo", ChatMessageRole.System);
        var userPins = new[] { BuildPin("p-pref", "be terse", PinType.UserPreference, matterId: null) };
        var matterPins = new[] { BuildPin("p-fact", "fact Y", PinType.MatterFact) };

        _recall.Setup(r => r.RecallAsync(TenantA, MatterX, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recalled);
        _compression.Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(compressed);
        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userPins);
        _repo.Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterPins);

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var result = await sut.ComposeAsync(BuildRequest(conv));

        // All 4 layers present per FR-44 acceptance criterion #1.
        result.RecentVerbatim.Should().NotBeEmpty(because: "recent layer present");
        result.CompressedMid.Should().NotBeNull(because: "compressed-mid layer present");
        result.RetrievedOld.Should().NotBeEmpty(because: "retrieved-old layer present");
        result.Pinned.Should().NotBeEmpty(because: "pinned layer present");
        result.DroppedLayers.Should().BeEmpty(because: "generous default budget");
        result.IsEmpty.Should().BeFalse();
    }

    // =========================================================================================
    // Tenant + matter isolation propagation
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_PassesTenantAndMatterUnchangedToAllPrimitives()
    {
        const string tenantB = "tenant-b";
        const string matterY = "matter-y";

        var conv = Enumerable.Range(1, 60).Select(i => BuildMessage(i, $"msg-{i}")).ToArray();
        _recall.Setup(r => r.RecallAsync(tenantB, matterY, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _compression.Setup(c => c.CompressAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage?)null);
        _repo.Setup(r => r.GetByUserAsync(tenantB, UserAlice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());
        _repo.Setup(r => r.GetByMatterAsync(tenantB, matterY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        var request = new MemoryCompositionRequest(tenantB, UserAlice, matterY, conv, "msg");
        await sut.ComposeAsync(request);

        _repo.Verify(r => r.GetByUserAsync(tenantB, UserAlice, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.GetByMatterAsync(tenantB, matterY, It.IsAny<CancellationToken>()), Times.Once);
        _recall.Verify(r => r.RecallAsync(tenantB, matterY, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        // Cross-tenant query must NEVER occur.
        _repo.Verify(r => r.GetByUserAsync(TenantA, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================================
    // Cancellation propagates
    // =========================================================================================

    [Fact]
    public async Task ComposeAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _repo.Setup(r => r.GetByUserAsync(TenantA, UserAlice, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut(_compression, _repo, _recall, _logger);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.ComposeAsync(BuildRequest(), cts.Token));
    }

    // =========================================================================================
    // Static slicing helpers
    // =========================================================================================

    [Fact]
    public void SelectRecentVerbatim_ReturnsAllMessages_WhenLessThanN()
    {
        var msgs = Enumerable.Range(1, 3).Select(i => BuildMessage(i, "m")).ToArray();
        var result = MemoryCompositionService.SelectRecentVerbatim(msgs, 10);
        result.Should().HaveCount(3);
    }

    [Fact]
    public void SelectRecentVerbatim_ReturnsLastNMessages_WhenMoreThanN()
    {
        var msgs = Enumerable.Range(1, 12).Select(i => BuildMessage(i, "m")).ToArray();
        var result = MemoryCompositionService.SelectRecentVerbatim(msgs, 10);
        result.Should().HaveCount(10);
        result.First().SequenceNumber.Should().Be(3);
        result.Last().SequenceNumber.Should().Be(12);
    }

    [Fact]
    public void SelectRecentVerbatim_ReturnsEmpty_WhenConversationEmpty()
    {
        var result = MemoryCompositionService.SelectRecentVerbatim(Array.Empty<ChatMessage>(), 10);
        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectMidWindow_ReturnsEmpty_WhenConversationAtOrBelowRecentN()
    {
        var msgs = Enumerable.Range(1, 8).Select(i => BuildMessage(i, "m")).ToArray();
        var result = MemoryCompositionService.SelectMidWindow(msgs, recentN: 10, midEnd: 50);
        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectMidWindow_ReturnsCorrectSlice_For20MessagesWithDefaults()
    {
        var msgs = Enumerable.Range(1, 20).Select(i => BuildMessage(i, "m")).ToArray();
        var result = MemoryCompositionService.SelectMidWindow(msgs, recentN: 10, midEnd: 50);
        // Window [max(0,-30), 10) = [0, 10) → messages 1-10.
        result.Should().HaveCount(10);
        result.First().SequenceNumber.Should().Be(1);
        result.Last().SequenceNumber.Should().Be(10);
    }

    [Fact]
    public void SelectMidWindow_ReturnsCorrectSlice_For100MessagesWithDefaults()
    {
        var msgs = Enumerable.Range(1, 100).Select(i => BuildMessage(i, "m")).ToArray();
        var result = MemoryCompositionService.SelectMidWindow(msgs, recentN: 10, midEnd: 50);
        // Window [100-50, 100-10) = [50, 90) → messages 51..90 (40 items).
        result.Should().HaveCount(40);
        result.First().SequenceNumber.Should().Be(51);
        result.Last().SequenceNumber.Should().Be(90);
    }

    // =========================================================================================
    // Constructor null-arg defense
    // =========================================================================================

    [Fact]
    public void Ctor_ThrowsOnNullDependencies()
    {
        var opt = Options.Create(new MemoryCompositionOptions());

        Assert.Throws<ArgumentNullException>(() =>
            new MemoryCompositionService(null!, _repo.Object, _recall.Object, opt, _logger.Object));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryCompositionService(_compression.Object, null!, _recall.Object, opt, _logger.Object));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryCompositionService(_compression.Object, _repo.Object, null!, opt, _logger.Object));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryCompositionService(_compression.Object, _repo.Object, _recall.Object, null!, _logger.Object));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryCompositionService(_compression.Object, _repo.Object, _recall.Object, opt, null!));
    }

    // =========================================================================================
    // MemoryComposition.Empty + IsEmpty
    // =========================================================================================

    [Fact]
    public void MemoryComposition_Empty_IsEmpty_ReturnsTrue()
    {
        MemoryComposition.Empty.IsEmpty.Should().BeTrue();
        MemoryComposition.Empty.EstimatedTokenCount.Should().Be(0);
        MemoryComposition.Empty.DroppedLayers.Should().BeEmpty();
    }
}
