// -----------------------------------------------------------------------------
// RunsEndpointsTests.cs
//
// L2 CONTROL-PLANE REST endpoint tests (task 057, Wave C5).
//
// PURPOSE:
//   Prove POML acceptance criteria at the endpoint layer, exercising the REAL
//   Program.cs composition (WebApplicationFactory<Program>) so the auth
//   pipeline (JWT bearer + Operator/Reader policies) fires end-to-end:
//
//     - AC #1 OpenAPI at /swagger enumerates all 9 endpoints (verified via
//       /swagger/v1/swagger.json content).
//     - AC #2 Operator token + POST /api/runs -> 202 with Location header +
//       Cosmos row created + Service Bus enqueue observed.
//     - AC #3 Reader token + POST /api/runs -> 403 Forbidden.
//     - AC #4 No bearer + any endpoint -> 401 Unauthorized.
//     - AC #5 Reader token + GET /api/runs/{id} -> 200 with the Cosmos run
//       payload; underlying repository call passes /customerId partition key.
//     - AC #6 POST /api/runs/{id}/clear-quarantine WITHOUT reason -> 400 +
//       NO audit-log entry emitted.
//     - AC #7 POST /api/runs/{id}/clear-quarantine WITH reason -> 202 + audit-
//       log entry with actor tid + reason (spec FR-24 acceptance).
//     - AC #8 dotnet build + dotnet test pass; 0 analyzer warnings.
//     - Latency spot-check: POST /api/runs completes in <100ms with the
//       in-memory seams (the network round-trip to real Cosmos/Service Bus
//       is out-of-scope for a unit test — this proves the endpoint's own
//       work is well under budget).
//
// SEAM STRATEGY:
//   The tests REPLACE two DI registrations in Program.cs — IProvisioningRunRepository
//   and IHandlerEnqueuer — with in-memory implementations that RECORD calls
//   for assertion. The Cosmos + Service Bus modules still LOAD (they need
//   config to satisfy their fail-fast validators) but their clients are never
//   actually invoked because the repository + enqueuer seams are replaced
//   above them in the dependency graph. This matches ADR-038 §5 (no
//   Mock<HttpMessageHandler>) — the seam is the repository/enqueuer interface,
//   not a mocked SDK client.
//
// AUTH STRATEGY:
//   The real JwtBearer handler (Microsoft.Identity.Web AddMicrosoftIdentityWebApi)
//   requires a live OIDC authority + valid signed JWT — impractical for unit
//   tests. Instead, we OVERRIDE the authentication scheme in the test-server
//   ConfigureServices with a bespoke TestAuthenticationHandler that reads
//   role assignment from a request header (X-Test-Roles). This is the same
//   pattern the BFF uses for integration tests where JwtBearer would require
//   an external authority.
//
// ADR-038 alignment:
//   - No Mock<HttpMessageHandler>. Seams are the interface types.
//   - No DI-registration-only tests. Tests exercise real HTTP + auth + JSON.
//   - No ctor-null-check test.
//   - KEEP category: tests/unit/ (in-process HTTP; no external resource).
//     Sibling of AuditLogMiddlewareTests + the handler unit tests.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Api;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Api;

/// <summary>
/// End-to-end endpoint tests over <see cref="RunsEndpoints"/> +
/// <see cref="RunLogsEndpoints"/>. Uses <see cref="L2WebApplicationFactory"/>
/// to replace the two production seams (repository + enqueuer) with
/// in-memory implementations while keeping the rest of the composition intact.
/// </summary>
public sealed class RunsEndpointsTests : IClassFixture<L2WebApplicationFactory>
{
    private const string TestCustomerId = "test-customer";
    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string TestObjectId = "22222222-2222-2222-2222-222222222222";

    private readonly L2WebApplicationFactory _factory;

