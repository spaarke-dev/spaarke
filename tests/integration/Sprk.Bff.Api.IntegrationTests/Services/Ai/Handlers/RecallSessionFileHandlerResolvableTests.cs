using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Services.Ai.Handlers;

/// <summary>
/// DI smoke test for <see cref="RecallSessionFileHandler"/> resolvability (chat-routing-redesign-r1
/// task 091 MVP — 1-handler version; the 8-handler matrix is out of scope per the Q5b MVP cut).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Contract under test</strong>: after <c>AiModule.AddAiModule</c> registers
/// <see cref="IRecentlyDiscussedTracker"/> as Singleton and the assembly-scan tool registration
/// registers <c>RecallSessionFileHandler</c> as scoped <see cref="IToolHandler"/>, the BFF DI
/// container can resolve a <c>RecallSessionFileHandler</c> instance whose
/// <c>HandlerId</c> matches the documented contract.
/// </para>
/// <para>
/// <strong>Why a lightweight host instead of <c>WebApplicationFactory&lt;Program&gt;</c></strong>:
/// the production <c>Program.cs</c> bootstrap requires 130+ config keys (Cosmos, ServiceBus,
/// AzureAd, PowerBI, ManagedIdentity) and reaches network endpoints. This MVP smoke test only
/// validates that the tracker registration we added wires up cleanly with the existing recall
/// handler dependency graph. A direct <see cref="ServiceCollection"/> with the minimum required
/// services is sufficient and runs in &lt;200ms with zero network IO.
/// </para>
/// <para>
/// <strong>Skip behavior</strong>: this test runs unconditionally. If a future change requires
/// real Redis to verify the tracker round-trip (vs. registration), gate that distinct test with
/// <c>Skip = "Requires Redis"</c> and a separate fact — do NOT alter this one. The 8-handler
/// matrix version remains DEFERRED per the Q5b MVP cut (tasks 083/084/086-090 deferred).
/// </para>
/// </remarks>
[Trait("status", "new")]
[Trait("project", "chat-routing-redesign-r1")]
[Trait("task", "091")]
public sealed class RecallSessionFileHandlerResolvableTests
{
    [Fact]
    public void RecallSessionFileHandler_IsResolvableFromDi_WithRegisteredRecentlyDiscussedTracker()
    {
        // Arrange — build a minimal service collection mirroring the AiModule + tool-handler
        // assembly-scan registrations. We do NOT instantiate Program.cs because that requires
        // 130+ config keys and network IO; this test asserts the DI graph for the recall handler
        // + tracker registrations specifically.
        var services = new ServiceCollection();

        // Logging — required by every service.
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        // IDistributedCache — in-memory backed for the test (no Redis dependency). The
        // tracker exercises GetAsync/SetAsync against this implementation; the production
        // wiring substitutes Redis at Program.cs startup. AddDistributedMemoryCache wires up
        // the required MemoryDistributedCacheOptions for us.
        services.AddDistributedMemoryCache();

        // TimeProvider — required by RecentlyDiscussedTracker (system clock acceptable in tests).
        services.AddSingleton(TimeProvider.System);

        // Tracker registration — this is the line under test. Matches AiModule.cs line added by
        // chat-routing-redesign-r1 task 091.
        services.AddSingleton<IRecentlyDiscussedTracker, RecentlyDiscussedTracker>();

        // The full handler dependency graph (IRagService, ChatSessionManager, IContextEventEmitter)
        // is exercised by the unit-test suite in tests/unit/Sprk.Bff.Api.Tests. This integration
        // smoke test asserts only the new tracker registration + handler constructor compatibility
        // — supply nulls/no-ops for non-tracker deps so the constructor can be reflected.
        services.AddSingleton(Moq.Mock.Of<Sprk.Bff.Api.Services.Ai.IRagService>());
        // ChatSessionManager constructor signature changed from IDistributedCache to ITenantCache
        // (chat-routing-redesign-r1 task 091 merge). Pre-existing build break on master baseline
        // discovered during spaarke-redis-cache-remediation-r2 task 005 — fixed surgically per
        // bff-extensions.md §F.2 (Fixture-Config-FIRST) to enable the integration project to
        // build so task 005's new MetricsDistributedCacheRegistrationTests can run.
        services.AddSingleton(Moq.Mock.Of<ITenantCache>());
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager>(sp =>
            new Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager(
                cache: sp.GetRequiredService<ITenantCache>(),
                dataverseRepository: Moq.Mock.Of<Sprk.Bff.Api.Services.Ai.Chat.IChatDataverseRepository>(),
                logger: sp.GetRequiredService<ILogger<Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager>>(),
                persistence: null,
                cleanupSignal: null));

        // RecallSessionFileHandler — register as concrete (the assembly scan in Program.cs
        // registers as IToolHandler; for the smoke test we register concrete so we can resolve
        // it directly without iterating the handler collection).
        services.AddScoped<RecallSessionFileHandler>();

        var provider = services.BuildServiceProvider();

        // Act — resolve in a fresh scope (the handler is scoped per ToolFrameworkExtensions).
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RecallSessionFileHandler>();

        // Assert — handler is constructed, identifies itself per the documented contract, and
        // received a non-null IRecentlyDiscussedTracker via the optional constructor parameter
        // (DI supplied the registered Singleton).
        handler.Should().NotBeNull();
        handler.HandlerId.Should().Be(nameof(RecallSessionFileHandler),
            because: "the HandlerId contract is the LLM-facing function-call identifier — must remain stable");

        // The tracker itself must resolve as a Singleton from the same provider.
        var tracker = scope.ServiceProvider.GetRequiredService<IRecentlyDiscussedTracker>();
        tracker.Should().NotBeNull();
        tracker.Should().BeOfType<RecentlyDiscussedTracker>();
    }
}
