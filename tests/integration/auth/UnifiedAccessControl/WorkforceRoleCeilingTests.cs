using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 186 (owner round 101, option (a)): the Workforce role "Spaarke Internal Contact User
/// (Workforce)" is the CEILING for a workforce-contact caller. Covers the three parts below the routes:
/// <list type="bullet">
/// <item>the privilege → table × operation map (<see cref="UserPrivilegeChecker.BuildWorkforceCeilingMap"/>);</item>
/// <item>the checker's read, its 5-minute cache and its fail-closed paths (a fault is never cached);</item>
/// <item>the gate: who it applies to and its two 403s (<see cref="WorkforceRoleCeilingGate"/>).</item>
/// </list>
/// The Dataverse read itself needs a live ServiceClient; it is replaced through the checker's test seam.
/// </summary>
[Trait("category", "external-access")]
public sealed class WorkforceRoleCeilingTests
{
    private const string ServiceRequest = "sprk_servicerequest";
    private const string Document = "sprk_document";

    // ── The map ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ServiceRequestPrivileges_MapToTheLogicalName_WithTheirOperations()
    {
        var map = UserPrivilegeChecker.BuildWorkforceCeilingMap(new[]
        {
            ("prvCreatesprk_ServiceRequest", 32),
            ("prvReadsprk_ServiceRequest", 1),
            ("prvAppendTosprk_ServiceRequest", 16),
            ("prvDeletesprk_ServiceRequest", 65536),
            ("prvReadsprk_Document", 1),
        });

        map[ServiceRequest].Should().Be(AccessRights.Create | AccessRights.Read | AccessRights.AppendTo | AccessRights.Delete);
        map[Document].Should().Be(AccessRights.Read);
    }

    [Fact]
    public void TheOperation_ComesFromAccessRight_NotFromTheNamePrefix()
    {
        // "prvAppendTool" is Append on a table named "tool", not AppendTo on "ol".
        var map = UserPrivilegeChecker.BuildWorkforceCeilingMap(new[] { ("prvAppendTool", 4), ("prvAppendToTool", 16) });

        map.Should().ContainSingle();
        map["tool"].Should().Be(AccessRights.Append | AccessRights.AppendTo);
    }

    [Fact]
    public void AnUnrecognizedPrivilege_GrantsNothing()
    {
        var map = UserPrivilegeChecker.BuildWorkforceCeilingMap(new[]
        {
            ("prvAssignsprk_ServiceRequest", 524288), // Assign has no AccessRights bit
            ("prvExportToExcel", 0),                   // a miscellaneous privilege
            ("prvRead", 1),                            // no table
            ("prvReadsprk_Document", 2),               // the name and the accessright disagree
            ("", 1),
        });

        map.Should().BeEmpty();
    }

    // ── Holds ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Holds_RequiresEveryRequestedOperation_AtAnyDepth_CaseInsensitively()
    {
        var ceiling = WorkforceRoleCeiling.Available(new Dictionary<string, AccessRights>
        {
            [ServiceRequest] = AccessRights.Read | AccessRights.Create,
        });

        ceiling.Holds(ServiceRequest, AccessRights.Read).Should().BeTrue();
        ceiling.Holds("SPRK_ServiceRequest", AccessRights.Read | AccessRights.Create).Should().BeTrue();
        ceiling.Holds(ServiceRequest, AccessRights.Read | AccessRights.Delete).Should().BeFalse("Delete is not held");
        ceiling.Holds(Document, AccessRights.Read).Should().BeFalse("the role holds nothing on sprk_document");
        ceiling.Holds(ServiceRequest, AccessRights.None).Should().BeFalse("a check that names no operation fails closed");
    }

    [Fact]
    public void AnUnavailableRole_HoldsNothing()
    {
        var ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        ceiling.IsAvailable.Should().BeFalse();
        ceiling.Holds(ServiceRequest, AccessRights.Read).Should().BeFalse();
    }

