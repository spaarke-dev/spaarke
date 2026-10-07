using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Domain tests for the ADR-024 <c>sprk_analysis</c> regarding field-stager
/// (<see cref="DataverseServiceClientImpl.StageAnalysisRegardingFields"/>) — the LOAD-BEARING write
/// behind FR-D9 "Set related record". These exercise the ACTUAL attribute write (a typo'd field name,
/// wrong <see cref="EntityReference"/> target, or a staged-but-nonexistent attribute would pass the
/// endpoint contract tests — which mock <c>CreateAnalysisAsync</c> — and only fail on live deploy).
///
/// The stager is pure (no ServiceClient / no I/O), so it is testable directly by capturing the staged
/// <see cref="Entity"/> attributes — mirroring the <c>TodoRegardingBuilderTests</c> precedent and
/// avoiding the tests/CLAUDE.md B8 ban on internal/reflection tests.
///
/// KEEP path: tests/unit/domain/** (ADR-038 §2 #7 — pure domain mapping logic).
/// </summary>
public class AnalysisRegardingWriteTests
{
    private static readonly Guid RecordTypeRefId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    [Fact]
    public void StageAnalysisRegardingFields_ForMatter_WritesMatterLookupAndFourResolverFields()
    {
        var analysis = new Entity("sprk_analysis");
        var matterId = Guid.NewGuid();
        var target = new AnalysisRegardingTarget("sprk_matter", matterId, "MAT-100");

        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            analysis, target,
            resolvedName: "Acme Holdings",       // true name retrieved from the matter (SRFR-052)
            resolvedNumber: "MAT-100",
            recordTypeRefId: RecordTypeRefId,
            recordTypeRefName: "Matter");

        // Entity-specific lookup — the load-bearing field for the Analyses-tab subgrid relationship.
        var lookup = analysis["sprk_regardingmatter"].Should().BeOfType<EntityReference>().Subject;
        lookup.LogicalName.Should().Be("sprk_matter");
        lookup.Id.Should().Be(matterId);

        var cleanId = matterId.ToString("D").ToLowerInvariant();
        analysis["sprk_regardingrecordid"].Should().Be(cleanId);
        analysis["sprk_regardingrecordname"].Should().Be("Acme Holdings");
        analysis["sprk_regardingrecordnumber"].Should().Be("MAT-100");

