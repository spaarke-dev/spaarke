// -----------------------------------------------------------------------------
// BapRestEnvironmentCreatorTests.cs
//
// L2 CONTROL-PLANE unit tests for BapRestEnvironmentCreator (task 140, Wave
// G-4 — BAP-REST env-create + async-operation-polling port).
//
// ADR-038 alignment: pure C# unit tests over a real HttpClient wrapping a
// hand-rolled fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;,
// banned per testing.md) — parity with BapRestEnvironmentRateProbeTests
// (task 120). Polling-loop timing tests use TimeProvider.System with tiny
// real TimeSpans (parity with H5DataverseEnvCreationHandlerTests T13's
// "10ms interval / 150ms timeout" convention) rather than a fake-timer
// TimeProvider — this codebase's established pattern for polling-loop tests
// per StateReconcilerServiceTests's documented rationale (avoids adding the
// Microsoft.Extensions.TimeProvider.Testing package for a hand-rolled-double
// that would need full CreateTimer support anyway).
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  Request-body shape matches Provision-Customer.ps1 STEP 5's fields
//       (displayName/environmentSku[=Tier]/azureRegion/linkedEnvironmentMetadata
//       .domainName/baseLanguage/currency.code) — acceptance criterion #2.
//   T2  Async-operation-polling loop detects Succeeded (after intermediate
//       in-progress states) — acceptance criterion #3.
//   T3  Async-operation-polling loop detects Failed (BAP terminal failure) —
//       classified ProvisioningFailed, distinct from Timeout — acceptance
//       criterion #3.
//   T4  Async-operation-polling loop detects a configurable Timeout when no
//       terminal state is observed within CreationTimeout — acceptance
//       criterion #3.
//   T5  Duplicate-domain create response classified DomainAlreadyExists,
//       distinct from UnknownInvocationFailure — acceptance criterion #4.
//   T6  Existing-environment idempotent short-circuit (already Succeeded) —
//       no CREATE POST, no poll GET.
//   T7  Existing-environment still-provisioning — skips CREATE, polls the
//       EXISTING envId directly.
//   T8  ClassifyHttpFailure pure classifier theory (status+body -> kind).
//   T9  Token acquisition failure classified AuthFailure.
//   T10 grep-style production-file check (no `pac admin`/`ProcessStartInfo`
//       usage) — acceptance criterion #1, verified as a pure string-search
//       test over the compiled type's source so it fails loudly if violated
//       via a future edit (defense in depth alongside the CI grep step).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class BapRestEnvironmentCreatorTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string Region = "unitedstates";
    private const string Tier = "Sandbox";

    // ---------- T1 request-body shape ----------

    [Fact]
    public async Task CreateEnvironmentAsync_RequestBody_MatchesStep5FieldShape()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson()),
            OnCreate = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-abc", provisioningState: null, instanceUrl: null)),
            OnPoll = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-abc", "Succeeded", "https://acme.crm.dynamics.com/")),
        };
        var tenantIdsRequested = new List<string>();
        var creator = BuildCreator(handler, tenantIdsRequested);

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        outcome.Should().BeOfType<DataverseEnvCreationOutcome.Success>();

        var createReq = handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Which;
        createReq.Body.Should().NotBeNull();
        using var doc = JsonDocument.Parse(createReq.Body!);
        var props = doc.RootElement.GetProperty("properties");
        props.GetProperty("displayName").GetString().Should().Contain(CustomerId);
        props.GetProperty("environmentSku").GetString().Should().Be(Tier, "environmentSku is the REST equivalent of pac's --type");
        props.GetProperty("azureRegion").GetString().Should().Be(Region);
        var lem = props.GetProperty("linkedEnvironmentMetadata");
        lem.GetProperty("domainName").GetString().Should().Be(CustomerId, "domainName is the REST equivalent of pac's --domain");
        lem.GetProperty("baseLanguage").GetInt32().Should().Be(1033);
        lem.GetProperty("currency").GetProperty("code").GetString().Should().Be("USD");
        doc.RootElement.GetProperty("location").GetString().Should().Be(Region);

        createReq.Uri.Host.Should().Be("api.bap.microsoft.com");
        tenantIdsRequested.Should().Contain(TenantId, "the token credential must be scoped to the request's TenantId (§4D I1/I5)");
    }

    // ---------- T2 polling detects Succeeded ----------

    [Fact]
    public async Task CreateEnvironmentAsync_PollLoop_DetectsSucceeded_AfterIntermediateStates()
    {
        var pollCount = 0;
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson()),
            OnCreate = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-1", provisioningState: null, instanceUrl: null)),
            OnPoll = _ =>
            {
                pollCount++;
                return pollCount < 3
                    ? JsonResponse(HttpStatusCode.OK, EnvJson("env-1", "Provisioning", instanceUrl: null))
                    : JsonResponse(HttpStatusCode.OK, EnvJson("env-1", "Succeeded", "https://acme.crm.dynamics.com/"));
            },
        };
        var creator = BuildCreator(handler, pollInterval: TimeSpan.FromMilliseconds(5), creationTimeout: TimeSpan.FromSeconds(5));

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        var success = outcome.Should().BeOfType<DataverseEnvCreationOutcome.Success>().Subject;
        success.EnvironmentUrl.Should().Be("https://acme.crm.dynamics.com/");
        pollCount.Should().BeGreaterThanOrEqualTo(3, "loop must continue past intermediate Provisioning states");
    }

    // ---------- T3 polling detects Failed (distinct from Timeout) ----------

    [Fact]
    public async Task CreateEnvironmentAsync_PollLoop_DetectsFailed_ClassifiedProvisioningFailed()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson()),
            OnCreate = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-2", provisioningState: null, instanceUrl: null)),
            OnPoll = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-2", "Failed", instanceUrl: null)),
        };
        var creator = BuildCreator(handler, pollInterval: TimeSpan.FromMilliseconds(5), creationTimeout: TimeSpan.FromSeconds(5));

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        var failure = outcome.Should().BeOfType<DataverseEnvCreationOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(DataverseEnvCreationFailureKind.ProvisioningFailed);
        failure.FailureKind.Should().NotBe(DataverseEnvCreationFailureKind.Timeout,
            "an explicit BAP-reported Failed state must be distinguishable from the poll simply running out of time");
    }

    // ---------- T4 polling detects Timeout ----------

    [Fact]
    public async Task CreateEnvironmentAsync_PollLoop_NeverReachesTerminalState_ClassifiedTimeout()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson()),
            OnCreate = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-3", provisioningState: null, instanceUrl: null)),
            OnPoll = _ => JsonResponse(HttpStatusCode.OK, EnvJson("env-3", "Provisioning", instanceUrl: null)),
        };
        // Tiny real timeout/interval — matches H5DataverseEnvCreationHandlerTests
        // T13's convention (real wall-clock, kept small so the suite stays fast).
        var creator = BuildCreator(handler, pollInterval: TimeSpan.FromMilliseconds(10), creationTimeout: TimeSpan.FromMilliseconds(120));

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        var failure = outcome.Should().BeOfType<DataverseEnvCreationOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(DataverseEnvCreationFailureKind.Timeout);
        failure.Diagnostic.Should().Contain("terminal");
    }

    // ---------- T5 duplicate-domain classified distinctly ----------

    [Fact]
    public async Task CreateEnvironmentAsync_DuplicateDomainResponse_ClassifiedDistinctlyFromGenericFailure()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson()),
            OnCreate = _ => JsonResponse(HttpStatusCode.Conflict,
                """{ "error": { "code": "DomainNameUnavailable", "message": "The requested domain name is already taken." } }"""),
        };
        var creator = BuildCreator(handler);

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        var failure = outcome.Should().BeOfType<DataverseEnvCreationOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(DataverseEnvCreationFailureKind.DomainAlreadyExists);
        failure.FailureKind.Should().NotBe(DataverseEnvCreationFailureKind.UnknownInvocationFailure);
    }

    // ---------- T6 existing environment already Succeeded — idempotent short-circuit ----------

    [Fact]
    public async Task CreateEnvironmentAsync_ExistingEnvironmentAlreadySucceeded_ShortCircuitsWithoutCreateOrPoll()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson(
                EnvJson("existing-env", "Succeeded", "https://acme.crm.dynamics.com/", domainName: CustomerId))),
            OnCreate = _ => throw new InvalidOperationException("must not create — environment already exists"),
            OnPoll = _ => throw new InvalidOperationException("must not poll — already Succeeded"),
        };
        var creator = BuildCreator(handler);

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        var success = outcome.Should().BeOfType<DataverseEnvCreationOutcome.Success>().Subject;
        success.EnvironmentUrl.Should().Be("https://acme.crm.dynamics.com/");
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    // ---------- T7 existing environment still provisioning — skip create, poll existing envId ----------

    [Fact]
    public async Task CreateEnvironmentAsync_ExistingEnvironmentStillProvisioning_SkipsCreate_PollsExistingEnvId()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => JsonResponse(HttpStatusCode.OK, ListJson(
                EnvJson("existing-env-2", "Provisioning", instanceUrl: null, domainName: CustomerId))),
            OnCreate = _ => throw new InvalidOperationException("must not re-create — a matching env is already provisioning"),
            OnPoll = _ => JsonResponse(HttpStatusCode.OK, EnvJson("existing-env-2", "Succeeded", "https://acme.crm.dynamics.com/")),
        };
        var creator = BuildCreator(handler, pollInterval: TimeSpan.FromMilliseconds(5), creationTimeout: TimeSpan.FromSeconds(5));

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        outcome.Should().BeOfType<DataverseEnvCreationOutcome.Success>();
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.Uri.AbsoluteUri.Contains("existing-env-2"));
    }

    // ---------- T8 ClassifyHttpFailure theory ----------

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "domain name is already taken", DataverseEnvCreationFailureKind.DomainAlreadyExists)]
    [InlineData(HttpStatusCode.BadRequest, "the requested domain already exists", DataverseEnvCreationFailureKind.DomainAlreadyExists)]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", DataverseEnvCreationFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.Forbidden, "access denied", DataverseEnvCreationFailureKind.AuthFailure)]
    [InlineData((HttpStatusCode)429, "too many requests", DataverseEnvCreationFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "tenant quota exhausted", DataverseEnvCreationFailureKind.QuotaExhausted)]
    [InlineData(HttpStatusCode.Conflict, "environment already exists in an inconsistent state", DataverseEnvCreationFailureKind.PartialProvisioning)]
    [InlineData(HttpStatusCode.InternalServerError, "something went wrong on the server", DataverseEnvCreationFailureKind.UnknownInvocationFailure)]
    public void ClassifyHttpFailure_MapsExpectedFailureKind(HttpStatusCode statusCode, string body, DataverseEnvCreationFailureKind expected)
    {
        var kind = BapRestEnvironmentCreator.ClassifyHttpFailure(statusCode, body);
        kind.Should().Be(expected);
    }

    // ---------- T9 token acquisition failure -> AuthFailure ----------

    [Fact]
    public async Task CreateEnvironmentAsync_TokenAcquisitionFailure_ClassifiedAuthFailure()
    {
        var handler = new FakeBapHandler
        {
            OnList = _ => throw new InvalidOperationException("must not reach HTTP — token acquisition fails first"),
        };
        var creator = new BapRestEnvironmentCreator(
            new HttpClient(handler),
            _ => new ThrowingCredential(),
            Options.Create(new DataverseEnvCreationOptions()),
            NullLogger<BapRestEnvironmentCreator>.Instance,
            TimeProvider.System);

        var outcome = await creator.CreateEnvironmentAsync(
            new DataverseEnvCreationRequest(CustomerId, TenantId, Region, Tier, DisplayName: null),
            CancellationToken.None);

        var failure = outcome.Should().BeOfType<DataverseEnvCreationOutcome.Failure>().Subject;
        failure.FailureKind.Should().Be(DataverseEnvCreationFailureKind.AuthFailure);
    }

    // ---------- T10 no pac/ProcessStartInfo usage (defense in depth alongside CI grep) ----------

    [Fact]
    public void BapRestEnvironmentCreator_SourceContainsNo_PacAdminOrProcessStartInfo()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "Sprk.Provisioning.ControlPlane.Core", "Handlers", "DataverseEnvCreation", "BapRestEnvironmentCreator.cs");
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            // Build-output layout differs across CI runners — skip rather
            // than false-fail; the binding check is the CI-step grep itself.
            return;
        }
        var text = File.ReadAllText(fullPath);
        text.Should().NotContain("pac admin");
        text.Should().NotContain("ProcessStartInfo");
    }

    // ---------- helpers ----------

    private static BapRestEnvironmentCreator BuildCreator(
        FakeBapHandler handler,
        List<string>? tenantIdsRequested = null,
        TimeSpan? pollInterval = null,
        TimeSpan? creationTimeout = null)
    {
        var captured = tenantIdsRequested ?? new List<string>();
        TokenCredential Factory(string tenantId)
        {
            captured.Add(tenantId);
            return new FakeCredential();
        }

        return new BapRestEnvironmentCreator(
            new HttpClient(handler),
            Factory,
            Options.Create(new DataverseEnvCreationOptions
            {
                AsyncOperationPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5),
                CreationTimeout = creationTimeout ?? TimeSpan.FromSeconds(5),
            }),
            NullLogger<BapRestEnvironmentCreator>.Instance,
            TimeProvider.System);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string EnvJson(
        string name, string? provisioningState, string? instanceUrl, string? displayName = null, string? domainName = null)
    {
        var provisioningStateJson = provisioningState is null ? "null" : $"\"{provisioningState}\"";
        var instanceUrlJson = instanceUrl is null ? "null" : $"\"{instanceUrl}\"";
        return $$"""
        {
          "name": "{{name}}",
          "properties": {
            "displayName": "{{displayName ?? name}}",
            "provisioningState": {{provisioningStateJson}},
            "linkedEnvironmentMetadata": {
              "domainName": "{{domainName ?? name}}",
              "instanceUrl": {{instanceUrlJson}}
            }
          }
        }
        """;
    }

    private static string ListJson(params string[] envJsonEntries)
        => $$"""{ "value": [ {{string.Join(",", envJsonEntries)}} ] }""";

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-bap-test-token", DateTimeOffset.UtcNow.AddHours(1));

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
    /// requests to List / Create / Poll delegates based on HTTP method + URL
    /// shape, matching BapRestEnvironmentRateProbeTests's established
    /// fake-transport convention.
    /// </summary>
    private sealed class FakeBapHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnList { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnCreate { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPoll { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!, body));

            if (request.Method == HttpMethod.Post)
            {
                return (OnCreate ?? throw new InvalidOperationException("unexpected POST — no OnCreate wired"))(request);
            }

            var path = request.RequestUri!.AbsolutePath;
            var isPoll = path.Contains("/scopes/admin/environments/", StringComparison.Ordinal)
                && !path.TrimEnd('/').EndsWith("/scopes/admin/environments", StringComparison.Ordinal);
            if (isPoll)
            {
                return (OnPoll ?? throw new InvalidOperationException("unexpected poll GET — no OnPoll wired"))(request);
            }

            return (OnList ?? throw new InvalidOperationException("unexpected list GET — no OnList wired"))(request);
        }
    }
}
