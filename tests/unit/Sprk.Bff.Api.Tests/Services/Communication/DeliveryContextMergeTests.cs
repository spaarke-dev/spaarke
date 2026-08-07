using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// FR-C2 (task 022) delivery-context merge. Each test protects a concrete contract: the set-union is IDEMPOTENT
/// (SB redelivery of the same duplicate adds nothing), case-insensitive, order-preserving; and the read-modify-
/// write <see cref="DeliveryContextMerge.MergeAsync"/> writes ONLY on change and is non-fatal (NFR-04). The
/// Dataverse boundary is the mocked <see cref="IGenericEntityService"/>.
/// </summary>
public class DeliveryContextMergeTests
{
    // ── Pure set-union (the idempotency core) ───────────────────────────────────────────────

    [Fact]
    public void Union_FirstValue_ReturnsIt() =>
        DeliveryContextMerge.Union(null, "a@x.com").Should().Be("a@x.com");

    [Fact]
    public void Union_NewValue_AppendsAsSet() =>
        DeliveryContextMerge.Union("a@x.com", "b@y.com").Should().Be("a@x.com; b@y.com");

    [Fact]
    public void Union_AlreadyPresent_IsIdempotent() =>
        DeliveryContextMerge.Union("a@x.com; b@y.com", "a@x.com").Should().Be("a@x.com; b@y.com",
            "re-merging an existing member (SB redelivery) must not double-append");

    [Fact]
    public void Union_CaseInsensitiveDedup() =>
        DeliveryContextMerge.Union("Mailbox@Spaarke.com", "MAILBOX@spaarke.COM").Should().Be("Mailbox@Spaarke.com",
            "mailbox addresses / oids are case-insensitive — a case variant is the same member");

    [Fact]
    public void Union_BlankValue_LeavesSetUnchanged() =>
        DeliveryContextMerge.Union("a@x.com", "   ").Should().Be("a@x.com");

    [Fact]
    public void Union_PreservesOrder_ExistingFirstThenNew() =>
        DeliveryContextMerge.Union("a@x.com; b@y.com", "c@z.com").Should().Be("a@x.com; b@y.com; c@z.com");

    // ── MergeAsync (read-modify-write; write-only-on-change; non-fatal) ──────────────────────

    private static Entity Row(Guid id, string attr, string? value)
    {
        var e = new Entity("sprk_communication", id);
        if (value is not null) e[attr] = value;
        return e;
    }

    [Fact]
    public async Task MergeAsync_NewMailbox_WritesUnion()
    {
        var id = Guid.NewGuid();
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveAsync("sprk_communication", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(id, DeliveryContextMerge.DeliveredMailboxesAttribute, "a@x.com"));
        Dictionary<string, object>? written = null;
        ds.Setup(g => g.UpdateAsync("sprk_communication", id, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);

        await DeliveryContextMerge.MergeAsync(ds.Object, id, DeliveryContextMerge.DeliveredMailboxesAttribute, "b@y.com", NullLogger.Instance, CancellationToken.None);

        written.Should().NotBeNull();
        written![DeliveryContextMerge.DeliveredMailboxesAttribute].Should().Be("a@x.com; b@y.com");
    }

    [Fact]
    public async Task MergeAsync_AlreadyPresent_DoesNotWrite()
    {
        var id = Guid.NewGuid();
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveAsync("sprk_communication", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(id, DeliveryContextMerge.DeliveredMailboxesAttribute, "a@x.com; b@y.com"));

        await DeliveryContextMerge.MergeAsync(ds.Object, id, DeliveryContextMerge.DeliveredMailboxesAttribute, "a@x.com", NullLogger.Instance, CancellationToken.None);

        ds.Verify(g => g.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never,
            "an unchanged set (idempotent redelivery) must not incur a write");
    }

    [Fact]
    public async Task MergeAsync_RetrieveThrows_IsNonFatal()
    {
        var id = Guid.NewGuid();
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse blip"));

        var act = async () => await DeliveryContextMerge.MergeAsync(ds.Object, id, DeliveryContextMerge.DeliveredMailboxesAttribute, "b@y.com", NullLogger.Instance, CancellationToken.None);

        await act.Should().NotThrowAsync("the merge must never fail capture/upload (NFR-04)");
        ds.Verify(g => g.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MergeAsync_EmptyValueOrId_IsNoOp()
    {
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict); // any Dataverse call fails the test

        await DeliveryContextMerge.MergeAsync(ds.Object, Guid.NewGuid(), DeliveryContextMerge.DeliveredMailboxesAttribute, "  ", NullLogger.Instance, CancellationToken.None);
        await DeliveryContextMerge.MergeAsync(ds.Object, Guid.Empty, DeliveryContextMerge.DeliveredMailboxesAttribute, "a@x.com", NullLogger.Instance, CancellationToken.None);
        // no Dataverse interaction expected (strict mock would throw)
    }
}
