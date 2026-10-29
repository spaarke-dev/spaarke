// unified-access-control-r2 task 194b V-fix 3 (coordinator ruling, 2026-10-28) — an editable copy's link to the byte-identical
// canonical it was saved as (sprk_document.sprk_canonicaldocument) is a SECURE-ONLY parent: it counts for secure-if-any (a copy
// of Secure content is Secure) and never as the ordinary primary (an ordinary canonical never hands the saver's copy to the
// canonical's team, task 146). One definition (RecordOwnershipResolver.IsSecureOnlyParent) read by the writers' question
// (RecordOwnershipContext.ForRow), the reconciler's (ForExistingRowAsync) and the re-file's (ReparentAsync). These tests run the
// REAL resolver over the shared ownership directory.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

[Trait("status", "task-194b-uac-r2")]
public class CanonicalCopySecureOnlyParentTests
{
    private static readonly Guid GeneralBu = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid ChildBu = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");
    private static readonly Guid SecureBu = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000001");
    private static readonly Guid GeneralTeam = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid ChildTeam = Guid.Parse("cf15f587-baa0-f111-aaac-000d3a99d1d7");
    private static readonly Guid SecureNamedTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000003");

    /// <summary>Matter A: ordinary, in the general business unit. The copy is filed here.</summary>
    private static readonly Guid MatterA = Guid.Parse("194b0003-0000-4000-8000-0000000000a1");

    /// <summary>Matter B: ordinary, in the child business unit. The ordinary canonical is filed here.</summary>
    private static readonly Guid MatterB = Guid.Parse("194b0003-0000-4000-8000-0000000000b1");

    /// <summary>A Secure matter. The Secure canonical is filed here.</summary>
    private static readonly Guid SecureMatter = Guid.Parse("194b0003-0000-4000-8000-0000000000c1");

