using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The wire contract of <c>POST /api/finance/invoice-submissions/from-email</c> and <c>GET /api/finance/core-records/{type}/{id}/firms</c>
/// (ebilling-r1 task 095; <c>projects/ebilling-r1/notes/095-from-email-contract.md</c>, agreed with word-add-in-r1). The REAL mapper, the
/// REAL filters in their constrained order (step 0, the Finance filter, OfficeAuth, EntityAccessFilter, the Office Save rate limit, the
/// version-save gate, the kill switch, the idempotency filter bound to K) and the REAL <see cref="FromEmailSubmissionService"/> and
/// <see cref="InvoiceSubmissionService"/>; doubles only at Dataverse, the in-process Office save and task 094's operation
/// (<see cref="FromEmailWorld"/>).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): every field name, status and code here is a client contract (word-add-in-r1). One test per error row and
/// per replay row of contract sections 5 and 6; rows with no producer today are listed in the task notes, not faked here.
/// </remarks>
public sealed class InvoiceSubmissionFromEmailContractTests : IAsyncLifetime
{
    private const string Url = "/api/finance/invoice-submissions/from-email";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly W _world = new();
    private readonly RecordingAccessDataSource _access = new();
    private readonly RecordProbe _probe = new();
    private readonly RateLimits _rateLimits = new();
    private readonly InMemoryTenantCache _cache = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public StatedCaller StepZero { get; private set; } = null!;