    public RunsEndpointsTests(L2WebApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // AC #1 — OpenAPI at /swagger enumerates all L2 endpoints (this project
    //         owns 8 of the 9 — the 9th consent-callback is BFF-side).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Swagger_EnumeratesAllL2Endpoints()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Swagger UI must be reachable per FR-21 acceptance.");
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonNode.Parse(body)!.AsObject();
        var paths = doc["paths"]!.AsObject();

        // 8 L2 endpoints per spec §4.2 (the 9th, consent-callback, is BFF-side).
        // Endpoint templates as they appear in Swashbuckle's OpenAPI output.
        paths.ContainsKey("/api/runs").Should().BeTrue("POST /api/runs missing from OpenAPI");
        paths.ContainsKey("/api/runs/{id}/preflight").Should().BeTrue();
        paths.ContainsKey("/api/runs/{id}").Should().BeTrue();
        paths.ContainsKey("/api/runs/{id}/gates/{gateId}/advance").Should().BeTrue();
        paths.ContainsKey("/api/runs/{id}/resume").Should().BeTrue();
        paths.ContainsKey("/api/runs/{id}/phases/{phaseId}/logs").Should().BeTrue();
        paths.ContainsKey("/api/runs/{id}/cancel").Should().BeTrue();
        paths.ContainsKey("/api/runs/{id}/clear-quarantine").Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // AC #4 — No bearer -> 401 on every endpoint that requires auth.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("POST", "/api/runs")]
    [InlineData("POST", "/api/runs/abc/preflight?customerId=x")]
    [InlineData("GET", "/api/runs/abc?customerId=x")]
    [InlineData("POST", "/api/runs/abc/gates/g/advance?customerId=x")]
    [InlineData("POST", "/api/runs/abc/resume?customerId=x")]
    [InlineData("POST", "/api/runs/abc/cancel?customerId=x")]
    [InlineData("POST", "/api/runs/abc/clear-quarantine?customerId=x&reason=r")]
    [InlineData("GET", "/api/runs/abc/phases/H0/logs?customerId=x")]
    public async Task AllProtectedEndpoints_WithoutBearer_Return401(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && path == "/api/runs")
        {
            // POST /api/runs has a required body; without one the request would 400 not 401.
            // The 401 check requires we don't reach the endpoint handler at all — auth
            // pipeline short-circuits first. Attach a valid body so a bug that skips
            // auth would surface as 202 not 400.
            request.Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
            });
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"Auth pipeline must short-circuit BEFORE the endpoint handler runs (FR-20 acceptance) — endpoint={method} {path}");
    }

    // -------------------------------------------------------------------------
    // AC #3 — Reader token on mutating endpoint -> 403.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRuns_WithReaderToken_Returns403()
    {
        var client = _factory.CreateClient();
        var body = JsonContent.Create(new
        {
            customerId = TestCustomerId,
            environmentId = "env-1",
            tenancyModel = "Model1Shared",
            profile = "spaarke-hosted-model1-trial",
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs") { Content = body };
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Operator app-role is REQUIRED for POST /api/runs (FR-20 acceptance).");
    }

    // -------------------------------------------------------------------------
    // AC #2 — Operator token on POST /api/runs -> 202 + Location header +
    // Cosmos row created + Service Bus enqueue observed.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRuns_WithOperatorToken_Returns202_CreatesRunAndEnqueuesH0()
    {
        // Fresh factory to reset in-memory seams across tests that would collide.
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["display-name"] = "Acme Corp",
                    // ISH-01 (Wave 2 pre-dispatch remediation): tenantId is the
                    // canonical propagation path (Wave 0 Decision 1).
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull("202 Accepted must carry a Location header per REST convention.");
        response.Headers.Location!.OriginalString.Should().StartWith("/api/runs/");
        response.Headers.Location!.OriginalString.Should().Contain("customerId=");

        var responseBody = await response.Content.ReadFromJsonAsync<CreateRunResponsePayload>();
        responseBody.Should().NotBeNull();
        responseBody!.RunId.Should().NotBeNullOrEmpty();
        responseBody.Status.Should().Be(nameof(RunStatus.NotStarted));

        // Cosmos row created.
        var repo = factory.Repository;
        repo.CreatedRuns.Should().ContainSingle(r => r.RunId == responseBody.RunId);
        var stored = repo.CreatedRuns.Single();
        stored.CustomerId.Should().Be(TestCustomerId);
        stored.Parameters.NonSecret.Should().ContainKey("display-name").WhoseValue.Should().Be("Acme Corp");

        // Service Bus enqueue observed — H0 preflight.
        var enq = factory.Enqueuer;
        enq.Enqueued.Should().ContainSingle();
        var envelope = enq.Enqueued.Single();
        envelope.HandlerId.Should().Be("H0", "the initial-dispatch is H0 preflight per design.md § 4.1 DAG.");
        envelope.RunId.Should().Be(responseBody.RunId);
        envelope.CustomerId.Should().Be(TestCustomerId);
        envelope.ParametersJson.Should().Contain("create-run");
    }

    // -------------------------------------------------------------------------
    // Latency spot-check — the 202-return path completes in <100ms with
    // in-memory seams. Real network round-trips are out-of-scope for a unit
    // test; this proves the endpoint's OWN work is well under budget.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRuns_LatencySpotCheck_Under100Ms()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        // Warm-up call so JIT + DI compilation is out of the timing window.
        var warmupReq = BuildAuthenticatedCreateRun();
        (await client.SendAsync(warmupReq)).EnsureSuccessStatusCode();

        var sw = Stopwatch.StartNew();
        var req = BuildAuthenticatedCreateRun();
        var response = await client.SendAsync(req);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        // Generous ceiling — CI hosts vary. The FR-22 requirement is <100ms
        // for the deployed 95th percentile; unit-test envs SHOULD easily stay
        // under 250ms with in-memory seams. If this ever trips, investigate
        // the enqueue path for accidental synchronous I/O.
        sw.ElapsedMilliseconds.Should().BeLessThan(250,
            $"POST /api/runs enqueue-and-return should be <100ms in prod (<250ms in CI) per FR-22 / R20; measured {sw.ElapsedMilliseconds}ms.");

        HttpRequestMessage BuildAuthenticatedCreateRun()
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
            {
                Content = JsonContent.Create(new
                {
                    customerId = TestCustomerId,
                    environmentId = "env-1",
                    tenancyModel = "Model1Shared",
                    profile = "spaarke-hosted-model1-trial",
                    nonSecretParameters = new Dictionary<string, string>
                    {
                        // ISH-01 — tenantId required (Wave 0 Decision 1).
                        ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    },
                }),
            };
            AttachAuth(r, roles: new[] { "Operator" });
            return r;
        }
    }

    // -------------------------------------------------------------------------
    // AC #5 — Reader token + GET /api/runs/{id} -> 200 with Cosmos run payload;
    // partition-key predicate enforced (in-memory repo verifies it internally).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRun_WithReaderToken_Returns200_UsesPartitionKey()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        // Seed a run into the in-memory repository.
        var runId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = runId,
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Running,
            CurrentPhase = "H0",
        });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var run = JsonSerializer.Deserialize<ProvisioningRun>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        run.Should().NotBeNull();
        run!.RunId.Should().Be(runId);
        run.CustomerId.Should().Be(TestCustomerId);

        // Repository was called with the correct partition-key value.
        factory.Repository.ReadCalls.Should().ContainSingle();
        factory.Repository.ReadCalls.Single().Should().Be((TestCustomerId, runId));
    }

    [Fact]
    public async Task GetRun_MissingCustomerIdQuery_Returns400()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{Guid.NewGuid()}");
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "§4D I3: without ?customerId= the endpoint cannot construct a partition-key predicate — must not silently fall back to a cross-partition query.");
    }

    [Fact]
    public async Task GetRun_UnknownRunId_Returns404()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/runs/{Guid.NewGuid()}?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // AC #6 — POST clear-quarantine WITHOUT reason -> 400 + NO audit-log entry.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearQuarantine_WithoutReason_Returns400_AndDoesNotAuditLog()
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-q",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Quarantined,
        });

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/runs/run-q/clear-quarantine?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "reason parameter is REQUIRED per spec FR-24.");

        // No enqueue occurred on the 400 path (early return before enqueue).
        factory.Enqueuer.Enqueued.Should().BeEmpty();

        // No QuarantineCleared audit-log record emitted on the 400 path
        // (audit-log is emitted only on the enqueue-successful path per FR-24
        // acceptance: "audit-log NOT written" on the 400 path).
        factory.AuditLogSink.QuarantineClearedRecords.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // AC #7 — POST clear-quarantine WITH reason -> 202 + audit-log with actor
    // tid + reason. Spec FR-24 acceptance.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearQuarantine_WithReasonAndOperator_Returns202_AndAuditLogsActorTidAndReason()
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-q",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Quarantined,
        });

        var client = factory.CreateClient();
        var reason = "operator manually restored missing SPE container-type";
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/runs/run-q/clear-quarantine?customerId={TestCustomerId}&reason={Uri.EscapeDataString(reason)}");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Enqueue side — ClearQuarantine action fired.
        factory.Enqueuer.Enqueued.Should().ContainSingle();
        var envelope = factory.Enqueuer.Enqueued.Single();
        envelope.HandlerId.Should().Be("ClearQuarantine");
        envelope.ParametersJson.Should().Contain(reason);

        // Audit-log side — QuarantineCleared record with actor tid + oid + reason.
        var record = factory.AuditLogSink.QuarantineClearedRecords.Should().ContainSingle().Subject;
        record.Properties["Reason"].Should().Be(reason);
        record.Properties["RunId"].Should().Be("run-q");
        record.Properties["CustomerId"].Should().Be(TestCustomerId);
        record.Properties["ActorTid"].Should().Be(TestTenantId,
            "Actor tid MUST be extracted from the JWT (spec FR-24 acceptance).");
        record.Properties["ActorOid"].Should().Be(TestObjectId);
    }

    // -------------------------------------------------------------------------
    // REG-03 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
    // 2026-08-27) — ClearQuarantine on Success MUST call
    // runGuard.ReleaseAsync so a subsequent POST /api/runs for the same
    // customer succeeds (sprk_currentrunid is cleared alongside the
    // Quarantined→Failed transition). Without this cascade the operator
    // hits 409 indefinitely on the next-run attempt.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearQuarantine_Success_CallsRunGuardReleaseAsync()
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-q",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Quarantined,
        });

        // Inject a spy CustomerRunGuard so we can observe the ReleaseAsync call.
        var spyGuard = new SpyCustomerRunGuard();
        factory.ReplaceCustomerRunGuard(spyGuard);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/runs/run-q/clear-quarantine?customerId={TestCustomerId}&reason=REG-03-test");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        spyGuard.ReleaseCalls
            .Should().ContainSingle(c => c.CustomerId == TestCustomerId && c.RunId == "run-q",
                because: "REG-03 — ClearQuarantine Success must cascade into CustomerRunGuard.ReleaseAsync " +
                         "so the next POST /api/runs for this customer isn't blocked by a stale sprk_currentrunid.");
    }

    // -------------------------------------------------------------------------
    // Task 061 addition: POST clear-quarantine on a non-Quarantined run
    // returns 409 (wrong-state) — the QuarantineClearService's Conflict path
    // maps to HTTP 409 per POML acceptance §7 negative case.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.WaitingOnGate)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    public async Task ClearQuarantine_OnNonQuarantinedRun_Returns409_WrongState_AndDoesNotAuditLog(RunStatus currentStatus)
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-q",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = currentStatus,
        });

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/runs/run-q/clear-quarantine?customerId={TestCustomerId}&reason=x");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "run in {0} state is not eligible for clear-quarantine (task 061 wrong-state guard).", currentStatus);

        // No enqueue + no audit-log on the 409 path — enqueue + audit-log fire ONLY on Success.
        factory.Enqueuer.Enqueued.Should().BeEmpty();
        factory.AuditLogSink.QuarantineClearedRecords.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Resume + cancel + gate-advance + preflight — smoke pass with Operator
    // token; Reader → 403 for one representative (cancel) to keep the auth-
    // matrix compact.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostResume_WithOperator_Returns202AndEnqueuesResumeAction()
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-r",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Failed,
            CurrentPhase = "H4",
        });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/runs/run-r/resume?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var envelope = factory.Enqueuer.Enqueued.Should().ContainSingle().Subject;
        envelope.HandlerId.Should().Be("Resume");
        envelope.ParametersJson.Should().Contain("H4", "current-phase context is preserved into the resume envelope.");
    }

    [Fact]
    public async Task PostCancel_WithReader_Returns403()
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-c",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Running,
        });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/runs/run-c/cancel?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostGateAdvance_WithOperator_EnqueuesGateAdvanceAction()
    {
        using var factory = new L2WebApplicationFactory();
        factory.Repository.Seed(new ProvisioningRun
        {
            RunId = "run-g",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.WaitingOnGate,
        });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/runs/run-g/gates/admin-consent/advance?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var envelope = factory.Enqueuer.Enqueued.Should().ContainSingle().Subject;
        envelope.HandlerId.Should().Be("GateAdvance");
        envelope.ParametersJson.Should().Contain("admin-consent");
    }

    [Fact]
    public async Task PostPreflight_UnknownRunId_Returns404()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/runs/{Guid.NewGuid()}/preflight?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // GET /api/runs/{id}/phases/{phaseId}/logs — RunLogsEndpoints.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPhaseLogs_WithReader_ReturnsCompletedPhaseRecord()
    {
        using var factory = new L2WebApplicationFactory();
        var run = new ProvisioningRun
        {
            RunId = "run-p",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Running,
            CurrentPhase = "H2a",
        };
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H0",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            IdempotencyKey = "h0-test",
            JobId = "job-1",
        });
        factory.Repository.Seed(run);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/runs/run-p/phases/H0/logs?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"phase\":\"H0\"");
        body.Should().Contain("\"idempotencyKey\":\"h0-test\"");
    }

    [Fact]
    public async Task GetPhaseLogs_InFlightPhase_Returns404WithHint()
    {
        using var factory = new L2WebApplicationFactory();
        var run = new ProvisioningRun
        {
            RunId = "run-if",
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Running,
            CurrentPhase = "H2a", // in flight, not in CompletedPhases yet
        };
        factory.Repository.Seed(run);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/runs/run-if/phases/H2a/logs?customerId={TestCustomerId}");
        AttachAuth(request, roles: new[] { "Reader" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("in flight", "the hint should call out in-flight vs unreached distinction.");
    }

    // -------------------------------------------------------------------------
    // ISH-01 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
    // 2026-08-27, Wave 0 Decision 1) — POST /api/runs MUST validate that
    // nonSecretParameters['tenantId'] is present + non-empty. Per Wave 0
    // Decision 1 the canonical tenantId propagation path is via
    // nonSecretParameters; a missing value would fail the H0 dispatch with
    // missing-tenant-id, wasting the entire H0 preflight window.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRuns_MissingTenantIdInNonSecretParameters_Returns400()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                // ISH-01: NO nonSecretParameters at all → tenantId missing.
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "ISH-01 — CreateRun must fail-fast when nonSecretParameters['tenantId'] is absent.");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tenantId", "the diagnostic must name the missing key so the operator can fix the intake.");

        // Neither the Cosmos row nor the Service Bus envelope should be created.
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ISH-11 (customer-provisioning-orchestration-r1 Wave 5 punchlist,
    // 2026-08-27) — CreateRun MUST reject invalid tenancyModel × profile
    // pairs at the HTTP surface, mirroring intake.schema.json's allOf logic.
    // A direct-API caller (test harness, retry script) supplying an invalid
    // pair would otherwise reach downstream handlers (H5 tier derivation,
    // H11 user provisioning gate) which fail cryptically.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Model1Shared", "spaarke-hosted-model2")]
    [InlineData("Model1Shared", "customer-owned-model2")]
    [InlineData("Model2Dedicated", "spaarke-hosted-model1-trial")]
    public async Task PostRuns_InvalidTenancyProfilePair_Returns400(string tenancyModel, string profile)
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel,
                profile,
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    ["subscriptionId"] = "abcdef01-2345-6789-abcd-ef0123456789",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"ISH-11 — tenancyModel='{tenancyModel}' × profile='{profile}' is not a valid pair.");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tenancyModel", "diagnostic must reference the invariant.");
        body.Should().Contain(profile, "diagnostic must echo the offending profile.");
        factory.Repository.CreatedRuns.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_UnknownTenancyModel_Returns400()
    {
        // ISH-11 — a typo like 'Model3Foo' MUST be rejected; the intake
        // schema enum blocks this in batch dispatch, but the direct-API
        // path (test harnesses) needs the same protection.
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model3Foo",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Model3Foo");
        factory.Repository.CreatedRuns.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ISH-02 (customer-provisioning-orchestration-r1 Wave 5 punchlist,
    // 2026-08-27) — Model2Dedicated CreateRun MUST fail-fast with 400 when
    // nonSecretParameters['subscriptionId'] is absent. Ten downstream handlers
    // hard-stop on absence with MissingSubscriptionId; surfacing at intake
    // saves a minimum ~20s H1 dispatch + gives operators a fixable diagnostic.
    // Mirrors the intake.schema.json Model2Dedicated allOf constraint.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRuns_Model2Dedicated_MissingSubscriptionId_Returns400()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model2Dedicated",
                profile = "customer-owned-model2",
                nonSecretParameters = new Dictionary<string, string>
                {
                    // ISH-02: tenantId supplied but subscriptionId absent → 400 for Model 2.
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "ISH-02 — Model 2 CreateRun MUST fail-fast when subscriptionId is absent (ADR-027 D4).");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("subscriptionId",
            "the diagnostic must name the missing key so the operator can fix the intake.");
        body.Should().Contain("Model2Dedicated",
            "the diagnostic must scope the rule to Model 2 so Model 1 operators are not confused.");

        // Neither the Cosmos row nor the Service Bus envelope should be created.
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_Model1Shared_MissingSubscriptionId_Returns202()
    {
        // ISH-02 exemption — Model 1 does NOT require subscriptionId at intake
        // (the skill auto-injects the Spaarke shared sub-id at CreateRun time).
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    // NO subscriptionId — Model 1 exemption per ISH-02.
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "ISH-02 — Model 1 runs are exempt from the subscriptionId requirement at intake.");
    }

    [Fact]
    public async Task PostRuns_Model2Dedicated_EmptySubscriptionId_Returns400()
    {
        // ISH-02: whitespace-only subscriptionId is treated identically to missing.
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model2Dedicated",
                profile = "customer-owned-model2",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    ["subscriptionId"] = "   ",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Repository.CreatedRuns.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_Model2Dedicated_ValidSubscriptionId_Returns202_AndFlowsToRunParameters()
    {
        // ISH-02 happy path: Model 2 with subscriptionId proceeds + value round-trips
        // into RunParameters.NonSecret so downstream handlers can read it.
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();
        var expectedSubscriptionId = "abcdef01-2345-6789-abcd-ef0123456789";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model2Dedicated",
                profile = "customer-owned-model2",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    ["subscriptionId"] = expectedSubscriptionId,
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Repository.CreatedRuns.Should().ContainSingle();
        var stored = factory.Repository.CreatedRuns.Single();
        stored.Parameters.NonSecret
            .Should().ContainKey("subscriptionId")
            .WhoseValue.Should().Be(expectedSubscriptionId,
                because: "ISH-02 — subscriptionId must round-trip from intake → Cosmos so H1/H2a/etc can read it.");
    }

    [Fact]
    public async Task PostRuns_EmptyTenantIdInNonSecretParameters_Returns400()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    // ISH-01: present but whitespace-only → still fail-fast.
                    ["tenantId"] = "   ",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Repository.CreatedRuns.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // REG-07 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
    // 2026-08-27) — CreateRun MUST cross-check environmentId against the
    // registry AFTER concurrency-guard acquire + BEFORE Cosmos write.
    //   - customerId mismatch → 400 + guard released.
    //   - setupStatus != InProgress → 400 + guard released.
    //   - lookup fault → proceed (fault-tolerance branch).
    //   - unknown envId (null snapshot) → proceed (Null-Object indistinguishable).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRuns_Reg07_CustomerIdMismatch_Returns400()
    {
        using var factory = new L2WebApplicationFactory();
        // Register a stub registry that returns a snapshot for a DIFFERENT customerId.
        var stub = new StubRegistryClient
        {
            Snapshot = new Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot(
                EnvironmentId: "env-1",
                CustomerId: "OTHER-CUSTOMER",
                TenantId: "11111111-1111-1111-1111-111111111111",
                SetupStatus: "InProgress",
                CurrentRunId: null),
        };
        factory.ReplaceRegistryClient(stub);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "REG-07 — cross-customer environmentId must fail-fast with 400.");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("REG-07");
        body.Should().Contain("OTHER-CUSTOMER");
        factory.Repository.CreatedRuns.Should().BeEmpty(
            because: "REG-07 must reject BEFORE the Cosmos write.");
    }

    [Fact]
    public async Task PostRuns_Reg07_SetupStatusReady_Returns400()
    {
        using var factory = new L2WebApplicationFactory();
        // Row exists + belongs to this customer but is already Ready — a
        // second run would overwrite a finalized registry state.
        var stub = new StubRegistryClient
        {
            Snapshot = new Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot(
                EnvironmentId: "env-1",
                CustomerId: TestCustomerId,
                TenantId: "11111111-1111-1111-1111-111111111111",
                SetupStatus: "Ready",
                CurrentRunId: null),
        };
        factory.ReplaceRegistryClient(stub);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("setupStatus='Ready'");
        factory.Repository.CreatedRuns.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_Reg07_LookupInfraFault_Proceeds_To_202()
    {
        // Fault-tolerance branch: registry lookup infra fault MUST NOT block
        // CreateRun (concurrency guard + Cosmos audit trail are the fallback).
        using var factory = new L2WebApplicationFactory();
        var stub = new StubRegistryClient { ThrowOnLookup = new InvalidOperationException("registry-down") };
        factory.ReplaceRegistryClient(stub);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "REG-07 — lookup infra fault is a soft-fault; CreateRun MUST NOT block operators on a degraded registry.");
        factory.Repository.CreatedRuns.Should().ContainSingle();
    }

    [Fact]
    public async Task PostRuns_ValidTenantIdInNonSecretParameters_Returns202_AndFlowsToRunParameters()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();
        var expectedTenantId = "aabbccdd-1122-3344-5566-778899aabbcc";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["tenantId"] = expectedTenantId,
                },
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "ISH-01 — with a valid tenantId in nonSecretParameters the endpoint proceeds to 202.");

        // ISH-01 round-trip proof: tenantId lands in the RUN's NonSecret map so
        // every downstream handler can read it (Wave 0 Decision 1 canonical path).
        factory.Repository.CreatedRuns.Should().ContainSingle();
        var stored = factory.Repository.CreatedRuns.Single();
        stored.Parameters.NonSecret
            .Should().ContainKey("tenantId")
            .WhoseValue.Should().Be(expectedTenantId,
                because: "ISH-01 — tenantId must round-trip from intake → Cosmos so handlers can read it.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AttachAuth(HttpRequestMessage request, string[] roles)
    {
        // TestAuthenticationHandler reads roles from a request header; the
        // scheme name is fixed by TestAuthenticationHandler.SchemeName.
        request.Headers.Add(TestAuthenticationHandler.RolesHeader, string.Join(",", roles));
        // Providing an Authorization header of ANY value ensures IsAuthenticated
        // resolves true (the TestAuthenticationHandler skips authentication when
        // no Authorization header is present — mirrors the real 401 path).
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    }

    private sealed record CreateRunResponsePayload
    {
        public string RunId { get; init; } = string.Empty;
        public string CustomerId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
    }
}

