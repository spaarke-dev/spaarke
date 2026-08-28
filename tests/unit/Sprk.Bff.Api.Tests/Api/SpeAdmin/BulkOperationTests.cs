using FluentAssertions;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the bulk operation validation rules on <see cref="BulkPermissionsRequest"/>.
///
/// Strategy: Tests validate the userId/groupId mutual-exclusion rule, the role allow-list, and
/// the 500-item max-count boundary that BulkOperationEndpoints enforces on incoming bulk requests.
///
/// CORRECTED 2026-08-27 (task 042): the previous docstring claimed this file also validated "the
/// in-memory status tracking behaviour of BulkOperationService" — no test in this file ever
/// constructs a BulkOperationService instance, so that claimed coverage never existed.
///
/// SPE-083: Bulk delete and bulk permission assignment with background processing.
/// </summary>
public class BulkOperationTests
{
    // =========================================================================
    // Validation logic mirrors (duplicated from endpoint validation)
    // =========================================================================

    #region Validation Logic Tests

    // AMBIGUOUS (task 042): re-implements BulkOperationEndpoints' userId/groupId mutual-exclusion
    // guard locally rather than calling it, so a production change would NOT fail this test — but
    // it is the only record of that rule, and bulk operations have zero contract coverage.
    // /test-diet at task 090 decides.
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

    // AMBIGUOUS (task 042): re-implements BulkOperationEndpoints' role allow-list validation
    // locally rather than calling it, so a production change would NOT fail this test — but it is
    // the only record of the role allow-list. /test-diet at task 090 decides.
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

    // AMBIGUOUS (task 042): re-implements BulkOperationEndpoints' 500-item cap locally rather than
    // calling it, so a production change would NOT fail this test — but it is the only record of
    // the cap. /test-diet at task 090 decides.
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
