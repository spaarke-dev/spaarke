/**
 * ComposeBannerStack.tsx — workspace banner stack (errors / warnings / status).
 *
 * Project:   spaarkeai-compose-r1
 * Extracted: R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC) — pure render
 *            composition lifted to keep the orchestrator thin.
 *
 * Renders, in this order:
 *   1. Save error MessageBar         — when `errorMessage` is non-null.
 *   2. Cross-user 409 conflict banner (Task 050) — when `checkoutStatus === 'conflict'`.
 *   3. Non-fatal checkout failure banner — when `checkoutStatus === 'failed'`.
 *   4. Multi-tab cancelled banner (Task 051) — when `checkoutStatus === 'cancelled'`.
 *   5. Import warnings banner — when the load-time import surfaced any warnings
 *      (suppressible via `hideImportWarnings`, UAT round-7 #8).
 *   6. Save-degradation banner (026-F5, task 012 r6) — SAVE-time warnings; its own
 *      family, NOT gated by `hideImportWarnings`; a clean save clears it.
 *   7. Pending assistant draft banner (Flow 5) — when there is a staged draft.
 *
 * The whole stack renders only when at least one row would surface; the parent
 * decides whether to mount it at all. This keeps the DOM minimal.
 *
 * AI actions (Summarize etc.) render in the Assistant pane via chat
 * messages — this stack owns only CRUD/lifecycle status.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 only; semantic tokens; no hex colors.
 *   - ADR-022: React 19; pure functional component.
 *
 * @see ./ComposeWorkspace.tsx (consumer)
 * @see ./ComposeWorkspace.types.ts (state shape)
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Button,
} from '@fluentui/react-components';
import { Dismiss16Regular, Info16Regular } from '@fluentui/react-icons';

import type {
  ComposeCheckoutLockedByInfo,
  ComposeCheckoutStatus,
  ComposePartialApplyInfo,
  ComposeReviewFindingsDegraded,
} from './ComposeWorkspace.types';
import type { ComposeAssistantToWorkspaceFlow } from '../types/compose-contracts';
import type { PendingRedlineError } from './hooks/usePendingRedline';
import { describeRedlineError } from './redlineFailureCopy';

export interface ComposeBannerStackProps {
  errorMessage: string | null;
  /** UAT #10/#11 (task 052): when true, the errorMessage is a Word co-authoring lock (HTTP 423). Render the
   *  honest "Open in Word" bar (warning intent) with Retry + Reload-from-Word actions instead of the generic
   *  save-error bar. There is no programmatic unlock — the actions are retry + pull-Word's-version. */
  saveErrorIsLock?: boolean;
  /** Retry the save (used by the lock bar — succeeds once Word is closed). */
  onRetrySave?: () => void;
  /** Reload the latest SPE bytes (used by the lock bar — pulls Word's version as the new baseline). */
  onReloadFromWord?: () => void;
  checkoutStatus: ComposeCheckoutStatus;
  checkoutLockedBy: ComposeCheckoutLockedByInfo | null;
  checkoutFailureMessage: string | null;
  importWarnings: Array<{ type: string; message: string }>;
  /**
   * UAT round-7 #8 — when true, the "Some formatting was simplified" import banner is suppressed (the
   * reviewer asked to remove the formatting warnings). The warnings still exist in the data; they just
   * don't render. Default false (every other consumer keeps the banner).
   */
  hideImportWarnings?: boolean;
  /**
   * 026-F5 (task 012, spaarkeai-compose-r6): SAVE-time degradation warnings — content the server
   * (and/or the client imported-model mapper) simplified/dropped while authoring the LAST successful
   * save. Rendered as its OWN dismissible warning banner, deliberately NOT gated by
   * {@link ComposeBannerStackProps.hideImportWarnings} (that UAT round-7 #8 suppression applies only
   * to the LOAD-time import-fidelity banner — save warnings must still render). `null`/empty (a clean
   * save, or a parent that predates the field) renders nothing — the parent dispatches `null` after a
   * clean save so a stale banner clears. Dismissal is signature-keyed sessionStorage (same convention
   * as the import banner, SEPARATE key): a NEW warning set (different signature) re-shows the banner.
   */
  saveDegradationWarnings?: Array<{ code: string; count: number }> | null;
  /**
   * Task 041 (spaarkeai-compose-r6, FR-06 — PDF intake): true while the mounted document was opened
   * FROM A PDF (server-synthesized docx, task 040). Renders the honest-lossiness notice: fixed-layout
   * PDF → the editable version may reflow/simplify formatting; saving creates a NEW Word document and
   * the original PDF is unchanged (version history is the safety net). No false "identical to source"
   * claim. Dismissable per mount; a fresh PDF open re-warns (honesty over convenience). The parent
   * clears it after the first successful save (the doc is a native docx from then on).
   */
  pdfSourceNotice?: boolean;
  pendingAssistantInsert: ComposeAssistantToWorkspaceFlow | null;
  /**
   * UAT #7 (compose-r2): a monotonically-incrementing token bumped by the parent on every
   * successful Save. A CHANGE in value (not its magnitude) surfaces a transient "Saved ✓"
   * MessageBar that auto-dismisses after {@link SAVE_SUCCESS_VISIBLE_MS}. 0 = no save yet.
   */
  saveSuccessToken?: number;
  /**
   * Prong 1 (task 055): populated when the last save applied only PART of the edit batch (some ops could
   * not be anchored server-side). Renders an honest warning bar — "Saved, but N of M edits couldn't be
   * anchored; please redo them" — INSTEAD of the plain "Saved ✓" success bar. Null/omitted on a clean save.
   */
  partialApply?: ComposePartialApplyInfo | null;
  /**
   * UAT-13 (2026-08-18, honest/safe): populated when a create-on-save persisted the document but its
   * parent/regarding association write failed — the doc is saved but ORPHANED (not filed under its
   * matter). Renders an honest, dismissible warning bar with a Retry action. Null when the association
   * succeeded or none was attempted.
   */
  associationWarning?: { documentRecordId: string } | null;
  /** UAT-13: re-run the host association write for {@link associationWarning}. Clears the banner on success. */
  onRetryAssociation?: () => void;
  /**
   * UAT (2026-08-18, owner): shown while the document has NOT been saved to the DMS yet — a file
   * uploaded / a review run persists nothing until the user Saves (the owner's SAVE-driven model).
   * Informs the user that the Document (and, when a review ran, its Analysis) isn't saved and that
   * Save creates it. `null` once the document is persisted. `reviewRan` tailors the copy (mentions the
   * Analysis) — the Analysis is created on Save only when a review actually ran.
   */
  unsavedDocumentNotice?: { reviewRan: boolean } | null;
  /**
   * UAT-12 (2026-08-18, honest/safe): true when the server's annotation read FAILED on load, so the
   * document's imported tracked changes / reviewer comments are EMPTY as a fallback — NOT proof the
   * document is clean. Renders a prominent honest warning so the reviewer never treats a
   * possibly-redlined legal document as clean. Dismissable per mount.
   */
  annotationReadFailed?: boolean;
  /**
   * ai-advanced-capabilities-agreements-r1 task 032 (FR-16 128KB budget, Leg B) — populated when a
   * prior agreement-review's findings could not be fully restored on reopen (the 128KB inline-payload
   * cap silently dropped the ledger entry server-side, or a present-but-corrupted findings payload
   * yielded zero usable items). Renders an honest "couldn't be fully restored" notice — the closed
   * guarantee's Leg B ("the truncated case shows an explicit notice — never silent absence"). Null on
   * a clean restore (the normal case).
   */
  reviewFindingsDegraded?: ComposeReviewFindingsDegraded | null;
  /**
   * Banner consolidation (2026-08-19): the pending-redline anchor-failure notice, HOISTED out of
   * ComposeEditor (where it was a hand-rolled bar BELOW the toolbar) into this single rail so all
   * passive Compose notices share one location + Fluent MessageBar styling. Surfaced by the editor via
   * `onRedlineErrorChange`; `null` (every target placed) renders nothing. The interactive
   * "N suggested edits pending / Accept all" bar stays by the editor — only this passive NOTICE moved.
   */
  pendingRedlineError?: PendingRedlineError | null;
  /** Banner consolidation (2026-08-19): dismiss {@link pendingRedlineError} — routes to the editor handle's clearRedlineError. */
  onClearRedlineError?: () => void;
  /**
   * Banner consolidation (2026-08-19): soft-failure notice for AI-draft materialization (FR-04,
   * task 016) — folded in from a stray MessageBar the host rendered in its own div. Null renders nothing.
   */
  composeDraftError?: string | null;
  /**
   * Banner consolidation (2026-08-19): "Create Summary Memo" negative-path notice (FR-14, task 051) —
   * the honest "no memo yet / generate failed" message, folded in from a stray host MessageBar. Null
   * renders nothing. No dismiss affordance (cleared by the parent at the next Generate/Email attempt).
   */
  memoActionMessage?: string | null;
  /**
   * R8 UAT item 8 — the change-summary negative-path notice. EXACT sibling of
   * {@link memoActionMessage} (same shape, same lifecycle, same rationale): the honest
   * "no tracked changes to summarise" / "couldn't generate" answer, rendered here rather than as a
   * stray host MessageBar. Null renders nothing; cleared by the parent at the next attempt.
   *
   * This one carries more weight than a convenience notice. The summary Action is ASKED for from the
   * Word menu, so "there is nothing to summarise" is an answer the user is owed — the alternative is
   * dispatching an empty operand, which is what makes the model fabricate a phantom "[Insertion]".
   */
  changeSummaryMessage?: string | null;
}

