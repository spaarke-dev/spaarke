using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 3.6 (deterministic contact-name match, email-r4 UAT R2 B1) unit tests. The rung extracts Title-Case
/// full-name phrases from the envelope and resolves each by EXACT full-name lookup against existing contacts
/// (mocked <see cref="ICommunicationDataverseService"/> boundary — ADR-038 boundary mock, not a DI-registration
/// test). The REAL extraction + precision logic runs over the mocked contact-lookup boundary. Suggest-only:
/// every match is a <c>sprk_regardingperson</c> (mapper fallback field) at a Suggested-band confidence
/// (&lt; 0.85, the auto-file threshold).
/// </summary>
public class ContactNameMatchRungTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();

    public ContactNameMatchRungTests()
    {
        // Default: no contact resolves for any name (tests opt in explicitly).
        _dv.Setup(d => d.QueryContactsByFullNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());
    }

    private ContactNameMatchRung Build(ContactNameMatchOptions? opts = null) =>
        new(_dv.Object, Options.Create(opts ?? new ContactNameMatchOptions()), NullLogger<ContactNameMatchRung>.Instance);

    private void SetupContact(string fullName, Guid id) =>
        _dv.Setup(d => d.QueryContactsByFullNameAsync(
                It.Is<string>(n => string.Equals(n, fullName, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new DataverseEntity("contact") { Id = id, ["fullname"] = fullName } });

    private static NormalizedMessage Envelope(string? subject = null, string? bodyText = null, string? attachmentText = null) =>
        new()
        {
            Direction = CommunicationDirection.Incoming,
            Subject = subject,
            BodyText = bodyText,
            AttachmentText = attachmentText,
            From = "sender@external.com",
        };

    [Fact]
    public async Task Evaluate_WhenFullNameNamedInBody_MatchesExistingContact()
    {
        var contactId = Guid.NewGuid();
        SetupContact("Sara Chen", contactId);

        var matches = await Build().EvaluateAsync(
            Envelope(bodyText: "Working on this file will be Sara Chen, please loop her in."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingperson");
        match.Target!.LogicalName.Should().Be("contact");
        match.Target!.Id.Should().Be(contactId);
        match.Target!.Name.Should().Be("Sara Chen");
        match.Rung.Should().Be(RungKind.ContactNameMatch);
        match.Confidence.Should().Be(0.68);                       // BodyNameConfidence
        match.Confidence.Should().BeLessThan(0.85);               // Suggested band — never auto-file
        match.Provenance.Should().Contain("where=body");
        match.Provenance.Should().Contain("Sara Chen");
    }

    [Fact]
    public async Task Evaluate_WhenFullNameInSubject_UsesSubjectBandConfidence()
    {
        var contactId = Guid.NewGuid();
        SetupContact("Sara Chen", contactId);

        var matches = await Build().EvaluateAsync(
            Envelope(subject: "Intro to Sara Chen", bodyText: "See below."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Confidence.Should().Be(0.72);                       // SubjectNameConfidence (strongest)
        match.Provenance.Should().Contain("where=subject");
    }

    [Fact]
    public async Task Evaluate_WhenSingleTokenName_DoesNotMatch()
    {
        // "Sara" alone is a single token — below the 2-token minimum, so no candidate phrase is even generated.
        // A common first name is far too weak a signal for a deterministic person match.
        var matches = await Build().EvaluateAsync(
            Envelope(bodyText: "Please ask Sara about the schedule."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _dv.Verify(d => d.QueryContactsByFullNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never); // no 2-token phrase to resolve
    }

    [Fact]
    public async Task Evaluate_WhenFullNameResolvesToNoContact_DoesNotMatch()
    {
        // "Sara Chen" is a valid candidate phrase, but the exact contact lookup returns nothing — the precision
        // guard: a name that resolves to no existing contact emits nothing (default mock returns empty).
        var matches = await Build().EvaluateAsync(
            Envelope(bodyText: "A note about Sara Chen and the matter."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _dv.Verify(d => d.QueryContactsByFullNameAsync("Sara Chen", It.IsAny<CancellationToken>()),
            Times.Once); // it DID attempt the exact lookup
    }

    [Fact]
    public async Task Evaluate_Email1Scenario_TwoNamedContacts_SurfacesBothSuggested()
    {
        // The email-1 regression: body names TWO contacts already in the system. Both must surface as
        // Suggested sprk_regardingperson candidates ("and" is lowercase, so it splits the two Title-Case runs).
        var eyalId = Guid.NewGuid();
        var saraId = Guid.NewGuid();
        SetupContact("Eyal Iffergan", eyalId);
        SetupContact("Sara Chen", saraId);

        var matches = await Build().EvaluateAsync(
            Envelope(bodyText: "Working on this file will be Eyal Iffergan and Sara Chen."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(m => m.RegardingFieldName == "sprk_regardingperson");
        matches.Should().OnlyContain(m => m.Rung == RungKind.ContactNameMatch);
        matches.Should().OnlyContain(m => m.Confidence == 0.68 && m.Confidence < 0.85); // body band, Suggested
        matches.Select(m => m.Target!.Id).Should().BeEquivalentTo(new[] { eyalId, saraId });
        matches.Select(m => m.Target!.Name).Should().BeEquivalentTo(new[] { "Eyal Iffergan", "Sara Chen" });
    }

    [Fact]
    public async Task Evaluate_WhenNameOnlyInAttachmentText_MatchesAtAttachmentBand()
    {
        var contactId = Guid.NewGuid();
        SetupContact("Sara Chen", contactId);

        var matches = await Build().EvaluateAsync(
            Envelope(subject: "Please see attached", bodyText: "Details attached.",
                     attachmentText: "MEMO\nAssigned counsel: Sara Chen\nRe: engagement."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(contactId);
        match.Confidence.Should().Be(0.60);                       // AttachmentNameConfidence (demoted)
        match.Provenance.Should().Contain("where=attachment");
    }

    [Fact]
    public async Task Evaluate_WhenSameContactInSubjectAndBody_KeepsSubjectBand()
    {
        // A contact named in BOTH subject and body dedups to one match at the higher (subject) confidence.
        var contactId = Guid.NewGuid();
        SetupContact("Sara Chen", contactId);

        var matches = await Build().EvaluateAsync(
            Envelope(subject: "Sara Chen intro", bodyText: "Copying Sara Chen here."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(contactId);
        match.Confidence.Should().Be(0.72);                       // subject band wins
    }

    [Fact]
    public async Task Evaluate_WhenDisabled_ReturnsEmpty_AndDoesNotQueryContacts()
    {
        SetupContact("Sara Chen", Guid.NewGuid());

        var matches = await Build(new ContactNameMatchOptions { Enabled = false }).EvaluateAsync(
            Envelope(bodyText: "Sara Chen is on this."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _dv.Verify(d => d.QueryContactsByFullNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
