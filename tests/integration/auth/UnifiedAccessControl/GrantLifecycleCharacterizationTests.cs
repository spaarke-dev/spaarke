using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization suite for the external grant write path.
///
/// Pins finding A-11 (unified-access-control-r2 spec NFR-07), rated High and ranked #1 of 13 in
/// notes/investigation/10-finding-confirmations.md: <c>/grant</c> unconditionally CREATEs a row
/// (<c>GrantExternalAccessEndpoint.CreateGrantAsync:130-147</c>) with no pre-existence or upsert
/// check, while <c>/revoke</c> deactivates exactly ONE row by <c>AccessRecordId</c>
/// (<c>RevokeExternalAccessEndpoint.cs:96-97</c>). Two identical grants therefore produce two active
/// rows, and revoking one leaves the other active — access survives revocation. The read path hides
/// this: <c>ExternalParticipationService.QueryGrantSetAsync:469-472</c> collapses duplicates with
/// <c>GroupBy(...).Max(level)</c> and never returns the underlying access-record ids, so no
/// effective-access view can reveal that N active rows back one logical grant.
///
/// The seam is <see cref="DataverseWebApiClient"/>, whose write methods are <c>virtual</c> — the
/// module boundary ADR-038 §4 designates for substitution. No <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (banned, ADR-038 §7 B1) and no production change.
/// </summary>
public class GrantLifecycleCharacterizationTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";

    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it).
    /// ClientSecretCredential is constructed but never used — the virtual methods are all overridden,
    /// so no token is ever requested and no network call occurs.
    /// </summary>
    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    private static Mock<DataverseWebApiClient> NewClientMock()
    {
        var mock = new Mock<DataverseWebApiClient>(
            ClientConfig(),
            NullLogger<DataverseWebApiClient>.Instance);

        mock.Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid());

        mock.Setup(c => c.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    private static GrantAccessRequest Request(ExternalAccessLevel level = ExternalAccessLevel.ViewOnly) =>
        new(
            ContactId: ContactId,
            ProjectId: ProjectId,
            AccessLevel: level,
            ExpiryDate: null,
            OrganizationId: null);

    /// <summary>
    /// Invokes the production create path. <c>callerOid: null</c> short-circuits
    /// <c>ResolveGrantedBySystemUserIdAsync</c> before it touches the client, isolating the create.
    /// </summary>
    private static Task<Guid> CreateGrant(Mock<DataverseWebApiClient> client, GrantAccessRequest request) =>
        GrantExternalAccessEndpoint.CreateGrantAsync(
            request,
            ExternalGrantRootType.Project,
            ProjectId,
            callerOid: null,
            client.Object,
            Mock.Of<ITenantCache>(),
            new DefaultHttpContext(),
            NullLogger.Instance,
            CancellationToken.None);

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-11. Flipped by task 010 (FR-09).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-11 — CURRENT (BROKEN) BEHAVIOR. Granting the SAME contact the SAME level on the SAME root
    /// twice issues two CREATEs and yields two distinct active access-record ids. There is no
    /// pre-existence query, so the write path cannot be idempotent.
    ///
    /// FLIPPED BY: task 010 (FR-09) — the grant MUST become idempotent (upsert), so the second call
    /// returns the FIRST record id and issues no second create.
    /// </summary>
    [Fact]
    public async Task Characterization_CreateGrantAsync_CalledTwiceWithSameInput_CreatesTwoActiveRows()
    {
        // Arrange
        var client = NewClientMock();

        // Act — the double-submit / grant-revoke-regrant shape from the confirmed failure scenario.
        var firstId = await CreateGrant(client, Request());
        var secondId = await CreateGrant(client, Request());

        // Assert — CURRENT behavior: two rows, two ids, both active.
        secondId.Should().NotBe(firstId,
            "A-11 pins the CURRENT broken state: /grant is not idempotent, so the second grant is a " +
            "second active row rather than a no-op. Task 010 makes this an upsert and flips this to Be(firstId).");

        client.Verify(
            c => c.CreateAsync(GrantEntitySet, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// A-11 — the root cause, stated directly: the create path performs NO read before writing. A
    /// correct upsert must first look for an existing active row for (contact, root, level).
    ///
    /// FLIPPED BY: task 010 (FR-09) — a pre-existence query MUST then occur.
    /// </summary>
    [Fact]
    public async Task Characterization_CreateGrantAsync_PerformsNoPreExistenceCheckBeforeCreating()
    {
        // Arrange
        var client = NewClientMock();

        // Act
        await CreateGrant(client, Request());

        // Assert — CURRENT behavior: a bare create, with no query for an existing grant.
        client.Verify(
            c => c.QueryAsync<It.IsAnyType>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "A-11 pins the CURRENT broken state: CreateGrantAsync:130-147 writes without ever asking " +
            "whether an active grant already exists. Task 010 adds that query and flips this.");

        client.Verify(
            c => c.CreateAsync(GrantEntitySet, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A-11 — duplicates accumulate across DIFFERENT levels for the same (contact, root) too. This is
    /// the shape that makes revocation lossy: revoking the FullAccess row can leave a ViewOnly row —
    /// or, worse, revoking a ViewOnly row leaves FullAccess standing.
    ///
    /// FLIPPED BY: task 010 (FR-09).
    /// </summary>
    [Fact]
    public async Task Characterization_CreateGrantAsync_ForSameContactAtDifferentLevels_CreatesSeparateRows()
    {
        // Arrange
        var client = NewClientMock();

        // Act
        var viewOnlyId = await CreateGrant(client, Request(ExternalAccessLevel.ViewOnly));
        var fullAccessId = await CreateGrant(client, Request(ExternalAccessLevel.FullAccess));

        // Assert — CURRENT behavior: two independent active rows for one logical grantee/root pair.
        fullAccessId.Should().NotBe(viewOnlyId);
        client.Verify(
            c => c.CreateAsync(GrantEntitySet, It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — must already hold. Task 010 MUST preserve the payload contract while
    // making the write idempotent; a wrong @odata.bind key silently breaks every grant.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildGrantPayload_ForPerContactGrant_BindsContactAndExactlyOneTypedRoot()
    {
        // Act
        var payload = GrantExternalAccessEndpoint.BuildGrantPayload(
            Request(), ExternalGrantRootType.Project, ProjectId, grantedBySystemUserId: null);

        // Assert — PascalCase nav properties, verified live against $metadata (task 070).
        var dict = payload.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;

        dict.Should().ContainKey("sprk_Project@odata.bind");
        dict["sprk_Project@odata.bind"].Should().Be($"/sprk_projects({ProjectId})");
        dict.Should().ContainKey("sprk_Contact@odata.bind");
        dict["sprk_Contact@odata.bind"].Should().Be($"/contacts({ContactId})");

        // Exactly ONE typed root lookup — never two.
        dict.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)
                             && k.Contains("sprk_Matter", StringComparison.Ordinal))
            .Should().Be(0);
        dict.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)
                             && k.Contains("sprk_WorkAssignment", StringComparison.Ordinal))
            .Should().Be(0);
    }

    [Fact]
    public void BuildGrantPayload_ForOrganizationGrant_OmitsContactBind()
    {
        // Arrange — an ORG grant is identified by the ABSENCE of sprk_Contact (task 073 #7). The read
        // path (AccessibleRecordSetService Term 3) depends on that omission, so it is load-bearing.
        var orgRequest = new GrantAccessRequest(
            ContactId: Guid.Empty,
            ProjectId: ProjectId,
            AccessLevel: ExternalAccessLevel.ViewOnly,
            ExpiryDate: null,
            OrganizationId: Guid.Parse("33333333-3333-3333-3333-333333333333"));

        // Act
        var payload = GrantExternalAccessEndpoint.BuildGrantPayload(
            orgRequest, ExternalGrantRootType.Project, ProjectId, grantedBySystemUserId: null);

        // Assert
        var dict = payload.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        dict.Should().NotContainKey("sprk_Contact@odata.bind");
        dict.Should().ContainKey("sprk_Project@odata.bind");
    }
}
