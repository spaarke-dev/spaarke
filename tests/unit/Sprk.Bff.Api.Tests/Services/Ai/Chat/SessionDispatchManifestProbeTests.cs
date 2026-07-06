using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// G-P2 UAT round-1 finding 4 (2026-07-06): the LOOP capability path (and every other
/// caller of the ONE dispatch seam) hits the same upload → manifest-write → cache
/// propagation race the Event path fixed at G-P1 Defect 3. A fresh upload followed by
/// an immediate "summarize this document" resolved ZERO target files and the capability
/// honestly reported the file as missing.
///
/// These tests prove <see cref="SessionDispatchOrchestrator.DispatchAsync"/> now runs
/// the SAME bounded wait-briefly-or-degrade manifest readiness probe
/// (<see cref="EventRulesOptions.ReadinessProbeAttempts"/> ×
/// <see cref="EventRulesOptions.ReadinessProbeDelayMs"/>) before degrading to the
/// zero-file error. Probe delay is 0 in tests (deterministic — no wall-clock waits).
/// </summary>
public class SessionDispatchManifestProbeTests
{
    private const string TenantId = "tenant-probe";
    private const string SessionId = "session-probe";
    private static readonly Guid BindingId = Guid.Parse("651194cd-3670-f111-ab0e-70a8a590c51c");
    private static readonly Guid ActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    // ─────────────────────────────────────────────────────────────────────────
    // Session-manager double: returns a scripted sequence of manifest states
    // (subclass pattern per TestableChatSessionManager / FakeChatSessionManager)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SequencedChatSessionManager : ChatSessionManager
    {
        private readonly Queue<ChatSession?> _reads = new();
        private ChatSession? _last;

        public int ReadCount { get; private set; }

        public SequencedChatSessionManager(params ChatSession?[] reads)
            : base(
                cache: Mock.Of<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
                dataverseRepository: Mock.Of<IChatDataverseRepository>(),
                logger: Mock.Of<ILogger<ChatSessionManager>>())
        {
            foreach (var read in reads)
            {
                _reads.Enqueue(read);
            }
        }

        public override Task<ChatSession?> GetSessionAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
        {
            ReadCount++;
            if (_reads.Count > 0)
            {
                _last = _reads.Dequeue();
            }
            return Task.FromResult(_last);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Probe resolves the fresh file: requested id appears on a re-read → executes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_RequestedFileAppearsOnProbeReRead_ExecutesAgainstFreshManifest()
    {
        var withoutFreshFile = BuildSession("file-old");
        var withFreshFile = BuildSession("file-old", "file-fresh");
        var sessions = new SequencedChatSessionManager(withoutFreshFile, withFreshFile);
        var (orchestrator, textSource) = BuildOrchestrator(sessions, probeAttempts: 3);

        var chunks = await Drain(orchestrator.DispatchAsync(new SessionDispatchRequest(
            TenantId, SessionId, BindingId, BuildArgs("file-fresh"))));

        chunks.Should().NotContain(c => c.Error != null,
            "the probe must resolve the just-uploaded file instead of degrading to the missing-file error");
        textSource.Verify(
            t => t.FetchAsync(
                TenantId, SessionId,
                It.Is<IReadOnlyList<ChatSessionFile>>(f => f.Count == 1 && f[0].FileId == "file-fresh"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "execution targets the file resolved by the probe's fresh manifest read");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bounded degrade: file never appears → honest error after ≤ attempts re-reads
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_RequestedFileNeverAppears_DegradesToErrorAfterBoundedProbe()
    {
        const int attempts = 3;
        var sessions = new SequencedChatSessionManager(BuildSession("file-old"));
        var (orchestrator, _) = BuildOrchestrator(sessions, probeAttempts: attempts);

        var chunks = await Drain(orchestrator.DispatchAsync(new SessionDispatchRequest(
            TenantId, SessionId, BindingId, BuildArgs("file-ghost"))));

        chunks.Should().ContainSingle(c => c.Error != null).Which.Error
            .Should().Contain("No session files were available",
                "wait-briefly-or-degrade: an id that never lands still gets the honest error");
        sessions.ReadCount.Should().Be(1 + attempts,
            "the probe is BOUNDED — initial read + at most ReadinessProbeAttempts re-reads");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Default-all (FR-08) with an empty manifest also probes: the fresh upload may
    // simply not be visible yet
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_DefaultAllWithEmptyManifest_ProbesUntilManifestNonEmpty()
    {
        var emptySession = BuildSession();
        var withFile = BuildSession("file-fresh");
        var sessions = new SequencedChatSessionManager(emptySession, emptySession, withFile);
        var (orchestrator, textSource) = BuildOrchestrator(sessions, probeAttempts: 4);

        var chunks = await Drain(orchestrator.DispatchAsync(new SessionDispatchRequest(
            TenantId, SessionId, BindingId, Args: null)));

        chunks.Should().NotContain(c => c.Error != null);
        textSource.Verify(
            t => t.FetchAsync(
                TenantId, SessionId,
                It.Is<IReadOnlyList<ChatSessionFile>>(f => f.Count == 1 && f[0].FileId == "file-fresh"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-08 default-all resolves against the manifest state the probe observed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // No-probe fast path: complete resolution on the first read never re-reads
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ResolutionCompleteOnFirstRead_DoesNotProbe()
    {
        var sessions = new SequencedChatSessionManager(BuildSession("file-A"));
        var (orchestrator, _) = BuildOrchestrator(sessions, probeAttempts: 5);

        var chunks = await Drain(orchestrator.DispatchAsync(new SessionDispatchRequest(
            TenantId, SessionId, BindingId, BuildArgs("file-A"))));

        chunks.Should().NotContain(c => c.Error != null);
        // One read in DispatchAsync + one inside PendingPlanManager.ResolveElicitationOnDispatchAsync
        // (the elicitation-resolution ledger check at the dispatch seam, FR-P2-03).
        sessions.ReadCount.Should().Be(2,
            "the common path (file already visible) adds NO probe re-reads and NO latency");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Builders
    // ─────────────────────────────────────────────────────────────────────────

    private static (SessionDispatchOrchestrator Orchestrator, Mock<ISessionFileTextSource> TextSource)
        BuildOrchestrator(ChatSessionManager sessions, int probeAttempts)
    {
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = BindingId,
                ConsumerType = "chat-summarize",
                ActionId = ActionId,
                ActionKind = ActionKind.Prompted,
                Disposition = BindingDisposition.Informational,
            });

        var scopeResolver = new Mock<IScopeResolverService>();
        scopeResolver
            .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = ActionId, Name = "Summarize (probe test)" });

        var actionRunner = new Mock<IActionRunner>();
        actionRunner
            .Setup(r => r.RunAsync(
                It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { summary = "probe-test summary" }));

        var textSource = new Mock<ISessionFileTextSource>();
        textSource
            .Setup(t => t.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText
            {
                ExtractedText = "lorem ipsum",
                DisplayName = "probe.pdf",
                ChunkCount = 1,
            });

        var router = new Mock<IOutputRouter>();
        router
            .Setup(r => r.RouteAsync(
                It.IsAny<ChatSession>(), It.IsAny<Binding>(), It.IsAny<JsonElement>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns((ChatSession s, Binding b, JsonElement output, IReadOnlyList<string>? _, CancellationToken _) =>
                Task.FromResult(new RoutedOutput
                {
                    Entry = new SessionOutput
                    {
                        Key = SessionLedger.BuildOutputKey(b.BindingId.ToString(), 1),
                        BindingId = b.BindingId.ToString(),
                        UcId = b.Ucid ?? b.ConsumerType,
                        Turn = 1,
                        Disposition = "informational",
                        Payload = output.Clone(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    Session = s,
                }));

        var pendingPlanManager = new PendingPlanManager(
            new InMemoryTenantCache(), sessions, Mock.Of<ILogger<PendingPlanManager>>());

        var orchestrator = new SessionDispatchOrchestrator(
            sessions,
            routing.Object,
            scopeResolver.Object,
            actionRunner.Object,
            textSource.Object,
            router.Object,
            pendingPlanManager,
            Options.Create(new EventRulesOptions
            {
                // Bounded + deterministic: 0 ms delay (no wall-clock waits in tests —
                // the probe's Task.Delay(0) yields without sleeping).
                ReadinessProbeAttempts = probeAttempts,
                ReadinessProbeDelayMs = 0,
            }),
            Mock.Of<ILogger<SessionDispatchOrchestrator>>());

        return (orchestrator, textSource);
    }

    private static ChatSession BuildSession(params string[] fileIds)
    {
        var files = fileIds
            .Select(id => new ChatSessionFile(
                FileId: id, FileName: $"{id}.pdf", ContentType: "application/pdf",
                SizeBytes: 1024, SearchDocumentIdsCsv: $"doc-{id}-1",
                UploadedAt: DateTimeOffset.UtcNow))
            .ToList();

        return new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            UploadedFiles: files.Count > 0 ? files : null);
    }

    private static JsonElement? BuildArgs(params string[] fileIds)
        => JsonSerializer.SerializeToElement(new { fileIds });

    private static async Task<List<AnalysisChunk>> Drain(IAsyncEnumerable<AnalysisChunk> stream)
    {
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }
        return chunks;
    }
}
