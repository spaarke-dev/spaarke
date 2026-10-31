using System.Text.Json;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// <see cref="InvoiceSubmissionService"/> (ebilling-r1 task 044, FR-17; task 092 moved the bytes to the Finance file store): payload
/// validation (D-91 first), the write order, the row and job contract task 049 relies on, and compensation (a failure after the stage
/// leaves no Received submission and no staged file behind).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): these are the intake guarantees every channel relies on. Dataverse, the allocator and the queue are
/// substituted at their narrow seams, and the file store is the real one over an in-memory blob gateway (<see cref="StoredFileRig"/>); the validation, the 013 guard and the orchestration are real. Each write double behaves like the
/// real service it stands in for: it commits first, then fails or honours the token, so a "failure" can leave a committed write behind
/// whose reply never arrived.
/// </remarks>
public sealed class InvoiceSubmissionServiceTests
{
    internal static readonly Guid FirmId = Guid.NewGuid();
    internal static readonly Guid MatterId = Guid.NewGuid();
    internal static readonly Guid ContactId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid RecordTypeId = Guid.NewGuid();
    private static readonly Guid CallerOid = Guid.NewGuid();

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
        public readonly Mock<ICommunicationDataverseService> RecordTypes = new();
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();
        public readonly Mock<ITrackingNumberAllocator> Allocator = new();
        public readonly Mock<JobSubmissionService> Jobs;
        public readonly EbillingOptions Options = new() { Enabled = true, MaxSubmissionFileSizeMb = 1 };

        /// <summary>
        /// ebilling-r1 task 092: the Finance file store the uploads stage into (the real <see cref="StoredFileService"/> over an in-memory
        /// blob gateway and Dataverse). Replace it before <see cref="Build"/> for an unconfigured store.
        /// </summary>
        public StoredFileRig Files { get; set; } = new();

        public readonly List<(string Entity, Guid Id)> Deleted = new();
        public readonly List<(Guid SubmissionId, Guid FirmId, string Number, IReadOnlyDictionary<string, object>? Fields)> Allocations = new();
        public readonly List<JobContract> JobsSent = new();
        public readonly List<RecordOwnershipContext> OwnerContexts = new();
        public readonly List<CancellationToken> WriteTokens = new();

        /// <summary>Every stage the intake asked the file store for (one write-once blob create each), as "container/name".</summary>
        public IReadOnlyList<string> Stages => BlobCalls("create");

        /// <summary>Every staged-blob delete (compensation), as "container/name".</summary>
        public IReadOnlyList<string> BlobDeletes => BlobCalls("delete");

        private List<string> BlobCalls(string op) =>
            Files.Gateway.Calls.Where(c => c.StartsWith(op + " ", StringComparison.Ordinal)).Select(c => c[(op.Length + 1)..]).ToList();

        public RecordOwnerResolution OwnerAnswer = RecordOwnerResolution.Owned(TeamId);
        public TrackingNumberResult Allocation = TrackingNumberResult.Created("INV-1.1", 1);
        public Entity? Firm = new("sprk_organization", FirmId) { ["statecode"] = new OptionSetValue(0) };
        public Entity? Matter = new("sprk_matter", MatterId) { ["sprk_mattername"] = "Acme v. Roe", ["sprk_matternumber"] = "M-1" };

        /// <summary>
        /// False: a project or work assignment is not found by the core-record read (a record deleted since the caller's access was composed).
        /// True (default): any project or work assignment id is found, with a name and a number.
        /// </summary>
        public bool OtherCoreRecordsExist = true;
        public Entity? Submission;

        /// <summary>
        /// Task 094 (EB-D-167): submissions by id. When any is listed, a read by id answers ONLY from here (a missing id is not found), and the
        /// allocator stores <see cref="StoredOnCreate"/> under the id it was asked to create; otherwise <see cref="Submission"/> answers every id.
        /// </summary>
        public readonly Dictionary<Guid, Entity> Submissions = new();

        /// <summary>050a review #5: non-null, the allocator's create stores this row as <see cref="Submission"/> (what a concurrent creator left).</summary>
        public Entity? StoredOnCreate;

        /// <summary>Task 097b: the <c>sprk_invoice</c> rows a Correction reads, by id (absent: not found).</summary>
        public readonly Dictionary<Guid, Entity> Invoices = new();

        /// <summary>
        /// Task 097b: the submissions the Correction guard's query (<c>sprk_invoicesubmission</c> by <c>sprk_invoice</c>) can find; each is
        /// returned when its <c>sprk_invoice</c> is the queried invoice.
        /// </summary>
        public readonly List<Entity> PriorSubmissions = new();

        /// <summary>Task 097b: non-null, the Correction guard's submission query throws this.</summary>
        public Exception? PriorSubmissionReadFailure;

        /// <summary>Task 097e: non-null, the query for the submissions that name an earlier one (<c>sprk_resubmissionof</c>) throws this.</summary>
        public Exception? ResubmissionReadFailure;

        /// <summary>050a: the <c>sprk_document</c> the existing-document entry reads (null: not found).</summary>
        public Entity? ExistingDocument;

        /// <summary>050a: non-null, the existing document's read throws this.</summary>
        public Exception? ExistingDocumentReadFailure;