    private static readonly Guid OrdinaryCanonical = Guid.Parse("194b0003-0000-4000-8000-0000000000d1");
    private static readonly Guid SecureCanonical = Guid.Parse("194b0003-0000-4000-8000-0000000000d2");
    private static readonly Guid Copy = Guid.Parse("194b0003-0000-4000-8000-0000000000e1");

    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithRecord("sprk_matter", MatterA, GeneralBu, owningTeam: GeneralTeam)
        .WithRecord("sprk_matter", MatterB, ChildBu, owningTeam: ChildTeam)
        .WithRecord("sprk_matter", SecureMatter, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
        .WithRecord("sprk_document", OrdinaryCanonical, ChildBu, owningTeam: ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterB) })
        .WithRecord("sprk_document", SecureCanonical, SecureBu, owningTeam: SecureNamedTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter) });

    /// <summary>The copy as a writer writes it: filed under <paramref name="matter"/> (or nothing), linked to its canonical.</summary>
    private static Entity CopyRow(Guid? matter, Guid canonical)
    {
        var row = new Entity("sprk_document", Copy)
        {
            ["sprk_canonicaldocument"] = new EntityReference("sprk_document", canonical),
        };
        if (matter is { } m)
            row["sprk_matter"] = new EntityReference("sprk_matter", m);
        return row;
    }

    // ── The question ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCanonicalLink_IsASecureOnlyParent_OutOfThePrimaryOrder_InBothTheWritersAndTheReconcilersQuestion()
    {
        var written = CopyRow(MatterA, OrdinaryCanonical);

        var asked = RecordOwnershipContext.ForChild(written);

        RecordOwnershipResolver.IsSecureOnlyParent("sprk_document", "sprk_canonicaldocument").Should().BeTrue();
        RecordOwnershipResolver.IsSecureOnlyParent("sprk_document", "sprk_parentdocument").Should().BeFalse();
        asked.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(written));
        asked.SecureOnlyParents.Should().Equal(RecordOwnershipContext.SecureOnlyParentsOf(written));
        asked.SecureOnlyParents.Should().ContainSingle().Which.Should().Be(new RecordOwnershipParent("sprk_document", OrdinaryCanonical));
        asked.Parents.Should().NotContain(new RecordOwnershipParent("sprk_document", OrdinaryCanonical), "never the primary");
        asked.TargetEntityLogicalName.Should().Be("sprk_matter");
    }

    [Fact]
    public async Task TheReconcilersQuestion_CarriesTheSameSecureOnlyParents()
    {
        var written = CopyRow(null, OrdinaryCanonical);

        var question = await RecordOwnershipContext.ForExistingRowAsync(
            written, new ExistingRowReads(Moq.Mock.Of<Spaarke.Dataverse.IGenericEntityService>(), null, null), CancellationToken.None);

        question.Context.Parents.Should().BeEmpty();
        question.Context.SecureOnlyParents.Should().Equal(RecordOwnershipContext.ForChild(written).SecureOnlyParents);
    }

    // ── The answer (the real resolver) ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACopyOfACanonicalUnderASecureMatter_FiledUnderOrdinaryA_IsOwnedByTheSecureTeam()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(CopyRow(MatterA, SecureCanonical)), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam, "a copy of Secure content is Secure (secure-if-any)");
        resolution.IsSecureOwner.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnfiledCopyOfACanonicalUnderASecureMatter_IsOwnedByTheSecureTeam()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(CopyRow(null, SecureCanonical)) with { WhenUnfiled = UnfiledOwnership.KeepCreator },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam, "with no other parent, a Secure canonical still makes the copy Secure");
    }

    [Fact]
    public async Task ACopyFiledUnderOrdinaryA_OfACanonicalUnderOrdinaryB_StaysInA()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(CopyRow(MatterA, OrdinaryCanonical)), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(GeneralTeam, "the primary (matter A) decides; the canonical is never the primary");
    }

    [Fact]
    public async Task AnUnfiledCopyOfAnOrdinaryCanonical_TakesTheUnfiledAnswer_NeverTheCanonicalsBusinessUnit()
    {
        var resolver = World().Resolver();

        var keepCreator = await resolver.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(CopyRow(null, OrdinaryCanonical)) with { WhenUnfiled = UnfiledOwnership.KeepCreator },
            CancellationToken.None);
        var noCaller = await resolver.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(CopyRow(null, OrdinaryCanonical)), CancellationToken.None);

        keepCreator.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "the reconciler's unfiled answer: the row keeps its creator");
        noCaller.Outcome.Should().Be(RecordOwnerOutcome.Refused, "a writer's unfiled answer is its caller's team; with none, a refusal");
        noCaller.RefusalCode.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        noCaller.OwningTeamId.Should().NotBe(ChildTeam);
        keepCreator.OwningTeamId.Should().NotBe(ChildTeam, "the canonical's business unit (B) never decides the copy");
    }

    [Fact]
    public async Task ACopyWhoseCanonicalCannotBeRead_IsRefused_AsAnyUnreadableParentIs()
    {
        var resolution = await World().Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(CopyRow(MatterA, Guid.NewGuid())), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused, "the canonical that cannot be read might be the Secure one");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact]
    public async Task ALookThroughAUserOwnedUnfiledCopy_FollowsItsPrimaryOrderOnly_NeverItsCanonical()
    {
        // A file version of a copy that is not team-owned: the copy's own filing decides (the look-through), in its primary
        // order alone. The copy is filed under nothing but its canonical, so it keeps its own business unit, never B's.
        var version = Guid.NewGuid();
        var resolver = World()
            .WithRecord("sprk_document", Copy, GeneralBu, extra: new()
            {
                ["sprk_canonicaldocument"] = new EntityReference("sprk_document", OrdinaryCanonical),
            })
            .Resolver();

        var resolution = await resolver.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(new Entity("sprk_fileversion", version)
            {
                ["sprk_document"] = new EntityReference("sprk_document", Copy),
            }),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(GeneralTeam, "the copy's own business unit, not its canonical's (B)");
    }

    // ── The re-file ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReFilingACopyOutOfMatterA_WhileItKeepsAnOrdinaryCanonicalInB_TakesTheUnfiledAnswer_NotB()
    {
        // FakeDirectory.Standard() holds the caller (CallerOid) in the general business unit.
        var directory = World()
            .WithRecord("sprk_document", Copy, GeneralBu, owningTeam: GeneralTeam, extra: new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterA),
                ["sprk_canonicaldocument"] = new EntityReference("sprk_document", OrdinaryCanonical),
            });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = Copy,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = null },
            CallerObjectId = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe"),
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_document", Copy, new Dictionary<string, object?> { ["sprk_matter"] = null });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(GeneralTeam, "unfiled: the caller's team (general), never the canonical's (B)");
        directory.Assignments.Should().NotContain(a => a.TeamId == ChildTeam);
    }

    [Fact]
    public async Task ReFilingACopyOfASecureCanonical_FromAToB_StaysWithTheSecureTeam_AndIsNotReadAsLeavingTheSecureRoot()
    {
        var directory = World()
            .WithRecord("sprk_document", Copy, SecureBu, owningTeam: SecureNamedTeam, extra: new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterA),
                ["sprk_canonicaldocument"] = new EntityReference("sprk_document", SecureCanonical),
            });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_document",
            RecordId = Copy,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", MatterB) },
            CallerObjectId = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe"),
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_document", Copy,
                    new Dictionary<string, object?> { ["sprk_matter"] = new EntityReference("sprk_matter", MatterB) });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(
            "the copy keeps its canonical, so it leaves no secure root and needs no F3 rights: " + resolution.Reason);
        resolution.OwningTeamId.Should().Be(SecureNamedTeam, "a copy of Secure content stays Secure wherever it is filed");
        directory.Assignments.Should().NotContain(a => a.TeamId == ChildTeam);
    }
}
