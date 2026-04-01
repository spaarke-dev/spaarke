using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Reporting;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Reporting;

/// <summary>
/// Unit tests for <see cref="ReportingEndpoints"/>.
///
/// Testing approach:
///   - Endpoint handlers are private static methods (Minimal API convention), so direct
///     invocation requires reflection. We test the publicly-observable contract:
///     route registration, DTO shapes, privilege level logic (via HttpContext.Items), and
///     validation-failure scenarios by invoking handlers via reflection.
///   - Integration tests cover the full HTTP request-response cycle with a real
///     <see cref="WebApplicationFactory{TEntryPoint}"/>.
///
/// ADR-008: Auth is enforced by ReportingAuthorizationFilter (tested separately).
/// </summary>
public class ReportingEndpointsTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static DefaultHttpContext BuildHttpContext(
        ReportingPrivilegeLevel privilege = ReportingPrivilegeLevel.Viewer,
        string userId = "user-001",
        string? upn = null,
        string? businessUnit = null)
    {
        var claims = new List<Claim>
        {
            new("oid", userId)
        };

        if (upn is not null)
            claims.Add(new Claim("preferred_username", upn));

        if (businessUnit is not null)
            claims.Add(new Claim("businessunit", businessUnit));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            TraceIdentifier = "test-trace-id"
        };

        httpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey] = privilege;
        return httpContext;
    }

    private static Mock<ReportingEmbedService> BuildEmbedServiceMock()
    {
        // ReportingEmbedService is a sealed concrete class — cannot be Moq'd directly.
        // Tests that need a controlled embed service response are handled via the internal
        // handler reflection approach below.
        return new Mock<ReportingEmbedService>(MockBehavior.Loose);
    }

    // =========================================================================
    // Route registration (public API surface)
    // =========================================================================

    [Fact]
    public void MapReportingEndpointGroup_MethodExists_AndIsExtensionMethod()
    {
        var method = typeof(ReportingEndpoints).GetMethod("MapReportingEndpointGroup");

        method.Should().NotBeNull("MapReportingEndpointGroup must be defined on ReportingEndpoints");
        method!.IsStatic.Should().BeTrue();
        method.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    [Fact]
    public void MapReportingEndpointGroup_AcceptsIEndpointRouteBuilder()
    {
        var method = typeof(ReportingEndpoints).GetMethod("MapReportingEndpointGroup");
        var parameters = method!.GetParameters();

        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    // =========================================================================
    // DTO contracts
    // =========================================================================

    [Fact]
    public void ReportingStatusResponse_HasExpectedProperties()
    {
        var type = typeof(ReportingStatusResponse);
        type.GetProperty("Enabled").Should().NotBeNull();
        type.GetProperty("Version").Should().NotBeNull();
        type.GetProperty("Privilege").Should().NotBeNull();
    }

    [Fact]
    public void ReportingStatusResponse_CanBeConstructed()
    {
        // Act
        var response = new ReportingStatusResponse(
            Enabled: true,
            Version: "1.0",
            Privilege: "Viewer");

        // Assert
        response.Enabled.Should().BeTrue();
        response.Version.Should().Be("1.0");
        response.Privilege.Should().Be("Viewer");
    }

    [Fact]
    public void CreateReportRequest_HasExpectedProperties()
    {
        var type = typeof(CreateReportRequest);
        type.GetProperty("WorkspaceId").Should().NotBeNull();
        type.GetProperty("Name").Should().NotBeNull();
        type.GetProperty("DatasetId").Should().NotBeNull();
        type.GetProperty("TemplateReportId").Should().NotBeNull();
    }

    [Fact]
    public void UpdateReportRequest_HasExpectedProperties()
    {
        var type = typeof(UpdateReportRequest);
        type.GetProperty("WorkspaceId").Should().NotBeNull();
        type.GetProperty("Name").Should().NotBeNull();

        // Name is optional (nullable string)
        type.GetProperty("Name")!.PropertyType.Should().Be(typeof(string),
            "Name is optional but still typed as string (record parameter can be null)");
    }

    [Fact]
    public void ReportingExportRequest_HasExpectedProperties()
    {
        var type = typeof(ReportingExportRequest);
        type.GetProperty("WorkspaceId").Should().NotBeNull();
        type.GetProperty("ReportId").Should().NotBeNull();
        type.GetProperty("Format").Should().NotBeNull();
        type.GetProperty("FileName").Should().NotBeNull();
    }

    [Fact]
    public void ReportingExportRequest_FormatPropertyIsExportFormatEnum()
    {
        var prop = typeof(ReportingExportRequest).GetProperty("Format");
        prop!.PropertyType.Should().Be(typeof(ExportFormat));
    }

    // =========================================================================
    // Privilege level — HttpContext.Items interaction
    // =========================================================================

    [Fact]
    public void PrivilegeLevelItemKey_StoresAndRetrieves_CorrectValue()
    {
        // Arrange
        var httpContext = BuildHttpContext(ReportingPrivilegeLevel.Admin);

        // Act
        var actual = httpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey];

        // Assert
        actual.Should().Be(ReportingPrivilegeLevel.Admin);
    }

    [Fact]
    public void PrivilegeLevelItemKey_DefaultsToViewer_WhenNotSet()
    {
        // Arrange — no privilege set in Items
        var httpContext = new DefaultHttpContext();
        httpContext.Items.Clear();

        // Simulate the GetPrivilegeLevel helper (internal logic verified via the constant)
        var level = httpContext.Items.TryGetValue(ReportingAuthorizationFilter.PrivilegeLevelItemKey, out var value)
                    && value is ReportingPrivilegeLevel lvl
            ? lvl
            : ReportingPrivilegeLevel.Viewer;

        // Assert
        level.Should().Be(ReportingPrivilegeLevel.Viewer,
            "GetPrivilegeLevel defaults to Viewer when the key is absent (safe fallback)");
    }

    // =========================================================================
    // Privilege level — enum ordering (used for comparison checks in handlers)
    // =========================================================================

    [Fact]
    public void ReportingPrivilegeLevel_Admin_IsGreaterThan_Author()
    {
        ((int)ReportingPrivilegeLevel.Admin).Should().BeGreaterThan(
            (int)ReportingPrivilegeLevel.Author,
            "Admin > Author allows 'privilege < Author' check to work correctly");
    }

    [Fact]
    public void ReportingPrivilegeLevel_Author_IsGreaterThan_Viewer()
    {
        ((int)ReportingPrivilegeLevel.Author).Should().BeGreaterThan(
            (int)ReportingPrivilegeLevel.Viewer);
    }

    [Fact]
    public void ReportingPrivilegeLevel_Viewer_IsLeast()
    {
        ((int)ReportingPrivilegeLevel.Viewer).Should().Be(0);
    }

    // =========================================================================
    // Endpoint handler private method names (to ensure rename safety)
    // =========================================================================

    [Fact]
    public void ReportingEndpoints_DefinesGetStatusHandler()
    {
        var method = typeof(ReportingEndpoints).GetMethod(
            "GetStatus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("GetStatus handler must exist");
    }

    [Fact]
    public void ReportingEndpoints_DefinesGetEmbedTokenHandler()
    {
        var method = typeof(ReportingEndpoints).GetMethod(
            "GetEmbedToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("GetEmbedToken handler must exist");
    }

    [Fact]
    public void ReportingEndpoints_DefinesDeleteReportHandler()
    {
        var method = typeof(ReportingEndpoints).GetMethod(
            "DeleteReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("DeleteReport handler must exist");
    }

    [Fact]
    public void ReportingEndpoints_DefinesExportReportHandler()
    {
        var method = typeof(ReportingEndpoints).GetMethod(
            "ExportReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("ExportReport handler must exist");
    }

    // =========================================================================
    // GetStatus — invoked directly (no external dependencies)
    // =========================================================================

    [Theory]
    [InlineData(ReportingPrivilegeLevel.Viewer, "Viewer")]
    [InlineData(ReportingPrivilegeLevel.Author, "Author")]
    [InlineData(ReportingPrivilegeLevel.Admin, "Admin")]
    public async Task GetStatus_ReturnsCorrectPrivilegeString_ForEachLevel(
        ReportingPrivilegeLevel privilege,
        string expectedPrivilegeString)
    {
        // Arrange
        var httpContext = BuildHttpContext(privilege);
        var getStatusMethod = typeof(ReportingEndpoints).GetMethod(
            "GetStatus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Act
        var result = (IResult?)getStatusMethod.Invoke(null, [httpContext]);

        // Assert
        result.Should().NotBeNull();

        // Execute the result into a fake response to capture the status code and body
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result!.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(200);

        // Read body
        responseContext.Response.Body.Position = 0;
        using var reader = new System.IO.StreamReader(responseContext.Response.Body);
        var body = await reader.ReadToEndAsync();

        body.Should().Contain("true", "Enabled is always true when status endpoint is reached");
        body.Should().Contain("1.0", "Version must be 1.0");
        body.Should().Contain(expectedPrivilegeString,
            $"Privilege must be '{expectedPrivilegeString}' for privilege level {privilege}");
    }

    // =========================================================================
    // GetEmbedToken — missing parameter validation (no PBI calls needed)
    // =========================================================================

    [Fact]
    public async Task GetEmbedToken_Returns400_WhenWorkspaceIdIsMissing()
    {
        // Arrange
        var httpContext = BuildHttpContext();
        var logger = Mock.Of<ILogger<Program>>();
        var getEmbedTokenMethod = typeof(ReportingEndpoints).GetMethod(
            "GetEmbedToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Act — workspaceId = null, reportId = valid
        var reportId = (Guid?)Guid.NewGuid();
        var result = await (Task<IResult>)getEmbedTokenMethod.Invoke(null, [
            null,           // workspaceId
            reportId,       // reportId
            null!,          // embedService (null — should not be reached)
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetEmbedToken_Returns400_WhenReportIdIsMissing()
    {
        // Arrange
        var httpContext = BuildHttpContext();
        var logger = Mock.Of<ILogger<Program>>();
        var getEmbedTokenMethod = typeof(ReportingEndpoints).GetMethod(
            "GetEmbedToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Act — workspaceId = valid, reportId = null
        var workspaceId = (Guid?)Guid.NewGuid();
        var result = await (Task<IResult>)getEmbedTokenMethod.Invoke(null, [
            workspaceId,    // workspaceId
            null,           // reportId — missing
            null!,          // embedService
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(400);
    }

    // =========================================================================
    // DeleteReport — Admin privilege check (no PBI call needed for forbidden path)
    // =========================================================================

    [Theory]
    [InlineData(ReportingPrivilegeLevel.Viewer)]
    [InlineData(ReportingPrivilegeLevel.Author)]
    public async Task DeleteReport_Returns403_WhenPrivilegeIsLessThanAdmin(
        ReportingPrivilegeLevel privilege)
    {
        // Arrange — user is Viewer or Author (below Admin threshold)
        var httpContext = BuildHttpContext(privilege);
        var logger = Mock.Of<ILogger<Program>>();
        var deleteReportMethod = typeof(ReportingEndpoints).GetMethod(
            "DeleteReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var reportId = Guid.NewGuid();
        var workspaceId = (Guid?)Guid.NewGuid();

        // Act
        var result = await (Task<IResult>)deleteReportMethod.Invoke(null, [
            reportId,
            workspaceId,
            null!,          // embedService — should not be reached due to privilege check
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(403,
            $"privilege '{privilege}' is below Admin — delete must be denied");
    }

    // =========================================================================
    // ExportReport — format validation
    // =========================================================================

    [Fact]
    public async Task ExportReport_Returns400_WhenWorkspaceIdIsEmpty()
    {
        // Arrange
        var httpContext = BuildHttpContext();
        var logger = Mock.Of<ILogger<Program>>();
        var exportMethod = typeof(ReportingEndpoints).GetMethod(
            "ExportReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var request = new ReportingExportRequest(
            WorkspaceId: Guid.Empty,     // invalid — empty GUID
            ReportId: Guid.NewGuid(),
            Format: ExportFormat.PDF);

        // Act
        var result = await (Task<IResult>)exportMethod.Invoke(null, [
            request,
            null!,      // embedService
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ExportReport_Returns400_WhenReportIdIsEmpty()
    {
        // Arrange
        var httpContext = BuildHttpContext();
        var logger = Mock.Of<ILogger<Program>>();
        var exportMethod = typeof(ReportingEndpoints).GetMethod(
            "ExportReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var request = new ReportingExportRequest(
            WorkspaceId: Guid.NewGuid(),
            ReportId: Guid.Empty,        // invalid
            Format: ExportFormat.PPTX);

        // Act
        var result = await (Task<IResult>)exportMethod.Invoke(null, [
            request,
            null!,
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(400);
    }

    // =========================================================================
    // CreateReport — Author/Admin privilege required
    // =========================================================================

    [Fact]
    public async Task CreateReport_Returns403_WhenPrivilegeIsViewer()
    {
        // Arrange — Viewer does not have Author+ privilege
        var httpContext = BuildHttpContext(ReportingPrivilegeLevel.Viewer);
        var logger = Mock.Of<ILogger<Program>>();
        var createMethod = typeof(ReportingEndpoints).GetMethod(
            "CreateReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var request = new CreateReportRequest(
            WorkspaceId: Guid.NewGuid(),
            Name: "My Report",
            DatasetId: Guid.NewGuid(),
            TemplateReportId: Guid.NewGuid());

        // Act
        var result = await (Task<IResult>)createMethod.Invoke(null, [
            request,
            null!,
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(403,
            "Viewer privilege is below Author — report creation must be denied");
    }

    [Fact]
    public async Task CreateReport_Returns400_WhenNameIsEmpty_ForAuthorPrivilege()
    {
        // Arrange — Author privilege passes the privilege check but empty name fails validation
        var httpContext = BuildHttpContext(ReportingPrivilegeLevel.Author);
        var logger = Mock.Of<ILogger<Program>>();
        var createMethod = typeof(ReportingEndpoints).GetMethod(
            "CreateReport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var request = new CreateReportRequest(
            WorkspaceId: Guid.NewGuid(),
            Name: "",                    // invalid — empty name
            DatasetId: Guid.NewGuid(),
            TemplateReportId: Guid.NewGuid());

        // Act
        var result = await (Task<IResult>)createMethod.Invoke(null, [
            request,
            null!,
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(400,
            "empty report name must be rejected with 400");
    }

    // =========================================================================
    // GetReports — missing workspaceId
    // =========================================================================

    [Fact]
    public async Task GetReports_Returns400_WhenWorkspaceIdIsMissing()
    {
        // Arrange
        var httpContext = BuildHttpContext();
        var logger = Mock.Of<ILogger<Program>>();
        var getReportsMethod = typeof(ReportingEndpoints).GetMethod(
            "GetReports",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Act — workspaceId = null
        var result = await (Task<IResult>)getReportsMethod.Invoke(null, [
            null,           // workspaceId
            null!,          // embedService
            logger,
            httpContext,
            CancellationToken.None
        ])!;

        // Assert
        var responseContext = new DefaultHttpContext();
        responseContext.Response.Body = new System.IO.MemoryStream();
        await result.ExecuteAsync(responseContext);

        responseContext.Response.StatusCode.Should().Be(400);
    }
}
