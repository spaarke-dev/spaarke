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
    public void Defaults_ForChatRoutingR1CodeProperties_MatchSpec_Section_1_7_3()
    {
        // chat-routing-redesign-r1 task 013 (CRIT-1 fix): the 4 new stable-code
        // properties have explicit defaults matching spec §1.7.3 codes table.
        // AiSummaryPlaybookCode is intentionally empty pending task 018.

        // Arrange + Act
        var options = new WorkspaceOptions();

        // Assert
        options.ChatSummarizePlaybookCode.Should().Be("summarize-document-chat",
            "spec §1.7.3 codes table fixes the chat-side summarize code (FR-05)");
        options.MatterPreFillPlaybookCode.Should().Be("create-matter-prefill",
            "spec §1.7.3 codes table fixes the matter pre-fill code (FR-02 + NFR-07)");
        options.ProjectPreFillPlaybookCode.Should().Be("create-project-prefill",
            "spec §1.7.3 codes table fixes the project pre-fill code (FR-02 + NFR-07)");
        options.AiSummaryPlaybookCode.Should().BeEmpty(
            "AiSummaryPlaybookCode value is confirmed in task 018; default to empty until then");
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
    // chat-routing-redesign-r1 task 013 (CRIT-1 fix) — 4 new typed code options
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("Workspace:ChatSummarizePlaybookCode", "summarize-document-chat-override")]
    [InlineData("Workspace:MatterPreFillPlaybookCode", "create-matter-prefill-override")]
    [InlineData("Workspace:ProjectPreFillPlaybookCode", "create-project-prefill-override")]
    [InlineData("Workspace:AiSummaryPlaybookCode", "ai-summary-override")]
    public void ChatRoutingR1_CodeProperties_BindFromConfiguration(string configKey, string overrideValue)
    {
        // Arrange: each of the 4 new typed code properties should bind from its
        // corresponding Workspace:<Name> config key. Overrides are required because
        // the properties carry non-empty defaults (spec §1.7.3 codes table) — we
        // need to confirm the binding *actually* picks the configured value rather
        // than silently returning the default.
        var configData = new Dictionary<string, string?>
        {
            [configKey] = overrideValue
        };
        var options = BuildBoundOptions(configData);

        // Assert: read back the matching property
        var propName = configKey.Split(':')[1];
        var actual = typeof(WorkspaceOptions).GetProperty(propName)!.GetValue(options) as string;
        actual.Should().Be(overrideValue,
            $"config key {configKey} should bind to WorkspaceOptions.{propName}");
    }

    [Fact]
    public void ChatRoutingR1_CodeProperties_RetainDefaults_WhenConfigKeysMissing()
    {
        // Arrange: empty configuration (no Workspace section at all)
        var options = BuildBoundOptions(new Dictionary<string, string?>());

        // Assert — defaults applied per spec §1.7.3 (and empty for AiSummaryPlaybookCode
        // pending task 018).
        options.ChatSummarizePlaybookCode.Should().Be("summarize-document-chat");
        options.MatterPreFillPlaybookCode.Should().Be("create-matter-prefill");
        options.ProjectPreFillPlaybookCode.Should().Be("create-project-prefill");
        options.AiSummaryPlaybookCode.Should().BeEmpty();
    }

    [Fact]
    public void ChatRoutingR1_AllFourCodeProperties_CoexistWithSummarizePlaybookCode()
    {
        // Arrange: simulate the post-task-013 appsettings template state — all 5
        // code properties present simultaneously (task 012's SummarizePlaybookCode
        // + task 013's 4 new). Verify all bind cleanly without interfering with
        // each other (the CRIT-1 invariant: WorkspaceOptions.cs supports the full
        // wave 1-E migration without further extension).
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:SummarizePlaybookCode"] = "summarize-document-workspace",
            ["Workspace:ChatSummarizePlaybookCode"] = "summarize-document-chat",
            ["Workspace:MatterPreFillPlaybookCode"] = "create-matter-prefill",
            ["Workspace:ProjectPreFillPlaybookCode"] = "create-project-prefill",
            ["Workspace:AiSummaryPlaybookCode"] = "ai-summary-future-code"
        };
        var options = BuildBoundOptions(configData);

        // Assert: all 5 properties carry their configured values
        options.SummarizePlaybookCode.Should().Be("summarize-document-workspace");
        options.ChatSummarizePlaybookCode.Should().Be("summarize-document-chat");
        options.MatterPreFillPlaybookCode.Should().Be("create-matter-prefill");
        options.ProjectPreFillPlaybookCode.Should().Be("create-project-prefill");
        options.AiSummaryPlaybookCode.Should().Be("ai-summary-future-code");
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
