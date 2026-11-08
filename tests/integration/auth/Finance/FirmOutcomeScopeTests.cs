using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// The firm outcome routes (ebilling-r1 task 130b, FR-38, NFR-10, D-70) over the REAL <see cref="FirmOutcomeEndpoints"/>, the REAL
/// <see cref="FirmOutcomeAuthorizationFilter"/> (through the real mapper), the caller-principal group filter, the REAL eBilling kill switch,
/// the REAL <see cref="FirmFindingsProjection"/> and the REAL <see cref="InvoiceSubmissionService"/>, substituted only at Dataverse (the
/// app-only readers), the caller's firms (the membership read) and the (a') visibility seams (whose own rules have their own suites).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): this is the NFR-10 boundary of the firm outcome. The seeding proofs of task 130b neutralised the firm check in
/// <see cref="FirmOutcomeAuthorizationFilter"/> and watched the another-firm tests here fail (task notes). Every refusal is asserted to read
/// NOTHING through the projection, and every refusal body is the same, so the status says nothing about why.
/// </remarks>
public sealed class FirmOutcomeScopeTests : IAsyncLifetime
{
    private static readonly Guid FirmA = Guid.Parse("13000000-0000-4000-8000-0000000000a1");
    private static readonly Guid FirmB = Guid.Parse("13000000-0000-4000-8000-0000000000b1");
    private static readonly Guid FirmC = Guid.Parse("13000000-0000-4000-8000-0000000000c1");
    private static readonly Guid SharedMatter = Guid.Parse("13000000-0000-4000-8000-00000000aa01");
    private static readonly Guid OtherMatter = Guid.Parse("13000000-0000-4000-8000-00000000aa02");
    private static readonly Guid ContactId = Guid.Parse("13000000-0000-4000-8000-0000000000e1");
    private static readonly Guid WriterId = Guid.Parse("13000000-0000-4000-8000-0000000000f1");
    private static readonly Guid UserId = Guid.Parse("13000000-0000-4000-8000-0000000000f9");

    private const string Reason = "Travel time is billed at half rate.";
    private const string ForgedReason = "A user forged this reason.";
    private const string Forbidden = "ebilling.firm-outcome.forbidden";
    private const string Unavailable = "ebilling.firm-outcome.unavailable";

