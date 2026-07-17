using System.Net.Http;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// The MSCRMCallerID impersonation-header contract (messaging read-access rework, 2026-07-16). This is a
/// leak-prevention control: the header carries the identity Dataverse impersonates, so the messaging read query
/// returns only the caller's rows. If the header value or the opt-in semantics regressed, either the wrong user's
/// rows would return (over-share) or every consumer would silently gain an impersonation header (breaking app-only
/// callers). Verified without a transport mock (ADR-038 bans Mock&lt;HttpMessageHandler&gt;) by inspecting the
/// per-request message the helper stamps.
/// </summary>
public class DataverseImpersonationTests
{
    private static readonly Guid SystemUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Apply_WithSystemUserId_SetsMscrmCallerIdHeaderToThatId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "sprk_communications");

        DataverseImpersonation.Apply(request, SystemUserId);

        DataverseImpersonation.CallerIdHeader.Should().Be("MSCRMCallerID");
        request.Headers.GetValues("MSCRMCallerID").Single().Should().Be(SystemUserId.ToString());
    }

    [Fact]
    public void Apply_WithNull_AddsNoHeader_SoAppOnlyRequestsAreUnchanged()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "sprk_communications");

        DataverseImpersonation.Apply(request, null);

        request.Headers.Contains("MSCRMCallerID").Should().BeFalse();
    }

    [Fact]
    public void Apply_WithEmptyGuid_AddsNoHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "sprk_communications");

        DataverseImpersonation.Apply(request, Guid.Empty);

        request.Headers.Contains("MSCRMCallerID").Should().BeFalse();
    }

    [Fact]
    public void Apply_CalledTwice_ReplacesRatherThanAccumulatesTheIdentity()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "sprk_communications");
        var second = Guid.Parse("55555555-5555-5555-5555-555555555555");

        DataverseImpersonation.Apply(request, SystemUserId);
        DataverseImpersonation.Apply(request, second);

        request.Headers.GetValues("MSCRMCallerID").Should().ContainSingle().Which.Should().Be(second.ToString());
    }
}
