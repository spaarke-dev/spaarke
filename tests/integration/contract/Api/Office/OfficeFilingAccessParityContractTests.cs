using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// "Pickable equals savable" (spaarkeai-word-add-in-r1 task 084, GitHub #1037): the Office picker's per-row
/// <c>canFile</c> must agree with what <c>POST /api/office/save</c> actually does with that row.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>GET /api/office/search/entities</c> returns every record the caller can READ;
/// the save authorizes its target by APPENDTO (<c>EntityAccessFilter</c> → <c>OperationAccessPolicy</c>
/// <c>entity.associate_document</c>). A user holding Read without AppendTo could pick a record and then have
/// the save refuse it. The owner decided to show such a record disabled, with the reason.</para>
///
/// <para><b>What these tests prove.</b> The core test runs BOTH routes, in one host, under the SAME stated
/// rights, and requires them to agree row by row: <c>canFile == false</c> exactly when the save's association
/// gate refuses that row. Agreement is the property; it holds because both read one operation constant
/// through one entity-set map and one policy, and a test that compared <c>canFile</c> with a hand-written
/// expectation would only prove that the test and the code share an author.</para>
///
/// <para><b>What is doubled.</b> Only the module boundaries:
/// <list type="bullet">
///   <item><description>the caller-rights probe, which answers from the bearer token
///   (<c>Bearer rights=…;fail=…</c>, the <c>OfficeSaveTestFixture</c> convention), identically for its single-
///   and multi-record methods. The multi-record method's own behaviour (the identity is asked for once, the
///   lookups are bounded) is proven against the shipped type in
///   <c>tests/integration/auth/Office/CallerRecordAccessProbeRecordsTests.cs</c>;</description></item>
///   <item><description>the impersonated Dataverse read, which returns one row per searchable type;</description></item>
///   <item><description>the caller → systemuser resolver.</description></item>
/// </list>
/// The route, its filters, the search, <c>OfficeSearchService</c>, the save's <c>EntityAccessFilter</c> and
/// <c>OperationAccessPolicy</c> are the shipped code.</para>
/// </remarks>
[Trait("status", "new")]
public class OfficeFilingAccessParityContractTests : IClassFixture<OfficeFilingAccessTestFixture>
{
    private readonly OfficeFilingAccessTestFixture _fixture;

    public OfficeFilingAccessParityContractTests(OfficeFilingAccessTestFixture fixture) => _fixture = fixture;

    private const string SearchAllWithFilingAccess = "/api/office/search/entities?q=Ac&top=50&access=file";

    #region The core property: for every rights mask, canFile agrees with the save, row by row

    [Theory]
    [InlineData("")]
    [InlineData("ReadAccess")]
    [InlineData("ReadAccess,WriteAccess")]
    [InlineData("AppendToAccess")]
    [InlineData("ReadAccess,AppendToAccess")]
    [InlineData("ReadAccess,WriteAccess,AppendAccess,AppendToAccess")]
    public async Task SearchCanFile_ForEveryRow_AgreesWithWhetherTheSaveAcceptsThatRow(string rights)
    {
        using var client = _fixture.CreateClientWithRights(rights);

        var search = await client.GetFromJsonAsync<EntitySearchResponse>(SearchAllWithFilingAccess);

        search!.Results.Should().HaveCount(5, "one record of each of the five searchable types is readable");
        foreach (var row in search.Results)
        {
            row.CanFile.Should().NotBeNull($"{row.EntityType} was asked about with access=file, under the cap");

            // The pane sends the row's FRIENDLY type as TargetEntity.EntityType (useSaveFlow.ts).
            var save = await client.PostAsJsonAsync(
                "/api/office/save", SaveRequestTargeting(row.EntityType.ToString(), row.Id));

            (await IsRefusedByTheAssociationGateAsync(save)).Should().Be(
                row.CanFile == false,
                $"with rights '{rights}', the picker marked {row.EntityType} canFile={row.CanFile}, so the save "
                + "must refuse it exactly when the picker said it would");
        }
    }

    [Fact]
    public async Task SearchCanFile_ForReadWithoutAppendTo_IsFalse_AndWithAppendToIsTrue()
    {
        // The two values the parity theory above could agree on vacuously if canFile were constant. Pin both.
        using var readOnly = _fixture.CreateClientWithRights("ReadAccess");
        using var canAppend = _fixture.CreateClientWithRights("ReadAccess,AppendToAccess");

        var denied = await readOnly.GetFromJsonAsync<EntitySearchResponse>(SearchAllWithFilingAccess);
        var allowed = await canAppend.GetFromJsonAsync<EntitySearchResponse>(SearchAllWithFilingAccess);

        denied!.Results.Should().OnlyContain(r => r.CanFile == false,
            "Read alone does not let a caller file a document to a record");

        allowed!.Results.Where(r => r.EntityType != AssociationEntityType.Account)
            .Should().OnlyContain(r => r.CanFile == true, "AppendTo is exactly the right filing needs");
        allowed.Results.Single(r => r.EntityType == AssociationEntityType.Account).CanFile.Should().BeFalse(
            "the save refuses an account target whatever the caller holds (no sprk_document account lookup), so "
            + "the picker must not offer one as fileable");
    }

