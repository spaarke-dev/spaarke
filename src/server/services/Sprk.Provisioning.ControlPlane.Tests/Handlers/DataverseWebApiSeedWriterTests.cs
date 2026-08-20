// -----------------------------------------------------------------------------
// DataverseWebApiSeedWriterTests.cs
//
// Unit tests over DataverseWebApiSeedWriter (task 150, Wave G-5 Batch G-5A —
// H12a YamlDotNet manifest engine + DV-REST seed writes). Replaces
// InvokeSeedManifestScriptRunner (deleted — pwsh shell-out).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test over a real HttpClient wrapping a hand-rolled
//   fake HttpMessageHandler (NOT Mock<HttpMessageHandler>, banned per
//   testing.md) + a hand-rolled fake TokenCredential (NOT a real
//   DefaultAzureCredential network path) — same seam shape as
//   DataverseWebApiSolutionImporterTests' credentialFactory test seam
//   (task 141). NO live Dataverse / Azure API.
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  Happy path fresh seed — real embedded manifest.yaml + the 4 real
//       embedded seed-content JSON files against an EMPTY target env: every
//       row across type-lookups (19) + knowledge (10) + skills (10) +
//       output-types (5) = 44 rows upserted; PENDING/PLACEHOLDER markers
//       present for the 8 out-of-writer-scope artifacts; topological order
//       respected (type-lookups fully seeded before knowledge/skills, which
//       dependsOn it).
//   T2  Idempotency (acceptance criterion 2) — re-running InvokeAsync a
//       SECOND time against a handler that now reports every row as
//       existing (from T1's own POSTs) fires ZERO new POST calls — a no-op.
//   T3  Same request-shape as H12c (acceptance criterion 3) — POST + GET
//       requests carry IDENTICAL header shape (Bearer auth, OData-Version
//       4.0, OData-MaxVersion 4.0, Prefer return=representation, Accept
//       application/json) and the SAME $filter=sprk_name eq '...' existence-
//       check query shape as DataverseWebApiModelDeploymentReferenceWriter
//       (H12c).
//   T4  Token acquisition failure -> Failure outcome, zero HTTP calls made.
//   T5  Invalid target Dataverse URL -> Failure outcome without touching the
//       credential/HTTP path.
//   T6  Dataverse write fails mid-run (500 on a later artifact) -> Failure
//       outcome citing the failing artifact id + the row count already
//       upserted before the failure (retry-safe framing).
//   T7  Source grep defense-in-depth — production file contains neither
//       "powershell-yaml" nor "ProcessStartInfo" nor "Install-Module".
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiSeedWriterTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com";

    private static readonly string[] TypeLookupEntitySets =
        { "sprk_analysisactiontype", "sprk_aiskilltype", "sprk_aiknowledgetype", "sprk_aitooltype" };

    // ---------- T1 happy path ----------

    [Fact]
    public async Task InvokeAsync_FreshSeed_UpsertsEveryDirectlySeededRow_AndReportsPendingForTheRest()
    {
        var handler = new FakeDataverseHandler();
        var writer = BuildWriter(handler);

        var outcome = await writer.InvokeAsync(BuildRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<SeedManifestInvocationOutcome.Success>().Subject;

        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Should().HaveCount(44, "type-lookups(19) + knowledge(10) + skills(10) + output-types(5)");

        var gets = handler.Requests.Where(r => r.Method == HttpMethod.Get).ToList();
        gets.Should().HaveCount(44, "one existence-check GET per seeded row");

        success.StdoutSummary.Should().Contain("44 row(s) upserted across 12 artifact(s)");
        success.StdoutSummary.Should().Contain("[type-lookups] OK");
        success.StdoutSummary.Should().Contain("[knowledge] OK");
        success.StdoutSummary.Should().Contain("[skills] OK");
        success.StdoutSummary.Should().Contain("[output-types] OK");
        success.StdoutSummary.Should().Contain("[aimodeldeployment] PLACEHOLDER");
        success.StdoutSummary.Should().Contain("[input-schemas] PENDING");
        success.StdoutSummary.Should().Contain("[output-schemas] PENDING");
        success.StdoutSummary.Should().Contain("[actions-r7] PENDING");
        success.StdoutSummary.Should().Contain("[tools-r7] PENDING");
        success.StdoutSummary.Should().Contain("[playbooks-mvp] PENDING");
        success.StdoutSummary.Should().Contain("[action-outputschema-patches] PENDING");
        success.StdoutSummary.Should().Contain("[playbook-consumers] PENDING");

        // Topological order: type-lookups (dependency of knowledge + skills)
        // is FULLY seeded before either dependent artifact's first request —
        // the 4 type-lookup entity sets never overlap with sprk_analysisknowledges
        // or sprk_analysisskills, so index comparison is unambiguous.
        var lastTypeLookupIndex = handler.Requests.FindLastIndex(r => TypeLookupEntitySets.Contains(EntitySetOf(r)));
        var firstKnowledgeIndex = handler.Requests.FindIndex(r => EntitySetOf(r) == "sprk_analysisknowledges");
        var firstSkillIndex = handler.Requests.FindIndex(r => EntitySetOf(r) == "sprk_analysisskills");
        lastTypeLookupIndex.Should().BeGreaterThanOrEqualTo(0);
        firstKnowledgeIndex.Should().BeGreaterThan(lastTypeLookupIndex);
        firstSkillIndex.Should().BeGreaterThan(lastTypeLookupIndex);
    }

    // ---------- T2 idempotency (acceptance criterion 2) ----------

    [Fact]
    public async Task InvokeAsync_SecondRunAgainstUnchangedManifest_IsNoOp()
    {
        var handler = new FakeDataverseHandler();
        var writer = BuildWriter(handler);
        var request = BuildRequest();

        var first = await writer.InvokeAsync(request, CancellationToken.None);
        first.Should().BeOfType<SeedManifestInvocationOutcome.Success>();
        handler.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(44);

        handler.Requests.Clear();

        var second = await writer.InvokeAsync(request, CancellationToken.None);
        var success = second.Should().BeOfType<SeedManifestInvocationOutcome.Success>().Subject;

        handler.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(
            0, "existence-check-then-insert is idempotent — every row is now reported as existing");
        handler.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(44, "existence is still re-checked per row");
        success.StdoutSummary.Should().Contain("0 row(s) upserted across 12 artifact(s)");
    }

    // ---------- T3 same request-shape as H12c (acceptance criterion 3) ----------

    [Fact]
    public async Task InvokeAsync_Requests_UseSameShapeAsH12cUpsertIdiom()
    {
        var handler = new FakeDataverseHandler();
        var writer = BuildWriter(handler, token: "unit-test-bearer-token");

        await writer.InvokeAsync(BuildRequest(), CancellationToken.None);

        var post = handler.Requests.First(r => r.Method == HttpMethod.Post);
        post.Headers.Authorization.Should().NotBeNull();
        post.Headers.Authorization!.Scheme.Should().Be("Bearer");
        post.Headers.Authorization.Parameter.Should().Be("unit-test-bearer-token");
        post.Headers.Accept.Should().Contain(h => h.MediaType == "application/json");
        post.Headers.GetValues("OData-Version").Should().ContainSingle().Which.Should().Be("4.0");
        post.Headers.GetValues("OData-MaxVersion").Should().ContainSingle().Which.Should().Be("4.0");
        post.Headers.GetValues("Prefer").Should().ContainSingle().Which.Should().Be("return=representation");
        post.RequestUri!.AbsolutePath.Should().StartWith("/api/data/v9.2/");

        var get = handler.Requests.First(r => r.Method == HttpMethod.Get);
        get.Headers.Authorization!.Scheme.Should().Be("Bearer");
        get.Headers.Authorization.Parameter.Should().Be("unit-test-bearer-token");
        var decodedQuery = Uri.UnescapeDataString(get.RequestUri!.Query);
        decodedQuery.Should().Contain("$filter=sprk_name eq '");
        decodedQuery.Should().Contain("$select=sprk_name");
    }

    // ---------- T4 token acquisition failure ----------

    [Fact]
    public async Task InvokeAsync_TokenAcquisitionThrows_ReturnsFailure_NoHttpCalls()
    {
        var handler = new FakeDataverseHandler();
        using var httpClient = new HttpClient(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new AiSeedChainOptions());
        var writer = new DataverseWebApiSeedWriter(
            httpClient, options, NullLogger<DataverseWebApiSeedWriter>.Instance,
            _ => new ThrowingTokenCredential());

        var outcome = await writer.InvokeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SeedManifestInvocationOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("Token acquisition failed");
        handler.Requests.Should().BeEmpty();
    }

    // ---------- T5 invalid Dataverse URL ----------

    [Fact]
    public async Task InvokeAsync_InvalidDataverseUrl_ReturnsFailure()
    {
        var handler = new FakeDataverseHandler();
        var writer = BuildWriter(handler);
        var badRequest = new SeedManifestInvocationRequest(CustomerId, TenantId, "not-a-valid-url");

        var outcome = await writer.InvokeAsync(badRequest, CancellationToken.None);

        outcome.Should().BeOfType<SeedManifestInvocationOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("not a valid absolute URI");
        handler.Requests.Should().BeEmpty();
    }

    // ---------- T6 mid-run failure ----------

    [Fact]
    public async Task InvokeAsync_DataverseWriteFailsMidRun_ReturnsFailure_WithPartialUpsertCount()
    {
        var handler = new FakeDataverseHandler { FailPostForEntitySet = "sprk_analysisskills" };
        var writer = BuildWriter(handler);

        var outcome = await writer.InvokeAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SeedManifestInvocationOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("'skills'");
        // type-lookups (19) + knowledge (10) already upserted before skills fails.
        failure.Diagnostic.Should().Contain("29 row(s)");
    }

    // ---------- T7 source grep defense-in-depth ----------

    [Fact]
    public void ProductionSource_ContainsNoPowerShellYamlOrProcessStartInfoReferences()
    {
        var path = LocateSourceFile("DataverseWebApiSeedWriter.cs");
        var text = File.ReadAllText(path);
        text.Should().NotContain("powershell-yaml");
        text.Should().NotContain("Install-Module");
        text.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static SeedManifestInvocationRequest BuildRequest() => new(CustomerId, TenantId, EnvUrl);

    private static DataverseWebApiSeedWriter BuildWriter(HttpMessageHandler handler, string token = "fake-token")
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AiSeedChainOptions());
        return new DataverseWebApiSeedWriter(
            httpClient, options, NullLogger<DataverseWebApiSeedWriter>.Instance,
            _ => new FakeTokenCredential(token));
    }

    private static string EntitySetOf(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.Split('/').Last();

    private static string LocateSourceFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core",
                "Handlers", "AiSeedChain", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}.");
    }

    private sealed class ThrowingTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential failure — no real network call made");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated credential failure — no real network call made");
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        private readonly string _token;
        public FakeTokenCredential(string token) => _token = token;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(_token, DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Fake Dataverse Web API transport. GET (existence check) returns a
    /// matching row iff the entity/name pair is in <see cref="ExistingKeys"/>;
    /// POST (create) succeeds (204) and records the new row into
    /// <see cref="ExistingKeys"/> — UNLESS <see cref="FailPostForEntitySet"/>
    /// matches, which returns a 500 to simulate a mid-run Dataverse fault.
    /// </summary>
    private sealed class FakeDataverseHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public HashSet<string> ExistingKeys { get; } = new(StringComparer.Ordinal);
        public string? FailPostForEntitySet { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var entitySet = request.RequestUri!.AbsolutePath.Split('/').Last();

            if (request.Method == HttpMethod.Get)
            {
                var name = ExtractFilterName(request.RequestUri!.Query);
                var exists = ExistingKeys.Contains($"{entitySet}::{name}");
                var json = exists
                    ? $"{{\"value\":[{{\"sprk_name\":\"{name}\"}}]}}"
                    : "{\"value\":[]}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                if (string.Equals(entitySet, FailPostForEntitySet, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{\"error\":{\"message\":\"simulated Dataverse fault\"}}", Encoding.UTF8, "application/json"),
                    };
                }

                var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var name = doc.RootElement.GetProperty("sprk_name").GetString()!;
                ExistingKeys.Add($"{entitySet}::{name}");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"unexpected request: {request.Method} {request.RequestUri}");
        }

        private static string ExtractFilterName(string query)
        {
            var decoded = Uri.UnescapeDataString(query);
            var match = Regex.Match(decoded, "sprk_name eq '([^']*)'");
            if (!match.Success)
            {
                throw new InvalidOperationException($"could not extract sprk_name filter from query: {query}");
            }
            return match.Groups[1].Value;
        }
    }
}