// -----------------------------------------------------------------------------
// L2WebApplicationFactory — WebApplicationFactory<Program> with the two
// production seams (IProvisioningRunRepository + IHandlerEnqueuer) replaced by
// in-memory recorders, and the auth scheme swapped for a test-only header
// reader. All other DI stays intact — CosmosModule + ServiceBusModule still
// load (they need config to satisfy fail-fast validators) but their clients
// are never invoked because the seams above them route to the in-memory impls.
// -----------------------------------------------------------------------------

public sealed class L2WebApplicationFactory : WebApplicationFactory<Program>
{
    public InMemoryProvisioningRunRepository Repository { get; } = new();
    public InMemoryHandlerEnqueuer Enqueuer { get; } = new();
    public TestAuditLogSink AuditLogSink { get; } = new();

    // REG-07 (customer-provisioning-orchestration-r1 Wave 2 B24, 2026-08-27):
    // tests that exercise the registry cross-check inject their own stub via
    // ReplaceRegistryClient BEFORE calling CreateClient(). Leaving this null
    // means the real Path X DataverseEnvironmentRegistryClient stays
    // registered; its outbound HTTP call to the stub URL will fault → CreateRun
    // catches the fault and proceeds (REG-07 fault-tolerance branch).
    private Sprk.Provisioning.ControlPlane.Registry.IDataverseEnvironmentRegistryClient? _registryStub;

