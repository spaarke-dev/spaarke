using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Tracking;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 0 (tier) — tracking-token reader (FR-A1). Reads the transparent, HMAC-signed footer token that the
/// outbound send path (task 012) stamps on communications Spaarke originates, and on capture VERIFIES the
/// signature (<see cref="ITrackingTokenSigner.VerifyAsync"/>) BEFORE trusting the bound record — so a thread
/// Spaarke started is recognized with high trust on the reply, while a forged / tampered token can never
/// auto-file to an attacker-chosen record (ADR-028 verify-before-trust; NFR-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuses <see cref="RungKind.ExplicitReference"/> (zero mapper change).</b> A verified token is the
/// strongest possible explicit reference — Spaarke itself minted it — so it belongs in the rung-0 explicit
/// tier alongside <see cref="ExplicitReferenceRung"/> and <see cref="IdentifierReverseLookupRung"/>. Emitting
/// <see cref="RungKind.ExplicitReference"/> means the unmodified <see cref="AssociationStatusMapper"/> already
/// treats a signed match as auto-file-eligible (rung 0) and its existing same-field-2-targets conflict path
/// yields <c>Ambiguous</c> — so a forwarded prior token that conflicts with another signal never silently
/// misfiles. The mapper collapses same-kind matches per target to their MAX, so this rung never double-counts
/// with the other rung-0 rungs.
/// </para>
/// <para>
/// <b>Confidence tiers encode trust.</b>
/// <list type="bullet">
///   <item><b>Signed-valid</b> → <c>1.0</c>, provenance <c>tracking-token:signed</c> — auto-file-eligible.</item>
///   <item><b>Bare / edited textual reference</b> (a resolvable <c>{entityType} {guid}</c> the disclosure
///   fallback embeds, with NO valid signature covering it) → <c>0.65</c>, provenance
///   <c>tracking-token:bare</c> — a corroborating candidate the ladder cross-checks; below the 0.85 threshold,
///   so it never auto-files alone.</item>
///   <item><b>Forged / invalid signature</b> → no match (ignored).</item>
///   <item><b>Absent / deleted footer</b> → <see cref="Array.Empty{T}()"/>, no error — the other rungs decide.</item>
/// </list>
/// </para>
/// <para>
/// <b>Envelope-only (ADR-045) + best-effort/non-fatal (NFR-04).</b> The rung reads only
/// <see cref="NormalizedMessage.BodyText"/> + <see cref="NormalizedMessage.BodyHtml"/> (including quoted
/// reply/forward history — the footer is quoted back verbatim), consuming ONLY the signer +
/// <see cref="RegardingFieldMap"/> (no Dataverse, no AI — ADR-013). Any failure degrades to no-match; the
/// signer's <see cref="ITrackingTokenSigner.VerifyAsync"/> never throws. A token whose verified
/// <c>RecordType</c> has no <see cref="RegardingFieldMap"/> entry is skipped (never a guessed unknown lookup).
/// </para>
/// </remarks>
public sealed class TrackingTokenRung : IAssociationRung
{
    private readonly ITrackingTokenSigner _signer;
    private readonly ILogger<TrackingTokenRung> _logger;

    /// <summary>Confidence for a signature-valid token — the strongest explicit reference (auto-file-eligible).</summary>
    private const double SignedConfidence = 1.0;

    /// <summary>
    /// Confidence for a bare/edited textual record reference with NO valid signature — corroborating only,
    /// deliberately BELOW the 0.85 auto-file threshold so it never auto-files alone (it reinforces / surfaces
    /// for review).
    /// </summary>
    private const double BareReferenceConfidence = 0.65;

    /// <summary>Max distinct token/reference candidates scanned per message (cost + abuse cap).</summary>
    private const int MaxCandidates = 25;

