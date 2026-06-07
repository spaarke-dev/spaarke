using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Shared test-fixture base for the 8 typed tool handler workstream (R6 Pillar 2, FR-13..FR-20).
/// </summary>
/// <remarks>
/// <para>
/// Provides:
/// </para>
/// <list type="bullet">
/// <item>
/// Pre-built mocks for the two AI collaborators every typed handler unit test needs:
/// <see cref="OpenAiClientMock"/> and <see cref="ScopeResolverMock"/>. Concrete handlers
/// with additional dependencies (e.g., <c>IRagService</c>, <c>ITextChunkingService</c>)
/// add their own per-test Moq instances — this fixture supplies only the universally-used
/// collaborators.
/// </item>
/// <item>
/// Context builders <see cref="BuildToolExecutionContext"/> and
/// <see cref="BuildChatInvocationContext"/> for the two sister context types introduced by
/// task 009 (D-A-09). Both pre-populate ADR-015-safe defaults (deterministic tenant ID;
/// no raw user text on the chat context's <c>UserContext</c>).
/// </item>
/// <item>
/// Telemetry assertion helper <see cref="AssertTelemetryRespectsAdr015"/> that scans
/// captured log messages and asserts: only handler name + outcome + timestamp + deterministic
/// IDs appear. Document/email body content, full prompts, and full model responses MUST NOT
/// surface in log output (ADR-015 binding).
/// </item>
/// </list>
/// <para>
/// <strong>Why a fixture instead of a base class?</strong> Each handler test still declares
/// its own xUnit class — fixtures inherit from <see cref="TypedToolHandlerTestFixture"/> only
/// when the handler needs the shared scaffolding. Handlers that already follow a clean
/// per-test mock pattern (see <c>DocumentClassifierHandlerTests</c>) are free to keep their
/// existing layout; they remain compliant with the R6 conventions doc.
/// </para>
/// <para>
/// Wave 1 handler tests (101–104; pure-deterministic handlers like
/// <c>DateExtractorHandler</c>, <c>FinancialCalculatorHandler</c>, etc.) and Wave 2 handler
/// tests (105–108; LLM-assisted handlers) inherit from this fixture so each PR ships
/// consistent shared scaffolding and uniform ADR-015 telemetry assertions.
/// </para>
/// <para>
/// <strong>References</strong>:
/// </para>
/// <list type="bullet">
/// <item>ADR-010: DI minimalism — fixture lives in tests, not in production DI</item>
/// <item>ADR-013: AI architecture — handlers stay AI-internal; fixture mocks the AI collaborators</item>
/// <item>ADR-014: AI caching — tests can layer per-tenant cache assertions on top of this fixture</item>
/// <item>ADR-015: AI data governance — see <see cref="AssertTelemetryRespectsAdr015"/></item>
/// <item>ADR-016: Rate limit — tests can layer rate-limit assertions on top of this fixture</item>
/// </list>
/// </remarks>
public abstract class TypedToolHandlerTestFixture
{
    /// <summary>
    /// Default tenant ID used by the fixture's context builders. Deterministic so log
    /// assertions can match exactly. Not a real customer tenant.
    /// </summary>
    protected const string DefaultTenantId = "tenant-test-fixture";

    /// <summary>
    /// Default model deployment name the OpenAI mock returns when handlers ask. Wave 2
    /// LLM-assisted handlers can override per-test.
    /// </summary>
    protected const string DefaultModelDeployment = "gpt-4o-mini";

    /// <summary>
    /// Shared mock for <see cref="IOpenAiClient"/>. Wave 2 LLM-assisted handlers set up
    /// per-test responses via <c>OpenAiClientMock.Setup(...)</c>. Wave 1 deterministic
    /// handlers typically don't use this — leave it in default state (no setups) and the
    /// handler's no-LLM code path runs.
    /// </summary>
    protected Mock<IOpenAiClient> OpenAiClientMock { get; } = new();

    /// <summary>
    /// Shared mock for <see cref="IScopeResolverService"/>. Configure per-test if the
    /// handler resolves scopes. Default returns empty results so tests can opt into
    /// scope-aware behavior explicitly.
    /// </summary>
    protected Mock<IScopeResolverService> ScopeResolverMock { get; } = new();

