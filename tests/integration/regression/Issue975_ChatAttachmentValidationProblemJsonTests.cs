// Regression anchor for GitHub issue #975 (spaarkeai-word-add-in-r1 task 052 — one of the three
// sites task 050's blast-radius survey found but was scoped out of; see
// projects/spaarkeai-word-add-in-r1/notes/050-problem-json-content-type.md §5). Same defect class as
// Issue975_ProblemJsonContentTypeTests.cs (task 050, the global exception handler):
// ChatEndpoints.SendMessageAsync's attachment-validation 4xx path sets
// `response.ContentType = "application/problem+json"` and then calls
// `response.WriteAsJsonAsync(err.payload, cancellationToken)` with NO explicit content-type argument.
// HttpResponseJsonExtensions.WriteAsJsonAsync unconditionally overwrites ContentType with
// "application/json; charset=utf-8" when no content type is passed — it never consults the response's
// existing header. Every FR-07 attachment-validation rejection was served as "application/json",
// contrary to ADR-019 (RFC 7807 mandates application/problem+json).
//
// SCOPE (task 052's SSE judgment call): this site precedes ALL SSE framing. The code comment
// immediately above the check (ChatEndpoints.cs) states "Validate BEFORE setting SSE headers so we
// can return a normal JSON 400 ProblemDetails response" — response.ContentType = "text/event-stream"
// is set several lines AFTER this early return. A plain error response, not a frame inside a started
// stream — squarely in scope for the same fix task 050 established.
//
// Fixed here by passing the content type explicitly to WriteAsJsonAsync — the framework's own
// mechanism, task 050's established pattern — rather than hand-rolled header juggling. The response
// BODY is unchanged (same err.payload object, same JsonSerializerOptions resolution: WriteAsJsonAsync's
// convenience overload and the 4-arg overload used by the fix both resolve `options: null` to the same
// DI-registered JsonOptions fallback).
//
// TECHNIQUE — why reflection into SendMessageAsync (not a WebApplicationFactory round trip): the only
// existing HTTP-level fixture for this route (ChatEndpointsTestFixture) lives in the
// Spe.Integration.Tests project, which Sprk.Bff.Api.Tests does not reference (no ProjectReference) —
// and tests/CLAUDE.md binds regression tests to tests/integration/regression/Issue{N}_*Tests.cs,
// compiled INTO Sprk.Bff.Api.Tests (see that project's .csproj RegressionTests Compile glob). Reusing a
// fixture from a different assembly is not possible, and standing up a SECOND
// WebApplicationFactory<Program> host to duplicate ChatEndpointsTestFixture's ~150 lines of DI stubs
// for one assertion would itself be the "5 components that overlap" anti-pattern CLAUDE.md §11 warns
// against. The attachment check fires before ANY of SendMessageAsync's other collaborators
// (historyManager, agentFactory, chatClient, pendingPlanManager, matterContextDetector,
// conversationHistorySanitizer, crossMatterTelemetry, aiTelemetry, sessionPersistence,
// suggestionService, logger) are touched — verified by reading the method's source end to end — so
// this test extends the SAME reflection technique ChatEndpointsAttachmentsTests.cs already established
// for ChatEndpoints' private static helpers, one level up to the handler itself, passing null for
// every collaborator this code path never reaches. TestHttpContexts.Authenticated (tests/integration/
// Shared) and a real ChatSessionManager over mocked ITenantCache + IChatDataverseRepository (the exact
// construction ChatSessionManagerTests.cs already uses) are reused as-is — no new fixture.
//
// MAINTAIN-class (regression-protector; ADR-038 §1 KEEP path, tests/integration/regression/**,
// Issue{N}_*Tests.cs naming). NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test,
// NO Stopwatch/Task.Delay.

