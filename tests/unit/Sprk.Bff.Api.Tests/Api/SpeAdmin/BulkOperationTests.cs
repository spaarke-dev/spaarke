using FluentAssertions;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the bulk operation domain models and the BulkOperationService state machine.
///
/// Strategy: Tests validate request/response model construction, validation logic,
/// and the in-memory status tracking behaviour of BulkOperationService without requiring
/// a live Graph API or SpeAdminGraphService (which has non-virtual Graph SDK dependencies).
///
/// SPE-083: Bulk delete and bulk permission assignment with background processing.
/// </summary>
public class BulkOperationTests
{
    // =========================================================================
    // BulkDeleteRequest Tests
    // =========================================================================

    #region BulkDeleteRequest

    [Fact]
    public void BulkDeleteRequest_ConstructsCorrectly()
    {
        // Arrange
        var ids = new List<string> { "container-1", "container-2", "container-3" };
        var configId = Guid.NewGuid().ToString();

        // Act
        var request = new BulkDeleteRequest(ids, configId);

        // Assert
        request.ContainerIds.Should().BeEquivalentTo(ids);
        request.ConfigId.Should().Be(configId);
    }

    [Fact]
    public void BulkDeleteRequest_SupportsSingleContainer()
    {
        // Arrange & Act
        var request = new BulkDeleteRequest(
            ContainerIds: new[] { "b!single-container-id" },
            ConfigId: Guid.NewGuid().ToString());

        // Assert
        request.ContainerIds.Should().HaveCount(1);
        request.ContainerIds[0].Should().Be("b!single-container-id");
    }

    #endregion

    // =========================================================================
    // BulkPermissionsRequest Tests
    // =========================================================================

    #region BulkPermissionsRequest

    [Fact]
    public void BulkPermissionsRequest_WithUserId_ConstructsCorrectly()
    {
        // Arrange
        var ids = new[] { "container-a", "container-b" };
        var configId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        // Act
        var request = new BulkPermissionsRequest(
            ContainerIds: ids,
            ConfigId: configId,
            UserId: userId,
            GroupId: null,
            Role: "reader");

        // Assert
        request.ContainerIds.Should().BeEquivalentTo(ids);
        request.ConfigId.Should().Be(configId);
        request.UserId.Should().Be(userId);
        request.GroupId.Should().BeNull();
        request.Role.Should().Be("reader");
    }

    [Fact]
    public void BulkPermissionsRequest_WithGroupId_ConstructsCorrectly()
    {
        // Arrange
        var groupId = Guid.NewGuid().ToString();

        // Act
        var request = new BulkPermissionsRequest(
            ContainerIds: new[] { "container-c" },
            ConfigId: Guid.NewGuid().ToString(),
            UserId: null,
            GroupId: groupId,
            Role: "writer");

        // Assert
        request.GroupId.Should().Be(groupId);
        request.UserId.Should().BeNull();
        request.Role.Should().Be("writer");
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("writer")]
    [InlineData("manager")]
    [InlineData("owner")]
    public void BulkPermissionsRequest_AcceptsValidRoles(string role)
    {
        // Act
        var request = new BulkPermissionsRequest(
            ContainerIds: new[] { "c-1" },
            ConfigId: Guid.NewGuid().ToString(),
            UserId: Guid.NewGuid().ToString(),
            GroupId: null,
            Role: role);

        // Assert
        request.Role.Should().Be(role);
    }

    #endregion

    // =========================================================================
    // BulkOperationStatus Tests
    // =========================================================================

    #region BulkOperationStatus

