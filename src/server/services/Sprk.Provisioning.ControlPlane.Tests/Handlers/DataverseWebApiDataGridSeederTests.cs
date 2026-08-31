// -----------------------------------------------------------------------------
// DataverseWebApiDataGridSeederTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiDataGridSeeder (task 151,
// Wave G-5 Batch G-5A — H12b DV-REST port, DataGrid scope).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) — parity with DataverseWebApiSolutionImporterTests
// (task 141).
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  All 4 rows not-yet-existing -> POST fired per row with the correct
//       body shape (sprk_name/sprk_entitylogicalname/sprk_configjson, +
//       sprk_gridconfigurationid ONLY for the 3 rows with a fixed id — the
//       "My Team's Reviews" row has none).
//   T2  All 4 rows already-existing -> PATCH fired per row with ONLY
//       sprk_configjson in the body (no sprk_name/sprk_entitylogenamename
//       re-sent) — no POST calls at all.
//   T3  Name-based lookup escapes an embedded apostrophe (My Team's Reviews)
//       per OData literal-escaping convention ('' not \').
//   T4  Lookup GET failure on the FIRST row -> Failed immediately; only 1
//       HTTP call made (fail-fast parity with $ErrorActionPreference='Stop').
//   T5  Token acquisition failure -> Failed, zero HTTP calls made.
//   T6  ReadEmbeddedConfigJson resolves all 4 embedded resources to
//       non-empty JSON content (build-time embedding wired correctly).
//   T7  Source grep defense-in-depth — the production file contains no
//       "ProcessStartInfo" (acceptance criterion #1).
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

public sealed class DataverseWebApiDataGridSeederTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    private static readonly string[] ExpectedNames =
    {
        "Communication Reconciliation - Needs Review",
        "Communication Reconciliation - My Team's Reviews",
        "Communication Reconciliation - Email Review All",
        "Communication Reconciliation - Email Review Completed",
    };

    // ---------- T1 all rows not-yet-existing -> POST with correct shape ----------

    [Fact]
    public async Task SeedAsync_AllRowsNotYetExisting_PostsEachWithCorrectBodyShape()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnPost = _ => JsonResponse(HttpStatusCode.Created, "{\"sprk_gridconfigurationid\":\"11111111-1111-1111-1111-111111111111\"}"),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        var posts = fake.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Should().HaveCount(4);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);

        // 3 of 4 rows declare a fixed id; "My Team's Reviews" (index 1) does not.
        foreach (var (post, index) in posts.Select((p, i) => (p, i)))
        {
            using var body = JsonDocument.Parse(post.Body!);
            body.RootElement.GetProperty("sprk_name").GetString().Should().Be(ExpectedNames[index]);
            body.RootElement.GetProperty("sprk_entitylogicalname").GetString().Should().Be("sprk_communication");
            body.RootElement.GetProperty("sprk_configjson").GetString().Should().NotBeNullOrEmpty();

            if (index == 1)
            {
                body.RootElement.TryGetProperty("sprk_gridconfigurationid", out _).Should().BeFalse(
                    "the 'My Team's Reviews' row has no fixed id in the source script");
            }
            else
            {
                body.RootElement.TryGetProperty("sprk_gridconfigurationid", out _).Should().BeTrue();
            }
        }
    }

    // ---------- T2 all rows already-existing -> PATCH sprk_configjson only ----------

    [Fact]
    public async Task SeedAsync_AllRowsAlreadyExisting_PatchesConfigJsonOnly_NoPostCalls()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK,
                ValueJson(("22222222-2222-2222-2222-222222222222", "whatever"))),
            OnPatch = _ => JsonResponse(HttpStatusCode.OK, "{}"),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        var patches = fake.Requests.Where(r => r.Method == HttpMethod.Patch).ToList();
        patches.Should().HaveCount(4);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);

        foreach (var patch in patches)
        {
            using var body = JsonDocument.Parse(patch.Body!);
            body.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "sprk_configjson" });
        }
    }

    // ---------- T3 apostrophe escaping in name-based lookup ----------

    [Fact]
    public async Task SeedAsync_NameLookupWithApostrophe_EscapesPerODataConvention()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnPost = _ => JsonResponse(HttpStatusCode.Created, "{}"),
        };
        var seeder = BuildSeeder(fake);

        await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        var gets = fake.Requests.Where(r => r.Method == HttpMethod.Get).ToList();
        // Row index 1 = "My Team's Reviews" (name-only lookup, no fixed id).
        var nameLookup = gets[1];
        var decodedQuery = Uri.UnescapeDataString(nameLookup.Uri.Query);
        decodedQuery.Should().Contain("My Team''s Reviews", "OData string literals escape ' as ''");
    }

    // ---------- T4 fail-fast on first GET failure ----------

    [Fact]
    public async Task SeedAsync_FirstLookupFails_ReturnsFailed_OnlyOneHttpCallMade()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"boom\"}}"),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("Needs Review");
        fake.Requests.Should().HaveCount(1, "fail-fast parity with the source script's $ErrorActionPreference='Stop'");
    }

    // ---------- T5 token acquisition failure ----------

    [Fact]
    public async Task SeedAsync_TokenAcquisitionFails_ReturnsFailed_NoHttpCallsMade()
    {
        var fake = new FakeDataverseHandler();
        var seeder = BuildSeeder(fake, throwingCredential: true);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Failed);
        result.Diagnostic.Should().Contain("token acquisition", "diagnostic should name the failure mode");
        fake.Requests.Should().BeEmpty();
    }

    // ---------- T6 embedded resource resolution ----------

    [Theory]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.needs-review.gridconfiguration.json")]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.per-team.gridconfiguration.json")]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.email-review-all.gridconfiguration.json")]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.GridConfig.email-review-completed.gridconfiguration.json")]
    public void ReadEmbeddedConfigJson_ResolvesEachEmbeddedResource_ReturnsNonEmptyJson(string logicalName)
    {
        var json = DataverseWebApiDataGridSeeder.ReadEmbeddedConfigJson(logicalName);

        json.Should().NotBeNullOrWhiteSpace();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow("the embedded config JSON must be well-formed");
    }

    // ---------- T7 grep-collision defense ----------

    [Fact]
    public void ProductionFile_DoesNotContainProcessStartInfo()
    {
        var path = LocateSourceFile("DataverseWebApiDataGridSeeder.cs");
        var content = File.ReadAllText(path);
        content.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static AppConfigSeedInput BuildInput() => new(CustomerId, TenantId, EnvUrl);

    private static DataverseWebApiDataGridSeeder BuildSeeder(
        FakeDataverseHandler handler, bool throwingCredential = false)
    {
        TokenCredential Factory(string tenantId) => throwingCredential ? new ThrowingCredential() : new FakeCredential();

        return new DataverseWebApiDataGridSeeder(
            new HttpClient(handler),
            Options.Create(new AppConfigSeedOptions { DataverseRequestTimeout = TimeSpan.FromSeconds(5) }),
            NullLogger<DataverseWebApiDataGridSeeder>.Instance,
            Factory);
    }

    private static string EmptyValueJson() => """{"value":[]}""";

    private static string ValueJson(params (string Id, string Name)[] rows)
    {
        var items = rows.Select(r => $$"""{"sprk_gridconfigurationid":"{{r.Id}}","sprk_name":"{{r.Name}}"}""");
        return $$"""{"value":[{{string.Join(",", items)}}]}""";
    }

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
    /// GET/PATCH/POST requests to per-verb delegates.
    /// </summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGet { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPatch { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPost { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!, body));

            if (request.Method == HttpMethod.Get)
            {
                return (OnGet ?? throw new InvalidOperationException("unexpected GET — no OnGet wired"))(request);
            }
            if (request.Method == HttpMethod.Patch)
            {
                return (OnPatch ?? throw new InvalidOperationException("unexpected PATCH — no OnPatch wired"))(request);
            }
            if (request.Method == HttpMethod.Post)
            {
                return (OnPost ?? throw new InvalidOperationException("unexpected POST — no OnPost wired"))(request);
            }
            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }
    }
}
