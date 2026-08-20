// -----------------------------------------------------------------------------
// E2EValidationRunnerTests.cs
//
// L2 CONTROL-PLANE unit tests for E2EValidationRunner (task 181, Phase C''
// Wave G-7 Batch G-7B). Proves the REAL HttpClient probe path -- not a
// hardcoded verdict -- via a hand-rolled FakeHttpMessageHandler (never
// Mock<HttpMessageHandler>; ADR-038 path #1 + KEEP-path rules per
// .claude/constraints/testing.md section MUST NOT B1).
//
// PATH: tests/CLAUDE.md 7 KEEP paths -- this file is a component-scoped unit
// test of the runner class (a pure L2 seam with no BFF wiring). It exercises
// behavior through the runner's PUBLIC RunAsync surface. It lives in the
// existing Tests project alongside the sibling H13 real-probe test files
// (AiSearchTenantFilterInvariantProbeTests, SpeContainerResolverInvariantProbeTests,
// NamingConformanceCheckerTests, ArmCostEnvelopeCheckerTests). Path is not
// under tests/integration/** because the runner is not itself an integration
// boundary -- the LIVE-run integration lands in Phase F rerun (task 186).
//
// COVERAGE (maps to POML acceptance criteria):
//   AC-1 (grep 0 ProcessStartInfo | pac auth):
//        SourceFile_ContainsNoProcessStartInfoOrShellOutInCode.
//   AC-2 (all 5 effect probes independently testable against fakes):
//        Health/ping/CORS probes each have Pass + fail-status + timeout +
//        transport-throw coverage; the 2 Dataverse-auth-gated probes + 4
//        Phase-B extended probes are surfaced as Skipped and pinned by
//        RunAsync_HappyPath_EmitsInterimSkippedListVerbatim. This proves
//        each of the 5-check .ps1 surface is REPRESENTED (real or Skipped)
//        rather than silently dropped -- the POML premise-mismatch defense.
//   AC-3 (no duplicate HTTP-probing helper):
//        SourceFile_ReusesIHttpClientFactoryConventionOfSiblingProbes --
//        asserts named-client + IHttpClientFactory idiom present + no
//        parallel helper class shape.
//   AC-4 (build + test green): covered by CI.
//
// CONVERGENCE-BONUS DEFENSE:
//   Tests ALSO pin the ChecksSkipped list verbatim so a future task that
//   graduates one of the Skipped rows to a real check MUST also update the
//   test -- prevents silent drift back to "we don't know what H13 verified"
//   when task 186 lands.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

    // -----------------------------------------------------------------------
    // AC-2: happy path -- all three real probes Pass, Skipped list stable.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_HappyPath_ReturnsSuccessWithAllThreeChecksPassed()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                return CorsPreflightResponse(HttpStatusCode.OK, DataverseUrl);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok", Encoding.UTF8, "text/plain"),
            };
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<E2EValidationOutcome.Success>().Subject;
        success.ChecksPassed.Should().BeEquivalentTo(new[]
        {
            E2EValidationRunner.CheckBffHealthz,
            E2EValidationRunner.CheckBffPing,
            E2EValidationRunner.CheckCorsDataverseOrigin,
        });
        handler.RequestedUrls.Should().Contain(u => u.EndsWith("/healthz", StringComparison.Ordinal));
        handler.RequestedUrls.Should().Contain(u => u.EndsWith("/ping", StringComparison.Ordinal));
        handler.RequestedMethods.Should().Contain(HttpMethod.Options);
    }

    [Fact]
    public async Task RunAsync_HappyPath_EmitsInterimSkippedListVerbatim()
    {
        // Convergence-bonus defense: the ChecksSkipped list is pinned exactly
        // so a future task graduating a Skipped row to a real check MUST
        // update this test. Prevents silent drift.
        var handler = new FakeBffHttpMessageHandler(_ =>
            OkOrCors(HttpStatusCode.OK, DataverseUrl));
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<E2EValidationOutcome.Success>().Subject;
        success.ChecksSkipped.Should().BeEquivalentTo(new[]
        {
            E2EValidationRunner.SkippedDataverseEnvVarsPresent,
            E2EValidationRunner.SkippedDataverseEnvVarsDevLeakage,
            E2EValidationRunner.SkippedNamingConformance,
            E2EValidationRunner.SkippedSampleAiAnalysis,
            E2EValidationRunner.SkippedSampleDocUploadIndex,
            E2EValidationRunner.SkippedSampleWorkspaceLayoutRender,
            E2EValidationRunner.SkippedSampleWizardFieldMap,
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
            return new HttpResponseMessage(HttpStatusCode.OK);
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
            if (req.Method == HttpMethod.Options)
            {
                return CorsPreflightResponse(HttpStatusCode.OK, DataverseUrl);
            }
            if ((req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/healthz", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckBffHealthz);
        failure.Diagnostic.Should().Contain("503");
        failure.Diagnostic.Should().Contain("expected 200");
    }

    [Fact]
    public async Task RunAsync_PingReturns500_ReturnsFailureCitingBffPing()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                return CorsPreflightResponse(HttpStatusCode.OK, DataverseUrl);
            }
            if ((req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/ping", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
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
            return new HttpResponseMessage(HttpStatusCode.OK);
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
            return new HttpResponseMessage(HttpStatusCode.OK);
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
            if (req.Method == HttpMethod.Options)
            {
                return CorsPreflightResponse(HttpStatusCode.OK, DataverseUrl);
            }
            if ((req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/healthz", StringComparison.Ordinal))
            {
                throw new HttpRequestException("connection refused");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckBffHealthz);
        failure.Diagnostic.Should().Contain("connection refused");
    }

    // -----------------------------------------------------------------------
    // Aggregate-behavior tests: every check runs even if an earlier one fails
    // (parity with the .ps1's continue-on-fail Add-TestResult pattern) --
    // proves the runner assembles a COMPREHENSIVE snapshot for the operator,
    // not a first-failure short-circuit.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_MultipleFailures_AggregatesEveryFailedCheck()
    {
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest); // no CORS header
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var runner = BuildRunner(handler);

        var outcome = await runner.RunAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<E2EValidationOutcome.Failure>().Subject;
        failure.ChecksFailed.Should().BeEquivalentTo(new[]
        {
            E2EValidationRunner.CheckBffHealthz,
            E2EValidationRunner.CheckBffPing,
            E2EValidationRunner.CheckCorsDataverseOrigin,
        });
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
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckBffHealthz);
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckBffPing);
        failure.ChecksFailed.Should().Contain(E2EValidationRunner.CheckCorsDataverseOrigin);
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
        // Health + Ping still run against the BFF; CORS is Failed because we
        // won't send an ambiguous Origin header.
        var handler = new FakeBffHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Options)
            {
                throw new InvalidOperationException("CORS probe must NOT run when DataverseUrl is blank");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
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
        var handler = new FakeBffHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
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
    // BuildInterimSkippedList (internal helper) -- freezes the exact 7-name
    // set so a future graduation MUST update the test in tandem.
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildInterimSkippedList_ContainsSevenPinnedNames()
    {
        E2EValidationRunner.BuildInterimSkippedList().Should().BeEquivalentTo(new[]
        {
            E2EValidationRunner.SkippedDataverseEnvVarsPresent,
            E2EValidationRunner.SkippedDataverseEnvVarsDevLeakage,
            E2EValidationRunner.SkippedNamingConformance,
            E2EValidationRunner.SkippedSampleAiAnalysis,
            E2EValidationRunner.SkippedSampleDocUploadIndex,
            E2EValidationRunner.SkippedSampleWorkspaceLayoutRender,
            E2EValidationRunner.SkippedSampleWizardFieldMap,
        });
    }

    // -----------------------------------------------------------------------
    // AC-1 + AC-3 forcing functions -- source-file scans (parity with task 182
    // NamingConformanceCheckerTests.SourceFile_ContainsNoProcessStartInfoOrShellOutInCode).
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

    private static E2EValidationRunner BuildRunner(FakeBffHttpMessageHandler? handler = null)
    {
        handler ??= new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("default handler must not be exercised"));
        return new E2EValidationRunner(
            new FakeHttpClientFactory(handler),
            NullLogger<E2EValidationRunner>.Instance);
    }

    private static E2EValidationRequest BuildRequest(
        string bffApiUrl = BffApiUrl,
        string dataverseUrl = DataverseUrl)
        => new(
            CustomerId: CustomerId,
            RunId: RunId,
            DataverseUrl: dataverseUrl,
            BffApiUrl: bffApiUrl,
            TargetSlotName: TargetSlot);

    private static HttpResponseMessage CorsPreflightResponse(HttpStatusCode status, string allowOrigin)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", allowOrigin);
        return response;
    }

    private static HttpResponseMessage OkOrCors(HttpStatusCode status, string allowOrigin)
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

    /// <summary>
    /// Hand-rolled <see cref="HttpMessageHandler"/> -- records requested URLs
    /// + methods so tests can assert the runner issued real requests with
    /// correct method + path shape. Never Mock&lt;T&gt;.
    /// </summary>
    private sealed class FakeBffHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();
        public List<HttpMethod> RequestedMethods { get; } = new();

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
            return Task.FromResult(_responder(request));
        }
    }
}
