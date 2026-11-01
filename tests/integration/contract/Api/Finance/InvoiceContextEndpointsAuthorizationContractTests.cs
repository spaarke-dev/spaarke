using System.Net;
using System.Net.Http.Headers;
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
using Sprk.Bff.Api.Services.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// <c>GET /api/finance/invoices/{invoiceId}/context</c> through the REAL <c>MapInvoiceContextEndpoints</c>,
/// <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/>
/// (ebilling-r1 task 106, FR-33, ADR-008, ADR-019): 401 unauthenticated, 403 for a caller who cannot Read the invoice, the stable
/// <c>ebilling.disabled</c> 503 when the flag is off, and the HTTP shape of the 200 / degraded-200 / 404.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake answering "what may this caller do to record X of set S") and at
/// <see cref="IGenericEntityService"/> under the REAL <see cref="InvoiceContextService"/>, so a denied request is shown to read nothing from
/// Dataverse. The service's own behaviour is <c>InvoiceContextServiceTests</c>.
/// </remarks>
public sealed class InvoiceContextEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // live EntityDefinitions(LogicalName='sprk_invoice'), 2026-10-01 (FinanceAuthorizationFilter.InvoiceEntitySet)

    private static readonly Guid InvoiceId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static string Url(Guid id) => $"/api/finance/invoices/{id}/context";

    // =========================================================================================
    // Authorization (ADR-008)
    // =========================================================================================

    [Fact]
    public async Task Unauthenticated_Returns401_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(Url(InvoiceId), authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CallerWithNoRightsOnTheInvoice_Returns403_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    public static IEnumerable<object[]> InternalKinds => StatedCaller.InternalKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "Task 099 must-fire: /context refuses a flagged-external, row-less or unreadable caller with 403, before any rights call or read, even when it holds Read")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "Task 099 must-not-fire: /context with a flag that reads false or blank reaches the record check as before")]
    [MemberData(nameof(InternalKinds))]
    public async Task StepZero_AdmitsAnInternalCaller(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CallerWithoutReadOnTheInvoice_IsDenied_EvenWithOtherRights()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadOnAnotherInvoice_IsNotReadOnThisOne_AndAQueryIdCannotSubstitute()
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read);

        var request = Request(Url(InvoiceId) + $"?invoiceId={decoy}");
        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(InvoiceId);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough()
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.Entities.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // The eBilling kill switch (ADR-018): after authorization, never instead of it
    // =========================================================================================

    [Fact]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantRead();

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FlagOff_UnauthorizedCaller_StillGets403_TheFlagNeverReplacesAuthorization()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================================
    // 200 and its shape
    // =========================================================================================

    [Fact]
    public async Task AuthorizedCaller_Gets200_WithFiveSections_ScopedToTheInvoicesOwnFirmAndCoreRecord()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Header = Header(matter: true);
        host.GrantCoreRead();

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["invoiceId"]!.GetValue<Guid>().Should().Be(InvoiceId);
        foreach (var section in new[] { "coreRecord", "budget", "priorInvoices", "disallowHistory", "rateHistory" })
        {
            json[section]!["status"]!.GetValue<string>().Should().Be("available", section);
        }

        json["coreRecord"]!["data"]!["type"]!.GetValue<string>().Should().Be("matter");
        json["budget"]!["data"]!["spendToDate"]!.GetValue<decimal>().Should().Be(10000m);
        host.Queries.Select(q => q.EntityName).Should().Contain("sprk_invoice");
    }

    [Fact]
    public async Task AnInvoiceReaderWithoutReadOnTheMatter_NeverSeesTheMatterBudgetOrDetails()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();   // Read on the invoice, none on its matter (sprk_matters)
        host.Header = Header(matter: true);

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!.AsObject();
        json["coreRecord"]!["code"]!.GetValue<string>().Should().Be("context.core-record.access-denied");
        json["budget"]!["code"]!.GetValue<string>().Should().Be("context.budget.access-denied");
        json["coreRecord"]!["data"].Should().BeNull();
        json["budget"]!["data"].Should().BeNull();
        body.Should().NotContain("Acme v. Globex").And.NotContain("10000");
        host.Access.Calls.Should().Contain(new FinAuth.AccessCall(
            FinAuth.AccessPath.Record, "sprk_matters", Guid.Parse("00000000-0000-0000-0000-0000000000b1"), HasToken: true));
        host.Queries.Select(q => q.EntityName).Should().NotContain("sprk_matter", "the matter row and its rollups were never read");
    }

    [Fact]
    public async Task EverySourceFailing_StillReturns200_WithAMessageAndStableCodePerSection()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Header = Header(matter: true);
        host.GrantCoreRead();
        host.FailEverythingButTheHeader = true;

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "FR-33: the panel degrades with a message, not an error");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var expected = new Dictionary<string, string>
        {
            ["coreRecord"] = "context.core-record.unavailable",
            ["budget"] = "context.budget.unavailable",
            ["priorInvoices"] = "context.prior-invoices.unavailable",
            ["disallowHistory"] = "context.disallow-history.unavailable",
            ["rateHistory"] = "context.rate-history.unavailable",
        };
        foreach (var (section, code) in expected)
        {
            json[section]!["status"]!.GetValue<string>().Should().Be("unavailable", section);
            json[section]!["code"]!.GetValue<string>().Should().Be(code);
            json[section]!["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            json[section]!["data"].Should().BeNull();
        }
    }

    [Fact]
    public async Task ASiblingInvoiceTheCallerCannotRead_NeverAppearsInTheResponse()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Header = Header(matter: true);
        host.GrantCoreRead();
        var readable = Guid.Parse("00000000-0000-0000-0000-00000000e001");
        var hidden = Guid.Parse("00000000-0000-0000-0000-00000000e002");
        host.Siblings.Add(Sibling(readable, "INV-READABLE"));
        host.Siblings.Add(Sibling(hidden, "INV-HIDDEN"));
        host.Access.Grant(InvoiceSet, readable, AccessRights.Read);   // Write on the hidden one is not Read
        host.Access.Grant(InvoiceSet, hidden, AccessRights.Write);

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!.AsObject();
        json["priorInvoices"]!["data"]!["items"]!.AsArray().Select(i => i!["number"]!.GetValue<string>()).Should().Equal("INV-READABLE");
        body.Should().NotContain("INV-HIDDEN").And.NotContain(hidden.ToString());
        host.Access.Calls.Should().Contain(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, hidden, HasToken: true),
            "the siblings are put to the caller's own rights, with the caller's token");
    }

    private static Entity Sibling(Guid id, string number)
    {
        var e = new Entity("sprk_invoice", id);
        e["sprk_invoicenumber"] = number;
        e["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview);
        return e;
    }

    [Fact]
    public async Task WorkAssignmentInvoice_Returns200_WithBudgetNotTracked()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Header = Header(matter: false);
        host.GrantCoreRead();

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["budget"]!["status"]!.GetValue<string>().Should().Be("notTracked");
        json["budget"]!["code"]!.GetValue<string>().Should().Be("context.budget.not-tracked");
    }

    [Fact]
    public async Task InvoiceDeletedAfterTheCheck_Returns404_TheUniformShape()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Header = null;

        var response = await host.SendAsync(Request(Url(InvoiceId)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(InvoiceId.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static Entity Header(bool matter)
    {
        var e = new Entity("sprk_invoice", InvoiceId);
        e["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.Parse("00000000-0000-0000-0000-0000000000f1"));
        if (matter)
        {
            e["sprk_matter"] = new EntityReference("sprk_matter", Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
        }
        else
        {
            e["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", Guid.Parse("00000000-0000-0000-0000-0000000000d1"));
        }

        return e;
    }

    private static HttpRequestMessage Request(string url, bool authenticated = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
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

        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public List<QueryExpression> Queries { get; } = [];
        public Entity? Header { get; set; }
        public bool FailEverythingButTheHeader { get; set; }
        public List<Entity> Siblings { get; } = [];

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

            // The REAL service over a fake Dataverse seam that records every query.
            host.Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) => host.Answer(q));
            builder.Services.AddScoped(sp => new InvoiceContextService(
                host.Entities.Object, sp.GetRequiredService<AuthorizationService>(), NullLogger<InvoiceContextService>.Instance));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceContextEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantRead() => Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);

        /// <summary>Read on the invoice's core record (the matter, or the work assignment) - a separate right from Read on the invoice.</summary>
        public void GrantCoreRead()
        {
            Access.Grant("sprk_matters", Guid.Parse("00000000-0000-0000-0000-0000000000b1"), AccessRights.Read);
            Access.Grant("sprk_workassignments", Guid.Parse("00000000-0000-0000-0000-0000000000d1"), AccessRights.Read);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

        private Task<EntityCollection> Answer(QueryExpression q)
        {
            Queries.Add(q);

            if (q.EntityName == "sprk_invoice" && q.ColumnSet.Columns.Contains("sprk_vendororg"))
            {
                return Task.FromResult(Header is null ? new EntityCollection() : new EntityCollection([Header]));
            }

            if (FailEverythingButTheHeader)
            {
                return Task.FromException<EntityCollection>(new InvalidOperationException("source down"));
            }

            if (q.EntityName == "sprk_matter" && q.ColumnSet.Columns.Contains("statuscode"))
            {
                var core = new Entity("sprk_matter", Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
                core["sprk_mattername"] = "Acme v. Globex";
                core["statuscode"] = new OptionSetValue(659490002);
                return Task.FromResult(new EntityCollection([core]));
            }

            if (q.EntityName == "sprk_matter")
            {
                var budget = new Entity("sprk_matter", Guid.Parse("00000000-0000-0000-0000-0000000000b1"));
                budget["sprk_totalspendtodate"] = new Money(10000m);
                return Task.FromResult(new EntityCollection([budget]));
            }

            if (q.EntityName == "sprk_invoice")
            {
                return Task.FromResult(new EntityCollection(Siblings.ToList()));
            }

            return Task.FromResult(new EntityCollection());
        }

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
