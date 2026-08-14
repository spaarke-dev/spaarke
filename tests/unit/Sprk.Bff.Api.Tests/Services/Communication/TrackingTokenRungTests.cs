using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Tracking;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 0 (tracking-token) tests: a Spaarke-minted, signature-VALID footer token yields a high-confidence
/// (1.0) auto-file-eligible match; a bare/edited textual reference with no valid signature yields a 0.65
/// corroborating match that never auto-files alone; a forged/tampered token is ignored; a deleted/absent
/// footer degrades to an empty list (no error); a token quoted inside reply/forward history is still
/// extracted + verified; and a forwarded prior token that conflicts with another rung's ≥threshold target
/// resolves through the UNMODIFIED mapper to Ambiguous (never a silent misfile).
///
/// Uses an in-memory <see cref="FakeTrackingTokenSigner"/> real test double — the rung's trust boundary is the
/// signer's VerifyAsync verdict, so exercising valid/forged/absent verdicts through a deterministic double is
/// the honest boundary (no transport mocks / DI-registration / ctor-null tests — ADR-038).
/// </summary>
public class TrackingTokenRungTests
{
    private const string MatterField = "sprk_regardingmatter";

    private readonly FakeTrackingTokenSigner _signer = new();
    private readonly TrackingTokenRung _rung;

    public TrackingTokenRungTests() => _rung = new TrackingTokenRung(_signer, NullLogger<TrackingTokenRung>.Instance);

    private static NormalizedMessage Body(string? text = null, string? html = null) =>
        new() { Direction = CommunicationDirection.Incoming, BodyText = text, BodyHtml = html };

    // ── Case 1: signed-valid → 1.0, auto-file-eligible through the unmodified mapper ──────────────────────

