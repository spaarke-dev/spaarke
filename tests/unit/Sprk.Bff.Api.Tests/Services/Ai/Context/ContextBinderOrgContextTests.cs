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
/// spaarkeai-assistant-enhancements-r1 FR-E5 BU/team (un-defer D-032-01): the Context Binder folds the
/// User-scope ORG (business-unit + team) block (read via <see cref="IUserOrgContextReader"/>, rendered via
/// <see cref="UserOrgContextRenderer"/>) into <c>ContextEnvelope.userFragment</c> as its OWN deterministic
/// block — AFTER the stated-profile block, BEFORE the memory-recall block. Pins: (a) a user with BU/team
/// carries the org block, (b) the deterministic three-block composition order, (c) an org-read failure
/// soft-fails to null (ADR-032 P2 quiet no-op), (d) the composed fragment stays within the 700-token User
/// budget (task 032), and (e) the ADR-039 preference-only pin — BU/team never reaches
/// <see cref="AgentToolFilterContext"/>.
/// </summary>
public sealed class ContextBinderOrgContextTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private const string SystemUserId = "9b0e6a1e-0000-4000-8000-00000000abcd";

    private static ChatSessionManager BuildSessionManager() =>
        new(new InMemoryTenantCache(),
            Mock.Of<IChatDataverseRepository>(),
            Mock.Of<ILogger<ChatSessionManager>>());

    private static ContextBinder BuildBinder(
        IUserOrgContextReader orgReader,
        IStatedProfileReader? statedReader = null,
        IMemoryItemStore? store = null) =>
        new(BuildSessionManager(),
            Mock.Of<ILogger<ContextBinder>>(),
            memoryItemStore: store,
            timeProvider: new FixedTimeProvider(FixedNow),
            statedProfileReader: statedReader ?? NullStatedProfileReader.Instance,
            userOrgContextReader: orgReader);

    private static IUserOrgContextReader OrgReaderReturning(UserOrgContext? context)
    {
        var reader = new Mock<IUserOrgContextReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        return reader.Object;
    }

    private static IStatedProfileReader StatedReaderReturning(StatedProfile profile)
    {
        var reader = new Mock<IStatedProfileReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return reader.Object;
    }

    private static IMemoryItemStore MemoryReturning(string fragment)
    {
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.ToUserPromptFragmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fragment);
        return store.Object;
    }

    private static UserOrgContext FullOrg() => new()
    {
        BusinessUnitName = "Litigation Group",
        TeamNames = new[] { "Corporate", "Litigation" },
    };

    [Fact]
    public async Task BindAsync_UserWithOrgContext_CarriesOrgBlockInUserFragment()
    {
        var bound = await BuildBinder(OrgReaderReturning(FullOrg())).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        var fragment = bound.Context.User!.Fragment;
        fragment.Should().NotBeNullOrEmpty();
        fragment.Should().Contain("### Your Organization");
        fragment.Should().Contain("Business Unit: Litigation Group");
        fragment.Should().Contain("Teams: Corporate, Litigation");
    }

    [Fact]
    public async Task BindAsync_OrgReaderReturnsNull_ProducesNoOrgBlockAndBindSucceeds()
    {
        var bound = await BuildBinder(OrgReaderReturning(null)).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        bound.Context.User?.Fragment.Should().BeNull(
            "a user with no BU/team and no other User producer contributes no User fragment");
        bound.Context.Workspace!.Meta.Present.Should().BeTrue("the rest of the envelope still assembles");
    }

    [Fact]
    public async Task BindAsync_OrgReaderThrows_SoftFailsToNullAndBindSucceeds()
    {
        var reader = new Mock<IUserOrgContextReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("identity outage"));

        var bound = await BuildBinder(reader.Object).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        bound.Context.User?.Fragment.Should().BeNull(
            "an org-context read failure soft-fails to null — a bind is never taken down by the org context");
        bound.Context.Workspace!.Meta.Present.Should().BeTrue(
            "the rest of the envelope still assembles despite the org soft-fail");
    }

    [Fact]
    public async Task BindAsync_StatedProfileOrgAndMemory_ComposesInDeterministicOrder()
    {
        var stated = StatedReaderReturning(new StatedProfile
        {
            RoleLabel = "Partner",
            PracticeAreaNames = new[] { "Corporate" },
        });
        var memory = MemoryReturning("### User Memory (from prior sessions)\n- Preferred citation style: Bluebook");

        var bound = await BuildBinder(OrgReaderReturning(FullOrg()), stated, memory).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        var fragment = bound.Context.User!.Fragment!;
        var statedIndex = fragment.IndexOf("### Your Profile (stated)", StringComparison.Ordinal);
        var orgIndex = fragment.IndexOf("### Your Organization", StringComparison.Ordinal);
        var memoryIndex = fragment.IndexOf("Bluebook", StringComparison.Ordinal);

        statedIndex.Should().BeGreaterThanOrEqualTo(0);
        orgIndex.Should().BeGreaterThanOrEqualTo(0);
        memoryIndex.Should().BeGreaterThanOrEqualTo(0);
        statedIndex.Should().BeLessThan(orgIndex, "the stated-profile block renders FIRST");
        orgIndex.Should().BeLessThan(memoryIndex, "the org block renders SECOND (after stated, before memory recall)");
    }

    [Fact]
    public async Task BindAsync_ComposedUserFragmentWithOrgBlock_StaysWithinUserBudget()
    {
        // task 032 budget invariant: the org block (~25–50 tokens) must NOT push a realistic worst-case
        // composed User fragment (heavy stated free text + a full 250-token memory recall) over the 700
        // ceiling. Build a heavy-but-realistic composition and assert the User slice is NOT breached.
        var stated = StatedReaderReturning(new StatedProfile
        {
            RoleLabel = "Associate General Counsel",
            PracticeAreaNames = new[] { "Corporate", "Litigation", "Employment", "Real Estate" },
            FocusAreas = new string('x', 300),          // ~75 tokens of free text
            OfficeLocation = "New York",
            AssistantPreferences = new string('y', 300), // ~75 tokens of free text
        });
        var memory = MemoryReturning(new string('z', 1000)); // ~250 tokens (User-memory recall cap)

        var bound = await BuildBinder(OrgReaderReturning(FullOrg()), stated, memory).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        bound.Context.User!.Meta.EstimatedTokens.Should().BeLessThanOrEqualTo(EnvelopeBudget.User,
            "the org block keeps the composed User fragment under the 700-token budget (task 032 headroom holds)");

        var userLine = bound.BudgetReport!.Lines.Single(l => l.Slice == ContextBudgetSlice.User);
        userLine.Breached.Should().BeFalse(
            "adding the FR-E5 org block does not breach the ratified User budget for a realistic worst-case profile");
    }

    // ── ADR-039 preference-only pin: BU/team is CONTEXT that biases the one turn's prompt only ───────────

    [Fact]
    public async Task BindAsync_OrgContext_ConfinedToUserFragment_AndAgentToolFilterContextHasNoOrgMember()
    {
        var bound = await BuildBinder(OrgReaderReturning(FullOrg())).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        // The org context lands ONLY in the preference-only User fragment (the prompt channel).
        bound.Context.User!.Fragment.Should().Contain("Litigation Group",
            "the BU/team context biases the NL turn via the User fragment — the prompt channel, not a control channel");

        // Architectural invariant (ADR-039 "preference-only, never feeds AgentToolFilterContext"): the
        // grounding pre-filter's context carries ONLY structural session facts — no BU/team-derived member
        // exists through which the org context could enter the grounding/dispatch decision.
        typeof(AgentToolFilterContext).GetProperties()
            .Select(p => p.Name)
            .Should().BeEquivalentTo(
                new[] { "Surface", "HasSessionFiles", "HasActiveDocument", "HasAnalysisBinding" },
                "the grounding filter context is structural-facts-only — no business-unit/team-derived member exists");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