/** How long the transient "Saved ✓" confirmation stays up before auto-dismissing. */
const SAVE_SUCCESS_VISIBLE_MS = 4000;

// ---------------------------------------------------------------------------
// FR-21 (DEF-15, R3 UAT round-3 carry-in) — sessionStorage-backed dismissal
// ---------------------------------------------------------------------------
//
// The R2 UAT-round-3 fix (DEF-15) shipped a per-mount-only dismissal (a plain
// local flag, reset whenever a NEW `importWarnings` array reference arrived —
// see the owner note this replaces: "it need not persist across mounts").
// FR-21 upgrades that: the dismissal must persist for the rest of the browser
// SESSION (sessionStorage, not localStorage — a fresh tab/session re-warns).
// The sentinel is keyed by a CONTENT signature of the warnings (not object
// identity) so the R2 "a genuinely different import re-warns" behavior is
// preserved: a new document whose warnings differ in count/type/message gets
// a different signature and is NOT suppressed by a prior dismissal, while the
// SAME warnings set (re-render, remount, or the same document reopened this
// session) stays dismissed. No network call (ADR-028) — sessionStorage only.

/** Stable content signature for an import-warnings array — the sessionStorage dismissal key suffix. */
function importWarningsSignature(warnings: ReadonlyArray<{ type: string; message: string }>): string {
  return warnings.map(w => `${w.type}:${w.message}`).join('|');
}

const IMPORT_WARNINGS_DISMISS_KEY_PREFIX = 'spaarke-compose:import-warnings-dismissed:';

// 026-F5 (task 012, r6): SEPARATE dismissal key for the SAVE-degradation banner family — dismissing
// the load-time import banner must never suppress save warnings (and vice-versa).
const SAVE_DEGRADATION_DISMISS_KEY_PREFIX = 'spaarke-compose:save-degradation-dismissed:';

/** Best-effort sessionStorage read — never throws (private-browsing / quota / SSR-safe). */
function readDismissedFlag(prefix: string, signature: string): boolean {
  if (typeof window === 'undefined' || !window.sessionStorage || signature === '') return false;
  try {
    return window.sessionStorage.getItem(prefix + signature) === '1';
  } catch {
    return false;
  }
}

/** Best-effort sessionStorage write — never throws. */
function writeDismissedFlag(prefix: string, signature: string): void {
  if (typeof window === 'undefined' || !window.sessionStorage || signature === '') return;
  try {
    window.sessionStorage.setItem(prefix + signature, '1');
  } catch {
    // Ignore — a failed persist just means the per-mount React state still governs this render.
  }
}

/** Kept as thin wrappers so the FR-21 import-banner call sites read unchanged. */
function readImportWarningsDismissed(signature: string): boolean {
  return readDismissedFlag(IMPORT_WARNINGS_DISMISS_KEY_PREFIX, signature);
}
function writeImportWarningsDismissed(signature: string): void {
  writeDismissedFlag(IMPORT_WARNINGS_DISMISS_KEY_PREFIX, signature);
}

// ---------------------------------------------------------------------------
// 026-F5 (task 012, r6) — save-degradation warning copy
// ---------------------------------------------------------------------------
// Known degradation codes → one concise human sentence each; anything else falls back to the generic
// "Some content was simplified when saving (code ×N)." line. Codes are the server render-side /
// client mapper vocabulary (ComposeContentModel save path).

