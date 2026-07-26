using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 — memory-activation Move 1 (pins-recall integrity fix). Pinned
/// context was WRITTEN (manage-pinned-context tool + Q7 UI) but NEVER recalled into any prompt — the only
/// reader was the dark <c>MemoryCompositionService</c>, so a "pinned" fact never reached the LLM. This
/// pins the fix: on a MATTER host the Binder folds the matter's user-curated pins into the record-memory
/// PROMPT FRAGMENT — deterministic order, TopK-capped, gated (matter + tenant + kill-switch), soft-fail.
/// These are behavior protectors: delete any one and a real regression (pin silently absent from prompt,
/// non-matter leak, nondeterministic order, or a bind taken down by a pin-store outage) goes unnoticed.
/// </summary>
public sealed class ContextBinderPinnedContextTests
{
    private const string Tenant = "tenant-1";
    private const string MatterId = "matter-9f2";

    private static ChatSessionManager BuildSessionManager() =>
        new(new InMemoryTenantCache(),
            Mock.Of<IChatDataverseRepository>(),
            Mock.Of<ILogger<ChatSessionManager>>());

    private static ContextBinder BuildBinder(
        IPinnedContextRepository? pins = null,
        PinnedContextRecallOptions? options = null) =>
        new(BuildSessionManager(),
            Mock.Of<ILogger<ContextBinder>>(),
            pinnedContextRepository: pins,
            pinnedContextRecallOptions: options is null ? null : Options.Create(options));

    private static ContextBindingRequest MatterRequest() =>
        new() { HostEntityType = "matter", HostEntityId = MatterId, TenantId = Tenant };

    // ─── The payoff: matter pins reach the prompt fragment ───

