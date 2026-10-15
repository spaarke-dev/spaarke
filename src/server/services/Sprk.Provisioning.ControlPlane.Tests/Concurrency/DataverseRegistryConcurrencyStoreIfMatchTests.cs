using System.Net.Http;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Concurrency;

/// <summary>
/// The CustomerRunGuard's If-Match header, built from the registry row's <c>@odata.etag</c> exactly as Dataverse
/// returns it. T186 (2026-10-14): Dataverse sends weak ETags (<c>W/"27691597"</c>, observed live on spaarkedev1) and
/// the store built the header with <c>new EntityTagHeaderValue(etag, isWeak: false)</c>, which throws on that form —
/// every POST /api/runs against a real registry row was a 500. <see cref="CustomerRunGuardTests"/> use an in-memory
/// store, so they never reached this code.
/// </summary>
public class DataverseRegistryConcurrencyStoreIfMatchTests
{
    [Fact]
    public void WeakEtagFromDataverse_BecomesAWeakIfMatchWithTheSameTag()
    {
        var header = DataverseRegistryConcurrencyStore.ParseIfMatch("W/\"27691597\"");

        header.IsWeak.Should().BeTrue();
        header.Tag.Should().Be("\"27691597\"");
        header.ToString().Should().Be("W/\"27691597\"");
    }

    [Fact]
    public void StrongEtag_BecomesAStrongIfMatch()
    {
        var header = DataverseRegistryConcurrencyStore.ParseIfMatch("\"abc\"");

        header.IsWeak.Should().BeFalse();
        header.ToString().Should().Be("\"abc\"");
    }

    [Fact]
    public void WeakEtag_IsSentVerbatimOnTheRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "https://example.crm.dynamics.com/api/data/v9.2/x(1)");
        request.Headers.IfMatch.Add(DataverseRegistryConcurrencyStore.ParseIfMatch("W/\"27691597\""));

        request.Headers.GetValues("If-Match").Should().ContainSingle().Which.Should().Be("W/\"27691597\"");
    }

    [Theory]
    [InlineData("27691597")]
    [InlineData("W/27691597")]
    public void UnquotedEtag_IsRejected(string etag)
    {
        var act = () => DataverseRegistryConcurrencyStore.ParseIfMatch(etag);

        act.Should().Throw<FormatException>();
    }
}
