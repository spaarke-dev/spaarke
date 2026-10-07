using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.OptionsValidation;

/// <summary>
/// unified-access-control-r2 task 123 (D-14) — the customer runtime discriminator.
/// </summary>
/// <remarks>
/// <para>These pin the two properties the mechanism exists for. The first is that NEITHER path may
/// invent a value: an unresolved identity stays unresolved rather than becoming a placeholder, because
/// a placeholder used as a cache-key prefix is a key SHARED by every customer — the same defect
/// task 122 found in the Foundry agent-thread key, where compile-time constants occupied the tenant
/// slot and produced one global thread.</para>
/// <para>The second is the deny-list. Deriving the id from <c>WEBSITE_RESOURCE_GROUP</c> is what lets
/// existing per-customer stamps work with no deployment change — but <c>rg-spaarke-platform-{env}</c>
/// matches the per-customer shape exactly, and the platform stamp is a real, live resource group that
/// hosts the BFF. Without the deny-list it would derive <c>customerId = "platform"</c>: a silently
/// invented customer, arrived at through the very mechanism meant to prevent one. That case is the
/// reason this file exists, so it is asserted directly rather than left to the shape rules.</para>
/// <para>Namespace is <c>OptionsValidation</c>, not <c>Configuration</c> — a
/// <c>Sprk.Bff.Api.Tests.Configuration</c> namespace shadows <c>Sprk.Bff.Api.Configuration</c> for
/// every test referring to production options as <c>Configuration.X</c>.</para>
/// </remarks>
[Trait("Category", "Configuration")]
public class CustomerIdResolverTests
{
    // ---------------------------------------------------------------------------------------------
    // Precedence
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ExplicitSetting_WinsOverTheResourceGroup()
    {
        var (id, source) = CustomerIdResolver.Resolve("acme", "rg-spaarke-other-prod");

        id.Should().Be("acme");
        source.Should().Be(CustomerIdSource.Explicit);
    }

    [Fact]
    public void ResourceGroup_IsUsedOnlyWhenTheExplicitSettingIsAbsent()
    {
        var (id, source) = CustomerIdResolver.Resolve(null, "rg-spaarke-acme-prod");

        id.Should().Be("acme");
        source.Should().Be(CustomerIdSource.DerivedFromResourceGroup);
    }

    [Fact]
    public void NeitherSource_ResolvesToNothing_NotToAPlaceholder()
    {
        var (id, source) = CustomerIdResolver.Resolve(null, null);

        id.Should().BeNull();
        source.Should().Be(CustomerIdSource.Unresolved);
    }

    // ---------------------------------------------------------------------------------------------
    // 🔴 The deny-list. These resource groups are live and are NOT customers.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // platform.bicep:123 + platform-controlplane.bicep:209 — hosts the BFF and the L2 control plane.
    [InlineData("rg-spaarke-platform-prod")]
    [InlineData("rg-spaarke-platform-dev")]
    // model1-shared.bicep:84 — the retired Model 1 shared tier.
    [InlineData("rg-spaarke-shared-prod")]
    // infrastructure/byok/main.bicep:19.
    [InlineData("rg-spaarke-byok-prod")]
    public void NonCustomerResourceGroups_DoNotDeriveACustomer(string resourceGroupName)
    {
        var (id, source) = CustomerIdResolver.Resolve(null, resourceGroupName);

        id.Should().BeNull(
            "'{0}' occupies the customerId position but names a platform function, not a customer — "
            + "deriving from it would invent a customer that does not exist",
            resourceGroupName);
        source.Should().Be(CustomerIdSource.Unresolved);
    }

    // ---------------------------------------------------------------------------------------------
    // Resource-group shape. Anchored: exactly two dash-separated segments after the prefix.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // scripts/Deploy-BffApi.ps1 default — one segment, not a per-customer group.
    [InlineData("rg-spaarke-dev")]
    // The retired model1-customer shape (model1-shared.bicep:209) — three segments.
    [InlineData("rg-spaarke-acme-prod-model1")]
    // Not ours at all.
    [InlineData("SharePointEmbedded")]
    [InlineData("rg-something-else-prod")]
    [InlineData("")]
    [InlineData("   ")]
    public void ResourceGroupsThatAreNotPerCustomer_DoNotDerive(string resourceGroupName)
    {
        var (id, source) = CustomerIdResolver.Resolve(null, resourceGroupName);

        id.Should().BeNull();
        source.Should().Be(CustomerIdSource.Unresolved);
    }

