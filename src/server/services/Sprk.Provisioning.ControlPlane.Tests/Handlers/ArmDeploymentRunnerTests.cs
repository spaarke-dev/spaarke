// -----------------------------------------------------------------------------
// ArmDeploymentRunnerTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmDeploymentRunner (task 123, Wave G-2).
// Proves the REAL Azure.ResourceManager.Resources + Azure.Storage.Blobs SDK
// call paths — not a hard-coded outcome — by constructing an ArmClient /
// BlobContainerClient against a fake HttpClientTransport (parity with
// ArmSubscriptionReadinessProbeTests.cs, task 121): the SDK's own request
// construction, URL building, and response deserialization all run
// unmodified; only the HTTP boundary is faked. ADR-038 path #1 (pure C#
// unit test — the only "external process" is an in-memory fake
// HttpMessageHandler).
//
// GROUND-TRUTHED RESPONSE SHAPES (task 123 POML's xhigh-effort directive —
// verified via a throwaway reflection/spike console app against the
// installed Azure.ResourceManager.Resources 1.11.2 + Azure.Storage.Blobs
// 12.29.1 packages BEFORE writing this file, not guessed):
//   - ResourceGroupCollection.CreateOrUpdateAsync(WaitUntil.Completed, ...)
//     and ArmDeploymentCollection.CreateOrUpdateAsync(WaitUntil.Completed, ...)
//     both complete WITHOUT extra polling when the initial PUT response is a
//     plain 200 with a body whose provisioningState is already terminal
//     ("Succeeded") — no Azure-AsyncOperation / Location header dance needed
//     in the fake.
//   - ArmDeploymentPropertiesExtended.Outputs deserializes directly from the
//     deployment response's `properties.outputs` (ARM's
//     `{ "key": { "type": "...", "value": ... } }` output shape) — this is
//     the SAME BinaryData shape ArmDeploymentRunner.MapOutputs parses.
//
// COVERAGE:
//   T1  Happy path: RG-ensure + deploy succeed, manifest resolves the
//       "customer" template (Model2Dedicated / default), outputs map onto
//       BicepDeployOutputs (including the honest-empty fields for outputs
//       customer.bicep does not currently produce — see ArmDeploymentRunner.cs
//       file-header "BLOCKING DISCOVERY" note).
//   T2  Both models resolve the same `customer` template (D-12; Model 1 deployable since task 228 —
//       before it, Model 1 failed closed) — ResolveTemplateAsync, split from deploy by task 245b.
//   T3  RG-ensure ARM rejection (403) -> BicepDeployOutcome.Failure, domain
//       result (does NOT throw).
//   T4  Deployment ARM rejection (400 quota) -> BicepDeployOutcome.Failure.
//   T5  Manifest missing the requested template key -> throws
//       InvalidOperationException (infra-configuration fault, MAY throw per
//       IBicepDeployRunner's contract).
//   T7  Task 245b: the resolved template's Version is the SHA-256 of the
//       downloaded bytes; a manifest sha256 that disagrees throws
//       InvalidDataException (nothing is deployed from unvouched bytes).
//   T6  Deploy call asserted to have actually reached ARM (RequestedUris
//       assertion) — not a hard-coded Success.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmDeploymentRunnerTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string CustomerId = "acme";

    private static BicepInfraDeployOptions NewOptions() => new()
    {
        ProvisioningArtifactsContainerUri = "https://fakeaccount.blob.core.windows.net/provisioning-artifacts",
        ArmManifestBlobName = "provisioning-arm-latest.json",
    };

    private const string L2PrincipalObjectId = "7d1f0c3e-2b6a-4c55-9e1d-3a8b5c6d7e8f";

    private static IOptions<ControlPlaneIdentityOptions> Identity() =>
        Options.Create(new ControlPlaneIdentityOptions { PrincipalObjectId = L2PrincipalObjectId });

    private static BicepDeployRequest NewRequest(string tenancyModel = "Model2") => new(
        CustomerId: CustomerId,
        TenantId: "00000000-1111-2222-3333-444444444444",
        SubscriptionId: SubscriptionId,
        TenancyModel: tenancyModel,
        Template: new ResolvedArmTemplate("customer", "customer-arm-2026.08.19-1.json", """{"resources":[]}""", "abc123"),
        EnvironmentName: "prod",
        Location: "westus2",
        SignalREnabled: false);

    // ---------- T1 happy path ----------

    [Fact]
    public async Task DeployAsync_HappyPath_CustomerTemplate_MapsOutputsAndReturnsSuccess()
    {
        var handler = ArmSdkTestFakes.NewHandler(RespondHappyPath);

        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            Identity(),
            NullLogger<ArmDeploymentRunner>.Instance);

        var outcome = await runner.DeployAsync(NewRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<BicepDeployOutcome.Success>().Subject;
        success.Outputs.ResourceGroupName.Should().Be("rg-spaarke-acme-prod");
        success.Outputs.CosmosEndpoint.Should().Be("https://spaarke-acme-prod-cosmos.documents.azure.com:443/");
        success.Outputs.SignalRDeployed.Should().BeFalse();
        // Task 245a — customer.bicep outputs H2a now persists for downstream handlers.
        success.Outputs.KeyVaultName.Should().Be("sprk-acme-prod-kv");
        success.Outputs.KeyVaultUri.Should().Be("https://sprk-acme-prod-kv.vault.azure.net/");
        success.Outputs.ServiceBusFullyQualifiedNamespace.Should().Be("spaarke-acme-prod-sbus.servicebus.windows.net");
        // Task 242 — the Managed Redis endpoint, verbatim (host:10000).
        success.Outputs.RedisEndpoint.Should().Be("sprk-acme-prod-redis.westus2.redis.azure.net:10000");
        // Task 246 — the Content Safety endpoint, verbatim.
        success.Outputs.ContentSafetyEndpoint.Should().Be("https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/");
        // Honest-empty per the file-header "BLOCKING DISCOVERY" note — customer.bicep
        // does not currently produce these; the runner must NOT fabricate values.
        success.Outputs.UserAssignedIdentityObjectId.Should().BeEmpty();
        success.Outputs.AppServiceName.Should().BeEmpty();
        success.Outputs.OpenAiEndpoint.Should().BeEmpty();

        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase),
            "asserts the ARM deployment CreateOrUpdate call was actually invoked over HTTP, not a hard-coded Success");
    }

    [Theory]
    [InlineData("https://spaarke-acme-prod-sbus.servicebus.windows.net:443/", "spaarke-acme-prod-sbus.servicebus.windows.net")]
    [InlineData("https://spaarke-acme-prod-sbus.servicebus.windows.net/", "spaarke-acme-prod-sbus.servicebus.windows.net")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("not a uri", "")]
    public void ServiceBusFullyQualifiedNamespaceFromEndpoint_ReturnsTheEndpointHost(string endpoint, string expected)
    {
        // The FQNS is parsed from the authoritative ARM endpoint, never composed from
        // the naming convention; blank/unparseable → empty → H2a reports the output incomplete.
        ArmDeploymentRunner.ServiceBusFullyQualifiedNamespaceFromEndpoint(endpoint).Should().Be(expected);
    }

    // ---------- Task 249: the L2 principal reaches customer.bicep for Model 1 stamps only ----------

    [Theory]
    [InlineData("Model1", true)]
    [InlineData("Model2", false)]
    public async Task DeployAsync_SendsTheConfiguredL2Principal_ForModel1StampsOnly(string tenancyModel, bool expectSent)
    {
        string? deploymentBody = null;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            if (request.Method == HttpMethod.Put && request.Content is not null
                && request.RequestUri!.AbsolutePath.Contains("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase))
            {
                deploymentBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            return RespondHappyPath(request);
        });
        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            Identity(),
            NullLogger<ArmDeploymentRunner>.Instance);

        await runner.DeployAsync(NewRequest(tenancyModel), CancellationToken.None);

        deploymentBody.Should().NotBeNull("the deployment PUT carries the parameters payload");
        using var body = System.Text.Json.JsonDocument.Parse(deploymentBody!);
        var parameters = body.RootElement.GetProperty("properties").GetProperty("parameters");
        if (expectSent)
        {
            parameters.GetProperty("controlPlaneUamiPrincipalId").GetProperty("value").GetString()
                .Should().Be(L2PrincipalObjectId, "a Model 1 stamp grants L2 Website Contributor on its BFF (H4b + H9)");
        }
        else
        {
            parameters.TryGetProperty("controlPlaneUamiPrincipalId", out _).Should().BeFalse(
                "a Model 2 stamp is in the customer's tenant — a role assignment cannot name a Spaarke-tenant principal");
        }
    }

    // ---------- T2 both models deploy the dedicated customer stamp (D-12; T228 made Model 1 deployable) ----------

    [Fact]
    public async Task ResolveTemplateAsync_BothModels_ResolveTheSameCustomerTemplate()
    {
        var model1 = await NewRunner(TemplateHandler(TemplateBody, Sha256Of(TemplateBody))).ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1, CancellationToken.None);
        var model2 = await NewRunner(TemplateHandler(TemplateBody, Sha256Of(TemplateBody))).ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2, CancellationToken.None);

        model1.Json.Should().Be(TemplateBody, "Model 1 is the same dedicated stamp, in Spaarke's tenant");
        model1.Version.Should().Be(model2.Version);
    }

    // ---------- T3 RG-ensure ARM rejection ----------

    [Fact]
    public async Task DeployAsync_ResourceGroupCreateRejected_ReturnsFailureDomainResult_DoesNotThrow()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("provisioning-arm-latest.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmManifestBody());
            }
            if (path.EndsWith("customer-arm-2026.08.19-1.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{"resources":[]}""");
            }
            if (path.Contains("resourcegroups", StringComparison.OrdinalIgnoreCase))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have authorization."));
            }
            throw new InvalidOperationException("must not reach deployment call: " + path);
        });

        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            Identity(),
            NullLogger<ArmDeploymentRunner>.Instance);

        var act = async () => await runner.DeployAsync(NewRequest(), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        var failure = result.Subject.Should().BeOfType<BicepDeployOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Resource-group ensure failed");
    }

    // ---------- T4 deployment ARM rejection ----------

    [Fact]
    public async Task DeployAsync_DeploymentRejected_ReturnsFailureDomainResult()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("provisioning-arm-latest.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmManifestBody());
            }
            if (path.EndsWith("customer-arm-2026.08.19-1.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{"resources":[]}""");
            }
            if (path.Contains("resourcegroups", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("providers", StringComparison.OrdinalIgnoreCase))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ResourceGroupBody(SubscriptionId, "rg-spaarke-acme-prod"));
            }
            if (path.Contains("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.BadRequest, ArmSdkTestFakes.ArmErrorBody("QuotaExceeded", "Cognitive Services capacity 0/150 for gpt-4o in westus2."));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            Identity(),
            NullLogger<ArmDeploymentRunner>.Instance);

        var outcome = await runner.DeployAsync(NewRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<BicepDeployOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("QuotaExceeded");
    }

    // ---------- T5 manifest missing requested template ----------

    [Fact]
    public async Task ResolveTemplateAsync_ManifestMissingTemplateKey_Throws()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("provisioning-arm-latest.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{ "templates": { } }""");
            }
            throw new InvalidOperationException("must not reach the template blob: " + path);
        });

        var act = async () => await NewRunner(handler).ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*armJsonBlobName*");
    }

    // ---------- T7 template content version (task 245b) ----------

    private const string TemplateBody = """{"resources":[{"type":"Microsoft.Storage/storageAccounts"}]}""";

    private static string Sha256Of(string body) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private static FakeArmHttpMessageHandler TemplateHandler(string body, string? manifestSha256) =>
        ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("provisioning-arm-latest.json"))
            {
                var sha = manifestSha256 is null ? string.Empty : $", \"sha256\": \"{manifestSha256}\"";
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    $$"""{ "templates": { "customer": { "armJsonBlobName": "customer-arm-1.json"{{sha}} } } }""");
            }
            if (path.EndsWith("customer-arm-1.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, body);
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

    [Fact]
    public async Task ResolveTemplateAsync_VersionIsTheSha256OfTheDownloadedBytes_SameBytesSameVersion()
    {
        var first = await NewRunner(TemplateHandler(TemplateBody, Sha256Of(TemplateBody))).ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2, CancellationToken.None);
        var second = await NewRunner(TemplateHandler(TemplateBody, manifestSha256: null)).ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2, CancellationToken.None);
        var changed = await NewRunner(TemplateHandler(TemplateBody + " ", manifestSha256: null)).ResolveTemplateAsync(
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2, CancellationToken.None);

        first.Version.Should().Be(Sha256Of(TemplateBody));
        first.Json.Should().Be(TemplateBody);
        second.Version.Should().Be(first.Version, "the same template bytes give the same version");
        changed.Version.Should().NotBe(first.Version, "changed bytes give a new version");
    }

    [Fact]
    public async Task ResolveTemplateAsync_ManifestSha256Disagrees_ThrowsInvalidData()
    {
        var act = async () => await NewRunner(TemplateHandler(TemplateBody, Sha256Of("something else")))
            .ResolveTemplateAsync(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*does not vouch*");
    }

    private static ArmDeploymentRunner NewRunner(FakeArmHttpMessageHandler handler) => new(
        ArmSdkTestFakes.NewArmClient(handler),
        ArmSdkTestFakes.NewBlobContainerClient(handler),
        Options.Create(NewOptions()),
        Identity(),
        NullLogger<ArmDeploymentRunner>.Instance);

    private static HttpResponseMessage RespondHappyPath(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("provisioning-arm-latest.json"))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmManifestBody());
        }
        if (path.EndsWith("customer-arm-2026.08.19-1.json"))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{"resources":[]}""");
        }
        if (path.Contains("resourcegroups", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("providers", StringComparison.OrdinalIgnoreCase))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ResourceGroupBody(SubscriptionId, "rg-spaarke-acme-prod"));
        }
        if (path.Contains("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmDeploymentSuccessBody(
                "customer-acme-1",
                outputs: """
                {
                  "resourceGroupName": { "type": "String", "value": "rg-spaarke-acme-prod" },
                  "cosmosAccountEndpoint": { "type": "String", "value": "https://spaarke-acme-prod-cosmos.documents.azure.com:443/" },
                  "userAssignedIdentityResourceId": { "type": "String", "value": "" },
                  "keyVaultName": { "type": "String", "value": "sprk-acme-prod-kv" },
                  "keyVaultUri": { "type": "String", "value": "https://sprk-acme-prod-kv.vault.azure.net/" },
                  "serviceBusEndpoint": { "type": "String", "value": "https://spaarke-acme-prod-sbus.servicebus.windows.net:443/" },
                  "redisEndpoint": { "type": "String", "value": "sprk-acme-prod-redis.westus2.redis.azure.net:10000" },
                  "contentSafetyEndpoint": { "type": "String", "value": "https://sprk-acme-prod-contentsafety.cognitiveservices.azure.com/" },
                  "signalrEnabled": { "type": "Bool", "value": false }
                }
                """));
        }
        throw new InvalidOperationException("unexpected request: " + path);
    }
}

/// <summary>
/// Task 123 (Wave G-2) extension of the shared <see cref="ArmSdkTestFakes"/>
/// partial class — body builders + client factory for ARM deployment /
/// resource-group / blob-storage fake-transport tests.
/// </summary>
internal static partial class ArmSdkTestFakes
{
    public static BlobContainerClient NewBlobContainerClient(FakeArmHttpMessageHandler handler)
    {
        var options = new Azure.Storage.Blobs.BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };
        return new BlobContainerClient(
            new Uri("https://fakeaccount.blob.core.windows.net/provisioning-artifacts"),
            new FakeStorageTokenCredential(),
            options);
    }

    public static string ArmManifestBody() =>
        """
        {
          "buildId": "2026.08.19-1",
          "templates": {
            "customer": { "armJsonBlobName": "customer-arm-2026.08.19-1.json" }
          }
        }
        """;

    public static string ResourceGroupBody(string subscriptionId, string resourceGroupName) =>
        $$"""
        {
          "id": "/subscriptions/{{subscriptionId}}/resourceGroups/{{resourceGroupName}}",
          "name": "{{resourceGroupName}}",
          "location": "westus2",
          "properties": { "provisioningState": "Succeeded" }
        }
        """;

    public static string ArmDeploymentSuccessBody(string deploymentName, string outputs) =>
        $$"""
        {
          "id": "/subscriptions/22222222-3333-4444-5555-666666666666/providers/Microsoft.Resources/deployments/{{deploymentName}}",
          "name": "{{deploymentName}}",
          "properties": {
            "provisioningState": "Succeeded",
            "outputs": {{outputs}}
          }
        }
        """;

    private sealed class FakeStorageTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-storage-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