        /// <summary>D-140: documents of the filing chain other than the confirmed one (its parents), by id.</summary>
        public readonly Dictionary<Guid, Entity> OtherDocuments = new();

        /// <summary>D-140: the communications a document of the chain links (absent: not found).</summary>
        public readonly Dictionary<Guid, Entity> Communications = new();

        /// <summary>D-140: non-null, a communication read throws this.</summary>
        public Exception? CommunicationReadFailure;

        /// <summary>050a round 4 (F1): the communication threads a communication of the chain links (absent: not found).</summary>
        public readonly Dictionary<Guid, Entity> Threads = new();

        /// <summary>
        /// A thread read answers only the columns it asked for (as Dataverse does), so a read that omits one (the thread's type, say)
        /// sees it unset.
        /// </summary>
        private static Entity OnlyColumns(Entity row, string[] columns)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var column in columns.Where(row.Contains))
            {
                copy[column] = row[column];
            }

            return copy;
        }

        /// <summary>050a round 4 (F1): non-null, a thread read throws this.</summary>
        public Exception? ThreadReadFailure;

        /// <summary>
        /// 050a review round 3 (uac-r2 F1-a): the isolation facts the existing-document check reads, through the REAL
        /// <see cref="Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer"/> walk. By default a world with a Secure Record owner team
        /// and no rows: every document reads as not isolated.
        /// </summary>
        public SecureChildShareWorld Isolation = SecureChildShareWorld.Standard();

        private static Exception NotFound() =>
            new System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault>(
                new Microsoft.Xrm.Sdk.OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist");

        /// <summary>050a: every read of a submission by id (the existing-document entry's dedupe read).</summary>
        public readonly List<Guid> SubmissionReads = new();

        /// <summary>050a: non-null, a read of a submission by id throws this.</summary>
        public Exception? SubmissionReadFailure;
        public Exception? MatterReadFailure;
        public Exception? JobFailure;
        public Exception? AllocatorFailure;
        public Exception? FirmReadFailure;

        /// <summary>
        /// The assigned law firms (<c>sprk_assignedlawfirm1/2</c>) of each core record, by (table, id) - D-106/D-110. By default the test matter is
        /// linked to <see cref="FirmId"/>; a record not listed references no firm. Behind <see cref="Participations"/> unless a test replaces it.
        /// </summary>
        public readonly Dictionary<(string Entity, Guid Id), Guid[]> Links = new() { [("sprk_matter", MatterId)] = new[] { FirmId } };

        /// <summary>True: every record comes back unresolved (the record-to-organization read could not be completed).</summary>
        public bool LinksUnreadable;

        /// <summary>Non-null: the record-to-organization read throws this.</summary>
        public Exception? LinkReadFailure;

        /// <summary>Every record-to-organization read the service made.</summary>
        public readonly List<(string Entity, IReadOnlyCollection<Guid> Ids)> LinkReads = new();

        /// <summary>
        /// The participation service the submission service reads the firm links through. The default double answers from <see cref="Links"/>;
        /// a test (or the contract suite) sets this to its own before <see cref="Build"/>.
        /// </summary>
        public ExternalParticipationService Participations { get; set; } = null!;

        public int Writes => Stages.Count + Allocations.Count + JobsSent.Count;

        public Harness()
        {
            var participations = new Mock<ExternalParticipationService>(
                new HttpClient(), Mock.Of<ITenantCache>(), new ConfigurationBuilder().Build(), Mock.Of<TokenCredential>(),
                Mock.Of<IHttpContextAccessor>(), NullLogger<ExternalParticipationService>.Instance, null!);
            participations.Setup(p => p.GetReferencedOrganizationIdsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                {
                    LinkReads.Add((entity, ids));
                    if (LinkReadFailure is { } failure)
                    {
                        throw failure;
                    }

                    IReadOnlyDictionary<Guid, ReferencedOrganizations> answer = ids.Distinct().ToDictionary(
                        id => id,
                        id => LinksUnreadable
                            ? ReferencedOrganizations.Unresolved
                            : new ReferencedOrganizations(Links.TryGetValue((entity, id), out var firms) ? firms : Array.Empty<Guid>(), Unreadable: false));
                    return Task.FromResult(answer);
                });
            Participations = participations.Object;

            RecordTypes.Setup(r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", RecordTypeId) { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber" });

            Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] columns, CancellationToken _) => entity switch
                {
                    "sprk_organization" => FirmReadFailure is { } firmFailure ? throw firmFailure : Firm!,
                    "sprk_matter" => MatterReadFailure is { } failure ? throw failure : Matter!,
                    "sprk_project" or "sprk_workassignment" => OtherCoreRecordsExist
                        ? new Entity(entity, id) { [RegardingRecordType.GetPrimaryNameField(entity)!] = "Core record", ["sprk_matternumber"] = "C-1" }
                        : null!,
                    "sprk_invoicesubmission" => ReadSubmission(id),
                    "sprk_document" when OtherDocuments.TryGetValue(id, out var otherDocument) => otherDocument,
                    "sprk_document" => ExistingDocumentReadFailure is { } documentFailure ? throw documentFailure : ExistingDocument!,
                    "sprk_communication" => CommunicationReadFailure is { } communicationFailure
                        ? throw communicationFailure
                        : Communications.TryGetValue(id, out var communication) ? communication : throw NotFound(),
                    "sprk_communicationthread" => ThreadReadFailure is { } threadFailure
                        ? throw threadFailure
                        : Threads.TryGetValue(id, out var thread) ? OnlyColumns(thread, columns) : throw NotFound(),
                    "sprk_invoice" when Invoices.Count > 0 => Invoices.TryGetValue(id, out var invoice) ? OnlyColumns(invoice, columns) : throw NotFound(),
                    _ => null!,
                });
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                {
                    // Task 097b: the Correction guard's read of an invoice's submission; every other query finds nothing, as before.
                    if (q.EntityName == "sprk_invoicesubmission"
                        && q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_invoice") is { } byInvoice)
                    {
                        if (PriorSubmissionReadFailure is { } failure)
                        {
                            throw failure;
                        }

                        // Second review F1: only the columns asked for, as Dataverse does, so a column dropped from the guard's read
                        // (the submission's status) fails a test instead of being answered anyway.
                        return new EntityCollection(PriorSubmissions
                            .Where(s => s.GetAttributeValue<EntityReference>("sprk_invoice")?.Id is { } invoiceId
                                && byInvoice.Values.Contains(invoiceId))
                            .Select(s =>
                            {
                                var row = new Entity(s.LogicalName, s.Id);
                                foreach (var column in q.ColumnSet.Columns.Where(s.Attributes.ContainsKey))
                                {
                                    row[column] = s[column];
                                }

                                return row;
                            })
                            .ToList());
                    }

                    // Task 097e: the correction prefill's read of the submissions that name an earlier one (sprk_resubmissionof), among the same rows.
                    if (q.EntityName == "sprk_invoicesubmission"
                        && q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_resubmissionof") is { } byEarlier)
                    {
                        if (ResubmissionReadFailure is { } failure)
                        {
                            throw failure;
                        }

                        return new EntityCollection(PriorSubmissions
                            .Where(s => s.GetAttributeValue<EntityReference>("sprk_resubmissionof")?.Id is { } earlierId
                                && byEarlier.Values.Contains(earlierId))
                            .Select(s => OnlyColumns(s, q.ColumnSet.Columns.ToArray()))
                            .ToList());
                    }

                    return new EntityCollection();
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
                .ReturnsAsync((Guid id, Guid firm, string number, IReadOnlyDictionary<string, object>? fields, CancellationToken ct) =>
                {
                    Allocations.Add((id, firm, number, fields));
                    WriteTokens.Add(ct);
                    if (AllocatorFailure is { } failure)
                    {
                        throw failure;
                    }

                    ct.ThrowIfCancellationRequested();
                    if (StoredOnCreate is { } stored)
                    {
                        if (Submissions.Count > 0)
                        {
                            Submissions[id] = stored;
                        }
                        else
                        {
                            Submission = stored;
                        }
                    }

                    return Allocation;
                });

            var sbOptions = Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
            {
                QueueName = "test-jobs",
                CommunicationQueueName = "test-comms",
                ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
            });
            Jobs = new Mock<JobSubmissionService>(sbOptions, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
            Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken ct) =>
                {
                    WriteTokens.Add(ct);
                    if (JobFailure is not null)
                    {
                        throw JobFailure;
                    }

                    JobsSent.Add(job);
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });
        }

        private Entity ReadSubmission(Guid id)
        {
            SubmissionReads.Add(id);
            if (SubmissionReadFailure is { } failure)
            {
                throw failure;
            }

            if (Submissions.Count > 0)
            {
                return Submissions.TryGetValue(id, out var byId) ? byId : null!;
            }

            return Submission!;
        }

        public IOptionsMonitor<EbillingOptions> Monitor => Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == Options);

        public InvoiceSubmissionService Build() =>
            new(Allocator.Object, Entities.Object, RecordTypes.Object, Participations, Files.Service, Ownership.Object,
                IsolationOver(() => Isolation), Jobs.Object, Monitor, NullLogger<InvoiceSubmissionService>.Instance);

        /// <summary>The container and name the upload of <paramref name="submissionId"/> is staged under (the job derives the same).</summary>
        public (string Container, string Name) StagedName(Guid submissionId)
        {
            var spec = InvoiceSubmissionService.UploadFileSpec(submissionId, null, InvoiceSubmissionChannel.Spa);
            return (StoredFileSchema.ContainerOf(spec.Purpose), StoredFileBlobName.Build(Files.TenantId, spec.Parent, spec.FileId));
        }
    }

    /// <summary>
    /// 050a review round 3: the REAL <see cref="ExistingDocumentIsolationCheck"/> over the REAL synchronizer, both reading the world
    /// <paramref name="world"/> answers at query time (its document row, its lineage and its Secure Record owner team).
    /// </summary>
    internal static ExistingDocumentIsolationCheck IsolationOver(Func<SecureChildShareWorld> world) =>
        new(SecureChildShareWorld.EntitiesOver(world).Object,
            SecureChildShareWorld.SynchronizerOver(world, new FakeRecordShareTable()),
            NullLogger<ExistingDocumentIsolationCheck>.Instance);

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
    public async Task Spa_submission_is_received_with_a_staged_file_no_document_and_one_job()
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Spa(number: "  INV-1  ", po: "PO-9"), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        var receipt = outcome.Receipt!;
        receipt.TrackingNumber.Should().Be("INV-1.1");
        receipt.DocumentId.Should().BeNull("an upload creates no sprk_document (ebilling-r1 task 092)");

        // The bytes: staged once in the Finance file store, under the submission's upload id with the invoice the job creates as parent,
        // carrying the submission id. No Stored File row yet: the job writes it in its commit transaction.
        var (container, name) = h.StagedName(receipt.SubmissionId);
        name.Should().Contain(StoredFileIds.ForUpload(receipt.SubmissionId).ToString("D"))
            .And.Contain(InvoiceSubmissionPersister.InvoiceIdFor(receipt.SubmissionId).ToString("D"));
        h.Stages.Should().Equal($"{container}/{name}");
        var blob = h.Files.Gateway.Get(container, name)!;
        blob.Content.Should().Equal(Pdf());
        blob.ContentType.Should().Be("application/pdf");
        blob.Metadata[StoredFileBlobs.MetadataSubmission].Should().Be(receipt.SubmissionId.ToString("D"));
        h.Files.Dataverse.Writes.Should().Be(0, "the Stored File row is written only inside the job's commit transaction (ADR-002)");
        h.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never, "no document row is created");

        // The submission: created through the allocator with the minted id, the firm and the trimmed number (D-87, D-91); no document.
        var allocation = h.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(receipt.SubmissionId);
        allocation.FirmId.Should().Be(FirmId);
        allocation.Number.Should().Be("INV-1");
        var fields = allocation.Fields!;
        fields.Keys.Should().NotContain("sprk_document", "the upload path's submission names no document");
        ((OptionSetValue)fields["sprk_processingstatus"]).Value.Should().Be((int)InvoiceSubmissionStatus.Received);
        ((OptionSetValue)fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Spa);
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
        h.BlobDeletes.Should().BeEmpty();
    }

    private static string[] InvoiceFamilyResolverFieldNames() =>
        new[] { "sprk_regardingrecordtype", "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl" };

    [Fact]
    public async Task Internal_submission_stages_the_same_way_and_names_the_caller()
    {
        var h = new Harness();

        var outcome = await h.Build().SubmitAsync(Internal(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt!.DocumentId.Should().BeNull();
        var (container, name) = h.StagedName(outcome.Receipt.SubmissionId);
        h.Stages.Should().Equal($"{container}/{name}");
        h.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.ObjectId.Should().Be(CallerOid);
        // 194b R3b-2: the submission row's own question (ForChild): its table, its core record under its sprk_regarding{core} lookup.
        h.OwnerContexts[0].ChildEntityLogicalName.Should().Be("sprk_invoicesubmission");
        h.OwnerContexts[0].Parents.Should().ContainSingle().Which.EntityLogicalName.Should().BeOneOf("sprk_matter", "sprk_project", "sprk_workassignment");
        var fields = h.Allocations.Should().ContainSingle().Subject.Fields!;
        ((OptionSetValue)fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);
        fields.Keys.Should().NotContain(new[] { "sprk_submitter", "sprk_document" });
        h.JobsSent.Should().ContainSingle();
    }

    [Fact]
    public void The_intake_has_no_way_to_reach_SharePoint_Embedded_or_create_a_document()
    {
        // Task 092: the upload channels stage through the file store only. The type takes none of the SPE / document seams it used to.
        var parameters = typeof(InvoiceSubmissionService).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();

        parameters.Should().Contain(typeof(StoredFileService));
        parameters.Should().NotContain(new[]
        {
            typeof(SpeFileStore), typeof(IDocumentDataverseService), typeof(ExternalDataService), typeof(RecordContainerResolver),
        });
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa, true)]
    [InlineData(InvoiceSubmissionChannel.Internal, false)]
    public void The_upload_spec_is_the_invoice_pdf_of_the_invoice_to_be_and_firm_visible_only_from_the_spa(InvoiceSubmissionChannel channel, bool firmVisible)
    {
        var submissionId = Guid.NewGuid();

        var spec = InvoiceSubmissionService.UploadFileSpec(submissionId, "May invoice.pdf", channel, "INV-1.1");

        spec.Invalid().Should().BeNull();
        spec.FileId.Should().Be(StoredFileIds.ForUpload(submissionId));
        spec.Purpose.Should().Be(StoredFilePurpose.InvoicePdf);
        spec.Parent.Should().Be(new StoredFileParent(StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(submissionId)));
        spec.FirmVisible.Should().Be(firmVisible);
        spec.SubmissionId.Should().Be(submissionId);
        spec.ContentType.Should().Be("application/pdf");
        spec.Name.Should().Be("INV-1.1.pdf", "the job, which writes the row, names it by the tracking number");
        spec.SourceDocumentId.Should().BeNull("an upload has no source document");
    }

    [Theory]
    [InlineData(null, "May invoice.pdf", "May invoice.pdf")]
    [InlineData(" INV-7.2 ", "May invoice.pdf", "INV-7.2.pdf")]
    [InlineData(null, null, "invoice.pdf")]
    [InlineData("", ".pdf", "invoice.pdf")]
    public void The_upload_name_is_the_tracking_number_else_the_client_stem_else_invoice(string? trackingNumber, string? clientFileName, string expected) =>
        InvoiceSubmissionService.UploadName(clientFileName, trackingNumber).Should().Be(expected);

    [Theory]
    [InlineData("INV/7.2", "INV7.2.pdf")]
    [InlineData("..\\INV-7.2", "..INV-7.2.pdf")]
    [InlineData("INV\r\n-7.2\t", "INV-7.2.pdf")]
    [InlineData("/\\\u0001", "May invoice.pdf")]
    public void The_upload_name_strips_path_and_control_characters_from_the_tracking_number(string trackingNumber, string expected) =>
        InvoiceSubmissionService.UploadName("May invoice.pdf", trackingNumber).Should().Be(expected);

    [Theory]
    [InlineData("INV/7.2", "INV7.2.pdf")]
    [InlineData("a\\b\u0000c", "abc.pdf")]
    [InlineData("\u001f/", "invoice.pdf")]
    [InlineData(" INV-7.2 ", "INV-7.2.pdf")]
    public void The_source_copy_name_strips_path_and_control_characters_from_the_tracking_number(string trackingNumber, string expected) =>
        Sprk.Bff.Api.Services.Jobs.Handlers.InvoiceSubmissionProcessingJobHandler.SourceCopyName(trackingNumber).Should().Be(expected);

    [Fact]
    public void A_long_upload_name_is_cut_to_100_characters_before_the_extension() =>
        InvoiceSubmissionService.UploadName(new string('a', 150) + ".pdf", null).Should().Be(new string('a', 100) + ".pdf");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task External_entry_refuses_a_systemuser_403_use_internal_route_before_any_read_or_write(bool linkedToAContact)
    {
        var h = new Harness();
        var systemUser = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            SystemUserId = Guid.NewGuid(),
            ContactId = linkedToAContact ? ContactId : Guid.Empty,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        var outcome = await h.Build().SubmitAsync(Spa() with { ExternalCaller = systemUser }, CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o =>
            o.StatusCode == StatusCodes.Status403Forbidden && o.ErrorCode == InvoiceSubmissionErrors.UseInternalRoute);
        h.Writes.Should().Be(0);
        h.Files.Gateway.StorageCalls.Should().Be(0);
        h.Entities.Invocations.Should().BeEmpty("a systemuser costs no read on the external entry point");
        h.LinkReads.Should().BeEmpty();
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
    public async Task A_firm_that_cannot_be_read_is_503_firm_unverifiable_not_a_500()
    {
        var h = new Harness { FirmReadFailure = new TimeoutException("throttled") };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        h.Writes.Should().Be(0);
    }

    [Fact]
    public async Task A_core_record_that_does_not_exist_is_422_and_one_that_cannot_be_read_is_503()
    {
        var missing = new Harness { Matter = null };
        (await missing.Build().SubmitAsync(Spa(), CancellationToken.None)).Should().Match<InvoiceSubmissionOutcome>(o =>
            o.StatusCode == 422 && o.ErrorCode == InvoiceSubmissionErrors.CoreRecordUnavailable);
        missing.Writes.Should().Be(0);
        missing.LinkReads.Should().BeEmpty("the record's own fields are read first; a missing record never reaches the link read");

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

    [Fact]
    public async Task An_unconfigured_file_store_is_422_storage_not_configured_and_nothing_is_written()
    {
        // The status and code the route answered when no SPE container resolved (044), so the external route contract is unchanged.
        var h = new Harness { Files = new StoredFileRig(configured: false) };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 422 && o.ErrorCode == InvoiceSubmissionErrors.StorageNotConfigured);
        h.Writes.Should().Be(0);
        h.Files.Gateway.StorageCalls.Should().Be(0);
        h.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stage_the_store_refuses_is_500_and_leaves_the_blob_already_there_untouched()
    {
        // Another blob already holds the name with other bytes (ContentConflict): the stage writes nothing and nothing of it is ours to delete.
        var h = new Harness();
        h.Files.Gateway.BeforeOp = (op, path) =>
        {
            if (op == "create" && h.Files.Gateway.Get(path[..path.IndexOf('/')], path[(path.IndexOf('/') + 1)..]) is null)
            {
                h.Files.Gateway.Seed(path[..path.IndexOf('/')], path[(path.IndexOf('/') + 1)..], "%PDF-other"u8.ToArray(),
                    new Dictionary<string, string> { [StoredFileBlobs.MetadataSha256] = new string('0', 64) });
            }
        };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 500 && o.ErrorCode == InvoiceSubmissionErrors.Failed);
        h.Allocations.Should().BeEmpty();
        h.JobsSent.Should().BeEmpty();
        h.BlobDeletes.Should().BeEmpty("a refused stage wrote nothing, so there is nothing of this request's to remove");
        h.Files.Gateway.Names.Should().ContainSingle("the other blob is left as it was");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The firm is linked to the record (D-106, refined by D-110): THIS service refuses, whichever channel called
    // ═══════════════════════════════════════════════════════════════════════════

    internal static readonly Guid OtherFirmId = Guid.NewGuid();

    internal static InvoiceSubmissionRequest On(InvoiceSubmissionChannel channel) =>
        channel == InvoiceSubmissionChannel.Spa ? Spa() : Internal();

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa)]
    [InlineData(InvoiceSubmissionChannel.Internal)]
    public async Task A_firm_not_linked_to_the_record_is_403_firm_not_linked_on_every_channel_and_nothing_is_written(InvoiceSubmissionChannel channel)
    {
        var h = new Harness();
        h.Links[("sprk_matter", MatterId)] = new[] { OtherFirmId }; // the matter belongs to another firm

        var outcome = await h.Build().SubmitAsync(On(channel), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 403 && o.ErrorCode == InvoiceSubmissionErrors.FirmNotLinked);
        outcome.Detail.Should().NotContain(OtherFirmId.ToString()).And.NotContain(FirmId.ToString(), "the refusal names no firm");
        h.Writes.Should().Be(0);
        h.OwnerContexts.Should().BeEmpty("the refusal comes before the owner is resolved");
        h.LinkReads.Should().ContainSingle().Which.Entity.Should().Be("sprk_matter");
        h.LinkReads[0].Ids.Should().Equal(MatterId);
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa)]
    [InlineData(InvoiceSubmissionChannel.Internal)]
    public async Task A_record_with_no_assigned_firm_links_no_firm(InvoiceSubmissionChannel channel)
    {
        var h = new Harness();
        h.Links.Clear();

        var outcome = await h.Build().SubmitAsync(On(channel), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 403 && o.ErrorCode == InvoiceSubmissionErrors.FirmNotLinked);
        h.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa, true)]
    [InlineData(InvoiceSubmissionChannel.Spa, false)]
    [InlineData(InvoiceSubmissionChannel.Internal, true)]
    [InlineData(InvoiceSubmissionChannel.Internal, false)]
    public async Task A_firm_linked_to_the_record_in_either_assigned_firm_slot_is_accepted_on_both_channels(InvoiceSubmissionChannel channel, bool firstSlot)
    {
        // The control for the refusals above: only the link differs.
        var h = new Harness();
        h.Links[("sprk_matter", MatterId)] = firstSlot ? new[] { FirmId, OtherFirmId } : new[] { OtherFirmId, FirmId };

        var outcome = await h.Build().SubmitAsync(On(channel), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Allocations.Should().ContainSingle().Which.FirmId.Should().Be(FirmId);
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa, "unreadable")]
    [InlineData(InvoiceSubmissionChannel.Internal, "unreadable")]
    [InlineData(InvoiceSubmissionChannel.Spa, "threw")]
    [InlineData(InvoiceSubmissionChannel.Internal, "threw")]
    public async Task A_link_that_cannot_be_read_is_503_firm_unverifiable_not_a_pass_and_not_a_deny_and_nothing_is_written(
        InvoiceSubmissionChannel channel, string failure)
    {
        var h = new Harness();
        if (failure == "unreadable")
        {
            h.LinksUnreadable = true;
        }
        else
        {
            h.LinkReadFailure = new TimeoutException("throttled");
        }

        var outcome = await h.Build().SubmitAsync(On(channel), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        h.Writes.Should().Be(0);
        h.OwnerContexts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InvoiceCoreRecordKind.Project, "sprk_project")]
    [InlineData(InvoiceCoreRecordKind.WorkAssignment, "sprk_workassignment")]
    public async Task The_link_is_read_from_the_table_of_the_record_kind_so_a_project_or_work_assignment_is_held_to_it_too(
        InvoiceCoreRecordKind kind, string entity)
    {
        var recordId = Guid.NewGuid();
        var request = Internal() with { CoreRecord = new InvoiceCoreRecord(kind, recordId) };

        var unlinked = new Harness();
        unlinked.Links[(entity, recordId)] = new[] { OtherFirmId };
        (await unlinked.Build().SubmitAsync(request, CancellationToken.None)).Should().Match<InvoiceSubmissionOutcome>(o =>
            o.StatusCode == 403 && o.ErrorCode == InvoiceSubmissionErrors.FirmNotLinked);
        unlinked.LinkReads.Should().ContainSingle().Which.Entity.Should().Be(entity);
        unlinked.Writes.Should().Be(0);

        // Linked: the request goes on past the link check and is received.
        var linked = new Harness();
        linked.Links[(entity, recordId)] = new[] { FirmId };
        (await linked.Build().SubmitAsync(request, CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact]
    public async Task The_link_is_not_read_for_a_request_an_earlier_check_refuses()
    {
        var noNumber = new Harness();
        await noNumber.Build().SubmitAsync(Spa(number: ""), CancellationToken.None);
        noNumber.LinkReads.Should().BeEmpty("the payload is validated before anything is read");

        var inactiveFirm = new Harness { Firm = new Entity("sprk_organization", FirmId) { ["statecode"] = new OptionSetValue(1) } };
        (await inactiveFirm.Build().SubmitAsync(Internal(), CancellationToken.None)).ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmInvalid);
        inactiveFirm.LinkReads.Should().BeEmpty("an inactive firm is refused as invalid before its links are looked up");

        var missingRecord = new Harness { Matter = null };
        (await missingRecord.Build().SubmitAsync(Internal(), CancellationToken.None)).ErrorCode.Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        missingRecord.LinkReads.Should().BeEmpty("a record that cannot be found is refused before its links are looked up");
    }

    // The link reader cannot tell a record that is not returned (deleted) from a fault: both come back unreadable. The link is therefore asked
    // for only AFTER the record's own fields were read, so a missing record keeps its 422 and 503 firm_unverifiable means a real fault.
    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa, InvoiceCoreRecordKind.Matter)]
    [InlineData(InvoiceSubmissionChannel.Internal, InvoiceCoreRecordKind.Matter)]
    [InlineData(InvoiceSubmissionChannel.Spa, InvoiceCoreRecordKind.Project)]
    [InlineData(InvoiceSubmissionChannel.Internal, InvoiceCoreRecordKind.Project)]
    [InlineData(InvoiceSubmissionChannel.Spa, InvoiceCoreRecordKind.WorkAssignment)]
    [InlineData(InvoiceSubmissionChannel.Internal, InvoiceCoreRecordKind.WorkAssignment)]
    public async Task A_record_that_cannot_be_found_is_422_not_503_even_though_its_links_come_back_unreadable(
        InvoiceSubmissionChannel channel, InvoiceCoreRecordKind kind)
    {
        var h = new Harness { LinksUnreadable = true, OtherCoreRecordsExist = false };
        var request = On(channel) with { CoreRecord = new InvoiceCoreRecord(kind, kind == InvoiceCoreRecordKind.Matter ? MatterId : Guid.NewGuid()) };
        if (kind == InvoiceCoreRecordKind.Matter)
        {
            h.Matter = null;
        }

        var outcome = await h.Build().SubmitAsync(request, CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 422 && o.ErrorCode == InvoiceSubmissionErrors.CoreRecordUnavailable);
        h.Writes.Should().Be(0);
        h.OwnerContexts.Should().BeEmpty();
        h.LinkReads.Should().BeEmpty("the missing record is refused before its links are asked for");
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa)]
    [InlineData(InvoiceSubmissionChannel.Internal)]
    public async Task A_real_fault_reading_the_links_of_an_existing_record_is_still_503_after_the_record_was_read(InvoiceSubmissionChannel channel)
    {
        var h = new Harness { LinkReadFailure = new TimeoutException("throttled") };

        var outcome = await h.Build().SubmitAsync(On(channel), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == InvoiceSubmissionErrors.FirmUnverifiable);
        h.LinkReads.Should().ContainSingle("the record exists, so its links are asked for, and the fault is what refuses");
        h.Writes.Should().Be(0);
        h.OwnerContexts.Should().BeEmpty("the refusal comes before the owner is resolved");
    }

    [Fact]
    public async Task A_caller_who_abandons_the_request_during_the_link_read_is_a_cancellation_not_a_503()
    {
        using var cts = new CancellationTokenSource();
        var h = new Harness { LinkReadFailure = new OperationCanceledException(cts.Token) };
        cts.Cancel();

        var act = () => h.Build().SubmitAsync(Internal(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Writes.Should().Be(0);
    }

    public static TheoryData<string> UnreadableAnswers => new() { "missing", "flagged", "defaulted" };

    [Theory]
    [MemberData(nameof(UnreadableAnswers))]
    public void An_answer_that_is_missing_flagged_unreadable_or_defaulted_is_never_linked(string answer)
    {
        ReferencedOrganizations? read = answer switch
        {
            "missing" => null,
            "flagged" => new ReferencedOrganizations(new[] { FirmId }, Unreadable: true), // even though it names the firm
            _ => default(ReferencedOrganizations), // a defaulted struct: Unreadable false, no id collection
        };

        InvoiceSubmissionService.LinkOf(read, FirmId).Should().Be(InvoiceFirmLink.Unreadable);
    }

    [Fact]
    public void An_empty_firm_id_never_matches_even_when_an_empty_slot_was_returned_as_an_id()
    {
        // A null lookup never counts as a match: the empty firm id is not linked to a record, whatever the record's id list holds.
        var read = new ReferencedOrganizations(new[] { Guid.Empty, OtherFirmId }, Unreadable: false);

        InvoiceSubmissionService.LinkOf(read, Guid.Empty).Should().Be(InvoiceFirmLink.NotLinked);
        InvoiceSubmissionService.LinkOf(read, OtherFirmId).Should().Be(InvoiceFirmLink.Linked);
        InvoiceSubmissionService.LinkOf(new ReferencedOrganizations(Array.Empty<Guid>(), Unreadable: false), FirmId).Should().Be(InvoiceFirmLink.NotLinked);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Compensation: a failure after the stage leaves no Received submission and no staged file
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task No_tracking_number_removes_the_staged_file_and_enqueues_nothing()
    {
        var h = new Harness { Allocation = TrackingNumberResult.Refused(TrackingNumberReasons.Unavailable) };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 503 && o.ErrorCode == TrackingNumberReasons.Unavailable);
        var (container, name) = h.StagedName(h.Allocations.Single().SubmissionId);
        h.Deleted.Should().BeEmpty("a refused allocation created no row");
        h.BlobDeletes.Should().Equal($"{container}/{name}");
        h.Files.Gateway.Names.Should().BeEmpty();
        h.JobsSent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_enqueue_removes_the_submission_then_the_staged_file()
    {
        var h = new Harness { JobFailure = new ServiceBusException("queue down", ServiceBusFailureReason.ServiceCommunicationProblem) };
        var order = new List<string>();
        h.Entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback((string entity, Guid id, CancellationToken _) =>
            {
                order.Add(entity);
                h.Deleted.Add((entity, id));
            })
            .Returns(Task.CompletedTask);
        h.Files.Gateway.BeforeOp = (op, _) =>
        {
            if (op == "delete")
            {
                order.Add("blob");
            }
        };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 500 && o.ErrorCode == InvoiceSubmissionErrors.Failed);
        var submissionId = h.Allocations.Single().SubmissionId;
        h.Deleted.Should().Equal(("sprk_invoicesubmission", submissionId));
        order.Should().Equal("sprk_invoicesubmission", "blob");
        h.Files.Gateway.Names.Should().BeEmpty();
    }

    [Fact]
    public async Task A_submission_create_whose_reply_is_lost_is_deleted_by_its_id_and_its_file_removed()
    {
        // The allocator's create may have committed before the timeout: the row would be Received with no job.
        var h = new Harness { AllocatorFailure = new TimeoutException("the create timed out after Dataverse committed it") };

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        var submissionId = h.Allocations.Single().SubmissionId;
        h.Deleted.Should().Equal(("sprk_invoicesubmission", submissionId));
        h.Files.Gateway.Names.Should().BeEmpty();
        h.JobsSent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stage_fault_creates_no_submission()
    {
        var h = new Harness();
        h.Files.Gateway.FailOp["create"] = new Azure.RequestFailedException(503, "storage unavailable");

        var outcome = await h.Build().SubmitAsync(Internal(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 500 && o.ErrorCode == InvoiceSubmissionErrors.Failed);
        h.Allocations.Should().BeEmpty();
        h.JobsSent.Should().BeEmpty();
        h.Files.Gateway.Names.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stage_whose_reply_is_lost_is_removed_by_its_name()
    {
        // The create committed the blob, then the reply was lost: compensation removes it by its deterministic name.
        var h = new Harness();
        h.Files.Gateway.BeforeOp = (op, path) =>
        {
            if (op == "create")
            {
                h.Files.Gateway.Seed(path[..path.IndexOf('/')], path[(path.IndexOf('/') + 1)..], Pdf());
            }
        };
        h.Files.Gateway.FailOp["create"] = new TimeoutException("the PUT timed out after storage committed it");

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        h.BlobDeletes.Should().ContainSingle();
        h.Files.Gateway.Names.Should().BeEmpty();
        h.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cleanup_that_fails_never_masks_the_original_failure()
    {
        var h = new Harness { JobFailure = new ServiceBusException("queue down", ServiceBusFailureReason.ServiceCommunicationProblem) };
        h.Files.Gateway.FailOp["delete"] = new Azure.RequestFailedException(503, "storage unavailable");

        var outcome = await h.Build().SubmitAsync(Spa(), CancellationToken.None);

        outcome.Should().Match<InvoiceSubmissionOutcome>(o => o.StatusCode == 500 && o.ErrorCode == InvoiceSubmissionErrors.Failed);
        h.Deleted.Should().ContainSingle("the submission is still removed");
        h.Files.Gateway.Names.Should().ContainSingle("the blob the failed delete kept is left for the reconcile job");
    }

    [Theory]
    [InlineData(InvoiceSubmissionChannel.Spa)]
    [InlineData(InvoiceSubmissionChannel.Internal)]
    public async Task A_caller_who_disconnects_mid_write_gets_a_complete_submission_not_orphans(InvoiceSubmissionChannel channel)
    {
        // The tab closes while the bytes are staged. Every later write still runs to the end: an abandoned write may commit with its reply
        // lost. The in-memory stage copies the bytes under the token it was given, so a stage that observed the caller's token would fail here.
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        h.Files.Gateway.BeforeOp = (op, _) =>
        {
            if (op == "create")
            {
                cts.Cancel();
            }
        };

        var outcome = await h.Build().SubmitAsync(channel == InvoiceSubmissionChannel.Spa ? Spa() : Internal(), cts.Token);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.JobsSent.Should().ContainSingle();
        h.Deleted.Should().BeEmpty();
        h.BlobDeletes.Should().BeEmpty();
        h.Files.Gateway.Names.Should().ContainSingle();
        h.WriteTokens.Should().HaveCount(2).And.OnlyContain(t => !t.CanBeCanceled, "no write observes the caller's token");
    }

    [Fact]
    public async Task A_caller_already_gone_before_the_stage_writes_nothing()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => h.Build().SubmitAsync(Spa(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Writes.Should().Be(0);
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
    public async Task A_read_fault_is_a_503_problem_never_a_500_or_not_found()
    {
        var h = new Harness();
        h.Entities.Setup(e => e.RetrieveAsync("sprk_invoicesubmission", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));
        h.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));
        var service = h.Build();

        (await service.Invoking(s => s.GetAsync(Guid.NewGuid(), CancellationToken.None)).Should().ThrowAsync<SdapProblemException>())
            .Which.Should().Match<SdapProblemException>(e => e.StatusCode == 503 && e.Code == InvoiceSubmissionErrors.CoreRecordUnavailable);
        (await service.Invoking(s => s.ListAsync(new[] { FirmId }, new[] { MatterId }, Array.Empty<Guid>(), Array.Empty<Guid>(), CancellationToken.None))
                .Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(503);
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
