using System.Text.Json;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// <see cref="InvoiceSubmissionService"/> (ebilling-r1 task 044, FR-17): payload validation (D-91 first), the write order, the row and
/// job contract task 049 relies on, and compensation (a failure after the upload leaves no Received submission behind).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): these are the intake guarantees every channel relies on. Dataverse, SPE, the allocator and the queue are
/// substituted at their narrow seams; the validation, the 013 guard and the orchestration are real.
/// </remarks>
public sealed class InvoiceSubmissionServiceTests
{
    internal static readonly Guid FirmId = Guid.NewGuid();
    internal static readonly Guid MatterId = Guid.NewGuid();
    internal static readonly Guid ContactId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid RecordTypeId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();
    private const string Drive = "b!matterdrive";

    internal static byte[] Pdf(int size = 64)
    {
        var bytes = new byte[size];
        "%PDF-1.7"u8.ToArray().CopyTo(bytes, 0);
        return bytes;
    }

    // ── Harness ──

    internal sealed class Harness
    {
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<IDocumentDataverseService> Documents = new();
        public readonly Mock<ICommunicationDataverseService> RecordTypes = new();
        public readonly Mock<ExternalDataService> ExternalData;
        public readonly Mock<SpeFileStore> Spe;
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();
        public readonly Mock<ITrackingNumberAllocator> Allocator = new();
        public readonly Mock<JobSubmissionService> Jobs;
        public readonly EbillingOptions Options = new() { Enabled = true, MaxSubmissionFileSizeMb = 1 };

        public readonly List<(string Drive, string Name, ConflictBehavior Conflict)> Uploads = new();
        public readonly List<(string Drive, string Item)> FileDeletes = new();
        public readonly List<(string Entity, Guid Id)> Deleted = new();
        public readonly List<CreateDocumentRequest> DocumentCreates = new();
        public readonly List<(string Id, UpdateDocumentRequest Request)> DocumentUpdates = new();
        public readonly List<(ExternalDataService.DocumentParentKind Kind, Guid Parent, ExternalUploadedFilePointers Pointers, Guid Team)> ExternalCreates = new();
        public readonly List<(Guid SubmissionId, Guid FirmId, string Number, IReadOnlyDictionary<string, object>? Fields)> Allocations = new();
        public readonly List<JobContract> JobsSent = new();
        public readonly List<RecordOwnershipContext> OwnerContexts = new();

        public RecordOwnerResolution OwnerAnswer = RecordOwnerResolution.Owned(TeamId);
        public ContainerDecision Container = new(ContainerDecisionOutcome.ResolvedFallback, Drive);
        public TrackingNumberResult Allocation = TrackingNumberResult.Created("INV-1.1", 1);
        public Entity? Firm = new("sprk_organization", FirmId) { ["statecode"] = new OptionSetValue(0) };
        public Entity? Matter = new("sprk_matter", MatterId) { ["sprk_mattername"] = "Acme v. Roe", ["sprk_matternumber"] = "M-1" };
        public Entity? Submission;
        public Exception? MatterReadFailure;
        public Exception? JobFailure;
        public Exception? DocumentCreateFailure;
        public Exception? DocumentUpdateFailure;
        public Exception? AllocatorFailure;

        public int Writes => Uploads.Count + DocumentCreates.Count + ExternalCreates.Count + Allocations.Count + JobsSent.Count;

