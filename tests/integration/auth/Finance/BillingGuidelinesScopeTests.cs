using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// The Billing Guidelines route (ebilling-r1 task 135, FR-49, D-66, NFR-10) over the REAL <see cref="BillingGuidelinesEndpoints"/>, the REAL
/// <see cref="BillingGuidelinesAuthorizationFilter"/>, the caller-principal group filter, the workforce ceiling filter (the route is exempt), the
/// REAL eBilling kill switch and the REAL <see cref="BillingGuidelinesService"/> over the in-memory Dataverse of <see cref="RecordProcedureRig"/>.
/// Substituted only: the caller's firms (the membership read) and the caller's accessible records (whose own rules have their own suites).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): this is the NFR-10 boundary of the view. Every refusal is asserted to read NOTHING from the store, and the
/// refusals that must not reveal why (no firm, a workforce principal; a missing and an unreadable record) share one body.
/// </remarks>
public sealed class BillingGuidelinesScopeTests : IAsyncLifetime
{
    private static readonly Guid ContactId = Guid.Parse("13500000-0000-4000-8000-0000000000e1");
    private static readonly Guid Unreadable = Guid.Parse("13500000-0000-4000-8000-00000000dd01");
    private const string Forbidden = "ebilling.billing-guidelines.forbidden";
    private const string Unavailable = "ebilling.billing-guidelines.unavailable";
    private const string NotFound = "ebilling.billing-guidelines.record-not-found";
    private const string Route = "/api/v1/external/ebilling/billing-guidelines";

