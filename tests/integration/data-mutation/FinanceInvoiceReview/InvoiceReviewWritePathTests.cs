using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
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
/// fails or races (unified-access-control-r2 task 130, owner decision G5 2026-10-01; fix rounds 2 and 3).
/// </summary>
/// <remarks>
/// <para><b>The live schema</b> (spaarkedev1, read-only metadata, 2026-10-01): <c>sprk_invoice</c> has no lookup to
/// the document — the link is <c>sprk_document.sprk_invoice</c> (navigation property <c>sprk_Invoice</c>); the
/// invoice's lookups are <c>sprk_Matter</c> and <c>sprk_vendororg</c>; its primary name is <c>sprk_name</c>;
/// <c>sprk_reviewernotes</c>, <c>sprk_createdon</c> and <c>sprk_document.sprk_invoicerejectionnotes</c> do not
/// exist (the notes column is <c>sprk_invoicereviewnotes</c>).</para>
/// <para><b>Order and atomicity</b>: create invoice → link document (conditional on the document's version) → set
/// review status LAST (conditional too). A failed link deletes the invoice it just created; a failed status update
/// names the invoice and a retry resumes from the link; a concurrent confirm that linked first is answered with,
/// never overwritten, and its extraction is never queued twice; a reject refuses a linked document.</para>
/// <para>Substitution is at the Dataverse service seams (<see cref="IFieldMappingDataverseService"/>,
/// <see cref="IGenericEntityService"/>, <see cref="ICommunicationDataverseService"/>), the ownership resolver and the
/// job queue's own method boundary. The write seam is a hand-written double that keeps row state (including each
/// row's version, as Dataverse does) so the asserted thing is the exact sequence of writes and what remains — no
/// HTTP handler is mocked (ADR-038). Interleavings are driven by explicit gates and hooks, never by sleeps.</para>
/// </remarks>
public class InvoiceReviewWritePathTests
{
    private static readonly Guid TeamId = Guid.Parse("7e4a0000-0000-4000-8000-000000000130");
    private static readonly Guid MatterRecordTypeId = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df"); // spaarkedev1 'sprk_matter' row

    private const int ToReview = 100000000;
    private const int Confirmed = 100000001;
    private const int Rejected = 100000002;