        var typeRef = analysis["sprk_regardingrecordtype"].Should().BeOfType<EntityReference>().Subject;
        typeRef.LogicalName.Should().Be("sprk_recordtype_ref");
        typeRef.Id.Should().Be(RecordTypeRefId);
        typeRef.Name.Should().Be("Matter");
    }

    [Fact]
    public void StageAnalysisRegardingFields_ForProject_WritesProjectLookupAndResolverFields()
    {
        var analysis = new Entity("sprk_analysis");
        var projectId = Guid.NewGuid();
        var target = new AnalysisRegardingTarget("sprk_project", projectId, "PRJ-42");

        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            analysis, target,
            resolvedName: "Website Rebuild",
            resolvedNumber: "PRJ-42",
            recordTypeRefId: RecordTypeRefId,
            recordTypeRefName: "Project");

        var lookup = analysis["sprk_regardingproject"].Should().BeOfType<EntityReference>().Subject;
        lookup.LogicalName.Should().Be("sprk_project");
        lookup.Id.Should().Be(projectId);

        analysis.Contains("sprk_regardingmatter").Should().BeFalse("project branch must not set the matter lookup");
        analysis["sprk_regardingrecordid"].Should().Be(projectId.ToString("D").ToLowerInvariant());
        analysis["sprk_regardingrecordname"].Should().Be("Website Rebuild");
        analysis["sprk_regardingrecordnumber"].Should().Be("PRJ-42");
    }

    [Fact]
    public void StageAnalysisRegardingFields_ForAnyTarget_NeverWritesRegardingRecordUrl()
    {
        // W1 regression guard: sprk_analysis does NOT carry sprk_regardingrecordurl (unlike sprk_event/
        // sprk_todo/sprk_communication). Staging it would 500 every regarding promote at CreateAsync.
        var matter = new Entity("sprk_analysis");
        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            matter, new AnalysisRegardingTarget("sprk_matter", Guid.NewGuid(), "MAT-1"),
            "Acme", "MAT-1", RecordTypeRefId, "Matter");
        matter.Contains("sprk_regardingrecordurl").Should().BeFalse();

        var project = new Entity("sprk_analysis");
        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            project, new AnalysisRegardingTarget("sprk_project", Guid.NewGuid(), "PRJ-1"),
            "Rebuild", "PRJ-1", RecordTypeRefId, "Project");
        project.Contains("sprk_regardingrecordurl").Should().BeFalse();
    }

    [Fact]
    public void StageAnalysisRegardingFields_WhenResolvedNameNull_FallsBackToPickerName()
    {
        var analysis = new Entity("sprk_analysis");
        var target = new AnalysisRegardingTarget("sprk_matter", Guid.NewGuid(), "Picker Fallback Name");

        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            analysis, target,
            resolvedName: null,        // target-record retrieve yielded nothing
            resolvedNumber: null,
            recordTypeRefId: RecordTypeRefId,
            recordTypeRefName: "Matter");

        analysis["sprk_regardingrecordname"].Should().Be("Picker Fallback Name");
    }

    [Fact]
    public void StageAnalysisRegardingFields_WhenResolvedNumberNull_OmitsRecordNumber()
    {
        var analysis = new Entity("sprk_analysis");
        var target = new AnalysisRegardingTarget("sprk_matter", Guid.NewGuid(), "Acme");

        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            analysis, target, "Acme", resolvedNumber: null, RecordTypeRefId, "Matter");

        analysis.Contains("sprk_regardingrecordnumber").Should()
            .BeFalse("a null/blank number is graceful-blank, not an empty write");
    }

    [Fact]
    public void StageAnalysisRegardingFields_WhenRecordTypeRefNull_OmitsRecordType()
    {
        var analysis = new Entity("sprk_analysis");
        var target = new AnalysisRegardingTarget("sprk_matter", Guid.NewGuid(), "Acme");

        DataverseServiceClientImpl.StageAnalysisRegardingFields(
            analysis, target, "Acme", "MAT-1", recordTypeRefId: null, recordTypeRefName: null);

        // Soft-fail: the load-bearing lookup is still set even when recordtype-ref resolution failed.
        analysis.Contains("sprk_regardingrecordtype").Should().BeFalse();
        analysis["sprk_regardingmatter"].Should().BeOfType<EntityReference>();
    }

    [Fact]
    public void StageAnalysisRegardingFields_ForUnsupportedEntityType_Throws()
    {
        var analysis = new Entity("sprk_analysis");
        var target = new AnalysisRegardingTarget("account", Guid.NewGuid(), "Acme Inc");

        var act = () => DataverseServiceClientImpl.StageAnalysisRegardingFields(
            analysis, target, "Acme Inc", null, RecordTypeRefId, "Account");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unsupported regarding entity type 'account'*");
    }

    // =========================================================================
    // Task 097 round 10: the I/O half — PopulateAnalysisRegardingAsync with its two Dataverse reads passed in.
    // =========================================================================

    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter", "sprk_mattername", "sprk_matternumber")]
    [InlineData("sprk_project", "sprk_regardingproject", "sprk_projectname", "sprk_projectnumber")]
    public async Task PopulateAnalysisRegarding_ReadsTheNameAndTheNumberColumnTheCatalogNames(
        string entity, string lookupField, string nameField, string numberField)
    {
        var id = Guid.NewGuid();
        var reads = new List<string[]>();
        var analysis = new Entity("sprk_analysis");

        await DataverseServiceClientImpl.PopulateAnalysisRegardingAsync(
            analysis,
            new AnalysisRegardingTarget(entity, id, "picker label"),
            (_, _) => Task.FromResult<Entity?>(CatalogRow(numberField)),
            (_, _, columns, _) =>
            {
                reads.Add(columns);
                return Task.FromResult(new Entity(entity, id) { [nameField] = "zz-097 name", [numberField] = "NUM-7" });
            },
            NullLogger.Instance,
            CancellationToken.None);

        reads.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { nameField, numberField });
        analysis[lookupField].Should().BeOfType<EntityReference>().Which.Id.Should().Be(id);
        analysis["sprk_regardingrecordname"].Should().Be("zz-097 name");
        analysis["sprk_regardingrecordnumber"].Should().Be("NUM-7");
        analysis["sprk_regardingrecordtype"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(RecordTypeRefId);
    }

    [Fact]
    public async Task PopulateAnalysisRegarding_ACatalogRowThatNamesNoNumberColumn_ReadsTheNameOnly()
    {
        var id = Guid.NewGuid();
        var reads = new List<string[]>();
        var analysis = new Entity("sprk_analysis");

        await DataverseServiceClientImpl.PopulateAnalysisRegardingAsync(
            analysis,
            new AnalysisRegardingTarget("sprk_matter", id, "picker label"),
            (_, _) => Task.FromResult<Entity?>(CatalogRow(numberField: null)),
            (_, _, columns, _) =>
            {
                reads.Add(columns);
                return Task.FromResult(new Entity("sprk_matter", id) { ["sprk_mattername"] = "zz-097 matter" });
            },
            NullLogger.Instance,
            CancellationToken.None);

        reads.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { "sprk_mattername" });
        analysis["sprk_regardingrecordname"].Should().Be("zz-097 matter");
        analysis.Contains("sprk_regardingrecordnumber").Should().BeFalse();
        analysis.Contains("sprk_regardingrecordtype").Should().BeTrue();
    }

    [Fact]
    public async Task PopulateAnalysisRegarding_ACatalogReadThatThrows_StillWritesTheRecordsName()
    {
        // Round 10 item 3: on dcdc83995 the catalog read sat ahead of the name read inside ONE try, so a
        // TimeoutException lost the resolved name (the stager then fell back to the picker label).
        var id = Guid.NewGuid();
        var analysis = new Entity("sprk_analysis");

        await DataverseServiceClientImpl.PopulateAnalysisRegardingAsync(
            analysis,
            new AnalysisRegardingTarget("sprk_matter", id, "MAT-100"), // a matter picker's label is its NUMBER (SRFR-052)
            (_, _) => Task.FromException<Entity?>(new TimeoutException("catalog timed out")),
            (_, _, _, _) => Task.FromResult(new Entity("sprk_matter", id) { ["sprk_mattername"] = "Acme Holdings" }),
            NullLogger.Instance,
            CancellationToken.None);

        analysis["sprk_regardingrecordname"].Should().Be("Acme Holdings");
        analysis.Contains("sprk_regardingrecordnumber").Should().BeFalse();
        analysis.Contains("sprk_regardingrecordtype").Should().BeFalse();
        analysis["sprk_regardingmatter"].Should().BeOfType<EntityReference>();
    }

    [Fact]
    public async Task PopulateAnalysisRegarding_ACatalogNamingAColumnTheRecordLacks_StillWritesTheRecordsName()
    {
        // Round 10 item 6: the combined read faults on the bad column; the name is read again on its own.
        var id = Guid.NewGuid();
        var analysis = new Entity("sprk_analysis");

        await DataverseServiceClientImpl.PopulateAnalysisRegardingAsync(
            analysis,
            new AnalysisRegardingTarget("sprk_matter", id, "MAT-100"),
            (_, _) => Task.FromResult<Entity?>(CatalogRow("sprk_nosuchnumber")),
            (_, _, columns, _) => columns.Contains("sprk_nosuchnumber")
                ? Task.FromException<Entity>(new InvalidOperationException(
                    "'sprk_matter' entity doesn't contain attribute with Name = 'sprk_nosuchnumber'"))
                : Task.FromResult(new Entity("sprk_matter", id) { ["sprk_mattername"] = "Acme Holdings" }),
            NullLogger.Instance,
            CancellationToken.None);

        analysis["sprk_regardingrecordname"].Should().Be("Acme Holdings");
        analysis.Contains("sprk_regardingrecordnumber").Should().BeFalse();
        analysis.Contains("sprk_regardingrecordtype").Should().BeTrue();
    }

    private static Entity CatalogRow(string? numberField)
    {
        var row = new Entity("sprk_recordtype_ref", RecordTypeRefId) { ["sprk_recorddisplayname"] = "Matter" };
        if (numberField is not null)
            row["sprk_regardingrecordnumberfield"] = numberField;
        return row;
    }
}