    #endregion

    #region Opt-in and cost

    [Fact]
    public async Task Search_WithoutAccessFile_LeavesCanFileUnset_AndAsksForNoRights()
    {
        // The To Do assignee search (App.tsx) does not ask; it must not pay for a rights check per row.
        using var client = _fixture.CreateClientWithRights("ReadAccess,AppendToAccess");
        var before = _fixture.Probe.RecordsCalls;

        var search = await client.GetFromJsonAsync<EntitySearchResponse>("/api/office/search/entities?q=Ac&top=50");

        search!.Results.Should().HaveCount(5);
        search.Results.Should().OnlyContain(r => r.CanFile == null);
        _fixture.Probe.RecordsCalls.Should().Be(before, "no caller-rights lookup without access=file");
    }

    [Fact]
    public async Task Search_WithAnUnrecognisedAccessValue_ChecksNothing()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,AppendToAccess");
        var before = _fixture.Probe.RecordsCalls;

        var search = await client.GetFromJsonAsync<EntitySearchResponse>(
            "/api/office/search/entities?q=Ac&top=50&access=everything");

        search!.Results.Should().OnlyContain(r => r.CanFile == null);
        _fixture.Probe.RecordsCalls.Should().Be(before);
    }

    [Fact]
    public async Task Search_WithAccessFile_AsksOnceForThePage_AndNeverAboutATypeTheSaveRefuses()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,AppendToAccess");
        var before = _fixture.Probe.RecordsCalls;

        await client.GetFromJsonAsync<EntitySearchResponse>(SearchAllWithFilingAccess);

        _fixture.Probe.RecordsCalls.Should().Be(before + 1, "one multi-record lookup per request, not one per row");
        _fixture.Probe.LastRecordsRequest.Select(t => t.EntitySet).Should().BeEquivalentTo(
            new[] { "sprk_matters", "sprk_projects", "sprk_invoices", "contacts" },
            "each fileable row is asked about in its own collection; the account row is not asked about at all, "
            + "because no rights could make the save accept it");
    }

    [Fact]
    public async Task Search_WhenOneRecordsRightsCannotBeRead_MarksOnlyThatRowNotFileable()
    {
        // The probe answers None for a record it could not ask about (OBO/RPA failure). That row must read
        // as not fileable — never as fileable — and must not take the other rows or the response with it.
        using var client = _fixture.CreateClientWithRights(
            "ReadAccess,AppendToAccess", failFor: OfficeFilingAccessTestFixture.MatterId);

        var response = await client.GetAsync(SearchAllWithFilingAccess);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var search = await response.Content.ReadFromJsonAsync<EntitySearchResponse>();

        search!.Results.Single(r => r.Id == OfficeFilingAccessTestFixture.MatterId).CanFile.Should().BeFalse();
        search.Results.Single(r => r.Id == OfficeFilingAccessTestFixture.ProjectId).CanFile.Should().BeTrue();
        search.Results.Single(r => r.Id == OfficeFilingAccessTestFixture.InvoiceId).CanFile.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateFilingAccess_PastThePerRequestCap_LeavesTheRestUnchecked()
    {
        // The search route can never exceed the cap (top is capped at 50), so the cap is exercised on the
        // shipped service directly, resolved from the same host.
        using var scope = _fixture.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<OfficeSearchService>();
        var targets = Enumerable.Range(0, OfficeSearchService.MaxFilingAccessChecks + 5)
            .Select(_ => ("Matter", Guid.NewGuid()))
            .ToList();

        var verdicts = await search.EvaluateFilingAccessAsync(targets, "rights=ReadAccess,AppendToAccess");

        verdicts.Take(OfficeSearchService.MaxFilingAccessChecks).Should().OnlyContain(v => v == true);
        verdicts.Skip(OfficeSearchService.MaxFilingAccessChecks).Should().OnlyContain(v => v == null,
            "past the cap a record is reported as not checked, which the client treats as today's behaviour");
        _fixture.Probe.LastRecordsRequest.Should().HaveCount(OfficeSearchService.MaxFilingAccessChecks);
    }

    #endregion

    #region Suggestions (the Outlook cards and the ribbon quick-save's prediction)

    [Theory]
    [InlineData("ReadAccess", false)]
    [InlineData("ReadAccess,AppendToAccess", true)]
    public async Task CandidateFilingAccess_ForANamedCandidate_AgreesWithTheSave(string rights, bool expected)
    {
        // The suggestions route's 200 path runs the real Association Engine, which these hosts cannot stand
        // up without scaffolding it (CommunicationsEndpointsContractTests' own note). The route delegates its
        // filing verdicts to this helper, so the helper is exercised directly with the shipped service.
        var matter = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        var candidates = new[]
        {
            Candidate("sprk_matter", matter),
            Candidate("sprk_project", unreadable),
            Candidate("sprk_matter", matter), // the same record proposed for a second field
        };
        var names = new Dictionary<string, string> { [matter.ToString()] = "Acme Onboarding" };

        using var scope = _fixture.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<OfficeSearchService>();
        var before = _fixture.Probe.RecordsCalls;

        var filingAccess = await OfficeCommunicationsEndpoints.ResolveCandidateFilingAccessAsync(
            search, candidates, names, $"rights={rights}", CancellationToken.None);

        filingAccess.Should().ContainKey(matter.ToString()).WhoseValue.Should().Be(expected);
        filingAccess.Should().NotContainKey(unreadable.ToString(),
            "a candidate with no resolved name is one the caller cannot read; the client drops it, so it is not asked about");
        _fixture.Probe.RecordsCalls.Should().Be(before + 1);
        _fixture.Probe.LastRecordsRequest.Should().ContainSingle("the duplicate candidate is asked about once");

        // ...and the save agrees. The client sends the FRIENDLY type for this candidate (LOGICAL_TO_ENTITY_TYPE).
        using var client = _fixture.CreateClientWithRights(rights);
        var save = await client.PostAsJsonAsync("/api/office/save", SaveRequestTargeting("Matter", matter));
        (await IsRefusedByTheAssociationGateAsync(save)).Should().Be(!expected);
    }

    #endregion

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static SuggestedCandidate Candidate(string entity, Guid id) => new()
    {
        Field = "sprk_regardingmatter",
        TargetEntity = entity,
        TargetId = id.ToString(),
        ReinforcedConfidence = 0.9,
        DeterministicConfidence = 0.9,
        Written = false,
        Conflict = false
    };

    private static object SaveRequestTargeting(string entityType, Guid entityId) => new
    {
        contentType = 0, // SaveContentType.Email
        email = new { subject = "Filing note", senderEmail = "counsel@example.com", senderName = "Counsel" },
        targetEntity = new { entityType, entityId }
    };

    /// <summary>
    /// True when the save was stopped by its association gate: 403 <c>insufficient_rights</c> (the caller lacks
    /// the right) or 400 <c>OFFICE_002</c> (a type the save cannot file to). Anything else means the gate let the
    /// target through; whatever the test host's save pipeline does next is not this property's concern.
    /// </summary>
    private static async Task<bool> IsRefusedByTheAssociationGateAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        string? reasonCode = null;
        string? errorCode = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    reasonCode = document.RootElement.TryGetProperty("reasonCode", out var r) ? r.GetString() : null;
                    errorCode = document.RootElement.TryGetProperty("errorCode", out var e) ? e.GetString() : null;
                }
            }
            catch (JsonException)
            {
                // Not a ProblemDetails body, so not the gate.
            }
        }

        return (response.StatusCode == HttpStatusCode.Forbidden && reasonCode == "insufficient_rights")
            || (response.StatusCode == HttpStatusCode.BadRequest && errorCode == "OFFICE_002");
    }
}

