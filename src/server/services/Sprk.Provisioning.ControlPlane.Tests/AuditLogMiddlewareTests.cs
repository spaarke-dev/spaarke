// -----------------------------------------------------------------------------
// AuditLogMiddlewareTests.cs
//
// L2 CONTROL-PLANE AuditLogMiddleware unit tests (task 039 step 6).
//
// PURPOSE: Prove NFR-11 acceptance criteria at the middleware level, without
//          bringing up a live App Insights pipeline:
//
//            #2 Mutating POST with a valid Operator JWT -> AuditableAction
//               log record with Method + Path + StatusCode + ActorTid +
//               ActorOid + ActorAppRoles + TraceId.
//            #3 GET request -> NO AuditableAction record (non-mutating skip).
//            #4 Unauthenticated POST that fails at 401 -> AuditableAction
//               record IS emitted with StatusCode=401 and empty actor claims
//               (auth failures are audit-worthy).
//            #6 Negative: no request/response body content read (verified by
//               construction — middleware never touches the body).
//
// ADR-038 alignment:
//   - Unit-level test with in-process HTTP context; no live App Insights,
//     no Azure resources. KEEP path: tests/unit/ (or the project-scoped
//     equivalent) — mirrors ServiceBusSmokeTests' deterministic MessageId
//     unit tests.
//   - No `Mock<HttpMessageHandler>` (banned per ADR-038 §5).
//   - `TestLogger<T>` is a small local `ILogger<T>` implementation that
//     records emitted messages + structured properties into an in-memory list
//     — the ADR-038-friendly alternative to Moq'ing ILogger (which xUnit +
//     Microsoft docs both discourage for ILogger extensions).
//
// NOT a smoke test — no env-guard. Runs unconditionally in the CI unit suite.
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sprk.Provisioning.ControlPlane.Middleware;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests;

