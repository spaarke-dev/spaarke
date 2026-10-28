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
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
        // The flag is held and the secured baseline reads empty, so the reader asks whether the BFF can read that column:
        // READS of its own identity, role and field permissions (none configured here, so the proof fails closed).
        dataverse.Verify(
            d => d.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.AtMost(4));
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

    // ═════════════════════════════════════════════════════════════════════════════
    // contact.sprk_accesspermissiongrant is field-secured: an EMPTY baseline is trusted as "not set" only once the BFF's
    // read of the column is proven (docs/data-model/access-inheritance.md, "When a record is trusted"). Unproven = no
    // baseline, BaselineUnreadable, and never a default level.
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid BffUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>What the BFF's own-identity queries answer.</summary>
    private sealed record Platform(
        bool OwnUserReadable = true, bool SystemAdministrator = false, bool DirectProfileRead = false,
        bool TeamProfileRead = false, bool Faults = false);

    private static Mock<IDataverseService> DataverseWith(Entity contact, Platform platform, List<QueryExpression> queries)
        => DataverseWith(contact, () => platform, queries);

    /// <summary>As above, with the platform's answers read at each query (a grant removed later is seen).</summary>
    private static Mock<IDataverseService> DataverseWith(Entity contact, Func<Platform> current, List<QueryExpression> queries)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contact);
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                queries.Add(q);
                var platform = current();
                if (platform.Faults)
                {
                    throw new InvalidOperationException("simulated metadata fault");
                }

                var rows = new EntityCollection();
                switch (q.EntityName)
                {
                    case "systemuser" when platform.OwnUserReadable:
                        rows.Entities.Add(new Entity("systemuser", BffUserId) { ["systemuserid"] = BffUserId });
                        break;
                    case "role" when platform.SystemAdministrator:
                        rows.Entities.Add(new Entity("role", Guid.NewGuid()));
                        break;
                    case "fieldpermission":
                        var throughTeam = q.LinkEntities.Single().LinkToEntityName == "teamprofiles";
                        if (throughTeam ? platform.TeamProfileRead : platform.DirectProfileRead)
                        {
                            rows.Entities.Add(new Entity("fieldpermission", Guid.NewGuid())
                            {
                                ["entityname"] = ContactEntity,
                                ["attributelogicalname"] = "sprk_accesspermissiongrant",
                            });
                        }
                        break;
                }

                return Task.FromResult(rows);
            });
        return dataverse;
    }

    private static Entity HeldWithoutBaseline(Guid? id = null) =>
        new(ContactEntity, id ?? ContactId) { [StandingGrantAttribute] = true };

    [Fact]
    public async Task EmptyBaseline_WhenTheBffsReadIsNotProven_IsUnreadable_AndConfersNothing()
    {
        // MUST FIRE: the BFF's user is found, is not a System Administrator, and no profile (direct or team) grants Read.
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(HeldWithoutBaseline(), new Platform(), queries);

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Held.Should().BeTrue();
        state.Baseline.Should().BeNull("an unproven empty read is no baseline");
        state.BaselineUnreadable.Should().BeTrue("an empty read the BFF cannot vouch for may hide a real level");
        state.Rights.Should().Be(AccessRights.None, "an unreadable level is never replaced by a default");
        queries.Select(q => q.EntityName).Should().Equal("systemuser", "role", "fieldpermission", "fieldpermission");
    }

    [Theory]
    [InlineData(false, false)]  // the BFF's own user cannot be read
    [InlineData(true, true)]    // every proof query faults
    public async Task EmptyBaseline_WhenTheProofCannotBeMade_FailsClosed(bool ownUserReadable, bool faults)
    {
        // MUST FIRE: a fault, or no own-user answer, is "not proven" — never "readable".
        var dataverse = DataverseWith(HeldWithoutBaseline(),
            new Platform(OwnUserReadable: ownUserReadable, SystemAdministrator: true, DirectProfileRead: true, Faults: faults),
            new List<QueryExpression>());

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.BaselineUnreadable.Should().BeTrue();
        state.Baseline.Should().BeNull();
        state.Rights.Should().Be(AccessRights.None);
    }

    [Theory]
    [InlineData(true, false, false)]   // System Administrator
    [InlineData(false, true, false)]   // a profile the BFF's user is a direct member of grants Read
    [InlineData(false, false, true)]   // a profile of a team the BFF's user belongs to grants Read
    public async Task EmptyBaseline_WhenTheBffsReadIsProven_IsNotSet_NotUnreadable(bool admin, bool direct, bool team)
    {
        // MUST NOT FIRE: a proven read makes the empty value "not set" (the owner's option B: it confers nothing).
        var dataverse = DataverseWith(HeldWithoutBaseline(),
            new Platform(SystemAdministrator: admin, DirectProfileRead: direct, TeamProfileRead: team),
            new List<QueryExpression>());

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Held.Should().BeTrue();
        state.Baseline.Should().BeNull();
        state.BaselineUnreadable.Should().BeFalse("the BFF's read of the column is proven, so empty means not set");
        state.Rights.Should().Be(AccessRights.None);
    }

    [Fact]
    public async Task EmptyBaseline_AfterANonEmptyReadOfAnotherContact_IsStillUnreadableWithoutProof()
    {
        // MUST FIRE: a value read on one row proves nothing about another row's empty read (a field share can grant one
        // record without the column), so the empty read is still decided on only with a proof — here, none.
        var otherContact = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(HeldWithoutBaseline(), new Platform(), queries);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, otherContact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(ContactEntity, otherContact)
            {
                [StandingGrantAttribute] = true,
                ["sprk_accesspermissiongrant"] = new OptionSetValue(100000000),
            });

        var sut = CreateSut(dataverse.Object);
        (await sut.ReadForContactAsync(otherContact, CancellationToken.None)).BaselineUnreadable.Should().BeFalse();
        var state = await sut.ReadForContactAsync(ContactId, CancellationToken.None);

        state.BaselineUnreadable.Should().BeTrue("only the System Administrator, direct-profile and team-profile legs prove a read");
        state.Rights.Should().Be(AccessRights.None);
        queries.Select(q => q.EntityName).Should().Equal("systemuser", "role", "fieldpermission", "fieldpermission");
    }

    [Fact]
    public async Task AProvenRead_IsHeldWithinTheProofLifetime()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(HeldWithoutBaseline(), new Platform(DirectProfileRead: true), queries);
        var sut = CreateSut(dataverse.Object, clock);

        await sut.ReadForContactAsync(ContactId, CancellationToken.None);
        var proofQueries = queries.Count;
        clock.Advance(SecuredColumnReadProof.ProofLifetime - TimeSpan.FromSeconds(1));
        var second = await sut.ReadForContactAsync(ContactId, CancellationToken.None);

        second.BaselineUnreadable.Should().BeFalse();
        queries.Should().HaveCount(proofQueries, "within its lifetime the proof is held, not made again");
    }

    [Fact]
    public async Task AfterTheProofLifetime_ANowFailingReproof_IsUnreadable()
    {
        // MUST FIRE: the proof expires after ProofLifetime and is made again; a grant removed meanwhile now fails it.
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        var platform = new Platform(DirectProfileRead: true);
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(HeldWithoutBaseline(), () => platform, queries);
        var sut = CreateSut(dataverse.Object, clock);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None)).BaselineUnreadable.Should().BeFalse();
        var proofQueries = queries.Count;
        platform = new Platform();
        clock.Advance(SecuredColumnReadProof.ProofLifetime);
        var state = await sut.ReadForContactAsync(ContactId, CancellationToken.None);

        state.BaselineUnreadable.Should().BeTrue("the expired proof was made again, and it no longer holds");
        state.Rights.Should().Be(AccessRights.None);
        queries.Count.Should().BeGreaterThan(proofQueries, "an expired proof is made again");
    }

    [Fact]
    public async Task AnUnprovenRead_IsAttemptedAgain_AtTheNextUse()
    {
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(HeldWithoutBaseline(), new Platform(), queries);
        var sut = CreateSut(dataverse.Object);

        (await sut.ReadForContactAsync(ContactId, CancellationToken.None)).BaselineUnreadable.Should().BeTrue();
        var first = queries.Count;
        (await sut.ReadForContactAsync(ContactId, CancellationToken.None)).BaselineUnreadable.Should().BeTrue();

        queries.Count.Should().Be(first * 2, "a failed proof is not remembered, so a later grant is picked up");
    }

    [Fact]
    public async Task TheProofQueries_AskForReadOnTheContactBaselineColumn_ForTheBffsOwnUser()
    {
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(HeldWithoutBaseline(), new Platform(), queries);

        await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        queries[0].Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "systemuserid" && c.Operator == ConditionOperator.EqualUserId);
        queries[1].Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "roletemplateid" && (Guid)c.Values[0] == SecuredColumnReadProof.SystemAdministratorRoleTemplateId);
        foreach (var fieldPermission in queries.Where(q => q.EntityName == "fieldpermission"))
        {
            // The table is checked in code (Dataverse refuses a query condition on fieldpermission.entityname given a
            // logical name), so the query selects entityname and has no condition on it.
            fieldPermission.Criteria.Conditions.Select(c => (c.AttributeName, c.Values[0]))
                .Should().BeEquivalentTo(new (string, object)[]
                {
                    ("attributelogicalname", "sprk_accesspermissiongrant"), ("canread", 4),
                });
            fieldPermission.ColumnSet.Columns.Should().Contain("entityname");
        }
    }

    [Theory]
    [InlineData("Platform Administrators", true, false)]  // MUST NOT FIRE: the System Administrator template, renamed, proves
    [InlineData("System Administrator", false, true)]     // MUST FIRE: the name on a role of another template proves nothing
    public async Task TheSystemAdministratorLeg_MatchesTheRoleTemplate_NeverTheRoleName(
        string roleName, bool systemAdministratorTemplate, bool unreadable)
    {
        // The BFF's user holds exactly one role; no profile grants Read. The role query is answered by its own conditions,
        // so it is the query (by template id, or by name) that decides.
        var heldTemplate = systemAdministratorTemplate ? SecuredColumnReadProof.SystemAdministratorRoleTemplateId : Guid.NewGuid();
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HeldWithoutBaseline());
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                var rows = new EntityCollection();
                if (q.EntityName == "systemuser")
                {
                    rows.Entities.Add(new Entity("systemuser", BffUserId) { ["systemuserid"] = BffUserId });
                }
                else if (q.EntityName == "role" && q.Criteria.Conditions.All(c => c.AttributeName switch
                         {
                             "name" => Equals(c.Values[0], roleName),
                             "roletemplateid" => Equals(c.Values[0], heldTemplate),
                             _ => false,
                         }))
                {
                    rows.Entities.Add(new Entity("role", Guid.NewGuid()));
                }

                return Task.FromResult(rows);
            });

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.BaselineUnreadable.Should().Be(unreadable);
        state.Rights.Should().Be(AccessRights.None);
    }

    [Fact]
    public async Task UnrecognisedBaselineValue_IsARead_NotUnreadable()
    {
        // MUST NOT FIRE: a value came back, so the column is readable; the value is simply not a level.
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(ContactWithBaseline(true, 999999999), new Platform(), queries);

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.BaselineUnreadable.Should().BeFalse();
        state.Rights.Should().Be(AccessRights.None);
        queries.Should().BeEmpty();
    }

    [Fact]
    public async Task FlagNotHeld_NeverAsksForTheProof()
    {
        var queries = new List<QueryExpression>();
        var dataverse = DataverseWith(ContactWith(StandingGrantAttribute, false), new Platform(), queries);

        var state = await CreateSut(dataverse.Object).ReadForContactAsync(ContactId, CancellationToken.None);

        state.Should().Be(StandingGrantState.NotHeld);
        queries.Should().BeEmpty();
    }

    [Fact]
    public async Task OrganizationBaseline_IsNotFieldSecured_SoAnEmptyReadIsNotSet()
    {
        // The organization column is not field-secured: an empty read there is "not set" and needs no proof.
        var orgId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync("sprk_organization", orgId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_organization", orgId) { [StandingGrantAttribute] = true });

        var state = await CreateSut(dataverse.Object).ReadForOrganizationAsync(orgId, CancellationToken.None);

        state.Held.Should().BeTrue();
        state.BaselineUnreadable.Should().BeFalse();
        state.Rights.Should().Be(AccessRights.None);
        dataverse.Verify(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancellationDuringTheProof_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HeldWithoutBaseline());
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new EntityCollection());
            });

        await FluentActions
            .Awaiting(() => CreateSut(dataverse.Object).ReadForContactAsync(ContactId, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static SubjectStandingGrantReader CreateSut(IDataverseService dataverse, TimeProvider? time = null)
        => new(dataverse, NullLogger<SubjectStandingGrantReader>.Instance, time);

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
