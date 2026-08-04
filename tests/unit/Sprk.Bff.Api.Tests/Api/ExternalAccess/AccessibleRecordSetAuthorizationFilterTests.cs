// teams-app-r1 Task 022 — AccessibleRecordSetAuthorizationFilter tests (ADR-008 enforcement gate).
//
// Verifies the single enforcement POINT: record ∈ accessible(principal) ⇒ continue; otherwise DENY
// (403, no body — not a silent omission). Also covers fail-closed pipeline misconfiguration (401)
// and a malformed record-id contract error (400). The composition itself is covered by
// AccessibleRecordSetServiceTests; here the service is substituted so the filter's allow/deny wiring
// is exercised for every principal plane.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public class AccessibleRecordSetAuthorizationFilterTests
{
    private const string EntityType = "sprk_project";
    private const string RouteKey = "recordId";
    private static readonly Guid RecordId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private static readonly WorkforcePrincipal SystemUser = new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Oid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        TenantId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    };

    private static readonly WorkforcePrincipal Contact = new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Oid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        TenantId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    };

    [Theory]
    [MemberData(nameof(AllPrincipals))]
    public async Task InvokeAsync_RecordInAccessibleSet_CallsNext(WorkforcePrincipal principal)
    {
        var service = new Mock<IAccessibleRecordSetService>();
        service
            .Setup(s => s.IsRecordAccessibleAsync(principal, EntityType, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sentinel = new object();
        var (context, nextCalled) = BuildContext(principal, RecordId.ToString(), sentinel);
        var filter = CreateFilter(service.Object);

        var result = await filter.InvokeAsync(context, nextCalled.Next);

        nextCalled.WasCalled.Should().BeTrue();
        result.Should().BeSameAs(sentinel);
    }

    [Theory]
    [MemberData(nameof(AllPrincipals))]
    public async Task InvokeAsync_RecordNotInAccessibleSet_Denies403AndDoesNotCallNext(WorkforcePrincipal principal)
    {
        var service = new Mock<IAccessibleRecordSetService>();
        service
            .Setup(s => s.IsRecordAccessibleAsync(principal, EntityType, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var (context, nextCalled) = BuildContext(principal, RecordId.ToString(), new object());
        var filter = CreateFilter(service.Object);

        var result = await filter.InvokeAsync(context, nextCalled.Next);

        nextCalled.WasCalled.Should().BeFalse("a denied record must short-circuit — no bytes served");
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_NoResolvedPrincipal_Denies401FailClosed()
    {
        var service = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var (context, nextCalled) = BuildContext(principal: null, RecordId.ToString(), new object());
        var filter = CreateFilter(service.Object);

        var result = await filter.InvokeAsync(context, nextCalled.Next);

        nextCalled.WasCalled.Should().BeFalse();
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvokeAsync_MissingOrInvalidRecordId_Returns400(string? rawId)
    {
        var service = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var (context, nextCalled) = BuildContext(SystemUser, rawId, new object());
        var filter = CreateFilter(service.Object);

        var result = await filter.InvokeAsync(context, nextCalled.Next);

        nextCalled.WasCalled.Should().BeFalse();
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    public static IEnumerable<object[]> AllPrincipals()
    {
        yield return new object[] { SystemUser };
        yield return new object[] { Contact };
    }

    // ─────────────────────────────────────────────────────────────────────

    private static AccessibleRecordSetAuthorizationFilter CreateFilter(IAccessibleRecordSetService service)
        => new(service, NullLogger<AccessibleRecordSetAuthorizationFilter>.Instance, EntityType, RouteKey);

    private static (EndpointFilterInvocationContext Context, NextTracker Tracker) BuildContext(
        WorkforcePrincipal? principal, string? rawRecordId, object sentinel)
    {
        var httpContext = new DefaultHttpContext();
        if (principal is not null)
        {
            httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] = principal;
        }
        if (rawRecordId is not null)
        {
            httpContext.Request.RouteValues[RouteKey] = rawRecordId;
        }

        var context = EndpointFilterInvocationContext.Create(httpContext);
        return (context, new NextTracker(sentinel));
    }

    private sealed class NextTracker
    {
        private readonly object _sentinel;
        public bool WasCalled { get; private set; }

        public NextTracker(object sentinel) => _sentinel = sentinel;

        public ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            WasCalled = true;
            return ValueTask.FromResult<object?>(_sentinel);
        }
    }
}
