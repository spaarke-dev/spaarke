/**
 * advisoryNoteFormatting.ts — the ONE shared source for how an advisory (NDA/agreement-review)
 * comment thread's content is split into labelled aspects, consumed by BOTH the on-screen
 * `ComposeCommentGutter` display AND the Word `w:comment` export mapping
 * (`composeSessionCommentThreadsToAnchoredComments`, `ComposeCommentThread.types.ts`)
 * (ai-advanced-capabilities-agreements-r1 task 052, spec FR-15).
 *
 * ROOT CAUSE this hoist fixes (see `projects/ai-advanced-capabilities-agreements-r1/notes/
 * word-comment-export-gap.md`): the "Grounded fact" → "Flagged clause" / "Advisory judgment" →
 * "Assessment says" relabeling used to live ONLY inside `ComposeCommentGutter.tsx` (display-only)
 * — the docx export read `thread.text`/`author`/`timestamp` verbatim, so a saved-then-reopened
 * Word comment never matched what the reviewer saw on screen. Moving the label map + the
 * discrete-vs-legacy decision here means there is exactly ONE place that decides "what does this
 * thread's flagged clause / assessment / standard look like" — both consumers call it.
 *
 * TWO SOURCES, ONE OUTPUT SHAPE ({@link AdvisoryNoteSegment}):
 *  1. **Discrete fields** (`flaggedClause`/`assessment` — added by
 *     `ai-advanced-capabilities-agreements-r1` task 002's Action-output schema split; threads
 *     created after that split carry them via `ComposeEditor.placeAdvisoryComments` →
 *     `useComposeCommentThreads.createThread`'s metadata parameter). When present, segments are
 *     built DIRECTLY from them — no string-parsing, nothing to get wrong.
 *  2. **Legacy marker-parsed text** (`thread.text` alone) — pre-002 threads (or any thread whose
 *     caller never wired the discrete fields) carry the model's explanation as ONE string,
 *     optionally containing the model's own "Grounded fact: … Advisory judgment: …" (or older
 *     "Judgment: …") markers. {@link parseAdvisoryNote} splits + relabels those; text with no
 *     recognized marker degrades to a single UNLABELLED segment — never fabricated structure.
 *
 * A thread with no advisory-specific metadata at all (a plain session Comments-panel thread —
 * see {@link isAdvisoryCommentThread}) never enters either path: it is not an advisory finding,
 * so its text is exported/rendered completely unchanged.
 *
 * @see ./ComposeCommentGutter.tsx — the on-screen consumer (re-exports `parseAdvisoryNote` /
 *      `AdvisoryNoteSegment` from here so its existing import sites are unaffected)
 * @see ./ComposeCommentThread.types.ts — `composeSessionCommentThreadsToAnchoredComments`, the
 *      export-mapping consumer ({@link composeAdvisoryCommentExportText})
 * @see projects/ai-advanced-capabilities-agreements-r1/notes/word-comment-export-gap.md
 * @see projects/ai-advanced-capabilities-agreements-r1/tasks/052-word-comment-export-mirror.poml
 */

/** The subset of {@link ../widgets/ComposeCommentThread.types.ComposeCommentThreadModel} this
 *  module's functions read. Declared structurally here (rather than importing the full model
 *  type) so this leaf module has no dependency on `ComposeCommentThread.types.ts` — that file
 *  imports FROM here, not the other way around (avoids a cycle). */
export interface AdvisoryNoteThreadFields {
  /** The thread's combined/root comment text — the legacy (pre-002) source of the note. */
  text: string;
  /** Coarse qualitative risk signal. Presence (along with sectionRef/standardRef) marks a thread
   *  as an advisory finding rather than a plain session comment — see {@link isAdvisoryCommentThread}. */
  riskLevel?: string;
  /** Section/clause reference from the review Action's output. */
  sectionRef?: string;
  /** Grounded-fact prose — "what the clause does" (task 002 discrete field). */
  flaggedClause?: string;
  /** Reasoned-judgment prose — "why it matters" (task 002 discrete field). */
  assessment?: string;
  /** Standard/playbook citation the flag references (e.g. "B5 - Use & disclosure obligations"). */
  standardRef?: string;
  /** Full resolved standard-clause text, when the thread happens to carry it (payload-driven —
   *  nothing populates this today; see the task 052 execution notes). Absent ⇒ export cites
   *  `standardRef` alone. */
  standardText?: string;
}

