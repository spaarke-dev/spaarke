// R3 Task 032 — OrganizationMembershipResolver unit tests
// Verifies Option (b) — configurable Lookup field on sprk_organization
// targeting systemuser. See projects/spaarke-platform-foundations-r3/notes/
// sprk-organization-mapping-decision.md for the mechanism decision.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

public class OrganizationMembershipResolverTests
{
    private static readonly Guid TestUserId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetOrganizationIdsAsync_ReturnsEmpty_WhenUserLookupFieldNotConfigured()
    {
        // Arrange — operator has not set MembershipOptions.OrganizationLookup.UserLookupField.
        // Expected behaviour: fail-soft, return empty list, do NOT call Dataverse.
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions { UserLookupField = string.Empty }
            });

        // Act
        var result = await sut.GetOrganizationIdsAsync(
            TestUserId, identityContext: null, ct: CancellationToken.None);

        // Assert
        result.Should().NotBeNull("the contract forbids null returns");
        result.Should().BeEmpty("no mapping configured → no organizations");
        entityService.VerifyNoOtherCalls(); // Dataverse must NOT be hit
    }

    [Fact]
    public async Task GetOrganizationIdsAsync_ReturnsEmpty_WhenSystemUserIdIsEmpty()
    {
        // Arrange — guard for Guid.Empty inputs (defensive — should never come from
        // a healthy caller, but membership pipeline must not throw on bad input).
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions { UserLookupField = "sprk_owneruser" }
            });

        // Act
        var result = await sut.GetOrganizationIdsAsync(
            Guid.Empty, identityContext: null, ct: CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        entityService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrganizationIdsAsync_ReturnsEmpty_WhenDataverseReturnsZeroRows()
    {
        // Arrange — user simply has no organization affiliations. Common case
        // (most users won't map to any organization).
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions { UserLookupField = "sprk_owneruser" }
            });

        // Act
        var result = await sut.GetOrganizationIdsAsync(
            TestUserId, identityContext: null, ct: CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrganizationIdsAsync_ReturnsAllOrganizationIds_WhenDataverseReturnsRows()
    {
        // Arrange — user maps to 3 organizations via the configured Lookup field.
        var orgIds = new[]
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
        };
        var collection = new EntityCollection();
        foreach (var id in orgIds)
        {
            var ent = new Entity("sprk_organization", id);
            collection.Entities.Add(ent);
        }

        var entityService = new Mock<IGenericEntityService>();
        FetchExpression? captured = null;
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(collection);

        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions
                {
                    UserLookupField = "sprk_owneruser",
                    MaxOrganizationsPerUser = 500,
                }
            });

        // Act
        var result = await sut.GetOrganizationIdsAsync(
            TestUserId, identityContext: null, ct: CancellationToken.None);

        // Assert — order is preserved, all GUIDs round-tripped
        result.Should().HaveCount(3);
        result.Should().ContainInOrder(orgIds);

        // Sanity-check the FetchXml composition — configured field name, systemUserId,
        // and the configured cap all appear in the query.
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("sprk_organization");
        captured.Query.Should().Contain("sprk_owneruser");
        captured.Query.Should().Contain(TestUserId.ToString("D"));
        captured.Query.Should().Contain("top='500'");
    }

    [Fact]
    public async Task GetOrganizationIdsAsync_ReturnsEmpty_WhenDataverseThrows()
    {
        // Arrange — fail-soft contract: a query exception (e.g., the configured
        // Lookup field doesn't exist on sprk_organization, or permission denied)
        // returns empty + logs Warning. Must NOT propagate the exception.
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse failure"));

        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions { UserLookupField = "sprk_owneruser" }
            });

        // Act
        var result = await sut.GetOrganizationIdsAsync(
            TestUserId, identityContext: null, ct: CancellationToken.None);

        // Assert
        result.Should().BeEmpty(
            "fail-soft: a Dataverse failure must not cascade-fail the membership pipeline");
    }

    [Fact]
    public async Task GetOrganizationIdsAsync_HonoursCap_WhenMaxOrganizationsPerUserIsLow()
    {
        // Arrange — operator configured a low cap (e.g., 2). FetchXml emits `top='2'`.
        var entityService = new Mock<IGenericEntityService>();
        FetchExpression? captured = null;
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((fe, _) => captured = fe)
            .ReturnsAsync(new EntityCollection());

        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions
                {
                    UserLookupField = "sprk_owneruser",
                    MaxOrganizationsPerUser = 2,
                }
            });

        // Act
        await sut.GetOrganizationIdsAsync(TestUserId, identityContext: null, ct: CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("top='2'");
    }

    [Fact]
    public async Task GetOrganizationIdsAsync_PropagatesCancellation()
    {
        // Arrange — cancellation MUST surface to the caller (NOT swallowed by the
        // fail-soft catch block).
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = BuildSut(
            entityService,
            new MembershipOptions
            {
                OrganizationLookup = new OrganizationLookupOptions { UserLookupField = "sprk_owneruser" }
            });

        // Act + Assert
        var act = async () => await sut.GetOrganizationIdsAsync(
            TestUserId, identityContext: null, ct: CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OrganizationMembershipResolver BuildSut(
        Mock<IGenericEntityService> entityService,
        MembershipOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<MembershipOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);

        return new OrganizationMembershipResolver(
            entityService.Object,
            monitor.Object,
            NullLogger<OrganizationMembershipResolver>.Instance);
    }
}
