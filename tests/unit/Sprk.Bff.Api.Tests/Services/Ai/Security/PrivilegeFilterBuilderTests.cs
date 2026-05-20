using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Security;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Security;

/// <summary>
/// Unit tests for PrivilegeFilterBuilder — validates OData filter construction
/// for privilege_group_ids security filtering (AIPU2-027).
/// </summary>
public class PrivilegeFilterBuilderTests
{
    [Fact]
    public void BuildFilter_EmptyGroupList_ReturnsPublicOnlyFilter()
    {
        // Arrange — user with no group memberships
        var groups = Array.Empty<string>();

        // Act
        var filter = PrivilegeFilterBuilder.BuildFilter(groups);

        // Assert — only public documents (empty privilege_group_ids collection)
        filter.Should().Be("not privilege_group_ids/any()");
    }

    [Fact]
    public void BuildFilter_SingleGroup_ContainsGroupClauseAndPublicClause()
    {
        // Arrange
        var groups = new[] { "group-a" };

        // Act
        var filter = PrivilegeFilterBuilder.BuildFilter(groups);

        // Assert — includes group clause and public fallback
        filter.Should().Contain("privilege_group_ids/any(g: g eq 'group-a')");
        filter.Should().Contain("not privilege_group_ids/any()");
        // Result should be wrapped in parentheses (combined OR)
        filter.Should().StartWith("(");
        filter.Should().EndWith(")");
    }

    [Fact]
    public void BuildFilter_MultipleGroups_IncludesAllGroupsAndPublicClause()
    {
        // Arrange
        var groups = new[] { "group-1", "group-2", "group-3" };

        // Act
        var filter = PrivilegeFilterBuilder.BuildFilter(groups);

        // Assert — all three groups present
        filter.Should().Contain("privilege_group_ids/any(g: g eq 'group-1')");
        filter.Should().Contain("privilege_group_ids/any(g: g eq 'group-2')");
        filter.Should().Contain("privilege_group_ids/any(g: g eq 'group-3')");
        // Public documents always accessible
        filter.Should().Contain("not privilege_group_ids/any()");
    }

    [Fact]
    public void BuildFilter_MultipleGroups_ClausesJoinedWithOr()
    {
        // Arrange
        var groups = new[] { "aaa", "bbb" };

        // Act
        var filter = PrivilegeFilterBuilder.BuildFilter(groups);

        // Assert — clauses joined with " or " (OData syntax)
        filter.Should().Contain(" or ");
        // Should not contain " and " between group clauses
        // (all group checks must be OR, not AND)
        var andCount = filter.Split(" and ").Length - 1;
        andCount.Should().Be(0, "privilege clauses must be combined with OR, not AND");
    }

    [Fact]
    public void BuildFilter_GroupIdWithSingleQuote_EscapesCorrectly()
    {
        // Arrange — adversarial: group ID containing a single quote (defence-in-depth)
        var groups = new[] { "group'with'quotes" };

        // Act
        var filter = PrivilegeFilterBuilder.BuildFilter(groups);

        // Assert — single quotes doubled per OData escaping rules
        filter.Should().Contain("g eq 'group''with''quotes'");
        // Must not contain unescaped single quotes inside the value
        filter.Should().NotContain("g eq 'group'with'quotes'");
    }

    [Fact]
    public void BuildFilter_NullGroupList_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<string> groups = null!;

        // Act & Assert
        var act = () => PrivilegeFilterBuilder.BuildFilter(groups);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildFilter_GuidGroups_ProducesValidODataSyntax()
    {
        // Arrange — realistic Azure AD group GUIDs
        var groups = new[]
        {
            "00000000-0000-0000-0000-000000000001",
            "00000000-0000-0000-0000-000000000002"
        };

        // Act
        var filter = PrivilegeFilterBuilder.BuildFilter(groups);

        // Assert — GUIDs appear verbatim (no encoding changes for valid GUID chars)
        filter.Should().Contain("g eq '00000000-0000-0000-0000-000000000001'");
        filter.Should().Contain("g eq '00000000-0000-0000-0000-000000000002'");
    }
}
