using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The wire shape of the submission routes' own refusals (ebilling-r1 task 095 contract fix, 2026-10-29; contract section 6, "Client rule":
/// <c>code = errorCode ?? reasonCode</c>). The REAL endpoints, filters and Submit Invoice step 1 and step A, behind the BFF's REAL pipeline
/// (<c>UseSpaarkeMiddleware</c>, its global exception handler included), so a test sees a refusal exactly as a client reads it. Before the fix,
/// step A's 403 (<c>COMMUNICATION_REFILE_FORBIDDEN</c>) was an <c>SdapProblemException</c>, which that handler renders with the code only under a
/// nested <c>extensions.code</c>: a client following the contract found no code.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the field names and codes are a client contract (word-add-in-r1). Two routes are driven here: from-email (the real
/// <see cref="SubmitInvoiceFromEmailOperation"/> over the world's doubles) and from-document. The confirm route's twin is in
/// <c>FinanceEndpointsAuthorizationContractTests</c>.
/// </remarks>
public sealed class InvoiceSubmissionRefusalWireContractTests : IAsyncLifetime
{
    private const string FromEmailUrl = "/api/finance/invoice-submissions/from-email";
    private const string FromDocumentUrl = "/api/finance/invoice-submissions/from-document";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid SourceDocumentId = Guid.Parse("09500000-0000-4000-8000-0000000000d7");

    private readonly W _world = new();
    private readonly RecordingAccessDataSource _access = new();
    private readonly RecordProbe _probe = new();
    private readonly InMemoryTenantCache _cache = new();
    private readonly Mock<IGenericEntityService> _emlRows = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>The operation the from-email service calls: the world's double, or the real one when a test installs it.</summary>
    private ISubmitInvoiceFromEmail? _operation;

    public StatedCaller StepZero { get; private set; } = null!;

