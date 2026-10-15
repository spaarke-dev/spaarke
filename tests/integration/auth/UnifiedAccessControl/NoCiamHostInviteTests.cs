using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 185 slice F (ADR-010 path C: lazy validation instead of a service locator) — a BFF host
/// with NO <c>Ciam:*</c> configuration. The CIAM provisioner validates its configuration at first use, so the shared invite
/// service (which injects it directly) resolves there, and <c>/grant</c> needs no CIAM. <c>/invite</c> and
/// <c>/invite-and-grant</c> answer a 503 ProblemDetails carrying <c>ciam_not_configured</c> (§12 I-11, the code /grant
/// reports), with nothing read or written. The grant side of the same host is
/// <c>GrantSendsInviteTests.Grant_OnAHostWithNoCiamConfiguration_*</c>.
/// KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class NoCiamHostInviteTests : IClassFixture<NoCiamHostInviteTests.NoCiamFixture>
{
    // Write and Share: what the delegation gate requires since task 179 (owner round 89).
    private const string Delegator = "ReadAccess,WriteAccess,ShareAccess";

    private readonly NoCiamFixture _fixture;

    public NoCiamHostInviteTests(NoCiamFixture fixture) => _fixture = fixture;

    /// <summary>The delegation-rule host, built without any <c>Ciam:*</c> key.</summary>
    public sealed class NoCiamFixture : DelegationRuleTestFixture
    {
        protected override bool IncludeCiamConfiguration => false;
    }

    [Theory]
    [InlineData("/api/v1/external-access/invite")]
    [InlineData("/api/v1/external-access/invite-and-grant")]
    public async Task TheInviteRoutes_Answer503CiamNotConfigured_AndReadOrWriteNothing(string path)
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CiamUserProvisioningService>().IsConfigured.Should().BeFalse(
                "the fixture really is a host without Ciam:* (no vacuous pass)");
        }

        var matterId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(Delegator);
        _fixture.DataverseClient.Invocations.Clear();

        var response = await client.PostAsJsonAsync(path, new
        {
            email = "no-ciam@firm.example",
            recordType = "matter",
            recordId = matterId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly,
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "§12 I-11: a 503, never the global handler's 500");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("reasonCode").GetString().Should().Be(
            Sprk.Bff.Api.Api.ExternalAccess.Dtos.GrantInviteStatus.CiamNotConfiguredReasonCode, "the code /grant already reports");
        _fixture.RootFlags.Reads.Should().NotContain(r => r.RecordId == matterId, "the record's policy is never read");
        _fixture.DataverseClient.Verify(
            c => c.CreateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never,
            "no contact is created");
        _fixture.DataverseClient.Verify(
            c => c.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never, "no contact is marked");
    }
}