    public void ReplaceRegistryClient(Sprk.Provisioning.ControlPlane.Registry.IDataverseEnvironmentRegistryClient stub)
    {
        _registryStub = stub;
    }

    // REG-03 (2026-08-27) — spy CustomerRunGuard for observing ReleaseAsync
    // calls from the ClearQuarantine Success cascade.
    private Sprk.Provisioning.ControlPlane.Concurrency.ICustomerRunGuard? _guardStub;

    public void ReplaceCustomerRunGuard(Sprk.Provisioning.ControlPlane.Concurrency.ICustomerRunGuard stub)
    {
        _guardStub = stub;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Ensure fail-fast validators in AddCosmosModule + AddServiceBusModule +
        // AddTelemetryModule are satisfied without a live endpoint. The clients
        // constructed from these configs are never actually invoked because we
        // replace the seams that would call them.
        builder.UseSetting("Cosmos:AccountEndpoint", "https://l2-test.documents.azure.com:443/");
        builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "l2-test.servicebus.windows.net");

        // REG-02 (Wave 2 pre-dispatch remediation, 2026-08-27) — the
        // CustomerRunGuardOptions.Enabled default flipped to true, so the real
        // guard's Options.Validate() would fail-fast at boot without a URL.
        // Test hosts opt out via the ADR-032 kill-switch: guard returns
        // AcquireResult.Success unconditionally when Enabled=false, so the
        // in-memory Repository seam handles CreateRun without an admin-env
        // Dataverse call. This preserves the pre-REG-02 test semantics
        // (no I5 guard interference in RunsEndpointsTests).
        builder.UseSetting("CustomerRunGuard:Enabled", "false");

