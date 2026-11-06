using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The wire contract of <c>GET /api/finance/invoices/{invoiceId}/correction-prefill</c> (ebilling-r1 task 097e; contract section 10 of
/// <c>projects/ebilling-r1/notes/095-from-email-contract.md</c>, agreed with word-add-in-r1 2026-10-31). The REAL route, filters in their
/// order (step 0, the Finance filter, the kill switch) and the REAL <see cref="InvoiceSubmissionService"/> intake guard; doubles only at
/// Dataverse (<see cref="FromEmailWorld"/>, <see cref="CorrectionSeed"/>).
/// </summary>
/// <remarks>Maintain-class (ADR-038): every field name, status and code here is a client contract (word-add-in-r1).</remarks>
public sealed class InvoiceCorrectionPrefillContractTests : IAsyncLifetime
{
    private static readonly Guid OldInvoice = CorrectionSeed.OldInvoiceId;
    private static readonly Guid ReplacementSubmissionId = Guid.Parse("097e0000-0000-4000-8000-0000000000b1");
    private static readonly Guid ReplacementInvoice = InvoiceSubmissionPersister.InvoiceIdFor(ReplacementSubmissionId);
    private static readonly Guid SecondReplacementSubmissionId = Guid.Parse("097e0000-0000-4000-8000-0000000000b2");
    private static readonly Guid SecondReplacementInvoice = InvoiceSubmissionPersister.InvoiceIdFor(SecondReplacementSubmissionId);

    private readonly W _world = new();
    private readonly RecordingAccessDataSource _access = new();
    private readonly HashSet<Guid> _throwFor = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public StatedCaller StepZero { get; private set; } = null!;

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

        builder.Services.AddSingleton<IAccessDataSource>(new ThrowingAccess(_access, _throwFor));
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton<ITenantCache>(new InMemoryTenantCache());

        builder.Services.AddSingleton(_world.Intake.Monitor);
        builder.Services.AddSingleton(_world.Intake.Build());
        builder.Services.AddSingleton(_world.Intake.Participations);
        builder.Services.AddSingleton(_world.Build());
        builder.Services.AddSingleton(_world.Intake.Entities.Object);

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

    /// <summary>The recording access source, but a rights read of a listed record THROWS (a real rights-read fault, not a denial).</summary>
    private sealed class ThrowingAccess(IAccessDataSource inner, HashSet<Guid> throwFor) : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);

        public Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
            throwFor.Contains(recordId)
                ? Task.FromException<AccessSnapshot>(new TimeoutException("rights read faulted"))
                : inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);
    }

    // ── Helpers ──

    private Task<HttpResponseMessage> GetAsync(Guid? invoiceId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/invoices/{invoiceId ?? OldInvoice}/correction-prefill");
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string Normalise(string body) => Regex.Replace(body, "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"x\"");

    /// <summary>The old invoice (In Review unless said otherwise), its submission, the firm and the caller's Write on it.</summary>
    private Entity Seed(int state = InvoiceStatusCodes.StateActive, int status = InvoiceStatusCodes.InReview, Entity? submission = null,
        AccessRights rights = AccessRights.Read | AccessRights.Write)
    {
        var invoice = CorrectionSeed.Invoice(state: state, status: status);
        invoice["sprk_name"] = "INV-7 (October)";
        var own = submission ?? CorrectionSeed.Submission();
        own[TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.1";
        CorrectionSeed.Seed(_world.Intake, invoice, own);
        _world.Intake.Firm!["sprk_organizationname"] = "Firm LLP";
        _access.Grant(FinanceAuthorizationFilter.InvoiceEntitySet, OldInvoice, rights);
        return invoice;
    }

    /// <summary>A Superseded old invoice, replaced by <see cref="ReplacementInvoice"/> (In Review, readable per <paramref name="replacementRights"/>).</summary>
    private void SeedSuperseded(AccessRights replacementRights = AccessRights.Read)
    {
        Seed(InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Superseded);
        AddReplacement(ReplacementSubmissionId, ReplacementInvoice, InvoiceStatusCodes.InReview, "INV-7 (corrected)");
        _access.Grant(FinanceAuthorizationFilter.InvoiceEntitySet, ReplacementInvoice, replacementRights);
    }

    private void AddReplacement(Guid submissionId, Guid invoiceId, int status, string name)
    {
        _world.Intake.PriorSubmissions.Add(new Entity("sprk_invoicesubmission", submissionId)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId),
            ["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", CorrectionSeed.OldSubmissionId),
        });
        _world.Intake.Invoices[invoiceId] = new Entity("sprk_invoice", invoiceId)
        {
            ["sprk_name"] = name,
            ["statuscode"] = new OptionSetValue(status),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 200: the prefill
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "097e: a caller with Write on an In Review invoice gets 200 with exactly the agreed fields, and canCorrect true")]
    public async Task InReview_WithWrite_Is200_WithThePrefill_AndCanCorrect()
    {
        Seed();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.EnumerateObject().Select(p => p.Name).Should().Equal(
            "invoiceId", "firm", "coreRecord", "invoiceStatus", "trackingNumber", "firmInvoiceNumber", "canCorrect", "reason", "replacedBy");
        body.GetProperty("invoiceId").GetGuid().Should().Be(OldInvoice);
        body.GetProperty("firm").EnumerateObject().Select(p => p.Name).Should().Equal("id", "name");
        body.GetProperty("firm").GetProperty("id").GetGuid().Should().Be(W.FirmId);
        body.GetProperty("firm").GetProperty("name").GetString().Should().Be("Firm LLP");
        var core = body.GetProperty("coreRecord");
        core.EnumerateObject().Select(p => p.Name).Should().Equal("entityType", "entityId", "name");
        core.GetProperty("entityType").GetString().Should().Be("Matter", "the intake vocabulary, never a logical name");
        core.GetProperty("entityId").GetGuid().Should().Be(W.MatterId);
        core.GetProperty("name").GetString().Should().Be("Acme v. Roe");
        body.GetProperty("invoiceStatus").GetString().Should().Be("InReview");
        body.GetProperty("trackingNumber").GetString().Should().Be("INV-7.1");
        body.GetProperty("firmInvoiceNumber").GetString().Should().Be(CorrectionSeed.Number);
        body.GetProperty("canCorrect").GetBoolean().Should().BeTrue();
        body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("replacedBy").ValueKind.Should().Be(JsonValueKind.Null);
        _world.Intake.Writes.Should().Be(0, "a read");
    }

    [Fact(DisplayName = "097e: a submission with no tracking number answers trackingNumber null")]
    public async Task NoTrackingNumber_IsNull()
    {
        var invoice = Seed();
        _world.Intake.PriorSubmissions.Single()[TrackingNumberAllocator.TrackingNumberColumn] = null;

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("trackingNumber").ValueKind.Should().Be(JsonValueKind.Null);
        invoice.Should().NotBeNull();
    }

    [Theory(DisplayName = "097e: the core record is named in the intake vocabulary (Project, WorkAssignment)")]
    [InlineData("sprk_project", "sprk_project", "Project", "sprk_regardingproject")]
    [InlineData("sprk_regardingworkassignment", "sprk_workassignment", "WorkAssignment", "sprk_regardingworkassignment")]
    public async Task OtherCoreRecords_UseTheIntakeVocabulary(string column, string table, string expected, string submissionColumn)
    {
        var core = Guid.NewGuid();
        var invoice = CorrectionSeed.Invoice(coreColumn: column, coreEntity: table, coreId: core);
        var own = CorrectionSeed.Submission();
        own.Attributes.Remove("sprk_regardingmatter");
        own[submissionColumn] = new EntityReference(table, core);
        Seed(submission: own);
        _world.Intake.Invoices[OldInvoice] = invoice;

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("coreRecord").GetProperty("entityType").GetString().Should().Be(expected);
        body.GetProperty("coreRecord").GetProperty("entityId").GetGuid().Should().Be(core);
        body.GetProperty("coreRecord").GetProperty("name").GetString().Should().Be("Core record");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 200 with canCorrect false: the intake's reasons
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<int, int, string> NotInReview => new()
    {
        { InvoiceStatusCodes.StateActive, InvoiceStatusCodes.Approved, "Approved" },
        { InvoiceStatusCodes.StateActive, InvoiceStatusCodes.SentToAp, "SentToAp" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Paid, "Paid" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Rejected, "Rejected" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.ReturnedToFirm, "ReturnedToFirm" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Cancelled, "Cancelled" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Superseded, "Superseded" },
    };

    [Theory(DisplayName = "097e (must fire): an Approved, Sent to AP, Paid or inactive invoice is 200 canCorrect false, reason invoice_status, with its status name")]
    [MemberData(nameof(NotInReview))]
    public async Task NotInReview_IsCanCorrectFalse_InvoiceStatus(int state, int status, string name)
    {
        Seed(state, status);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.GetProperty("canCorrect").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("invoice_status");
        body.GetProperty("invoiceStatus").GetString().Should().Be(name);
    }

    [Fact(DisplayName = "097e: a status this lifecycle does not know is invoiceStatus null (as the intake leaves it absent), canCorrect false, invoice_status")]
    public async Task UnknownStatus_IsNull()
    {
        Seed(InvoiceStatusCodes.StateInactive, 659490099);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("invoiceStatus").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("canCorrect").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("invoice_status");
    }

    [Fact(DisplayName = "097e (must fire): an invoice with no submission is 200 canCorrect false, reason no_submission; tracking null, number empty")]
    public async Task NoSubmission_IsCanCorrectFalse()
    {
        Seed();
        _world.Intake.PriorSubmissions.Clear();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.GetProperty("canCorrect").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("no_submission");
        body.GetProperty("trackingNumber").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("firmInvoiceNumber").GetString().Should().BeEmpty();
    }

    [Fact(DisplayName = "097e (must fire): an invoice whose own submission is still Processing is 200 canCorrect false, reason submission_in_progress")]
    public async Task SubmissionInProgress_IsCanCorrectFalse()
    {
        Seed(submission: CorrectionSeed.Submission(status: InvoiceSubmissionStatus.Processing));

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("canCorrect").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("submission_in_progress");
    }

    [Fact(DisplayName = "097e: an invoice and a submission that disagree on the firm are reported as no_submission (a reason outside the prefill's three never leaves)")]
    public async Task SubmissionOfAnotherFirm_IsNoSubmission()
    {
        Seed(submission: CorrectionSeed.Submission(firm: Guid.NewGuid()));

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("canCorrect").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("no_submission");
    }

    public static TheoryData<string> RefusalCases => new() { "allowed", "invoice_status", "no_submission", "submission_in_progress" };

    [Theory(DisplayName = "097e: the prefill's canCorrect and reason are the intake guard's, for each refusal reason (one guard, asked of the same rows)")]
    [MemberData(nameof(RefusalCases))]
    public async Task Prefill_AgreesWithTheIntakeGuard(string scenario)
    {
        var invoice = scenario switch
        {
            "invoice_status" => Seed(InvoiceStatusCodes.StateActive, InvoiceStatusCodes.Approved),
            "submission_in_progress" => Seed(submission: CorrectionSeed.Submission(status: InvoiceSubmissionStatus.Processing)),
            _ => Seed(),
        };
        if (scenario == "no_submission")
        {
            _world.Intake.PriorSubmissions.Clear();
        }

        // The intake's side: the Correction guard, with the request the add-in would send from the prefill (the invoice's own firm, record, number).
        var (refusal, _) = await _world.Intake.Build().CheckCorrectionAsync(
            new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, W.MatterId), W.FirmId, CorrectionSeed.Number, invoice, CancellationToken.None);
        var body = await BodyAsync(await GetAsync());

        body.GetProperty("canCorrect").GetBoolean().Should().Be(refusal is null);
        if (refusal is null)
        {
            scenario.Should().Be("allowed");
            body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        }
        else
        {
            body.GetProperty("reason").GetString().Should().Be((string?)refusal.Extensions!["reason"]).And.Be(scenario);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 404 and 503
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "097e (seed-proof, the Write gate): a caller with only Read on the invoice gets the uniform 404, byte-for-byte the answer for an unknown invoice")]
    public async Task WithoutWrite_IsTheUniform404_SameAsAnUnknownInvoice()
    {
        Seed(rights: AccessRights.Read);

        var denied = await GetAsync();
        var unknown = await GetAsync(Guid.NewGuid());

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        deniedBody.Should().Contain("sdap.access.deny.record_unavailable");
        Normalise(await unknown.Content.ReadAsStringAsync()).Should().Be(Normalise(deniedBody), "the answer reveals nothing about the invoice");
        deniedBody.Should().NotContain(OldInvoice.ToString()).And.NotContain("Firm LLP").And.NotContain("INV-7");
    }

    [Fact(DisplayName = "097e: a step-0 refusal (an external caller) is the same uniform 404")]
    public async Task Step0Refusal_IsTheUniform404()
    {
        Seed();
        StepZero.Kind = StepZeroCallerKind.FlagTrue;

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.record_unavailable");
    }

    [Fact(DisplayName = "097e: a rights-read fault is the same uniform 404")]
    public async Task RightsReadFault_IsTheUniform404()
    {
        Seed();
        _throwFor.Add(OldInvoice);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.record_unavailable");
    }

    [Fact(DisplayName = "097e: an invoice that vanished after the rights check is the uniform 404 (the same bytes)")]
    public async Task InvoiceGoneAfterTheCheck_IsTheUniform404()
    {
        Seed();
        _world.Intake.Invoices.Remove(OldInvoice);
        _world.Intake.Invoices[Guid.NewGuid()] = CorrectionSeed.Invoice(); // the read faults NotFound only while some invoice is listed

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.record_unavailable");
    }

    [Fact(DisplayName = "097e: a read fault (the invoice's submission) is 503 record_unavailable, never a partial answer")]
    public async Task SubmissionReadFault_Is503()
    {
        Seed();
        _world.Intake.PriorSubmissionReadFailure = new TimeoutException();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response)).GetProperty("errorCode").GetString().Should().Be("ebilling.submission.record_unavailable");
    }

    [Fact(DisplayName = "097e: a read fault (the firm's name) is 503 record_unavailable")]
    public async Task FirmReadFault_Is503()
    {
        Seed();
        _world.Intake.FirmReadFailure = new TimeoutException();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response)).GetProperty("errorCode").GetString().Should().Be("ebilling.submission.record_unavailable");
    }

    [Fact(DisplayName = "097e: with Ebilling:Enabled false the route answers 503 ebilling.disabled")]
    public async Task KillSwitch_EbillingOff_Is503EbillingDisabled()
    {
        Seed();
        _world.Intake.Options.Enabled = false;

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response)).GetProperty("errorCode").GetString().Should().Be("ebilling.disabled");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // replacedBy (agreed with word-add-in-r1, 2026-10-31)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "097e: a Superseded invoice whose replacement the caller can Read names it in replacedBy (id and name)")]
    public async Task Superseded_WithReadOnTheReplacement_NamesIt()
    {
        SeedSuperseded(AccessRights.Read);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("invoiceStatus").GetString().Should().Be("Superseded");
        var replaced = body.GetProperty("replacedBy");
        replaced.EnumerateObject().Select(p => p.Name).Should().Equal("invoiceId", "name");
        replaced.GetProperty("invoiceId").GetGuid().Should().Be(ReplacementInvoice);
        replaced.GetProperty("name").GetString().Should().Be("INV-7 (corrected)");
    }

    [Fact(DisplayName = "097e (seed-proof, the Read check on the replacement): a Superseded invoice whose replacement the caller cannot Read has replacedBy null, and the answer does not name the replacement")]
    public async Task Superseded_WithoutReadOnTheReplacement_IsNull_AndRevealsNothing()
    {
        SeedSuperseded(AccessRights.None);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        (await BodyAsync(response)).GetProperty("replacedBy").ValueKind.Should().Be(JsonValueKind.Null);
        text.Should().NotContain(ReplacementInvoice.ToString()).And.NotContain("corrected");
    }

    [Fact(DisplayName = "097e: an invoice that is not Superseded has replacedBy null, even when a submission names its submission")]
    public async Task NotSuperseded_IsNull()
    {
        Seed();
        AddReplacement(ReplacementSubmissionId, ReplacementInvoice, InvoiceStatusCodes.InReview, "INV-7 (corrected)");
        _access.Grant(FinanceAuthorizationFilter.InvoiceEntitySet, ReplacementInvoice, AccessRights.Read);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("invoiceStatus").GetString().Should().Be("InReview");
        body.GetProperty("replacedBy").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "097e (deliberate): a fault finding the replacement leaves replacedBy null and the rest of the answer intact; it is not a 503")]
    public async Task Superseded_ReplacementReadFault_IsNull_NotA503()
    {
        SeedSuperseded();
        _world.Intake.ResubmissionReadFailure = new TimeoutException();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body.GetProperty("replacedBy").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("invoiceStatus").GetString().Should().Be("Superseded");
        body.GetProperty("reason").GetString().Should().Be("invoice_status");
    }

    [Fact(DisplayName = "097e (deliberate): a fault reading the caller's rights on the replacement leaves replacedBy null, not a 503")]
    public async Task Superseded_ReplacementRightsFault_IsNull_NotA503()
    {
        SeedSuperseded();
        _throwFor.Add(ReplacementInvoice);

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(response)).GetProperty("replacedBy").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "097e: with several replacements the one that is not itself Superseded is named; if that is still ambiguous, replacedBy is null")]
    public async Task Superseded_SeveralReplacements_TakesTheOneNotSuperseded_ElseNull()
    {
        SeedSuperseded();
        AddReplacement(SecondReplacementSubmissionId, SecondReplacementInvoice, InvoiceStatusCodes.Superseded, "INV-7 (first try)");
        _access.Grant(FinanceAuthorizationFilter.InvoiceEntitySet, SecondReplacementInvoice, AccessRights.Read);

        var named = await BodyAsync(await GetAsync());
        named.GetProperty("replacedBy").GetProperty("invoiceId").GetGuid().Should().Be(ReplacementInvoice);

        _world.Intake.Invoices[SecondReplacementInvoice]["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview);
        var ambiguous = await BodyAsync(await GetAsync());
        ambiguous.GetProperty("replacedBy").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "097e: the replacement is the DIRECT one: a Superseded replacement is still named when it is the only one (no chain is followed)")]
    public async Task Superseded_TheOnlyReplacementIsSuperseded_IsStillNamed()
    {
        SeedSuperseded();
        _world.Intake.Invoices[ReplacementInvoice]["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Superseded);

        var body = await BodyAsync(await GetAsync());

        body.GetProperty("replacedBy").GetProperty("invoiceId").GetGuid().Should().Be(ReplacementInvoice);
    }

    [Fact(DisplayName = "097e: the route runs the Finance filter with step 0, and a caller's rights are asked as the caller for the invoice and for the replacement")]
    public async Task RightsAreAskedAsTheCaller_ForBothInvoices()
    {
        SeedSuperseded();

        await GetAsync();

        _access.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.InvoiceEntitySet && c.Id == OldInvoice && c.HasToken);
        _access.Calls.Should().Contain(c => c.Set == FinanceAuthorizationFilter.InvoiceEntitySet && c.Id == ReplacementInvoice && c.HasToken);
    }
}
