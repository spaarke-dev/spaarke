using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// Unit tests for the R6 Pillar 3 / Q11 / task 020 <see cref="IInvokePlaybookAi"/> facade
/// (interface + <see cref="InvokePlaybookAi"/> impl + <see cref="NullInvokePlaybookAi"/>
/// kill-switch peer).
/// </summary>
/// <remarks>
/// <para>
/// Coverage matrix:
/// <list type="bullet">
///   <item><b>ADR-013 facade boundary</b>: reflection-based assertion that the public surface
///   exposes only domain-shape types (no <c>PlaybookStreamEvent</c>, <c>NodeOutput</c>,
///   <c>IPlaybookOrchestrationService</c>, <c>IOpenAiClient</c> leaks).</item>
///   <item><b>Delegation</b>: facade calls <see cref="IPlaybookOrchestrationService.ExecuteAsync"/>
///   with the caller-supplied <c>playbookId</c> + <c>parameters</c> + <c>HttpContext</c>.</item>
///   <item><b>Aggregation</b>: terminal-node text + structured data + citations are projected
///   from <see cref="NodeOutput"/> + <see cref="ToolResult.Metadata"/> into
///   <see cref="PlaybookInvocationResult"/>.</item>
///   <item><b>Telemetry hygiene (ADR-015)</b>: sentinel-string scan over captured log
///   messages — no parameter values, no user content; only playbookId + runId + decision
///   + tenantId.</item>
///   <item><b>Failure paths</b>: RunFailed → Success=false + ErrorMessage set; cancellation
///   propagates.</item>
///   <item><b>Null peer (ADR-032 P3 Fail-fast)</b>: throws
///   <see cref="FeatureDisabledException"/> with stable error code
///   <c>ai.playbook-invocation.disabled</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class InvokePlaybookAiTests
{
    private static readonly Assembly BffAssembly = typeof(IInvokePlaybookAi).Assembly;

    // ────────────────────────────────────────────────────────────────────────
    // ADR-013 facade boundary: public surface must not leak AI-internal types
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ADR-013 binding: <see cref="IInvokePlaybookAi"/> + its return type
    /// <see cref="PlaybookInvocationResult"/> + its input type
    /// <see cref="PlaybookInvocationContext"/> must NOT reference any AI-internal
    /// orchestration type through their public members.
    /// </summary>
    [Fact]
    public void Facade_PublicSurface_DoesNotLeakAiInternalTypes()
    {
        var forbiddenTypes = new[]
        {
            typeof(PlaybookStreamEvent).FullName!,
            typeof(NodeOutput).FullName!,
            typeof(PlaybookRunMetrics).FullName!,
            typeof(PlaybookEventType).FullName!,
            typeof(IPlaybookOrchestrationService).FullName!,
        };

        var surfaceTypes = new[]
        {
            typeof(IInvokePlaybookAi),
            typeof(PlaybookInvocationResult),
            typeof(PlaybookInvocationContext),
        };

        foreach (var t in surfaceTypes)
        {
            foreach (var member in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var typeRefs = new List<Type>();

                if (member is MethodInfo mi)
                {
                    typeRefs.Add(mi.ReturnType);
                    typeRefs.AddRange(mi.GetParameters().Select(p => p.ParameterType));
                }
                else if (member is PropertyInfo pi)
                {
                    typeRefs.Add(pi.PropertyType);
                }

                foreach (var refType in typeRefs)
                {
                    var fullName = refType.FullName ?? refType.Name;
                    forbiddenTypes.Should().NotContain(fullName,
                        $"public member {t.Name}.{member.Name} must not reference AI-internal type {fullName} (ADR-013)");
                }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Delegation + aggregation
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokePlaybookAsync_DelegatesToOrchestrator_WithCallerArguments()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var parameters = new Dictionary<string, string> { ["scope"] = "matter" };
        var httpContext = new DefaultHttpContext();
        var context = new PlaybookInvocationContext
        {
            TenantId = "tenant-a",
            HttpContext = httpContext,
        };

        var orchestrator = new Mock<IPlaybookOrchestrationService>(MockBehavior.Strict);
        PlaybookRunRequest? capturedRequest = null;
        HttpContext? capturedHttpContext = null;

        orchestrator
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns((PlaybookRunRequest req, HttpContext ctx, CancellationToken ct) =>
            {
                capturedRequest = req;
                capturedHttpContext = ctx;
                return YieldEvents(new[]
                {
                    PlaybookStreamEvent.RunStarted(runId, playbookId, nodeCount: 1),
                    PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics { TotalNodes = 1, CompletedNodes = 1 }),
                });
            });

        var sut = new InvokePlaybookAi(orchestrator.Object, NullLogger<InvokePlaybookAi>.Instance);

        // Act
        var result = await sut.InvokePlaybookAsync(playbookId, parameters, context, CancellationToken.None);

        // Assert — delegation
        orchestrator.Verify(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.PlaybookId.Should().Be(playbookId);
        capturedRequest.Parameters.Should().BeSameAs(parameters);
        capturedRequest.DocumentIds.Should().BeEmpty("invoke_playbook callers pass parameters only — no document context");
        capturedHttpContext.Should().BeSameAs(httpContext, "HttpContext flows through unchanged for OBO auth");

        // Assert — result shape
        result.Should().NotBeNull();
        result.RunId.Should().Be(runId);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task InvokePlaybookAsync_AggregatesTerminalNodeOutput()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var structuredData = JsonSerializer.SerializeToElement(new { entities = new[] { "X" } });

        var orchestrator = BuildOrchestratorEmitting(new[]
        {
            PlaybookStreamEvent.RunStarted(runId, playbookId, 1),
            PlaybookStreamEvent.NodeCompleted(runId, playbookId, nodeId, "DeliverOutput", new NodeOutput
            {
                NodeId = nodeId,
                OutputVariable = "deliverable",
                Success = true,
                TextContent = "summary text",
                StructuredData = structuredData,
                Confidence = 0.87,
                Metrics = NodeExecutionMetrics.Empty,
                IsDeliverOutput = true,
            }),
            PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics { TotalNodes = 1, CompletedNodes = 1 }),
        });

        var sut = new InvokePlaybookAi(orchestrator, NullLogger<InvokePlaybookAi>.Instance);

        // Act
        var result = await sut.InvokePlaybookAsync(
            playbookId,
            parameters: null,
            new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Be("summary text");
        result.StructuredData.Should().NotBeNull();
        result.StructuredData!.Value.GetProperty("entities").GetArrayLength().Should().Be(1);
        result.Confidence.Should().Be(0.87);
        result.ErrorMessage.Should().BeNull();
        result.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task InvokePlaybookAsync_AccumulatesCitationsFromToolResults()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var citations = new[]
        {
            new ToolResultCitation("chunk-1", "doc-a.pdf", PageNumber: 2),
            new ToolResultCitation("chunk-2", "doc-b.pdf"),
        };

        var toolResult = ToolResult.Ok(
            handlerId: "kb-search",
            toolId: toolId,
            toolName: "kb-search",
            data: new { hits = 2 }) with
        {
            Metadata = new Dictionary<string, object?>
            {
                [ToolResultMetadataKeys.Citations] = (IEnumerable<ToolResultCitation>)citations,
            },
        };

        var nodeOutput = new NodeOutput
        {
            NodeId = nodeId,
            OutputVariable = "search",
            Success = true,
            TextContent = "found",
            Metrics = NodeExecutionMetrics.Empty,
            ToolResults = new[] { toolResult },
        };

        var orchestrator = BuildOrchestratorEmitting(new[]
        {
            PlaybookStreamEvent.RunStarted(runId, playbookId, 1),
            PlaybookStreamEvent.NodeCompleted(runId, playbookId, nodeId, "Search", nodeOutput),
            PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics { TotalNodes = 1, CompletedNodes = 1 }),
        });

        var sut = new InvokePlaybookAi(orchestrator, NullLogger<InvokePlaybookAi>.Instance);

        // Act
        var result = await sut.InvokePlaybookAsync(
            playbookId,
            parameters: null,
            new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
            CancellationToken.None);

        // Assert
        result.Citations.Should().HaveCount(2);
        result.Citations[0].ChunkId.Should().Be("chunk-1");
        result.Citations[0].SourceName.Should().Be("doc-a.pdf");
        result.Citations[0].PageNumber.Should().Be(2);
        result.Citations[1].ChunkId.Should().Be("chunk-2");
    }

    [Fact]
    public async Task InvokePlaybookAsync_RunFailed_ReturnsSuccessFalseWithErrorMessage()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var orchestrator = BuildOrchestratorEmitting(new[]
        {
            PlaybookStreamEvent.RunStarted(runId, playbookId, 1),
            PlaybookStreamEvent.RunFailed(runId, playbookId, "node X failed"),
        });

        var sut = new InvokePlaybookAi(orchestrator, NullLogger<InvokePlaybookAi>.Instance);

        // Act
        var result = await sut.InvokePlaybookAsync(
            playbookId,
            parameters: null,
            new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("node X failed");
        result.ErrorCode.Should().Be("PLAYBOOK_INVOCATION_FAILED");
    }

    [Fact]
    public async Task InvokePlaybookAsync_CancellationPropagates()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        var orchestrator = new Mock<IPlaybookOrchestrationService>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns((PlaybookRunRequest _, HttpContext _, CancellationToken ct) => CancelingEnumerable(cts));

        var sut = new InvokePlaybookAi(orchestrator.Object, NullLogger<InvokePlaybookAi>.Instance);

        // Act + Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.InvokePlaybookAsync(
                Guid.NewGuid(),
                parameters: null,
                new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
                cts.Token));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Argument validation
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokePlaybookAsync_EmptyPlaybookId_Throws()
    {
        var sut = new InvokePlaybookAi(Mock.Of<IPlaybookOrchestrationService>(), NullLogger<InvokePlaybookAi>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.InvokePlaybookAsync(
                Guid.Empty,
                parameters: null,
                new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
                CancellationToken.None));
    }

    [Fact]
    public async Task InvokePlaybookAsync_NullContext_Throws()
    {
        var sut = new InvokePlaybookAi(Mock.Of<IPlaybookOrchestrationService>(), NullLogger<InvokePlaybookAi>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.InvokePlaybookAsync(
                Guid.NewGuid(),
                parameters: null,
                context: null!,
                CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullOrchestrator_Throws()
    {
        var act = () => new InvokePlaybookAi(orchestrator: null!, NullLogger<InvokePlaybookAi>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new InvokePlaybookAi(Mock.Of<IPlaybookOrchestrationService>(), logger: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry hygiene — sentinel-string log scan
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokePlaybookAsync_TelemetryHygiene_NoParameterValuesInLogs()
    {
        // Arrange
        // Sentinel values that MUST NOT appear in any log message — they represent
        // user content / parameter values that would violate ADR-015 if logged.
        const string sentinelParamValue = "SENSITIVE-USER-CONTENT-XYZ123";
        const string sentinelTenantId = "TENANT-Y-DETERMINISTIC";

        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var orchestrator = BuildOrchestratorEmitting(new[]
        {
            PlaybookStreamEvent.RunStarted(runId, playbookId, 1),
            PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics { TotalNodes = 1, CompletedNodes = 1 }),
        });

        var loggedMessages = new List<string>();
        var logger = new CapturingLogger<InvokePlaybookAi>(loggedMessages);

        var sut = new InvokePlaybookAi(orchestrator, logger);

        // Act
        await sut.InvokePlaybookAsync(
            playbookId,
            parameters: new Dictionary<string, string> { ["userQuery"] = sentinelParamValue },
            new PlaybookInvocationContext { TenantId = sentinelTenantId, HttpContext = new DefaultHttpContext() },
            CancellationToken.None);

        // Assert
        loggedMessages.Should().NotBeEmpty("the impl SHOULD log start + completion at Information level");

        // Parameter values MUST NEVER appear in logs.
        loggedMessages.Should().NotContain(m => m.Contains(sentinelParamValue, StringComparison.OrdinalIgnoreCase),
            "ADR-015: parameter VALUES MUST NOT appear in log messages (only counts / IDs)");

        // Deterministic IDs (tenantId, playbookId, runId) are PERMITTED per ADR-015
        // — but the test does not assert their presence (logger format is implementation choice).
        // The negative assertion above (no parameter values) is the binding ADR-015 check.
    }

    // ────────────────────────────────────────────────────────────────────────
    // NullInvokePlaybookAi (ADR-032 P3 Fail-fast)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Null_InvokePlaybookAsync_ThrowsFeatureDisabledException()
    {
        // Arrange
        var sut = new NullInvokePlaybookAi(NullLogger<NullInvokePlaybookAi>.Instance);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(() =>
            sut.InvokePlaybookAsync(
                Guid.NewGuid(),
                parameters: null,
                new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
                CancellationToken.None));

        ex.ErrorCode.Should().Be(NullInvokePlaybookAi.ErrorCode);
        ex.ErrorCode.Should().Be("ai.playbook-invocation.disabled",
            "errorCode must be stable across releases — clients switch on this string");
        ex.Message.Should().Contain("Playbook invocation");
    }

    [Fact]
    public async Task Null_InvokePlaybookAsync_ExceptionConvertsToProblemDetails503()
    {
        var sut = new NullInvokePlaybookAi(NullLogger<NullInvokePlaybookAi>.Instance);

        try
        {
            await sut.InvokePlaybookAsync(
                Guid.NewGuid(),
                parameters: null,
                new PlaybookInvocationContext { TenantId = "tenant-a", HttpContext = new DefaultHttpContext() },
                CancellationToken.None);
            throw new InvalidOperationException("Expected FeatureDisabledException not thrown");
        }
        catch (FeatureDisabledException ex)
        {
            var result = ex.AsFeatureDisabled503();
            result.Should().NotBeNull(
                "FeatureDisabledResults.AsFeatureDisabled503 must accept the facade's exception unchanged");
        }
    }

    [Fact]
    public void Null_Constructor_NullLogger_Throws()
    {
        var act = () => new NullInvokePlaybookAi(logger: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static IPlaybookOrchestrationService BuildOrchestratorEmitting(PlaybookStreamEvent[] events)
    {
        var orchestrator = new Mock<IPlaybookOrchestrationService>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(YieldEvents(events));
        return orchestrator.Object;
    }

#pragma warning disable CS1998 // method lacks await — yields synchronously
    private static async IAsyncEnumerable<PlaybookStreamEvent> YieldEvents(PlaybookStreamEvent[] events)
    {
        foreach (var ev in events)
        {
            yield return ev;
        }
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> CancelingEnumerable(
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        yield break;
    }
#pragma warning restore CS1998

    /// <summary>
    /// Captures formatted log messages into a list for sentinel-string assertions.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _sink;

        public CapturingLogger(List<string> sink) => _sink = sink;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
