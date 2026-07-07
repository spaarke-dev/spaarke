using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-P3-03 (task 042) — the typed-handler confirm-RESUME seam
/// (<see cref="TypedHandlerResumeExecutor"/>): resolution inverts the projection's
/// name transform against the catalog (ADR-039 — declarations, never name lists);
/// execution drives the SAME handler contract the loop's adapter uses
/// (<c>ValidateChat</c> → <c>ExecuteChatAsync</c>) with the STORED args under the
/// confirming user's context; the outcome ledger-writes (SessionOutput
/// <c>loop@t{n}</c> + ToolChain, NFR-07 redaction-safe) BEFORE the result returns
/// for rendering (ADR-040).
/// </summary>
/// <remarks>
/// Placement note: sibling to the ConfirmationGateUnification / LoopElicitation
/// suites (030-W5 / 033-W2 / 041-W3 precedent). Real <see cref="PendingPlanManager"/> +
/// <see cref="ChatSessionManager"/> over the in-memory tenant cache; the Dataverse
/// catalog read is stubbed at the module boundary via a
/// <see cref="StubAnalysisToolService"/> subclass (ADR-038 — no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertions).
/// </remarks>
public class TypedHandlerResumeExecutorTests
{
    private const string TenantId = "tenant-resume-tests";

    // =========================================================================
    // Resolution — TryResolveAsync (peek-only supportability decision)
    // =========================================================================

    [Fact]
    public async Task TryResolveAsync_SuspendedToolId_InvertsTheProjectionNameTransform_ReturnsRowAndHandler()
    {
        // The gate wrapper preserved the LLM function name verbatim from the projection:
        // SanitiseToolName("SYS-Dataverse Create Record") == "SYS-Dataverse_Create_Record".
        var row = BuildChatRow("SYS-Dataverse Create Record", handlerClass: SpyToolHandler.Id);
        var handler = new SpyToolHandler();
        var executor = BuildExecutor(new[] { row }, handler, out _, out _);

        var resolution = await executor.TryResolveAsync("SYS-Dataverse_Create_Record", CancellationToken.None);

        resolution.Should().NotBeNull();
        resolution!.Tool.Id.Should().Be(row.Id);
        resolution.Handler.Should().BeSameAs(handler,
            "the handler resolves by the row's DECLARED sprk_handlerclass (ADR-039 — no name lists)");
    }

    [Fact]
    public async Task TryResolveAsync_NoMatchingChatAvailableRow_ReturnsNull()
    {
        // Same name but playbook-only availability — NOT resolvable from the chat gate.
        var playbookOnly = BuildChatRow("SYS-Dataverse Create Record", handlerClass: SpyToolHandler.Id)
            with
        { AvailableInContexts = ToolAvailabilityContext.Playbook };
        var executor = BuildExecutor(new[] { playbookOnly }, new SpyToolHandler(), out _, out _);

        (await executor.TryResolveAsync("SYS-Dataverse_Create_Record", CancellationToken.None))
            .Should().BeNull("non-chat rows are unsupported — the caller keeps the honest confirmed-unexecutable path");
        (await executor.TryResolveAsync("sys_totally_unknown_tool", CancellationToken.None))
            .Should().BeNull("an unmatched tool id is unsupported");
    }

    [Fact]
    public async Task TryResolveAsync_RowWithoutRegisteredHandler_ReturnsNull()
    {
        var row = BuildChatRow("SYS-Dataverse Create Record", handlerClass: "NotARegisteredHandler");
        var executor = BuildExecutor(new[] { row }, new SpyToolHandler(), out _, out _);

        (await executor.TryResolveAsync("SYS-Dataverse_Create_Record", CancellationToken.None))
            .Should().BeNull("a row whose HandlerClass is not registered cannot execute — honest fallback");
    }

