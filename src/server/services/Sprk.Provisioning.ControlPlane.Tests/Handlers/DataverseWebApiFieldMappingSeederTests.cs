// -----------------------------------------------------------------------------
// DataverseWebApiFieldMappingSeederTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiFieldMappingSeeder (task
// 152, Wave G-5 Batch G-5B — H12b GREENFIELD seeder, FieldMapping scope).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) — parity with the sibling task 151 seeder tests.
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  SeedProfiles static data shape matches
//       SPAARKE-FIELD-MAPPING-FRAMEWORK.md's documented schema (3 profiles,
//       8/4/8 rule counts, Invoice has no law-firm/external/internal rules).
//   T2  All profiles + rules not-yet-existing -> POSTs 3 profiles + 20 rules
//       with the correct @odata.bind shapes + mapping_type/field-type
//       integers (Copy=0, Lookup=1) per FIELD-MAPPING-ADMIN-GUIDE.md.
//   T3  All profiles + rules already-existing -> IDEMPOTENT — zero POST
//       calls, every outcome "skipped (exists...)".
//   T4  Missing sprk_recordtype_ref row for the source entity -> Failed,
//       diagnostic names the exact FIELD-MAPPING-ADMIN-GUIDE.md remediation.
//   T5  Fail-fast on first recordtype_ref lookup HTTP failure.
//   T6  Token acquisition failure -> Failed, zero HTTP calls made.
//   T7  Active DI registration (AppConfigSeedModule.cs) contains zero
//       `new DeferredAppConfigSeeder(` call sites (acceptance criterion #1).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiFieldMappingSeederTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    private static readonly IReadOnlyDictionary<string, string> RecordTypeIds = new Dictionary<string, string>
    {
        ["sprk_matter"] = "10000000-0000-0000-0000-000000000001",
        ["sprk_event"] = "10000000-0000-0000-0000-000000000002",
        ["sprk_invoice"] = "10000000-0000-0000-0000-000000000003",
        ["sprk_reportcard"] = "10000000-0000-0000-0000-000000000004",
    };

    // ---------- T1 SeedProfiles static data shape ----------

    [Fact]
    public void SeedProfiles_MatchesDocumentedSchema_3ProfilesWith8_4_8RuleCounts()
    {
        DataverseWebApiFieldMappingSeeder.SeedProfiles.Should().HaveCount(3);

        var eventProfile = DataverseWebApiFieldMappingSeeder.SeedProfiles
            .Should().ContainSingle(p => p.TargetEntityLogicalName == "sprk_event").Which;
        eventProfile.ProfileName.Should().Be("Matter to Event (Attorney Matrix)");
        eventProfile.Rules.Should().HaveCount(8);

        var invoiceProfile = DataverseWebApiFieldMappingSeeder.SeedProfiles
            .Should().ContainSingle(p => p.TargetEntityLogicalName == "sprk_invoice").Which;
        invoiceProfile.ProfileName.Should().Be("Matter to Invoice (Attorney Matrix)");
        invoiceProfile.Rules.Should().HaveCount(4);
        invoiceProfile.Rules.Should().OnlyContain(r =>
            !r.TargetField.Contains("lawfirm", StringComparison.OrdinalIgnoreCase) &&
            r.TargetField != "sprk_assignedtoexternal" &&
            r.TargetField != "sprk_assignedtointernal",
            "Invoice has no law-firm field and no external/internal field at all (verified via MCP describe)");

        var reportCardProfile = DataverseWebApiFieldMappingSeeder.SeedProfiles
            .Should().ContainSingle(p => p.TargetEntityLogicalName == "sprk_reportcard").Which;
        reportCardProfile.ProfileName.Should().Be("Matter to Report Card (Attorney Matrix)");
        reportCardProfile.Rules.Should().HaveCount(8);
        reportCardProfile.Rules.Should().ContainSingle(r => r.SourceField == "sprk_assignedlawfirm1")
            .Which.TargetField.Should().Be("sprk_assignedtolawfirm1", "Report Card renames law-firm 1 specifically");
        reportCardProfile.Rules.Should().ContainSingle(r => r.SourceField == "sprk_assignedlawfirm2")
            .Which.TargetField.Should().Be("sprk_assignedlawfirm2", "law-firm 2 keeps the same name on Report Card");
    }

    // ---------- T2 all not-yet-existing -> creates everything ----------

    [Fact]
    public async Task SeedAsync_NothingExistsYet_Creates3ProfilesAnd20RulesWithCorrectShape()
    {
        var fake = new FakeDataverseHandler
        {
            OnGetRecordType = req => RecordTypeResponse(req),
            OnGetProfile = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnGetRule = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnPostProfile = _ => JsonResponse(HttpStatusCode.Created, "{\"sprk_fieldmappingprofileid\":\"20000000-0000-0000-0000-000000000001\"}"),
            OnPostRule = _ => JsonResponse(HttpStatusCode.Created, "{\"sprk_fieldmappingruleid\":\"30000000-0000-0000-0000-000000000001\"}"),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);

        var profilePosts = fake.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("sprk_fieldmappingprofiles")).ToList();
        var rulePosts = fake.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("sprk_fieldmappingrules")).ToList();
        profilePosts.Should().HaveCount(3);
        rulePosts.Should().HaveCount(20, "8 (Event) + 4 (Invoice) + 8 (Report Card) = 20 total rules");

        using var firstProfileBody = JsonDocument.Parse(profilePosts[0].Body!);
        firstProfileBody.RootElement.GetProperty("sprk_name").GetString().Should().NotBeNullOrEmpty();
        firstProfileBody.RootElement.GetProperty("sprk_sourcerecordtype@odata.bind").GetString()
            .Should().Be($"/sprk_recordtype_refs({RecordTypeIds["sprk_matter"]})");
        firstProfileBody.RootElement.TryGetProperty("sprk_targetrecordtype@odata.bind", out _).Should().BeTrue();

        using var firstRuleBody = JsonDocument.Parse(rulePosts[0].Body!);
        firstRuleBody.RootElement.GetProperty("sprk_mapping_type").GetInt32().Should().Be(0, "Copy");
        firstRuleBody.RootElement.GetProperty("sprk_sourcefieldtype").GetInt32().Should().Be(1, "Lookup");
        firstRuleBody.RootElement.GetProperty("sprk_targetfieldtype").GetInt32().Should().Be(1, "Lookup");
        firstRuleBody.RootElement.GetProperty("sprk_isactive").GetBoolean().Should().BeTrue();
        firstRuleBody.RootElement.TryGetProperty("sprk_FieldMappingProfile@odata.bind", out var bindEl).Should().BeTrue();
        bindEl.GetString().Should().StartWith("/sprk_fieldmappingprofiles(");
    }

    // ---------- T3 idempotency: everything already exists ----------

    [Fact]
    public async Task SeedAsync_EverythingAlreadyExists_IsIdempotent_NoPostCallsMade()
    {
        var fake = new FakeDataverseHandler
        {
            OnGetRecordType = req => RecordTypeResponse(req),
            OnGetProfile = _ => JsonResponse(HttpStatusCode.OK, ValueJson("sprk_fieldmappingprofileid", "40000000-0000-0000-0000-000000000001")),
            OnGetRule = _ => JsonResponse(HttpStatusCode.OK, ValueJson("sprk_fieldmappingruleid", "50000000-0000-0000-0000-000000000001")),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        result.Diagnostic.Should().Contain("skipped");
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post,
            "a re-run against fully-seeded data must be a pure no-op — find-then-skip, never a duplicate insert");

        // Re-run (idempotency retry) — same result, still zero POSTs.
        var secondResult = await seeder.SeedAsync(BuildInput(), CancellationToken.None);
        secondResult.Status.Should().Be(AppConfigSeederStatus.Ok);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    // ---------- T4 missing recordtype_ref row -> Failed, fail-loud diagnostic ----------

    [Fact]
    public async Task SeedAsync_MissingRecordTypeRefForSource_ReturnsFailed_WithRemediationDiagnostic()
    {
        var fake = new FakeDataverseHandler
        {
            OnGetRecordType = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()), // no row for sprk_matter
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("sprk_recordtype_ref");
        result.Diagnostic.Should().Contain("FIELD-MAPPING-ADMIN-GUIDE.md");
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    // ---------- T5 fail-fast on first HTTP failure ----------

    [Fact]
    public async Task SeedAsync_FirstRecordTypeLookupFails_ReturnsFailed_OnlyOneHttpCallMade()
    {
        var fake = new FakeDataverseHandler
        {
            OnGetRecordType = _ => JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"boom\"}}"),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        fake.Requests.Should().HaveCount(1, "fail-fast — the very first lookup failure aborts the invocation");
    }

    // ---------- T6 token acquisition failure ----------

    [Fact]
    public async Task SeedAsync_TokenAcquisitionFails_ReturnsFailed_NoHttpCallsMade()
    {
        var fake = new FakeDataverseHandler();
        var seeder = BuildSeeder(fake, throwingCredential: true);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("token acquisition");
        fake.Requests.Should().BeEmpty();
    }

    // ---------- T7 acceptance criterion: 0 active DI call sites for DeferredAppConfigSeeder ----------

    [Fact]
    public void AppConfigSeedModule_ContainsNoActiveDeferredAppConfigSeederRegistration()
    {
        var path = LocateSourceFile("AppConfigSeedModule.cs");
        var content = File.ReadAllText(path);
        content.Should().NotContain("new DeferredAppConfigSeeder(",
            "FR-16's 4-scope delivery is complete — field-mapping and chart-def now route through real seeders");
    }

    // ---------- helpers ----------

    private static AppConfigSeedInput BuildInput() => new(CustomerId, TenantId, EnvUrl);

    private static DataverseWebApiFieldMappingSeeder BuildSeeder(
        FakeDataverseHandler handler, bool throwingCredential = false)
    {
        TokenCredential Factory(string tenantId) => throwingCredential ? new ThrowingCredential() : new FakeCredential();

        return new DataverseWebApiFieldMappingSeeder(
            new HttpClient(handler),
            Options.Create(new AppConfigSeedOptions { DataverseRequestTimeout = TimeSpan.FromSeconds(5) }),
            NullLogger<DataverseWebApiFieldMappingSeeder>.Instance,
            Factory);
    }

    private static HttpResponseMessage RecordTypeResponse(HttpRequestMessage request)
    {
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        foreach (var (logicalName, id) in RecordTypeIds)
        {
            if (query.Contains($"sprk_recordlogicalname eq '{logicalName}'", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, $$"""{"value":[{"sprk_recordtype_refid":"{{id}}"}]}""");
            }
        }
        return JsonResponse(HttpStatusCode.OK, EmptyValueJson());
    }

    private static string EmptyValueJson() => """{"value":[]}""";

    private static string ValueJson(string idField, string id)
        => $$"""{"value":[{"{{idField}}":"{{id}}"}]}""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string LocateSourceFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core",
                "Handlers", "AppConfigSeed", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}.");
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential chain failure");
    }

    /// <summary>
    /// Hand-rolled fake <see cref="HttpMessageHandler"/> (NOT
    /// Mock&lt;HttpMessageHandler&gt; — banned per testing.md) that routes
    /// GET/POST requests to per-entity-set delegates (this seeder issues 3
    /// distinct GET shapes: recordtype_refs, fieldmappingprofiles,
    /// fieldmappingrules — routing on AbsolutePath is more robust than
    /// call-ordering).
    /// </summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGetRecordType { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGetProfile { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGetRule { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPostProfile { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPostRule { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!, body));

            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get)
            {
                if (path.Contains("sprk_recordtype_refs", StringComparison.Ordinal))
                {
                    return (OnGetRecordType ?? throw new InvalidOperationException("unexpected GET recordtype_refs — no OnGetRecordType wired"))(request);
                }
                if (path.Contains("sprk_fieldmappingprofiles", StringComparison.Ordinal))
                {
                    return (OnGetProfile ?? throw new InvalidOperationException("unexpected GET fieldmappingprofiles — no OnGetProfile wired"))(request);
                }
                if (path.Contains("sprk_fieldmappingrules", StringComparison.Ordinal))
                {
                    return (OnGetRule ?? throw new InvalidOperationException("unexpected GET fieldmappingrules — no OnGetRule wired"))(request);
                }
            }
            if (request.Method == HttpMethod.Post)
            {
                if (path.Contains("sprk_fieldmappingprofiles", StringComparison.Ordinal))
                {
                    return (OnPostProfile ?? throw new InvalidOperationException("unexpected POST fieldmappingprofiles — no OnPostProfile wired"))(request);
                }
                if (path.Contains("sprk_fieldmappingrules", StringComparison.Ordinal))
                {
                    return (OnPostRule ?? throw new InvalidOperationException("unexpected POST fieldmappingrules — no OnPostRule wired"))(request);
                }
            }
            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }
    }
}