/** One labelled aspect of an advisory note ("Grounded fact" / "Advisory judgment"), or an unlabelled run. */
export interface AdvisoryNoteSegment {
  /** The aspect label (e.g. "Grounded fact"), or undefined for text with no recognized label. */
  label?: string;
  /** The aspect's prose. */
  body: string;
}

/** The labels the review Action historically authored its advisory explanations with (case-insensitive). */
const ADVISORY_NOTE_LABELS = ['Grounded fact', 'Advisory judgment', 'Judgment'] as const;

/**
 * The DISPLAY labels the reviewer asked for (UAT round-6 #5), mapped from the model's authored
 * markers: "Grounded fact" → "Flagged clause", "Advisory judgment" / "Judgment" → "Assessment
 * says". Detection still keys on the model's original words; only the rendered/exported label
 * changes. This is the SOLE label map in Compose — do not duplicate it (task 052 grep-proof).
 */
const ADVISORY_NOTE_DISPLAY_LABEL: Record<string, string> = {
  'grounded fact': 'Flagged clause',
  'advisory judgment': 'Assessment says',
  judgment: 'Assessment says',
};

/**
 * UAT round-5 #7 — split an advisory note into its "Grounded fact" / "Advisory judgment" aspects so each
 * renders as its own labelled, separated paragraph. The Action historically authored explanations as
 * "Grounded fact: … Advisory judgment: …" (also older "Judgment: …"); this splits on those markers. Text
 * with no recognized label returns a single unlabelled segment (rendered/exported plainly, unchanged) —
 * so a note that doesn't follow the convention never gets mangled. LEGACY PATH ONLY — a thread carrying
 * the task-002 discrete `flaggedClause`/`assessment` fields never reaches this parser; see
 * {@link getAdvisoryNoteSegments}.
 */
export function parseAdvisoryNote(text: string): AdvisoryNoteSegment[] {
  const trimmed = (text ?? '').trim();
  if (!trimmed) return [];
  // Find every label occurrence (label + following ":"/"—"/"-"), in document order.
  const pattern = new RegExp(`\\b(${ADVISORY_NOTE_LABELS.join('|')})\\b\\s*[:—-]\\s*`, 'gi');
  const marks: { label: string; start: number; bodyStart: number }[] = [];
  for (let m = pattern.exec(trimmed); m !== null; m = pattern.exec(trimmed)) {
    marks.push({ label: m[1], start: m.index, bodyStart: m.index + m[0].length });
  }
  if (marks.length === 0) return [{ body: trimmed }];
  const segments: AdvisoryNoteSegment[] = [];
  // Any prose before the first label is kept as an unlabelled lead segment (never dropped).
  if (marks[0].start > 0) {
    const lead = trimmed.slice(0, marks[0].start).trim();
    if (lead) segments.push({ body: lead });
  }
  for (let i = 0; i < marks.length; i++) {
    const end = i + 1 < marks.length ? marks[i + 1].start : trimmed.length;
    const body = trimmed.slice(marks[i].bodyStart, end).trim();
    // Display the reviewer's preferred label ("Flagged clause" / "Assessment says"), falling back
    // to the model's own word for any unmapped label.
    const label = ADVISORY_NOTE_DISPLAY_LABEL[marks[i].label.toLowerCase()] ?? marks[i].label;
    segments.push({ label, body });
  }
  return segments;
}

