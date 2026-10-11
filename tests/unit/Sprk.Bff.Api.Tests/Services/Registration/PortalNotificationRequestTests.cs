using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 036 review (K3/K4): the REAL shared-mailbox request the portal notification
/// email sends - <see cref="RegistrationEmailService.BuildPortalNotificationRequest"/> over the shipped template - not a
/// test double of the send. Pure: no mailbox, no I/O beyond reading the embedded template.
/// </summary>
public class PortalNotificationRequestTests
{
    private static string Template()
    {
        using var stream = typeof(RegistrationEmailService).Assembly.GetManifestResourceStream(
            "Sprk.Bff.Api.Services.Registration.EmailTemplates.PortalNotificationTemplate.html");
        stream.Should().NotBeNull();
        return new StreamReader(stream!).ReadToEnd();
    }

    private static SendCommunicationRequest Build(
        string headline = "You were given access", string message = "Open it.", string link = "https://portal.test/?a=1&b=2",
        string linkText = "Open the matter", string subject = "Subject", string to = "partner@outside.test", int year = 2026)
        => RegistrationEmailService.BuildPortalNotificationRequest(
            Template(), to, subject, headline, message, link, linkText, "corr-1", year);

    [Fact]
    public void TheRequest_IsAddressedToTheRecipient_FromTheSharedMailbox_InSharedMailboxMode()
    {
        var request = Build();

        request.To.Should().Equal("partner@outside.test");
        request.FromMailbox.Should().Be("demo@demo.spaarke.com");
        request.SendMode.Should().Be(SendMode.SharedMailbox, "app-only through the shared mailbox; never the user's mailbox");
        request.BodyFormat.Should().Be(BodyFormat.HTML);
        request.Subject.Should().Be("Subject");
        request.CorrelationId.Should().Be("corr-1");
        request.Bcc.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ARecordNameWithMarkupAndPlaceholders_IsEncoded_AndNeverSubstituted()
    {
        const string hostile = "<b>&\"{{Year}}{{SupportEmail}}";

        var body = Build(headline: $"You were given access to the matter \"{hostile}\"", message: hostile).Body;

        body.Should().NotContain("<b>", "markup in a record name is encoded, never rendered");
        body.Should().Contain("&lt;b&gt;&amp;&quot;{{Year}}{{SupportEmail}}",
            "the literal placeholders inside a value are text, not substituted by a later placeholder");
        body.Should().Contain("&copy; 2026 Spaarke", "the template's own {{Year}} is still filled");
        body.Should().Contain("mailto:support@spaarke.com");
    }

    [Fact]
    public void TheLink_IsAttributeEncoded_AndEveryTemplatePlaceholderIsFilled()
    {
        var body = Build(link: "https://portal.test/?customer=acme&record=matter:1").Body;

        body.Should().Contain("href=\"https://portal.test/?customer=acme&amp;record=matter:1\"");
        body.Should().Contain(">Open the matter</a>");
        System.Text.RegularExpressions.Regex.IsMatch(body, @"\{\{\w+\}\}").Should().BeFalse("no template placeholder is left unfilled");
    }

    [Fact]
    public void FillTemplate_IsOnePass_AValueThatLooksLikeAPlaceholderIsNotExpandedAgain()
    {
        var values = new Dictionary<string, string> { ["A"] = "{{B}}", ["B"] = "second" };

        RegistrationEmailService.FillTemplate("{{A}} / {{B}} / {{Unknown}}", values)
            .Should().Be("{{B}} / second / {{Unknown}}");
    }
}
