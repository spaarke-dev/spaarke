// -----------------------------------------------------------------------------
// SpeOwnerCredentialProbeTests.cs
//
// Task 248 — H0's SPE owner check (replaces KeyVaultCertBootstrapProbe). The probe
// runs the real Microsoft Graph SDK request (container-type registration GET) against
// a fake HttpMessageHandler, with a fake owning-app TokenCredential, both injected
// through SpeConfidentialClientGraphFactory's internal seam (ADR-038 — SDK
// marshaling runs; never Mock<HttpMessageHandler>).
//
//   P1  owner entry + token + registered → Passed, no time gate; the call is made as
//       the owning app in the run's tenant.
//   P2  no owner entry (no containerTypeId / unknown type) → spe-owner-not-configured,
//       no token requested.
//   P3  token exchange fails, or Graph refuses the token (403) → spe-owner-token-failed.
//   P4  container type not registered (404) → spe-container-type-not-registered.
// H0's use of the probe-named code (Resumable) is covered by H0PreflightHandlerTests.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpeOwnerCredentialProbeTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string ContainerTypeId = "fb3817a8-5a55-42ba-8cc9-12cf055168b8";
    private const string OwnerAppId = "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e";

    private const string RegistrationJson =
        """{"id":"fb3817a8-5a55-42ba-8cc9-12cf055168b8","owningAppId":"bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e","billingClassification":"standard","billingStatus":"valid"}""";

    [Fact]
    public async Task OwnerConfigured_TokenWorks_TypeRegistered_Passes()
    {
        var graph = new FakeGraphHandler(HttpStatusCode.OK, RegistrationJson);
        var credentials = new CredentialLog();
        var probe = BuildProbe(graph, credentials);

        var result = await probe.CheckAsync(Input(ContainerTypeId.ToUpperInvariant()), CancellationToken.None);

        result.Passed.Should().BeTrue(result.Diagnostic);
        result.CheckName.Should().Be(PreflightCheckNames.SpeOwnerCredential);
        result.RejectionCode.Should().BeNull();
        result.Headroom.GetProperty("billingClassification").GetString().Should().Be("Standard");
        credentials.Requests.Should().ContainSingle().Which.Should().Be((TenantId, OwnerAppId),
            "L2 signs in as the container type's owning app in the run's tenant");
        graph.RequestUri!.AbsolutePath.Should().Be($"/v1.0/storage/fileStorage/containerTypeRegistrations/{ContainerTypeId}");
        graph.Authorization.Should().Be("Bearer owner-app-graph-token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dddddddd-0000-0000-0000-000000000009")]
    public async Task NoOwnerEntry_RejectsOwnerNotConfigured_WithoutSigningIn(string? containerTypeId)
    {
        var graph = new FakeGraphHandler(HttpStatusCode.OK, RegistrationJson);
        var credentials = new CredentialLog();

        var result = await BuildProbe(graph, credentials).CheckAsync(Input(containerTypeId), CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.RejectionCode.Should().Be(SpeOwnerCredentialProbe.OwnerNotConfiguredRejectionCode);
        credentials.Requests.Should().BeEmpty();
        graph.RequestUri.Should().BeNull();
    }

    [Fact]
    public async Task TokenExchangeFails_RejectsOwnerTokenFailed()
    {
        var graph = new FakeGraphHandler(HttpStatusCode.OK, RegistrationJson);
        var credentials = new CredentialLog(
            new AuthenticationFailedException("AADSTS70021: No matching federated identity record found."));

        var result = await BuildProbe(graph, credentials).CheckAsync(Input(ContainerTypeId), CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.RejectionCode.Should().Be(SpeOwnerCredentialProbe.OwnerTokenFailedRejectionCode);
        result.Diagnostic.Should().Contain("AADSTS70021").And.Contain(OwnerAppId).And.Contain("federated");
    }

    [Fact]
    public async Task GraphRefusesTheOwnerToken_RejectsOwnerTokenFailed()
    {
        var graph = new FakeGraphHandler(HttpStatusCode.Forbidden,
            """{"error":{"code":"accessDenied","message":"Either scp or roles claim need to be present in the token."}}""");

        var result = await BuildProbe(graph, new CredentialLog()).CheckAsync(Input(ContainerTypeId), CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.RejectionCode.Should().Be(SpeOwnerCredentialProbe.OwnerTokenFailedRejectionCode);
        result.Diagnostic.Should().Contain("403").And.Contain("FileStorageContainerTypeReg.Selected");
    }

    [Fact]
    public async Task ContainerTypeNotRegistered_RejectsNotRegistered()
    {
        var graph = new FakeGraphHandler(HttpStatusCode.NotFound,
            """{"error":{"code":"itemNotFound","message":"The container type registration was not found."}}""");

        var result = await BuildProbe(graph, new CredentialLog()).CheckAsync(Input(ContainerTypeId), CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.RejectionCode.Should().Be(SpeOwnerCredentialProbe.ContainerTypeNotRegisteredRejectionCode);
        result.Diagnostic.Should().Contain(ContainerTypeId).And.Contain("containerTypeRegistrations");
    }

    // ---------- helpers ----------

    private static PreflightProbeInput Input(string? containerTypeId)
    {
        var parameters = new Dictionary<string, string>();
        if (containerTypeId is not null)
        {
            parameters[IntakeParameterCatalog.ContainerTypeId] = containerTypeId;
        }
        return new PreflightProbeInput("acme", TenantId, parameters);
    }

    private static SpeOwnerCredentialProbe BuildProbe(FakeGraphHandler graph, CredentialLog credentials) => new(
        new SpeConfidentialClientGraphFactory(credentials.Create, graph),
        Options.Create(new SpeContainerOptions
        {
            ContainerTypeOwners = [new SpeContainerTypeOwner { ContainerTypeId = ContainerTypeId, OwnerAppId = OwnerAppId }],
        }),
        NullLogger<SpeOwnerCredentialProbe>.Instance);

    /// <summary>Records each owning-app credential requested; the credential issues a fixed token or throws.</summary>
    private sealed class CredentialLog(Exception? failure = null)
    {
        public List<(string TenantId, string OwnerAppId)> Requests { get; } = new();

        public TokenCredential Create(string tenantId, string ownerAppId)
        {
            Requests.Add((tenantId, ownerAppId));
            return new FixedCredential(failure);
        }

        private sealed class FixedCredential(Exception? failure) : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => failure is null ? new AccessToken("owner-app-graph-token", DateTimeOffset.UtcNow.AddHours(1)) : throw failure;

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => new(GetToken(requestContext, cancellationToken));
        }
    }

    /// <summary>Stands in for Microsoft Graph: one canned response; captures the request.</summary>
    private sealed class FakeGraphHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