    [Fact]
    public void ResourceGroupCase_IsNormalised_BecauseAzurePreservesWhateverWasTyped()
    {
        // Azure resource-group names are case-insensitive; 'RG-Spaarke-Acme-Prod' names the same group
        // Bicep would have created as 'rg-spaarke-acme-prod'. Failing over letter case would be a
        // spurious startup failure.
        var (id, source) = CustomerIdResolver.Resolve(null, "RG-Spaarke-Acme-Prod");

        id.Should().Be("acme");
        source.Should().Be(CustomerIdSource.DerivedFromResourceGroup);
    }

    [Theory]
    // Leading digit — violates the standard, so the naming convention derivation depends on no longer
    // holds. Assert rather than parse-and-hope (D-14 §8).
    [InlineData("rg-spaarke-1acme-prod")]
    // Too long: the standard caps at 8 because 'sprk-{id}-staging-kv' must stay a valid Key Vault name.
    [InlineData("rg-spaarke-toolongid-prod")]
    // Too short.
    [InlineData("rg-spaarke-ab-prod")]
    public void DerivedIdThatViolatesTheStandard_IsRejectedRatherThanReturned(string resourceGroupName)
    {
        var (id, source) = CustomerIdResolver.Resolve(null, resourceGroupName);

        id.Should().BeNull();
        source.Should().Be(CustomerIdSource.Unresolved);
    }

    // ---------------------------------------------------------------------------------------------
    // The explicit setting is held to the canonical form — it is the authoritative value.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // The shape currently sitting in sprk_dataverseenvironment.sprk_customerid: 16 chars with hyphens.
    // It would also fail the Bicep @maxLength(8); cpo-r1 is re-issuing these
    // (INCOMING-CUSTOMERID-STANDARD.md).
    [InlineData("trial-2026-08-18")]
    // Hyphens are the dangerous case: the storage-account name strips them
    // (replace(...,'-','')), so 'acme-x' and 'acmex' resolve to the SAME account with nothing
    // validating the collision.
    [InlineData("acme-x")]
    [InlineData("ACME")]
    [InlineData("1acme")]
    [InlineData("ab")]
    [InlineData("toolongid")]
    [InlineData("acme_x")]
    public void ExplicitValueThatViolatesTheStandard_IsNotAccepted(string explicitValue)
    {
        var (id, source) = CustomerIdResolver.Resolve(explicitValue, resourceGroupName: null);

        id.Should().BeNull();
        source.Should().Be(CustomerIdSource.Unresolved);
    }

    [Theory]
    [InlineData("acm")]       // minimum length
    [InlineData("acme")]
    [InlineData("nwind")]     // the abbreviation worked example from the cpo-r1 handoff
    [InlineData("a1b2c3d4")]  // maximum length, digits after the leading letter
    public void ExplicitValueMatchingTheStandard_IsAccepted(string explicitValue)
    {
        var (id, source) = CustomerIdResolver.Resolve(explicitValue, resourceGroupName: null);

        id.Should().Be(explicitValue);
        source.Should().Be(CustomerIdSource.Explicit);
    }

    [Fact]
    public void ExplicitValue_IsTrimmed_BecauseAppSettingsPickUpStrayWhitespace()
    {
        var (id, source) = CustomerIdResolver.Resolve("  acme  ", null);

        id.Should().Be("acme");
        source.Should().Be(CustomerIdSource.Explicit);
    }

    [Fact]
    public void BlankExplicitValue_FallsThroughToDerivation_RatherThanFailing()
    {
        var (id, source) = CustomerIdResolver.Resolve("   ", "rg-spaarke-acme-prod");

        id.Should().Be("acme");
        source.Should().Be(CustomerIdSource.DerivedFromResourceGroup);
    }

    // ---------------------------------------------------------------------------------------------
    // The operator-facing message. It is what an on-call engineer sees at 3am; it has to name both
    // sources and say what to set.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void UnresolvedMessage_NamesBothSourcesAndTheSettingToSet()
    {
        var message = CustomerIdResolver.BuildUnresolvedMessage(null, "rg-spaarke-platform-prod");

        message.Should().Contain(CustomerIdResolver.ExplicitSettingEnvironmentName);
        message.Should().Contain(CustomerIdResolver.ResourceGroupEnvironmentVariable);
        message.Should().Contain("rg-spaarke-platform-prod");
    }

    [Fact]
    public void UnresolvedMessage_QuotesAnIllegalExplicitValue_SoTheTypoIsVisible()
    {
        var message = CustomerIdResolver.BuildUnresolvedMessage("trial-2026-08-18", null);

        message.Should().Contain("trial-2026-08-18");
        message.Should().Contain(CustomerIdResolver.CustomerIdPattern);
    }
}
