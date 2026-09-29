using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// Tests for the Compose-side POLICY over the <c>sprk_document</c> link vocabulary.
///
/// <para><b>Rewritten 2026-09-29 at the master merge.</b> This class used to pin the column LIST, because
/// <c>DocumentLinkFieldMap</c> used to declare it. It no longer does — <c>unified-access-control-r2</c>
/// hoisted the vocabulary to <c>Spaarke.Dataverse.DocumentLinkFields</c> (a better home: shared library,
/// case-sensitive <c>SchemaName</c> carried), and this file's column tests would now be a second
/// hand-maintained copy of a closed set, which is the exact drift the hoist ended. Column coverage and
/// casing belong to <c>DocumentLinkFieldsTests</c>; what is tested here is the policy that has no
/// equivalent there — legacy mapping and copy-forward semantics.</para>
/// </summary>
public class DocumentLinkFieldMapTests
{
    [Fact]
    public void TheFourUnprefixedColumns_AreMappedToTheSiblingThatSupersedesThem()
    {
        DocumentLinkFieldMap.SupersededBy.Should().HaveCount(4);
        DocumentLinkFieldMap.SupersededBy["sprk_matter"].Should().Be("sprk_relatedmatter");
        DocumentLinkFieldMap.SupersededBy["sprk_project"].Should().Be("sprk_relatedproject");
        DocumentLinkFieldMap.SupersededBy["sprk_invoice"].Should().Be("sprk_relatedinvoice");
        DocumentLinkFieldMap.SupersededBy["sprk_workassignment"].Should().Be("sprk_relatedworkassignment");
    }

    /// <summary>
    /// A legacy mapping that named a column the schema does not have would be a migration plan that
    /// cannot run. Both ends are checked against the shared vocabulary, not a list re-typed here.
    /// </summary>
    [Fact]
    public void EveryLegacyMapping_NamesRealColumnsOnBothEnds()
    {
        var known = DocumentLinkFields.LogicalNames;

        foreach (var (legacy, successor) in DocumentLinkFieldMap.SupersededBy)
        {
            known.Should().Contain(legacy, $"the legacy column {legacy} must exist to be superseded");
            known.Should().Contain(successor, $"the successor {successor} must exist to supersede {legacy}");
            DocumentLinkFieldMap.IsLegacy(successor).Should().BeFalse(
                $"{successor} supersedes something, so it must not itself be marked legacy");
        }
    }

    [Fact]
    public void IsLegacy_IsCaseInsensitive_BecauseDataverseLogicalNamesArriveInMixedCase()
    {
        DocumentLinkFieldMap.IsLegacy("sprk_matter").Should().BeTrue();
        DocumentLinkFieldMap.IsLegacy("SPRK_MATTER").Should().BeTrue();
        DocumentLinkFieldMap.IsLegacy("sprk_relatedmatter").Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ProjectForCopy — the create-on-save link inheritance
    // -----------------------------------------------------------------------

    private static EntityReference Ref(string entity) => new(entity, Guid.NewGuid());

    /// <summary>
    /// THE LOAD-BEARING TEST. An earlier cut redirected legacy → related on write, to migrate rows as
    /// they were touched. A Dataverse subgrid binds to ONE relationship, so if the Matter form's
    /// Documents subgrid is bound to <c>sprk_matter</c> and the source PDF sits there, re-filing the copy
    /// under <c>sprk_relatedmatter</c> means the two never appear together — silently defeating "files
    /// alongside the source", the whole point of the feature.
    /// </summary>
    [Fact]
    public void ProjectForCopy_CopiesColumnForColumn_NeverRedirectingALegacyColumn()
    {
        var source = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = Ref("sprk_matter"),
        };

        var copied = DocumentLinkFieldMap.ProjectForCopy(f =>
            source.TryGetValue(f.LogicalName, out var r) ? r : null);

        copied.Should().ContainKey("sprk_matter");
        copied.Should().NotContainKey("sprk_relatedmatter",
            "redirecting the copy to a different relationship would stop it appearing beside its source");
    }

    [Fact]
    public void ProjectForCopy_KeepsBothForms_WhenTheSourceCarriesLegacyAndModernSeparately()
    {
        var legacy = Ref("sprk_matter");
        var modern = Ref("sprk_matter");
        var source = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = legacy,
            ["sprk_relatedmatter"] = modern,
        };

        var copied = DocumentLinkFieldMap.ProjectForCopy(f =>
            source.TryGetValue(f.LogicalName, out var r) ? r : null);

        copied["sprk_matter"].Should().BeSameAs(legacy);
        copied["sprk_relatedmatter"].Should().BeSameAs(modern);
    }

    /// <summary>
    /// The regression this whole area exists for: the two prior hard-coded copies knew 6 columns, so a
    /// PDF filed under an Agreement produced a Word document with no Agreement link and no error. Asserted
    /// against the shared vocabulary so it cannot pass by re-listing what the code happens to do.
    /// </summary>
    [Fact]
    public void ProjectForCopy_CarriesEveryColumnTheSharedVocabularyDeclares()
    {
        var copied = DocumentLinkFieldMap.ProjectForCopy(f => Ref(f.TargetEntityLogicalName));

        copied.Keys.Should().BeEquivalentTo(DocumentLinkFields.LogicalNames);
        copied.Should().ContainKey("sprk_relatedagreement",
            "the Agreement link was one of the ten the old hard-coded lists missed");
        copied.Should().ContainKey("sprk_email",
            "the email link was the 17th column the 2026-09-05 hoist itself missed");
    }

    [Fact]
    public void ProjectForCopy_OmitsEmptyLinks()
    {
        var copied = DocumentLinkFieldMap.ProjectForCopy<EntityReference>(_ => null);
        copied.Should().BeEmpty("a source with no links yields no attributes to write");
    }
}