    /// <summary>
    /// Token shape: <c>base64url(payloadJson) "." base64url(signature)</c> (see
    /// <see cref="TrackingTokenSigner"/>). base64url is <c>[A-Za-z0-9_-]</c>, so the token survives HTML
    /// encoding unchanged in <see cref="NormalizedMessage.BodyHtml"/>. Precision comes from the signature
    /// verification, not this pattern — an over-match simply fails <see cref="ITrackingTokenSigner.VerifyAsync"/>.
    /// </summary>
    private static readonly Regex TokenPattern =
        new(@"[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Bare textual record reference — an entity logical name followed by a GUID, the disclosure fallback
    /// form (<c>{entityType} {entityId}</c>) the send path emits when the record has no display name. Matched
    /// deterministically so an edited footer (token stripped/tampered) still yields a corroborating candidate.
    /// The entity name is validated against <see cref="RegardingFieldMap"/> (unmapped → skipped), so this
    /// never writes an unknown lookup.
    /// </summary>
    private static readonly Regex BareReferencePattern =
        new(@"\b(sprk_[a-z]+|account|contact)\b[\s:#>]*\(?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public TrackingTokenRung(ITrackingTokenSigner signer, ILogger<TrackingTokenRung> logger)
    {
        _signer = signer;
        _logger = logger;
    }

    // Reuse the rung-0 explicit-reference kind: a Spaarke-minted, signature-verified token IS the strongest
    // explicit reference. Same Kind + Order as the other rung-0 rungs — the engine aggregates all of them and
    // the mapper collapses same-kind matches per target to their MAX, so there is no double-count.
    public RungKind Kind => RungKind.ExplicitReference;
    public int Order => 0;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        // Dedup by (field, target id) keeping the highest confidence: a signed 1.0 beats a bare 0.65 for the
        // same record, and a token quoted twice in the reply history collapses to one match.
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        // ── Tier 1: signature-valid tokens → 1.0 (verify-before-trust) ──────────────────────────────────
        var tokens = ExtractCandidates(TokenPattern, message);
        foreach (var token in tokens)
        {
            TrackingTokenVerification verification;
            try
            {
                // The signer is contractually no-throw (NFR-07); the try is a defensive NFR-04 backstop.
                verification = await _signer.VerifyAsync(token, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TrackingTokenRung — token verification threw (treated as untrusted).");
                continue;
            }

            if (!verification.IsValid || verification.Payload is null)
                continue; // forged / tampered / malformed → ignored (never a low-confidence write)

            var payload = verification.Payload;
            var field = RegardingFieldMap.FieldFor(payload.RecordType);
            if (field is null)
            {
                // Escalation-trigger #2 boundary: a verified type with no regarding field — skip, don't invent.
                _logger.LogWarning(
                    "TrackingTokenRung — verified token RecordType '{RecordType}' has no RegardingFieldMap entry; skipping match.",
                    payload.RecordType);
                continue;
            }
            if (payload.RecordId == Guid.Empty)
                continue;

            AddOrKeepHigher(best, new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(payload.RecordType, payload.RecordId),
                Confidence = SignedConfidence,
                Provenance = $"tracking-token:signed:{payload.RecordType}",
                Rung = RungKind.ExplicitReference,
            });
        }

        // ── Tier 2: bare / edited textual references → 0.65 (corroborating; never auto-files alone) ───────
        foreach (var (entityType, id) in ExtractBareReferences(message))
        {
            var field = RegardingFieldMap.FieldFor(entityType);
            if (field is null)
                continue; // unmapped entity name in text → not a record reference we can write

            AddOrKeepHigher(best, new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(entityType, id),
                Confidence = BareReferenceConfidence,
                Provenance = $"tracking-token:bare:{entityType}",
                Rung = RungKind.ExplicitReference,
            });
        }

        return best.Count == 0 ? Array.Empty<RungMatch>() : best.Values.ToArray();
    }

    /// <summary>
    /// A signed match for a target OUTRANKS a bare match for the same (field, target); when both are present
    /// (an intact footer whose fallback text also carries the id), the 1.0 wins. Mirrors the max-wins dedup
    /// <see cref="IdentifierReverseLookupRung"/> uses (the mapper collapses per-kind max anyway).
    /// </summary>
    private static void AddOrKeepHigher(Dictionary<(string Field, Guid Id), RungMatch> best, RungMatch match)
    {
        var key = (match.RegardingFieldName!, match.Target!.Id);
        if (!best.TryGetValue(key, out var existing) || match.Confidence > existing.Confidence)
            best[key] = match;
    }

    /// <summary>
    /// Extracts distinct token-shaped candidates from BodyText + BodyHtml (incl. quoted history). Best-effort
    /// (NFR-04): a regex timeout yields no candidates for that section. Capped at <see cref="MaxCandidates"/>.
    /// </summary>
    private static IReadOnlyList<string> ExtractCandidates(Regex pattern, NormalizedMessage message)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();

        void Scan(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || order.Count >= MaxCandidates)
                return;
            try
            {
                foreach (Match m in pattern.Matches(text))
                {
                    var value = m.Value;
                    if (seen.Add(value))
                    {
                        order.Add(value);
                        if (order.Count >= MaxCandidates)
                            return;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Defensive (NFR-04): a pathological input yields no candidates for this section.
            }
        }

        Scan(message.BodyText);
        Scan(message.BodyHtml);
        return order;
    }

    /// <summary>
    /// Extracts distinct <c>(entityType, guid)</c> bare references from BodyText + BodyHtml (incl. quoted
    /// history). Best-effort (NFR-04). Capped at <see cref="MaxCandidates"/>. Callers still validate the
    /// entity name against <see cref="RegardingFieldMap"/>.
    /// </summary>
    private static IReadOnlyList<(string EntityType, Guid Id)> ExtractBareReferences(NormalizedMessage message)
    {
        var seen = new HashSet<(string, Guid)>();
        var order = new List<(string EntityType, Guid Id)>();

        void Scan(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || order.Count >= MaxCandidates)
                return;
            try
            {
                foreach (Match m in BareReferencePattern.Matches(text))
                {
                    var entityType = m.Groups[1].Value.ToLowerInvariant();
                    if (!Guid.TryParse(m.Groups[2].Value, out var id) || id == Guid.Empty)
                        continue;
                    var pair = (entityType, id);
                    if (seen.Add(pair))
                    {
                        order.Add(pair);
                        if (order.Count >= MaxCandidates)
                            return;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Defensive (NFR-04).
            }
        }

        Scan(message.BodyText);
        Scan(message.BodyHtml);
        return order;
    }
}
