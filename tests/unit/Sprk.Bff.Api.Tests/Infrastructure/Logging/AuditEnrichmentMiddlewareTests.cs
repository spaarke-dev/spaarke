// -----------------------------------------------------------------------------
// AuditEnrichmentMiddlewareTests.cs
//
// unified-access-control-r2 task 081. This middleware had NO tests when task 081
// rewired it onto Spaarke.Core.Auth.CallerIdentity and deleted its four private
// claim readers. Behaviour parity was argued in a comment and proved by nothing:
// swapping ObjectId for TenantId in the scope dictionary would compile, pass the
// whole suite, and silently corrupt the audit trail customers pipe into Sentinel.
//
// These tests pin the five scope fields (oid / appid / obo / tenantId /
// correlationId) to the values the PREVIOUS private helpers produced, so the
// promotion is verified rather than asserted.
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Logging;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Logging;

public sealed class AuditEnrichmentMiddlewareTests
{
    private const string UserObjectId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string ServicePrincipalObjectId = "bbbbbbbb-5555-6666-7777-888888888888";
    private const string AppId = "cccccccc-9999-0000-1111-222222222222";
    private const string TenantId = "dddddddd-3333-4444-5555-666666666666";

    [Fact]
    public async Task UserDelegatedToken_EnrichesScopeWithOidAppIdTenantAndObo()
    {
        var (scopes, next) = await InvokeAsync(Authenticated(
            ("oid", UserObjectId),
            ("appid", AppId),
            ("tid", TenantId),
            ("scp", "user_impersonation"),
            ("sub", "pairwise-subject-not-an-object-id")));

        next.Should().BeTrue("the middleware must always call the next delegate");

        var scope = scopes.Should().ContainSingle().Subject;
        scope["oid"].Should().Be(UserObjectId);
        scope["appid"].Should().Be(AppId);
        scope["tenantId"].Should().Be(TenantId);
        scope["obo"].Should().Be(true, "a delegated token with a user oid is OBO-eligible");
        scope["correlationId"].Should().NotBeNull();
    }

    [Fact]
    public async Task AppOnlyToken_EnrichesWithOboFalse_AndTheServicePrincipalObjectId()
    {
        // sub == oid is the app-only shape; obo must be false.
        var (scopes, _) = await InvokeAsync(Authenticated(
            ("oid", ServicePrincipalObjectId),
            ("sub", ServicePrincipalObjectId),
            ("appid", AppId),
            ("tid", TenantId)));

        var scope = scopes.Should().ContainSingle().Subject;
        scope["obo"].Should().Be(false, "no human is behind an app-only token");
        scope["oid"].Should().Be(ServicePrincipalObjectId);
        scope["appid"].Should().Be(AppId);
    }

    [Fact]
    public async Task IndeterminateToken_LogsOboFalse_NotTrue()
    {
        // The deliberate asymmetry: Indeterminate projects to obo=false for LOGGING
        // (preserving this middleware's pre-task-081 behaviour) while an authorization
        // site facing the same classification must DENY.
        var (scopes, _) = await InvokeAsync(Authenticated(("appid", AppId), ("tid", TenantId)));

        scopes.Should().ContainSingle().Subject["obo"].Should().Be(false);
    }

    [Fact]
    public async Task V2AzpClaim_IsResolvedAsTheAppId()
    {
        // v1 emits appid, v2 emits azp. The deleted ResolveAppId handled both; so must the primitive.
        var (scopes, _) = await InvokeAsync(Authenticated(
            ("azp", AppId), ("oid", ServicePrincipalObjectId), ("sub", ServicePrincipalObjectId)));

        scopes.Should().ContainSingle().Subject["appid"].Should().Be(AppId);
    }

    [Fact]
    public async Task MappedUriClaimForms_AreResolved()
    {
        // The deleted helpers each checked a long WS-Fed URI fallback. Verify none was lost.
        var (scopes, _) = await InvokeAsync(Authenticated(
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", UserObjectId),
            ("http://schemas.microsoft.com/identity/claims/tenantid", TenantId),
            ("http://schemas.microsoft.com/identity/claims/scope", "Files.Read")));

        var scope = scopes.Should().ContainSingle().Subject;
        scope["oid"].Should().Be(UserObjectId);
        scope["tenantId"].Should().Be(TenantId);
        scope["obo"].Should().Be(true);
    }

    [Fact]
    public async Task AlternativeTenantIdClaim_IsResolved()
    {
        // ResolveTenantId had a third fallback (`tenant_id`, emitted by some federated issuers).
        var (scopes, _) = await InvokeAsync(Authenticated(
            ("tenant_id", TenantId), ("oid", UserObjectId), ("scp", "Files.Read")));

        scopes.Should().ContainSingle().Subject["tenantId"].Should().Be(TenantId);
    }

    [Fact]
    public async Task AnonymousRequest_GetsNoScope_AndStillCallsNext()
    {
        var (scopes, next) = await InvokeAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        scopes.Should().BeEmpty("anonymous requests must stay out of the audit scope and SIEM noise");
        next.Should().BeTrue();
    }

    [Fact]
    public async Task MissingClaims_AreNullNotEmptyString()
    {
        // Null vs "" is a contract with log sinks: it distinguishes "absent" from "present but empty".
        var (scopes, _) = await InvokeAsync(Authenticated(
            ("oid", ServicePrincipalObjectId), ("sub", ServicePrincipalObjectId)));

        var scope = scopes.Should().ContainSingle().Subject;
        scope["appid"].Should().BeNull();
        scope["tenantId"].Should().BeNull();
    }

    // ---------------------------------------------------------------- helpers

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "TestJwt"));

    /// <summary>Runs the middleware and returns every logging scope it opened, plus whether next ran.</summary>
    private static async Task<(List<Dictionary<string, object?>> Scopes, bool NextCalled)> InvokeAsync(
        ClaimsPrincipal user)
    {
        var logger = new ScopeCapturingLogger();
        var nextCalled = false;

        var middleware = new AuditEnrichmentMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            logger);

        var context = new DefaultHttpContext { User = user };
        await middleware.InvokeAsync(context);

        return (logger.Scopes, nextCalled);
    }

    /// <summary>
    /// Hand-rolled ILogger capturing BeginScope payloads (ADR-038 — no mocking framework).
    /// It records the scope dictionary itself rather than a formatted string, because the
    /// assertion is about the FIELD VALUES the SIEM will receive, not about log text.
    /// </summary>
    private sealed class ScopeCapturingLogger : ILogger<AuditEnrichmentMiddleware>
    {
        public List<Dictionary<string, object?>> Scopes { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                Scopes.Add(pairs.ToDictionary(p => p.Key, p => p.Value));
            }
            else
            {
                throw new InvalidOperationException(
                    $"AuditEnrichmentMiddleware opened a scope of unexpected type {typeof(TState)}. " +
                    "Structured-logging providers require an IEnumerable<KeyValuePair<string, object?>> " +
                    "to materialise top-level log properties — this test models only that contract.");
            }

            return new NoopDisposable();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Not under test — this middleware asserts on scope contents, not log lines.
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
                // Scope lifetime is not under test.
            }
        }
    }
}