    // =========================================================================
    // Execution — stored args, user context, ledger writes (ADR-040 / NFR-07)
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_ConfirmedInvocation_ExecutesHandlerWithStoredArgsUnderTheConfirmingUser()
    {
        var row = BuildChatRow("SYS-Dataverse Create Record", handlerClass: SpyToolHandler.Id);
        var handler = new SpyToolHandler();
        var executor = BuildExecutor(new[] { row }, handler, out var sessionManager, out var sessionId);
        var invocation = BuildInvocation(sessionId, argsJson:
            """{"tablename":"sprk_event","item":{"sprk_eventname":"Review NDA issues"}}""");
        var oid = Guid.NewGuid().ToString("D");

        var resolution = await executor.TryResolveAsync(invocation.ToolId, CancellationToken.None);
        var outcome = await executor.ExecuteAsync(resolution!, invocation, oid, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Summary.Should().Be("spy-summary");
        handler.Executions.Should().Be(1, "the CONFIRMED invocation executes exactly once");
        handler.LastContext.Should().NotBeNull();
        handler.LastContext!.ToolArgumentsJson.Should().Be(invocation.ArgsJson,
            "the resume executes the STORED args verbatim — no re-run of the LLM (Tier-3 payload)");
        handler.LastContext.TenantId.Should().Be(TenantId);
        handler.LastContext.UserId.Should().Be(oid,
            "the confirming principal's oid flows into the handler context (user-OBO discipline)");
        handler.LastContext.RequestedToolName.Should().Be(row.Name);
        handler.LastContext.ChatSessionId.ToString("N").Should().Be(sessionId,
            "the invocation's session id is the handler's correlation identity");
    }

    [Fact]
    public async Task ExecuteAsync_Success_LedgerWritesSessionOutputAndToolChain_RedactionSafe()
    {
        var row = BuildChatRow("SYS-Dataverse Create Record", handlerClass: SpyToolHandler.Id);
        var handler = new SpyToolHandler
        {
            Result = tool => new ToolResult
            {
                HandlerId = SpyToolHandler.Id,
                ToolId = tool.Id,
                ToolName = tool.Name,
                Success = true,
                Execution = new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow },
                Summary = "Created record 11111111-2222-3333-4444-555555555555 in 'sprk_event'.",
                Data = JsonSerializer.SerializeToElement(new
                {
                    tablename = "sprk_event",
                    recordId = "11111111-2222-3333-4444-555555555555",
                }),
                Metadata = new Dictionary<string, object?>
                {
                    [ToolResultMetadataKeys.Citations] = new[]
                    {
                        new { chunkId = "tables/sprk_event/records/11111111-2222-3333-4444-555555555555", sourceName = "sprk_event" },
                    },
                },
            },
        };
        var executor = BuildExecutor(new[] { row }, handler, out var sessionManager, out var sessionId);
        var freeText = "Prepare and send the firm's response to Acme about the uncapped indemnity clause";
        var invocation = BuildInvocation(sessionId, argsJson: JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["tablename"] = "sprk_event",
                ["item"] = new Dictionary<string, object> { ["sprk_description"] = freeText },
            }));

        var resolution = await executor.TryResolveAsync(invocation.ToolId, CancellationToken.None);
        var outcome = await executor.ExecuteAsync(resolution!, invocation, userObjectId: null, CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.LedgerKey.Should().Be("loop@t1",
            "the executed result is an addressable loop-native ledger output (ADR-040 reserved binding id)");

        var session = await sessionManager.GetSessionAsync(TenantId, sessionId);

        // SessionOutput — addressable evidence of the executed record side effect.
        var output = session!.Outputs.Should().ContainSingle().Subject;
        output.Key.Should().Be("loop@t1");
        output.BindingId.Should().Be("loop");
        output.UcId.Should().Be(invocation.ToolId);
        output.Disposition.Should().Be("record",
            "the payload documents an already-executed record write (the rendering is the endpoint's JSON response)");
        output.Payload.GetProperty("recordId").GetString().Should().Be("11111111-2222-3333-4444-555555555555");
        output.SourceRefs.Should().ContainSingle()
            .Which.Should().Be("tables/sprk_event/records/11111111-2222-3333-4444-555555555555",
                "citation ids from the handler's metadata become the output's source refs (ids only)");

        // ToolChain — NFR-07 audit entry: identifiers + redaction-safe summary only.
        var chain = session.ToolChains.Should().ContainSingle().Subject;
        var call = chain.Calls.Should().ContainSingle().Subject;
        call.ToolId.Should().Be(invocation.ToolId);
        call.ArgsSummary.Should().NotBeNull();
        call.ArgsSummary.Should().NotContain("Acme",
            "free-text argument content must NEVER reach the ToolChain ledger (NFR-07)");
        call.ArgsSummary.Should().Contain("tablename=sprk_event",
            "identifier-shaped values stay verbatim for auditability");
        call.DurationMs.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_HandlerFailure_ReturnsError_NoSessionOutput_ToolChainStillAudited()
    {
        var row = BuildChatRow("SYS-Dataverse Create Record", handlerClass: SpyToolHandler.Id);
        var handler = new SpyToolHandler
        {
            Result = tool => new ToolResult
            {
                HandlerId = SpyToolHandler.Id,
                ToolId = tool.Id,
                ToolName = tool.Name,
                Success = false,
                Execution = new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow },
                ErrorCode = "DATAVERSE_ACCESS_DENIED",
                ErrorMessage = "The calling user cannot create rows in this table.",
            },
        };
        var executor = BuildExecutor(new[] { row }, handler, out var sessionManager, out var sessionId);
        var invocation = BuildInvocation(sessionId, argsJson: """{"tablename":"sprk_event"}""");

        var resolution = await executor.TryResolveAsync(invocation.ToolId, CancellationToken.None);
        var outcome = await executor.ExecuteAsync(resolution!, invocation, userObjectId: null, CancellationToken.None);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("cannot create rows",
            "the USER's own access error surfaces (user-OBO: no privilege escalation, no masking)");

        var session = await sessionManager.GetSessionAsync(TenantId, sessionId);
        session!.Outputs.Should().BeNullOrEmpty("a failed execution produced no record — no output entry");
        session.ToolChains.Should().ContainSingle("the attempted call is still audited on the ToolChain");
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFailure_HandlerNeverExecutes()
    {
        var row = BuildChatRow("SYS-Dataverse Create Record", handlerClass: SpyToolHandler.Id);
        var handler = new SpyToolHandler { ValidationError = "TenantId is required." };
        var executor = BuildExecutor(new[] { row }, handler, out var sessionManager, out var sessionId);
        var invocation = BuildInvocation(sessionId, argsJson: "{}");

        var resolution = await executor.TryResolveAsync(invocation.ToolId, CancellationToken.None);
        var outcome = await executor.ExecuteAsync(resolution!, invocation, userObjectId: null, CancellationToken.None);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("TenantId is required");
        handler.Executions.Should().Be(0, "ValidateChat failure short-circuits — same contract as the loop adapter");
        (await sessionManager.GetSessionAsync(TenantId, sessionId))!
            .Outputs.Should().BeNullOrEmpty();
    }

    // =========================================================================
    // NFR-07 args summarization (the ONE summarizer, rebuilt from stored JSON)
    // =========================================================================

    [Fact]
    public void SummarizeArgsJson_IdentifiersKept_FreeTextAndContentBearingNamesRedacted()
    {
        var summary = TypedHandlerResumeExecutor.SummarizeArgsJson(
            """{"tablename":"sprk_event","content":"short","note":"this free text has spaces"}""");

        summary.Should().Contain("tablename=sprk_event", "identifier-shaped values stay verbatim");
        summary.Should().NotContain("this free text has spaces", "free text redacts by shape");
        summary.Should().Contain("content=<redacted:",
            "content-bearing arg NAMES redact regardless of shape (AgentTurnContract.AlwaysRedactedArgNames)");
    }

    [Fact]
    public void SummarizeArgsJson_EmptyOrMalformed_ReturnsNull()
    {
        TypedHandlerResumeExecutor.SummarizeArgsJson(null).Should().BeNull();
        TypedHandlerResumeExecutor.SummarizeArgsJson("   ").Should().BeNull();
        TypedHandlerResumeExecutor.SummarizeArgsJson("{}").Should().BeNull();
        TypedHandlerResumeExecutor.SummarizeArgsJson("{not json").Should().BeNull();
    }

    // =========================================================================
    // Harness
    // =========================================================================

    private static TypedHandlerResumeExecutor BuildExecutor(
        IReadOnlyList<AnalysisTool> rows,
        IToolHandler handler,
        out ChatSessionManager sessionManager,
        out string sessionId)
    {
        var cache = new InMemoryTenantCache();
        sessionManager = new ChatSessionManager(
            cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChatSessionManager>>().Object);

        sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        sessionManager.UpdateSessionCacheAsync(new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: new List<ChatMessage>())).GetAwaiter().GetResult();

        var registry = new ToolHandlerRegistry(
            new[] { handler },
            Options.Create(new ToolFrameworkOptions()),
            NullLogger<ToolHandlerRegistry>.Instance);

        return new TypedHandlerResumeExecutor(
            new StubAnalysisToolService(rows),
            registry,
            sessionManager,
            NullLogger.Instance);
    }

    private static AnalysisTool BuildChatRow(string name, string handlerClass) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = "test row",
        HandlerClass = handlerClass,
        AvailableInContexts = ToolAvailabilityContext.Chat,
        JsonSchema = """{"type":"object","properties":{}}""",
        SideEffectClass = ToolSideEffectClass.Write,
    };

    private static PendingInvocation BuildInvocation(string sessionId, string argsJson) => new()
    {
        GateId = $"confirmation-{Guid.NewGuid():N}",
        Kind = PendingPlanManager.GateKindConfirmation,
        SessionId = sessionId,
        TenantId = TenantId,
        ToolId = "SYS-Dataverse_Create_Record",
        SideEffectClass = "write",
        Risk = "none",
        ArgsJson = argsJson,
        Turn = 1,
    };

    /// <summary>
    /// Stubs the Dataverse catalog read at the module boundary (ADR-038) — the base
    /// class never issues HTTP because the override returns the supplied rows.
    /// </summary>
    private sealed class StubAnalysisToolService : AnalysisToolService
    {
        private readonly IReadOnlyList<AnalysisTool> _rows;

        public StubAnalysisToolService(IReadOnlyList<AnalysisTool> rows)
            : base(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dataverse:ServiceUrl"] = "https://stub.crm.dynamics.com",
                }).Build(),
                new Mock<Azure.Core.TokenCredential>().Object,
                NullLogger<AnalysisToolService>.Instance)
        {
            _rows = rows;
        }

        public override Task<ScopeListResult<AnalysisTool>> ListToolsAsync(
            ScopeListOptions options, CancellationToken cancellationToken)
            => Task.FromResult(new ScopeListResult<AnalysisTool>
            {
                Items = _rows.ToArray(),
                TotalCount = _rows.Count,
                Page = 1,
                PageSize = options.PageSize,
            });
    }

    /// <summary>
    /// Minimal chat-capable handler spy: captures the invocation context; result and
    /// validation outcome are configurable per test.
    /// </summary>
    private sealed class SpyToolHandler : IToolHandler
    {
        public const string Id = "SpyToolHandler";

        public int Executions { get; private set; }
        public ChatInvocationContext? LastContext { get; private set; }
        public string? ValidationError { get; init; }
        public Func<AnalysisTool, ToolResult>? Result { get; init; }

        public string HandlerId => Id;

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Spy Tool Handler",
            Description: "Test double capturing chat invocations.",
            Version: "1.0.0",
            SupportedInputTypes: new[] { "text/plain" },
            Parameters: Array.Empty<ToolParameterDefinition>());

        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
            => ToolValidationResult.Failure("chat-only spy");

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken)
            => throw new NotSupportedException("chat-only spy");

        public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
            => ValidationError is null ? ToolValidationResult.Success() : ToolValidationResult.Failure(ValidationError);

        public Task<ToolResult> ExecuteChatAsync(ChatInvocationContext context, AnalysisTool tool, CancellationToken cancellationToken)
        {
            Executions++;
            LastContext = context;
            var result = Result?.Invoke(tool) ?? new ToolResult
            {
                HandlerId = Id,
                ToolId = tool.Id,
                ToolName = tool.Name,
                Success = true,
                Execution = new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow },
                Summary = "spy-summary",
            };
            return Task.FromResult(result);
        }
    }
}
