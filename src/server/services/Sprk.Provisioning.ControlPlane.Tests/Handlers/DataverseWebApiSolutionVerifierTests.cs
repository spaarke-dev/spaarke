// -----------------------------------------------------------------------------
// DataverseWebApiSolutionVerifierTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiSolutionVerifier (task 141,
// Wave G-4 — H6 Web-API import port). ADR-038 alignment: pure C# unit tests
// over a real HttpClient wrapping a hand-rolled fake HttpMessageHandler (NOT
// Mock&lt;HttpMessageHandler&gt;, banned per testing.md).
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  AllPresent happy path — parses uniquename/version/solutionid per
//       catalog entry, preserving Tier from the catalog.
//   T2  Missing — one expected solution absent from the response.
//   T3  Token acquisition failure -> Missing (all names, diagnostic notes auth).
//   T4  Non-success HTTP status -> Missing.
//   T5  Malformed JSON response -> Missing (does not throw).
//   T6  Invalid target URL -> Missing, zero HTTP calls made.
//   T7  Source grep defense-in-depth — no "pac solution"/"ProcessStartInfo".
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiSolutionVerifierTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ClientSecret = "test-client-secret-placeholder";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";

    private static ImmutableArray<CanonicalSolutionEntry> Catalog() => ImmutableArray.Create(
        new CanonicalSolutionEntry("SolA", "SolA", "Solution A", Tier: 1),
        new CanonicalSolutionEntry("SolB", "SolB", "Solution B", Tier: 2));

    // ---------- T1 happy path ----------

    [Fact]
    public async Task VerifyAsync_AllPresent_ReturnsManifestWithCorrectTiers()
    {
        var json = SolutionsJson(
            ("SolA", "1.0.0.0", "11111111-2222-3333-4444-555555555555"),
            ("SolB", "2.0.0.0", "22222222-3333-4444-5555-666666666666"));
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        var verifier = BuildVerifier(handler);

        var outcome = await verifier.VerifyAsync(BuildRequest(), CancellationToken.None);

        var allPresent = outcome.Should().BeOfType<SolutionVerificationOutcome.AllPresent>().Subject;
        allPresent.ImportedRecords.Should().HaveCount(2);
        var solA = allPresent.ImportedRecords.Single(r => r.SolutionUniqueName == "SolA");
        solA.Version.Should().Be("1.0.0.0");
        solA.SolutionId.Should().Be("11111111-2222-3333-4444-555555555555");
        solA.Tier.Should().Be(1);
        allPresent.ImportedRecords.Single(r => r.SolutionUniqueName == "SolB").Tier.Should().Be(2);

        var request = handler.Requests.Should().ContainSingle().Which;
        request.AbsolutePath.Should().Be("/api/data/v9.2/solutions");
        request.Query.Should().Contain("uniquename").And.Contain("version").And.Contain("solutionid");
    }

    // ---------- T2 missing ----------

    [Fact]
    public async Task VerifyAsync_OneSolutionMissing_ReturnsMissingOutcome()
    {
        var json = SolutionsJson(("SolA", "1.0.0.0", "11111111-2222-3333-4444-555555555555"));
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        var verifier = BuildVerifier(handler);

        var outcome = await verifier.VerifyAsync(BuildRequest(), CancellationToken.None);

        var missing = outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>().Subject;
        missing.MissingUniqueNames.Should().ContainSingle().Which.Should().Be("SolB");
    }

    // ---------- T3 token acquisition failure ----------

    [Fact]
    public async Task VerifyAsync_TokenAcquisitionFails_ReturnsMissingAllWithAuthDiagnostic()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("should not be called"));
        var verifier = BuildVerifier(handler, throwingCredential: true);

        var outcome = await verifier.VerifyAsync(BuildRequest(), CancellationToken.None);

        var missing = outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>().Subject;
        missing.MissingUniqueNames.Should().BeEquivalentTo(new[] { "SolA", "SolB" });
        missing.Diagnostic.Should().Contain("Token acquisition failed");
        handler.Requests.Should().BeEmpty();
    }

    // ---------- T4 non-success HTTP status ----------

    [Fact]
    public async Task VerifyAsync_NonSuccessStatus_ReturnsMissingAll()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var verifier = BuildVerifier(handler);

        var outcome = await verifier.VerifyAsync(BuildRequest(), CancellationToken.None);

        var missing = outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>().Subject;
        missing.MissingUniqueNames.Should().BeEquivalentTo(new[] { "SolA", "SolB" });
        missing.Diagnostic.Should().Contain("403");
    }

    // ---------- T5 malformed JSON ----------

    [Fact]
    public async Task VerifyAsync_MalformedJson_ReturnsMissingAll_DoesNotThrow()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, "{ not valid json"));
        var verifier = BuildVerifier(handler);

        var act = async () => await verifier.VerifyAsync(BuildRequest(), CancellationToken.None);

        var outcome = await act.Should().NotThrowAsync();
        outcome.Subject.Should().BeOfType<SolutionVerificationOutcome.Missing>();
    }

    // ---------- T6 invalid URL ----------

    [Fact]
    public async Task VerifyAsync_InvalidTargetUrl_ReturnsMissingAll_ZeroHttpCalls()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("should not be called"));
        var verifier = BuildVerifier(handler);
        var request = new SolutionVerificationRequest(
            TargetDataverseUrl: "not-a-url",
            TenantId: TenantId,
            ClientId: ClientId,
            ExpectedCatalog: Catalog(),
            ClientSecret: ClientSecret);

        var outcome = await verifier.VerifyAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>();
        handler.Requests.Should().BeEmpty();
    }

    // ---------- T7 source grep defense-in-depth ----------

    [Fact]
    public void ProductionSource_ContainsNoPacSolutionOrProcessStartInfoReferences()
    {
        var path = LocateSourceFile("DataverseWebApiSolutionVerifier.cs");
        var text = File.ReadAllText(path);
        text.Should().NotContain("pac solution");
        text.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static SolutionVerificationRequest BuildRequest() => new(
        TargetDataverseUrl: EnvUrl,
        TenantId: TenantId,
        ClientId: ClientId,
        ExpectedCatalog: Catalog(),
        ClientSecret: ClientSecret);

    private static DataverseWebApiSolutionVerifier BuildVerifier(FakeHandler handler, bool throwingCredential = false)
    {
        TokenCredential Factory(string tenantId, string clientId, string clientSecret)
            => throwingCredential ? new ThrowingCredential() : new FakeCredential();

        return new DataverseWebApiSolutionVerifier(
            new HttpClient(handler),
            Options.Create(new SolutionImportOptions()),
            NullLogger<DataverseWebApiSolutionVerifier>.Instance,
            Factory);
    }

    private static string SolutionsJson(params (string UniqueName, string Version, string SolutionId)[] entries)
    {
        var items = entries.Select(e =>
            $$"""{"uniquename":"{{e.UniqueName}}","version":"{{e.Version}}","solutionid":"{{e.SolutionId}}"}""");
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
                "Handlers", "SolutionImport", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}.");
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

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<Uri> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responder(request));
        }
    }
}
