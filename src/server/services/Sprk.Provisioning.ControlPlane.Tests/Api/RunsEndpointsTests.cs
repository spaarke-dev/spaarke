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
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
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
    private const string TestCustomerId = "testcust"; // T237: customerId standard ^[a-z][a-z0-9]{2,7}$
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
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
            tenancyModel = "Model1",
            profile = "spaarke-hosted-model2",
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    // An accepted intake key (IntakeParameterCatalog, task 245a) — proves intake
                    // values reach the stored run.
                    ["region"] = "westus2",
                    // ISH-01 (Wave 2 pre-dispatch remediation): tenantId is the
                    // canonical propagation path (Wave 0 Decision 1).
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                }),
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
        stored.Parameters.NonSecret.Should().ContainKey("region").WhoseValue.Should().Be("westus2");

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
                    tenancyModel = "Model1",
                    profile = "spaarke-hosted-model2",
                    nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                    {
                        // ISH-01 — tenantId required (Wave 0 Decision 1).
                        ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    }),
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
        // COMP-06 / ROLLBACK-1 (SESSION 17): as of this session's fix,
        // Release is called TWICE on the Success path — once by the service
        // (QuarantineClearService.ClearAsync, atomic with the Cosmos state
        // transition per file header §5) and once by the endpoint (REG-03
        // belt-and-suspenders — retained so a future non-endpoint caller of
        // ClearAsync doesn't need to duplicate the release step). Both calls
        // are idempotent-safe: the CustomerRunGuard only clears the column
        // when its current value matches this runId, so the second call is a
        // documented Mismatched no-op (well, Released the first time,
        // Mismatched the second — either way sprk_currentrunid ends up null).
        spyGuard.ReleaseCalls
            .Should().OnlyContain(c => c.CustomerId == TestCustomerId && c.RunId == "run-q",
                because: "every release call MUST scope to the same customer+run tuple")
            .And.HaveCount(2,
                because: "COMP-06 (SESSION 17): the service now releases atomically as part of ClearAsync, " +
                         "AND the endpoint keeps its REG-03 belt-and-suspenders release call — both fire on Success, " +
                         "both are idempotent-safe via the CustomerRunGuard's stale-value guard.");
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
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
    // T237 (owner D10 / INCOMING-CUSTOMERID-STANDARD §3.1) — CreateRun enforces the
    // customerId standard ^[a-z][a-z0-9]{2,7}$ before the registry lookup, any Cosmos
    // write and any enqueue. Reject, never repair (no trimming / lower-casing).
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("ab")]          // 2 chars
    [InlineData("abcdefghi")]   // 9 chars
    [InlineData("Acme")]        // uppercase
    [InlineData("acme-x")]      // hyphen
    [InlineData("1acme")]       // leading digit
    [InlineData("acme_x")]      // underscore
    [InlineData(" acme")]       // leading space — not trimmed
    public async Task PostRuns_NonCompliantCustomerId_Returns400_BeforeRegistryCosmosOrEnqueue(string customerId)
    {
        using var factory = new L2WebApplicationFactory();
        var registry = new StubRegistryClient();
        factory.ReplaceRegistryClient(registry);
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildValidCreateRunRequest(customerId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("customerId standard", "the diagnostic must name the rule the value broke");
        ReadProblemDetail(body).Should().Contain($"'{customerId}'", "the diagnostic must echo the rejected value");
        ReadProblemErrorCode(body).Should().Be("customer-id-nonstandard");
        registry.LookupCount.Should().Be(0, "the standard is checked before the REG-07 registry lookup");
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData("platform")]    // rg-spaarke-platform-{env} hosts the BFF + L2
    [InlineData("shared")]
    [InlineData("byok")]
    public async Task PostRuns_ReservedCustomerId_Returns400_BeforeRegistryCosmosOrEnqueue(string customerId)
    {
        using var factory = new L2WebApplicationFactory();
        var registry = new StubRegistryClient();
        factory.ReplaceRegistryClient(registry);
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildValidCreateRunRequest(customerId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        ReadProblemDetail(body).Should().Contain($"customerId '{customerId}' is reserved");
        ReadProblemErrorCode(body).Should().Be("customer-id-reserved");
        registry.LookupCount.Should().Be(0);
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData("abc")]         // 3 chars — lower bound
    [InlineData("abcdefgh")]    // 8 chars — upper bound
    public async Task PostRuns_CompliantCustomerIdAtLengthBounds_Returns202(string customerId)
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildValidCreateRunRequest(customerId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Repository.CreatedRuns.Should().ContainSingle(r => r.CustomerId == customerId);
    }

    // -------------------------------------------------------------------------
    // Task 245a (G25 — run-context contract): nonSecretParameters is the only
    // writer of run.Parameters.NonSecret, so it accepts only the closed
    // IntakeParameterCatalog set, and the stamp environment is resolved once.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("tenant_id")]       // snake_case typo of an accepted key — no handler reads it
    [InlineData("display-name")]    // read by nothing
    [InlineData("keyVaultUri")]     // an H2a output smuggled in as a parameter — belongs in InterStepState
    public async Task PostRuns_UnknownNonSecretKey_Returns400_BeforeRegistryCosmosOrEnqueue(string unknownKey)
    {
        using var factory = new L2WebApplicationFactory();
        var registry = new StubRegistryClient();
        factory.ReplaceRegistryClient(registry);
        var guard = new SpyCustomerRunGuard();
        factory.ReplaceCustomerRunGuard(guard);
        var client = factory.CreateClient();
        var nonSecret = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            [unknownKey] = "x",
        };

        var response = await client.SendAsync(BuildCreateRunRequest("testcust", nonSecret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        ReadProblemDetail(body).Should().Contain(unknownKey);
        ReadProblemErrorCode(body).Should().Be("intake-unknown-key");
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("acceptedKeys").EnumerateArray()
            .Select(k => k.GetString()).Should().BeEquivalentTo(IntakeParameterCatalog.All.Keys,
                "the caller learns the accepted set from the response, not from reading code");
        guard.AcquireCalls.Should().BeEmpty("the intake catalog is checked before the I5 run guard is acquired");
        registry.LookupCount.Should().Be(0, "the intake catalog is checked before the REG-07 registry lookup");
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_EnvironmentNameAbsent_StoresProdOnTheRun()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildValidCreateRunRequest("testcust"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Repository.CreatedRuns.Single().Parameters.NonSecret
            .Should().Contain(IntakeParameterCatalog.EnvironmentName, "prod",
                "every handler reads one stored stamp environment (owner D6: customer stamps are prod-only)");
    }

    [Theory]
    [InlineData("demo")]   // an L2 control-plane tier, not a customer.bicep environmentName
    [InlineData("Prod")]   // case-sensitive: resource names are built from it, so one spelling per stamp
    public async Task PostRuns_EnvironmentNameNotAllowed_Returns400_BeforeCosmosOrEnqueue(string environmentName)
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();
        var nonSecret = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            [IntakeParameterCatalog.EnvironmentName] = environmentName,
        };

        var response = await client.SendAsync(BuildCreateRunRequest("testcust", nonSecret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        ReadProblemDetail(body).Should().Contain($"'{environmentName}'");
        ReadProblemErrorCode(body).Should().Be("intake-invalid-environment-name");
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_SolutionPackageTypeAbsent_StoresManagedOnTheRun()
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildValidCreateRunRequest("testcust"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Repository.CreatedRuns.Single().Parameters.NonSecret
            .Should().Contain(IntakeParameterCatalog.SolutionPackageType, "managed",
                "managed by default; the stored value is what H6 imports and H13 records (ADR-027 §3, owner D8)");
    }

    [Theory]
    [InlineData("Managed")]   // exact case
    [InlineData("both")]
    [InlineData("")]
    public async Task PostRuns_SolutionPackageTypeNotAllowed_Returns400_BeforeCosmosOrEnqueue(string value)
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();
        var nonSecret = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            [IntakeParameterCatalog.SolutionPackageType] = value,
        };

        var response = await client.SendAsync(BuildCreateRunRequest("testcust", nonSecret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        ReadProblemErrorCode(body).Should().Be("intake-invalid-solution-package-type");
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRuns_ProvisionEnvironmentSkillStep40Payload_Returns202()
    {
        // The exact key set /provision-environment Step 4.0 sends (SKILL.md) must stay accepted.
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();
        var nonSecret = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["subscriptionId"] = "22222222-2222-2222-2222-222222222222",
            ["openAiLocation"] = "westus3",
            ["confirmationAcknowledgment"] = "proceed with provisioning",
            ["intakeFileSha256"] = "ABCDEF",
            ["region"] = "westus2",
            ["tier"] = "dedicated",          // T229: a CostEnvelopeIntake tier (required, exact case)
            ["estimatedMonthlyUsd"] = "900",
            ["operatorUpn"] = "operator@spaarke.com",
            ["containerTypeId"] = "33333333-3333-3333-3333-333333333333",
            ["dataverseEnvUrl"] = "https://spaarke-testcust.crm.dynamics.com/",   // T228
            // T245c: the operator intake H11 / H14 / H4 need. T232: Model 1 takes only B2BGuest + the environment group.
            ["identityPreset"] = "B2BGuest",
            ["usersJson"] = "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@contoso.com\",\"companyName\":\"Contoso\"}]",
            ["environmentSecurityGroupId"] = "6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b",
            ["exchangePolicyScopeGroupId"] = "spaarke-mail-scope@contoso.com",
            ["communicationGraphResource"] = "users/comms@contoso.com/messages",
            ["emailGraphResource"] = null!,   // Step 4.0 always sends the key; null when the intake omits it
            ["communicationDefaultMailbox"] = "comms@contoso.com",
        };

        var response = await client.SendAsync(BuildCreateRunRequest("testcust", nonSecret));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Repository.CreatedRuns.Should().ContainSingle();
    }

    // -------------------------------------------------------------------------
    // Task 245c (G25): the operator intake H11 / H14 / H4 need is validated at
    // POST /api/runs with the handlers' own rules — H11's through the same
    // UserProvisioningIntake code H11 runs — and the handlers' own codes.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("identityPreset", null, "userprov-missing-identity-preset")]
    [InlineData("identityPreset", "nativeaccount", "userprov-invalid-identity-preset")]   // exact case, as H11
    [InlineData("usersJson", null, "userprov-missing-users")]
    [InlineData("usersJson", "[]", "userprov-missing-users")]
    [InlineData("usersJson", "{\"firstName\":\"Ada\"}", "userprov-malformed-users-payload")]   // an object, not an array
    [InlineData("usersJson", "[{\"firstName\":\"Ada\"", "userprov-malformed-users-payload")]  // truncated
    [InlineData("usersJson", "[{\"firstName\":\"Ada\",\"lastName\":\" \"}]", "userprov-invalid-user-entry")]   // a guest without an email
    [InlineData("identityPreset", "NativeAccount", "userprov-model1-requires-b2b-guest")]            // T232 (owner D2)
    [InlineData("environmentSecurityGroupId", null, "userprov-missing-security-group-id")]            // T232
    [InlineData("environmentSecurityGroupId", "sprk-testcust-users", "userprov-invalid-security-group-id")]   // a name, not the object id
    [InlineData("environmentSecurityGroupId", "{6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b}", "userprov-invalid-security-group-id")]   // only the schema's hyphenated form
    [InlineData("usersJson", "[{\"email\":\"ada@contoso.com\"},{\"email\":\"ADA@contoso.com\"}]", "userprov-invalid-user-entry")]   // a repeated email (any case) would be invited twice
    [InlineData("exchangePolicyScopeGroupId", null, "h14a-missing-policy-scope-group-id")]
    [InlineData("exchangePolicyScopeGroupId", "  ", "h14a-missing-policy-scope-group-id")]
    [InlineData("communicationDefaultMailbox", null, "intake-communication-default-mailbox-invalid")]
    [InlineData("communicationDefaultMailbox", "Contoso Communications", "intake-communication-default-mailbox-invalid")]
    public async Task PostRuns_OperatorIntakeBreaksAHandlerRule_Returns400_BeforeGuardRegistryCosmosOrEnqueue(
        string key, string? value, string expectedErrorCode)
    {
        var nonSecret = WithOperatorIntake(new Dictionary<string, string> { ["tenantId"] = "11111111-1111-1111-1111-111111111111" });
        if (value is null) nonSecret.Remove(key); else nonSecret[key] = value;

        await AssertRejectedBeforeAnySideEffectAsync(nonSecret, expectedErrorCode);
    }

    [Fact]
    public async Task PostRuns_B2BGuestUserWithoutEmail_Returns400_NamingTheEntryNotThePerson()
    {
        // H11 used to find this mid-loop, after inviting the users before it.
        var nonSecret = WithOperatorIntake(new Dictionary<string, string> { ["tenantId"] = "11111111-1111-1111-1111-111111111111" });
        nonSecret["identityPreset"] = "B2BGuest";
        nonSecret["usersJson"] =
            "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@contoso.com\"},{\"firstName\":\"Grace\",\"lastName\":\"Hopper\"}]";

        var detail = await AssertRejectedBeforeAnySideEffectAsync(nonSecret, "userprov-invalid-user-entry");

        detail.Should().Contain("entry 2").And.NotContain("Grace", "diagnostics identify an entry by position, not by personal data");
    }

    [Fact]
    public async Task PostRuns_NoGraphResource_Returns400_WithH14bCode()
    {
        var nonSecret = WithOperatorIntake(new Dictionary<string, string> { ["tenantId"] = "11111111-1111-1111-1111-111111111111" });
        nonSecret.Remove("communicationGraphResource");
        nonSecret["emailGraphResource"] = " ";

        await AssertRejectedBeforeAnySideEffectAsync(nonSecret, "h14b-no-webhook-targets-configured");
    }

    [Theory]
    [InlineData("Model2", "NativeAccount", "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\"}]", "communicationGraphResource")]   // email and group optional for NativeAccount (Model 2 only — T232)
    [InlineData("Model2", "NativeAccount", "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"team@contoso.com\"},{\"firstName\":\"Grace\",\"lastName\":\"Hopper\",\"email\":\"team@contoso.com\"}]", "communicationGraphResource")]   // a shared contact email is fine — nothing is invited for NativeAccount
    [InlineData("Model1", "B2BGuest", "[{\"email\":\"ada@contoso.com\"}]", "emailGraphResource")]   // a guest needs only an email; either Graph resource alone is enough
    public async Task PostRuns_CompleteOperatorIntake_Returns202_AndStoresTheValues(
        string tenancyModel, string identityPreset, string usersJson, string graphResourceKey)
    {
        using var factory = new L2WebApplicationFactory();
        var client = factory.CreateClient();
        var nonSecret = WithOperatorIntake(new Dictionary<string, string> { ["tenantId"] = "11111111-1111-1111-1111-111111111111" });
        nonSecret.Remove("communicationGraphResource");
        nonSecret[graphResourceKey] = "users/comms@contoso.com/messages";
        nonSecret["identityPreset"] = identityPreset;
        nonSecret["usersJson"] = usersJson;
        if (identityPreset == "NativeAccount")
        {
            nonSecret.Remove("environmentSecurityGroupId");
        }

        var response = await client.SendAsync(BuildCreateRunRequest("testcust", nonSecret, tenancyModel));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Repository.CreatedRuns.Single().Parameters.NonSecret.Should().Contain(new Dictionary<string, string>
        {
            ["identityPreset"] = identityPreset,
            ["usersJson"] = usersJson,
            [graphResourceKey] = "users/comms@contoso.com/messages",
            ["exchangePolicyScopeGroupId"] = nonSecret["exchangePolicyScopeGroupId"],
            ["communicationDefaultMailbox"] = nonSecret["communicationDefaultMailbox"],
        });
    }

    [Fact]
    public async Task PostRuns_MoreUsersThanTheCap_Returns400()
    {
        // The list is stored in the Cosmos run document (D15); an unbounded one could exceed the 2 MB item limit.
        var nonSecret = WithOperatorIntake(new Dictionary<string, string> { ["tenantId"] = "11111111-1111-1111-1111-111111111111" });
        nonSecret["usersJson"] = "[" + string.Join(",", Enumerable.Repeat(
            "{\"firstName\":\"A\",\"lastName\":\"B\"}", UserProvisioningIntake.MaxUsers + 1)) + "]";

        await AssertRejectedBeforeAnySideEffectAsync(nonSecret, "userprov-too-many-users");
    }

    [Fact]
    public async Task PostRuns_RunStoreWriteFails_ReleasesTheRunGuard()
    {
        // Found in the T245c review: only an id collision released the guard, so any other run-store failure left the
        // customer blocked until the guard went stale.
        using var factory = new L2WebApplicationFactory();
        var guard = new SpyCustomerRunGuard();
        factory.ReplaceCustomerRunGuard(guard);
        factory.Repository.CreateFailure = new HttpRequestException("run store unavailable");
        var client = factory.CreateClient();

        var send = () => client.SendAsync(BuildValidCreateRunRequest("testcust"));

        await send.Should().ThrowAsync<HttpRequestException>();
        guard.AcquireCalls.Should().ContainSingle();
        guard.ReleaseCalls.Should().ContainSingle().Which.Should().Be(guard.AcquireCalls.Single());
        factory.Enqueuer.Enqueued.Should().BeEmpty();
    }

    private static async Task<string> AssertRejectedBeforeAnySideEffectAsync(
        Dictionary<string, string> nonSecret, string expectedErrorCode)
    {
        using var factory = new L2WebApplicationFactory();
        var registry = new StubRegistryClient();
        factory.ReplaceRegistryClient(registry);
        var guard = new SpyCustomerRunGuard();
        factory.ReplaceCustomerRunGuard(guard);
        var client = factory.CreateClient();

        var response = await client.SendAsync(BuildCreateRunRequest("testcust", nonSecret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        ReadProblemErrorCode(body).Should().Be(expectedErrorCode);
        guard.AcquireCalls.Should().BeEmpty("operator intake is checked before the I5 run guard is acquired");
        registry.LookupCount.Should().Be(0, "operator intake is checked before the REG-07 registry lookup");
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
        return ReadProblemDetail(body);
    }

    /// <summary>
    /// Adds a complete, valid T245c operator intake (H11 preset + users, H14 scope group + Graph resource, H4 mailbox)
    /// to <paramref name="nonSecret"/> — every run must carry it to get past POST /api/runs.
    /// </summary>
    internal static Dictionary<string, string> WithOperatorIntake(Dictionary<string, string> nonSecret)
    {
        // T232: the request builder sends Model1, which takes only B2BGuest — guests need an email and the environment's
        // security group.
        nonSecret.TryAdd("identityPreset", "B2BGuest");
        nonSecret.TryAdd("usersJson", "[{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@contoso.com\"}]");
        nonSecret.TryAdd("environmentSecurityGroupId", "6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b");
        nonSecret.TryAdd("exchangePolicyScopeGroupId", "spaarke-mail-scope@contoso.com");
        nonSecret.TryAdd("communicationGraphResource", "users/comms@contoso.com/messages");
        nonSecret.TryAdd("communicationDefaultMailbox", "comms@contoso.com");
        // T228: required for every model — the customer's own subscription, the model's container type (G19) and the
        // Dataverse environment the operator created (named for TestCustomerId).
        nonSecret.TryAdd("subscriptionId", "abcdef01-2345-6789-abcd-ef0123456789");
        nonSecret.TryAdd("containerTypeId", "8a6ce34c-6055-4681-8f87-2f4f9f921c06");
        nonSecret.TryAdd("dataverseEnvUrl", $"https://spaarke-{TestCustomerId}.crm.dynamics.com/");
        // T229: H0's cost tier + estimate, required for every model.
        nonSecret.TryAdd("tier", "smb");
        nonSecret.TryAdd("estimatedMonthlyUsd", "450");
        return nonSecret;
    }

    // ProblemDetails JSON escapes ' as ', so assert on the parsed detail, not the raw body.
    private static string ReadProblemDetail(string body)
        => System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString() ?? string.Empty;

    // ADR-019: callers branch on the stable errorCode extension, never on detail text.
    private static string ReadProblemErrorCode(string body)
        => System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString() ?? string.Empty;

    private static HttpRequestMessage BuildValidCreateRunRequest(string customerId)
        => BuildCreateRunRequest(customerId, WithOperatorIntake(new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["dataverseEnvUrl"] = $"https://spaarke-{customerId}.crm.dynamics.com/",   // T228: named for THIS customer
        }));

    private static HttpRequestMessage BuildCreateRunRequest(
        string customerId, Dictionary<string, string> nonSecretParameters, string tenancyModel = "Model1")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId,
                environmentId = "env-1",
                tenancyModel,
                profile = tenancyModel == "Model2" ? "customer-owned-model2" : "spaarke-hosted-model2",
                nonSecretParameters,
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });
        return request;
    }

    // -------------------------------------------------------------------------
    // ISH-11 (customer-provisioning-orchestration-r1 Wave 5 punchlist,
    // 2026-08-27) — CreateRun MUST reject invalid tenancyModel × profile
    // pairs at the HTTP surface, mirroring intake.schema.json's allOf logic.
    // A direct-API caller (test harness, retry script) supplying an invalid
    // pair would otherwise reach downstream handlers (H5 tier derivation,
    // H11 user provisioning gate) which fail cryptically.
    //
    // Task 225b (D-12, G6): the only accepted pairs are Model1 ↔
    // spaarke-hosted-model2 and Model2 ↔ customer-owned-model2; the retired
    // shared-tier profile spaarke-hosted-model1-trial is an unknown profile.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Model1", "spaarke-hosted-model2", null)]   // accepted: Spaarke-hosted dedicated stamp
    [InlineData("Model2", "customer-owned-model2", null)]   // accepted: customer-hosted dedicated stamp
    [InlineData("Model1", "customer-owned-model2", "'Model1' MUST pair with 'spaarke-hosted-model2'")]
    [InlineData("Model2", "spaarke-hosted-model2", "'Model2' MUST pair with 'customer-owned-model2'")]
    [InlineData("Model1", "spaarke-hosted-model1-trial", "Invalid profile 'spaarke-hosted-model1-trial'")]   // retired (D-12)
    public async Task PostRuns_TenancyProfilePair_OnlyTheD12PairsAreAccepted(
        string tenancyModel, string profile, string? expectedRefusal)
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
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                    ["subscriptionId"] = "abcdef01-2345-6789-abcd-ef0123456789",
                }),
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        if (expectedRefusal is null)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Accepted,
                $"tenancyModel='{tenancyModel}' × profile='{profile}' is a D-12 pair.");
            factory.Repository.CreatedRuns.Should().ContainSingle();
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"ISH-11 — tenancyModel='{tenancyModel}' × profile='{profile}' is not a valid pair.");
        var body = await response.Content.ReadAsStringAsync();
        ReadProblemErrorCode(body).Should().Be("tenancy-profile-invalid");
        ReadProblemDetail(body).Should().Contain(expectedRefusal).And.Contain(profile,
            "the diagnostic must name the rule and echo the offending profile.");
        factory.Repository.CreatedRuns.Should().BeEmpty();
        factory.Enqueuer.Enqueued.Should().BeEmpty();
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
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                }),
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
    // T228 (owner D4 / Q1; ADR-027) — the operator creates the customer's subscription and Dataverse environment; intake
    // requires both for EVERY tenancy model (ISH-02's Model 1 exemption is gone), plus the container type (G19). Each
    // refusal happens before any Cosmos write or enqueue.
    // -------------------------------------------------------------------------

    private async Task<(HttpStatusCode Status, string Body, L2WebApplicationFactory Factory)> PostT228RunAsync(
        string tenancyModel, Action<Dictionary<string, string>> shape)
    {
        var factory = new L2WebApplicationFactory();
        var nonSecret = WithOperatorIntake(new Dictionary<string, string> { ["tenantId"] = "11111111-1111-1111-1111-111111111111" });
        shape(nonSecret);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId = TestCustomerId,
                environmentId = "env-1",
                tenancyModel,
                profile = tenancyModel == "Model1" ? "spaarke-hosted-model2" : "customer-owned-model2",
                nonSecretParameters = nonSecret,
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });
        var response = await factory.CreateClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), factory);
    }

    [Theory]
    [InlineData("Model1", null)]
    [InlineData("Model2", null)]
    [InlineData("Model1", "   ")]
    [InlineData("Model2", "not-a-guid")]
    [InlineData("Model1", "00000000-0000-0000-0000-000000000000")]
    public async Task PostRuns_WithoutTheCustomersSubscription_Returns400_ForEveryModel(string model, string? subscriptionId)
    {
        var (status, body, factory) = await PostT228RunAsync(model, p =>
        {
            if (subscriptionId is null) p.Remove("subscriptionId"); else p["subscriptionId"] = subscriptionId;
        });
        using (factory)
        {
            status.Should().Be(HttpStatusCode.BadRequest, "no subscription is defaulted or shared for any model (T228)");
            ReadProblemErrorCode(body).Should().Be("subscription-id-required");
            factory.Repository.CreatedRuns.Should().BeEmpty();
            factory.Enqueuer.Enqueued.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("standard")]
    public async Task PostRuns_WithoutAContainerTypeGuid_Returns400(string? containerTypeId)
    {
        var (status, body, factory) = await PostT228RunAsync("Model1", p =>
        {
            if (containerTypeId is null) p.Remove("containerTypeId"); else p["containerTypeId"] = containerTypeId;
        });
        using (factory)
        {
            status.Should().Be(HttpStatusCode.BadRequest, "H0, H4b and H8 need it — fail at intake, not one by one (G19)");
            ReadProblemErrorCode(body).Should().Be("container-type-id-required");
            factory.Repository.CreatedRuns.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://spaarke-testcust.crm.dynamics.com/")]                 // not https
    [InlineData("https://spaarke-testcust.crm.dynamics.com/main.aspx")]       // a path
    [InlineData("https://spaarke-testcust.crm.dynamics.com:8443/")]           // a port
    [InlineData("https://spaarke-testcust.example.com/")]                     // not Dataverse
    [InlineData("https://spaarke-other.crm.dynamics.com/")]                   // another customer's environment
    [InlineData("https://spaarke-testcustx.crm.dynamics.com/")]               // a longer id is another customer
    [InlineData("https://testcust.crm.dynamics.com/")]                        // not the naming rule
    [InlineData("https://spaarke-testcust-dev.crm.dynamics.com/")]            // another environment of this customer
    public async Task PostRuns_ADataverseEnvironmentNotNamedForThisCustomer_Returns400(string? url)
    {
        var (status, body, factory) = await PostT228RunAsync("Model1", p =>
        {
            if (url is null) p.Remove("dataverseEnvUrl"); else p["dataverseEnvUrl"] = url;
        });
        using (factory)
        {
            status.Should().Be(HttpStatusCode.BadRequest, "a run must never adopt an environment that is not this customer's");
            ReadProblemErrorCode(body).Should().Be("dataverse-env-url-invalid");
            factory.Repository.CreatedRuns.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("https://SPAARKE-testcust.crm.dynamics.com", "https://spaarke-testcust.crm.dynamics.com/")]
    [InlineData("https://spaarke-testcust-prod.crm4.dynamics.com/", "https://spaarke-testcust-prod.crm4.dynamics.com/")]
    public async Task PostRuns_TheCustomersEnvironment_IsStoredInCanonicalForm(string url, string stored)
    {
        var (status, _, factory) = await PostT228RunAsync("Model1", p => p["dataverseEnvUrl"] = url);
        using (factory)
        {
            status.Should().Be(HttpStatusCode.Accepted);
            factory.Repository.CreatedRuns.Single().Parameters.NonSecret["dataverseEnvUrl"].Should().Be(stored,
                "H5 compares against and hands on the canonical https://{host}/");
        }
    }

    [Fact]
    public async Task PostRuns_Model2_ValidSubscriptionId_Returns202_AndFlowsToRunParameters()
    {
        var (status, _, factory) = await PostT228RunAsync("Model2", p => p["subscriptionId"] = "abcdef01-2345-6789-abcd-ef0123456789");
        using (factory)
        {
            status.Should().Be(HttpStatusCode.Accepted);
            factory.Repository.CreatedRuns.Single().Parameters.NonSecret["subscriptionId"]
                .Should().Be("abcdef01-2345-6789-abcd-ef0123456789", "H1 / H2a and the rest read it from the run");
        }
    }

    // -------------------------------------------------------------------------
    // T229 (G4) — every run deploys a dedicated stamp, so H0's cost tier + estimate are required for every model and
    // validated with H0's own rules (CostEnvelopeIntake); there is no shared-trial tier and no warnAndProceed waiver.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Model1", null, "450", "quota-cost-envelope-required-missing")]
    [InlineData("Model2", "  ", "450", "quota-cost-envelope-required-missing")]
    [InlineData("Model1", "shared-trial", "450", "quota-cost-envelope-unknown-tier")]   // retired (D-12)
    [InlineData("Model2", "Dedicated", "450", "quota-cost-envelope-unknown-tier")]      // exact case
    [InlineData("Model1", "standard", "450", "quota-cost-envelope-unknown-tier")]
    [InlineData("Model1", "smb", null, "quota-cost-envelope-required-missing")]
    [InlineData("Model2", "smb", "lots", "quota-cost-envelope-unparseable-estimate")]
    [InlineData("Model1", "smb", "-1", "quota-cost-envelope-unparseable-estimate")]
    [InlineData("Model1", "smb", "1,200.50", "quota-cost-envelope-unparseable-estimate")]   // invariant decimal only
    public async Task PostRuns_WithoutAUsableCostTierAndEstimate_Returns400_ForEveryModel(
        string model, string? tier, string? estimate, string errorCode)
    {
        var (status, body, factory) = await PostT228RunAsync(model, p =>
        {
            if (tier is null) p.Remove("tier"); else p["tier"] = tier;
            if (estimate is null) p.Remove("estimatedMonthlyUsd"); else p["estimatedMonthlyUsd"] = estimate;
        });
        using (factory)
        {
            status.Should().Be(HttpStatusCode.BadRequest, "H0 would refuse it — refuse it before anything is written (T229)");
            ReadProblemErrorCode(body).Should().Be(errorCode);
            factory.Repository.CreatedRuns.Should().BeEmpty();
            factory.Enqueuer.Enqueued.Should().BeEmpty();
        }
    }

    // T254 (G37): the OPTIONAL monthly OpenAI spend limit — absent = no limit; present must be usable.
    [Theory]
    [InlineData("0")]          // zero reads as "no limit" in the BFF — omit the key instead
    [InlineData("-5")]
    [InlineData("lots")]
    [InlineData("5.")]
    [InlineData("1,000")]
    [InlineData("1000000.01")] // above the ceiling: a typo of extra digits
    [InlineData(" ")]
    public async Task PostRuns_AnUnusableOpenAiMonthlyLimit_Returns400(string limit)
    {
        var (status, body, factory) = await PostT228RunAsync("Model1", p => p["openAiMonthlyLimitUsd"] = limit);
        using (factory)
        {
            status.Should().Be(HttpStatusCode.BadRequest);
            ReadProblemErrorCode(body).Should().Be("quota-openai-monthly-limit-invalid");
            factory.Repository.CreatedRuns.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("500")]
    [InlineData("1250.75")]
    public async Task PostRuns_AUsableOpenAiMonthlyLimit_IsStoredForH4b(string limit)
    {
        var (status, _, factory) = await PostT228RunAsync("Model2", p => p["openAiMonthlyLimitUsd"] = limit);
        using (factory)
        {
            status.Should().Be(HttpStatusCode.Accepted);
            factory.Repository.CreatedRuns.Single().Parameters.NonSecret["openAiMonthlyLimitUsd"].Should().Be(limit);
        }
    }

    [Fact]
    public async Task PostRuns_WithoutAnOpenAiMonthlyLimit_IsAccepted_NoLimitIsTheDefault()
    {
        var (status, _, factory) = await PostT228RunAsync("Model1", p => p.Remove("openAiMonthlyLimitUsd"));
        using (factory)
        {
            status.Should().Be(HttpStatusCode.Accepted);
            factory.Repository.CreatedRuns.Single().Parameters.NonSecret.Should().NotContainKey("openAiMonthlyLimitUsd");
        }
    }

    [Fact]
    public async Task PostRuns_TheRetiredCostEnvelopePolicy_IsAnUnknownParameter()
    {
        var (status, body, factory) = await PostT228RunAsync("Model1", p => p["costEnvelopePolicy"] = "warnAndProceed");
        using (factory)
        {
            status.Should().Be(HttpStatusCode.BadRequest, "the waiver key is retired, so it is an unknown intake key (T229)");
            ReadProblemDetail(body).Should().Contain("costEnvelopePolicy");
            factory.Repository.CreatedRuns.Should().BeEmpty();
        }
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    // ISH-01: present but whitespace-only → still fail-fast.
                    ["tenantId"] = "   ",
                }),
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
                CustomerId: "other",
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                }),
            }),
        };
        AttachAuth(request, roles: new[] { "Operator" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "REG-07 — cross-customer environmentId must fail-fast with 400.");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("REG-07");
        ReadProblemDetail(body).Should().Contain("belongs to customer 'other'");
        ReadProblemErrorCode(body).Should().Be("registry-customer-mismatch");
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                }),
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    ["tenantId"] = "11111111-1111-1111-1111-111111111111",
                }),
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
                tenancyModel = "Model1",
                profile = "spaarke-hosted-model2",
                nonSecretParameters = WithOperatorIntake(new Dictionary<string, string>
                {
                    ["tenantId"] = expectedTenantId,
                }),
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

    /// <summary>When set, <see cref="CreateRunAsync"/> throws it (a run-store failure other than an id collision).</summary>
    public Exception? CreateFailure { get; set; }

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
        if (CreateFailure is not null)
        {
            throw CreateFailure;
        }
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
    public int LookupCount { get; private set; }

    public Task<Sprk.Provisioning.ControlPlane.Registry.DataverseEnvironmentRegistrySnapshot?> LookupByEnvironmentIdAsync(
        string environmentId, CancellationToken cancellationToken)
    {
        LookupCount++;
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
