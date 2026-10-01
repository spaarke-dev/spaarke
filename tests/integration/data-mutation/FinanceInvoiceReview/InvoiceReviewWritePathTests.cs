using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceInvoiceReview;

/// <summary>
/// What the invoice-review confirm and reject paths WRITE, in what order, and what they leave behind when a write
/// fails (unified-access-control-r2 task 130, owner decision G5 2026-10-01).
/// </summary>
/// <remarks>
/// <para><b>The live schema</b> (spaarkedev1, read-only metadata, 2026-10-01): <c>sprk_invoice</c> has no lookup to
/// the document — the link is <c>sprk_document.sprk_invoice</c> (navigation property <c>sprk_Invoice</c>); the
/// invoice's lookups are <c>sprk_Matter</c> and <c>sprk_vendororg</c>; its primary name is <c>sprk_name</c>;
/// <c>sprk_reviewernotes</c>, <c>sprk_createdon</c> and <c>sprk_document.sprk_invoicerejectionnotes</c> do not
/// exist (the notes column is <c>sprk_invoicereviewnotes</c>).</para>
/// <para><b>Order and atomicity</b>: create invoice → link document → set review status LAST. A failed link deletes
/// the invoice it just created; a failed status update names the invoice and a retry resumes from the link.</para>
/// <para>Substitution is at the Dataverse service seams (<see cref="IFieldMappingDataverseService"/>,
/// <see cref="IGenericEntityService"/>), the ownership resolver and the job queue's own method boundary. The
/// write seam is a hand-written recording double so the asserted thing is the exact sequence of writes and their
/// payloads — no HTTP handler is mocked (ADR-038).</para>
/// </remarks>
public class InvoiceReviewWritePathTests
{
    private static readonly Guid TeamId = Guid.Parse("7e4a0000-0000-4000-8000-000000000130");

