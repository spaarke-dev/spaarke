using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The invoice-adjustment routes through the REAL <c>MapInvoiceAdjustmentEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 102, FR-32, ADR-008, ADR-019, D-74, D-77): 401
/// unauthenticated, 403 for a caller without the right on the invoice (the core-record access guard), the stable <c>ebilling.disabled</c> 503,
/// 409 when the invoice is not In Review, the uniform 404, and the stable <c>errorCode</c> of each 400.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake) and at <see cref="IGenericEntityService"/> under the REAL
/// <see cref="InvoiceAdjustmentService"/>, with a STRICT mock: a denied request is shown to read nothing and write nothing. The service's own
/// behaviour is <c>InvoiceAdjustmentServiceTests</c>.
/// <para><b>Seeding proof (task 102 acceptance).</b> <c>Denied_WithoutTheRight_ReadsAndWritesNothing</c> fails when the route filter is removed from
/// a route: the request reaches the service, the strict Dataverse mock throws, and the status is not 403. Recorded in
/// <c>projects/ebilling-r1/notes/102-invoice-adjustments.md</c>.</para>
/// </remarks>
public sealed class InvoiceAdjustmentEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // FinanceAuthorizationFilter.InvoiceEntitySet

    private static readonly Guid InvoiceId = Guid.Parse("21000000-0000-4000-8000-0000000000a1");
    private static readonly Guid LineId = Guid.Parse("21000000-0000-4000-8000-0000000000b1");
    private static readonly Guid Reviewer = Guid.Parse("21000000-0000-4000-8000-0000000000c1");

    private static string Rules => $"/api/finance/invoices/{InvoiceId}/adjustment-rules";

    private static string Adjustments => $"/api/finance/invoices/{InvoiceId}/adjustments";

    private static string One(Guid? line = null) => $"/api/finance/invoices/{InvoiceId}/adjustments/{line ?? LineId}";

    private static object ValidBody() => new { adjustmentId = Guid.NewGuid(), method = "percent", value = 10, reason = "Volume discount" };

    /// <summary>One request per route, with a body that is valid for it.</summary>
    public static IEnumerable<object[]> Routes() =>
    [
        ["GET", Rules, null!],
        ["POST", Adjustments, ValidBody()],
        ["DELETE", One(), null!],
    ];

    public static IEnumerable<object[]> WriteRoutes() => Routes().Skip(1);

    // =========================================================================================
    // Authorization (ADR-008)
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Unauthenticated_Returns401_AndReadsNothing(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(method, url, body, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Denied_WithoutTheRight_ReadsAndWritesNothing(string method, string url, object? body)
    {
        // The core-record access guard: a caller with no right on the invoice (which inherits from its core record) is refused before any service runs.
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    public static IEnumerable<object[]> RoutesRefused() => StatedCaller.Cross(Routes(), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> RoutesInternal() => StatedCaller.Cross(Routes(), StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: every adjustment route refuses a flagged-external, row-less or unreadable caller with 403, before any rights call, even one holding Read and Write")]
    [MemberData(nameof(RoutesRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string method, string url, object? body, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read | AccessRights.Write | AccessRights.Append);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, url, body));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: every adjustment route with a flag that reads false or blank reaches the record check as before")]
    [MemberData(nameof(RoutesInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string method, string url, object? body, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task WriteRoutes_NeedWrite_ReadAloneIsNotEnough(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RuleListRoute_NeedsRead_WriteAloneIsNotEnough()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Request("GET", Rules, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task RightOnAnotherInvoice_IsNotRightOnThisOne_AndAQueryIdCannotSubstitute(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(Request(method, url + $"?invoiceId={decoy}", body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(InvoiceId);
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.Entities.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // The eBilling kill switch (ADR-018): after authorization, never instead of it
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantAll();

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_UnauthorizedCaller_StillGets403(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================================
    // Status and stable code of each refusal
    // =========================================================================================

    [Fact]
    public async Task Writes_NotInReview_Return409_ForBothWriteRoutes()
    {
        foreach (var route in WriteRoutes())
        {
            await using var host = await Host.StartAsync();
            host.GrantAll();
            host.Invoice = InvoiceRow(InvoiceStatusCodes.Approved);

            var response = await host.SendAsync(Request((string)route[0], (string)route[1], route[2]));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict, (string)route[1]);
            (await ErrorCode(response)).Should().Be("ebilling.invoice.not_in_review");
            host.Writes.Should().Be(0);
        }
    }

    [Fact]
    public async Task Remove_AnAdjustmentThatIsNotTheInvoices_Returns404Uniform_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        var foreign = Guid.NewGuid();
        host.Lines.Add(new Entity("sprk_billingevent", foreign)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()), // an Adjustment line of ANOTHER invoice
            ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Adjustment),
            ["statecode"] = new OptionSetValue(0),
            ["versionnumber"] = 3L,
        });
        var fee = FeeRow(500m); // a line of THIS invoice that is not an Adjustment
        host.Lines.Add(fee);

        foreach (var id in new[] { foreign, fee.Id })
        {
            var response = await host.SendAsync(Request("DELETE", One(id), null));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(id.ToString());
            JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        }

        host.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Add_TheApprovedTotalWouldGoNegative_Returns400_WithItsCode_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview, billed: 100m);

        var response = await host.SendAsync(Request("POST", Adjustments, new { adjustmentId = Guid.NewGuid(), method = "fixed", value = 100.01, reason = "Too much" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("ebilling.adjustment.negative_total");
        host.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("percent", 10, null, "ebilling.adjustment.reason_required")]
    [InlineData("percent", 10, "  ", "ebilling.adjustment.reason_required")]
    [InlineData("amount", 10, "r", "ebilling.adjustment.method_invalid")]
    [InlineData(null, 10, "r", "ebilling.adjustment.method_invalid")]
    [InlineData("percent", 100.5, "r", "ebilling.adjustment.value_invalid")]
    [InlineData("percent", 0, "r", "ebilling.adjustment.value_invalid")]
    [InlineData("fixed", -3, "r", "ebilling.adjustment.value_invalid")]
    [InlineData("fixed", null, "r", "ebilling.adjustment.value_invalid")]
    public async Task Add_InvalidRequests_Return400_WithStableCodes_BeforeAnyRead(string? method, object? value, string? reason, string code)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", Adjustments, new { adjustmentId = Guid.NewGuid(), method, value, reason }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(code);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Add_ARuleThatIsNotAnOption_Returns400_WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);

        var response = await host.SendAsync(Request("POST", Adjustments, new { adjustmentId = Guid.NewGuid(), method = "percent", value = 5, reason = "r", ruleId = Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("ebilling.adjustment.rule_not_allowed");
        host.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Add_WithoutABody_Returns400()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", Adjustments, null));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Writes_WithAnUnprovenIdentity_Return403_AndReadNothing(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("ebilling.decider.unresolved");
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Add_Succeeds_With200_TheReviewerIsTheCallersOwnSystemUser_AndTheBodyCarriesNoAmount()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        host.Lines.Add(FeeRow(1000m));
        var id = Guid.NewGuid();

        // The body names a reviewer and an amount: neither is read.
        var response = await host.SendAsync(Request("POST", Adjustments, new
        {
            adjustmentId = id, method = "percent", value = 10, reason = "Volume discount", reviewer = Guid.NewGuid(), amount = 999999,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["adjustment"]!["method"]!.GetValue<string>().Should().Be("Percent");
        json["adjustment"]!["adjustmentId"]!.GetValue<Guid>().Should().Be(id);
        json["alreadyApplied"]!.GetValue<bool>().Should().BeFalse();
        host.Probe.SystemUserIdCalls.Should().Be(1);
        host.Writes.Should().Be(1);
        host.Created.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(Reviewer);
    }

    [Fact]
    public async Task Remove_Succeeds_With200()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        var adjustment = new Entity("sprk_billingevent", LineId)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Adjustment),
            ["statecode"] = new OptionSetValue(0),
            ["versionnumber"] = 3L,
        };
        host.Lines.Add(adjustment);

        var response = await host.SendAsync(Request("DELETE", One(), null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["removedLineId"]!.GetValue<Guid>().Should().Be(LineId);
        host.Writes.Should().Be(1);
    }

    // =========================================================================================
    // The rule list
    // =========================================================================================

    [Fact]
    public async Task RuleList_AuthorizedCaller_Gets200_WithTheOptions_AndNoDocumentLink()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);

        var response = await host.SendAsync(Request("GET", Rules, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(text)!.AsObject();
        json["asOf"]!.GetValue<string>().Should().Be("2026-09-15");
        var rule = json["rules"]!.AsArray().Should().ContainSingle().Subject!;
        rule["ruleSetName"]!.GetValue<string>().Should().Be("Acme Billing Guidelines");
        rule["version"]!.GetValue<int>().Should().Be(3);
        rule["clauseReference"]!.GetValue<string>().Should().Contain("Volume discounts");
        text.ToLowerInvariant().Should().NotContain("document").And.NotContain("url");
    }

    [Fact]
    public async Task RuleList_InvoiceDeletedAfterTheCheck_Returns404Uniform()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);
        host.Invoice = null;

        var response = await host.SendAsync(Request("GET", Rules, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(InvoiceId.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

    private static Entity InvoiceRow(int status, decimal billed = 1000m) => new("sprk_invoice", InvoiceId)
    {
        ["statuscode"] = new OptionSetValue(status),
        ["sprk_totalamount"] = new Money(billed),
        ["sprk_invoicedate"] = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
        ["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.NewGuid()),
        ["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid()),
    };

    private static Entity FeeRow(decimal amount) => new("sprk_billingevent", Guid.NewGuid())
    {
        ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        ["sprk_linesequence"] = 1,
        ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
        ["sprk_amount"] = new Money(amount),
        ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted),
        ["statecode"] = new OptionSetValue(0),
        ["versionnumber"] = 7L,
    };

    private static HttpRequestMessage Request(string method, string url, object? body, bool authenticated = true)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        }

        return request;
    }

    private sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private int _writes;

        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new() { CallerSystemUserId = Reviewer };
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public Entity? Invoice { get; set; }
        public List<Entity> Lines { get; } = [];
        public List<Entity> Created { get; } = [];

        /// <summary>How many writes (conditional updates, transactions) reached Dataverse.</summary>
        public int Writes => _writes;

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
        {
            var host = new Host();
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
            host.StepZero = StatedCaller.Register(builder.Services); // task 099: every /api/finance route runs step 0
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);

            // The REAL service over a strict Dataverse seam that answers reads from the fixture and counts writes.
            host.Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) => Task.FromResult(q.EntityName switch
                {
                    "sprk_invoice" => new EntityCollection(host.Invoice is null ? [] : [host.Invoice]),
                    "sprk_billingevent" => new EntityCollection(host.Lines
                        .Where(l => q.Criteria.Conditions.All(c => c.AttributeName != "sprk_billingeventid") || l.Id == (Guid)q.Criteria.Conditions
                            .First(c => c.AttributeName == "sprk_billingeventid").Values[0])
                        .ToList()),
                    "sprk_ruleset" => new EntityCollection([new Entity("sprk_ruleset", RuleSetId) { ["sprk_name"] = "Acme Billing Guidelines" }]),
                    _ => new EntityCollection(),
                }));
            host.Entities
                .Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                    It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns(() => { Interlocked.Increment(ref host._writes); return Task.CompletedTask; });
            host.Entities
                .Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    Interlocked.Increment(ref host._writes);
                    host.Created.AddRange(requests.OfType<Microsoft.Xrm.Sdk.Messages.CreateRequest>().Select(r => r.Target));
                    return Task.CompletedTask;
                });

            var totals = new Mock<IInvoiceTotalsService>();
            totals.Setup(t => t.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InvoiceTotalsCalculation(1000m, 0m, 0m, 100m, 900m, new Dictionary<Guid, decimal>(), null));
            var locks = new Mock<IIdempotencyService>();
            locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var ownership = new Mock<IRecordOwnershipResolver>();
            ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RecordOwnerResolution.Owned(Guid.NewGuid()));
            builder.Services.AddScoped(_ => new InvoiceAdjustmentService(
                host.Entities.Object, totals.Object, locks.Object, ownership.Object,
                // One published billing rule in force on the invoice date; the real selection and gate run over it.
                (_, _) => Task.FromResult<IReadOnlyList<BillingRuleCandidate>>([new BillingRuleCandidate
                {
                    PolicyId = Guid.NewGuid(), PolicyCode = "BILL-DISCOUNT", PolicyVersionId = Guid.NewGuid(), RuleSetId = RuleSetId,
                    Scope = RuleSetScope.ClientDefault, RuleType = ((int)Sprk.Bff.Api.Services.Signals.RuleType.Discount).ToString(),
                    ClauseReference = "Volume discounts: 5% above 10,000 dollars a month.", IsCurrent = true, VersionNumber = 3,
                    InForceFrom = new DateOnly(2026, 1, 1),
                }]),
                NullLogger<InvoiceAdjustmentService>.Instance));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceAdjustmentEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        private static readonly Guid RuleSetId = Guid.Parse("21000000-0000-4000-8000-0000000000d1");

        public void GrantAll() => Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read | AccessRights.Write);

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
