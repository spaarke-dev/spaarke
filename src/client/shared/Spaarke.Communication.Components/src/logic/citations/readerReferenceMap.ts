/**
 * readerReferenceMap.ts — reconciliation reader QUOTED-TEXT citation anchor
 * (email-communication-intelligence-r2 task 054, spec NFR-11 / UI model §99).
 *
 * Pure logic (no React, no I/O, deep-importable — ADR-022) that maps an email
 * proposal `Citation{Source, Locator, QuotedText}` onto the task-053 reconciliation
 * reader's NORMALIZED text surface (email body + folded attachment text — the same
 * text the AI extraction ran over, which is what makes an anchor exact) and returns
 * the located segment + span, or an explicit NOT-LOCATED result. A forged/absent
 * quote is NEVER silently mapped to a nearest/wrong passage (project constraint).
 *
 * ── §6.5 ADR TENSION — documented, owner-approved 2026-08-07 (project-scoped
 *    exception to NFR-11's "reuse composeCitationResolver") ────────────────────
 * The POML directed reuse of `composeCitationResolver.resolveCitation` over a
 * "ParaIdMap-style" map. That resolver is a LEGAL-SECTION-NUMBER resolver — it
 * parses "Section 4.2" / "4.2(b)(iii)" / "Sections 4–7" and matches
 * `ParaIdMapEntry.computedNumber/listPath` from a NUMBERED legal document
 * (`composeCitationResolver.ts`; COMPOSE-READ-REFERENCE-FIDELITY §4). Email
 * proposal citations anchor by **QuotedText** (verbatim prose, NFR-06) in
 * FREE-FORM email/attachment text that carries no legal numbering — feeding any
 * of them to that resolver yields `UNRECOGNIZED` → zero matches. Compose's actual
 * quoted-text primitives (`highlightCitedSpan` / `findCommentAnchorRange`) are
 * ProseMirror/TipTap-EDITOR-bound (`@tiptap/pm/model` marks/ranges); the
 * reconciliation reader is not an editor (a sandboxed `.eml` iframe + sanitized-
 * HTML div + plain-text folds). So NEITHER Compose primitive can anchor an email
 * QuotedText into this reader. This module is the email-domain quoted-text ANALOG
 * — NOT a fork of the legal-number resolver, and NOT a second legal-citation
 * mechanism. A coordination note is filed for spaarkeai-compose (NFR-11). Full
 * reasoning: notes/054-citation-navigation-complete.md.
 *
 * SCOPE: forward resolution only (citation → located segment + span). In-place
 * highlight is possible for reachable segments (attachment folds; the normalized
 * body projection). An ARCHIVED `.eml` body renders in a `sandbox=""` iframe
 * (NFR-03) the parent cannot reach into — a body citation there surfaces
 * "open original to view in context" (the reader owns that affordance).
 */

/** The email proposal citation triple (mirrors the server `Citation*` fields on `QueueFeedItem`). */
export interface EmailCitation {
  /** e.g. "body" or an attachment file name; scopes which segment(s) are searched. */
  source?: string | null;
  /** e.g. "body: sentence 1" or "OA_908068.pdf p.1" — informational; QuotedText is the anchor. */
  locator?: string | null;
  /** The verbatim cited text (NFR-06) — the authoritative anchor. */
  quotedText?: string | null;
}

/** The logical kind of a reader segment. */
export type ReaderSegmentKind = 'body' | 'attachment';

/** One addressable segment of the reader's normalized text (the body, or one attachment fold). */
export interface ReaderSegment {
  /** Stable id — `'body'` for the body, else the attachment id. */
  readonly segmentId: string;
  readonly kind: ReaderSegmentKind;
  /** Human label used for `Citation.source` scoping — `'body'` or the attachment name. */
  readonly label: string;
  /** The NORMALIZED text of this segment (markup stripped, whitespace collapsed) — the anchor space. */
  readonly text: string;
}

/** The reader's reference map — the ordered set of normalized segments a citation resolves against. */
export interface ReaderReferenceMap {
  readonly segments: readonly ReaderSegment[];
}

/** A located citation — the exact segment + span (offsets into the segment's NORMALIZED `text`). */
export interface QuotedCitationMatch {
  readonly located: true;
  readonly segmentId: string;
  readonly kind: ReaderSegmentKind;
  readonly label: string;
  /** Start offset (inclusive) into the segment's normalized text. */
  readonly start: number;
  /** End offset (exclusive) into the segment's normalized text. */
  readonly end: number;
  /** The exact matched slice of the normalized segment text. */
  readonly matchedText: string;
}

