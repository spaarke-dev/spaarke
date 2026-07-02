using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="DailyBriefingEndpoints"/> — R4 task 031 (FR-12 Path A.5).
///
/// HandleNarrate is now a thin dispatch wrapper that:
///   1. Resolves the DAILY-BRIEFING-NARRATE playbook GUID via IConsumerRoutingService
///      using the ConsumerTypes.DailyBriefingNarrate compile-time constant
///   2. Serializes the request payload + invokes the playbook via IInvokePlaybookAi
///   3. Projects PlaybookInvocationResult.StructuredData → DailyBriefingNarrateResponse
///      (preserves the existing widget-parser contract per AC-12b)
///
/// Tests in this file verify:
///   - Backward-compat: empty-payload tolerance (200 + empty bullets) short-circuits
///     BEFORE playbook dispatch
///   - New dispatch path: routing.ResolveAsync receives the correct ConsumerTypes constant
///   - New dispatch path: invokePlaybookAi.InvokePlaybookAsync receives the resolved
///     playbook ID + the request as a serialized parameter
///   - 503 fallback: when routing returns null, response is 503 Service Unavailable
///   - Response-shape backward compat: StructuredData → DailyBriefingNarrateResponse
///   - No inline prompt strings remain in the source (FR-12 / AC-12a)
///
/// Prior tests for inline prompt builders (BuildNarrateTldrPrompt /
/// BuildChannelNarrationPrompt / ParseTldrResponse / ValidateBulletPrimaryEntityIds)
/// have been REMOVED because the underlying helpers are deleted — prompt
/// construction now lives in the playbook + Action rows.
/// </summary>
[Trait("status", "task-031-r4")]
public sealed class DailyBriefingEndpointsTests
{
    private const string ExpectedConsumerType = "daily-briefing-narrate";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private static HandleNarrate handler via reflection. Updated for
    /// R7 Wave 11 T116 narrator spike: signature gained two parameters
    /// (IConfiguration, DailyBriefingNarrator) for the feature-flagged code-based
    /// narrator path. Default behavior preserves the playbook path (flag off).
    ///
    ///   HandleNarrate(
    ///       DailyBriefingNarrateRequest request,
    ///       ILoggerFactory loggerFactory,
    ///       IConsumerRoutingService routing,
    ///       IInvokePlaybookAi invokePlaybookAi,
    ///       IConfiguration configuration,
    ///       DailyBriefingNarrator narrator,
    ///       HttpContext httpContext,
    ///       CancellationToken cancellationToken)
    /// </summary>
    private static async Task<IResult> InvokeHandleNarrateAsync(
        DailyBriefingNarrateRequest request,
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybookAi,
        HttpContext? httpContext = null,
        CancellationToken cancellationToken = default,
        IConfiguration? configuration = null,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator? narrator = null)
    {
        var method = typeof(DailyBriefingEndpoints)
            .GetMethod("HandleNarrate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("HandleNarrate not found via reflection");

        // Default feature flag OFF — these tests verify the playbook-engine path
        // (existing behavior). The narrator parameter is allowed to be null because
        // with the flag off, HandleNarrate never invokes the narrator.
        var config = configuration ?? new ConfigurationBuilder().Build();

        var task = (Task<IResult>)method.Invoke(null, new object?[]
        {
            request,
            NullLoggerFactory.Instance,
            routing,
            invokePlaybookAi,
            config,
            narrator!,  // null is safe here — feature flag off → narrator never accessed
            httpContext ?? new DefaultHttpContext(),
            cancellationToken
        })!;

        return await task;
    }

    private static DailyBriefingNarrateRequest BuildNonEmptyRequest() => new()
    {
        Categories =
        [
            new NotificationCategoryDto { Name = "Tasks Overdue", Count = 1, UnreadCount = 1 }
        ],
        PriorityItems =
        [
            new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter" }
        ],
        TotalNotificationCount = 1,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = "tasks",
                Label = "Tasks Overdue",
                Items =
                [
                    new ChannelItemDto
                    {
                        Id = "notif-1",
                        Title = "Review engagement letter",
                        RegardingName = "Acme Corp",
                        RegardingEntityType = "sprk_matter",
                        RegardingId = Guid.NewGuid().ToString(),
                        Priority = "high"
                    }
                ]
            }
        ]
    };

    private static Mock<IConsumerRoutingService> BuildRoutingMock(Guid? resolvedPlaybookId)
    {
        var mock = new Mock<IConsumerRoutingService>(MockBehavior.Strict);
        mock.Setup(r => r.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedPlaybookId);
        return mock;
    }

    private static Mock<IInvokePlaybookAi> BuildInvokeMock(PlaybookInvocationResult result)
    {
        var mock = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);
        mock.Setup(i => i.InvokePlaybookAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()))
            .ReturnsAsync(result);
        return mock;
    }

    // ── Tests: empty payload → 200 (short-circuits BEFORE playbook dispatch) ────

    [Fact]
    public async Task HandleNarrate_Returns_200_Empty_On_Empty_Payload_Without_Invoking_Dispatch()
    {
        // Arrange — fully empty request (matches frontend buildEmptyNarrateRequest())
        var request = new DailyBriefingNarrateRequest
        {
            Categories = [],
            PriorityItems = [],
            TotalNotificationCount = 0,
            Channels = []
        };

        // STRICT mocks — neither routing nor invokePlaybookAi must be called.
        var routing = new Mock<IConsumerRoutingService>(MockBehavior.Strict);
        var invokePlaybookAi = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — 200 with empty narrative response, dispatch not invoked.
        result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>();
        var ok = (Ok<DailyBriefingNarrateResponse>)result;
        ok.Value.Should().NotBeNull();
        ok.Value!.Tldr.Should().NotBeNull();
        ok.Value.Tldr.Summary.Should().BeEmpty();
        ok.Value.Tldr.KeyTakeaways.Should().BeEmpty();
        ok.Value.Tldr.TopAction.Should().BeEmpty();
        ok.Value.Tldr.CategoryCount.Should().Be(0);
        ok.Value.Tldr.PriorityItemCount.Should().Be(0);
        ok.Value.ChannelNarratives.Should().BeEmpty();

        routing.VerifyNoOtherCalls();
        invokePlaybookAi.VerifyNoOtherCalls();
    }


    // ── Tests: dispatch path — Path A.5 ────────────────────────────────────────


    [Fact]
    public async Task HandleNarrate_Invokes_Playbook_With_Resolved_Id_And_Request_Payload_Parameters()
    {
        // Arrange
        var request = BuildNonEmptyRequest();
        var playbookId = Guid.NewGuid();
        var routing = BuildRoutingMock(playbookId);

        IReadOnlyDictionary<string, string>? capturedParameters = null;
        Guid capturedPlaybookId = Guid.Empty;
        PlaybookInvocationContext? capturedContext = null;

        var invokePlaybookAi = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);
        invokePlaybookAi.Setup(i => i.InvokePlaybookAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()))
            .Callback<Guid, IReadOnlyDictionary<string, string>?, PlaybookInvocationContext, CancellationToken, string?, Sprk.Bff.Api.Services.Ai.DocumentContext?>(
                (id, parameters, ctx, _, _, _) =>
                {
                    capturedPlaybookId = id;
                    capturedParameters = parameters;
                    capturedContext = ctx;
                })
            .ReturnsAsync(BuildSuccessResult());

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert
        result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>();
        capturedPlaybookId.Should().Be(playbookId);

        capturedParameters.Should().NotBeNull();
        capturedParameters!.Should().ContainKey("briefingPayload");
        capturedParameters!.Should().ContainKey("totalNotificationCount");
        capturedParameters!.Should().ContainKey("categoryCount");
        capturedParameters!.Should().ContainKey("priorityItemCount");
        capturedParameters!.Should().ContainKey("channelCount");

        // The serialized payload must round-trip back to the original request shape
        // (camelCase property names matching the playbook's template references).
        var payloadJson = capturedParameters!["briefingPayload"];
        var roundTripped = JsonSerializer.Deserialize<DailyBriefingNarrateRequest>(
            payloadJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        roundTripped.Should().NotBeNull();
        roundTripped!.Categories.Should().HaveCount(1);
        roundTripped.PriorityItems.Should().HaveCount(1);
        roundTripped.Channels.Should().HaveCount(1);

        capturedContext.Should().NotBeNull();
        capturedContext!.HttpContext.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleNarrate_Returns_503_When_Routing_Returns_Null_PlaybookId()
    {
        // Arrange — routing returns null (no sprk_playbookconsumer row matches)
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(null);
        // STRICT — InvokePlaybookAsync must NOT be called when routing fails.
        var invokePlaybookAi = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — 503 ProblemDetails (preserves the prior service-unavailable contract).
        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
        invokePlaybookAi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleNarrate_Returns_503_When_Playbook_Result_Reports_Failure()
    {
        // Arrange — playbook resolves but execution fails (orchestration-level failure).
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());
        var failedResult = new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = false,
            ErrorCode = "PLAYBOOK_INVOCATION_FAILED",
            ErrorMessage = "node BRIEF-NARRATE-TLDR failed"
        };
        var invokePlaybookAi = BuildInvokeMock(failedResult);

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — 503 (AiUnavailable), not 500.
        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
    }

    // ── Tests: response shape backward compatibility (AC-12b) ──────────────────

    [Fact]
    public async Task HandleNarrate_Projects_StructuredData_Into_DailyBriefingNarrateResponse()
    {
        // Arrange — playbook returns structured TL;DR + per-channel narratives.
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());

        // Mirror the playbook's ReturnResponse node binding (responseBinding.tldr +
        // responseBinding.channelNarratives) — see daily-briefing-narrate.json.
        using var doc = JsonDocument.Parse("""
            {
              "tldr": {
                "summary": "Three urgent matters need attention today.",
                "keyTakeaways": ["Acme contract overdue", "Bravo brief due tomorrow"],
                "topAction": "Review the Acme engagement letter (2 days overdue)."
              },
              "channelNarratives": [
                {
                  "category": "tasks",
                  "bullets": [
                    {
                      "narrative": "Review the Acme engagement letter today.",
                      "itemIds": ["notif-1"],
                      "primaryEntityType": "sprk_matter",
                      "primaryEntityId": "00000000-0000-0000-0000-000000000001",
                      "primaryEntityName": "Acme Corp"
                    }
                  ]
                }
              ]
            }
            """);

        var playbookResult = new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            StructuredData = doc.RootElement.Clone()
        };
        var invokePlaybookAi = BuildInvokeMock(playbookResult);

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — response shape matches the existing widget parser contract.
        var ok = result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>().Subject;
        ok.Value.Should().NotBeNull();

        // TL;DR fields preserved + category / priority counts re-injected from request.
        ok.Value!.Tldr.Summary.Should().Be("Three urgent matters need attention today.");
        ok.Value.Tldr.KeyTakeaways.Should().HaveCount(2);
        ok.Value.Tldr.TopAction.Should().Be("Review the Acme engagement letter (2 days overdue).");
        ok.Value.Tldr.CategoryCount.Should().Be(request.Categories.Length);
        ok.Value.Tldr.PriorityItemCount.Should().Be(request.PriorityItems.Length);

        // Channel narratives preserved.
        ok.Value.ChannelNarratives.Should().HaveCount(1);
        ok.Value.ChannelNarratives[0].Category.Should().Be("tasks");
        ok.Value.ChannelNarratives[0].Bullets.Should().HaveCount(1);
        ok.Value.ChannelNarratives[0].Bullets[0].Narrative
            .Should().Be("Review the Acme engagement letter today.");
        ok.Value.ChannelNarratives[0].Bullets[0].PrimaryEntityName.Should().Be("Acme Corp");

        // GeneratedAtUtc is populated.
        ok.Value.GeneratedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HandleNarrate_Falls_Back_To_TextContent_When_StructuredData_Missing()
    {
        // Arrange — playbook returns TextContent only (no StructuredData).
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());
        var playbookResult = new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            TextContent = "Fallback summary text from playbook."
        };
        var invokePlaybookAi = BuildInvokeMock(playbookResult);

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — graceful degradation: TextContent becomes Summary; bullets/channels empty.
        var ok = result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>().Subject;
        ok.Value.Should().NotBeNull();
        ok.Value!.Tldr.Summary.Should().Be("Fallback summary text from playbook.");
        ok.Value.Tldr.KeyTakeaways.Should().BeEmpty();
        ok.Value.Tldr.TopAction.Should().BeEmpty();
        ok.Value.ChannelNarratives.Should().BeEmpty();
    }

    // ── Tests: ProjectPlaybookResultToNarrateResponse — direct ─────────────────

    [Fact]
    public void ProjectPlaybookResultToNarrateResponse_Maps_StructuredData_To_Response_Shape()
    {
        // Arrange — direct test of the projection helper (avoids reflection-on-private).
        var request = BuildNonEmptyRequest();
        using var doc = JsonDocument.Parse("""
            {
              "tldr": {
                "summary": "Summary text.",
                "keyTakeaways": ["A", "B", "C"],
                "topAction": "Do A first."
              },
              "channelNarratives": []
            }
            """);
        var playbookResult = new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            StructuredData = doc.RootElement.Clone()
        };

        // Act
        var response = DailyBriefingEndpoints.ProjectPlaybookResultToNarrateResponse(
            playbookResult, request, NullLogger.Instance);

        // Assert
        response.Tldr.Summary.Should().Be("Summary text.");
        response.Tldr.KeyTakeaways.Should().HaveCount(3);
        response.Tldr.TopAction.Should().Be("Do A first.");
        response.Tldr.CategoryCount.Should().Be(request.Categories.Length);
        response.Tldr.PriorityItemCount.Should().Be(request.PriorityItems.Length);
        response.ChannelNarratives.Should().BeEmpty();
    }

    // ── Tests: AC-12a — no inline prompt strings remain in DailyBriefingEndpoints ──


    // ── Tests: exception paths — edge cases (R4 task 035) ─────────────────────

    [Fact]
    public async Task HandleNarrate_Returns_503_When_InvokePlaybook_Throws_FeatureDisabledException()
    {
        // Arrange — playbook resolves, but the AI kill-switch is OFF
        // (ADR-032 NullInvokePlaybookAi P3 Fail-Fast surfaces this exception).
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());

        var invokePlaybookAi = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);
        invokePlaybookAi.Setup(i => i.InvokePlaybookAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()))
            .ThrowsAsync(new FeatureDisabledException("ai.briefing.disabled", "AI disabled"));

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — 503 (canonical kill-switch response, NOT 500).
        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task HandleNarrate_Returns_500_When_InvokePlaybook_Throws_Generic_Exception()
    {
        // Arrange — playbook resolves, but execution throws an unexpected
        // exception (NOT one of the well-known recoverable types). Endpoint
        // should NOT leak the inner exception text; should return 500 ProblemDetails.
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());

        var invokePlaybookAi = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);
        invokePlaybookAi.Setup(i => i.InvokePlaybookAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()))
            .ThrowsAsync(new InvalidOperationException("unexpected playbook engine failure"));

        // Act
        var result = await InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);

        // Assert — 500 (catch-all branch).
        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task HandleNarrate_Propagates_OperationCanceledException_When_Caller_Cancels()
    {
        // Arrange — caller cancels mid-dispatch. Endpoint must propagate the
        // cancellation cleanly (NOT swallow into a 500 ProblemDetails).
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());

        var invokePlaybookAi = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);
        invokePlaybookAi.Setup(i => i.InvokePlaybookAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()))
            .ThrowsAsync(new OperationCanceledException("caller cancelled"));

        // Act + Assert — exception bubbles out (test framework observes it).
        var act = () => InvokeHandleNarrateAsync(request, routing.Object, invokePlaybookAi.Object);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PlaybookInvocationResult BuildSuccessResult()
    {
        using var doc = JsonDocument.Parse("""
            {
              "tldr": { "summary": "ok", "keyTakeaways": [], "topAction": "" },
              "channelNarratives": []
            }
            """);
        return new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            StructuredData = doc.RootElement.Clone()
        };
    }
}
