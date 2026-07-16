using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 task 030 (FR-E2): the Context Binder folds the User-scope STATED
/// profile block (read via <see cref="IStatedProfileReader"/>, rendered via <see cref="StatedProfileRenderer"/>)
/// into <c>ContextEnvelope.userFragment</c> — the SIBLING of the user-memory RECALL fragment, composed
/// AHEAD of it. Pins: (a) a profiled user carries the block (role label + practice areas + focus/office/
/// prefs), (b) an unprofiled/absent user yields no stated block and the bind succeeds, (c) a reader failure
/// soft-fails to null (ADR-032 P2 quiet no-op), (d) an explicit request fragment always wins, and the
/// stated-profile-FIRST + memory-recall-SECOND composition order.
/// </summary>
public sealed class ContextBinderStatedProfileTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private const string SystemUserId = "9b0e6a1e-0000-4000-8000-00000000abcd";

    private static ChatSessionManager BuildSessionManager() =>
        new(new InMemoryTenantCache(),
            Mock.Of<IChatDataverseRepository>(),
            Mock.Of<ILogger<ChatSessionManager>>());

    private static ContextBinder BuildBinder(
        IStatedProfileReader reader,
        IMemoryItemStore? store = null) =>
        new(BuildSessionManager(),
            Mock.Of<ILogger<ContextBinder>>(),
            memoryItemStore: store,
            timeProvider: new FixedTimeProvider(FixedNow),
            statedProfileReader: reader);

    private static Mock<IStatedProfileReader> ReaderReturning(StatedProfile? profile)
    {
        var reader = new Mock<IStatedProfileReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return reader;
    }

    private static StatedProfile FullProfile() => new()
    {
        RoleLabel = "Partner",
        PracticeAreaNames = new[] { "Corporate", "Litigation" },
        FocusAreas = "M&A and joint ventures",
        OfficeLocation = "New York",
        AssistantPreferences = "Concise, cite sources",
    };

    [Fact]
    public async Task BindAsync_ProfiledUser_CarriesStatedProfileFragment()
    {
        var reader = ReaderReturning(FullProfile());

        var bound = await BuildBinder(reader.Object).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        var fragment = bound.Context.User!.Fragment;
        fragment.Should().NotBeNullOrEmpty();
        fragment.Should().Contain("### Your Profile (stated)");
        fragment.Should().Contain("Role: Partner");
        fragment.Should().Contain("Practice Areas: Corporate, Litigation");
        fragment.Should().Contain("Focus Areas: M&A and joint ventures");
        fragment.Should().Contain("Office: New York");
        fragment.Should().Contain("Assistant Preferences: Concise, cite sources");
    }

    [Fact]
    public async Task BindAsync_UnprofiledUser_ProducesNoStatedProfileFragmentAndBindSucceeds()
    {
        // Reader returns null (no profile row) and NO memory store — the User fragment is absent.
        var reader = ReaderReturning(null);

        var bound = await BuildBinder(reader.Object).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        bound.Context.User?.Fragment.Should().BeNull(
            "an unprofiled user with no memory contributes no User fragment");
        bound.Context.Workspace!.Meta.Present.Should().BeTrue(
            "the rest of the envelope still assembles — the bind succeeds");
    }

    [Fact]
    public async Task BindAsync_ReaderThrows_SoftFailsToNullAndBindSucceeds()
    {
        var reader = new Mock<IStatedProfileReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse outage"));

        var bound = await BuildBinder(reader.Object).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        bound.Context.User?.Fragment.Should().BeNull(
            "a stated-profile read failure soft-fails to null — a bind is never taken down by the profile");
        bound.Context.Workspace!.Meta.Present.Should().BeTrue(
            "the rest of the envelope still assembles despite the profile soft-fail");
    }

    [Fact]
    public async Task BindAsync_ExplicitUserFragment_WinsOverStatedProfile()
    {
        var reader = ReaderReturning(FullProfile());

        var bound = await BuildBinder(reader.Object).BindAsync(
            new ContextBindingRequest
            {
                CallerSystemUserId = SystemUserId,
                UserFragment = "EXPLICIT-USER-FRAGMENT",
            },
            CancellationToken.None);

        bound.Context.User!.Fragment.Should().Be("EXPLICIT-USER-FRAGMENT",
            "an explicit request fragment ALWAYS wins over stated-profile self-production");
        reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the reader is not even consulted when an explicit fragment is supplied");
    }

    [Fact]
    public async Task BindAsync_ProfileAndMemory_ComposesProfileBeforeMemoryRecall()
    {
        var reader = ReaderReturning(FullProfile());
        var store = new Mock<IMemoryItemStore>();
        store.Setup(s => s.ToUserPromptFragmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("### User Memory (from prior sessions)\n- Preferred citation style: Bluebook");

        var bound = await BuildBinder(reader.Object, store.Object).BindAsync(
            new ContextBindingRequest { CallerSystemUserId = SystemUserId },
            CancellationToken.None);

        var fragment = bound.Context.User!.Fragment!;
        fragment.Should().Contain("### Your Profile (stated)").And.Contain("Bluebook");
        fragment.IndexOf("### Your Profile (stated)", StringComparison.Ordinal)
            .Should().BeLessThan(fragment.IndexOf("Bluebook", StringComparison.Ordinal),
                "the stated-profile block renders FIRST, then the memory-recall block");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
