using System;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Domain tests for <see cref="DataverseServiceClientImpl.MapToDocumentEntity"/> — the read-model
/// mapper the task 021 (spaarkeai-word-add-in-r1, FR-07) Profile-section read path depends on.
/// </summary>
/// <remarks>
/// <para><b>The regression this file exists to pin.</b> <c>sprk_documenttype</c> and
/// <c>sprk_filesummarystatus</c> are Dataverse Choice (Picklist) columns — verified live 2026-09-12
/// via the metadata API, and corroborated by the existing writer
/// (<c>DataverseServiceClientImpl.cs:850</c>: <c>document["sprk_documenttype"] = new
/// OptionSetValue(request.DocumentType.Value)</c>). A Choice attribute retrieved from Dataverse is
/// stored on the <see cref="Entity"/> as an <see cref="OptionSetValue"/>, never a
/// <see cref="string"/>. The mapper's original line —
/// <c>entity.GetAttributeValue&lt;string&gt;("sprk_documenttype")</c> — casts that
/// <see cref="OptionSetValue"/> directly to <see cref="string"/>, which the SDK's
/// <c>Entity.GetAttributeValue&lt;T&gt;</c> implements as an unconditional <c>(T)obj</c> cast, so it
/// throws <see cref="InvalidCastException"/> for ANY entity where the attribute is present. Because
/// task 021 also added <c>sprk_documenttype</c> to <c>GetDocumentAsync</c>'s <c>ColumnSet</c>, this
/// was reachable from <c>GET /api/v1/documents/{id}</c> AND from
/// <c>VisualizationService.SearchForVisualizationAsync</c> Step 1 (<c>sourceDataverseDoc =
/// GetDocumentAsync(...)</c>, "always required" per its own comment) — i.e. it would have broken
/// Find Similar for any document with a classified document type, not merely the new read surface.
/// </para>
/// <para><b>Seam choice (ADR-038 B8).</b> <c>MapToDocumentEntity</c> is pure (no ServiceClient, no
/// I/O, no instance state) and was made <c>public static</c> specifically so this test can call it
/// directly against a real <see cref="Entity"/> — the same precedent as
/// <c>DataverseServiceClientImpl.StageAnalysisRegardingFields</c> (this same file) and
/// <c>TodoRegardingBuilder.ApplyResolverFieldsAsync</c>, both cited in their own test files for the
/// identical reason: avoid the B8 ban on internal/reflection tests by exposing a public, pure seam
/// instead of using <c>InternalsVisibleTo</c> or reflection.</para>
/// <para><b>Fail-then-pass, demonstrated manually during task 021's fix</b> (not re-derivable from
/// git history alone — recorded here for the record): with the ORIGINAL
/// <c>entity.GetAttributeValue&lt;string&gt;("sprk_documenttype")</c> line, every test below that sets
/// <c>sprk_documenttype</c> as an <see cref="OptionSetValue"/> threw
/// <c>InvalidCastException: Unable to cast object of type 'Microsoft.Xrm.Sdk.OptionSetValue' to type
/// 'System.String'.</c> at the mapper call. After the fix (prefer <c>entity.FormattedValues</c>,
/// fall back to the raw numeric value only when unformatted), the same tests pass.</para>
/// <para>KEEP path: tests/unit/domain/** (ADR-038 §2 #6 — pure domain mapping logic).</para>
/// </remarks>
public class DocumentEntityMappingTests
{
    [Fact]
    public void MapToDocumentEntity_WhenDocumentTypeHasFormattedValue_ReturnsTheFriendlyLabel()
    {
        // The normal live shape: ServiceClient.RetrieveAsync populates FormattedValues for every
        // Choice/Picklist attribute it returns alongside the raw OptionSetValue.
        var entity = new Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_documentname"] = "Master Services Agreement",
            ["sprk_documenttype"] = new OptionSetValue(100000003),
        };
        entity.FormattedValues["sprk_documenttype"] = "NDA";

        var result = DataverseServiceClientImpl.MapToDocumentEntity(entity);

