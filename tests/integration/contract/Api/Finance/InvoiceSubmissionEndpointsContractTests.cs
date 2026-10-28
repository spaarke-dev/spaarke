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
    private WebApplication _app = null!;
    // ebilling-r1 task 098 (review round 4b): the submission routes' seam is the REAL ExternalDataService.HideSubmissionsForAsync over
    // an app-only Web API double (InvoiceWorld): a test seeds the invoice's and the documents' rows the way their writers do.
    private readonly Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld _world = new();
    private readonly List<InvoiceSubmissionView[]> _submissionDecisions = new();
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
        // ebilling-r1 task 098: the submission routes' (a') seam, as AddExternalAccess registers it (each call recorded).
        if (_registerSubmissionHiding)
            builder.Services.AddScoped<ExternalInvoiceSubmissionHiding>(_ => (caller, submissions, ct) =>
            {
                lock (_submissionDecisions) _submissionDecisions.Add(submissions.ToArray());
                return _world.NewService(SubmissionIdentity.Object).HideSubmissionsForAsync(caller, submissions, _world.RootFlags, ct);
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

    private static readonly Guid HiddenInvoice = Guid.Parse("09800000-0000-4000-8000-00000000c0de");
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
        InvoiceSubmissionStatus status = InvoiceSubmissionStatus.Received)
    {
        var row = new Entity("sprk_invoicesubmission", Guid.NewGuid())
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
        var id = Guid.NewGuid();

        await ReadIsTheSameAsMissingAsync(StoredSubmissionWithInvoice(id, HiddenInvoice), "its invoice is hidden");

        _submissionDecisions.Should().ContainSingle().Which.Should().ContainSingle()
            .Which.InvoiceId.Should().Be(HiddenInvoice);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("no predicate")]
    public async Task Task098_get_of_a_submission_with_an_invoice_the_predicate_cannot_decide_is_404_fail_closed(string kind)
    {
        if (kind == "no predicate")
        {
            await DisposeAsync();
            _registerSubmissionHiding = false;
            await InitializeAsync();
        }
        else
        {
            _world.Faults["sprk_storedfiles"] = HttpStatusCode.ServiceUnavailable;
        }

        var id = Guid.NewGuid();
        StoredSubmissionWithInvoice(id, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Task098_get_of_a_submission_whose_invoice_is_visible_or_absent_is_200_as_before()
    {
        var invoice = Guid.NewGuid();
        _world.SpaUpload(invoice);
        var withInvoice = Guid.NewGuid();
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
        var invoice = Guid.NewGuid();
        StoredSubmissionWithInvoice(id, invoice);

        var body = await (await SendAsync(HttpMethod.Get, $"/api/v1/external/invoice-submissions/{id}")).Content.ReadAsStringAsync();

        body.Should().NotContain(invoice.ToString(), "the invoice link is read for the predicate only");
    }

    [Fact]
    public async Task Task098_list_omits_a_submission_whose_invoice_is_hidden_and_keeps_the_others_in_one_decision()
    {
        _world.Source(HiddenInvoice, permission: RestrictedPermission);

        var body = await ListBodyAsync(
            SubmissionRow("INV-HIDDEN", Spa, invoice: HiddenInvoice, status: InvoiceSubmissionStatus.Accepted),
            SubmissionRow("INV-PENDING", Spa));

        body.Should().Contain("INV-PENDING").And.NotContain("INV-HIDDEN");
        _submissionDecisions.Should().ContainSingle("one decision for the page").Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task Task098_list_keeps_a_submission_whose_invoice_is_visible()
    {
        var invoice = Guid.NewGuid();
        _world.SpaUpload(invoice);

        (await ListBodyAsync(SubmissionRow("INV-VISIBLE", Spa, invoice: invoice, status: InvoiceSubmissionStatus.Accepted)))
            .Should().Contain("INV-VISIBLE");
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
    public async Task Task098_4b_a_failed_source_read_withholds_on_the_read_and_the_list(string caller)
    {
        CallerIs(caller);
        var readable = _world.Document(Guid.NewGuid(), permission: StandardPermission);
        _world.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;

        await ReadIsTheSameAsMissingAsync(SubmissionRow("INV-READABLE", Internal, readable), "its source could not be read");
        var body = await ListBodyAsync(SubmissionRow("INV-READABLE", Internal, readable), SubmissionRow("INV-SPA", Spa));

        body.Should().Be("[]", "a failed read withholds every submission the decision was asked about");
    }

    [Fact]
    public async Task Task098_list_withholds_every_asked_submission_when_the_invoice_read_fails()
    {
        _world.Faults["sprk_storedfiles"] = HttpStatusCode.ServiceUnavailable;

        var body = await ListBodyAsync(
            SubmissionRow("INV-WITH", Spa, invoice: Guid.NewGuid(), status: InvoiceSubmissionStatus.Accepted), SubmissionRow("INV-PENDING", Spa));

        body.Should().Be("[]", "a failed read withholds every submission the decision was asked about");
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
}
