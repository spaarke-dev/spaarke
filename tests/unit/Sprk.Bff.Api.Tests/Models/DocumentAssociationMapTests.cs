using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models;

/// <summary>
/// The association map is the ONE place a caller-supplied type becomes a document lookup.
///
/// It replaced four hand-written copies of the same switch that had drifted apart on which SPELLING
/// each accepted — <c>UploadFinalizationWorker</c> took only "matter", <c>EmailAttachmentProcessor</c>
/// took only "sprk_matter". That drift was the defect: the identical association token applied in one
/// path and silently vanished in another, producing a document the user believes is filed and is not.
/// These tests exist because nothing failed loudly when it happened.
/// </summary>
public class DocumentAssociationMapTests
{
    // Both spellings for every supported type. The friendly/logical split is exactly what drifted,
    // so it is asserted per-type rather than spot-checked.
    [Theory]
    [InlineData("matter")]
    [InlineData("sprk_matter")]
    [InlineData("MATTER")]
    public void Matter_applies_under_either_spelling(string type)
    {
        var request = new UpdateDocumentRequest();
        var id = Guid.NewGuid();

        Assert.True(DocumentAssociationMap.TryApply(request, type, id));
        Assert.Equal(id, request.MatterLookup);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("sprk_project")]
    public void Project_applies_under_either_spelling(string type)
    {
        var request = new UpdateDocumentRequest();
        var id = Guid.NewGuid();

        Assert.True(DocumentAssociationMap.TryApply(request, type, id));
        Assert.Equal(id, request.ProjectLookup);
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("sprk_invoice")]
    public void Invoice_applies_under_either_spelling(string type)
    {
        var request = new UpdateDocumentRequest();
        var id = Guid.NewGuid();

        Assert.True(DocumentAssociationMap.TryApply(request, type, id));
        Assert.Equal(id, request.InvoiceLookup);
    }

    // The two types the Q4 widening added. Their lookup columns already existed on sprk_document —
    // only the mappers were missing them, which is why a save filed to a work assignment produced no
    // error and no association.
    [Theory]
    [InlineData("workassignment")]
    [InlineData("sprk_workassignment")]
    public void WorkAssignment_applies_under_either_spelling(string type)
    {
        var request = new UpdateDocumentRequest();
        var id = Guid.NewGuid();

        Assert.True(DocumentAssociationMap.TryApply(request, type, id));
        Assert.Equal(id, request.WorkAssignmentLookup);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("sprk_event")]
    public void Event_applies_under_either_spelling(string type)
    {
        var request = new UpdateDocumentRequest();
        var id = Guid.NewGuid();

        Assert.True(DocumentAssociationMap.TryApply(request, type, id));
        Assert.Equal(id, request.EventLookup);
    }

    /// <summary>
    /// The types with NO lookup column on sprk_document (verified against live Dataverse metadata
    /// 2026-09-03). They must report failure so the caller can log loudly or reject — never be
    /// quietly accepted, which is what produced unassociated documents.
    ///
    /// <c>sprk_todo</c> is here deliberately: the Q4-widening note listed it as mappable alongside
    /// work assignment and event. It is not. If someone adds the column later, this assertion is the
    /// thing that should be revisited — not deleted silently.
    /// </summary>
    [Theory]
    [InlineData("account")]
    [InlineData("contact")]
    [InlineData("sprk_todo")]
    [InlineData("todo")]
    [InlineData("sprk_analysis")]
    [InlineData("nonsense")]
    public void Unsupported_types_report_failure_and_write_nothing(string type)
    {
        var request = new UpdateDocumentRequest();

        Assert.False(DocumentAssociationMap.TryApply(request, type, Guid.NewGuid()));
        AssertNoLookupSet(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_type_reports_failure_and_writes_nothing(string? type)
    {
        var request = new UpdateDocumentRequest();

        Assert.False(DocumentAssociationMap.TryApply(request, type, Guid.NewGuid()));
        AssertNoLookupSet(request);
    }

    [Fact]
    public void Empty_or_missing_id_reports_failure_and_writes_nothing()
    {
        var request = new UpdateDocumentRequest();

        // An empty GUID is not a record. Applying it would bind the lookup to nothing and read as a
        // successful association at every call site.
        Assert.False(DocumentAssociationMap.TryApply(request, "matter", Guid.Empty));
        Assert.False(DocumentAssociationMap.TryApply(request, "matter", null));
        AssertNoLookupSet(request);
    }

    [Fact]
    public void Applying_one_type_leaves_the_other_lookups_untouched()
    {
        // A document is filed to exactly one record; a mapper that set two lookups would be a
        // different bug than the one this class is about, and just as invisible.
        var request = new UpdateDocumentRequest();
        var id = Guid.NewGuid();

        Assert.True(DocumentAssociationMap.TryApply(request, "sprk_event", id));

        Assert.Equal(id, request.EventLookup);
        Assert.Null(request.MatterLookup);
        Assert.Null(request.ProjectLookup);
        Assert.Null(request.InvoiceLookup);
        Assert.Null(request.WorkAssignmentLookup);
    }

    private static void AssertNoLookupSet(UpdateDocumentRequest request)
    {
        Assert.Null(request.MatterLookup);
        Assert.Null(request.ProjectLookup);
        Assert.Null(request.InvoiceLookup);
        Assert.Null(request.WorkAssignmentLookup);
        Assert.Null(request.EventLookup);
    }
}
