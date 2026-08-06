using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Affinity rung (FR-A4) — a <b>deterministic learning loop</b>. Computes a message's affinity signals (sender,
/// sender-domain, subject keywords, participant set), reads the per-tenant <c>sprk_affinity</c> store for the
/// highest-frequency record confirmed against ANY of them, and surfaces that record as an explainable
/// SUGGEST-ONLY candidate whose provenance cites the confirmation count.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never auto-files.</b> This is structurally guaranteed: <see cref="RungKind.Affinity"/> is excluded from
/// BOTH <c>AssociationStatusMapper.IsAutoFileEligible</c> and <c>IsDeterministicWriteEligible</c> (mirroring
/// <see cref="RungKind.RecordNameMatch"/> / <see cref="RungKind.ContactNameMatch"/>), so an affinity match can
/// raise a communication to at-most Suggested for the reviewer to confirm, never to Resolved. It runs in the
/// deterministic pass (it is deterministic frequency counting — zero AI/ML, ADR-013), self-gated by
/// <c>Communication:Affinity:Enabled</c> (ADR-018), per-tenant.
/// </para>
/// <para>
/// Best-effort/non-fatal (NFR-04): a throwing rung is a non-match; the store read returns null on failure. The
/// increment-on-confirmation counterpart lives on <see cref="AffinityStore.RecordConfirmationAsync"/>; wiring
/// its call at the r5-owned confirmation surface is escalated to r5 coordination (FR-E6). Registered as a
/// concrete <see cref="IAssociationRung"/> singleton in <c>CommunicationModule</c> (ADR-010).
/// </para>
/// </remarks>
public sealed class AffinityRung : IAssociationRung
{
    private readonly AffinityStore _store;
    private readonly IOptionsMonitor<AffinityOptions> _options;
    private readonly ILogger<AffinityRung> _logger;

    /// <summary>Splits subject text into candidate keyword tokens (letters/digits runs). 1s timeout (NFR-04).</summary>
    private static readonly Regex TokenPattern =
        new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public AffinityRung(
        AffinityStore store,
        IOptionsMonitor<AffinityOptions> options,
        ILogger<AffinityRung> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    public RungKind Kind => RungKind.Affinity;

    // Deterministic tier-3 detector (runs alongside the other name/structure detectors). The
    // deterministic-vs-AI partition keys off Kind, not Order.
    public int Order => 3;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var opts = _options.CurrentValue;

        if (!opts.IsEnabledFor(context.TenantKey))
        {
            _logger.LogDebug("Affinity rung skipped — disabled via Communication:Affinity (tenant {TenantKey}).", context.TenantKey);
            return Array.Empty<RungMatch>();
        }

        var signals = ExtractSignals(message, opts);
        if (signals.Count == 0)
            return Array.Empty<RungMatch>();

        var hit = await _store.GetTopAffinityAsync(signals, context.TenantKey, opts.MinConfirmations, ct);
        if (hit is null)
            return Array.Empty<RungMatch>();

        // Map the stored target entity → its ADR-024 regarding lookup field. An unmapped entity (dirty row)
        // is a non-match, not a throw (NFR-04).
        var regardingField = RegardingFieldMap.FieldFor(hit.TargetEntity);
        if (regardingField is null || !Guid.TryParse(hit.TargetId, out var targetGuid) || targetGuid == Guid.Empty)
        {
            _logger.LogDebug(
                "Affinity hit skipped — target {Entity}:{Id} is unmapped or not a valid id.",
                hit.TargetEntity, hit.TargetId);
            return Array.Empty<RungMatch>();
        }

        var provenance =
            $"affinity:signal={hit.MatchedSignalType}:confirmations={hit.ConfirmationCount}:" +
            $"reason=\"{RungProvenanceFormat.EscapeValue($"filed {hit.ConfirmationCount} times to this record from a matching {SignalLabel(hit.MatchedSignalType)}")}\"";

        _logger.LogInformation(
            "Affinity rung fired | Target: {Entity}:{Id}, Signal: {Signal}, Confirmations: {Count}",
            hit.TargetEntity, hit.TargetId, hit.MatchedSignalType, hit.ConfirmationCount);

        return new[]
        {
            new RungMatch
            {
                RegardingFieldName = regardingField,
                Target = new EntityReference(hit.TargetEntity, targetGuid),
                Confidence = opts.SuggestConfidence,
                Provenance = provenance,
                Rung = RungKind.Affinity,
            },
        };
    }

    /// <summary>
    /// Computes the affinity signals for a message: sender, sender-domain, up to
    /// <see cref="AffinityOptions.MaxSubjectKeywords"/> subject keywords, and the canonical participant set
    /// (from + to + cc, lower-cased, distinct, sorted). Exposed <c>public static</c> so the
    /// increment-on-confirmation writer (when wired at the r5 surface, FR-E6) computes the SAME signals from
    /// the SAME envelope — keeping the read (this rung) and write (confirmation) canonicalization identical.
    /// </summary>
    public static IReadOnlyList<AffinitySignal> ExtractSignals(NormalizedMessage message, AffinityOptions opts)
    {
        var signals = new List<AffinitySignal>();

        var sender = Normalize(message.From);
        if (sender is not null)
        {
            signals.Add(new AffinitySignal(AffinitySignalType.Sender, sender));
            var at = sender.IndexOf('@');
            if (at >= 0 && at < sender.Length - 1)
                signals.Add(new AffinitySignal(AffinitySignalType.SenderDomain, sender[(at + 1)..]));
        }

        foreach (var keyword in ExtractSubjectKeywords(message.Subject, opts))
            signals.Add(new AffinitySignal(AffinitySignalType.SubjectKeyword, keyword));

        var participants = new SortedSet<string>(StringComparer.Ordinal);
        void AddParticipant(string? addr) { var n = Normalize(addr); if (n is not null) participants.Add(n); }
        AddParticipant(message.From);
        foreach (var to in message.To) AddParticipant(to);
        foreach (var cc in message.Cc) AddParticipant(cc);
        if (participants.Count > 0)
            signals.Add(new AffinitySignal(AffinitySignalType.ParticipantSet, string.Join(";", participants)));

        return signals;
    }

    private static IReadOnlyList<string> ExtractSubjectKeywords(string? subject, AffinityOptions opts)
    {
        if (string.IsNullOrWhiteSpace(subject) || opts.MaxSubjectKeywords <= 0)
            return Array.Empty<string>();

        var keywords = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (Match token in TokenPattern.Matches(subject))
            {
                var word = token.Value.ToLowerInvariant();
                if (word.Length < opts.MinKeywordLength)
                    continue;
                if (seen.Add(word))
                    keywords.Add(word);
                if (keywords.Count >= opts.MaxSubjectKeywords)
                    break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological subject → no keyword signals for this message (NFR-04).
        }
        return keywords;
    }

    private static string? Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        var trimmed = address.Trim().ToLowerInvariant();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string SignalLabel(AffinitySignalType type) => type switch
    {
        AffinitySignalType.Sender => "sender",
        AffinitySignalType.SenderDomain => "sender domain",
        AffinitySignalType.SubjectKeyword => "subject keyword",
        AffinitySignalType.ParticipantSet => "participant set",
        _ => "signal",
    };
}
