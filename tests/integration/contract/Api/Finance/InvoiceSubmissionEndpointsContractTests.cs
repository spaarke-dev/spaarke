using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The invoice submission routes (ebilling-r1 task 044, FR-17, NFR-10) over the REAL mappers, the REAL filters (the Finance filter on the
/// internal routes, <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/> and the caller-principal group filter on the external
/// ones), the REAL kill switch and the REAL <see cref="InvoiceSubmissionService"/>, substituted only at Dataverse, SPE, the allocator and the
/// queue (<see cref="InvoiceSubmissionServiceTests.Harness"/>).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the firm and core-record checks are the NFR-10 boundary of the SPA channel; every denial is asserted to
/// leave nothing behind (no upload, no row, no job). The seeding proofs of task 044 neutralised the firm check and then the core-record
/// check in <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/> and watched the matching tests here fail (task notes).
/// </remarks>
public sealed class InvoiceSubmissionEndpointsContractTests : IAsyncLifetime
{
    /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
    public StatedCaller StepZero { get; private set; } = null!;

    private static readonly Guid FirmId = InvoiceSubmissionServiceTests.FirmId;
    private static readonly Guid MatterId = InvoiceSubmissionServiceTests.MatterId;
    private static readonly Guid OtherFirmId = Guid.NewGuid();

    private readonly InvoiceSubmissionServiceTests.Harness _h = new();
    private readonly Participations _participations = new();
    private readonly StubResolver _resolver = new();
    private readonly RecordingAccessDataSource _access = new();

    /// <summary>Task 094: the caller's own rights the from-document route's step 1 asks (gate variant) on the source document and communication.</summary>
    private readonly Sprk.Bff.Api.Tests.Finance.Files.RecordingRightsProbe _sourceRights = new();

    /// <summary>Task 118b-2: SharePoint Embedded's download, which the shared document read calls for a supporting document (its item's bytes).</summary>
    private readonly Mock<Sprk.Bff.Api.Infrastructure.Graph.ISpeFileOperations> _spe = new();
    private WebApplication _app = null!;
    // ebilling-r1 task 098 (review round 4b): the submission routes' seam is the REAL ExternalDataService.HideSubmissionsForAsync over
    // an app-only Web API double (InvoiceWorld): a test seeds the invoice's and the documents' rows the way their writers do.
    private readonly Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld _world = new();
    private readonly List<InvoiceSubmissionView[]> _submissionDecisions = new();
    private readonly List<Guid[]> _invoiceDecisions = new(); // task 130-0 fix round 1: each call of the caller-aware invoice seam
    private bool _registerSubmissionHiding = true;
    private HttpClient _client = null!;

    public InvoiceSubmissionEndpointsContractTests()
    {
        // The submission service reads the firm links through the SAME double the filter and the picker use, so one set of links
        // drives the early refusal on the external routes and the service's own check on both channels (D-110).
        _h.Participations = _participations;
        _participations.Firms = new[] { FirmId };
        // D-106: the firm is one of the matter's assigned law firms (the work-assignment and project below are linked per test).
        _participations.Links[("sprk_matter", MatterId)] = new[] { FirmId };
        _resolver.Principal = Collaborator(AccessRights.Read | AccessRights.Create | AccessRights.Write);
        _spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string item, CancellationToken _) => (Stream?)new MemoryStream(System.Text.Encoding.ASCII.GetBytes("bytes of " + item)));
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
        StepZero = StatedCaller.Register(builder.Services); // task 099: every /api/finance route runs step 0
        builder.Services.AddRateLimiter(opt =>
            opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

        // The REAL internal authorization stack, substituted only at the access-data boundary.
        builder.Services.AddSingleton<IAccessDataSource>(_access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();

        builder.Services.AddSingleton(_h.Monitor);
        builder.Services.AddSingleton(_h.Build());
        builder.Services.AddSingleton<ExternalParticipationService>(_participations);
        builder.Services.AddSingleton<ICallerPrincipalResolver>(_resolver);
        // Task 094: Submit Invoice step 1 (from-document route): the caller's rights probe and the app-only reader (the harness's Dataverse).
        builder.Services.AddSingleton<CallerRecordAccessProbe>(_sourceRights);
        builder.Services.AddSingleton(_h.Entities.Object);
        // Task 118b-2: the shared document read's seams the route binds for a supporting document (the download, and the REAL container
        // check over a world whose business unit claims the test drive).
        builder.Services.AddSingleton(_spe.Object);
        builder.Services.AddSingleton(TestRecordContainerResolver.ForBusinessUnitContainers("b!drive"));
        // ebilling-r1 task 098: the submission routes' (a') seam, as AddExternalAccess registers it (each call recorded).
        if (_registerSubmissionHiding)
            builder.Services.AddScoped<ExternalInvoiceSubmissionHiding>(_ => (caller, submissions, ct) =>
            {
                lock (_submissionDecisions) _submissionDecisions.Add(submissions.ToArray());
                return _world.NewService(SubmissionIdentity.Object).HideSubmissionsForAsync(caller, submissions, _world.RootFlags, ct);
            });
        // ebilling-r1 task 130-0 fix round 1: the caller-aware invoice seam the submission reads also apply (a submission follows its
        // invoice), as AddExternalAccess registers it: the REAL HideInvoicesForAsync over the same world, with the world's caller firms.
        builder.Services.AddScoped<ExternalInvoiceHiding>(_ => (caller, invoiceIds, ct) =>
        {
            lock (_invoiceDecisions) _invoiceDecisions.Add(invoiceIds.ToArray());
            return _world.NewService(SubmissionIdentity.Object).HideInvoicesForAsync(caller, invoiceIds, _world.RootFlags, _world.CallerFirms, ct);
        });

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        // Task 099: a request carrying RecordBodyHeader has its body stream wrapped, so a test can prove whether anything read it.
        _app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey(RecordBodyHeader))
            {
                context.Request.Body = new ReadRecordingStream(context.Request.Body, () => Interlocked.Increment(ref _bodyReads));
            }

            await next();
        });
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapInvoiceSubmissionEndpoints();
        // Stands in for ExternalAccessEndpoints' group: authenticated, then the caller-principal filter.
        _app.MapGroup("/api/v1/external").RequireAuthorization().AddCallerPrincipalAuthorizationFilter()
            .MapExternalInvoiceSubmissionEndpoints();

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

    private static CallerPrincipal Collaborator(AccessRights rightsOnMatter, Guid? contactId = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId ?? InvoiceSubmissionServiceTests.ContactId,
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [MatterId] = rightsOnMatter },
    };

    private static MultipartFormDataContent Form(byte[]? pdf = null, string contentType = "application/pdf", string? number = "INV-1")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf ?? InvoiceSubmissionServiceTests.Pdf());
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "invoice.pdf");
        if (number is not null)
        {
            form.Add(new StringContent(number), "firmInvoiceNumber");
        }

        return form;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null, bool authenticated = true)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (authenticated)
        {
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        }

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostExternalAsync(HttpContent content, string query = "") =>
        SendAsync(HttpMethod.Post, $"/api/v1/external/matters/{MatterId}/invoice-submissions{query}", content);

    private Task<HttpResponseMessage> PostInternalAsync(HttpContent content, Guid? firmId = null) =>
        SendAsync(HttpMethod.Post, $"/api/finance/matters/{MatterId}/invoice-submissions?firmId={firmId ?? FirmId}", content);

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private void NothingWritten()
    {
        _h.Stages.Should().BeEmpty("a refused submission stages nothing");
        _h.Allocations.Should().BeEmpty("a refused submission creates no row");
        _h.JobsSent.Should().BeEmpty("a refused submission enqueues nothing");
    }

    private void GrantInternalSubmit()
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.AppendTo);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);
    }

    /// <summary>A stored submission as the external (SPA) route writes it: channel Spa, no document (review round 4b: writer shape).</summary>
    private Entity StoredSubmission(Guid id, Guid firmId) => _h.Submission = new Entity("sprk_invoicesubmission", id)
    {
        ["sprk_trackingnumber"] = "INV-1.1",
        ["sprk_vendororg"] = new EntityReference("sprk_organization", firmId),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        ["sprk_processingstatus"] = new OptionSetValue(100000000),
        ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // External: the happy path
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task External_collaborator_of_their_firm_gets_202_with_location_and_receipt()
    {
        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var submissionId = body.RootElement.GetProperty("submissionId").GetGuid();
        var statusUrl = body.RootElement.GetProperty("statusUrl").GetString();
        statusUrl.Should().Be($"/api/v1/external/invoice-submissions/{submissionId}");
        response.Headers.Location!.ToString().Should().Be(statusUrl);
        body.RootElement.GetProperty("trackingNumber").GetString().Should().Be("INV-1.1");
        body.RootElement.GetProperty("jobId").GetGuid().Should().NotBeEmpty();

        var allocation = _h.Allocations.Should().ContainSingle().Subject;
        allocation.FirmId.Should().Be(FirmId);
        ((OptionSetValue)allocation.Fields!["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Spa);
        _h.JobsSent.Should().ContainSingle().Which.JobType.Should().Be(InvoiceSubmissionService.JobType);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: the provisional billing-terms warning on the 202 (ebilling-r1 082c-3; uac-r2 conditions (a) to (d))
    // ═══════════════════════════════════════════════════════════════════════════

    private static ProcedureResolution HeldForTerms()
    {
        var kit = new ProcedureKit().Procedure("Ended", from: new DateOnly(2000, 1, 1), to: new DateOnly(2001, 1, 1));
        kit.Rule("Ended", "ENDED-INCREMENT", RuleType.TimeIncrement);
        return kit.Build();
    }

    [Fact(DisplayName = "082c-3 must fire: a held-for-terms pre-check puts `warning {code, message}` (the exact approved constants) on the SPA 202")]
    public async Task External_202_CarriesTheProvisionalWarning_WhenTheRecordIsHeldForTerms()
    {
        _h.ProcedureRead = HeldForTerms;

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("submissionId", "jobId", "trackingNumber", "statusUrl", "warning");
        var warning = body.RootElement.GetProperty("warning");
        warning.EnumerateObject().Select(p => p.Name).Should().Equal("code", "message");
        warning.GetProperty("code").GetString().Should().Be("ebilling.submission.procedure_gap");
        warning.GetProperty("message").GetString().Should().Be(
            "As of today, no billing terms are set up for the record you chose. If the invoice's work dates are not covered either, it will be held for review, not rejected.");
        _h.Allocations.Should().ContainSingle("the warning is advisory: the submission is received either way");
    }

    [Fact(DisplayName = "082c-3 must not fire: terms in force give a 202 with no `warning` member at all (the four fields, as before)")]
    public async Task External_202_HasNoWarning_WhenTheRecordIsNotHeld()
    {
        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("submissionId", "jobId", "trackingNumber", "statusUrl");
        _h.PrecheckReads.Should().ContainSingle("the pre-check ran, and judged the record not held");
    }

    [Fact(DisplayName = "082c-3 fail quiet (seed 1): a pre-check fault gives the same 202 without a warning; the submission is received and its job enqueued")]
    public async Task External_202_SurvivesAPrecheckFault_WithNoWarning()
    {
        _h.ProcedureRead = () => throw new InvalidOperationException("SECRET-RECORD-NAME 6f1c0d7e-aaaa-4bbb-8ccc-0123456789ab");

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("warning").And.NotContain("SECRET").And.NotContain("6f1c0d7e");
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("trackingNumber").GetString().Should().Be("INV-1.1");
        _h.Allocations.Should().ContainSingle();
        _h.JobsSent.Should().ContainSingle();
    }

    [Fact(DisplayName = "082c-3 (uac-r2 (a)): a refused submission never runs the pre-check (it runs only after the submission's own authorization passed and it was received)")]
    public async Task External_RefusedSubmission_NeverRunsThePrecheck()
    {
        _h.ProcedureRead = HeldForTerms;
        _resolver.Principal = Collaborator(AccessRights.Read);

        var forbidden = await PostExternalAsync(Form());
        var notPdf = await PostExternalAsync(Form(contentType: "text/plain", pdf: new byte[] { 1, 2, 3 }));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        notPdf.IsSuccessStatusCode.Should().BeFalse();
        (await forbidden.Content.ReadAsStringAsync()).Should().NotContain("warning");
        (await notPdf.Content.ReadAsStringAsync()).Should().NotContain("warning");
        _h.PrecheckReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "082c-3 (uac-r2 (b)): FORM fields naming another record or firm are ignored (the firm is the `?firmId=` query, not a field); the pre-check reads the filter's record and firm")]
    public async Task External_FormFieldsNamingAnotherRecordOrFirm_AreIgnored()
    {
        _h.ProcedureRead = HeldForTerms;
        var otherRecord = Guid.NewGuid();
        var form = Form();
        form.Add(new StringContent(OtherFirmId.ToString()), "firmId");
        form.Add(new StringContent(otherRecord.ToString()), "coreRecordId");
        form.Add(new StringContent(otherRecord.ToString()), "matterId");
        form.Add(new StringContent("sprk_project"), "coreRecordKind");

        var plain = await PostExternalAsync(Form());
        var crafted = await PostExternalAsync(form);

        crafted.StatusCode.Should().Be(plain.StatusCode);
        using var plainBody = JsonDocument.Parse(await plain.Content.ReadAsStringAsync());
        using var craftedBody = JsonDocument.Parse(await crafted.Content.ReadAsStringAsync());
        craftedBody.RootElement.GetProperty("warning").GetRawText().Should().Be(plainBody.RootElement.GetProperty("warning").GetRawText());
        _h.PrecheckReads.Should().HaveCount(2).And.OnlyContain(
            r => r.Core == new CoreRecordReference(CoreRecordKind.Matter, MatterId) && r.FirmId == FirmId,
            "the record and firm come from the authorized scope, never from the body");
    }

    [Fact(DisplayName = "082c-3 (uac-r2 (b), K3): the firm the caller NAMES (the `?firmId=` query) is another firm: 403 before the pre-check, which reads nothing")]
    public async Task External_QueryNamingAnotherFirm_Is403_AndThePrecheckReadsNothing()
    {
        _h.ProcedureRead = HeldForTerms;

        var response = await PostExternalAsync(Form(), $"?firmId={OtherFirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("warning");
        _h.PrecheckReads.Should().BeEmpty("the pre-check runs only for the firm the caller's own authorization passed");
        NothingWritten();
    }

    [Fact(DisplayName = "082c-3 F1 (seed 2): a pre-check read that never completes is cut off at the budget; the 202 goes out with no `warning`, the submission is received and its job enqueued")]
    public async Task External_202_HasNoWarning_WhenThePrecheckReadTimesOut()
    {
        _h.PrecheckTimeLimit = TimeSpan.FromMilliseconds(50);
        // A read that IGNORES its token (as the Dataverse SDK's channel call does): only the wait itself can cut it off.
        _h.ProcedureReadAsync = _ => new TaskCompletionSource<ProcedureResolution>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        var response = await PostExternalAsync(Form()).WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("warning");
        using var body = JsonDocument.Parse(text);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("submissionId", "jobId", "trackingNumber", "statusUrl");
        _h.PrecheckReads.Should().ContainSingle("the pre-check ran and was cut off");
        _h.Allocations.Should().ContainSingle();
        _h.JobsSent.Should().ContainSingle();
    }

    [Fact(DisplayName = "082c-3 scope: the internal route's 202 never carries the warning and never runs the pre-check (the SPA channel only)")]
    public async Task Internal_202_HasNoWarning_AndNeverRunsThePrecheck()
    {
        _h.ProcedureRead = HeldForTerms;
        GrantInternalSubmit();

        var response = await PostInternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("warning");
        _h.PrecheckReads.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: the firm and the core record (NFR-10)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task External_caller_naming_another_firm_is_403_firm_forbidden_and_nothing_is_written()
    {
        var response = await PostExternalAsync(Form(), $"?firmId={OtherFirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        NothingWritten();
    }

    [Fact]
    public async Task External_caller_with_no_firm_is_403_firm_forbidden()
    {
        _participations.Firms = Array.Empty<Guid>();

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        NothingWritten();
    }

    [Fact]
    public async Task External_firms_are_those_of_the_callers_own_contact()
    {
        // Another contact, with collaborate on the same matter, belongs to no firm: the filter reads the caller's memberships, nobody else's.
        _resolver.Principal = Collaborator(AccessRights.Read | AccessRights.Create, contactId: Guid.NewGuid());

        var response = await PostExternalAsync(Form(), $"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        NothingWritten();
    }

    [Fact]
    public async Task External_caller_in_two_firms_must_name_one()
    {
        _participations.Firms = new[] { FirmId, OtherFirmId };

        var unnamed = await PostExternalAsync(Form());
        unnamed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(unnamed)).Should().Be(InvoiceSubmissionErrors.FirmRequired);
        NothingWritten();

        var named = await PostExternalAsync(Form(), $"?firmId={FirmId}");
        named.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _h.Allocations.Should().ContainSingle().Which.FirmId.Should().Be(FirmId);
    }

    [Fact]
    public async Task External_firm_memberships_that_cannot_be_read_are_503_not_a_deny()
    {
        _participations.Unreadable = true;

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        NothingWritten();
    }

    [Theory]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Read)] // View Only
    public async Task External_caller_without_collaborate_on_the_record_is_403_record_forbidden_and_nothing_is_written(AccessRights rights)
    {
        _resolver.Principal = Collaborator(rights);

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.RecordForbidden);
        NothingWritten();
    }

    [Fact]
    public async Task External_caller_cannot_submit_on_a_record_outside_their_set()
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/external/projects/{Guid.NewGuid()}/invoice-submissions", Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.RecordForbidden);
        NothingWritten();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: a systemuser is refused before any write (ebilling-r1 task 051)
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid WorkAssignmentId = Guid.NewGuid();
    private static readonly Guid SystemUserId = Guid.NewGuid();

    /// <summary>The route segment, table and record id of each core record kind the external submit routes accept.</summary>
    public static TheoryData<string> CoreSegments => new() { "matters", "projects", "workassignments" };

    private static (string Entity, Guid Id) CoreOf(string segment) => segment switch
    {
        "matters" => ("sprk_matter", MatterId),
        "projects" => ("sprk_project", ProjectId),
        _ => ("sprk_workassignment", WorkAssignmentId),
    };

    /// <summary>A caller with Read + Create on a matter, a project and a work assignment; a workforce systemuser when <paramref name="systemUser"/> is set.</summary>
    private static CallerPrincipal OnEveryRoot(bool systemUser = false) => new()
    {
        Plane = systemUser ? CallerPrincipalPlane.Workforce : CallerPrincipalPlane.CiamContact,
        ContactId = InvoiceSubmissionServiceTests.ContactId,
        SystemUserId = systemUser ? SystemUserId : null,
        ProjectAccess = new[] { new CallerProjectAccess { ProjectId = ProjectId, Rights = AccessRights.Read | AccessRights.Create } },
        MatterAccess = new Dictionary<Guid, AccessRights> { [MatterId] = AccessRights.Read | AccessRights.Create },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [WorkAssignmentId] = AccessRights.Read | AccessRights.Create },
    };

    private void LinkEveryRecordTo(params Guid[] firms)
    {
        foreach (var segment in new[] { "matters", "projects", "workassignments" })
        {
            _participations.Links[CoreOf(segment)] = firms;
        }
    }

    private Task<HttpResponseMessage> PostToAsync(string segment, string query = "") =>
        SendAsync(HttpMethod.Post, $"/api/v1/external/{segment}/{CoreOf(segment).Id}/invoice-submissions{query}", Form());

    [Theory]
    [MemberData(nameof(CoreSegments))]
    public async Task A_systemuser_is_refused_on_each_submit_route_before_any_read_or_write(string segment)
    {
        // The systemuser holds the rights, belongs (through its linked contact) to the firm, and the firm is linked to the record: only
        // the systemuser check can be what refuses it.
        _resolver.Principal = OnEveryRoot(systemUser: true);
        LinkEveryRecordTo(FirmId);

        var response = await PostToAsync(segment);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.UseInternalRoute);
        NothingWritten();
        _participations.MembershipReads.Should().Be(0, "the refusal comes before the firm is looked up");
        _participations.LinkReads.Should().BeEmpty("the refusal comes before the record's links are looked up");
    }

    [Theory]
    [MemberData(nameof(CoreSegments))]
    public async Task The_same_caller_without_a_systemuser_id_is_accepted_on_each_submit_route(string segment)
    {
        // Control for the systemuser tests above: same rights, same firm, same links; only SystemUserId differs.
        _resolver.Principal = OnEveryRoot(systemUser: false);
        LinkEveryRecordTo(FirmId);

        await AssertPastTheFilterAsync(segment, await PostToAsync(segment));
    }

    /// <summary>
    /// The request got through the route filter and the service's checks: the whole submission ran (202, a row allocated). The shared test service
    /// serves the record of every kind (a matter, a project, a work assignment), so each kind is received.
    /// </summary>
    private async Task AssertPastTheFilterAsync(string segment, HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, $"the {segment} submission is received ({await ErrorCodeAsync(response)})");
        _h.Allocations.Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: the firm must be linked to the record (D-106, ebilling-r1 task 051)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CoreSegments))]
    public async Task A_firm_not_linked_to_the_record_is_403_firm_not_linked_with_the_code_only_and_nothing_is_written(string segment)
    {
        _resolver.Principal = OnEveryRoot();
        LinkEveryRecordTo(OtherFirmId); // the record belongs to a firm the caller is not (and not to the caller's)

        var response = await PostToAsync(segment);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(OtherFirmId.ToString(), "the refusal names no firm").And.NotContain(FirmId.ToString());
        NothingWritten();
    }

    [Fact]
    public async Task A_record_with_no_assigned_firm_is_403_firm_not_linked()
    {
        // Fail closed: a record that references no firm links none.
        _participations.Links.Clear();

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(CoreSegments))]
    public async Task A_firm_linked_to_the_record_is_accepted(string segment)
    {
        _resolver.Principal = OnEveryRoot();
        LinkEveryRecordTo(FirmId, OtherFirmId); // either assigned-firm slot may hold the caller's firm

        await AssertPastTheFilterAsync(segment, await PostToAsync(segment));
    }

    [Fact]
    public async Task A_caller_in_two_firms_may_submit_only_under_the_firm_linked_to_the_record()
    {
        _participations.Firms = new[] { FirmId, OtherFirmId };
        _participations.Links[("sprk_matter", MatterId)] = new[] { OtherFirmId };

        var unlinked = await PostExternalAsync(Form(), $"?firmId={FirmId}");
        unlinked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(unlinked)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        NothingWritten();

        var linked = await PostExternalAsync(Form(), $"?firmId={OtherFirmId}");
        linked.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _h.Allocations.Should().ContainSingle().Which.FirmId.Should().Be(OtherFirmId);
    }

    [Fact]
    public async Task A_link_read_that_cannot_be_completed_is_503_firm_unverifiable_not_a_deny_and_not_a_pass()
    {
        _participations.LinksUnreadable = true;

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        NothingWritten();
    }

    // ── A record that cannot be found keeps its 422; 503 means a real fault (round 3) ──
    // The link reader cannot tell a record that is not returned (deleted) from a fault, so both come back unreadable. The route filter's early
    // refusal therefore fires only on a definite NotLinked: an unreadable answer goes on to the service, which reads the record's own fields
    // first (a missing record is its 422) and only then asks for the link.

    private void RemoveCoreRecord(string segment)
    {
        if (segment == "matters")
        {
            _h.Matter = null;
        }
        else
        {
            _h.OtherCoreRecordsExist = false;
        }
    }

    [Theory]
    [MemberData(nameof(CoreSegments))]
    public async Task External_submit_on_a_record_that_cannot_be_found_is_422_not_503_even_though_its_links_come_back_unreadable(string segment)
    {
        _resolver.Principal = OnEveryRoot();
        _participations.LinksUnreadable = true; // what the reader answers for a deleted record
        RemoveCoreRecord(segment);

        var response = await PostToAsync(segment);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        NothingWritten();
    }

    [Fact]
    public async Task A_real_link_fault_on_an_existing_record_passes_the_route_filter_and_is_503_from_the_service()
    {
        _participations.LinksUnreadable = true;

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        _participations.LinkReads.Should().HaveCount(2, "the filter's early copy let the unreadable answer through and the service read again");
        NothingWritten();
    }

    [Fact]
    public async Task An_unlinked_firm_is_refused_403_by_the_route_filter_early_before_the_service_reads_the_record()
    {
        _participations.Links[("sprk_matter", MatterId)] = new[] { OtherFirmId };

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        _participations.LinkReads.Should().ContainSingle("only the filter's early copy asked; the service was never reached");
        _h.Entities.Verify(
            e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        NothingWritten();
    }

    [Fact]
    public async Task The_link_is_not_read_for_a_caller_who_failed_an_earlier_check()
    {
        _resolver.Principal = Collaborator(AccessRights.Read); // View Only

        var response = await PostExternalAsync(Form());

        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.RecordForbidden);
        _participations.LinkReads.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: a deactivated contact confers no firms (UAC task 137)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task A_deactivated_contact_confers_no_firms_so_the_submit_is_403_firm_forbidden_and_the_list_is_empty()
    {
        _participations.ContactState = ContactRecordState.Inactive;

        var post = await PostExternalAsync(Form());
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(post)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        NothingWritten();
        _participations.MembershipReads.Should().Be(0, "an inactive contact's memberships are not even read");

        var list = await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().Be("[]");
    }

    [Fact]
    public async Task A_contact_whose_state_cannot_be_read_is_503_firm_unverifiable()
    {
        _participations.ContactState = ContactRecordState.Unreadable;

        var response = await PostExternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        NothingWritten();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: the picker's link read, GET /invoice-submissions/firm-records (D-106)
    // ═══════════════════════════════════════════════════════════════════════════

    private Task<HttpResponseMessage> GetFirmRecordsAsync(string query) =>
        SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/firm-records{query}");

    private static async Task<(string[] Matters, string[] Projects, string[] WorkAssignments, bool Partial)> ReadFirmRecordsAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] Ids(string name) => doc.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToArray();
        return (Ids("matters"), Ids("projects"), Ids("workAssignments"), doc.RootElement.GetProperty("partial").GetBoolean());
    }

    [Fact]
    public async Task The_picker_lists_only_the_readable_records_the_firm_is_linked_to_and_a_submit_agrees()
    {
        _resolver.Principal = OnEveryRoot();
        _participations.Links[("sprk_matter", MatterId)] = new[] { FirmId };
        _participations.Links[("sprk_project", ProjectId)] = new[] { OtherFirmId };         // linked to someone else: must not be offered
        _participations.Links[("sprk_workassignment", WorkAssignmentId)] = new[] { OtherFirmId, FirmId };

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (matters, projects, workAssignments, partial) = await ReadFirmRecordsAsync(response);
        matters.Should().Equal(MatterId.ToString());
        projects.Should().BeEmpty("the project is linked to another firm only");
        workAssignments.Should().Equal(WorkAssignmentId.ToString());
        partial.Should().BeFalse();

        // The list and the submit are one rule: what is offered is accepted, what is left out is refused.
        await AssertPastTheFilterAsync("matters", await PostToAsync("matters"));
        await AssertPastTheFilterAsync("workassignments", await PostToAsync("workassignments"));
        var unlisted = await PostToAsync("projects");
        unlisted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(unlisted)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
    }

    [Fact]
    public async Task The_picker_returns_the_intersection_of_what_the_caller_can_read_and_what_the_firm_is_linked_to_never_every_record_of_the_firm()
    {
        // The caller reads one matter, one project and one work assignment. For each kind the firm is ALSO linked to a record the caller has no
        // grant on (a stranger): it must be neither returned nor even looked up. And a record the caller can read but the firm is not linked to
        // is left out. Only the accessible AND linked record of each kind is offered (must-not-fire control inside the same call).
        _resolver.Principal = OnEveryRoot();
        var strangerMatter = Guid.NewGuid();
        var strangerProject = Guid.NewGuid();
        var strangerWorkAssignment = Guid.NewGuid();
        _participations.Links[("sprk_matter", strangerMatter)] = new[] { FirmId };
        _participations.Links[("sprk_project", strangerProject)] = new[] { FirmId };
        _participations.Links[("sprk_workassignment", strangerWorkAssignment)] = new[] { FirmId };
        _participations.Links[("sprk_matter", MatterId)] = new[] { FirmId };                    // readable and linked: offered
        _participations.Links[("sprk_project", ProjectId)] = new[] { FirmId };                  // readable and linked: offered
        _participations.Links[("sprk_workassignment", WorkAssignmentId)] = new[] { OtherFirmId }; // readable, linked to someone else: not offered

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (matters, projects, workAssignments, partial) = await ReadFirmRecordsAsync(response);
        matters.Should().Equal(MatterId.ToString());
        projects.Should().Equal(ProjectId.ToString());
        workAssignments.Should().BeEmpty();
        partial.Should().BeFalse();
        (matters.Concat(projects).Concat(workAssignments)).Should()
            .NotContain(new[] { strangerMatter.ToString(), strangerProject.ToString(), strangerWorkAssignment.ToString() });
        _participations.LinkReads.SelectMany(r => r.Ids).Should()
            .NotContain(new[] { strangerMatter, strangerProject, strangerWorkAssignment }, "a record the caller cannot read is never looked up");
        _participations.LinkReads.SelectMany(r => r.Ids).Should().BeEquivalentTo(new[] { MatterId, ProjectId, WorkAssignmentId });
    }

    [Fact]
    public async Task The_picker_is_bounded_it_returns_at_most_the_cap_per_kind_and_says_the_list_is_truncated()
    {
        const int Cap = ExternalInvoiceSubmissionEndpoints.MaxFirmRecordsPerKind;
        var matters = Enumerable.Range(0, Cap + 25).Select(_ => Guid.NewGuid()).ToArray();
        _resolver.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = InvoiceSubmissionServiceTests.ContactId,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = matters.ToDictionary(id => id, _ => AccessRights.Read | AccessRights.Create),
        };
        foreach (var id in matters)
        {
            _participations.Links[("sprk_matter", id)] = new[] { FirmId };
        }

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("matters").GetArrayLength().Should().Be(Cap);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue("a linked record beyond the cap is not offered, and the list must say so");
        doc.RootElement.GetProperty("partial").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("matters").EnumerateArray().Select(e => Guid.Parse(e.GetString()!)).Should().BeSubsetOf(matters);
    }

    [Fact]
    public async Task The_picker_reads_the_links_of_at_most_the_candidate_cap_per_kind_however_large_the_callers_access_set_is()
    {
        const int Candidates = ExternalInvoiceSubmissionEndpoints.MaxFirmRecordCandidatesPerKind;
        var matters = Enumerable.Range(0, Candidates + 100).Select(_ => Guid.NewGuid()).ToArray();
        _resolver.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = InvoiceSubmissionServiceTests.ContactId,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = matters.ToDictionary(id => id, _ => AccessRights.Read | AccessRights.Create),
        };

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _participations.LinkReads.Where(r => r.Entity == "sprk_matter").SelectMany(r => r.Ids).Should().HaveCount(Candidates);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue("records beyond the examined candidates may be linked");
    }

    [Fact]
    public async Task The_picker_does_not_offer_a_linked_record_the_caller_can_only_read()
    {
        // Read alone is not enough to submit (the submit filter needs Read + Create), so a view-only record is not offered only to be refused later.
        var viewOnlyMatter = Guid.NewGuid();
        _resolver.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = InvoiceSubmissionServiceTests.ContactId,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights>
            {
                [MatterId] = AccessRights.Read | AccessRights.Create,
                [viewOnlyMatter] = AccessRights.Read,
            },
        };
        _participations.Links[("sprk_matter", MatterId)] = new[] { FirmId };
        _participations.Links[("sprk_matter", viewOnlyMatter)] = new[] { FirmId };

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (matters, _, _, _) = await ReadFirmRecordsAsync(response);
        matters.Should().Equal(MatterId.ToString());
        _participations.LinkReads.SelectMany(r => r.Ids).Should().NotContain(viewOnlyMatter, "a record the caller cannot submit on is not even looked up");
    }

    [Fact]
    public async Task A_list_inside_its_bounds_is_not_truncated()
    {
        _resolver.Principal = OnEveryRoot();
        LinkEveryRecordTo(FirmId);

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("matters").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task The_picker_leaves_out_records_whose_links_cannot_be_read_and_says_so()
    {
        _resolver.Principal = OnEveryRoot();
        LinkEveryRecordTo(FirmId);
        _participations.LinksUnreadable = true;

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (matters, projects, workAssignments, partial) = await ReadFirmRecordsAsync(response);
        (matters.Length + projects.Length + workAssignments.Length).Should().Be(0);
        partial.Should().BeTrue();
    }

    [Fact]
    public async Task The_picker_refuses_a_firm_that_is_not_the_callers()
    {
        var response = await GetFirmRecordsAsync($"?firmId={OtherFirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        _participations.LinkReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("?firmId=not-a-guid")]
    [InlineData("?firmId=00000000-0000-0000-0000-000000000000")]
    public async Task The_picker_needs_a_valid_firm_id(string query)
    {
        var response = await GetFirmRecordsAsync(query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmInvalid);
    }

    [Fact]
    public async Task The_picker_refuses_a_systemuser()
    {
        _resolver.Principal = OnEveryRoot(systemUser: true);
        LinkEveryRecordTo(FirmId);

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.UseInternalRoute);
        _participations.LinkReads.Should().BeEmpty();
        _participations.MembershipReads.Should().Be(0, "the refusal is the first thing the route does, as on the submit routes");
    }

    [Fact]
    public async Task The_picker_refuses_a_firm_that_only_another_contact_belongs_to()
    {
        // The firm is the caller's only if the CALLER's contact belongs to it: a contact with no membership is refused for a real firm.
        _resolver.Principal = Collaborator(AccessRights.Read | AccessRights.Create, contactId: Guid.NewGuid());

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        _participations.LinkReads.Should().BeEmpty("a firm that is not the caller's is refused before any record is looked up");
    }

    [Fact]
    public async Task The_picker_refuses_a_firm_a_deactivated_contact_no_longer_confers()
    {
        // The firm is re-checked against the caller's CURRENT memberships on every call (UAC task 137: an inactive contact confers nothing).
        _participations.ContactState = ContactRecordState.Inactive;

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmForbidden);
        _participations.LinkReads.Should().BeEmpty();
    }

    [Fact]
    public async Task The_picker_is_503_when_the_callers_memberships_cannot_be_read()
    {
        _participations.Unreadable = true;

        var response = await GetFirmRecordsAsync($"?firmId={FirmId}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External: payload refusals, authentication, the kill switch
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task External_non_pdf_is_400_not_pdf()
    {
        var response = await PostExternalAsync(Form(pdf: "MZ"u8.ToArray(), contentType: "application/octet-stream"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.NotPdf);
        NothingWritten();
    }

    [Fact]
    public async Task External_file_above_the_configured_limit_is_413_too_large()
    {
        var response = await PostExternalAsync(Form(pdf: InvoiceSubmissionServiceTests.Pdf(1024 * 1024 + 10)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.TooLarge);
        NothingWritten();
    }

    [Fact]
    public async Task External_missing_firm_invoice_number_is_400()
    {
        var response = await PostExternalAsync(Form(number: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmInvoiceNumberRequired);
        NothingWritten();
    }

    [Theory]
    [InlineData("POST", "/api/v1/external/matters/{0}/invoice-submissions")]
    [InlineData("GET", "/api/v1/external/invoice-submissions/{0}")]
    [InlineData("GET", "/api/v1/external/invoice-submissions")]
    [InlineData("GET", "/api/v1/external/invoice-submissions/firm-records?firmId={1}")]
    [InlineData("POST", "/api/finance/matters/{0}/invoice-submissions?firmId={1}")]
    [InlineData("GET", "/api/finance/invoice-submissions/{0}")]
    public async Task Every_route_is_401_unauthenticated(string method, string route)
    {
        var response = await SendAsync(new HttpMethod(method), string.Format(route, MatterId, FirmId),
            method == "POST" ? Form() : null, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        NothingWritten();
    }

    [Theory]
    [InlineData("POST", "/api/v1/external/matters/{0}/invoice-submissions")]
    [InlineData("GET", "/api/v1/external/invoice-submissions/{1}")]
    [InlineData("GET", "/api/v1/external/invoice-submissions")]
    [InlineData("GET", "/api/v1/external/invoice-submissions/firm-records?firmId={2}")]
    [InlineData("POST", "/api/finance/matters/{0}/invoice-submissions?firmId={2}")]
    [InlineData("GET", "/api/finance/invoice-submissions/{1}")]
    public async Task Every_route_is_503_ebilling_disabled_when_the_flag_is_off(string method, string route)
    {
        _h.Options.Enabled = false;
        GrantInternalSubmit();
        var submissionId = Guid.NewGuid();
        StoredSubmission(submissionId, FirmId);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, submissionId, AccessRights.Read);

        var response = await SendAsync(new HttpMethod(method), string.Format(route, MatterId, submissionId, FirmId),
            method == "POST" ? Form() : null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(EbillingEnabledFilter.ErrorCode);
        NothingWritten();
    }

    [Fact]
    public async Task The_kill_switch_runs_after_authorization_so_an_unauthorized_caller_is_denied_not_told_503()
    {
        _h.Options.Enabled = false;
        _resolver.Principal = Collaborator(AccessRights.None);

        var external = await PostExternalAsync(Form());
        var internalPost = await PostInternalAsync(Form()); // no grants

        external.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        internalPost.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        NothingWritten();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // External reads: own firm and readable record only, else one 404
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task External_get_of_own_firms_submission_is_200()
    {
        var id = Guid.NewGuid();
        StoredSubmission(id, FirmId);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("trackingNumber").GetString().Should().Be("INV-1.1");
        body.RootElement.GetProperty("status").GetString().Should().Be("Received");
    }

    [Fact(DisplayName = "095a (uac-r2): a submission that carries sprk_sourceemaildocument is answered to an external caller without it (single read and list), and without the .eml id")]
    public async Task External_get_and_list_never_carry_the_source_email_link()
    {
        var eml = Guid.NewGuid();
        var id = Guid.NewGuid();
        var row = StoredSubmission(id, FirmId);
        row[InvoiceSubmissionService.SourceEmailDocumentColumn] = new EntityReference("sprk_document", eml);

        var single = await (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}")).Content.ReadAsStringAsync();
        var listed = SubmissionRow("INV-LIST", Spa);
        listed[InvoiceSubmissionService.SourceEmailDocumentColumn] = new EntityReference("sprk_document", eml);
        ListReturns(listed);
        var list = await (await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions")).Content.ReadAsStringAsync();

        // Control (must fire): the rows ARE answered, so the absence below is not an empty body.
        single.Should().Contain("INV-1.1");
        list.Should().Contain("INV-LIST");
        foreach (var text in new[] { single, list })
        {
            text.ToLowerInvariant().Should().NotContain("sourceemail");
            text.Should().NotContain(eml.ToString());
        }
    }

    [Fact]
    public async Task External_get_of_another_firms_submission_is_the_same_404_as_a_missing_one()
    {
        var foreign = Guid.NewGuid();
        StoredSubmission(foreign, OtherFirmId);
        var foreignResponse = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{foreign}");

        _h.Submission = null;
        var missingResponse = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{Guid.NewGuid()}");

        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreignResponse.Content.ReadAsStringAsync()).Should().NotContain("INV-1.1");
    }

    // ── ebilling-r1 task 095c (P2 precondition, uac-r2): sprk_invoicesubmission.sprk_vendororg may become blank for internal from-email
    //    rows. The partner read routes must FAIL CLOSED on a submission with no firm: never read, listed or named. ──

    /// <summary><paramref name="row"/> with no firm (<c>sprk_vendororg</c> never written, as a blank-able internal row would read).</summary>
    private static Entity WithoutFirm(Entity row)
    {
        row.Attributes.Remove("sprk_vendororg").Should().BeTrue("the row must have carried a firm for this to be a blank-firm row");
        return row;
    }

    [Fact(DisplayName = "095c A1 (uac-r2): the single read of a submission with no firm is the same 404 as a missing one, naming no field of the row; the caller's own firm's submission is still 200")]
    public async Task External_get_of_a_submission_with_no_firm_is_the_same_404_as_a_missing_one()
    {
        var blank = Guid.NewGuid();
        var row = WithoutFirm(StoredSubmission(blank, FirmId)); // the caller can otherwise reach it: the matter is readable and linked to their firm

        await ReadIsTheSameAsMissingAsync(row, "a submission with no firm belongs to none of the caller's firms");
        _h.Submission = row;
        var body = await (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{blank}")).Content.ReadAsStringAsync();
        body.Should().NotContain("INV-1.1").And.NotContain("trackingNumber").And.NotContain(MatterId.ToString());

        // Control (must fire): the same row WITH the caller's firm is read, so the 404 above is the missing firm and nothing else.
        row["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId);
        (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{blank}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The list's Dataverse double: answers a <c>sprk_invoicesubmission</c> query that names the firm column from <paramref name="store"/>,
    /// EVALUATING every <c>In</c> condition of the query against the row's attributes (an entity reference by its id; an absent or null
    /// attribute matches nothing), as Dataverse does. Every such query is recorded.
    /// </summary>
    private List<Microsoft.Xrm.Sdk.Query.QueryExpression> ListIsEvaluated(params Entity[] store)
    {
        var queries = new List<Microsoft.Xrm.Sdk.Query.QueryExpression>();
        _h.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_invoicesubmission"
                    && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_vendororg")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) =>
            {
                lock (queries) queries.Add(q);
                return new EntityCollection(store.Where(row => q.Criteria.Conditions
                    .Where(c => c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.In)
                    .All(c => row.Attributes.TryGetValue(c.AttributeName, out var value)
                        && value is EntityReference reference
                        && c.Values.Contains(reference.Id))).ToList());
            });
        return queries;
    }

    [Fact(DisplayName = "095c A2 (uac-r2): the list never includes a submission with no firm (the query is firm In [caller's firms] with no OR/null branch, and the evaluated result keeps only the firm's row)")]
    public async Task External_list_excludes_a_submission_with_no_firm()
    {
        var inFirm = SubmissionRow("INV-FIRM", Spa);
        var blank = WithoutFirm(SubmissionRow("INV-BLANK", Spa));
        var otherFirm = SubmissionRow("INV-OTHER", Spa);
        otherFirm["sprk_vendororg"] = new EntityReference("sprk_organization", OtherFirmId);
        var queries = ListIsEvaluated(blank, inFirm, otherFirm);

        var response = await SendAsync(HttpMethod.Get, ExternalRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("INV-FIRM", "positive control: the caller's firm's row is listed")
            .And.NotContain("INV-BLANK").And.NotContain("INV-OTHER");

        // Query level: the firm condition is a plain conjunct, an In over exactly the caller's firms, with no nested or alternative branch.
        queries.Should().NotBeEmpty();
        foreach (var query in queries)
        {
            query.Criteria.FilterOperator.Should().Be(Microsoft.Xrm.Sdk.Query.LogicalOperator.And);
            query.Criteria.Filters.Should().BeEmpty("no OR / null branch can widen the firm condition");
            var firm = query.Criteria.Conditions.Where(c => c.AttributeName == "sprk_vendororg").Should().ContainSingle().Subject;
            firm.Operator.Should().Be(Microsoft.Xrm.Sdk.Query.ConditionOperator.In);
            firm.Values.Cast<Guid>().Should().BeEquivalentTo(new[] { FirmId });
        }
    }

    [Theory(DisplayName = "098 (uac-r2): even if the store returned a submission with no firm, the registered seam withholds it from the list, whatever its channel and document")]
    [InlineData("spa")]
    [InlineData("internal-with-document")]
    public async Task External_list_withholds_a_returned_submission_with_no_firm(string kind)
    {
        var document = _world.Document(Guid.NewGuid(), permission: StandardPermission);
        // The firmless Internal row has its own readable document (its id is derived from it), so only the firm check can withhold it.
        var blankDocument = _world.Document(Guid.NewGuid(), permission: StandardPermission);
        var blank = kind == "spa" ? SubmissionRow("INV-BLANK", Spa) : SubmissionRow("INV-BLANK", Internal, blankDocument);
        WithoutFirm(blank);
        var control = kind == "spa" ? SubmissionRow("INV-CONTROL", Spa) : SubmissionRow("INV-CONTROL", Internal, document);

        var body = await ListBodyAsync(blank, control); // ListReturns does not filter: the withholding is the seam's

        body.Should().Contain("INV-CONTROL", "the same shape with a firm is listed").And.NotContain("INV-BLANK");
    }

    [Fact(DisplayName = "095c A2 (uac-r2): a caller with no firm is answered an empty list and no submission query is issued")]
    public async Task External_list_of_a_caller_with_no_firm_issues_no_query()
    {
        _participations.Firms = Array.Empty<Guid>();
        var queries = ListIsEvaluated(SubmissionRow("INV-FIRM", Spa), WithoutFirm(SubmissionRow("INV-BLANK", Spa)));

        var response = await SendAsync(HttpMethod.Get, ExternalRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[]");
        queries.Should().BeEmpty("an empty firm set reads nothing: the scope IS the query");
    }

    [Fact(DisplayName = "095c A4 (uac-r2): a blank firm id never reaches the list query (hygiene), and a set of only blanks reads nothing")]
    public async Task External_list_drops_a_blank_firm_id_before_the_query()
    {
        _participations.Firms = new[] { Guid.Empty, FirmId };
        var queries = ListIsEvaluated(SubmissionRow("INV-FIRM", Spa));
        (await SendAsync(HttpMethod.Get, ExternalRoute)).StatusCode.Should().Be(HttpStatusCode.OK);

        queries.Should().NotBeEmpty();
        queries.SelectMany(q => q.Criteria.Conditions.Where(c => c.AttributeName == "sprk_vendororg"))
            .Should().OnlyContain(c => c.Values.Cast<Guid>().SequenceEqual(new[] { FirmId }), "Guid.Empty is not a firm");

        _participations.Firms = new[] { Guid.Empty };
        queries.Clear();
        var blankOnly = await SendAsync(HttpMethod.Get, ExternalRoute);
        blankOnly.StatusCode.Should().Be(HttpStatusCode.OK);
        (await blankOnly.Content.ReadAsStringAsync()).Should().Be("[]");
        queries.Should().BeEmpty("a firm set of only Guid.Empty is an empty firm set");
    }

    [Fact(DisplayName = "095c A3 (uac-r2): the replaced-by-correction note never names a correcting submission with no firm; one of the caller's firm is named")]
    public async Task External_ReplacementWithNoFirm_IsNotNamed()
    {
        SeedSuperseded();
        _h.PriorSubmissions.Clear();
        AddReplacement(NewSubmissionId, NewInvoiceId, InvoiceStatusCodes.InReview, "INV-1.2");
        WithoutFirm(_h.PriorSubmissions.Single());

        var (blank, text) = await ReadAsync(ExternalRoute, OldSubmissionId);

        blank.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection, "the old submission still answers that it was replaced");
        text.Should().NotContain("replacedBy").And.NotContain("INV-1.2").And.NotContain(NewSubmissionId.ToString());

        _h.PriorSubmissions.Clear();
        AddReplacement(NewSubmissionId, NewInvoiceId, InvoiceStatusCodes.InReview, "INV-1.2");
        (await ReadAsync(ExternalRoute, OldSubmissionId)).Body.GetProperty("replacedBy").GetProperty("submissionId").GetGuid()
            .Should().Be(NewSubmissionId, "control: the same replacement is named once it carries the caller's firm");
    }

    [Fact]
    public async Task External_get_of_own_firms_submission_on_a_record_the_caller_cannot_read_is_404()
    {
        _resolver.Principal = Collaborator(AccessRights.None);
        var id = Guid.NewGuid();
        StoredSubmission(id, FirmId);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task External_reads_that_fault_are_503_record_unavailable_not_a_500_or_a_404()
    {
        _h.Entities.Setup(e => e.RetrieveAsync("sprk_invoicesubmission", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));
        _h.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));

        var one = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{Guid.NewGuid()}");
        var list = await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions");

        one.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(one)).Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        list.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(list)).Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 098 surface (c): the submission read and list, decided by ONE seam (review round 4b): a submission with an
    // invoice by the invoice predicate, one without by its own source. The seam is the real ExternalDataService over InvoiceWorld.
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Guid HiddenSubmission = Guid.Parse("09800000-0000-4000-8000-00000000c0df");
    private static readonly Guid HiddenInvoice = InvoiceSubmissionPersister.InvoiceIdFor(HiddenSubmission); // its own invoice (task 165c)
    private static readonly Guid FlaggedSystemUser = Guid.Parse("09800000-0000-4000-8000-0000000000f2");
    private const int RestrictedPermission = 100000002;
    private const int StandardPermission = 100000000;

    /// <summary>The identity the seam asks about a systemuser caller: the test's systemuser is flagged external.</summary>
    private Mock<Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver> SubmissionIdentity { get; } = FlaggedIsExternal();

    private static Mock<Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver> FlaggedIsExternal()
    {
        var identity = new Mock<Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver>(MockBehavior.Strict);
        identity.Setup(i => i.IsExternalAsync(FlaggedSystemUser, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return identity;
    }

    public static TheoryData<string> ExternalCallerKinds => new() { "contact", "flagged-systemuser" };

    /// <summary>The caller of the external routes: a CIAM contact, or a flagged-external systemuser linked to the same contact.</summary>
    private void CallerIs(string kind) => _resolver.Principal = kind switch
    {
        "contact" => Collaborator(AccessRights.Read | AccessRights.Create | AccessRights.Write),
        "flagged-systemuser" => new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = InvoiceSubmissionServiceTests.ContactId,
            SystemUserId = FlaggedSystemUser,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterId] = AccessRights.Read | AccessRights.Create | AccessRights.Write },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>A submission row of the caller's firm on the matter, as <c>ToView</c> reads it.</summary>
    private static Entity SubmissionRow(
        string number, int? channel, Guid? document = null, Guid? invoice = null,
        InvoiceSubmissionStatus status = InvoiceSubmissionStatus.Received, Guid? id = null)
    {
        // Task 165f: a row naming a document is, by default, the submission derived from it (as the confirm route creates it).
        var row = new Entity("sprk_invoicesubmission",
            id ?? (document is { } own ? InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(own) : Guid.NewGuid()))
        {
            ["sprk_trackingnumber"] = number,
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
        };
        if (channel is { } c) row["sprk_channel"] = new OptionSetValue(c);
        if (document is { } d) row["sprk_document"] = new EntityReference("sprk_document", d);
        if (invoice is { } i) row[InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", i);
        return row;
    }

    private const int Spa = (int)InvoiceSubmissionChannel.Spa;
    private const int Internal = (int)InvoiceSubmissionChannel.Internal;

    private Entity StoredSubmissionWithInvoice(Guid id, Guid invoice)
    {
        var row = StoredSubmission(id, FirmId);
        row[InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", invoice);
        return row;
    }

    private void ListReturns(params Entity[] rows) =>
        _h.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_invoicesubmission"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    private static string WithoutCorrelation(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"-\"");

    /// <summary>GET of <paramref name="row"/>, asserted to be the SAME 404 (status and body) as a missing submission.</summary>
    private async Task ReadIsTheSameAsMissingAsync(Entity row, string because)
    {
        _h.Submission = row;
        var withheld = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{row.Id}");
        _h.Submission = null;
        var missing = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{Guid.NewGuid()}");

        withheld.StatusCode.Should().Be(HttpStatusCode.NotFound, because);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        WithoutCorrelation(await withheld.Content.ReadAsStringAsync()).Should().Be(
            WithoutCorrelation(await missing.Content.ReadAsStringAsync()),
            "a withheld submission and a missing submission are indistinguishable (only the per-request id differs)");
    }

    private async Task<HttpStatusCode> ReadStatusAsync(Entity row)
    {
        _h.Submission = row;
        return (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{row.Id}")).StatusCode;
    }

    private async Task<string> ListBodyAsync(params Entity[] rows)
    {
        ListReturns(rows);
        var response = await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    // ── With an invoice: the invoice predicate decides (as before review round 4b) ──

    [Fact]
    public async Task Task098_get_of_a_submission_whose_invoice_is_hidden_is_the_same_404_as_a_missing_one()
    {
        _world.Source(HiddenInvoice, permission: RestrictedPermission);
        var id = HiddenSubmission;

        await ReadIsTheSameAsMissingAsync(StoredSubmissionWithInvoice(id, HiddenInvoice), "its invoice is hidden");

        _submissionDecisions.Should().ContainSingle().Which.Should().ContainSingle()
            .Which.InvoiceId.Should().Be(HiddenInvoice);
    }

    [Fact]
    public async Task Task098_get_of_a_submission_when_no_predicate_is_registered_is_404_fail_closed()
    {
        await DisposeAsync();
        _registerSubmissionHiding = false;
        await InitializeAsync();

        var id = Guid.NewGuid();
        StoredSubmissionWithInvoice(id, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- EB-D-169 (owner, 2026-10-28): a read that fails while deciding is a retryable 503 on both external routes --

    /// <summary>The retryable 503 every submission read fault answers (<c>ebilling.submission.record_unavailable</c>).</summary>
    private static async Task ShouldBeRetryable503Async(HttpResponseMessage response, params string[] mustNotContain)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString()
            .Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        foreach (var text in mustNotContain)
            body.Should().NotContain(text, "a faulted read never shows a row");
    }

    [Fact]
    public async Task EB_D_169_get_of_a_submission_whose_invoice_decision_read_faults_is_503_not_404()
    {
        _world.Faults["sprk_storedfiles"] = HttpStatusCode.ServiceUnavailable;
        var id = Guid.NewGuid();
        StoredSubmissionWithInvoice(id, InvoiceSubmissionPersister.InvoiceIdFor(id));

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}");

        await ShouldBeRetryable503Async(response, "trackingNumber");
    }

    [Fact]
    public async Task Task098_get_of_a_submission_whose_invoice_is_visible_or_absent_is_200_as_before()
    {
        var withInvoice = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(withInvoice);
        _world.SpaUpload(invoice, withInvoice);
        StoredSubmissionWithInvoice(withInvoice, invoice);
        (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{withInvoice}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var withoutInvoice = Guid.NewGuid();
        StoredSubmission(withoutInvoice, FirmId);
        (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{withoutInvoice}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Task098_get_never_returns_the_invoice_id()
    {
        var id = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(id);
        StoredSubmissionWithInvoice(id, invoice);

        var body = await (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}")).Content.ReadAsStringAsync();

        body.Should().NotContain(invoice.ToString(), "the invoice link is read for the predicate only");
    }

    [Fact]
    public async Task Task098_list_omits_a_submission_whose_invoice_is_hidden_and_keeps_the_others_in_one_decision()
    {
        _world.Source(HiddenInvoice, permission: RestrictedPermission);

        var body = await ListBodyAsync(
            SubmissionRow("INV-HIDDEN", Spa, invoice: HiddenInvoice, status: InvoiceSubmissionStatus.Accepted, id: HiddenSubmission),
            SubmissionRow("INV-PENDING", Spa));

        body.Should().Contain("INV-PENDING").And.NotContain("INV-HIDDEN");
        _submissionDecisions.Should().ContainSingle("one decision for the page").Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task Task098_list_keeps_a_submission_whose_invoice_is_visible()
    {
        var id = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(id);
        _world.SpaUpload(invoice, id);

        (await ListBodyAsync(SubmissionRow("INV-VISIBLE", Spa, invoice: invoice, status: InvoiceSubmissionStatus.Accepted, id: id)))
            .Should().Contain("INV-VISIBLE");
    }

    // ── ebilling-r1 task 130-0 fix round 1: a submission follows its invoice (a partner sees only invoices of its own firms) ──

    /// <summary>A submission of the caller's own firm on an invoice whose own source is visible; the invoice is <paramref name="invoiceFirm"/>'s.</summary>
    private (Guid Submission, Guid Invoice) OwnFirmSubmissionOn(Guid? invoiceFirm)
    {
        var id = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(id);
        _world.SpaUpload(invoice, id);
        if (invoiceFirm is { } firm)
            _world.InvoiceFirm(invoice, firm);
        return (id, invoice);
    }

    [Fact(DisplayName = "130-0 fix1 submission read (must fire): the caller's firm's submission on another firm's invoice is the same 404 as a missing one")]
    public async Task Task130_0_get_of_an_own_firm_submission_whose_invoice_is_another_firms_is_the_same_404_as_a_missing_one()
    {
        var (id, invoice) = OwnFirmSubmissionOn(OtherFirmId);

        await ReadIsTheSameAsMissingAsync(StoredSubmissionWithInvoice(id, invoice), "its invoice is another firm's");

        _invoiceDecisions.Should().ContainSingle("the missing submission never reaches the invoice decision")
            .Which.Should().Equal(invoice);
    }

    [Fact(DisplayName = "130-0 fix1 submission list (must fire): the caller's firm's submission on another firm's invoice is absent; the others are listed, in one invoice decision")]
    public async Task Task130_0_list_omits_an_own_firm_submission_whose_invoice_is_another_firms()
    {
        var (other, otherInvoice) = OwnFirmSubmissionOn(OtherFirmId);
        var (own, ownInvoice) = OwnFirmSubmissionOn(invoiceFirm: null);

        var body = await ListBodyAsync(
            SubmissionRow("INV-OTHER-FIRM", Spa, invoice: otherInvoice, status: InvoiceSubmissionStatus.Accepted, id: other),
            SubmissionRow("INV-OWN-FIRM", Spa, invoice: ownInvoice, status: InvoiceSubmissionStatus.Accepted, id: own),
            SubmissionRow("INV-PENDING", Spa));

        body.Should().Contain("INV-OWN-FIRM").And.Contain("INV-PENDING").And.NotContain("INV-OTHER-FIRM");
        _invoiceDecisions.Should().ContainSingle("one invoice decision for the page")
            .Which.Should().BeEquivalentTo(new[] { otherInvoice, ownInvoice });
    }

    [Fact(DisplayName = "130-0 fix1 submission read and list (must not fire): the caller's firm's submission on its own firm's invoice is served")]
    public async Task Task130_0_an_own_firm_submission_on_an_own_firm_invoice_is_served_on_the_read_and_the_list()
    {
        var (id, invoice) = OwnFirmSubmissionOn(invoiceFirm: null);

        (await ReadStatusAsync(StoredSubmissionWithInvoice(id, invoice))).Should().Be(HttpStatusCode.OK);
        (await ListBodyAsync(SubmissionRow("INV-OWN-FIRM", Spa, invoice: invoice, status: InvoiceSubmissionStatus.Accepted, id: id)))
            .Should().Contain("INV-OWN-FIRM");
        _invoiceDecisions.Should().HaveCount(2).And.OnlyContain(call => call.SequenceEqual(new[] { invoice }));
    }

    [Fact(DisplayName = "130-0 fix1 submission read and list (fault): the caller's firms cannot be read: a retryable 503, never a row; a missing submission is still the 404")]
    public async Task Task130_0_when_the_firm_read_fails_the_read_and_the_list_are_503_and_a_missing_submission_is_404()
    {
        var (id, invoice) = OwnFirmSubmissionOn(invoiceFirm: null);
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");

        _h.Submission = StoredSubmissionWithInvoice(id, invoice);
        await ShouldBeRetryable503Async(await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}"), "trackingNumber");
        _h.Submission = null;
        (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the record check comes first: the 503 is no existence oracle");

        ListReturns(SubmissionRow("INV-OWN-FIRM", Spa, invoice: invoice, status: InvoiceSubmissionStatus.Accepted, id: id));
        await ShouldBeRetryable503Async(await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions"), "INV-OWN-FIRM");
    }

    // ── Without an invoice: the submission's own source decides (review round 4b), on BOTH routes ──

    public static TheoryData<string, InvoiceSubmissionStatus> CallerKindsAtStatusesWithoutAnInvoice()
    {
        var data = new TheoryData<string, InvoiceSubmissionStatus>();
        foreach (var kind in new[] { "contact", "flagged-systemuser" })
        {
            foreach (var status in new[] { InvoiceSubmissionStatus.Received, InvoiceSubmissionStatus.Processing, InvoiceSubmissionStatus.Failed })
                data.Add(kind, status);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CallerKindsAtStatusesWithoutAnInvoice))]
    public async Task Task098_4b_an_isolated_submission_document_without_an_invoice_is_withheld_on_the_read_and_the_list(
        string caller, InvoiceSubmissionStatus status)
    {
        CallerIs(caller);
        var isolated = _world.Document(Guid.NewGuid(), permission: RestrictedPermission);

        await ReadIsTheSameAsMissingAsync(
            SubmissionRow("INV-ISOLATED", Internal, isolated, status: status), "its own document is isolated");
        var body = await ListBodyAsync(SubmissionRow("INV-ISOLATED", Internal, isolated, status: status), SubmissionRow("INV-SPA", Spa));

        body.Should().Contain("INV-SPA").And.NotContain("INV-ISOLATED");
    }

    [Theory]
    [MemberData(nameof(ExternalCallerKinds))]
    public async Task Task098_4b_without_an_invoice_an_internal_empty_link_or_an_unknown_channel_is_withheld_on_the_read_and_the_list(
        string caller)
    {
        CallerIs(caller);

        await ReadIsTheSameAsMissingAsync(SubmissionRow("INV-EMPTY", Internal), "an Internal submission with no source to decide by");
        await ReadIsTheSameAsMissingAsync(SubmissionRow("INV-UNKNOWN", channel: null), "an unknown channel");
        var body = await ListBodyAsync(SubmissionRow("INV-EMPTY", Internal), SubmissionRow("INV-UNKNOWN", 999), SubmissionRow("INV-SPA", Spa));

        body.Should().Contain("INV-SPA").And.NotContain("INV-EMPTY").And.NotContain("INV-UNKNOWN");
    }

    [Theory]
    [MemberData(nameof(ExternalCallerKinds))]
    public async Task Task098_4b_without_an_invoice_spa_and_a_readable_internal_document_are_visible_on_the_read_and_the_list(string caller)
    {
        CallerIs(caller);
        var readable = _world.Document(Guid.NewGuid(), permission: StandardPermission);
        var spa = SubmissionRow("INV-SPA", Spa);
        var internalReadable = SubmissionRow("INV-READABLE", Internal, readable, status: InvoiceSubmissionStatus.Processing);

        (await ReadStatusAsync(spa)).Should().Be(HttpStatusCode.OK);
        (await ReadStatusAsync(internalReadable)).Should().Be(HttpStatusCode.OK);
        var body = await ListBodyAsync(spa, internalReadable);

        body.Should().Contain("INV-SPA").And.Contain("INV-READABLE");
    }

    [Theory]
    [MemberData(nameof(ExternalCallerKinds))]
    public async Task EB_D_169_a_failed_source_read_is_503_on_the_read_and_the_list(string caller)
    {
        CallerIs(caller);
        var readable = _world.Document(Guid.NewGuid(), permission: StandardPermission);
        _world.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;

        var row = SubmissionRow("INV-READABLE", Internal, readable);
        _h.Submission = row;
        await ShouldBeRetryable503Async(
            await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{row.Id}"), "INV-READABLE");
        ListReturns(SubmissionRow("INV-READABLE", Internal, readable), SubmissionRow("INV-SPA", Spa));
        await ShouldBeRetryable503Async(
            await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions"), "INV-READABLE", "INV-SPA");
    }

    [Fact]
    public async Task EB_D_169_list_is_503_when_the_invoice_read_fails()
    {
        _world.Faults["sprk_storedfiles"] = HttpStatusCode.ServiceUnavailable;
        var with = Guid.NewGuid();
        ListReturns(
            SubmissionRow("INV-WITH", Spa, invoice: InvoiceSubmissionPersister.InvoiceIdFor(with), status: InvoiceSubmissionStatus.Accepted, id: with),
            SubmissionRow("INV-PENDING", Spa));

        await ShouldBeRetryable503Async(
            await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions"), "INV-WITH", "INV-PENDING");
    }

    [Theory]
    [MemberData(nameof(ExternalCallerKinds))]
    public async Task EB_D_169_without_a_fault_a_withheld_submission_is_still_the_404_and_omitted_from_the_list(string caller)
    {
        CallerIs(caller);
        var isolated = _world.Document(Guid.NewGuid(), permission: RestrictedPermission);

        await ReadIsTheSameAsMissingAsync(SubmissionRow("INV-ISOLATED", Internal, isolated), "decided as withheld, not faulted");
        var body = await ListBodyAsync(SubmissionRow("INV-ISOLATED", Internal, isolated), SubmissionRow("INV-SPA", Spa));

        body.Should().Contain("INV-SPA").And.NotContain("INV-ISOLATED");
    }

    /// <summary>Review round 5 (F3): a missing seam is a registration defect: logged at Error, and every row withheld.</summary>
    [Fact]
    public async Task Task098_R5_no_registered_seam_withholds_every_submission_and_logs_at_error()
    {
        var logger = new LevelLogger();
        var views = new[] { new InvoiceSubmissionView { SubmissionId = Guid.NewGuid(), ChannelValue = Spa } };

        var visible = await ExternalInvoiceSubmissionEndpoints.VisibleSubmissionsAsync(
            Collaborator(AccessRights.Read), views, hideSubmissions: null, logger, CancellationToken.None);

        visible.Should().BeEmpty();
        logger.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Error);
    }

    private sealed class LevelLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    [Fact]
    public async Task Task098_4b_the_external_routes_never_return_the_document_id_and_the_internal_route_still_does()
    {
        var readable = _world.Document(Guid.NewGuid(), permission: StandardPermission);
        var row = SubmissionRow("INV-READABLE", Internal, readable);

        _h.Submission = row;
        var one = await (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{row.Id}")).Content.ReadAsStringAsync();
        var list = await ListBodyAsync(row);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, row.Id, AccessRights.Read);
        var internalResponse = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{row.Id}");

        one.Should().Contain("INV-READABLE").And.NotContain(readable.ToString()).And.NotContain("documentId");
        list.Should().Contain("INV-READABLE").And.NotContain(readable.ToString()).And.NotContain("documentId");
        internalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var internalBody = JsonDocument.Parse(await internalResponse.Content.ReadAsStringAsync());
        internalBody.RootElement.GetProperty("documentId").GetGuid().Should().Be(readable, "the internal route's contract is unchanged");
        internalBody.RootElement.TryGetProperty("channelValue", out _).Should().BeFalse("the raw channel is server-side only");
    }

    [Fact]
    public async Task External_list_is_200_and_empty_for_a_caller_with_no_firm()
    {
        _participations.Firms = Array.Empty<Guid>();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[]");
        _h.Entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal routes: the firm must be linked to the record too (D-110: the rule holds on EVERY channel)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The internal route segment of each core record kind, and the Finance entity set the route authorizes the record against.</summary>
    public static TheoryData<string, string> InternalSegments => new()
    {
        { "matters", FinanceAuthorizationFilter.MatterEntitySet },
        { "projects", FinanceAuthorizationFilter.ProjectEntitySet },
        { "workassignments", FinanceAuthorizationFilter.WorkAssignmentEntitySet },
    };

    private Task<HttpResponseMessage> PostInternalToAsync(string segment, string set, Guid recordId)
    {
        // The caller holds exactly what the internal route needs: Read + AppendTo on the record, AppendTo on the firm.
        _access.Grant(set, recordId, AccessRights.Read | AccessRights.AppendTo);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);
        return SendAsync(HttpMethod.Post, $"/api/finance/{segment}/{recordId}/invoice-submissions?firmId={FirmId}", Form());
    }

    [Theory]
    [MemberData(nameof(InternalSegments))]
    public async Task Internal_caller_filing_under_a_firm_not_linked_to_the_record_is_403_firm_not_linked_and_nothing_is_written(string segment, string set)
    {
        // The internal user holds every right the route asks for, and the firm is active: only the link can be what refuses it.
        var (entity, id) = CoreOf(segment);
        _participations.Links[(entity, id)] = new[] { OtherFirmId };

        var response = await PostInternalToAsync(segment, set, id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(OtherFirmId.ToString()).And.NotContain(FirmId.ToString());
        NothingWritten();
        _h.OwnerContexts.Should().BeEmpty("the refusal comes before the owner is resolved");
    }

    [Theory]
    [MemberData(nameof(InternalSegments))]
    public async Task Internal_caller_filing_under_a_firm_linked_to_the_record_is_not_refused_by_the_link(string segment, string set)
    {
        // The control for the refusal above: same caller, same rights, same firm; only the link differs.
        var (entity, id) = CoreOf(segment);
        _participations.Links[(entity, id)] = new[] { OtherFirmId, FirmId };

        var response = await PostInternalToAsync(segment, set, id);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _h.Allocations.Should().ContainSingle().Which.FirmId.Should().Be(FirmId);
    }

    [Fact]
    public async Task Internal_caller_with_a_record_whose_links_cannot_be_read_is_503_firm_unverifiable_and_nothing_is_written()
    {
        GrantInternalSubmit();
        _participations.LinksUnreadable = true;

        var response = await PostInternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(InternalSegments))]
    public async Task Internal_submit_on_a_record_that_cannot_be_found_is_422_not_503_even_though_its_links_come_back_unreadable(string segment, string set)
    {
        var (_, id) = CoreOf(segment);
        _participations.LinksUnreadable = true; // what the reader answers for a deleted record
        RemoveCoreRecord(segment);

        var response = await PostInternalToAsync(segment, set, id);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        _participations.LinkReads.Should().BeEmpty("the record's own fields are read first; a missing record never reaches the link read");
        NothingWritten();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 0 (ebilling-r1 task 099; section 5.3): internal routes refuse flagged-external callers; external routes are unchanged
    // ═══════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> SubmitRoutesRefused =>
        StatedCaller.Cross(InternalSegments.Select(row => row), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> SubmitRoutesInternal =>
        StatedCaller.Cross(InternalSegments.Select(row => row), StatedCaller.InternalKinds);

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "Task 099 must-fire: every internal submit route refuses a flagged-external, row-less or unreadable caller with 403, before any rights call, read or write")]
    [MemberData(nameof(SubmitRoutesRefused))]
    public async Task StepZero_internal_submit_refuses_before_any_rights_call_or_write(string segment, string set, StepZeroCallerKind kind)
    {
        var (entity, id) = CoreOf(segment);
        _participations.Links[(entity, id)] = new[] { FirmId };
        StepZero.Kind = kind;

        var response = await PostInternalToAsync(segment, set, id);

        await StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        _access.Calls.Should().BeEmpty();
        _participations.LinkReads.Should().BeEmpty();
        NothingWritten();
    }

    [Theory(DisplayName = "Task 099 must-fire: a refused caller's multipart upload is refused at step 0 WITHOUT the request body being read (the form is parsed in the handler, after step 0 and the rights checks)")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_internal_submit_never_reads_the_request_body_of_a_refused_caller(StepZeroCallerKind kind)
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.AppendTo);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);
        StepZero.Kind = kind;

        var response = await PostRecordingBodyAsync();

        await StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        Volatile.Read(ref _bodyReads).Should().Be(0, "minimal API binds no form parameter on this route; the handler reads the form only after the filter let the request through");
        NothingWritten();
    }

    [Theory(DisplayName = "Task 099 must-not-fire control: the same recording catches the body being read for an internal caller (so the zero above is not a blind probe)")]
    [InlineData(StepZeroCallerKind.FlagFalse)]
    [InlineData(StepZeroCallerKind.FlagBlank)]
    public async Task StepZero_internal_submit_reads_the_request_body_of_an_internal_caller(StepZeroCallerKind kind)
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.AppendTo);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);
        StepZero.Kind = kind;

        var response = await PostRecordingBodyAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Volatile.Read(ref _bodyReads).Should().BeGreaterThan(0);
    }

    private const string RecordBodyHeader = "X-Test-Record-Body-Reads";
    private int _bodyReads;

    private Task<HttpResponseMessage> PostRecordingBodyAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/finance/matters/{MatterId}/invoice-submissions?firmId={FirmId}") { Content = Form() };
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Add(RecordBodyHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return _client.SendAsync(request);
    }

    /// <summary>Forwards every read to <paramref name="inner"/> and reports each one.</summary>
    private sealed class ReadRecordingStream(Stream inner, Action onRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead();
            return inner.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            onRead();
            return inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead();
            return inner.ReadAsync(buffer, cancellationToken);
        }
    }

    [Theory(DisplayName = "Task 099 must-not-fire: every internal submit route admits a caller whose flag reads false or blank, as before")]
    [MemberData(nameof(SubmitRoutesInternal))]
    public async Task StepZero_internal_submit_admits_an_internal_caller(string segment, string set, StepZeroCallerKind kind)
    {
        var (entity, id) = CoreOf(segment);
        _participations.Links[(entity, id)] = new[] { FirmId };
        StepZero.Kind = kind;

        var response = await PostInternalToAsync(segment, set, id);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Theory(DisplayName = "Task 099 must-fire: GET /api/finance/invoice-submissions/{id} refuses a step-0 caller with the uniform 404, before any rights call, indistinguishable from an internal caller's 404 for an absent id")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_internal_get_is_the_uniform_404_indistinguishable_from_an_internal_absent_id(StepZeroCallerKind kind)
    {
        var submissionId = Guid.NewGuid();
        var absentId = Guid.NewGuid();
        StoredSubmission(submissionId, FirmId);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, submissionId, AccessRights.Read);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, absentId, AccessRights.Read); // rights pass: the handler's own 404
        StepZero.Kind = kind;

        var refused = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{submissionId}");
        await StepZero.AssertRefusedAsync(refused, kind, uniformNotFound: true);
        _access.Calls.Should().BeEmpty();

        StepZero.Kind = StepZeroCallerKind.FlagFalse; // an INTERNAL caller, with the rights, asking for an id that does not exist
        _h.Submission = null; // the store holds nothing: the handler's own 404
        var absent = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{absentId}");
        _access.Calls.Should().NotBeEmpty("the internal caller reached the record check and the handler");

        await Sprk.Bff.Api.Tests.Api.Filters.FinanceStepZeroTests.AssertIndistinguishableAsync(refused, absent);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: GET /api/finance/invoice-submissions/{id} with a flag that reads false or blank answers as before")]
    [InlineData(StepZeroCallerKind.FlagFalse)]
    [InlineData(StepZeroCallerKind.FlagBlank)]
    public async Task StepZero_internal_get_admits_an_internal_caller(StepZeroCallerKind kind)
    {
        var submissionId = Guid.NewGuid();
        StoredSubmission(submissionId, FirmId);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, submissionId, AccessRights.Read);
        StepZero.Kind = kind;

        var response = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{submissionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Task 099: the external submit route is untouched — it runs no step-0 lookup")]
    public async Task StepZero_does_not_run_on_the_external_submit_route()
    {
        StepZero.Kind = StepZeroCallerKind.FlagTrue;

        var external = await PostExternalAsync(Form());

        external.StatusCode.Should().Be(HttpStatusCode.Accepted);
        StepZero.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Both_channels_refuse_an_unlinked_firm_with_the_same_status_and_code()
    {
        _participations.Links[("sprk_matter", MatterId)] = new[] { OtherFirmId };
        GrantInternalSubmit();

        var external = await PostExternalAsync(Form());
        var internalPost = await PostInternalAsync(Form());

        external.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        internalPost.StatusCode.Should().Be(external.StatusCode);
        (await ErrorCodeAsync(internalPost)).Should().Be(await ErrorCodeAsync(external)).And.Be(InvoiceSubmissionErrors.FirmNotLinked);
        NothingWritten();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal routes: the caller's own Dataverse rights
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Internal_caller_with_read_and_appendto_gets_202_on_the_internal_status_url()
    {
        GrantInternalSubmit();

        var response = await PostInternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var submissionId = body.RootElement.GetProperty("submissionId").GetGuid();
        body.RootElement.GetProperty("statusUrl").GetString().Should().Be($"/api/finance/invoice-submissions/{submissionId}");
        ((OptionSetValue)_h.Allocations.Single().Fields!["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);
        _access.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.MatterEntitySet && c.Id == MatterId && c.HasToken);
    }

    [Theory]
    [InlineData(AccessRights.Read)]     // no AppendTo
    [InlineData(AccessRights.AppendTo)] // no Read
    public async Task Internal_caller_without_read_and_appendto_on_the_record_is_403_and_nothing_is_written(AccessRights rights)
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, rights);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);

        var response = await PostInternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        NothingWritten();
    }

    [Fact]
    public async Task Internal_caller_without_appendto_on_the_firm_is_403()
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.AppendTo);

        var response = await PostInternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        NothingWritten();
    }

    [Fact]
    public async Task Internal_route_runs_the_same_validation()
    {
        GrantInternalSubmit();

        var notPdf = await PostInternalAsync(Form(pdf: "MZ"u8.ToArray(), contentType: "text/plain"));
        var noNumber = await PostInternalAsync(Form(number: "  "));

        (await ErrorCodeAsync(notPdf)).Should().Be(InvoiceSubmissionErrors.NotPdf);
        (await ErrorCodeAsync(noNumber)).Should().Be(InvoiceSubmissionErrors.FirmInvoiceNumberRequired);
        NothingWritten();
    }

    [Fact]
    public async Task Internal_get_with_read_on_the_submission_is_200()
    {
        var id = Guid.NewGuid();
        StoredSubmission(id, FirmId);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, id, AccessRights.Read);

        var response = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _access.Calls.Should().Contain(c => c.Set == "sprk_invoicesubmissions" && c.Id == id);
    }

    [Fact]
    public async Task Internal_get_whose_read_faults_is_503_record_unavailable()
    {
        var id = Guid.NewGuid();
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, id, AccessRights.Read);
        _h.Entities.Setup(e => e.RetrieveAsync("sprk_invoicesubmission", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));

        var response = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
    }

    [Fact]
    public async Task Internal_submit_whose_firm_read_faults_is_503_firm_unverifiable()
    {
        GrantInternalSubmit();
        _h.FirmReadFailure = new TimeoutException("throttled");

        var response = await PostInternalAsync(Form());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        NothingWritten();
    }

    [Fact]
    public async Task Internal_get_without_read_on_the_submission_is_404()
    {
        var id = Guid.NewGuid();
        StoredSubmission(id, FirmId);

        var response = await SendAsync(HttpMethod.Get, $"/api/finance/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Doubles ──

    private sealed class StubResolver : ICallerPrincipalResolver
    {
        public CallerPrincipal? Principal { get; set; }

        public Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct) =>
            Task.FromResult(Principal is null
                ? CallerPrincipalResolution.Denied(Results.Problem(statusCode: 403, detail: "No external principal (test stub)"))
                : CallerPrincipalResolution.Resolved(Principal));
    }

    private sealed class Participations : ExternalParticipationService
    {
        public Participations()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: Mock.Of<TokenCredential>(),
                httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance, filing: null)
        {
        }

        public IReadOnlyList<Guid> Firms { get; set; } = Array.Empty<Guid>();

        public bool Unreadable { get; set; }

        /// <summary>The live state every contact answers (an Active contact unless a test says otherwise).</summary>
        public ContactRecordState ContactState { get; set; } = ContactRecordState.Active;

        /// <summary>The assigned law firms (<c>sprk_assignedlawfirm1/2</c>) of each core record, by (table, id); a record not listed references none.</summary>
        public Dictionary<(string Entity, Guid Id), Guid[]> Links { get; } = new();

        /// <summary>True when the record-to-organization read cannot be completed (every record comes back unresolved).</summary>
        public bool LinksUnreadable { get; set; }

        public List<(string Entity, IReadOnlyCollection<Guid> Ids)> LinkReads { get; } = new();

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct) =>
            Task.FromResult(ContactState);

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            LinkReads.Add((entityType, recordIds));
            IReadOnlyDictionary<Guid, ReferencedOrganizations> answer = recordIds.Distinct().ToDictionary(
                id => id,
                id => LinksUnreadable
                    ? ReferencedOrganizations.Unresolved
                    : new ReferencedOrganizations(Links.TryGetValue((entityType, id), out var firms) ? firms : Array.Empty<Guid>(), Unreadable: false));
            return Task.FromResult(answer);
        }

        /// <summary>The memberships of the test contact only; any other contact belongs to no firm (so a filter that read the wrong contact fails).</summary>
        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            MembershipReads++;
            return Task.FromResult(Unreadable ? ActiveOrgMemberships.Failed
                : contactId == InvoiceSubmissionServiceTests.ContactId ? new ActiveOrgMemberships(Firms, Firms, false)
                : new ActiveOrgMemberships(Array.Empty<Guid>(), Array.Empty<Guid>(), false));
        }

        public int MembershipReads { get; private set; }
    }

    // ── ebilling-r1 task 094: POST /api/finance/invoice-submissions/from-document (Submit Invoice; model 9.1 step 1) ──

    private static readonly Guid SourceDocumentId = Guid.Parse("09400000-0000-4000-8000-0000000000d7");
    private static readonly Guid SourceCommunicationId = Guid.Parse("09400000-0000-4000-8000-0000000000c7");

    private Task<HttpResponseMessage> PostFromDocumentAsync(object body) =>
        SendAsync(HttpMethod.Post, "/api/finance/invoice-submissions/from-document", JsonContent.Create(body));

    private static object FromDocumentBody(Guid? documentId = null, string kind = "matter", string? number = "INV-94") => new
    {
        communicationId = SourceCommunicationId,
        documentId = documentId ?? SourceDocumentId,
        coreRecord = new { kind, id = MatterId },
        firmId = FirmId,
        firmInvoiceNumber = number,
    };

    /// <summary>The source: a document of a communication already filed under the matter (so step A files nothing), both readable.</summary>
    private void SourceDocumentExists(bool readable = true)
    {
        _h.ExistingDocument = new Entity("sprk_document", SourceDocumentId)
        {
            ["sprk_graphdriveid"] = "b!drive",
            ["sprk_graphitemid"] = "item-94",
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", SourceCommunicationId),
        };
        _h.Communications[SourceCommunicationId] = new Entity("sprk_communication", SourceCommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };
        if (readable)
        {
            _sourceRights.Grant(FinanceAuthorizationFilter.DocumentEntitySet, SourceDocumentId, AccessRights.Read);
            _sourceRights.Grant("sprk_communications", SourceCommunicationId, AccessRights.Read);
        }
    }

    [Fact(DisplayName = "094: a Submit Invoice from a document is 202 with the receipt: one Internal submission naming the source, its job queued, nothing uploaded")]
    public async Task FromDocument_received_is_202_with_one_internal_submission_and_its_job()
    {
        GrantInternalSubmit();
        SourceDocumentExists();

        var response = await PostFromDocumentAsync(FromDocumentBody());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("submissionId").GetGuid().Should().Be(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(SourceDocumentId));
        var allocation = _h.Allocations.Should().ContainSingle().Subject;
        ((OptionSetValue)allocation.Fields!["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);
        allocation.Fields["sprk_document"].Should().Be(new EntityReference("sprk_document", SourceDocumentId));
        _h.JobsSent.Should().ContainSingle();
        _h.Stages.Should().BeEmpty("the job copies the source; the route uploads nothing");
        _sourceRights.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.DocumentEntitySet && c.Id == SourceDocumentId && c.Token != null);
    }

    [Theory(DisplayName = "094 (must fire): without Read AND AppendTo on the core record (finance.submit_invoice) the route is 403 before step 1; nothing read or written")]
    [InlineData(AccessRights.Read)]
    [InlineData(AccessRights.AppendTo)]
    public async Task FromDocument_without_submit_invoice_on_the_core_record_is_403(AccessRights rights)
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, rights);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);
        SourceDocumentExists();

        var response = await PostFromDocumentAsync(FromDocumentBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _sourceRights.Calls.Should().BeEmpty("the filter refuses before step 1 asks anything");
        NothingWritten();
    }

    [Fact(DisplayName = "094 (must fire): without AppendTo on the firm (finance.attach_invoice) the route is 403; nothing written")]
    public async Task FromDocument_without_attach_on_the_firm_is_403()
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.AppendTo);
        SourceDocumentExists();

        var response = await PostFromDocumentAsync(FromDocumentBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        NothingWritten();
    }

    [Fact(DisplayName = "094 step 1 (must fire): without Read on the source document the route is 403 source_forbidden, the same answer as an unknown document; nothing written")]
    public async Task FromDocument_without_read_on_the_document_is_403_source_forbidden()
    {
        GrantInternalSubmit();
        SourceDocumentExists(readable: false);

        var unreadable = await PostFromDocumentAsync(FromDocumentBody());
        var unknown = await PostFromDocumentAsync(FromDocumentBody(documentId: Guid.NewGuid()));

        unreadable.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(unreadable)).Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(unknown)).Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        NothingWritten();
    }

    [Theory(DisplayName = "094: a body without a document, a core record of a known kind or an invoice number is 400 before anything is read (fix round: the number is validated with the body); nothing written")]
    [InlineData("document")]
    [InlineData("kind")]
    [InlineData("number")]
    public async Task FromDocument_invalid_body_is_400(string missing)
    {
        GrantInternalSubmit();
        SourceDocumentExists();

        var response = await PostFromDocumentAsync(missing switch
        {
            "document" => FromDocumentBody(documentId: Guid.Empty),
            "kind" => FromDocumentBody(kind: "invoice"),
            _ => FromDocumentBody(number: " "),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _sourceRights.Calls.Should().BeEmpty("the body is validated before step 1 reads the source");
        if (missing == "number")
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("errors").TryGetProperty("firmInvoiceNumber", out _).Should().BeTrue();
        }

        NothingWritten();
    }

    [Fact(DisplayName = "094 step 1 (must fire): without Read on the source communication the route is 403 source_forbidden; nothing written")]
    public async Task FromDocument_without_read_on_the_communication_is_403_source_forbidden()
    {
        GrantInternalSubmit();
        SourceDocumentExists(readable: false);
        _sourceRights.Grant(FinanceAuthorizationFilter.DocumentEntitySet, SourceDocumentId, AccessRights.Read);

        var response = await PostFromDocumentAsync(FromDocumentBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        NothingWritten();
    }

    [Theory(DisplayName = "094 (Task 099 step 0, must fire): a flagged-external, row-less or unreadable caller is refused before any rights call, record read or write")]
    [MemberData(nameof(RefusedKinds))]
    public async Task FromDocument_step_zero_refuses_before_any_record_read(StepZeroCallerKind kind)
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        StepZero.Kind = kind;

        var response = await PostFromDocumentAsync(FromDocumentBody());

        await StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        _access.Calls.Should().BeEmpty();
        _sourceRights.Calls.Should().BeEmpty();
        NothingWritten();
    }

    [Fact(DisplayName = "094: an unauthenticated Submit Invoice is 401; nothing read or written")]
    public async Task FromDocument_unauthenticated_is_401()
    {
        SourceDocumentExists();

        var response = await SendAsync(HttpMethod.Post, "/api/finance/invoice-submissions/from-document", JsonContent.Create(FromDocumentBody()), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _sourceRights.Calls.Should().BeEmpty();
        NothingWritten();
    }

    [Fact(DisplayName = "094: with eBilling off an authorized Submit Invoice is 503 and nothing is read or written")]
    public async Task FromDocument_with_ebilling_off_is_503()
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        _h.Options.Enabled = false;

        var response = await PostFromDocumentAsync(FromDocumentBody());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _sourceRights.Calls.Should().BeEmpty();
        NothingWritten();
    }

    // ── ebilling-r1 task 097b (EB-D-179 item 2): a Correction from a document (mode / invoiceId, as the from-email route takes them) ──

    private static object CorrectionBody(Guid? invoiceId = null, string? mode = "Correction", string number = CorrectionSeed.Number) => new
    {
        communicationId = SourceCommunicationId,
        documentId = SourceDocumentId,
        coreRecord = new { kind = "matter", id = MatterId },
        firmId = FirmId,
        firmInvoiceNumber = number,
        mode,
        invoiceId = invoiceId ?? CorrectionSeed.OldInvoiceId,
    };

    /// <summary>A correctable invoice (In Review, its own submission, this firm and matter) the caller may Write.</summary>
    private void CorrectableInvoice(Entity? invoice = null, AccessRights rights = AccessRights.Read | AccessRights.Write)
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        CorrectionSeed.Seed(_h, invoice);
        _sourceRights.Grant(FinanceAuthorizationFilter.InvoiceEntitySet, CorrectionSeed.OldInvoiceId, rights);
    }

    [Fact(DisplayName = "097b: a Correction from a document is 202; its submission names the replaced invoice's submission in sprk_resubmissionof")]
    public async Task FromDocument_correction_is_202_with_the_target_recorded()
    {
        CorrectableInvoice();

        var response = await PostFromDocumentAsync(CorrectionBody());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        _h.Allocations.Should().ContainSingle().Which.Fields!["sprk_resubmissionof"]
            .Should().Be(new EntityReference("sprk_invoicesubmission", CorrectionSeed.OldSubmissionId));
        _sourceRights.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.InvoiceEntitySet && c.Id == CorrectionSeed.OldInvoiceId && c.Token != null,
            "the Write check is the caller's own (OBO)");
    }

    [Fact(DisplayName = "097b: a from-document body without mode or invoiceId is unchanged: a New submission, no sprk_resubmissionof, no invoice asked about")]
    public async Task FromDocument_without_the_correction_fields_is_unchanged()
    {
        CorrectableInvoice();

        var response = await PostFromDocumentAsync(FromDocumentBody());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _h.Allocations.Should().ContainSingle().Which.Fields!.Should().NotContainKey("sprk_resubmissionof");
        _sourceRights.Calls.Should().NotContain(c => c.Set == FinanceAuthorizationFilter.InvoiceEntitySet);
    }

    [Fact(DisplayName = "097b (seed-proof, the Write guard): without the caller's own Write on the invoice the route is the uniform 404, byte-for-byte the answer for an unknown invoice; nothing read about it, nothing written")]
    public async Task FromDocument_correction_without_write_is_the_uniform_404()
    {
        CorrectableInvoice(rights: AccessRights.Read);

        var denied = await PostFromDocumentAsync(CorrectionBody());
        var unknown = await PostFromDocumentAsync(CorrectionBody(invoiceId: Guid.NewGuid()));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        using (var problem = JsonDocument.Parse(deniedBody))
        {
            problem.RootElement.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.record_unavailable");
        }

        var normalise = (string body) => System.Text.RegularExpressions.Regex.Replace(body, "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"x\"");
        normalise(await unknown.Content.ReadAsStringAsync()).Should().Be(normalise(deniedBody), "the answer reveals nothing about the invoice");
        deniedBody.Should().NotContain(CorrectionSeed.OldInvoiceId.ToString());
        _h.Entities.Verify(e => e.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never());
        _sourceRights.Calls.Should().NotContain(c => c.Set == FinanceAuthorizationFilter.DocumentEntitySet, "the source is not read for a refused Correction");
        NothingWritten();
    }

    [Fact(DisplayName = "097b (review F3): the Write check runs before the earlier-submission path: a retry of a correction without Write is the uniform 404, never the earlier receipt")]
    public async Task FromDocument_correction_retry_without_write_is_the_uniform_404()
    {
        CorrectableInvoice(rights: AccessRights.Read);
        _h.Submission = new Entity("sprk_invoicesubmission", InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(SourceDocumentId))
        {
            ["sprk_trackingnumber"] = "INV-7.2",
            ["sprk_firminvoicenumber"] = CorrectionSeed.Number,
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
            ["sprk_document"] = new EntityReference("sprk_document", SourceDocumentId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", CorrectionSeed.OldSubmissionId),
        };

        var response = await PostFromDocumentAsync(CorrectionBody());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.record_unavailable");
        problem.RootElement.TryGetProperty("submissionId", out _).Should().BeFalse();
        _h.SubmissionReads.Should().BeEmpty("the earlier submission is not read for a caller without Write");
        NothingWritten();
    }

    public static TheoryData<string> CorrectionRefusals =>new() { "invoice_status", "different_firm", "different_core_record", "different_invoice_number", "no_submission", "submission_in_progress" };

    [Theory(DisplayName = "097b (seed-proof, the firm and core-record guards; must fire): a Correction the guard refuses is 409 correction_not_allowed with its reason; nothing written")]
    [MemberData(nameof(CorrectionRefusals))]
    public async Task FromDocument_correction_refused_is_409_with_its_reason(string reason)
    {
        CorrectableInvoice(reason switch
        {
            "invoice_status" => CorrectionSeed.Invoice(status: Sprk.Bff.Api.Services.Finance.InvoiceStatusCodes.Approved),
            "different_firm" => CorrectionSeed.Invoice(firm: Guid.NewGuid()),
            "different_core_record" => CorrectionSeed.Invoice(coreId: Guid.NewGuid()),
            _ => null,
        });
        if (reason == "no_submission")
        {
            _h.PriorSubmissions.Clear();
        }

        if (reason == "submission_in_progress")
        {
            // Task 097 second review F1: the invoice's own submission is still running.
            _h.PriorSubmissions[0]["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing);
        }

        var response = await PostFromDocumentAsync(CorrectionBody(number: reason == "different_invoice_number" ? "INV-8" : CorrectionSeed.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("ebilling.submission.correction_not_allowed");
        problem.RootElement.GetProperty("reason").GetString().Should().Be(reason);
        if (reason == "invoice_status")
        {
            problem.RootElement.GetProperty("invoiceStatus").GetString().Should().Be("Approved");
        }

        NothingWritten();
    }

    [Theory(DisplayName = "097b: an invalid mode, a New naming an invoice, or a Correction without one is 400 before anything is read; nothing written")]
    [InlineData("Replace", true, "mode")]
    [InlineData("New", true, "invoiceId")]
    [InlineData(null, true, "invoiceId")]
    [InlineData("Correction", false, "invoiceId")]
    public async Task FromDocument_invalid_correction_fields_are_400(string? mode, bool withInvoice, string field)
    {
        CorrectableInvoice();
        object body = withInvoice
            ? CorrectionBody(mode: mode)
            : new
            {
                communicationId = SourceCommunicationId,
                documentId = SourceDocumentId,
                coreRecord = new { kind = "matter", id = MatterId },
                firmId = FirmId,
                firmInvoiceNumber = CorrectionSeed.Number,
                mode,
            };

        var response = await PostFromDocumentAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
        _sourceRights.Calls.Should().BeEmpty("the body is validated before anything is asked");
        NothingWritten();
    }

    // ── ebilling-r1 task 118b-1: supportingDocumentIds on POST from-document (contract, the caller's Read, the same communication) ──

    private static readonly Guid SupportingA = Guid.Parse("118b0000-0000-4000-8000-0000000000a1");
    private static readonly Guid SupportingB = Guid.Parse("118b0000-0000-4000-8000-0000000000b1");

    private static object SupportingBody(params Guid[] supporting) => new
    {
        communicationId = SourceCommunicationId,
        documentId = SourceDocumentId,
        coreRecord = new { kind = "matter", id = MatterId },
        firmId = FirmId,
        firmInvoiceNumber = "INV-94",
        supportingDocumentIds = supporting,
    };

    /// <summary>A supporting document row linking <paramref name="communication"/> (null: none), readable by the caller when <paramref name="readable"/>.</summary>
    private void SupportingDocumentExists(Guid id, Guid? communication, bool readable = true, int? fileSize = null)
    {
        var row = new Entity("sprk_document", id)
        {
            ["sprk_graphdriveid"] = "b!drive",
            ["sprk_graphitemid"] = "item-" + id.ToString("N"),
            ["versionnumber"] = 5L,
            ["sprk_mimetype"] = "application/pdf",
            ["sprk_filename"] = "Timesheet.pdf",
        };
        if (fileSize is { } recorded)
        {
            row["sprk_filesize"] = recorded;
        }

        if (communication is { } linked)
        {
            row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", linked);
        }

        _h.OtherDocuments[id] = row;
        if (readable)
        {
            _sourceRights.Grant(FinanceAuthorizationFilter.DocumentEntitySet, id, AccessRights.Read);
        }
    }

    [Fact(DisplayName = "118b-2 (acceptance: two readable supporting documents): 202 with the document's submission; each supporting document checked once as the caller (OBO), read through the shared read (its container verified, its bytes downloaded) and staged under its part id with its own source; one submission, one job")]
    public async Task FromDocument_two_readable_supporting_documents_are_202_and_each_copy_is_staged_with_its_source()
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        SupportingDocumentExists(SupportingA, SourceCommunicationId);
        SupportingDocumentExists(SupportingB, SourceCommunicationId);

        var response = await PostFromDocumentAsync(SupportingBody(SupportingB, SupportingA, SupportingB));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var submissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(SourceDocumentId);
        using (var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            receipt.RootElement.GetProperty("submissionId").GetGuid().Should().Be(submissionId, "the id is still the document's (D-134)");
        }

        foreach (var id in new[] { SupportingA, SupportingB })
        {
            _sourceRights.Calls.Should().ContainSingle(c => c.Set == FinanceAuthorizationFilter.DocumentEntitySet && c.Id == id && c.Token != null,
                "each supporting document is checked once (repeats ignored), as the caller");
            _spe.Verify(s => s.DownloadFileAsync("b!drive", "item-" + id.ToString("N"), It.IsAny<CancellationToken>()), Times.Once());
        }

        var parent = new Sprk.Bff.Api.Services.Finance.Files.StoredFileParent(
            Sprk.Bff.Api.Services.Finance.Files.StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(submissionId));
        var container = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.ContainerOf(Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoiceSupportingFile);
        _h.Stages.Should().HaveCount(2);
        var sorted = new[] { SupportingA, SupportingB }.Order().ToArray();
        for (var number = 1; number <= sorted.Length; number++)
        {
            var blob = _h.Files.Gateway.Get(container, Sprk.Bff.Api.Services.Finance.Files.StoredFileBlobName.Build(
                _h.Files.TenantId, parent, InvoiceSubmissionPersister.DocumentPartFileId(submissionId, number)))!;
            blob.Metadata[Sprk.Bff.Api.Services.Finance.Files.StoredFileBlobs.MetadataSourceDocument].Should().Be(
                sorted[number - 1].ToString("D"), "part numbers follow the sorted ids");
            blob.Metadata[Sprk.Bff.Api.Services.Finance.Files.StoredFileBlobs.MetadataSourceCommunication].Should().Be(SourceCommunicationId.ToString("D"));
            blob.Metadata[Sprk.Bff.Api.Services.Finance.Files.StoredFileBlobs.MetadataSourceVersion].Should().Be("5");
        }

        _h.Allocations.Should().ContainSingle().Which.SubmissionId.Should().Be(submissionId);
        _h.JobsSent.Should().ContainSingle();
    }

    [Theory(DisplayName = "118b-1 (seed-proof, the Read guard; must fire): a supporting document the caller cannot Read, an unknown one, one of another communication or of none is the uniform 404, byte-for-byte the same; an unreadable row is never read; nothing written")]
    [InlineData("unreadable")]
    [InlineData("other_communication")]
    [InlineData("no_communication")]
    public async Task FromDocument_supporting_document_refusals_are_the_uniform_404(string state)
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        SupportingDocumentExists(SupportingA, SourceCommunicationId);
        switch (state)
        {
            case "unreadable":
                SupportingDocumentExists(SupportingB, SourceCommunicationId, readable: false);
                break;
            case "other_communication":
                SupportingDocumentExists(SupportingB, Guid.NewGuid());
                break;
            default:
                SupportingDocumentExists(SupportingB, communication: null);
                break;
        }

        var refused = await PostFromDocumentAsync(SupportingBody(SupportingA, SupportingB));
        var unknown = await PostFromDocumentAsync(SupportingBody(SupportingA, Guid.Parse("118b0000-0000-4000-8000-0000000000ff")));

        refused.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        using (var problem = JsonDocument.Parse(refusedBody))
        {
            problem.RootElement.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.record_unavailable");
        }

        WithoutCorrelation(await unknown.Content.ReadAsStringAsync()).Should().Be(WithoutCorrelation(refusedBody), "the answer reveals nothing about the document");
        refusedBody.Should().NotContain(SupportingB.ToString());
        if (state == "unreadable")
        {
            _h.Entities.Verify(e => e.RetrieveAsync("sprk_document", SupportingB, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never(),
                "the row of a document the caller cannot Read is never read");
        }

        NothingWritten();
    }

    [Fact(DisplayName = "118b review F1-b (must fire): a supporting document whose row records a size over the configured file limit is 413 ebilling.submission.too_large (the upload's code) through the route's own binding of the shared read; nothing downloaded for it, nothing staged or created")]
    public async Task FromDocument_a_supporting_document_over_the_file_limit_is_413_nothing_written()
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        SupportingDocumentExists(SupportingA, SourceCommunicationId);
        // 2 MB against the harness's 1 MB limit: over the file's own limit and far under the total, so it is the per-file path (review 2 F1
        // sums recorded sizes first: a size over the whole request cap would be the total's refusal instead).
        SupportingDocumentExists(SupportingB, SourceCommunicationId, fileSize: 2 * 1024 * 1024);

        var response = await PostFromDocumentAsync(SupportingBody(SupportingA, SupportingB));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await response.Content.ReadAsStringAsync());
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.TooLarge);
        _spe.Verify(s => s.DownloadFileAsync("b!drive", "item-" + SupportingB.ToString("N"), It.IsAny<CancellationToken>()), Times.Never(),
            "the row says it is too big: no download");
        NothingWritten();
    }

    [Fact(DisplayName = "118b review 2 F1 (must fire): supporting documents whose rows record sizes that add up to more than the upload route's request cap are 413 ebilling.submission.too_large through the route's own binding, in the total's own words; not one is downloaded; nothing staged or created")]
    public async Task FromDocument_supporting_documents_over_the_total_are_413_nothing_downloaded()
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        _h.Options.MaxSubmissionFileSizeMb = EbillingOptions.MaxSubmissionFileSizeMbCeiling; // no single file is over its limit
        var third = (int)(InvoiceSubmissionHttp.MaxRequestBodyBytes / 3) + 1;
        var ids = new[] { SupportingA, SupportingB, Guid.Parse("118b0000-0000-4000-8000-0000000000c1") };
        foreach (var id in ids)
        {
            SupportingDocumentExists(id, SourceCommunicationId, fileSize: third);
        }

        var response = await PostFromDocumentAsync(SupportingBody(ids));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await response.Content.ReadAsStringAsync());
        (await ErrorCodeAsync(response)).Should().Be(InvoiceSubmissionErrors.TooLarge);
        (await response.Content.ReadAsStringAsync()).Should().Contain("together");
        _spe.Verify(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never(),
            "the recorded sizes already pass the cap: no download");
        NothingWritten();
    }

    [Fact(DisplayName = "118b review 2 F4 (must fire): the from-document route's OpenAPI contract lists 413 as a problem response and the description names too_large, document_unavailable and storage_not_configured among its refusals")]
    public void FromDocument_route_metadata_lists_413_and_the_supporting_document_refusals()
    {
        var endpoint = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/finance/invoice-submissions/from-document");

        endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(m => m.StatusCode)
            .Should().Contain(StatusCodes.Status413PayloadTooLarge);
        endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()!.Description.Should()
            .Contain("413 too_large").And.Contain("document_unavailable").And.Contain("storage_not_configured");
    }

    public static TheoryData<string> SupportingShapeRefusals => new() { "invoice_document", "empty_id", "too_many" };

    [Theory(DisplayName = "118b-1 (must fire): the invoice document listed as supporting, an empty id, or more than 50 files in all is 400 supportingDocumentIds before anything is read; nothing written")]
    [MemberData(nameof(SupportingShapeRefusals))]
    public async Task FromDocument_invalid_supporting_documents_are_400(string shape)
    {
        GrantInternalSubmit();
        SourceDocumentExists();

        var response = await PostFromDocumentAsync(shape switch
        {
            "invoice_document" => SupportingBody(SupportingA, SourceDocumentId),
            "empty_id" => SupportingBody(SupportingA, Guid.Empty),
            _ => SupportingBody(Enumerable.Range(1, SubmissionCommitGate.MaxFiles).Select(_ => Guid.NewGuid()).ToArray()),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").TryGetProperty("supportingDocumentIds", out _).Should().BeTrue();
        _sourceRights.Calls.Should().BeEmpty("the body is validated before anything is asked");
        NothingWritten();
    }

    [Fact(DisplayName = "118b-1 (must not fire): 49 supporting documents (50 files with the invoice document) pass the limit, and repeats are not counted")]
    public async Task FromDocument_the_file_limit_counts_distinct_documents_and_the_invoice_document()
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        var ids = Enumerable.Range(1, SubmissionCommitGate.MaxFiles - 1).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            SupportingDocumentExists(id, SourceCommunicationId);
        }

        var response = await PostFromDocumentAsync(SupportingBody([.. ids, .. ids.Take(5)]));

        // Task 118b review: the whole request goes through (not only "past the 400"): 202, one stage per distinct supporting document,
        // repeats not counted, and the limit's own count (49 + the invoice document = MaxFiles) is not off by one at the intake.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        _sourceRights.Calls.Count(c => c.Set == FinanceAuthorizationFilter.DocumentEntitySet && ids.Contains(c.Id)).Should().Be(ids.Length);
        _h.Stages.Should().HaveCount(SubmissionCommitGate.MaxFiles - 1, "49 distinct supporting documents are staged, one part each");
        _h.Allocations.Should().ContainSingle();
        _h.JobsSent.Should().ContainSingle();
    }

    [Fact(DisplayName = "118b (must fire): a retry of an earlier submission of the document that staged no supporting documents (the empty set), naming some, is 409 document_already_submitted; the same request without them replays; nothing written")]
    public async Task FromDocument_retry_with_another_supporting_set_is_409()
    {
        GrantInternalSubmit();
        SourceDocumentExists();
        SupportingDocumentExists(SupportingA, SourceCommunicationId);
        _h.Submission = new Entity("sprk_invoicesubmission", InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(SourceDocumentId))
        {
            ["sprk_trackingnumber"] = "INV-94.1",
            ["sprk_firminvoicenumber"] = "INV-94",
            ["sprk_vendororg"] = new EntityReference("sprk_organization", FirmId),
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
            ["sprk_document"] = new EntityReference("sprk_document", SourceDocumentId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };

        var withSet = await PostFromDocumentAsync(SupportingBody(SupportingA));
        var without = await PostFromDocumentAsync(FromDocumentBody());

        withSet.StatusCode.Should().Be(HttpStatusCode.Conflict, await withSet.Content.ReadAsStringAsync());
        (await ErrorCodeAsync(withSet)).Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        without.StatusCode.Should().Be(HttpStatusCode.Accepted, "the same request without the field replays as before");
        NothingWritten();
    }


    // ═══════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 097f (EB-D-180): the status read of an Accepted submission whose invoice a correction Superseded answers Accepted with
    // the replaced-by-correction reason and, when the caller may read the correcting submission, its id and tracking number. Derived at read time.
    // ═══════════════════════════════════════════════════════════════════════════

    private const string ReplacedByCorrection = "ebilling.submission.replaced_by_correction";
    private const string InternalRoute = "/api/finance/invoice-submissions";
    private const string ExternalRoute = "/api/v1/external/invoice-submissions";
    private static readonly Guid OldSubmissionId = Guid.Parse("097f0000-0000-4000-8000-000000000001");
    private static readonly Guid OldInvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(OldSubmissionId); // its own invoice (task 165c)
    private static readonly Guid NewSubmissionId = Guid.Parse("097f0000-0000-4000-8000-000000000003");
    private static readonly Guid NewInvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(NewSubmissionId);
    private static readonly Guid SecondSubmissionId = Guid.Parse("097f0000-0000-4000-8000-000000000005");
    private static readonly Guid SecondInvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SecondSubmissionId);
    private static readonly Guid OtherSubmissionId = Guid.Parse("097f0000-0000-4000-8000-000000000008");
    private static readonly Guid OtherInvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(OtherSubmissionId);

    private static Entity InvoiceRow(Guid id, int status) =>
        new("sprk_invoice", id) { ["statuscode"] = new OptionSetValue(status) };

    /// <summary>A correcting submission of the old one: Accepted, the given firm (the caller's by default) and the matter, naming <see cref="OldSubmissionId"/>.</summary>
    private void AddReplacement(Guid submissionId, Guid invoiceId, int invoiceStatus, string tracking, Guid? firm = null)
    {
        _h.PriorSubmissions.Add(new Entity("sprk_invoicesubmission", submissionId)
        {
            ["sprk_trackingnumber"] = tracking,
            ["sprk_vendororg"] = new EntityReference("sprk_organization", firm ?? FirmId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
            ["sprk_channel"] = new OptionSetValue(Internal),
            [InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", invoiceId),
            [InvoiceSubmissionService.ResubmissionOfColumn] = new EntityReference("sprk_invoicesubmission", OldSubmissionId),
        });
        _h.Invoices[invoiceId] = InvoiceRow(invoiceId, invoiceStatus);
        _world.SpaUpload(invoiceId, submissionId); // visible to a firm by the (a') predicate
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, submissionId, AccessRights.Read);
    }

    /// <summary>The old submission (Accepted, its invoice Superseded) and one correcting submission "INV-1.2" with an In Review invoice; the caller may read both.</summary>
    private Entity SeedSuperseded()
    {
        var old = StoredSubmissionWithInvoice(OldSubmissionId, OldInvoiceId);
        old["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);
        old["sprk_trackingnumber"] = "INV-1.1";
        _h.Invoices[OldInvoiceId] = InvoiceRow(OldInvoiceId, InvoiceStatusCodes.Superseded);
        _world.SpaUpload(OldInvoiceId, OldSubmissionId);
        _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, OldSubmissionId, AccessRights.Read);
        AddReplacement(NewSubmissionId, NewInvoiceId, InvoiceStatusCodes.InReview, "INV-1.2");
        return old;
    }

    private async Task<(JsonElement Body, string Text)> ReadAsync(string route, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"{route}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private void NothingWrittenToDataverse() =>
        _h.Entities.Invocations.Select(i => i.Method.Name).Where(n => !n.StartsWith("Retrieve", StringComparison.Ordinal))
            .Should().BeEmpty("the note is derived at read time; nothing is written");

    [Theory(DisplayName = "097f: a Superseded invoice's submission answers Accepted, the replaced-by-correction reason and the correcting submission, on the internal and the external read")]
    [InlineData(InternalRoute)]
    [InlineData(ExternalRoute)]
    public async Task Superseded_AnswersAccepted_WithTheReason_AndTheCorrectingSubmission(string route)
    {
        SeedSuperseded();

        var (body, _) = await ReadAsync(route, OldSubmissionId);

        body.GetProperty("status").GetString().Should().Be("Accepted");
        body.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection);
        var replacedBy = body.GetProperty("replacedBy");
        replacedBy.EnumerateObject().Select(p => p.Name).Should().Equal("submissionId", "trackingNumber");
        replacedBy.GetProperty("submissionId").GetGuid().Should().Be(NewSubmissionId);
        replacedBy.GetProperty("trackingNumber").GetString().Should().Be("INV-1.2");
        body.GetProperty("trackingNumber").GetString().Should().Be("INV-1.1", "the submission keeps its own tracking number");
        NothingWrittenToDataverse();
    }

    [Theory(DisplayName = "097f (seed-proof, the Read check on the replacement): the caller cannot read the replacement: the reason without replacedBy, and nothing of the replacement in the answer")]
    [InlineData(InternalRoute)]
    [InlineData(ExternalRoute)]
    public async Task Superseded_CallerCannotReadTheReplacement_HasTheReason_WithoutReplacedBy(string route)
    {
        SeedSuperseded();
        if (route == InternalRoute)
        {
            _access.Grant(FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, NewSubmissionId, AccessRights.None);
        }
        else
        {
            _world.Source(NewInvoiceId, permission: RestrictedPermission); // the (a') predicate withholds the replacement from this caller
        }

        var (body, text) = await ReadAsync(route, OldSubmissionId);

        body.GetProperty("status").GetString().Should().Be("Accepted");
        body.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection);
        body.TryGetProperty("replacedBy", out _).Should().BeFalse("it is not named for a caller who cannot read it");
        text.Should().NotContain(NewSubmissionId.ToString()).And.NotContain("INV-1.2");
    }

    [Fact(DisplayName = "097f: an external caller is not told a replacement of another firm")]
    public async Task External_ReplacementOfAnotherFirm_IsNotNamed()
    {
        SeedSuperseded();
        _h.PriorSubmissions.Clear();
        AddReplacement(NewSubmissionId, NewInvoiceId, InvoiceStatusCodes.InReview, "INV-1.2", firm: OtherFirmId);

        var (otherFirm, text) = await ReadAsync(ExternalRoute, OldSubmissionId);

        otherFirm.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection);
        text.Should().NotContain("replacedBy").And.NotContain("INV-1.2");

        _h.PriorSubmissions.Clear();
        AddReplacement(NewSubmissionId, NewInvoiceId, InvoiceStatusCodes.InReview, "INV-1.2");
        (await ReadAsync(ExternalRoute, OldSubmissionId)).Body.GetProperty("replacedBy").GetProperty("submissionId").GetGuid()
            .Should().Be(NewSubmissionId, "control: the same replacement is named once it is the caller's firm");
    }

    [Theory(DisplayName = "097f: an invoice that is not Superseded is answered exactly as before: the same bytes, no reason, no replacedBy")]
    [InlineData(InternalRoute, InvoiceStatusCodes.InReview)]
    [InlineData(InternalRoute, InvoiceStatusCodes.Approved)]
    [InlineData(ExternalRoute, InvoiceStatusCodes.InReview)]
    [InlineData(ExternalRoute, InvoiceStatusCodes.Paid)]
    public async Task NotSuperseded_IsUnchanged_ByteForByte(string route, int invoiceStatus)
    {
        SeedSuperseded();
        _h.Invoices[OldInvoiceId] = InvoiceRow(OldInvoiceId, invoiceStatus);

        var (body, text) = await ReadAsync(route, OldSubmissionId);
        _h.Invoices.Clear(); // the baseline: the answer when nothing is known of the invoice at all
        var (_, baseline) = await ReadAsync(route, OldSubmissionId);

        text.Should().Be(baseline);
        text.Should().NotContain("replacedBy");
        body.GetProperty("outcomeReasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        NothingWrittenToDataverse();
    }

    [Fact(DisplayName = "097f: only an Accepted submission carries the note (a Rejected one with a Superseded invoice is answered as stored)")]
    public async Task NotAccepted_IsUnchanged()
    {
        var old = SeedSuperseded();
        old["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected);
        old["sprk_outcomereasoncode"] = SubmissionReasonCodes.Blocked;

        var (body, text) = await ReadAsync(InternalRoute, OldSubmissionId);

        body.GetProperty("status").GetString().Should().Be("Rejected");
        body.GetProperty("outcomeReasonCode").GetString().Should().Be(SubmissionReasonCodes.Blocked);
        text.Should().NotContain("replacedBy");
    }

    [Theory(DisplayName = "097f (deliberate): a fault finding the replacement leaves the reason and no replacedBy; it is not a 503")]
    [InlineData(InternalRoute)]
    [InlineData(ExternalRoute)]
    public async Task ReplacementReadFault_HasTheReason_WithoutReplacedBy_NotA503(string route)
    {
        SeedSuperseded();
        _h.ResubmissionReadFailure = new TimeoutException("dataverse down");

        var (body, text) = await ReadAsync(route, OldSubmissionId);

        body.GetProperty("status").GetString().Should().Be("Accepted");
        body.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection);
        text.Should().NotContain("replacedBy");
    }

    [Fact(DisplayName = "097f (deliberate): a fault finding which invoices are Superseded leaves the answer as it was before the note (still Accepted, no reason); it is not a 503")]
    public async Task SupersededReadFault_LeavesTheAnswerUnchanged()
    {
        SeedSuperseded();
        _h.SupersededInvoiceReadFailure = new TimeoutException("dataverse down");

        var (body, text) = await ReadAsync(InternalRoute, OldSubmissionId);

        body.GetProperty("status").GetString().Should().Be("Accepted");
        body.GetProperty("outcomeReasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        text.Should().NotContain("replacedBy");
    }

    [Fact(DisplayName = "097f: with several correcting submissions the one whose invoice is not Superseded is named; if that is still ambiguous, replacedBy is absent")]
    public async Task SeveralReplacements_PreferTheLive_ElseNone()
    {
        SeedSuperseded();
        AddReplacement(SecondSubmissionId, SecondInvoiceId, InvoiceStatusCodes.Superseded, "INV-1.3");

        var preferred = (await ReadAsync(InternalRoute, OldSubmissionId)).Body;
        preferred.GetProperty("replacedBy").GetProperty("submissionId").GetGuid().Should().Be(NewSubmissionId);

        _h.Invoices[SecondInvoiceId] = InvoiceRow(SecondInvoiceId, InvoiceStatusCodes.InReview);
        var (ambiguous, text) = await ReadAsync(InternalRoute, OldSubmissionId);
        ambiguous.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection);
        text.Should().NotContain("replacedBy");
    }

    [Fact(DisplayName = "097f: the external list notes each visible Accepted submission whose invoice is Superseded, leaves the others as they were, and asks which invoices are Superseded once")]
    public async Task ExternalList_NotesOnlyTheSuperseded_WithOneStatusRead()
    {
        var old = SeedSuperseded();
        var other = SubmissionRow("INV-9.1", Spa, invoice: OtherInvoiceId, status: InvoiceSubmissionStatus.Accepted, id: OtherSubmissionId);
        _h.Invoices[OtherInvoiceId] = InvoiceRow(OtherInvoiceId, InvoiceStatusCodes.InReview);
        _world.SpaUpload(OtherInvoiceId, OtherSubmissionId);
        var received = SubmissionRow("INV-9.2", Spa);
        _h.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_invoicesubmission"
                    && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_vendororg")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { old, other, received }));

        var response = await SendAsync(HttpMethod.Get, ExternalRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();
        rows.Should().HaveCount(3);
        var noted = rows.Single(r => r.GetProperty("submissionId").GetGuid() == OldSubmissionId);
        noted.GetProperty("outcomeReasonCode").GetString().Should().Be(ReplacedByCorrection);
        noted.GetProperty("replacedBy").GetProperty("trackingNumber").GetString().Should().Be("INV-1.2");
        foreach (var unchanged in rows.Where(r => r.GetProperty("submissionId").GetGuid() != OldSubmissionId))
        {
            unchanged.GetProperty("outcomeReasonCode").ValueKind.Should().Be(JsonValueKind.Null);
            unchanged.TryGetProperty("replacedBy", out _).Should().BeFalse();
        }

        _h.SupersededInvoiceReads.Should().ContainSingle("one status read for the whole page")
            .Which.Should().BeEquivalentTo(new[] { OldInvoiceId, OtherInvoiceId }, "only the Accepted submissions with an invoice are asked about");
    }
}
