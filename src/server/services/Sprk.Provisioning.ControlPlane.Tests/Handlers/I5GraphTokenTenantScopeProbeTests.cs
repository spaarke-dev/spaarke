// -----------------------------------------------------------------------------
// I5GraphTokenTenantScopeProbeTests.cs
//
// Task 179 (Phase C'' Wave G-7 — H13 acceptance-gate real probe) — pure
// C# unit tests over I5GraphTokenTenantScopeProbe. NO live Graph or
// Azure.Identity chain: a fake TokenCredential returns pre-baked JWTs so
// the probe's verdict logic is exercised in isolation.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. Fake TokenCredential replaces the credential
//   chain; fake JWTs cover the decision tree exhaustively. Live-Azure
//   coverage belongs to the Phase F acceptance suite / task 186 rerun.
//
// COVERAGE (POML acceptance criteria):
//   AC-Pass_TidMatchesTenant                     — tid==tenantId -> Passed
//   AC-Fail_TidMismatch                          — cross-tenant leak -> Failed (CATASTROPHIC)
//   AC-Fail_BlankTenantId                        — refuse ambient acquisition -> Failed
//   AC-InfraFault_CredentialFactoryThrows        — factory throw -> InfraFault
//   AC-InfraFault_CredentialFactoryReturnsNull   — null factory result -> InfraFault
//   AC-InfraFault_GetTokenAsyncThrows            — chain throw -> InfraFault
//   AC-InfraFault_EmptyToken                     — token empty -> InfraFault
//   AC-InfraFault_MalformedJwt                   — non-JWT string -> InfraFault
//   AC-InfraFault_MissingTidClaim                — well-formed JWT, no tid -> InfraFault
//   AC-Pass_TidCaseInsensitive                   — GUID casing tolerated
//   AC-FactoryCalledWithTenantId                 — verifies factory receives explicit tenantId
//   AC-KindProperty                              — reports I5GraphTokenTenant
//
// PARALLEL-SAFETY: does not touch any shared file — new test file, only
// consumes public/internal probe surface + fake TokenCredential (same
// pattern as ArmSubscriptionReadinessProbeTests / BapRestEnvironmentCreator
// Tests).
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class I5GraphTokenTenantScopeProbeTests
{
    private const string ExpectedTenantId = "00000000-1111-2222-3333-444444444444";
    private const string ForeignTenantId  = "99999999-8888-7777-6666-555555555555";
    private const string CustomerId       = "acme";
    private const string RunId            = "01j7q3zp-i5-run";

    // ---------- AC-Pass_TidMatchesTenant ----------

    [Fact]
    public async Task ProbeAsync_TidClaimMatchesTenantId_ReturnsPassed()
    {
        var credential = new FakeTokenCredential(BuildJwtWithTid(ExpectedTenantId));
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>()
               .Which.Kind.Should().Be(InvariantKind.I5GraphTokenTenant);
    }

    // ---------- AC-Pass_TidCaseInsensitive ----------

    [Fact]
    public async Task ProbeAsync_TidClaimMatchesTenantId_CaseInsensitive_ReturnsPassed()
    {
        var upperTid = ExpectedTenantId.ToUpperInvariant();
        var credential = new FakeTokenCredential(BuildJwtWithTid(upperTid));
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(
            BuildRequest(ExpectedTenantId.ToLowerInvariant()), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
    }

    // ---------- AC-Fail_TidMismatch (CATASTROPHIC) ----------

    [Fact]
    public async Task ProbeAsync_TidClaimDifferentFromTenantId_ReturnsFailedWithCatastrophicDiagnostic()
    {
        // Simulates a cross-tenant leak: credential returned a token minted
        // for ForeignTenantId even though the factory was invoked with
        // ExpectedTenantId. The probe MUST NOT silently accept — this is
        // the specific silent-fail category the invariant guards.
        var credential = new FakeTokenCredential(BuildJwtWithTid(ForeignTenantId));
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        var failed = outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(InvariantKind.I5GraphTokenTenant);
        failed.Diagnostic.Should().Contain(ExpectedTenantId, "expected tenantId cited");
        failed.Diagnostic.Should().Contain(ForeignTenantId, "observed foreign tid cited");
        failed.Diagnostic.Should().Contain("CATASTROPHIC");
    }

    // ---------- AC-Fail_BlankTenantId ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProbeAsync_BlankTenantId_RefusesAmbientAcquisition_ReturnsFailed(string tenantId)
    {
        var credential = new NeverInvokedCredential();
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(tenantId), CancellationToken.None);

        var failed = outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(InvariantKind.I5GraphTokenTenant);
        failed.Diagnostic.Should().Contain("blank tenantId");
        credential.WasInvoked.Should().BeFalse(
            "the probe MUST NOT attempt ambient/default-tenant Graph token acquisition");
    }

    // ---------- AC-InfraFault_CredentialFactoryThrows ----------

    [Fact]
    public async Task ProbeAsync_CredentialFactoryThrows_ReturnsInfraFault()
    {
        var probe = new I5GraphTokenTenantScopeProbe(
            tenantId => throw new InvalidOperationException("simulated cred-chain init failure"),
            NullLogger<I5GraphTokenTenantScopeProbe>.Instance);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
               .Which.Diagnostic.Should().Contain("simulated cred-chain init failure");
    }

    // ---------- AC-InfraFault_CredentialFactoryReturnsNull ----------

    [Fact]
    public async Task ProbeAsync_CredentialFactoryReturnsNull_ReturnsInfraFault()
    {
        var probe = new I5GraphTokenTenantScopeProbe(
            tenantId => null!, NullLogger<I5GraphTokenTenantScopeProbe>.Instance);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
               .Which.Diagnostic.Should().Contain("returned null");
    }

    // ---------- AC-InfraFault_GetTokenAsyncThrows ----------

    [Fact]
    public async Task ProbeAsync_GetTokenAsyncThrows_ReturnsInfraFault()
    {
        var credential = new ThrowingCredential(new InvalidOperationException("no MI available"));
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
               .Which.Diagnostic.Should().Contain("no MI available");
    }

    // ---------- AC-InfraFault_EmptyToken ----------

    [Fact]
    public async Task ProbeAsync_EmptyToken_ReturnsInfraFault()
    {
        var credential = new FakeTokenCredential(string.Empty);
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
               .Which.Diagnostic.Should().Contain("empty token");
    }

    // ---------- AC-InfraFault_MalformedJwt ----------

    [Fact]
    public async Task ProbeAsync_MalformedJwt_ReturnsInfraFault()
    {
        var credential = new FakeTokenCredential("not-a-jwt");
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
               .Which.Diagnostic.Should().Contain("JWT-parsed");
    }

    // ---------- AC-InfraFault_MissingTidClaim ----------

    [Fact]
    public async Task ProbeAsync_JwtMissingTidClaim_ReturnsInfraFault()
    {
        var credential = new FakeTokenCredential(BuildJwtWithoutTid());
        var probe = BuildProbe(credential);

        var outcome = await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
               .Which.Diagnostic.Should().Contain("no `tid` claim");
    }

    // ---------- AC-FactoryCalledWithTenantId ----------

    [Fact]
    public async Task ProbeAsync_CallsFactoryWithExplicitTenantId_NotAmbient()
    {
        string? capturedTenantId = null;
        var credential = new FakeTokenCredential(BuildJwtWithTid(ExpectedTenantId));
        var probe = new I5GraphTokenTenantScopeProbe(
            tenantId =>
            {
                capturedTenantId = tenantId;
                return credential;
            },
            NullLogger<I5GraphTokenTenantScopeProbe>.Instance);

        await probe.ProbeAsync(BuildRequest(ExpectedTenantId), CancellationToken.None);

        capturedTenantId.Should().Be(ExpectedTenantId,
            "the probe MUST invoke the credential factory with the request's explicit tenantId " +
            "(that is exactly what the invariant asserts about production wiring)");
    }

    // ---------- AC-KindProperty ----------

    [Fact]
    public void Kind_IsI5GraphTokenTenant()
    {
        var probe = new I5GraphTokenTenantScopeProbe(
            tenantId => new NeverInvokedCredential(),
            NullLogger<I5GraphTokenTenantScopeProbe>.Instance);

        probe.Kind.Should().Be(InvariantKind.I5GraphTokenTenant);
    }

    // ---------- JWT helper coverage (internal statics) ----------

    [Fact]
    public void TryExtractTidClaim_ValidJwtWithTid_ReturnsTid()
    {
        var jwt = BuildJwtWithTid("tid-under-test");
        I5GraphTokenTenantScopeProbe.TryExtractTidClaim(jwt).Should().Be("tid-under-test");
    }

    [Fact]
    public void TryExtractTidClaim_ValidJwtWithoutTid_ReturnsNull()
    {
        I5GraphTokenTenantScopeProbe.TryExtractTidClaim(BuildJwtWithoutTid()).Should().BeNull();
    }

    [Fact]
    public void TryExtractTidClaim_MalformedJwt_Throws()
    {
        var act = () => I5GraphTokenTenantScopeProbe.TryExtractTidClaim("only-one-segment");
        act.Should().Throw<FormatException>();
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static I5GraphTokenTenantScopeProbe BuildProbe(TokenCredential credential)
        => new(tenantId => credential, NullLogger<I5GraphTokenTenantScopeProbe>.Instance);

    private static InvariantVerificationRequest BuildRequest(string tenantId)
        => new(
            CustomerId: CustomerId,
            RunId: RunId,
            TenantId: tenantId,
            SubscriptionId: "sub-cus-acme-prod",
            AiSearchEndpoint: "https://sprk-acme-search.search.windows.net",
            CosmosEndpoint: "https://sprk-acme-cosmos.documents.azure.com/",
            BffApiUrl: "https://sprk-bff-acme.azurewebsites.net",
            ProvisioningScriptsDirectory: "/opt/spaarke/scripts");

    /// <summary>
    /// Builds a JWT (unsigned — signature bytes irrelevant to the probe) with
    /// a header, payload containing the given `tid` claim, and a placeholder
    /// signature segment. The probe reads the payload only.
    /// </summary>
    private static string BuildJwtWithTid(string tid)
    {
        var header = new { alg = "none", typ = "JWT" };
        var payload = new { tid, aud = "https://graph.microsoft.com", iss = $"https://sts.windows.net/{tid}/" };
        return $"{Base64UrlEncode(header)}.{Base64UrlEncode(payload)}.sig-placeholder";
    }

    private static string BuildJwtWithoutTid()
    {
        var header = new { alg = "none", typ = "JWT" };
        var payload = new { aud = "https://graph.microsoft.com" };
        return $"{Base64UrlEncode(header)}.{Base64UrlEncode(payload)}.sig-placeholder";
    }

    private static string Base64UrlEncode(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // -------------------------------------------------------------------
    // Fake TokenCredentials
    // -------------------------------------------------------------------

    /// <summary>
    /// Returns a fixed JWT string on every GetToken(Async) call. Not mocked
    /// (per testing.md ban on Mock&lt;HttpMessageHandler&gt;-style mocking);
    /// hand-rolled per project convention (see DataverseWebApiChartDefSeederTests
    /// .FakeCredential precedent).
    /// </summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        private readonly string _jwt;

        public FakeTokenCredential(string jwt) => _jwt = jwt;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(_jwt, DateTimeOffset.UtcNow.AddMinutes(55));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Throws on GetToken(Async) — proves the probe classifies InfraFault
    /// on a credential-chain-level failure (e.g., no MI, DefaultAzureCredential
    /// chain exhausted).
    /// </summary>
    private sealed class ThrowingCredential : TokenCredential
    {
        private readonly Exception _exception;
        public ThrowingCredential(Exception exception) => _exception = exception;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _exception;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _exception;
    }

    /// <summary>
    /// Records whether it was invoked at all — used to prove the blank-tenantId
    /// path never even TRIES to acquire a token (refuses ambient acquisition).
    /// </summary>
    private sealed class NeverInvokedCredential : TokenCredential
    {
        public bool WasInvoked { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            throw new InvalidOperationException("NeverInvokedCredential invoked — the probe should have refused earlier.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            throw new InvalidOperationException("NeverInvokedCredential invoked — the probe should have refused earlier.");
        }
    }
}
