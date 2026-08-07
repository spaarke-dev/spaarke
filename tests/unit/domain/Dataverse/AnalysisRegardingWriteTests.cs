using System;
using FluentAssertions;
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
}