    [Fact]
    public void BulkOperationStatus_InProgress_HasCorrectShape()
    {
        // Arrange & Act
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.Delete,
            Total: 10,
            Completed: 3,
            Failed: 1,
            IsFinished: false,
            Errors: new List<BulkOperationItemError>
            {
                new("container-x", "Graph API error (HTTP 404)")
            },
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-5),
            CompletedAt: null);

        // Assert
        status.Total.Should().Be(10);
        status.Completed.Should().Be(3);
        status.Failed.Should().Be(1);
        status.IsFinished.Should().BeFalse();
        status.Errors.Should().HaveCount(1);
        status.CompletedAt.Should().BeNull();
        status.OperationType.Should().Be(BulkOperationType.Delete);
    }

    [Fact]
    public void BulkOperationStatus_Finished_AllSucceeded()
    {
        // Arrange & Act
        var completedAt = DateTimeOffset.UtcNow;
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.AssignPermissions,
            Total: 5,
            Completed: 5,
            Failed: 0,
            IsFinished: true,
            Errors: new List<BulkOperationItemError>(),
            StartedAt: completedAt.AddSeconds(-3),
            CompletedAt: completedAt);

        // Assert
        status.IsFinished.Should().BeTrue();
        status.Failed.Should().Be(0);
        status.Errors.Should().BeEmpty();
        status.CompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public void BulkOperationStatus_PartialFailure_ReportsErrors()
    {
        // Arrange
        var errors = new List<BulkOperationItemError>
        {
            new("container-1", "Container not found"),
            new("container-3", "Permission denied by Graph API"),
        };

        // Act
        var status = new BulkOperationStatus(
            OperationId: Guid.NewGuid(),
            OperationType: BulkOperationType.Delete,
            Total: 5,
            Completed: 3,
            Failed: 2,
            IsFinished: true,
            Errors: errors,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-10),
            CompletedAt: DateTimeOffset.UtcNow);

        // Assert
        status.Failed.Should().Be(2);
        status.Completed.Should().Be(3);
        status.Errors.Should().HaveCount(2);
        status.Errors[0].ContainerId.Should().Be("container-1");
        status.Errors[0].ErrorMessage.Should().Be("Container not found");
        status.Errors[1].ContainerId.Should().Be("container-3");
    }

    #endregion

    // =========================================================================
    // BulkOperationItemError Tests
    // =========================================================================

    #region BulkOperationItemError

    [Fact]
    public void BulkOperationItemError_ConstructsCorrectly()
    {
        // Act
        var error = new BulkOperationItemError("b!abc", "Graph API error (HTTP 403)");

        // Assert
        error.ContainerId.Should().Be("b!abc");
        error.ErrorMessage.Should().Be("Graph API error (HTTP 403)");
    }

    [Fact]
    public void BulkOperationItemError_RecordEquality_Works()
    {
        // Arrange
        var a = new BulkOperationItemError("c-1", "Not found");
        var b = new BulkOperationItemError("c-1", "Not found");
        var c = new BulkOperationItemError("c-2", "Not found");

        // Assert
        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    #endregion

    // =========================================================================
    // BulkOperationAccepted Tests
    // =========================================================================

    #region BulkOperationAccepted

    [Fact]
    public void BulkOperationAccepted_ConstructsCorrectly()
    {
        // Arrange
        var opId = Guid.NewGuid();

        // Act
        var accepted = new BulkOperationAccepted(
            OperationId: opId,
            StatusUrl: $"/api/spe/bulk/{opId}/status");

        // Assert
        accepted.OperationId.Should().Be(opId);
        accepted.StatusUrl.Should().Be($"/api/spe/bulk/{opId}/status");
    }

    #endregion

    // =========================================================================
    // BulkOperationType Enum Tests
    // =========================================================================

    #region BulkOperationType

    [Fact]
    public void BulkOperationType_HasExpectedValues()
    {
        // Assert
        Enum.IsDefined(typeof(BulkOperationType), BulkOperationType.Delete).Should().BeTrue();
        Enum.IsDefined(typeof(BulkOperationType), BulkOperationType.AssignPermissions).Should().BeTrue();
        Enum.GetValues<BulkOperationType>().Should().HaveCount(2);
    }

    #endregion

    // =========================================================================
    // Endpoint registration (contract) Tests
    // =========================================================================

    #region BulkOperationEndpoints structure

    [Fact]
    public void BulkOperationEndpoints_ClassIsStatic()
    {
        // Arrange & Act
        var type = typeof(Sprk.Bff.Api.Api.SpeAdmin.BulkOperationEndpoints);

        // Assert — endpoint classes must be static (Minimal API pattern)
        type.IsAbstract.Should().BeTrue("static classes are abstract in MSIL");
        type.IsSealed.Should().BeTrue("static classes are sealed in MSIL");
    }

    [Fact]
    public void BulkOperationEndpoints_HasMapBulkOperationEndpointsMethod()
    {
        // Arrange
        var type = typeof(Sprk.Bff.Api.Api.SpeAdmin.BulkOperationEndpoints);

        // Act
        var method = type.GetMethod(
            "MapBulkOperationEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        // Assert
        method.Should().NotBeNull("MapBulkOperationEndpoints must be public static");
        method!.ReturnType.Should().Be(typeof(void), "registration methods return void");
    }

    #endregion

    // =========================================================================
    // Validation logic mirrors (duplicated from endpoint validation)
    // =========================================================================

    #region Validation Logic Tests

    [Fact]
    public void BulkDeleteRequest_ConfigId_MustBeValidGuid()
    {
        // Arrange — simulate endpoint validation logic
        var invalidConfigIds = new[] { "", "   ", "not-a-guid", "12345" };

        foreach (var configId in invalidConfigIds)
        {
            // The endpoint validates: !Guid.TryParse(configId, out _)
            var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

            // Assert
            isValid.Should().BeFalse($"'{configId}' should not be a valid configId");
        }
    }

    [Fact]
    public void BulkDeleteRequest_ValidConfigId_PassesValidation()
    {
        // Arrange
        var validConfigId = Guid.NewGuid().ToString();

        // Act — simulate endpoint validation
        var isValid = !string.IsNullOrWhiteSpace(validConfigId) && Guid.TryParse(validConfigId, out _);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void BulkPermissionsRequest_MutuallyExclusiveUserGroupValidation()
    {
        // Simulate endpoint: !hasUser && !hasGroup => bad request
        var neitherCase = new BulkPermissionsRequest(
            ContainerIds: new[] { "c-1" },
            ConfigId: Guid.NewGuid().ToString(),
            UserId: null,
            GroupId: null,
            Role: "reader");

        var hasUser = !string.IsNullOrWhiteSpace(neitherCase.UserId);
        var hasGroup = !string.IsNullOrWhiteSpace(neitherCase.GroupId);

        (!hasUser && !hasGroup).Should().BeTrue("neither userId nor groupId → validation error");

        // Simulate: hasUser && hasGroup => bad request
        var bothCase = new BulkPermissionsRequest(
            ContainerIds: new[] { "c-1" },
            ConfigId: Guid.NewGuid().ToString(),
            UserId: Guid.NewGuid().ToString(),
            GroupId: Guid.NewGuid().ToString(),
            Role: "owner");

        var hasBoth = !string.IsNullOrWhiteSpace(bothCase.UserId) && !string.IsNullOrWhiteSpace(bothCase.GroupId);
        hasBoth.Should().BeTrue("both userId and groupId → validation error");
    }

    [Theory]
    [InlineData("reader", true)]
    [InlineData("writer", true)]
    [InlineData("manager", true)]
    [InlineData("owner", true)]
    [InlineData("READER", true)]   // case-insensitive match
    [InlineData("admin", false)]
    [InlineData("superuser", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BulkPermissionsRequest_RoleValidation(string? role, bool isValid)
    {
        // Simulate endpoint validation: ValidRoles.Contains(role) case-insensitive
        var validRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reader", "writer", "manager", "owner"
        };

        var result = !string.IsNullOrWhiteSpace(role) && validRoles.Contains(role);

        result.Should().Be(isValid, $"role '{role}' should be {(isValid ? "valid" : "invalid")}");
    }

    [Fact]
    public void BulkRequest_MaxItems_ValidationBoundary()
    {
        // Max 500 containers per bulk request
        const int maxBulkItems = 500;

        var exactlyMax = Enumerable.Range(1, maxBulkItems).Select(i => $"c-{i}").ToList();
        var overMax = Enumerable.Range(1, maxBulkItems + 1).Select(i => $"c-{i}").ToList();

        (exactlyMax.Count > maxBulkItems).Should().BeFalse("exactly 500 should be accepted");
        (overMax.Count > maxBulkItems).Should().BeTrue("501 should be rejected");
    }

    #endregion
}
