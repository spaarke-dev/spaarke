using System.Net;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Unit tests for the missing-entity tolerance logic in
/// <see cref="DataverseCapabilityManifestLoader"/>.
///
/// Verifies fix for chat resilience (task 083): when the `sprk_aicapability` table
/// is not provisioned in the target environment (e.g., fresh dev), the loader returns
/// an empty manifest instead of throwing — chat tool detection then proceeds with
/// zero tools available instead of bubbling a 500 to the SSE stream.
/// </summary>
public sealed class DataverseCapabilityManifestLoaderTests
{
    // ── IsMissingEntityResponse ──────────────────────────────────────────────

    [Fact]
    public void IsMissingEntityResponse_ReturnsTrue_OnNotFound()
    {
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.NotFound, body: "anything")
            .Should().BeTrue();
    }

    [Fact]
    public void IsMissingEntityResponse_ReturnsTrue_OnNotFound_WithEmptyBody()
    {
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.NotFound, body: string.Empty)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"Resource not found for the segment 'sprk_aicapabilities'.\"}}")]
    [InlineData("Could not find a property named 'sprk_keywordhints'")]
    [InlineData("Entity sprk_aicapability does not exist")]
    [InlineData("resource not found for the segment")] // case-insensitive
    public void IsMissingEntityResponse_ReturnsTrue_OnBadRequest_WithMissingEntityMessage(string body)
    {
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.BadRequest, body)
            .Should().BeTrue(because: $"body '{body}' indicates a missing entity");
    }

    [Fact]
    public void IsMissingEntityResponse_ReturnsFalse_OnBadRequest_WithGenericMessage()
    {
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.BadRequest, body: "{\"error\":{\"message\":\"Invalid filter syntax.\"}}")
            .Should().BeFalse();
    }

    [Fact]
    public void IsMissingEntityResponse_ReturnsFalse_OnInternalServerError()
    {
        // Transient 5xx must NOT be treated as missing-entity — stale-on-error policy
        // depends on the loader throwing so ManifestRefreshService retains the prior manifest.
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.InternalServerError, body: "boom")
            .Should().BeFalse();
    }

    [Fact]
    public void IsMissingEntityResponse_ReturnsFalse_OnUnauthorized()
    {
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.Unauthorized, body: "no token")
            .Should().BeFalse();
    }

    [Fact]
    public void IsMissingEntityResponse_ReturnsFalse_OnBadRequest_WithEmptyBody()
    {
        DataverseCapabilityManifestLoader
            .IsMissingEntityResponse(HttpStatusCode.BadRequest, body: string.Empty)
            .Should().BeFalse();
    }
}
