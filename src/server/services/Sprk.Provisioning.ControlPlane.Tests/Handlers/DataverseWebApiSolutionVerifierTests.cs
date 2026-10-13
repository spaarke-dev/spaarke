// -----------------------------------------------------------------------------
// DataverseWebApiSolutionVerifierTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseWebApiSolutionVerifier (task 141,
// Wave G-4 — H6 Web-API import port). ADR-038 alignment: pure C# unit tests
// over a real HttpClient wrapping a hand-rolled fake HttpMessageHandler (NOT
// Mock&lt;HttpMessageHandler&gt;, banned per testing.md).
//
// COVERAGE (T218b — one package, SpaarkeMaster, checked for presence AND type):
//   T1  Present with the requested type → AllPresent (version, solutionid,
//       isManaged); the GET filters on SpaarkeMaster and selects ismanaged.
//   T1b Unmanaged requested + unmanaged installed → AllPresent.
//   T2  Absent → Missing naming SpaarkeMaster.
//   T2b Installed with the OTHER type → Missing (wrong type is not success).
//   T3  Token acquisition failure -> Missing, diagnostic notes auth, no HTTP.
//   T4  Non-success HTTP status -> Missing.
//   T5  Malformed JSON response -> Missing (does not throw).
//   T6  Invalid target URL -> Missing, zero HTTP calls made.
//   T7  Source grep defense-in-depth — no "pac solution"/"ProcessStartInfo".
// -----------------------------------------------------------------------------

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

    private const string SolutionId = "11111111-2222-3333-4444-555555555555";

    // ---------- T1 present with the requested type ----------

    [Fact]
    public async Task VerifyAsync_PresentManaged_ManagedRequested_ReturnsRecord()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, SolutionsJson(("1.2.0.0", true))));
        var verifier = BuildVerifier(handler);

        var outcome = await verifier.VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        var record = outcome.Should().BeOfType<SolutionVerificationOutcome.AllPresent>().Subject
            .ImportedRecords.Should().ContainSingle().Subject;
        record.SolutionUniqueName.Should().Be("SpaarkeMaster");
        record.Version.Should().Be("1.2.0.0");
        record.SolutionId.Should().Be(SolutionId);
        record.IsManaged.Should().BeTrue();

        var request = handler.Requests.Should().ContainSingle().Which;
        request.AbsolutePath.Should().Be("/api/data/v9.2/solutions");
        Uri.UnescapeDataString(request.Query).Should().Contain("ismanaged").And.Contain("uniquename eq 'SpaarkeMaster'");
    }

    [Fact]
    public async Task VerifyAsync_PresentUnmanaged_UnmanagedRequested_ReturnsRecord()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, SolutionsJson(("1.2.0.0", false))));

        var outcome = await BuildVerifier(handler).VerifyAsync(BuildRequest(managed: false), CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.AllPresent>()
            .Which.ImportedRecords.Single().IsManaged.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_PresentButNotAtTheImportedVersion_ReturnsMissing()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, SolutionsJson(("1.1.0.0", true))));
        var request = BuildRequest(managed: true) with { ExpectedVersion = "1.2.0.0" };

        var outcome = await BuildVerifier(handler).VerifyAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>()
            .Which.Diagnostic.Should().Contain("'1.1.0.0'").And.Contain("'1.2.0.0'");
    }

    [Fact]
    public async Task VerifyAsync_AtTheImportedVersion_Padded_IsPresent()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, SolutionsJson(("1.2.0.0", true))));
        var request = BuildRequest(managed: true) with { ExpectedVersion = "1.2" };

        var outcome = await BuildVerifier(handler).VerifyAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.AllPresent>();
    }

    // ---------- T2 absent / wrong type ----------

    [Fact]
    public async Task VerifyAsync_Absent_ReturnsMissing()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, SolutionsJson()));

        var outcome = await BuildVerifier(handler).VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>()
            .Which.MissingUniqueNames.Should().ContainSingle().Which.Should().Be("SpaarkeMaster");
    }

    [Fact]
    public async Task VerifyAsync_InstalledWithOtherType_ReturnsMissing()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, SolutionsJson(("1.2.0.0", false))));

        var outcome = await BuildVerifier(handler).VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        var missing = outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>().Subject;
        missing.Diagnostic.Should().Contain("installed as unmanaged");
    }

    // ---------- T3 token acquisition failure ----------

    [Fact]
    public async Task VerifyAsync_TokenAcquisitionFails_ReturnsMissingAllWithAuthDiagnostic()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("should not be called"));
        var verifier = BuildVerifier(handler, throwingCredential: true);

        var outcome = await verifier.VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.Unavailable>(
                "nothing was learned about the environment — Resumable, not a quarantine")
            .Which.Diagnostic.Should().Contain("Token acquisition failed");
        handler.Requests.Should().BeEmpty();
    }

    // ---------- T4 non-success HTTP status ----------

    [Fact]
    public async Task VerifyAsync_NonSuccessStatus_ReturnsMissingAll()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var verifier = BuildVerifier(handler);

        var outcome = await verifier.VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        var missing = outcome.Should().BeOfType<SolutionVerificationOutcome.Missing>().Subject;
        missing.MissingUniqueNames.Should().Equal("SpaarkeMaster");
        missing.Diagnostic.Should().Contain("403");
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(408)]
    public async Task VerifyAsync_TransientStatus_ReturnsUnavailable(int status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status));

        var outcome = await BuildVerifier(handler).VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.Unavailable>();
    }

    [Fact]
    public async Task VerifyAsync_RequestTimeout_ReturnsUnavailable()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("HttpClient.Timeout elapsed"));

        var outcome = await BuildVerifier(handler).VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

        outcome.Should().BeOfType<SolutionVerificationOutcome.Unavailable>();
    }

    // ---------- T5 malformed JSON ----------

    [Fact]
    public async Task VerifyAsync_MalformedJson_ReturnsMissingAll_DoesNotThrow()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, "{ not valid json"));
        var verifier = BuildVerifier(handler);

        var act = async () => await verifier.VerifyAsync(BuildRequest(managed: true), CancellationToken.None);

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
            Managed: true,
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

    private static SolutionVerificationRequest BuildRequest(bool managed) => new(
        TargetDataverseUrl: EnvUrl,
        TenantId: TenantId,
        ClientId: ClientId,
        Managed: managed,
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

    private static string SolutionsJson(params (string Version, bool IsManaged)[] entries)
    {
        var items = entries.Select(e =>
            $$"""{"uniquename":"SpaarkeMaster","version":"{{e.Version}}","solutionid":"{{SolutionId}}","ismanaged":{{(e.IsManaged ? "true" : "false")}}}""");
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
