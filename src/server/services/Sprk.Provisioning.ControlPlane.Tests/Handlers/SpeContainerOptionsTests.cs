// -----------------------------------------------------------------------------
// SpeContainerOptionsTests.cs
//
// Task 245b — SpeContainerOptions.ContainerTypeOwners is the SPE owning-app
// credential per container type (H0 / H8 / T6). Validate() runs at Worker
// startup; TryGetOwner() selects by the run's containerTypeId. Covers the
// topology-R1 rules (one owner per container type, one container type per
// owner, one certificate per owner) and id canonicalisation. Blank-field
// startup failures are covered end to end by WorkerL2OwnedOptionsBootTests.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpeContainerOptionsTests
{
    private const string TypeA = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string TypeB = "cccccccc-dddd-eeee-ffff-000000000002";
    private const string OwnerA = "77777777-8888-9999-aaaa-bbbbbbbbbbb1";
    private const string OwnerB = "77777777-8888-9999-aaaa-bbbbbbbbbbb2";

    private static SpeContainerTypeOwner Owner(string type, string app, string secret = "SPE-OwnerCert-Pfx") => new()
    {
        ContainerTypeId = type,
        OwnerAppId = app,
        OwnerCertKeyVaultName = "sprk-controlplane-dev-kv",
        OwnerCertSecretName = secret,
    };

    [Fact]
    public void Validate_TwoContainerTypes_EachWithItsOwnOwnerAndCertificate_Passes()
    {
        var options = new SpeContainerOptions { ContainerTypeOwners = [Owner(TypeA, OwnerA, "SPE-OwnerCert-Pfx"), Owner(TypeB, OwnerB, "SPE-Model1-OwnerCert-Pfx")] };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_SameContainerTypeTwice_Throws()
    {
        var options = new SpeContainerOptions { ContainerTypeOwners = [Owner(TypeA, OwnerA, "c1"), Owner(TypeA.ToUpperInvariant(), OwnerB, "c2")] };

        options.Invoking(o => o.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*listed more than once*");
    }

    [Fact]
    public void Validate_OneOwnerForTwoContainerTypes_Throws()
    {
        var options = new SpeContainerOptions { ContainerTypeOwners = [Owner(TypeA, OwnerA, "c1"), Owner(TypeB, OwnerA, "c2")] };

        options.Invoking(o => o.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*owns exactly one container type*");
    }

    [Fact]
    public void Validate_TwoOwnersSharingOneCertificateSecret_Throws()
    {
        var options = new SpeContainerOptions { ContainerTypeOwners = [Owner(TypeA, OwnerA), Owner(TypeB, OwnerB)] };

        options.Invoking(o => o.Validate()).Should().Throw<InvalidOperationException>().WithMessage("*already the certificate of owning app*");
    }

    [Fact]
    public void TryGetOwner_MatchesAsAGuid_AndReturnsCanonicalIds()
    {
        var options = new SpeContainerOptions { ContainerTypeOwners = [Owner("{" + TypeA.ToUpperInvariant() + "}", "{" + OwnerA.ToUpperInvariant() + "}")] };

        options.TryGetOwner($"  {TypeA}\n", out var owner).Should().BeTrue();
        owner!.ContainerTypeId.Should().Be(TypeA);
        owner.OwnerAppId.Should().Be(OwnerA, "the owning app id reaches Graph in canonical form, not as configured");
        options.TryGetOwner(TypeB, out _).Should().BeFalse();
        options.TryGetOwner("not-a-guid", out _).Should().BeFalse();
    }
}
