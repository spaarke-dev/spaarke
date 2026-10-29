// unified-access-control-r2 #2427 — a row's OUTPUT lookup is never an ownership parent. An analysis's output file
// (sprk_analysis.sprk_outputfileid, the document the analysis was saved as) is produced BY the analysis; ownership follows
// filing, so the output is not the primary, not in secure-if-any, and not a re-file's parent. One definition
// (RecordOwnershipResolver.IsOutput) read by the writers' question (RecordOwnershipContext.ForRow), the reconciler's
// (ForExistingRowAsync / OrderedParentsOf) and the re-file's (ReparentAsync). These tests run the REAL resolver over the shared
// ownership directory.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

[Trait("status", "issue-2427-uac-r2")]
public class AnalysisOutputLookupTests
{
    private static readonly Guid GeneralBu = FakeDirectory.GeneralBu;
    private static readonly Guid ChildBu = FakeDirectory.ChildBu;
    private static readonly Guid SecureBu = FakeDirectory.SecureBu;
    private static readonly Guid GeneralTeam = FakeDirectory.GeneralTeam;
    private static readonly Guid ChildTeam = FakeDirectory.ChildTeam;
    private static readonly Guid SecureNamedTeam = FakeDirectory.SecureNamedTeam;

    /// <summary>Matter A: ordinary, in the general business unit.</summary>
    private static readonly Guid MatterA = Guid.Parse("24270000-0000-4000-8000-0000000000a1");

    /// <summary>Matter B: ordinary, in the child business unit.</summary>
    private static readonly Guid MatterB = Guid.Parse("24270000-0000-4000-8000-0000000000b1");

    /// <summary>A Secure matter.</summary>
    private static readonly Guid SecureMatter = Guid.Parse("24270000-0000-4000-8000-0000000000c1");

    /// <summary>The analysed (source) document: ordinary, filed under matter B.</summary>
    private static readonly Guid SourceDocument = Guid.Parse("24270000-0000-4000-8000-0000000000d1");

    /// <summary>An output file filed under the Secure matter.</summary>
    private static readonly Guid SecureOutput = Guid.Parse("24270000-0000-4000-8000-0000000000e1");

    /// <summary>An ordinary output file, filed under matter B.</summary>
    private static readonly Guid OrdinaryOutput = Guid.Parse("24270000-0000-4000-8000-0000000000e2");

