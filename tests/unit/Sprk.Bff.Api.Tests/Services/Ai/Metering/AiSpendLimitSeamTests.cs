using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Metering;
using Xunit;
using OpenAiChat = OpenAI.Chat;

namespace Sprk.Bff.Api.Tests.Services.Ai.Metering;

/// <summary>
/// Task 254 — the two AI client seams of the stamp's optional spend limit. Task 077 wired its predecessor into
/// <see cref="OpenAiClient"/> and a later merge of master dropped that wiring silently: its tests covered the policy,
/// not the wiring. These tests cover the wiring — every public <see cref="OpenAiClient"/> method refuses an over-limit
/// call before any network I/O (the client points at an unreachable endpoint), and the <see cref="IChatClient"/> seam
/// refuses before the model is called and counts what the model reports.
/// </summary>
public sealed class AiSpendLimitSeamTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryAiSpendLedger _ledger = new();

    private AiSpendLimit Limit(decimal? monthlyLimitUsd) => new(
        new StaticOptionsMonitor(new AiSpendLimitOptions { MonthlyLimitUsd = monthlyLimitUsd }),
        _ledger, new FakeTimeProvider(Now), NullLogger<AiSpendLimit>.Instance);

    private AiSpendLimit OverLimit()
    {
        _ledger.Add(10m, Now);
        return Limit(5m);
    }

    // ── OpenAiClient: every public method ──────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, Func<OpenAiClient, Task>> EveryOpenAiClientMethod =
        new Dictionary<string, Func<OpenAiClient, Task>>
        {
            [nameof(OpenAiClient.StreamCompletionAsync)] = c => Drain(c.StreamCompletionAsync("p")),
            [nameof(OpenAiClient.GetCompletionAsync)] = c => c.GetCompletionAsync("p"),
            [nameof(OpenAiClient.StreamVisionCompletionAsync)] = c => Drain(c.StreamVisionCompletionAsync("p", [1], "image/png")),
            [nameof(OpenAiClient.GetVisionCompletionAsync)] = c => c.GetVisionCompletionAsync("p", [1], "image/png"),
            [nameof(OpenAiClient.GenerateEmbeddingAsync)] = c => c.GenerateEmbeddingAsync("t"),
            [nameof(OpenAiClient.GenerateEmbeddingsAsync)] = c => c.GenerateEmbeddingsAsync(["t"]),
            [nameof(OpenAiClient.GetChatCompletionWithToolsAsync)] = c => c.GetChatCompletionWithToolsAsync(
                [new OpenAiChat.UserChatMessage("p")], []),
            [nameof(OpenAiClient.GetStructuredCompletionAsync)] = c => c.GetStructuredCompletionAsync<object>(
                [new OpenAiChat.UserChatMessage("p")], BinaryData.FromString("{}"), "s", "gpt-4o"),
            [nameof(OpenAiClient.GetStructuredCompletionRawAsync)] = c => c.GetStructuredCompletionRawAsync(
                "p", BinaryData.FromString("{}"), "s"),
            [nameof(OpenAiClient.StreamStructuredCompletionAsync)] = c => Drain(c.StreamStructuredCompletionAsync(
                [new OpenAiChat.UserChatMessage("p")], BinaryData.FromString("{}"), "s")),
        };

    public static TheoryData<string> OpenAiClientMethods => new(EveryOpenAiClientMethod.Keys);

    [Fact]
    public void TheTheoryCoversEveryPublicModelCallOfOpenAiClient()
    {
        var publicAsync = typeof(OpenAiClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AsyncStateMachineAttribute>() is not null
                        || m.GetCustomAttribute<AsyncIteratorStateMachineAttribute>() is not null)
            .Select(m => m.Name)
            .Distinct();

        publicAsync.Should().BeEquivalentTo(EveryOpenAiClientMethod.Keys,
            "a new model call on OpenAiClient must check the spend limit — add it here and to the method");
    }

    [Theory]
    [MemberData(nameof(OpenAiClientMethods))]
    public async Task OpenAiClient_OverTheLimit_RefusesBeforeCallingTheModel(string method)
    {
        var client = new OpenAiClient(
            Options.Create(new DocumentIntelligenceOptions
            {
                Enabled = true,
                // Unroutable: a call that got past the check would fail with a transport error, not the refusal.
                OpenAiEndpoint = "https://127.0.0.1:9/",
                OpenAiKey = "test-key",
                SummarizeModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-large",
                MaxOutputTokens = 100,
            }),
            NullLogger<OpenAiClient>.Instance,
            spendLimit: OverLimit());

        await EveryOpenAiClientMethod[method](client).Invoking(t => t)
            .Should().ThrowAsync<AiSpendLimitExceededException>();
    }

    // ── IChatClient seam ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatClient_OverTheLimit_RefusesBeforeTheModelIsCalled()
    {
        var model = new RecordingChatClient();
        var client = new AiSpendLimitChatClient(model, OverLimit());

        await client.Invoking(c => c.GetResponseAsync("hi")).Should().ThrowAsync<AiSpendLimitExceededException>();
        await client.Invoking(c => Drain(c.GetStreamingResponseAsync("hi"))).Should().ThrowAsync<AiSpendLimitExceededException>();

        model.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ChatClient_CountsTheUsageTheModelReports()
    {
        var client = new AiSpendLimitChatClient(new RecordingChatClient(), Limit(monthlyLimitUsd: null));

        await client.GetResponseAsync("hi");                       // 1M input  → 2.50
        await Drain(client.GetStreamingResponseAsync("hi"));       // 1M output → 10.00 (UsageContent on the last update)

        (await _ledger.GetMonthToDateUsdAsync(Now, CancellationToken.None)).Should().Be(12.50m);
    }

    private static async Task Drain<T>(IAsyncEnumerable<T> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 0 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 0, OutputTokenCount = 1_000_000 })],
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StaticOptionsMonitor(AiSpendLimitOptions value) : IOptionsMonitor<AiSpendLimitOptions>
    {
        public AiSpendLimitOptions CurrentValue => value;

        public AiSpendLimitOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AiSpendLimitOptions, string?> listener) => null;
    }
}
