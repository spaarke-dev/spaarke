using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// <see cref="CommunicationType"/>-keyed dispatch over the channel seams (ADR-045 rule 4 / NFR-04).
/// Resolves the sender/archiver/ingestor implementation for a communication type from the set of registered
/// implementations. Adding a channel is purely additive: register a new
/// <see cref="ICommunicationChannelSender"/> + <see cref="ICommunicationArchiver"/> +
/// <see cref="ICommunicationChannelIngestor"/> keyed to its own
/// <see cref="ICommunicationChannelSender.SupportedType"/> in <c>CommunicationModule</c> — the dispatch
/// site in <see cref="CommunicationService"/> does NOT change, and neither do the engine, enrichment
/// service, regarding model, or review UI.
/// </summary>
public sealed class CommunicationChannelDispatcher
{
    private readonly IReadOnlyDictionary<CommunicationType, ICommunicationChannelSender> _senders;
    private readonly IReadOnlyDictionary<CommunicationType, ICommunicationArchiver> _archivers;
    private readonly IReadOnlyDictionary<CommunicationType, ICommunicationChannelIngestor> _ingestors;

    public CommunicationChannelDispatcher(
        IEnumerable<ICommunicationChannelSender> senders,
        IEnumerable<ICommunicationArchiver> archivers,
        IEnumerable<ICommunicationChannelIngestor>? ingestors = null)
    {
        // ToDictionary throws on a duplicate key — a second sender/archiver/ingestor claiming the same
        // CommunicationType is a registration bug, surfaced at startup rather than silently shadowed.
        // `ingestors` is optional (defaults to empty) so the shipped outbound-only construction sites and
        // tests are unchanged; DI supplies the registered ingestors (messaging in R1).
        _senders = senders.ToDictionary(s => s.SupportedType);
        _archivers = archivers.ToDictionary(a => a.SupportedType);
        _ingestors = (ingestors ?? Enumerable.Empty<ICommunicationChannelIngestor>())
            .ToDictionary(i => i.SupportedType);
    }

    /// <summary>Resolves the sender for the given type, or throws a 400 if the channel is unsupported.</summary>
    public ICommunicationChannelSender ResolveSender(CommunicationType type)
        => _senders.TryGetValue(type, out var sender)
            ? sender
            : throw UnsupportedChannel(type, "send");

    /// <summary>Resolves the archiver for the given type, or throws a 400 if the channel is unsupported.</summary>
    public ICommunicationArchiver ResolveArchiver(CommunicationType type)
        => _archivers.TryGetValue(type, out var archiver)
            ? archiver
            : throw UnsupportedChannel(type, "archive");

    /// <summary>
    /// Resolves the inbound-capture ingestor for the given type, or throws a 400 if the channel has no
    /// registered ingestor (ADR-045 net-new inbound leg). R1 registers only the messaging ingestor
    /// (<c>CommunicationType.Message</c>); email inbound stays on the concrete
    /// <see cref="IncomingCommunicationProcessor"/> path (no email ingestor is registered), so
    /// <c>ResolveIngestor(Email)</c> throws <c>CHANNEL_NOT_SUPPORTED</c> by design until a future refactor
    /// routes email through the seam.
    /// </summary>
    public ICommunicationChannelIngestor ResolveIngestor(CommunicationType type)
        => _ingestors.TryGetValue(type, out var ingestor)
            ? ingestor
            : throw UnsupportedChannel(type, "ingest");

    private static SdapProblemException UnsupportedChannel(CommunicationType type, string leg)
        => new(
            code: "CHANNEL_NOT_SUPPORTED",
            title: "Unsupported Communication Channel",
            detail: $"No {leg} channel is registered for communication type '{type}'. Email is the only channel implemented in R4.",
            statusCode: 400,
            extensions: new Dictionary<string, object> { ["communicationType"] = type.ToString() });
}
