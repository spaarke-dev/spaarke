using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Graph;

/// <summary>
/// Graph sharing-token construction for the FR-01 identity resolver (spaarkeai-word-add-in-r1 task 012). The first
/// two URL strings are the ones observed LIVE for one SPE file: what <c>Office.context.document.url</c> returned (raw
/// spaces) and what the BFF's own open-links returned (<c>%20</c>) — spike-1 §19 and §21.
/// </summary>
public class SharingUrlTokenTests
{
    private const string HostReturnedUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx";

    private const string BffReturnedUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document%20Library/Examiner%20report%20draft.docx";

    [Fact]
    public void BuildCandidates_FromTheHostsRawUrl_TriesTheBffSpellingFirst_ThenTheHostSpelling()
    {
        SharingUrlToken.BuildCandidates(new Uri(HostReturnedUrl)).Should().Equal(
            (SharingUrlToken.EncodedForm, BffReturnedUrl),
            (SharingUrlToken.RawForm, HostReturnedUrl));
    }

    [Fact]
    public void BuildCandidates_FromAnAlreadyEncodedUrl_StillReachesBothObservedSpellings()
    {
        // The first form re-encodes the caller's %20 (it assumes a raw path, which is what Office sends); the
        // normalized and raw forms that follow are the two spellings observed live.
        SharingUrlToken.BuildCandidates(new Uri(BffReturnedUrl)).Should().Equal(
            (SharingUrlToken.EncodedForm, BffReturnedUrl.Replace("%20", "%2520")),
            (SharingUrlToken.NormalizedForm, BffReturnedUrl),
            (SharingUrlToken.RawForm, HostReturnedUrl));
    }

    [Fact]
    public void BuildCandidates_WhenNothingNeedsEscaping_ProducesOneCandidate()
    {
        const string url = "https://spaarke.sharepoint.com/contentstorage/CSP_1/Documents/brief.docx";

        SharingUrlToken.BuildCandidates(new Uri(url))
            .Should().Equal((SharingUrlToken.EncodedForm, url));
    }

    [Fact]
    public void BuildCandidates_AHashInTheFileName_IsEncoded_NotTreatedAsAFragment()
    {
        // '#' is legal in a SharePoint file name and Office returns it raw. Parsed as a URI it would start a
        // fragment, and Graph would be handed ".../Draft " — a different file.
        const string raw = "https://spaarke.sharepoint.com/contentstorage/CSP_1/Document Library/Draft #3.docx";

        SharingUrlToken.BuildCandidates(new Uri(raw))[0].Should().Be(
            (SharingUrlToken.EncodedForm, "https://spaarke.sharepoint.com/contentstorage/CSP_1/Document%20Library/Draft%20%233.docx"));
    }

    [Fact]
    public void BuildCandidates_ALiteralPercentInTheFileName_IsEncoded_NotReadAsAnEscape()
    {
        // A file literally named "Q1%20draft.docx": read as an escape it would resolve its sibling "Q1 draft.docx".
        const string raw = "https://spaarke.sharepoint.com/contentstorage/CSP_1/Documents/Q1%20draft.docx";

        SharingUrlToken.BuildCandidates(new Uri(raw))[0].Should().Be(
            (SharingUrlToken.EncodedForm, "https://spaarke.sharepoint.com/contentstorage/CSP_1/Documents/Q1%2520draft.docx"));
    }

    [Theory]
    [InlineData(HostReturnedUrl)]
    [InlineData(BffReturnedUrl)]
    // Lengths 1, 2 and 3 mod 3 — the three padding cases base64 produces.
    [InlineData("https://a.sharepoint.com/x")]
    [InlineData("https://a.sharepoint.com/xy")]
    [InlineData("https://a.sharepoint.com/xyz")]
    public void Encode_IsUnpaddedBase64Url_ThatRoundTripsToTheExactUrl(string url)
    {
        var token = SharingUrlToken.Encode(url);

        token.Should().StartWith("u!");
        var body = token[2..];
        body.Should().NotContainAny("=", "+", "/");

        var base64 = body.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Should().Be(url);
    }

    [Fact]
    public void Encode_TheTwoObservedSpellings_ProduceDifferentTokens()
    {
        // The reason more than one spelling is tried: Graph sees two different tokens for the same file.
        SharingUrlToken.Encode(HostReturnedUrl).Should().NotBe(SharingUrlToken.Encode(BffReturnedUrl));
    }
}