    /// <summary>
    /// Captures log messages emitted during handler execution so
    /// <see cref="AssertTelemetryRespectsAdr015"/> can scan them for forbidden content.
    /// </summary>
    /// <remarks>
    /// Concrete handler tests obtain a typed logger via <see cref="CreateLogger{T}"/> —
    /// the returned logger writes to <see cref="CapturedLogMessages"/> on every call.
    /// </remarks>
    protected List<CapturedLogMessage> CapturedLogMessages { get; } = new();

    /// <summary>
    /// Build a <see cref="ToolExecutionContext"/> with safe defaults for handler unit tests.
    /// </summary>
    /// <param name="extractedText">
    /// Optional document text. Defaults to a short deterministic stub. Tests asserting
    /// truncation behavior override with longer strings.
    /// </param>
    /// <param name="tenantId">
    /// Optional tenant override. Defaults to <see cref="DefaultTenantId"/>.
    /// </param>
    /// <param name="actionSystemPrompt">
    /// Optional Action-system-prompt the handler should treat as the primary instruction
    /// (Option A: Action = what to do, Tool = how to do it). Null for the default flow.
    /// </param>
    /// <param name="documentId">
    /// Optional document id. Defaults to a fresh <see cref="Guid.NewGuid"/>.
    /// </param>
    protected static ToolExecutionContext BuildToolExecutionContext(
        string? extractedText = null,
        string? tenantId = null,
        string? actionSystemPrompt = null,
        Guid? documentId = null)
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = tenantId ?? DefaultTenantId,
            ActionSystemPrompt = actionSystemPrompt,
            Document = new DocumentContext
            {
                DocumentId = documentId ?? Guid.NewGuid(),
                Name = "fixture-document.pdf",
                ExtractedText = extractedText ?? "Fixture document content for handler unit tests.",
                FileName = "fixture-document.pdf",
                ContentType = "application/pdf"
            }
        };
    }

    /// <summary>
    /// Build a <see cref="ChatInvocationContext"/> with safe defaults for handler unit
    /// tests. Per ADR-015, the context exposes only IDs and handles — never user text.
    /// </summary>
    /// <param name="toolArgumentsJson">
    /// Optional structured tool-call arguments JSON (validated against
    /// <c>AnalysisTool.JsonSchema</c> at the adapter boundary in task D-A-10). Defaults to
    /// an empty object.
    /// </param>
    /// <param name="requestedToolName">
    /// Optional tool name the LLM invoked. Defaults to "fixture-tool".
    /// </param>
    /// <param name="tenantId">
    /// Optional tenant override. Defaults to <see cref="DefaultTenantId"/>.
    /// </param>
    /// <param name="matterId">
    /// Optional matter scope id. Null when chat invocation is not matter-scoped.
    /// </param>
    /// <param name="conversationHistoryRef">
    /// Optional opaque handle (NOT content) referencing conversation history. Defaults to
    /// null so tests opt-in explicitly.
    /// </param>
    protected static ChatInvocationContext BuildChatInvocationContext(
        string? toolArgumentsJson = null,
        string? requestedToolName = null,
        string? tenantId = null,
        Guid? matterId = null,
        string? conversationHistoryRef = null)
    {
        return new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = tenantId ?? DefaultTenantId,
            DecisionId = Guid.NewGuid(),
            RequestedToolName = requestedToolName ?? "fixture-tool",
            ToolArgumentsJson = toolArgumentsJson ?? "{}",
            MatterId = matterId,
            ConversationHistoryRef = conversationHistoryRef
        };
    }

    /// <summary>
    /// Build an <see cref="AnalysisTool"/> stub for handler unit tests.
    /// </summary>
    /// <param name="handlerClass">
    /// The <c>sprk_handlerclass</c> column value — must match the C# class name of the
    /// handler under test (R6 Pillar 2 binding).
    /// </param>
    /// <param name="configuration">Optional JSON configuration override.</param>
    /// <param name="name">Optional human-readable name. Defaults to <paramref name="handlerClass"/>.</param>
    /// <param name="toolType">Tool type. Defaults to <see cref="ToolType.Custom"/>.</param>
    protected static AnalysisTool BuildAnalysisTool(
        string handlerClass,
        string? configuration = null,
        string? name = null,
        ToolType toolType = ToolType.Custom)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = name ?? handlerClass,
            Type = toolType,
            Configuration = configuration
        };
    }

    /// <summary>
    /// Construct a logger that captures all log messages into
    /// <see cref="CapturedLogMessages"/> for ADR-015 telemetry assertions.
    /// </summary>
    /// <typeparam name="T">The logging category type (typically the handler under test).</typeparam>
    protected ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(CapturedLogMessages);

    /// <summary>
    /// Assert that captured log messages respect ADR-015 governance:
    /// handler-name + outcome + timestamp only; never raw user input, document content,
    /// extracted text, full prompts, or full model responses.
    /// </summary>
    /// <param name="forbiddenSubstrings">
    /// Strings that MUST NOT appear in any captured log message — typically the raw
    /// document text, conversation transcript fragments, or model response text passed
    /// to the handler in the current test. Defaults to an empty list (only the universal
    /// content patterns below are checked).
    /// </param>
    /// <remarks>
    /// <para>
    /// Universal patterns checked even with no <paramref name="forbiddenSubstrings"/>:
    /// </para>
    /// <list type="bullet">
    /// <item>Bearer tokens — <c>Bearer eyJ...</c></item>
    /// <item>API keys — heuristic match for long contiguous base64-like strings</item>
    /// <item>SSE-style content blocks — raw <c>data:</c> JSON payloads</item>
    /// </list>
    /// <para>
    /// Wave 2 LLM-assisted handler tests SHOULD pass the document text + model response text
    /// as forbidden substrings to assert no accidental leak.
    /// </para>
    /// </remarks>
    protected void AssertTelemetryRespectsAdr015(params string[] forbiddenSubstrings)
    {
        var allMessages = CapturedLogMessages
            .Select(m => m.FormattedMessage)
            .ToList();

        // Universal forbidden patterns
        foreach (var message in allMessages)
        {
            message.Should().NotMatchRegex(
                @"Bearer\s+eyJ[A-Za-z0-9_\-\.]+",
                because: "ADR-015: bearer tokens must never appear in logs");

            // Heuristic: a >40-char run of base64 chars likely indicates a leaked key/secret
            message.Should().NotMatchRegex(
                @"[A-Za-z0-9+/]{40,}={0,2}\b",
                because: "ADR-015: long base64-like strings in logs are likely leaked keys/secrets/payloads");

            // SSE-formatted data: blocks in logs indicate the raw model response was logged
            message.Should().NotMatch(
                "*\ndata: {*",
                because: "ADR-015: raw SSE event payloads must never appear in logs (the response stream's content is governed Tier 3, not Tier 1 app-logs)");
        }

        // Test-specified forbidden substrings (document content, conversation text, etc.)
        foreach (var forbidden in forbiddenSubstrings ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(forbidden) || forbidden.Length < 10)
                continue; // Skip too-short patterns that could match in normal log text

            foreach (var message in allMessages)
            {
                message.Should().NotContain(
                    forbidden,
                    because: $"ADR-015: '{TruncateForError(forbidden)}' is governed content and must not appear in app logs");
            }
        }
    }

    /// <summary>
    /// Convert a captured-message log entry's static format + args into the final
    /// formatted string. Used by <see cref="AssertTelemetryRespectsAdr015"/> to scan
    /// realized log output.
    /// </summary>
    protected static string FormatLogMessage(string format, IReadOnlyList<KeyValuePair<string, object?>> args)
    {
        if (args.Count == 0)
            return format;

        var formatted = format;
        foreach (var kvp in args)
        {
            formatted = Regex.Replace(
                formatted,
                @"\{" + Regex.Escape(kvp.Key) + @"(\:[^\}]+)?\}",
                kvp.Value?.ToString() ?? string.Empty);
        }
        return formatted;
    }

    private static string TruncateForError(string s) =>
        s.Length <= 32 ? s : s.Substring(0, 29) + "...";

    /// <summary>
    /// A single captured log entry. The fixture inspects these via
    /// <see cref="AssertTelemetryRespectsAdr015"/> for ADR-015 compliance.
    /// </summary>
    /// <param name="LogLevel">Level the handler logged at.</param>
    /// <param name="EventId">Optional EventId.</param>
    /// <param name="FormattedMessage">Final formatted log string after placeholder substitution.</param>
    public sealed record CapturedLogMessage(
        LogLevel LogLevel,
        EventId EventId,
        string FormattedMessage);

    /// <summary>
    /// <see cref="ILogger{T}"/> implementation that captures structured log entries into
    /// a shared list so <see cref="AssertTelemetryRespectsAdr015"/> can inspect them.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<CapturedLogMessage> _sink;

        public CapturingLogger(List<CapturedLogMessage> sink)
        {
            _sink = sink;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var formatted = formatter(state, exception);
            _sink.Add(new CapturedLogMessage(logLevel, eventId, formatted));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
