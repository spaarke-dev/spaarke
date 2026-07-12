using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarke-ai-architecture-redesign-r2 task AIR2-053 (FR-B-04): the Context Binder self-produces the
/// Business (host-identity), Workspace (environment facts), and Memory (record memory references) slices
/// from a host record — folding the R1 primitives into ONE assembly path. An explicit request fragment
/// always wins; the FR-B-04 NEGATIVE criterion (a stored fact that mirrors a live Dataverse field is
/// EXCLUDED from the envelope) and the memory soft-fail posture are pinned here.
/// </summary>
public sealed class ContextBinderSliceProductionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 7, 18, 30, 0, TimeSpan.Zero);

    private static ChatSessionManager BuildSessionManager() =>
        new(new InMemoryTenantCache(),
            Mock.Of<IChatDataverseRepository>(),
            Mock.Of<ILogger<ChatSessionManager>>());

    private static ContextBinder BuildBinder(IMemoryItemStore? store = null) =>
        new(BuildSessionManager(),
            Mock.Of<ILogger<ContextBinder>>(),
            memoryItemStore: store,
            timeProvider: new FixedTimeProvider(FixedNow));

    // ─── Business slice (host-identity) ───

    [Fact]
    public async Task BindAsync_HostContextIdOnly_ProducesIdOnlyBusinessFragment()
    {
        var bound = await BuildBinder().BindAsync(
            new ContextBindingRequest { HostEntityType = "matter", HostEntityId = "entity-123" },
            CancellationToken.None);

        bound.Context.Business!.Fragment.Should().Be(
            HostIdentityProducer.BuildEnrichmentBlock("matter", "entity-123", null, null),
            "with a host record but no explicit BusinessFragment the Binder self-produces the id-only block");
        bound.Context.Business.Meta.Present.Should().BeTrue();
    }

    [Fact]
    public async Task BindAsync_HostContextWithName_ProducesNamedBusinessFragment()
    {
        var bound = await BuildBinder().BindAsync(
            new ContextBindingRequest
            {
                HostEntityType = "matter",
                HostEntityId = "entity-123",
                HostEntityName = "Smith v. Jones",
                HostPageTypeLabel = "main form",
            },
            CancellationToken.None);

        bound.Context.Business!.Fragment.Should().Be(
            HostIdentityProducer.BuildEnrichmentBlock("matter", "entity-123", "Smith v. Jones", "main form"));
    }

    [Fact]
    public async Task BindAsync_ExplicitBusinessFragment_WinsOverHostContext()
    {
        var bound = await BuildBinder().BindAsync(
            new ContextBindingRequest
            {
                BusinessFragment = "EXPLICIT-BUSINESS",
                HostEntityType = "matter",
                HostEntityId = "entity-123",
            },
            CancellationToken.None);

        bound.Context.Business!.Fragment.Should().Be("EXPLICIT-BUSINESS",
            "an explicit request fragment ALWAYS wins over host-context self-production");
    }

    // ─── Workspace slice (environment facts) ───

    [Fact]
    public async Task BindAsync_AlwaysPopulatesWorkspaceWithCurrentDateDirective()
    {
        var bound = await BuildBinder().BindAsync(new ContextBindingRequest(), CancellationToken.None);

        bound.Context.Workspace!.Fragment.Should().Be(
            EnvironmentFactsProducer.BuildCurrentDateDirective(FixedNow),
            "environment facts exist every turn — the Workspace slice is always populated with the date directive");
        bound.Context.Workspace.Meta.Present.Should().BeTrue();
    }

    // ─── Memory slice (record memory references) ───

    [Fact]
    public async Task BindAsync_StoreFedRecordMemory_ProjectsReferences()
    {
        var items = new List<MemoryItem>
        {
            BuildRecordItem("mem-1", MemoryFactType.Party, "Plaintiff", "Company X"),
            BuildRecordItem("mem-2", MemoryFactType.KeyDate, "Hearing Date", "2026-07-15"),
        };
        var store = BuildStore(items);

        var bound = await BuildBinder(store.Object).BindAsync(
            new ContextBindingRequest { HostEntityType = "matter", HostEntityId = "entity-123" },
            CancellationToken.None);

        bound.Context.Memory!.Items.Select(r => r.ItemId).Should().BeEquivalentTo(new[] { "mem-1", "mem-2" });
        bound.Context.Memory.Items.Should().OnlyContain(r => r.Scope == MemoryScope.Record);
    }

    [Fact]
    public async Task BindAsync_StoreReturnsDataverseFieldMirror_ExcludesItFromMemoryReferences()
    {
        // FR-B-04 NEGATIVE criterion: a fact keyed by a Dataverse logical column name (sprk_matternumber)
        // merely mirrors a live field — the Binder reads those directly, so the mirror is EXCLUDED.
        var items = new List<MemoryItem>
        {
            BuildRecordItem("mem-derived", MemoryFactType.KeyFact, "Governing Law", "New York"),
            BuildRecordItem("mem-mirror", MemoryFactType.KeyFact, "sprk_matternumber", "M-2026-0042"),
        };
        var store = BuildStore(items);

        var bound = await BuildBinder(store.Object).BindAsync(
            new ContextBindingRequest { HostEntityType = "matter", HostEntityId = "entity-123" },
            CancellationToken.None);

        bound.Context.Memory!.Items.Select(r => r.ItemId).Should().ContainSingle()
            .Which.Should().Be("mem-derived",
                "the Dataverse-field-mirror item is excluded; only the derived-knowledge item survives");
    }

    [Fact]
    public async Task BindAsync_StoreThrows_SoftFailsToEmptyMemoryItems()
    {
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.GetForRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cosmos outage"));

        var bound = await BuildBinder(store.Object).BindAsync(
            new ContextBindingRequest { HostEntityType = "matter", HostEntityId = "entity-123" },
            CancellationToken.None);

        bound.Context.Memory!.Items.Should().BeEmpty(
            "a memory-store read failure soft-fails to empty items — a bind is never taken down by memory");
        bound.Context.Business!.Fragment.Should().NotBeNullOrEmpty(
            "the rest of the envelope still assembles despite the memory soft-fail");
    }

    // ─── Helpers ───

    private static Mock<IMemoryItemStore> BuildStore(IReadOnlyList<MemoryItem> items)
    {
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.GetForRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        return store;
    }

    private static MemoryItem BuildRecordItem(string id, MemoryFactType type, string key, string value) =>
        new()
        {
            Version = MemoryItemContract.SchemaVersion,
            Id = id,
            Scope = MemoryScope.Record,
            SubjectType = "matter",
            SubjectId = "entity-123",
            Source = MemoryOrigin.AiDerived,
            Fact = new MemoryFact { Type = type, Key = key, Value = value },
        };

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