/**
 * FR-S02 (r8 task 011): the concurrency notice's code. It travels in the save response's
 * `degradationWarnings` array (one wire field, one dismissal), but renders as its own row — see the
 * partition in the component body for why.
 */
export const CONCURRENT_EXTERNAL_CHANGE_CODE = 'concurrent-external-change';

const SAVE_DEGRADATION_COPY: Record<string, string> = {
  // FR-S09 item 7 (r8 task 016): the document saved completely; only the Dataverse columns that DESCRIBE
  // it (size, SharePoint path) could not be brought up to date with it. Kept calm on purpose — the
  // user's work is fine and there is nothing for them to redo — but not silent, because those columns
  // are what the Documents grid shows and what "Open in SharePoint" follows, so a stale value is a wrong
  // number displayed rather than a hidden one. It clears itself on the next successful save.
  'document-metadata-stale':
    "Saved. The document's size and location details in Spaarke could not be refreshed just now, so " +
    'they may look out of date elsewhere until the next save.',
  // FR-S02 (r8 task 011): concurrency is LAST-WRITER-WINS with a warning. The save SUCCEEDED; someone
  // else's version landed between this document being opened and being saved, and this save is now the
  // current one. Version history is the honest recovery path — their content is not lost, it is the
  // previous version. Supersedes the 412 refusal shipped 2026-08-18, which left the user with unsaved
  // work in a browser tab and no way forward.
  'concurrent-external-change':
    'Someone else saved a new version of this document while you had it open. Your save is now the ' +
    'current version — use version history in the document management system to see or restore theirs.',
  'comment-anchor-dropped': "A comment's anchor could not be placed; the comment text was kept.",
  // UAT-23 (2026-08-18): an edit whose anchor drifted during editing so it couldn't be re-anchored on
  // save (a still-valid edit the op-log path had to drop) — surfaced instead of vanishing silently.
  'edit-anchor-lost': "An edit couldn't be saved because its location changed while editing — please redo it.",
  'hyperlink-target-dropped': "A link's target could not be preserved.",
  'tracked-format-change-dropped': 'A tracked formatting change was simplified.',
  'tracked-format-change-flattened': 'A tracked formatting change was simplified.',
  'comment-duplicate-dropped': 'A duplicate comment was dropped.',
  'op-log-ignored': 'Some pending edit operations were superseded by the saved document state.',
  'text-box-flattened': 'A text box was converted to regular text.',
  'complex-object-dropped': 'A drawing or embedded object could not be carried over.',
  // Task 013 (012-review F6): an id collision is not a "simplification" - the comment kept the
  // document's own version rather than the posted one.
  'comment-id-collision': "A comment could not be matched to its original; the document's version was kept.",
  'tracked-move-downgraded': 'A tracked move was saved as delete + insert.',
  'tracked-nested-revision-simplified': 'A nested tracked change was simplified.',
  'edited-paragraph-page-break-dropped': 'A page break inside an edited paragraph was dropped.',
  'edited-paragraph-line-break-dropped': 'A line break inside an edited paragraph was dropped.',
  'edited-table-structure-rebuilt': "An edited table's structure was rebuilt; some table formatting may be simplified.",
  // Warned-but-cryptic copy gap (2026-08-18, honest/safe): the server EMITS these degradation codes
  // but they had no friendly copy, so the banner fell through to the raw "…(code ×N)" line. Give each
  // an honest, plain-language sentence (no false "your content is intact" — several are real content
  // losses). Sibling of the DEF-002/compose-r8 fidelity work, which actually PRESERVES these; here we
  // only make the existing WARNING legible.
  'unrepresented-footnote-reference': "A footnote couldn't be carried into the saved document.",
  'unrepresented-endnote-reference': "An endnote couldn't be carried into the saved document.",
  'field-flattened-to-text':
    'A Word field (such as a cross-reference, date, or page number) was saved as plain text and will no longer update automatically.',
  'hard-tier-sdt-flattened': 'A content control (form field, dropdown, or date picker) was saved as plain text.',
  'comment-flattened': "A comment's rich content was simplified when saving.",
  'comment-anchor-flattened': "A comment's anchored range was simplified when saving.",
  'strikethrough-flattened': 'Strikethrough formatting was not preserved.',
  // Task 044 (r8): the merge's shortfall report emits this for a w:sym in an EDITED paragraph. Every
  // other code it emits already had copy — this was the one gap, and a banner that falls through to
  // the raw "(symbol-flattened ×2)" line is developer language in a user-facing sentence.
  'symbol-flattened': 'A special symbol in an edited paragraph was saved as ordinary text.',
  'numbering-unresolved': "An automatic list number couldn't be preserved and may differ.",
  'numstylelink-unresolved': "A linked list-numbering style couldn't be preserved.",
  'style-linked-numbering-dropped': 'Style-linked list numbering was simplified.',
  'heading-direct-numbering-dropped': 'Direct numbering on a heading was simplified.',
  'picture-bullet-unresolved': 'A picture bullet was replaced with a standard bullet.',
  'ruby-phonetic-guide-dropped': 'A phonetic (ruby) guide was not preserved.',
  'empty-table-dropped': 'An empty table was removed.',
  'template-merge-comment-threading-dropped': 'Comment reply threading from a merged template was simplified.',
  'template-merge-numbering-unresolved': "List numbering from a merged template couldn't be preserved.",
  'template-merge-story-reference-dropped':
    'A header, footer, or note reference from a merged template was not carried over.',
  'template-merge-unresolved-reference': "A cross-reference from a merged template couldn't be preserved.",
  // UAT-22 (2026-08-18): a session/advisory comment the user sees in the gutter whose anchored text
  // changed so it could NOT be written into the saved document — surfaced (counted) instead of silently
  // dropped. (Distinct from -dropped above, where the comment text is retained.)
  'comment-anchor-unresolved':
    "A comment couldn't be saved to the document because its anchored text changed — re-add it if it's still needed.",
  // Task 041 (FR-06, PDF intake): the pdf-intake-* degradation family (task 040's projector) — these
  // ride ContentModelWarnings on a PDF open and fold into the first model-path save like the docx
  // flatten codes. Honest, non-alarming copy; the general reflow fact also drives the PDF notice banner.
  'pdf-intake-fixed-layout-reflowed': 'Content was reflowed from the fixed PDF page layout.',
  'pdf-intake-page-chrome-dropped': 'Repeating page headers, footers, and page numbers were not carried over.',
  'pdf-intake-footnote-inlined': 'A footnote was placed inline in the main text.',
  'pdf-intake-formula-flattened': 'A formula was converted to plain text.',
  'pdf-intake-list-approximated': 'A bulleted line was converted to a list item.',
  'pdf-intake-table-style-approximated': "A table's PDF styling was replaced with standard table formatting.",
  'pdf-intake-table-cell-consolidated': 'Overlapping table cells were combined into one cell.',
  'pdf-intake-table-cell-dropped': "A table cell's text could not be placed and was left out — please check the table.",
  // UAT-07b (owner 2026-08-18): the render-on-save "flatten" widener family previously had NO friendly
  // copy, so users saw raw codes like "paragraph-style-flattened ×62". These are FORMATTING-only
  // simplifications (the text/content is intact) — folded into the concise summary below via
  // SAVE_DEGRADATION_LABEL rather than one sentence each. Entries here keep the per-code fallback honest
  // if the summary path is ever bypassed. See also DEF-002 (the actual widener engine work, UAT-07a).
  // 'indentation-dropped' and 'paragraph-style-flattened' RETIRED (#777, 2026-09-01) — same
  // Direction-B rule as 'internal-link-flattened' below. Neither has a producer any more: task 041 made
  // an edited block inherit w:ind from its base, and ComposeBlockMerge.InheritParagraphProperties now
  // carries an UNMODELED w:pStyle (only Normal/Heading1-6/ListParagraph are the model's to decide). They
  // were also whole-document open-time counts, which is how an untouched contract reported "×84 / ×85".
  // Leaving the copy would let a reader conclude the server still emits them.
  'section-break-flattened':
    'A section break was removed — page setup and headers from that point now follow the final section.',
  'tab-flattened': 'Some tab stops were simplified.',
  'table-formatting-flattened': 'Some table formatting was simplified.',
  'line-break-flattened': 'A line break was simplified.',
  // 'internal-link-flattened' RETIRED (UAT 2026-08-26 / D-1). An internal cross-reference is no longer
  // flattened — `w:anchor` is a self-contained scalar and is now carried, so the code has no producer.
  // Retiring the copy in the same change is the Direction-B rule: a taxonomy that advertises a code
  // nothing can emit is the same over-claim as a residual-loss doc that under-reports.
};

