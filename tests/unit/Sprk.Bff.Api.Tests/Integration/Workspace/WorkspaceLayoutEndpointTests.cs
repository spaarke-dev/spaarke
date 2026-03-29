using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Workspace;

/// <summary>
/// Integration tests for workspace layout CRUD endpoints, section registry, and templates.
/// Exercises the full Minimal API pipeline with a mocked IGenericEntityService for Dataverse
/// operations, validating endpoint routing, status codes, business rules, and JSON shapes.
///
/// Endpoints covered:
///   GET    /api/workspace/layouts          — List all layouts (system + user)
///   GET    /api/workspace/layouts/default  — Get default layout (with system fallback)
///   GET    /api/workspace/layouts/{id}     — Get specific layout by ID
///   POST   /api/workspace/layouts          — Create layout (max 10 enforcement)
///   PUT    /api/workspace/layouts/{id}     — Update layout (system immutability)
///   DELETE /api/workspace/layouts/{id}     — Delete layout (system immutability)
///   GET    /api/workspace/sections         — Static section registry
///   GET    /api/workspace/templates        — Static layout templates
/// </summary>
public class WorkspaceLayoutEndpointTests : IClassFixture<WorkspaceLayoutTestFixture>
{
    private readonly WorkspaceLayoutTestFixture _fixture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WorkspaceLayoutEndpointTests(WorkspaceLayoutTestFixture fixture)
    {
        _fixture = fixture;
    }

    // =========================================================================
    // GET /api/workspace/sections — Static section registry
    // =========================================================================

    [Fact]
    public async Task GetSections_AuthenticatedRequest_Returns200WithSectionList()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/sections");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var sections = await response.Content.ReadFromJsonAsync<SectionDto[]>(JsonOptions);
        sections.Should().NotBeNull();
        sections!.Length.Should().BeGreaterOrEqualTo(5, "registry should include at least the 5 core sections");

        // Verify known sections exist
        sections.Should().Contain(s => s.Id == "get-started");
        sections.Should().Contain(s => s.Id == "quick-summary");
        sections.Should().Contain(s => s.Id == "latest-updates");
        sections.Should().Contain(s => s.Id == "todo");
        sections.Should().Contain(s => s.Id == "documents");

        // Verify shape of a section
        var getStarted = sections.First(s => s.Id == "get-started");
        getStarted.Label.Should().NotBeNullOrEmpty();
        getStarted.Description.Should().NotBeNullOrEmpty();
        getStarted.Category.Should().Be("core");
        getStarted.IconName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSections_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/sections");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // GET /api/workspace/templates — Static layout templates
    // =========================================================================

    [Fact]
    public async Task GetTemplates_AuthenticatedRequest_Returns200WithTemplateList()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/templates");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var templates = await response.Content.ReadFromJsonAsync<LayoutTemplateDto[]>(JsonOptions);
        templates.Should().NotBeNull();
        templates!.Length.Should().BeGreaterOrEqualTo(4, "registry should include at least 4 templates");

        // Verify known templates
        templates.Should().Contain(t => t.Id == "1-column");
        templates.Should().Contain(t => t.Id == "2-column");
        templates.Should().Contain(t => t.Id == "3-column");
        templates.Should().Contain(t => t.Id == "3-row-mixed");

