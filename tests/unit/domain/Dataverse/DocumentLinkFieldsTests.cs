using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Pure-domain tests for <see cref="DocumentLinkFields"/> — the single hoisted <c>sprk_document</c>
/// record-link vocabulary (unified-access-control-r2, 2026-09-05). Before the hoist, two independent
/// copies of this list lived in <c>Sprk.Bff.Api</c> (<c>AttachmentDocumentAssociationRung</c> and
/// <c>ComposeService</c>), both the SAME incomplete six entries. These tests pin the COMPLETE,
/// live-metadata-verified set (16 columns) and — critically — the exact per-column schema-name casing,
/// since the <c>sprk_related*</c> family is NOT uniformly cased and a convention-based rebuild
/// (<c>$"sprk_Related{type}"</c>) silently produces the wrong value for 3 of the 12 <c>related</c>
/// columns. Every expected value here was verified against live Dataverse metadata on <c>spaarkedev1</c>
/// (2026-09-05) — see <c>projects/unified-access-control-r2/notes/document-link-vocabulary-hoist.md</c>.
///
/// KEEP path: tests/unit/domain/** (ADR-038 §2 #7 — pure domain mapping logic; no I/O, no mocks).
/// </summary>
public class DocumentLinkFieldsTests
{
    // The complete, live-verified vocabulary. A change to this table without a corresponding change
    // in production is exactly the regression this suite exists to catch.
    public static IEnumerable<object[]> ExpectedEntries()
    {
        yield return new object[] { "sprk_matter", "sprk_Matter", "sprk_matter" };
        yield return new object[] { "sprk_relatedmatter", "sprk_relatedmatter", "sprk_matter" };
        yield return new object[] { "sprk_project", "sprk_Project", "sprk_project" };
        yield return new object[] { "sprk_relatedproject", "sprk_relatedproject", "sprk_project" };
        yield return new object[] { "sprk_invoice", "sprk_Invoice", "sprk_invoice" };
        yield return new object[] { "sprk_relatedinvoice", "sprk_RelatedInvoice", "sprk_invoice" };
        yield return new object[] { "sprk_workassignment", "sprk_WorkAssignment", "sprk_workassignment" };
        yield return new object[] { "sprk_relatedworkassignment", "sprk_RelatedWorkAssignment", "sprk_workassignment" };
        yield return new object[] { "sprk_relatedagreement", "sprk_RelatedAgreement", "sprk_agreement" };
        yield return new object[] { "sprk_relatedcommunication", "sprk_RelatedCommunication", "sprk_communication" };
        yield return new object[] { "sprk_relatedcontact", "sprk_RelatedContact", "contact" };
        yield return new object[] { "sprk_relatedevent", "sprk_RelatedEvent", "sprk_event" };
        yield return new object[] { "sprk_relatedorganization", "sprk_RelatedOrganization", "sprk_organization" };
        yield return new object[] { "sprk_relatedservicerequest", "sprk_RelatedServiceRequest", "sprk_servicerequest" };
        yield return new object[] { "sprk_relatedtodo", "sprk_RelatedToDo", "sprk_todo" };
        yield return new object[] { "sprk_relatedvendororg", "sprk_relatedvendororg", "sprk_organization" };
    }

    [Fact]
    public void All_ContainsExactlySixteenColumns_MatchingLiveMetadata()
    {
        // 6 prior (incomplete) + 10 added by the 2026-09-05 hoist. A count drift in EITHER direction
        // means either a column silently disappeared (documents linked only through it become
        // invisible again) or an unverified one was added (see the per-column casing test below for
        // why an unverified addition is dangerous).
        DocumentLinkFields.All.Should().HaveCount(16);
    }

    [Theory]
    [MemberData(nameof(ExpectedEntries))]
    public void All_EveryEntry_HasTheLiveVerifiedLogicalSchemaAndTargetName(
        string logicalName, string schemaName, string targetEntity)
    {
        DocumentLinkFields.All.Should().ContainSingle(f => f.LogicalName == logicalName)
            .Which.Should().BeEquivalentTo(new { SchemaName = schemaName, TargetEntityLogicalName = targetEntity },
                because: $"'{logicalName}' schema name and target were verified against live Dataverse " +
                         "metadata — a mismatch here means the pinned value drifted from the real column");
    }