/**
 * True when `thread` carries ANY advisory-review-specific metadata (riskLevel / sectionRef /
 * standardRef / flaggedClause / assessment) — the same discriminant `ComposeCommentGutter.tsx`
 * already relied on ("Only advisory notes carry a sectionRef; a plain session comment has none").
 * A plain session Comments-panel thread carries none of these and is NEVER relabeled/restructured
 * — its text is exported/rendered completely verbatim, so this gate is what keeps the
 * `composeCommentThreadsToDocxAnnotations`/`composeSessionCommentThreadsToAnchoredComments` unit
 * tests (which use plain fixture threads) passing unchanged.
 */
export function isAdvisoryCommentThread(thread: AdvisoryNoteThreadFields): boolean {
  return Boolean(
    thread.riskLevel || thread.sectionRef || thread.standardRef || thread.flaggedClause || thread.assessment
  );
}

/**
 * The ONE function both the gutter and the export mapping call to get a thread's labelled
 * aspects. Discrete fields (task 002) win when present — no string-parsing, nothing to get
 * wrong. Their absence (legacy thread, or a plain session comment routed here anyway) degrades to
 * {@link parseAdvisoryNote} over `thread.text` — which itself degrades to a single unlabelled
 * segment when the text carries no recognized marker. Never throws, never fabricates a label the
 * source data didn't support.
 */
export function getAdvisoryNoteSegments(
  thread: Pick<AdvisoryNoteThreadFields, 'text' | 'flaggedClause' | 'assessment'>
): AdvisoryNoteSegment[] {
  if (thread.flaggedClause) {
    const segments: AdvisoryNoteSegment[] = [{ label: 'Flagged clause', body: thread.flaggedClause }];
    if (thread.assessment) segments.push({ label: 'Assessment says', body: thread.assessment });
    return segments;
  }
  return parseAdvisoryNote(thread.text);
}

/**
 * Composes the FLAT `w:comment` export string for one advisory thread's root comment — the
 * export-mapping seam (task 052 step-0 audit: composing here, rather than at
 * `ComposeEditor.placeAdvisoryComments`-time, keeps the thread's raw fields intact for the gutter
 * to render as separate styled elements + a clickable Standard chip; see the task's execution
 * notes for the full seam-choice rationale).
 *
 * - Plain (non-advisory) threads: `thread.text`, completely unchanged — see
 *   {@link isAdvisoryCommentThread}. Covers every existing plain-comment fixture/test.
 * - Advisory threads: joins {@link getAdvisoryNoteSegments}' labelled aspects ("Flagged clause: …",
 *   "Assessment says: …") with blank-line separators, then appends a "Standard: {ref}" line (task
 *   052 scope lift — see `ComposeCommentThread.types.ts`'s `ComposeCommentThreadModel.standardRef`
 *   doc) when the thread carries a `standardRef`, including the full standard clause text after an
 *   em-dash when the thread happens to carry `standardText` too (payload-driven — "full clause
 *   text when available").
 *
 * PAYLOAD-DRIVEN ONLY (durable-recall coordination, tasks 030-032): this function reads nothing
 * but the thread's own fields — a re-materialized (recalled) thread with the same discrete fields
 * as a live one composes byte-identical export text, regardless of provenance.
 *
 * Replies are NEVER passed through this function — a reply is follow-up discussion (its own
 * `ComposeCommentReply`, which carries no advisory metadata) and exports as raw text, unchanged.
 */
export function composeAdvisoryCommentExportText(thread: AdvisoryNoteThreadFields): string {
  if (!isAdvisoryCommentThread(thread)) return thread.text;

  const segments = getAdvisoryNoteSegments(thread);
  const body = segments.map(seg => (seg.label ? `${seg.label}: ${seg.body}` : seg.body)).join('\n\n');

  if (!thread.standardRef) return body;

  const standardLine = thread.standardText
    ? `Standard: ${thread.standardRef} — ${thread.standardText}`
    : `Standard: ${thread.standardRef}`;

  return body ? `${body}\n\n${standardLine}` : standardLine;
}