    public InvoiceSubmissionRefusalWireContractTests()
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
        builder.Services.AddCors();
        StepZero = StatedCaller.Register(builder.Services);
        builder.Services.AddRateLimiter(opt => opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

        builder.Services.AddSingleton<IAccessDataSource>(_access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton<CallerRecordAccessProbe>(_probe);
        builder.Services.AddSingleton<IOfficeRateLimitService>(new AllowAllRateLimits());
        builder.Services.AddSingleton<ITenantCache>(_cache);

        // Step 1 and step A (from-document and from-email): the app-only reader and A's isolation check are the world's.
        builder.Services.AddSingleton(_world.Intake.Entities.Object);
        builder.Services.AddSingleton(InvoiceSubmissionServiceTests.IsolationOver(() => _world.Intake.Isolation));

        builder.Services.AddSingleton(_world.Intake.Monitor);
        builder.Services.AddSingleton(_world.Intake.Build());
        builder.Services.AddSingleton(_world.Intake.Participations);
        builder.Services.AddSingleton(_world.Build(new Switch(this)));

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseSpaarkeMiddleware(); // the REAL pipeline: the global exception handler renders an SdapProblemException
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

    private Task<HttpResponseMessage> PostAsync(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return _client.SendAsync(request);
    }

    private static FromEmailSubmissionRequest FromEmailBody() => new()
    {
        TargetEntity = new FromEmailTargetEntity { EntityType = "Matter", EntityId = W.MatterId },
        Email = W.Email(),
        Marks = new List<FromEmailMark> { W.Mark(W.Invoice, "Invoice"), W.Mark(W.Timesheet, "Supporting") },
        FirmId = W.FirmId,
        FirmInvoiceNumber = "1001",
        Mode = "New",
    };

    private static object FromDocumentBody() => new
    {
        communicationId = W.CommunicationId,
        documentId = SourceDocumentId,
        coreRecord = new { kind = "matter", id = W.MatterId },
        firmId = W.FirmId,
        firmInvoiceNumber = "INV-95",
    };

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private void NothingWritten()
    {
        _world.Intake.Allocations.Should().BeEmpty("a refused submission creates no row");
        _world.Intake.JobsSent.Should().BeEmpty("a refused submission enqueues nothing");
        _world.Intake.Stages.Should().BeEmpty("a refused submission stages nothing");
        _world.Operation.Submits.Should().BeEmpty();
    }

    /// <summary>The wire shape of a route refusal: a TOP-LEVEL <c>errorCode</c>, the problem media type, no nested <c>extensions</c>.</summary>
    private static async Task<JsonElement> RefusalAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be(errorCode, "the client rule is errorCode ?? reasonCode");
        body.TryGetProperty("extensions", out _).Should().BeFalse("the code is not nested under extensions.code");
        return body;
    }

    /// <summary>An unfiled communication of a source document the caller may read, not write (step A refuses to file it).</summary>
    private void UnfiledSourceTheCallerMayOnlyRead(Guid documentId)
    {
        _world.Intake.ExistingDocument = new Entity("sprk_document", documentId)
        {
            ["sprk_graphdriveid"] = "b!drive",
            ["sprk_graphitemid"] = "item-95",
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", W.CommunicationId),
        };
        _world.Intake.Communications[W.CommunicationId] = new Entity("sprk_communication", W.CommunicationId); // no filing
        _probe.Rights[documentId] = AccessRights.Read;
        _probe.Rights[W.CommunicationId] = AccessRights.Read; // Read, no Write
    }

    /// <summary>The real Submit Invoice operation over the world's doubles (what <c>SubmitInvoiceFromEmailOperationTests</c> builds).</summary>
    private SubmitInvoiceFromEmailOperation RealOperation()
    {
        var h = _world.Intake;
        h.Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecordOwnershipContext c, CancellationToken _) => h.OwnerAnswer with { CreatedByPerson = c.RequestedBy?.SystemUserId });
        var eml = _world.AddEarlierEml(W.EmlId, null, W.Invoice, W.Timesheet);
        eml["versionnumber"] = 4711L;
        _emlRows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(eml);
        return new SubmitInvoiceFromEmailOperation(
            h.Build(), h.Allocator.Object, h.Files.Service, h.Jobs.Object,
            new EmlMarkedPartReader(_emlRows.Object, _world.Spe.Object, NullLogger<EmlMarkedPartReader>.Instance),
            NullLogger<SubmitInvoiceFromEmailOperation>.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step A's 403: COMMUNICATION_REFILE_FORBIDDEN, on every route that runs step A
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095 contract fix (from-document): step A's 403 is COMMUNICATION_REFILE_FORBIDDEN as a TOP-LEVEL errorCode; nothing of the submission is written; nothing was filed so no filingKept")]
    public async Task FromDocument_StepA_WithoutWrite_Is403_WithATopLevelErrorCode_AndNothingIsWritten()
    {
        UnfiledSourceTheCallerMayOnlyRead(SourceDocumentId);

        var response = await PostAsync(FromDocumentUrl, FromDocumentBody());

        var body = await RefusalAsync(response, HttpStatusCode.Forbidden, CommunicationRecordAuthorizationFilter.RefileDenyCode);
        body.GetProperty("detail").GetString().Should().Be(InvoiceSubmissionEndpoints.RefileForbiddenDetail);
        body.TryGetProperty("filingKept", out _).Should().BeFalse("step A filed nothing");
        NothingWritten();
    }

    [Fact(DisplayName = "095 contract fix (from-email): step A's 403 is COMMUNICATION_REFILE_FORBIDDEN as a TOP-LEVEL errorCode; nothing of the submission is written; nothing was filed so no filingKept")]
    public async Task FromEmail_StepA_WithoutWrite_Is403_WithATopLevelErrorCode_AndNothingIsWritten()
    {
        _operation = RealOperation();
        UnfiledSourceTheCallerMayOnlyRead(W.EmlId);

        var response = await PostAsync(FromEmailUrl, FromEmailBody());

        var body = await RefusalAsync(response, HttpStatusCode.Forbidden, CommunicationRecordAuthorizationFilter.RefileDenyCode);
        body.GetProperty("detail").GetString().Should().Be(InvoiceSubmissionEndpoints.RefileForbiddenDetail);
        body.TryGetProperty("filingKept", out _).Should().BeFalse("step A filed nothing");
        _world.Intake.Allocations.Should().BeEmpty("nothing of the submission is written");
        _world.Intake.JobsSent.Should().BeEmpty();
        _world.Intake.Stages.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // filingKept: a refusal after step A filed the communication (contract section 6, "After step A")
    // ═══════════════════════════════════════════════════════════════════════════

    private const string Sentence = "The communication was filed under the record first; that filing is kept.";

    [Fact(DisplayName = "095 contract fix (from-email): a refusal after step A filed the communication carries filingKept: true and the unchanged sentence; its errorCode stays")]
    public async Task FromEmail_RefusalAfterFiling_CarriesFilingKept_AndTheUnchangedSentence()
    {
        _operation = _world.Operation;
        _world.Operation.Answer = _ => FromEmailSubmitResult.Refused(
            StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.Failed,
            InvoiceSubmissionService.AfterFilingDetail("The invoice could not be submitted right now. Try again shortly."), filed: true);

        var response = await PostAsync(FromEmailUrl, FromEmailBody());

        var body = await RefusalAsync(response, HttpStatusCode.ServiceUnavailable, InvoiceSubmissionErrors.Failed);
        body.GetProperty("filingKept").GetBoolean().Should().BeTrue();
        body.GetProperty("detail").GetString().Should().EndWith(Sentence);
    }

    [Fact(DisplayName = "095 contract fix (from-email): a refusal with nothing filed has neither filingKept nor the sentence")]
    public async Task FromEmail_RefusalWithNothingFiled_HasNoFilingKept_AndNoSentence()
    {
        _operation = _world.Operation;
        _world.Operation.Answer = _ => FromEmailSubmitResult.Refused(
            StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.Failed, "The invoice could not be submitted right now. Try again shortly.");

        var response = await PostAsync(FromEmailUrl, FromEmailBody());

        var body = await RefusalAsync(response, HttpStatusCode.ServiceUnavailable, InvoiceSubmissionErrors.Failed);
        body.TryGetProperty("filingKept", out _).Should().BeFalse();
        body.GetProperty("detail").GetString().Should().NotContain("filing is kept");
    }

    [Fact(DisplayName = "095 contract fix (from-email): a reasonCode-only answer after step A filed (the owner refusal) keeps its reasonCode and gains filingKept: true and the sentence")]
    public async Task FromEmail_ReasonCodeOnlyAnswerAfterFiling_CarriesFilingKept_AndTheSentence()
    {
        _operation = _world.Operation;
        _world.Operation.Answer = _ => throw new FromEmailRouteAnswerException(
            ProblemDetailsHelper.RecordOwnerRefused("record_owner.no_owner_source", "no owner team", "invoice submission", "trace-95"), filingKept: true);

        var response = await PostAsync(FromEmailUrl, FromEmailBody());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body.GetProperty("reasonCode").GetString().Should().Be("record_owner.no_owner_source");
        body.TryGetProperty("errorCode", out _).Should().BeFalse("its code field is left as it was");
        body.GetProperty("filingKept").GetBoolean().Should().BeTrue();
        body.GetProperty("detail").GetString().Should().EndWith(Sentence);
    }

    [Fact(DisplayName = "095 contract fix (from-email): a reasonCode-only answer with nothing filed has neither filingKept nor the sentence")]
    public async Task FromEmail_ReasonCodeOnlyAnswerWithNothingFiled_HasNoFilingKept()
    {
        _operation = _world.Operation;
        _world.Operation.Answer = _ => throw new FromEmailRouteAnswerException(
            ProblemDetailsHelper.RecordOwnerRefused("record_owner.no_owner_source", "no owner team", "invoice submission", "trace-95"));

        var response = await PostAsync(FromEmailUrl, FromEmailBody());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body.TryGetProperty("filingKept", out _).Should().BeFalse();
        body.GetProperty("detail").GetString().Should().NotContain("filing is kept");
    }

    [Fact(DisplayName = "095 contract fix (from-email): the loser of a create race, whose request filed, gets its replay's refusal with filingKept: true and the sentence")]
    public async Task FromEmail_ARaceLoserThatFiled_GetsFilingKept_OnItsReplayRefusal()
    {
        _operation = _world.Operation;
        _world.Operation.Answer = input =>
        {
            // Another caller's request created the attempt while this one filed and then lost the race.
            _world.AddAttempt(FromEmailSubmissionKey.Of(InvoiceSubmissionFromEmailHttp.ToInput(FromEmailBody())), 1, InvoiceSubmissionStatus.Received, creator: W.OtherCallerId);
            return FromEmailSubmitResult.AlreadyCreated(filed: true);
        };

        var response = await PostAsync(FromEmailUrl, FromEmailBody());

        var body = await RefusalAsync(response, HttpStatusCode.Conflict, InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        body.GetProperty("filingKept").GetBoolean().Should().BeTrue();
        body.GetProperty("detail").GetString().Should().EndWith(Sentence);
    }

    [Fact(DisplayName = "095 contract fix (from-document route): InvoiceSubmissionHttp.ToResult - an errorCode refusal after filing, a reasonCode-only owner refusal after filing, and one with nothing filed")]
    public async Task ToResult_AfterFiling_CarriesFilingKept_AndNothingFiled_DoesNot()
    {
        var refused = InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadySubmitted, "Already submitted. Nothing was saved.");
        var owner = InvoiceSubmissionOutcome.OwnerRefused(RecordOwnerResolution.Refused("record_owner.no_owner_source", "no owner team"));

        var (_, afterFiling) = await WireBody.OfAsync(InvoiceSubmissionHttp.ToResult(InvoiceSubmissionService.AfterFiling(refused, filed: true), id => id.ToString(), "trace"));
        var (_, ownerAfterFiling) = await WireBody.OfAsync(InvoiceSubmissionHttp.ToResult(InvoiceSubmissionService.AfterFiling(owner, filed: true), id => id.ToString(), "trace"));
        var (_, nothingFiled) = await WireBody.OfAsync(InvoiceSubmissionHttp.ToResult(InvoiceSubmissionService.AfterFiling(refused, filed: false), id => id.ToString(), "trace"));
        var (_, ownerNothingFiled) = await WireBody.OfAsync(InvoiceSubmissionHttp.ToResult(InvoiceSubmissionService.AfterFiling(owner, filed: false), id => id.ToString(), "trace"));

        afterFiling.GetProperty("filingKept").GetBoolean().Should().BeTrue();
        afterFiling.GetProperty("errorCode").GetString().Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        afterFiling.GetProperty("detail").GetString().Should().Be("Already submitted. No submission was saved. " + Sentence);
        ownerAfterFiling.GetProperty("filingKept").GetBoolean().Should().BeTrue();
        WireBody.Code(ownerAfterFiling).Should().Be("record_owner.no_owner_source", "its reasonCode is left as it was");
        ownerAfterFiling.TryGetProperty("errorCode", out _).Should().BeFalse();
        ownerAfterFiling.GetProperty("detail").GetString().Should().EndWith(Sentence);
        nothingFiled.TryGetProperty("filingKept", out _).Should().BeFalse();
        nothingFiled.GetProperty("detail").GetString().Should().Be("Already submitted. Nothing was saved.");
        ownerNothingFiled.TryGetProperty("filingKept", out _).Should().BeFalse();
        ownerNothingFiled.GetProperty("detail").GetString().Should().NotContain("filing is kept");
    }

    [Fact(DisplayName = "095 contract fix (Office pass-through): the in-process save's own refusal, answered after filing, is extended with filingKept: true and the sentence; its code stays. Nothing filed: unchanged")]
    public async Task OfficePassThrough_AfterFiling_CarriesFilingKept_AndNothingFiled_DoesNot()
    {
        var error = new SaveError { Code = "OFFICE_012", Message = "Upload failed", Retryable = true };
        var outcome = FromEmailSubmissionOutcome.Office(error);

        // The service flags it (an Office answer has no errorCode to extend); the route extends the problem it renders.
        FromEmailSubmissionService.AfterFiling(outcome, filed: true).FilingKept.Should().BeTrue();
        FromEmailSubmissionService.AfterFiling(outcome, filed: false).FilingKept.Should().BeFalse();

        var (_, kept) = await WireBody.OfAsync(InvoiceSubmissionHttp.WithFilingKept(OfficeEndpoints.MapSaveErrorToProblem(error, "trace")));
        var (_, untouched) = await WireBody.OfAsync(OfficeEndpoints.MapSaveErrorToProblem(error, "trace"));

        kept.GetProperty("filingKept").GetBoolean().Should().BeTrue();
        kept.GetProperty("errorCode").GetString().Should().Be(untouched.GetProperty("errorCode").GetString(), "its code field is left as it was");
        kept.GetProperty("detail").GetString().Should().EndWith(Sentence);
        untouched.TryGetProperty("filingKept", out _).Should().BeFalse();
        untouched.GetProperty("detail").GetString().Should().NotContain("filing is kept");
    }

    // ── Doubles ──

    private sealed class Switch(InvoiceSubmissionRefusalWireContractTests test) : ISubmitInvoiceFromEmail
    {
        private ISubmitInvoiceFromEmail Current => test._operation ?? test._world.Operation;

        public bool IsAvailable => Current.IsAvailable;

        public Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct) => Current.SubmitAsync(input, ct);

        public Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct) =>
            Current.RequeueAsync(submissionId, jobId, correlationId, ct);

        public Task<FromEmailAttemptSource?> ReadAttemptSourceAsync(Guid submissionId, CancellationToken ct) =>
            Current.ReadAttemptSourceAsync(submissionId, ct);
    }

    private sealed class RecordProbe : CallerRecordAccessProbe
    {
        public RecordProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public Dictionary<Guid, AccessRights> Rights { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default) =>
            Task.FromResult(Rights.TryGetValue(recordId, out var rights) ? rights : AccessRights.None);
    }

    private sealed class AllowAllRateLimits : IOfficeRateLimitService
    {
        public Task<RateLimitResult> CheckAndIncrementAsync(string tenantId, string userId, OfficeRateLimitCategory category) =>
            Task.FromResult(new RateLimitResult
            {
                IsAllowed = true, Limit = 10, Remaining = 9, ResetTimestamp = DateTimeOffset.UtcNow.AddSeconds(30), RetryAfterSeconds = 30,
            });
    }
}