    [Fact]
    public void All_ContainsNoUnexpectedColumns()
    {
        // The inverse of the theory above: catches an ADDITION nobody pinned an expected value for.
        var expectedLogicalNames = ExpectedEntries().Select(e => (string)e[0]).ToArray();

        DocumentLinkFields.All.Select(f => f.LogicalName).Should().BeEquivalentTo(expectedLogicalNames);
    }

    [Theory]
    [InlineData("sprk_RelatedAgreement")]
    [InlineData("sprk_RelatedCommunication")]
    [InlineData("sprk_RelatedContact")]
    [InlineData("sprk_RelatedInvoice")]
    [InlineData("sprk_RelatedOrganization")]
    [InlineData("sprk_RelatedServiceRequest")]
    [InlineData("sprk_RelatedToDo")]
    [InlineData("sprk_RelatedWorkAssignment")]
    [InlineData("sprk_RelatedEvent")]
    public void All_PascalCasedRelatedSchemaNames_AreExactlyThisNineColumnSet(string expectedPascalCaseSchemaName)
    {
        // The trap the brief warns about: a convention-derived "$sprk_Related{type}" builder would
        // produce EVERY one of these correctly, which is exactly why it is dangerous — it also
        // produces a WRONG value for the 3 lowercase exceptions below, with no compiler or runtime
        // signal. Pinning the PascalCase set explicitly means an accidental "fix" toward the
        // convention for matter/project/vendororg fails this suite immediately.
        DocumentLinkFields.All.Should().ContainSingle(f => f.SchemaName == expectedPascalCaseSchemaName);
    }

    [Theory]
    [InlineData("sprk_relatedmatter")]
    [InlineData("sprk_relatedproject")]
    [InlineData("sprk_relatedvendororg")]
    public void All_LowercaseRelatedSchemaNames_AreExactlyThisThreeColumnException(string expectedLowercaseSchemaName)
    {
        // These three are the ones a "$sprk_Related{type}" convention gets WRONG (it would produce
        // "sprk_Relatedmatter"/"sprk_Relatedproject"/"sprk_Relatedvendororg" — none of which exist).
        // Matter and project are "the two that matter most" per the hoist brief.
        var entry = DocumentLinkFields.All.Should().ContainSingle(
            f => f.LogicalName == expectedLowercaseSchemaName).Which;

        entry.SchemaName.Should().Be(expectedLowercaseSchemaName,
            "the schema name for this column is lowercase — identical to its logical name, unlike " +
            "9 of its 12 related-family siblings");
        entry.SchemaName.Should().NotBe(
            "sprk_Related" + entry.LogicalName["sprk_related".Length..],
            "a convention-derived PascalCase schema name is WRONG for this column — that is the trap");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_relatedmatter")]
    [InlineData("sprk_project", "sprk_relatedproject")]
    [InlineData("sprk_invoice", "sprk_relatedinvoice")]
    [InlineData("sprk_workassignment", "sprk_relatedworkassignment")]
    public void All_RelatedColumn_TargetsTheSameEntityAsItsPrimaryCounterpart(string primary, string related)
    {
        // "A related matter is still a matter" (061 UAT round-2) — the type-agnostic design principle
        // both consumers rely on.
        var primaryTarget = DocumentLinkFields.All.Single(f => f.LogicalName == primary).TargetEntityLogicalName;
        var relatedTarget = DocumentLinkFields.All.Single(f => f.LogicalName == related).TargetEntityLogicalName;

        relatedTarget.Should().Be(primaryTarget);
    }

    [Fact]
    public void All_RelatedOrganizationAndRelatedVendorOrg_BothTargetOrganization()
    {
        // There is no separate "vendor org" entity in Spaarke's model — both lookups point at the
        // same sprk_organization table (verified live; not a doc-comment assumption).
        DocumentLinkFields.All.Single(f => f.LogicalName == "sprk_relatedorganization")
            .TargetEntityLogicalName.Should().Be("sprk_organization");
        DocumentLinkFields.All.Single(f => f.LogicalName == "sprk_relatedvendororg")
            .TargetEntityLogicalName.Should().Be("sprk_organization");
    }

    [Fact]
    public void LogicalNames_MatchesAllInTheSameOrder()
    {
        // The convenience projection both consumers use for ColumnSet/RetrieveAsync columns must never
        // silently diverge in order or membership from the canonical All list.
        DocumentLinkFields.LogicalNames.Should().Equal(DocumentLinkFields.All.Select(f => f.LogicalName));
    }
}
