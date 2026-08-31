// -----------------------------------------------------------------------------
// DataverseWebApiWorkspaceLayoutSeederTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiWorkspaceLayoutSeeder (task
// 151, Wave G-5 Batch G-5A — H12b DV-REST port, WorkspaceLayout scope).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) — parity with DataverseWebApiDataGridSeederTests
// (sibling task 151 file) / DataverseWebApiSolutionImporterTests (task 141).
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  All layouts not-yet-existing -> POST fired per layout with the
//       correct body shape (sprk_name/sprk_layouttemplateid/sprk_sectionsjson/
//       sprk_isdefault/sprk_sortorder/sprk_issystem=true).
//   T2  All layouts already-existing -> SKIPPED (no POST, no PATCH) — parity
//       with the source script's DEFAULT no-`-Force` behavior.
//   T3  Lookup filter includes BOTH sprk_name eq '{name}' AND
//       sprk_issystem eq true (the script's own Find-SystemLayout shape).
//   T4  Fail-fast on first GET failure — only 1 HTTP call made.
//   T5  Token acquisition failure -> Failed, zero HTTP calls made.
//   T6  BuildSectionsJson (single-column) places the sectionId in row-1 with
//       3 empty trailing rows; an unsupported template id throws
//       NotSupportedException (parity with the script's own hard stop).
//   T7  ReadEmbeddedLayouts resolves the embedded system-layouts.json,
//       returns &gt;0 layouts, and includes the known "Daily Briefing"
//       default layout.
//   T8  Source grep defense-in-depth — the production file contains no
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

public sealed class DataverseWebApiWorkspaceLayoutSeederTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    // ---------- T1 all layouts not-yet-existing -> POST with correct shape ----------

    [Fact]
    public async Task SeedAsync_AllLayoutsNotYetExisting_PostsEachWithCorrectBodyShape()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK, EmptyValueJson()),
            OnPost = _ => JsonResponse(HttpStatusCode.Created, "{\"sprk_workspacelayoutid\":\"33333333-3333-3333-3333-333333333333\"}"),
        };
        var seeder = BuildSeeder(fake);
        var expectedCount = DataverseWebApiWorkspaceLayoutSeeder.ReadEmbeddedLayouts().Count;

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        var posts = fake.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Should().HaveCount(expectedCount);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);

        using var firstBody = JsonDocument.Parse(posts[0].Body!);
        firstBody.RootElement.GetProperty("sprk_name").GetString().Should().NotBeNullOrEmpty();
        firstBody.RootElement.GetProperty("sprk_layouttemplateid").GetString().Should().Be("single-column");
        firstBody.RootElement.GetProperty("sprk_sectionsjson").GetString().Should().NotBeNullOrEmpty();
        firstBody.RootElement.GetProperty("sprk_issystem").GetBoolean().Should().BeTrue();
        firstBody.RootElement.TryGetProperty("sprk_isdefault", out _).Should().BeTrue();
        firstBody.RootElement.TryGetProperty("sprk_sortorder", out _).Should().BeTrue();
    }

    // ---------- T2 all layouts already-existing -> skip, no writes ----------

    [Fact]
    public async Task SeedAsync_AllLayoutsAlreadyExisting_SkipsEveryRow_NoPostOrPatch()
    {
        var fake = new FakeDataverseHandler
        {
            OnGet = _ => JsonResponse(HttpStatusCode.OK,
                ValueJson(("44444444-4444-4444-4444-444444444444", "whatever"))),
        };
        var seeder = BuildSeeder(fake);

        var result = await seeder.SeedAsync(BuildInput(), CancellationToken.None);

        result.Status.Should().Be(AppConfigSeederStatus.Ok);
        result.Diagnostic.Should().Contain("skipped");
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch,
            "default (no -Force) behavior skips an existing row rather than refreshing it");
    }

    // ---------- T3 lookup filter shape ----------

    [Fact]
    public async Task SeedAsync_LookupFilter_IncludesNameAndIsSystemTrue()
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
        decodedQuery.Should().Contain("sprk_issystem eq true");
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
        result.Diagnostic.Should().Contain("token acquisition");
        fake.Requests.Should().BeEmpty();
    }

    // ---------- T6 BuildSectionsJson ----------

    [Fact]
    public void BuildSectionsJson_SingleColumn_PlacesSectionInRow1_WithThreeEmptyTrailingRows()
    {
        var json = DataverseWebApiWorkspaceLayoutSeeder.BuildSectionsJson("daily-briefing", "single-column");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("scope").GetString().Should().Be("my");

        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(4);
        rows[0].GetProperty("sections")[0].GetString().Should().Be("daily-briefing");
        rows[1].GetProperty("sections")[0].GetString().Should().BeEmpty();
        rows[2].GetProperty("sections")[0].GetString().Should().BeEmpty();
        rows[3].GetProperty("sections")[0].GetString().Should().BeEmpty();
    }

    [Fact]
    public void BuildSectionsJson_UnsupportedTemplate_ThrowsNotSupportedException()
    {
        var act = () => DataverseWebApiWorkspaceLayoutSeeder.BuildSectionsJson("todo", "two-column");

        act.Should().Throw<NotSupportedException>().WithMessage("*single-column*");
    }

    // ---------- T7 ReadEmbeddedLayouts ----------

    [Fact]
    public void ReadEmbeddedLayouts_ReturnsNonEmptyList_IncludingDailyBriefingDefault()
    {
        var layouts = DataverseWebApiWorkspaceLayoutSeeder.ReadEmbeddedLayouts();

        layouts.Should().NotBeEmpty();
        var dailyBriefing = layouts.Should().ContainSingle(l => l.Name == "Daily Briefing").Which;
        dailyBriefing.IsDefault.Should().BeTrue();
        dailyBriefing.LayoutTemplateId.Should().Be("single-column");
        dailyBriefing.SectionId.Should().Be("daily-briefing");
    }

    // ---------- T8 grep-collision defense ----------

    [Fact]
    public void ProductionFile_DoesNotContainProcessStartInfo()
    {
        var path = LocateSourceFile("DataverseWebApiWorkspaceLayoutSeeder.cs");
        var content = File.ReadAllText(path);
        content.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static AppConfigSeedInput BuildInput() => new(CustomerId, TenantId, EnvUrl);

    private static DataverseWebApiWorkspaceLayoutSeeder BuildSeeder(
        FakeDataverseHandler handler, bool throwingCredential = false)
    {
        TokenCredential Factory(string tenantId) => throwingCredential ? new ThrowingCredential() : new FakeCredential();

        return new DataverseWebApiWorkspaceLayoutSeeder(
            new HttpClient(handler),
            Options.Create(new AppConfigSeedOptions { DataverseRequestTimeout = TimeSpan.FromSeconds(5) }),
            NullLogger<DataverseWebApiWorkspaceLayoutSeeder>.Instance,
            Factory);
    }

    private static string EmptyValueJson() => """{"value":[]}""";

    private static string ValueJson(params (string Id, string Name)[] rows)
    {
        var items = rows.Select(r => $$"""{"sprk_workspacelayoutid":"{{r.Id}}","sprk_name":"{{r.Name}}"}""");
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
    /// GET/POST requests to per-verb delegates. No PATCH delegate exists —
    /// this seeder's default (no -Force) behavior never issues a PATCH.
    /// </summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGet { get; set; }
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
            if (request.Method == HttpMethod.Post)
            {
                return (OnPost ?? throw new InvalidOperationException("unexpected POST — no OnPost wired"))(request);
            }
            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }
    }
}