using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public class Issue975_ChatAttachmentValidationProblemJsonTests
{
    private const string TenantId = "issue975-chat-tenant";
    private const string SessionId = "issue975-chat-session";

    private static readonly MethodInfo SendMessageAsyncMethod =
        typeof(ChatEndpoints).GetMethod("SendMessageAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(ChatEndpoints), "SendMessageAsync");

    /// <summary>
    /// Builds a real <see cref="ChatSessionManager"/> (mirrors ChatSessionManagerTests.cs's own
    /// construction — no new fixture) that resolves <see cref="SessionId"/>/<see cref="TenantId"/> to a
    /// valid, owned session on a cache miss (the unconfigured <c>ITenantCache</c> Loose mock returns
    /// null for GetAsync&lt;ChatSession&gt;, per Moq's default-value provider for Task&lt;T&gt;).
    /// </summary>
    private static ChatSessionManager BuildSessionManager()
    {
        var cacheMock = new Mock<ITenantCache>();
        var repoMock = new Mock<IChatDataverseRepository>();
        var now = DateTimeOffset.UtcNow;
        repoMock
            .Setup(r => r.GetSessionAsync(TenantId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSession(
                SessionId: SessionId,
                TenantId: TenantId,
                DocumentId: null,
                PlaybookId: null,
                CreatedAt: now,
                LastActivity: now,
                Messages: Array.Empty<ChatMessage>())
            { OwnerOid = TestSessionOwner.Oid });

        return new ChatSessionManager(cacheMock.Object, repoMock.Object, NullLogger<ChatSessionManager>.Instance);
    }

    /// <summary>6 attachments — one over ChatEndpoints.MaxAttachmentsPerMessage (5), trips rule 1.</summary>
    private static IReadOnlyList<ChatMessageAttachment> TooManyAttachments()
    {
        return Enumerable.Range(1, ChatEndpoints.MaxAttachmentsPerMessage + 1)
            .Select(i => new ChatMessageAttachment($"file-{i}.txt", "text/plain", $"content-{i}"))
            .ToList();
    }

    private static async Task InvokeSendMessageAsync(ChatSendMessageRequest request, DefaultHttpContext httpContext)
    {
        // Positional order MUST match ChatEndpoints.SendMessageAsync's declared parameter list exactly
        // (reflection binds by position, not by [FromServices] attribute — those are minimal-API
        // model-binding metadata, irrelevant to a direct MethodInfo.Invoke).
        var args = new object?[]
        {
            SessionId,
            request,
            BuildSessionManager(),
            null, // ChatHistoryManager historyManager — never reached (attachment check fires first)
            null, // SprkChatAgentFactory agentFactory
            null, // PendingPlanManager pendingPlanManager
            null, // IChatClient chatClient
            null, // IMatterContextDetector matterContextDetector
            null, // IConversationHistorySanitizer conversationHistorySanitizer
            null, // CrossMatterSafetyTelemetry crossMatterTelemetry
            null, // AiTelemetry aiTelemetry
            null, // ISessionPersistenceService? sessionPersistence
            null, // AssistantSuggestionService suggestionService
            httpContext,
            NullLogger<SprkChatAgentFactory>.Instance
        };

        await (Task)SendMessageAsyncMethod.Invoke(null, args)!;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 1. THE DEFECT: an over-cap attachment rejection must carry application/problem+json. Before
    //    this task's fix, this failed — MediaType was "application/json" instead.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendMessage_WithTooManyAttachments_RejectionResponse_CarriesProblemJsonContentType()
    {
        // Arrange
        var httpContext = TestHttpContexts.Authenticated(oid: TestSessionOwner.Oid, tenantId: TenantId);
        httpContext.Response.Body = new MemoryStream();
        var request = new ChatSendMessageRequest("Summarize these.", Attachments: TooManyAttachments());

        // Act
        await InvokeSendMessageAsync(request, httpContext);

        // Assert — status unchanged (400); content-type is the ONLY thing this task's fix touches.
        httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        httpContext.Response.ContentType.Should().Be("application/problem+json",
            "RFC 7807 (ADR-019) mandates application/problem+json for a ProblemDetails response — " +
            "ChatEndpoints.SendMessageAsync's WriteAsJsonAsync call was overwriting this header with " +
            "application/json before task 052's fix");

        // Body shape UNCHANGED by this fix (byte-for-byte contract, POML constraint) — the same 4
        // fields ChatEndpointsAttachmentsTests already pins for ValidateAttachments' rule-1 rejection.
        httpContext.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Response.Body);
        body.GetProperty("title").GetString().Should().Be("Too many attachments");
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("detail").GetString().Should().Contain(ChatEndpoints.MaxAttachmentsPerMessage.ToString());
        body.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
    }
}
