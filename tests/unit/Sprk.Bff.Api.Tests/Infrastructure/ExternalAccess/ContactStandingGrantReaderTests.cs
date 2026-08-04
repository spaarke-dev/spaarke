// teams-app-r1 Task 051 — ContactStandingGrantReader hardening tests (design §5 standing-grant seam).
//
// The reader answers the single yes/no gate the accessible-set composition (task 022) uses to admit a
// contact's standing-grant runtime-membership term: "does contact.sprk_standinggrant == true?" It reads
// the FLS-secured boolean APP-ONLY and is FAIL-CLOSED — any ambiguity (missing contact, FLS-stripped
// attribute, transport fault) must read as NO standing grant so an unreadable flag can never over-grant.
//
// These protect two security-load-bearing behaviors task 051 hardens:
//   (1) fail-closed on every non-true outcome (false / absent / null / throw), and
//   (2) NO write path — the standing-grant read never creates/updates/deletes a sprk_externalrecordaccess
//       row (proven with a Strict mock: only RetrieveAsync is permitted; any other Dataverse call fails).
//
// Module-boundary substitute only (IDataverseService) per tests/CLAUDE.md.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class ContactStandingGrantReaderTests
{
    private const string ContactEntity = "contact";
    private const string StandingGrantAttribute = "sprk_standinggrant";
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task HasStandingGrantAsync_WhenFlagTrue_ReturnsTrue()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, true));

        var sut = CreateSut(dataverse.Object);

        (await sut.HasStandingGrantAsync(ContactId, CancellationToken.None))
            .Should().BeTrue("the secured flag is set true → the contact holds a standing grant");
    }

    [Fact]
    public async Task HasStandingGrantAsync_WhenFlagFalse_ReturnsFalse()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, false));

        var sut = CreateSut(dataverse.Object);

        (await sut.HasStandingGrantAsync(ContactId, CancellationToken.None))
            .Should().BeFalse("an explicit false flag confers no standing grant");
    }

    [Fact]
    public async Task HasStandingGrantAsync_WhenAttributeAbsent_ReturnsFalseFailClosed()
    {
        // The FLS-denial signature: a successful retrieve whose payload does NOT carry the secured
        // attribute (platform strips it when the app user lacks field read). MUST fail closed.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(ContactEntity, ContactId)); // attribute absent

        var sut = CreateSut(dataverse.Object);

        (await sut.HasStandingGrantAsync(ContactId, CancellationToken.None))
            .Should().BeFalse("an FLS-stripped/absent flag must read as no standing grant (fail-closed)");
    }

    [Fact]
    public async Task HasStandingGrantAsync_WhenRetrieveReturnsNull_ReturnsFalseFailClosed()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);

        var sut = CreateSut(dataverse.Object);

        (await sut.HasStandingGrantAsync(ContactId, CancellationToken.None))
            .Should().BeFalse("a null contact read cannot prove a standing grant → fail closed");
    }

    [Fact]
    public async Task HasStandingGrantAsync_WhenRetrieveThrows_ReturnsFalseFailClosedWithoutPropagating()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated FLS/transport fault"));

        var sut = CreateSut(dataverse.Object);

        (await sut.HasStandingGrantAsync(ContactId, CancellationToken.None))
            .Should().BeFalse("a read fault must be swallowed as no standing grant, never surfaced to the caller");
    }

    [Fact]
    public async Task HasStandingGrantAsync_WhenCancelled_PropagatesOperationCanceled()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut(dataverse.Object);

        await FluentActions
            .Awaiting(() => sut.HasStandingGrantAsync(ContactId, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>(
                "cancellation is control flow, not a fail-closed 'no grant' outcome");
    }

    [Fact]
    public async Task HasStandingGrantAsync_WhenContactIdEmpty_ThrowsArgumentException()
    {
        var sut = CreateSut(new Mock<IDataverseService>(MockBehavior.Strict).Object);

        await FluentActions
            .Awaiting(() => sut.HasStandingGrantAsync(Guid.Empty, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task HasStandingGrantAsync_NeverPerformsAnyWrite_ReadOnlyPath()
    {
        // A Strict mock with ONLY RetrieveAsync configured is the no-materialization proof: if the reader
        // attempted ANY create/update/delete (e.g. to materialize a sprk_externalrecordaccess row), the
        // Strict mock would throw on the unconfigured call and fail this test.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, true));

        var sut = CreateSut(dataverse.Object);

        await sut.HasStandingGrantAsync(ContactId, CancellationToken.None);

        dataverse.Verify(
            d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        dataverse.VerifyNoOtherCalls();
    }

    private static ContactStandingGrantReader CreateSut(IDataverseService dataverse)
        => new(dataverse, NullLogger<ContactStandingGrantReader>.Instance);

    private static Entity ContactWith(string attribute, bool value)
    {
        var e = new Entity(ContactEntity, ContactId);
        e[attribute] = value;
        return e;
    }
}
