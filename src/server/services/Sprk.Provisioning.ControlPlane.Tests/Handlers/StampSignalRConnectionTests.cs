// -----------------------------------------------------------------------------
// StampSignalRConnectionTests.cs
//
// ISS-022 (plan D36). The keyless managed-identity SignalR connection string H4b writes on a stamp:
//   - built from the deployed host name + the stamp UAMI client id, in the Management SDK's AuthType=azure.msi form;
//   - never carries an AccessKey (the stamp's SignalR has local auth disabled);
//   - null (setting not written) when SignalR is not deployed or an input is not a plain host / GUID — a ';' or '='
//     in the host must not be able to inject another connection-string field.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class StampSignalRConnectionTests
{
    private const string Host = "sprk-acme-prod-signalr.service.signalr.net";
    private const string ClientId = "00000000-1111-2222-3333-555555555555";

    [Fact]
    public void Deployed_BuildsTheManagedIdentityString_FromHostAndUamiClientId()
    {
        StampSignalRConnection.ManagedIdentityConnectionString(Host, ClientId).Should().Be(
            "Endpoint=https://sprk-acme-prod-signalr.service.signalr.net;AuthType=azure.msi;ClientId=00000000-1111-2222-3333-555555555555;Version=1.0;");
    }

    [Theory]
    [InlineData("https://sprk-acme-prod-signalr.service.signalr.net")]
    [InlineData("https://sprk-acme-prod-signalr.service.signalr.net/")]
    [InlineData("  sprk-acme-prod-signalr.service.signalr.net  ")]
    public void AcceptsASchemeOrTrailingSlash_ButEmitsOneHttpsEndpoint(string host)
    {
        StampSignalRConnection.ManagedIdentityConnectionString(host, ClientId)
            .Should().StartWith("Endpoint=https://sprk-acme-prod-signalr.service.signalr.net;AuthType=azure.msi;");
    }

    [Fact]
    public void NeverContainsAnAccessKey()
    {
        var value = StampSignalRConnection.ManagedIdentityConnectionString(Host, ClientId);

        value.Should().NotBeNull();
        value!.Should().NotContainEquivalentOf("AccessKey");
    }

    [Theory]
    [InlineData(null, ClientId)]
    [InlineData("", ClientId)]
    [InlineData("   ", ClientId)]
    [InlineData(Host, null)]
    [InlineData(Host, "")]
    [InlineData(Host, "not-a-guid")]
    [InlineData("host;AccessKey=abc", ClientId)]          // injection of another field
    [InlineData("host.signalr.net;Version=1.0", ClientId)]
    [InlineData("localhost", ClientId)]                    // not a dotted service host
    [InlineData("https://a.b/path", ClientId)]
    public void NotDeployedOrMalformedInput_YieldsNull_SoNothingIsWritten(string? host, string? clientId)
    {
        StampSignalRConnection.ManagedIdentityConnectionString(host, clientId).Should().BeNull();
    }

    [Theory]
    [InlineData(Host, "true")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void EnabledValue_IsTrueOnlyWhenSignalRWasDeployed(string? host, string? expected)
    {
        StampSignalRConnection.EnabledValue(host).Should().Be(expected);
    }
}
