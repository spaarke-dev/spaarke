using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Pure, stateless verification helpers for the FR-26 SPE change-notification webhook receiver
/// (task 053: <c>POST /api/compose/webhooks/spe-doc-changed</c>). Extracted from the endpoint so
/// the security-sensitive verification logic (validation-token handshake detection, constant-time
/// clientState comparison, driveId parsing) is directly unit-testable without booting a host or
/// mocking the endpoint's DI graph — mirrors the equivalent logic in
/// <c>CommunicationEndpoints.HandleIncomingWebhookAsync</c> (task 044), extracted rather than
/// duplicated inline per CLAUDE.md §11 (no new I/O, no new DTO shape — reuses
/// <see cref="GraphChangeNotification"/>/<see cref="GraphChangeNotificationCollection"/>, the
/// existing generic Graph-notification model).
///
/// <para>
/// <b>No Microsoft.Graph type</b> crosses this class (ADR-007) — it only ever sees the generic
/// notification DTOs and primitive strings.
/// </para>
/// </summary>
public static class SpeWebhookNotificationVerifier
{
    /// <summary>
    /// True when the request is Microsoft Graph's subscription-validation handshake (a POST
    /// carrying a non-empty <c>validationToken</c> query parameter). Graph does not sign this
    /// probe, so it is detected before any clientState/signature verification runs.
    /// </summary>
    public static bool TryGetValidationToken(IQueryCollection query, out string? token)
    {
        if (query.TryGetValue("validationToken", out var value) && !string.IsNullOrEmpty(value))
        {
            token = value.ToString();
            return true;
        }

        token = null;
        return false;
    }

    /// <summary>
    /// Verifies every notification in the batch carries a <c>clientState</c> matching
    /// <paramref name="expectedClientState"/> via a constant-time comparison (prevents timing side
    /// channels). Fails closed on the FIRST mismatch — mirrors Graph's own webhook contract (an
    /// invalid clientState anywhere in the batch rejects the whole delivery). Returns the
    /// offending notification via <paramref name="invalidNotification"/> for logging.
    /// </summary>
    public static bool VerifyClientState(
        IReadOnlyList<GraphChangeNotification> notifications,
        string expectedClientState,
        out GraphChangeNotification? invalidNotification)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentException.ThrowIfNullOrEmpty(expectedClientState);

        var expectedBytes = Encoding.UTF8.GetBytes(expectedClientState);

        foreach (var notification in notifications)
        {
            var providedBytes = Encoding.UTF8.GetBytes(notification.ClientState ?? string.Empty);
            if (providedBytes.Length != expectedBytes.Length
                || !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                invalidNotification = notification;
                return false;
            }
        }

        invalidNotification = null;
        return true;
    }

    /// <summary>
    /// Extracts the driveId segment from a Graph subscription resource path of the shape
    /// <c>drives/{driveId}/root</c> — the exact resource
    /// <see cref="SpeSyncOrchestrator"/>'s <c>CreateDriveRootSubscriptionAsync</c> call subscribes
    /// to. Returns <c>false</c> for any other shape (defensive — an unrecognized resource is
    /// skipped by the caller, not treated as an error).
    /// </summary>
    public static bool TryExtractDriveIdFromResource(string? resource, out string? driveId)
    {
        driveId = null;
        if (string.IsNullOrWhiteSpace(resource))
        {
            return false;
        }

        var segments = resource.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "drives", StringComparison.OrdinalIgnoreCase))
            {
                driveId = segments[i + 1];
                return !string.IsNullOrEmpty(driveId);
            }
        }

        return false;
    }
}
