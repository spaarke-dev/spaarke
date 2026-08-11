using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="DailyBriefingOverviewHandler"/> — the Lane-3 (composed-service)
/// parity-tool backing for the Daily Briefing layout tab
/// (spaarkeai-assistant-enhancements-r3 task 021, FR-07).
/// </summary>
/// <remarks>
/// <para>
/// Constructs a REAL <see cref="BriefingService"/> (its <c>GetBriefingAsync</c> is not
/// virtual, so it cannot be Moq'd directly) wired to mocked leaf boundaries
/// (<see cref="IDataverseService"/>, <see cref="IMembershipResolverService"/>,
/// <see cref="IGenericEntityService"/> via a real <see cref="PortfolioService"/>) — the same
/// construction pattern as <c>BriefingServiceTests.CreateSut</c>. This exercises the handler's
/// public surface end-to-end against the real aggregation/heuristic logic, not a hand-rolled
/// double for <c>BriefingResponse</c>.
/// </para>
/// </remarks>
public sealed class DailyBriefingOverviewHandlerTests
{
    private static readonly Guid TestAadObjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly string TestAadOidString = TestAadObjectId.ToString("D");
    private static readonly Guid TestSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatterId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IDataverseService> _dataverseMock = new(MockBehavior.Strict);
    private readonly Mock<IMembershipResolverService> _resolverMock = new(MockBehavior.Strict);
    private readonly IDistributedCache _briefingCache = new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions()));
    private readonly Mock<IDistributedCache> _portfolioCacheMock = new(MockBehavior.Loose);
    private readonly Mock<IGenericEntityService> _portfolioEntityServiceMock = new(MockBehavior.Loose);

    private readonly List<TypedToolHandlerTestFixture.CapturedLogMessage> _capturedLogs = new();

    private DailyBriefingOverviewHandler CreateHandler()
    {
        _portfolioCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _portfolioCacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _portfolioEntityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var portfolio = new PortfolioService(
            _portfolioCacheMock.Object,
            _portfolioEntityServiceMock.Object,
            NullLogger<PortfolioService>.Instance);

        var briefingService = new BriefingService(
            portfolioService: portfolio,
            cache: _briefingCache,
            membershipResolver: _resolverMock.Object,
            dataverse: _dataverseMock.Object,
            logger: NullLogger<BriefingService>.Instance,
            briefingAi: null); // deterministic template narrative — no AI facade in unit tests

        return new DailyBriefingOverviewHandler(
            briefingService,
            new CapturingLogger(_capturedLogs));
    }

    private static AnalysisTool BuildTool() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "SYS-Daily Briefing Overview",
            Type = ToolType.Custom,
            Configuration = null
        };

    private static ChatInvocationContext BuildContext(string? userId, string? tenantId = "tenant-test") =>
        new()
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = tenantId ?? "tenant-test",
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = "{}",
            UserId = userId
        };

    private void SetupAadOidLookup(Guid? returnUserId)
    {
        var systemUserCollection = returnUserId.HasValue
            ? new EntityCollection(new List<Entity> { new Entity("systemuser", returnUserId.Value) })
            : new EntityCollection();

        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemUserCollection);
    }

    private static Entity BuildMatterEntity(Guid id, string name, int overdue, decimal spend, decimal budget, DateTime? deadline)
    {
        var entity = new Entity("sprk_matter", id);
        entity["sprk_name"] = name;
        entity["sprk_overdueeventcount"] = overdue;
        entity["sprk_totalspend"] = new Money(spend);
        entity["sprk_totalbudget"] = new Money(budget);
        entity["statecode"] = new OptionSetValue(0);
        if (deadline.HasValue)
        {
            entity["sprk_duedate"] = deadline.Value;
        }
        return entity;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Contract / discovery
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .Should().Contain(typeof(DailyBriefingOverviewHandler),
                because: "the handler must be auto-discovered by the assembly scan (ADR-010)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(nameof(DailyBriefingOverviewHandler));
    }

    [Fact]
    public void Metadata_IsValid_AndChatOnly()
    {
        var handler = CreateHandler();
        handler.Metadata.Name.Should().NotBeNullOrWhiteSpace();
        handler.Metadata.Description.Should().NotBeNullOrWhiteSpace();
        handler.Metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        handler.Metadata.Parameters.Should().BeEmpty("the briefing is always for the calling user — no arguments");
        handler.SupportedInvocationContexts.Should().Be(InvocationContextKind.Chat);
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_IsUnsupported()
    {
        var handler = CreateHandler();
        var ctx = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "t1",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "fixture-document.pdf",
                ExtractedText = "fixture text",
                FileName = "fixture-document.pdf",
                ContentType = "application/pdf"
            }
        };
        var tool = BuildTool();

        handler.Validate(ctx, tool).IsValid.Should().BeFalse();
        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument / identity validation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_MissingTenantId_Rejected()
    {
        var ctx = BuildContext(userId: TestAadOidString, tenantId: "");
        CreateHandler().ValidateChat(ctx, BuildTool()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateChat_MissingUserId_Rejected()
    {
        var ctx = BuildContext(userId: null);
        var result = CreateHandler().ValidateChat(ctx, BuildTool());
        result.IsValid.Should().BeFalse();
        string.Join(" ", result.Errors).Should().ContainEquivalentOf("user");
    }

    [Fact]
    public void ValidateChat_ValidContext_Passes()
    {
        var ctx = BuildContext(userId: TestAadOidString);
        CreateHandler().ValidateChat(ctx, BuildTool()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserId_FailsClosed_NoAppOnlyFallback()
    {
        var ctx = BuildContext(userId: null);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        // Strict mocks would have thrown if the handler had attempted an app-only/tenant-wide
        // resolution path instead of failing closed — no setups were configured, so any call
        // to _dataverseMock/_resolverMock here would already have thrown MockException.
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Happy path — real BriefingService aggregation, narrated overview + citation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsBriefingOverview_SourcedFromBriefingServiceForTheCallingUser()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        var memberships = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(TestSystemUserId, null, null, null, null, null, null),
            Ids: new[] { MatterId },
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 1,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        var deadline = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                BuildMatterEntity(MatterId, "Matter Alpha", overdue: 3, spend: 60_000m, budget: 100_000m, deadline: deadline)
            }));

        var ctx = BuildContext(userId: TestAadOidString);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("tool").GetString().Should().Be("spaarke.daily_briefing_overview");
        data.GetProperty("narrative").GetString().Should().NotBeNullOrWhiteSpace(
            "the response is always narratable — template narrative when AI is unavailable");
        data.TryGetProperty("topPriorityMatter", out var top).Should().BeTrue();
        top.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null,
            "a resolved top-priority matter must be surfaced");
        top.GetProperty("matterId").GetString().Should().Be(MatterId.ToString("D"));
        top.GetProperty("citationPath").GetString().Should().Be($"tables/sprk_matter/records/{MatterId:D}");

        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!).ToList();
        citations.Should().ContainSingle().Which.ChunkId.Should().Be($"tables/sprk_matter/records/{MatterId:D}");

        result.Summary.Should().NotBeNullOrWhiteSpace();

        // OBO scoping — the resolver was scoped to the identity cross-referenced FROM the
        // calling chat principal's oid (ChatInvocationContext.UserId), never app-only/tenant-wide.
        _resolverMock.Verify(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteChatAsync_NoTopPriorityMatter_ReturnsBriefingWithoutCitations()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        var emptyMemberships = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(TestSystemUserId, null, null, null, null, null, null),
            Ids: Array.Empty<Guid>(),
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyMemberships);

        var ctx = BuildContext(userId: TestAadOidString);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("activeMatters").GetInt32().Should().Be(0);
        data.GetProperty("topPriorityMatter").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null,
            "zero memberships → no top-priority matter, but the briefing still returns");
        data.GetProperty("narrative").GetString().Should().NotBeNullOrWhiteSpace();

        result.Metadata.Should().BeNull("no citations are attached when there is no top-priority matter to cite");
    }

    [Fact]
    public async Task ExecuteChatAsync_BriefingServiceFailureSoftPath_StillReturnsNarratableOverview()
    {
        // The resolver throws — BriefingService's own failure-soft contract (proven by
        // BriefingServiceTests.GetBriefing_ResolverThrows_ReturnsNullTopMatterWithFullBriefing)
        // degrades to TopPriorityMatter=null rather than failing the whole briefing. This test
        // proves the HANDLER surfaces that same degrade gracefully as tool success, not an error.
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated resolver failure"));

        var ctx = BuildContext(userId: TestAadOidString);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue("BriefingService degrades gracefully — a membership-resolution failure never fails the whole briefing");
        result.Data!.Value.GetProperty("narrative").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteChatAsync_Cancelled_ReturnsCancelledError()
    {
        SetupAadOidLookup(returnUserId: TestSystemUserId);
        using var cts = new CancellationTokenSource();
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, _, _) => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var ctx = BuildContext(userId: TestAadOidString);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 — narrative text + matter names never logged
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsNarrativeTextOrMatterNames_Adr015()
    {
        const string sensitiveMatterName = "Confidential Settlement — Acme v. Beta";
        SetupAadOidLookup(returnUserId: TestSystemUserId);

        var memberships = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(TestSystemUserId, null, null, null, null, null, null),
            Ids: new[] { MatterId },
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 1,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        _resolverMock
            .Setup(r => r.ResolveAsync(TestSystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                BuildMatterEntity(MatterId, sensitiveMatterName, overdue: 1, spend: 10_000m, budget: 20_000m, deadline: null)
            }));

        var ctx = BuildContext(userId: TestAadOidString);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var narrative = result.Data!.Value.GetProperty("narrative").GetString()!;
        narrative.Should().Contain(sensitiveMatterName, "the template narrative names the top-priority matter (returned to caller, not logged)");

        foreach (var log in _capturedLogs)
        {
            log.FormattedMessage.Should().NotContain(sensitiveMatterName,
                "ADR-015: matter names must never appear in app logs");
            log.FormattedMessage.Should().NotContain(narrative,
                "ADR-015: the narrative text (may be AI-generated free text) must never appear in app logs");
        }
    }

    /// <summary>Minimal capturing <see cref="ILogger{T}"/> mirroring the shared fixture's private one.</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<DailyBriefingOverviewHandler>
    {
        private readonly List<TypedToolHandlerTestFixture.CapturedLogMessage> _sink;
        public CapturingLogger(List<TypedToolHandlerTestFixture.CapturedLogMessage> sink) => _sink = sink;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Add(new TypedToolHandlerTestFixture.CapturedLogMessage(logLevel, eventId, formatter(state, exception)));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
