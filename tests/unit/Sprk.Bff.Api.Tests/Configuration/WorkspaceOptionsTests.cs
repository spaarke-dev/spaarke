using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="WorkspaceOptions"/> binding (chat-routing-redesign-r1 task 012,
/// spec FR-04). Asserts the new <see cref="WorkspaceOptions.SummarizePlaybookCode"/> property
/// binds from <c>Workspace:SummarizePlaybookCode</c> alongside the existing playbook-ID
/// properties without regressing backward-compat.
/// </summary>
public class WorkspaceOptionsTests
{
    [Fact]
    public void Defaults_ShouldBeUnsetExceptSummarizePlaybookCode()
    {
        // Arrange + Act
        var options = new WorkspaceOptions();

        // Assert — nullable playbook IDs default to null; SummarizePlaybookCode
        // is non-nullable string with `= string.Empty` initializer.
        options.PreFillPlaybookId.Should().BeNull();
        options.ProjectPreFillPlaybookId.Should().BeNull();
        options.AiSummaryPlaybookId.Should().BeNull();
        options.SummarizePlaybookId.Should().BeNull();
        options.SummarizePlaybookCode.Should().BeEmpty();
    }

    [Fact]
    public void SectionName_IsWorkspace()
    {
        WorkspaceOptions.SectionName.Should().Be("Workspace");
    }

    [Fact]
    public void SummarizePlaybookCode_BindsFromConfiguration()
    {
        // Arrange: simulate appsettings.json with Workspace:SummarizePlaybookCode set
        // (task 012 added this key to the template).
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:SummarizePlaybookCode"] = "summarize-document-workspace"
        };
        var options = BuildBoundOptions(configData);

        // Assert
        options.SummarizePlaybookCode.Should().Be("summarize-document-workspace");
    }

    [Fact]
    public void SummarizePlaybookId_BindsFromConfiguration_BackwardCompat()
    {
        // Arrange: the existing IConfiguration["Workspace:SummarizePlaybookId"] key
        // (previously read via raw indexer at WorkspaceFileEndpoints.cs:30,254) must
        // still bind to the typed-options property for backward-compat until task 019
        // retires it.
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:SummarizePlaybookId"] = "4a72f99c-a119-f111-8343-7ced8d1dc988"
        };
        var options = BuildBoundOptions(configData);

        // Assert
        options.SummarizePlaybookId.Should().Be("4a72f99c-a119-f111-8343-7ced8d1dc988");
        options.SummarizePlaybookCode.Should().BeEmpty(
            "the code property should remain unset when only the legacy GUID key is configured");
    }

    [Fact]
    public void BothCodeAndId_CanCoexist_DuringMigration()
    {
        // Arrange: during the migration window between task 012 (this) and task 019,
        // both keys may be present simultaneously. Verify both bind cleanly.
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:SummarizePlaybookId"] = "4a72f99c-a119-f111-8343-7ced8d1dc988",
            ["Workspace:SummarizePlaybookCode"] = "summarize-document-workspace"
        };
        var options = BuildBoundOptions(configData);

        // Assert
        options.SummarizePlaybookId.Should().Be("4a72f99c-a119-f111-8343-7ced8d1dc988");
        options.SummarizePlaybookCode.Should().Be("summarize-document-workspace");
    }

    [Fact]
    public void ExistingPlaybookIdProperties_BindWithoutRegression()
    {
        // Arrange: verify task 012 did not break the existing PreFill / ProjectPreFill /
        // AiSummary playbook-ID properties.
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:PreFillPlaybookId"] = "2d660cad-d418-f111-8343-7ced8d1dc988",
            ["Workspace:ProjectPreFillPlaybookId"] = "00000000-0000-0000-0000-000000000001",
            ["Workspace:AiSummaryPlaybookId"] = "18cf3cc8-02ec-f011-8406-7c1e520aa4df"
        };
        var options = BuildBoundOptions(configData);

        // Assert
        options.PreFillPlaybookId.Should().Be("2d660cad-d418-f111-8343-7ced8d1dc988");
        options.ProjectPreFillPlaybookId.Should().Be("00000000-0000-0000-0000-000000000001");
        options.AiSummaryPlaybookId.Should().Be("18cf3cc8-02ec-f011-8406-7c1e520aa4df");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static WorkspaceOptions BuildBoundOptions(IDictionary<string, string?> configData)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.Configure<WorkspaceOptions>(
            configuration.GetSection(WorkspaceOptions.SectionName));
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<WorkspaceOptions>>().Value;
    }
}