    private readonly RecordingRecords _records = new();
    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Strict);
    private readonly Mock<IRecordOwnershipResolver> _ownership = new(MockBehavior.Strict);
    private readonly Mock<ICommunicationDataverseService> _recordTypes = new(MockBehavior.Strict);
    private readonly Mock<JobSubmissionService> _jobs;
    private readonly List<JobContract> _submitted = new();
    private readonly List<(Guid Id, CancellationToken Token)> _deleted = new();

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

        // c1-r1: the invoice owner is asked with ResolveOwnerAsync, so the reviewer can be recorded as its creator person.
        _ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecordOwnershipContext context, CancellationToken _) =>
                RecordOwnerResolution.Owned(TeamId) with { CreatedByPerson = context.RequestedBy?.SystemUserId });

        // Task 146: linking the document files it under the invoice — a reparent through the resolver, which runs the
        // create + link as its change. This pass-through applies the change and answers the matter's team.
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RecordReparent _, Func<CancellationToken, Task> apply, CancellationToken token) =>
            {
                await apply(token);
                return RecordOwnerResolution.Owned(TeamId);
            });

        _recordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterRecordTypeId));
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
            (WriteKind.UpdateIfUnchanged, "sprk_document"),
            (WriteKind.UpdateIfUnchanged, "sprk_document"));
        _records.Writes[1].Fields.Should().ContainKey("sprk_Invoice@odata.bind", "the second write is the link");
        _records.Writes[1].ExpectedVersion.Should().Be(1, "the link is conditional on the version the resume check read");
        _records.Writes[2].Fields.Should().ContainKey("sprk_invoicereviewstatus", "the status is the LAST write");
        _records.Writes[2].ExpectedVersion.Should().Be(2, "the status is conditional on the version read AFTER the link");

        result.InvoiceId.Should().Be(_records.Writes[0].Id);
        result.JobId.Should().NotBeNull();
        result.StatusUrl.Should().Be($"/api/finance/jobs/{result.JobId}/status");
        result.ExtractionAlreadyQueued.Should().BeFalse();
        _submitted.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"invoice-extraction-{result.InvoiceId}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_RecordsTheReviewerOnTheAppCreatedInvoice_OnlyWhenTheEndpointNamedThem(bool named)
    {
        // Task 146 c1-r1 (owner round 13 item 9): the invoice is created by the application, so the reviewer the endpoint
        // names (from the signed-in caller, never the body) is recorded as sprk_createdbyperson.
        var reviewer = Guid.Parse("c1c1c1c1-0000-4000-8000-0000000000f1");
        var request = NewRequest() with { RequestedBy = named ? RecordRequester.Of(reviewer) : null };
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");

        await Sut().ConfirmInvoiceAsync(request, "corr-c1r1");

        var create = _records.Writes.Should().Contain(w => w.Kind == WriteKind.Upsert && w.Entity == "sprk_invoice").Subject;
        if (named)
            create.Fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind").WhoseValue.Should().Be($"/systemusers({reviewer:D})");
        else
            create.Fields.Keys.Should().NotContain(k => k.StartsWith("sprk_CreatedByPerson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Confirm_TheReviewerIsNeverBoundFromTheRequestBody()
    {
        var body = "{\"documentId\":\"" + Guid.NewGuid() + "\",\"matterId\":\"" + Guid.NewGuid() + "\",\"vendorOrgId\":\""
                   + Guid.NewGuid() + "\",\"requestedBy\":{\"systemUserId\":\"" + Guid.NewGuid() + "\"}}";
        var bound = System.Text.Json.JsonSerializer.Deserialize<InvoiceReviewConfirmRequest>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        bound.RequestedBy.Should().BeNull("a client never chooses who created a record");
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
        _records.Matter(request.MatterId, "Real estate transaction analysis", "REAL-2026-123456.01");
        var matterId = request.MatterId.ToString("D").ToLowerInvariant();

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
            // ADR-024 resolver fields, populated together as TodoRegardingBuilder / IncomingAssociationResolver do
            // and as the 4 typed live invoices carry them (spaarkedev1, read-only 2026-10-01).
            ["sprk_regardingrecordtype@odata.bind"] = $"/sprk_recordtype_refs({MatterRecordTypeId})",
            ["sprk_regardingrecordid"] = matterId,
            ["sprk_regardingrecordname"] = "Real estate transaction analysis",
            ["sprk_regardingrecordnumber"] = "REAL-2026-123456.01",
            ["sprk_regardingrecordurl"] = $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={matterId}",
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
    public async Task Confirm_RegardingNameIsCappedAtTheLiveColumnLength_AndANumberlessMatterOmitsTheNumber()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.Matter(request.MatterId, new string('m', 150), number: null);

        await Sut().ConfirmInvoiceAsync(request, "corr-2c");

        var create = _records.Writes[0].Fields;
        ((string)create["sprk_regardingrecordname"]!).Length.Should().Be(100, "sprk_invoice.sprk_regardingrecordname is 100 chars live");
        create.Should().NotContainKey("sprk_regardingrecordnumber");
    }

    [Fact]
    public async Task Confirm_MatterRecordTypeUnresolved_CreatesWithoutTheRegardingType()
    {
        // ApplicationRequired is form-level only; live invoices exist without it. Unresolvable → left unset, never
        // a guessed id (the TodoRegardingBuilder precedent) — the other resolver fields are still written.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _recordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        await Sut().ConfirmInvoiceAsync(request, "corr-2b");

        _records.Writes[0].Fields.Should().NotContainKey("sprk_regardingrecordtype@odata.bind");
        _records.Writes[0].Fields.Should().ContainKey("sprk_regardingrecordid");
    }

    [Fact]
    public async Task Confirm_OwnerIsResolvedRecordFirstFromTheMatter_NeverFromTheCaller()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");

        await Sut().ConfirmInvoiceAsync(request, "corr-3");

        _ownership.Verify(o => o.ResolveOwnerAsync(
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
        _ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no default team"));

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-4");

        (await act.Should().ThrowAsync<InvoiceReviewException>())
            .Which.Failure.Should().Be(InvoiceReviewFailure.OwnerTeamUnresolved);
        _records.Writes.Should().BeEmpty("an invoice nobody's team owns is never created — not user-owned, not app-owned");
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_LinkingTheDocument_IsAReparentUnderTheMatter_ThatWrapsTheCreateAndTheLink()
    {
        // Task 146: the document is filed under the invoice, so its owner is re-derived (secure-if-any) over its own
        // parents plus the invoice's — the matter stands in for the not-yet-created invoice.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");

        await Sut().ConfirmInvoiceAsync(request, "corr-146");

        _ownership.Verify(o => o.ReparentAsync(
            It.Is<RecordReparent>(r =>
                r.EntityLogicalName == "sprk_document"
                && r.RecordId == request.DocumentId
                && r.InheritedParents.Count == 1
                && r.InheritedParents[0].EntityLogicalName == "sprk_matter"
                && r.InheritedParents[0].RecordId == request.MatterId),
            It.IsAny<Func<CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirm_DocumentReparentRefused_CreatesNoInvoice_AndWritesNothing()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Refused(
                RecordOwnerRefusal.SecureParentNotIsolated, "the matter is marked secure but is not isolated"));

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-146r");

        (await act.Should().ThrowAsync<InvoiceReviewException>())
            .Which.Failure.Should().Be(InvoiceReviewFailure.OwnerTeamUnresolved);
        _records.Writes.Should().BeEmpty("a refused re-file writes nothing — no invoice, no link");
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
    public async Task Confirm_CreateResponseLost_ButTheRowWasCreated_TheRowIsDeleted_AndNothingRemains()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailUpsertAfterApplying = new TaskCanceledException("the create timed out after it was sent");
        AllowInvoiceDeletes();

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-5b");

        await act.Should().ThrowAsync<TaskCanceledException>("the original failure stands — nothing was saved");
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _deleted.Should().ContainSingle().Which.Id.Should().Be(createdId);
        _records.Exists("sprk_invoice", createdId).Should().BeFalse("the row the lost response hid is removed");
        _records.LinkOf(request.DocumentId).Should().BeNull();
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_CreateResponseLost_AndTheRowCannotBeRemoved_NamesTheInvoice()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailUpsertAfterApplying = new TaskCanceledException("timed out");
        _entities.Setup(e => e.DeleteAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-5c");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        failure.Failure.Should().Be(InvoiceReviewFailure.CreateFailedInvoiceMayRemain);
        failure.InvoiceId.Should().Be(createdId);
        failure.Message.Should().Contain(createdId.ToString());
        _records.Exists("sprk_invoice", createdId).Should().BeTrue("the precondition of naming it: it is still there");
    }

    [Fact]
    public async Task Confirm_CreateResponseLost_AndTheUndoResponseLostToo_ButTheRowIsGone_ReportsTheOriginalFailure()
    {
        // Residual (b), create leg: the delete removed the row and only its response was lost. Reading the row back
        // shows it gone, so nothing "may remain" — the original failure stands.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailUpsertAfterApplying = new TaskCanceledException("timed out");
        DeletesApplyButTheirResponseIsLost();

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-5d");

        await act.Should().ThrowAsync<TaskCanceledException>();
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _records.Exists("sprk_invoice", createdId).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_DocumentOwnerCannotBeMovedAfterTheLink_DeletesTheCreatedInvoice_AndQueuesNothing()
    {
        // Task 146 b2 (verifier RESIDUAL FAIL-OPEN): the resolver ran the change (create + link) and then could not
        // assign the document's new owner. The link is this flow's change, so the flow deletes the invoice it created,
        // which removes the link too — the document is never left filed under a secure invoice while owned elsewhere.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        AllowInvoiceDeletes();
        var ownerFailure = new InvalidOperationException("Read Privilege Check For Owner failed ... prvReadsprk_Document");
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RecordReparent _, Func<CancellationToken, Task> apply, CancellationToken token) =>
            {
                await apply(token);
                throw ownerFailure;
            });

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-146b2");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.LinkFailed);
        failure.InnerException.Should().BeSameAs(ownerFailure);
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _deleted.Should().ContainSingle().Which.Id.Should().Be(createdId,
            "deleting the created invoice also removes the link (RemoveLink cascade)");
        _records.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_LinkFails_DeletesTheCreatedInvoice_AndLeavesTheDocumentUntouched()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailDocumentLink = new HttpRequestException("403 prvAppend");
        AllowInvoiceDeletes();

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-6");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.LinkFailed);
        failure.Message.Should().Contain("Nothing was saved");

        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _deleted.Should().ContainSingle().Which.Id.Should().Be(createdId, "the invoice created in step 1 is the one compensated away");
        _records.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_LinkResponseLost_ButTheLinkLanded_ContinuesWithoutDeleting()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailDocumentLinkAfterApplying = new TaskCanceledException("the link timed out after it was sent");

        var result = await Sut().ConfirmInvoiceAsync(request, "corr-6b");

        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        result.InvoiceId.Should().Be(createdId);
        _records.LinkOf(request.DocumentId).Should().Be(createdId);
        _entities.VerifyNoOtherCalls(); // the linked invoice is NOT deleted — that would undo a link that landed
        _records.Writes.Should().Contain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
        _submitted.Should().ContainSingle();
    }

    [Fact]
    public async Task Confirm_RequestCancelledBetweenCreateAndLink_TheUndoStillRuns()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        using var cts = new CancellationTokenSource();
        _records.OnUpsertApplied = () => cts.Cancel(); // the caller goes away once the invoice exists
        AllowInvoiceDeletes();

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-6c", cts.Token);

        (await act.Should().ThrowAsync<InvoiceReviewException>()).Which.Failure.Should().Be(InvoiceReviewFailure.LinkFailed);
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _deleted.Should().ContainSingle().Which.Id.Should().Be(createdId);
        _deleted[0].Token.IsCancellationRequested.Should().BeFalse("the undo must not inherit the cancelled request token");
        _records.Exists("sprk_invoice", createdId).Should().BeFalse();
        _records.LinkOf(request.DocumentId).Should().BeNull();
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
        _records.Exists("sprk_invoice", createdId).Should().BeTrue("orphaned is reported only for an invoice that is still there");
        _records.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
    }

    [Fact]
    public async Task Confirm_LinkFails_AndTheUndoResponseIsLostButTheInvoiceIsGone_ReportsTheCleanFailure()
    {
        // Residual (b): the compensating delete removed the row; only its response was lost. Read back → gone, so
        // the error must not claim an orphan (and must not name an invoice that does not exist).
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.FailDocumentLink = new HttpRequestException("503");
        DeletesApplyButTheirResponseIsLost();

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-7b");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        failure.Failure.Should().Be(InvoiceReviewFailure.LinkFailed);
        failure.InvoiceId.Should().BeNull();
        failure.Message.Should().NotContain(createdId.ToString());
        _records.Exists("sprk_invoice", createdId).Should().BeFalse();
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
        _records.LinkOf(request.DocumentId).Should().Be(invoiceId);
        _records.FailDocumentStatus = null;
        _records.Writes.Clear();

        var retry = await Sut().ConfirmInvoiceAsync(request, "corr-8b");

        retry.InvoiceId.Should().Be(invoiceId);
        var statusWrite = _records.Writes.Should().ContainSingle("the retry neither creates nor re-links — it only finishes the status").Subject;
        statusWrite.Fields["sprk_invoicereviewstatus"].Should().Be(100000001);
        statusWrite.Kind.Should().Be(WriteKind.UpdateIfUnchanged, "the resume's status write is conditional on the version it read");
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
    public async Task Confirm_DocumentAlreadyLinkedToAnotherMattersInvoice_IsRefusedWithNoWrite_AndNeverNamesThatInvoice()
    {
        var request = NewRequest();
        var otherInvoice = Guid.NewGuid();
        _records.ExistingDocument(request.DocumentId, linkedInvoiceId: otherInvoice);
        _records.Invoice(otherInvoice, matterId: Guid.NewGuid(), vendorId: request.VendorOrgId);

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-10");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToAnotherInvoice);
        failure.InvoiceId.Should().BeNull("the other matter's invoice was never authorized for this caller");
        failure.Message.Should().NotContain(otherInvoice.ToString()).And.NotContainEquivalentOf(otherInvoice.ToString("N"));
        _records.Writes.Should().BeEmpty();
        _ownership.Verify(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Never);
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
    // Confirm — two concurrent confirms of the same document (round 2 item 1; round 3 item 1)
    // =========================================================================================

    /// <summary>
    /// Both confirms read the document at the SAME version before either links (held at the create step by a
    /// gate). Whichever links first wins; the other's conditional link fails (412), it deletes its own invoice and
    /// answers with the winner's — WITHOUT a second extraction job (sdap-jobs has duplicate detection off, so a
    /// same-key submission would run twice) and without inventing a job id. Exactly one invoice, one job — both orders.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_TwoConcurrentConfirms_SameMatter_LeaveExactlyOneInvoice_AndOneExtractionJob_InEitherOrder(
        bool firstReaderLinksFirst)
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        AllowInvoiceDeletes();
        var gate = _records.HoldCreates(2);

        var a = Sut().ConfirmInvoiceAsync(request, "corr-A");
        await gate.Arrived[0].Task;            // A has read version 1 and created its invoice
        var b = Sut().ConfirmInvoiceAsync(request, "corr-B");
        await gate.Arrived[1].Task;            // B has ALSO read version 1 and created its own

        var invoiceA = gate.CreatedIds[0];
        var invoiceB = gate.CreatedIds[1];
        var (winner, loser, winnerTask, loserTask, winnerIndex, loserIndex) = firstReaderLinksFirst
            ? (invoiceA, invoiceB, a, b, 0, 1)
            : (invoiceB, invoiceA, b, a, 1, 0);

        gate.Release[winnerIndex].SetResult();
        var winnerResult = await winnerTask;
        gate.Release[loserIndex].SetResult();
        var loserResult = await loserTask;

        winnerResult.InvoiceId.Should().Be(winner);
        winnerResult.JobId.Should().NotBeNull();
        winnerResult.ExtractionAlreadyQueued.Should().BeFalse();

        loserResult.InvoiceId.Should().Be(winner, "the loser answers with the invoice that was linked first");
        loserResult.JobId.Should().BeNull("the loser queued nothing, and a job id is never fabricated");
        loserResult.StatusUrl.Should().BeNull();
        loserResult.ExtractionAlreadyQueued.Should().BeTrue();

        _submitted.Should().ContainSingle("ONE extraction job for ONE invoice — the loser does not submit a second")
            .Which.JobId.Should().Be(winnerResult.JobId!.Value);
        _submitted[0].IdempotencyKey.Should().Be($"invoice-extraction-{winner}");

        _records.LinkOf(request.DocumentId).Should().Be(winner, "the first link is never overwritten");
        _records.Exists("sprk_invoice", winner).Should().BeTrue();
        _records.Exists("sprk_invoice", loser).Should().BeFalse("the loser deletes the invoice it created — no orphan");
        _deleted.Select(d => d.Id).Should().Equal(loser);
        _records.Writes.Count(w => w.Fields.ContainsKey("sprk_Invoice@odata.bind")).Should().Be(1, "only one link was applied");
        _records.Writes.Count(w => w.Fields.ContainsKey("sprk_invoicereviewstatus")).Should().Be(1, "only the winner writes the status");
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_ConcurrentConfirmForAnotherMatterLinkedFirst_TheLoserIs409_AndLeavesNoInvoice(bool firstReaderLinksFirst)
    {
        var requestA = NewRequest();
        var requestB = requestA with { MatterId = Guid.NewGuid() }; // same document, different matter
        _records.ExistingDocument(requestA.DocumentId);
        _records.Vendor(requestA.VendorOrgId, "Acme LLP");
        AllowInvoiceDeletes();
        var gate = _records.HoldCreates(2);

        var a = Sut().ConfirmInvoiceAsync(requestA, "corr-A");
        await gate.Arrived[0].Task;
        var b = Sut().ConfirmInvoiceAsync(requestB, "corr-B");
        await gate.Arrived[1].Task;

        var (winnerIndex, loserIndex, winnerTask, loserTask) = firstReaderLinksFirst ? (0, 1, a, b) : (1, 0, b, a);
        var winner = gate.CreatedIds[winnerIndex];
        var loser = gate.CreatedIds[loserIndex];

        gate.Release[winnerIndex].SetResult();
        (await winnerTask).InvoiceId.Should().Be(winner);
        gate.Release[loserIndex].SetResult();
        var failure = (await FluentActions.Awaiting(() => loserTask).Should().ThrowAsync<InvoiceReviewException>()).Which;

        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToAnotherInvoice);
        failure.InvoiceId.Should().BeNull();
        failure.Message.Should().NotContain(winner.ToString());
        _records.LinkOf(requestA.DocumentId).Should().Be(winner);
        _records.Exists("sprk_invoice", loser).Should().BeFalse("the refused confirm deleted the invoice it created");
        _submitted.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"invoice-extraction-{winner}");
    }

    // =========================================================================================
    // Confirm — an unrelated write between the read and the link (round 3 item 2)
    // =========================================================================================

    [Fact]
    public async Task Confirm_AnUnrelatedWriteBetweenReadAndLink_IsRetried_AndSucceedsWithExactlyOneInvoice()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        var touched = false;
        _records.BeforeConditionalWrite = fields =>
        {
            // e.g. a profiling / indexing status update lands once, after the read and before the first link
            if (fields.ContainsKey("sprk_Invoice@odata.bind") && !touched)
            {
                touched = true;
                _records.TouchDocument(request.DocumentId);
            }

            return Task.CompletedTask;
        };

        var result = await Sut().ConfirmInvoiceAsync(request, "corr-12a");

        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        result.InvoiceId.Should().Be(createdId);
        _records.LinkAttempts.Should().Be(2, "the first link met the unrelated write (412) and the second used the fresh version");
        _records.LinkOf(request.DocumentId).Should().Be(createdId);
        _records.Exists("sprk_invoice", createdId).Should().BeTrue();
        _entities.VerifyNoOtherCalls(); // nothing deleted — the retry kept the invoice
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
        _submitted.Should().ContainSingle();
    }

    [Fact]
    public async Task Confirm_DocumentChangedOnEveryLinkAttempt_Is409AfterTheBound_AndLeavesNoInvoice()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        _records.BeforeConditionalWrite = fields =>
        {
            if (fields.ContainsKey("sprk_Invoice@odata.bind"))
            {
                _records.TouchDocument(request.DocumentId); // a writer that never lets the version stand still
            }

            return Task.CompletedTask;
        };
        AllowInvoiceDeletes();

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-12b");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentChangedConcurrently);
        failure.ReasonCode.Should().Be("sdap.finance.invoice_review.document_changed");
        _records.LinkAttempts.Should().Be(3, "at most 3 attempts in total");
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _records.Exists("sprk_invoice", createdId).Should().BeFalse();
        _records.LinkOf(request.DocumentId).Should().BeNull();
        _records.Writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"));
        _submitted.Should().BeEmpty();
    }

    // =========================================================================================
    // Confirm vs reject (round 3 item 3)
    // =========================================================================================

    [Fact]
    public async Task ConfirmLinksFirst_ThenAConcurrentReject_TheRejectIs409_AndConfirmedStands()
    {
        // The reject reads the document (unlinked, version 1); before its write, a confirm links and confirms it.
        // The reject's conditional write fails (412), its re-read sees the link, and it refuses — it does not
        // overwrite ConfirmedInvoice.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        InvoiceReviewResult? confirmed = null;
        _records.BeforeConditionalWrite = async fields =>
        {
            if (confirmed is null && fields.TryGetValue("sprk_invoicereviewstatus", out var s) && (int)s! == Rejected)
            {
                confirmed = await Sut().ConfirmInvoiceAsync(request, "corr-confirm");
            }
        };

        var act = () => Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = request.DocumentId, Notes = "not an invoice" }, "corr-reject");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToInvoice);
        failure.ReasonCode.Should().Be("sdap.finance.invoice_review.document_linked_to_invoice");
        failure.InvoiceId.Should().BeNull();
        confirmed.Should().NotBeNull();
        failure.Message.Should().NotContain(confirmed!.InvoiceId.ToString());
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed, "the reject did not overwrite the confirmation");
        _records.LinkOf(request.DocumentId).Should().Be(confirmed.InvoiceId);
        _records.Writes.Where(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"))
            .Select(w => (int)w.Fields["sprk_invoicereviewstatus"]!)
            .Should().NotContain(Rejected);
    }

    [Fact]
    public async Task RejectLandsFirst_BetweenAConfirmsReadAndLink_TheConfirmIs409_AndRejectedStands()
    {
        // The confirm reads the document (unlinked, ToReview); while its invoice is being created, a reject lands.
        // The confirm's link fails (412), its re-read sees the review decision changed, and it deletes its invoice
        // and refuses — it does not link (and later mark Confirmed) over the reject.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        AllowInvoiceDeletes();
        _records.OnUpsertAppliedAsync = () => Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = request.DocumentId, Notes = "statement" }, "corr-reject");

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-confirm");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ReviewDecisionChanged);
        failure.ReasonCode.Should().Be("sdap.finance.invoice_review.review_decision_changed");
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _records.Exists("sprk_invoice", createdId).Should().BeFalse("the refused confirm removed its invoice");
        _records.LinkOf(request.DocumentId).Should().BeNull();
        _records.StatusOf(request.DocumentId).Should().Be(Rejected, "the reject stands");
        _records.LinkAttempts.Should().Be(1, "a changed decision is not retried");
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task AlreadyRejected_ASecondRejectLandsBetweenAConfirmsReadAndLink_TheConfirmIs409_AndTheRejectStands()
    {
        // The document is ALREADY RejectedNotInvoice when the confirm reads it (confirming it then is a deliberate
        // re-decision, which is allowed). While the confirm's invoice is being created, someone rejects it AGAIN,
        // with the same notes. The status does not change, so only the reviewed-on stamp shows a decision was
        // written: the confirm must treat it as a concurrent reject (409, invoice removed), NOT as an unrelated
        // write to retry the link over — which would leave the second reject's caller told "Rejected" while the
        // document ends up Confirmed.
        var request = NewRequest();
        _records.ExistingDocument(
            request.DocumentId, reviewStatus: Rejected, reviewedOnUtc: DateTime.UtcNow.AddDays(-1), reviewNotes: "statement");
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        AllowInvoiceDeletes();
        _records.OnUpsertAppliedAsync = () => Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = request.DocumentId, Notes = "statement" }, "corr-reject-2");

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-confirm");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ReviewDecisionChanged);
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        _records.Exists("sprk_invoice", createdId).Should().BeFalse("the refused confirm removed its invoice");
        _records.LinkOf(request.DocumentId).Should().BeNull();
        _records.StatusOf(request.DocumentId).Should().Be(Rejected, "the second reject stands");
        _records.LinkAttempts.Should().Be(1, "a written decision is not retried over");
        _records.Writes.Where(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"))
            .Select(w => (int)w.Fields["sprk_invoicereviewstatus"]!)
            .Should().NotContain(Confirmed);
        _submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_DocumentRelinkedBetweenLinkAndStatus_TheStatusIsNotWritten_AndTheInvoiceIsNamed()
    {
        // Proves the confirm's own status write is conditional AND checks the link it is confirming: someone
        // re-points the document after this confirm linked it; the status write 412s, the re-read sees another
        // link, and ConfirmedInvoice is NOT written over it.
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        _records.Vendor(request.VendorOrgId, "Acme LLP");
        var relinked = false;
        _records.BeforeConditionalWrite = fields =>
        {
            if (!relinked && fields.ContainsKey("sprk_invoicereviewstatus"))
            {
                relinked = true;
                _records.SetLink(request.DocumentId, Guid.NewGuid());
            }

            return Task.CompletedTask;
        };

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-13");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var createdId = _records.Writes.Single(w => w.Kind == WriteKind.Upsert).Id;
        failure.Failure.Should().Be(InvoiceReviewFailure.StatusNotUpdated);
        failure.InvoiceId.Should().Be(createdId);
        _records.StatusOf(request.DocumentId).Should().Be(ToReview, "nothing marked the re-pointed document confirmed");
        _submitted.Should().BeEmpty();
    }

    // =========================================================================================
    // Reject
    // =========================================================================================

    [Fact]
    public async Task Reject_WithNotes_WritesTheLiveNotesColumn_ConditionallyAndUpdateOnly()
    {
        var documentId = Guid.NewGuid();
        _records.ExistingDocument(documentId);

        await Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = documentId, Notes = "statement, not an invoice" }, "corr-14");

        var write = _records.Writes.Should().ContainSingle().Subject;
        write.Kind.Should().Be(WriteKind.UpdateIfUnchanged, "conditional on the version read — never creates, never overwrites unseen");
        write.ExpectedVersion.Should().Be(1);
        write.Entity.Should().Be("sprk_document");
        write.Id.Should().Be(documentId);
        write.Fields["sprk_invoicereviewstatus"].Should().Be(100000002);
        write.Fields["sprk_invoicereviewnotes"].Should().Be("statement, not an invoice");
        write.Fields.Keys.Should().NotContain("sprk_invoicerejectionnotes", "that column does not exist on live sprk_document");
    }

    [Fact]
    public async Task Reject_DocumentLinkedToAnInvoice_Is409_WithNoWrite_AndNeverNamesTheInvoice()
    {
        var documentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        _records.ExistingDocument(documentId, linkedInvoiceId: invoiceId, reviewStatus: Confirmed);

        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = documentId }, "corr-15");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToInvoice);
        failure.InvoiceId.Should().BeNull();
        failure.Message.Should().NotContain(invoiceId.ToString()).And.NotContainEquivalentOf(invoiceId.ToString("N"));
        _records.Writes.Should().BeEmpty();
        _records.StatusOf(documentId).Should().Be(Confirmed);
    }

    [Fact]
    public async Task Reject_DocumentGone_ThrowsNotFound_WithNoWrite()
    {
        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = Guid.NewGuid() }, "corr-16");

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _records.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Reject_DocumentChangedOnEveryAttempt_Is409AfterTheBound_WithNoWrite()
    {
        var documentId = Guid.NewGuid();
        _records.ExistingDocument(documentId);
        var attempts = 0;
        _records.BeforeConditionalWrite = _ =>
        {
            attempts++;
            _records.TouchDocument(documentId);
            return Task.CompletedTask;
        };

        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = documentId }, "corr-17");

        (await act.Should().ThrowAsync<InvoiceReviewException>())
            .Which.Failure.Should().Be(InvoiceReviewFailure.DocumentChangedConcurrently);
        attempts.Should().Be(3);
        _records.Writes.Should().BeEmpty();
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
        _records, _entities.Object, _ownership.Object, _recordTypes.Object, _jobs.Object, new FinanceTelemetry(),
        NullLogger<InvoiceReviewService>.Instance);

    /// <summary>
    /// Deletes succeed and remove the row — unless handed an already-cancelled token, which (like the real
    /// ServiceClient) throws before doing anything. Every delete and its token are recorded.
    /// </summary>
    private void AllowInvoiceDeletes() =>
        _entities.Setup(e => e.DeleteAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, CancellationToken>((_, id, token) =>
            {
                token.ThrowIfCancellationRequested();
                _deleted.Add((id, token));
                _records.Remove("sprk_invoice", id);
                return Task.CompletedTask;
            });

    /// <summary>A delete that removes the row and then loses its response (throws, as a timeout would).</summary>
    private void DeletesApplyButTheirResponseIsLost() =>
        _entities.Setup(e => e.DeleteAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, CancellationToken>((_, id, token) =>
            {
                _deleted.Add((id, token));
                _records.Remove("sprk_invoice", id);
                return Task.FromException(new TaskCanceledException("the delete timed out after it was applied"));
            });

    private static InvoiceReviewConfirmRequest NewRequest() => new()
    {
        DocumentId = Guid.NewGuid(),
        MatterId = Guid.NewGuid(),
        VendorOrgId = Guid.NewGuid(),
    };

    internal enum WriteKind { Upsert, UpdateExisting, UpdateIfUnchanged }

    internal sealed record Write(
        WriteKind Kind, string Entity, Guid Id, Dictionary<string, object?> Fields, long? ExpectedVersion = null);

    /// <summary>Holds the first N creates after they are applied, until released — the interleaving control.</summary>
    internal sealed class CreateGate
    {
        public CreateGate(int count)
        {
            Arrived = Enumerable.Range(0, count)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            Release = Enumerable.Range(0, count)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            CreatedIds = new Guid[count];
        }

        public TaskCompletionSource[] Arrived { get; }
        public TaskCompletionSource[] Release { get; }
        public Guid[] CreatedIds { get; }
        public int Next { get; set; }
    }

    /// <summary>
    /// Hand-written <see cref="IFieldMappingDataverseService"/> that keeps row state like Dataverse: reads answer
    /// from registered rows (an empty dictionary for an unknown row, as the live Web API implementation does);
    /// every successful write is recorded and applied; a document's <c>versionnumber</c> rises on every write and
    /// the conditional write refuses a stale one with <see cref="DBConcurrencyException"/> (the live 412) and an
    /// absent row with <see cref="KeyNotFoundException"/> (the live 404). A write configured to fail throws and is not
    /// applied — unless it is an "after applying" failure, which models a lost response. A cancelled token throws
    /// before anything happens, as the real transport does. Hooks run other writers at exact points.
    /// </summary>
    internal sealed class RecordingRecords : IFieldMappingDataverseService
    {
        private readonly Dictionary<(string Entity, Guid Id), Dictionary<string, object?>> _rows = new();
        private CreateGate? _gate;

        public List<Write> Writes { get; } = new();
        public Exception? FailUpsert { get; set; }
        public Exception? FailUpsertAfterApplying { get; set; }
        public Exception? FailDocumentLink { get; set; }
        public Exception? FailDocumentLinkAfterApplying { get; set; }
        public Exception? FailDocumentStatus { get; set; }
        public Action? OnUpsertApplied { get; set; }
        public Func<Task>? OnUpsertAppliedAsync { get; set; }

        /// <summary>Runs at the start of every conditional write, BEFORE its version is checked — another writer's slot.</summary>
        public Func<Dictionary<string, object?>, Task>? BeforeConditionalWrite { get; set; }

        /// <summary>Conditional document writes that carried the link, applied or refused.</summary>
        public int LinkAttempts { get; private set; }

        public void ExistingDocument(
            Guid id, Guid? linkedInvoiceId = null, int? reviewStatus = null, DateTime? reviewedOnUtc = null,
            string? reviewNotes = null) =>
            _rows[("sprk_document", id)] = new()
            {
                ["_sprk_invoice_value"] = linkedInvoiceId?.ToString(),
                ["sprk_invoicereviewstatus"] = reviewStatus is null ? null : (long)reviewStatus.Value,
                ["sprk_invoicereviewedon"] = reviewedOnUtc is null ? null : AsWebApiDateTime(reviewedOnUtc.Value),
                ["sprk_invoicereviewnotes"] = reviewNotes,
                ["versionnumber"] = 1L,
            };

        /// <summary>The Web API answers a date-time column as an ISO string at whole-second precision.</summary>
        private static string AsWebApiDateTime(DateTime value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        public void Invoice(Guid id, Guid matterId, Guid vendorId) =>
            _rows[("sprk_invoice", id)] = new()
            {
                ["_sprk_matter_value"] = matterId.ToString(),
                ["_sprk_vendororg_value"] = vendorId.ToString(),
            };

        public void Vendor(Guid id, string name) =>
            _rows[("sprk_organization", id)] = new() { ["sprk_organizationname"] = name };

        public void Matter(Guid id, string name, string? number) =>
            _rows[("sprk_matter", id)] = new() { ["sprk_mattername"] = name, ["sprk_matternumber"] = number };

        public void TouchDocument(Guid id) => Bump(_rows[("sprk_document", id)]);

        public void SetLink(Guid documentId, Guid invoiceId)
        {
            var row = _rows[("sprk_document", documentId)];
            row["_sprk_invoice_value"] = invoiceId.ToString();
            Bump(row);
        }

        public bool Exists(string entity, Guid id) => _rows.ContainsKey((entity, id));

        public void Remove(string entity, Guid id) => _rows.Remove((entity, id));

        public Guid? LinkOf(Guid documentId) =>
            _rows.TryGetValue(("sprk_document", documentId), out var row)
            && row["_sprk_invoice_value"] is string raw ? Guid.Parse(raw) : null;

        public int? StatusOf(Guid documentId) =>
            _rows.TryGetValue(("sprk_document", documentId), out var row)
            && row["sprk_invoicereviewstatus"] is long status ? (int)status : null;

        public CreateGate HoldCreates(int count) => _gate = new CreateGate(count);

        public Task<Dictionary<string, object?>> RetrieveRecordFieldsAsync(
            string entityLogicalName, Guid recordId, string[] fields, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                return Task.FromResult(new Dictionary<string, object?>());
            }

            return Task.FromResult(fields.ToDictionary(f => f, f => row.TryGetValue(f, out var v) ? v : null));
        }

        public async Task UpdateRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields,
            CancellationToken ct = default, Guid? impersonateSystemUserId = null)
        {
            ct.ThrowIfCancellationRequested();
            if (FailUpsert is not null)
            {
                throw FailUpsert;
            }

            Writes.Add(new Write(WriteKind.Upsert, entityLogicalName, recordId, new(fields)));
            if (entityLogicalName == "sprk_invoice")
            {
                _rows[("sprk_invoice", recordId)] = new()
                {
                    ["_sprk_matter_value"] = BoundId(fields, "sprk_Matter@odata.bind")?.ToString(),
                    ["_sprk_vendororg_value"] = BoundId(fields, "sprk_vendororg@odata.bind")?.ToString(),
                };
            }

            OnUpsertApplied?.Invoke();
            if (OnUpsertAppliedAsync is not null)
            {
                await OnUpsertAppliedAsync();
            }

            if (FailUpsertAfterApplying is not null)
            {
                throw FailUpsertAfterApplying;
            }

            if (_gate is { } gate && gate.Next < gate.Arrived.Length)
            {
                var index = gate.Next++;
                gate.CreatedIds[index] = recordId;
                gate.Arrived[index].SetResult();
                await gate.Release[index].Task;
            }
        }

        public Task UpdateExistingRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Writes.Add(new Write(WriteKind.UpdateExisting, entityLogicalName, recordId, new(fields)));
            if (_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                Apply(row, fields); // an UNconditional write overwrites whatever is there
            }

            return Task.CompletedTask;
        }

        public async Task UpdateRecordFieldsIfUnchangedAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, long expectedVersion,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var isLink = fields.ContainsKey("sprk_Invoice@odata.bind");
            if (isLink)
            {
                LinkAttempts++;
            }

            if (BeforeConditionalWrite is not null)
            {
                await BeforeConditionalWrite(fields);
            }

            if (entityLogicalName == "sprk_document" && isLink && FailDocumentLink is not null)
            {
                throw FailDocumentLink;
            }

            if (entityLogicalName == "sprk_document" && fields.ContainsKey("sprk_invoicereviewstatus") && FailDocumentStatus is not null)
            {
                throw FailDocumentStatus;
            }

            if (!_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                throw new KeyNotFoundException($"{entityLogicalName} record was not found.");
            }

            if ((long)row["versionnumber"]! != expectedVersion)
            {
                throw new DBConcurrencyException("412 Precondition Failed");
            }

            Writes.Add(new Write(WriteKind.UpdateIfUnchanged, entityLogicalName, recordId, new(fields), expectedVersion));
            Apply(row, fields);

            if (isLink && FailDocumentLinkAfterApplying is not null)
            {
                throw FailDocumentLinkAfterApplying;
            }
        }

        private static void Apply(Dictionary<string, object?> row, Dictionary<string, object?> fields)
        {
            if (BoundId(fields, "sprk_Invoice@odata.bind") is { } invoiceId)
            {
                row["_sprk_invoice_value"] = invoiceId.ToString();
            }

            if (fields.TryGetValue("sprk_invoicereviewstatus", out var status) && status is int value)
            {
                row["sprk_invoicereviewstatus"] = (long)value; // the Web API reads an option set back as a number (long)
            }

            if (fields.TryGetValue("sprk_invoicereviewedon", out var reviewedOn) && reviewedOn is DateTime stamp)
            {
                row["sprk_invoicereviewedon"] = AsWebApiDateTime(stamp);
            }

            if (fields.TryGetValue("sprk_invoicereviewnotes", out var notes))
            {
                row["sprk_invoicereviewnotes"] = notes;
            }

            Bump(row);
        }

        private static void Bump(Dictionary<string, object?> row) =>
            row["versionnumber"] = row.TryGetValue("versionnumber", out var v) && v is long version ? version + 1 : 1L;

        private static Guid? BoundId(Dictionary<string, object?> fields, string bind)
        {
            if (!fields.TryGetValue(bind, out var raw) || raw is not string path)
            {
                return null;
            }

            var open = path.IndexOf('(');
            return Guid.Parse(path[(open + 1)..path.IndexOf(')')]);
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
