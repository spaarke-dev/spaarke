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
//   T2  Model1Shared routes the manifest lookup to the "model1-shared" key.
//   T3  RG-ensure ARM rejection (403) -> BicepDeployOutcome.Failure, domain
//       result (does NOT throw).
//   T4  Deployment ARM rejection (400 quota) -> BicepDeployOutcome.Failure.
//   T5  Manifest missing the requested template key -> throws
//       InvalidOperationException (infra-configuration fault, MAY throw per
//       IBicepDeployRunner's contract).
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

    private static BicepDeployRequest NewRequest(string tenancyModel = "Model2Dedicated") => new(
        CustomerId: CustomerId,
        TenantId: "00000000-1111-2222-3333-444444444444",
        SubscriptionId: SubscriptionId,
        TenancyModel: tenancyModel,
        BicepVersion: "abc123",
        EnvironmentName: "prod",
        Location: "westus2",
        SignalREnabled: false);

    // ---------- T1 happy path ----------

    [Fact]
    public async Task DeployAsync_HappyPath_CustomerTemplate_MapsOutputsAndReturnsSuccess()
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
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmDeploymentSuccessBody(
                    "customer-acme-1",
                    outputs: """
                    {
                      "resourceGroupName": { "type": "String", "value": "rg-spaarke-acme-prod" },
                      "cosmosAccountEndpoint": { "type": "String", "value": "https://spaarke-acme-prod-cosmos.documents.azure.com:443/" },
                      "userAssignedIdentityResourceId": { "type": "String", "value": "" },
                      "signalrEnabled": { "type": "Bool", "value": false }
                    }
                    """));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArmDeploymentRunner>.Instance);

        var outcome = await runner.DeployAsync(NewRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<BicepDeployOutcome.Success>().Subject;
        success.Outputs.ResourceGroupName.Should().Be("rg-spaarke-acme-prod");
        success.Outputs.CosmosEndpoint.Should().Be("https://spaarke-acme-prod-cosmos.documents.azure.com:443/");
        success.Outputs.SignalRDeployed.Should().BeFalse();
        // Honest-empty per the file-header "BLOCKING DISCOVERY" note — customer.bicep
        // does not currently produce these; the runner must NOT fabricate values.
        success.Outputs.UserAssignedIdentityObjectId.Should().BeEmpty();
        success.Outputs.AppServiceName.Should().BeEmpty();
        success.Outputs.OpenAiEndpoint.Should().BeEmpty();

        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase),
            "asserts the ARM deployment CreateOrUpdate call was actually invoked over HTTP, not a hard-coded Success");
    }

    // ---------- T2 Model1Shared template routing ----------

    [Fact]
    public async Task DeployAsync_Model1Shared_ResolvesModel1SharedTemplateFromManifest()
    {
        var requestedTemplateBlob = string.Empty;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("provisioning-arm-latest.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmManifestBody());
            }
            if (path.EndsWith("model1-shared-arm-2026.08.19-1.json"))
            {
                requestedTemplateBlob = path;
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
                    "customer-acme-1", outputs: """{ "resourceGroupName": { "type": "String", "value": "rg-spaarke-acme-prod" } }"""));
            }
            throw new InvalidOperationException("unexpected request: " + path);
        });

        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArmDeploymentRunner>.Instance);

        var outcome = await runner.DeployAsync(NewRequest(tenancyModel: "Model1Shared"), CancellationToken.None);

        outcome.Should().BeOfType<BicepDeployOutcome.Success>();
        requestedTemplateBlob.Should().Contain("model1-shared-arm-2026.08.19-1.json");
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
            NullLogger<ArmDeploymentRunner>.Instance);

        var outcome = await runner.DeployAsync(NewRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<BicepDeployOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("QuotaExceeded");
    }

    // ---------- T5 manifest missing requested template ----------

    [Fact]
    public async Task DeployAsync_ManifestMissingTemplateKey_Throws()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("provisioning-arm-latest.json"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{ "templates": { } }""");
            }
            throw new InvalidOperationException("must not reach RG/deploy calls: " + path);
        });

        var runner = new ArmDeploymentRunner(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArmDeploymentRunner>.Instance);

        var act = async () => await runner.DeployAsync(NewRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*armJsonBlobName*");
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
            "customer": { "armJsonBlobName": "customer-arm-2026.08.19-1.json" },
            "model1-shared": { "armJsonBlobName": "model1-shared-arm-2026.08.19-1.json" }
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