        public Harness()
        {
            var gcf = Mock.Of<IGraphClientFactory>();
            Spe = new Mock<SpeFileStore>(
                MockBehavior.Strict,
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
                null!);
            Spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string name, Stream content, ConflictBehavior conflict, CancellationToken _) =>
                {
                    Uploads.Add((drive, name, conflict));
                    return new FileHandleDto("item-1", name, null, content.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, "https://web/item-1", drive);
                });
            Spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    FileDeletes.Add((drive, item));
                    return true;
                });

            ExternalData = new Mock<ExternalDataService>(
                new HttpClient(), new ConfigurationBuilder().Build(), Mock.Of<TokenCredential>(), NullLogger<ExternalDataService>.Instance, null!, null!);
            ExternalData.Setup(x => x.CreateDocumentAsync(
                    It.IsAny<ExternalDataService.DocumentParentKind>(), It.IsAny<Guid>(), It.IsAny<ExternalUploadedFilePointers>(), It.IsAny<Guid>(),
                    It.IsAny<CallerPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ExternalDataService.DocumentParentKind kind, Guid parent, ExternalUploadedFilePointers pointers, Guid team, CallerPrincipal _, CancellationToken _) =>
                {
                    ExternalCreates.Add((kind, parent, pointers, team));
                    return new ExternalDocumentDto { SprkDocumentid = DocumentId.ToString() };
                });

            Documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest r, CancellationToken _) =>
                {
                    if (DocumentCreateFailure is not null)
                    {
                        throw DocumentCreateFailure;
                    }

                    DocumentCreates.Add(r);
                    return DocumentId.ToString();
                });
            Documents.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Returns((string id, UpdateDocumentRequest r, CancellationToken _) =>
                {
                    if (DocumentUpdateFailure is not null)
                    {
                        throw DocumentUpdateFailure;
                    }

                    DocumentUpdates.Add((id, r));
                    return Task.CompletedTask;
                });

            RecordTypes.Setup(r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", RecordTypeId) { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber" });

            Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => entity switch
                {
                    "sprk_organization" => Firm!,
                    "sprk_matter" => MatterReadFailure is { } failure ? throw failure : Matter!,
                    "sprk_invoicesubmission" => Submission!,
                    _ => null!,
                });
            Entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback((string entity, Guid id, CancellationToken _) => Deleted.Add((entity, id)))
                .Returns(Task.CompletedTask);

            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RecordOwnershipContext c, CancellationToken _) =>
                {
                    OwnerContexts.Add(c);
                    return OwnerAnswer;
                });

            Allocator.Setup(a => a.CreateSubmissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid firm, string number, IReadOnlyDictionary<string, object>? fields, CancellationToken _) =>
                {
                    Allocations.Add((id, firm, number, fields));
                    return AllocatorFailure is { } failure ? throw failure : Allocation;
                });

            var sbOptions = Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
            {
                QueueName = "test-jobs",
                CommunicationQueueName = "test-comms",
                ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
            });
            Jobs = new Mock<JobSubmissionService>(sbOptions, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
            Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken _) =>
                {
                    if (JobFailure is not null)
                    {
                        throw JobFailure;
                    }

                    JobsSent.Add(job);
                    return Task.CompletedTask;
                });
        }

        public IOptionsMonitor<EbillingOptions> Monitor => Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == Options);

        public InvoiceSubmissionService Build()
        {
            var monitor = Monitor;
            var containers = new RecordContainerResolver(
                Mock.Of<ISecurableEntityRegistry>(), Mock.Of<IGenericEntityService>(), NullLogger<RecordContainerResolver>.Instance);
            return new TestableService(this, Allocator.Object, Entities.Object, Documents.Object, RecordTypes.Object, ExternalData.Object,
                Spe.Object, containers, Ownership.Object, Jobs.Object, monitor);
        }

        private sealed class TestableService(
            Harness harness,
            ITrackingNumberAllocator allocator,
            IGenericEntityService entities,
            IDocumentDataverseService documents,
            ICommunicationDataverseService recordTypes,
            ExternalDataService externalData,
            SpeFileStore spe,
            RecordContainerResolver containers,
            IRecordOwnershipResolver ownership,
            JobSubmissionService jobs,
            IOptionsMonitor<EbillingOptions> ebilling)
            : InvoiceSubmissionService(allocator, entities, documents, recordTypes, externalData, spe, containers, ownership, jobs, ebilling,
                NullLogger<InvoiceSubmissionService>.Instance)
        {
            internal override Task<ContainerDecision> ResolveContainerAsync(InvoiceCoreRecord core, CancellationToken ct) =>
                Task.FromResult(harness.Container);
        }
    }

    internal static CallerPrincipal Contact() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = ContactId,
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    internal static InvoiceSubmissionRequest Spa(
        string? number = "INV-1", byte[]? bytes = null, string? contentType = "application/pdf", string? po = null) => new()
    {
        FirmId = FirmId,
        CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, MatterId),
        Channel = InvoiceSubmissionChannel.Spa,
        FirmInvoiceNumber = number,
        PoNumber = po,
        File = new InvoiceSubmissionFile("May invoice.pdf", contentType, bytes ?? Pdf()),
        ExternalCaller = Contact(),
        CorrelationId = "corr-1",
    };

    private static InvoiceSubmissionRequest Internal() => Spa() with
    {
        Channel = InvoiceSubmissionChannel.Internal,
        ExternalCaller = null,
        CallerObjectId = CallerOid,
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Received
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Spa_submission_is_received_with_document_row_and_one_job()
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Spa(number: "  INV-1  ", po: "PO-9"), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        var receipt = outcome.Receipt!;
        receipt.TrackingNumber.Should().Be("INV-1.1");
        receipt.DocumentId.Should().Be(DocumentId);

        // The bytes: once, into the core record's container, under a flat name unique to this submission, never overwriting.
        h.Uploads.Should().ContainSingle();
        h.Uploads[0].Drive.Should().Be(Drive);
        h.Uploads[0].Conflict.Should().Be(ConflictBehavior.Fail);
        h.Uploads[0].Name.Should().Be($"May invoice-{receipt.SubmissionId:N}.pdf");

        // The document: the external create bound to the matter with the resolved team, then typed Invoice.
        h.ExternalCreates.Should().ContainSingle().Which.Should().Match<(ExternalDataService.DocumentParentKind Kind, Guid Parent, ExternalUploadedFilePointers Pointers, Guid Team)>(
            c => c.Kind == ExternalDataService.DocumentParentKind.Matter && c.Parent == MatterId && c.Team == TeamId && c.Pointers.ItemId == "item-1");
        h.DocumentUpdates.Should().ContainSingle().Which.Request.Should().Match<UpdateDocumentRequest>(
            u => u.DocumentType == InvoiceSubmissionService.InvoiceDocumentType && u.MimeType == "application/pdf");
        h.DocumentCreates.Should().BeEmpty();

        // The submission: created through the allocator with the minted id, the firm and the trimmed number (D-87, D-91).
        var allocation = h.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(receipt.SubmissionId);
        allocation.FirmId.Should().Be(FirmId);
        allocation.Number.Should().Be("INV-1");
        var fields = allocation.Fields!;
        ((OptionSetValue)fields["sprk_processingstatus"]).Value.Should().Be((int)InvoiceSubmissionStatus.Received);
        ((OptionSetValue)fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Spa);
        ((EntityReference)fields["sprk_document"]).Id.Should().Be(DocumentId);
        ((EntityReference)fields["sprk_regardingmatter"]).Id.Should().Be(MatterId);
        ((EntityReference)fields["sprk_submitter"]).Should().Match<EntityReference>(r => r.LogicalName == "contact" && r.Id == ContactId);
        ((EntityReference)fields["ownerid"]).Id.Should().Be(TeamId);
        fields["sprk_ponumber"].Should().Be("PO-9");
        fields["sprk_name"].Should().Be("INV-1");
        fields.Keys.Should().Contain(InvoiceFamilyResolverFieldNames());
        fields.Keys.Should().NotContain(new[] { "sprk_vendororg", "sprk_trackingnumber", "sprk_firminvoicenumber", "sprk_submissionsequence" },
            "the allocator owns those columns");

        // The job: ADR-004 contract 049 handles.
        var job = h.JobsSent.Should().ContainSingle().Subject;
        job.JobId.Should().Be(receipt.JobId);
        job.JobType.Should().Be("InvoiceSubmissionProcessing");
        job.SubjectId.Should().Be(receipt.SubmissionId.ToString());
        job.IdempotencyKey.Should().Be(receipt.SubmissionId.ToString());
        job.Payload!.RootElement.GetProperty("submissionId").GetGuid().Should().Be(receipt.SubmissionId);
        h.Deleted.Should().BeEmpty();
        h.FileDeletes.Should().BeEmpty();
    }

    private static string[] InvoiceFamilyResolverFieldNames() =>
        new[] { "sprk_regardingrecordtype", "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl" };

    [Fact]
    public async Task Internal_submission_creates_the_document_through_the_shared_seam_and_names_the_caller()
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Internal(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.ExternalCreates.Should().BeEmpty();
        h.DocumentCreates.Should().ContainSingle().Which.OwningTeamId.Should().Be(TeamId);
        h.DocumentUpdates.Should().ContainSingle().Which.Request.Should().Match<UpdateDocumentRequest>(u =>
            u.MatterLookup == MatterId && u.ProjectLookup == null && u.DocumentType == InvoiceSubmissionService.InvoiceDocumentType
            && u.GraphItemId == "item-1" && u.HasFile == true);
        h.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.ObjectId.Should().Be(CallerOid);
        var fields = h.Allocations.Should().ContainSingle().Subject.Fields!;
        ((OptionSetValue)fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);
        fields.Keys.Should().NotContain("sprk_submitter");
        h.JobsSent.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Refusals before any write
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_firm_invoice_number_is_400_before_any_read_or_write(string? number)
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Spa(number: number), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o =>
            o.StatusCode == 400 && o.ErrorCode == InvoiceSubmissionErrors.FirmInvoiceNumberRequired);
        h.Writes.Should().Be(0);
        h.Entities.Invocations.Should().BeEmpty("D-91: a request without a number costs no read");
    }

    [Fact]
    public async Task Firm_invoice_number_longer_than_the_tracking_column_allows_is_400()
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Spa(number: new string('9', TrackingNumberAllocator.MaxFirmInvoiceNumberLength + 1)), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmInvoiceNumberTooLong);
        h.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("application/pdf", false)]
    [InlineData(null, true)]
    public async Task Not_a_pdf_is_400_not_pdf(string? contentType, bool pdfBytes)
    {
        var h = new Harness();
        var bytes = pdfBytes ? Pdf() : "MZ not a pdf"u8.ToArray();

        var outcome = await h.Build().SubmitAsync(Spa(bytes: bytes, contentType: contentType), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 400 && o.ErrorCode == InvoiceSubmissionErrors.NotPdf);
        h.Writes.Should().Be(0);
    }

    [Fact]
    public void A_pdf_content_type_with_parameters_is_still_a_pdf() =>
        InvoiceSubmissionService.IsPdf(new InvoiceSubmissionFile("a.pdf", "Application/PDF; name=a.pdf", Pdf())).Should().BeTrue();

    [Fact]
    public async Task An_empty_file_is_400_file_required()
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Spa(bytes: Array.Empty<byte>()), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FileRequired);
        h.Writes.Should().Be(0);
    }

    [Fact]
    public async Task A_file_above_the_configured_limit_is_413_too_large()
    {
        var h = new Harness(); // MaxSubmissionFileSizeMb = 1

        var outcome = await h.Build().SubmitAsync(Spa(bytes: Pdf(1024 * 1024 + 1)), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 413 && o.ErrorCode == InvoiceSubmissionErrors.TooLarge);
        h.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_missing_or_inactive_firm_is_400_firm_invalid(bool missing)
    {
        var h = new Harness();
        h.Firm = missing ? null : new Entity("sprk_organization", FirmId) { ["statecode"] = new OptionSetValue(1) };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 400 && o.ErrorCode == InvoiceSubmissionErrors.FirmInvalid);
        h.Writes.Should().Be(0);
    }

    [Fact]
    public async Task A_core_record_that_does_not_exist_is_422_and_one_that_cannot_be_read_is_503()
    {
        var missing = new Harness { Matter = null };
        (await missing.Build().SubmitAsync(Spa(), CancellationToken.None)).Should().Match<InvoiceSubmissionOutcome>(o =>
            o.StatusCode == 422 && o.ErrorCode == InvoiceSubmissionErrors.CoreRecordUnavailable);
        missing.Writes.Should().Be(0);

        var faulted = new Harness { MatterReadFailure = new TimeoutException("throttled") };
        (await faulted.Build().SubmitAsync(Spa(), CancellationToken.None)).Should().Match<InvoiceSubmissionOutcome>(o =>
            o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.CoreRecordUnavailable);
        faulted.Writes.Should().Be(0);
    }

    [Fact]
    public async Task An_unresolved_owner_refuses_before_the_upload()
    {
        var h = new Harness { OwnerAnswer = RecordOwnerResolution.Refused("no_team", "the matter has no owning team") };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.OwnerRefused);
        h.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(ContainerDecisionOutcome.FailClosed)]
    [InlineData(ContainerDecisionOutcome.Unresolved)]
    public async Task No_usable_container_is_422_storage_not_configured(ContainerDecisionOutcome decision)
    {
        var h = new Harness { Container = new ContainerDecision(decision, null) };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 422 && o.ErrorCode == InvoiceSubmissionErrors.StorageNotConfigured);
        h.Writes.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Compensation: a failure after the upload leaves no Received submission, document or file
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task No_tracking_number_removes_the_document_and_the_file_and_enqueues_nothing()
    {
        var h = new Harness { Allocation = TrackingNumberResult.Refused(TrackingNumberReasons.Unavailable) };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == TrackingNumberReasons.Unavailable);
        h.Deleted.Should().Equal(("sprk_document", DocumentId));
        h.FileDeletes.Should().Equal((Drive, "item-1"));
        h.JobsSent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_enqueue_removes_the_submission_the_document_and_the_file_in_reverse_order()
    {
        var h = new Harness { JobFailure = new ServiceBusException("queue down", ServiceBusFailureReason.ServiceCommunicationProblem) };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 500 && o.ErrorCode == InvoiceSubmissionErrors.Failed);
        var submissionId = h.Allocations.Single().SubmissionId;
        h.Deleted.Should().Equal(("sprk_invoicesubmission", submissionId), ("sprk_document", DocumentId));
        h.FileDeletes.Should().Equal((Drive, "item-1"));
    }

    [Fact]
    public async Task A_failed_document_create_removes_the_file_and_never_creates_a_submission()
    {
        var h = new Harness { DocumentCreateFailure = new InvalidOperationException("dataverse down") };

        var outcome = await h.Build().SubmitAsync(Internal(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        h.Allocations.Should().BeEmpty();
        h.FileDeletes.Should().Equal((Drive, "item-1"));
        h.JobsSent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa)]
    [InlineData(InvoiceSubmissionChannel.Internal)]
    public async Task A_failed_document_update_removes_the_document_row_it_created_and_the_file(InvoiceSubmissionChannel channel)
    {
        var h = new Harness { DocumentUpdateFailure = new TimeoutException("throttled") };

        var outcome = await h.Build().SubmitAsync(channel == InvoiceSubmissionChannel.Spa ? Spa() : Internal(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        h.Deleted.Should().Equal(("sprk_document", DocumentId));
        h.FileDeletes.Should().Equal((Drive, "item-1"));
        h.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_submission_create_whose_reply_is_lost_is_deleted_by_its_id()
    {
        // The allocator's create may have committed before the timeout: the row would be Received with no job.
        var h = new Harness { AllocatorFailure = new TimeoutException("the create timed out after Dataverse committed it") };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        var submissionId = h.Allocations.Single().SubmissionId;
        h.Deleted.Should().Equal(("sprk_invoicesubmission", submissionId), ("sprk_document", DocumentId));
        h.JobsSent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_request_is_compensated_and_rethrown()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        h.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => h.Build().SubmitAsync(Spa(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Deleted.Select(d => d.Entity).Should().Equal("sprk_invoicesubmission", "sprk_document");
        h.FileDeletes.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Reads
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void A_stored_row_maps_to_the_status_view()
    {
        var id = Guid.NewGuid();
        var row = new Entity("sprk_invoicesubmission", id)
        {
            ["sprk_trackingnumber"] = "INV-1.2",
            ["sprk_firminvoicenumber"] = "INV-1",
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
            ["sprk_processingstatus"] = new OptionSetValue(100000002),
            ["sprk_channel"] = new OptionSetValue(100000000),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", MatterId),
        };

        var view = InvoiceSubmissionService.ToView(row);

        view.Should().Match<InvoiceSubmissionView>(v => v.SubmissionId == id && v.TrackingNumber == "INV-1.2" && v.Status == "Accepted"
            && v.Channel == "SPA" && v.FirmId == FirmId && v.CoreRecord == new InvoiceCoreRecord(InvoiceCoreRecordKind.Project, MatterId));
    }

    [Fact]
    public void A_row_regarding_two_core_records_regards_none()
    {
        var row = new Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", Guid.NewGuid()),
        };

        InvoiceSubmissionService.ToView(row).CoreRecord.Should().BeNull("no route can scope an FR-05 violation to a caller");
    }

    [Fact]
    public async Task The_list_reads_nothing_for_a_caller_with_no_firm_and_scopes_by_firm_and_record_otherwise()
    {
        var h = new Harness();
        var queries = new List<QueryExpression>();
        h.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback((QueryExpression q, CancellationToken _) => queries.Add(q))
            .ReturnsAsync(new EntityCollection());
        var service = h.Build();

        (await service.ListAsync(Array.Empty<Guid>(), new[] { MatterId }, Array.Empty<Guid>(), Array.Empty<Guid>(), CancellationToken.None))
            .Should().BeEmpty();
        queries.Should().BeEmpty();

        await service.ListAsync(new[] { FirmId }, new[] { MatterId }, Array.Empty<Guid>(), Array.Empty<Guid>(), CancellationToken.None);

        var query = queries.Should().ContainSingle("a root type the caller reads nothing of is not queried").Subject;
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_vendororg" && c.Values.Contains(FirmId));
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_regardingmatter" && c.Values.Contains(MatterId));
    }
}
