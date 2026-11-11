using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
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
    private static readonly Guid SecureProject = Guid.Parse("13000000-0000-4000-8000-00000000bb01");
    private static readonly Guid DocumentId = Guid.Parse("13000000-0000-4000-8000-00000000cc01");
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
    private readonly List<Entity> _signalRows = [];
    private readonly List<Entity> _versionRows = [];
    private readonly List<Entity> _policyRows = [];
    private readonly List<Entity> _ruleSetRows = [];
    private readonly List<Entity> _procedureRows = [];
    private readonly List<Guid> _clauseInvoices = [];
    private readonly WorkforceCeilingDouble _ceiling = new();
    private readonly List<string> _projectionReads = [];
    private readonly List<Guid[]> _invoiceDecisions = [];
    private readonly List<Guid[]> _submissionDecisions = [];
    private readonly HashSet<Guid> _hiddenInvoices = [];
    private readonly HashSet<Guid> _hiddenSubmissions = [];
    private readonly HashSet<string> _faultingProjectionEntities = [];
    private bool _invoiceHidingFaults;
    private SeamFault _submissionHidingFault;
    private Func<Guid, FirmClauseText> _clauseFor = _ => FirmClauseText.None;
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
                    "sprk_signal" => _signalRows,
                    "sprk_policyversion" => _versionRows,
                    "sprk_policy" => _policyRows,
                    "sprk_ruleset" => _ruleSetRows,
                    "sprk_procedure" => _procedureRows,
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
        (_, invoice, _) =>
        {
            lock (_clauseInvoices)
            {
                _clauseInvoices.Add(invoice);
            }

            return Task.FromResult(_clauseFor(invoice));
        },
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
        builder.Services.AddSingleton<IDataversePrivilegeChecker>(_ceiling);
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
                return _submissionHidingFault switch
                {
                    SeamFault.Throws => Task.FromException<InvoiceVisibilityResult>(new TimeoutException("the submission decision is down")),
                    SeamFault.ReadFailed => Task.FromResult(InvoiceVisibilityResult.ReadFailed(ids)),
                    _ => Task.FromResult(new InvoiceVisibilityResult(
                        ids, ids.Where(_hiddenSubmissions.Contains).ToDictionary(id => id, _ => InvoiceHiddenReason.SourceHidden), faulted: false)),
                };
            });
        }

        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Stands in for ExternalAccessEndpoints' group: authenticated, then the caller-principal filter, then the workforce ceiling (the
        // group's own order).
        var group = app.MapGroup("/api/v1/external").RequireAuthorization();
        if (callerPrincipalFilter)
        {
            group.AddCallerPrincipalAuthorizationFilter();
        }

        group.AddWorkforceCeilingFilter();

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
    private (Guid Line, Guid Record) SeedReducedLine(Guid invoiceId, string reason, Guid? createdBy = null)
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
        var recordId = Guid.NewGuid();
        _recordRows.Add(new Entity("sprk_decisionrecord", recordId)
        {
            ["sprk_actioncode"] = "reduce-line",
            ["sprk_reason"] = reason,
            ["sprk_decidedon"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", line.Id),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoiceId),
            ["createdby"] = new EntityReference("systemuser", createdBy ?? WriterId),
        });
        return (line.Id, recordId);
    }

    /// <summary>A Billing rule version cited by the decision <paramref name="record"/> on <paramref name="line"/> of <paramref name="invoiceId"/>.</summary>
    private void SeedCitation(Guid invoiceId, Guid line, Guid record, Guid version)
    {
        if (_versionRows.All(v => v.Id != version))
        {
            // A Guideline Procedure's rule set (a document firms receive): its citation is shown when the clause reader publishes it.
            var procedure = Guid.NewGuid();
            _procedureRows.Add(new Entity("sprk_procedure", procedure)
            {
                ["sprk_proceduretype"] = new OptionSetValue((int)Sprk.Bff.Api.Services.Finance.Procedures.ProcedureType.Guideline),
            });
            var ruleSet = Guid.NewGuid();
            _ruleSetRows.Add(new Entity("sprk_ruleset", ruleSet) { ["sprk_procedure"] = new EntityReference("sprk_procedure", procedure) });
            var policy = Guid.NewGuid();
            _policyRows.Add(new Entity("sprk_policy", policy)
            {
                ["sprk_name"] = "Travel time",
                [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                ["sprk_ruleset"] = new EntityReference("sprk_ruleset", ruleSet),
            });
            _versionRows.Add(new Entity("sprk_policyversion", version)
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policy),
                ["sprk_versionnumber"] = 2,
            });
        }

        _signalRows.Add(new Entity("sprk_signal", Guid.NewGuid())
        {
            ["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", record),
            ["sprk_policyversion"] = new EntityReference("sprk_policyversion", version),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoiceId),
            ["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", line),
            ["createdon"] = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
        });
    }

    private static CallerPrincipal WorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = ContactId,
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [SharedMatter] = AccessRights.Read },
    };

    /// <summary>Every Read the two outcome routes declare (the must-not-fire ceiling), less <paramref name="withoutReadOn"/>.</summary>
    private static WorkforceRoleCeiling OutcomeCeiling(string? withoutReadOn = null)
    {
        var map = new Dictionary<string, AccessRights>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_invoicesubmission"] = AccessRights.Read,
            ["sprk_invoice"] = AccessRights.Read,
            ["sprk_billingevent"] = AccessRights.Read,
            ["sprk_decisionrecord"] = AccessRights.Read,
            ["sprk_signal"] = AccessRights.Read,
        };
        if (withoutReadOn is not null)
        {
            map[withoutReadOn] = AccessRights.None;
        }

        return WorkforceRoleCeiling.Available(map);
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
        // The submission's own row as the secure-roots rule reads it (its core lookups and parents), which names the same core record.
        _data.SubmissionRows[id] = new Dictionary<string, object?> { ["sprk_regardingmatter"] = matter ?? SharedMatter };
        _submissionRows.Add(new Entity("sprk_invoicesubmission", id)
        {
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
            ["sprk_trackingnumber"] = "INV-1.1",
        });
        return id;
    }

    private const string InternalStoredClause = "INTERNAL stored clause quote";

    private static JsonObject StoredFinding(string ruleType, Guid version, int? line, params (string Name, string Value)[] values) => new()
    {
        ["ruleType"] = ruleType,
        ["policyId"] = Guid.NewGuid().ToString("D"),
        ["policyCode"] = "BILL-INTERNAL-CODE",
        ["policyVersionId"] = version.ToString("D"),
        ["clause"] = InternalStoredClause,
        ["clauseLocation"] = "INTERNAL location",
        ["subject"] = line is null ? "Invoice" : "Line",
        ["line"] = line,
        ["reasonCode"] = ruleType == "RequiredData" ? "required_data.missing_field" : "approved_rate.above_approved",
        ["detailWithheld"] = false,
        ["rejects"] = true,
        // The writer records the extracted field's name with an extracted value; a reference value (the approved rate) has none.
        ["valuesRead"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject
        {
            ["name"] = v.Name,
            ["value"] = v.Value,
            ["field"] = v.Name.StartsWith("approvedRate", StringComparison.Ordinal) ? null : "Rate",
            ["confidence"] = 0.42m,
        }).ToArray()),
    };

    /// <summary>The stored findings (<c>sprk_findingsjson</c>) of an auto-rejected submission with a line finding (line index 2) and an invoice-level one.</summary>
    private static string RejectedFindingsJson(Guid version) => new JsonObject
    {
        ["schema"] = 1,
        ["kind"] = "AutoRejected",
        ["reasonCode"] = "approved_rate.above_approved",
        ["findings"] = new JsonArray(
            StoredFinding("ApprovedRate", version, 2, ("line.rate", "450.00"), ("line.hours", "2.5"), ("approvedRate", "400.00")),
            StoredFinding("RequiredData", version, null)),
        ["signalPlan"] = new JsonArray(new JsonObject { ["sentence"] = "INTERNAL Signal plan sentence" }),
    }.ToJsonString();

    /// <summary>A Rejected submission of <paramref name="firm"/> carrying <paramref name="findingsJson"/> (the projection's store is what holds the findings).</summary>
    private Guid SeedRejectedSubmission(Guid? firm, string findingsJson)
    {
        var id = SeedSubmission(firm, status: InvoiceSubmissionStatus.Rejected);
        _submissionRows.Single(r => r.Id == id)["sprk_findingsjson"] = findingsJson;
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

    [Fact(DisplayName = "130b fix 5 (must-fire): an invoice with two core records set, the caller reading the ordinary one and the other secure and unreadable, is 403 and nothing is projected")]
    public async Task Invoice_WithASecureUnreadableSecondCore_Is403_AndNothingIsProjected()
    {
        var invoice = SeedInvoice(FirmA);
        SeedReducedLine(invoice, Reason);
        _data.Invoices[invoice]["sprk_project"] = SecureProject;

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
        _data.FlagReads.Should().Be(1, "the secure status of the unreadable root was asked");
    }

    [Fact(DisplayName = "130b fix 5 (must-not-fire): an invoice whose only core is readable is 200, and no secure status is read")]
    public async Task Invoice_WithOneReadableCore_Is200_AndNoSecureStatusIsRead()
    {
        var invoice = SeedInvoice(FirmA);

        (await Invoice(invoice)).StatusCode.Should().Be(HttpStatusCode.OK);
        _data.FlagReads.Should().Be(0, "every root the invoice names is readable, so no flag is needed");
    }

    [Fact(DisplayName = "130b fix 5 (must-not-fire): a second core the caller cannot read but that is KNOWN not secure does not hide the invoice: 200")]
    public async Task Invoice_WithAnOrdinaryUnreadableSecondCore_Is200()
    {
        var invoice = SeedInvoice(FirmA);
        _data.Invoices[invoice]["sprk_project"] = SecureProject;
        _data.NonSecureRoots.Add((ExternalGrantRootType.Project, SecureProject));

        (await Invoice(invoice)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "130b fix 5: a secure-root status read that faults is a retryable 503, never a pass, and nothing is projected")]
    public async Task ASecureRootStatusReadThatFaults_Is503_AndNothingIsProjected()
    {
        var invoice = SeedInvoice(FirmA);
        _data.Invoices[invoice]["sprk_project"] = SecureProject;
        _data.FlagFault = new TimeoutException("the root status is down");

        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty("a refused request projects nothing");
    }

    [Fact(DisplayName = "130b: a caller with NO firm is 403 and no invoice is read")]
    public async Task ACallerWithNoFirm_Is403_AndNothingIsRead()
    {
        _participations.Firms = [];
        var invoice = SeedInvoice(FirmA);

        await AssertRefusedAndNothingProjectedAsync(await Invoice(invoice));
        _data.Reads.Should().Be(0, "no firm means no invoice is looked at");
    }

    [Theory(DisplayName = "130b fix 4: a firm read that faults, or comes back unreadable, is a retryable 503 (unknown), never a pass and never 'no firm' (403)")]
    [InlineData("membership-fault")]
    [InlineData("membership-unreadable")]
    [InlineData("contact-unreadable")]
    public async Task AFirmReadThatCannotBeCompleted_Is503_AndNoSubjectIsRead(string kind)
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

        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty("a refused request projects nothing");
        _data.Reads.Should().Be(0, "the firm is decided before any invoice is read");
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

    [Fact(DisplayName = "130b fix 1: an Unavailable projection (the invoice header read FAULTED) is 503; 503 is for faults only")]
    public async Task AnUnavailableProjection_Is503()
    {
        var invoice = SeedInvoice(FirmA);
        _faultingProjectionEntities.Add("sprk_invoice");
        await AssertRefusedAsync(await Invoice(invoice), HttpStatusCode.ServiceUnavailable, Unavailable);
    }

    [Theory(DisplayName = "130b fix 1: a Cancelled, Superseded or unknown-status invoice is a permanent state: 200, status only (Closed), not degraded, never a retryable 503")]
    [InlineData(InvoiceStatusCodes.Cancelled, "Cancelled")]
    [InlineData(InvoiceStatusCodes.Superseded, "Replaced by a correction")]
    [InlineData(424242, "Closed")]
    public async Task APermanentInvoiceState_Is200_StatusOnly(int status, string label)
    {
        var invoice = SeedInvoice(FirmA, status: status);
        SeedReducedLine(invoice, Reason);

        var response = await Invoice(invoice);

        await AssertClosedAsync(response, label);
    }

    private static async Task AssertClosedAsync(HttpResponseMessage response, string label)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(Reason).And.NotContain("Travel to the hearing");
        using var body = JsonDocument.Parse(text);
        var root = body.RootElement;
        root.GetProperty("kind").GetString().Should().Be("Closed");
        root.GetProperty("statusLabel").GetString().Should().Be(label);
        root.GetProperty("degraded").GetBoolean().Should().BeFalse();
        root.GetProperty("items").GetArrayLength().Should().Be(0);
        root.GetProperty("billedTotal").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("approvedTotal").ValueKind.Should().Be(JsonValueKind.Null);
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

    [Fact(DisplayName = "130b fix 4: on the submission route a caller with no firm is 403, and one whose firm cannot be read is a retryable 503")]
    public async Task Submission_ForACallerWithNoFirm_Is403_AndAnUnreadableFirm_Is503()
    {
        var submission = SeedSubmission(FirmA);

        _participations.Firms = [];
        await AssertRefusedAndNothingProjectedAsync(await Submission(submission));

        _participations.Firms = [FirmA];
        _participations.MembershipFault = new TimeoutException("memberships are down");
        await AssertRefusedAsync(await Submission(submission), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty("a refused request projects nothing");
        _data.Reads.Should().Be(0, "the firm is decided before any invoice is read");
        _submissionDecisions.Should().BeEmpty("the firm is decided before the submission is looked at");
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

    [Fact(DisplayName = "130b fix 1: a Failed submission is a permanent state (200, Closed, status only, not degraded); a Rejected one is 200 with its status only")]
    public async Task SubmissionStates_MapToTheirStatus()
    {
        var failed = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Failed);
        var rejected = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Rejected);

        await AssertClosedAsync(await Submission(failed), "Failed");
        var response = await Submission(rejected);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SubmissionRejected");
    }

    // ── 130c (FR-18): a rejected submission's findings ──

    [Fact(DisplayName = "130c SEEDING PROOF (firm scope): another firm's REJECTED submission on a shared matter is 403 with no finding, and nothing is read")]
    public async Task RejectedSubmission_OfAnotherFirm_Is403_AndNothingIsRead()
    {
        var version = Guid.NewGuid();
        var others = SeedRejectedSubmission(FirmB, RejectedFindingsJson(version));
        SeedInvoice(FirmB, status: InvoiceStatusCodes.Rejected, id: InvoiceSubmissionPersister.InvoiceIdFor(others));
        _clauseFor = _ => new FirmClauseText("Clause of firm B.", "B-1", "Firm B guidelines");

        var response = await Submission(others);

        await AssertRefusedAndNothingProjectedAsync(response);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("450.00").And.NotContain("Clause of firm B.").And.NotContain("line.rate");
        _clauseInvoices.Should().BeEmpty();
    }

    [Fact(DisplayName = "130c happy path: the caller's own firm's rejected submission is 200 with each finding's line, values read, rule and clause, and a writer-created firm reason")]
    public async Task RejectedSubmission_OfTheCallersFirm_Is200_WithItsFindings()
    {
        var version = Guid.NewGuid();
        var own = SeedRejectedSubmission(FirmA, RejectedFindingsJson(version));
        var invoice = SeedInvoice(FirmA, status: InvoiceStatusCodes.Rejected, id: InvoiceSubmissionPersister.InvoiceIdFor(own));
        SeedCitation(invoice, Guid.NewGuid(), Guid.NewGuid(), version); // the rule rows; its decision-linked Signal is not a rejection Signal
        _signalRows.Add(new Entity("sprk_signal", Guid.NewGuid())
        {
            ["sprk_policyversion"] = new EntityReference("sprk_policyversion", version),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice),
            ["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", InvoiceSubmissionPersister.LineIdFor(own, 2)),
            ["sprk_firmreason"] = "Your rate is above the agreed rate.",
            ["createdon"] = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc),
            ["createdby"] = new EntityReference("systemuser", WriterId),
        });
        _signalRows.Add(new Entity("sprk_signal", Guid.NewGuid())
        {
            ["sprk_policyversion"] = new EntityReference("sprk_policyversion", version),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice),
            ["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", InvoiceSubmissionPersister.LineIdFor(own, 2)),
            ["sprk_firmreason"] = ForgedReason,
            ["createdon"] = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            ["createdby"] = new EntityReference("systemuser", UserId),
        });
        _clauseFor = _ => new FirmClauseText("Clause of firm A.", "A-1", "Firm A guidelines");

        var response = await Submission(own);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var root = body.RootElement;
        root.GetProperty("kind").GetString().Should().Be("SubmissionRejected");
        root.GetProperty("degraded").GetBoolean().Should().BeFalse();
        var items = root.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        var line = items[0];
        line.GetProperty("decision").GetString().Should().Be("RejectionFinding");
        line.GetProperty("lineNumber").GetInt32().Should().Be(3);
        line.GetProperty("valuesRead")[0].GetProperty("name").GetString().Should().Be("line.rate");
        line.GetProperty("valuesRead")[0].GetProperty("value").GetString().Should().Be("450.00");
        line.GetProperty("valuesRead").GetArrayLength().Should().Be(2, "the approved rate is a reference value, not one extracted from the invoice");
        line.GetProperty("valuesRead")[1].GetProperty("value").GetString().Should().Be("2.5");
        line.GetProperty("firmReasons").EnumerateArray().Select(r => r.GetString()).Should().Equal("Your rate is above the agreed rate.");
        var citation = line.GetProperty("citations").EnumerateArray().Should().ContainSingle().Subject;
        citation.GetProperty("ruleName").GetString().Should().Be("Travel time");
        citation.GetProperty("label").GetString().Should().Be("Firm A guidelines");
        citation.GetProperty("sectionReference").GetString().Should().Be("A-1");
        citation.GetProperty("quote").GetString().Should().Be("Clause of firm A.");

        items[1].TryGetProperty("lineNumber", out _).Should().BeFalse("an invoice-level finding names no line");
        text.Should().NotContain(ForgedReason).And.NotContain(InternalStoredClause).And.NotContain("INTERNAL").And.NotContain("0.42")
            .And.NotContain("BILL-INTERNAL-CODE").And.NotContain("400.00").And.NotContain(version.ToString("D")).And.NotContain(invoice.ToString("D"));
        _clauseInvoices.Should().NotBeEmpty().And.OnlyContain(id => id == invoice);
        _projectionReads.Should().NotContain("sprk_billingevent", "a rejected submission's live lines are never read");
    }

    [Fact(DisplayName = "130c: a rejected submission whose invoice does not exist yet is 200 with its findings but no citation and no firm reason, and nothing of the invoice is read")]
    public async Task RejectedSubmission_WithNoInvoiceYet_ShowsFindingsWithoutCitation()
    {
        var version = Guid.NewGuid();
        var own = SeedRejectedSubmission(FirmA, RejectedFindingsJson(version));
        _clauseFor = _ => new FirmClauseText("Clause of firm A.", "A-1", "Firm A guidelines");

        var response = await Submission(own);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var items = body.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("valuesRead").GetArrayLength().Should().Be(2);
        items.EnumerateArray().Should().OnlyContain(i => i.GetProperty("citations").GetArrayLength() == 0);
        // 130d: no Signal is read for an undecided invoice, but every rejection still carries its reason code and the firm-safe default text.
        items.EnumerateArray().Should().OnlyContain(i => i.GetProperty("firmReasons").GetArrayLength() == 1 && i.GetProperty("reasonCode").GetString()!.Length > 0);
        text.Should().NotContain("Clause of firm A.");
        _clauseInvoices.Should().BeEmpty();
        _projectionReads.Should().NotContain(new[] { "sprk_invoice", "sprk_billingevent", "sprk_signal", "sprk_policyversion", "sprk_policy" });
    }

    [Fact(DisplayName = "130c: a firm read that faults is a retryable 503 on a rejected submission, and no finding is read")]
    public async Task RejectedSubmission_WhenTheFirmReadFaults_Is503_AndNothingIsRead()
    {
        var own = SeedRejectedSubmission(FirmA, RejectedFindingsJson(Guid.NewGuid()));
        _participations.MembershipFault = new TimeoutException("memberships are down");

        var response = await Submission(own);

        await AssertRefusedAsync(response, HttpStatusCode.ServiceUnavailable, Unavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("450.00");
        _projectionReads.Should().BeEmpty("a refused request projects nothing");
        _data.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "130c: a rejected submission's findings that fault to read are a retryable 503 (Degraded), never a 200 with no findings")]
    public async Task RejectedSubmission_WhoseRulesCannotBeRead_Is503()
    {
        var version = Guid.NewGuid();
        var own = SeedRejectedSubmission(FirmA, RejectedFindingsJson(version));
        SeedInvoice(FirmA, status: InvoiceStatusCodes.Rejected, id: InvoiceSubmissionPersister.InvoiceIdFor(own));
        _faultingProjectionEntities.Add("sprk_policyversion");

        var response = await Submission(own);

        await AssertRefusedAsync(response, HttpStatusCode.ServiceUnavailable, Unavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("450.00");
    }

    [Fact(DisplayName = "130b fix 1: an Accepted submission whose invoice a correction replaced is 200, Closed, status only (not a retryable 503)")]
    public async Task AnAcceptedSubmission_WhoseInvoiceWasSuperseded_Is200_Closed()
    {
        var accepted = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoice(FirmA, status: InvoiceStatusCodes.Superseded, id: InvoiceSubmissionPersister.InvoiceIdFor(accepted));
        SeedReducedLine(invoice, Reason);

        await AssertClosedAsync(await Submission(accepted), "Replaced by a correction");
    }

    [Fact(DisplayName = "130b fix 1 (F2): a Received submission whose billed total cannot be read is a retryable 503, never a 200 with no total")]
    public async Task AReceivedSubmission_WhoseBilledTotalReadFaults_Is503()
    {
        // Fix round 2: the total is read only for an invoice the filter decided, so the invoice exists (the caller's firm) when the filter looks.
        var received = SeedSubmission(FirmA);
        SeedInvoice(FirmA, id: InvoiceSubmissionPersister.InvoiceIdFor(received));
        _faultingProjectionEntities.Add("sprk_invoice");

        await AssertRefusedAsync(await Submission(received), HttpStatusCode.ServiceUnavailable, Unavailable);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Submission: its invoice is decided as the invoice route decides it (fix round 1, F1)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b fix 1 SEEDING PROOF (F1): an own-firm Accepted submission whose invoice is another firm's, on a matter the caller cannot read, is the same 403 as a foreign submission, and no invoice content is read")]
    public async Task ASubmission_WhoseInvoiceIsAnotherFirms_IsTheSame403AsAForeignSubmission()
    {
        var accepted = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoice(FirmB, matter: OtherMatter, id: InvoiceSubmissionPersister.InvoiceIdFor(accepted));
        SeedReducedLine(invoice, Reason);
        var foreign = await Submission(SeedSubmission(FirmB));
        _projectionReads.Clear();

        var response = await Submission(accepted);

        await AssertRefusedAndNothingProjectedAsync(response);
        (await ReadAsync(response)).Body.Should().Be((await ReadAsync(foreign)).Body, "the two refusals are indistinguishable");
    }

    [Theory(DisplayName = "130b fix 1 (F1): a submission is hidden when its invoice is not the caller's to see: another firm's (on a readable matter), on an unreadable record, or hidden by (a')")]
    [InlineData("other-firm")]
    [InlineData("unreadable-record")]
    [InlineData("hidden")]
    public async Task ASubmission_WhoseInvoiceIsNotVisible_Is403(string kind)
    {
        var accepted = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(accepted);
        var invoice = kind switch
        {
            "other-firm" => SeedInvoice(FirmB, id: invoiceId),
            "unreadable-record" => SeedInvoice(FirmA, matter: OtherMatter, id: invoiceId),
            _ => SeedInvoice(FirmA, id: invoiceId),
        };
        if (kind == "hidden")
        {
            _hiddenInvoices.Add(invoice);
        }

        SeedReducedLine(invoice, Reason);

        await AssertRefusedAndNothingProjectedAsync(await Submission(accepted));
    }

    [Fact(DisplayName = "130b fix 1 (F1): the same applies while Received: an existing invoice of another firm withholds the submission (its billed total is that invoice's)")]
    public async Task AReceivedSubmission_WhoseExistingInvoiceIsAnotherFirms_Is403()
    {
        var received = SeedSubmission(FirmA);
        SeedInvoice(FirmB, id: InvoiceSubmissionPersister.InvoiceIdFor(received));

        await AssertRefusedAndNothingProjectedAsync(await Submission(received));
    }

    [Fact(DisplayName = "130b fix 1 (F1) must-not-fire: an own-firm Accepted submission with its own firm's invoice is 200, decided by the invoice rule too")]
    public async Task AnOwnSubmission_WithAnOwnInvoice_Is200()
    {
        var accepted = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoice(FirmA, id: InvoiceSubmissionPersister.InvoiceIdFor(accepted));
        SeedReducedLine(invoice, Reason);

        var response = await Submission(accepted);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Reason);
        _invoiceDecisions.Should().ContainSingle().Which.Should().Equal(new[] { invoice }, "the invoice predicate decided the submission's invoice");
    }

    [Fact(DisplayName = "130b fix 1 (F1) must-not-fire: a Received submission with no invoice yet is 200, status only")]
    public async Task AReceivedSubmission_WithNoInvoiceYet_Is200_StatusOnly()
    {
        var received = SeedSubmission(FirmA);

        var response = await Submission(received);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("kind").GetString().Should().Be("SubmissionInProgress");
        body.RootElement.GetProperty("billedTotal").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        _invoiceDecisions.Should().BeEmpty("there is no invoice to decide");
    }

    // ── Fix round 6: a record decided by a core gets that core's secure-roots check, including a submission with no invoice yet ──

    [Theory(DisplayName = "130b fix 6 (must-fire): a submission with NO invoice yet whose own row names a secure root the caller cannot read (a second core, or a parent filed under one) is 403 and nothing is projected")]
    [InlineData("second-core")]
    [InlineData("parent-under-secure-root")]
    public async Task ASubmissionWithNoInvoice_WhoseOwnRowNamesASecureUnreadableRoot_Is403_AndNothingIsProjected(string kind)
    {
        var received = SeedSubmission(FirmA);
        AddSecureRootTo(received, kind);

        await AssertRefusedAndNothingProjectedAsync(await Submission(received));
        _data.SubmissionReads.Should().Be(1, "the submission's own row was read for the rule");
        _data.FlagReads.Should().Be(1, "the secure status of the unreadable root was asked");
        _invoiceDecisions.Should().BeEmpty("there is no invoice, so the refusal came from the submission's own row");
    }

    [Theory(DisplayName = "130b fix 6 (must-not-fire): a submission with no invoice yet whose own core is readable (or whose extra root is known not secure) is 200, status only")]
    [InlineData("readable-core")]
    [InlineData("second-core-not-secure")]
    [InlineData("parent-not-secure")]
    public async Task ASubmissionWithNoInvoice_WhoseOwnRootsAreReadableOrNotSecure_Is200(string kind)
    {
        var received = SeedSubmission(FirmA);
        if (kind != "readable-core")
        {
            AddSecureRootTo(received, kind == "second-core-not-secure" ? "second-core" : "parent-under-secure-root");
            _data.NonSecureRoots.Add((ExternalGrantRootType.Project, SecureProject));
        }

        var response = await Submission(received);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("kind").GetString().Should().Be("SubmissionInProgress");
        if (kind == "readable-core")
        {
            _data.FlagReads.Should().Be(0, "every root the submission names is readable, so no flag is needed");
        }
    }

    [Theory(DisplayName = "130b fix 6: a root-status read on the submission's own row that faults or cannot be completed is a retryable 503, never a pass, and nothing is projected")]
    [InlineData("flag-fault")]
    [InlineData("row-fault")]
    [InlineData("live-parent-fault")]
    public async Task ASubmissionWithNoInvoice_WhoseOwnRootStatusCannotBeRead_Is503_AndNothingIsProjected(string kind)
    {
        var received = SeedSubmission(FirmA);
        switch (kind)
        {
            case "flag-fault":
                AddSecureRootTo(received, "second-core");
                _data.FlagFault = new TimeoutException("the root status is down");
                break;
            case "row-fault":
                _data.SubmissionFault = new TimeoutException("the submission row is down");
                break;
            default:
                // A parent whose live roots could not be read is unknowable, so the submission is withheld as unavailable.
                _data.SubmissionRows[received]["sprk_document"] = DocumentId;
                _data.LiveParentFault = true;
                break;
        }

        await AssertRefusedAsync(await Submission(received), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty("a refused request projects nothing");
    }

    // ── Fix round 7: each read of the submission stands on its own ──

    [Fact(DisplayName = "130b fix 7 (must-fire): a submission whose own row, as read the second time, names only a core the caller cannot read (known not secure) is 403 and nothing is projected, though the first read named a readable core")]
    public async Task ASubmission_WhoseRowNamesOnlyAnUnreadableCore_OnTheSecondRead_Is403_AndNothingIsProjected()
    {
        var received = SeedSubmission(FirmA);
        // The first read (the service's) keeps the readable matter; the row the rule reads next names only a project the caller cannot read,
        // which is known not secure, so the secure-roots rule alone would pass it.
        _data.SubmissionRows[received] = new Dictionary<string, object?> { ["sprk_regardingproject"] = SecureProject };
        _data.NonSecureRoots.Add((ExternalGrantRootType.Project, SecureProject));

        await AssertRefusedAndNothingProjectedAsync(await Submission(received));
        _data.SubmissionReads.Should().Be(1, "the submission's own row was read");
        _data.FlagReads.Should().Be(0, "the row names no core the caller can read, so the secure status is never asked");
        _invoiceDecisions.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b fix 7 (must-fire): a submission whose row is not found on the second read is 403 and nothing is projected")]
    public async Task ASubmission_NotFoundOnTheSecondRead_Is403_AndNothingIsProjected()
    {
        var received = SeedSubmission(FirmA);
        _data.SubmissionRows.Remove(received);

        await AssertRefusedAndNothingProjectedAsync(await Submission(received));
        _data.SubmissionReads.Should().Be(1, "the second read was made and found nothing");
        _invoiceDecisions.Should().BeEmpty();
    }

    /// <summary>Makes the submission's own row (as the rule reads it) name a project the caller cannot read, as a second core or through a parent.</summary>
    private void AddSecureRootTo(Guid submission, string kind)
    {
        var row = _data.SubmissionRows[submission];
        if (kind == "second-core")
        {
            row["sprk_regardingproject"] = SecureProject;
            return;
        }

        row["sprk_document"] = DocumentId;
        _data.LiveRoots[("sprk_document", DocumentId)] = [(ExternalGrantRootType.Project, SecureProject)];
    }

    /// <summary>
    /// An invoice the PROJECTION's store holds and the filter's read does not: the filter sees no invoice (it passes undecided), the projection
    /// would find one. Seeded as another firm's on a matter the caller cannot read, so showing anything of it is a leak.
    /// </summary>
    private Guid SeedInvoiceOnlyInTheProjectionsStore(Guid submissionId)
    {
        var invoiceId = SeedInvoice(FirmB, matter: OtherMatter, id: InvoiceSubmissionPersister.InvoiceIdFor(submissionId));
        _data.Invoices.Remove(invoiceId);
        return invoiceId;
    }

    [Theory(DisplayName = "130b fix 2 (F1) SEEDING PROOF must-fire: a Received or Processing submission whose invoice the filter did not decide shows no billed total, and the projection never reads the invoice")]
    [InlineData(InvoiceSubmissionStatus.Received)]
    [InlineData(InvoiceSubmissionStatus.Processing)]
    public async Task AnUndecidedInvoice_InProgress_Is200_WithNoBilledTotal_AndIsNeverRead(InvoiceSubmissionStatus status)
    {
        var submission = SeedSubmission(FirmA, status: status);
        SeedInvoiceOnlyInTheProjectionsStore(submission);

        var response = await Submission(submission);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("1000").And.NotContain("TRK-");
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("kind").GetString().Should().Be("SubmissionInProgress");
        body.RootElement.GetProperty("billedTotal").ValueKind.Should().Be(JsonValueKind.Null);
        _projectionReads.Should().NotContain("sprk_invoice", "no invoice decision covers it");
        _invoiceDecisions.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b fix 2 (F1) SEEDING PROOF must-fire: an Accepted submission whose invoice the filter did not decide is a retryable 503, and nothing of the invoice is read")]
    public async Task AnUndecidedInvoice_Accepted_Is503_AndIsNeverRead()
    {
        var submission = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoiceOnlyInTheProjectionsStore(submission);
        SeedReducedLine(invoice, Reason);

        await AssertRefusedAsync(await Submission(submission), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().NotContain("sprk_invoice").And.NotContain("sprk_billingevent").And.NotContain("sprk_decisionrecord");
    }

    // This file builds the projection on a NullLogger, so the route test asserts the 503 and the no-read; the Warning that carries
    // FirmFindingsProjection.AcceptedWithoutInvoiceCode is covered in FirmFindingsProjectionTests
    // (AnAcceptedSubmission_WithNoInvoice_Undecided_IsUnavailable_AndWarnedWithTheStableCode).
    [Fact(DisplayName = "130b fix 3 (F2) must-fire: an Accepted submission with no invoice at all is a retryable 503 on the real route, and no invoice is read")]
    public async Task AnAcceptedSubmission_WithNoInvoice_Is503_AndNothingIsRead()
    {
        var submission = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);

        await AssertRefusedAsync(await Submission(submission), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().NotContain("sprk_invoice").And.NotContain("sprk_billingevent").And.NotContain("sprk_decisionrecord");
    }

    [Theory(DisplayName = "130b fix 2 (F1) must-fire: the invoice is created between the filter and the projection: no total, no content; an Accepted one is 503 and the retry is decided afresh")]
    [InlineData(InvoiceSubmissionStatus.Processing)]
    [InlineData(InvoiceSubmissionStatus.Accepted)]
    public async Task AnInvoiceCreatedBetweenTheFilterAndTheProjection_IsNotShown(InvoiceSubmissionStatus status)
    {
        var submission = SeedSubmission(FirmA, status: status);
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        // The filter looks for the invoice and misses; the processing job creates it (the caller's own firm, a readable matter) right after.
        _data.OnMiss = missed =>
        {
            if (missed == invoiceId)
            {
                _data.OnMiss = null;
                SeedInvoice(FirmA, id: invoiceId);
                SeedReducedLine(invoiceId, Reason);
            }
        };

        var response = await Submission(submission);

        if (status == InvoiceSubmissionStatus.Accepted)
        {
            await AssertRefusedAsync(response, HttpStatusCode.ServiceUnavailable, Unavailable);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var text = await response.Content.ReadAsStringAsync();
            text.Should().NotContain("1000").And.NotContain(Reason);
            using var body = JsonDocument.Parse(text);
            body.RootElement.GetProperty("billedTotal").ValueKind.Should().Be(JsonValueKind.Null);
        }

        _projectionReads.Should().NotContain("sprk_invoice", "the invoice existed by then, but the filter did not decide it");

        // The retry re-enters the filter, which now decides the invoice: its content is shown.
        var retry = await Submission(submission);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.Content.ReadAsStringAsync()).Should().Contain(status == InvoiceSubmissionStatus.Accepted ? Reason : "1000");
        _invoiceDecisions.Should().ContainSingle().Which.Should().Equal(new[] { invoiceId });
    }

    [Fact(DisplayName = "130b fix 2 (F1) must-not-fire: a Processing submission whose own-firm invoice the filter decided shows its billed total")]
    public async Task ADecidedInvoice_InProgress_ShowsItsBilledTotal()
    {
        var submission = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Processing);
        var invoice = SeedInvoice(FirmA, id: InvoiceSubmissionPersister.InvoiceIdFor(submission));

        var response = await Submission(submission);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("billedTotal").GetDecimal().Should().Be(1000m);
        _invoiceDecisions.Should().ContainSingle().Which.Should().Equal(new[] { invoice });
    }

    [Theory(DisplayName = "130b fix 1 (F3): a submission decision that faults is a retryable 503, decided after the firm and core-record checks")]
    [InlineData(SeamFault.ReadFailed)]
    [InlineData(SeamFault.Throws)]
    public async Task ASubmissionDecisionThatFaults_Is503_AfterTheRecordChecks(SeamFault fault)
    {
        var own = SeedSubmission(FirmA);
        var others = SeedSubmission(FirmB);
        _submissionHidingFault = fault;

        await AssertRefusedAsync(await Submission(own), HttpStatusCode.ServiceUnavailable, Unavailable);
        _projectionReads.Should().BeEmpty();
        _submissionDecisions.Should().ContainSingle();

        await AssertRefusedAndNothingProjectedAsync(await Submission(others));
        _submissionDecisions.Should().ContainSingle("another firm's submission is refused before the decision is asked");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Two firms: the clause of the invoice's own firm only (POML 076/083 constraint)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130b fix 1 (F3): a caller in firms A and B viewing firm A's invoice gets A's clause and never B's")]
    public async Task ACallerInTwoFirms_GetsTheClauseOfTheInvoicesFirmOnly()
    {
        _participations.Firms = [FirmA, FirmB];
        var version = Guid.NewGuid();
        var ofA = SeedInvoice(FirmA);
        var ofB = SeedInvoice(FirmB);
        var (lineA, recordA) = SeedReducedLine(ofA, Reason);
        var (lineB, recordB) = SeedReducedLine(ofB, Reason);
        SeedCitation(ofA, lineA, recordA, version);
        SeedCitation(ofB, lineB, recordB, version);
        _clauseFor = invoice => _data.Invoices[invoice].TryGetValue("sprk_vendororg", out var firm) && firm is Guid id
            ? id == FirmA ? new FirmClauseText("Clause of firm A.", "A-1", "Firm A guidelines")
            : id == FirmB ? new FirmClauseText("Clause of firm B.", "B-1", "Firm B guidelines")
            : FirmClauseText.None
            : FirmClauseText.None;

        var response = await Invoice(ofA);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("Clause of firm A.").And.Contain("Firm A guidelines");
        text.Should().NotContain("Clause of firm B.").And.NotContain("Firm B guidelines").And.NotContain("B-1");
        text.Should().NotContainEquivalentOf(version.ToString(), "no policy version id is in the body (fix round 2)");
        _clauseInvoices.Should().NotBeEmpty().And.OnlyContain(id => id == ofA, "the clause is asked only for the invoice being viewed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The workforce ceiling (uac-r2 task 186): each route declares Read on every table it returns (fix round 1, F4)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "130b fix 1 (F4) must-fire: a workforce contact whose role lacks Read on a table the route returns gets the ceiling's 403 before anything is read")]
    [InlineData("invoice", "sprk_invoice")]
    [InlineData("invoice", "sprk_billingevent")]
    [InlineData("invoice", "sprk_decisionrecord")]
    [InlineData("invoice", "sprk_signal")]
    [InlineData("submission", "sprk_invoicesubmission")]
    [InlineData("submission", "sprk_invoice")]
    [InlineData("submission", "sprk_billingevent")]
    [InlineData("submission", "sprk_decisionrecord")]
    [InlineData("submission", "sprk_signal")]
    public async Task AWorkforceContact_WithoutReadOnAReturnedTable_IsRefusedByTheCeiling(string route, string table)
    {
        _resolver.Principal = WorkforceContact();
        _ceiling.Ceiling = OutcomeCeiling(withoutReadOn: table);
        var submission = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoice(FirmA, id: InvoiceSubmissionPersister.InvoiceIdFor(submission));
        SeedReducedLine(invoice, Reason);

        var response = route == "invoice" ? await Invoice(invoice) : await Submission(submission);

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Reason).And.NotContain("TRK-");
        _ceiling.Reads.Should().Be(1);
        _participations.MembershipReads.Should().Be(0, "the ceiling runs before the route's own filter");
        _data.Reads.Should().Be(0);
        _projectionReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130b fix 1 (F4) must-not-fire: a workforce contact whose role holds every declared Read is served on both routes")]
    public async Task AWorkforceContact_WithEveryDeclaredRead_IsServed()
    {
        _resolver.Principal = WorkforceContact();
        _ceiling.Ceiling = OutcomeCeiling();
        var submission = SeedSubmission(FirmA, status: InvoiceSubmissionStatus.Accepted);
        var invoice = SeedInvoice(FirmA, id: InvoiceSubmissionPersister.InvoiceIdFor(submission));
        SeedReducedLine(invoice, Reason);

        var invoiceResponse = await Invoice(invoice);
        var submissionResponse = await Submission(submission);

        invoiceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await invoiceResponse.Content.ReadAsStringAsync()).Should().Contain(Reason);
        submissionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await submissionResponse.Content.ReadAsStringAsync()).Should().Contain(Reason);
        _ceiling.Reads.Should().Be(2);
    }

    [Fact(DisplayName = "130b fix 2 (F4b): the routes declare exactly the reads they return: invoice, line, Decision Record and Signal; never the policy, version, rule set or Procedure tables")]
    public void TheRoutes_DeclareTheReadsTheyReturn()
    {
        WorkforceRoleCeilingGate.ReadInvoiceOutcome.Should().BeEquivalentTo(new[]
        {
            new WorkforceCeilingRequirement("sprk_invoice", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_billingevent", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_decisionrecord", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_signal", AccessRights.Read),
        });
        WorkforceRoleCeilingGate.ReadSubmissionOutcome.Should().BeEquivalentTo(new[]
        {
            new WorkforceCeilingRequirement("sprk_invoicesubmission", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_invoice", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_billingevent", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_decisionrecord", AccessRights.Read),
            new WorkforceCeilingRequirement("sprk_signal", AccessRights.Read),
        });
        foreach (var undeclared in new[] { "sprk_policy", "sprk_policyversion", "sprk_ruleset", "sprk_procedure" })
        {
            WorkforceRoleCeilingGate.ReadInvoiceOutcome.Concat(WorkforceRoleCeilingGate.ReadSubmissionOutcome)
                .Should().NotContain(r => r.Table == undeclared, "a citation is shown only as the policy's firm flag publishes it");
        }
    }

    /// <summary>How the submission (a') seam fails in a test: not at all, a failed read (its contract), or an exception.</summary>
    public enum SeamFault
    {
        None,
        ReadFailed,
        Throws,
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

        /// <summary>Called with the id of an invoice the filter looked for and did not find (before the miss is answered).</summary>
        public Action<Guid>? OnMiss { get; set; }

        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        /// <summary>The roots the root-status read answers as NOT secure; any other unreadable root counts as secure.</summary>
        public HashSet<(ExternalGrantRootType Root, Guid Id)> NonSecureRoots { get; } = new();

        /// <summary>When set, the root-status read throws it.</summary>
        public Exception? FlagFault { get; set; }

        private int _flagReads;

        public int FlagReads => Volatile.Read(ref _flagReads);

        public override Task<NonSecureRootsRead> GetNonSecureRootsAsync(
            IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)> roots, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _flagReads);
            return FlagFault is { } fault
                ? Task.FromException<NonSecureRootsRead>(fault)
                : Task.FromResult(new NonSecureRootsRead(roots.Where(NonSecureRoots.Contains).ToHashSet()));
        }

        /// <summary>Submission id to its own row's lookups as <see cref="ExternalDataService.GetSubmissionLookupsAsync"/> returns them.</summary>
        public Dictionary<Guid, Dictionary<string, object?>> SubmissionRows { get; } = new();

        /// <summary>When set, the submission row read throws it.</summary>
        public Exception? SubmissionFault { get; set; }

        private int _submissionReads;

        public int SubmissionReads => Volatile.Read(ref _submissionReads);

        /// <summary>The live roots of a parent (table, id) as the live-parent read resolves them; any other parent is left unresolved.</summary>
        public Dictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>> LiveRoots { get; } = new();

        /// <summary>When set, the live-parent read answers that it faulted.</summary>
        public bool LiveParentFault { get; set; }

        public override Task<LiveParentRootsRead> GetLiveParentRootsAsync(
            IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct = default) =>
            Task.FromResult(new LiveParentRootsRead(
                parents.Where(LiveRoots.ContainsKey).ToDictionary(p => p, p => LiveRoots[p]),
                Faulted: LiveParentFault));

        public override Task<IReadOnlyDictionary<string, object?>> GetSubmissionLookupsAsync(
            Guid submissionId, IReadOnlyCollection<string> lookups, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _submissionReads);
            if (SubmissionFault is { } fault)
            {
                return Task.FromException<IReadOnlyDictionary<string, object?>>(fault);
            }

            return SubmissionRows.TryGetValue(submissionId, out var row)
                ? Task.FromResult<IReadOnlyDictionary<string, object?>>(row)
                : Task.FromException<IReadOnlyDictionary<string, object?>>(
                    new Sprk.Bff.Api.Services.Dataverse.RecordNotFoundException("sprk_invoicesubmission", submissionId));
        }

        public override Task<IReadOnlyDictionary<string, object?>> GetInvoiceLookupsAsync(
            Guid invoiceId, IReadOnlyCollection<string> lookups, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            if (Fault is { } fault)
            {
                return Task.FromException<IReadOnlyDictionary<string, object?>>(fault);
            }

            if (Invoices.TryGetValue(invoiceId, out var row))
            {
                return Task.FromResult<IReadOnlyDictionary<string, object?>>(row);
            }

            OnMiss?.Invoke(invoiceId);
            return Task.FromException<IReadOnlyDictionary<string, object?>>(
                new Sprk.Bff.Api.Services.Dataverse.RecordNotFoundException("sprk_invoice", invoiceId));
        }
    }
}
