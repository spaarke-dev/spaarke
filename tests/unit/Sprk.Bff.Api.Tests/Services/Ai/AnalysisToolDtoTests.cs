using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// R6 Pillar 2 (task D-A-07 / FR-07) — DTO contract tests for the
/// AnalysisTool.AvailableInContexts discriminator + ToolAvailabilityContext enum
/// + Dataverse option-set value mapper.
///
/// These tests guard the AnalysisTool DTO contract that downstream tasks
/// (010 adapter, 011 chat resolver, 012 batch migration) all depend on.
/// </summary>
public class AnalysisToolDtoTests
{
    // -------------------------------------------------------------------------
    // Enum value contract — must match canonical Spaarke Dataverse option-set
    // values used by sprk_availableincontexts on sprk_analysistool.
    // Changing these values is a breaking change to Dataverse-side stored rows.
    // -------------------------------------------------------------------------

    [Fact]
    public void ToolAvailabilityContext_Playbook_MatchesDataverseOptionSetValue()
    {
        ((int)ToolAvailabilityContext.Playbook).Should().Be(100000000);
    }

    [Fact]
    public void ToolAvailabilityContext_Chat_MatchesDataverseOptionSetValue()
    {
        ((int)ToolAvailabilityContext.Chat).Should().Be(100000001);
    }

    [Fact]
    public void ToolAvailabilityContext_Both_MatchesDataverseOptionSetValue()
    {
        ((int)ToolAvailabilityContext.Both).Should().Be(100000002);
    }

    [Fact]
    public void ToolAvailabilityContext_HasExactlyThreeValues()
    {
        // FR-07 binding: exactly three discriminator values. If a fourth is added,
        // FR-07 has been amended and every consumer of AvailableInContexts must be reviewed.
        var values = Enum.GetValues<ToolAvailabilityContext>();
        values.Should().HaveCount(3);
        values.Should().BeEquivalentTo(new[]
        {
            ToolAvailabilityContext.Playbook,
            ToolAvailabilityContext.Chat,
            ToolAvailabilityContext.Both
        });
    }

    // -------------------------------------------------------------------------
    // DTO default — backward-compat per FR-07:
    // AvailableInContexts is nullable. Default DTO leaves it unset (null) and
    // consumers treat null as Playbook.
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_DefaultConstruction_AvailableInContextsIsNull()
    {
        // Pre-R6 rows in Dataverse won't have sprk_availableincontexts populated.
        // The DTO must default to null so the mapper can apply the
        // "null → Playbook" backward-compat rule at resolve time.
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-DocumentSearch",
            Type = ToolType.Custom
        };

        tool.AvailableInContexts.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Round-trip serialization — JSON serializer (System.Text.Json) must
    // preserve all three enum values + null. This is the over-the-wire
    // contract between BFF endpoints and clients.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ToolAvailabilityContext.Playbook)]
    [InlineData(ToolAvailabilityContext.Chat)]
    [InlineData(ToolAvailabilityContext.Both)]
    public void AnalysisTool_SerializeDeserialize_RoundTripsAvailableInContexts(ToolAvailabilityContext value)
    {
        // Arrange
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "TestTool",
            Type = ToolType.Custom,
            HandlerClass = "TestHandler",
            AvailableInContexts = value
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.AvailableInContexts.Should().Be(value);
    }

    [Fact]
    public void AnalysisTool_SerializeDeserialize_NullAvailableInContextsPreserved()
    {
        // Arrange — backward-compat invariant: null wire value remains null DTO value.
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "PreR6Tool",
            Type = ToolType.Custom,
            AvailableInContexts = null
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<AnalysisTool>(json);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.AvailableInContexts.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Dataverse option-set mapper — AnalysisToolService.MapAvailableInContexts
    // converts raw int? from Dataverse into ToolAvailabilityContext?.
    // -------------------------------------------------------------------------

    [Fact]
    public void MapAvailableInContexts_NullRaw_ReturnsNull()
    {
        // Pre-R6 rows whose column is unpopulated (or new rows that haven't
        // been migrated yet). FR-07 backward-compat — callers treat null as Playbook.
        AnalysisToolService.MapAvailableInContexts(null).Should().BeNull();
    }

    [Theory]
    [InlineData(100000000, ToolAvailabilityContext.Playbook)]
    [InlineData(100000001, ToolAvailabilityContext.Chat)]
    [InlineData(100000002, ToolAvailabilityContext.Both)]
    public void MapAvailableInContexts_KnownRaw_ReturnsCorrectEnum(int rawValue, ToolAvailabilityContext expected)
    {
        AnalysisToolService.MapAvailableInContexts(rawValue).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99999)]
    [InlineData(100000003)]
    public void MapAvailableInContexts_UnknownRaw_ReturnsNull(int rawValue)
    {
        // Forward-compat safety: an unknown option-set value (e.g., admin added a
        // new option that BFF doesn't yet know about) maps to null rather than
        // throwing. Callers fall back to Playbook (backward-compat per FR-07).
        AnalysisToolService.MapAvailableInContexts(rawValue).Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Record copy semantics — AnalysisTool is a positional record; the `with`
    // expression must preserve AvailableInContexts (verifies the property is
    // a true record member, not a forgotten manually-added field).
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalysisTool_WithExpression_PreservesAvailableInContexts()
    {
        var original = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "TestTool",
            Type = ToolType.Custom,
            AvailableInContexts = ToolAvailabilityContext.Chat
        };

        var modified = original with { Name = "RenamedTool" };

        modified.AvailableInContexts.Should().Be(ToolAvailabilityContext.Chat);
        modified.Name.Should().Be("RenamedTool");
    }
}