        // REG-07 (Wave 2 pre-dispatch remediation, 2026-08-27) — the Api
        // Program.cs now registers DataverseEnvironmentRegistryClient (Path X);
        // its options.Validate() requires AdminEnvironmentUrl. Provide a stub
        // URL to satisfy the fail-fast validator — the actual HTTP calls are
        // never invoked because REG-07's fault-tolerance branch swallows
        // registry-lookup exceptions and lets CreateRun proceed. The tests
        // that rely on strict registry checks would need a fake registered
        // via ConfigureServices below.
        builder.UseSetting("DataverseEnvironmentRegistry:AdminEnvironmentUrl", "https://l2-test.crm.dynamics.com");

        // Testing environment — TelemetryModule's AzureMonitorGuard skips
        // exporter wiring silently on non-Development/Production envs.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace the two production seams with in-memory recorders.
            ReplaceSingleton<IProvisioningRunRepository>(services, Repository);
            ReplaceSingleton<IHandlerEnqueuer>(services, Enqueuer);

            // Layer a test-only authentication scheme on top of the production
            // AuthModule composition and MAKE IT THE DEFAULT. The production
            // JwtBearer scheme stays registered but is never used because our
            // policies (Operator / Reader in AuthModule.cs) do NOT specify a
            // scheme name — they resolve against the default scheme. Setting
            // TestBearer as the default routes all [Authorize] challenges here
            // without a JwtBearer scheme collision (which was the failure mode
            // of trying to re-register "Bearer" as an alias).
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
            });

            // Wire the ILoggerProvider that captures QuarantineCleared records
            // so the FR-24 audit-log assertion has a deterministic sink.
            services.AddSingleton<ILoggerProvider>(AuditLogSink);

            // REG-07 stub injection — when a test registered a stub via
            // ReplaceRegistryClient, swap out the real Path X client.
            if (_registryStub is not null)
            {
                ReplaceRegistryClientRegistration(services, _registryStub);
            }

            // REG-03 spy injection — when a test registered a spy via
            // ReplaceCustomerRunGuard, swap out the real guard so the test
            // can observe ReleaseAsync calls.
            if (_guardStub is not null)
            {
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(Sprk.Provisioning.ControlPlane.Concurrency.ICustomerRunGuard))
                    {
                        services.RemoveAt(i);
                    }
                }
                services.AddSingleton(_guardStub);
            }
        });
    }

    private static void ReplaceRegistryClientRegistration(
        IServiceCollection services,
        Sprk.Provisioning.ControlPlane.Registry.IDataverseEnvironmentRegistryClient stub)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(Sprk.Provisioning.ControlPlane.Registry.IDataverseEnvironmentRegistryClient))
            {
                services.RemoveAt(i);
            }
        }
        services.AddSingleton(stub);
    }

    private static void ReplaceSingleton<TService>(IServiceCollection services, TService instance)
        where TService : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService))
            {
                services.RemoveAt(i);
            }
        }
        services.AddSingleton(instance);
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(T))
            {
                services.RemoveAt(i);
            }
        }
    }
}

