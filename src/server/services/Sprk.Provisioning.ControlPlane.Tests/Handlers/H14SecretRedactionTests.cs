// -----------------------------------------------------------------------------
// H14SecretRedactionTests.cs
//
// Unit tests over the pure-function secret-redaction helpers on
// GraphRestSubscriptionCreator + DataverseWebApiServiceEndpointWebhookRegistrar
// (task 073 code-review hardening — defense-in-depth against a theoretical
// Dataverse/Graph error-response echo of a write-only secret field). These two
// classes are otherwise "NOT under test in the CI unit suite" (real REST
// calls — parity with GraphRestAppRoleGranter / DataverseWebApiAppUserCreator),
// but RedactSecret is a pure string function safely testable without HTTP.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14SecretRedactionTests
{
    [Fact]
    public void GraphRestSubscriptionCreator_RedactSecret_RemovesLiteralOccurrence()
    {
        var body = "{\"error\":{\"message\":\"Invalid clientState 'super-secret-hmac-key' for subscription.\"}}";
        var redacted = GraphRestSubscriptionCreator.RedactSecret(body, "super-secret-hmac-key");
        redacted.Should().NotContain("super-secret-hmac-key");
        redacted.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void GraphRestSubscriptionCreator_RedactSecret_NoOccurrence_ReturnsUnchanged()
    {
        var body = "{\"error\":{\"message\":\"Resource not found.\"}}";
        var redacted = GraphRestSubscriptionCreator.RedactSecret(body, "super-secret-hmac-key");
        redacted.Should().Be(body);
    }

    [Fact]
    public void GraphRestSubscriptionCreator_RedactSecret_EmptyBodyOrSecret_ReturnsUnchanged()
    {
        GraphRestSubscriptionCreator.RedactSecret("", "secret").Should().Be("");
        GraphRestSubscriptionCreator.RedactSecret("body", "").Should().Be("body");
    }

    [Fact]
    public void DataverseWebApiServiceEndpointWebhookRegistrar_RedactSecret_RemovesLiteralOccurrence()
    {
        var body = "{\"error\":{\"message\":\"Invalid saskey 'super-secret-hmac-key' for serviceendpoint.\"}}";
        var redacted = DataverseWebApiServiceEndpointWebhookRegistrar.RedactSecret(body, "super-secret-hmac-key");
        redacted.Should().NotContain("super-secret-hmac-key");
        redacted.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void DataverseWebApiServiceEndpointWebhookRegistrar_RedactSecret_NoOccurrence_ReturnsUnchanged()
    {
        var body = "{\"error\":{\"message\":\"Duplicate name.\"}}";
        var redacted = DataverseWebApiServiceEndpointWebhookRegistrar.RedactSecret(body, "super-secret-hmac-key");
        redacted.Should().Be(body);
    }
}