    [Fact]
    public async Task BindAsync_MatterHostWithPins_FoldsPinnedContentIntoRecordMemoryFragment()
    {
        var repo = BuildRepo(
            Pin("p1", "NDA", "The operative NDA is Exhibit C.", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var bound = await BuildBinder(repo.Object).BindAsync(MatterRequest(), CancellationToken.None);

        bound.RecordMemoryFragment.Should().NotBeNull();
        bound.RecordMemoryFragment.Should().Contain("Pinned Context (user-curated)");
        bound.RecordMemoryFragment.Should().Contain("The operative NDA is Exhibit C.",
            "a written pin must now actually reach the prompt — that IS the Move 1 fix");
        bound.RecordMemoryFragment.Should().Contain("NDA — The operative NDA is Exhibit C.",
            "a pin title is rendered as the bullet label");
    }

    [Fact]
    public async Task BindAsync_MatterHostWithPins_RendersDeterministicOrderByCreatedAtThenId()
    {
        // Supplied out of order; expected order = CreatedAt asc, then ordinal Id — byte-stable across turns
        // so the appended block does not perturb the record-memory determinism/budget/fingerprint contract.
        var repo = BuildRepo(
            Pin("b", "Later", "second", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            Pin("a", "Earlier", "first", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var bound = await BuildBinder(repo.Object).BindAsync(MatterRequest(), CancellationToken.None);

        var fragment = bound.RecordMemoryFragment!;
        fragment.IndexOf("first", StringComparison.Ordinal)
            .Should().BeLessThan(fragment.IndexOf("second", StringComparison.Ordinal),
                "the earlier-created pin renders first regardless of repository order");
    }

    [Fact]
    public async Task BindAsync_MatterHostWithMorePinsThanTopK_CapsAtTopK()
    {
        var repo = BuildRepo(
            Pin("p1", "one", "alpha", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Pin("p2", "two", "bravo", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            Pin("p3", "three", "charlie", new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)));

        var bound = await BuildBinder(repo.Object, new PinnedContextRecallOptions { TopK = 2 })
            .BindAsync(MatterRequest(), CancellationToken.None);

        var fragment = bound.RecordMemoryFragment!;
        fragment.Should().Contain("alpha").And.Contain("bravo");
        fragment.Should().NotContain("charlie", "only the first TopK (2) pins are rendered — NFR-10 budget bound");
    }

    [Fact]
    public async Task BindAsync_RecordMemoryAndPinsBothPresent_AppendsPinsAfterRecordMemory()
    {
        // The pin block rides the record-memory fragment; when both exist, record memory comes first and the
        // pin block is appended (a distinct, later block) so existing record-memory prompt sites are unchanged.
        var repo = BuildRepo(Pin("p1", "Clause", "settlement clause Y", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var memoryStore = new Mock<IMemoryItemStore>();
        memoryStore.Setup(s => s.ToRecordPromptFragmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("### Matter Context (from prior sessions)\n**Parties**: Plaintiff — Company X");

        var binder = new ContextBinder(
            BuildSessionManager(),
            Mock.Of<ILogger<ContextBinder>>(),
            memoryItemStore: memoryStore.Object,
            pinnedContextRepository: repo.Object);

        var bound = await binder.BindAsync(MatterRequest(), CancellationToken.None);

        var fragment = bound.RecordMemoryFragment!;
        fragment.IndexOf("Matter Context", StringComparison.Ordinal)
            .Should().BeLessThan(fragment.IndexOf("Pinned Context", StringComparison.Ordinal),
                "record memory renders first; the pin block is appended after it");
    }

    // ─── The gates: no leak, no accident ───

    [Fact]
    public async Task BindAsync_NonMatterHost_ProducesNoPinnedBlock()
    {
        var repo = BuildRepo(Pin("p1", "t", "should not appear", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var bound = await BuildBinder(repo.Object).BindAsync(
            new ContextBindingRequest { HostEntityType = "project", HostEntityId = "proj-1", TenantId = Tenant },
            CancellationToken.None);

        (bound.RecordMemoryFragment ?? string.Empty).Should().NotContain("Pinned Context",
            "pins are matter-scoped (GetByMatterAsync) — a non-matter host never resolves them");
        repo.Verify(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the matter gate short-circuits before any repository read on a non-matter host");
    }

    [Fact]
    public async Task BindAsync_NoTenant_ProducesNoPinnedBlock()
    {
        var repo = BuildRepo(Pin("p1", "t", "no tenant partition", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var bound = await BuildBinder(repo.Object).BindAsync(
            new ContextBindingRequest { HostEntityType = "matter", HostEntityId = MatterId },
            CancellationToken.None);

        (bound.RecordMemoryFragment ?? string.Empty).Should().NotContain("Pinned Context",
            "no tenant → no Cosmos partition key → the pin read is skipped");
        repo.Verify(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BindAsync_KillSwitchDisabled_ProducesNoPinnedBlock()
    {
        var repo = BuildRepo(Pin("p1", "t", "kill-switch off", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var bound = await BuildBinder(repo.Object, new PinnedContextRecallOptions { Enabled = false })
            .BindAsync(MatterRequest(), CancellationToken.None);

        (bound.RecordMemoryFragment ?? string.Empty).Should().NotContain("Pinned Context");
        repo.Verify(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "Enabled=false short-circuits before the repository read");
    }

    [Fact]
    public async Task BindAsync_NoPinsForMatter_ProducesNoPinnedBlock()
    {
        var bound = await BuildBinder(BuildRepo().Object).BindAsync(MatterRequest(), CancellationToken.None);

        (bound.RecordMemoryFragment ?? string.Empty).Should().NotContain("Pinned Context",
            "an empty pin set renders no block (no dangling heading)");
    }

    [Fact]
    public async Task BindAsync_PinRepositoryThrows_SoftFailsWithoutTakingDownTheBind()
    {
        var repo = new Mock<IPinnedContextRepository>();
        repo.Setup(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cosmos outage"));

        var bound = await BuildBinder(repo.Object).BindAsync(MatterRequest(), CancellationToken.None);

        (bound.RecordMemoryFragment ?? string.Empty).Should().NotContain("Pinned Context",
            "a pin-store read failure degrades the pin block to absent — never a null block");
        bound.Context.Business!.Fragment.Should().NotBeNullOrEmpty(
            "the rest of the envelope still assembles despite the pin soft-fail — a bind is never taken down by pins");
    }

    // ─── Helpers ───

    private static Mock<IPinnedContextRepository> BuildRepo(params PinnedContextItem[] pins)
    {
        var repo = new Mock<IPinnedContextRepository>();
        repo.Setup(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pins);
        return repo;
    }

    private static PinnedContextItem Pin(string id, string title, string content, DateTimeOffset createdAt) =>
        new()
        {
            Id = $"pinned-context_{Tenant}_user-1_{id}",
            DocumentType = "pinned-context",
            TenantId = Tenant,
            UserId = "user-1",
            PinType = PinType.MatterFact,
            Title = title,
            Content = content,
            MatterId = MatterId,
            CreatedAt = createdAt,
            CreatedBy = "user-1",
        };
}