// UAT-07b: short NOUN labels for the common formatting-simplification codes, used to build ONE concise,
// plain-language summary ("indentation, paragraph styles, tables") instead of a wall of per-code sentences.
// Codes not listed here fall back to their full SAVE_DEGRADATION_COPY sentence (they are usually
// content-affecting, e.g. a dropped link target, and deserve their own line).
const SAVE_DEGRADATION_LABEL: Record<string, string> = {
  // 'indentation-dropped' / 'paragraph-style-flattened' RETIRED — see SAVE_DEGRADATION_COPY above.
  'section-break-flattened': 'section breaks',
  'tab-flattened': 'tab stops',
  'table-formatting-flattened': 'table formatting',
  'line-break-flattened': 'line breaks',
  // 'internal-link-flattened' RETIRED — see SAVE_DEGRADATION_COPY above.
};

/** One human-readable line per warning; known codes get friendly copy (+ ×N when repeated). */
function saveDegradationSentence(warning: { code: string; count: number }): string {
  const known = SAVE_DEGRADATION_COPY[warning.code];
  if (known) {
    return warning.count > 1 ? `${known} (×${warning.count})` : known;
  }
  return `Some content was simplified when saving (${warning.code}${warning.count > 1 ? ` ×${warning.count}` : ''}).`;
}

/**
 * UAT-07b — build ONE plain-language body for the save-degradation banner. Formatting-only
 * simplifications (SAVE_DEGRADATION_LABEL) collapse into a single reassuring sentence naming the
 * categories; any OTHER (content-affecting) codes keep their own full sentence. Deduplicates + orders
 * so the user reads "your text is intact; these formatting kinds were simplified" instead of
 * "paragraph-style-flattened ×62".
 */
function summarizeSaveDegradation(warnings: ReadonlyArray<{ code: string; count: number }>): string {
  const formattingLabels: string[] = [];
  const otherSentences: string[] = [];
  for (const w of warnings) {
    const label = SAVE_DEGRADATION_LABEL[w.code];
    if (label) {
      if (!formattingLabels.includes(label)) formattingLabels.push(label);
    } else {
      otherSentences.push(saveDegradationSentence(w));
    }
  }
  const parts: string[] = [];
  if (formattingLabels.length > 0) {
    const list =
      formattingLabels.length === 1
        ? formattingLabels[0]
        : `${formattingLabels.slice(0, -1).join(', ')} and ${formattingLabels[formattingLabels.length - 1]}`;
    parts.push(`Some formatting (${list}) was simplified to fit Compose's editor. Your text and content are intact.`);
  }
  if (otherSentences.length > 0) parts.push(otherSentences.join(' '));
  return parts.join(' ');
}

/** Stable content signature for a save-degradation set — the sessionStorage dismissal key suffix. */
function saveDegradationSignature(warnings: ReadonlyArray<{ code: string; count: number }>): string {
  return warnings.map(w => `${w.code}:${w.count}`).join('|');
}

const useStyles = makeStyles({
  // UAT round 1 #2 — the collapsed formatting-notice row. One line of chrome instead of a stack of
  // full-width MessageBars. Semantic tokens only (ADR-021).
  noticeRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXXS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  noticeIcon: { color: tokens.colorNeutralForeground3, flexShrink: 0 },
  noticeText: { color: tokens.colorNeutralForeground2 },
  noticePopover: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    maxWidth: '380px',
  },
  bannerStack: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
});

