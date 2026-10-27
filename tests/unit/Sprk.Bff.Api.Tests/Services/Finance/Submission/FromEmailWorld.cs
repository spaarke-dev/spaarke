using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095: the world a from-email submission runs in, for <see cref="FromEmailSubmissionServiceTests"/> and the route's
/// contract suite. The intake service is the REAL one over <see cref="InvoiceSubmissionServiceTests.Harness"/> (the firm, its link and the
/// communication's filing are real). The rest are doubles at their narrow seams:
/// <list type="bullet">
/// <item>the attempt rows (by id, as the deterministic ids address them);</item>
/// <item>the communication lookup by Message-ID;</item>
/// <item>the in-process email save (it builds the real <c>.eml</c> from what it is handed);</item>
/// <item>the caller's systemuser;</item>
/// <item>SPE downloads of earlier <c>.eml</c> files;</item>
/// <item>task 094's Submit Invoice operation. When it receives a submission it stores the Received row, as 094 will, so a repeat is a replay.</item>
/// </list>
/// </summary>
internal sealed class FromEmailWorld
{
    public static readonly Guid CallerId = Guid.Parse("09509509-0950-4950-8950-00000000ca11");
    public static readonly Guid OtherCallerId = Guid.Parse("09509509-0950-4950-8950-00000000ca12");
    public static readonly Guid EmlId = Guid.Parse("09509509-0950-4950-8950-0000000000e1");
    public static readonly Guid CommunicationId = Guid.Parse("09509509-0950-4950-8950-0000000000c1");
    public const string MessageId = "<inv-1001@firm.example>";

    public static Guid MatterId => InvoiceSubmissionServiceTests.MatterId;
    public static Guid FirmId => InvoiceSubmissionServiceTests.FirmId;

    public static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 invoice 1001 for services");
    public static readonly byte[] XlsxBytes = Encoding.ASCII.GetBytes("PK timesheet October");

    public readonly InvoiceSubmissionServiceTests.Harness Intake = new();

    /// <summary>The attempt rows by submission id (the operation double adds one on Received).</summary>
    public readonly Dictionary<Guid, Entity> Attempts = new();

    public Exception? AttemptReadFailure;

    /// <summary>The communication the Message-ID lookup finds (null: none).</summary>
    public Entity? Communication;

    public Exception? CommunicationLookupFailure;

    /// <summary>The caller's earlier <c>.eml</c> candidates (the duplicate path's query), and its unlinked-save lookup.</summary>
    public readonly List<Entity> Candidates = new();
    public readonly List<Entity> UnlinkedSaves = new();
    public readonly List<QueryExpression> Queries = new();

    /// <summary>The files in SPE by (drive, item).</summary>
    public readonly Dictionary<(string Drive, string Item), byte[]> Files = new();

    public readonly FakeSave Save = new();
    public readonly FakeOperation Operation;
    public CallerSystemUserResolution Caller = CallerSystemUserResolution.Resolved(CallerId.ToString("D"));

    public readonly Mock<IGenericEntityService> Entities = new();
    public readonly Mock<ICommunicationDataverseService> Communications = new();

