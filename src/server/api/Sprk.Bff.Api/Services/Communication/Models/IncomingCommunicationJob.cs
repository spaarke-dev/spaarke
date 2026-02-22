namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Represents an incoming email job to be processed by IncomingCommunicationProcessor.
/// Created when the Graph webhook or backup polling detects a new inbound email.
/// </summary>
public sealed record IncomingCommunicationJob(string MailboxEmail, string GraphMessageId);
