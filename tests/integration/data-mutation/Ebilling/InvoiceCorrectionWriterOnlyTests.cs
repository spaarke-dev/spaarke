using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 ISS-028 (EB-D-248; owner question (e)): a correction row (<c>sprk_invoicecorrection</c>) and the reviewer's note it carries are
/// created and changed only by the services that own them, the same rule as a Signal and a Decision Record
/// (<see cref="RecordTableRule.CreatedOnlyByItsWriter"/>). The refusal on each generic path is pinned where that path's tests live, with the
/// table added to the writer-only table lists (the general create: <c>RecordCreationServicePipelineSeamTests.WriterOnlyTables</c>; the chat create:
/// <c>ChatServerWrittenTableBlockTests</c>; the playbook updates: <c>WriterOnlyTablesUpdateRefusalTests</c>; the chat update:
/// <c>SecureChildOwnershipAiToolTests.WriterOnlyTableUpdate</c>; the chat delete: <c>DataverseDeleteRecordHandlerTests</c>; the field-mapping push and
/// the work-product persister: their contract tests). This file pins the rule itself and the must-not-fire side: the two writers still write the row
/// and its note, over the in-memory Dataverse of <see cref="CorrectionWorld"/> and <see cref="ApprovalWorld"/> with the real services.
/// </summary>
public sealed class InvoiceCorrectionWriterOnlyTests
{
    private const string Table = "sprk_invoicecorrection";

    // ── The rule ──

    [Fact(DisplayName = "ISS-028 (must fire): the correction rule refuses the generic create, update and delete, with the writer-only codes")]
    public void TheRule_IsWriterOnly_OnCreateUpdateAndDelete()
    {
        var rule = RecordTableRules.Get(Table);

        rule.CreatedOnlyByItsWriter.Should().BeTrue();
        rule.GenericCreateRefused.Should().BeTrue("the create sites read GenericCreateRefused, which the flag implies");
        RecordTableRules.RefuseWriterOnlyTableUpdate(Table)!.RefusalCode.Should().Be(RecordTableRules.ChangedOnlyByItsWriterReasonCode);
        RecordTableRules.RefuseWriterOnlyTableDelete(Table)!.RefusalCode.Should().Be(RecordTableRules.RemovedOnlyByItsWriterReasonCode);
        // Matched trimmed and in any case, as for the other writer-only tables.
        RecordTableRules.RefuseWriterOnlyTableUpdate("  SPRK_InvoiceCorrection ").Should().NotBeNull();
    }

    [Fact(DisplayName = "ISS-028 (must not fire): the rest of the correction's entry is as before, and the sibling family tables stay open to the generic paths")]
    public void TheRestOfTheEntry_IsAsBefore_AndTheSiblingsAreNotWriterOnly()
    {
        var rule = RecordTableRules.Get(Table);

        rule.Kind.Should().Be(RecordTableKind.Child);
        rule.PreWriteRules.Should().Contain(RecordTableRules.InvoiceFamilyRule, "the invoice-family rule is still registered");
        rule.MappingProtectedColumns.Should().NotBeEmpty("the core lookups stay mapping-protected");
        rule.FixedAfterCreateColumns.Should().BeEmpty("no new column rule: only the table-level flag changed");
        rule.WriterOnlyColumns.Should().BeEmpty();
        rule.GenericCreateOnlyRefused.Should().BeFalse("the flag alone drives every refusal; no second flag");

        foreach (var sibling in new[] { "sprk_invoicesubmission", "sprk_invoiceapproval", "sprk_externalidentifier", "sprk_invoice", "sprk_billingevent" })
        {
            RecordTableRules.RefuseWriterOnlyTableUpdate(sibling).Should().BeNull(sibling);
            RecordTableRules.RefuseWriterOnlyTableDelete(sibling).Should().BeNull(sibling);
        }
    }

    // ── Must not fire: the writers ──

    [Theory(DisplayName = "ISS-028 must-not-fire: InvoiceCorrectionService still writes the row and its note, in the one transaction, with the refusal in force")]
    [InlineData("line-correct")]
    [InlineData("line-confirm")]
    [InlineData("header-correct")]
    [InlineData("header-confirm")]
    public async Task TheCorrectionService_StillWritesTheRowAndItsNote(string shape)
    {
        RecordTableRules.RefuseWriterOnlyTableUpdate(Table).Should().NotBeNull("control: the generic update of the table is refused");
        var world = new CorrectionWorld();
        var command = shape switch
        {
            "line-correct" => new CorrectionCommand(W.InvoiceId, W.FeeLineId, "Hours", "2.5", false, W.Reviewer, "Checked the rate card."),
            "line-confirm" => new CorrectionCommand(W.InvoiceId, W.FeeLineId, "Amount", null, true, W.Reviewer, "Checked the rate card."),
            "header-correct" => new CorrectionCommand(W.InvoiceId, null, "TotalDue", "2400", false, W.Reviewer, "Checked the rate card."),
            _ => new CorrectionCommand(W.InvoiceId, null, "TotalDue", null, true, W.Reviewer, "Checked the rate card."),
        };

        var result = await world.Service().CorrectAsync(command, CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        row.LogicalName.Should().Be(Table);
        row.GetAttributeValue<string>(InvoiceCorrectionNote.CorrectionRowColumn).Should().Be("Checked the rate card.");
        row.GetAttributeValue<bool>("sprk_islabel").Should().BeTrue();
        world.RowTransactions.Should().Be(1, "the row and its note are one upsert of the service's own transaction");
    }

    [Fact(DisplayName = "ISS-028 must-not-fire: InvoiceLineDecisionService still writes its bulk correction rows with the refusal in force")]
    public async Task TheLineDecisionService_StillWritesItsCorrectionRows()
    {
        RecordTableRules.RefuseWriterOnlyTableUpdate(Table).Should().NotBeNull("control: the generic update of the table is refused");
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, 300m, extraction: Extraction);
        w.AddLine(InvoiceLineStatus.Clean, 200m, extraction: Extraction);

        var result = await w.LineService().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), AliceUser, CancellationToken.None, CallerToken);

        result.IsApplied.Should().BeTrue(result.Code + " " + result.Detail);
        w.Corrections.Should().HaveCount(14, "seven 'Accepted in bulk' rows per line, written by the line decision service").And.OnlyContain(r => r.LogicalName == Table);
    }

    /// <summary>An extraction JSON as 049 stores it, so Accept in bulk writes its seven "Accepted in bulk" correction rows per line.</summary>
    private const string Extraction =
        "{\"fields\":{\"Date\":{\"confidence\":0.95,\"combined\":0.9,\"page\":1},\"Hours\":{\"confidence\":0.9,\"page\":1},\"Rate\":{\"confidence\":0.92,\"page\":1},"
        + "\"Quantity\":{\"confidence\":null,\"page\":null},\"Amount\":{\"confidence\":0.99,\"page\":1},\"Narrative\":{\"confidence\":0.8,\"page\":1}},"
        + "\"coding\":{\"status\":\"Coded\",\"confidence\":0.82,\"task\":{\"code\":\"L110\",\"mismatch\":false},\"activity\":{\"code\":\"A101\",\"mismatch\":false},\"expense\":{\"code\":null}}}";
}
