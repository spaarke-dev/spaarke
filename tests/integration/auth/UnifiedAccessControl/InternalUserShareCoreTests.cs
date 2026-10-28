using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
/// Task 193 (#2338) step 2: <see cref="InternalUserShareCore"/>, the /share-user write core that Manage Access's /grant will
/// share through. Its refusals, narrowing, No Access guard, read-back and fan-out are /share-user's and stay pinned by
/// <see cref="InternalUserShareTests"/> (unchanged, through the thin route). Pinned here: what the core adds for its second
/// caller — WHO the record was confirmed shared with, reported only with the confirmed 200, never with a refusal.
/// </summary>
public class InternalUserShareCoreTests
{
    private static readonly Guid MatterId = Guid.Parse("19319319-2222-4000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("19319319-1111-4000-8000-000000000001");

    private const AccessRights CollaboratingCaller =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    private readonly Rig _rig = new();

    private Task<InternalUserShareResult> Share()
        => _rig.ShareAsync(MatterId, UserId, ExternalAccessLevel.Collaborate,
            new InternalUserShareTests.StubCallerRightsProbe(CollaboratingCaller), Rig.Caller());

    [Fact]
    public async Task ShareAsync_ConfirmedNewShare_ReportsTheUserItWasSharedWith()
    {
        _rig.Users.SeedPerson(UserId, "Ada Lovelace");

        var result = await Share();

        var ok = result.Result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>().Subject;
        result.Shared.Should().NotBeNull();
        result.Shared!.SystemUserId.Should().Be(UserId);
        result.Shared.Response.Should().Be(ok.Value);
        result.Shared.Response.Outcome.Should().Be(InternalShareEndpoints.OutcomeCreated);
    }

    [Fact]
    public async Task ShareAsync_ShareTheUserAlreadyHolds_ReportsTheUserToo()
    {
        _rig.Users.SeedPerson(UserId, "Ada Lovelace");
        await Share();

        var again = await Share();

        again.Result.Should().BeOfType<Ok<ShareRecordWithUserResponse>>();
        again.Shared.Should().NotBeNull();
        again.Shared!.SystemUserId.Should().Be(UserId);
        again.Shared.Response.Outcome.Should().Be(InternalShareEndpoints.OutcomeUnchanged);
    }

    [Fact]
    public async Task ShareAsync_RefusedForADisabledUser_ReportsNobody_AndWritesNothing()
    {
        _rig.Users.SeedPerson(UserId, "Ada Lovelace", isDisabled: true);

        var result = await Share();

        (result.Result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        result.Shared.Should().BeNull();
        _rig.Shares.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// The core over in-memory stores, as <see cref="InternalUserShareTests"/> wires the /share-user route: an ordinary record
    /// (no secure roots, nothing filed), no No Access entries, an inert Assigned-To ledger and notifier.
    /// </summary>
    internal sealed class Rig
    {
        private const string TenantId = "00000000-0000-0000-0000-0000000000cc";

        private readonly Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld _children =
            Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard();

        private readonly SecureShareNoAccessGuard _guard;

        public Rig()
        {
            _guard = new SecureShareNoAccessGuard(Flags, new GrantPolicyTestDoubles.SeamNoAccessListReader(),
                new IdentityBinding.InMemoryContactIdentityStore(), AssignedAccessTestDoubles.NoFilingRows(),
                NullLogger<SecureShareNoAccessGuard>.Instance);
        }

        public FakeRecordShareTable Shares { get; } = new();

        public InternalUserShareTests.FakeSystemUsers Users { get; } = new();

        public GrantPolicyTestDoubles.FlagStubParticipationService Flags { get; } = new(RootRecordFlags.None);

        public Mock<ITenantCache> Cache { get; } = new();

        /// <summary>An authenticated workforce caller with a bearer token (the probe's OBO input).</summary>
        public static HttpContext Caller()
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("tid", TenantId),
                    new Claim("oid", "19319319-3333-4000-8000-000000000001"),
                }, "test")),
                TraceIdentifier = "trace-193",
            };
            http.Request.Headers.Authorization = "Bearer caller";
            return http;
        }

        public Task<InternalUserShareResult> ShareAsync(
            Guid recordId, Guid userId, ExternalAccessLevel level, CallerRecordAccessProbe probe, HttpContext http)
            => InternalUserShareCore.ShareAsync(
                new ShareRecordWithUserRequest("matter", recordId, userId, level),
                Shares, Users.Client, Flags, Cache.Object, probe, _children.Synchronizer(Shares), _guard,
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing(),
                AssignedAccessTestDoubles.InertMaterializer(), AssignedAccessTestDoubles.InertNotifier(), http,
                NullLogger.Instance, CancellationToken.None);
    }
}
