using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using Sprk.Bff.Api.Services.Finance.Submission;
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
    private static readonly Guid FirmId = InvoiceSubmissionServiceTests.FirmId;
    private static readonly Guid MatterId = InvoiceSubmissionServiceTests.MatterId;
    private static readonly Guid OtherFirmId = Guid.NewGuid();

    private readonly InvoiceSubmissionServiceTests.Harness _h = new();
    private readonly Participations _participations = new();
    private readonly StubResolver _resolver = new();
    private readonly RecordingAccessDataSource _access = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public InvoiceSubmissionEndpointsContractTests()
    {
        _participations.Firms = new[] { FirmId };
        _resolver.Principal = Collaborator(AccessRights.Read | AccessRights.Create | AccessRights.Write);
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

        builder.WebHost.UseTestServer();
        _app = builder.Build();
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
        _h.Uploads.Should().BeEmpty("a refused submission uploads nothing");
        _h.Allocations.Should().BeEmpty("a refused submission creates no row");
        _h.JobsSent.Should().BeEmpty("a refused submission enqueues nothing");
        _h.DocumentCreates.Should().BeEmpty();
        _h.ExternalCreates.Should().BeEmpty();
    }

    private void GrantInternalSubmit()
    {
        _access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.AppendTo);
        _access.Grant(FinanceAuthorizationFilter.VendorOrganizationEntitySet, FirmId, AccessRights.AppendTo);
    }

    private Entity StoredSubmission(Guid id, Guid firmId) => _h.Submission = new Entity("sprk_invoicesubmission", id)
    {
        ["sprk_trackingnumber"] = "INV-1.1",
        ["sprk_vendororg"] = new EntityReference("sprk_organization", firmId),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        ["sprk_processingstatus"] = new OptionSetValue(100000000),
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
    public async Task External_list_is_200_and_empty_for_a_caller_with_no_firm()
    {
        _participations.Firms = Array.Empty<Guid>();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/external/invoice-submissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("[]");
        _h.Entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
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

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default) =>
            Task.FromResult(Unreadable ? ActiveOrgMemberships.Failed : new ActiveOrgMemberships(Firms, Firms, false));
    }
}