        // Verify shape includes rows
        var mixed = templates.First(t => t.Id == "3-row-mixed");
        mixed.Name.Should().NotBeNullOrEmpty();
        mixed.Rows.Should().HaveCount(3, "3-row-mixed template has 3 rows");
        mixed.Rows[0].SlotCount.Should().Be(2, "first row has 2 columns");
        mixed.Rows[1].SlotCount.Should().Be(1, "middle row is full-width");
    }

    [Fact]
    public async Task GetTemplates_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/templates");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // GET /api/workspace/layouts — List layouts
    // =========================================================================

    [Fact]
    public async Task GetLayouts_AuthenticatedRequest_Returns200WithSystemAndUserLayouts()
    {
        // Arrange
        using var fixture = WorkspaceLayoutTestFixture.WithUserLayouts(2);
        using var client = fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/layouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layouts = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto[]>(JsonOptions);
        layouts.Should().NotBeNull();

        // Should include at least the system layout (Corporate Workspace) + 2 user layouts
        layouts!.Length.Should().Be(3, "1 system + 2 user layouts");

        // System layouts appear first
        layouts[0].IsSystem.Should().BeTrue("first layout should be system");
        layouts[0].Name.Should().Be("Corporate Workspace");

        // User layouts follow
        layouts[1].IsSystem.Should().BeFalse();
        layouts[2].IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task GetLayouts_NoUserLayouts_ReturnsOnlySystemLayouts()
    {
        // Arrange
        using var fixture = WorkspaceLayoutTestFixture.WithUserLayouts(0);
        using var client = fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/layouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layouts = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto[]>(JsonOptions);
        layouts.Should().NotBeNull();
        layouts!.Length.Should().Be(1, "only the system layout");
        layouts[0].IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task GetLayouts_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/layouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // GET /api/workspace/layouts/default — Default layout
    // =========================================================================

    [Fact]
    public async Task GetDefaultLayout_NoUserDefault_ReturnsCorporateWorkspaceFallback()
    {
        // Arrange — empty entity collection means no user default found
        using var fixture = WorkspaceLayoutTestFixture.WithEmptyDefaults();
        using var client = fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/layouts/default");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.Id.Should().Be(SystemWorkspaceLayouts.CorporateWorkspaceId);
        layout.IsSystem.Should().BeTrue();
        layout.Name.Should().Be("Corporate Workspace");
    }

    [Fact]
    public async Task GetDefaultLayout_UserHasDefault_ReturnsUserDefault()
    {
        // Arrange — mock returns a user layout marked as default
        var defaultId = Guid.NewGuid();
        using var fixture = WorkspaceLayoutTestFixture.WithUserDefault(defaultId, "My Custom Layout");
        using var client = fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/layouts/default");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.Id.Should().Be(defaultId);
        layout.Name.Should().Be("My Custom Layout");
        layout.IsSystem.Should().BeFalse();
    }

    // =========================================================================
    // GET /api/workspace/layouts/{id} — Get by ID
    // =========================================================================

    [Fact]
    public async Task GetLayoutById_SystemLayoutId_ReturnsCorporateWorkspace()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();
        var systemId = SystemWorkspaceLayouts.CorporateWorkspaceId;

        // Act
        var response = await client.GetAsync($"/api/workspace/layouts/{systemId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.Id.Should().Be(systemId);
        layout.IsSystem.Should().BeTrue();
        layout.Name.Should().Be("Corporate Workspace");
    }

    [Fact]
    public async Task GetLayoutById_ExistingUserLayout_Returns200WithLayout()
    {
        // Arrange
        var layoutId = Guid.NewGuid();
        using var fixture = WorkspaceLayoutTestFixture.WithSingleUserLayout(layoutId, "Test Layout");
        using var client = fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync($"/api/workspace/layouts/{layoutId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.Id.Should().Be(layoutId);
        layout.Name.Should().Be("Test Layout");
        layout.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task GetLayoutById_NonExistentId_Returns404()
    {
        // Arrange — mock throws (simulating record not found)
        using var fixture = WorkspaceLayoutTestFixture.WithRetrieveThrows();
        using var client = fixture.CreateAuthenticatedClient();
        var unknownId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/workspace/layouts/{unknownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // POST /api/workspace/layouts — Create layout
    // =========================================================================

    [Fact]
    public async Task CreateLayout_ValidRequest_Returns201WithCreatedLayout()
    {
        // Arrange
        var createdId = Guid.NewGuid();
        using var fixture = WorkspaceLayoutTestFixture.WithCreateSuccess(createdId, existingCount: 0);
        using var client = fixture.CreateAuthenticatedClient();

        var request = new CreateWorkspaceLayoutRequest
        {
            Name = "My Custom Layout",
            LayoutTemplateId = "2-column",
            SectionsJson = """[{"sectionId":"documents","slotIndex":0}]""",
            IsDefault = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/workspace/layouts", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain($"/api/workspace/layouts/{createdId}");

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.Id.Should().Be(createdId);
        layout.Name.Should().Be("My Custom Layout");
        layout.LayoutTemplateId.Should().Be("2-column");
        layout.IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task CreateLayout_MaxLayoutsReached_Returns409Conflict()
    {
        // Arrange — mock returns 10 existing user layouts
        using var fixture = WorkspaceLayoutTestFixture.WithCreateSuccess(Guid.NewGuid(), existingCount: 10);
        using var client = fixture.CreateAuthenticatedClient();

        var request = new CreateWorkspaceLayoutRequest
        {
            Name = "One Too Many",
            LayoutTemplateId = "1-column",
            SectionsJson = "[]",
            IsDefault = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/workspace/layouts", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Maximum");
    }

    [Fact]
    public async Task CreateLayout_SetAsDefault_ClearsPreviousDefault()
    {
        // Arrange — 1 existing layout that is currently default
        var createdId = Guid.NewGuid();
        var existingDefaultId = Guid.NewGuid();
        using var fixture = WorkspaceLayoutTestFixture.WithCreateAndExistingDefault(
            createdId, existingDefaultId);
        using var client = fixture.CreateAuthenticatedClient();

        var request = new CreateWorkspaceLayoutRequest
        {
            Name = "New Default",
            LayoutTemplateId = "2-column",
            SectionsJson = "[]",
            IsDefault = true
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/workspace/layouts", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.IsDefault.Should().BeTrue();

        // Verify BulkUpdateAsync was called to clear the previous default
        fixture.EntityServiceMock.Verify(
            s => s.BulkUpdateAsync(
                "sprk_workspacelayout",
                It.Is<List<(Guid id, Dictionary<string, object> fields)>>(
                    updates => updates.Any(u => u.id == existingDefaultId)),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "should clear isDefault on the previous default layout");
    }

    [Fact]
    public async Task CreateLayout_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        var request = new CreateWorkspaceLayoutRequest
        {
            Name = "Should Fail",
            LayoutTemplateId = "1-column",
            SectionsJson = "[]",
            IsDefault = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/workspace/layouts", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // PUT /api/workspace/layouts/{id} — Update layout
    // =========================================================================

    [Fact]
    public async Task UpdateLayout_ExistingUserLayout_Returns200WithUpdatedLayout()
    {
        // Arrange
        var layoutId = Guid.NewGuid();
        using var fixture = WorkspaceLayoutTestFixture.WithUpdateSuccess(layoutId);
        using var client = fixture.CreateAuthenticatedClient();

        var request = new UpdateWorkspaceLayoutRequest
        {
            Name = "Updated Layout",
            LayoutTemplateId = "3-column",
            SectionsJson = """[{"sectionId":"todo","slotIndex":0}]""",
            IsDefault = false
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/workspace/layouts/{layoutId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layout = await response.Content.ReadFromJsonAsync<WorkspaceLayoutDto>(JsonOptions);
        layout.Should().NotBeNull();
        layout!.Id.Should().Be(layoutId);
        layout.Name.Should().Be("Updated Layout");
        layout.LayoutTemplateId.Should().Be("3-column");
    }

    [Fact]
    public async Task UpdateLayout_SystemLayout_Returns403Forbidden()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();
        var systemId = SystemWorkspaceLayouts.CorporateWorkspaceId;

        var request = new UpdateWorkspaceLayoutRequest
        {
            Name = "Hacked Corporate",
            LayoutTemplateId = "1-column",
            SectionsJson = "[]",
            IsDefault = false
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/workspace/layouts/{systemId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("System");
    }

    [Fact]
    public async Task UpdateLayout_NonExistentId_Returns404()
    {
        // Arrange — RetrieveAsync throws (layout not found)
        using var fixture = WorkspaceLayoutTestFixture.WithRetrieveThrows();
        using var client = fixture.CreateAuthenticatedClient();
        var unknownId = Guid.NewGuid();

        var request = new UpdateWorkspaceLayoutRequest
        {
            Name = "Ghost Layout",
            LayoutTemplateId = "1-column",
            SectionsJson = "[]",
            IsDefault = false
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/workspace/layouts/{unknownId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // DELETE /api/workspace/layouts/{id} — Delete layout
    // =========================================================================

    [Fact]
    public async Task DeleteLayout_ExistingUserLayout_Returns204NoContent()
    {
        // Arrange
        var layoutId = Guid.NewGuid();
        using var fixture = WorkspaceLayoutTestFixture.WithDeleteSuccess(layoutId);
        using var client = fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.DeleteAsync($"/api/workspace/layouts/{layoutId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteLayout_SystemLayout_Returns403Forbidden()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();
        var systemId = SystemWorkspaceLayouts.CorporateWorkspaceId;

        // Act
        var response = await client.DeleteAsync($"/api/workspace/layouts/{systemId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("System");
    }

    [Fact]
    public async Task DeleteLayout_NonExistentId_Returns404()
    {
        // Arrange — RetrieveAsync throws (layout not found)
        using var fixture = WorkspaceLayoutTestFixture.WithRetrieveThrows();
        using var client = fixture.CreateAuthenticatedClient();
        var unknownId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/workspace/layouts/{unknownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteLayout_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.DeleteAsync($"/api/workspace/layouts/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
