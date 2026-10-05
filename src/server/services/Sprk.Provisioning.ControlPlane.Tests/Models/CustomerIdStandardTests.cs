// -----------------------------------------------------------------------------
// CustomerIdStandardTests.cs
//
// T237 (2026-09-30) — pins the customerId standard at the provisioning intake
// edge (docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "The customerId
// standard"). The BFF restates the same pattern in CustomerIdResolver; its own
// tests cover the BFF copy.
//
// TEST SCOPE: the length bounds (3 and 8 accepted; 2 and 9 rejected), the
// character rule (uppercase, hyphen, underscore, leading digit rejected), and
// reject-not-repair (padded values, including a trailing newline that .NET's
// `$` would otherwise accept, are rejected). Parity with intake.schema.json is
// pinned by IntakeSchemaProfileParityTests.CustomerIdPattern_MatchesCustomerIdStandard_Exactly.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class CustomerIdStandardTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("acme")]
    [InlineData("nwind")]
    [InlineData("abcdefgh")]
    [InlineData("a1b2c3")]
    public void IsValid_CompliantId_ReturnsTrue(string value)
    {
        CustomerIdStandard.IsValid(value).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]            // too short
    [InlineData("abcdefghi")]     // 9 chars — customer.bicep's Key Vault name would be truncated
    [InlineData("Acme")]          // uppercase
    [InlineData("acme-x")]        // hyphen — storage name strips it and collides with "acmex"
    [InlineData("1acme")]         // leading digit
    [InlineData("acme_x")]        // underscore
    [InlineData(" acme")]         // padded — rejected, not trimmed
    [InlineData("acme ")]
    [InlineData("acme\n")]        // .NET `$` matches before a final newline; must still be rejected
    public void IsValid_NonCompliantId_ReturnsFalse(string? value)
    {
        CustomerIdStandard.IsValid(value).Should().BeFalse();
    }

    // These match the pattern but name non-customer resource groups (rg-spaarke-platform-{env}
    // hosts the BFF + L2), so intake must refuse them separately.
    [Theory]
    [InlineData("platform")]
    [InlineData("shared")]
    [InlineData("byok")]
    public void IsReserved_NonCustomerSegment_ReturnsTrue(string value)
    {
        CustomerIdStandard.IsReserved(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("platfor")]
    [InlineData(null)]
    public void IsReserved_OrdinaryId_ReturnsFalse(string? value)
    {
        CustomerIdStandard.IsReserved(value).Should().BeFalse();
    }
}
