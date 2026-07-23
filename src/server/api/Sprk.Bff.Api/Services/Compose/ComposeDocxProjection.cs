namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Outcome of a server-side DOCX → editor projection (Phase 1, design
/// <c>notes/design-server-side-docx-html-conversion.md</c> §3.3). Replaces the client-side mammoth
/// convert + position-based paraId stamping — the two-engine drift that produced the recurring
/// "w14:paraId matches no paragraph in the retained original" save failures.
/// </summary>
/// <remarks>
/// Fail-closed (F-04 / GPT §11): the client MUST NOT infer success from <see cref="Html"/> being
/// non-empty. A projection <b>failure</b> (vs. a legitimately empty document) must not mount a blank
/// editable surface over a non-empty retained baseline — the client keys off <see cref="Status"/> +
/// <see cref="CanEdit"/>. Load still returns the source bytes; only the projection fails closed.
/// </remarks>
public sealed record ComposeDocxProjection
{
    /// <summary>Success = fully projected; Partial = projected with fidelity warnings; Failed = could not project.</summary>
    public required ComposeProjectionStatus Status { get; init; }

    /// <summary>
    /// False ⇒ the client mounts a read-only / "Open in Word" state rather than an editable document.
    /// True only when the projection produced editable content the save path can safely delta against.
    /// </summary>
    public required bool CanEdit { get; init; }

    /// <summary>
    /// paraId-tagged, TipTap-shaped HTML (<c>data-paraid</c> on every block). The editor's paraId
    /// extension parses <c>data-paraid</c> on <c>setContent</c> — no client stamping. Tier-3 content
    /// (document text) — NEVER logged. Empty when <see cref="Status"/> is <see cref="ComposeProjectionStatus.Failed"/>.
    /// </summary>
    public string Html { get; init; } = string.Empty;

    /// <summary>
    /// The ordered <c>w14:paraId</c> map, one entry per body paragraph in <c>Descendants&lt;Paragraph&gt;()</c>
    /// document order — the SAME sequence the HTML blocks carry (produced from the same paragraph instances,
    /// NOT an ordinal re-join). Consumed by the save-side <see cref="ComposeBaselineParaIdStamper"/> and by
    /// <see cref="ComposeService"/>'s imported-revision/comment paraId resolution. Empty (never null) for a
    /// body-less document.
    /// </summary>
    public IReadOnlyList<ParaIdMapEntry> ParaIdMap { get; init; } = Array.Empty<ParaIdMapEntry>();

    /// <summary>Machine-readable, user-presentable fidelity warnings — codes + counts only (no document content).</summary>
    public IReadOnlyList<ComposeProjectionWarning> Warnings { get; init; } = Array.Empty<ComposeProjectionWarning>();

    /// <summary>
    /// Projection contract version. Phase 1 is <c>compose-html-v1</c> (HTML, transitional). A future
    /// TipTap-JSON / in-pass-marks projection (design §11 Phase 2/3) is a versioned change, not a silent one.
    /// </summary>
    public string SchemaVersion { get; init; } = "compose-html-v1";

    /// <summary>A closed (never editable) failure projection carrying an optional diagnostic code.</summary>
    public static ComposeDocxProjection Failed(string? code = null) => new()
    {
        Status = ComposeProjectionStatus.Failed,
        CanEdit = false,
        Html = string.Empty,
        Warnings = code is null
            ? Array.Empty<ComposeProjectionWarning>()
            : new[] { new ComposeProjectionWarning(code, 1) },
    };
}

/// <summary>Phase-1 projection status. Drives the client's fail-closed mount decision (design §4).</summary>
public enum ComposeProjectionStatus
{
    /// <summary>Fully projected; mount editable.</summary>
    Success,

    /// <summary>Projected with fidelity gaps (see <see cref="ComposeDocxProjection.Warnings"/>); mount editable with a banner.</summary>
    Partial,

    /// <summary>Could not project; client must NOT mount an editable blank over the source.</summary>
    Failed,
}

/// <summary>
/// A single fidelity warning: a stable <paramref name="Code"/> (e.g. <c>multi-level-numbering</c>,
/// <c>unrendered-paragraphs</c>, <c>numbering-unresolved</c>, <c>content-control</c>), the
/// <paramref name="Count"/> of occurrences, and an optional non-content <paramref name="Detail"/>.
/// Carries NO document text (Tier-1 safe).
/// </summary>
public sealed record ComposeProjectionWarning(string Code, int Count, string? Detail = null);