    private readonly RecordingRecords _records = new();
    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Strict);
    private readonly Mock<IRecordOwnershipResolver> _ownership = new(MockBehavior.Strict);
    private readonly Mock<JobSubmissionService> _jobs;
    private readonly List<JobContract> _submitted = new();

    public InvoiceReviewWritePathTests()
    {
        var sbOptions = Options.Create(new ServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        _jobs = new Mock<JobSubmissionService>(
            MockBehavior.Strict, sbOptions, Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        _jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => _submitted.Add(job))
            .Returns(Task.CompletedTask);

        _ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamId);
    }

    // =========================================================================================
    // Confirm — the successful path
    // =========================================================================================

    [Fact]
    public async Task Confirm_WritesCreateThenLinkThenStatus_InThatOrder()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");

        var result = await Sut().ConfirmInvoiceAsync(request, "corr-1");

        _records.Writes.Select(w => (w.Kind, w.Entity)).Should().Equal(
            (WriteKind.Upsert, "sprk_invoice"),
            (WriteKind.UpdateExisting, "sprk_document"),
            (WriteKind.UpdateExisting, "sprk_document"));
        _records.Writes[1].Fields.Should().ContainKey("sprk_Invoice@odata.bind", "the second write is the link");
        _records.Writes[2].Fields.Should().ContainKey("sprk_invoicereviewstatus", "the status is the LAST write");

        result.InvoiceId.Should().Be(_records.Writes[0].Id);
        _submitted.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"invoice-extraction-{result.InvoiceId}");
    }

    [Fact]
    public async Task Confirm_CreatePayload_BindsLookupsByNavigationProperty_IsTeamOwned_AndUsesOnlyLiveColumns()
    {
        var request = NewRequest() with
        {
            InvoiceNumber = "INV-77",
            InvoiceDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = 1234.5m,
            Notes = "checked against engagement letter",
        };
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");

        await Sut().ConfirmInvoiceAsync(request, "corr-2");

        var create = _records.Writes[0].Fields;
        create.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["ownerid@odata.bind"] = $"/teams({TeamId})",
            ["sprk_Matter@odata.bind"] = $"/sprk_matters({request.MatterId})",
            ["sprk_vendororg@odata.bind"] = $"/sprk_organizations({request.VendorOrgId})",
            ["sprk_name"] = "Acme LLP - INV-77",
            ["sprk_invoicestatus"] = 100000000,
            ["sprk_extractionstatus"] = 100000000,
            ["sprk_invoicenumber"] = "INV-77",
            ["sprk_invoicedate"] = request.InvoiceDate!.Value,
            ["sprk_totalamount"] = 1234.5m,
        });
        create.Keys.Should().NotContain(k => k.StartsWith('_') && k.EndsWith("_value"),
            "the Web API does not accept _x_value keys as a lookup write");

        var link = _records.Writes[1];
        link.Id.Should().Be(request.DocumentId);
        link.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({_records.Writes[0].Id})",
        });

        var status = _records.Writes[2].Fields;
        status["sprk_invoicereviewstatus"].Should().Be(100000001);
        status["sprk_invoicereviewnotes"].Should().Be("checked against engagement letter");
        status.Keys.Should().BeEquivalentTo("sprk_invoicereviewstatus", "sprk_invoicereviewedon", "sprk_invoicereviewnotes");
    }

    [Fact]
    public async Task Confirm_OwnerIsResolvedRecordFirstFromTheMatter_NeverFromTheCaller()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");

        await Sut().ConfirmInvoiceAsync(request, "corr-3");

        _ownership.Verify(o => o.ResolveOwningTeamAsync(
            It.Is<RecordOwnershipContext>(c =>
                c.TargetEntityLogicalName == "sprk_matter"
                && c.TargetRecordId == request.MatterId
                && c.CallerSystemUserId == null
                && c.CallerObjectId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirm_OwnerTeamUnresolved_IsRefusedBeforeAnyWrite()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-4");

        (await act.Should().ThrowAsync<InvoiceReviewException>())
            .Which.Failure.Should().Be(InvoiceReviewFailure.OwnerTeamUnresolved);
        _records.Writes.Should().BeEmpty("an invoice nobody's team owns is never created — not user-owned, not app-owned");
        _submitted.Should().BeEmpty();
    }

    // =========================================================================================
    // Confirm — no partial write
    // =========================================================================================

    [Fact]
    public async Task Confirm_CreateFails_NothingElseIsWritten()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailUpsert = new HttpRequestException("400 Bad Request");

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-5");

        await act.Should().ThrowAsync<HttpRequestException>();
        _records.Writes.Should().BeEmpty();
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_LinkFails_DeletesTheCreatedInvoice_AndLeavesTheDocumentUntouched()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailDocumentLink = new HttpRequestException("403 prvAppend");
        Guid? deleted = null;
        _entities.Setup(e => e.DeleteAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CancellationToken>((_, id, _) => deleted = id)
            .Returns(Task.CompletedTask);

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-6");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.LinkFailed);
        failure.Message.Should().Contain("Nothing was saved");

        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        deleted.Should().Be(createdId, "the invoice created in step 1 is the one compensated away");
        _records.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_LinkFailsAndTheUndoFails_NamesTheOrphanedInvoice()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailDocumentLink = new HttpRequestException("503");
        _entities.Setup(e => e.DeleteAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-7");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        failure.Failure.Should().Be(InvoiceReviewFailure.LinkFailedInvoiceNotRemoved);
        failure.InvoiceId.Should().Be(createdId);
        failure.Message.Should().Contain(createdId.ToString());
        _records.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
    }

    [Fact]
    public async Task Confirm_StatusUpdateFails_NamesTheInvoice_AndARetryResumesWithoutASecondInvoice()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailDocumentStatus = new HttpRequestException("503");

        // First attempt: create + link succeed, the final status write fails.
        var first = () => Sut().ConfirmInvoiceAsync(request, "corr-8a");
        var failure = (await first.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var invoiceId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        failure.Failure.Should().Be(InvoiceReviewFailure.StatusNotUpdated);
        failure.InvoiceId.Should().Be(invoiceId);
        failure.Message.Should().Contain(invoiceId.ToString());
        _entities.VerifyNoOtherCalls(); // the linked invoice is NOT undone — the retry completes it
        _submitted.Should().BeEmpty();

        // Dataverse now holds what the first attempt left: the document linked to that invoice.
        _records.ExistingDocument(request.DocumentId, linkedInvoiceId: invoiceId);
        _records.Invoice(invoiceId, request.MatterId, request.VendorOrgId);
        _records.FailDocumentStatus = null;
        _records.Writes.Clear();

        var retry = await Sut().ConfirmInvoiceAsync(request, "corr-8b");

        retry.InvoiceId.Should().Be(invoiceId);
        _records.Writes.Should().ContainSingle("the retry neither creates nor re-links — it only finishes the status")
            .Which.Fields["sprk_invoicereviewstatus"].Should().Be(100000001);
        _submitted.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"invoice-extraction-{invoiceId}");
    }

    [Fact]
    public async Task Confirm_ExtractionQueueFails_NamesTheInvoice()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service bus down"));

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-9");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ExtractionNotQueued);
        failure.InvoiceId.Should().Be(_records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id);
    }

    [Fact]
    public async Task Confirm_DocumentAlreadyLinkedToAnotherMattersInvoice_IsRefusedWithNoWrite()
    {
        var request = NewRequest();
        var otherInvoice = Guid.NewGuid();
        _records.ExistingDocument(request.DocumentId, linkedInvoiceId: otherInvoice);
        _records.Invoice(otherInvoice, matterId: Guid.NewGuid(), vendorId: request.VendorOrgId);

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-10");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToAnotherInvoice);
        _records.Writes.Should().BeEmpty();
        _ownership.Verify(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_DocumentGone_IsRefusedWithNoWrite()
    {
        var request = NewRequest(); // no document row registered → the read answers an empty dictionary

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-11");

        (await act.Should().ThrowAsync<InvoiceReviewException>())
            .Which.Failure.Should().Be(InvoiceReviewFailure.DocumentNotFound);
        _records.Writes.Should().BeEmpty();
    }

    // =========================================================================================
    // Reject
    // =========================================================================================

    [Fact]
    public async Task Reject_WithNotes_WritesTheLiveNotesColumn_UpdateOnly()
    {
        var documentId = Guid.NewGuid();

        await Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = documentId, Notes = "statement, not an invoice" }, "corr-12");

        var write = _records.Writes.Should().ContainSingle().Subject;
        write.Kind.Should().Be(WriteKind.UpdateExisting, "a document deleted after the check must not be recreated");
        write.Entity.Should().Be("sprk_document");
        write.Id.Should().Be(documentId);
        write.Fields["sprk_invoicereviewstatus"].Should().Be(100000002);
        write.Fields["sprk_invoicereviewnotes"].Should().Be("statement, not an invoice");
        write.Fields.Keys.Should().NotContain("sprk_invoicerejectionnotes", "that column does not exist on live sprk_document");
    }

    // =========================================================================================
    // Name
    // =========================================================================================

    [Theory]
    [InlineData("Acme LLP", "INV-1", null, "Acme LLP - INV-1")]
    [InlineData("Acme LLP", null, "2026-09-01", "Acme LLP - 2026-09-01")]
    [InlineData("Acme LLP", "  ", null, "Acme LLP - Invoice")]
    [InlineData(null, "INV-1", null, "Invoice INV-1")]
    [InlineData(" ", null, null, "Invoice")]
    public void InvoiceName_IsVendorPlusNumberOrDate(string? vendor, string? number, string? date, string expected)
    {
        var parsed = date is null ? (DateTime?)null : DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        InvoiceReviewService.BuildInvoiceName(vendor, number, parsed).Should().Be(expected);
    }

    [Fact]
    public void InvoiceName_IsCappedAtTheLiveColumnLength()
    {
        InvoiceReviewService.BuildInvoiceName(new string('v', 900), "INV-1", null).Length.Should().Be(850);
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private InvoiceReviewService Sut() => new(
        _records, _entities.Object, _ownership.Object, _jobs.Object, new FinanceTelemetry(),
        NullLogger<InvoiceReviewService>.Instance);

    private static InvoiceReviewConfirmRequest NewRequest() => new()
    {
        DocumentId = Guid.NewGuid(),
        MatterId = Guid.NewGuid(),
        VendorOrgId = Guid.NewGuid(),
    };

    internal enum WriteKind { Upsert, UpdateExisting }

    internal sealed record Write(WriteKind Kind, string Entity, Guid Id, Dictionary<string, object?> Fields);

    /// <summary>
    /// Hand-written <see cref="IFieldMappingDataverseService"/>: answers reads from registered rows (an empty
    /// dictionary for an unknown row, as the live Web API implementation does) and records every SUCCESSFUL write.
    /// A write configured to fail throws and is not recorded — it did not happen.
    /// </summary>
    internal sealed class RecordingRecords : IFieldMappingDataverseService
    {
        private readonly Dictionary<(string Entity, Guid Id), Dictionary<string, object?>> _rows = new();

        public List<Write> Writes { get; } = new();
        public Exception? FailUpsert { get; set; }
        public Exception? FailDocumentLink { get; set; }
        public Exception? FailDocumentStatus { get; set; }

        public void ExistingDocument(Guid id, Guid? linkedInvoiceId = null) =>
            _rows[("sprk_document", id)] = new() { ["_sprk_invoice_value"] = linkedInvoiceId?.ToString() };

        public void Invoice(Guid id, Guid matterId, Guid vendorId) =>
            _rows[("sprk_invoice", id)] = new()
            {
                ["_sprk_matter_value"] = matterId.ToString(),
                ["_sprk_vendororg_value"] = vendorId.ToString(),
            };

        public void Vendor(Guid id, string name) =>
            _rows[("sprk_organization", id)] = new() { ["sprk_organizationname"] = name };

        public Task<Dictionary<string, object?>> RetrieveRecordFieldsAsync(
            string entityLogicalName, Guid recordId, string[] fields, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                return Task.FromResult(new Dictionary<string, object?>());
            }

            return Task.FromResult(fields.ToDictionary(f => f, f => row.TryGetValue(f, out var v) ? v : null));
        }

        public Task UpdateRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields,
            CancellationToken ct = default, Guid? impersonateSystemUserId = null)
        {
            if (FailUpsert is not null)
            {
                return Task.FromException(FailUpsert);
            }

            Writes.Add(new Write(WriteKind.Upsert, entityLogicalName, recordId, new(fields)));
            return Task.CompletedTask;
        }

        public Task UpdateExistingRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, CancellationToken ct = default)
        {
            if (entityLogicalName == "sprk_document" && fields.ContainsKey("sprk_Invoice@odata.bind") && FailDocumentLink is not null)
            {
                return Task.FromException(FailDocumentLink);
            }

            if (entityLogicalName == "sprk_document" && fields.ContainsKey("sprk_invoicereviewstatus") && FailDocumentStatus is not null)
            {
                return Task.FromException(FailDocumentStatus);
            }

            Writes.Add(new Write(WriteKind.UpdateExisting, entityLogicalName, recordId, new(fields)));
            return Task.CompletedTask;
        }

        public Task<FieldMappingProfileEntity[]> QueryFieldMappingProfilesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FieldMappingProfileEntity?> GetFieldMappingProfileAsync(
            string sourceEntity, string targetEntity, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FieldMappingRuleEntity[]> GetFieldMappingRulesAsync(
            Guid profileId, bool activeOnly = true, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid[]> QueryChildRecordIdsAsync(
            string childEntityLogicalName, string parentLookupField, Guid parentRecordId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FieldMappingProfileEntity?> GetFieldMappingProfileWithRulesAsync(
            string sourceEntity, string targetEntity, bool activeRulesOnly = true, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