export function ComposeBannerStack(props: ComposeBannerStackProps): React.JSX.Element | null {
  const styles = useStyles();
  const {
    errorMessage,
    saveErrorIsLock = false,
    onRetrySave,
    onReloadFromWord,
    checkoutStatus,
    checkoutLockedBy,
    checkoutFailureMessage,
    importWarnings,
    hideImportWarnings = false,
    saveDegradationWarnings = null,
    pdfSourceNotice = false,
    pendingAssistantInsert,
    saveSuccessToken = 0,
    partialApply = null,
    associationWarning = null,
    onRetryAssociation,
    annotationReadFailed = false,
    unsavedDocumentNotice = null,
    reviewFindingsDegraded = null,
    pendingRedlineError = null,
    onClearRedlineError,
    composeDraftError = null,
    memoActionMessage = null,
    changeSummaryMessage = null,
  } = props;

  // Task 041 (FR-06, PDF intake): per-mount dismissal only — DELIBERATELY not sessionStorage-keyed
  // (unlike the import/save-warning banners): every fresh PDF open must re-warn (honesty over
  // convenience — the lossiness is per-open, and the parent clears the notice after the first save).
  const [pdfNoticeDismissed, setPdfNoticeDismissed] = React.useState(false);
  React.useEffect(() => {
    if (pdfSourceNotice) setPdfNoticeDismissed(false);
  }, [pdfSourceNotice]);
  const showPdfSourceNotice = pdfSourceNotice && !pdfNoticeDismissed;

  // FR-21 (DEF-15, R3 UAT round-3 carry-in): the "Document opened with N
  // simplification(s)" warning is informational and dismiss-and-stay-closed for
  // the SESSION (sessionStorage — see the helpers above), superseding the R2
  // per-mount-only flag. Keyed by content SIGNATURE (not object identity) so a
  // genuinely different import (new/changed warnings) still surfaces — only the
  // SAME warnings set (re-render, remount, or the same document reopened this
  // session) stays suppressed.
  const importWarningsSig = React.useMemo(() => importWarningsSignature(importWarnings), [importWarnings]);
  const [importWarningsDismissed, setImportWarningsDismissed] = React.useState<boolean>(() =>
    readImportWarningsDismissed(importWarningsSig)
  );
  React.useEffect(() => {
    setImportWarningsDismissed(readImportWarningsDismissed(importWarningsSig));
  }, [importWarningsSig]);

  const dismissImportWarnings = React.useCallback((): void => {
    writeImportWarningsDismissed(importWarningsSig);
    setImportWarningsDismissed(true);
  }, [importWarningsSig]);

  const showImportWarnings = importWarnings.length > 0 && !importWarningsDismissed && !hideImportWarnings;

  // 026-F5 (task 012, r6): the SAVE-degradation banner — its own family, its own signature-keyed
  // sessionStorage dismissal (SEPARATE key from the import banner), and deliberately NOT gated by
  // `hideImportWarnings` (that suppression covers only load-time import fidelity). A NEW warning set
  // (different signature) re-shows the banner; the parent passing null (a clean save) clears it.
  const saveWarnings = saveDegradationWarnings ?? [];
  const saveWarningsSig = React.useMemo(() => saveDegradationSignature(saveWarnings), [saveDegradationWarnings]);
  const [saveWarningsDismissed, setSaveWarningsDismissed] = React.useState<boolean>(() =>
    readDismissedFlag(SAVE_DEGRADATION_DISMISS_KEY_PREFIX, saveWarningsSig)
  );
  React.useEffect(() => {
    setSaveWarningsDismissed(readDismissedFlag(SAVE_DEGRADATION_DISMISS_KEY_PREFIX, saveWarningsSig));
  }, [saveWarningsSig]);
  const dismissSaveWarnings = React.useCallback((): void => {
    writeDismissedFlag(SAVE_DEGRADATION_DISMISS_KEY_PREFIX, saveWarningsSig);
    setSaveWarningsDismissed(true);
  }, [saveWarningsSig]);

  // FR-S02 (r8 task 011): the concurrency notice rides the SAME wire field and the same dismissal, but
  // it is NOT a degradation — nothing was simplified. Partition it out so the degradation banner's
  // "Some formatting was simplified when saving" title and its version-history trailer stay TRUE of
  // what they describe; both would be false of a concurrency notice.
  //
  // UAT-S-01 (2026-08-21, owner UAT of task 017): the trailer previously read "The original file is
  // unchanged until you save." That is FALSE everywhere this banner renders. `saveDegradationWarnings`
  // is dispatched from the SERVER's response to a COMPLETED save (ComposeWorkspace triggerSave, and
  // the post-save re-mount carry) — the bytes are already written and the simplification the banner
  // describes is already IN them. Telling the user their original is untouched at the exact moment it
  // was overwritten is the misreporting class Track S exists to remove (FR-S06/FR-S09). The trailer now
  // names the real recovery: version history, same safety net FR-S02's concurrency notice points at.
  const concurrencyNotice = saveWarnings.find(w => w.code === CONCURRENT_EXTERNAL_CHANGE_CODE) ?? null;
  const degradationOnlyWarnings = saveWarnings.filter(w => w.code !== CONCURRENT_EXTERNAL_CHANGE_CODE);
  const showSaveDegradation = degradationOnlyWarnings.length > 0 && !saveWarningsDismissed;
  // UAT round 1 #2 — how many formatting-notice families are currently showing. Drives the collapsed
  // row's count and its render gate. Deliberately a count of FAMILIES, not of individual warning codes:
  // the popover shows one paragraph per family, and "7 formatting notices" for a single simplification
  // message would be the same over-reporting the retired per-paragraph warnings were guilty of.
  const formattingNoticeCount = (showImportWarnings ? 1 : 0) + (showSaveDegradation ? 1 : 0);
  const showConcurrencyNotice = concurrencyNotice !== null && !saveWarningsDismissed;

  // UAT #7: a successful Save previously showed no confirmation — the button flipped from
  // "Saving" back to idle silently. Surface a transient success MessageBar whenever the parent
  // bumps `saveSuccessToken`, auto-dismissing after SAVE_SUCCESS_VISIBLE_MS. Keyed on the token
  // value (not a boolean) so a second identical Save re-triggers the banner. An in-flight save
  // error (a fresh `errorMessage`) suppresses the stale success row.
  const [showSaveSuccess, setShowSaveSuccess] = React.useState(false);
  React.useEffect(() => {
    if (saveSuccessToken <= 0) return;
    setShowSaveSuccess(true);
    const timer = setTimeout(() => setShowSaveSuccess(false), SAVE_SUCCESS_VISIBLE_MS);
    return () => clearTimeout(timer);
  }, [saveSuccessToken]);

  // Prong 1 (task 055): a partial-apply outcome (some ops couldn't be anchored). Dismissable; re-shows on
  // each NEW partial save (the parent passes a fresh object per saveSucceeded, cleared to null otherwise).
  const showPartialApply = !!partialApply && partialApply.unresolvedCount > 0;
  const [partialApplyDismissed, setPartialApplyDismissed] = React.useState(false);
  React.useEffect(() => {
    // Reset the dismissal whenever a new partial-apply summary arrives (or it clears).
    setPartialApplyDismissed(false);
  }, [partialApply]);
  const showPartialApplyBanner = showPartialApply && !partialApplyDismissed;

  // A partial save DID persist (the resolvable edits) but is not a clean success — suppress the plain
  // "Saved ✓" bar in favor of the honest partial-apply warning so the two never stack redundantly.
  const showSaveSuccessBanner = showSaveSuccess && !errorMessage && !showPartialApplyBanner;

  // Task 032 — same dismiss-and-reshow-on-a-new-instance pattern as `partialApply` above: dismissable,
  // re-shows if a NEW degraded-restore object arrives (a different session/count), stays dismissed for
  // the SAME object reference otherwise.
  const showReviewFindingsDegraded = !!reviewFindingsDegraded;
  const [reviewFindingsDegradedDismissed, setReviewFindingsDegradedDismissed] = React.useState(false);
  React.useEffect(() => {
    setReviewFindingsDegradedDismissed(false);
  }, [reviewFindingsDegraded]);
  const showReviewFindingsDegradedBanner = showReviewFindingsDegraded && !reviewFindingsDegradedDismissed;

  // UAT-13 (2026-08-18): the create-on-save persisted the document but its parent association write
  // failed — the doc is saved but not filed under its matter. Dismissable; re-shows on a NEW warning
  // (fresh object per failed association), stays dismissed for the same reference.
  const showAssociationWarning = !!associationWarning;
  const [associationWarningDismissed, setAssociationWarningDismissed] = React.useState(false);
  React.useEffect(() => {
    setAssociationWarningDismissed(false);
  }, [associationWarning]);
  const showAssociationWarningBanner = showAssociationWarning && !associationWarningDismissed;

  // UAT-12 (2026-08-18): the server annotation read failed — the document may CONTAIN tracked changes
  // and comments that couldn't be read, so it must NOT be presented as clean. Per-mount dismissal
  // (like the PDF notice): a fresh load re-warns (honesty over convenience).
  const [annotationReadFailedDismissed, setAnnotationReadFailedDismissed] = React.useState(false);
  React.useEffect(() => {
    if (annotationReadFailed) setAnnotationReadFailedDismissed(false);
  }, [annotationReadFailed]);
  const showAnnotationReadFailedBanner = annotationReadFailed && !annotationReadFailedDismissed;

  // UAT (2026-08-18, owner): the "not saved yet — Save to create" notice. Non-dismissible: it reflects
  // LIVE persistence state and clears automatically when the parent stops passing it (i.e. on Save).
  const showUnsavedDocumentNotice = !!unsavedDocumentNotice;

  // UAT-05 (owner 2026-08-18): the generic Save-error banner previously had NO dismiss ✕ (unlike the
  // warning/info banners), so a stale "Save error" could not be cleared. Add a local dismissal reset
  // whenever the message changes (a NEW error re-shows). The parent still owns `errorMessage`; this only
  // hides the CURRENT one on the client. The Word-lock variant keeps its Retry/Reload actions instead.
  const [errorDismissed, setErrorDismissed] = React.useState(false);
  React.useEffect(() => {
    setErrorDismissed(false);
  }, [errorMessage]);
  const showErrorBanner = !!errorMessage && !saveErrorIsLock && !errorDismissed;

  const showStack =
    showPdfSourceNotice ||
    showImportWarnings ||
    showSaveDegradation ||
    (!!errorMessage && (saveErrorIsLock || !errorDismissed)) ||
    !!pendingAssistantInsert ||
    showSaveSuccessBanner ||
    showPartialApplyBanner ||
    showReviewFindingsDegradedBanner ||
    showAssociationWarningBanner ||
    showAnnotationReadFailedBanner ||
    showUnsavedDocumentNotice ||
    !!pendingRedlineError ||
    !!composeDraftError ||
    !!memoActionMessage ||
    !!changeSummaryMessage ||
    checkoutStatus === 'conflict' ||
    checkoutStatus === 'failed' ||
    checkoutStatus === 'cancelled';

  if (!showStack) return null;

  return (
    <div className={styles.bannerStack}>
      {showPdfSourceNotice ? (
        // Task 041 (FR-06, PDF intake) — the honest-lossiness notice. Fluent v9 semantic tokens only
        // (ADR-021; MessageBar intent colors are theme-derived — correct in light AND dark). Copy
        // contract: fixed-layout honesty, NO "identical to source" claim, version history / original
        // PDF preserved as the safety net, save-creates-a-new-Word-document expectation set up front.
        <MessageBar intent="info" data-testid="compose-workspace-pdf-source-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Opened from PDF</MessageBarTitle>
            This document was converted from a fixed-layout PDF, so some formatting was simplified and content may
            reflow. Saving creates a new Word document — the original PDF is unchanged and remains available with its
            version history.
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-pdf-source-dismiss"
                onClick={() => setPdfNoticeDismissed(true)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {showSaveSuccessBanner ? (
        <MessageBar intent="success" data-testid="compose-workspace-save-success-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Saved to matter files</MessageBarTitle>
            Your document was saved and is available in the matter&apos;s files.
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-save-success-dismiss"
                onClick={() => setShowSaveSuccess(false)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {showPartialApplyBanner && partialApply ? (
        // Prong 1 (task 055): the save PERSISTED the resolvable edits but couldn't anchor some ops — honest
        // "please redo" prompt (never silently applied a wrong edit, never silently dropped one). The
        // resolved edits are safe; the user re-does just the unresolved ones.
        <MessageBar intent="warning" data-testid="compose-workspace-partial-apply-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Some edits couldn&apos;t be saved</MessageBarTitle>
            {`Saved ${partialApply.appliedCount} of ${partialApply.total} edit${partialApply.total === 1 ? '' : 's'}. ` +
              `${partialApply.unresolvedCount} edit${partialApply.unresolvedCount === 1 ? '' : 's'} couldn't be ` +
              `placed in the document and ${partialApply.unresolvedCount === 1 ? 'was' : 'were'} not saved — please redo ` +
              `${partialApply.unresolvedCount === 1 ? 'it' : 'them'}. Nothing else was lost.`}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-partial-apply-dismiss"
                onClick={() => setPartialApplyDismissed(true)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {showReviewFindingsDegradedBanner && reviewFindingsDegraded ? (
        // Task 032 (FR-16 128KB budget, Leg B) — an honest "couldn't be fully restored" notice.
        // 'skipped': the ledger read shows no findings at all but a same-tab marker recorded a prior
        // review's results (the 128KB inline-payload cap likely dropped the entry server-side).
        // 'malformed': a findings-shaped entry IS present but every item failed the projection guard.
        <MessageBar intent="warning" data-testid="compose-workspace-review-findings-degraded-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Review results couldn&apos;t be fully restored</MessageBarTitle>
            {reviewFindingsDegraded.reason === 'skipped'
              ? `A prior review of this document (about ${reviewFindingsDegraded.expectedCount} finding${reviewFindingsDegraded.expectedCount === 1 ? '' : 's'}) could not be restored — the results may have exceeded the storage limit for this session. Re-run the review to refresh the findings.`
              : "A prior review's stored results were incomplete and couldn't be restored. Re-run the review to refresh the findings."}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-review-findings-degraded-dismiss"
                onClick={() => setReviewFindingsDegradedDismissed(true)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {showUnsavedDocumentNotice ? (
        // UAT (2026-08-18, owner): SAVE-driven persistence — nothing is saved until the user clicks Save.
        // Informational (info intent), non-dismissible; clears automatically once the document is saved.
        <MessageBar intent="info" data-testid="compose-workspace-unsaved-notice" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Not saved yet</MessageBarTitle>
            {unsavedDocumentNotice?.reviewRan
              ? 'This document and its analysis haven’t been saved yet — click Save to create them.'
              : 'This document hasn’t been saved yet — click Save to keep it.'}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {showAnnotationReadFailedBanner ? (
        // UAT-12 (2026-08-18): the server couldn't read this document's tracked changes / comments —
        // it may CONTAIN redlines or reviewer comments that are NOT shown. Never let a legal reviewer
        // treat it as clean. Prominent (warning intent), dismissable per mount.
        <MessageBar intent="warning" data-testid="compose-workspace-annotation-read-failed-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Tracked changes and comments couldn&apos;t be read</MessageBarTitle>
            {"This document's existing tracked changes and comments couldn't be read, so they aren't " +
              "shown here. Don't treat this document as clean — open it in Word to review its changes and " +
              'comments before relying on it.'}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-annotation-read-failed-dismiss"
                onClick={() => setAnnotationReadFailedDismissed(true)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {showAssociationWarningBanner ? (
        // UAT-13 (2026-08-18): the document SAVED but its parent-association write failed — honest
        // "saved but not filed" notice with a Retry (re-runs the host association write). The document
        // itself is safe; only the matter/regarding link is missing.
        <MessageBar intent="warning" data-testid="compose-workspace-association-warning-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Saved, but not filed under its matter</MessageBarTitle>
            {'This document was saved, but it couldn’t be linked to its matter/parent record. ' +
              'It won’t appear under that record until the link is written.'}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-association-warning-dismiss"
                onClick={() => setAssociationWarningDismissed(true)}
              />
            }
          >
            {onRetryAssociation ? (
              <Button
                appearance="primary"
                size="small"
                data-testid="compose-workspace-association-warning-retry"
                onClick={onRetryAssociation}
              >
                Retry
              </Button>
            ) : null}
          </MessageBarActions>
        </MessageBar>
      ) : null}

      {errorMessage && saveErrorIsLock ? (
        // UAT #10/#11 (task 052): honest Word co-authoring lock bar. No programmatic unlock exists — offer
        // Retry (works once Word is closed) + Reload from Word (pull Word's version as the new baseline).
        <MessageBar intent="warning" data-testid="compose-workspace-word-lock-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Open in Word</MessageBarTitle>
            {errorMessage}
          </MessageBarBody>
          <MessageBarActions>
            {onRetrySave ? (
              <Button size="small" appearance="primary" onClick={onRetrySave} data-testid="compose-word-lock-retry">
                Retry Save
              </Button>
            ) : null}
            {onReloadFromWord ? (
              <Button size="small" onClick={onReloadFromWord} data-testid="compose-word-lock-reload">
                Reload from Word
              </Button>
            ) : null}
          </MessageBarActions>
        </MessageBar>
      ) : showErrorBanner ? (
        <MessageBar intent="error" data-testid="compose-workspace-error-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Save error</MessageBarTitle>
            {errorMessage}
          </MessageBarBody>
          {/* UAT-05: dismiss ✕ (the error banner previously had none). */}
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-error-dismiss"
                onClick={() => setErrorDismissed(true)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {checkoutStatus === 'conflict' && checkoutLockedBy ? (
        <MessageBar intent="warning" data-testid="compose-workspace-checkout-conflict-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Document is checked out</MessageBarTitle>
            {checkoutLockedBy.checkedOutAt
              ? `Locked by ${checkoutLockedBy.name} since ${new Date(checkoutLockedBy.checkedOutAt).toLocaleString()}. You can view the document but changes cannot be saved until the lock is released.`
              : `Locked by ${checkoutLockedBy.name}. You can view the document but changes cannot be saved until the lock is released.`}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'failed' && checkoutFailureMessage ? (
        <MessageBar intent="info" data-testid="compose-workspace-checkout-failed-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Lock not acquired</MessageBarTitle>
            {checkoutFailureMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'cancelled' ? (
        <MessageBar intent="info" data-testid="compose-workspace-checkout-cancelled-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>This session is no longer active</MessageBarTitle>
            This document is open in another Compose session. Refresh this page to attempt to acquire the lock again, or
            close this tab.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {/* ═══ UAT round 1 #2 (r8, 2026-09-02) — FORMATTING NOTICES, COLLAPSED ═══
          The two formatting-notice families (load-time "simplified view" and save-time "simplified when
          saving") used to render as full-width MessageBars stacked in this rail. On a real contract that
          is several lines of chrome above the document, every session — the owner's "very intrusive,
          takes a lot of space".

          They now collapse into ONE compact row: a count plus a popover holding the same copy verbatim.
          Nothing is lost, and both keep their own sessionStorage dismissal keys, so dismissing the load
          notice still does not dismiss a later save notice (026-F5) — the two families stay independent
          behind one affordance.

          WHY HERE AND NOT IN THE TOOLBAR: the owner asked for this as part of the toolbar redesign. The
          warning STATE lives in ComposeWorkspace and the toolbar lives inside ComposeEditor, so hosting
          it there would mean threading this state through a 4,000-line component purely for placement.
          This rail sits immediately above the toolbar, so the affordance reads as adjacent to it while
          the state stays where it is owned. Flagged for the owner to redirect if the exact position
          matters more than the coupling.

          ERRORS ARE DELIBERATELY NOT COLLAPSED. A failed save, a checkout conflict and a redline anchor
          failure stay full-width: they are actionable and blocking, and hiding one behind a popover would
          be the opposite of the never-silent rule the rest of this release enforces. ═══ */}
      {formattingNoticeCount > 0 ? (
        <div className={styles.noticeRow} data-testid="compose-workspace-formatting-notices">
          <Info16Regular className={styles.noticeIcon} aria-hidden />
          <Text size={200} className={styles.noticeText}>
            {formattingNoticeCount === 1 ? '1 formatting notice' : `${formattingNoticeCount} formatting notices`}
          </Text>
          <Popover withArrow positioning="below-start" size="small">
            <PopoverTrigger disableButtonEnhancement>
              <Button appearance="transparent" size="small" data-testid="compose-workspace-formatting-notices-open">
                View
              </Button>
            </PopoverTrigger>
            <PopoverSurface data-testid="compose-workspace-formatting-notices-popover">
              <div className={styles.noticePopover}>
                {showImportWarnings ? (
                  <div data-testid="compose-workspace-import-warning-notice">
                    <Text weight="semibold" as="p">
                      Some formatting was simplified
                    </Text>
                    <Text size={200} as="p">
                      This document uses advanced Word features that Compose shows in a simplified view. Your original
                      file isn&apos;t changed until you save.
                    </Text>
                  </div>
                ) : null}
                {showSaveDegradation ? (
                  <div data-testid="compose-workspace-save-degradation-notice">
                    <Text weight="semibold" as="p">
                      Some formatting was simplified when saving
                    </Text>
                    <Text size={200} as="p">
                      {`${summarizeSaveDegradation(degradationOnlyWarnings)} These changes are in the version you just saved. The previous version is still available in version history.`}
                    </Text>
                  </div>
                ) : null}
              </div>
            </PopoverSurface>
          </Popover>
          <Button
            appearance="transparent"
            size="small"
            aria-label="Dismiss formatting notices"
            icon={<Dismiss16Regular />}
            data-testid="compose-workspace-formatting-notices-dismiss"
            onClick={() => {
              // Each family keeps its OWN dismissal key (026-F5). Dismissing the collapsed row dismisses
              // whichever families are currently showing — not a single shared flag, which would make a
              // later save notice inherit an earlier load dismissal.
              if (showImportWarnings) dismissImportWarnings();
              if (showSaveDegradation) dismissSaveWarnings();
            }}
          />
        </div>
      ) : null}

      {showConcurrencyNotice ? (
        // FR-S02 (r8 task 011): concurrency is last-writer-wins with a warning. The save SUCCEEDED and is
        // now the current version; the other writer's content is the PREVIOUS version, not lost. Version
        // history is the recovery path, and saying so is the whole point — the 412 refusal this replaces
        // left the user with unsaved work in a tab and no way forward.
        <MessageBar intent="warning" data-testid="compose-workspace-concurrency-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Someone else saved this document while you had it open</MessageBarTitle>
            {SAVE_DEGRADATION_COPY[CONCURRENT_EXTERNAL_CHANGE_CODE]}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-concurrency-dismiss"
                onClick={dismissSaveWarnings}
              />
            }
          />
        </MessageBar>
      ) : null}

      {/* Banner consolidation (2026-08-19): the pending-redline anchor-failure NOTICE, hoisted here
          from a hand-rolled bar BELOW the toolbar inside ComposeEditor. Now a Fluent MessageBar in the
          single rail (above the toolbar), matching every other notice's font/icon/chrome. Copy is
          verbatim from the former in-editor bar. */}
      {pendingRedlineError ? (
        <MessageBar intent="warning" data-testid="compose-redline-error" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>
              {pendingRedlineError.kind === 'target_deleted'
                ? "Suggested edit's target is gone"
                : "Suggested edit couldn't be placed"}
            </MessageBarTitle>
            {/* FR-C05 outcome 3 (r8 task 052): a DELETED target gets its own sentence. It used to share
                the generic "wording differs slightly" copy with an unresolvable citation, which was
                simply untrue — the anchor resolved fine, the paragraph it named is no longer there,
                and "re-select the passage and try again" is advice the user cannot act on.

                FR-C07 (r8 task 053): the "wording differs slightly" branch is GONE, and this is the
                one place it was ever rendered. It survived because ONE branch served two unrelated
                states, and for the one that actually fires now it was a fabrication:

                  - `source: 'anchored'` — the suggestion named a `target_para_id`/`target_ref` and
                    that anchor did not resolve. NO TEXT WAS COMPARED, so there is no wording
                    difference to report; telling the user their wording drifted invented a cause and
                    sent them to re-word a clause that was never the problem. Since task 051 every
                    newly produced edit is anchored, so this is the branch a user can actually hit —
                    which is exactly why the copy had to become true.
                  - `source: 'legacy-replay'` — a REPLAYED pre-anchor ledger entry (FR-C06) whose
                    quoted prose is not in the document. Here prose really was compared, and the
                    honest answer is not "your wording differs" but "this predates paragraph
                    references — re-run it", which is a remedy the user can act on in one click.

                See `projects/spaarkeai-compose-r8/notes/wording-differs-elimination-trace.md`. */}
            {describeRedlineError(pendingRedlineError)}
          </MessageBarBody>
          {onClearRedlineError ? (
            <MessageBarActions
              containerAction={
                <Button
                  appearance="transparent"
                  aria-label="Dismiss"
                  icon={<Dismiss16Regular />}
                  data-testid="compose-redline-error-dismiss"
                  onClick={onClearRedlineError}
                />
              }
            />
          ) : null}
        </MessageBar>
      ) : null}

      {/* Banner consolidation (2026-08-19): FR-04 (task 016) AI-draft materialization soft failure,
          folded in from a stray host MessageBar so it shares this rail's location + styling. */}
      {composeDraftError ? (
        <MessageBar intent="warning" data-testid="compose-workspace-draft-error" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Could not insert AI draft</MessageBarTitle>
            {composeDraftError}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {/* Banner consolidation (2026-08-19): FR-14 (task 051) "Create Summary Memo" negative-path notice,
          folded in from a stray host MessageBar. Cleared by the parent at the next Generate/Email attempt. */}
      {memoActionMessage ? (
        <MessageBar intent="warning" data-testid="compose-workspace-memo-action-message" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Create Summary Memo</MessageBarTitle>
            {memoActionMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {/* R8 UAT item 8: the change-summary negative path — "no tracked changes to summarise" or a
          failure. Mirrors the memo notice above; cleared by the parent at the next attempt. */}
      {changeSummaryMessage ? (
        <MessageBar intent="info" data-testid="compose-workspace-change-summary-message" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Summarise changes</MessageBarTitle>
            {changeSummaryMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {pendingAssistantInsert ? (
        <MessageBar intent="info" data-testid="compose-workspace-pending-assistant-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Assistant draft ready</MessageBarTitle>A draft from the Assistant is staged for insertion.
            (R2 wires the insert action; R1 acknowledges receipt only.)
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </div>
  );
}
