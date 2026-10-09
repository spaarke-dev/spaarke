// -----------------------------------------------------------------------------
// E2EValidationRunnerTests.cs
//
// L2 CONTROL-PLANE unit tests for E2EValidationRunner (task 181, Phase C''
// Wave G-7 Batch G-7B; extended by G-8 Batch 11 which graduated the four SC #5
// sample-workload checks from permanently-Skipped to real authenticated
// checks). Proves the REAL HttpClient probe path -- not a hardcoded verdict --
// via a hand-rolled FakeHttpMessageHandler (never Mock<HttpMessageHandler>;
// ADR-038 path #1 + KEEP-path rules per .claude/constraints/testing.md section
// MUST NOT B1).
//
// PATH: ADR-038 KEEP paths (eight, incl. Amendment A1) -- this file is a component-scoped unit
// test of the runner class (a pure L2 seam with no BFF wiring). It exercises
// behavior through the runner's PUBLIC RunAsync surface. It lives in the
// existing Tests project alongside the sibling H13 real-probe test files
// (AiSearchTenantFilterInvariantProbeTests, SpeContainerTenantDerivationInvariantProbeTests,
// ArmCostEnvelopeCheckerTests). Path is not
// under tests/integration/** because the runner is not itself an integration
// boundary -- the LIVE-run integration lands in Phase F rerun (task 186).
//
// COVERAGE:
//   task 181: health / ping / CORS each Pass + fail-status + transport-throw; parameter guards; the source-file
//        forcing functions (no shell-out, named-client convention, module registration).
//   task 230b (keyless proof — replaced the four G-8 Batch 11 sample-workload checks): token audience
//        api://{BffAppRegId}; the BFF refusing the L2 identity (401/403), the route missing (404), no token, a bad
//        BffAppRegId → Failure, NEVER a skip; each service's outcome → its own check (proved passes; refused /
//        key-credential / not-configured / failed / unknown / missing fail; unreachable is Inconclusive); a failure
//        wins over an inconclusive; transient call faults retry once, then Inconclusive.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Contracts.Provisioning;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class E2EValidationRunnerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "run-181-1";
    private const string DataverseUrl = "https://acme.crm.dynamics.com";
    private const string BffApiUrl = "https://bff-acme.azurewebsites.net";
    private const string TargetSlot = "production";
    private const string BffAppRegId = "2f3a9c1e-7b44-4d2e-9a10-5c6d7e8f9a0b";

    private static readonly string[] AllCheckNames = new[]
        {
            E2EValidationRunner.CheckBffHealthz,
            E2EValidationRunner.CheckBffPing,
            E2EValidationRunner.CheckCorsDataverseOrigin,
            E2EValidationRunner.CheckKeylessProof,
        }
        .Concat(KeylessProofContract.Services.All.Select(svc => E2EValidationRunner.KeylessProofCheckPrefix + svc))
        .ToArray();

    // -----------------------------------------------------------------------
    // AC-2: happy path -- all seven real checks Pass, Skipped list stable.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_HappyPath_ReturnsSuccessWithEveryCheckPassed()
    {
        var handler = new FakeBffHttpMessageHandler(HappyResponder);
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<E2EValidationOutcome.Success>().Subject;
        success.ChecksPassed.Should().BeEquivalentTo(AllCheckNames);
        handler.RequestedUrls.Should().Contain(u => u.EndsWith("/healthz", StringComparison.Ordinal));
        handler.RequestedUrls.Should().Contain(u => u.EndsWith("/ping", StringComparison.Ordinal));
        handler.RequestedUrls.Should().Contain(u => u.EndsWith(KeylessProofContract.Route, StringComparison.Ordinal));
        handler.RequestedMethods.Should().Contain(HttpMethod.Options);
    }

    [Fact]
    public async Task RunAsync_KeylessProof_IsAnAuthenticatedPost_ForTheBffAppRegistrationAudience()
    {
        // Task 230b: the BFF validates api://{appId}; a token for the host name (the pre-230b scope) is not its audience.
        var credential = new FakeTokenCredential();
        var handler = new FakeBffHttpMessageHandler(HappyResponder);
        var runner = BuildRunner(handler, credential);

        await runner.RunAsync(BuildRequest(), CancellationToken.None);

        credential.RequestedScopes.Should().Equal($"api://{BffAppRegId}/.default");
        var proof = handler.Requests.Single(r => r.Url.EndsWith(KeylessProofContract.Route, StringComparison.Ordinal));
        proof.Method.Should().Be(HttpMethod.Post);
        proof.AuthorizationScheme.Should().Be("Bearer");
        proof.AuthorizationParameter.Should().Be(FakeTokenCredential.TokenValue);
    }

    [Fact]
    public async Task RunAsync_HappyPath_EmitsInterimSkippedListVerbatim()
    {
        // Convergence-bonus defense: the ALWAYS-skipped ChecksSkipped list is
        // pinned exactly so a future task graduating a Skipped row to a real
        // check MUST update this test. Prevents silent drift. G-8 Batch 11
        // shrank this from 7 to 3 (the four sample rows are now real checks).
        var handler = new FakeBffHttpMessageHandler(HappyResponder);
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<E2EValidationOutcome.Success>().Subject;
        success.ChecksSkipped.Should().BeEquivalentTo(new[]
        {
            E2EValidationRunner.SkippedDataverseEnvVarsPresent,
            E2EValidationRunner.SkippedDataverseEnvVarsDevLeakage,
            E2EValidationRunner.SkippedNamingConformance,
        });
    }

    [Fact]
    public async Task RunAsync_HappyPath_WithWildcardCorsOrigin_ReturnsSuccess()
    {
        // Parity with the .ps1's `-eq $DataverseUrl -or -eq '*'` predicate.
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                return CorsPreflightResponse(HttpStatusCode.OK, "*");
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Success>();
    }

    // -----------------------------------------------------------------------
    // AC-2: individual probe failure paths -- each returns Failure with the
    //       specific check name in ChecksFailed + a diagnostic snippet.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_HealthzReturns503_ReturnsFailureCitingBffHealthz()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if ((req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/healthz", StringComparison.Ordinal)
                && req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().ContainSingle().Which.Should().Be(E2EValidationRunner.CheckBffHealthz);
        failure.Diagnostic.Should().Contain("503");
        failure.Diagnostic.Should().Contain("expected 200");
    }

    [Fact]
    public async Task RunAsync_PingReturns500_ReturnsFailureCitingBffPing()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if ((req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/ping", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckBffPing);
        failure.Diagnostic.Should().Contain("500");
    }

    [Fact]
    public async Task RunAsync_CorsHeaderAbsent_ReturnsFailureCitingCorsCheck()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                // 200 OK but NO Access-Control-Allow-Origin header at all.
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckCorsDataverseOrigin);
        failure.Diagnostic.Should().Contain("<absent>");
        failure.Diagnostic.Should().Contain(DataverseUrl);
    }

    [Fact]
    public async Task RunAsync_CorsHeaderIsForeignOrigin_ReturnsFailureCitingCorsCheck()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                return CorsPreflightResponse(HttpStatusCode.OK, "https://foreign.example.com");
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckCorsDataverseOrigin);
        failure.Diagnostic.Should().Contain("foreign.example.com");
    }

    [Fact]
    public async Task RunAsync_HealthzThrowsHttpRequestException_ReturnsFailure()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if ((req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/healthz", StringComparison.Ordinal)
                && req.Method == HttpMethod.Get)
            {
                throw new HttpRequestException("connection refused");
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckBffHealthz);
        failure.Diagnostic.Should().Contain("connection refused");
    }

    // -----------------------------------------------------------------------
    // Task 230b: the keyless proof — an auth failure is a FAILURE, never a skip.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RunAsync_BffRefusesTheL2Identity_IsAFailure_NeverASkip(HttpStatusCode status)
    {
        var handler = new FakeBffHttpMessageHandler(req =>
            IsKeylessProof(req) ? new HttpResponseMessage(status) : HappyResponder(req));

        var outcome = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Equal(E2EValidationRunner.CheckKeylessProof);
        failure.Diagnostic.Should().Contain(KeylessProofContract.AppRoleValue).And.Contain("H3");
    }

    [Fact]
    public async Task RunAsync_BffBuildWithoutTheRoute_404_IsInconclusive_RedeployThenResume()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
            IsKeylessProof(req) ? new HttpResponseMessage(HttpStatusCode.NotFound) : HappyResponder(req));

        var outcome = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Inconclusive>()
            .Which.Diagnostic.Should().Contain("404").And.Contain("230b");
    }

    [Fact]
    public async Task RunAsync_ABffError500_IsAFailure_NotTransient()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
            IsKeylessProof(req) ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : HappyResponder(req));

        var outcome = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Which.ChecksFailed.Should().Equal(E2EValidationRunner.CheckKeylessProof);
    }

    [Fact]
    public async Task RunAsync_PlainHttpBff_NeverSendsTheToken_AndFails()
    {
        var credential = new FakeTokenCredential();
        var handler = new FakeBffHttpMessageHandler(HappyResponder);

        var outcome = await BuildRunner(handler, credential).RunAsync(BuildRequest(bffApiUrl: "http://bff-acme.azurewebsites.net"), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Which.ChecksFailed.Should().Equal(E2EValidationRunner.CheckKeylessProof);
        credential.RequestedScopes.Should().BeEmpty();
        handler.RequestedUrls.Should().NotContain(u => u.EndsWith(KeylessProofContract.Route, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ATokenWithoutTheRole_FailsWithTheTokenCacheDiagnostic_WithoutCallingTheBff()
    {
        var jwt = "eyJhbGciOiJub25lIn0." + Base64Url("{\"aud\":\"api://x\",\"roles\":[\"Other.Role\"]}") + ".";
        var handler = new FakeBffHttpMessageHandler(HappyResponder);

        var outcome = await BuildRunner(handler, new FakeTokenCredential(jwt)).RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("carries no").And.Contain("24 hours").And.Contain("Other.Role");
        handler.RequestedUrls.Should().NotContain(u => u.EndsWith(KeylessProofContract.Route, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ATokenCarryingTheRole_IsSent()
    {
        var jwt = "eyJhbGciOiJub25lIn0." + Base64Url("{\"roles\":[\"" + KeylessProofContract.AppRoleValue + "\"]}") + ".";

        var outcome = await BuildRunner(new FakeBffHttpMessageHandler(HappyResponder), new FakeTokenCredential(jwt))
            .RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Success>();
    }

    [Fact]
    public async Task RunAsync_AStatusNumberThatIsNotAnInt_IsDropped_TheOutcomeStillDecides()
    {
        var body = KeylessBody().Replace("\"statusCode\":200", "\"statusCode\":1e100", StringComparison.Ordinal);
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req) ? Json(body) : HappyResponder(req));

        var outcome = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Success>();
    }

    [Fact]
    public async Task RunAsync_AnUnreachableThenARefusedDuplicate_TheRefusalWins()
    {
        var body = KeylessBody((KeylessProofContract.Services.Cosmos, KeylessProofContract.Outcomes.Unreachable, "timeout"))
            .Replace("]}", ",{\"service\":\"cosmos\",\"outcome\":\"refused\",\"code\":\"http-403\"}]}", StringComparison.Ordinal);
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req) ? Json(body) : HappyResponder(req));

        var outcome = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Failure>()
            .Which.ChecksFailed.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.Cosmos);
    }

    private static string Base64Url(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task RunAsync_NoTokenForTheBff_IsAFailure()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
            IsKeylessProof(req) ? throw new InvalidOperationException("must not be called without a token") : HappyResponder(req));
        var runner = BuildRunner(handler, new ThrowingTokenCredential(new Azure.Identity.CredentialUnavailableException("AADSTS500011")));

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Equal(E2EValidationRunner.CheckKeylessProof);
        failure.Diagnostic.Should().Contain("AADSTS500011");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task RunAsync_BffAppRegIdNotAnAppId_IsAFailure_AndNoTokenIsRequested(string bffAppRegId)
    {
        var credential = new FakeTokenCredential();
        var runner = BuildRunner(new FakeBffHttpMessageHandler(HappyResponder), credential);

        var outcome = await runner.RunAsync(BuildRequest(bffAppRegId: bffAppRegId), CancellationToken.None);

        outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Which.ChecksFailed.Should().Equal(E2EValidationRunner.CheckKeylessProof);
        credential.RequestedScopes.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Task 260 (ISS-014): the secure-record isolation census — same identity and call path as the keyless proof; only
    // `isolated` passes.
    // -----------------------------------------------------------------------

    private static bool IsCensus(HttpRequestMessage req) => PathIs(req, KeylessProofContract.SecureRecordIsolationCensus.Route);

    private static FakeBffHttpMessageHandler CensusAnswers(string body)
        => new(req => IsCensus(req) ? Json(body) : throw new InvalidOperationException($"unexpected call {req.RequestUri}"));

    [Fact]
    public async Task Census_Isolated_IsIsolated_AndIsAnAuthenticatedPostForTheBffAudience()
    {
        var credential = new FakeTokenCredential();
        var handler = CensusAnswers("{\"status\":\"isolated\",\"verdict\":\"Isolated\",\"findings\":[]}");

        var outcome = await BuildRunner(handler, credential).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Isolated>();
        credential.RequestedScopes.Should().Equal($"api://{BffAppRegId}/.default");
        var call = handler.Requests.Single();
        call.Url.Should().Be(BffApiUrl + KeylessProofContract.SecureRecordIsolationCensus.Route);
        call.Method.Should().Be(HttpMethod.Post);
        call.AuthorizationScheme.Should().Be("Bearer");
        call.AuthorizationParameter.Should().Be(FakeTokenCredential.TokenValue);
    }

    [Fact]
    public async Task Census_Findings_IsNotIsolated_CarryingEachFinding()
    {
        var handler = CensusAnswers(
            "{\"status\":\"findings\",\"verdict\":\"SecureBusinessUnitHasUsers\",\"findings\":["
            + "{\"verdict\":\"SecureBusinessUnitHasUsers\",\"message\":\"'Moved Attorney' sits in the Secure Record unit.\"},"
            + "{\"verdict\":\"HumanPrincipalReachesSecureBusinessUnit\",\"message\":\"'Root Paralegal' holds Deep.\"}]}");

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        var notIsolated = outcome.Should().BeOfType<SecureIsolationCensusOutcome.NotIsolated>().Subject;
        notIsolated.Status.Should().Be(KeylessProofContract.SecureRecordIsolationCensus.Findings);
        notIsolated.Verdict.Should().Be("SecureBusinessUnitHasUsers");
        notIsolated.Findings.Should().Equal(
            "SecureBusinessUnitHasUsers: 'Moved Attorney' sits in the Secure Record unit.",
            "HumanPrincipalReachesSecureBusinessUnit: 'Root Paralegal' holds Deep.");
    }

    [Fact]
    public async Task Census_Inert_IsNotIsolated_NeverAPass()
    {
        var handler = CensusAnswers(
            "{\"status\":\"inert\",\"verdict\":\"SecureBusinessUnitNotFound\",\"findings\":[{\"verdict\":\"SecureBusinessUnitNotFound\",\"message\":\"Secure Record BU not found\"}]}");

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.NotIsolated>()
            .Which.Status.Should().Be(KeylessProofContract.SecureRecordIsolationCensus.Inert);
    }

    [Fact]
    public async Task Census_Error_IsInconclusive()
    {
        var handler = CensusAnswers("{\"status\":\"error\",\"verdict\":\"unknown\",\"findings\":[]}");

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Inconclusive>()
            .Which.Diagnostic.Should().Contain("isolation is unknown");
    }

    [Theory]
    [InlineData("{\"status\":\"clean\",\"findings\":[]}")]                                        // unknown status
    [InlineData("{\"verdict\":\"Isolated\",\"findings\":[]}")]                                    // no status
    [InlineData("<html>gateway</html>")]                                                          // not JSON
    [InlineData("{\"status\":\"isolated\",\"findings\":[{\"verdict\":\"X\",\"message\":\"y\"}]}")] // isolated WITH findings
    [InlineData("{\"status\":\"ISOLATED\",\"findings\":[]}")]                                     // statuses are exact
    [InlineData("{\"status\":\" isolated\",\"findings\":[]}")]                                    // padded
    [InlineData("{\"status\":\"isolated!\",\"findings\":[]}")]                                    // decorated
    [InlineData("{\"status\":\"isolated\\u200b\",\"findings\":[]}")]                              // zero-width suffix
    public async Task Census_AnAnswerThisBuildCannotRead_FailsClosed(string body)
    {
        var outcome = await BuildRunner(CensusAnswers(body)).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Failed>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Census_BffRefusesTheL2Identity_Fails_NeverASkip(HttpStatusCode status)
    {
        var handler = new FakeBffHttpMessageHandler(_ => new HttpResponseMessage(status));

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Failed>()
            .Which.Diagnostic.Should().Contain(KeylessProofContract.AppRoleValue).And.Contain(E2EValidationRunner.CheckSecureIsolationCensus);
    }

    [Fact]
    public async Task Census_ABffBuildWithoutTheRoute_404_IsInconclusive_NamingTask260()
    {
        var handler = new FakeBffHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Inconclusive>()
            .Which.Diagnostic.Should().Contain("404").And.Contain("260");
    }

    [Fact]
    public async Task Census_ABffError500_Fails()
    {
        var handler = new FakeBffHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Failed>();
    }

    [Fact]
    public async Task Census_Transient503ThenIsolated_RetriesOnce()
    {
        var calls = 0;
        var handler = new FakeBffHttpMessageHandler(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json("{\"status\":\"isolated\",\"verdict\":\"Isolated\",\"findings\":[]}"));

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Isolated>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Census_TransientTwice_IsInconclusive()
    {
        var handler = new FakeBffHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout));

        var outcome = await BuildRunner(handler).RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Inconclusive>();
    }

    [Fact]
    public async Task Census_PlainHttpBff_NeverSendsTheToken_AndFails()
    {
        var credential = new FakeTokenCredential();
        var handler = new FakeBffHttpMessageHandler(_ => throw new InvalidOperationException("must not be called"));

        var outcome = await BuildRunner(handler, credential)
            .RunSecureIsolationCensusAsync(BuildRequest(bffApiUrl: "http://bff-acme.azurewebsites.net"), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Failed>().Which.Diagnostic.Should().Contain("not https");
        credential.RequestedScopes.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Census_BffAppRegIdNotAnAppId_Fails_AndNoTokenIsRequested(string bffAppRegId)
    {
        var credential = new FakeTokenCredential();
        var handler = new FakeBffHttpMessageHandler(_ => throw new InvalidOperationException("must not be called"));

        var outcome = await BuildRunner(handler, credential)
            .RunSecureIsolationCensusAsync(BuildRequest(bffAppRegId: bffAppRegId), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Failed>();
        credential.RequestedScopes.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Census_ATokenWithoutTheRole_Fails_WithoutCallingTheBff()
    {
        var jwt = "eyJhbGciOiJub25lIn0." + Base64Url("{\"roles\":[\"Other.Role\"]}") + ".";
        var handler = new FakeBffHttpMessageHandler(_ => throw new InvalidOperationException("must not be called"));

        var outcome = await BuildRunner(handler, new FakeTokenCredential(jwt))
            .RunSecureIsolationCensusAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SecureIsolationCensusOutcome.Failed>().Which.Diagnostic.Should().Contain("carries no");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void ParseCensus_ControlCharactersAreNeutralised_LongMessagesCapped_AndExtraFindingsCounted()
    {
        var findings = Enumerable.Range(1, E2EValidationRunner.MaxReportedFindings + 3)
            .Select(i => $"{{\"verdict\":\"SecureBusinessUnitHasUsers\",\"message\":\"user {i}\\r\\nInjected: line{new string('x', 600)}\"}}");
        var body = "{\"status\":\"findings\",\"verdict\":\"SecureBusinessUnitHasUsers\",\"findings\":[" + string.Join(",", findings) + "]}";

        var outcome = E2EValidationRunner.ParseCensus(body);

        var notIsolated = outcome.Should().BeOfType<SecureIsolationCensusOutcome.NotIsolated>().Subject;
        notIsolated.Findings.Should().HaveCount(E2EValidationRunner.MaxReportedFindings + 1);
        notIsolated.Findings.Take(E2EValidationRunner.MaxReportedFindings).Should().OnlyContain(f =>
            !f.Any(char.IsControl) && f.Length <= "SecureBusinessUnitHasUsers: ".Length + E2EValidationRunner.MaxReportedFindingLength + 3);
        notIsolated.Findings.Last().Should().Contain("+3 more");
    }

    [Theory]
    [InlineData("refused", "http-403")]
    [InlineData("key-credential", "key-configured:AzureOpenAI:ApiKey")]
    [InlineData("not-configured", "setting-missing:AzureOpenAI:Endpoint")]
    [InlineData("failed", "http-404")]
    [InlineData("healthy", "ok")] // an outcome this L2 build does not know is never a pass
    public async Task RunAsync_AServiceNotProved_FailsThatServiceCheck(string outcome, string code)
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? Json(KeylessBody((KeylessProofContract.Services.OpenAiChat, outcome, code)))
            : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.OpenAiChat);
        failure.Diagnostic.Should().Contain(outcome).And.Contain(code);
    }

    [Fact]
    public async Task RunAsync_BlobNotInUseByDesign_Passes()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? Json(KeylessBody((KeylessProofContract.Services.BlobStorage, KeylessProofContract.Outcomes.NotInUse, "store-disabled:SessionFileStore:BlobEndpoint")))
            : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Success>()
            .Which.ChecksPassed.Should().Contain(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.BlobStorage + "-not-in-use");
    }

    [Fact]
    public async Task RunAsync_NotInUseForAServiceEveryStampUses_Fails()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? Json(KeylessBody((KeylessProofContract.Services.OpenAiChat, KeylessProofContract.Outcomes.NotInUse, "store-disabled:x")))
            : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Failure>()
            .Which.ChecksFailed.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.OpenAiChat);
    }

    [Fact]
    public async Task RunAsync_AServiceMissingFromTheAnswer_Fails()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? Json(KeylessBody(omit: KeylessProofContract.Services.Redis))
            : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Failure>()
            .Which.ChecksFailed.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.Redis);
    }

    [Fact]
    public async Task RunAsync_ADuplicateEntry_CannotHideARefusal()
    {
        var body = KeylessBody((KeylessProofContract.Services.Cosmos, KeylessProofContract.Outcomes.Refused, "http-403"))
            .Replace("\"services\":[", "\"services\":[{\"service\":\"cosmos\",\"outcome\":\"proved\",\"code\":\"ok\"},", StringComparison.Ordinal);
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req) ? Json(body) : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Failure>()
            .Which.ChecksFailed.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.Cosmos);
    }

    [Fact]
    public async Task RunAsync_AServiceUnreachable_IsInconclusive_NotAFailure()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? Json(KeylessBody((KeylessProofContract.Services.Redis, KeylessProofContract.Outcomes.Unreachable, "redis-connection")))
            : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Inconclusive>()
            .Which.ChecksInconclusive.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.Redis);
    }

    [Fact]
    public async Task RunAsync_AFailureWinsOverAnInconclusive()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? Json(KeylessBody(
                (KeylessProofContract.Services.Redis, KeylessProofContract.Outcomes.Unreachable, "timeout"),
                (KeylessProofContract.Services.Cosmos, KeylessProofContract.Outcomes.Refused, "http-403")))
            : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Failure>()
            .Which.ChecksFailed.Should().Equal(E2EValidationRunner.KeylessProofCheckPrefix + KeylessProofContract.Services.Cosmos);
    }

    [Fact]
    public async Task RunAsync_KeylessProofTransient503ThenOk_RetriesOnceAndPasses()
    {
        var calls = 0;
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (IsKeylessProof(req))
            {
                return ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(KeylessBody());
            }
            return HappyResponder(req);
        });

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Success>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_KeylessProofTransientTwice_IsInconclusive()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
            IsKeylessProof(req) ? new HttpResponseMessage(HttpStatusCode.BadGateway) : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Inconclusive>()
            .Which.ChecksInconclusive.Should().Equal(E2EValidationRunner.CheckKeylessProof);
    }

    [Fact]
    public async Task RunAsync_KeylessProofTimesOut_IsInconclusive()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req)
            ? throw new TaskCanceledException("simulated HttpClient timeout")
            : HappyResponder(req));
        var runner = BuildRunner(handler);

        var result = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Inconclusive>()
            .Which.Diagnostic.Should().Contain("KeylessProofTimeout");
    }

    [Fact]
    public async Task RunAsync_KeylessProofUnparseableAnswer_IsAFailure()
    {
        var handler = new FakeBffHttpMessageHandler(req => IsKeylessProof(req) ? Json("<html>gateway</html>") : HappyResponder(req));

        var result = await BuildRunner(handler).RunAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<E2EValidationOutcome.Failure>().Which.ChecksFailed.Should().Equal(E2EValidationRunner.CheckKeylessProof);
    }

    // -----------------------------------------------------------------------
    // Parameter-guard failures -- surfaced as Failure so H13 classifies
    // QuarantineRequired (silent-fail defense: never throw on a bad URL).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_BlankBffApiUrl_ReturnsFailureCitingAllBffChecks()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when BffApiUrl is blank"));
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(bffApiUrl: ""), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().BeEquivalentTo(AllCheckNames);
        failure.Diagnostic.Should().Contain("BffApiUrl parameter is empty");
    }

    [Fact]
    public async Task RunAsync_MalformedBffApiUrl_ReturnsFailure()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when BffApiUrl is malformed"));
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(bffApiUrl: "not a url"), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("not a valid http(s) absolute URL");
    }

    [Fact]
    public async Task RunAsync_NonHttpBffApiUrl_ReturnsFailure()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised for non-http schemes"));
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(bffApiUrl: "ftp://example.com"), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("not a valid http(s) absolute URL");
    }

    [Fact]
    public async Task RunAsync_BlankDataverseUrl_ReturnsFailureCitingCorsOnly()
    {
        // Health + Ping + the keyless proof still run against the BFF;
        // CORS is Failed because we won't send an ambiguous Origin header.
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                throw new InvalidOperationException("CORS probe must NOT run when DataverseUrl is blank");
            }
            return HappyResponder(req);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(dataverseUrl: ""), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().ContainSingle().Which.Should().Be(E2EValidationRunner.CheckCorsDataverseOrigin);
        failure.Diagnostic.Should().Contain("DataverseUrl");
        failure.Diagnostic.Should().Contain("defense-in-depth");
    }

    // -----------------------------------------------------------------------
    // Cancellation semantics.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_CancellationBeforeHttp_Throws()
    {
        var handler = new FakeBffHttpMessageHandler(HappyResponder);
        var runner = BuildRunner(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await runner.RunAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // IsAllowOriginAcceptable (internal helper) -- direct table-driven
    // coverage of the .ps1's `-eq $DataverseUrl -or -eq '*'` predicate.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null, "https://acme.crm.dynamics.com", false)]
    [InlineData("", "https://acme.crm.dynamics.com", false)]
    [InlineData("*", "https://acme.crm.dynamics.com", true)]
    [InlineData("https://acme.crm.dynamics.com", "https://acme.crm.dynamics.com", true)]
    [InlineData("https://foreign.example.com", "https://acme.crm.dynamics.com", false)]
    [InlineData("HTTPS://acme.crm.dynamics.com", "https://acme.crm.dynamics.com", false)]
    public void IsAllowOriginAcceptable_TableDriven(string? observed, string dataverseOrigin, bool expected)
    {
        E2EValidationRunner.IsAllowOriginAcceptable(observed, dataverseOrigin).Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    // BuildInterimSkippedList (internal helper) -- freezes the exact 3-name
    // set so a future graduation MUST update the test in tandem. G-8 Batch 11
    // shrank this from 7 (four sample rows graduated to real checks).
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildInterimSkippedList_ContainsThreePinnedNames()
    {
        E2EValidationRunner.BuildInterimSkippedList().Should().BeEquivalentTo(new[]
        {
            E2EValidationRunner.SkippedDataverseEnvVarsPresent,
            E2EValidationRunner.SkippedDataverseEnvVarsDevLeakage,
            E2EValidationRunner.SkippedNamingConformance,
        });
    }

    [Fact]
    public void AllBffDependentCheckNames_AreTheBffChecksAndOnePerKeylessService()
    {
        E2EValidationRunner.AllBffDependentCheckNames().Should().BeEquivalentTo(AllCheckNames);
    }

    // -----------------------------------------------------------------------
    // AC-1 + AC-3 forcing functions -- source-file scans (parity with task 182's
    // NamingConformanceCheckerTests source scan; that test was deleted by task 230a).
    // -----------------------------------------------------------------------

    [Fact]
    public void SourceFile_ContainsNoProcessStartInfoOrShellOutInCode()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var runnerPath = Path.Combine(
            repoRoot,
            "src", "server", "services",
            "Sprk.Provisioning.ControlPlane.Core", "Handlers", "E2EAcceptance",
            "E2EValidationRunner.cs");

        File.Exists(runnerPath).Should().BeTrue(
            $"E2EValidationRunner.cs must exist at '{runnerPath}'");

        var contents = File.ReadAllText(runnerPath);
        var codeOnly = StripComments(contents);

        codeOnly.Should().NotContain("ProcessStartInfo",
            "task 181 acceptance: pure-C# port MUST have zero ProcessStartInfo references in code");
        codeOnly.Should().NotContain("System.Diagnostics.Process",
            "task 181 acceptance: pure-C# port MUST NOT reference System.Diagnostics.Process in code");
        codeOnly.Should().NotContain("Process.Start",
            "task 181 acceptance: pure-C# port MUST NOT call Process.Start in code");
        codeOnly.Should().NotContain("pac auth",
            "task 181 acceptance: pure-C# port MUST NOT reference pac auth session creation");
        codeOnly.Should().NotContain("Invoke-RestMethod",
            "task 181 acceptance: pure-C# port MUST NOT reference the PowerShell Invoke-RestMethod cmdlet");
        codeOnly.Should().NotContain("az rest",
            "task 181 acceptance: pure-C# port MUST NOT shell out to az rest");
    }

    [Fact]
    public void SourceFile_UsesNamedHttpClientFactoryConventionOfSiblingProbes()
    {
        // AC-3: proves the runner REUSES the sibling probes' named-client
        // HttpClient idiom (task 173/176 established) rather than authoring a
        // parallel HTTP-probing helper class -- POML constraint 1 (DS-4 §5 /
        // DS-1b §1 convergence bonus).
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var runnerPath = Path.Combine(
            repoRoot,
            "src", "server", "services",
            "Sprk.Provisioning.ControlPlane.Core", "Handlers", "E2EAcceptance",
            "E2EValidationRunner.cs");

        var contents = File.ReadAllText(runnerPath);
        contents.Should().Contain("IHttpClientFactory",
            "task 181 AC-3: runner MUST reuse the sibling probes' IHttpClientFactory convention");
        contents.Should().Contain("HttpClientName",
            "task 181 AC-3: runner MUST expose a named-HttpClient constant matching the sibling I2/I4 convention");
        contents.Should().Contain("_httpClientFactory.CreateClient(HttpClientName)",
            "task 181 AC-3: runner MUST pull its client via the named-client factory call, not construct HttpClient directly");
    }

    [Fact]
    public void RegistrationModule_RegistersE2EValidationRunnerAsIE2EValidationRunner()
    {
        // Forcing function: swap-in didn't drop the interface registration.
        // Deliberately a source-file scan (not a live DI resolve) to stay
        // aligned with .claude/constraints/testing.md MUST NOT B3 (no
        // DI-registration tests via container introspection).
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var modulePath = Path.Combine(
            repoRoot,
            "src", "server", "services",
            "Sprk.Provisioning.ControlPlane.Core", "Handlers", "E2EAcceptance",
            "E2EAcceptanceModule.cs");

        var contents = File.ReadAllText(modulePath);
        contents.Should().Contain("AddSingleton<IE2EValidationRunner, E2EValidationRunner>()",
            "task 181 acceptance: module MUST register E2EValidationRunner as IE2EValidationRunner");
        contents.Should().NotContain(
            "AddSingleton<IE2EValidationRunner, ValidateDeployedEnvironmentScriptRunner>()",
            "task 181 acceptance: retired script-runner registration MUST be removed from the composition");
        contents.Should().Contain("AddHttpClient(E2EValidationRunner.HttpClientName)",
            "task 181 acceptance: named HttpClient for the runner MUST be registered in the same module (parity with sibling I2/I4)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static E2EValidationRunner BuildRunner(
        FakeBffHttpMessageHandler? handler = null,
        TokenCredential? credential = null)
    {
        handler ??= new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("default handler must not be exercised"));
        var runner = new E2EValidationRunner(
            new FakeHttpClientFactory(handler),
            credential ?? new FakeTokenCredential(),
            Options.Create(new H13AcceptanceOptions
            {
                KeylessProofTimeout = TimeSpan.FromSeconds(30),
            }),
            NullLogger<E2EValidationRunner>.Instance);
        runner.TransientRetryDelay = TimeSpan.Zero; // no real sleeping in unit tests
        return runner;
    }

    private static E2EValidationRequest BuildRequest(
        string bffApiUrl = BffApiUrl,
        string dataverseUrl = DataverseUrl,
        string bffAppRegId = BffAppRegId)
        => new(
            CustomerId: CustomerId,
            RunId: RunId,
            DataverseUrl: dataverseUrl,
            BffApiUrl: bffApiUrl,
            TargetSlotName: TargetSlot,
            BffAppRegId: bffAppRegId);

    private static bool IsKeylessProof(HttpRequestMessage req) => PathIs(req, KeylessProofContract.Route);

    /// <summary>
    /// The BFF's keyless-proof answer: every service proved, except the given overrides; <paramref name="omit"/> drops a
    /// service from the answer.
    /// </summary>
    private static string KeylessBody(params (string Service, string Outcome, string Code)[] overrides)
        => KeylessBody(null, overrides);

    private static string KeylessBody(string? omit, params (string Service, string Outcome, string Code)[] overrides)
    {
        var entries = KeylessProofContract.Services.All
            .Where(svc => svc != omit)
            .Select(svc =>
            {
                var o = overrides.FirstOrDefault(x => x.Service == svc);
                var outcome = o.Service is null ? KeylessProofContract.Outcomes.Proved : o.Outcome;
                var code = o.Service is null ? "ok" : o.Code;
                return $"{{\"service\":\"{svc}\",\"outcome\":\"{outcome}\",\"statusCode\":200,\"elapsedMs\":5,\"code\":\"{code}\"}}";
            });
        return "{\"services\":[" + string.Join(",", entries) + "]}";
    }

    private static bool PathIs(HttpRequestMessage req, string path)
        => string.Equals(req.RequestUri?.AbsolutePath, path, StringComparison.Ordinal);

    /// <summary>
    /// Canned "fully deployed + seeded customer BFF" responder: every check's
    /// endpoint answers with a valid happy-path payload. Individual tests
    /// override one path and delegate the rest here.
    /// </summary>
    private static HttpResponseMessage HappyResponder(HttpRequestMessage req)
    {
        if (req.Method == HttpMethod.Options)
        {
            return CorsPreflightResponse(HttpStatusCode.OK, DataverseUrl);
        }
        if (IsKeylessProof(req))
        {
            return Json(KeylessBody());
        }
        // /healthz + /ping (and any other anonymous probe target).
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok", Encoding.UTF8, "text/plain"),
        };
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage CorsPreflightResponse(HttpStatusCode status, string allowOrigin)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", allowOrigin);
        return response;
    }

    private static string FindRepoRoot(string startDir)
    {
        var cur = new DirectoryInfo(startDir);
        while (cur is not null)
        {
            var candidate = Path.Combine(cur.FullName, "src", "server", "services");
            if (Directory.Exists(candidate))
            {
                return cur.FullName;
            }
            cur = cur.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repo root from '{startDir}' -- expected a parent directory containing src/server/services.");
    }

    private static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') { i++; }
            }
            else if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) { i++; }
                i = Math.Min(i + 2, source.Length);
            }
            else
            {
                sb.Append(source[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    // ---- Fake collaborators (hand-rolled per ADR-038 §5 no Mock<T>) --------

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public const string TokenValue = "fake-keyless-proof-token";

        private readonly string _token;

        public FakeTokenCredential(string token = TokenValue) => _token = token;

        public List<string> RequestedScopes { get; } = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            RequestedScopes.AddRange(requestContext.Scopes);
            return new(_token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingTokenCredential : TokenCredential
    {
        private readonly Exception _toThrow;
        public ThrowingTokenCredential(Exception toThrow) { _toThrow = toThrow; }
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _toThrow;
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _toThrow;
    }

    /// <summary>
    /// Hand-rolled <see cref="HttpMessageHandler"/> -- records requested URLs,
    /// methods, and Authorization headers so tests can assert the runner
    /// issued real requests with the correct method + path + auth shape.
    /// Never Mock&lt;T&gt;.
    /// </summary>
    private sealed class FakeBffHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();
        public List<HttpMethod> RequestedMethods { get; } = new();
        public List<(string Url, HttpMethod Method, string? AuthorizationScheme, string? AuthorizationParameter)> Requests { get; } = new();

        public FakeBffHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            RequestedMethods.Add(request.Method);
            Requests.Add((
                request.RequestUri?.ToString() ?? string.Empty,
                request.Method,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(_responder(request));
        }
    }
}
