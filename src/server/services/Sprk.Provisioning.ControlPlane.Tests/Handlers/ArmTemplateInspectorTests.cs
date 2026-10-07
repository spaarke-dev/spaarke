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

    /// <summary>
    /// Task 225b: ARM rejects a deployment parameter the template does not declare. Every key
    /// <see cref="ArmDeploymentRunner.BuildParametersPayload"/> can send (optional ones populated) must be a
    /// parameter of the real compiled <c>customer.json</c> — the check that would have caught a runner still
    /// sending a parameter the template dropped (e.g. the retired <c>requireSecretFreeIdentity</c>).
    /// </summary>
    [Fact]
    public void RealCustomerTemplate_DeclaresEveryParameterTheRunnerSends()
    {
        using var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "bicep", "customer.json")));
        var declared = template.RootElement.GetProperty("parameters").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var request = new BicepDeployRequest(
            CustomerId: "acme",
            TenantId: "00000000-1111-2222-3333-444444444444",
            SubscriptionId: "22222222-3333-4444-5555-666666666666",
            TenancyModel: "Model1",   // task 249: the superset — Model 1 also carries controlPlaneUamiPrincipalId
            Template: new ResolvedArmTemplate("customer", "customer-arm.json", "{}", "v1"),
            EnvironmentName: "prod",
            Location: "westus2",
            SignalREnabled: true,
            OpenAiLocation: "westus3");
        using var payload = JsonDocument.Parse(ArmDeploymentRunner.BuildParametersPayload(request, "7d1f0c3e-2b6a-4c55-9e1d-3a8b5c6d7e8f").ToString());
        var sent = payload.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        sent.Should().NotBeEmpty();
        sent.Should().OnlyContain(name => declared.Contains(name),
            "every parameter the runner sends must be declared by customer.json, or ARM rejects the deployment");
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