        result.DocumentType.Should().Be("NDA",
            "the pane displays a human-readable label, not a raw option-set integer");
    }

    [Fact]
    public void MapToDocumentEntity_WhenDocumentTypeHasNoFormattedValue_FallsBackToTheRawNumericValue()
    {
        // Defensive path: some retrieval shapes (e.g. hand-built entities in other unit tests, or a
        // future caller that doesn't request formatted values) may carry the OptionSetValue with no
        // FormattedValues entry. The mapper must still produce SOMETHING usable, not throw.
        var entity = new Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_documentname"] = "Untitled",
            ["sprk_documenttype"] = new OptionSetValue(100000003),
        };
        // Deliberately NOT setting entity.FormattedValues["sprk_documenttype"].

        var result = DataverseServiceClientImpl.MapToDocumentEntity(entity);

        result.DocumentType.Should().Be("100000003",
            "with no label available, the raw option value is surfaced rather than throwing or silently dropping the field");
    }

    [Fact]
    public void MapToDocumentEntity_WhenDocumentTypeColumnWasNotSelected_ReturnsNull()
    {
        // Most GetDocumentAsync-family callers (GetDocumentsByMatterAsync, GetDocumentsByParentAsync,
        // etc.) do NOT select sprk_documenttype in their ColumnSet — this must stay a safe null, not
        // an exception, exactly as it behaved before task 021 activated the field for GetDocumentAsync.
        var entity = new Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_documentname"] = "No type set",
        };

        var result = DataverseServiceClientImpl.MapToDocumentEntity(entity);

        result.DocumentType.Should().BeNull();
    }

    [Theory]
    [InlineData(100000000, "None")]
    [InlineData(100000002, "Completed")]
    [InlineData(100000004, "Failed")]
    public void MapToDocumentEntity_SummaryStatusOptionSetValue_ReturnsTheRawIntegerNeverTheLabel(
        int rawValue, string formattedLabel)
    {
        // sprk_filesummarystatus is ALSO a Choice column. Unlike DocumentType (a display label), the
        // client-side seven-state table (documentProfileChoices.ts) keys off the raw Dataverse option
        // integer, not the label — so this mapper must return .Value (int), not FormattedValues, even
        // when a formatted label is present. This was already correct pre-task-021's fix (only
        // DocumentType had the bug) — pinned here so a future refactor doesn't accidentally "fix" it
        // to match the DocumentType pattern and break the client's status switch.
        var entity = new Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_documentname"] = "Status probe",
            ["sprk_filesummarystatus"] = new OptionSetValue(rawValue),
        };
        entity.FormattedValues["sprk_filesummarystatus"] = formattedLabel;

        var result = DataverseServiceClientImpl.MapToDocumentEntity(entity);

        result.SummaryStatus.Should().Be(rawValue);
    }

    [Fact]
    public void MapToDocumentEntity_WhenSummaryStatusColumnWasNotSelected_ReturnsNullNotAnImplicitDefault()
    {
        var entity = new Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_documentname"] = "Never profiled",
        };

        var result = DataverseServiceClientImpl.MapToDocumentEntity(entity);

        result.SummaryStatus.Should().BeNull(
            "Dataverse applies no implicit default for an unset Choice column; the mapper must not invent one");
    }

    [Theory]
    [InlineData("sprk_filesummary")]
    [InlineData("sprk_filetldr")]
    [InlineData("sprk_filekeywords")]
    public void MapToDocumentEntity_MemoColumns_ReadDirectlyAsStringWithNoCastIssue(string attributeName)
    {
        // sprk_filesummary / sprk_filetldr / sprk_filekeywords are Memo columns (verified live
        // 2026-09-12, alongside the sprk_documenttype/sprk_filesummarystatus Picklist finding) — the
        // SDK stores a Memo attribute as a plain string, so GetAttributeValue<string> is correct here
        // and needs no OptionSetValue handling. This test proves that reading them does not throw and
        // returns the value verbatim (the same scrutiny requested for sprk_documenttype, applied to
        // its Memo siblings and confirmed clean).
        var entity = new Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_documentname"] = "Memo probe",
            [attributeName] = "A long free-text value with punctuation, and a newline.\nSecond line.",
        };

        var result = DataverseServiceClientImpl.MapToDocumentEntity(entity);

        var actual = attributeName switch
        {
            "sprk_filesummary" => result.Summary,
            "sprk_filetldr" => result.Tldr,
            "sprk_filekeywords" => result.Keywords,
            _ => throw new InvalidOperationException("unreachable"),
        };

        actual.Should().Be("A long free-text value with punctuation, and a newline.\nSecond line.");
    }
}