/// <summary>
/// Unit tests over <see cref="AuditLogMiddleware"/>. Verifies verb-scoping,
/// claim extraction, and status-code capture per POML acceptance criteria
/// #2-#4 + #6.
/// </summary>
public sealed class AuditLogMiddlewareTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private const string TenantIdClaimUri = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ObjectIdClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string SampleTenantId = "11111111-1111-1111-1111-111111111111";
    private const string SampleObjectId = "22222222-2222-2222-2222-222222222222";

    private static ClaimsPrincipal BuildOperatorPrincipal(
        string? tenantId = SampleTenantId,
        string? objectId = SampleObjectId,
        params string[] roles)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            claims.Add(new Claim(TenantIdClaimUri, tenantId));
        }
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            claims.Add(new Claim(ObjectIdClaimUri, objectId));
        }
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        var identity = new ClaimsIdentity(claims, authenticationType: "TestBearer");
        return new ClaimsPrincipal(identity);
    }

    private static DefaultHttpContext BuildHttpContext(
        string method,
        string path,
        ClaimsPrincipal? user = null,
        int responseStatusCode = StatusCodes.Status200OK)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.TraceIdentifier = "trace-" + Guid.NewGuid().ToString("N");
        if (user is not null)
        {
            context.User = user;
        }
        context.Response.StatusCode = responseStatusCode;
        return context;
    }

    private static (AuditLogMiddleware Middleware, TestLogger<AuditLogMiddleware> Logger, int NextCallCount) InvokeMiddleware(
        HttpContext context,
        Func<HttpContext, Task>? next = null)
    {
        var logger = new TestLogger<AuditLogMiddleware>();
        var invocationCount = 0;
        Task Next(HttpContext ctx)
        {
            invocationCount++;
            return next?.Invoke(ctx) ?? Task.CompletedTask;
        }
        var middleware = new AuditLogMiddleware(Next, logger);
        middleware.InvokeAsync(context).GetAwaiter().GetResult();
        return (middleware, logger, invocationCount);
    }

    // ------------------------------------------------------------------
    // POML acceptance #3 — GET request does NOT emit
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/ping")]
    [InlineData("HEAD", "/api/runs/abc")]
    [InlineData("OPTIONS", "/api/runs")]
    public async Task GetRequest_DoesNotEmit_AuditableActionRecord(string method, string path)
    {
        var context = BuildHttpContext(method, path);
        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        logger.Records.Should().BeEmpty(
            "non-mutating verbs (GET/HEAD/OPTIONS) skip the audit emitter per POML acceptance #3.");
    }

    // ------------------------------------------------------------------
    // POML acceptance #2 — Mutating POST with Operator JWT emits full record
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("POST", "/api/runs")]
    [InlineData("PUT", "/api/runs/abc")]
    [InlineData("PATCH", "/api/runs/abc")]
    [InlineData("DELETE", "/api/runs/abc")]
    public async Task MutatingRequest_WithOperatorPrincipal_EmitsFullAuditRecord(string method, string path)
    {
        var user = BuildOperatorPrincipal(roles: new[] { "Operator" });
        var context = BuildHttpContext(method, path, user, StatusCodes.Status202Accepted);
        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = logger.Records.Should().ContainSingle(
            "exactly one AuditableAction record is emitted per mutating request.").Subject;

        record.LogLevel.Should().Be(LogLevel.Information);
        record.Message.Should().StartWith("AuditableAction: ",
            "Kusto queries pivot on the 'AuditableAction' prefix — do not change without a coordinated update.");
        record.Properties.Should().ContainKey("Method").WhoseValue.Should().Be(method);
        record.Properties.Should().ContainKey("Path").WhoseValue.Should().Be(path);
        record.Properties.Should().ContainKey("StatusCode").WhoseValue.Should().Be(StatusCodes.Status202Accepted);
        record.Properties.Should().ContainKey("ActorTid").WhoseValue.Should().Be(SampleTenantId);
        record.Properties.Should().ContainKey("ActorOid").WhoseValue.Should().Be(SampleObjectId);
        record.Properties.Should().ContainKey("ActorAppRoles").WhoseValue.Should().Be("Operator");
        record.Properties.Should().ContainKey("RequestId").WhoseValue.Should().Be(context.TraceIdentifier);
        record.Properties.Should().ContainKey("TraceId"); // may be empty string if OTel is unwired — that's OK
    }

    // ------------------------------------------------------------------
    // POML acceptance #4 — Unauthenticated POST 401 STILL emits (nulls for actor)
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnauthenticatedPost_ThatShortCircuitsAs401_StillEmits_WithEmptyActorClaims()
    {
        // Anonymous user — no claims, no identity marked authenticated. Simulates
        // the pipeline state at AuditLogMiddleware WHEN UseAuthorization()
        // downstream is about to short-circuit with 401.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var context = BuildHttpContext("POST", "/api/runs", anonymous);

        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(
            ctx =>
            {
                // Downstream UseAuthorization simulated as 401 short-circuit.
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        var record = logger.Records.Should().ContainSingle().Subject;
        record.Properties["StatusCode"].Should().Be(StatusCodes.Status401Unauthorized,
            "the audit record MUST reflect the final response status — including 401 auth-failure short-circuits (POML acceptance #4).");
        record.Properties["ActorTid"].Should().Be(string.Empty,
            "unauthenticated principal has no tid claim — recorded as empty, not omitted (queryable as 'no operator identity').");
        record.Properties["ActorOid"].Should().Be(string.Empty);
        record.Properties["ActorAppRoles"].Should().Be(string.Empty);
    }

    // ------------------------------------------------------------------
    // Auth with multiple app roles — Operator + Reader
    // ------------------------------------------------------------------

    [Fact]
    public async Task MutatingRequest_WithMultipleRoles_JoinsRolesCommaSeparated()
    {
        var user = BuildOperatorPrincipal(roles: new[] { "Operator", "Reader" });
        var context = BuildHttpContext("POST", "/api/runs", user);

        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = logger.Records.Should().ContainSingle().Subject;
        // Order stability: we emit whatever ClaimsPrincipal returns them in;
        // the important property is BOTH roles are present in the CSV.
        var rolesCsv = record.Properties["ActorAppRoles"] as string;
        rolesCsv.Should().NotBeNull("ActorAppRoles is always emitted as a string (empty when no roles).");
        var roles = rolesCsv!.Split(',');
        roles.Should().Contain("Operator").And.Contain("Reader");
    }

    // ------------------------------------------------------------------
    // Auth with raw `roles` claim (MapInboundClaims=false path)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MutatingRequest_WithRawRolesClaim_ExtractsRolesFromShortForm()
    {
        // Microsoft.Identity.Web v3+ defaults MapInboundClaims=false — the JWT
        // `roles` array survives as a `roles`-named claim rather than being
        // mapped onto ClaimTypes.Role. AuditLogMiddleware.ExtractAppRoles
        // reads BOTH forms.
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(TenantIdClaimUri, SampleTenantId),
            new Claim(ObjectIdClaimUri, SampleObjectId),
            new Claim("roles", "Operator"),
        }, authenticationType: "TestBearer");
        var user = new ClaimsPrincipal(identity);
        var context = BuildHttpContext("POST", "/api/runs", user);

        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = logger.Records.Should().ContainSingle().Subject;
        record.Properties["ActorAppRoles"].Should().Be("Operator",
            "raw `roles` claim (short-form JWT) is a first-class read path — Microsoft.Identity.Web v3+ default.");
    }

    // ------------------------------------------------------------------
    // Short-form tid/oid claims (test tokens; not the WS-* long URI)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MutatingRequest_WithShortFormTidOidClaims_ExtractsCorrectly()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tid", SampleTenantId),
            new Claim("oid", SampleObjectId),
            new Claim(ClaimTypes.Role, "Operator"),
        }, authenticationType: "TestBearer");
        var user = new ClaimsPrincipal(identity);
        var context = BuildHttpContext("POST", "/api/runs", user);

        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var record = logger.Records.Should().ContainSingle().Subject;
        record.Properties["ActorTid"].Should().Be(SampleTenantId,
            "short-form `tid` claim (test-token convention) MUST be read as a fallback when the WS-* long URI is absent.");
        record.Properties["ActorOid"].Should().Be(SampleObjectId);
    }

    // ------------------------------------------------------------------
    // Downstream throws -> audit record still emits (finally-block) AND
    // captures StatusCode=500 (not default 200). Regression coverage for the
    // Kestrel-timing subtlety: Kestrel writes the 500 to the socket only
    // AFTER the middleware chain unwinds, so at the `finally` moment
    // `Response.StatusCode` is still whatever the pipeline left it as.
    // AuditLogMiddleware's catch-block MUST assign 500 before re-throwing so
    // the audit record reflects the failure.
    // ------------------------------------------------------------------

    [Fact]
    public async Task MutatingRequest_WhenDownstreamThrows_EmitsAuditRecord_With500Status()
    {
        var user = BuildOperatorPrincipal(roles: new[] { "Operator" });
        // DO NOT pre-seed responseStatusCode — leaving it at the framework
        // default (200 OK) is what makes this test faithful to real Kestrel
        // behavior. The middleware is responsible for assigning 500 in the
        // catch-block; without that assignment the audit record would
        // misreport an unhandled exception as a 200 success (compliance-red).
        var context = BuildHttpContext("POST", "/api/runs", user);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
            "sanity: DefaultHttpContext initialises StatusCode=200 pre-pipeline; this test relies on that default to prove the catch-block assigns 500 correctly.");

        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(
            _ => throw new InvalidOperationException("simulated handler failure"),
            logger);

        Func<Task> act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "middleware MUST propagate downstream exceptions — not swallow them.");
        var record = logger.Records.Should().ContainSingle(
            "audit record MUST emit via finally-block even when downstream throws.").Subject;
        record.Properties["StatusCode"].Should().Be(StatusCodes.Status500InternalServerError,
            "unhandled downstream exception MUST be audited as 500, not the pipeline-default 200 (NFR-11 compliance query correctness).");
    }

    [Fact]
    public async Task MutatingRequest_WhenDownstreamThrowsAfterSettingStatus_PreservesUpstreamStatus()
    {
        // Upstream exception-handler middleware may set the status code before
        // AuditLogMiddleware's catch runs. In that case the catch MUST NOT
        // overwrite — preserving whatever the exception-handler chose (e.g.,
        // 502 Bad Gateway for a mapped upstream failure).
        var user = BuildOperatorPrincipal(roles: new[] { "Operator" });
        var context = BuildHttpContext("POST", "/api/runs", user);
        var logger = new TestLogger<AuditLogMiddleware>();
        var middleware = new AuditLogMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
                throw new InvalidOperationException("simulated upstream failure with explicit status");
            },
            logger);

        Func<Task> act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var record = logger.Records.Should().ContainSingle().Subject;
        record.Properties["StatusCode"].Should().Be(StatusCodes.Status502BadGateway,
            "the catch-block MUST NOT overwrite an explicitly-set non-200 status code — that would erase upstream error-mapping decisions.");
    }

    // ------------------------------------------------------------------
    // Verb-scoping helper — internal method
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("post", true)] // case-insensitive per RFC 9110 defensive read
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("", false)]
    [InlineData("TRACE", false)] // uncommon but definitionally not mutating
    public void IsMutatingMethod_ClassifiesVerbsCorrectly(string method, bool expected)
    {
        AuditLogMiddleware.IsMutatingMethod(method).Should().Be(expected);
    }
}

// -----------------------------------------------------------------------------
// TestLogger<T> — small in-memory ILogger<T> impl for unit tests.
//
// ADR-038-friendly alternative to Moq'ing ILogger (which is discouraged by
// Microsoft docs + xUnit). Captures message + structured properties + level +
// exception. Local to this test project; do NOT promote to a shared test
// helper without a spec-scoped need.
// -----------------------------------------------------------------------------

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<LogRecord> Records { get; } = new();

    IDisposable? ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
        {
            foreach (var kvp in kvps)
            {
                // The last entry is always "{OriginalFormat}" — the template
                // itself. Skip it; the callers assert on the resolved values
                // under the named keys.
                if (kvp.Key == "{OriginalFormat}")
                {
                    continue;
                }
                properties[kvp.Key] = kvp.Value;
            }
        }
        Records.Add(new LogRecord(logLevel, eventId, message, properties, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

internal sealed record LogRecord(
    LogLevel LogLevel,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);
