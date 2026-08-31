using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for ContainerTypeSettingsEndpoints (SPE-052).
///
/// Tests cover:
///   - ValidSharingCapabilities set (ADR-007: input validation rules) and the validation logic
///     that consumes it (configId / sharingCapability)
///
/// Note (task 042): DTO-shape, domain-record, auth-filter, and error-code-naming tests previously
/// here were removed as build-class scaffolding (ADR-038 §7). ADR-007 Graph SDK isolation for nested
/// domain records under this facade is covered generically by
/// tests/Spaarke.ArchTests/ADR007_NestedDomainRecordTests.cs.
/// </summary>
public class UpdateContainerTypeSettingsTests
{
    #region ValidSharingCapabilities Tests

    [Theory]
    // 🔴 CORRECTED 2026-08-24 (task 023). This theory previously asserted that "view", "edit", and
    // "full" were valid. None of them is a Graph value — the real set is the members of
    // Microsoft.Graph.Models.SharingCapabilities, which is what the SPE Admin client has always sent.
    // Because ValidSharingCapabilities IS the endpoint's validation allow-list, the effect was that
    // every value the client could send except "disabled" was rejected with a 400 by our own
    // validator. These tests are why it survived: correcting the list would have "broken tests", so
    // the wrong values looked load-bearing.
    [InlineData("disabled")]
    [InlineData("externalUserSharingOnly")]
    [InlineData("existingExternalUserSharingOnly")]
    [InlineData("externalUserAndGuestSharing")]
    public void ValidSharingCapabilities_ContainsAllAllowedValues(string capability)
    {
        SpeAdminGraphService.ValidSharingCapabilities
            .Should().Contain(capability, $"'{capability}' is a valid sharing capability");
    }

    [Theory]
    [InlineData("DISABLED")]
    [InlineData("EXTERNALUSERSHARINGONLY")]
    [InlineData("ExistingExternalUserSharingOnly")]
    [InlineData("externalUserAndGuestSharing")]
    public void ValidSharingCapabilities_IsCaseInsensitive(string capability)
    {
        // HashSet uses OrdinalIgnoreCase comparer
        SpeAdminGraphService.ValidSharingCapabilities
            .Contains(capability)
            .Should().BeTrue($"'{capability}' is a valid sharing capability (case-insensitive)");
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("none")]
    [InlineData("all")]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void ValidSharingCapabilities_DoesNotContainInvalidValues(string capability)
    {
        SpeAdminGraphService.ValidSharingCapabilities
            .Contains(capability)
            .Should().BeFalse($"'{capability}' is not a valid sharing capability");
    }

    [Fact]
    public void ValidSharingCapabilities_HasExactlyFourEntries()
    {
        // The count is the arity of Microsoft.Graph.Models.SharingCapabilities (minus the
        // UnknownFutureValue forward-compat sentinel), not a hand-picked number — the set is built
        // from Enum.GetNames<SharingCapabilities>() in SpeAdminGraphService. The real four are
        // disabled / externalUserSharingOnly / existingExternalUserSharingOnly /
        // externalUserAndGuestSharing (negative-controlled in SharingCapability_InvalidValues_FailValidation
        // below for the three wrong names — view/edit/full — this allow-list carried until 2026-08-24).
        // If the Graph SDK ever adds a member, this test failing is informative: it means the allow-list
        // grew and callers should learn about the new value, not that the test itself is wrong.
        SpeAdminGraphService.ValidSharingCapabilities
            .Should().HaveCount(4, "the set is derived from Microsoft.Graph.Models.SharingCapabilities " +
                "(excluding UnknownFutureValue), which currently has 4 real members");
    }

    #endregion

    #region Validation Logic Tests

    [Theory]
    [InlineData("disabled")]
    [InlineData("externalUserSharingOnly")]
    [InlineData("existingExternalUserSharingOnly")]
    [InlineData("externalUserAndGuestSharing")]
    [InlineData("Disabled")]                   // case-insensitive
    [InlineData("EXTERNALUSERSHARINGONLY")]    // uppercase
    public void SharingCapability_ValidValues_PassValidation(string capability)
    {
        var isValid = SpeAdminGraphService.ValidSharingCapabilities.Contains(capability);

        isValid.Should().BeTrue($"'{capability}' is a valid sharing capability that should pass validation");
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("admin")]
    [InlineData("restricted")]
    [InlineData("unknown")]
    // The three names this allow-list wrongly accepted until 2026-08-24. Kept as explicit negatives
    // so re-adding any of them fails here.
    [InlineData("view")]
    [InlineData("edit")]
    [InlineData("full")]
    public void SharingCapability_InvalidValues_FailValidation(string capability)
    {
        var isValid = SpeAdminGraphService.ValidSharingCapabilities.Contains(capability);

        isValid.Should().BeFalse($"'{capability}' is not a valid sharing capability and should fail validation");
    }

    #endregion
}
