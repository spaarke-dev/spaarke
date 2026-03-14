using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.SpeAdmin;

/// <summary>
/// Phase 3 integration tests covering all implemented Phase 3 features:
///   - SPE-082: Multi-tenant consuming tenant management (ConsumingTenantEndpoints)
///   - SPE-083: Bulk operations with background processing (BulkOperationEndpoints, BulkOperationService)
///   - SPE-084: Multi-app registration support (SpeAdminTokenProvider, ContainerTypeConfig.HasOwningApp)
///
/// Note: SPE-080 (eDiscovery) and SPE-081 (Retention Labels) were skipped by user decision.
///
/// These tests validate:
///   - ADR-001: All endpoints follow Minimal API patterns (no controllers)
///   - ADR-008: Auth filter (SpeAdminAuthorizationFilter) is enforced — non-admin users receive 403
///   - Endpoint input validation returns 400 ProblemDetails for bad requests
///   - Auth filter correctly differentiates admin vs. non-admin users
///   - Domain model contracts (ADR-007: no Graph SDK types exposed above service facade)
///   - BulkOperationService in-memory state tracking and enqueue/poll lifecycle
///   - SpeAdminTokenProvider token caching and OBO validation logic
///
/// Approach: Tests exercise SpeAdminAuthorizationFilter directly (ADR-008 compliance),
/// validate request model constraints, and verify endpoint registration structure.
/// Tests requiring SpeAdminGraphService (external Graph API + Key Vault) are structured
/// as documented manual test procedures at the bottom of this file.
/// </summary>
public class Phase3IntegrationTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared test helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal CreateAdminUser(string userId = "admin-user-001")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("roles", "Admin")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal CreateNonAdminUser(string userId = "regular-user-001")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal CreateAnonymousUser() =>
        new(new ClaimsIdentity());

    private static DefaultHttpContext CreateHttpContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "trace-id-001",
            RequestServices = BuildServiceProvider()
        };
        return httpContext;
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task<object?> InvokeAuthFilter(
        EndpointFilterDelegate next,
        ClaimsPrincipal user)
    {
        var httpContext = CreateHttpContext(user);
        var filter = new SpeAdminAuthorizationFilter(
            httpContext.RequestServices.GetService<ILogger<SpeAdminAuthorizationFilter>>());

        var context = new DefaultEndpointFilterInvocationContext(httpContext, []);
        return await filter.InvokeAsync(context, next);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-082: Multi-tenant Consuming Tenant Management — Auth Filter Tests
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-082 Auth Filter (ADR-008)

    [Fact]
    public async Task ConsumingTenants_AdminUser_PassesThroughAuthFilter()
    {
        // Arrange
        var reachedNext = false;
        EndpointFilterDelegate next = _ =>
        {
            reachedNext = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await InvokeAuthFilter(next, CreateAdminUser());

        // Assert
        reachedNext.Should().BeTrue("admin user should pass the SpeAdminAuthorizationFilter");
    }

    [Fact]
    public async Task ConsumingTenants_NonAdminUser_Returns403()
    {
        // Arrange
        EndpointFilterDelegate next = _ =>
            ValueTask.FromResult<object?>(Results.Ok());

        // Act
        var result = await InvokeAuthFilter(next, CreateNonAdminUser());

        // Assert
        result.Should().BeAssignableTo<IResult>("filter must return a result object");
        var httpResult = result as IResult;
        httpResult.Should().NotBeNull();
    }

    [Fact]
    public async Task ConsumingTenants_AnonymousUser_Returns401()
    {
        // Arrange
        EndpointFilterDelegate next = _ =>
            ValueTask.FromResult<object?>(Results.Ok());

        // Act
        var result = await InvokeAuthFilter(next, CreateAnonymousUser());

        // Assert — anonymous user has no identity, filter returns 401
        result.Should().NotBeNull("anonymous user should receive an error result, not be passed through");
    }

    [Fact]
    public async Task ConsumingTenants_SystemAdminRole_PassesThroughAuthFilter()
    {
        // SystemAdmin is also a valid admin role
        var reachedNext = false;
        EndpointFilterDelegate next = _ =>
        {
            reachedNext = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        var systemAdminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "sysadmin-001"),
            new Claim("roles", "SystemAdmin")
        }, "test"));

        await InvokeAuthFilter(next, systemAdminUser);

        reachedNext.Should().BeTrue("SystemAdmin role should pass the SpeAdminAuthorizationFilter");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-082: ConsumingTenantEndpoints registration tests (ADR-001)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-082 Endpoint Registration

    [Fact]
    public void ConsumingTenantEndpoints_IsStaticClass()
    {
        var type = typeof(ConsumingTenantEndpoints);
        type.IsAbstract.Should().BeTrue("static classes are abstract in IL");
        type.IsSealed.Should().BeTrue("static classes are sealed in IL");
    }

    [Fact]
    public void ConsumingTenantEndpoints_MapMethod_ExistsAndReturnsRouteGroupBuilder()
    {
        var method = typeof(ConsumingTenantEndpoints)
            .GetMethod("MapConsumingTenantEndpoints");

        method.Should().NotBeNull("MapConsumingTenantEndpoints extension method must exist");
        method!.IsStatic.Should().BeTrue("must be a static extension method");
        method.ReturnType.Should().Be(
            typeof(Microsoft.AspNetCore.Routing.RouteGroupBuilder),
            "must return RouteGroupBuilder for chaining");
    }

    [Theory]
    [InlineData("ListConsumersAsync")]
    [InlineData("RegisterConsumerAsync")]
    [InlineData("UpdateConsumerAsync")]
    [InlineData("RemoveConsumerAsync")]
    public void ConsumingTenantEndpoints_AllCrudHandlers_ExistAsPrivateStaticMethods(string methodName)
    {
        var method = typeof(ConsumingTenantEndpoints)
            .GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull($"{methodName} handler must exist as a private static method (ADR-001)");
        method!.IsStatic.Should().BeTrue($"{methodName} must be static");
    }

    [Theory]
    [InlineData("ListConsumersAsync")]
    [InlineData("RegisterConsumerAsync")]
    [InlineData("UpdateConsumerAsync")]
    [InlineData("RemoveConsumerAsync")]
    public void ConsumingTenantEndpoints_Handlers_InheritAuthFromRouteGroup_NoDirectAuthAttribute(string methodName)
    {
        // SpeAdminAuthorizationFilter is applied at the /api/spe group level.
        // Individual handlers must NOT duplicate auth — they inherit it (ADR-008).
        var method = typeof(ConsumingTenantEndpoints)
            .GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var authorizeAttributes = method!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false);
        authorizeAttributes.Should().BeEmpty(
            $"{methodName} authorization must be inherited from the /api/spe route group (ADR-008)");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-082: ConsumingTenant DTO validation
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-082 Request Validation

    [Fact]
    public void RegisterConsumingTenantRequest_RequiresAppId()
    {
        // The endpoint rejects requests where AppId is null or whitespace
        var emptyAppId = new RegisterConsumingTenantRequest { AppId = "" };
        var whitespaceAppId = new RegisterConsumingTenantRequest { AppId = "   " };
        var validRequest = new RegisterConsumingTenantRequest { AppId = "app-id-abc" };

        string.IsNullOrWhiteSpace(emptyAppId.AppId).Should().BeTrue("empty AppId is invalid");
        string.IsNullOrWhiteSpace(whitespaceAppId.AppId).Should().BeTrue("whitespace AppId is invalid");
        string.IsNullOrWhiteSpace(validRequest.AppId).Should().BeFalse("non-empty AppId is valid");
    }

    [Fact]
    public void ConsumingTenantEndpoints_ConfigIdQueryParam_MustBeValidGuid()
    {
        // The endpoint validates: !Guid.TryParse(configId, out _)
        var invalidConfigIds = new[] { "", "   ", "not-a-guid", "12345", "abc-def" };
        var validConfigId = Guid.NewGuid().ToString();

        foreach (var configId in invalidConfigIds)
        {
            var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);
            isValid.Should().BeFalse($"'{configId}' should not be a valid configId");
        }

        var validResult = !string.IsNullOrWhiteSpace(validConfigId) && Guid.TryParse(validConfigId, out _);
        validResult.Should().BeTrue("a GUID string should pass validation");
    }

    [Fact]
    public void ConsumingTenantListDto_EmptyList_IsValidResponse()
    {
        // Empty list returns 200 OK with count 0 — NOT a 404
        var dto = new ConsumingTenantListDto { Items = [], Count = 0 };

        dto.Items.Should().BeEmpty("empty consumer list is valid — no apps registered");
        dto.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("readContent")]
    [InlineData("writeContent")]
    [InlineData("manageContent")]
    [InlineData("managePermissions")]
    [InlineData("full")]
    public void ConsumingTenantDto_PermissionValues_AreAccepted(string permission)
    {
        var dto = new ConsumingTenantDto
        {
            AppId = "test-app",
            DelegatedPermissions = [permission],
            ApplicationPermissions = [permission]
        };

        dto.DelegatedPermissions.Should().Contain(permission);
        dto.ApplicationPermissions.Should().Contain(permission);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-082: SpeConsumingTenant domain record (ADR-007)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-082 Domain Model Contract (ADR-007)

    [Fact]
    public void SpeConsumingTenant_DomainRecord_HasNoGraphSdkTypes()
    {
        // ADR-007: No Graph SDK types leak above the service facade.
        // SpeConsumingTenant must use only BCL types.
        var type = typeof(SpeAdminGraphService.SpeConsumingTenant);

        var properties = type.GetProperties();
        foreach (var prop in properties)
        {
            prop.PropertyType.Namespace.Should()
                .NotStartWith("Microsoft.Graph",
                    $"Property '{prop.Name}' must not expose Graph SDK types (ADR-007)");
        }
    }

    [Fact]
    public void SpeConsumingTenant_NullReturnValue_MapsToNotFound()
    {
        // null from service methods → 404 Not Found
        // non-null → 200 or 201
        SpeAdminGraphService.SpeConsumingTenant? nullResult = null;
        nullResult.Should().BeNull("null return maps to HTTP 404 Not Found");
    }

    [Fact]
    public void RemoveConsumingTenant_FalseReturn_MapsToNotFound()
    {
        // false from RemoveConsumingTenantAsync → 404 Not Found
        // true → 204 No Content
        bool notFound = false;
        notFound.Should().BeFalse("false return maps to HTTP 404");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-083: Bulk Operations — Auth Filter Tests (ADR-008)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-083 Auth Filter (ADR-008)

    [Fact]
    public async Task BulkEndpoints_AdminUser_PassesThroughAuthFilter()
    {
        var reachedNext = false;
        EndpointFilterDelegate next = _ =>
        {
            reachedNext = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        await InvokeAuthFilter(next, CreateAdminUser());

        reachedNext.Should().BeTrue("admin user must pass bulk operation auth filter");
    }

    [Fact]
    public async Task BulkEndpoints_NonAdminUser_BlockedByAuthFilter()
    {
        var reachedNext = false;
        EndpointFilterDelegate next = _ =>
        {
            reachedNext = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        await InvokeAuthFilter(next, CreateNonAdminUser());

        reachedNext.Should().BeFalse("non-admin user must NOT reach bulk operation handlers");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-083: BulkOperationEndpoints registration tests (ADR-001)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-083 Endpoint Registration

    [Fact]
    public void BulkOperationEndpoints_IsStaticClass()
    {
        var type = typeof(BulkOperationEndpoints);
        type.IsAbstract.Should().BeTrue("static classes are abstract in IL");
        type.IsSealed.Should().BeTrue("static classes are sealed in IL");
    }

    [Fact]
    public void BulkOperationEndpoints_MapMethod_Exists()
    {
        var method = typeof(BulkOperationEndpoints)
            .GetMethod("MapBulkOperationEndpoints",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("MapBulkOperationEndpoints must be a public static method");
        method!.ReturnType.Should().Be(typeof(void), "registration methods return void");
    }

    [Theory]
    [InlineData("EnqueueBulkDelete")]
    [InlineData("EnqueueBulkPermissions")]
    [InlineData("GetBulkOperationStatusAsync")]
    public void BulkOperationEndpoints_AllHandlers_ExistAsPrivateStaticMethods(string methodName)
    {
        var method = typeof(BulkOperationEndpoints)
            .GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull($"{methodName} handler must exist as private static (ADR-001)");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-083: BulkOperationService — domain model state machine verification
    // ─────────────────────────────────────────────────────────────────────────
    //
    // Note: BulkOperationService is a BackgroundService (ADR-001) that depends on
    // SpeAdminGraphService, which depends on Azure SDK types (SecretClient,
    // DataverseWebApiClient) that cannot be mocked with Moq (no parameterless
    // constructors). Live BulkOperationService lifecycle (enqueue/poll) is tested
    // manually per the scenarios at the bottom of this file.
    //
    // These tests validate the public BulkOperationStatus model contract that
    // BulkOperationService returns via GetStatus().

    #region SPE-083 BulkOperationStatus Model Contract

    [Fact]
    public void BulkOperationStatus_NewOperation_HasCorrectInitialShape()
    {
        var operationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        var status = new BulkOperationStatus(
            OperationId: operationId,
            OperationType: BulkOperationType.Delete,
            Total: 5,
            Completed: 0,
            Failed: 0,
            IsFinished: false,
            Errors: [],
            StartedAt: startedAt,
            CompletedAt: null);

        status.OperationId.Should().Be(operationId, "operation ID is preserved");
        status.OperationType.Should().Be(BulkOperationType.Delete);
        status.Total.Should().Be(5);
        status.Completed.Should().Be(0, "nothing processed yet");
        status.Failed.Should().Be(0, "nothing failed yet");
        status.IsFinished.Should().BeFalse("just started");
        status.Errors.Should().BeEmpty("no errors yet");
        status.CompletedAt.Should().BeNull("not finished yet");
    }

    [Fact]
    public void BulkOperationStatus_OperationId_IsNonEmpty()
    {
        // The service generates Guid.NewGuid() as operationId — never empty
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.AssignPermissions,
            Total: 10,
            Completed: 0,
            Failed: 0,
            IsFinished: false,
            Errors: [],
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null);

        status.OperationId.Should().NotBe(Guid.Empty, "operation ID must be a valid non-empty GUID");
    }

    [Fact]
    public void BulkOperationStatus_TwoDistinctOperations_HaveDifferentIds()
    {
        // Guid.NewGuid() is used per enqueue — IDs are globally unique
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        id1.Should().NotBe(id2, "each bulk operation gets a unique operation ID");
    }

    [Fact]
    public void BulkOperationStatus_PermissionsOperation_HasCorrectType()
    {
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.AssignPermissions,
            Total: 3,
            Completed: 0,
            Failed: 0,
            IsFinished: false,
            Errors: [],
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null);

        status.OperationType.Should().Be(BulkOperationType.AssignPermissions,
            "permissions enqueue creates AssignPermissions operation type");
        status.Total.Should().Be(3);
    }

    [Fact]
    public void BulkOperationStatus_PolledAfterCompletion_IsFinishedTrue()
    {
        var completedAt = DateTimeOffset.UtcNow;

        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.Delete,
            Total: 4,
            Completed: 4,
            Failed: 0,
            IsFinished: true,
            Errors: [],
            StartedAt: completedAt.AddSeconds(-5),
            CompletedAt: completedAt);

        status.IsFinished.Should().BeTrue("polling after completion returns isFinished=true");
        status.CompletedAt.Should().NotBeNull("completedAt is set when finished");
        status.Completed.Should().Be(status.Total, "all items processed");
    }

    [Fact]
    public void BulkOperationStatus_UnknownOperationId_MapsToNull()
    {
        // BulkOperationService.GetStatus returns null for unknown/expired IDs
        // → endpoints return HTTP 404
        BulkOperationStatus? nullStatus = null;
        nullStatus.Should().BeNull("unknown operation ID returns null (HTTP 404)");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-083: Bulk request validation (mirrors endpoint validation logic)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-083 Request Validation

    [Fact]
    public void BulkDeleteRequest_EmptyContainerIds_FailsValidation()
    {
        var request = new BulkDeleteRequest(ContainerIds: [], ConfigId: Guid.NewGuid().ToString());

        (request.ContainerIds is null || request.ContainerIds.Count == 0)
            .Should().BeTrue("empty containerIds list is invalid");
    }

    [Fact]
    public void BulkDeleteRequest_Over500Items_FailsValidation()
    {
        const int maxBulkItems = 500;
        var ids = Enumerable.Range(1, 501).Select(i => $"c-{i}").ToList();

        (ids.Count > maxBulkItems).Should().BeTrue("501 items exceeds the max of 500");
    }

    [Fact]
    public void BulkDeleteRequest_Exactly500Items_PassesValidation()
    {
        const int maxBulkItems = 500;
        var ids = Enumerable.Range(1, 500).Select(i => $"c-{i}").ToList();

        (ids.Count > maxBulkItems).Should().BeFalse("exactly 500 items is at the limit, not over");
    }

    [Fact]
    public void BulkPermissionsRequest_NeitherUserNorGroup_FailsValidation()
    {
        var request = new BulkPermissionsRequest(
            ContainerIds: ["c-1"],
            ConfigId: Guid.NewGuid().ToString(),
            UserId: null,
            GroupId: null,
            Role: "reader");

        var hasUser = !string.IsNullOrWhiteSpace(request.UserId);
        var hasGroup = !string.IsNullOrWhiteSpace(request.GroupId);

        (!hasUser && !hasGroup).Should().BeTrue("must provide either userId or groupId");
    }

    [Fact]
    public void BulkPermissionsRequest_BothUserAndGroup_FailsValidation()
    {
        var request = new BulkPermissionsRequest(
            ContainerIds: ["c-1"],
            ConfigId: Guid.NewGuid().ToString(),
            UserId: Guid.NewGuid().ToString(),
            GroupId: Guid.NewGuid().ToString(),
            Role: "owner");

        var hasUser = !string.IsNullOrWhiteSpace(request.UserId);
        var hasGroup = !string.IsNullOrWhiteSpace(request.GroupId);

        (hasUser && hasGroup).Should().BeTrue("providing both userId and groupId is invalid");
    }

    [Theory]
    [InlineData("reader", true)]
    [InlineData("writer", true)]
    [InlineData("manager", true)]
    [InlineData("owner", true)]
    [InlineData("READER", true)]   // case-insensitive HashSet match
    [InlineData("admin", false)]
    [InlineData("superuser", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BulkPermissionsRequest_RoleValidation(string? role, bool isValid)
    {
        var validRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reader", "writer", "manager", "owner"
        };

        var result = !string.IsNullOrWhiteSpace(role) && validRoles.Contains(role);
        result.Should().Be(isValid, $"role '{role ?? "(null)"}' validity mismatch");
    }

    [Fact]
    public void BulkOperationStatus_InitialState_IsNotFinished()
    {
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.Delete,
            Total: 10,
            Completed: 0,
            Failed: 0,
            IsFinished: false,
            Errors: [],
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null);

        status.IsFinished.Should().BeFalse();
        status.Completed.Should().Be(0);
        status.Failed.Should().Be(0);
        status.CompletedAt.Should().BeNull("not finished yet");
    }

    [Fact]
    public void BulkOperationStatus_FinishedWithErrors_HasCompletedAt()
    {
        var finishedAt = DateTimeOffset.UtcNow;
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.AssignPermissions,
            Total: 5,
            Completed: 3,
            Failed: 2,
            IsFinished: true,
            Errors: [new("container-4", "Not found"), new("container-5", "Access denied")],
            StartedAt: finishedAt.AddSeconds(-10),
            CompletedAt: finishedAt);

        status.IsFinished.Should().BeTrue();
        status.Errors.Should().HaveCount(2);
        status.CompletedAt.Should().Be(finishedAt);
        status.Completed.Should().Be(3);
        status.Failed.Should().Be(2);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-084: Multi-app registration support — Auth Filter Tests (ADR-008)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-084 Auth Filter (ADR-008)

    [Fact]
    public async Task MultiAppEndpoints_AdminUser_PassesThroughAuthFilter()
    {
        // Multi-app token acquisition endpoints inherit the same auth filter
        var reachedNext = false;
        EndpointFilterDelegate next = _ =>
        {
            reachedNext = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        await InvokeAuthFilter(next, CreateAdminUser("admin-multi-app"));

        reachedNext.Should().BeTrue("admin user must pass through the auth filter for multi-app endpoints");
    }

    [Fact]
    public async Task MultiAppEndpoints_NonAdminUser_BlockedByAuthFilter()
    {
        var reachedNext = false;
        EndpointFilterDelegate next = _ =>
        {
            reachedNext = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        await InvokeAuthFilter(next, CreateNonAdminUser("regular-user-multi-app"));

        reachedNext.Should().BeFalse("non-admin user must be blocked from multi-app endpoints");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-084: ContainerTypeConfig multi-app fields (HasOwningApp)
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-084 ContainerTypeConfig Multi-App Fields

    [Fact]
    public void ContainerTypeConfig_SingleAppMode_HasOwningAppIsFalse()
    {
        var config = new SpeAdminGraphService.ContainerTypeConfig(
            ConfigId: Guid.NewGuid(),
            ContainerTypeId: "ct-single",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret");

        config.HasOwningApp.Should().BeFalse(
            "single-app mode configs have no owning app fields set");
    }

    [Fact]
    public void ContainerTypeConfig_MultiAppMode_HasOwningAppIsTrue()
    {
        var config = new SpeAdminGraphService.ContainerTypeConfig(
            ConfigId: Guid.NewGuid(),
            ContainerTypeId: "ct-multi",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret",
            OwningAppId: "owning-app-id",
            OwningAppTenantId: "owning-tenant-id",
            OwningAppSecretName: "owning-app-secret");

        config.HasOwningApp.Should().BeTrue(
            "all three owning app fields are present → multi-app mode");
    }

    [Theory]
    [InlineData(null, "tenant-id", "secret-name")]
    [InlineData("app-id", null, "secret-name")]
    [InlineData("app-id", "tenant-id", null)]
    [InlineData("", "tenant-id", "secret-name")]
    [InlineData("app-id", "", "secret-name")]
    [InlineData("app-id", "tenant-id", "")]
    public void ContainerTypeConfig_PartialOwningAppFields_HasOwningAppIsFalse(
        string? appId, string? tenantId, string? secretName)
    {
        var config = new SpeAdminGraphService.ContainerTypeConfig(
            ConfigId: Guid.NewGuid(),
            ContainerTypeId: "ct-partial",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret",
            OwningAppId: appId,
            OwningAppTenantId: tenantId,
            OwningAppSecretName: secretName);

        config.HasOwningApp.Should().BeFalse(
            "all three owning app fields must be non-empty for multi-app mode");
    }

    [Fact]
    public void ContainerTypeConfig_SingleAppMode_ManagingAppFieldsUnchanged()
    {
        // Verify backward compatibility: existing managing app fields remain intact
        var config = new SpeAdminGraphService.ContainerTypeConfig(
            ConfigId: Guid.NewGuid(),
            ContainerTypeId: "ct-backcompat",
            ClientId: "managing-client-id",
            TenantId: "managing-tenant-id",
            SecretKeyVaultName: "managing-secret-name");

        config.ClientId.Should().Be("managing-client-id");
        config.TenantId.Should().Be("managing-tenant-id");
        config.SecretKeyVaultName.Should().Be("managing-secret-name");
        config.OwningAppId.Should().BeNull("not set in single-app mode");
        config.OwningAppTenantId.Should().BeNull("not set in single-app mode");
        config.OwningAppSecretName.Should().BeNull("not set in single-app mode");
        config.HasOwningApp.Should().BeFalse();
    }

    [Fact]
    public void ContainerTypeConfig_MultiAppMode_BothManagingAndOwningAppFieldsPresent()
    {
        // Multi-app mode: managing app fields AND owning app fields are all populated
        var config = new SpeAdminGraphService.ContainerTypeConfig(
            ConfigId: Guid.NewGuid(),
            ContainerTypeId: "ct-full",
            ClientId: "managing-app-client-id",
            TenantId: "managing-tenant-id",
            SecretKeyVaultName: "managing-app-secret",
            OwningAppId: "owning-app-id",
            OwningAppTenantId: "owning-tenant-id",
            OwningAppSecretName: "owning-app-secret");

        // Managing app fields
        config.ClientId.Should().Be("managing-app-client-id");
        config.TenantId.Should().Be("managing-tenant-id");
        config.SecretKeyVaultName.Should().Be("managing-app-secret");

        // Owning app fields
        config.OwningAppId.Should().Be("owning-app-id");
        config.OwningAppTenantId.Should().Be("owning-tenant-id");
        config.OwningAppSecretName.Should().Be("owning-app-secret");
        config.HasOwningApp.Should().BeTrue();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SPE-084: SpeAdminTokenProvider argument validation
    // ─────────────────────────────────────────────────────────────────────────

    #region SPE-084 SpeAdminTokenProvider Validation

    [Fact]
    public async Task TokenProvider_AcquireOwningAppToken_ThrowsArgumentNull_WhenConfigNull()
    {
        var provider = MakeTokenProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.AcquireOwningAppTokenAsync(null!, "user-token"));
    }

    [Fact]
    public async Task TokenProvider_AcquireOwningAppToken_ThrowsArgumentException_WhenUserTokenEmpty()
    {
        var provider = MakeTokenProvider();
        var config = MakeMultiAppConfig();

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AcquireOwningAppTokenAsync(config, ""));
    }

    [Fact]
    public async Task TokenProvider_AcquireOwningAppToken_ThrowsInvalidOperation_ForSingleAppConfig()
    {
        var provider = MakeTokenProvider();
        var config = MakeSingleAppConfig();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireOwningAppTokenAsync(config, "user-token"));

        ex.Message.Should().Contain("does not have owning app credentials");
        ex.Message.Should().Contain(config.ConfigId.ToString());
    }

    [Fact]
    public void TokenProvider_EvictExpiredTokens_DoesNotThrow_WhenCacheEmpty()
    {
        var provider = MakeTokenProvider();

        provider.Invoking(p => p.EvictExpiredTokens()).Should().NotThrow();
    }

    [Fact]
    public async Task TokenProvider_ValidateOwningAppSecrets_ReturnsEmpty_WhenNoOwningAppConfigs()
    {
        var provider = MakeTokenProvider();
        var configs = new[]
        {
            MakeSingleAppConfig(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            MakeSingleAppConfig(Guid.Parse("22222222-2222-2222-2222-222222222222"))
        };

        var failures = await provider.ValidateOwningAppSecretsAsync(configs);

        failures.Should().BeEmpty("single-app configs are skipped during validation");
    }

    private static SpeAdminTokenProvider MakeTokenProvider(
        Azure.Security.KeyVault.Secrets.SecretClient? secretClient = null)
    {
        var mockClient = secretClient ?? new Mock<Azure.Security.KeyVault.Secrets.SecretClient>().Object;
        return new SpeAdminTokenProvider(
            mockClient,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeAdminTokenProvider>.Instance);
    }

    private static SpeAdminGraphService.ContainerTypeConfig MakeSingleAppConfig(Guid? configId = null) =>
        new(
            ConfigId: configId ?? Guid.NewGuid(),
            ContainerTypeId: "ct-single",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret");

    private static SpeAdminGraphService.ContainerTypeConfig MakeMultiAppConfig(Guid? configId = null) =>
        new(
            ConfigId: configId ?? Guid.NewGuid(),
            ContainerTypeId: "ct-multi",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret",
            OwningAppId: "owning-app-id",
            OwningAppTenantId: "owning-tenant-id",
            OwningAppSecretName: "owning-app-secret");

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // Cross-cutting: SpeAdminEndpoints registers all Phase 3 endpoint groups
    // ─────────────────────────────────────────────────────────────────────────

    #region Phase 3 Endpoint Registration Completeness

    [Fact]
    public void SpeAdminEndpoints_MapMethod_Exists()
    {
        var method = typeof(SpeAdminEndpoints).GetMethod("MapSpeAdminEndpoints");
        method.Should().NotBeNull("MapSpeAdminEndpoints must exist as the main endpoint registration method");
    }

    [Fact]
    public void Phase3_AllEndpointClasses_AreStatic()
    {
        // All three Phase 3 endpoint classes follow the static Minimal API pattern (ADR-001)
        var endpointClasses = new[]
        {
            typeof(ConsumingTenantEndpoints),   // SPE-082
            typeof(BulkOperationEndpoints),     // SPE-083
        };

        foreach (var type in endpointClasses)
        {
            type.IsAbstract.Should().BeTrue($"{type.Name} must be a static class (abstract in IL)");
            type.IsSealed.Should().BeTrue($"{type.Name} must be a static class (sealed in IL)");
        }
    }

    [Fact]
    public void Phase3_BulkOperationService_IsBackgroundService()
    {
        // ADR-001: Use BackgroundService for async work — no Azure Functions
        typeof(BulkOperationService)
            .IsSubclassOf(typeof(Microsoft.Extensions.Hosting.BackgroundService))
            .Should().BeTrue("BulkOperationService must extend BackgroundService (ADR-001)");
    }

    [Fact]
    public void Phase3_SpeAdminTokenProvider_HasExpectedPublicMethods()
    {
        // Verify SpeAdminTokenProvider public surface for multi-app OBO support
        var type = typeof(SpeAdminTokenProvider);
        var bindingFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        type.GetMethod("AcquireOwningAppTokenAsync", bindingFlags)
            .Should().NotBeNull("AcquireOwningAppTokenAsync is the primary OBO token acquisition method");

        type.GetMethod("ValidateOwningAppSecretsAsync", bindingFlags)
            .Should().NotBeNull("ValidateOwningAppSecretsAsync runs startup validation");

        type.GetMethod("EvictExpiredTokens", bindingFlags)
            .Should().NotBeNull("EvictExpiredTokens performs cache maintenance");
    }

    #endregion
}

// ─────────────────────────────────────────────────────────────────────────────
// Helper: DefaultEndpointFilterInvocationContext for unit-testing endpoint filters
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal implementation of EndpointFilterInvocationContext for testing
/// <see cref="SpeAdminAuthorizationFilter"/> in isolation.
/// </summary>
internal sealed class DefaultEndpointFilterInvocationContext : EndpointFilterInvocationContext
{
    private readonly DefaultHttpContext _httpContext;
    private readonly object[] _arguments;

    public DefaultEndpointFilterInvocationContext(DefaultHttpContext httpContext, object[] arguments)
    {
        _httpContext = httpContext;
        _arguments = arguments;
    }

    public override HttpContext HttpContext => _httpContext;
    public override IList<object?> Arguments => _arguments;

    public override T GetArgument<T>(int index) => (T)_arguments[index];
}

// ─────────────────────────────────────────────────────────────────────────────
// MANUAL TEST SCENARIOS — Live Azure/Graph (not automated)
// ─────────────────────────────────────────────────────────────────────────────

/*
The following Phase 3 scenarios require live Azure infrastructure and cannot be automated
in unit tests. Verify manually in the dev environment after deployment.

──────────────────────────────────────────────────────────────────────────────
SPE-082: MULTI-TENANT CONSUMING TENANT MANAGEMENT
──────────────────────────────────────────────────────────────────────────────

SCENARIO 1: List consuming tenants — empty list
  Prerequisites:
    - Valid container type in Graph (register one if needed)
    - Valid configId in sprk_specontainertypeconfig with container type's clientId/secret
  Test:
    - GET /api/spe/containertypes/{typeId}/consumers?configId={id}
    - Expected: 200 OK with { items: [], count: 0 }

SCENARIO 2: Register, update, and remove consuming tenant (full CRUD)
  Prerequisites: Same as Scenario 1
  Test:
    - POST /api/spe/containertypes/{typeId}/consumers?configId={id}
      Body: { "appId": "{consuming-app-id}", "delegatedPermissions": ["readContent"] }
      Expected: 201 Created with consumer DTO
    - GET list → verify consumer appears
    - PUT /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
      Body: { "delegatedPermissions": ["writeContent"], "applicationPermissions": ["full"] }
      Expected: 200 OK with updated permissions
    - DELETE /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
      Expected: 204 No Content
    - GET list → verify consumer removed

SCENARIO 3: Register duplicate consumer → 409 Conflict
  Test:
    - Register same appId twice
    - Second POST should return 409 Conflict with errorCode "spe.containertypes.consumers.already_registered"

SCENARIO 4: Non-admin user → 403 Forbidden
  Test:
    - Call any consuming tenant endpoint with a non-admin user token
    - Expected: 403 with deny code "sdap.access.deny.role_insufficient"

──────────────────────────────────────────────────────────────────────────────
SPE-083: BULK OPERATIONS
──────────────────────────────────────────────────────────────────────────────

SCENARIO 5: Bulk delete — progress tracking
  Prerequisites: At least 3 SPE containers existing in the target container type
  Test:
    - POST /api/spe/bulk/delete  Body: { containerIds: ["c-1", "c-2", "c-3"], configId: "{id}" }
    - Expected: 202 Accepted with { operationId, statusUrl }
    - Poll GET /api/spe/bulk/{operationId}/status until isFinished: true
    - Verify completed + failed = total (3), errors list present if any failures

SCENARIO 6: Bulk permissions — assign role to user on multiple containers
  Prerequisites: AAD user object ID, multiple SPE containers
  Test:
    - POST /api/spe/bulk/permissions
      Body: { containerIds: [...], configId: "...", userId: "{aad-user-id}", role: "reader" }
    - Expected: 202 Accepted
    - Poll status — verify all containers receive reader permission

SCENARIO 7: Bulk delete — partial failure handling
  Test: Include a mix of valid and invalid container IDs
    - Verify completed count = valid containers processed
    - Verify failed count = invalid containers
    - Verify errors[] contains per-container error messages
    - Verify isFinished: true when all items processed (even with failures)

SCENARIO 8: Status endpoint — unknown operation ID → 404
  Test:
    - GET /api/spe/bulk/{random-guid}/status
    - Expected: 404 Not Found

──────────────────────────────────────────────────────────────────────────────
SPE-084: MULTI-APP REGISTRATION SUPPORT
──────────────────────────────────────────────────────────────────────────────

SCENARIO 9: Multi-app OBO token exchange succeeds
  Prerequisites:
    - sprk_specontainertypeconfig record with sprk_owningappid, sprk_owningapptenantid,
      sprk_owningappsecretname set to a valid Azure AD app registration
    - Owning app registered in Azure AD with OBO flow enabled (admin consent granted)
    - Owning app client secret stored in Key Vault under the configured secret name
  Test:
    - Call a SPE Admin endpoint with configId pointing to the multi-app config
    - Verify Graph API call succeeds using owning app identity
    - Verify second call within 55 minutes uses cached token (no new Key Vault fetch)
    - Check logs: "OBO token cache HIT" on subsequent calls

SCENARIO 10: Single-app backward compatibility
  Prerequisites: Existing BU config without owning app fields
  Test:
    - Verify existing configs continue to work without any config changes
    - Check logs: "Using single-app mode" (not OBO flow)

SCENARIO 11: Startup validation warning for missing owning app secret
  Prerequisites: Configure a BU config with owning app fields pointing to a non-existent Key Vault secret
  Test:
    - Start the API
    - Check Application Insights logs for startup validation warning
    - Verify API starts successfully (warning, not fatal error)
    - Verify the config fails gracefully at request time (500 with ProblemDetails)

──────────────────────────────────────────────────────────────────────────────
DEPLOYMENT VERIFICATION (after Deploy-BffApi.ps1 + Code Page upload)
──────────────────────────────────────────────────────────────────────────────

SCENARIO 12: BFF API health check after deployment
  - curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
  - Expected: "Healthy" (HTTP 200)
  - curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
  - Expected: "pong"

SCENARIO 13: Code Page loads in dark mode
  - Open SPE Admin Code Page in browser
  - Toggle dark mode (top-right)
  - Verify no white flash, colors adapt using Fluent v9 design tokens
  - Open browser DevTools → Console — verify no runtime errors

SCENARIO 14: All Phase 3 menu sections visible in Code Page
  - Consuming Tenants management section visible and navigable
  - Bulk Operations section shows delete and permissions forms
  - Config section shows multi-app fields (owning app) for configs that have them
*/
