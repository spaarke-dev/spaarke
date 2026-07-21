using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Association Engine's deterministic contact-name match rung
/// (<see cref="Sprk.Bff.Api.Services.Communication.Engine.RungKind.ContactNameMatch"/>). Bound from the
/// <c>Communication:ContactNameMatch</c> section and consumed via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>
/// (parity with rungs 3.5/4/5).
/// </summary>
/// <remarks>
/// <para>
/// The rung extracts candidate PERSON-NAME phrases (a run of ≥ <see cref="MinNameTokens"/> Title-Case tokens)
/// from the envelope subject/body/attachment text and resolves each by EXACT full-name lookup against existing
/// <c>contact</c> records. Contacts are NOT in the <c>spaarke-records-index</c>, so this is a Dataverse query
/// (via <c>ICommunicationDataverseService</c>), not a RecordSearch call.
/// </para>
/// <para>
/// Per owner spec (2026-07-20) contact-name matches are SUGGEST-ONLY — surfaced for the reviewer, never
/// auto-filed. This is structurally guaranteed regardless: <c>sprk_regardingperson</c> is a FALLBACK field the
/// mapper excludes from auto-file, and this rung is (deliberately) NOT auto-file-eligible. Precision is protected
/// by the 2-token-minimum + <see cref="MinNameLength"/> guards AND by the EXACT contact lookup — a single common
/// name, or a name resolving to no contact, matches nothing (common-name false positives are the main hazard).
/// </para>
/// </remarks>
public class ContactNameMatchOptions
{
    public const string SectionName = "Communication:ContactNameMatch";

    /// <summary>
    /// Operational kill-switch. <c>true</c> (default) runs the rung; <c>false</c> makes it a no-op without a
    /// redeploy. Registered unconditionally (ADR-010); this flag gates behavior.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum token count a candidate name phrase must have to be eligible — a single common name (one token)
    /// is far too weak a signal for a deterministic person match; require a full name (first + last). Default 2;
    /// keep ≥ 2 (a value of 1 would defeat the core precision guard).
    /// </summary>
    [Range(2, 10)]
    public int MinNameTokens { get; set; } = 2;

    /// <summary>
    /// Maximum token count of a candidate name phrase / sliding window considered from a Title-Case run. Bounds
    /// the number of candidate phrases (and thus Dataverse round-trips) generated from a long capitalized run.
    /// Default 4 (covers first + optional two middle + last).
    /// </summary>
    [Range(2, 10)]
    public int MaxNameTokens { get; set; } = 4;

    /// <summary>
    /// Minimum length (chars, after alphanumeric collapse) a candidate name phrase must have to be eligible.
    /// Guards against a pair of very short tokens matching noise. Default 4.
    /// </summary>
    [Range(1, 100)]
    public int MinNameLength { get; set; } = 4;

    /// <summary>
    /// Max distinct candidate name phrases extracted per evaluation (bounds serial Dataverse contact lookups on
    /// the capture path). Default 25.
    /// </summary>
    [Range(1, 100)]
    public int MaxCandidates { get; set; } = 25;

    /// <summary>
    /// Max characters of each location's text (subject / body / attachment) scanned for candidate name phrases.
    /// Bounds the regex work over a long attachment. Default 6000.
    /// </summary>
    [Range(1, 20000)]
    public int MaxTextChars { get; set; } = 6000;

    // ── Confidence tiered by WHERE the name matched — ALL below the 0.85 auto-file threshold (Suggested band) ──
    // A name in the SUBJECT is a stronger association signal than one buried in a long attachment. Every value is
    // < 0.85 (auto-file threshold) and ≥ 0.50 (the mapper's suggest/write floor) so a contact-name match always
    // lands as a Suggested, WRITTEN candidate — never Resolved / auto-filed. (sprk_regardingperson is also a
    // mapper fallback field, so it is structurally non-auto-filing regardless of these values.)

    /// <summary>Confidence for a full name matched in the SUBJECT (0–1, default 0.72) — strongest location.</summary>
    [Range(0.0, 0.84)]
    public double SubjectNameConfidence { get; set; } = 0.72;

    /// <summary>Confidence for a full name matched in the BODY (0–1, default 0.68).</summary>
    [Range(0.0, 0.84)]
    public double BodyNameConfidence { get; set; } = 0.68;

    /// <summary>Confidence for a full name matched ONLY in an ATTACHMENT (0–1, default 0.60) — often incidental.</summary>
    [Range(0.0, 0.84)]
    public double AttachmentNameConfidence { get; set; } = 0.60;
}