    private readonly RecordProcedureRig _rig = new();
    private readonly Participations _participations = new();
    private readonly StubResolver _resolver = new();
    private readonly WorkforceCeilingDouble _ceiling = new();
    private readonly Mock<IAccessibleRecordSetService> _accessible = new(MockBehavior.Strict);
    private readonly List<string> _composed = [];
    private Exception? _composeFault;
    private bool _enabled = true;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public BillingGuidelinesScopeTests()
    {
        _participations.Firms = [RecordProcedureRig.F];
        _resolver.Principal = Contact();
        _rig.World.Add("sprk_organization", RecordProcedureRig.F, ("sprk_organizationname", "Firm F"), ("statecode", RecordProcedureRig.Option(0)));
        _rig.World.Add("sprk_organization", RecordProcedureRig.G, ("sprk_organizationname", "Firm G"), ("statecode", RecordProcedureRig.Option(0)));

        _accessible.Setup(a => a.ComposeForCiamContactAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid contact, string table, CancellationToken _) =>
            {
                lock (_composed)
                {
                    _composed.Add(table);
                }

                if (_composeFault is { } fault)
                {
                    return Task.FromException<AccessibleRecordSet>(fault);
                }

                var readable = table == RecordProcedureRig.Matter && contact == ContactId
                    ? new Dictionary<Guid, AccessRights> { [RecordProcedureRig.M] = AccessRights.Read }
                    : new Dictionary<Guid, AccessRights>();
                return Task.FromResult(new AccessibleRecordSet
                {
                    PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                    EntityType = table,
                    Rights = readable,
                    Sources = new AccessibleRecordSetSources(false, true, false),
                });
            });
    }

    public async Task InitializeAsync() => (_app, _client) = await StartAsync();

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<(WebApplication App, HttpClient Client)> StartAsync()
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
        var monitor = new Mock<IOptionsMonitor<EbillingOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(() => new EbillingOptions { Enabled = _enabled });
        builder.Services.AddSingleton(monitor.Object);
        builder.Services.AddSingleton<ExternalParticipationService>(_participations);
        builder.Services.AddSingleton<ICallerPrincipalResolver>(_resolver);
        builder.Services.AddSingleton(_accessible.Object);
        builder.Services.AddSingleton<IDataversePrivilegeChecker>(_ceiling);
        builder.Services.AddSingleton(_rig.Entities.Object);
        builder.Services.AddSingleton(Service());
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Stands in for ExternalAccessEndpoints' group: authenticated, then the caller-principal filter, then the workforce ceiling.
        var group = app.MapGroup("/api/v1/external").RequireAuthorization();
        group.AddCallerPrincipalAuthorizationFilter();
        group.AddWorkforceCeilingFilter();
        group.MapBillingGuidelinesEndpoints();

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private BillingGuidelinesService Service()
    {
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
        return new BillingGuidelinesService(
            _rig.Entities.Object, new RuleBodyDescriber(validator, _rig.Entities.Object, NullLogger<RuleBodyDescriber>.Instance),
            NullLogger<BillingGuidelinesService>.Instance);
    }

    // ── Seeding ──

    private static CallerPrincipal Contact(Guid? systemUser = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = ContactId,
        SystemUserId = systemUser,
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal WorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = ContactId,
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights>(),
    };

    /// <summary>A published rule set shown to firms (or not), as the rig's rule set row plus the flag and status.</summary>
    private Guid SeedProcedure(string code, Guid? firm, bool visible, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords)
    {
        var simple = _rig.Simple(code, appliesTo, firm);
        _rig.World.Add("sprk_ruleset", _rig.RuleSetOf(simple.Procedure),
            ("sprk_procedure", RecordProcedureRig.Ref(ProcedureEntity.Entity, simple.Procedure)),
            (RuleDomains.PolicyDomainColumn, RecordProcedureRig.Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", RecordProcedureRig.Option(0)), ("sprk_visibletofirms", visible),
            ("statuscode", RecordProcedureRig.Option(RuleSetService.RuleSetPublished)));
        return simple.Procedure;
    }

    // ── Requests ──

    private Task<HttpResponseMessage> GetAsync(string url = Route, bool authenticated = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (authenticated)
        {
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        }

        return _client.SendAsync(request);
    }

    private static async Task<(string? Code, string Body)> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return (doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null, body);
    }

    private void NothingRead()
    {
        _rig.World.Queries.Should().BeEmpty("a refused request reads nothing from the store");
        _ceiling.Reads.Should().Be(0, "the route is exempt from the workforce ceiling");
    }

    // ── 200 ──

    [Fact(DisplayName = "135: a CIAM contact of one firm gets its firm's rule sets and the client default, never another firm's, with no-store")]
    public async Task AContactOfOneFirm_GetsItsFirmsSets()
    {
        SeedProcedure("RATE-ALL", null, visible: true);
        SeedProcedure("RATE-F", RecordProcedureRig.F, visible: true, ProcedureAppliesTo.Firm);
        SeedProcedure("RATE-G", RecordProcedureRig.G, visible: true, ProcedureAppliesTo.Firm);

        var response = await GetAsync();
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var doc = JsonDocument.Parse(body);
        var names = doc.RootElement.GetProperty("sets").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        names.Should().BeEquivalentTo("Procedure RATE-ALL", "Procedure RATE-F");
        doc.RootElement.GetProperty("sets").EnumerateArray().Select(s => s.GetProperty("firmName").GetString()).Should().OnlyContain(n => n == "Firm F");
        body.Should().NotContain("RATE-G");
        _ceiling.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "135 negative: a rule set with the flag off is not in the 200 body, and the body names no document, container, drive item or Procedure id")]
    public async Task AFlagOffSet_IsNotServed_AndTheBodyHasNoDocumentOrIdentity()
    {
        var shown = SeedProcedure("RATE-ON", null, visible: true);
        var hidden = SeedProcedure("RATE-OFF", null, visible: false);

        var response = await GetAsync();
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("RATE-ON").And.NotContain("RATE-OFF");
        body.Should().NotContain(shown.ToString()).And.NotContain(hidden.ToString());
        body.ToLowerInvariant().Should().NotContain("container").And.NotContain("driveitem").And.NotContain("documentid").And.NotContain("http");
    }

    [Fact(DisplayName = "135: a caller of several firms gets each firm's sets, labelled with its firm")]
    public async Task AContactOfSeveralFirms_GetsEachFirmsSets()
    {
        _participations.Firms = [RecordProcedureRig.F, RecordProcedureRig.G];
        SeedProcedure("RATE-F", RecordProcedureRig.F, visible: true, ProcedureAppliesTo.Firm);
        SeedProcedure("RATE-G", RecordProcedureRig.G, visible: true, ProcedureAppliesTo.Firm);

        var body = await (await GetAsync()).Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("sets").EnumerateArray()
            .Select(s => (s.GetProperty("firmName").GetString(), s.GetProperty("name").GetString()))
            .Should().BeEquivalentTo([("Firm F", "Procedure RATE-F"), ("Firm G", "Procedure RATE-G")]);
    }

    // ── 401 / 403 / 503 ──

    [Fact(DisplayName = "135 auth: no bearer is 401 and nothing is read")]
    public async Task NoBearer_Is401_AndNothingIsRead()
    {
        var response = await GetAsync(authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        NothingRead();
        _participations.MembershipReads.Should().Be(0);
    }

    [Fact(DisplayName = "135 auth: a workforce principal is the stable 403 and nothing is read")]
    public async Task AWorkforcePrincipal_Is403_AndNothingIsRead()
    {
        SeedProcedure("RATE-ALL", null, visible: true);
        _resolver.Principal = WorkforceContact();
        _rig.World.Queries.Clear();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).Code.Should().Be(Forbidden);
        NothingRead();
        _participations.MembershipReads.Should().Be(0);
    }

    [Fact(DisplayName = "135 auth: a contact that is also a system user is the stable 403 and nothing is read")]
    public async Task ASystemUser_Is403()
    {
        _resolver.Principal = Contact(systemUser: Guid.NewGuid());

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).Code.Should().Be(Forbidden);
        NothingRead();
    }

    [Fact(DisplayName = "135 auth: a contact with no active firm is the same 403 body as a workforce principal, and nothing is read")]
    public async Task ANoFirmContact_Is403_WithTheWorkforceBody()
    {
        _resolver.Principal = WorkforceContact();
        var workforce = await ReadAsync(await GetAsync());
        _resolver.Principal = Contact();
        _participations.Firms = [];

        var response = await GetAsync();
        var noFirm = await ReadAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noFirm.Code.Should().Be(Forbidden);
        noFirm.Body.Should().Be(workforce.Body, "the status and the body say nothing about why");
        NothingRead();
    }

    [Fact(DisplayName = "135 auth: unreadable memberships are a retryable 503, never a 403 and never a pass")]
    public async Task UnreadableMemberships_Are503()
    {
        SeedProcedure("RATE-ALL", null, visible: true);
        _participations.Unreadable = true;

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReadAsync(response)).Code.Should().Be(Unavailable);
        NothingRead();
    }

    [Fact(DisplayName = "135 kill switch: with the flag off a valid caller gets 503 ebilling.disabled and nothing is read")]
    public async Task WithTheFlagOff_AValidCallerGets503Disabled()
    {
        SeedProcedure("RATE-ALL", null, visible: true);
        _enabled = false;

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReadAsync(response)).Code.Should().Be(EbillingEnabledFilter.ErrorCode);
        _rig.World.Queries.Should().NotContain(q => q.EntityName == "sprk_ruleset");
    }

    [Fact(DisplayName = "135 kill switch: the flag never replaces authorization, so a workforce principal gets the 403 with the flag off")]
    public async Task WithTheFlagOff_AWorkforcePrincipalStillGets403()
    {
        _enabled = false;
        _resolver.Principal = WorkforceContact();

        var response = await GetAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).Code.Should().Be(Forbidden);
    }

    // ── The per-record view ──

    [Fact(DisplayName = "135 per record: a record the contact can read is 200 with the record's rules")]
    public async Task AReadableRecord_Is200()
    {
        SeedProcedure("RATE-ALL", null, visible: true);

        var response = await GetAsync($"{Route}?coreRecordId={RecordProcedureRig.M}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("RATE-ALL");
        _composed.Should().Contain(RecordProcedureRig.Matter);
    }

    [Theory(DisplayName = "135 per record negative: a record the contact cannot read, a missing one and a malformed id are the same 404 and read no rule")]
    [InlineData("unreadable")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("empty")]
    public async Task AnUnreadableOrMissingRecord_IsTheSame404(string kind)
    {
        SeedProcedure("RATE-ALL", null, visible: true);
        // The unreadable record EXISTS and has rules of its own: its 404 is proven apart from "missing" (the same body), and none of its
        // rules leak into the answer.
        _rig.World.Add(RecordProcedureRig.Matter, Unreadable, ("sprk_mattername", "Hidden matter"));
        _rig.Assign(RecordProcedureRig.Matter, Unreadable, SeedProcedure("RATE-HIDDEN", null, visible: true, ProcedureAppliesTo.AssignedRecordsOnly));
        var reference = await ReadAsync(await GetAsync($"{Route}?coreRecordId={Guid.NewGuid()}"));
        var id = kind switch
        {
            "unreadable" => Unreadable.ToString(),
            "missing" => Guid.NewGuid().ToString(),
            "malformed" => "not-a-guid",
            _ => string.Empty,
        };
        _rig.World.Queries.Clear();

        var response = await GetAsync($"{Route}?coreRecordId={id}");
        var answer = await ReadAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        answer.Code.Should().Be(NotFound);
        answer.Body.Should().Be(reference.Body);
        answer.Body.Should().NotContain("RATE-ALL").And.NotContain("RATE-HIDDEN");
        _rig.World.Queries.Should().NotContain(q => q.EntityName == "sprk_ruleset" || q.EntityName == ProcedureEntity.Entity);
    }

    [Fact(DisplayName = "135 per record: a fault composing the accessible records is a retryable 503, never a 404 and never a pass")]
    public async Task AComposeFault_Is503()
    {
        _composeFault = new TimeoutException("the composition is down");

        var response = await GetAsync($"{Route}?coreRecordId={RecordProcedureRig.M}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReadAsync(response)).Code.Should().Be(Unavailable);
        NothingRead();
    }

    // ── Doubles ──

    private sealed class StubResolver : ICallerPrincipalResolver
    {
        public CallerPrincipal? Principal { get; set; }

        public Task<CallerPrincipalResolution> ResolveAsync(Microsoft.AspNetCore.Http.HttpContext httpContext, CancellationToken ct)
        {
            if (Principal is null)
            {
                return Task.FromResult(CallerPrincipalResolution.Denied(Microsoft.AspNetCore.Http.Results.Problem(statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden)));
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

        public int MembershipReads { get; private set; }

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct) =>
            Task.FromResult(ContactRecordState.Active);

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            MembershipReads++;
            return Task.FromResult(Unreadable ? ActiveOrgMemberships.Failed
                : contactId == ContactId ? new ActiveOrgMemberships(Firms, Firms, false)
                : new ActiveOrgMemberships(Array.Empty<Guid>(), Array.Empty<Guid>(), false));
        }
    }
}
