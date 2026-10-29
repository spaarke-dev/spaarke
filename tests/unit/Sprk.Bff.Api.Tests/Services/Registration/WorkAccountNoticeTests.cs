using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// Slice B3 — the work-account notice (<see cref="ExternalInviteService.BuildWorkAccountNotice"/>) through the REAL portal
/// notification request and shipped template (<see cref="RegistrationEmailService.BuildPortalNotificationRequest"/>): a
/// record name is plain text, HTML-encoded in the body, with every control character removed from the subject. Pure.
/// </summary>
public class WorkAccountNoticeTests
{
    private const string PortalUrl = "https://external.test";

    private static string Template()
    {
        using var stream = typeof(RegistrationEmailService).Assembly.GetManifestResourceStream(
            "Sprk.Bff.Api.Services.Registration.EmailTemplates.PortalNotificationTemplate.html");
        stream.Should().NotBeNull();
        return new StreamReader(stream!).ReadToEnd();
    }

    private static Sprk.Bff.Api.Services.Communication.Models.SendCommunicationRequest Request(ExternalInviteService.WorkAccountNotice n)
        => RegistrationEmailService.BuildPortalNotificationRequest(
            Template(), "person@customer.example", n.Subject, n.Headline, n.Message, n.LinkUrl, n.LinkText, "corr-1", 2026,
            "mailbox-central@spaarke.com");

    [Fact]
    public void ARecordNameWithMarkup_IsEncodedInTheBody_NeverRendered()
    {
        var notice = ExternalInviteService.BuildWorkAccountNotice(
            PortalUrl, "acme", new ExternalInviteService.WorkAccountNoticeRecord("matter", "<script>alert(1)</script> & Co"))!;

        var body = Request(notice).Body;

        body.Should().NotContain("<script>");
        body.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt; &amp; Co");
    }

    [Fact]
    public void TheSubject_HasNoControlCharacters()
    {
        var notice = ExternalInviteService.BuildWorkAccountNotice(
            PortalUrl, "acme", new ExternalInviteService.WorkAccountNoticeRecord("matter", "Smith\r\nBcc: x@y.test\tv.\u0007 Jones\u2028"))!;

        notice.Subject.Should().Be("You've been given access to the matter \"SmithBcc: x@y.testv. Jones\" in Spaarke");
        notice.Subject.Should().NotContainAny("\r", "\n", "\t", "\u0007", "\u2028");
        Request(notice).Subject.Should().Be(notice.Subject);
    }

    [Fact]
    public void WithNoRecord_TheNoticeIsTheFixedText()
    {
        var notice = ExternalInviteService.BuildWorkAccountNotice(PortalUrl, "acme", record: null)!;

        notice.Subject.Should().Be("You've been invited to Spaarke");
        notice.Message.Should().Contain("Sign in with your work account");
        notice.LinkUrl.Should().Be("https://external.test/?customer=acme");
    }

    [Fact]
    public void TheLink_CarriesNoCode_AndAnUnusablePortalUrlBuildsNoNotice()
    {
        ExternalInviteService.BuildWorkAccountNotice(PortalUrl, "acme", record: null)!.LinkUrl
            .Should().NotContain("#").And.NotContain("invite=");
        ExternalInviteService.BuildWorkAccountNotice("http://external.test", "acme", record: null).Should().BeNull();
    }
}