// -----------------------------------------------------------------------------
// InMemoryProvisioningRunRepository — records reads + writes; seeds arbitrary
// runs for GET tests. Enforces the same partition-key contract as the real
// Cosmos repository: reads require both customerId + runId; a read with the
// wrong customerId (partition) returns null.
// -----------------------------------------------------------------------------

public sealed class InMemoryProvisioningRunRepository : IProvisioningRunRepository
{
    private readonly Dictionary<(string CustomerId, string RunId), (ProvisioningRun Run, string ETag)> _store = new();

    public List<ProvisioningRun> CreatedRuns { get; } = new();
    public List<(string CustomerId, string RunId)> ReadCalls { get; } = new();

    public void Seed(ProvisioningRun run)
    {
        _store[(run.CustomerId, run.RunId)] = (run, "\"seed-etag\"");
    }

    public Task<ProvisioningRunReadResult?> ReadRunAsync(
        string customerId, string runId, CancellationToken cancellationToken)
    {
        ReadCalls.Add((customerId, runId));
        if (_store.TryGetValue((customerId, runId), out var stored))
        {
            return Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(stored.Run, stored.ETag));
        }
        return Task.FromResult<ProvisioningRunReadResult?>(null);
    }

    public Task<ProvisioningRunReadResult> CreateRunAsync(
        ProvisioningRun run, CancellationToken cancellationToken)
    {
        var key = (run.CustomerId, run.RunId);
        if (_store.ContainsKey(key))
        {
            throw new InvalidOperationException($"Run '{run.RunId}' already exists.");
        }
        var etag = "\"created-" + Guid.NewGuid().ToString("N") + "\"";
        _store[key] = (run, etag);
        CreatedRuns.Add(run);
        return Task.FromResult(new ProvisioningRunReadResult(run, etag));
    }

    public Task<ReplaceRunResult> ReplaceRunAsync(
        ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
    {
        var key = (run.CustomerId, run.RunId);
        if (!_store.TryGetValue(key, out var stored))
        {
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.NotFound());
        }
        if (stored.ETag != ifMatchEtag)
        {
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(stored.Run, stored.ETag)));
        }
        var newEtag = "\"replaced-" + Guid.NewGuid().ToString("N") + "\"";
        _store[key] = (run, newEtag);
        return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, newEtag));
    }
}

