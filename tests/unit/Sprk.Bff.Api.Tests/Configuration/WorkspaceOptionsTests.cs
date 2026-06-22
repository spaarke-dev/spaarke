using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="WorkspaceOptions"/> binding (chat-routing-redesign-r1 task 012/013,
/// spec FR-04 + FR-05). Per Q&amp;A 2026-06-22 Q1, the Wave 1-A *PlaybookCode properties were
/// retired in favor of stable-ID *PlaybookId properties (GUID-format opaque IDs queried via
/// the <c>sprk_playbookid</c> alternate key). These tests assert the canonical stable-ID surface.
/// </summary>
public class WorkspaceOptionsTests
{
    [Fact]
    public void Defaults_PreExistingProperties_ShouldBeNull()
    {
        // Arrange + Act
        var options = new WorkspaceOptions();

        // Assert — the four pre-existing nullable stable-ID properties default to null
        // (services fall back to hardcoded GUIDs when unset).
        options.PreFillPlaybookId.Should().BeNull();
        options.ProjectPreFillPlaybookId.Should().BeNull();
        options.AiSummaryPlaybookId.Should().BeNull();
        options.SummarizePlaybookId.Should().BeNull();
    }

    [Fact]
    public void Defaults_ForChatRoutingR1IdProperties_AreEmptyString_PerQA_2026_06_22_Q1()
    {
        // Per Q&A 2026-06-22 Q1, the 2 new typed stable-ID properties default to empty string
        // (populated per-env at deploy time with the row's sprk_analysisplaybookid PK GUID).

        // Arrange + Act
        var options = new WorkspaceOptions();

        // Assert
        options.ChatSummarizePlaybookId.Should().BeEmpty(
            "ChatSummarizePlaybookId default is empty string; populated per-env at deploy time");
        options.MatterPreFillPlaybookId.Should().BeEmpty(
            "MatterPreFillPlaybookId default is empty string; populated per-env at deploy time");
    }

    [Fact]
    public void SectionName_IsWorkspace()
    {
        WorkspaceOptions.SectionName.Should().Be("Workspace");
    }

    [Fact]
    public void SummarizePlaybookId_BindsFromConfiguration()
    {
        // Arrange: SummarizePlaybookId is the canonical stable-ID lookup value
        // (Q&A 2026-06-22 Q1). Value is a GUID mirroring the row's sprk_analysisplaybookid PK.
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:SummarizePlaybookId"] = "4a72f99c-a119-f111-8343-7ced8d1dc988"
        };
        var options = BuildBoundOptions(configData);

        // Assert
        options.SummarizePlaybookId.Should().Be("4a72f99c-a119-f111-8343-7ced8d1dc988");
    }

    [Fact]
    public void ExistingPlaybookIdProperties_BindWithoutRegression()
    {
        // Arrange: verify the existing PreFill / ProjectPreFill / AiSummary playbook-ID
        // properties still bind correctly.
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
    // chat-routing-redesign-r1 task 013 + Q&A 2026-06-22 Q1 refactor —
    // 2 new typed stable-ID options (GUID-format; queried via sprk_playbookid alt-key)
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("Workspace:ChatSummarizePlaybookId", "11111111-1111-1111-1111-111111111111")]
    [InlineData("Workspace:MatterPreFillPlaybookId", "22222222-2222-2222-2222-222222222222")]
    public void ChatRoutingR1_IdProperties_BindFromConfiguration(string configKey, string overrideValue)
    {
        // Arrange: each of the new typed stable-ID properties should bind from its
        // corresponding Workspace:<Name> config key.
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
    public void ChatRoutingR1_IdProperties_RetainEmptyDefaults_WhenConfigKeysMissing()
    {
        // Arrange: empty configuration (no Workspace section at all)
        var options = BuildBoundOptions(new Dictionary<string, string?>());

        // Assert — empty string defaults per Q&A 2026-06-22 Q1
        // (per-env config will populate the GUID at deploy time).
        options.ChatSummarizePlaybookId.Should().BeEmpty();
        options.MatterPreFillPlaybookId.Should().BeEmpty();
    }

    [Fact]
    public void ChatRoutingR1_AllIdProperties_CoexistWithSummarizePlaybookId()
    {
        // Arrange: simulate the post-Q1-refactor appsettings template state — all stable-ID
        // properties present simultaneously. Verify all bind cleanly without interfering with
        // each other (the CRIT-1 invariant: WorkspaceOptions.cs supports the full wave 1-E
        // migration without further extension).
        var configData = new Dictionary<string, string?>
        {
            ["Workspace:SummarizePlaybookId"] = "4a72f99c-a119-f111-8343-7ced8d1dc988",
            ["Workspace:ChatSummarizePlaybookId"] = "33333333-3333-3333-3333-333333333333",
            ["Workspace:MatterPreFillPlaybookId"] = "44444444-4444-4444-4444-444444444444"
        };
        var options = BuildBoundOptions(configData);

        // Assert: all properties carry their configured values
        options.SummarizePlaybookId.Should().Be("4a72f99c-a119-f111-8343-7ced8d1dc988");
        options.ChatSummarizePlaybookId.Should().Be("33333333-3333-3333-3333-333333333333");
        options.MatterPreFillPlaybookId.Should().Be("44444444-4444-4444-4444-444444444444");
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