    public FromEmailWorld()
    {
        Operation = new FakeOperation(this);
        // The email's communication is filed under the test matter (D-140's hop reads it through the intake's harness).
        Intake.Communications[CommunicationId] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        Entities.Setup(e => e.RetrieveAsync(InvoiceSubmissionService.SubmissionEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) => AttemptReadFailure is { } failure
                ? Task.FromException<Entity>(failure)
                : Attempts.TryGetValue(id, out var row) ? Task.FromResult(row) : Task.FromException<Entity>(NotFound()));
        Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                Queries.Add(q);
                var unlinked = q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_relatedcommunication" && c.Operator == ConditionOperator.Null);
                return new EntityCollection((unlinked ? UnlinkedSaves : Candidates).ToList());
            });

        Communications.Setup(c => c.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) => CommunicationLookupFailure is { } failure
                ? Task.FromException<Entity?>(failure)
                : Task.FromResult(Communication));

        Intake.Spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string drive, string item, CancellationToken _) =>
                Files.TryGetValue((drive, item), out var bytes) ? new MemoryStream(bytes) : null);
    }

    public FromEmailSubmissionService Build()
    {
        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(c => c.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Caller);
        return new FromEmailSubmissionService(Intake.Build(), Operation, Save, Entities.Object, Communications.Object, callers.Object,
            Intake.Spe.Object, NullLogger<FromEmailSubmissionService>.Instance);
    }

    // ── the request ─────────────────────────────────────────────────────────────────────────────────────────────

    public static AttachmentReference Attachment(string name, byte[] bytes, string type) => new()
    {
        AttachmentId = "att-" + name, FileName = name, Size = bytes.Length, ContentType = type, ContentBase64 = Convert.ToBase64String(bytes),
    };

    public static readonly AttachmentReference Invoice = Attachment("Invoice 1001.pdf", PdfBytes, "application/pdf");
    public static readonly AttachmentReference Timesheet = Attachment("Timesheet.xlsx", XlsxBytes, "application/vnd.ms-excel");

    public static FromEmailMark Mark(AttachmentReference a, string mark) => new()
    {
        FileName = a.FileName, Size = a.Size, ContentType = a.ContentType, Mark = mark,
    };

    public static EmailMetadata Email(params AttachmentReference[] attachments) => new()
    {
        Subject = "Invoice 1001",
        SenderEmail = "billing@firm.example",
        InternetMessageId = MessageId,
        Body = "Please find our invoice attached.",
        Attachments = (attachments.Length == 0 ? new[] { Invoice, Timesheet } : attachments).ToList(),
    };

    public static FromEmailSubmissionInput Input(
        IReadOnlyList<FromEmailMark>? marks = null, EmailMetadata? email = null, string? number = "1001", string mode = "New", Guid? firm = null) => new()
    {
        CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, MatterId),
        SaveEntityType = "Matter",
        Email = email ?? Email(),
        Marks = marks ?? new[] { Mark(Invoice, "Invoice"), Mark(Timesheet, "Supporting") },
        FirmId = firm ?? FirmId,
        FirmInvoiceNumber = number,
        Mode = mode,
    };

    public static HttpContext Http()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-095" };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", "09509509-0950-4950-8950-0000000000a0") }, "Test"));
        return http;
    }

    // ── attempt rows ────────────────────────────────────────────────────────────────────────────────────────────

    public static string K(FromEmailSubmissionInput? input = null) => FromEmailSubmissionKey.Of(input ?? Input());

    /// <summary>Stores attempt <paramref name="generation"/> of <paramref name="k"/> as 094 would have created it.</summary>
    public Entity AddAttempt(string k, int generation, InvoiceSubmissionStatus status, Guid? creator = null, string? tracking = null, Guid? eml = null)
    {
        var id = FromEmailSubmissionKey.SubmissionIdFor(k, generation);
        var row = new Entity(InvoiceSubmissionService.SubmissionEntity, id)
        {
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
            ["sprk_document"] = new EntityReference("sprk_document", eml ?? EmlId),
            [TrackingNumberAllocator.TrackingNumberColumn] = tracking ?? "1001." + generation,
            [RecordCreatorPerson.Column] = new EntityReference("systemuser", creator ?? CallerId),
        };
        Attempts[id] = row;
        return row;
    }

    /// <summary>An earlier <c>.eml</c> of the caller in SPE, carrying <paramref name="attachments"/>, as a duplicate-path candidate.</summary>
    public Entity AddEarlierEml(Guid id, string? accessInheritance = null, params AttachmentReference[] attachments)
    {
        using var eml = OfficeEmailEnricher.BuildEmlFromMetadata(Email(attachments));
        using var copy = new MemoryStream();
        eml.CopyTo(copy);
        var item = "item-" + id.ToString("N")[..8];
        Files[("b!matter", item)] = copy.ToArray();
        var row = new Entity("sprk_document", id) { ["sprk_graphdriveid"] = "b!matter", ["sprk_graphitemid"] = item };
        if (accessInheritance is not null)
        {
            row[Sprk.Bff.Api.Services.Access.AccessInheritance.Column] = accessInheritance;
        }

        Candidates.Add(row);
        return row;
    }

    public static Exception NotFound() =>
        new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist");

    // ── the in-process save ─────────────────────────────────────────────────────────────────────────────────────

    internal sealed class FakeSave : IOfficeSubmissionEmailSave
    {
        public readonly List<SaveRequest> Requests = new();

        /// <summary>Null (default): a fresh save that builds the real <c>.eml</c> from the request. Otherwise this answer.</summary>
        public SubmissionEmailSaveOutcome? Answer;

        public Task<SubmissionEmailSaveOutcome> SaveEmailForSubmissionAsync(SaveRequest request, string userId, HttpContext http, CancellationToken ct)
        {
            Requests.Add(request);
            if (Answer is { } answer)
            {
                return Task.FromResult(answer);
            }

            using var eml = OfficeEmailEnricher.BuildEmlFromMetadata(request.Email!);
            return Task.FromResult(new SubmissionEmailSaveOutcome
            {
                Response = new SaveResponse { Success = true, JobId = Guid.NewGuid() },
                EmlDocumentId = EmlId,
                CommunicationId = CommunicationId,
                EmlParts = EmlMimeParts.Read(eml),
            });
        }

        public static SubmissionEmailSaveOutcome Duplicate() => new()
        {
            Response = new SaveResponse { Success = true, Duplicate = true, JobId = Guid.NewGuid() },
            CommunicationId = CommunicationId,
        };
    }

    // ── task 094's operation ────────────────────────────────────────────────────────────────────────────────────

    internal sealed class FakeOperation(FromEmailWorld world) : ISubmitInvoiceFromEmail
    {
        public bool Available = true;
        public readonly List<FromEmailSubmitInput> Submits = new();
        public readonly List<(Guid Submission, Guid Job)> Requeues = new();
        public readonly Dictionary<Guid, string> InvoiceShas = new();

        /// <summary>Null (default): Received, storing the row. Otherwise this answer (AlreadyCreated, Refused).</summary>
        public Func<FromEmailSubmitInput, FromEmailSubmitResult>? Answer;

        public bool Requeued = true;

        public bool IsAvailable => Available;

        public Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct)
        {
            Submits.Add(input);
            if (Answer is { } answer)
            {
                return Task.FromResult(answer(input));
            }

            var tracking = input.FirmInvoiceNumber + "." + input.Generation;
            world.Attempts[input.SubmissionId] = new Entity(InvoiceSubmissionService.SubmissionEntity, input.SubmissionId)
            {
                ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Received),
                ["sprk_document"] = new EntityReference("sprk_document", input.EmlDocumentId),
                [TrackingNumberAllocator.TrackingNumberColumn] = tracking,
                [RecordCreatorPerson.Column] = new EntityReference("systemuser", input.CallerSystemUserId),
            };
            InvoiceShas[input.SubmissionId] = input.MarkedParts.Single(p => p.Mark == FromEmailMarks.Invoice).Sha256;
            return Task.FromResult(FromEmailSubmitResult.Received(tracking));
        }

        public Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct)
        {
            Requeues.Add((submissionId, jobId));
            return Task.FromResult(Requeued);
        }

        public Task<string?> ReadInvoicePartSha256Async(Guid submissionId, CancellationToken ct) =>
            Task.FromResult(InvoiceShas.TryGetValue(submissionId, out var sha) ? sha : null);
    }
}
