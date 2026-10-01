using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.UnifiedAccessControl;

/// <summary>
/// The caller TABLE-privilege check behind invoice confirm (unified-access-control-r2 task 130, owner decision G5:
/// "the BFF checks AS THE USER: Create on sprk_invoice …").
/// </summary>
/// <remarks>
/// <para>The OBO exchange and the HTTP call cannot run in-process; what CAN go wrong silently is reading the
/// answer. <c>RetrieveUserSetOfPrivilegesByNames</c> answers with the privileges the user HOLDS among those asked
/// — an empty array means "not held", and that must never be read as "held". The response shape is the one
/// spaarkedev1 returned to a read-only call on 2026-10-01.</para>
/// </remarks>
public class CallerPrivilegeCheckTests
{
    private const string Held = """
        {"@odata.context":"…#Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNamesResponse",
         "RolePrivileges":[{"Depth":"Deep","PrivilegeId":"da03fe43-b23e-4031-baae-306ea3f59278",
         "BusinessUnitId":"cb15f587-baa0-f111-aaac-000d3a99d1d7","PrivilegeName":"prvCreatesprk_Invoice",
         "RecordFilterId":"00000000-0000-0000-0000-000000000000","RecordFilterUniqueName":""}]}
        """;

    [Fact]
    public void PrivilegeListedInTheResponse_IsHeld()
    {
        CallerRecordAccessProbe.ResponseGrantsPrivilege(Held, "prvCreatesprk_Invoice").Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"RolePrivileges":[]}""")]                                            // asked, not held
    [InlineData("""{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvReadsprk_Invoice"}]}""")] // a different privilege
    [InlineData("""{"value":[]}""")]                                                    // no RolePrivileges array
    [InlineData("""{"RolePrivileges":"prvCreatesprk_Invoice"}""")]                     // not an array
    [InlineData("not json")]
    [InlineData("")]
    public void AnythingElse_IsNotHeld(string body)
    {
        CallerRecordAccessProbe.ResponseGrantsPrivilege(body, "prvCreatesprk_Invoice").Should().BeFalse();
    }

    [Fact]
    public async Task NoCallerToken_DeniesWithoutAnyExchange()
    {
        var probe = new CallerRecordAccessProbe(
            new HttpClient(new NoNetworkHandler()), new ConfigurationBuilder().Build(),
            NullLogger<CallerRecordAccessProbe>.Instance);

        (await probe.CallerHoldsPrivilegeAsync(null, "prvCreatesprk_Invoice")).Should().BeFalse();
    }

    /// <summary>Fails the test if anything reaches the network (hand-written, not a mocked handler).</summary>
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No request may be sent when the caller has no token.");
    }
}
