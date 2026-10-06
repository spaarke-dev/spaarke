using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// PlaybookAuthorizationFilter on its SIBLING routes (PUT/share/unshare/canvas/node writes → OwnerOnly; GET by id,
/// sharing, canvas, clone, validate, run history → OwnerOrSharedOrPublic).
/// </summary>
/// <remarks>
/// unified-access-control-r2 task 164, owner round 12 item 6: OwnerOnly used to compare the caller's Entra
/// <c>oid</c> with the playbook's <c>_ownerid_value</c> (a Dataverse systemuserid), and the shared branch passed
/// the oid to a teammemberships query keyed by systemuserid — two GUID spaces, so OwnerOnly denied everyone and
/// OwnerOrSharedOrPublic reduced to "public". These tests pin the corrected identity: the caller's systemuserid
/// (WhoAmI over OBO) for OwnerOnly, and the playbook-use decision (public, or the caller's own Dataverse Read on
/// the row) for OwnerOrSharedOrPublic. The decision is evaluated by the REAL <see cref="AuthorizationService"/>
/// over a recording <see cref="IAccessDataSource"/>; the routes of task 164's own findings are covered through the
/// real host in <see cref="PlaybookRouteAuthorizationContractTests"/>.
/// </remarks>
[Trait("status", "repaired")]
public class PlaybookAuthorizationFilterTests
{
    private const string Playbooks = "sprk_analysisplaybooks";

    private static readonly Guid CallerOid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerSystemUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestPlaybookId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<IPlaybookService> _playbookService = new(MockBehavior.Strict);
    private readonly PlaybookRouteAuthorizationContractTests.RecordingAccessDataSource _access = new();
    private readonly PlaybookRouteAuthorizationContractTests.RecordingSystemUserProbe _probe = new() { SystemUserId = CallerSystemUserId };

    private PlaybookAuthorizationFilter CreateFilter(PlaybookAuthorizationMode mode) =>
        new(
            _playbookService.Object,
            NullLogger<PlaybookAuthorizationFilter>.Instance,
            mode,
            new AuthorizationService(
                _access,
                new IAuthorizationRule[] { new OperationAccessRule(NullLogger<OperationAccessRule>.Instance) },
                NullLogger<AuthorizationService>.Instance));

    [Fact]
    public void PlaybookAuthorizationMode_ValuesAreAppendedNeverRenumbered()
    {
        Assert.Equal(0, (int)PlaybookAuthorizationMode.OwnerOnly);
        Assert.Equal(1, (int)PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
        Assert.Equal(2, (int)PlaybookAuthorizationMode.UniformById);
        Assert.Equal(3, (int)PlaybookAuthorizationMode.Run);
    }

    // ── OwnerOnly ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OwnerOnly_CallerSystemUserIdIsTheOwner_Allows_EvenThoughTheOidDiffers()
    {
        PlaybookOwnedBy(CallerSystemUserId);

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOnly);

        Assert.True(nextCalled);
        Assert.Equal("success", result);
    }

    [Fact]
    public async Task OwnerOnly_OwnerIdEqualToTheCallersOid_IsNotOwnership()
    {
        // The pre-task-164 comparison: _ownerid_value is a systemuserid, so an oid-shaped value matching it
        // proves nothing about the caller.
        PlaybookOwnedBy(CallerOid);

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOnly);

        Assert.False(nextCalled);
        Assert.Equal(403, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task OwnerOnly_OtherOwner_Denies403()
    {
        PlaybookOwnedBy(OtherSystemUserId);

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOnly);

        Assert.False(nextCalled);
        Assert.Equal(403, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task OwnerOnly_UnresolvableCallerSystemUserId_Denies403()
    {
        PlaybookOwnedBy(CallerSystemUserId);
        _probe.SystemUserId = null;

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOnly);

        Assert.False(nextCalled);
        Assert.Equal(403, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task OwnerOnly_UnknownPlaybook_Is404()
    {
        _playbookService.Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOnly);

        Assert.False(nextCalled);
        Assert.IsType<NotFound>(result);
    }

    // ── OwnerOrSharedOrPublic: the playbook-use decision ─────────────────────────────────────

    [Fact]
    public async Task Access_PublicPlaybook_Allows_WithoutAnyRightsQuery()
    {
        Playbook(isPublic: true);

        var (_, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);

        Assert.True(nextCalled);
        Assert.Empty(_access.Calls);
    }

    [Fact]
    public async Task Access_PrivatePlaybookTheCallerCanReadInDataverse_Allows()
    {
        // Ownership, a team GrantAccess share and role depth all surface as Read in the caller's own answer.
        Playbook(isPublic: false);
        _access.Grant(Playbooks, TestPlaybookId, AccessRights.Read);

        var (_, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);

        Assert.True(nextCalled);
        Assert.Contains(_access.Calls, c => c.Set == Playbooks && c.Id == TestPlaybookId && c.HasToken);
    }

    [Fact]
    public async Task Access_PrivatePlaybookWithoutRead_Denies403()
    {
        Playbook(isPublic: false);
        _access.Grant(Playbooks, TestPlaybookId, AccessRights.AppendTo);

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);

        Assert.False(nextCalled);
        Assert.Equal(403, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Access_AccessSeamFault_Denies403_NeverAllows()
    {
        Playbook(isPublic: false);
        _access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess failed");

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);

        Assert.False(nextCalled);
        Assert.Equal(403, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Access_UnknownPlaybook_Is404()
    {
        _playbookService.Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);

        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);

        Assert.False(nextCalled);
        Assert.IsType<NotFound>(result);
    }

    // ── Caller identity and route id ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NoOidClaim_Is401()
    {
        var (result, nextCalled) = await InvokeAsync(
            PlaybookAuthorizationMode.OwnerOnly, user: new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(nextCalled);
        Assert.Equal(401, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task NonGuidRouteId_Is400()
    {
        var (result, nextCalled) = await InvokeAsync(PlaybookAuthorizationMode.OwnerOnly, routeId: "not-a-guid");

        Assert.False(nextCalled);
        Assert.Equal(400, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private void PlaybookOwnedBy(Guid ownerId) =>
        _playbookService.Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse { Id = TestPlaybookId, Name = "Test Playbook", OwnerId = ownerId, IsPublic = false });

    private void Playbook(bool isPublic) =>
        _playbookService.Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse { Id = TestPlaybookId, Name = "Test Playbook", OwnerId = OtherSystemUserId, IsPublic = isPublic });

    private async Task<(object? Result, bool NextCalled)> InvokeAsync(
        PlaybookAuthorizationMode mode, ClaimsPrincipal? user = null, string? routeId = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<CallerRecordAccessProbe>(_probe)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", CallerOid.ToString()), new Claim(ClaimTypes.NameIdentifier, "pairwise-sub-not-an-oid") },
                "TestAuth")),
            RequestServices = services,
        };
        httpContext.Request.RouteValues["id"] = routeId ?? TestPlaybookId.ToString();
        httpContext.Request.Headers.Authorization = "Bearer caller-token";

        var context = new Mock<EndpointFilterInvocationContext>();
        context.Setup(c => c.HttpContext).Returns(httpContext);

        var nextCalled = false;
        var result = await CreateFilter(mode).InvokeAsync(context.Object, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        });

        return (result, nextCalled);
    }
}
