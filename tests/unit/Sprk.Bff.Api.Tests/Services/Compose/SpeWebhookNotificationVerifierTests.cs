using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="SpeWebhookNotificationVerifier"/> — the FR-26 SPE webhook receiver's
/// (task 053) security-sensitive verification logic: the Graph subscription-validation handshake
/// detection, constant-time clientState comparison, and driveId-from-resource parsing.
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> (pure static class, zero I/O, zero mocks) —
/// per <c>tests/CLAUDE.md</c> "Authoring Template — Unit (DOMAIN LOGIC ONLY)". Every assertion
/// pins an actual acceptance criterion from task 053: the validation-token echo, the
/// verified-vs-rejected notification split, and the resource-path parse.
/// </para>
/// </summary>
public class SpeWebhookNotificationVerifierTests
{
    // =========================================================================
    // TryGetValidationToken — Graph subscription-validation handshake
    // =========================================================================

    [Fact]
    public void TryGetValidationToken_WhenPresent_ReturnsTrueAndEchoesToken()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["validationToken"] = "graph-handshake-token-abc123",
        });

        var found = SpeWebhookNotificationVerifier.TryGetValidationToken(query, out var token);

        found.Should().BeTrue();
        token.Should().Be("graph-handshake-token-abc123");
    }

    [Fact]
    public void TryGetValidationToken_WhenAbsent_ReturnsFalse()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>());

        var found = SpeWebhookNotificationVerifier.TryGetValidationToken(query, out var token);

        found.Should().BeFalse();
        token.Should().BeNull();
    }

    [Fact]
    public void TryGetValidationToken_WhenEmptyValue_ReturnsFalse()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["validationToken"] = string.Empty,
        });

        var found = SpeWebhookNotificationVerifier.TryGetValidationToken(query, out var token);

        found.Should().BeFalse();
    }

    // =========================================================================
    // VerifyClientState — constant-time batch verification (fail-closed)
    // =========================================================================

    [Fact]
    public void VerifyClientState_WhenAllNotificationsMatch_ReturnsTrue()
    {
        var notifications = new[]
        {
            new GraphChangeNotification { SubscriptionId = "sub-1", ClientState = "correct-secret", Resource = "drives/drive-a/root" },
            new GraphChangeNotification { SubscriptionId = "sub-1", ClientState = "correct-secret", Resource = "drives/drive-a/root" },
        };

        var verified = SpeWebhookNotificationVerifier.VerifyClientState(notifications, "correct-secret", out var invalid);

        verified.Should().BeTrue();
        invalid.Should().BeNull();
    }

    [Fact]
    public void VerifyClientState_WhenOneNotificationHasWrongClientState_RejectsWholeBatch()
    {
        var notifications = new[]
        {
            new GraphChangeNotification { SubscriptionId = "sub-1", ClientState = "correct-secret", Resource = "drives/drive-a/root" },
            new GraphChangeNotification { SubscriptionId = "sub-2", ClientState = "forged-secret", Resource = "drives/drive-b/root" },
        };

        var verified = SpeWebhookNotificationVerifier.VerifyClientState(notifications, "correct-secret", out var invalid);

        verified.Should().BeFalse("a single forged/mismatched clientState must reject the entire batch, not just the offending item");
        invalid.Should().NotBeNull();
        invalid!.SubscriptionId.Should().Be("sub-2");
    }

    [Fact]
    public void VerifyClientState_WhenClientStateMissing_IsRejected()
    {
        var notifications = new[]
        {
            new GraphChangeNotification { SubscriptionId = "sub-1", ClientState = null, Resource = "drives/drive-a/root" },
        };

        var verified = SpeWebhookNotificationVerifier.VerifyClientState(notifications, "correct-secret", out var invalid);

        verified.Should().BeFalse();
        invalid.Should().NotBeNull();
    }

    // =========================================================================
    // TryExtractDriveIdFromResource — resource-path parsing
    // =========================================================================

    [Theory]
    [InlineData("drives/b!drive-xyz/root", "b!drive-xyz")]
    [InlineData("Drives/DRIVE-ABC/Root", "DRIVE-ABC")] // case-insensitive segment match
    public void TryExtractDriveIdFromResource_WithValidShape_ExtractsDriveId(string resource, string expectedDriveId)
    {
        var found = SpeWebhookNotificationVerifier.TryExtractDriveIdFromResource(resource, out var driveId);

        found.Should().BeTrue();
        driveId.Should().Be(expectedDriveId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("users/user@domain.com/messages/abc")] // a different Graph resource shape (mail)
    [InlineData("drives/")] // missing the driveId segment entirely
    public void TryExtractDriveIdFromResource_WithUnrecognizedShape_ReturnsFalse(string? resource)
    {
        var found = SpeWebhookNotificationVerifier.TryExtractDriveIdFromResource(resource, out var driveId);

        found.Should().BeFalse();
        driveId.Should().BeNull();
    }
}