    private readonly InvoiceSubmissionServiceTests.Harness _h = new();
    private readonly Participations _participations = new();
    private readonly StubResolver _resolver = new();
    private readonly Data _data = new();
    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Strict);
    private readonly List<Entity> _invoiceRows = [];
    private readonly List<Entity> _lineRows = [];
    private readonly List<Entity> _recordRows = [];
    private readonly List<Entity> _submissionRows = [];
    private readonly List<string> _projectionReads = [];
    private readonly List<Guid[]> _invoiceDecisions = [];
    private readonly List<Guid[]> _submissionDecisions = [];
    private readonly HashSet<Guid> _hiddenInvoices = [];
    private readonly HashSet<Guid> _hiddenSubmissions = [];
    private readonly HashSet<string> _faultingProjectionEntities = [];
    private bool _invoiceHidingFaults;
    private Exception? _writerFault;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public FirmOutcomeScopeTests()
    {
        _participations.Firms = [FirmA];
        _resolver.Principal = Contact();

        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                lock (_projectionReads)
                {
                    _projectionReads.Add(query.EntityName);
                }

                if (_faultingProjectionEntities.Contains(query.EntityName))
                {
                    throw new InvalidOperationException($"{query.EntityName} is down");
                }

                var rows = query.EntityName switch
                {
                    "sprk_invoice" => _invoiceRows,
                    "sprk_billingevent" => _lineRows,
                    "sprk_decisionrecord" => _recordRows,
                    "sprk_invoicesubmission" => _submissionRows,
                    "sprk_signal" or "sprk_policyversion" or "sprk_policy" => [],
                    _ => throw new InvalidOperationException($"Unexpected read of {query.EntityName}"),
                };
                return new EntityCollection(rows.Where(r => Matches(r, query.Criteria)).Select(r => Requested(r, query.ColumnSet)).ToList());
            });
        _h.Participations = _participations;
    }

    public async Task InitializeAsync() => (_app, _client) = await StartAsync(registerHiding: true);

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private FirmFindingsProjection Projection() => new(
        _entities.Object,
        (_, _, _) => Task.FromResult(FirmClauseText.None),
        _ => _writerFault is { } fault ? Task.FromException<Guid>(fault) : Task.FromResult(WriterId),
        NullLogger<FirmFindingsProjection>.Instance);

    private async Task<(WebApplication App, HttpClient Client)> StartAsync(bool registerHiding, bool callerPrincipalFilter = true)
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
        builder.Services.AddSingleton(_h.Monitor);
        builder.Services.AddSingleton<ExternalParticipationService>(_participations);
        builder.Services.AddSingleton<ICallerPrincipalResolver>(_resolver);
        builder.Services.AddSingleton<ExternalDataService>(_data);
        builder.Services.AddSingleton(_h.Build());
        builder.Services.AddSingleton(Projection());
        if (registerHiding)
        {
            // The (a') seams as AddExternalAccess registers them, each call recorded; the predicate's own rules are tested elsewhere.
            builder.Services.AddScoped<ExternalInvoiceHiding>(_ => (caller, ids, ct) =>
            {
                lock (_invoiceDecisions)
                {
                    _invoiceDecisions.Add(ids.ToArray());
                }

                return Task.FromResult(_invoiceHidingFaults
                    ? InvoiceVisibilityResult.ReadFailed(ids)
                    : new InvoiceVisibilityResult(
                        ids, ids.Where(_hiddenInvoices.Contains).ToDictionary(id => id, _ => InvoiceHiddenReason.SourceHidden), faulted: false));
            });
            builder.Services.AddScoped<ExternalInvoiceSubmissionHiding>(_ => (caller, submissions, ct) =>
            {
                lock (_submissionDecisions)
                {
                    _submissionDecisions.Add(submissions.Select(s => s.SubmissionId).ToArray());
                }

                var ids = submissions.Select(s => s.SubmissionId).ToList();
                return Task.FromResult(new InvoiceVisibilityResult(
                    ids, ids.Where(_hiddenSubmissions.Contains).ToDictionary(id => id, _ => InvoiceHiddenReason.SourceHidden), faulted: false));
            });
        }

        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Stands in for ExternalAccessEndpoints' group: authenticated, then the caller-principal filter.
        var group = app.MapGroup("/api/v1/external").RequireAuthorization();
        if (callerPrincipalFilter)
        {
            group.AddCallerPrincipalAuthorizationFilter();
        }

        group.MapFirmOutcomeEndpoints();

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    // ── Seeding ──

    private static CallerPrincipal Contact(AccessRights onSharedMatter = AccessRights.Read, Guid? systemUser = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = ContactId,
        SystemUserId = systemUser,
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [SharedMatter] = onSharedMatter },
    };

    /// <summary>An invoice of <paramref name="firm"/> filed under <paramref name="matter"/>, as the route's filter and the projection read it.</summary>
    private Guid SeedInvoice(Guid? firm, Guid? matter = null, int status = InvoiceStatusCodes.InReview, Guid? id = null)
    {
        var invoiceId = id ?? Guid.NewGuid();
        var lookups = new Dictionary<string, object?> { ["sprk_matter"] = matter ?? SharedMatter };
        if (firm is not null)
        {
            lookups["sprk_vendororg"] = firm.Value;
        }

        _data.Invoices[invoiceId] = lookups;
        _invoiceRows.Add(new Entity("sprk_invoice", invoiceId)
        {
            ["sprk_trackingnumber"] = "TRK-" + invoiceId.ToString("N")[..6],
            ["sprk_totalamount"] = new Money(1000m),
            ["sprk_approvedtotal"] = new Money(900m),
            ["statuscode"] = new OptionSetValue(status),
        });
        return invoiceId;
    }

    /// <summary>A Reduced line of <paramref name="invoiceId"/> with a decision record carrying <paramref name="reason"/>, created by <paramref name="createdBy"/>.</summary>
    private void SeedReducedLine(Guid invoiceId, string reason, Guid? createdBy = null)
    {
        var line = new Entity("sprk_billingevent", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId),
            ["sprk_linesequence"] = 1,
            ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
            ["sprk_eventdate"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_timekeeper"] = "J. Smith",
            ["sprk_amount"] = new Money(100m),
            ["sprk_approvedamount"] = new Money(60m),
            ["sprk_description"] = "Travel to the hearing",
            ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Reduced),
        };
        _lineRows.Add(line);
        _recordRows.Add(new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_actioncode"] = "reduce-line",
            ["sprk_reason"] = reason,
            ["sprk_decidedon"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", line.Id),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoiceId),
            ["createdby"] = new EntityReference("systemuser", createdBy ?? WriterId),
        });
    }

    /// <summary>A submission of <paramref name="firm"/> under <paramref name="matter"/>, as the filter (the service) and the projection read it.</summary>
    private Guid SeedSubmission(Guid? firm, Guid? matter = null, InvoiceSubmissionStatus status = InvoiceSubmissionStatus.Received)
    {
        var id = Guid.NewGuid();
        var row = new Entity("sprk_invoicesubmission", id)
        {
            ["sprk_trackingnumber"] = "INV-1.1",
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter ?? SharedMatter),
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa),
        };
        if (firm is not null)
        {
            row["sprk_vendororg"] = new EntityReference("sprk_organization", firm.Value);
        }

        _h.Submissions[id] = row;
        _submissionRows.Add(new Entity("sprk_invoicesubmission", id)
        {
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
            ["sprk_trackingnumber"] = "INV-1.1",
        });
        return id;
    }

    // ── Requests ──

    private Task<HttpResponseMessage> GetAsync(string url, bool authenticated = true, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (authenticated)
        {
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        }

        return (client ?? _client).SendAsync(request);
    }

    private Task<HttpResponseMessage> Invoice(Guid id, bool authenticated = true) =>
        GetAsync($"/api/v1/external/ebilling/invoices/{id}/outcome", authenticated);

    private Task<HttpResponseMessage> Submission(Guid id, bool authenticated = true) =>
        GetAsync($"/api/v1/external/ebilling/submissions/{id}/outcome", authenticated);

    private static async Task<(string? Code, string Body)> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return (doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null, body);
    }

    /// <summary>A refusal: this status, this code, and NOTHING of the item (no tracking number, amount or reason) in the body.</summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        var (actual, body) = await ReadAsync(response);
        actual.Should().Be(code);
        body.Should().NotContain("TRK-").And.NotContain("INV-1.1").And.NotContain(Reason).And.NotContain(ForgedReason).And.NotContain("1000");
    }

    private async Task AssertRefusedAndNothingProjectedAsync(HttpResponseMessage response)
    {
        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, Forbidden);
        _projectionReads.Should().BeEmpty("a refused request projects nothing");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Invoice: the happy path and the contract
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b happy path: a caller of the invoice's firm with Read on its matter gets 200 and the projection (status, billed total, the reduced line with its reason), not cacheable")]
    public async Task Invoice_OfTheCallersFirm_Is200_WithTheProjection()
    {
        var invoice = SeedInvoice(FirmA);
        SeedReducedLine(invoice, Reason);

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("kind").GetString().Should().Be("InvoiceInReview", "the enum is a string, not a number");
        root.GetProperty("statusLabel").GetString().Should().Be("In review");
        root.GetProperty("billedTotal").GetDecimal().Should().Be(1000m);
        root.GetProperty("degraded").GetBoolean().Should().BeFalse();
        root.TryGetProperty("approvedTotal", out var approved).Should().BeTrue();
        approved.ValueKind.Should().Be(JsonValueKind.Null, "an invoice In Review has no approved total (EB-D-202)");
        var item = root.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("decision").GetString().Should().Be("Reduced");
        item.GetProperty("reason").GetString().Should().Be(Reason);
        item.GetProperty("line").GetProperty("billedAmount").GetDecimal().Should().Be(100m);
        item.GetProperty("line").GetProperty("approvedAmount").GetDecimal().Should().Be(60m);
    }

    [Fact(DisplayName = "130b must-fire: a decision record the BFF writer did not create never reaches the firm through the route (the line stays, with no reason)")]
    public async Task Invoice_AForgedRecordsReason_NeverReachesTheBody()
    {
        var invoice = SeedInvoice(FirmA);
        SeedReducedLine(invoice, ForgedReason, createdBy: UserId);

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(ForgedReason);
        using var body = JsonDocument.Parse(text);
        var item = body.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("decision").GetString().Should().Be("Reduced");
        item.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "130b: unauthenticated is 401 on both routes, and nothing is read")]
    public async Task Both_routes_are_401_unauthenticated()
    {
        var invoice = SeedInvoice(FirmA);
        var submission = SeedSubmission(FirmA);

        (await Invoice(invoice, authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Submission(submission, authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _data.Reads.Should().Be(0);
        _participations.MembershipReads.Should().Be(0);
        _projectionReads.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Invoice: the closed set of refusals (every one the same 403, nothing projected)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b SEEDING PROOF (firm scope): another firm's invoice on a SHARED matter is 403 with no data, and nothing is projected")]
    public async Task Invoice_OfAnotherFirm_OnASharedMatter_Is403_AndNothingIsProjected()
    {
        var others = SeedInvoice(FirmB);
        SeedReducedLine(others, Reason);

        await AssertRefusedAndNothingProjectedAsync(await Invoice(others));
    }

    [Fact(DisplayName = "130b: the caller's own firm's invoice on the same matter is 200 while another firm's is 403 (each firm sees only its own)")]
    public async Task OnASharedMatter_TheCallerSeesItsOwnFirmsInvoiceAndNotTheOthers()
    {
        var own = SeedInvoice(FirmA);
        var others = SeedInvoice(FirmB);

        (await Invoice(own)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Invoice(others)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "130b: a caller of the right firm WITHOUT access to the invoice's core record is 403, nothing projected")]
    public async Task Invoice_WithoutAccessToTheCoreRecord_Is403()
    {
        _resolver.Principal = Contact(AccessRights.None);
        var invoice = SeedInvoice(FirmA);
        var elsewhere = SeedInvoice(FirmA, matter: OtherMatter);
        _resolver.Principal = Contact(AccessRights.Read);

        (await Invoice(elsewhere)).StatusCode.Should().Be(HttpStatusCode.Forbidden); // Read on the shared matter says nothing about another matter
        _resolver.Principal = Contact(AccessRights.None);
        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
    }

    [Fact(DisplayName = "130b: an invoice with no core record the caller can read (none set) is 403")]
    public async Task Invoice_WithNoCoreRecordSet_Is403()
    {
        var invoice = SeedInvoice(FirmA);
        _data.Invoices[invoice].Remove("sprk_matter");

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
    }

    [Fact(DisplayName = "130b: a caller with NO firm is 403 and no invoice is read")]
    public async Task ACallerWithNoFirm_Is403_AndNothingIsRead()
    {
        _participations.Firms = [];
        var invoice = SeedInvoice(FirmA);

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
        _data.Reads.Should().Be(0, "no firm means no invoice is looked at");
    }

    [Theory(DisplayName = "130b: a firm read that faults, or comes back unreadable, is 403 (fail closed), never a pass and never 'no firm'")]
    [InlineData("membership-fault")]
    [InlineData("membership-unreadable")]
    [InlineData("contact-unreadable")]
    public async Task AFirmReadThatCannotBeCompleted_Is403(string kind)
    {
        var invoice = SeedInvoice(FirmA);
        switch (kind)
        {
            case "membership-fault":
                _participations.MembershipFault = new TimeoutException("memberships are down");
                break;
            case "membership-unreadable":
                _participations.Unreadable = true;
                break;
            default:
                _participations.ContactFault = new TimeoutException("the contact is down");
                break;
        }

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
        _data.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "130b: an invoice hidden by the (a') predicate is 403 like another firm's, and is never projected")]
    public async Task AHiddenInvoice_Is403_AndNeverProjected()
    {
        var invoice = SeedInvoice(FirmA);
        SeedReducedLine(invoice, Reason);
        _hiddenInvoices.Add(invoice);

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
        _invoiceDecisions.Should().ContainSingle().Which.Should().Equal(new[] { invoice }, "the decision is asked about this invoice only");
    }

    [Fact(DisplayName = "130b: a visibility decision that cannot be made is a retryable 503, never a 200 and never projected")]
    public async Task AVisibilityDecisionThatFaults_Is503_AndNothingIsProjected()
    {
        var invoice = SeedInvoice(FirmA);
        _invoiceHidingFaults = true;

        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b: with no (a') predicate registered the invoice is 403 (fail closed)")]
    public async Task WithNoInvoicePredicateRegistered_Is403()
    {
        var invoice = SeedInvoice(FirmA);
        var (app, client) = await StartAsync(registerHiding: false);
        try
        {
            var response = await GetAsync($"/api/v1/external/ebilling/invoices/{invoice}/outcome", client: client);

            await AssertRefusedAndNothingProjectedAsync(response);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact(DisplayName = "130b: an invoice that does not exist is the SAME 403 as another firm's (no existence oracle), with the same body")]
    public async Task AnInvoiceThatDoesNotExist_IsTheSame403AsAnotherFirms()
    {
        var others = SeedInvoice(FirmB);

        var missing = await Invoice(Guid.NewGuid());
        var foreign = await Invoice(others);

        await AssertRefusedAndNothingProjectedAsync(missing);
        (await ReadAsync(missing)).Body.Should().Be((await ReadAsync(foreign)).Body, "the two refusals are indistinguishable");
    }

    [Fact(DisplayName = "130b: an invoice with no firm (or a firm that is not a GUID) matches none of the caller's firms: 403")]
    public async Task AnInvoiceWithNoFirm_Is403()
    {
        var noFirm = SeedInvoice(firm: null);
        var malformed = SeedInvoice(FirmA);
        _data.Invoices[malformed]["sprk_vendororg"] = "not-a-guid";

        (await Invoice(noFirm)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Invoice(malformed)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _projectionReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b: an internal systemuser is not an external caller on these routes: 403 before anything is read, even when its linked contact belongs to the firm")]
    public async Task ASystemUser_IsRefused_BeforeAnythingIsRead()
    {
        _resolver.Principal = Contact(systemUser: UserId);
        var invoice = SeedInvoice(FirmA);
        var submission = SeedSubmission(FirmA);

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
        await AssertRefusedAndNothingProjectedAsync(await Submission(submission));
        _participations.MembershipReads.Should().Be(0);
        _data.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "130b: a request the principal resolver denies is refused by the group filter, and nothing is read")]
    public async Task AnUnresolvedPrincipal_IsRefusedByTheGroup()
    {
        _resolver.Principal = null;

        (await Invoice(SeedInvoice(FirmA))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _projectionReads.Should().BeEmpty();
        _data.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "130b: with no resolved principal on the request (the group filter missing) the route's own filter is 403, fail closed")]
    public async Task WithNoPrincipalOnTheRequest_TheRouteFilterIs403()
    {
        var invoice = SeedInvoice(FirmA);
        var (app, client) = await StartAsync(registerHiding: true, callerPrincipalFilter: false);
        try
        {
            await AssertRefusedAndNothingProjectedAsync(await GetAsync($"/api/v1/external/ebilling/invoices/{invoice}/outcome", client: client));
            await AssertRefusedAndNothingProjectedAsync(
                await GetAsync($"/api/v1/external/ebilling/submissions/{SeedSubmission(FirmA)}/outcome", client: client));
            _data.Reads.Should().Be(0);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Multi-firm callers
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b: a caller in TWO firms sees each of its firms' invoices and still not a third firm's, on the shared matter")]
    public async Task AMultiFirmCaller_SeesEachOwnFirmsItems_AndNotAThirdFirms()
    {
        _participations.Firms = [FirmA, FirmB];
        var ofA = SeedInvoice(FirmA);
        var ofB = SeedInvoice(FirmB);
        var ofC = SeedInvoice(FirmC);
        var submissionOfB = SeedSubmission(FirmB);
        var submissionOfC = SeedSubmission(FirmC);

        (await Invoice(ofA)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Invoice(ofB)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Submission(submissionOfB)).StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRefusedAsync(await Invoice(ofC), HttpStatusCode.Forbidden, Forbidden);
        await AssertRefusedAsync(await Submission(submissionOfC), HttpStatusCode.Forbidden, Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The kill switch, and a projection that did not complete
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b: with the eBilling flag off both routes are 503 ebilling.disabled for an authorized caller")]
    public async Task WithTheFlagOff_BothRoutesAre503_EbillingDisabled()
    {
        _h.Options.Enabled = false;
        var invoice = SeedInvoice(FirmA);
        var submission = SeedSubmission(FirmA);

        foreach (var response in new[] { await Invoice(invoice), await Submission(submission) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await ReadAsync(response)).Code.Should().Be(EbillingEnabledFilter.ErrorCode);
        }

        _projectionReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b: the kill switch runs AFTER authorization, so another firm's caller is told 403, not 503, with the flag off")]
    public async Task TheKillSwitchRunsAfterAuthorization()
    {
        _h.Options.Enabled = false;
        var others = SeedInvoice(FirmB);

        await AssertRefusedAsync(await Invoice(others), HttpStatusCode.Forbidden, Forbidden);
    }

    [Fact(DisplayName = "130b: a Degraded projection (the writer identity cannot be read while there are decided lines) is 503, never a 200 with an empty list")]
    public async Task ADegradedProjection_Is503()
    {
        var invoice = SeedInvoice(FirmA);
        SeedReducedLine(invoice, Reason);
        _writerFault = new InvalidOperationException("The writer connection is down.");

        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);
    }

    [Fact(DisplayName = "130b: an Unavailable projection (the invoice header cannot be read, or the invoice is Cancelled) is 503")]
    public async Task AnUnavailableProjection_Is503()
    {
        var invoice = SeedInvoice(FirmA);
        _faultingProjectionEntities.Add("sprk_invoice");
        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);

        _faultingProjectionEntities.Clear();
        var cancelled = SeedInvoice(FirmA, status: InvoiceStatusCodes.Cancelled);
        await AssertRefusedAsync(await Invoice(cancelled), HttpStatusCode.ServiceUnavailable, Unavailable);
    }

    [Fact(DisplayName = "130b: an invoice whose firm and record cannot be read (a Dataverse fault) is a retryable 503, not a 403 that blames the caller")]
    public async Task AnInvoiceReadThatFaults_Is503()
    {
        var invoice = SeedInvoice(FirmA);
        _data.Fault = new TimeoutException("Dataverse is down");

        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Submission
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b happy path: the caller's firm's Received submission is 200 (status only), Accepted gives its invoice's outcome")]
    public async Task Submission_OfTheCallersFirm_Is200()
    {
        var received = SeedSubmission(FirmA);

        var response = await Submission(received);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("kind").GetString().Should().Be("SubmissionInProgress");
            body.RootElement.GetProperty("statusLabel").GetString().Should().Be("Received");
            body.RootElement.GetProperty("trackingNumber").GetString().Should().Be("INV-1.1");
            body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        }

        var accepted = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoice(FirmA, id: InvoiceSubmissionPersister.InvoiceIdFor(accepted));
        SeedReducedLine(invoice, Reason);
        var acceptedResponse = await Submission(accepted);
        acceptedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await acceptedResponse.Content.ReadAsStringAsync()).Should().Contain("InvoiceInReview").And.Contain(Reason);
    }

    [Fact(DisplayName = "130b SEEDING PROOF (firm scope): another firm's submission on a SHARED matter is 403 with no data, and nothing is projected")]
    public async Task Submission_OfAnotherFirm_OnASharedMatter_Is403_AndNothingIsProjected()
    {
        var others = SeedSubmission(FirmB);

        await AssertRefusedAndNothingProjectedAsync(await Submission(others));
    }

    [Fact(DisplayName = "130b: a submission whose core record the caller cannot read is 403, nothing projected")]
    public async Task Submission_WithoutAccessToTheCoreRecord_Is403()
    {
        var elsewhere = SeedSubmission(FirmA, matter: OtherMatter);

        await AssertRefusedAndNothingProjectedAsync(await Submission(elsewhere));
    }

    [Fact(DisplayName = "130b: a caller with no firm, or whose firm cannot be read, is 403 on the submission route")]
    public async Task Submission_ForACallerWithNoFirm_OrAnUnreadableFirm_Is403()
    {
        var submission = SeedSubmission(FirmA);

        _participations.Firms = [];
        await AssertRefusedAndNothingProjectedAsync(await Submission(submission));

        _participations.Firms = [FirmA];
        _participations.MembershipFault = new TimeoutException("memberships are down");
        await AssertRefusedAndNothingProjectedAsync(await Submission(submission));
    }

    [Fact(DisplayName = "130b: a submission hidden by the (a') seam is 403 and never projected")]
    public async Task AHiddenSubmission_Is403_AndNeverProjected()
    {
        var submission = SeedSubmission(FirmA);
        _hiddenSubmissions.Add(submission);

        await AssertRefusedAndNothingProjectedAsync(await Submission(submission));
        _submissionDecisions.Should().ContainSingle().Which.Should().Equal(new[] { submission });
    }

    [Fact(DisplayName = "130b: a submission that does not exist, or has no firm, is the same 403")]
    public async Task ASubmissionThatDoesNotExist_OrHasNoFirm_Is403()
    {
        var noFirm = SeedSubmission(firm: null);

        await AssertRefusedAndNothingProjectedAsync(await Submission(Guid.NewGuid()));
        await AssertRefusedAndNothingProjectedAsync(await Submission(noFirm));
    }

    [Fact(DisplayName = "130b: a submission read that faults is a retryable 503")]
    public async Task ASubmissionReadThatFaults_Is503()
    {
        var submission = SeedSubmission(FirmA);
        _h.SubmissionReadFailure = new TimeoutException("Dataverse is down");

        await AssertRefusedAsync(await Submission(submission), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b: a Failed submission (Unavailable) is 503; a Rejected one is 200 with its status only")]
    public async Task SubmissionStates_MapToTheirStatus()
    {
        var failed = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Failed);
        var rejected = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Rejected);

        await AssertRefusedAsync(await Submission(failed), HttpStatusCode.ServiceUnavailable, Unavailable);
        var response = await Submission(rejected);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SubmissionRejected");
    }

    // ── Doubles ──

    /// <summary>The store applies the equality and In conditions on the row's id and lookups, and the writer rule, as Dataverse does.</summary>
    private static bool Matches(Entity row, FilterExpression criteria)
    {
        foreach (var condition in criteria.Conditions)
        {
            if (condition.AttributeName == "createdonbehalfby" && condition.Operator == ConditionOperator.Null)
            {
                if (row.GetAttributeValue<EntityReference>("createdonbehalfby") is not null)
                {
                    return false;
                }

                continue;
            }

            if (condition.Operator is not (ConditionOperator.Equal or ConditionOperator.In)
                || condition.Values.Count == 0 || condition.Values.Any(v => v is not Guid))
            {
                continue;
            }

            var actual = condition.AttributeName == row.LogicalName + "id"
                ? row.Id
                : row.GetAttributeValue<EntityReference>(condition.AttributeName)?.Id;
            if (actual is null || !condition.Values.Contains(actual.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static Entity Requested(Entity row, ColumnSet columns)
    {
        var projected = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns.Columns.Where(row.Contains))
        {
            projected[column] = row[column];
        }

        return projected;
    }

    private sealed class StubResolver : ICallerPrincipalResolver
    {
        public CallerPrincipal? Principal { get; set; }

        public Task<CallerPrincipalResolution> ResolveAsync(Microsoft.AspNetCore.Http.HttpContext httpContext, CancellationToken ct)
        {
            if (Principal is null)
            {
                return Task.FromResult(CallerPrincipalResolution.Denied(Microsoft.AspNetCore.Http.Results.Problem(statusCode: StatusCodes.Status403Forbidden)));
            }

            return Task.FromResult(CallerPrincipalResolution.Resolved(Principal));
        }
    }

    private sealed class Participations : ExternalParticipationService
    {
        public Participations()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: Mock.Of<TokenCredential>(),
                httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance, filing: null)
        {
        }

        public IReadOnlyList<Guid> Firms { get; set; } = [];

        public bool Unreadable { get; set; }

        public Exception? MembershipFault { get; set; }

        public Exception? ContactFault { get; set; }

        public int MembershipReads { get; private set; }

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct) =>
            ContactFault is { } fault ? Task.FromException<ContactRecordState>(fault) : Task.FromResult(ContactRecordState.Active);

        /// <summary>The memberships of the test contact only; any other contact belongs to no firm.</summary>
        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            MembershipReads++;
            if (MembershipFault is { } fault)
            {
                return Task.FromException<ActiveOrgMemberships>(fault);
            }

            return Task.FromResult(Unreadable ? ActiveOrgMemberships.Failed
                : contactId == ContactId ? new ActiveOrgMemberships(Firms, Firms, false)
                : new ActiveOrgMemberships(Array.Empty<Guid>(), Array.Empty<Guid>(), false));
        }
    }

    private sealed class Data : ExternalDataService
    {
        public Data()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), Mock.Of<TokenCredential>(), NullLogger<ExternalDataService>.Instance)
        {
        }

        /// <summary>Invoice id to its lookups as <see cref="ExternalDataService.GetInvoiceLookupsAsync"/> returns them (a set GUID lookup is its id).</summary>
        public Dictionary<Guid, Dictionary<string, object?>> Invoices { get; } = new();

        public Exception? Fault { get; set; }

        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public override Task<IReadOnlyDictionary<string, object?>> GetInvoiceLookupsAsync(
            Guid invoiceId, IReadOnlyCollection<string> lookups, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            if (Fault is { } fault)
            {
                return Task.FromException<IReadOnlyDictionary<string, object?>>(fault);
            }

            return Invoices.TryGetValue(invoiceId, out var row)
                ? Task.FromResult<IReadOnlyDictionary<string, object?>>(row)
                : Task.FromException<IReadOnlyDictionary<string, object?>>(
                    new Sprk.Bff.Api.Services.Dataverse.RecordNotFoundException("sprk_invoice", invoiceId));
        }
    }
}
