// -----------------------------------------------------------------------------
// TenantContainerResolverEndpointTests.cs
//
// Unit tests for GET /api/diagnostics/tenant-container-resolver. Exercises the
// endpoint handler directly with a fake HttpContext + hand-rolled resolver
// (parity with ConsentCallbackEndpointTests; ADR-038 — no mocking framework).
//
// Originally authored by customer-provisioning-orchestration-r1 (G-8 Batch 6) for
// the L2 SpeContainerResolverInvariantProbe contract. EXTENDED by
// unified-access-control-r2 task 081 with the operator gate, and CHANGED in two
// ways that are deliberate, not incidental:
//
//   1. The JWT-tid-fallback test is GONE because the behaviour is gone. tenantId is
//      now required in the query string; for the only callers that reach that line —
//      allow-listed operator service principals — the tid claim is the CONTROL-PLANE
//      tenant, never the customer tenant being probed, so falling back to it answered
//      a different question than the one asked.
//
//   2. FakeResolver no longer answers for every tenant. It THROWS on a tenant it was
//      not explicitly configured for. The previous version returned one canned result
//      regardless of the tenant argument, which meant a handler that resolved the
//      WRONG tenant would still have produced a green test — a double that encodes
//      what a call is FOR cannot detect a change in what it DOES.
//
// Coverage:
//   * operator gate: allow-listed app-only caller passes; user principals, unlisted
//     apps, indeterminate tokens, empty allow-list and ABSENT allow-list all 403
//   * the trap: a user-delegated token whose appid IS allow-listed is still denied
//   * denial is uniform (no enumeration oracle) and precedes every resolver call
//   * probe contract: 200 body shape, tenant echo, live resolver call
//   * error mapping: missing tenantId → 400 · TenantNotServed → 400 ·
//     ContainerNotConfigured/TenantScopeNotPinned → 500 · resolver throw → 500
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Endpoints.Diagnostics;
using Xunit;

namespace Sprk.Bff.Api.Tests.Diagnostics;

public sealed class TenantContainerResolverEndpointTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string ForeignTenantId = "99999999-8888-7777-6666-555555555555";
    private const string ContainerId = "b!AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abcdef";

    private const string OperatorAppId = "cccccccc-9999-0000-1111-222222222222";
    private const string UnlistedAppId = "eeeeeeee-7777-8888-9999-000000000000";
    private const string ServicePrincipalObjectId = "bbbbbbbb-5555-6666-7777-888888888888";
    private const string UserObjectId = "aaaaaaaa-1111-2222-3333-444444444444";

    private const string ConfigKey = TenantContainerResolverEndpoint.AllowedOperatorAppIdsConfigKey;

    // =========================================================== the operator gate

    [Fact]
    public async Task AllowListedOperator_ResolvingAForeignTenant_Returns200WithTenantEchoed()
    {
        // This IS the L2 H13 I4 probe contract: one tenant's machine identity asking about
        // ANOTHER tenant's resolution. If this test ever goes red, the probe is broken.
        var resolver = FakeResolver.For(ForeignTenantId, Success(ForeignTenantId));

        var result = await Invoke(
            resolver,
            Config(OperatorAppId),
            NewHttpContext(ForeignTenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status200OK);

        var response = result.Should().BeAssignableTo<IValueHttpResult>()
            .Which.Value.Should().BeOfType<TenantContainerResolverResponse>().Subject;
        response.TenantId.Should().Be(
            ForeignTenantId, "the probe compares the echo ordinally against its request");
        response.ContainerId.Should().Be(ContainerId).And.MatchRegex(@"^b![A-Za-z0-9_\-]{20,}$");
        response.ResolvedFromLiteral.Should().BeFalse("true is a CATASTROPHIC verdict to the probe");

        resolver.ResolvedTenantIds.Should().ContainSingle().Which.Should().Be(
            ForeignTenantId, "the handler must perform a LIVE resolver call on the REQUESTED tenant");
    }

    [Fact]
    public async Task TheTrap_UserDelegatedCallerWhoseAppIdIsAllowListed_IsDenied()
    {
        // The single most important test in this file. appid/azp is present in user-delegated
        // tokens too, so an allow-list keyed on appid ALONE would admit a human signed into the
        // operator's app registration. Only the conjunction with a positive app-only
        // determination denies this caller.
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(
            resolver,
            Config(OperatorAppId),                       // its appid IS on the list
            NewHttpContext(TenantId, CallerShape.UserHoldingOperatorAppId));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        resolver.ResolvedTenantIds.Should().BeEmpty("a denied caller must never reach the resolver");
    }

    [Theory]
    [InlineData(CallerShape.OrdinaryUser)]
    [InlineData(CallerShape.UserHoldingOperatorAppId)]
    [InlineData(CallerShape.UnlistedApplication)]
    [InlineData(CallerShape.IndeterminateToken)]
    [InlineData(CallerShape.Anonymous)]
    public async Task NonOperatorCallers_AreDenied_AndNeverReachTheResolver(CallerShape shape)
    {
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(resolver, Config(OperatorAppId), NewHttpContext(TenantId, shape));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        resolver.ResolvedTenantIds.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyAllowList_DeniesEvenAnOtherwiseValidOperator()
    {
        // "Empty means allow all" is the classic failure of this pattern.
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(
            resolver, Config(), NewHttpContext(TenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        resolver.ResolvedTenantIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsentAllowList_DeniesEveryone_SoAFreshEnvironmentDoesNotFailOpen()
    {
        // Distinct from the empty case: the configuration key does not exist at all, which is the
        // state of a freshly provisioned environment before anyone has configured it.
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(
            resolver, ConfigWithoutTheKey(), NewHttpContext(TenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        resolver.ResolvedTenantIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AllowList_AsCommaSeparatedScalar_IsHonoured()
    {
        // Flat App Service app settings are how this is most often configured by hand.
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(
            resolver,
            ConfigScalar($"{UnlistedAppId}, {OperatorAppId}"),
            NewHttpContext(TenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task AllowList_MatchIsCaseInsensitive_BecauseGuidCasingIsNotSemantic()
    {
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(
            resolver,
            Config(OperatorAppId.ToUpperInvariant()),
            NewHttpContext(TenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status200OK);
    }

    // ================================================ the enumeration oracle is closed

    [Fact]
    public async Task DeniedCallers_CannotDistinguishAServedTenantFromAnUnservedOne()
    {
        // The oracle was: "tenant not served" answers 400 while a served tenant answers 200, so an
        // authenticated caller could enumerate this stamp's customers from STATUS CODES ALONE.
        // Denial now precedes the resolver entirely, so both cases are byte-identical.
        var served = FakeResolver.For(TenantId, Success(TenantId));
        var notServed = FakeResolver.For(
            ForeignTenantId,
            TenantContainerResolutionResult.Failure(
                TenantContainerResolutionFailureCode.TenantNotServed, "not served by this stamp"));

        var againstServed = await Invoke(
            served, Config(OperatorAppId), NewHttpContext(TenantId, CallerShape.OrdinaryUser));
        var againstNotServed = await Invoke(
            notServed, Config(OperatorAppId), NewHttpContext(ForeignTenantId, CallerShape.OrdinaryUser));

        DenialFingerprint(againstServed).Should().Be(
            DenialFingerprint(againstNotServed),
            "a denied caller must learn nothing about which tenants this stamp serves");

        served.ResolvedTenantIds.Should().BeEmpty();
        notServed.ResolvedTenantIds.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryDenialReason_ProducesTheIdenticalResponse()
    {
        // Uniform denial: a rejected caller must not be able to tell WHY it was rejected — that
        // would leak whether the allow-list is configured, or how its own token was classified.
        var fingerprints = new List<(string Label, string Fingerprint)>();

        foreach (var shape in new[]
                 {
                     CallerShape.OrdinaryUser, CallerShape.UnlistedApplication,
                     CallerShape.IndeterminateToken, CallerShape.Anonymous,
                 })
        {
            var result = await Invoke(
                FakeResolver.For(TenantId, Success(TenantId)),
                Config(OperatorAppId),
                NewHttpContext(TenantId, shape));
            fingerprints.Add((shape.ToString(), DenialFingerprint(result)));
        }

        // ...and the two allow-list-shaped denials, which must look identical to the above.
        fingerprints.Add(("EmptyAllowList", DenialFingerprint(await Invoke(
            FakeResolver.For(TenantId, Success(TenantId)), Config(),
            NewHttpContext(TenantId, CallerShape.AllowListedOperator)))));
        fingerprints.Add(("AbsentAllowList", DenialFingerprint(await Invoke(
            FakeResolver.For(TenantId, Success(TenantId)), ConfigWithoutTheKey(),
            NewHttpContext(TenantId, CallerShape.AllowListedOperator)))));

        var reference = fingerprints[0].Fingerprint;
        foreach (var (label, fingerprint) in fingerprints)
        {
            fingerprint.Should().Be(
                reference,
                "denial '{0}' must be indistinguishable from every other denial reason", label);
        }
    }

    // ============================================ probe contract + error mapping

    [Fact]
    public async Task NoTenantIdInQuery_Returns400_WithNoResolverCall_AndNoTidFallback()
    {
        // The caller is a permitted operator carrying a tid claim. The handler must STILL refuse:
        // there is no ambient-tenant resolution and no inference from the caller's token.
        var resolver = FakeResolver.For(TenantId, Success(TenantId));

        var result = await Invoke(
            resolver,
            Config(OperatorAppId),
            NewHttpContext(queryTenantId: null, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
        resolver.ResolvedTenantIds.Should().BeEmpty(
            "no ambient/default-tenant resolution may be attempted (§4D I1/I4), and the operator's " +
            "own tid claim is the control-plane tenant, not a customer tenant");
    }

    [Fact]
    public async Task TenantNotServed_Returns400_ForAPermittedOperator()
    {
        // Scoped to PERMITTED callers on purpose: distinguishing served from not-served is the
        // probe's entire reason for existing, so this split must survive for operators.
        var resolver = FakeResolver.For(
            ForeignTenantId,
            TenantContainerResolutionResult.Failure(
                TenantContainerResolutionFailureCode.TenantNotServed, "not the tenant this stamp serves"));

        var result = await Invoke(
            resolver, Config(OperatorAppId),
            NewHttpContext(ForeignTenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData(TenantContainerResolutionFailureCode.ContainerNotConfigured)]
    [InlineData(TenantContainerResolutionFailureCode.TenantScopeNotPinned)]
    public async Task DeploymentMisconfiguration_Returns500WithDiagnostic(
        TenantContainerResolutionFailureCode failureCode)
    {
        var resolver = FakeResolver.For(
            TenantId,
            TenantContainerResolutionResult.Failure(failureCode, "deployment misconfiguration diagnostic"));

        var result = await Invoke(
            resolver, Config(OperatorAppId), NewHttpContext(TenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ResolverThrowsUnexpectedly_Returns500Problem_NotUnhandledException()
    {
        var resolver = FakeResolver.Throwing(new InvalidOperationException("boom"));

        var result = await Invoke(
            resolver, Config(OperatorAppId), NewHttpContext(TenantId, CallerShape.AllowListedOperator));

        StatusOf(result).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void MapTenantContainerResolverEndpoint_MapsGetOnProbePathWithAuthorizationMetadata()
    {
        // RequireAuthorization() attaches IAuthorizeData, so the standard JWT-bearer middleware 401s
        // missing/invalid tokens. Note this establishes only THAT a caller is authenticated — the
        // operator gate inside the handler is what establishes WHICH tenant they may ask about.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });
        using var app = builder.Build();

        ((IEndpointRouteBuilder)app).MapTenantContainerResolverEndpoint();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Should().ContainSingle().Subject;

        endpoint.RoutePattern.RawText.Should().Be("/api/diagnostics/tenant-container-resolver");
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
            .Should().ContainSingle().Which.Should().Be("GET", "the probe issues a GET");
        endpoint.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Should().BeEmpty();
    }

    // ---------------------------------------------------------------- helpers

    private static Task<IResult> Invoke(
        ITenantContainerResolver resolver, IConfiguration configuration, HttpContext context)
        => TenantContainerResolverEndpoint.HandleAsync(
            context, resolver, configuration, NullLoggerFactory.Instance, CancellationToken.None);

    private static int? StatusOf(IResult result)
        => result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode;

    private static ProblemDetails ProblemOf(IResult result)
        => result.Should().BeAssignableTo<IValueHttpResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;

    /// <summary>
    /// Everything about a denial response that a CALLER could use to tell two denials apart.
    ///
    /// <para><c>correlationId</c> is deliberately excluded: it is the per-request trace id and MUST
    /// differ between requests — it is what lets an operator find the real reason in the server log,
    /// and it carries no information about the tenant or about which condition fired. The extension
    /// KEY SET is included, so adding a new, reason-revealing extension to one denial path would
    /// still break this test.</para>
    /// </summary>
    private static string DenialFingerprint(IResult result)
    {
        var problem = ProblemOf(result);
        var extensionKeys = string.Join(
            ",", problem.Extensions.Keys.Where(k => k != "correlationId").OrderBy(k => k));
        var errorCode = problem.Extensions.TryGetValue("errorCode", out var code) ? code : null;

        return $"status={problem.Status}|title={problem.Title}|detail={problem.Detail}|" +
               $"errorCode={errorCode}|extensionKeys={extensionKeys}";
    }

    private static TenantContainerResolutionResult Success(string tenantId)
        => TenantContainerResolutionResult.Success(
            new TenantContainerResolution(tenantId, ContainerId, "options", ResolvedFromLiteral: false));

    /// <summary>Allow-list in configuration-array form (JSON array / <c>__0</c> env vars).</summary>
    private static IConfiguration Config(params string[] allowedAppIds)
    {
        var values = allowedAppIds
            .Select((id, i) => new KeyValuePair<string, string?>($"{ConfigKey}:{i}", id));
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>Allow-list as one flat comma-separated app setting.</summary>
    private static IConfiguration ConfigScalar(string commaSeparated)
        => new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(ConfigKey, commaSeparated)])
            .Build();

    /// <summary>
    /// Configuration in which the allow-list key is entirely ABSENT — a fresh environment. The
    /// unrelated key is present so the difference from <see cref="Config()"/> is genuinely
    /// "key missing" rather than "configuration object empty".
    /// </summary>
    private static IConfiguration ConfigWithoutTheKey()
        => new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Unrelated:Setting", "value")])
            .Build();

    /// <summary>The caller shapes this route must distinguish. Named, so no test asserts on an accident.</summary>
    public enum CallerShape
    {
        /// <summary>App-only token (sub == oid) whose appid is on the allow-list.</summary>
        AllowListedOperator,

        /// <summary>App-only token whose appid is NOT on the allow-list.</summary>
        UnlistedApplication,

        /// <summary>Ordinary user-delegated token.</summary>
        OrdinaryUser,

        /// <summary>User-delegated token whose appid IS on the allow-list — the trap.</summary>
        UserHoldingOperatorAppId,

        /// <summary>Authenticated but carrying no determinative claims.</summary>
        IndeterminateToken,

        /// <summary>No authenticated identity at all.</summary>
        Anonymous,
    }

    private static DefaultHttpContext NewHttpContext(string? queryTenantId, CallerShape shape)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };

        if (queryTenantId is not null)
        {
            ctx.Request.QueryString = new QueryString($"?tenantId={Uri.EscapeDataString(queryTenantId)}");
        }

        // Every non-anonymous shape carries a tid claim, so that "no tenantId in query" can never be
        // rescued by a fallback — if one were reintroduced, NoTenantIdInQuery_* would go red.
        Claim[]? claims = shape switch
        {
            CallerShape.AllowListedOperator =>
            [
                new Claim("appid", OperatorAppId),
                new Claim("oid", ServicePrincipalObjectId),
                new Claim("sub", ServicePrincipalObjectId),
                new Claim("tid", TenantId),
            ],
            CallerShape.UnlistedApplication =>
            [
                new Claim("appid", UnlistedAppId),
                new Claim("oid", ServicePrincipalObjectId),
                new Claim("sub", ServicePrincipalObjectId),
                new Claim("tid", TenantId),
            ],
            CallerShape.OrdinaryUser =>
            [
                new Claim("appid", UnlistedAppId),
                new Claim("scp", "user_impersonation"),
                new Claim("oid", UserObjectId),
                new Claim("sub", "pairwise-subject-not-an-object-id"),
                new Claim("tid", TenantId),
            ],
            CallerShape.UserHoldingOperatorAppId =>
            [
                new Claim("appid", OperatorAppId),
                new Claim("scp", "user_impersonation"),
                new Claim("oid", UserObjectId),
                new Claim("sub", "pairwise-subject-not-an-object-id"),
                new Claim("tid", TenantId),
            ],
            CallerShape.IndeterminateToken =>
            [
                new Claim("appid", OperatorAppId),
                new Claim("tid", TenantId),
            ],
            CallerShape.Anonymous => null,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "unmodelled caller shape"),
        };

        if (claims is not null)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestJwt"));
        }

        return ctx;
    }

    /// <summary>
    /// Hand-rolled <see cref="ITenantContainerResolver"/> (ADR-038 — no mocking framework).
    ///
    /// <para><b>It THROWS on a tenant it was not configured for, by design.</b> A double that answers
    /// every input with one canned result cannot distinguish "the handler resolved the tenant I asked
    /// about" from "the handler resolved something else" — the test stays green either way. Modelling
    /// only the expected tenant makes a wrong-tenant resolution a loud failure instead of a silent
    /// pass.</para>
    /// </summary>
    private sealed class FakeResolver : ITenantContainerResolver
    {
        private readonly Dictionary<string, TenantContainerResolutionResult> _byTenant =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Exception? _exception;

        private FakeResolver(Exception? exception = null) => _exception = exception;

        public static FakeResolver For(string tenantId, TenantContainerResolutionResult result)
        {
            var resolver = new FakeResolver();
            resolver._byTenant[tenantId] = result;
            return resolver;
        }

        public static FakeResolver Throwing(Exception exception) => new(exception);

        /// <summary>Every tenant the handler actually asked about, in order.</summary>
        public List<string> ResolvedTenantIds { get; } = [];

        public Task<TenantContainerResolutionResult> ResolveAsync(
            string tenantId, CancellationToken cancellationToken)
        {
            ResolvedTenantIds.Add(tenantId);

            if (_exception is not null)
            {
                throw _exception;
            }

            if (!_byTenant.TryGetValue(tenantId, out var result))
            {
                throw new InvalidOperationException(
                    $"FakeResolver was asked to resolve UNMODELLED tenant '{tenantId}'. Configured: " +
                    $"[{string.Join(", ", _byTenant.Keys)}]. The handler resolved a tenant this test " +
                    "did not intend — that is the defect, not a fixture gap.");
            }

            return Task.FromResult(result);
        }
    }
}
