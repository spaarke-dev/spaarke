using System;
using System.Collections.Generic;
using System.Linq;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// Pins the <c>sprk_document</c> record-link vocabulary and the copy-forward semantics.
/// </summary>
/// <remarks>
/// The defect these guard against: the vocabulary used to be declared twice and both copies had drifted
/// to 6 of the table's 17 link columns, so Compose create-on-save silently dropped a document's filing
/// for ten link types. Nothing failed — the new document was simply not where the user filed the source.
/// </remarks>
public sealed class DocumentLinkFieldMapTests
{
    /// <summary>
    /// The live column set from <c>describe('tables/sprk_document')</c>, 2026-09-04. Pinned as data so a
    /// drop or a silent rename is a red test rather than a quiet behaviour change.
    /// </summary>
    private static readonly string[] LiveLinkColumns =
    [
        "sprk_relatedagreement", "sprk_relatedcommunication", "sprk_relatedcontact", "sprk_relatedevent",
        "sprk_relatedinvoice", "sprk_relatedmatter", "sprk_relatedorganization", "sprk_relatedproject",
        "sprk_relatedservicerequest", "sprk_relatedtodo", "sprk_relatedvendororg",
        "sprk_relatedworkassignment", "sprk_email",
        "sprk_matter", "sprk_project", "sprk_invoice", "sprk_workassignment",
    ];

    [Fact]
    public void Map_CoversEveryLinkColumnOnTheLiveSchema()
    {
        Assert.Equal(
            LiveLinkColumns.OrderBy(x => x, StringComparer.Ordinal),
            DocumentLinkFieldMap.AllAttributes.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void TheFourUnprefixedColumns_AreMarkedLegacy_AndNameTheSiblingThatSupersedesThem()
    {
        // Recorded, not acted on: nothing redirects writes (see ProjectForCopy). This states WHICH column
        // replaces each retired one, so a future deliberate migration has the mapping and does not have to
        // re-derive it from names — sprk_relatedvendororg would defeat a name-based guess.
        var expected = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = "sprk_relatedmatter",
            ["sprk_project"] = "sprk_relatedproject",
            ["sprk_invoice"] = "sprk_relatedinvoice",
            ["sprk_workassignment"] = "sprk_relatedworkassignment",
        };

        var legacy = DocumentLinkFieldMap.All
            .Where(f => f.IsLegacy)
            .ToDictionary(f => f.Attribute, f => f.SupersededBy, StringComparer.Ordinal);

        Assert.Equal(expected, legacy);
    }

    [Fact]
    public void EverySupersedingColumn_IsItselfACurrentColumn()
    {
        // A pointer at a column that is absent (or itself legacy) would misdirect that future migration.
        var current = DocumentLinkFieldMap.Current.Select(f => f.Attribute).ToHashSet(StringComparer.Ordinal);

        foreach (var field in DocumentLinkFieldMap.All.Where(f => f.IsLegacy))
        {
            Assert.Contains(field.SupersededBy!, current);
        }
    }

    [Fact]
    public void TwoLookupsMayShareATargetEntity_SoTheVocabularyIsNeverKeyedByTarget()
    {
        // sprk_relatedorganization and sprk_relatedvendororg both point at sprk_organization in different
        // roles. Keying this map by target entity would silently drop one of them.
        var orgLinks = DocumentLinkFieldMap.Current
            .Where(f => f.TargetEntity == "sprk_organization")
            .Select(f => f.Attribute)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "sprk_relatedorganization", "sprk_relatedvendororg" }, orgLinks);
    }

    [Fact]
    public void AssociationCandidates_ExcludeTheInboundCommunicationOnly()
    {
        var excluded = DocumentLinkFieldMap.AllAttributes
            .Except(DocumentLinkFieldMap.AssociationCandidateFields.Select(f => f.Attribute), StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        // The rung matches FROM a communication; surfacing it back as a candidate is circular. Everything
        // else — notably Agreement, which the old hard-coded list missed — must be scannable.
        Assert.Equal(new[] { "sprk_email", "sprk_relatedcommunication" }, excluded);
        Assert.Contains(
            DocumentLinkFieldMap.AssociationCandidateFields,
            f => f.Attribute == "sprk_relatedagreement");
    }

    [Fact]
    public void ProjectForCopy_CopiesColumnForColumn_IncludingLegacyOnes()
    {
        // A first cut REDIRECTED sprk_matter -> sprk_relatedmatter to migrate rows as they were touched.
        // That broke the feature's actual guarantee: a subgrid binds to ONE relationship, so re-filing the
        // copy under a different column than its source stops the two appearing together. Two existing
        // tests caught it. Copying is reproducing where the source lives, not creating a new association.
        var projected = DocumentLinkFieldMap.ProjectForCopy<string>(
            field => field.Attribute == "sprk_matter" ? "legacy-matter" : null);

        Assert.Equal(new Dictionary<string, string> { ["sprk_matter"] = "legacy-matter" }, projected);
    }

    [Fact]
    public void ProjectForCopy_KeepsBothForms_WhenTheSourceCarriesLegacyAndModernSeparately()
    {
        // Old rows can carry both, pointing at different records. Neither is dropped and neither is
        // merged: the copy mirrors the source exactly.
        var projected = DocumentLinkFieldMap.ProjectForCopy<string>(field => field.Attribute switch
        {
            "sprk_matter" => "legacy-matter",
            "sprk_relatedmatter" => "modern-matter",
            _ => null,
        });

        Assert.Equal("legacy-matter", projected["sprk_matter"]);
        Assert.Equal("modern-matter", projected["sprk_relatedmatter"]);
    }

    [Fact]
    public void ProjectForCopy_CarriesTheLinkTypesTheOldHardCodedListDropped()
    {
        // The regression this whole change exists for: a PDF filed under an Agreement produced a Word
        // document with no Agreement link, silently.
        var dropped = new[]
        {
            "sprk_relatedagreement", "sprk_relatedservicerequest", "sprk_relatedtodo",
            "sprk_relatedevent", "sprk_relatedcontact", "sprk_relatedorganization",
            "sprk_relatedvendororg", "sprk_relatedinvoice", "sprk_relatedworkassignment",
        };

        var projected = DocumentLinkFieldMap.ProjectForCopy<string>(
            field => dropped.Contains(field.Attribute, StringComparer.Ordinal) ? field.Attribute : null);

        Assert.Equal(
            dropped.OrderBy(x => x, StringComparer.Ordinal),
            projected.Keys.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ProjectForCopy_OmitsEmptyLinks()
    {
        Assert.Empty(DocumentLinkFieldMap.ProjectForCopy<string>(_ => null));
    }
}