/** A citation that could NOT be located — surfaced to the reviewer, never silently mis-navigated. */
export interface QuotedCitationMiss {
  readonly located: false;
  /** `no-quoted-text` — the proposal carried no quote to anchor; `not-found` — the quote is absent from the reader (forged/mismatched). */
  readonly reason: 'no-quoted-text' | 'not-found';
}

export type QuotedCitationResolution = QuotedCitationMatch | QuotedCitationMiss;

/**
 * Normalize text into the anchor space: strip HTML markup, collapse ALL runs of
 * whitespace to a single space, trim. Applied identically to the reader text AND
 * the citation quote so a match is whitespace/markup-insensitive (the reader HTML
 * and the AI-extraction plain text differ only in those). Never throws.
 */
export function normalizeForAnchor(input: string | null | undefined): string {
  if (!input) return '';
  return input
    .replace(/<[^>]*>/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

/** Input shape for {@link buildReaderReferenceMap} — the reader's raw (un-normalized) content. */
export interface ReaderReferenceMapInput {
  /** `sprk_body` (HTML or plain) — becomes the `'body'` segment. */
  body?: string | null;
  /** Folded attachment contents — each becomes an `'attachment'` segment (skipped when it has no text). */
  attachments?: ReadonlyArray<{ attachmentId: string; name: string; text?: string | null }>;
}

/**
 * Build the reader reference map from the reader's raw content. The body segment
 * is emitted only when it normalizes to non-empty text; each attachment segment
 * is emitted only when it carries extractable text (an unextractable attachment
 * — no text — contributes NO segment, so a citation into it correctly resolves
 * `not-found` rather than matching stray body text).
 */
export function buildReaderReferenceMap(input: ReaderReferenceMapInput): ReaderReferenceMap {
  const segments: ReaderSegment[] = [];

  const bodyText = normalizeForAnchor(input.body);
  if (bodyText.length > 0) {
    segments.push({ segmentId: 'body', kind: 'body', label: 'body', text: bodyText });
  }

  for (const att of input.attachments ?? []) {
    const text = normalizeForAnchor(att.text);
    if (text.length > 0) {
      segments.push({ segmentId: att.attachmentId, kind: 'attachment', label: att.name, text });
    }
  }

  return { segments };
}

/** Case/whitespace-insensitive `source` → segment match: `'body'` hits the body segment; otherwise the label (attachment name) must match. */
function segmentMatchesSource(segment: ReaderSegment, source: string): boolean {
  const s = normalizeForAnchor(source).toLowerCase();
  if (s.length === 0) return false;
  if (segment.kind === 'body' && s === 'body') return true;
  return segment.label.toLowerCase() === s;
}

/**
 * Resolve an email proposal citation to the exact reader segment + span, or an
 * explicit NOT-LOCATED result. Total — never throws.
 *
 * - Requires a non-empty `quotedText` (the authoritative anchor); a proposal with
 *   no quote resolves `{ located: false, reason: 'no-quoted-text' }`.
 * - When `source` names a segment (`'body'` or an attachment name), the search is
 *   RESTRICTED to that segment — so a citation cannot accidentally match the same
 *   words in a different segment. When `source` is absent/unknown, all segments
 *   are searched in document order (body first).
 * - Matching is over normalized text (markup/whitespace-insensitive), first
 *   occurrence. No match anywhere ⇒ `{ located: false, reason: 'not-found' }` —
 *   NEVER a nearest/fuzzy guess.
 */
export function resolveQuotedCitation(citation: EmailCitation, map: ReaderReferenceMap): QuotedCitationResolution {
  const quoted = normalizeForAnchor(citation.quotedText);
  if (quoted.length === 0) return { located: false, reason: 'no-quoted-text' };

  const source = (citation.source ?? '').trim();
  const scoped = source.length > 0 ? map.segments.filter(seg => segmentMatchesSource(seg, source)) : map.segments;
  // If a source was named but matches no segment, fall back to searching all
  // segments (a mislabeled source must not turn a locatable quote into a miss).
  const candidates = scoped.length > 0 ? scoped : map.segments;

  const needle = quoted.toLowerCase();
  for (const segment of candidates) {
    const start = segment.text.toLowerCase().indexOf(needle);
    if (start >= 0) {
      const end = start + quoted.length;
      return {
        located: true,
        segmentId: segment.segmentId,
        kind: segment.kind,
        label: segment.label,
        start,
        end,
        matchedText: segment.text.slice(start, end),
      };
    }
  }

  return { located: false, reason: 'not-found' };
}