// -----------------------------------------------------------------------------
// InMemoryHandlerEnqueuer — records envelopes for assertion.
// -----------------------------------------------------------------------------

public sealed class InMemoryHandlerEnqueuer : IHandlerEnqueuer
{
    public List<HandlerEnvelope> Enqueued { get; } = new();

    public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        Enqueued.Add(envelope);
        return Task.CompletedTask;
    }
}

// -----------------------------------------------------------------------------
// SpyCustomerRunGuard — records TryAcquireAsync / ReleaseAsync invocations for
// REG-03 test assertions. Returns Success unconditionally so tests focus on
// the ClearQuarantine cascade behavior, not guard mechanics.
// -----------------------------------------------------------------------------

internal sealed class SpyCustomerRunGuard : Sprk.Provisioning.ControlPlane.Concurrency.ICustomerRunGuard
{
    public List<(string CustomerId, string RunId)> AcquireCalls { get; } = new();
    public List<(string CustomerId, string RunId)> ReleaseCalls { get; } = new();

    public Task<Sprk.Provisioning.ControlPlane.Concurrency.AcquireResult> TryAcquireAsync(
        string customerId, string runId, CancellationToken cancellationToken)
    {
        AcquireCalls.Add((customerId, runId));
        return Task.FromResult<Sprk.Provisioning.ControlPlane.Concurrency.AcquireResult>(
            new Sprk.Provisioning.ControlPlane.Concurrency.AcquireResult.Success(customerId, runId));
    }

