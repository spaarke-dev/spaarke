// -----------------------------------------------------------------------------
// ArmSubscriptionReadinessProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmSubscriptionReadinessProbe (task 121,
// Wave G-2). Proves the REAL Azure.ResourceManager SDK call path — not a
// hard-coded Passed — by constructing an ArmClient against a fake
// HttpClientTransport: the SDK's own request construction, URL building, and
// response deserialization all run unmodified; only the HTTP boundary is
// faked. This directly satisfies task 121 acceptance criterion #1 ("verified
// by a test asserting the ARM client method was invoked, not a hard-coded
// Passed") — DS-4 §3's overstatement finding on the retired
// NullSubscriptionReadinessProbe was exactly this gap.
//
// Also exposes ArmSdkTestFakes (internal) so H1SubscriptionReadinessHandlerTests
// can compose the REAL probe end-to-end (real probe + fake Cosmos/enqueuer) to
// prove the full H1 flow — not just the probe in isolation — genuinely reaches
// SubscriptionReadinessRejectionCodes.LighthouseDelegationMissing /
// SubscriptionUnreachable (acceptance criteria #1 + #2).
//
// SCOPE: pure unit tests — no live ARM, no live Azure. ADR-038 path #1 (pure
// C# unit test — the only "external process" here is an in-memory fake
// HttpMessageHandler, never a socket).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmSubscriptionReadinessProbeTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";

    // ---------- Reachability ----------

    [Fact]
    public async Task CheckSubscriptionReachableAsync_Reachable_ReturnsPassedViaGenuineArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be($"/subscriptions/{SubscriptionId}",
                "the probe must call the REAL subscription-GET endpoint, not a hard-coded stub");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.SubscriptionGetBody(SubscriptionId, TenantId, "Enabled"));
        });
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var result = await probe.CheckSubscriptionReachableAsync(SubscriptionId, TenantId, CancellationToken.None);

        result.Passed.Should().BeTrue();
        result.Evidence.Should().NotBeNull();
        result.Evidence!.Value.GetProperty("subscriptionId").GetString().Should().Be(SubscriptionId);
        handler.RequestedUris.Should().ContainSingle(
            uri => uri.AbsolutePath == $"/subscriptions/{SubscriptionId}",
            "asserts the ARM client method was actually invoked over HTTP, not a hard-coded Passed");
    }

    [Fact]
    public async Task CheckSubscriptionReachableAsync_NotFound_ReturnsFailedDomainResult()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("SubscriptionNotFound", "The subscription could not be found.")));
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var result = await probe.CheckSubscriptionReachableAsync(SubscriptionId, TenantId, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("Reader RBAC", "diagnostic must cite the remediation path, not just the raw ARM error");
        result.Evidence!.Value.GetProperty("armStatus").GetInt32().Should().Be(404);
    }

    [Fact]
    public async Task CheckSubscriptionReachableAsync_TenantMismatch_ReturnsFailedDomainResult()
    {
        // §4D I1 strengthening: subscription is ARM-reachable but belongs to
        // a DIFFERENT tenant than the run declares — must fail, not silently pass.
        const string wrongTenantId = "99999999-8888-7777-6666-555555555555";
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.SubscriptionGetBody(SubscriptionId, wrongTenantId, "Enabled")));
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var result = await probe.CheckSubscriptionReachableAsync(SubscriptionId, TenantId, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("belongs to tenant");
    }

    [Fact]
    public async Task CheckSubscriptionReachableAsync_Forbidden_ReturnsFailedDomainResult_DoesNotThrow()
    {
        // §4D I1 escalation trigger scenario (missing Reader RBAC grant) — the
        // probe MUST classify this as a domain failure (Passed=false), never
        // let it propagate as an unhandled exception (that would misclassify
        // to ProbeInfrastructureError at the handler layer).
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have authorization.")));
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var act = async () => await probe.CheckSubscriptionReachableAsync(SubscriptionId, TenantId, CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Passed.Should().BeFalse();
    }

    // ---------- Lighthouse delegation ----------

    [Fact]
    public async Task CheckLighthouseDelegationAsync_AssignmentPresent_ReturnsPassedViaGenuineArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Contain(
                $"/subscriptions/{SubscriptionId}/providers/Microsoft.ManagedServices/registrationAssignments",
                "the probe must call the REAL Lighthouse registrationAssignments list endpoint");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.RegistrationAssignmentsBody(SubscriptionId, count: 1));
        });
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var result = await probe.CheckLighthouseDelegationAsync(SubscriptionId, TenantId, CancellationToken.None);

        result.Passed.Should().BeTrue();
        result.Evidence!.Value.GetProperty("registrationAssignmentCount").GetInt32().Should().Be(1);
        handler.RequestedUris.Should().ContainSingle(
            uri => uri.AbsolutePath.Contains("registrationAssignments"),
            "asserts the ARM client's Lighthouse list method was actually invoked over HTTP");
    }

    [Fact]
    public async Task CheckLighthouseDelegationAsync_NoAssignments_ReturnsFailedDomainResult()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.RegistrationAssignmentsBody(SubscriptionId, count: 0)));
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var result = await probe.CheckLighthouseDelegationAsync(SubscriptionId, TenantId, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("Lighthouse delegation offer");
        result.Evidence!.Value.GetProperty("registrationAssignmentCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task CheckLighthouseDelegationAsync_ArmRejectsRequest_ReturnsFailedDomainResult_DoesNotThrow()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have authorization.")));
        var probe = new ArmSubscriptionReadinessProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var act = async () => await probe.CheckLighthouseDelegationAsync(SubscriptionId, TenantId, CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Passed.Should().BeFalse();
    }

    // ---------- Argument guards ----------

    [Fact]
    public async Task CheckSubscriptionReachableAsync_MissingSubscriptionId_Throws()
    {
        var probe = new ArmSubscriptionReadinessProbe(
            ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"))),
            NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var act = async () => await probe.CheckSubscriptionReachableAsync(string.Empty, TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Shared ARM-SDK test fakes for probe-level tests (this file) AND
/// H1SubscriptionReadinessHandlerTests' real-probe integration cases — an ARM
/// client built against a fake <see cref="HttpClientTransport"/> so the SDK's
/// own marshaling runs unmodified while only the HTTP boundary is faked.
/// Internal (not private) so both test classes in this namespace can reuse
/// one fake-transport implementation (CLAUDE.md §11 — extend, don't duplicate).
/// </summary>
internal static class ArmSdkTestFakes
{
    public static ArmClient NewArmClient(FakeArmHttpMessageHandler handler)
    {
        var options = new ArmClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };
        return new ArmClient(new FakeTokenCredential(), Guid.NewGuid().ToString(), options);
    }

    public static FakeArmHttpMessageHandler NewHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(responder);

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static string SubscriptionGetBody(string subscriptionId, string tenantId, string state) =>
        $$"""
        {
          "id": "/subscriptions/{{subscriptionId}}",
          "subscriptionId": "{{subscriptionId}}",
          "displayName": "Test Subscription",
          "tenantId": "{{tenantId}}",
          "state": "{{state}}",
          "authorizationSource": "RoleBased"
        }
        """;

    public static string ArmErrorBody(string code, string message) =>
        $$"""
        { "error": { "code": "{{code}}", "message": "{{message}}" } }
        """;

    public static string RegistrationAssignmentsBody(string subscriptionId, int count)
    {
        if (count == 0)
        {
            return """{ "value": [] }""";
        }

        var items = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var assignmentId = Guid.NewGuid().ToString();
            items.Add($$"""
            {
              "id": "/subscriptions/{{subscriptionId}}/providers/Microsoft.ManagedServices/registrationAssignments/{{assignmentId}}",
              "name": "{{assignmentId}}",
              "type": "Microsoft.ManagedServices/registrationAssignments",
              "properties": {
                "provisioningState": "Succeeded",
                "registrationDefinitionId": "/subscriptions/{{subscriptionId}}/providers/Microsoft.ManagedServices/registrationDefinitions/{{Guid.NewGuid()}}"
              }
            }
            """);
        }

        return $$"""{ "value": [{{string.Join(",", items)}}] }""";
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-arm-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> wrapped by <see cref="HttpClientTransport"/>
/// so <see cref="ArmClient"/>'s real request pipeline (auth header injection,
/// URL building, retry policy, STJ deserialization) runs unmodified against a
/// canned HTTP response. Records every requested URI so tests can assert the
/// ARM client genuinely made the call (not a hard-coded Passed).
/// </summary>
internal sealed class FakeArmHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeArmHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<Uri> RequestedUris { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri!);
        return Task.FromResult(_responder(request));
    }
}