    private static readonly Guid Analysis = Guid.Parse("24270000-0000-4000-8000-0000000000f1");

    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithRecord("sprk_matter", MatterA, GeneralBu, owningTeam: GeneralTeam)
        .WithRecord("sprk_matter", MatterB, ChildBu, owningTeam: ChildTeam)
        .WithRecord("sprk_matter", SecureMatter, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
        .WithRecord("sprk_document", SourceDocument, ChildBu, owningTeam: ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterB) })
        .WithRecord("sprk_document", SecureOutput, SecureBu, owningTeam: SecureNamedTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter) })
        .WithRecord("sprk_document", OrdinaryOutput, ChildBu, owningTeam: ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterB) });

    /// <summary>The analysis row as read or written: its document, its output file and its regarding matter (each optional).</summary>
    private static Entity AnalysisRow(Guid? document, Guid? output, Guid? regardingMatter)
    {
        var row = new Entity("sprk_analysis", Analysis);
        if (document is { } d)
            row["sprk_documentid"] = new EntityReference("sprk_document", d);
        if (output is { } o)
            row["sprk_outputfileid"] = new EntityReference("sprk_document", o);
        if (regardingMatter is { } m)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        return row;
    }

    private static readonly RecordOwnershipParent OutputParent = new("sprk_document", SecureOutput);

    // ── The one definition ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheAnalysisOutputFile_IsAnOutput_AndItsFilingLookupsAreNot()
    {
        RecordOwnershipResolver.IsOutput("sprk_analysis", "sprk_outputfileid").Should().BeTrue();
        RecordOwnershipResolver.IsOutput("SPRK_ANALYSIS", "Sprk_OutputFileId").Should().BeTrue("logical names compare case-insensitively");
        RecordOwnershipResolver.IsOutput("sprk_analysis", "sprk_documentid").Should().BeFalse();
        RecordOwnershipResolver.IsOutput("sprk_analysis", "sprk_regardingmatter").Should().BeFalse();
        RecordOwnershipResolver.IsOutput("sprk_document", "sprk_outputfileid").Should().BeFalse("the set is per (table, column)");
        RecordOwnershipResolver.IsOutput(null, "sprk_outputfileid").Should().BeFalse();
    }

    [Fact]
    public void ARefile_ReadsTheOutputAsALineageOnlyLookup_ThatStillMakesAPayloadACandidateRefile()
    {
        RecordReparent.LineageOnlyTargetOf("sprk_analysis", "sprk_outputfileid").Should().Be("sprk_document", "it files the row under nothing");
        RecordReparent.LineageOnlyTargetOf("sprk_analysis", "sprk_documentid").Should().BeNull();

        // A generic payload that binds or clears only the output still runs the re-file path, so the step after it (the Secure
        // mirror, which walks the output) runs as before #2427; the reparent decides no owner from it (below).
        var bound = RecordReparent.ParentChangesWithClearsIn("sprk_analysis", new Dictionary<string, object?>
        {
            ["sprk_OutputFileId@odata.bind"] = $"/sprk_documents({SecureOutput})",
        });
        var cleared = RecordReparent.ParentChangesWithClearsIn("sprk_analysis", new Dictionary<string, object?>
        {
            ["sprk_OutputFileId@odata.bind"] = null,
        });

        bound.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, EntityReference?>("sprk_outputfileid", new EntityReference("sprk_document", SecureOutput)));
        RecordReparent.MayRefile("sprk_analysis", bound).Should().BeTrue();
        RecordReparent.MayRefile("sprk_analysis", cleared).Should().BeTrue();
    }

    // ── The question ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithNoDocument_TheOutputFileIsNotOffered_AndIsNotThePrimary()
    {
        var row = AnalysisRow(document: null, output: SecureOutput, regardingMatter: MatterA);

        var asked = RecordOwnershipContext.ForChild(row);

        asked.Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", MatterA));
        asked.TargetEntityLogicalName.Should().Be("sprk_matter", "the output ranked ahead of every sprk_regarding* lookup before #2427");
        asked.SecureOnlyParents.Should().BeEmpty();
        RecordOwnershipContext.ParentsOf(row).Should().NotContain(OutputParent);
        RecordOwnershipContext.OrderedParentsOf(row).Should().NotContain(OutputParent);
    }

    [Fact]
    public void WithOnlyTheOutputFile_NothingIsOffered()
    {
        var row = AnalysisRow(document: null, output: SecureOutput, regardingMatter: null);

        RecordOwnershipContext.ForChild(row).Parents.Should().BeEmpty();
        RecordOwnershipContext.ForChild(row).HasParent.Should().BeFalse();
        RecordOwnershipContext.ParentsOf(row).Should().BeEmpty();
    }

    [Fact]
    public void WithBoth_ThePrimaryIsTheDocument_AndTheOutputIsNotInTheParentListAtAll()
    {
        var row = AnalysisRow(document: SourceDocument, output: SecureOutput, regardingMatter: MatterA);

        var asked = RecordOwnershipContext.ForChild(row);

        asked.Parents.Should().Equal(
            new RecordOwnershipParent("sprk_document", SourceDocument), new RecordOwnershipParent("sprk_matter", MatterA));
        asked.TargetRecordId.Should().Be(SourceDocument);
        asked.Parents.Should().NotContain(OutputParent);
        asked.SecureOnlyParents.Should().NotContain(OutputParent);
    }

    [Fact]
    public async Task TheReconcilersExistingRowQuestion_IsTheWritersQuestion()
    {
        var row = AnalysisRow(document: SourceDocument, output: SecureOutput, regardingMatter: null);

        var question = await RecordOwnershipContext.ForExistingRowAsync(
            row, new ExistingRowReads(Moq.Mock.Of<Spaarke.Dataverse.IGenericEntityService>(), null, null), CancellationToken.None);

        question.Undetermined.Should().BeNull();
        question.Context.Parents.Should().Equal(RecordOwnershipContext.ForChild(row).Parents);
        question.Context.Parents.Should().Equal(AppOnlyAnalysisService.OwnershipOfNewAnalysis(SourceDocument).Parents,
            "the analysis's writer names its document alone; the output it later gains asks nothing new");
        question.Context.SecureOnlyParents.Should().BeEmpty();
        RecordOwnershipContext.OrderedParentsOf(row).Should().Equal(question.Context.Parents);
    }

    // ── The answer (the real resolver): ownership follows filing, so an output is not in secure-if-any ────────────────

    [Fact]
    public async Task AnOrdinaryAnalysis_WithASecureOutputFile_StaysOrdinary()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(AnalysisRow(SourceDocument, SecureOutput, null)), CancellationToken.None);

        resolution.IsSecureOwner.Should().BeFalse("the analysis is filed under an ordinary document; its output files it under nothing");
        resolution.OwningTeamId.Should().Be(ChildTeam, "the analysed document's team decides");
    }

    [Fact]
    public async Task AnAnalysisUnderASecureMatter_WithAnOrdinaryOutputFile_IsSecure_ThroughItsFiling()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(AnalysisRow(SourceDocument, OrdinaryOutput, SecureMatter)), CancellationToken.None);

        resolution.IsSecureOwner.Should().BeTrue("secure-if-any over its filing (the Secure matter)");
        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task AnAnalysisFiledUnderNothingButItsOutputFile_TakesTheUnfiledAnswer_NeverTheOutputsTeam()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(AnalysisRow(null, SecureOutput, null)) with { WhenUnfiled = UnfiledOwnership.KeepCreator },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "unfiled: the reconciler's answer keeps the creator");
        resolution.OwningTeamId.Should().NotBe(SecureNamedTeam);
    }

    // ── The re-file ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReFilingAnAnalysisWithASecureOutputFile_FromAToB_FollowsItsFiling_NotTheOutput()
    {
        var directory = World()
            .WithRecord("sprk_analysis", Analysis, GeneralBu, owningTeam: GeneralTeam, extra: new()
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterA),
                ["sprk_outputfileid"] = new EntityReference("sprk_document", SecureOutput),
            });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_analysis",
            RecordId = Analysis,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterB) },
            CallerObjectId = FakeDirectory.CallerOid,
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_analysis", Analysis,
                    new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterB) });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.IsSecureOwner.Should().BeFalse("the output file is not a parent, so its Secure matter decides nothing");
        resolution.OwningTeamId.Should().Be(ChildTeam, "matter B decides");
        directory.Assignments.Should().NotContain(a => a.TeamId == SecureNamedTeam);
    }

    [Fact]
    public async Task SettingOnlyTheOutputFile_IsNoParentChange_AndReOwnsNothing()
    {
        var directory = World()
            .WithRecord("sprk_analysis", Analysis, ChildBu, owningTeam: ChildTeam, extra: new()
            {
                ["sprk_documentid"] = new EntityReference("sprk_document", SourceDocument),
            });
        var written = false;
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_analysis",
            RecordId = Analysis,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_outputfileid"] = new EntityReference("sprk_document", SecureOutput) },
            CallerObjectId = FakeDirectory.CallerOid,
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                written = true;
                directory.Write("sprk_analysis", Analysis,
                    new Dictionary<string, object?> { ["sprk_outputfileid"] = new EntityReference("sprk_document", SecureOutput) });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        written.Should().BeTrue("the change itself is written");
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, resolution.Reason);
        resolution.ParentsUnchanged.Should().BeFalse("a lineage lookup moved: the step after a re-file (the mirror) must still run");
        directory.Assignments.Should().BeEmpty();
    }
}
