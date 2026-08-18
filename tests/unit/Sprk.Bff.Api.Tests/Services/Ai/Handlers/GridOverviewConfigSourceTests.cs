using System;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// R4 UAT 2026-08-18 regression guard. The <c>spaarke.grid_overview</c> tool used to read a
/// <c>sprk_fetchxml</c> column that does NOT exist on <c>sprk_gridconfiguration</c> (the table stores
/// the query in <c>sprk_configjson</c> as a DataGrid-framework <c>source</c> block), so every call
/// errored and the advisory task-agenda answer had ZERO grounded data — the P1 "you have no tasks"
/// defect despite the grid widget showing them. <see cref="GridOverviewHandler.ParseGridSource"/> is the
/// pure parse that now resolves the real config shape; these lock its branches.
/// </summary>
public sealed class GridOverviewConfigSourceTests
{
    private static readonly Guid ConfigId = Guid.Parse("ac05e4f1-8d85-f111-8075-7c1e5268570d");

    [Fact]
    public void ParseGridSource_SavedQuerySource_YieldsSavedQueryIdToResolve()
    {
        // The shipped "My Tasks (Assistant)" config shape.
        const string json =
            """{"_version":"1.0","source":{"type":"savedquery","savedQueryId":"12a510e4-2517-f111-8343-7ced8d1dc988"}}""";

        var parsed = GridOverviewHandler.ParseGridSource(json, ConfigId);

        parsed.Error.Should().BeNull();
        parsed.InlineFetchXml.Should().BeNull();
        parsed.SavedQueryId.Should().Be("12a510e4-2517-f111-8343-7ced8d1dc988");
    }

    [Fact]
    public void ParseGridSource_InlineSource_YieldsFetchXmlDirectly()
    {
        const string json =
            """{"source":{"type":"inline","fetchXml":"<fetch><entity name='sprk_event'/></fetch>","layoutXml":"<grid/>"}}""";

        var parsed = GridOverviewHandler.ParseGridSource(json, ConfigId);

        parsed.Error.Should().BeNull();
        parsed.SavedQueryId.Should().BeNull();
        parsed.InlineFetchXml.Should().Be("<fetch><entity name='sprk_event'/></fetch>");
    }

    [Fact]
    public void ParseGridSource_SavedQuerySetSource_IsUnsupportedForOverview()
    {
        // savedquery-set (auto-discover multiple sibling views) is ambiguous for a single overview run.
        const string json = """{"source":{"type":"savedquery-set","entityLogicalName":"sprk_event"}}""";

        var parsed = GridOverviewHandler.ParseGridSource(json, ConfigId);

        parsed.Error.Should().Contain("not supported");
        parsed.InlineFetchXml.Should().BeNull();
        parsed.SavedQueryId.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseGridSource_EmptyConfigJson_ReturnsError(string? configJson)
    {
        GridOverviewHandler.ParseGridSource(configJson, ConfigId).Error.Should().Contain("sprk_configjson is empty");
    }

    [Fact]
    public void ParseGridSource_MalformedJson_ReturnsError()
    {
        GridOverviewHandler.ParseGridSource("{not json", ConfigId).Error.Should().Contain("malformed");
    }

    [Fact]
    public void ParseGridSource_NoSourceBlock_ReturnsError()
    {
        GridOverviewHandler.ParseGridSource("""{"_version":"1.0"}""", ConfigId).Error.Should().Contain("no 'source' block");
    }

    [Fact]
    public void ParseGridSource_SavedQueryWithoutId_ReturnsError()
    {
        GridOverviewHandler.ParseGridSource("""{"source":{"type":"savedquery"}}""", ConfigId)
            .Error.Should().Contain("no savedQueryId");
    }
}
