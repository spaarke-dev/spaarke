using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 018b-K2 (raised by UAC-r2 condition (b)) — create-time field mapping never writes
/// (a) the contact-plane creator and requester (<c>sprk_createdbycontact</c>, <c>sprk_requestedby</c>: a contact stamped
/// app-only would get requester or creator rights on the external plane), nor (b) any target
/// <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> refuses (a document's access record, D7 links and BFF-owned
/// columns). Such a rule is skipped with <see cref="RecordCreationWarning.MappingRuleSkippedCode"/> naming the column (and,
/// for the refusal, its code); mapping never refuses (FR-08). The one exemption is a document's Access Permission, which
/// stays fill-only at create (D7; pinned by <c>CreateAsync_ADocument_NeverMapsItsAccessInheritance_ButFillsItsAccessPermission</c>).
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    public static TheoryData<string> ContactPlaneCreatorColumns() => new() { "sprk_createdbycontact", "sprk_requestedby" };

    public static TheoryData<string, string> AppOnlyRefusedDocumentTargets() => new()
    {
        { "sprk_filepath", InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode },
        { "sprk_graphitemid", InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode },
        { "sprk_parentdocument", InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode },
        { "sprk_relatedcommunication", InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode },
        { "sprk_accessinheritance", InheritedAccessPermission.AppOnlyDocumentWriteReasonCode },
    };

    private RecordCreationRequest DocumentUnderMatter() =>
        Request("sprk_document", new Dictionary<string, object?>
        {
            ["sprk_documentname"] = "Brief.docx",
            ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        }, mapping: true);

    /// <summary>A write key as a column name, whatever its form (<c>sprk_CreatedByContact@odata.bind</c> or <c>sprk_createdbycontact</c>).</summary>
    private static string Normalized(string key) => key.Replace("_", string.Empty).ToLowerInvariant();

    /// <summary>The target declares the columns valid for create, so a rule that reached the write would be written.</summary>
    private void DeclareCreatableContactAndDocumentColumns()
    {
        _user.Columns["sprk_event"]["sprk_createdbycontact"] = ("Lookup", true);
        _user.Columns["sprk_event"]["sprk_requestedby"] = ("Lookup", true);
        foreach (var column in new[] { "sprk_filepath", "sprk_graphitemid", "sprk_parentdocument", "sprk_relatedcommunication" })
            _user.Columns["sprk_document"][column] = ("String", true);
    }

    [Theory]
    [MemberData(nameof(ContactPlaneCreatorColumns))]
    public async Task CreateAsync_ARuleTargetingTheContactPlaneCreatorOrRequester_IsSkippedWithTheWarning_AndAnOrdinaryRuleIsStillFilled(
        string column)
    {
        DeclareCreatableContactAndDocumentColumns();
        Profile("sprk_matter", "sprk_event",
            Rule(null, $" {column.ToUpperInvariant()} ", Text, Default, "someone"),
            Rule(null, "sprk_location", Text, Default, "Room 1"));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => Normalized(k).StartsWith(Normalized(column), StringComparison.Ordinal),
            $"{column} is the server's alone (must fire)");
        StringOf(fields, "sprk_location").Should().Be("Room 1", "an ordinary column of the same profile is still written (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains(column));
    }

    [Theory]
    [MemberData(nameof(AppOnlyRefusedDocumentTargets))]
    public async Task CreateAsync_ARuleTargetingAColumnTheAppOnlyRefusalRefuses_IsSkippedWithTheRefusalCode_AndAnOrdinaryRuleIsStillFilled(
        string column, string refusalCode)
    {
        DeclareCreatableContactAndDocumentColumns();
        Profile("sprk_matter", "sprk_document",
            Rule(null, $"  {column.ToUpperInvariant()} ", Text, Default, "x"),
            Rule("sprk_matterdescription", "sprk_documentdescription", Memo));

        var outcome = await Service().CreateAsync(DocumentUnderMatter(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => Normalized(k).StartsWith(Normalized(column), StringComparison.Ordinal),
            $"{column} is Spaarke's on a document (must fire)");
        StringOf(fields, "sprk_documentdescription").Should().Be("From the matter", "an ordinary column is still mapped (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Column == column
                                                                        && w.Detail.Contains(column) && w.Detail.Contains(refusalCode));
    }

    [Fact]
    public async Task CreateAsync_ADocumentsAccessPermissionRule_IsNotDropped_TheD7FillOnlyExemptionIsNarrow()
    {
        // The exemption is exactly sprk_document + sprk_accesspermission: the same rule on its sibling access record is dropped.
        Profile("sprk_matter", "sprk_document",
            Rule(null, "sprk_accesspermission", OptionSet, Default, "100000002"),
            Rule(null, "sprk_accessinheritance", OptionSet, Default, "100000001"));

        var outcome = await Service().CreateAsync(DocumentUnderMatter(), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        ((JsonElement)fields["sprk_accesspermission"]!).GetInt32().Should().Be(100000002);
        fields.Should().NotContainKey("sprk_accessinheritance");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Detail.Should().Contain("sprk_accessinheritance").And.Contain(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
    }

    [Theory]
    [MemberData(nameof(ContactPlaneCreatorColumns))]
    public async Task PostMappingDefault_TheContactPlaneCreatorOrRequester_IsAnArgumentError(string column)
    {
        var act = () => Service().CreateAsync(
            EventUnder(OrdinaryMatter) with { PostMappingDefaults = new Dictionary<string, object?> { [column] = "x" } },
            AsCaller(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _creates.Should().BeEmpty();
    }
}
