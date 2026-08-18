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
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Button,
} from '@fluentui/react-components';
import { Dismiss16Regular } from '@fluentui/react-icons';

import type {
  ComposeCheckoutLockedByInfo,
  ComposeCheckoutStatus,
  ComposePartialApplyInfo,
  ComposeReviewFindingsDegraded,
} from './ComposeWorkspace.types';
import type { ComposeAssistantToWorkspaceFlow } from '../types/compose-contracts';

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
   * ai-advanced-capabilities-agreements-r1 task 032 (FR-16 128KB budget, Leg B) — populated when a
   * prior agreement-review's findings could not be fully restored on reopen (the 128KB inline-payload
   * cap silently dropped the ledger entry server-side, or a present-but-corrupted findings payload
   * yielded zero usable items). Renders an honest "couldn't be fully restored" notice — the closed
   * guarantee's Leg B ("the truncated case shows an explicit notice — never silent absence"). Null on
   * a clean restore (the normal case).
   */
  reviewFindingsDegraded?: ComposeReviewFindingsDegraded | null;
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

const SAVE_DEGRADATION_COPY: Record<string, string> = {
  'comment-anchor-dropped': "A comment's anchor could not be placed; the comment text was kept.",
  // UAT-22 (2026-08-18): a session/advisory comment whose anchored text changed so it could NOT be
  // written into the saved document (distinct from -dropped above, where the text is retained).
  'comment-anchor-unresolved':
    "A comment couldn't be saved to the document because its anchored text changed — re-add it if it's still needed.",
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
  'comment-anchor-unresolved': 'A comment could not be anchored and was not saved.',
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
  'indentation-dropped': 'Some indentation was simplified.',
  'paragraph-style-flattened': 'Some paragraph styles were simplified.',
  'section-break-flattened': 'A section break was simplified.',
  'tab-flattened': 'Some tab stops were simplified.',
  'table-formatting-flattened': 'Some table formatting was simplified.',
  'line-break-flattened': 'A line break was simplified.',
  'internal-link-flattened': 'An internal link was simplified.',
};

// UAT-07b: short NOUN labels for the common formatting-simplification codes, used to build ONE concise,
// plain-language summary ("indentation, paragraph styles, tables") instead of a wall of per-code sentences.
// Codes not listed here fall back to their full SAVE_DEGRADATION_COPY sentence (they are usually
// content-affecting, e.g. a dropped link target, and deserve their own line).
const SAVE_DEGRADATION_LABEL: Record<string, string> = {
  'indentation-dropped': 'indentation',
  'paragraph-style-flattened': 'paragraph styles',
  'section-break-flattened': 'section breaks',
  'tab-flattened': 'tab stops',
  'table-formatting-flattened': 'table formatting',
  'line-break-flattened': 'line breaks',
  'internal-link-flattened': 'internal links',
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
    parts.push(
      `Some formatting (${list}) was simplified to fit Compose's editor. Your text and content are intact.`
    );
  }
  if (otherSentences.length > 0) parts.push(otherSentences.join(' '));
  return parts.join(' ');
}

/** Stable content signature for a save-degradation set — the sessionStorage dismissal key suffix. */
function saveDegradationSignature(warnings: ReadonlyArray<{ code: string; count: number }>): string {
  return warnings.map(w => `${w.code}:${w.count}`).join('|');
}

const useStyles = makeStyles({
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
    reviewFindingsDegraded = null,
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
  const showSaveDegradation = saveWarnings.length > 0 && !saveWarningsDismissed;

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

      {showImportWarnings ? (
        <MessageBar intent="info" data-testid="compose-workspace-import-warning-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Some formatting was simplified</MessageBarTitle>
            This document uses advanced Word features that Compose shows in a simplified view. Your original file
            isn&apos;t changed until you save.
          </MessageBarBody>
          {/* FR-21/DEF-15: Fluent v9's MessageBar dismiss affordance — the trailing
              container action. Clears the banner AND persists the dismissal to
              sessionStorage so it stays closed for the rest of the session. */}
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-import-warning-dismiss"
                onClick={dismissImportWarnings}
              />
            }
          />
        </MessageBar>
      ) : null}

      {showSaveDegradation ? (
        // 026-F5 (task 012, r6): save-time degradation — the save SUCCEEDED but some content was
        // simplified while authoring it. Own dismissible banner; NOT gated by hideImportWarnings.
        <MessageBar intent="warning" data-testid="compose-workspace-save-degradation-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Some formatting was simplified when saving</MessageBarTitle>
            {`${summarizeSaveDegradation(saveWarnings)} The original file is unchanged until you save.`}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-save-degradation-dismiss"
                onClick={dismissSaveWarnings}
              />
            }
          />
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
