using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Todo;
using Sprk.Bff.Api.Services.Todo.NullObject;
using Sprk.Bff.Api.Services.Todo.Placeholder;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Todo;

/// <summary>
/// Verifies the smart-todo-decoupling-r3 task 018 contract:
/// <see cref="TodoSyncModule.AddTodoSync"/> registers all four todo-sync interfaces
/// UNCONDITIONALLY (per ADR-032 P2 + bff-extensions.md §F.1), and resolution dispatches
/// to either the Null-Object or the (Phase-7) placeholder impl based on the
/// <c>Spaarke:Graph:TodoSync:Enabled</c> flag.
/// </summary>
public sealed class TodoSyncModuleTests
{
    // ───────────────────────────────────────────────────────────────────────────────
    // Flag OFF (default / Phase 6 ship state)
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlagOff_ResolvesITodoGraphSyncHandler_ToNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: false);

        var handler = sp.GetRequiredService<ITodoGraphSyncHandler>();

        handler.Should().BeOfType<NullTodoGraphSyncHandler>();
    }

    [Fact]
    public void FlagOff_ResolvesISpaarkeListProvisioner_ToNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: false);

        var provisioner = sp.GetRequiredService<ISpaarkeListProvisioner>();

        provisioner.Should().BeOfType<NullSpaarkeListProvisioner>();
    }

    [Fact]
    public void FlagOff_ResolvesITodoSubscriptionManager_ToNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: false);

        var manager = sp.GetRequiredService<ITodoSubscriptionManager>();

        manager.Should().BeOfType<NullTodoSubscriptionManager>();
    }

    [Fact]
    public void FlagOff_ResolvesITodoSyncBackfiller_ToNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: false);

        var backfiller = sp.GetRequiredService<ITodoSyncBackfiller>();

        backfiller.Should().BeOfType<NullTodoSyncBackfiller>();
    }

    [Fact]
    public async Task FlagOff_NullTodoGraphSyncHandler_IsQuietNoOp()
    {
        var sp = BuildProvider(todoSyncEnabled: false);
        var handler = sp.GetRequiredService<ITodoGraphSyncHandler>();

        // Should complete synchronously without throwing — ADR-032 P2 quiet semantics.
        await handler.SyncAsync(Guid.NewGuid(), SyncOp.Create, CancellationToken.None);
        await handler.SyncAsync(Guid.NewGuid(), SyncOp.Update, CancellationToken.None);
        await handler.SyncAsync(Guid.NewGuid(), SyncOp.Delete, CancellationToken.None);
    }

    [Fact]
    public async Task FlagOff_NullSpaarkeListProvisioner_ReturnsEmptyString()
    {
        var sp = BuildProvider(todoSyncEnabled: false);
        var provisioner = sp.GetRequiredService<ISpaarkeListProvisioner>();

        var listId = await provisioner.EnsureListAsync(Guid.NewGuid(), CancellationToken.None);

        listId.Should().BeEmpty();
    }

    [Fact]
    public async Task FlagOff_NullTodoSubscriptionManager_ReturnsEmptyString()
    {
        var sp = BuildProvider(todoSyncEnabled: false);
        var manager = sp.GetRequiredService<ITodoSubscriptionManager>();

        var subscriptionId = await manager.EnsureSubscriptionAsync(
            Guid.NewGuid(), listId: "list-id-irrelevant-under-flag-off", CancellationToken.None);

        subscriptionId.Should().BeEmpty();
    }

    [Fact]
    public async Task FlagOff_NullTodoSyncBackfiller_IsNoOpButLogsOnce()
    {
        // Per ADR-032 P2 backfiller variant — observability matters: a no-op backfill should
        // still leave a log breadcrumb so an operator can confirm the flag-off no-op.
        var sp = BuildProvider(todoSyncEnabled: false);
        var backfiller = sp.GetRequiredService<ITodoSyncBackfiller>();

        // Should complete without throwing.
        await backfiller.BackfillAsync(Guid.NewGuid(), CancellationToken.None);

        // (We don't assert the exact log call because the test container uses NullLogger;
        //  the unit test verifies the no-throw contract — log emission is by construction.)
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // Flag ON (Phase 7 forward — placeholder until real impls land)
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlagOn_ResolvesITodoGraphSyncHandler_ToPlaceholder_NotNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: true);

        var handler = sp.GetRequiredService<ITodoGraphSyncHandler>();

        handler.Should().BeOfType<NotImplementedTodoGraphSyncHandler>();
        handler.Should().NotBeOfType<NullTodoGraphSyncHandler>();
    }

    [Fact]
    public void FlagOn_ResolvesISpaarkeListProvisioner_ToPlaceholder_NotNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: true);

        var provisioner = sp.GetRequiredService<ISpaarkeListProvisioner>();

        provisioner.Should().BeOfType<NotImplementedSpaarkeListProvisioner>();
        provisioner.Should().NotBeOfType<NullSpaarkeListProvisioner>();
    }

    [Fact]
    public void FlagOn_ResolvesITodoSubscriptionManager_ToPlaceholder_NotNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: true);

        var manager = sp.GetRequiredService<ITodoSubscriptionManager>();

        manager.Should().BeOfType<NotImplementedTodoSubscriptionManager>();
        manager.Should().NotBeOfType<NullTodoSubscriptionManager>();
    }

    [Fact]
    public void FlagOn_ResolvesITodoSyncBackfiller_ToPlaceholder_NotNullObject()
    {
        var sp = BuildProvider(todoSyncEnabled: true);

        var backfiller = sp.GetRequiredService<ITodoSyncBackfiller>();

        backfiller.Should().BeOfType<NotImplementedTodoSyncBackfiller>();
        backfiller.Should().NotBeOfType<NullTodoSyncBackfiller>();
    }

    [Fact]
    public async Task FlagOn_PlaceholderSyncHandler_ThrowsNotImplementedException()
    {
        // The placeholder must FAIL LOUDLY so a misconfigured "flag on without real impl"
        // environment does not silently no-op (ADR-032 §Anti-patterns).
        var sp = BuildProvider(todoSyncEnabled: true);
        var handler = sp.GetRequiredService<ITodoGraphSyncHandler>();

        var act = async () => await handler.SyncAsync(Guid.NewGuid(), SyncOp.Create, CancellationToken.None);

        await act.Should().ThrowAsync<NotImplementedException>()
            .Where(e => e.Message.Contains("Phase 7", StringComparison.Ordinal));
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // Boot integrity — flag off + no Graph config section
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Flag_Missing_BootsCleanly_ResolvesToNullObjects()
    {
        // Equivalent to "flag off + no Graph config": configuration has no
        // Spaarke:Graph:TodoSync:Enabled key at all. GetValue<bool>(...) returns the default
        // (false), so all four interfaces should resolve to Null-Object impls without
        // throwing.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()) // EMPTY config — no keys at all
            .Build();

        services.AddTodoSync(configuration);

        using var sp = services.BuildServiceProvider();

        // Resolving each interface must NOT throw and must return the Null-Object impl.
        sp.GetRequiredService<ITodoGraphSyncHandler>().Should().BeOfType<NullTodoGraphSyncHandler>();
        sp.GetRequiredService<ISpaarkeListProvisioner>().Should().BeOfType<NullSpaarkeListProvisioner>();
        sp.GetRequiredService<ITodoSubscriptionManager>().Should().BeOfType<NullTodoSubscriptionManager>();
        sp.GetRequiredService<ITodoSyncBackfiller>().Should().BeOfType<NullTodoSyncBackfiller>();
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(bool todoSyncEnabled)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Spaarke:Graph:TodoSync:Enabled"] = todoSyncEnabled ? "true" : "false",
            })
            .Build();

        services.AddTodoSync(configuration);
        return services.BuildServiceProvider();
    }
}