    // ── The checker: read, cache, fail closed ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARoleThatWasRead_IsCachedForFiveMinutes_AndTheNextCallDoesNotReadAgain()
    {
        var cache = new RecordingTenantCache();
        var reads = 0;
        var checker = Checker(cache, _ =>
        {
            reads++;
            return Task.FromResult(WorkforceRoleCeiling.Available(Map(ServiceRequest, AccessRights.Create)));
        });

        var first = await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);
        var second = await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);

        reads.Should().Be(1);
        cache.Sets.Should().ContainSingle().Which.Should().Be(
            (UserPrivilegeChecker.WorkforceCeilingCacheResource, TimeSpan.FromMinutes(5)),
            "the owner's role edits take effect within five minutes");
        UserPrivilegeChecker.WorkforceCeilingTtl.Should().Be(TimeSpan.FromMinutes(5));
        first.Holds(ServiceRequest, AccessRights.Create).Should().BeTrue();
        second.IsAvailable.Should().BeTrue();
        second.Holds(ServiceRequest, AccessRights.Create).Should().BeTrue("the cached copy survives the JSON round trip");
        second.Holds(ServiceRequest, AccessRights.Read).Should().BeFalse();
    }

    [Fact]
    public async Task OnceTheEntryExpires_TheOwnersEditIsRead()
    {
        var cache = new RecordingTenantCache();
        var current = Map(ServiceRequest, AccessRights.Create);
        var checker = Checker(cache, _ => Task.FromResult(WorkforceRoleCeiling.Available(current)));

        (await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None)).Holds(ServiceRequest, AccessRights.Create)
            .Should().BeTrue();

        current = Map(ServiceRequest, AccessRights.Read); // the owner removes Create
        cache.Expire();

        (await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None)).Holds(ServiceRequest, AccessRights.Create)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("role_missing")]
    [InlineData("role_ambiguous")]
    [InlineData("dataverse_client_unavailable")]
    public async Task AMissingAmbiguousOrUnreachableRole_DeniesEverything_AndIsNeverCached(string reason)
    {
        var cache = new RecordingTenantCache();
        var reads = 0;
        var checker = Checker(cache, _ =>
        {
            reads++;
            return Task.FromResult(WorkforceRoleCeiling.Unavailable(reason));
        });

        var first = await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);
        await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);

        first.IsAvailable.Should().BeFalse();
        first.UnavailableReason.Should().Be(reason);
        cache.Sets.Should().BeEmpty("a fault is never cached");
        reads.Should().Be(2, "the next request reads again");
    }

    [Fact]
    public async Task ARoleReadThatThrows_FailsClosed_AndIsNeverCached()
    {
        var cache = new RecordingTenantCache();
        var checker = Checker(cache, _ => throw new TimeoutException("Dataverse did not answer"));

        var ceiling = await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);

        ceiling.IsAvailable.Should().BeFalse("an unreadable role is never \"allow\"");
        ceiling.UnavailableReason.Should().Be("read_failed");
        ceiling.Holds(ServiceRequest, AccessRights.Read).Should().BeFalse();
        cache.Sets.Should().BeEmpty();
    }

    [Fact]
    public async Task ARoleReadThatAnswersNothing_FailsClosed_AndIsNeverCached()
    {
        // Review round 1 nit: a seam that answers null was dereferenced (a 500); it is a fault like any other.
        var cache = new RecordingTenantCache();
        var checker = Checker(cache, _ => Task.FromResult<WorkforceRoleCeiling>(null!));

        var ceiling = await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);

        ceiling.IsAvailable.Should().BeFalse();
        ceiling.UnavailableReason.Should().Be("read_failed");
        cache.Sets.Should().BeEmpty();
    }

    [Fact]
    public async Task ACacheReadFault_FallsThroughToTheRoleRead()
    {
        var cache = new RecordingTenantCache { FaultOnGet = true };
        var checker = Checker(cache, _ => Task.FromResult(WorkforceRoleCeiling.Available(Map(ServiceRequest, AccessRights.Read))));

        var ceiling = await checker.GetWorkforceRoleCeilingAsync(CancellationToken.None);

        ceiling.Holds(ServiceRequest, AccessRights.Read).Should().BeTrue();
    }

    // ── The gate ─────────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> CallersOutsideTheCeiling => new() { "ciam-contact", "workforce-systemuser" };

    [Theory]
    [MemberData(nameof(CallersOutsideTheCeiling))]
    public async Task SystemusersAndCiamPartners_AreNotChecked_AndTheRoleIsNotRead(string kind)
    {
        var privileges = new WorkforceCeilingDouble { Ceiling = WorkforceRoleCeiling.Unavailable("role_missing") };
        var principal = kind == "ciam-contact"
            ? new CallerPrincipal { Plane = CallerPrincipalPlane.CiamContact, ContactId = Guid.NewGuid(), ProjectAccess = NoProjects }
            : new CallerPrincipal { Plane = CallerPrincipalPlane.Workforce, ContactId = Guid.NewGuid(), SystemUserId = Guid.NewGuid(), ProjectAccess = NoProjects };

        var denied = await Gate(principal, privileges, WorkforceRoleCeilingGate.SubmitServiceRequest);

        denied.Should().BeNull();
        privileges.Reads.Should().Be(0);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleIsUnavailable_Is403_BaselineUnavailable()
    {
        var privileges = new WorkforceCeilingDouble { Ceiling = WorkforceRoleCeiling.Unavailable("read_failed") };

        var denied = await Gate(WorkforceContact(), privileges, WorkforceRoleCeilingGate.SubmitServiceRequest);

        ShouldBe403(denied, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
    }

    [Fact]
    public async Task ACheckerThatAnswersNothing_IsTreatedAsAnUnavailableRole()
    {
        var privileges = new Mock<IDataversePrivilegeChecker>();
        privileges.Setup(p => p.GetWorkforceRoleCeilingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkforceRoleCeiling)null!);

        var denied = await Gate(WorkforceContact(), privileges.Object, WorkforceRoleCeilingGate.SubmitServiceRequest);

        ShouldBe403(denied, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleLacksThePrivilege_Is403_PrivilegeMissing()
    {
        var privileges = new WorkforceCeilingDouble
        {
            Ceiling = WorkforceRoleCeiling.Available(Map(ServiceRequest, AccessRights.Read | AccessRights.AppendTo)),
        };

        var denied = await Gate(WorkforceContact(), privileges, WorkforceRoleCeilingGate.UploadServiceRequestDocument);

        ShouldBe403(denied, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleHoldsEveryPrivilege_Passes()
    {
        var privileges = new WorkforceCeilingDouble();

        foreach (var required in new[]
                 {
                     WorkforceRoleCeilingGate.SubmitServiceRequest, WorkforceRoleCeilingGate.ReadServiceRequests,
                     WorkforceRoleCeilingGate.ReadServiceRequestDocuments, WorkforceRoleCeilingGate.UploadServiceRequestDocument,
                     WorkforceRoleCeilingGate.DeleteServiceRequest,
                 })
        {
            (await Gate(WorkforceContact(), privileges, required)).Should().BeNull();
        }
    }

    [Fact]
    public async Task AWorkforcePrincipal_WithNoSystemuserAndNoContact_IsStillChecked()
    {
        var privileges = new WorkforceCeilingDouble { Ceiling = WorkforceRoleCeiling.Available(Map(Document, AccessRights.Read)) };
        var principal = new CallerPrincipal { Plane = CallerPrincipalPlane.Workforce, ContactId = Guid.Empty, ProjectAccess = NoProjects };

        var denied = await Gate(principal, privileges, WorkforceRoleCeilingGate.SubmitServiceRequest);

        ShouldBe403(denied, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
    }

    [Fact]
    public async Task AnEmptyRequirementSet_Denies()
    {
        var denied = await Gate(WorkforceContact(), new WorkforceCeilingDouble(), Array.Empty<WorkforceCeilingRequirement>());

        ShouldBe403(denied, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
    }

    [Fact]
    public void TheRouteMap_IsTheOwnersRound101Map()
    {
        WorkforceRoleCeilingGate.SubmitServiceRequest.Should().Equal(new WorkforceCeilingRequirement(ServiceRequest, AccessRights.Create));
        WorkforceRoleCeilingGate.ReadServiceRequests.Should().Equal(new WorkforceCeilingRequirement(ServiceRequest, AccessRights.Read));
        WorkforceRoleCeilingGate.ReadServiceRequestDocuments.Should().Equal(
            new WorkforceCeilingRequirement(ServiceRequest, AccessRights.Read), new WorkforceCeilingRequirement(Document, AccessRights.Read));
        WorkforceRoleCeilingGate.UploadServiceRequestDocument.Should().Equal(
            new WorkforceCeilingRequirement(Document, AccessRights.Create), new WorkforceCeilingRequirement(ServiceRequest, AccessRights.AppendTo));
        WorkforceRoleCeilingGate.DeleteServiceRequest.Should().Equal(new WorkforceCeilingRequirement(ServiceRequest, AccessRights.Delete));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static UserPrivilegeChecker Checker(ITenantCache cache, Func<CancellationToken, Task<WorkforceRoleCeiling>> read) =>
        new(Mock.Of<IDataverseService>(), cache, NullLogger<UserPrivilegeChecker>.Instance, httpContextAccessor: null, read);

    private static readonly IReadOnlyList<CallerProjectAccess> NoProjects = Array.Empty<CallerProjectAccess>();

    private static Dictionary<string, AccessRights> Map(string table, AccessRights rights) => new() { [table] = rights };

    private static CallerPrincipal WorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        ProjectAccess = NoProjects,
    };

    private static Task<IResult?> Gate(
        CallerPrincipal principal, IDataversePrivilegeChecker privileges, IReadOnlyList<WorkforceCeilingRequirement> required) =>
        WorkforceRoleCeilingGate.DenyAsync(principal, privileges, required, NullLogger.Instance, CancellationToken.None);

    private static void ShouldBe403(IResult? result, string reasonCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(reasonCode);
    }

    /// <summary>
    /// The real <see cref="TenantCache"/> over an in-memory keyspace (so a cached map goes through JSON exactly as it does
    /// in Redis), recording every write's resource and TTL.
    /// </summary>
    private sealed class RecordingTenantCache : ITenantCache
    {
        private InMemoryRedisKeyspace _keyspace = new();
        private ITenantCache _inner;

        public RecordingTenantCache() => _inner = _keyspace.TenantCache();

        public List<(string Resource, TimeSpan? Ttl)> Sets { get; } = new();

        public bool FaultOnGet { get; set; }

        /// <summary>Drops every entry, as the TTL would.</summary>
        public void Expire()
        {
            _keyspace = new InMemoryRedisKeyspace();
            _inner = _keyspace.TenantCache();
        }

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default) =>
            FaultOnGet
                ? throw new InvalidOperationException("simulated cache read fault")
                : _inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Sets.Add((resource, ttl));
            return _inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version,
            Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default) =>
            _inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);
    }
}
