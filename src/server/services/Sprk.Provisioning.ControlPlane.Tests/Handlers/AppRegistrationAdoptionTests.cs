// -----------------------------------------------------------------------------
// AppRegistrationAdoptionTests.cs
//
// T240a review (F1, tenant isolation): H3 finds the customer's BFF registration by its predictable display name
// spaarke-bff-api-{customerId}. Anyone in the tenant who may register apps could pre-create that name; H3 would then
// give it the stamp's federated credential and H10 would make it the customer's Dataverse administrator. H3 adopts an
// existing registration only when nobody but the control plane can act as it. The decision is the pure
// AdoptionRefusal, pinned here (the Graph reads around it follow the provisioner's no-live-Graph test precedent).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class AppRegistrationAdoptionTests
{
    private const string ControlPlane = "12345678-1234-1234-1234-123456789abc";
    private const string Squatter = "87654321-4321-4321-4321-cba987654321";
    private const string FicName = "spaarke-uami-trust";

    private static string? Refusal(
        Application app, string[] owners, string[] fics, string? controlPlane = ControlPlane, bool secretFree = true)
        => GraphAppRegistrationProvisioner.AdoptionRefusal(app, owners, fics, controlPlane, FicName, secretFree);

    [Fact]
    public void OwnedOnlyByTheControlPlane_NoCredentials_OnlyOurFic_IsAdopted()
        => Refusal(new Application(), [ControlPlane], [FicName]).Should().BeNull();

    [Fact]
    public void NoOwnerAtAll_IsAdopted_NobodyCanAddACredentialLater()
        => Refusal(new Application(), [], []).Should().BeNull();

    [Fact]
    public void AnotherOwner_IsRefused_TheyCouldAddACredentialAfterH3()
        => Refusal(new Application(), [ControlPlane, Squatter], [FicName]).Should().Contain(Squatter);

    [Fact]
    public void AClientSecret_IsRefused_OnASecretFreeStamp()
    {
        var app = new Application { PasswordCredentials = [new PasswordCredential { DisplayName = "x" }] };

        Refusal(app, [], []).Should().Contain("1 client secret(s)");
    }

    [Fact]
    public void ACertificate_IsRefused_OnASecretFreeStamp()
    {
        var app = new Application { KeyCredentials = [new KeyCredential { DisplayName = "x" }] };

        Refusal(app, [], []).Should().Contain("1 certificate(s)");
    }

    [Fact]
    public void AForeignFederatedCredential_IsRefused()
        => Refusal(new Application(), [ControlPlane], [FicName, "my-own-trust"]).Should().Contain("my-own-trust");

    [Fact]
    public void Model2_SkipsTheOwnerCheck_ButStillRefusesCredentials()
    {
        Refusal(new Application(), [Squatter], [], controlPlane: null).Should().BeNull();
        var app = new Application { PasswordCredentials = [new PasswordCredential()] };
        Refusal(app, [Squatter], [], controlPlane: null).Should().NotBeNull();
    }

    [Fact]
    public void EveryReason_IsReported_NotJustTheFirst()
    {
        var app = new Application { PasswordCredentials = [new PasswordCredential()] };

        var refusal = Refusal(app, [Squatter], ["my-own-trust"]);

        refusal.Should().Contain("client secret").And.Contain("my-own-trust").And.Contain(Squatter);
    }

    [Fact]
    public void OwnerIdsCompare_IgnoringCase()
        => Refusal(new Application(), [ControlPlane.ToUpperInvariant()], []).Should().BeNull();
}
