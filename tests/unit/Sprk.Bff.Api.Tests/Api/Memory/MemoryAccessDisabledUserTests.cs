using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Memory;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Memory;

/// <summary>
/// Issue #1576 — a Dataverse-DISABLED user is nobody to the AI-memory governance routes. The production
/// <see cref="MemoryAccessAuthorizer"/> over the production <see cref="NotificationService"/> resolves the caller; only
/// the Dataverse read (<see cref="IGenericEntityService"/>) is substituted, by a fake that evaluates the query's
/// equality conditions against systemuser rows the way Dataverse would.
/// </summary>
public sealed class MemoryAccessDisabledUserTests
{
    private const string TenantId = "tenant-1576";
    private static readonly Guid ActiveOid = Guid.Parse("15760000-0000-0000-0000-0000000003a1");
    private static readonly Guid ActiveUser = Guid.Parse("15760000-0000-0000-0000-0000000003a2");
    private static readonly Guid DisabledOid = Guid.Parse("15760000-0000-0000-0000-0000000003d1");
    private static readonly Guid DisabledUser = Guid.Parse("15760000-0000-0000-0000-0000000003d2");

    [Fact]
    public async Task A_disabled_user_cannot_list_their_memory()
    {
        var store = new Mock<IMemoryItemStore>(MockBehavior.Strict);

        var result = await MemoryGovernanceEndpoints.ListUserMemoryAsync(
            Ctx(DisabledOid), store.Object, Authorizer(), NullLogger<MemoryListResponse>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_disabled_user_cannot_delete_a_memory_item()
    {
        var store = new Mock<IMemoryItemStore>(MockBehavior.Strict);

        var result = await MemoryGovernanceEndpoints.DeleteUserMemoryItemAsync(
            "item-1", Ctx(DisabledOid), store.Object, Authorizer(), Mock.Of<IAuditLogService>(),
            NullLogger<MemoryListResponse>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_authorizer_resolves_an_active_user_and_not_a_disabled_one()
    {
        var authorizer = Authorizer();

        (await authorizer.ResolveCallerUserSubjectAsync(Ctx(ActiveOid).User, default)).Should().Be(ActiveUser);
        (await authorizer.ResolveCallerUserSubjectAsync(Ctx(DisabledOid).User, default)).Should().BeNull(
            "a disabled systemuser keeps its oid, but must not resolve");
    }

    private static MemoryAccessAuthorizer Authorizer()
    {
        var rows = new[]
        {
            (Id: ActiveUser, Oid: ActiveOid, Disabled: false),
            (Id: DisabledUser, Oid: DisabledOid, Disabled: true),
        };

        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                query.EntityName.Should().Be("systemuser");
                var conditions = query.Criteria.Conditions.ToDictionary(c => c.AttributeName, c => c.Values.Single());
                var matches = rows.Where(r =>
                    string.Equals(conditions["azureactivedirectoryobjectid"]?.ToString(), r.Oid.ToString(), StringComparison.OrdinalIgnoreCase)
                    && (!conditions.TryGetValue("isdisabled", out var disabled) || Equals(disabled, r.Disabled)));
                return new EntityCollection(matches.Select(r => new Entity("systemuser", r.Id)).ToList());
            });

        return new MemoryAccessAuthorizer(new NotificationService(entities.Object, NullLogger<NotificationService>.Instance));
    }

    private static HttpContext Ctx(Guid oid)
        => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", oid.ToString("D")), new Claim("tid", TenantId) }, "test")),
        };
}