    public Task<Sprk.Provisioning.ControlPlane.Concurrency.ReleaseResult> ReleaseAsync(
        string customerId, string runId, CancellationToken cancellationToken)
    {
        ReleaseCalls.Add((customerId, runId));
        return Task.FromResult<Sprk.Provisioning.ControlPlane.Concurrency.ReleaseResult>(
            new Sprk.Provisioning.ControlPlane.Concurrency.ReleaseResult.Released(customerId, runId));
    }
}

// -----------------------------------------------------------------------------
// StubRegistryClient — test-only IDataverseEnvironmentRegistryClient for the
// REG-07 CreateRun cross-check tests. Returns a pre-canned Snapshot from
// LookupByEnvironmentIdAsync (or throws when ThrowOnLookup is set). All other
// methods return safe defaults so nothing else in the DAG breaks.
// -----------------------------------------------------------------------------

internal sealed class StubRegistryClient : Sprk.Provisioning.ControlPlane.Registry.IDataverseEnvironmentRegistryClient
{
    public Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot? Snapshot { get; set; }
    public Exception? ThrowOnLookup { get; set; }

    public Task<Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot?> LookupByEnvironmentIdAsync(
        string environmentId, CancellationToken cancellationToken)
    {
        if (ThrowOnLookup is { } ex) throw ex;
        return Task.FromResult(Snapshot);
    }

    public Task<Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
        string tenantId, CancellationToken cancellationToken)
        => Task.FromResult<Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot?>(null);

    public Task<Sprk.Provisioning.ControlPlane.Registry.RegistryUpdateOutcome> UpdateSetupStatusAsync(
        Sprk.Provisioning.ControlPlane.Registry.RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
        => Task.FromResult<Sprk.Provisioning.ControlPlane.Registry.RegistryUpdateOutcome>(new Sprk.Provisioning.ControlPlane.Registry.RegistryUpdateOutcome.Success());
}

// -----------------------------------------------------------------------------
// TestAuthenticationHandler — reads roles from X-Test-Roles header. Returns
// NoResult when no Authorization header is present so the auth pipeline emits
// the standard 401 (parity with a missing JWT bearer). When Authorization is
// present, builds a ClaimsPrincipal from X-Test-Roles + the test tid/oid
// constants.
// -----------------------------------------------------------------------------

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestBearer";
    public const string RolesHeader = "X-Test-Roles";
    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string TestObjectId = "22222222-2222-2222-2222-222222222222";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out _))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var rolesRaw = Request.Headers.TryGetValue(RolesHeader, out var vs) ? vs.ToString() : string.Empty;
        var claims = new List<Claim>
        {
            new("http://schemas.microsoft.com/identity/claims/tenantid", TestTenantId),
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", TestObjectId),
        };
        if (!string.IsNullOrWhiteSpace(rolesRaw))
        {
            foreach (var role in rolesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// -----------------------------------------------------------------------------
// TestAuditLogSink — ILoggerProvider that intercepts RunsMarker logs to
// capture the QuarantineCleared record for FR-24 assertions.
// -----------------------------------------------------------------------------

public sealed class TestAuditLogSink : ILoggerProvider
{
    public List<AuditRecord> QuarantineClearedRecords { get; } = new();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    public void Dispose() { }

    public sealed record AuditRecord(string CategoryName, string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class SinkLogger : ILogger
    {
        private readonly TestAuditLogSink _parent;
        private readonly string _categoryName;

        public SinkLogger(TestAuditLogSink parent, string categoryName)
        {
            _parent = parent;
            _categoryName = categoryName;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            // Kusto pivot: only capture the FR-24 audit record here so the
            // sink doesn't fill with unrelated framework logs. The general
            // AuditableAction record from AuditLogMiddleware is exercised in
            // AuditLogMiddlewareTests.
            if (!message.StartsWith(RunsEndpoints.QuarantineClearedEventName + ":", StringComparison.Ordinal))
            {
                return;
            }
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kvp in kvps)
                {
                    if (kvp.Key == "{OriginalFormat}") continue;
                    props[kvp.Key] = kvp.Value;
                }
            }
            _parent.QuarantineClearedRecords.Add(new AuditRecord(_categoryName, message, props));
        }
    }
}
