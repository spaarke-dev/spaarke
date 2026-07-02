namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Extracts plain text from DOCX byte streams for AI-context building.
/// </summary>
/// <remarks>
/// <para>
/// <b>Project</b>: spaarkeai-compose-r1 · Phase 8 (streaming SSE backend) · task 094
/// (per spec-supplement-2026-07-01-three-pane-pivot.md FR-S5).
/// </para>
/// <para>
/// <b>Consumers</b>: <c>ComposeService</c> + the <c>/api/compose/action/*</c> endpoint (task 097
/// SSE conversion) load DOCX bytes from SPE and need plain-text prose to send as
/// <c>UserContext</c> through the widened <c>IInvokePlaybookAi</c> facade (task 095).
/// This eliminates the current "AI analysis node requires document context" summarize
/// failure by shipping the document text to the playbook orchestrator alongside the JPS
/// scope parameters.
/// </para>
/// <para>
/// <b>Justified interface seam per ADR-010</b>: the default impl calls the
/// <c>DocumentFormat.OpenXml</c> SDK — a real I/O-shaped API surface (opens a
/// <see cref="System.IO.Stream"/>, parses XML). This is one of the three ADR-010-approved
/// interface-extraction cases (external I/O boundary): higher-level integration tests
/// against <c>ComposeService</c> and <c>ComposeEndpoints</c> can mock this seam without
/// binding to <c>WordprocessingDocument</c> parse behaviour, and the OpenXml SDK is
/// notoriously hard to fake in-process.
/// </para>
/// <para>
/// <b>Scope</b>: R1 extracts prose only — main body paragraph runs, concatenated with
/// paragraph breaks. Headers, footers, comments, revision marks, embedded objects, and
/// tables of contents are intentionally NOT extracted (per POML 094 §Behavior). The R3
/// Word-fidelity project (<c>projects/spaarkeai-compose-r3/</c>) may enrich extraction
/// for advanced consumers.
/// </para>
/// </remarks>
public interface IDocxTextExtractor
{
    /// <summary>
    /// Reads a DOCX stream and returns the concatenated plain text of its main body.
    /// </summary>
    /// <param name="docxBytes">
    /// A readable, seekable stream positioned at the beginning of a DOCX (Open XML
    /// WordprocessingML) file. The caller retains ownership of the stream and is
    /// responsible for disposing it.
    /// </param>
    /// <param name="maxCharacters">
    /// The maximum number of characters to return. When the extracted text exceeds this
    /// bound, the return value is truncated to <paramref name="maxCharacters"/>
    /// characters followed by a suffix of the form
    /// <c>"[TRUNCATED — {N} characters more]"</c>. Defaults to 100,000 (approximately
    /// 15-20 pages of prose) which is a conservative headroom below the ~128k prompt
    /// budget the compose-summarize orchestrator allots for document context.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the extraction operation. Callers on request-scoped code paths should
    /// propagate the HTTP request cancellation token so long-running extractions don't
    /// outlive a cancelled request.
    /// </param>
    /// <returns>
    /// The extracted plain text. Returns <see cref="string.Empty"/> for a valid DOCX
    /// with no body text (empty document). Throws
    /// <see cref="System.IO.InvalidDataException"/> when
    /// <paramref name="docxBytes"/> is not a valid Open XML package.
    /// </returns>
    /// <exception cref="System.IO.InvalidDataException">
    /// Thrown when <paramref name="docxBytes"/> is not a valid DOCX archive (corrupted
    /// zip, missing required parts, or a non-Word Open XML document such as XLSX).
    /// Callers should treat this as a bad-request condition — the document cannot be
    /// summarized until the file is uploaded again in a valid format.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is triggered before extraction
    /// completes.
    /// </exception>
    Task<string> ExtractPlainTextAsync(
        Stream docxBytes,
        int maxCharacters = 100_000,
        CancellationToken cancellationToken = default);
}