    public InvoiceSubmissionFromEmailContractTests()
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, W.MatterId, AccessRights.Read | AccessRights.AppendTo);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, W.FirmId, AccessRights.AppendTo);
        _probe.Rights[W.MatterId] = AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        StepZero = StatedCaller.Register(builder.Services);
        builder.Services.AddRateLimiter(opt => opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

        builder.Services.AddSingleton<IAccessDataSource>(_access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton<CallerRecordAccessProbe>(_probe);
        builder.Services.AddSingleton<IOfficeRateLimitService>(_rateLimits);
        builder.Services.AddSingleton<ITenantCache>(_cache);

        builder.Services.AddSingleton(_world.Intake.Monitor);
        builder.Services.AddSingleton(_world.Intake.Build());
        builder.Services.AddSingleton(_world.Intake.Participations);
        builder.Services.AddSingleton(_world.Build());

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapInvoiceSubmissionEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ── Helpers ──

    private static FromEmailSubmissionRequest Request(
        EmailMetadata? email = null, List<FromEmailMark>? marks = null, Guid? firm = null, string? number = "1001",
        string mode = "New", Guid? invoiceId = null) => new()
    {
        TargetEntity = new FromEmailTargetEntity { EntityType = "Matter", EntityId = W.MatterId },
        Email = email ?? W.Email(),
        Marks = marks ?? new List<FromEmailMark> { W.Mark(W.Invoice, "Invoice"), W.Mark(W.Timesheet, "Supporting") },
        FirmId = firm ?? W.FirmId,
        FirmInvoiceNumber = number,
        Mode = mode,
        InvoiceId = invoiceId,
    };

    private static string K(FromEmailSubmissionRequest? request = null) =>
        FromEmailSubmissionKey.Of(InvoiceSubmissionFromEmailHttp.ToInput(request ?? Request()));

    private Task<HttpResponseMessage> PostAsync(object? body = null, string? idempotencyKey = null, bool authenticated = true)
    {
        var json = body as string ?? JsonSerializer.Serialize(body ?? Request(), Json);
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (authenticated)
        {
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("X-Idempotency-Key", idempotencyKey);
        }

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetFirmsAsync(string entityType = "Matter", Guid? id = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/core-records/{entityType}/{id ?? W.MatterId}/firms");
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    /// <summary>Contract section 6, OP-8: the client reads <c>errorCode ?? reasonCode</c>.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return body.TryGetProperty("errorCode", out var code) ? code.GetString()
            : body.TryGetProperty("reasonCode", out var reason) ? reason.GetString() : null;
    }

    private void NothingWritten()
    {
        _world.Save.Requests.Should().BeEmpty("a refused request saves no email");
        _world.Operation.Submits.Should().BeEmpty("a refused request submits nothing");
        _world.Operation.Requeues.Should().BeEmpty();
    }

    private async Task<JsonElement> Accepted(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 202: the response shape (contract section 3)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Fresh_IsA202_WithExactlyTheFiveFields_AndLocationIsTheStatusUrl()
    {
        var response = await PostAsync();

        var body = await Accepted(response);
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            new[] { "submissionId", "jobId", "trackingNumber", "statusUrl", "emlDocumentId" }, o => o.WithStrictOrdering());
        var k = K();
        body.GetProperty("submissionId").GetGuid().Should().Be(FromEmailSubmissionKey.SubmissionIdFor(k, 1));
        body.GetProperty("jobId").GetGuid().Should().Be(FromEmailSubmissionKey.JobIdFor(k, 1));
        body.GetProperty("trackingNumber").GetString().Should().Be("1001.1");
        body.GetProperty("statusUrl").GetString().Should().Be($"/api/finance/invoice-submissions/{FromEmailSubmissionKey.SubmissionIdFor(k, 1)}");
        body.GetProperty("emlDocumentId").GetGuid().Should().Be(W.EmlId);
        response.Headers.Location!.ToString().Should().Be(body.GetProperty("statusUrl").GetString());

        // The in-process save: the email alone, filed to the body's core record, no attachment Documents, the server's own duplicate key.
        var save = _world.Save.Requests.Should().ContainSingle().Subject;
        save.ContentType.Should().Be(SaveContentType.Email);
        save.TargetEntity!.EntityId.Should().Be(W.MatterId);
        save.Email!.SelectedAttachmentFileNames.Should().BeEmpty();
        save.IdempotencyKey.Should().BeNull();

        // The seam's input: the staged Stored File ids (task 092), never a document as the submission's file.
        var submit = _world.Operation.Submits.Should().ContainSingle().Subject;
        submit.InvoiceStoredFileId.Should().Be(Sprk.Bff.Api.Services.Finance.Files.StoredFileIds.ForUpload(submit.SubmissionId));
        submit.SourceEmlDocumentId.Should().Be(W.EmlId);
    }

    [Fact]
    public async Task Request_SelectedAttachmentFileNames_AndABodyIdempotencyKey_AreIgnored()
    {
        // Contract section 2: the server overwrites selectedAttachmentFileNames with [] and accepts no body idempotencyKey.
        var json = JsonSerializer.Serialize(Request(email: W.Email() with { SelectedAttachmentFileNames = new List<string> { "Timesheet.xlsx" } }), Json);
        json = json.TrimEnd('}') + ",\"idempotencyKey\":\"client-chosen\"}";

        await Accepted(await PostAsync(json));

        var save = _world.Save.Requests.Should().ContainSingle().Subject;
        save.Email!.SelectedAttachmentFileNames.Should().BeEmpty();
        save.IdempotencyKey.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Section 6: the filter rows (before any handler refusal)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoToken_Is401_AndNothingIsWritten()
    {
        var response = await PostAsync(authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        NothingWritten();
    }

    [Fact]
    public async Task StepZero_AFlaggedExternalCaller_Is403InternalCallersOnly()
    {
        StepZero.Kind = StepZeroCallerKind.FlagTrue;

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("sdap.access.deny.internal_callers_only");
        NothingWritten();
    }

    [Fact]
    public async Task StepZero_ASystemUserLookupFault_Is403SystemFailure()
    {
        StepZero.Kind = StepZeroCallerKind.LookupFault;

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("sdap.access.error.system_failure");
        NothingWritten();
    }

    [Fact]
    public async Task FinanceFilter_ARightsReadFault_Is403SystemFailure()
    {
        _access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess unavailable");

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("sdap.access.error.system_failure");
        NothingWritten();
    }

    [Fact]
    public async Task FinanceFilter_NoAppendToOnTheCoreRecord_Is403InsufficientRights()
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, W.MatterId, AccessRights.Read);

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("sdap.access.deny.insufficient_rights");
        NothingWritten();
    }

    [Fact]
    public async Task FinanceFilter_NoAppendToOnTheFirm_Is403InsufficientRights()
    {
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, W.FirmId, AccessRights.Read);

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("sdap.access.deny.insufficient_rights");
        NothingWritten();
    }

    [Fact]
    public async Task EntityAccessFilter_GatesTheBodysCoreRecord_403Office009_WithReasonCodeAndEntityType()
    {
        // The Finance check passes; the caller cannot file a document to the record (EntityAccessFilter reads FromEmailSubmissionBody).
        _probe.Rights[W.MatterId] = AccessRights.Read;

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        body.GetProperty("reasonCode").GetString().Should().Be("insufficient_rights");
        body.GetProperty("entityType").GetString().Should().Be("Matter");
        _probe.Calls.Should().ContainSingle().Which.Should().Be(W.MatterId, "the filter asks about the body's own core record");
        NothingWritten();
    }

    public static TheoryData<string, string> ShapeRefusals() => new()
    {
        // (the JSON body, the errors key the ValidationProblem names)
        { "{}", "targetEntity" },
        { "not json", "$" },
        { Mutate(r => r with { TargetEntity = new FromEmailTargetEntity { EntityType = "Account", EntityId = Guid.NewGuid() } }), "targetEntity.entityType" },
        { Mutate(r => r with { TargetEntity = new FromEmailTargetEntity { EntityType = "Matter", EntityId = Guid.Empty } }), "targetEntity.entityId" },
        { Mutate(r => r with { Email = null }), "email" },
        { Mutate(r => r with { Email = r.Email! with { InternetMessageId = "AAMkAD-exchange-item-id" } }), "email.internetMessageId" },
        { Mutate(r => r with { Email = r.Email! with { InternetMessageId = null } }), "email.internetMessageId" },
        { Mutate(r => r with { Marks = null }), "marks" },
        { Mutate(r => r with { Marks = new List<FromEmailMark> { new() { FileName = "Invoice 1001.pdf", Size = 1, ContentType = "application/pdf" } } }), "marks[0].mark" },
        { Mutate(r => r with { FirmId = null }), "firmId" },
        { Mutate(r => r with { FirmInvoiceNumber = new string('9', 101) }), "firmInvoiceNumber" },
        { Mutate(r => r with { Mode = "new" }), "mode" },
        { Mutate(r => r with { InvoiceId = Guid.NewGuid() }), "invoiceId" },
        { Mutate(r => r with { Mode = "Correction" }), "invoiceId" },
    };

    private static string Mutate(Func<FromEmailSubmissionRequest, FromEmailSubmissionRequest> change) =>
        JsonSerializer.Serialize(change(Request()), Json);

    [Theory]
    [MemberData(nameof(ShapeRefusals))]
    public async Task BodyShape_Is400ValidationProblem_NamingTheField_BeforeAnyRightsQuery(string json, string field)
    {
        var response = await PostAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await BodyAsync(response);
        body.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(body.ToString());
        body.TryGetProperty("errorCode", out _).Should().BeFalse("the body-shape 400 carries no code");
        _access.Calls.Should().BeEmpty("the shape is refused before any rights query");
        _probe.Calls.Should().BeEmpty();
        NothingWritten();
    }

    [Fact]
    public async Task RateLimit_TheOfficeSaveBucket_Is429Office015_WithRetryAfter()
    {
        _rateLimits.Allowed = false;

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodeAsync(response)).Should().Be("OFFICE_015");
        response.Headers.RetryAfter.Should().NotBeNull();
        var body = await BodyAsync(response);
        body.TryGetProperty("retryAfterSeconds", out _).Should().BeTrue();
        body.TryGetProperty("limit", out _).Should().BeTrue();
        _rateLimits.Categories.Should().ContainSingle().Which.Should().Be(OfficeRateLimitCategory.Save);
        NothingWritten();
    }

    [Fact]
    public async Task KillSwitch_EbillingOff_Is503EbillingDisabled()
    {
        _world.Intake.Options.Enabled = false;

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("ebilling.disabled");
        NothingWritten();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Section 6: the handler rows (marks, then bytes, then the rest)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("invoice_count")]
    [InlineData("unknown_mark")]
    [InlineData("inline_attachment")]
    [InlineData("unmarked_attachment")]
    [InlineData("duplicate_mark")]
    [InlineData("ambiguous_attachment")]
    [InlineData("invoice_not_pdf")]
    public async Task Marks_EachRule_Is400MarksInvalid_WithItsReason(string reason)
    {
        var (email, marks, fileName) = MarksCase(reason);

        var response = await PostAsync(Request(email: email, marks: marks));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("ebilling.submission.marks_invalid");
        body.GetProperty("reason").GetString().Should().Be(reason);
        if (fileName is null)
        {
            body.TryGetProperty("fileName", out _).Should().BeFalse();
        }
        else
        {
            body.GetProperty("fileName").GetString().Should().Be(fileName);
        }

        NothingWritten();
    }

    private static (EmailMetadata Email, List<FromEmailMark> Marks, string? FileName) MarksCase(string reason)
    {
        var inline = W.Attachment("image001.png", Encoding.ASCII.GetBytes("png"), "image/png") with { IsInline = true, ContentId = "cid1" };
        var twin = W.Invoice with { AttachmentId = "att-twin" };
        var text = W.Attachment("invoice.txt", W.PdfBytes, "text/plain");
        return reason switch
        {
            "invoice_count" => (W.Email(), new() { W.Mark(W.Invoice, "Supporting"), W.Mark(W.Timesheet, "Supporting") }, null),
            "unknown_mark" => (W.Email(), new() { W.Mark(W.Invoice, "invoice"), W.Mark(W.Timesheet, "Supporting") }, "Invoice 1001.pdf"),
            "inline_attachment" => (W.Email(W.Invoice, inline), new() { W.Mark(W.Invoice, "Invoice"), W.Mark(inline, "Supporting") }, "image001.png"),
            "unmarked_attachment" => (W.Email(), new() { W.Mark(W.Invoice, "Invoice") }, "Timesheet.xlsx"),
            "duplicate_mark" => (W.Email(), new() { W.Mark(W.Invoice, "Invoice"), W.Mark(W.Timesheet, "Supporting"), W.Mark(W.Timesheet, "DontSave") }, "Timesheet.xlsx"),
            "ambiguous_attachment" => (W.Email(W.Invoice, twin), new() { W.Mark(W.Invoice, "Invoice") }, "Invoice 1001.pdf"),
            _ => (W.Email(text), new() { W.Mark(text, "Invoice") }, "invoice.txt"),
        };
    }

    [Fact]
    public async Task Marks_ADontSaveMarkMatchingNoEntry_IsAccepted()
    {
        var marks = new List<FromEmailMark>
        {
            W.Mark(W.Invoice, "Invoice"), W.Mark(W.Timesheet, "Supporting"),
            new() { FileName = "gone.docx", Size = 3, ContentType = "application/msword", Mark = "DontSave" },
        };

        await Accepted(await PostAsync(Request(marks: marks)));
    }

    [Fact]
    public async Task Bytes_AMarkedAttachmentWithoutContent_Is422MarkedFileMissing_WithFileNameAndMark()
    {
        var timesheet = W.Timesheet with { ContentBase64 = null };

        var response = await PostAsync(Request(email: W.Email(W.Invoice, timesheet),
            marks: new() { W.Mark(W.Invoice, "Invoice"), W.Mark(timesheet, "Supporting") }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("ebilling.submission.marked_file_missing");
        body.GetProperty("fileName").GetString().Should().Be("Timesheet.xlsx");
        body.GetProperty("mark").GetString().Should().Be("Supporting");
        NothingWritten();
    }

    [Fact]
    public async Task Order_MarksAreCheckedBeforeBytes()
    {
        // Both rules are broken; the 400 comes first (contract section 6, Order).
        var noBytes = W.Invoice with { ContentBase64 = null };

        var response = await PostAsync(Request(email: W.Email(noBytes, W.Timesheet), marks: new() { W.Mark(noBytes, "Invoice") }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("ebilling.submission.marks_invalid");
    }

    [Fact]
    public async Task FirmInvoiceNumber_Blank_Is400FirmInvoiceNumberRequired()
    {
        var response = await PostAsync(Request(number: "   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("ebilling.submission.firm_invoice_number_required");
        NothingWritten();
    }

    [Fact]
    public async Task Firm_EmptyId_Is400FirmInvalid()
    {
        var response = await PostAsync(Request(firm: Guid.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("ebilling.submission.firm_invalid");
        NothingWritten();
    }

    [Fact]
    public async Task Firm_Inactive_Is400FirmInvalid()
    {
        _world.Intake.Firm!["statecode"] = new OptionSetValue(1);

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("ebilling.submission.firm_invalid");
        NothingWritten();
    }

    [Fact]
    public async Task Firm_NotLinkedToTheRecord_Is403FirmNotLinked()
    {
        _world.Intake.Links[("sprk_matter", W.MatterId)] = new[] { Guid.NewGuid() };

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("ebilling.submission.firm_not_linked");
        NothingWritten();
    }

    [Fact]
    public async Task Firm_AReadFault_Is503RecordUnavailable()
    {
        _world.Intake.LinkReadFailure = new TimeoutException();

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("ebilling.submission.record_unavailable");
        NothingWritten();
    }

    [Fact]
    public async Task Correction_BeforeTask097_Is409CorrectionNotAllowed_NotAvailable()
    {
        var response = await PostAsync(Request(mode: "Correction", invoiceId: Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("ebilling.submission.correction_not_allowed");
        body.GetProperty("reason").GetString().Should().Be("not_available");
        NothingWritten();
    }

    [Fact]
    public async Task Communication_FiledUnderAnotherCoreRecord_Is409DocumentCoreRecordMismatch()
    {
        var other = Guid.NewGuid();
        _world.Communication = new Entity("sprk_communication", other);
        _world.Intake.Communications[other] = new Entity("sprk_communication", other)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
        };

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("ebilling.submission.document_core_record_mismatch");
        NothingWritten();
    }

    [Fact]
    public async Task EmailUnidentifiable_NoCommunication_Is422_WithReason()
    {
        _world.Save.Answer = new SubmissionEmailSaveOutcome { Refusal = SubmissionEmailSaveRefusal.NoCommunication };

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("ebilling.submission.email_unidentifiable");
        body.GetProperty("reason").GetString().Should().Be("no_communication");
        _world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordUnavailable_AnAttemptReadFault_Is503()
    {
        _world.AttemptReadFailure = new TimeoutException();

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("ebilling.submission.record_unavailable");
        NothingWritten();
    }

    [Fact]
    public async Task Failed_TheOperationIsNotAvailable_Is503SubmissionFailed_BeforeAnyWrite()
    {
        // Until task 094 registers its adapter, the NotYetAvailable seam answers exactly this.
        _world.Operation.Available = false;

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("ebilling.submission.failed");
        NothingWritten();
    }

    [Fact]
    public async Task OfficeSave_ItsOwnRefusal_IsPassedThroughAsOfficeSaveRendersIt()
    {
        _world.Save.Answer = new SubmissionEmailSaveOutcome
        {
            Response = new SaveResponse { Success = false, Error = new SaveError { Code = "OFFICE_012", Message = "Upload failed", Retryable = true } },
        };

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await CodeAsync(response)).Should().Be("OFFICE_012");
        _world.Operation.Submits.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Section 5: duplicate replay (EB-D-160)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(InvoiceSubmissionStatus.Received)]
    [InlineData(InvoiceSubmissionStatus.Processing)]
    [InlineData(InvoiceSubmissionStatus.Accepted)]
    public async Task Replay_SameCallerSameK_LatestNotEnded_Is202WithTheIdenticalBody(InvoiceSubmissionStatus status)
    {
        var first = await Accepted(await PostAsync());
        var row = _world.Attempts[first.GetProperty("submissionId").GetGuid()];
        row["sprk_processingstatus"] = new OptionSetValue((int)status);

        var second = await Accepted(await PostAsync());

        second.ToString().Should().Be(first.ToString());
        _world.Operation.Submits.Should().ContainSingle("a replay submits nothing new");
        _world.Operation.Requeues.Should().HaveCount(status == InvoiceSubmissionStatus.Received ? 1 : 0, "only a Received row is re-queued");
    }

    [Fact]
    public async Task Replay_FromTheResponseCache_Is202WithTheIdenticalBody_AndXIdempotencyStatusCached()
    {
        var first = await PostAsync(idempotencyKey: "action-1");
        var firstBody = await Accepted(first);

        var second = await PostAsync(idempotencyKey: "action-1");

        (await Accepted(second)).ToString().Should().Be(firstBody.ToString());
        second.Headers.GetValues("X-Idempotency-Status").Should().Equal("cached");
        _world.Save.Requests.Should().ContainSingle("the cached answer never reaches the handler");
    }

    [Fact]
    public async Task Replay_LatestFailed_IsANewAttempt_NewIdsAndTrackingSuffix()
    {
        var k = K();
        _world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);

        var body = await Accepted(await PostAsync(idempotencyKey: "retry-action"));

        body.GetProperty("submissionId").GetGuid().Should().Be(FromEmailSubmissionKey.SubmissionIdFor(k, 2));
        body.GetProperty("jobId").GetGuid().Should().Be(FromEmailSubmissionKey.JobIdFor(k, 2));
        body.GetProperty("trackingNumber").GetString().Should().Be("1001.2");
        _world.Operation.Submits.Should().ContainSingle().Which.Generation.Should().Be(2);
    }

    [Fact]
    public async Task Replay_AnIdenticalRetryWithoutTheHeader_AfterFailed_IsANewAttempt_NotTheCachedFailedOne()
    {
        // Without X-Idempotency-Key the filter would key its 24-hour response cache on the body; the route never replays from it, so
        // the identical retry after Failed reaches the handler and gets the new attempt EB-D-160 grants.
        var first = await Accepted(await PostAsync());
        _world.Attempts[first.GetProperty("submissionId").GetGuid()]["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed);

        var second = await Accepted(await PostAsync());

        second.GetProperty("submissionId").GetGuid().Should().Be(FromEmailSubmissionKey.SubmissionIdFor(K(), 2));
        second.GetProperty("trackingNumber").GetString().Should().Be("1001.2");
    }

    [Fact]
    public async Task Replay_TwoConcurrentRetriesOfOneFailedK_CreateExactlyOneNewAttempt_TheOtherGetsItsReplay()
    {
        var k = K();
        _world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);
        _world.Intake.LinkReads.Capacity = 16; // the harness lists two requests append to at once (no resize while they race)
        using var bothRead = new Barrier(2);
        var gate = new object();
        _world.Operation.Answer = input =>
        {
            // Both requests have read "latest = generation 1, Failed" before either creates (the race EB-D-160 names).
            bothRead.SignalAndWait(TimeSpan.FromSeconds(10)).Should().BeTrue("both retries reach the operation");
            lock (gate)
            {
                if (_world.Attempts.ContainsKey(input.SubmissionId))
                {
                    return FromEmailSubmitResult.AlreadyCreated(); // the allocator's alternate key refused the second create
                }

                _world.AddAttempt(k, input.Generation, InvoiceSubmissionStatus.Received, tracking: "1001.2", eml: input.SourceEmlDocumentId);
                return FromEmailSubmitResult.Received("1001.2");
            }
        };

        var responses = await Task.WhenAll(PostAsync(idempotencyKey: "tab-1"), PostAsync(idempotencyKey: "tab-2"));

        var bodies = await Task.WhenAll(responses.Select(Accepted));
        bodies.Select(b => b.GetProperty("submissionId").GetGuid()).Should().AllBeEquivalentTo(FromEmailSubmissionKey.SubmissionIdFor(k, 2));
        bodies[0].ToString().Should().Be(bodies[1].ToString());
        _world.Attempts.Keys.Should().HaveCount(2, "generation 1 and exactly one generation 2");
    }

    [Fact]
    public async Task Replay_LatestRejected_Is409SubmissionAlreadyEnded()
    {
        _world.AddAttempt(K(), 1, InvoiceSubmissionStatus.Rejected);

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("ebilling.submission.submission_already_ended");
        NothingWritten();
    }

    [Fact]
    public async Task Replay_SameK_ADifferentInvoicePart_Is409DocumentAlreadySubmitted()
    {
        var row = _world.AddAttempt(K(), 1, InvoiceSubmissionStatus.Received);
        _world.Operation.Sources[row.Id] = new FromEmailAttemptSource(W.EmlId, new string('0', 64));

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("ebilling.submission.document_already_submitted");
        NothingWritten();
    }

    [Fact]
    public async Task Replay_SameK_FromAnotherUser_Is409DocumentAlreadySubmitted()
    {
        // OP-5: K has no caller; the same email, record, firm, number and marks from a second user is the same submission.
        _world.AddAttempt(K(), 1, InvoiceSubmissionStatus.Received, creator: W.OtherCallerId);

        var response = await PostAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("ebilling.submission.document_already_submitted");
        NothingWritten();
    }

    [Fact]
    public async Task Replay_TheSameHeaderOnADifferentK_IsItsOwnOperation()
    {
        var first = await Accepted(await PostAsync(idempotencyKey: "reused"));

        var second = await Accepted(await PostAsync(Request(number: "1002"), idempotencyKey: "reused"));

        second.GetProperty("submissionId").GetGuid().Should().NotBe(first.GetProperty("submissionId").GetGuid());
        second.GetProperty("trackingNumber").GetString().Should().Be("1002.1");
        _world.Operation.Submits.Should().HaveCount(2);
    }

    [Fact]
    public async Task Replay_ADifferentMarking_IsANewSubmission()
    {
        var first = await Accepted(await PostAsync());

        var second = await Accepted(await PostAsync(Request(marks: new() { W.Mark(W.Invoice, "Invoice"), W.Mark(W.Timesheet, "DontSave") })));

        second.GetProperty("submissionId").GetGuid().Should().NotBe(first.GetProperty("submissionId").GetGuid());
        _world.Operation.Submits.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Section 8: GET /api/finance/core-records/{entityType}/{entityId}/firms
    // ═══════════════════════════════════════════════════════════════════════════

    private void Organizations(params (Guid Id, string Name)[] active) =>
        _world.Intake.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_organization"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var asked = q.Criteria.Conditions.Single(c => c.AttributeName == "sprk_organizationid").Values.Cast<Guid>().ToHashSet();
                var nameColumn = q.ColumnSet.Columns.Single(c => c != "statecode");
                return new EntityCollection(active.Where(f => asked.Contains(f.Id))
                    .Select(f => new Entity("sprk_organization", f.Id) { [nameColumn] = f.Name, ["statecode"] = new OptionSetValue(0) }).ToList());
            });

    [Fact]
    public async Task Firms_AreTheRecordsActiveLinkedFirms_SortedByNameIgnoringCase_ThenId()
    {
        var beta = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var alphaHigh = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
        var alphaLow = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        var inactive = Guid.NewGuid();
        _world.Intake.Links[("sprk_matter", W.MatterId)] = new[] { beta, alphaHigh, inactive, alphaLow, beta };
        Organizations((beta, "beta LLP"), (alphaHigh, "alpha & Co"), (alphaLow, "Alpha & Co"));

        var response = await GetFirmsAsync("matter");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.EnumerateArray().Select(f => (f.GetProperty("firmId").GetGuid(), f.GetProperty("name").GetString()))
            .Should().Equal((alphaLow, "Alpha & Co"), (alphaHigh, "alpha & Co"), (beta, "beta LLP"));
        body.EnumerateArray().First().EnumerateObject().Select(p => p.Name).Should().Equal("firmId", "name");
    }

    [Fact]
    public async Task Firms_NoLinkedFirm_Is200EmptyList()
    {
        _world.Intake.Links[("sprk_matter", W.MatterId)] = Array.Empty<Guid>();

        var response = await GetFirmsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[]");
    }

    [Fact]
    public async Task Firms_EveryFirmItReturns_PassesFirmNotLinkedOnTheSubmit()
    {
        Organizations((W.FirmId, "Firm"));

        var firms = await BodyAsync(await GetFirmsAsync());
        var firmId = firms.EnumerateArray().Should().ContainSingle().Subject.GetProperty("firmId").GetGuid();

        await Accepted(await PostAsync(Request(firm: firmId)));
    }

    [Fact]
    public async Task Firms_NoReadOnTheRecord_Is404RecordUnavailable()
    {
        var response = await GetFirmsAsync("Project", Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be("sdap.access.deny.record_unavailable");
    }

    [Fact]
    public async Task Firms_AnotherEntityType_Is400ValidationProblem()
    {
        var response = await GetFirmsAsync("Account");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(response)).GetProperty("errors").TryGetProperty("entityType", out _).Should().BeTrue();
        _access.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Firms_ALinkReadFault_Is503RecordUnavailable_NeverAPartialList()
    {
        _world.Intake.LinkReadFailure = new TimeoutException();

        var response = await GetFirmsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("ebilling.submission.record_unavailable");
    }

    [Fact]
    public async Task Firms_ANameReadFault_Is503RecordUnavailable()
    {
        _world.Intake.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_organization"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var response = await GetFirmsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be("ebilling.submission.record_unavailable");
    }

    // ── Doubles ──

    /// <summary><see cref="CallerRecordAccessProbe"/> at its virtual seam, as EntityAccessFilter asks it: rights by record id.</summary>
    private sealed class RecordProbe : CallerRecordAccessProbe
    {
        public RecordProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public Dictionary<Guid, AccessRights> Rights { get; } = new();

        public List<Guid> Calls { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add(recordId);
            }

            return Task.FromResult(Rights.TryGetValue(recordId, out var rights) ? rights : AccessRights.None);
        }
    }

    private sealed class RateLimits : IOfficeRateLimitService
    {
        public bool Allowed { get; set; } = true;

        public List<OfficeRateLimitCategory> Categories { get; } = new();

        public Task<RateLimitResult> CheckAndIncrementAsync(string tenantId, string userId, OfficeRateLimitCategory category)
        {
            lock (Categories)
            {
                Categories.Add(category);
            }

            return Task.FromResult(new RateLimitResult
            {
                IsAllowed = Allowed, Limit = 10, Remaining = Allowed ? 9 : 0, ResetTimestamp = DateTimeOffset.UtcNow.AddSeconds(30), RetryAfterSeconds = 30,
            });
        }
    }
}
