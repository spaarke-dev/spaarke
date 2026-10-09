// -----------------------------------------------------------------------------
// WorkerL2OwnedOptionsBootTests.cs
//
// Task 245b (G25) — the run inputs L2 owns are Worker configuration, validated
// at startup (ValidateOnStart), not run parameters:
//   - ControlPlaneIdentityOptions.PrincipalObjectId (task 245b; one option shared
//     by H4 and H2a since task 249) — the principal H4 grants Key Vault Secrets
//     Officer on each customer vault (L2's own UAMI, never the stamp's) and H2a
//     sends as customer.bicep's controlPlaneUamiPrincipalId on Model 1 stamps. (The vendor-key platform-vault option went with the
//     vendor keys — task 225b, owner D18 2026-10-02.)
//   - SpeContainerOptions.ContainerTypeOwners — the SPE owning app per container
//     type ({ContainerTypeId, OwnerAppId} — task 248: L2 signs in as it through the
//     Worker UAMI's federated credential, so no certificate is configured), read by
//     H0's SpeOwnerCredential probe, H8 and H13's T6 probe.
// Each factory below boots the REAL Worker composition root with one setting
// broken; starting the host must throw (ValidateOnStart), naming the setting —
// the tests never read the options themselves, so removing ValidateOnStart
// fails them. The control factory with a complete owner entry starts.
// -----------------------------------------------------------------------------

extern alias WorkerHost;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Xunit;
using WorkerProgram = WorkerHost::Program;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class WorkerL2OwnedOptionsBootTests
{
    [Fact]
    public void MissingL2Principal_FailsHostStart()
    {
        using var factory = new L2OptionsWorkerTestFactory(b =>
            b.UseSetting("ControlPlaneIdentity:PrincipalObjectId", string.Empty));

        // Host start alone (ValidateOnStart) must fail — nothing reads the options here.
        var act = () => factory.Services;

        act.Should().Throw<InvalidOperationException>().WithMessage("*ControlPlaneIdentity:PrincipalObjectId*");
    }

    [Theory]
    [InlineData("ReservedTenants:SpaarkeTenantId", "*ReservedTenants:SpaarkeTenantId*")]
    [InlineData("ReservedTenants:CiamTenantIds:0", "*ReservedTenants:CiamTenantIds:0*")]
    public void BlankReservedTenant_FailsHostStart(string setting, string expectedMessage)
    {
        // T255: H4b and H13 refuse Spaarke's tenant and the CIAM tenant as customer workforce tenants — a Worker that
        // does not know them must not start (the Api host registers the same options).
        using var factory = new L2OptionsWorkerTestFactory(b => b.UseSetting(setting, " "));

        var act = () => factory.Services;

        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("ContainerTypeId")]
    [InlineData("OwnerAppId")]
    public void IncompleteSpeOwnerEntry_FailsHostStart(string blankSetting)
    {
        using var factory = new L2OptionsWorkerTestFactory(b =>
        {
            WithCompleteOwner(b);
            b.UseSetting($"SpeContainerOptions:ContainerTypeOwners:0:{blankSetting}", " ");
        });

        // Host start alone (ValidateOnStart) must fail — nothing reads the options here.
        var act = () => factory.Services;

        act.Should().Throw<InvalidOperationException>().WithMessage($"*ContainerTypeOwners:0*{blankSetting}*");
    }

    [Fact]
    public void BlankGuestRoleName_FailsHostStart()
    {
        // T232: a blank role name would otherwise surface only in the first Model 1 run's H11, after H0–H10.
        using var factory = new L2OptionsWorkerTestFactory(b =>
        {
            WithCompleteOwner(b);
            b.UseSetting("H11UserProvisioningOptions:GuestSecurityRoleNames:0", " ");
        });

        var act = () => factory.Services;

        act.Should().Throw<OptionsValidationException>().WithMessage("*GuestSecurityRoleNames*");
    }

    [Fact]
    public void ConfiguredGuestRole_StartsHost_AndReplacesTheDefault()
    {
        using var factory = new L2OptionsWorkerTestFactory(b =>
        {
            WithCompleteOwner(b);
            b.UseSetting("H11UserProvisioningOptions:GuestSecurityRoleNames:0", "Spaarke Guest");
        });

        var options = factory.Services.GetRequiredService<IOptions<H11UserProvisioningOptions>>().Value;

        options.EffectiveGuestSecurityRoleNames.Should().Equal("Spaarke Guest");
    }

    [Fact]
    public void CompleteSpeOwnerEntry_StartsHost_AndIsFoundByContainerType()
    {
        using var factory = new L2OptionsWorkerTestFactory(WithCompleteOwner);

        var options = factory.Services.GetRequiredService<IOptions<SpeContainerOptions>>().Value;

        options.TryGetOwner("CCCCCCCC-DDDD-EEEE-FFFF-000000000001", out var owner).Should().BeTrue(
            "the container type id is matched as a GUID (case-insensitive), as the intake carries it");
        owner!.OwnerAppId.Should().Be("77777777-8888-9999-aaaa-bbbbbbbbbbbb");
    }

    private static void WithCompleteOwner(IWebHostBuilder b)
    {
        b.UseSetting("SpeContainerOptions:ContainerTypeOwners:0:ContainerTypeId", "cccccccc-dddd-eeee-ffff-000000000001");
        b.UseSetting("SpeContainerOptions:ContainerTypeOwners:0:OwnerAppId", "77777777-8888-9999-aaaa-bbbbbbbbbbbb");
    }

    /// <summary>The secret-free Worker fixture (WorkerSecretFreeBootTests) plus one per-test adjustment.</summary>
    private sealed class L2OptionsWorkerTestFactory(Action<IWebHostBuilder> adjust) : StartGatedWorkerTestFactory
    {
        protected override void ConfigureWorker(IWebHostBuilder builder)
        {
            SecretFreeWorkerTestFactory.ApplyCommonWorkerFixtureSettings(builder);
            builder.UseSetting("EnvVarValues:Credentials:Order:0", "ManagedIdentityFederated");
            builder.UseSetting("EnvVarValues:Credentials:RequireSecretFreeIdentity", "true");
            builder.UseSetting("SolutionImportOptions:Credentials:Order:0", "ManagedIdentityFederated");
            builder.UseSetting("SolutionImportOptions:Credentials:RequireSecretFreeIdentity", "true");
            builder.UseSetting("ManagedIdentity:ClientId", "11111111-aaaa-bbbb-cccc-222222222222");
            adjust(builder);
        }
    }
}