    [Fact]
    public async Task Evaluate_SignatureValidToken_EmitsSignedMatchAtFullConfidence()
    {
        var matterId = Guid.NewGuid();
        var token = _signer.AddValid("sprk_matter", matterId);

        var matches = await _rung.EvaluateAsync(
            Body(text: $"Reply body.\n\n---\nThis message regards Acme v Beta. {token}"),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Should().Match<RungMatch>(m =>
            m.RegardingFieldName == MatterField
            && m.Target!.LogicalName == "sprk_matter" && m.Target!.Id == matterId
            && m.Confidence == 1.0
            && m.Provenance.StartsWith("tracking-token:signed")
            && m.Rung == RungKind.ExplicitReference);
    }

    [Fact]
    public async Task Evaluate_SignatureValidMatterToken_UnmodifiedMapperAutoFilesResolved()
    {
        // Criterion #1: the 1.0 ExplicitReference match the rung emits is auto-file-eligible in the mapper
        // WITHOUT any mapper change — a core matter target ≥ threshold with the kill-switch on ⇒ Resolved.
        var matterId = Guid.NewGuid();
        var token = _signer.AddValid("sprk_matter", matterId);

        var matches = await _rung.EvaluateAsync(
            Body(text: $"body {token}"), new AssociationContext(), CancellationToken.None);
        var decision = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85)
            .Decide(matches, CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    // ── Case 2: bare / edited textual reference (no valid signature) → 0.65, never auto-files alone ────────

    [Fact]
    public async Task Evaluate_BareTextualReference_NoValidSignature_EmitsCorroboratingMatchAt065()
    {
        var matterId = Guid.NewGuid();

        var matches = await _rung.EvaluateAsync(
            // Footer edited: the token is gone, but the disclosure's {entityType} {guid} fallback remains.
            Body(text: $"Reply.\n\n---\nThis message regards sprk_matter {matterId:D}."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Should().Match<RungMatch>(m =>
            m.RegardingFieldName == MatterField
            && m.Target!.Id == matterId
            && m.Confidence == 0.65
            && m.Provenance.StartsWith("tracking-token:bare")
            && m.Rung == RungKind.ExplicitReference);
    }

    [Fact]
    public async Task Evaluate_BareReferenceOnly_MapperDoesNotAutoFile()
    {
        // 0.65 is below the 0.85 auto-file threshold — a bare reference surfaces for review, never auto-files.
        var matterId = Guid.NewGuid();

        var matches = await _rung.EvaluateAsync(
            Body(text: $"regards sprk_matter {matterId:D}"), new AssociationContext(), CancellationToken.None);
        var decision = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85)
            .Decide(matches, CommunicationDirection.Incoming, tenantKey: null);

        decision.AutoFiled.Should().BeFalse();
        decision.Status.Should().NotBe(AssociationStatusCodes.Resolved);
    }

    // ── Case 3: deleted / absent footer → empty, no error ─────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_NoFooter_ReturnsEmptyWithoutError()
    {
        var matches = await _rung.EvaluateAsync(
            Body(text: "Just an ordinary reply with no tracking footer at all.",
                 html: "<p>Just an ordinary reply with no tracking footer at all.</p>"),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    // ── Case 4: forged / invalid signature → ignored (no match) ──────────────────────────────────────────

    [Fact]
    public async Task Evaluate_ForgedToken_IsIgnored()
    {
        // Token-shaped but never issued by the signer → VerifyAsync returns Invalid → no match (never a
        // low-confidence write to an attacker-chosen record).
        const string forged = "Zm9yZ2VkcGF5bG9hZGZvcmdlZA.dGFtcGVyZWRzaWduYXR1cmV4eA";

        var matches = await _rung.EvaluateAsync(
            Body(text: $"Reply.\n\n---\nThis message regards Something. {forged}"),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    // ── Case 5: forwarded prior token conflicting with another rung → Ambiguous (via unmodified mapper) ───

    [Fact]
    public async Task Evaluate_ForwardedTokenConflictingWithAnotherRung_MapperResolvesAmbiguous()
    {
        // The rung reads a VALID forwarded token for matter A; a second rung (simulated) supplies matter B on
        // the SAME field at ≥ threshold. Two distinct targets on one field, each ≥ threshold → the mapper's
        // existing conflict path yields Ambiguous (never a silent misfile) with NO mapper change.
        var matterA = Guid.NewGuid();
        var matterB = Guid.NewGuid();
        var token = _signer.AddValid("sprk_matter", matterA);

        var tokenMatches = await _rung.EvaluateAsync(
            Body(text: $"Fwd: prior thread {token}"), new AssociationContext(), CancellationToken.None);

        var secondRungMatch = new RungMatch
        {
            RegardingFieldName = MatterField,
            Target = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matterB),
            Confidence = 0.90,
            Provenance = "thread:in-reply-to->parent",
            Rung = RungKind.ThreadContinuity,
        };

        var decision = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85)
            .Decide(tokenMatches.Append(secondRungMatch).ToArray(),
                    CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Ambiguous);
        decision.AutoFiled.Should().BeFalse();
    }

    // ── Case 6: token only inside quoted reply/forward history → still extracted + verified ───────────────

    [Fact]
    public async Task Evaluate_TokenInQuotedHistoryOnly_IsStillExtractedAndVerified()
    {
        var matterId = Guid.NewGuid();
        var token = _signer.AddValid("sprk_matter", matterId);

        var quoted =
            "Thanks, will review.\n\n" +
            "> On Tue, Aug 5, 2026, Spaarke wrote:\n" +
            "> Please see attached.\n" +
            "> \n" +
            "> ---\n" +
            $"> This message regards Acme v Beta. {token}\n";

        var matches = await _rung.EvaluateAsync(
            Body(text: quoted), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Should().Match<RungMatch>(m =>
            m.Target!.Id == matterId && m.Confidence == 1.0 && m.Provenance.StartsWith("tracking-token:signed"));
    }

    // ── Extra: token carried in the HTML body (HtmlEncode leaves base64url unchanged) ────────────────────

    [Fact]
    public async Task Evaluate_TokenInHtmlBody_IsExtractedAndVerified()
    {
        var matterId = Guid.NewGuid();
        var token = _signer.AddValid("sprk_matter", matterId);

        var matches = await _rung.EvaluateAsync(
            Body(html: $"<p>Reply.</p><hr /><p>This message regards Acme v Beta. {token}</p>"),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }

    // ── Extra: verified token whose RecordType has no RegardingFieldMap entry → skipped (trigger #2) ──────

    [Fact]
    public async Task Evaluate_ValidTokenForUnmappedRecordType_IsSkipped()
    {
        var token = _signer.AddValid("sprk_unmappedthing", Guid.NewGuid());

        var matches = await _rung.EvaluateAsync(
            Body(text: $"body {token}"), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    /// <summary>
    /// In-memory real test double for <see cref="ITrackingTokenSigner"/>. Issues token-shaped strings
    /// (base64url "." base64url, matching <see cref="TrackingTokenSigner"/>'s format so the rung's extraction
    /// regex picks them up) and returns a VALID verification only for tokens it minted — every other
    /// token-shaped input verifies as Invalid, exactly modelling a forged/tampered token.
    /// </summary>
    private sealed class FakeTrackingTokenSigner : ITrackingTokenSigner
    {
        private readonly Dictionary<string, TrackingTokenPayload> _valid = new(StringComparer.Ordinal);

        public string AddValid(string recordType, Guid recordId)
        {
            var token = $"{TokenSegment()}.{TokenSegment()}";
            _valid[token] = new TrackingTokenPayload(recordType, recordId, null, DateTimeOffset.UtcNow);
            return token;
        }

        public Task<string?> SignAsync(
            string recordType, Guid recordId, string? tenantId, DateTimeOffset issued, CancellationToken ct = default)
            => throw new NotSupportedException("The rung never signs; it only verifies.");

        public Task<TrackingTokenVerification> VerifyAsync(string? token, CancellationToken ct = default)
            => Task.FromResult(
                token is not null && _valid.TryGetValue(token, out var payload)
                    ? new TrackingTokenVerification(true, payload)
                    : TrackingTokenVerification.Invalid);

        // A base64url segment ≥ 16 chars (two GUIDs' worth), so a minted token clears the rung's {16,} pattern.
        private static string TokenSegment()
        {
            var raw = Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray());
            return raw.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
