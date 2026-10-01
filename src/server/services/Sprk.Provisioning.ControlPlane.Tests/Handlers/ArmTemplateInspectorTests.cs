// -----------------------------------------------------------------------------
// ArmTemplateInspectorTests.cs
//
// Task 245b — ArmTemplateInspector checks the ARM template H2a deploys:
//   R2 (ADR-020) every OpenAI model deployment is pinned — not `latest`, blank,
//      absent, or an ARM expression;
//   R3 (HANDLER-10 / F16) no `keyVaultReferenceIdentity: 'SystemAssigned'`.
// The real compiled infrastructure/bicep/customer.json passes; each rule has a
// negative case built from the shapes `az bicep build` emits.
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmTemplateInspectorTests
{
    private static BicepTemplateInspectionResult Inspect(string json) =>
        new ArmTemplateInspector(NullLogger<ArmTemplateInspector>.Instance).Inspect(new BicepDeployRequest(
            CustomerId: "acme",
            TenantId: "00000000-1111-2222-3333-444444444444",
            SubscriptionId: "22222222-3333-4444-5555-666666666666",
            TenancyModel: "Model2",
            Template: new ResolvedArmTemplate("customer", "customer-arm-1.json", json, "v"),
            EnvironmentName: "prod",
            Location: "westus2",
            SignalREnabled: false));

    // The shape openai.bicep's `deployments` module parameter compiles to (nested template default value).
    private static string Descriptor(string versionMember) =>
        "{\"resources\":[{\"properties\":{\"template\":{\"parameters\":{\"deployments\":{\"defaultValue\":" +
        "[{\"name\":\"gpt-4o\",\"model\":\"gpt-4o\"" + versionMember + ",\"capacity\":10}]}}}}}]}";

    [Fact]
    public void Inspect_TheCompiledCustomerTemplate_IsClean()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "bicep", "customer.json"));

        var result = Inspect(json);

        result.HasUnpinnedModelDeployment.Should().BeFalse(result.UnpinnedModelReference);
        result.HasInvalidKvRefIdentity.Should().BeFalse(result.KvRefIdentityReference);
    }

    [Theory]
    [InlineData(",\"version\":\"latest\"")]
    [InlineData(",\"version\":\"  \"")]
    [InlineData(",\"version\":\"[parameters('gpt4oVersion')]\"")]
    [InlineData("")]   // no version member at all
    public void Inspect_UnpinnedModelDescriptor_FailsR2(string versionMember)
    {
        var result = Inspect(Descriptor(versionMember));

        result.HasUnpinnedModelDeployment.Should().BeTrue();
        result.UnpinnedModelReference.Should().Contain("gpt-4o");
    }

    [Fact]
    public void Inspect_PinnedModelDescriptor_PassesR2()
        => Inspect(Descriptor(",\"version\":\"2024-11-20\"")).HasUnpinnedModelDeployment.Should().BeFalse();

    [Fact]
    public void Inspect_DeploymentResourceWithoutModelVersion_FailsR2()
    {
        const string json = """
            {"resources":[{"type":"Microsoft.CognitiveServices/accounts/deployments","name":"acct/gpt-4o",
              "properties":{"model":{"format":"OpenAI","name":"gpt-4o"}}}]}
            """;

        var result = Inspect(json);

        result.HasUnpinnedModelDeployment.Should().BeTrue();
        result.UnpinnedModelReference.Should().Contain("no properties.model.version");
    }

    [Fact]
    public void Inspect_SystemAssignedKeyVaultReferenceIdentity_FailsR3()
    {
        const string json = """{"resources":[{"type":"Microsoft.Web/sites","properties":{"keyVaultReferenceIdentity":"SystemAssigned"}}]}""";

        var result = Inspect(json);

        result.HasInvalidKvRefIdentity.Should().BeTrue();
        result.KvRefIdentityReference.Should().Contain("SystemAssigned");
    }

    [Fact]
    public void Inspect_MalformedTemplate_Throws()
    {
        var act = () => Inspect("{ not json");

        act.Should().Throw<JsonException>();
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with RunContextContractTests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
