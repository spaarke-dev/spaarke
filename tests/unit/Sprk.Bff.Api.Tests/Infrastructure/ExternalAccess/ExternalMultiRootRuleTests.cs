using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// The multi-root rule itself (spaarke-SPA-external-access-platform-r3 task 039 notes section 12; owner decision 2026-10-12,
/// option B): a linked root the caller cannot read withholds a row only when it is SECURE or its secure state is unknown.
/// The paths that call it are tested end to end in the list, document-route, message, feed and grid test classes.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalMultiRootRuleTests
{
    private static readonly Guid Readable = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid Other = Guid.Parse("a0390000-0000-4000-8000-0000000000dd");

    private static readonly CallerPrincipal Caller = new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(Readable, ExternalAccessLevel.ViewOnly) },
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static Task<SecureRootFlags> Read(
        NonSecureRootReader reader, params (ExternalGrantRootType, string?)[][] rows) =>
        ExternalMultiRootRule.ReadFlagsAsync(reader, Caller, rows, NullLogger.Instance, CancellationToken.None);

    private static NonSecureRootReader Answering(
        List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>> calls, params (ExternalGrantRootType, Guid)[] notSecure) =>
        (roots, _) =>
        {
            calls.Add(roots);
            return Task.FromResult<IReadOnlySet<(ExternalGrantRootType Root, Guid Id)>>(notSecure.ToHashSet());
        };

    [Fact]
    public async Task ANonSecureUnreadableRoot_DoesNotWithhold_ASecureOne_Does()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var flags = await Read(Answering(calls, (ExternalGrantRootType.Matter, Other)),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));

        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), Other.ToString(), null).Should().BeTrue();
        ExternalMultiRootRule.SecureRootsReadable(Caller, SecureRootFlags.None, Readable.ToString(), Other.ToString(), null)
            .Should().BeFalse("nothing known: the unreadable root counts as secure");
        calls.Should().ContainSingle().Which.Should().Equal((ExternalGrantRootType.Matter, Other));
    }

    [Fact]
    public async Task AReadableRoot_IsNeverLookedUp_AndARowWithNoUnreadableRoot_CausesNoRead()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();

        var flags = await Read(Answering(calls), ExternalMultiRootRule.Stamps(Readable.ToString(), null, null));

        calls.Should().BeEmpty();
        flags.Should().BeSameAs(SecureRootFlags.None);
    }

    [Fact]
    public async Task ManyRows_TheSameUnreadableRoot_IsAskedOnce()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var rows = Enumerable.Range(0, 20).Select(_ => ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null)).ToArray();

        await Read(Answering(calls), rows);

        calls.Should().ContainSingle().Which.Should().ContainSingle();
    }

    [Fact]
    public async Task AReaderThatThrows_FailsClosed()
    {
        var flags = await Read((_, _) => throw new InvalidOperationException("boom"),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));

        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), Other.ToString(), null).Should().BeFalse();
    }

    [Fact]
    public async Task AnAnswerForARootThatWasNotAsked_OpensNothing()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        // The reader claims the PROJECT with Other's id is not secure; the row links Other as a MATTER.
        var flags = await Read(Answering(calls, (ExternalGrantRootType.Project, Other)),
            ExternalMultiRootRule.Stamps(Readable.ToString(), Other.ToString(), null));

        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, Readable.ToString(), Other.ToString(), null).Should().BeFalse();
    }

    [Fact]
    public void AMalformedLookup_IsWithheld_AndNeverLookedUp()
    {
        ExternalMultiRootRule.UnreadableRoots(Caller, ExternalMultiRootRule.Stamps(Readable.ToString(), "not-a-guid", null))
            .Should().BeEmpty();
        ExternalMultiRootRule.SecureRootsReadable(Caller, SecureRootFlags.None, Readable.ToString(), "not-a-guid", null)
            .Should().BeFalse();
    }

    [Fact]
    public async Task PastTheLookupBound_TheRestCountAsSecure()
    {
        var calls = new List<IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)>>();
        var ids = Enumerable.Range(0, ExternalMultiRootRule.MaxFlagLookups + 5).Select(_ => Guid.NewGuid()).ToList();
        NonSecureRootReader everyAskedIsOpen = (roots, _) =>
        {
            calls.Add(roots);
            return Task.FromResult<IReadOnlySet<(ExternalGrantRootType Root, Guid Id)>>(roots.ToHashSet());
        };

        var flags = await Read(everyAskedIsOpen,
            ids.Select(id => ExternalMultiRootRule.Stamps(null, id.ToString(), null)).ToArray());

        calls.Should().ContainSingle().Which.Should().HaveCount(ExternalMultiRootRule.MaxFlagLookups);
        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, null, ids[0].ToString(), null).Should().BeTrue();
        ExternalMultiRootRule.SecureRootsReadable(Caller, flags, null, ids[^1].ToString(), null).Should().BeFalse();
    }
}
