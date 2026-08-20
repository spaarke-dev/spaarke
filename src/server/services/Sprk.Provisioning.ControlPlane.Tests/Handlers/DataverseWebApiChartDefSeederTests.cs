// -----------------------------------------------------------------------------
// DataverseWebApiChartDefSeederTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiChartDefSeeder (task 152,
// Wave G-5 Batch G-5B — H12b GREENFIELD seeder, ChartDefinition scope).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) — parity with the sibling task 151
// DataverseWebApiDataGridSeederTests (same always-refresh-on-match idiom).
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  All 4 rows not-yet-existing -> POST fired per row with the correct
//       5-field contract body shape.
//   T2  All 4 rows already-existing -> PATCH fired per row with the same
//       5-field contract body (always-refresh, NOT skip) — no POST calls.
//       Re-running twice is idempotent (2nd run also PATCHes, never
//       duplicate-inserts).
//   T3  Name-based lookup filter shape (sprk_name eq '...').
//   T4  Fail-fast on first GET failure — only 1 HTTP call made.
//   T5  Token acquisition failure -> Failed, zero HTTP calls made.
//   T6  ReadEmbeddedChartDef resolves all 4 embedded resources with the
//       expected sprk_visualtype (100000009 = DueDateCardList) + non-null
//       fetchxmlquery/drillthroughtarget.
//   T7  Source grep defense-in-depth — the production file contains no
//       "ProcessStartInfo".
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

public sealed class DataverseWebApiChartDefSeederTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    // ---------- T1 all rows not-yet-existing -> POST with correct shape ----------

    [Fact]
    public async Task SeedAsync_AllRowsNotYetExisting_PostsEachWithCorrectBodyShape()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnPost = _ => JsonResponse(HttpStatusCode.Created, "{\"sprk_chartdefinitionid\":\"11111111-1111-1111-1111-111111111111\"}"),
        };
        var seeder = BuildSeeder(fake);
        var expectedCount = DataverseWebApiChartDefSeeder.EmbeddedResourceLogicalNames.Count;

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        var posts = fake.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Should().HaveCount(expectedCount).And.HaveCount(4);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);

        using var firstBody = JsonDocument.Parse(posts[0].Body!);
        firstBody.RootElement.GetProperty("sprk_name").GetString().Should().NotBeNullOrEmpty();
        firstBody.RootElement.GetProperty("sprk_entitylogicalname").GetString().Should().Be("sprk_todo");
        firstBody.RootElement.GetProperty("sprk_visualtype").GetInt32().Should().Be(100000009);
        firstBody.RootElement.GetProperty("sprk_drillthroughtarget").GetString().Should().Be("sprk_smarttodo.html");
        firstBody.RootElement.GetProperty("sprk_fetchxmlquery").GetString().Should().NotBeNullOrEmpty();
        firstBody.RootElement.GetProperty("sprk_contextfieldname").GetString().Should().NotBeNullOrEmpty();
    }

    // ---------- T2 all rows already-existing -> PATCH (always refresh), idempotent on re-run ----------

    [Fact]
    public async Task SeedAsync_AllRowsAlreadyExisting_PatchesEachRow_NoPostCalls_IdempotentOnRerun()
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
            body.RootElement.TryGetProperty("sprk_name", out _).Should().BeTrue();
            body.RootElement.TryGetProperty("sprk_visualtype", out _).Should().BeTrue();
        }

        // Idempotency retry — a 2nd invocation against the same already-seeded
        // state still refreshes via PATCH, never duplicate-inserts via POST.
        var secondResult = await seeder.SeedAsync(BuildInput(), CancellationToken.None);
        secondResult.Status.Should().Be(AppConfigSeederStatus.Ok);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    // ---------- T3 lookup filter shape ----------

    [Fact]
    public async Task SeedAsync_LookupFilter_IsNameBased()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnPost = _ => JsonResponse(HttpStatusCode.Created, "{}"),
        };
        var seeder = BuildSeeder(fake);

        await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        var firstGet = fake.Requests.First(r => r.Method == HttpMethod.Get);
        var decodedQuery = Uri.UnescapeDataString(firstGet.Uri.Query);
        decodedQuery.Should().Contain("sprk_name eq '");
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
        fake.Requests.Should().HaveCount(1, "fail-fast parity with the sibling task 151 seeders");
    }

    // ---------- T5 token acquisition failure ----------

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

    // ---------- T6 embedded resource resolution ----------

    [Theory]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-matter.json", "sprk_regardingmatter")]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-project.json", "sprk_regardingproject")]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-invoice.json", "sprk_regardinginvoice")]
    [InlineData("Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.ChartDefs.upcoming-todos-workassignment.json", "sprk_regardingworkassignment")]
    public void ReadEmbeddedChartDef_ResolvesEachEmbeddedResource_ReturnsExpectedShape(string logicalName, string expectedContextField)
    {
        var item = DataverseWebApiChartDefSeeder.ReadEmbeddedChartDef(logicalName);

        item.Name.Should().NotBeNullOrWhiteSpace();
        item.EntityLogicalName.Should().Be("sprk_todo");
        item.ContextFieldName.Should().Be(expectedContextField);
        item.DrillThroughTarget.Should().Be("sprk_smarttodo.html");
        item.VisualType.Should().Be(100000009, "DueDateCardList");
        item.FetchXmlQuery.Should().NotBeNullOrWhiteSpace();
        var act = () => System.Xml.Linq.XDocument.Parse(item.FetchXmlQuery!);
        act.Should().NotThrow("the embedded FetchXML must be well-formed");
    }

    // ---------- T7 grep-collision defense ----------

    [Fact]
    public void ProductionFile_DoesNotContainProcessStartInfo()
    {
        var path = LocateSourceFile("DataverseWebApiChartDefSeeder.cs");
        var content = File.ReadAllText(path);
        content.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static AppConfigSeedInput BuildInput() => new(CustomerId, TenantId, EnvUrl);

    private static DataverseWebApiChartDefSeeder BuildSeeder(
        FakeDataverseHandler handler, bool throwingCredential = false)
    {
        TokenCredential Factory(string tenantId) => throwingCredential ? new ThrowingCredential() : new FakeCredential();

        return new DataverseWebApiChartDefSeeder(
            new HttpClient(handler),
            Options.Create(new AppConfigSeedOptions { DataverseRequestTimeout = TimeSpan.FromSeconds(5) }),
            NullLogger<DataverseWebApiChartDefSeeder>.Instance,
            Factory);
    }

    private static string EmptyValueJson() => """{"value":[]}""";

    private static string ValueJson(params (string Id, string Name)[] rows)
    {
        var items = rows.Select(r => $$"""{"sprk_chartdefinitionid":"{{r.Id}}","sprk_name":"{{r.Name}}"}""");
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
