using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 35 item 3 — the pure rule for which Key Vault secret names a container
/// type config may name: ONE pinned prefix (taken from the live configs' naming), then Key Vault's own name rule.
/// ADR-038 §2 path #6 (pure domain logic).
/// </summary>
public sealed class SpeConfigSecretNamePolicyTests
{
    [Theory]
    [InlineData("spe-owning-app-secret")]        // the live dev config's name (read 2026-10-04)
    [InlineData("spe-owning-app-acme")]
    [InlineData("SPE-Owning-App-Acme-2")]        // Key Vault names are case-insensitive, so is the rule
    [InlineData("spe-owning-app-x")]
    public void AConformingName_IsAllowed(string name) =>
        SpeConfigSecretNamePolicy.IsAllowed(name).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]                          // the other live dev config's stored value
    [InlineData("AzureOpenAI-ApiKey")]            // another secret in the BFF vault
    [InlineData("SPE-ContainerTypeId")]           // in the vault, starts with "spe-", is not an owning-app secret
    [InlineData("spe-owning-app-")]               // the prefix alone
    [InlineData("spe-owning-apps")]
    [InlineData(" spe-owning-app-acme")]          // never trimmed into conformity
    [InlineData("spe-owning-app-acme/../redis")]  // no path character may reach the vault request
    [InlineData("spe-owning-app-acme?api-version=1")]
    [InlineData("spe-owning-app-a_b")]            // Key Vault allows letters, digits and hyphens only
    [InlineData("spe-owning-app-secret\n")]       // round 41 item 5: '$' matched before a trailing newline; '\z' does not
    [InlineData("spe-owning-app-secret\r\n")]
    public void ANameOutsideTheRule_IsRefused(string? name) =>
        SpeConfigSecretNamePolicy.IsAllowed(name).Should().BeFalse();

    [Fact]
    public void TheLimit_IsKeyVaultsOwn127Characters()
    {
        var atLimit = SpeConfigSecretNamePolicy.RequiredPrefix + new string('a', 127 - SpeConfigSecretNamePolicy.RequiredPrefix.Length);

        SpeConfigSecretNamePolicy.IsAllowed(atLimit).Should().BeTrue();
        SpeConfigSecretNamePolicy.IsAllowed(atLimit + "a").Should().BeFalse();
    }
}
