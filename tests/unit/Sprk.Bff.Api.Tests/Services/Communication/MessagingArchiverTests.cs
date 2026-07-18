using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="MessagingArchiver"/> — the SECOND ADR-045 channel archiver (task 020 / FR-01), the
/// chat-transcript analog of email's <c>.eml</c>. Protects the archive-artifact contract the SPE upload +
/// downstream consumers rely on: the transcript aggregates the participants, subject, body, and the provider
/// message id (the echo-dedup key) into the artifact, and produces a sanitized transcript filename. A silent
/// drop of the body or the dedup id, or a filename that breaks on Windows clients, would regress unobserved.
/// </summary>
public class MessagingArchiverTests
{
    private static (SendCommunicationRequest req, SendCommunicationResponse resp) Sample(string subject = "Kickoff chat") =>
        (
            new SendCommunicationRequest
            {
                To = new[] { "a@contoso.com", "b@contoso.com" },
                Subject = subject,
                Body = "the message body",
                CommunicationType = CommunicationType.Message
            },
            new SendCommunicationResponse
            {
                GraphMessageId = "1700000000123", // provider (ACS) message id = echo-dedup key
                Status = CommunicationStatus.Delivered,
                SentAt = new DateTimeOffset(2026, 7, 16, 9, 30, 0, TimeSpan.Zero),
                From = "8:acs:res_sender",
                CorrelationId = "corr-020"
            });

    [Fact]
    public void GenerateEml_ProducesTranscriptCarryingParticipantsSubjectBodyAndDedupId()
    {
        var (req, resp) = Sample();

        var result = new MessagingArchiver().GenerateEml(req, resp);
        var text = Encoding.UTF8.GetString(result.Content);

        text.Should().Contain("From: 8:acs:res_sender");
        text.Should().Contain("To: a@contoso.com; b@contoso.com");
        text.Should().Contain("Subject: Kickoff chat");
        text.Should().Contain("Message-Id: 1700000000123"); // dedup id preserved in the archived artifact
        text.Should().Contain("the message body");
    }

    [Fact]
    public void GenerateEml_ProducesSanitizedTranscriptFileName()
    {
        var (req, resp) = Sample(subject: "Matter: 42/Kickoff?");

        var result = new MessagingArchiver().GenerateEml(req, resp);

        result.FileName.Should().EndWith("_transcript.txt");
        result.FileName.Should().NotContainAny("/", "\\", ":", "?", "*", "\"", "<", ">", "|");
    }

    [Fact]
    public void SupportedType_IsMessage()
    {
        new MessagingArchiver().SupportedType.Should().Be(CommunicationType.Message);
    }
}
