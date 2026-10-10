// -----------------------------------------------------------------------------
// ExchangePolicySidecarClientContractTests.cs
//
// Contract tests for ExchangePolicySidecarClient (H14a apply + H13 T4 read) against the sidecar's
// wire format (Listener.ps1 / SidecarCore.psm1, task 251 — RBAC for Applications):
//   - the request body carries every field the sidecar reads, with both headers
//     (X-Sidecar-Auth + X-Exchange-Access-Token) and correlationId = RunId;
//   - every wire outcome maps onto the seam (Success/AlreadyCompliant -> Applied, Drift -> Drift,
//     Failure -> one retry then Failure);
//   - transport, status and parse failures are explicit Failures;
//   - no HTTP call is made without BOTH credentials (shared secret, Exchange token) — never an
//     empty header. The Exchange token comes only from the managed-identity federated credential
//     (owner D24); a missing ExchangeAdminAppId or a sign-in failure stops before any call;
//   - every body names the tenant's initial domain as `organization` (looked up once per tenant);
//     a failed lookup stops before any call — a tenant GUID would let Exchange connect but fail
//     every write (live, 2026-10-04).
//
// ADR-038: pure unit tests over a hand-rolled HttpMessageHandler (never Mock<HttpMessageHandler>)
// and a fake TokenCredential. Live verification against a running sidecar is
// ExchangePolicySidecarLiveVerificationTests (env-gated).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ExchangePolicySidecarClientContractTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string UamiObjectId = "99999999-8888-7777-6666-555555555555";
    private const string ScopeGroupId = "77777777-8888-9999-0000-111111111111";
    private const string ExchangeAdminAppId = "46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7";
    private const string RunId = "01j7q3zp-h14a-contract-test-run";
    private const string PlatformKvVault = "sprk-controlplane-dev-kv";
    private const string PlatformKvSubscription = "22222222-3333-4444-5555-666666666666";
    private const string SharedSecretName = "Sidecar-Shared-Secret";
    private const string SharedSecretValue = "per-boot-shared-secret-value-42";
    private const string ExchangeToken = "exo-access-token-value";
    private const string InitialDomain = "contoso.onmicrosoft.com";

    // ========== Apply: request ==========

    [Fact]
    public async Task Apply_SendsEveryFieldTheSidecarReads_WithBothHeaders()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 2)) };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>();
        var sent = handler.CapturedRequests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.Should().Be(ExchangePolicySidecarClient.ApplyPath);
        sent.Headers[ExchangePolicySidecarClient.SharedSecretHeaderName].Should().Be(SharedSecretValue);
        sent.Headers[ExchangePolicySidecarClient.ExchangeTokenHeaderName].Should().Be(ExchangeToken);

        using var doc = JsonDocument.Parse(sent.BodyJson!);
        var root = doc.RootElement;
        root.GetProperty("tenantId").GetString().Should().Be(TenantId);
        root.GetProperty("organization").GetString().Should().Be(InitialDomain);
        root.GetProperty("appId").GetString().Should().Be(UamiClientId);
        root.GetProperty("servicePrincipalObjectId").GetString().Should().Be(UamiObjectId);
        root.GetProperty("displayName").GetString().Should().Be("Spaarke-acme-stamp-identity");
        root.GetProperty("scopeGroupId").GetString().Should().Be(ScopeGroupId);
        root.GetProperty("correlationId").GetString().Should().Be(RunId);
        root.GetProperty("timeoutSeconds").GetInt32().Should().BeGreaterThan(0);
        var assignments = root.GetProperty("assignments").EnumerateArray()
            .Select(a => (a.GetProperty("name").GetString(), a.GetProperty("role").GetString())).ToArray();
        assignments.Should().Equal(("Spaarke-acme-MailSend", "Application Mail.Send"), ("Spaarke-acme-MailRead", "Application Mail.Read"));
        sent.BodyJson.Should().NotContain(ExchangeToken, "the token travels only in its header");
    }

    // ========== Apply: outcome mapping ==========

    [Theory]
    [InlineData("Success", 2)]
    [InlineData("AlreadyCompliant", 0)]
    public async Task Apply_SuccessAndAlreadyCompliant_MapToApplied(string wireOutcome, int createdCount)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply(wireOutcome, createdCount)) };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        var applied = result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>().Subject;
        applied.CreatedCount.Should().Be(createdCount);
        applied.AssignmentNames.Should().Equal("Spaarke-acme-MailSend", "Spaarke-acme-MailRead");
    }

    [Fact]
    public async Task Apply_Drift_MapsToDrift_CarryingEveryConflict_WithoutRetry()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson("""
            { "outcome": "Drift", "createdCount": 0, "assignments": [],
              "conflicts": ["Assignment 'Spaarke-acme-MailSend' is scoped to 'other', expected group 'g'.", "App also holds 'Application Mail.Read' through 'manual-1'."],
              "diagnostic": "nothing was created or changed" }
            """) };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Drift>().Which.Conflicts.Should().HaveCount(2);
        handler.CapturedRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task Apply_WireFailure_RetriesOnce_ThenFailure()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Failure", 0, "Exchange throttled")) };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain("Exchange throttled");
        handler.CapturedRequests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Apply_WireFailure_ThenSuccess_ReturnsApplied()
    {
        var calls = 0;
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => ++calls == 1 ? OkJson(WireApply("Failure", 0, "transient")) : OkJson(WireApply("Success", 1)),
        };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>().Which.CreatedCount.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 1, "X-Sidecar-Auth")]
    [InlineData(HttpStatusCode.BadRequest, 1, "rejected the request")]
    [InlineData(HttpStatusCode.NotFound, 1, "does not serve this route")]
    [InlineData(HttpStatusCode.ServiceUnavailable, 1, "not configured")]
    [InlineData(HttpStatusCode.InternalServerError, 2, "HTTP 500")]
    public async Task Apply_HttpStatus_MapsToFailure_RetryingOnlyServerErrors(HttpStatusCode status, int expectedCalls, string diagnosticFragment)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => new HttpResponseMessage(status) { Content = new StringContent("{\"outcome\":\"Failure\"}") } };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain(diagnosticFragment);
        handler.CapturedRequests.Should().HaveCount(expectedCalls);
    }

    [Theory]
    [InlineData("{ not json", "unparseable JSON")]
    [InlineData("{\"outcome\":\"Mystery\"}", "unknown outcome 'Mystery'")]
    public async Task Apply_UnusableBody_IsTerminalFailure(string body, string diagnosticFragment)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(body) };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain(diagnosticFragment);
        handler.CapturedRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task Apply_ConnectionRefused_IsFailure_WithoutRetry()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => throw new HttpRequestException("Connection refused") };

        var result = await NewClient(handler).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain("transport failure");
        handler.CapturedRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task Apply_Timeout_IsFailure_WithoutRetry()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)), DelayBeforeResponse = TimeSpan.FromSeconds(5) };

        var result = await NewClient(handler, configureHttpClient: c => c.Timeout = TimeSpan.FromMilliseconds(100))
            .ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain("timed out");
        handler.CapturedRequests.Should().ContainSingle();
    }

    // ========== Credentials: never an empty header ==========

    [Fact]
    public async Task Apply_EmptyCorrelationId_FailsBeforeAnyCall()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };

        var result = await NewClient(handler).ApplyAsync(BuildRequest() with { CorrelationId = "" }, CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain("CorrelationId");
        handler.CapturedRequests.Should().BeEmpty();
    }

    public static TheoryData<string, KvSecretReadResult?, string> SharedSecretProblems => new()
    {
        { "config", null, "shared secret config missing" },
        { "notfound", new KvSecretReadResult.NotFound(), "not found on vault" },
        { "kvfailure", new KvSecretReadResult.Failure("403 Forbidden"), "403 Forbidden" },
    };

    [Theory]
    [MemberData(nameof(SharedSecretProblems))]
    public async Task Apply_SharedSecretUnavailable_FailsBeforeAnyCall(string problem, KvSecretReadResult? kvResult, string diagnosticFragment)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };
        var options = NewOptions();
        if (problem == "config")
        {
            options.SidecarSharedSecretVaultName = "";
        }

        var result = await NewClient(handler, options, kvResult is null ? null : new FakeKvSecretReader { Result = kvResult })
            .ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain(diagnosticFragment);
        handler.CapturedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ExchangeAdminAppNotConfigured_FailsBeforeAnyCall_NamingTheSetting()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };
        var options = NewOptions();
        options.ExchangeAdminAppId = "";

        var result = await NewClient(handler, options).ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain("ExchangeAdminAppId");
        handler.CapturedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ExchangeSignInFails_FailsBeforeAnyCall_PointingAtTheFederatedCredential()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };
        var credential = new FakeCredential { Error = new AuthenticationFailedException("AADSTS700213: No matching federated identity record found") };

        var result = await NewClient(handler, credential: credential).ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("AADSTS700213").And.Contain("federated identity credential");
        handler.CapturedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExchangeToken_IsRequestedForExchangeOnline_AsTheAdminApp_InTheRunsTenant()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };
        string? tenant = null, app = null;
        var credential = new FakeCredential();
        var source = new ExchangeAdminTokenSource((t, a) => { tenant = t; app = a; return credential; }, FoundDomain, Options.Create(NewOptions()));

        await NewClient(handler, tokenSource: source).ApplyAsync(BuildRequest(), CancellationToken.None);

        tenant.Should().Be(TenantId);
        app.Should().Be(ExchangeAdminAppId);
        credential.LastScopes.Should().Equal("https://outlook.office365.com/.default");
    }

    public static TheoryData<string> InitialDomainProblems => new() { "throws", "none" };

    [Theory]
    [MemberData(nameof(InitialDomainProblems))]
    public async Task Apply_InitialDomainUnknown_FailsBeforeAnyCall_NeverFallingBackToTheTenantId(string problem)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };
        Func<string, CancellationToken, Task<string?>> lookup = problem == "throws"
            ? (_, _) => throw new HttpRequestException("Graph GET /organization returned HTTP 403")
            : (_, _) => Task.FromResult<string?>(null);
        var opts = Options.Create(NewOptions());

        var result = await NewClient(handler, tokenSource: new ExchangeAdminTokenSource((_, _) => new FakeCredential(), lookup, opts))
            .ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Which.Diagnostic.Should().Contain("initial domain").And.Contain("Organization.Read.All");
        handler.CapturedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task InitialDomain_IsLookedUpOncePerTenant()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireApply("Success", 1)) };
        var lookups = new List<string>();
        var source = new ExchangeAdminTokenSource(
            (_, _) => new FakeCredential(),
            (t, _) => { lookups.Add(t); return Task.FromResult<string?>(InitialDomain); },
            Options.Create(NewOptions()));
        var client = NewClient(handler, tokenSource: source);

        await client.ApplyAsync(BuildRequest(), CancellationToken.None);
        await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        lookups.Should().Equal(TenantId);
    }

    [Fact]
    public void ParseInitialDomain_PicksTheInitialVerifiedDomain()
    {
        const string body = """
            { "value": [ { "verifiedDomains": [
                { "name": "contoso.com", "isInitial": false, "isDefault": true },
                { "name": "contoso.onmicrosoft.com", "isInitial": true, "isDefault": false } ] } ] }
            """;

        ExchangeAdminTokenSource.ParseInitialDomain(body).Should().Be("contoso.onmicrosoft.com");
        ExchangeAdminTokenSource.ParseInitialDomain("{ \"value\": [] }").Should().BeNull();
    }

    // ========== Read (H13 T4) ==========

    [Fact]
    public async Task Read_MapsAssignments_AndSendsTheRolesWithBothHeaders()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson("""
            { "outcome": "Success", "servicePrincipalRegistered": true,
              "assignments": [ { "name": "Spaarke-acme-MailSend", "role": "Application Mail.Send", "scope": "Group:g", "inExpectedScope": true },
                               { "name": "manual", "role": "Application Mail.Read", "scope": "", "inExpectedScope": false } ],
              "diagnostic": "2 assignments" }
            """) };

        var result = await NewClient(handler).ReadAsync(
            new ExchangePolicyReadRequest(TenantId, UamiClientId, ScopeGroupId, new[] { "Application Mail.Send", "Application Mail.Read" }, RunId),
            CancellationToken.None);

        var success = result.Should().BeOfType<ExchangePolicyReadOutcome.Success>().Subject;
        success.ServicePrincipalRegistered.Should().BeTrue();
        success.Assignments.Should().Equal(
            new ExchangeRoleAssignmentView("Spaarke-acme-MailSend", "Application Mail.Send", "Group:g", true),
            new ExchangeRoleAssignmentView("manual", "Application Mail.Read", "", false));
        var sent = handler.CapturedRequests.Should().ContainSingle().Subject;
        sent.RequestUri!.AbsolutePath.Should().Be(ExchangePolicySidecarClient.ReadPath);
        sent.Headers.Should().ContainKey(ExchangePolicySidecarClient.SharedSecretHeaderName)
            .And.ContainKey(ExchangePolicySidecarClient.ExchangeTokenHeaderName);
        using var doc = JsonDocument.Parse(sent.BodyJson!);
        doc.RootElement.GetProperty("roles").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("scopeGroupId").GetString().Should().Be(ScopeGroupId);
        doc.RootElement.GetProperty("organization").GetString().Should().Be(InitialDomain);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"outcome\":\"Failure\",\"diagnostic\":\"Exchange call failed: x\"}", "Exchange call failed")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"outcome\":\"Failure\"}", "not configured")]
    public async Task Read_NonSuccess_IsFailure(HttpStatusCode status, string body, string diagnosticFragment)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => new HttpResponseMessage(status) { Content = new StringContent(body) } };

        var result = await NewClient(handler).ReadAsync(
            new ExchangePolicyReadRequest(TenantId, UamiClientId, ScopeGroupId, new[] { "Application Mail.Send" }, RunId), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Which.Diagnostic.Should().Contain(diagnosticFragment);
    }

    // ========== Options validation (boot) ==========

    [Theory]
    [InlineData("not-a-guid", "Spaarke", "ExchangeAdminAppId")]
    [InlineData("", "has space", "ExchangeAssignmentNamePrefix")]
    [InlineData("", "", "ExchangeAssignmentNamePrefix")]
    public void Validate_RejectsBadExchangeSettings(string appId, string prefix, string fragment)
    {
        var options = NewOptions();
        options.ExchangeAdminAppId = appId;
        options.ExchangeAssignmentNamePrefix = prefix;

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{fragment}*");
    }

    [Fact]
    public void Validate_PartialSharedSecretConfig_Throws()
    {
        var options = NewOptions();
        options.SidecarSharedSecretSubscriptionId = "";

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*partial*");
    }

    // ---------- helpers ----------

    private static ExchangePolicyApplyRequest BuildRequest() => new(
        TenantId, UamiClientId, UamiObjectId, "Spaarke-acme-stamp-identity", ScopeGroupId,
        new[]
        {
            new ExchangeRoleAssignmentSpec("Spaarke-acme-MailSend", "Application Mail.Send"),
            new ExchangeRoleAssignmentSpec("Spaarke-acme-MailRead", "Application Mail.Read"),
        },
        RunId);

    // ========== Customer mailbox (H14m ensure / H13 read — task 263) ==========

    private static CustomerMailboxRequest MailboxRequest() => new(
        TenantId, UamiClientId, ScopeGroupId, "Spaarke-AppAccess-acme", "sprk-acme-mail", "Acme Corporation", "acme@contoso.com",
        new[] { "Application Mail.Read", "Application Mail.Send" }, RunId);

    [Fact]
    public async Task EnsureMailbox_SendsEveryFieldTheSidecarValidates_WithBothHeaders()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireMailbox("Success", created: true, verified: true)) };

        var result = await NewClient(handler).EnsureAsync(MailboxRequest(), CancellationToken.None);

        result.Should().BeOfType<CustomerMailboxEnsureOutcome.Ensured>().Which.Should().Match<CustomerMailboxEnsureOutcome.Ensured>(e => e.Created && e.Verified);
        var sent = handler.CapturedRequests.Should().ContainSingle().Subject;
        sent.RequestUri!.AbsolutePath.Should().Be(ExchangePolicySidecarClient.EnsureCustomerMailboxPath);
        sent.Headers[ExchangePolicySidecarClient.SharedSecretHeaderName].Should().Be(SharedSecretValue);
        sent.Headers[ExchangePolicySidecarClient.ExchangeTokenHeaderName].Should().Be(ExchangeToken);
        using var body = JsonDocument.Parse(sent.BodyJson!);
        var root = body.RootElement;
        // Test-CustomerMailboxRequest (SidecarCore.psm1) requires exactly these fields.
        foreach (var (field, value) in new[]
        {
            ("tenantId", TenantId), ("organization", InitialDomain), ("appId", UamiClientId), ("scopeGroupId", ScopeGroupId),
            ("expectedScopeGroupName", "Spaarke-AppAccess-acme"), ("name", "sprk-acme-mail"), ("displayName", "Acme Corporation"),
            ("primarySmtpAddress", "acme@contoso.com"), ("correlationId", RunId),
        })
        {
            root.GetProperty(field).GetString().Should().Be(value, field);
        }
        root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Application Mail.Read", "Application Mail.Send");
    }

    [Fact]
    public async Task EnsureMailbox_AlreadyCompliant_IsEnsuredNotCreated()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireMailbox("AlreadyCompliant", created: false, verified: true)) };

        var result = await NewClient(handler).EnsureAsync(MailboxRequest(), CancellationToken.None);

        var ensured = result.Should().BeOfType<CustomerMailboxEnsureOutcome.Ensured>().Subject;
        ensured.Created.Should().BeFalse();
        ensured.Authorization.Should().OnlyContain(a => a.InScope);
    }

    [Fact]
    public async Task EnsureMailbox_Drift_CarriesEveryConflict()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson("""{ "outcome": "Drift", "created": false, "verified": false, "authorization": [], "conflicts": ["a", "b"], "diagnostic": "x" }"""),
        };

        var result = await NewClient(handler).EnsureAsync(MailboxRequest(), CancellationToken.None);

        result.Should().BeOfType<CustomerMailboxEnsureOutcome.Drift>().Which.Conflicts.Should().Equal("a", "b");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "does not serve this route")]
    [InlineData(HttpStatusCode.Unauthorized, "X-Sidecar-Auth")]
    public async Task EnsureMailbox_NonOkStatus_IsADescribedFailure(HttpStatusCode status, string expected)
    {
        var handler = new CapturingHandler { ResponseFactory = _ => new HttpResponseMessage(status) { Content = new StringContent("{}") } };

        var result = await NewClient(handler).EnsureAsync(MailboxRequest(), CancellationToken.None);

        result.Should().BeOfType<CustomerMailboxEnsureOutcome.Failure>().Which.Diagnostic.Should().Contain(expected);
    }

    [Fact]
    public async Task EnsureMailbox_WireFailure_IsFailure_NotRetried()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson("""{ "outcome": "Failure", "diagnostic": "Spaarke Exchange Admin cannot run New-Mailbox -Shared — prerequisite PRQ-E-16" }"""),
        };

        var result = await NewClient(handler).EnsureAsync(MailboxRequest(), CancellationToken.None);

        result.Should().BeOfType<CustomerMailboxEnsureOutcome.Failure>().Which.Diagnostic.Should().Contain("PRQ-E-16");
        handler.CapturedRequests.Should().ContainSingle("a failed ensure is Resumable; its re-run is get-before-set");
    }

    [Fact]
    public async Task EnsureMailbox_IncompleteRequest_FailsWithoutCallingTheSidecar()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson("{}") };

        var result = await NewClient(handler).EnsureAsync(MailboxRequest() with { PrimarySmtpAddress = "" }, CancellationToken.None);

        result.Should().BeOfType<CustomerMailboxEnsureOutcome.Failure>();
        handler.CapturedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadMailbox_PostsToTheReadRoute_AndMapsTheState()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson("""{ "outcome": "Success", "exists": true, "conflicts": ["c1"], "authorization": [{ "role": "Application Mail.Send", "inScope": true }], "diagnostic": "d" }"""),
        };

        var result = await NewClient(handler).ReadAsync(MailboxRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().ContainSingle().Which.RequestUri!.AbsolutePath.Should().Be(ExchangePolicySidecarClient.ReadCustomerMailboxPath);
        var success = result.Should().BeOfType<CustomerMailboxReadOutcome.Success>().Subject;
        success.Exists.Should().BeTrue();
        success.Conflicts.Should().Equal("c1");
        success.Authorization.Should().ContainSingle().Which.Should().Be(new CustomerMailboxAuthorization("Application Mail.Send", true));
    }

    [Fact]
    public async Task ReadMailbox_WireFailure_IsFailure()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson("""{ "outcome": "Failure", "diagnostic": "Scope group not found" }""") };

        var result = await NewClient(handler).ReadAsync(MailboxRequest(), CancellationToken.None);

        result.Should().BeOfType<CustomerMailboxReadOutcome.Failure>().Which.Diagnostic.Should().Contain("Scope group not found");
    }

    private static string WireMailbox(string outcome, bool created, bool verified) => $$"""
      {
        "outcome": "{{outcome}}",
        "created": {{(created ? "true" : "false")}},
        "verified": {{(verified ? "true" : "false")}},
        "authorization": [
          { "role": "Application Mail.Read", "inScope": {{(verified ? "true" : "false")}} },
          { "role": "Application Mail.Send", "inScope": {{(verified ? "true" : "false")}} }
        ],
        "conflicts": [],
        "diagnostic": "done"
      }
      """;

    private static IntegrationWiringOptions NewOptions() => new()
    {
        SidecarBaseUrl = "http://127.0.0.1:8091/",
        SidecarRequestTimeout = TimeSpan.FromMinutes(6),
        SidecarTransientRetryDelay = TimeSpan.FromMilliseconds(100),
        SidecarSharedSecretVaultName = PlatformKvVault,
        SidecarSharedSecretSubscriptionId = PlatformKvSubscription,
        SidecarSharedSecretName = SharedSecretName,
        ExchangeAdminAppId = ExchangeAdminAppId,
    };

    private static ExchangePolicySidecarClient NewClient(
        CapturingHandler handler,
        IntegrationWiringOptions? options = null,
        FakeKvSecretReader? kvReader = null,
        Action<HttpClient>? configureHttpClient = null,
        FakeCredential? credential = null,
        ExchangeAdminTokenSource? tokenSource = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8091/") };
        configureHttpClient?.Invoke(httpClient);
        var opts = Options.Create(options ?? NewOptions());
        return new ExchangePolicySidecarClient(
            httpClient,
            kvReader ?? new FakeKvSecretReader { Result = new KvSecretReadResult.Success(SharedSecretValue) },
            tokenSource ?? new ExchangeAdminTokenSource((_, _) => credential ?? new FakeCredential(), FoundDomain, opts),
            opts,
            NullLogger<ExchangePolicySidecarClient>.Instance);
    }

    private static Task<string?> FoundDomain(string tenantId, CancellationToken cancellationToken) => Task.FromResult<string?>(InitialDomain);

    private static HttpResponseMessage OkJson(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static string WireApply(string outcome, int createdCount, string diagnostic = "done") => $$"""
      {
        "outcome": "{{outcome}}",
        "createdCount": {{createdCount}},
        "assignments": [
          { "name": "Spaarke-acme-MailSend", "role": "Application Mail.Send", "scope": "Group:g", "inExpectedScope": true },
          { "name": "Spaarke-acme-MailRead", "role": "Application Mail.Read", "scope": "Group:g", "inExpectedScope": true }
        ],
        "conflicts": [],
        "diagnostic": "{{diagnostic}}"
      }
      """;

    /// <summary>Records every outbound request's headers + body.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; init; }
        public TimeSpan? DelayBeforeResponse { get; init; }
        public List<CapturedRequest> CapturedRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
            {
                headers[h.Key] = string.Join(", ", h.Value);
            }
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            CapturedRequests.Add(new CapturedRequest(request.RequestUri, request.Method, headers, body));
            if (DelayBeforeResponse is { } d)
            {
                await Task.Delay(d, cancellationToken).ConfigureAwait(false);
            }
            return ResponseFactory(request);
        }
    }

    private sealed record CapturedRequest(Uri? RequestUri, HttpMethod Method, IReadOnlyDictionary<string, string> Headers, string? BodyJson);

    private sealed class FakeKvSecretReader : IKvSecretReader
    {
        public required KvSecretReadResult Result { get; init; }

        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }

    /// <summary>Stands in for the managed-identity federated credential.</summary>
    private sealed class FakeCredential : TokenCredential
    {
        public Exception? Error { get; init; }
        public string[]? LastScopes { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            LastScopes = requestContext.Scopes;
            return Error is null ? new AccessToken(ExchangeToken, DateTimeOffset.UtcNow.AddHours(1)) : throw Error;
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