/// <summary>
/// Host for <see cref="OfficeFilingAccessParityContractTests"/>: the search and the save, with the caller's
/// rights stated on the bearer token.
/// </summary>
public sealed class OfficeFilingAccessTestFixture : WorkspaceTestFixture
{
    public static readonly Guid CallerSystemUserId = new("aaaaaaaa-0000-0000-0000-0000000000a4");
    public static readonly Guid MatterId = new("84000000-0000-0000-0000-000000000001");
    public static readonly Guid ProjectId = new("84000000-0000-0000-0000-000000000002");
    public static readonly Guid InvoiceId = new("84000000-0000-0000-0000-000000000003");
    public static readonly Guid AccountId = new("84000000-0000-0000-0000-000000000004");
    public static readonly Guid ContactId = new("84000000-0000-0000-0000-000000000005");

    public RightsFromTokenProbe Probe { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // One caller identity across the class; the save route is capped at 10 requests/minute/user and
            // the parity theory makes 30 saves, so the cap would produce 429s that read as refusals.
            ["OfficeRateLimit:Enabled"] = "false"
        }));

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(Probe);

            services.RemoveAll<IImpersonatedCommunicationQuery>();
            services.AddSingleton<IImpersonatedCommunicationQuery>(new OneRowPerTypeDataverse());

            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddSingleton<ICallerSystemUserResolver>(new FixedCallerResolver(CallerSystemUserId));
        });
    }

    /// <summary>A caller holding exactly <paramref name="rights"/> on every record, except <paramref name="failFor"/>.</summary>
    public HttpClient CreateClientWithRights(string rights, params Guid[] failFor)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = failFor.Length == 0 ? $"rights={rights}" : $"rights={rights};fail={string.Join(',', failFor)}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Answers from the bearer token, identically for one record and for several. A record listed under
    /// <c>fail=</c> gets <see cref="AccessRights.None"/>, which is what the shipped probe returns when it cannot ask.
    /// </summary>
    public sealed class RightsFromTokenProbe : CallerRecordAccessProbe
    {
        private int _recordsCalls;

        public RightsFromTokenProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        /// <summary>How many multi-record lookups have been made.</summary>
        public int RecordsCalls => Volatile.Read(ref _recordsCalls);

        /// <summary>The targets of the most recent multi-record lookup.</summary>
        public IReadOnlyList<(string EntitySet, Guid RecordId)> LastRecordsRequest { get; private set; } =
            Array.Empty<(string, Guid)>();

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(Answer(callerBearerToken, recordId));

        public override Task<IReadOnlyList<AccessRights>> GetCallerRightsForRecordsAsync(
            string? callerBearerToken,
            IReadOnlyList<(string EntitySet, Guid RecordId)> targets,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _recordsCalls);
            LastRecordsRequest = targets.ToList();
            return Task.FromResult<IReadOnlyList<AccessRights>>(
                targets.Select(t => Answer(callerBearerToken, t.RecordId)).ToList());
        }

        private static AccessRights Answer(string? token, Guid recordId)
        {
            if (token is null || !token.StartsWith("rights=", StringComparison.Ordinal))
                return AccessRights.None;

            var parts = token["rights=".Length..].Split(';');
            var failing = parts.Length > 1 && parts[1].StartsWith("fail=", StringComparison.Ordinal)
                ? parts[1]["fail=".Length..].Split(',').Select(Guid.Parse).ToHashSet()
                : new HashSet<Guid>();

            return failing.Contains(recordId)
                ? AccessRights.None
                : DataverseAccessRightsMapper.FromAccessRightsString(parts[0]);
        }
    }

    /// <summary>One readable record of each searchable type, all matching "Ac".</summary>
    private sealed class OneRowPerTypeDataverse : IImpersonatedCommunicationQuery
    {
        private static readonly Dictionary<string, string> RowJsonBySet = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matters"] = $$"""{ "sprk_matterid": "{{MatterId}}", "sprk_mattername": "Acme Onboarding", "sprk_matternumber": "M-1", "modifiedon": "2026-09-01T00:00:00Z" }""",
            ["sprk_projects"] = $$"""{ "sprk_projectid": "{{ProjectId}}", "sprk_projectname": "Acme Migration", "sprk_projectnumber": "P-1", "modifiedon": "2026-09-02T00:00:00Z" }""",
            ["sprk_invoices"] = $$"""{ "sprk_invoiceid": "{{InvoiceId}}", "sprk_name": "Acme Retainer", "sprk_invoicenumber": "I-1", "modifiedon": "2026-09-03T00:00:00Z" }""",
            ["accounts"] = $$"""{ "accountid": "{{AccountId}}", "name": "Acme Supplies", "accountnumber": "A-1", "modifiedon": "2026-09-04T00:00:00Z" }""",
            ["contacts"] = $$"""{ "contactid": "{{ContactId}}", "fullname": "Ac Buyer", "jobtitle": "Buyer", "modifiedon": "2026-09-05T00:00:00Z" }"""
        };

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            IReadOnlyList<Dictionary<string, JsonElement>> rows = RowJsonBySet.TryGetValue(entitySetName, out var json)
                ? new[] { JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)! }
                : Array.Empty<Dictionary<string, JsonElement>>();
            return Task.FromResult(rows);
        }
    }

    private sealed class FixedCallerResolver : ICallerSystemUserResolver
    {
        private readonly Guid _systemUserId;

        public FixedCallerResolver(Guid systemUserId) => _systemUserId = systemUserId;

        public Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct) =>
            Task.FromResult(CallerSystemUserResolution.Resolved(_systemUserId.ToString("D")));
    }
}
