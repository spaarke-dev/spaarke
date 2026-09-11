// teams-app-r1 Task 051 — SubjectStandingGrantReader hardening tests (design §5 standing-grant seam).
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

public class SubjectStandingGrantReaderTests
{
    private const string ContactEntity = "contact";
    private const string StandingGrantAttribute = "sprk_standinggrant";
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ReadForContactAsync_WhenFlagTrue_ReturnsTrue()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, true));

        var sut = CreateSut(dataverse.Object);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None))
            .Held.Should().BeTrue("the secured flag is set true → the contact holds a standing grant");
    }

    [Fact]
    public async Task ReadForContactAsync_WhenFlagFalse_ReturnsFalse()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, false));

        var sut = CreateSut(dataverse.Object);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None))
            .Held.Should().BeFalse("an explicit false flag confers no standing grant");
    }

    [Fact]
    public async Task ReadForContactAsync_WhenAttributeAbsent_ReturnsFalseFailClosed()
    {
        // The FLS-denial signature: a successful retrieve whose payload does NOT carry the secured
        // attribute (platform strips it when the app user lacks field read). MUST fail closed.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(ContactEntity, ContactId)); // attribute absent

        var sut = CreateSut(dataverse.Object);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None))
            .Held.Should().BeFalse("an FLS-stripped/absent flag must read as no standing grant (fail-closed)");
    }

    [Fact]
    public async Task ReadForContactAsync_WhenRetrieveReturnsNull_ReturnsFalseFailClosed()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);

        var sut = CreateSut(dataverse.Object);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None))
            .Held.Should().BeFalse("a null contact read cannot prove a standing grant → fail closed");
    }

    [Fact]
    public async Task ReadForContactAsync_WhenRetrieveThrows_ReturnsFalseFailClosedWithoutPropagating()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated FLS/transport fault"));

        var sut = CreateSut(dataverse.Object);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None))
            .Held.Should().BeFalse("a read fault must be swallowed as no standing grant, never surfaced to the caller");
    }

    [Fact]
    public async Task ReadForContactAsync_WhenCancelled_PropagatesOperationCanceled()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut(dataverse.Object);

        await FluentActions
            .Awaiting(() => sut.ReadForContactAsync(ContactId, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>(
                "cancellation is control flow, not a fail-closed 'no grant' outcome");
    }

    [Fact]
    public async Task ReadForContactAsync_WhenContactIdEmpty_ThrowsArgumentException()
    {
        var sut = CreateSut(new Mock<IDataverseService>(MockBehavior.Strict).Object);

        await FluentActions
            .Awaiting(() => sut.ReadForContactAsync(Guid.Empty, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadForContactAsync_NeverPerformsAnyWrite_ReadOnlyPath()
    {
        // A Strict mock with ONLY RetrieveAsync configured is the no-materialization proof: if the reader
        // attempted ANY create/update/delete (e.g. to materialize a sprk_externalrecordaccess row), the
        // Strict mock would throw on the unconfigured call and fail this test.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, true));

        var sut = CreateSut(dataverse.Object);

        await sut.ReadForContactAsync(ContactId, CancellationToken.None);

        dataverse.Verify(
            d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        dataverse.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Task 042 / FR-25 — the standing grant is LEVEL-BEARING.
    // ═════════════════════════════════════════════════════════════════════════════

    public static TheoryData<int, ExternalAccessLevel, AccessRights> BaselineCases => new()
    {
        { 100000000, ExternalAccessLevel.ViewOnly,    AccessRights.Read },
        { 100000001, ExternalAccessLevel.Collaborate, AccessRights.Read | AccessRights.Write | AccessRights.Create },
        { 100000002, ExternalAccessLevel.FullAccess,  AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete },
    };

    /// <summary>
    /// The exact option values are asserted as INTEGERS, not via the enum, because the enum is our
    /// side of the contract and the integers are Dataverse's. Verified against live `spaarkedev1`
    /// metadata 2026-09-10 (notes/task-042-standing-grant-levels.md §1) — if someone renumbers the
    /// enum to match a different environment, this fails instead of silently re-mapping levels.
    /// </summary>
    [Theory]
    [MemberData(nameof(BaselineCases))]
    public async Task ReadForContactAsync_MapsEachBaselineToItsExactRights(
        int optionValue, ExternalAccessLevel expectedLevel, AccessRights expectedRights)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWithBaseline(true, optionValue));

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Held.Should().BeTrue();
        state.Baseline.Should().Be(expectedLevel);
        state.Rights.Should().Be(expectedRights, "FR-25 pins the level→rights arithmetic exactly");
    }

    /// <summary>
    /// 🔴 THE OWNER DECISION (2026-09-10, option B): a standing grant with NO baseline contributes
    /// NOTHING. The alternatives — a View Only floor and a Collaborate floor — were rejected because a
    /// level nobody chose must not confer access. Measured at decision time: 2 of 2 standing-grant
    /// contacts had an empty baseline, both were backfilled to Collaborate before this shipped.
    /// </summary>
    [Fact]
    public async Task ReadForContactAsync_WhenFlagTrueButBaselineUnset_ContributesNoRights()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWith(StandingGrantAttribute, true)); // flag only — no baseline

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Held.Should().BeTrue("the flag IS set — that fact is not in doubt and must stay visible");
        state.Baseline.Should().BeNull();
        state.Rights.Should().Be(AccessRights.None,
            "an unset baseline is a level nobody chose, so it confers nothing (owner decision, option B)");
    }

    /// <summary>
    /// An out-of-range integer must be treated as unset, not passed through to the mapping as an
    /// undefined enum value.
    /// </summary>
    [Fact]
    public async Task ReadForContactAsync_WhenBaselineIsAnUnrecognisedValue_ContributesNoRights()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWithBaseline(true, 999999999));

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Baseline.Should().BeNull("an unrecognised option value is not a level");
        state.Rights.Should().Be(AccessRights.None, "fail closed — an unknown level must never widen access");
    }

    /// <summary>
    /// A baseline WITHOUT the flag confers nothing: the flag is the gate, the baseline is only its
    /// magnitude. This is the load-bearing negative — populating a baseline must not accidentally
    /// become a way to grant standing access.
    /// </summary>
    [Fact]
    public async Task ReadForContactAsync_WhenBaselineSetButFlagFalse_ConfersNothing()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactWithBaseline(false, 100000002)); // Full Access baseline, flag OFF

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Held.Should().BeFalse();
        state.Rights.Should().Be(AccessRights.None,
            "the FLAG is the gate; a Full Access baseline on a subject without a standing grant must " +
            "confer nothing, or populating the field would itself become a grant path");
    }

    /// <summary>
    /// One read for BOTH attributes (NFR-02: exactly one round trip per subject).
    /// </summary>
    [Fact]
    public async Task ReadForContactAsync_RequestsBothAttributesInASingleRetrieve()
    {
        string[]? requested = null;
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, string[] cols, CancellationToken _) => requested = cols)
            .ReturnsAsync(ContactWithBaseline(true, 100000001));

        await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        requested.Should().BeEquivalentTo(
            new[] { "sprk_standinggrant", "sprk_accesspermissiongrant" },
            "both fields come back in ONE round trip — NFR-02");
        dataverse.Verify(
            d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // The ORGANIZATION variant — built and tested here, consumed by task 043.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Same two logical names on <c>sprk_organization</c> — design §10 said the org field was
    /// <c>sprk_accesspermissions</c>; live metadata says <c>sprk_accesspermissiongrant</c>, identical
    /// to the contact field (verified 2026-09-10). This test is what would catch that regressing.
    /// </summary>
    [Fact]
    public async Task ReadForOrganizationAsync_ReadsTheOrganizationEntityWithTheSameAttributeNames()
    {
        var orgId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        string[]? requested = null;

        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync("sprk_organization", orgId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, string[] cols, CancellationToken _) => requested = cols)
            .ReturnsAsync(OrganizationWithBaseline(orgId, true, 100000000));

        var state = await CreateSut(dataverse.Object).ReadForOrganizationAsync(orgId, CancellationToken.None);

        requested.Should().BeEquivalentTo(new[] { "sprk_standinggrant", "sprk_accesspermissiongrant" });
        state.Held.Should().BeTrue();
        state.Rights.Should().Be(AccessRights.Read, "View Only on an organization maps the same way");
    }

    [Fact]
    public async Task ReadForOrganizationAsync_WhenReadFaults_FailsClosed()
    {
        var orgId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync("sprk_organization", orgId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unreachable"));

        var state = await CreateSut(dataverse.Object).ReadForOrganizationAsync(orgId, CancellationToken.None);

        state.Held.Should().BeFalse("the org reader has the identical fail-closed contract");
        state.Rights.Should().Be(AccessRights.None);
    }

    [Fact]
    public async Task ReadForOrganizationAsync_WhenIdEmpty_ThrowsArgumentException() =>
        await FluentActions
            .Awaiting(() => CreateSut(new Mock<IDataverseService>().Object)
                .ReadForOrganizationAsync(Guid.Empty, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

    private static SubjectStandingGrantReader CreateSut(IDataverseService dataverse)
        => new(dataverse, NullLogger<SubjectStandingGrantReader>.Instance);

    private static Entity ContactWith(string attribute, bool value)
    {
        var e = new Entity(ContactEntity, ContactId);
        e[attribute] = value;
        return e;
    }

    private static Entity ContactWithBaseline(bool standingGrant, int baselineOptionValue)
    {
        var e = new Entity(ContactEntity, ContactId)
        {
            [StandingGrantAttribute] = standingGrant,
            ["sprk_accesspermissiongrant"] = new OptionSetValue(baselineOptionValue)
        };
        return e;
    }

    private static Entity OrganizationWithBaseline(Guid orgId, bool standingGrant, int baselineOptionValue)
    {
        var e = new Entity("sprk_organization", orgId)
        {
            [StandingGrantAttribute] = standingGrant,
            ["sprk_accesspermissiongrant"] = new OptionSetValue(baselineOptionValue)
        };
        return e;
    }
}
