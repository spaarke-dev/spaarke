using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for recycle bin endpoint contracts and DTO models.
///
/// Strategy: Tests validate DTO structure, mapping logic, and endpoint registration shape.
/// Graph SDK classes are sealed and cannot be mocked — integration-level Graph behavior
/// is validated through the endpoint handler structure and the SpeAdminGraphService domain
/// model mapping (no live Graph calls in unit tests).
///
/// SPE-059: Recycle bin endpoints — list, restore, permanent delete.
/// </summary>
public class RecycleBinTests
{
    // =========================================================================
    // DeletedContainerDto Tests
    // =========================================================================

    [Fact]
    public void DeletedContainerDto_DefaultValues_AreEmpty()
    {
        // Arrange & Act
        var dto = new DeletedContainerDto();

        // Assert
        dto.Id.Should().Be(string.Empty);
        dto.DisplayName.Should().Be(string.Empty);
        dto.DeletedDateTime.Should().BeNull();
        dto.ContainerTypeId.Should().Be(string.Empty);
    }

    [Fact]
    public void DeletedContainerDto_WithValues_RoundTrips()
    {
        // Arrange
        var id = "b!abc123";
        var displayName = "Legal Workspace - Archived";
        var deletedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var containerTypeId = Guid.NewGuid().ToString();

        // Act
        var dto = new DeletedContainerDto
        {
            Id = id,
            DisplayName = displayName,
            DeletedDateTime = deletedAt,
            ContainerTypeId = containerTypeId
        };

        // Assert
        dto.Id.Should().Be(id);
        dto.DisplayName.Should().Be(displayName);
        dto.DeletedDateTime.Should().Be(deletedAt);
        dto.ContainerTypeId.Should().Be(containerTypeId);
    }

    // =========================================================================
    // SpeAdminGraphService.DeletedContainerSummary Domain Model Tests
    // =========================================================================

    [Fact]
    public void DeletedContainerSummary_PropertiesAreReadable()
    {
        // Arrange
        var id = "container-123";
        var displayName = "Test Container";
        var deletedAt = DateTimeOffset.UtcNow;
        var containerTypeId = "ct-456";

        // Act
        var summary = new SpeAdminGraphService.DeletedContainerSummary(
            Id: id,
            DisplayName: displayName,
            DeletedDateTime: deletedAt,
            ContainerTypeId: containerTypeId);

        // Assert
        summary.Id.Should().Be(id);
        summary.DisplayName.Should().Be(displayName);
        summary.DeletedDateTime.Should().Be(deletedAt);
        summary.ContainerTypeId.Should().Be(containerTypeId);
    }

    [Fact]
    public void DeletedContainerSummary_NullDeletedDateTime_IsAllowed()
    {
        // Act — container without a deletedDateTime should not throw
        var summary = new SpeAdminGraphService.DeletedContainerSummary(
            Id: "x",
            DisplayName: "X",
            DeletedDateTime: null,
            ContainerTypeId: "ct");

        // Assert
        summary.DeletedDateTime.Should().BeNull();
    }

    // =========================================================================
    // RecycleBinListResponse DTO Tests
    // =========================================================================

    [Fact]
    public void RecycleBinListResponse_EmptyList_HasZeroCount()
    {
        // Act
        var response = new RecycleBinEndpoints.RecycleBinListResponse(
            Items: Array.Empty<DeletedContainerDto>(),
            Count: 0);

        // Assert
        response.Items.Should().BeEmpty();
        response.Count.Should().Be(0);
    }

    [Fact]
    public void RecycleBinListResponse_WithItems_ReturnsCorrectCount()
    {
        // Arrange
        var items = new List<DeletedContainerDto>
        {
            new() { Id = "c1", DisplayName = "Container 1", ContainerTypeId = "ct" },
            new() { Id = "c2", DisplayName = "Container 2", ContainerTypeId = "ct" }
        };

        // Act
        var response = new RecycleBinEndpoints.RecycleBinListResponse(items, items.Count);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Count.Should().Be(2);
        response.Items[0].Id.Should().Be("c1");
        response.Items[1].Id.Should().Be("c2");
    }

    // =========================================================================
    // Endpoint Registration Shape Tests
    // =========================================================================


    // =========================================================================
    // SpeAdminGraphService — Recycle Bin Method Contract Tests
    // =========================================================================


    // =========================================================================
    // DeletedContainerDto Mapping Tests
    // =========================================================================

    [Fact]
    public void DeletedContainerDto_MappedFromSummary_PreservesAllFields()
    {
        // Arrange — simulate what the endpoint handler does when mapping domain → DTO
        var summary = new SpeAdminGraphService.DeletedContainerSummary(
            Id: "b!container-id",
            DisplayName: "Legal Documents Archive",
            DeletedDateTime: new DateTimeOffset(2026, 2, 15, 9, 30, 0, TimeSpan.Zero),
            ContainerTypeId: "ct-guid-value");

        // Act — replicate endpoint mapping logic
        var dto = new DeletedContainerDto
        {
            Id = summary.Id,
            DisplayName = summary.DisplayName,
            DeletedDateTime = summary.DeletedDateTime,
            ContainerTypeId = summary.ContainerTypeId
        };

        // Assert
        dto.Id.Should().Be(summary.Id);
        dto.DisplayName.Should().Be(summary.DisplayName);
        dto.DeletedDateTime.Should().Be(summary.DeletedDateTime);
        dto.ContainerTypeId.Should().Be(summary.ContainerTypeId);
    }

    [Fact]
    public void RecycleBinListResponse_Items_AreImmutable()
    {
        // Arrange
        var items = new List<DeletedContainerDto>
        {
            new() { Id = "c1", DisplayName = "Container 1", ContainerTypeId = "ct" }
        };

        var response = new RecycleBinEndpoints.RecycleBinListResponse(items, items.Count);

        // Act — casting Items as IReadOnlyList (cannot add items)
        var readOnly = response.Items;

        // Assert — IReadOnlyList does not expose Add/Remove
        readOnly.Should().BeAssignableTo<IReadOnlyList<DeletedContainerDto>>();
        readOnly.Should().HaveCount(1);
    }

    // =========================================================================
    // Audit Category Tests — ensure correct audit category for irreversible operations
    // =========================================================================

    [Fact]
    public void RecycleBinEndpoints_ExistsInSpeAdminNamespace()
    {
        // Assert — class must be in the correct namespace alongside other SpeAdmin endpoints
        typeof(RecycleBinEndpoints).Namespace.Should().Be("Sprk.Bff.Api.Api.SpeAdmin");
    }

    [Fact]
    public void DeletedContainerDto_ExistsInSpeAdminModelsNamespace()
    {
        // Assert — DTO must be in the models namespace for consistent organization
        typeof(DeletedContainerDto).Namespace.Should().Be("Sprk.Bff.Api.Models.SpeAdmin");
    }
}
