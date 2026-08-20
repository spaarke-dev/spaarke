// -----------------------------------------------------------------------------
// GraphAppRoleParityT3ProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for GraphAppRoleParityT3Probe (task 178,
// Wave G-7 Batch G-7A2.2). ADR-038 path #1 -- pure C# unit tests over
//   * a hand-rolled fake IGraphAppRoleParityVerifier (parity with task 177's
//     FakeVerifier pattern for IDataverseAppUserVerifier; NEVER Mock<T>)
//   * a hand-rolled FakeGraphSpHttpMessageHandler (parity with task 173's
//     FakeAiSearchHttpMessageHandler pattern; NEVER Mock<HttpMessageHandler>)
//   * a hand-rolled FakeGraphAppRolesRegistry (parity with H10 tests' fake
//     catalog pattern -- lets tests exercise null-AppRoleId escalation +
//     small custom catalogs without depending on the 15-entry
//     L2GraphAppRolesRegistry).
//   * a hand-rolled FakeTokenCredential (parity with I5's tests, task 179).
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  Kind is TrapKind.T3GraphAppRoleParity (constant contract).
//   T2  Happy path (all catalog roles granted; UAMI SP resolves) -> Passed(T3).
//   T3  Some roles missing (H10 partial success shape) -> Failed(T3) with
//       silent-fail-catch diagnostic naming the specific missing roles.
//   T4  UAMI SP not registered in tenant (zero-result lookup) -> Failed(T3)
//       distinguished from parity mismatch, with unambiguous operator
//       diagnosis path (silent-fail catch: H2a-UAMI-never-materialized).
//   T5  Missing TenantId -> InfraFault(T3). Credential factory never invoked;
//       verifier never invoked.
//   T6  Missing UamiClientId -> InfraFault(T3). Credential factory never
//       invoked; verifier never invoked.
//   T7  Null-AppRoleId in catalog -> InfraFault(T3). Silent-regression guard
//       mirroring H10 s8 escalation gate. Credential factory never invoked;
//       verifier never invoked.
//   T8  Credential factory throws -> InfraFault(T3). Verifier never invoked.
//   T9  Credential factory returns null -> InfraFault(T3). Verifier never invoked.
//   T10 Token acquisition throws -> InfraFault(T3). Verifier never invoked.
//   T11 UAMI SP lookup HTTP 5xx -> InfraFault(T3). Verifier never invoked.
//   T12 UAMI SP lookup malformed body -> InfraFault(T3). Verifier never invoked.
//   T13 Parity verifier throws unexpectedly -> InfraFault(T3) (verifier
//       contract is Partial-on-failure, so a throw means the verifier itself
//       failed, not a T3 trigger).
//   T14 Tenant-scoped credential factory receives the request's tenantId (I5
//       idiom regression guard -- parity with I5 probe's test).
//   T15 Probe passes the FULL catalog (15 or whatever _rolesRegistry.GetAll()
//       returns) to the parity verifier -- regression guard against
//       hardcoded "14" or a partial-catalog bug.
//   T16 UAMI SP lookup issues the correct Graph URL shape (?$filter=appId eq
//       '{clientId}'&$select=id) -- fake handler asserts exact URL (regression
//       guard against filter drift; also validates URI escaping).
//   T17 Cancellation (OperationCanceledException) propagates -- never
//       swallowed as InfraFault.
//   T18 Uses UAMI SP object id (not client id) when calling the parity
//       verifier -- regression guard: probe MUST NOT confuse the two ids.
//
// SILENT-FAIL AUDIT (parent-dispatch "task 143 lesson: wrong-but-non-null GUID
// silent-fails"):
//   * T3 tests the operator diagnostic contains BOTH "WRONG-BUT-NON-NULL GUID"
//     phrasing AND "task 143" citation AND actionable `az ad sp show`
//     enumeration recipe. This is the load-bearing behavioral contract of
//     the probe: not just "roles missing", but the concrete two-class-
//     distinguishable diagnosis + do-not-blindly-retry warning.
//   * T4 tests the UAMI-SP-not-registered path emits a DIFFERENT diagnostic
//     (H2a-materialization + UamiClientId-propagation) so the operator does
//     NOT diagnose down the wrong branch.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class GraphAppRoleParityT3ProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string DataverseUrl = "https://sprk-acme.crm.dynamics.com";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string UamiSpObjectId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string AppServiceName = "sprk-acme-prod-bff";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";

    // ---------- T1 kind ----------

    [Fact]
    public void Kind_IsT3GraphAppRoleParity()
    {
        var probe = BuildProbe();
        probe.Kind.Should().Be(TrapKind.T3GraphAppRoleParity);
    }

    // ---------- T2 happy path ----------

    [Fact]
    public async Task ProbeAsync_AllRolesGranted_ReturnsPassed()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T3GraphAppRoleParity);
        verifier.CallCount.Should().Be(1);
    }

    // ---------- T3 parity mismatch (silent-fail catch) ----------

    [Fact]
    public async Task ProbeAsync_SomeRolesMissing_ReturnsFailedWithSilentFailDiagnostic()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var missingValues = new[] { "GroupMember.ReadWrite.All", "User.Invite.All" };
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Partial(
                missingValues,
                GrantedCount: registry.GetAll().Count - missingValues.Length,
                ExpectedCount: registry.GetAll().Count));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T3GraphAppRoleParity);
        // Silent-fail catch: MUST cite task 143 lesson + wrong-GUID class + az recipe + do-not-retry warning
        failed.Diagnostic.Should()
            .Contain("MANIFESTED")
            .And.Contain(UamiSpObjectId)
            .And.Contain(UamiClientId)
            .And.Contain(TenantId)
            .And.Contain("GroupMember.ReadWrite.All")
            .And.Contain("User.Invite.All")
            .And.Contain("WRONG-BUT-NON-NULL GUID")
            .And.Contain("task 143")
            .And.Contain("az ad sp show")
            .And.Contain("00000003-0000-0000-c000-000000000000")
            .And.Contain("Do NOT blindly retry")
            .And.Contain("PLAIN MISSING GRANT")
            .And.Contain("idempotent");
    }

    // ---------- T4 UAMI SP not registered (silent-fail catch — different class) ----------

    [Fact]
    public async Task ProbeAsync_UamiSpNotFound_ReturnsFailedWithUamiMaterializationDiagnostic()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var httpHandler = FakeGraphSpHandler.ReturnsEmpty();
        var probe = BuildProbe(registry, verifier, httpHandler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T3GraphAppRoleParity);
        // UAMI-SP-missing diagnosis must NOT lead the operator down the parity-mismatch (wrong-GUID) path.
        failed.Diagnostic.Should()
            .Contain("UAMI SP not registered")
            .And.Contain(UamiClientId)
            .And.Contain(TenantId)
            .And.Contain("H2a")
            .And.Contain("az ad sp list")
            .And.Contain("appId eq");
        failed.Diagnostic.Should().NotContain("WRONG-BUT-NON-NULL GUID");
        // Parity verifier MUST NOT be invoked once SP lookup returns empty (short-circuit contract).
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T5 missing tenantId ----------

    [Fact]
    public async Task ProbeAsync_MissingTenantId_ReturnsInfraFault_NeitherFactoryNorVerifierCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var credentialFactory = new RecordingCredentialFactory(new FakeTokenCredential());
        var httpHandler = FakeGraphSpHandler.Unused();
        var probe = BuildProbe(registry, verifier, httpHandler, credentialFactory.Build);

        var request = BuildRequest() with { TenantId = "" };
        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("TenantId").And.Contain("I1");
        credentialFactory.CalledWith.Should().BeEmpty();
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T6 missing uamiClientId ----------

    [Fact]
    public async Task ProbeAsync_MissingUamiClientId_ReturnsInfraFault_NeitherFactoryNorVerifierCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var credentialFactory = new RecordingCredentialFactory(new FakeTokenCredential());
        var httpHandler = FakeGraphSpHandler.Unused();
        var probe = BuildProbe(registry, verifier, httpHandler, credentialFactory.Build);

        var request = BuildRequest() with { UamiClientId = "  " };
        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("UamiClientId").And.Contain("H2a");
        credentialFactory.CalledWith.Should().BeEmpty();
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T7 null-AppRoleId catalog (silent-regression guard) ----------

    [Fact]
    public async Task ProbeAsync_CatalogHasNullAppRoleId_ReturnsInfraFault_NeitherFactoryNorVerifierCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithEntries(
            new GraphAppRoleEntry("Files.Read.All", "01d4889c-1287-42c6-ac1f-5d1e02578ef6"),
            new GraphAppRoleEntry("Files.ReadWrite.All", null),                     // ← the silent regression
            new GraphAppRoleEntry("User.Invite.All", "09850681-111b-4a89-9bed-3f2cae46d706"));
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var credentialFactory = new RecordingCredentialFactory(new FakeTokenCredential());
        var httpHandler = FakeGraphSpHandler.Unused();
        var probe = BuildProbe(registry, verifier, httpHandler, credentialFactory.Build);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T3GraphAppRoleParity);
        infra.Diagnostic.Should()
            .Contain("catalog was silently regressed")
            .And.Contain("Files.ReadWrite.All")
            .And.Contain("az ad sp show")
            .And.Contain("00000003-0000-0000-c000-000000000000");
        credentialFactory.CalledWith.Should().BeEmpty();
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T8 credential factory throws ----------

    [Fact]
    public async Task ProbeAsync_CredentialFactoryThrows_ReturnsInfraFault_VerifierNeverCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var probe = BuildProbe(
            registry, verifier, FakeGraphSpHandler.Unused(),
            tenantIdInput => throw new InvalidOperationException("simulated factory failure"));

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should()
            .Contain("credential factory threw")
            .And.Contain(TenantId)
            .And.Contain("InvalidOperationException")
            .And.Contain("simulated factory failure");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T9 credential factory returns null ----------

    [Fact]
    public async Task ProbeAsync_CredentialFactoryReturnsNull_ReturnsInfraFault_VerifierNeverCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var probe = BuildProbe(
            registry, verifier, FakeGraphSpHandler.Unused(),
            tenantIdInput => null!);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("returned null").And.Contain(TenantId);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T10 token acquisition throws ----------

    [Fact]
    public async Task ProbeAsync_TokenAcquisitionThrows_ReturnsInfraFault_VerifierNeverCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var probe = BuildProbe(
            registry, verifier, FakeGraphSpHandler.Unused(),
            _ => new ThrowingTokenCredential(new InvalidOperationException("acquire threw")));

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should()
            .Contain("token acquisition threw")
            .And.Contain(TenantId)
            .And.Contain("InvalidOperationException")
            .And.Contain("acquire threw");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T11 UAMI SP lookup HTTP 5xx ----------

    [Fact]
    public async Task ProbeAsync_UamiSpLookupHttp500_ReturnsInfraFault_VerifierNeverCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var httpHandler = new FakeGraphSpHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.InternalServerError, """{"error":{"code":"internalServerError","message":"boom"}}"""));
        var probe = BuildProbe(registry, verifier, httpHandler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should()
            .Contain("UAMI SP lookup HTTP call failed")
            .And.Contain(UamiClientId);
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T12 UAMI SP lookup malformed body ----------

    [Fact]
    public async Task ProbeAsync_UamiSpLookupMalformedBody_ReturnsInfraFault_VerifierNeverCalled()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        // Non-2xx wrapper so the probe's catch surfaces InfraFault (parity with T11 shape).
        var httpHandler = new FakeGraphSpHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, "not-json-at-all"));
        var probe = BuildProbe(registry, verifier, httpHandler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("UAMI SP lookup HTTP call failed");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T13 parity verifier throws ----------

    [Fact]
    public async Task ProbeAsync_ParityVerifierThrows_ReturnsInfraFault()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            throwOnCall: new InvalidOperationException("verifier is broken"));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should()
            .Contain("IGraphAppRoleParityVerifier.VerifyAsync threw")
            .And.Contain("InvalidOperationException")
            .And.Contain("verifier is broken");
    }

    // ---------- T14 credential factory receives correct tenantId ----------

    [Fact]
    public async Task ProbeAsync_TenantScopedCredentialFactory_ReceivesRequestedTenantId()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var credentialFactory = new RecordingCredentialFactory(new FakeTokenCredential());
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler, credentialFactory.Build);

        await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        credentialFactory.CalledWith.Should().ContainSingle().Which.Should().Be(TenantId);
    }

    // ---------- T15 full catalog passed to parity verifier ----------

    [Fact]
    public async Task ProbeAsync_PassesFullCatalogToParityVerifier_RegressionGuard()
    {
        var entries = new[]
        {
            new GraphAppRoleEntry("Role.A", Guid.NewGuid().ToString()),
            new GraphAppRoleEntry("Role.B", Guid.NewGuid().ToString()),
            new GraphAppRoleEntry("Role.C", Guid.NewGuid().ToString()),
            new GraphAppRoleEntry("Role.D", Guid.NewGuid().ToString()),
            new GraphAppRoleEntry("Role.E", Guid.NewGuid().ToString()),
        };
        var registry = FakeGraphAppRolesRegistry.WithEntries(entries);
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(entries.Length));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        // The probe must pass the SAME list the registry returns -- not a
        // filtered subset, not a hardcoded 14-entry catalog.
        verifier.LastExpectedRoles.Should().BeEquivalentTo(entries);
    }

    // ---------- T16 correct Graph URL shape for SP lookup ----------

    [Fact]
    public async Task ProbeAsync_IssuesCorrectGraphSpLookupUrl_UriEscaped()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        // NOTE: `System.Uri` normalizes the query string on ToString() -- spaces round-trip
        // as literal spaces (unescaped), but the SINGLE QUOTE around the clientId literal
        // stays %27-encoded. The load-bearing regression guard is: (a) exact endpoint path,
        // (b) $filter=appId eq '{clientId}' with the single-quotes escaped, (c) $select=id.
        httpHandler.RequestedUrls.Should().ContainSingle().Which.Should()
            .StartWith("https://graph.microsoft.com/v1.0/servicePrincipals?$filter=")
            .And.Contain($"appId eq %27{UamiClientId}%27")
            .And.EndWith("&$select=id");
    }

    // ---------- T17 cancellation ----------

    [Fact]
    public async Task ProbeAsync_Cancelled_PropagatesOperationCanceled()
    {
        // Sibling-test convention (task 177 T2 probe tests) -- induce cancellation
        // via a fake that throws OperationCanceledException, rather than relying on
        // the fake HttpMessageHandler / TokenCredential to honor a pre-cancelled
        // CancellationToken (they don't, by design -- they emulate happy-path bytes).
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            throwOnCall: new OperationCanceledException("caller cancelled"));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        var act = async () => await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- T18 verifier receives SP object id, not client id ----------

    [Fact]
    public async Task ProbeAsync_PassesUamiSpObjectId_NotClientId_ToParityVerifier()
    {
        var registry = FakeGraphAppRolesRegistry.WithFullCatalog();
        var verifier = new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        var httpHandler = FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        var probe = BuildProbe(registry, verifier, httpHandler);

        await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        verifier.LastUamiServicePrincipalObjectId.Should().Be(UamiSpObjectId);
        verifier.LastUamiServicePrincipalObjectId.Should().NotBe(UamiClientId);
        verifier.LastTenantId.Should().Be(TenantId);
    }

    // ============================================================
    // helpers
    // ============================================================

    private static GraphAppRoleParityT3Probe BuildProbe(
        IGraphAppRolesRegistry? registry = null,
        IGraphAppRoleParityVerifier? parityVerifier = null,
        FakeGraphSpHttpMessageHandler? httpHandler = null,
        Func<string, TokenCredential>? credentialFactory = null)
    {
        registry ??= FakeGraphAppRolesRegistry.WithFullCatalog();
        parityVerifier ??= new FakeParityVerifier(
            result: new GraphAppRoleParityResult.Verified(registry.GetAll().Count));
        httpHandler ??= FakeGraphSpHandler.ReturnsSp(UamiSpObjectId);
        credentialFactory ??= _ => new FakeTokenCredential();

        var httpClient = new HttpClient(httpHandler);
        return new GraphAppRoleParityT3Probe(
            parityVerifier,
            registry,
            httpClient,
            credentialFactory,
            NullLogger<GraphAppRoleParityT3Probe>.Instance);
    }

    private static TrapVerificationRequest BuildRequest() => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: TenantId,
        SubscriptionId: SubscriptionId,
        DataverseUrl: DataverseUrl,
        BffAppRegId: BffAppRegId,
        UamiClientId: UamiClientId,
        KeyVaultName: KeyVaultName,
        AppServiceName: AppServiceName,
        ResourceGroupName: ResourceGroupName);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Hand-rolled fake IGraphAppRoleParityVerifier -- returns canned Verified /
    /// Partial results or throws a configured exception. Records the arguments
    /// of the LAST invocation so tests can assert the probe passed the resolved
    /// SP object id (not the client id) and the FULL catalog to the verifier.
    /// NEVER Mock&lt;T&gt; per ADR-038.
    /// </summary>
    private sealed class FakeParityVerifier : IGraphAppRoleParityVerifier
    {
        private readonly GraphAppRoleParityResult? _result;
        private readonly Exception? _throwOnCall;

        public int CallCount { get; private set; }
        public string? LastUamiServicePrincipalObjectId { get; private set; }
        public string? LastTenantId { get; private set; }
        public IReadOnlyList<GraphAppRoleEntry>? LastExpectedRoles { get; private set; }

        public FakeParityVerifier(GraphAppRoleParityResult result)
        {
            _result = result;
        }

        public FakeParityVerifier(Exception throwOnCall)
        {
            _throwOnCall = throwOnCall;
        }

        public Task<GraphAppRoleParityResult> VerifyAsync(
            string uamiServicePrincipalObjectId,
            string tenantId,
            IReadOnlyList<GraphAppRoleEntry> expectedRoles,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastUamiServicePrincipalObjectId = uamiServicePrincipalObjectId;
            LastTenantId = tenantId;
            LastExpectedRoles = expectedRoles;

            if (_throwOnCall is not null)
            {
                throw _throwOnCall;
            }
            return Task.FromResult(_result!);
        }
    }

    /// <summary>
    /// Hand-rolled fake IGraphAppRolesRegistry -- lets tests exercise null-
    /// AppRoleId escalation + small custom catalogs without depending on
    /// L2GraphAppRolesRegistry's live 15-entry catalog. The
    /// <see cref="WithFullCatalog"/> factory uses a 15-entry fixture with
    /// well-formed GUIDs so happy-path tests match the real catalog's shape.
    /// </summary>
    private sealed class FakeGraphAppRolesRegistry : IGraphAppRolesRegistry
    {
        private readonly IReadOnlyList<GraphAppRoleEntry> _roles;

        public FakeGraphAppRolesRegistry(IReadOnlyList<GraphAppRoleEntry> roles)
        {
            _roles = roles;
        }

        public string GraphResourceAppId => "00000003-0000-0000-c000-000000000000";
        public IReadOnlyList<GraphAppRoleEntry> GetAll() => _roles;

        public static FakeGraphAppRolesRegistry WithFullCatalog()
        {
            // Fixture mirroring L2GraphAppRolesRegistry's real 15-entry catalog
            // shape (identical Values + AppRoleIds so probe diagnostics that
            // cite specific role names in the missing-list read realistically).
            return new FakeGraphAppRolesRegistry(new[]
            {
                new GraphAppRoleEntry("FileStorageContainer.Selected", "40dc41bc-0f7e-42ff-89bd-d9516947e474"),
                new GraphAppRoleEntry("Files.Read.All", "01d4889c-1287-42c6-ac1f-5d1e02578ef6"),
                new GraphAppRoleEntry("Files.ReadWrite.All", "75359482-378d-4052-8f01-80520e7db3cd"),
                new GraphAppRoleEntry("Sites.Read.All", "332a536c-c7ef-4017-ab91-336970924f0d"),
                new GraphAppRoleEntry("Sites.ReadWrite.All", "9492366f-7969-46a4-8d15-ed1a20078fff"),
                new GraphAppRoleEntry("User.Read.All", "df021288-bdef-4463-88db-98f22de89214"),
                new GraphAppRoleEntry("Group.Read.All", "5b567255-7703-4780-807c-7be8301ae99b"),
                new GraphAppRoleEntry("Mail.Read", "810c84a8-4a9e-49e6-bf7d-12d183f40d01"),
                new GraphAppRoleEntry("Mail.ReadWrite", "e2a3a72e-5f79-4c64-b1b1-878b674786c9"),
                new GraphAppRoleEntry("Mail.Send", "b633e1c5-b582-4048-a93e-9f11b44c7e96"),
                new GraphAppRoleEntry("MailboxSettings.Read", "40f97065-369a-49f4-947c-6a255697ae91"),
                new GraphAppRoleEntry("User.ReadWrite.All", "741f803b-c850-494e-b5df-cde7c675a1ca"),
                new GraphAppRoleEntry("GroupMember.ReadWrite.All", "dbaae8cf-10b5-4b86-a4a1-f871c94c6695"),
                new GraphAppRoleEntry("Directory.ReadWrite.All", "19dbc75e-c2e2-444c-a770-ec69d8559fc7"),
                new GraphAppRoleEntry("User.Invite.All", "09850681-111b-4a89-9bed-3f2cae46d706"),
            });
        }

        public static FakeGraphAppRolesRegistry WithEntries(params GraphAppRoleEntry[] entries) =>
            new(entries);
    }

    /// <summary>
    /// Hand-rolled fake HttpMessageHandler for the UAMI-SP lookup step --
    /// records every requested URL so tests can assert the probe issues a
    /// real GET /servicePrincipals?$filter=appId eq '...'&$select=id request.
    /// NEVER Mock&lt;HttpMessageHandler&gt; per ADR-038.
    /// </summary>
    private sealed class FakeGraphSpHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();

        public FakeGraphSpHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri?.ToString() ?? "");
            return Task.FromResult(_responder(request));
        }
    }

    private static class FakeGraphSpHandler
    {
        /// <summary>Fails if invoked (asserts short-circuit paths never make an HTTP call).</summary>
        public static FakeGraphSpHttpMessageHandler Unused() =>
            new(_ => throw new InvalidOperationException("HttpClient must not be exercised in this test path"));

        /// <summary>Returns a Graph 200 with exactly one SP whose id is <paramref name="spObjectId"/>.</summary>
        public static FakeGraphSpHttpMessageHandler ReturnsSp(string spObjectId) =>
            new(_ =>
            {
                var body = $$"""{"value":[{"id":"{{spObjectId}}"}]}""";
                return JsonResponse(HttpStatusCode.OK, body);
            });

        /// <summary>Returns a Graph 200 with an empty result set (UAMI SP not registered in tenant).</summary>
        public static FakeGraphSpHttpMessageHandler ReturnsEmpty() =>
            new(_ => JsonResponse(HttpStatusCode.OK, """{"value":[]}"""));
    }

    /// <summary>Records the tenantId argument passed to the credential factory (I5 idiom regression guard).</summary>
    private sealed class RecordingCredentialFactory
    {
        private readonly TokenCredential _credential;
        public List<string> CalledWith { get; } = new();

        public RecordingCredentialFactory(TokenCredential credential)
        {
            _credential = credential;
        }

        public TokenCredential Build(string tenantId)
        {
            CalledWith.Add(tenantId);
            return _credential;
        }
    }

    /// <summary>Non-throwing credential; returns a stable fake token so JWT-parse never fires.</summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-t3-probe-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Credential whose GetTokenAsync throws (T10).</summary>
    private sealed class ThrowingTokenCredential : TokenCredential
    {
        private readonly Exception _ex;

        public ThrowingTokenCredential(Exception ex)
        {
            _ex = ex;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _ex;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _ex;
    }
}
