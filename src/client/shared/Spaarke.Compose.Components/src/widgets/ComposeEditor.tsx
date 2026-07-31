/**
 * ComposeEditor — TipTap host for the Spaarke Compose drafting workspace (R1).
 *
 * Project:     spaarkeai-compose-r1, task 045 (Phase 4 W4).
 * Authority:   `projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md`
 *              (LOCKED extension set + DOCX bridge libraries) +
 *              `projects/spaarkeai-compose-r1/notes/spikes/spike-3-spe-checkout-promotion.md`
 *              (LOCKED heartbeat: 3-min sliding, visibility-gated, 15-min stale) +
 *              `src/solutions/SpaarkeAi/src/types/compose-contracts.ts`
 *              (LOCKED PaneEventBus flow contracts; Tier 3 privacy on selection).
 *
 * Responsibilities:
 *  1. Mount TipTap with the LOCKED Spike #1 extension set (StarterKit +
 *     11 standard MIT extensions — zero TipTap Pro, zero custom).
 *  2. Mount the `projection` prop's server-rendered HTML on `docxBytes` prop change (task 013,
 *     F-2 "one reader" — the client-side mammoth import reader was deleted).
 *  3. Expose `serialize()` via ref — lazy-loads `docx` to export TipTap state
 *     back to DOCX bytes for SPE save.
 *  4. Emit PaneEventBus events per the locked compose-contracts:
 *     - Flow 1 (`compose_selection_changed` on `context`) — 250ms debounce
 *     - Flow 2 (`compose_selection_offer` on `conversation`) — when selection
 *       is non-collapsed and ≥10 chars
 *  5. Honor ADR-021 (Fluent v9 semantic tokens; no hex) and ADR-022 (React 19).
 *  6. Mount the inline AI toolbar (task 030, FR-14, `ComposeAiToolbar`) inside
 *     the BubbleMenu on non-collapsed selection — see the "AI TOOLBAR MOUNT"
 *     region below for the exact insertion point. Task 111 (UAT-R2, 2026-07-10)
 *     made this popup AI-actions ONLY (formatting controls removed from it —
 *     see the BubbleMenu JSX comment) and added a SECOND, independent trigger:
 *     right-click inside the editor opens the same toolbar at the click point
 *     (works on a collapsed caret too) — see the `contextMenuAnchor` state +
 *     `handleDOMEvents.contextmenu` hook.
 *
 *  HEARTBEAT HOISTED (R2/R3 refactor, 2026-06-29): The 3-min SPE check-out
 *  heartbeat that previously lived here has been moved to the workspace level
 *  (`src/solutions/SpaarkeAi/src/components/compose/hooks/useComposeHeartbeatGate.ts`)
 *  and gated on `checkoutStatus === 'acquired'` to fix FU-1 (cancelled tab
 *  continued heart-beating after force-close).
 *
 * What this component DOES NOT do (binding):
 *  - Speak to SPE directly (host pane supplies bytes via prop / receives via
 *    serialize callback; SPE plumbing lives in `ComposeDocumentService`).
 *  - Invent a Compose-specific dispatch route. AI-action dispatch (task 030,
 *    FR-14) is the inline `ComposeAiToolbar`'s bound `dispatchConsumer(bindingId,
 *    { slots })` call — the shipped Click-path session-dispatch seam
 *    (`POST /api/ai/chat/sessions/{sessionId}/dispatch`, ADR-039) — NOT a
 *    `POST /api/compose/action/{consumerType}` endpoint (deleted; see
 *    `notes/spikes/spike-0-dispatch-path.md`) and NOT a `compose_action_request`
 *    PaneEventBus event (never existed; do not add one). The editor still
 *    emits `compose_selection_offer` on the `conversation` channel (Flow 2) —
 *    that is pane CHOREOGRAPHY (Assistant/Context awareness), not the
 *    dispatch trigger.
 *  - Log selection text or document content (ADR-015 Tier 3). The PaneEventBus
 *    `logFlowEvent` reference impl already strips Tier 3 fields; this editor
 *    likewise NEVER `console.log`s `selectionText` or full document HTML.
 *
 * Open-in-Word handoff (FR-12) — `useDocumentActions` from
 * `@spaarke/document-operations` is intentionally NOT wired here. The host
 * (ComposeWorkspace / ComposeToolbar — sibling W4 tasks 042/043) owns the
 * toolbar surface that drives Open-in-Web + Open-in-Desktop. ComposeEditor
 * exposes `documentRef` via its imperative handle so the host can pass it to
 * `useDocumentActions` when needed.
 *
 * @see projects/spaarkeai-compose-r1/spec.md FR-02, FR-03, FR-04, FR-12, FR-17, FR-20
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md §3.1 (LOCKED extension list)
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-3-spe-checkout-promotion.md §4.1 (heartbeat code reference)
 * @see src/solutions/SpaarkeAi/src/types/compose-contracts.ts (Flow 1 + Flow 2 dispatch shapes)
 */

import * as React from 'react';
import { useEditor, EditorContent, BubbleMenu, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import Image from '@tiptap/extension-image';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import CharacterCount from '@tiptap/extension-character-count';
import TextAlign from '@tiptap/extension-text-align';

import {
  makeStyles,
  mergeClasses,
  tokens,
  Spinner,
  Text,
  Button,
  Tooltip,
  Badge,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Textarea,
} from '@fluentui/react-components';
import {
  ArrowDown20Regular,
  Checkmark16Regular,
  CommentMultiple20Regular,
  Dismiss16Regular,
  DocumentProhibited24Regular,
  ErrorCircle24Regular,
} from '@fluentui/react-icons';
import { ComposeFormatToolbar } from './ComposeFormatToolbar';
import {
  ComposeAiToolbar,
  type ComposeAiToolbarAction,
  type ComposeActionEnqueue,
  getComposeAiToolbarActions,
  getToolsForSurface,
  subscribeComposeAiToolbarActions,
} from './ComposeAiToolbar';
import { ComposeFindReplace } from './ComposeFindReplace';
import { ComposeCommentThread, type ComposeCommentPendingRange } from './ComposeCommentThread';
import {
  AgreementReviewSummaryPanel,
  NDA_REVIEW_DISCLAIMER_TEXT,
  type NdaReviewFindingSummary,
} from './AgreementReviewSummaryPanel';
import { deriveClauseLocationLabel } from './clauseLocation';
import {
  composeSessionCommentThreadsToAnchoredComments,
  findCommentAnchorRange,
  type ComposeCommentThreadModel,
} from './ComposeCommentThread.types';
import { ComposeCommentGutter, COMMENT_GUTTER_WIDTH_PX, MAX_COMMENT_GUTTER_WIDTH_PX } from './ComposeCommentGutter';
import { authenticatedFetch } from '@spaarke/auth';
import { InsertionMark } from './marks/InsertionMark';
import { DeletionMark } from './marks/DeletionMark';
import { TrackChangesExtension, trackChangesPluginKey } from './marks/TrackChangesExtension';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { QaHighlightExtension } from './marks/QaHighlightExtension';
import { SelectedCommentExtension, selectedCommentPluginKey } from './marks/SelectedCommentExtension';
import {
  usePendingRedline,
  resolveTargetSpans,
  type MaterializeStatus,
  type ConfidenceBand,
} from './hooks/usePendingRedline';
import { useDocQaHighlight, type QaHighlightStatus } from './hooks/useDocQaHighlight';
import { useComposeCommentThreads } from './hooks/useComposeCommentThreads';
import { ComposeFindReplaceExtension } from './hooks/useComposeFindReplace';
import { COMPOSE_R3_STYLES } from './hooks/useComposeDocumentStyles';
import { COMPOSE_INDENT } from './composeIndentExtension';
import { COMPOSE_NUMBER_ATOM } from './composeNumberAtomExtension';
// spaarkeai-compose-r1 task 093: deep-import from `@spaarke/ai-widgets/events`
// rather than the barrel `@spaarke/ai-widgets` to skip the side-effect widget
// registration (`register-workspace-widgets.ts` transitively pulls in
// `@spaarke/ai-outputs` subpaths that LegalWorkspace standalone Rollup cannot
// resolve). Same rationale as ComposeWorkspace.tsx above.
import { useDispatchPaneEvent, type DispatchPaneEvent } from '@spaarke/ai-widgets/events';

// Task 013 (spaarkeai-compose-fidelity-r4.5, F-2 "one reader"): `docxToTipTapHtml` (the client
// mammoth reader) and `stampParaIds` (only ever called from the now-deleted mammoth branch) are no
// longer imported here — every mount now hydrates via the `projection` branch below, whose HTML
// already carries server paraIds (`data-paraid`), so no client-side stamping pass is needed.
import { captureParaIdSnapshot, buildContentModel, buildBaselineParaIdMap } from '../utils/docxBridge';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
// R4 FR-03/FR-06 (task 020/022/032) — the step→operation interceptor + its rebased
// operation log. A headless, read-only ProseMirror plugin captures transaction steps
// as task-003 operations anchored `(paraId, runIndex, run-local-offset)`; the
// RebasedOperationLog keeps them ordered + rebased across the dirty session. Task 032
// (the write-path cutover) wires the log PRODUCTION here (supplying the classifier
// callbacks) and exposes `serializeOperationLog()` on the handle so `triggerSave` sends
// the op-log the server applies via ComposeShadowPatchEngine. Capture only — no fetch,
// no doc mutation.
import {
  RebasedOperationLog,
  createRebasedOperationLogPlugin,
  type ComposeOperationLogSnapshot,
} from './stepOperationInterceptor';
import { Extension } from '@tiptap/core';
import { COMPOSE_R4_OPAQUE_ATOMS } from './opaqueAtomNode';
import { applyImportedRevisions, renderUnresolvedRevisionPlaceholders } from './importedRevisions';
import { applyImportedCommentAnchors, groupImportedComments } from './importedComments';
import type {
  ParaIdMapEntry,
  ComposeServerProjection,
  ComposeBaselineParaId,
  ComposeContentModel,
  ImportedRevision,
  ImportedComment,
  // Task 040 (comment-export wiring fix)
  ComposeAnchoredComment,
  // G7 (task 022): the Save split-button choice, threaded through to the toolbar.
  ComposeSaveMode,
} from '../types/compose-contracts';
// Redline → Word save fidelity (UAT-R7 #2/#3/#4): the redline→annotation bridge + its wire type.
import { redlineMarksToDocxAnnotations, type DocxAnnotationInput } from './useComposeWordShuttle';

// ---------------------------------------------------------------------------
// Wave 6 (UAT-R4, DEF-G) — non-docx reference-only guard
// ---------------------------------------------------------------------------
//
// Editable Compose content is DOCX-ONLY by design (the server projection parses
// OOXML/zip; there is no PDF→editable conversion — Open-in-Word is the escape
// hatch). A DOCX is a ZIP archive, so its first four bytes are the ZIP
// local-file-header magic `PK\x03\x04` (0x50 0x4B 0x03 0x04). Any other leading
// signature — a `%PDF-` header, plain text, etc. — cannot be a DOCX; the server
// projection would fail-closed on it and (pre-Wave-6) leave a silent, confusing
// empty `<p></p>` editor. Since Wave 3 made "Open in Compose" open the active
// SOURCE document, a chat-uploaded PDF can now reach this editor. We detect
// non-docx from the byte signature BEFORE the mount and render an explicit
// reference-only state instead (a `status:'failed'`/`canEdit:false` projection —
// see below — is a defensive second layer that renders the same state).
//
// `PK\x05\x06` (empty archive) / `PK\x07\x08` (spanned) are deliberately NOT
// treated as docx — a real .docx always begins with a local file header
// (`PK\x03\x04`), never an empty/spanned end-of-central-directory marker.
const ZIP_LOCAL_FILE_HEADER = [0x50, 0x4b, 0x03, 0x04] as const;

/** True when `bytes` begins with the ZIP local-file-header magic — the necessary signature of a DOCX. */
function isDocxBytes(bytes: ArrayBuffer): boolean {
  if (bytes.byteLength < ZIP_LOCAL_FILE_HEADER.length) return false;
  const sig = new Uint8Array(bytes, 0, ZIP_LOCAL_FILE_HEADER.length);
  return ZIP_LOCAL_FILE_HEADER.every((b, i) => sig[i] === b);
}

/**
 * Known non-DOCX file extensions. An OOXML ZIP signature is necessary but NOT
 * sufficient for a DOCX — .xlsx / .pptx / .zip are also `PK\x03\x04` ZIPs, so
 * the fileName extension is a second, complementary signal. A `.pdf` / `.txt`
 * is caught by the signature alone; a PK-zip sibling format needs the extension.
 */
const NON_DOCX_EXTENSION = /\.(pdf|txt|rtf|doc|xlsx?|pptx?|csv|zip|md|html?|json|xml|png|jpe?g|gif|tiff?)$/i;

/**
 * Wave 6 (DEF-G) — decide whether a mounted buffer is an EDITABLE DOCX (mount via
 * the server projection) or a reference-only file. Combines two signals so the
 * common non-docx cases are caught BEFORE the mount (never relying on a
 * projection failure, which — swapped mid-session — would fight ProseMirror's DOM):
 *  1. An explicitly non-docx fileName extension is reference-only even if the
 *     bytes look like a ZIP (xlsx/pptx are ZIPs too).
 *  2. Otherwise, a real .docx must carry the OOXML ZIP local-file-header magic.
 */
function isEditableDocx(bytes: ArrayBuffer, fileName: string | undefined): boolean {
  if (fileName && NON_DOCX_EXTENSION.test(fileName.trim())) return false;
  return isDocxBytes(bytes);
}

// ---------------------------------------------------------------------------
// LOCKED Spike #1 extension list — DO NOT add to or remove from this list
// without explicit reviewer sign-off + spike-artifact update.
// ---------------------------------------------------------------------------

/**
 * The LOCKED TipTap extension inventory per Spike #1 §3.1 (LOCKED ARTIFACT
 * 2026-06-29). Adding extensions outside this list is forbidden per
 * spec.md FR-03 MUST NOT and `projects/spaarkeai-compose-r1/CLAUDE.md`
 * Key Technical Constraints.
 *
 * Versions resolve via package.json `^2.10.3` semver. The build orchestrator
 * is responsible for `npm install --legacy-peer-deps` resolving compatible
 * versions across the 14 TipTap packages.
 *
 * All packages MIT-licensed; docx (server-side render input) is MIT. NO TipTap
 * Pro (Track Changes, Comments, Mathematics, Drawing). NO custom extensions.
 */
const LOCKED_EXTENSIONS = [
  // StarterKit bundle: Document, Paragraph, Text, Bold, Italic, Strike, Code,
  // CodeBlock, Heading (1-6), BulletList, OrderedList, ListItem, Blockquote,
  // HardBreak, HorizontalRule, History, Dropcursor, Gapcursor.
  StarterKit.configure({
    heading: { levels: [1, 2, 3, 4, 5, 6] as const },
  }),
  Underline,
  Link.configure({ openOnClick: false, autolink: true }),
  Image.configure({ inline: false, allowBase64: true }),
  Table.configure({ resizable: true }),
  TableRow,
  TableHeader,
  TableCell,
  TaskList,
  TaskItem.configure({ nested: true }),
  CharacterCount,
  TextAlign.configure({ types: ['heading', 'paragraph'] }),
];

/**
 * R2 custom marks (task 031, FR-15) — insertion/deletion/commentAnchor rendering primitives for
 * pending AI track-changes. ADDITIVE to LOCKED_EXTENSIONS per design §5: registered alongside the
 * locked Spike #1 list WITHOUT modifying it. These marks carry provenance and render only — they
 * do NOT dispatch or materialize from the ledger (that is FR-16 / task 033, core-gated). Styling
 * lives in `useStyles().editorSurface` via the `compose-mark-*` classes (semantic tokens; ADR-021).
 */
const COMPOSE_R2_MARKS = [InsertionMark, DeletionMark, CommentAnchorMark];

/**
 * FR-35 Doc Q&A ephemeral highlight (task 072, stretch) — a single ProseMirror
 * VIEW-DECORATION plugin (NOT a Mark; see the extension's file header). Kept
 * as its own additive array, separate from COMPOSE_R2_MARKS, because it is a
 * structurally different kind of extension (plugin-only, no schema mark).
 */
const COMPOSE_R2_QA_HIGHLIGHT = [QaHighlightExtension];

/**
 * ai-advanced-capabilities-nda-r1 UAT round-4 #8 — the "selected advisory comment" highlight, another
 * single ProseMirror VIEW-DECORATION plugin (NOT a Mark; same DOCX-safety rationale as
 * {@link COMPOSE_R2_QA_HIGHLIGHT}). Paints the currently-selected advisory thread's clause the SELECTED
 * colour (yellow); the base anchor stays light blue via the `compose-mark-comment-anchor` mark class.
 */
const COMPOSE_NDA_SELECTED_COMMENT = [SelectedCommentExtension];

/**
 * FR-17 in-editor find/replace (task 040) — another single ProseMirror VIEW-DECORATION plugin (NOT
 * a Mark, same rationale as {@link COMPOSE_R2_QA_HIGHLIGHT}: find highlighting never appears in
 * `editor.getHTML()`/`getJSON()` and never serializes to DOCX). Kept as its own additive array for
 * the same structural reason — a plugin-only extension, no schema mark.
 */
const COMPOSE_R3_FIND_REPLACE = [ComposeFindReplaceExtension];

// R3 FR-22 styles pane (task 043) — the `pStyle` hidden node attribute extension
// (`COMPOSE_R3_STYLES`, imported above from `./hooks/useComposeDocumentStyles`) is factored into the
// hook file alongside the hook that reads/applies it (same file-organization precedent as
// `COMPOSE_R3_FIND_REPLACE` above: schema + hook co-located). Registered additively below, same as
// every other array here — never mutates the LOCKED Spike #1 list.

// FR-07 indentation-preservation extension (task 021, fidelity-r4.5) — `COMPOSE_INDENT`, factored into
// `./composeIndentExtension` as a pure headless schema piece (see that module's header). Mirrors the
// LOCKED TextAlign registration for the SAME node types (`paragraph`/`heading`) so the server
// projection's `margin-left`/`text-indent` (AppendIndentDeclarations) round-trips through
// setContent/getHTML instead of being silently stripped by the base Paragraph/Heading node schema.

// FR-13/FR-14 explicit non-editable number-atom (task 032, fidelity-r4.5) — `COMPOSE_NUMBER_ATOM`,
// factored into `./composeNumberAtomExtension` (see that module's header). Preserves the server
// projection's `data-computed-number`/`data-numbering-level` (AppendNumberingAttrs, task 032) as hidden
// node attributes AND renders the label as a non-editable ProseMirror VIEW DECORATION prefix — never a
// doc node, so it cannot participate in the tracked-edit stream (FR-14 read-time-only boundary). The
// browser's native `<ol>` marker is unconditionally suppressed in `useStyles().editorSurface` below —
// this decoration is the SOLE source of a legal-numbered paragraph's displayed number (F-3 invariant).

// R3 FR-09/FR-10 paraId identity extension (task 011) — factored into
// `./paraIdExtension` as a pure headless schema piece (see that module's header).
// Registered additively below alongside the LOCKED Spike #1 list (never mutated).

// R4 FR-02 opaque-atom node types (task 021) — factored into `./opaqueAtomNode` as pure headless
// schema pieces (see that module's header). Registered additively below (never mutates the locked
// list). Renders the task-012 server projection's non-editable SDT/field/object placeholders.

// ---------------------------------------------------------------------------
// Constants — selection debounce
// ---------------------------------------------------------------------------
// (Heartbeat constants removed in R2/R3 refactor — see hoist note below.
//  The 3-minute interval now lives in
//  src/solutions/SpaarkeAi/src/components/compose/hooks/useComposeHeartbeatGate.ts.)

/** Selection-change debounce per `compose-contracts.ts` Flow 1 comment (250ms). */
const SELECTION_DEBOUNCE_MS = 250;

/** Minimum selection length to fire Flow 2 (`compose_selection_offer`). */
const FLOW2_MIN_CHARS = 10;

/**
 * Selection-text cap per compose-contracts.ts Flow 1 ComposeSelection
 * documentation: "CAP: ≤2000 characters." Dispatchers truncate at source.
 */
const SELECTION_TEXT_CAP = 2000;

/**
 * task 038 (spaarkeai-compose-r4 zero-error guardrails) — the user-visible, non-blocking notice shown
 * when a step the closed op set cannot yet carry is DEFERRED (a structural/unrepresentable step or a
 * refused opaque-atom edit). It exists to catch formatted/linked PASTE, which bypasses the toolbar
 * gate (change 1/2) and would otherwise be silently dropped. The SAVE STILL SUCCEEDS for the
 * representable edits — this banner only informs, it never blocks. The deferred features are
 * implemented in projects/spaarkeai-compose-r5 (G3 heading/list/alignment, G4 tables, G5 hyperlinks).
 */
const DEFERRED_FORMAT_NOTICE =
  "Some formatting (links, headings, lists, alignment) isn't saved yet and was not applied. Your text edits are still saved.";

/**
 * Contextual AI Tool Library (phase 1): the SET of tools a Review Note's ⋮ menu shows is now
 * driven by the library — `getToolsForSurface('review-note', …)` — NOT by this map (that replaces the
 * round-8 `NOTE_TOOL_LABELS` allow-list, which was doing double duty as both selector and labeller).
 * This map now carries ONLY per-surface LABEL OVERRIDES: a tool may read differently in the note menu
 * than on the BubbleMenu (e.g. "Draft alternative" → "Draft compliant alternative" for the advisory
 * context, round-8 #4). A tool with no entry here falls back to its own `label`.
 */
const NOTE_TOOL_SURFACE_LABELS: Record<string, string> = {
  'compose-draft-alternative': 'Draft compliant alternative',
  'compose-make-concise': 'Make more concise',
  'compose-rewrite-instruction': 'Describe a change…',
};

/**
 * Fallback work type for the Review-Note surface when the host does not pass one. All shared
 * edit primitives (Draft alternative / Make concise / Describe a change) are `workTypes: ['*']`,
 * so they show regardless. A WORK-TYPE-SCOPED note tool (e.g. an agreement-analysis-only tool)
 * shows only when the host threads `activeWorkType='agreement-analysis'` (via the ComposeEditor
 * prop). Knowledge sub-domain (NDA vs MSA) is NOT this — it only affects grounding.
 */
const NOTE_TOOL_FALLBACK_WORKTYPE = '*';

// ---------------------------------------------------------------------------
// Props + imperative handle
// ---------------------------------------------------------------------------

/**
 * Document pointer matching `ComposeDocumentRef` from compose-contracts.ts.
 * Kept locally typed (not re-imported from `@spaarke/types/compose-contracts`)
 * to avoid a cross-package coupling on solution-local types — the host pane
 * supplies the value, and the editor narrows on `speDriveItemId` only.
 */
export interface ComposeEditorDocumentRef {
  /** SPE drive-item id — canonical identity for the document. */
  speDriveItemId: string;
  /** Dataverse `sprk_documentid` after first-Save promotion. */
  sprkDocumentId?: string;
  /** Human-readable file name (UI label). */
  fileName?: string;
  /** SPE container id (multi-tenant scoping). */
  containerId?: string;
}

/**
 * FR-04 Compose-owned structured-edit payload (spaarkeai-compose-r2 task 016) —
 * the client mirror of `Services/Compose/ComposeDraftDisposition.cs`
 * `ComposeDraftPayload`. Rides inside the opaque `payload` of a `compose`-
 * disposition ledger entry; the core stores it opaquely, Compose renders it.
 * snake_case wire vocabulary. Owned here (the editor performs the insertion) and
 * re-imported by ComposeWorkspace so both sides of the materialize seam share
 * ONE type (dependency direction is workspace → editor, already established).
 *
 * Privacy: `new_text` / `target_text` / `rationale` are Tier 3 (LLM/user content)
 * and MUST NOT be logged; `sources` are identifiers only.
 */
export interface ComposeDraftPayload {
  /** Text the draft targets for replacement; absent/empty for an insertion-style draft. */
  target_text?: string;
  /** The drafted content to materialize into the editor (load-bearing single-edit field). */
  new_text?: string;
  /** How the client resolves `target_text` (Compose vocabulary, e.g. `strict` / `insert`). */
  match_mode?: string;
  /** Optional model-supplied rationale (provenance/explanation). */
  rationale?: string;
  /** Citations / source ids the draft was grounded on (ids only). */
  sources?: string[];
  /**
   * DEF-11 whole-document revision: a CHANGE LIST of targeted edits across the document
   * (`compose-revise-document` output). When present (non-empty), the workspace materializes a
   * MULTI-change redline via {@link ComposeEditorHandle.materializeComposeEdits} instead of the
   * single-edit path. Mutually meaningful with {@link comments}: a payload may carry either or both.
   */
  edits?: ComposeDraftEdit[];
  /**
   * DEF-11 whole-document revision: anchored review FLAGS (no rewrite) — `flag-risks` intent. Each
   * becomes an anchored `comment` annotation (DEF-13 path) shown in the doc + written as a Word
   * `w:comment` on Save. Flags are NOT accept/reject-able (they carry no edit).
   */
  comments?: ComposeDraftComment[];
}

/** DEF-11: one targeted edit in a whole-document revision change list. Shape = the single-edit redline payload. */
export interface ComposeDraftEdit {
  /** Exact substring to replace — VERBATIM from the document so the editor can locate it. */
  target_text: string;
  /** The proposed replacement clause language, inserted as a pending track-change. */
  new_text: string;
  /** How to locate `target_text`: `strict` (default) | `first` | `all`. */
  match_mode?: string;
  /** Optional per-change rationale. */
  rationale?: string;
}

/** DEF-11: one anchored review flag in a whole-document revision (no rewrite). */
export interface ComposeDraftComment {
  /** Exact substring the flag is anchored to. */
  target_text: string;
  /** The reviewer flag / comment body. */
  comment: string;
}

/**
 * Provenance stamp accompanying a materialized draft — all Tier 1 identifiers.
 * `ledgerRef` is the addressable `{bindingId}@t{n}` key of the stored output.
 */
export interface ComposeDraftProvenance {
  ledgerRef: string;
  bindingId: string;
  turn: number;
}

export interface ComposeEditorProps {
  /**
   * DOCX bytes to render (typically from SPE drive-item content via
   * `ComposeDocumentService.LoadDocumentAsync`). `null` means "no document
   * loaded yet"; the editor renders an empty paragraph. Changing this prop
   * triggers a mount via the `projection` prop's server-rendered HTML (task
   * 013, F-2 "one reader"); a present-but-`projection`-less mount renders an
   * explicit error/unavailable state — there is no client-side fallback reader.
   */
  docxBytes: ArrayBuffer | null;

  /**
   * DEF-08: seed HTML for an AI-drafted full document. When present (and `docxBytes` is null),
   * the editor sets its content DIRECTLY from this HTML instead of decoding DOCX — the drafting
   * body already IS the editor content (no docx round-trip). Rendered once on mount as a
   * TRANSIENT working draft (reported dirty so create-on-save first Save is reachable), then the
   * editor owns subsequent edits. Mutually exclusive with `docxBytes` (a draft seed sets
   * `docxBytes` null). R3 task 027: Save sends the paraId-keyed `ComposeContentModel`
   * (`buildContentModel`) and the SERVER renders the `.docx` — the client authors no bytes.
   */
  initialHtml?: string | null;

  /**
   * FR-08/FR-09 (spaarkeai-compose-r3 task 011) — the server pre-parse `w14:paraId`
   * map from the Load response (task 010), in document order. Historically stamped
   * onto the editor's paragraph nodes as hidden attributes via {@link stampParaIds}
   * immediately after the (now-deleted) client-side mammoth import. Task 013 (F-2
   * "one reader"): the server `projection` prop's HTML now carries `data-paraid`
   * directly, so the mount effect no longer calls `stampParaIds` with this map —
   * it is accepted for prop-shape/host-contract compatibility but currently unused
   * by the mount path. Identifiers only (Tier 1 safe).
   */
  paraIdMap?: readonly ParaIdMapEntry[];

  /**
   * FR-24 (spaarkeai-compose-r3 task 050, import round-trip) — the existing Word revisions
   * (`w:ins`/`w:del`, any authorship) recovered server-side on Load and projected onto the Load
   * response (`ImportedRevision[]`), in document order. On a `docxBytes` mount these are rendered as
   * first-class, author/date-attributed, accept/reject-able insertion/deletion marks anchored by
   * `paraId` (see {@link applyImportedRevisions}) — instead of the flattened prose the server
   * projection would otherwise show. Absent/empty for an AI-drafted seed (`initialHtml`) or a document with no
   * existing revisions. Set atomically with `docxBytes` + `paraIdMap` by the host (same mount contract).
   * Privacy: `text`/`anchorText` are Tier 3 (document content) — carried in-memory only, never logged.
   */
  importedRevisions?: readonly ImportedRevision[];

  /**
   * FR-25 (spaarkeai-compose-r3 task 051, import round-trip) — the existing Word comments
   * (`w:comment`, any authorship) recovered server-side on Load and projected onto the Load response
   * (`ImportedComment[]`), in document order. Grouped by shared `anchorText` into FR-23 comment
   * threads (first comment on a span = thread root, the rest = flat replies — see
   * {@link groupImportedComments}) and rendered via the `ComposeCommentThread` panel (task 044),
   * anchored by `paraId` (see {@link applyImportedCommentAnchors}) — instead of the comments the
   * server projection would otherwise silently drop. Absent/empty for an AI-drafted seed (`initialHtml`)
   * or a document with no existing comments. Set atomically with `docxBytes` + `paraIdMap` by the host
   * (same mount contract as `importedRevisions`).
   * Privacy: `commentText`/`anchorText` are Tier 3 (document content) — carried in-memory only, never logged.
   */
  importedComments?: readonly ImportedComment[];

  /**
   * The server-side DOCX→editor projection (`ComposeDocxProjectionBuilder`, `Sprk.Bff.Api`) — the
   * SOLE docx→editor reader as of task 013 (F-2 "one reader"; the client-side mammoth convert was
   * deleted). Every entry path (stored-document Load, assistant-upload, Browse, open-in-Compose)
   * supplies this by hydrating it server-side before mounting `docxBytes`. When present the editor
   * mounts `projection.html` DIRECTLY (the paraId extension parses `data-paraid`). Fail-closed:
   * `canEdit === false` (or `status === 'failed'`) ⇒ the editor renders a read-only / "Open in Word"
   * state, NEVER a blank editable doc over a non-empty baseline. Null/absent (the projection
   * round-trip failed, was unreachable, or was never attempted) with a non-null `docxBytes` ⇒ the
   * editor renders an explicit error/unavailable state (task 013) — there is no client-side fallback
   * reader to fall back to.
   * Privacy: `html` is Tier 3 (document content) — carried in-memory only, never logged.
   */
  projection?: ComposeServerProjection | null;

  /**
   * Document pointer used by PaneEventBus events + heartbeat endpoint URL.
   * Required when `docxBytes` is non-null (and heartbeat must run); optional
   * when the editor is mounted with no document.
   */
  documentRef?: ComposeEditorDocumentRef;

  /**
   * BFF base URL (host only, e.g. `https://host.azurewebsites.net`). Supplied
   * by the host via runtime-config resolution. Required for the heartbeat
   * call. When absent, heartbeat is suppressed (defensive — editor renders
   * fine without it; W7-052 wires the BFF side regardless).
   */
  bffBaseUrl?: string;

  /**
   * ChatSession id correlating this editor's events to a ChatSession row.
   * Threaded through Flow 1 + Flow 2 payloads per compose-contracts.ts.
   * Required when documentRef is supplied; defaults to an empty string
   * (stub receivers tolerate; smoke-test asserts).
   */
  sessionId?: string;

  /**
   * Called whenever the editor's `onUpdate` fires (after a small debounce).
   * The host pane can use this for dirty-state tracking, save-on-blur, etc.
   * NOT a Tier 3 sink — this callback receives only a `dirty` boolean, NOT
   * the document content.
   */
  onDirtyChange?: (dirty: boolean) => void;

  /**
   * Called with the server projection's fidelity-warning array after each DOCX mount (task 013:
   * formerly mammoth's per-conversion warnings; now `projection.warnings` + unresolved-revision
   * notices). The host can surface a "this document was simplified on load" banner (deferred to R2
   * per Spike #1 §5.4; R1 only logs to console).
   *
   * Tier 1 safe (warnings are configuration metadata, not document content).
   */
  onImportWarnings?: (messages: Array<{ type: string; message: string }>) => void;

  /**
   * FR-18 host serialization seam (task 032). Forwarded verbatim to the inline
   * `ComposeAiToolbar`; when present, toolbar action dispatches route through the
   * host's serial queue. Optional — see `ComposeActionEnqueue`.
   */
  enqueueComposeAction?: ComposeActionEnqueue;

  // ---------------------------------------------------------------------------
  // FIX #5 (UAT) — Word + Save command handlers, forwarded to the consolidated
  // ComposeFormatToolbar's "Word" dropdown + right-aligned Save button. The HOST
  // (ComposeWorkspace) still OWNS the binding (Open-in-Word via `useDocumentActions`,
  // Save via `triggerSave`, Push via the annotations shuttle) — the editor is a
  // pure forwarder, so the shared lib stays decoupled from `@spaarke/document-operations`.
  // All optional: the Word dropdown / Save button render only when their handlers
  // are threaded (standalone/library mounts omit them).
  // ---------------------------------------------------------------------------
  /** Open the current document in Word for the Web. */
  onOpenInWord?: () => void;
  /** Open the current document in the Word desktop app. */
  onOpenInWordDesktop?: () => void;
  /** Disables the two Open-in-Word items (no persisted document, or an action in flight). */
  wordActionsDisabled?: boolean;
  /** Save handler (create-on-save first Save, or update). Renders the Save split-button when set.
   *  G7 (task 022): receives the split-button choice — `'version'` (default, replace/dedup) or `'new'`
   *  (fork a new document). A bare call (Ctrl+S / cross-pane bridge) defaults to `'version'`. */
  onSave?: (mode?: ComposeSaveMode) => void;
  /** True when Save should be enabled (unsaved edit OR unpersisted transient draft). */
  canSave?: boolean;
  /** True while a save is in flight. */
  isSaving?: boolean;
  /** G10 (FR-09, task 040): manual "Refresh Profile" handler. Renders the toolbar button when set
   *  (the host wires it only for a promoted doc — one that has a sprk_document record to re-profile). */
  onRefreshProfile?: () => void;

  /** UAT #5 (task 053): "Reload from source" handler. Renders the toolbar button when set (the host wires
   *  it only for a doc with an SPE source). Pulls the latest SPE bytes on demand — e.g. after an external
   *  Word-web edit. The host honors the dirty-guard before discarding unsaved edits. */
  onReloadFromSource?: () => void;

  /** "Open Document" handler. Renders the toolbar button when set (the host wires it only for a doc with a
   *  preview source — a promoted sprk_document). Opens the source Dataverse Document in the shared preview
   *  modal (RichFilePreviewDialog + BFF preview-url). Pure forwarder — the host owns the modal. */
  onOpenDocument?: () => void;

  /** UAT #9 (task 054): true while a manual profile re-run is in flight — the toolbar shows a spinner on the
   *  Refresh-Profile button so the click gives visible feedback (the re-run is otherwise a silent 202). */
  isRefreshingProfile?: boolean;

  /**
   * FR-23 (task 044) — display name attributed to comment threads/replies the CURRENT user creates
   * via the comment-thread panel. Resolved by the host (no network call happens here — ADR-028);
   * defaults to `'You'` when omitted (standalone/library mounts).
   */
  commentAuthor?: string;

  /**
   * task 052 (FR-15, Word-comment export mirror; ADR-012 lib-level configurability) — display name
   * attributed to advisory (NDA/agreement-review finding) comment threads placed via
   * {@link ComposeEditorHandle.placeAdvisoryComments}, separate from the session Comments panel's
   * own `commentAuthor` above (the two use dedicated `useComposeCommentThreads` instances — see
   * that field's own placement JSDoc). Defaults to `'AI Advisory Review'` — the PRE-EXISTING
   * hardcoded literal this prop replaces — so every current mount keeps its exact current behavior
   * until a host opts into a different name.
   */
  advisoryCommentAuthor?: string;

  /**
   * ai-advanced-capabilities-nda-r1 (UAT round-2 items #1/#2) — the review-summary panel's
   * visibility state, threaded from the host (`ComposeWorkspace`) so the editor's own "Review"
   * toolbar dropdown can toggle it alongside the right-gutter "Review Notes". The host owns the
   * panel (it renders `AgreementReviewSummaryPanel`); the editor owns the gutter. Omitted for a mount with
   * no NDA advisory review — the "Review" control then never appears.
   */
  reviewSummary?: {
    /** Whether the review-summary panel is currently shown. */
    open: boolean;
    /** True once a review has produced findings (gates whether the "Review" control appears at all). */
    hasFindings: boolean;
    /** Toggle the review-summary panel's visibility. */
    onToggle: () => void;
    /**
     * UAT round-5 #1 — the flagged-section findings, so the editor can render the Review Summary panel
     * INSIDE its own top region (below the toolbar), replacing the former host-rendered docked panel.
     * The editor enriches each finding with a doc-derived `locationLabel` (section heading + ordinal —
     * {@link ../widgets/clauseLocation.ts}) before rendering.
     */
    findings: readonly NdaReviewFindingSummary[];
    /** Count of advisory comments that couldn't be anchored (passed through to the panel's notice). */
    placementFailureCount?: number;
  };
  /**
   * Contextual AI Tool Library — the ACTIVE work type (the product surface the user chose):
   * `'agreement-analysis'` (NDA/MSA/employment review), `'legal-research'`, … The host passes
   * this so the BubbleMenu + Review-Note ⋮ menu surface work-type-scoped tools in addition to the
   * shared `['*']` primitives. Defaults to `'*'` (shared primitives only). Knowledge sub-domain
   * (NDA vs MSA) is NOT this — it only affects grounding. See the tool-library design doc.
   */
  activeWorkType?: string;
}

/**
 * One flagged clause to materialize as an advisory comment (ai-advanced-capabilities-nda-r1 task
 * 031). A deliberately PLAIN structural type — decoupled from the PaneEventBus event shape
 * (`ComposeAdvisoryCommentItem` in `@spaarke/ai-widgets`), mirroring {@link
 * ComposeEditorHandle.highlightCitedSpan}'s primitive-args convention so ComposeEditor's public
 * surface never depends on the bus's event union.
 */
export interface AdvisoryCommentInput {
  /** Verbatim quoted NDA clause excerpt — the `resolveTargetSpans('strict')` anchor target. Tier-3. */
  targetText: string;
  /** The AI's advisory explanation for this flag — becomes the comment thread's text. Tier-3. */
  explanation: string;
  /**
   * task 032 (right-gutter comment layout) — optional metadata passed through to the created
   * thread's {@link ComposeCommentThreadModel} (via `useComposeCommentThreads.createThread`'s
   * metadata parameter) so the right-rail gutter card can render a risk badge + citation. Structural
   * mirror of `ComposeAdvisoryCommentItem`'s own optional fields (`@spaarke/ai-widgets`).
   */
  /** Section/clause reference from the NDA-REVIEW output (e.g. "3.2"). */
  sectionRef?: string;
  /** Coarse qualitative risk signal (NEVER a numeric score, per ADR-039). */
  riskLevel?: string;
  /** Optional standard/playbook reference the flag cites. */
  standardRef?: string;
  /**
   * task 052 (FR-15, Word-comment export mirror) — the review Action's discrete grounded-fact /
   * reasoned-judgment fields (`ai-advanced-capabilities-agreements-r1` task 002 schema split:
   * `explanation` → `flaggedClause` + `assessment`). When a caller supplies these, the created
   * thread's export mirrors the gutter's structured "Flagged clause: … / Assessment says: …" text
   * with NO string-parsing (see `./advisoryNoteFormatting.getAdvisoryNoteSegments`). Optional and
   * additive: `explanation` remains required above (unchanged) as the thread's `text`/legacy-degrade
   * source; no current caller populates these two yet (the client bridge that projects the review
   * Action's `flaggedSections[]` still reads the pre-002 `explanation` field — see the task 052
   * execution notes) — they are here so that wiring, once landed, needs no further ComposeEditor
   * change.
   */
  flaggedClause?: string;
  /** Reasoned-judgment prose (task 002 discrete field) — see `flaggedClause` above. */
  assessment?: string;
  /** Full resolved standard-clause text, when the caller has it (task 052 "full clause text when
   *  available" criterion) — see `ComposeCommentThreadModel.standardText`. */
  standardText?: string;
}

/**
 * A flagged range that could not be resolved to a unique span (FR-19 "do not guess" — reported,
 * never silently dropped). `kind` mirrors {@link ResolveResult}'s failure kinds.
 */
export interface AdvisoryCommentFailure {
  /** The unresolved target text (truncated by the caller for display; carried verbatim here). */
  targetText: string;
  /** `not_found` — zero matches; `ambiguous` — more than one match (do not guess which). */
  kind: 'not_found' | 'ambiguous';
}

/** Outcome of {@link ComposeEditorHandle.placeAdvisoryComments}. */
export interface AdvisoryCommentPlacementResult {
  /** Count of comments successfully anchored + created. */
  placed: number;
  /** Ranges that could not be resolved to a unique span — never silently dropped. */
  failed: AdvisoryCommentFailure[];
}

/**
 * Imperative handle exposed by ComposeEditor — host calls these via ref.
 */
export interface ComposeEditorHandle {
  /**
   * R4 FR-06 (task 032, the write-path cutover): the ordered, rebased task-003 OPERATION LOG snapshot
   * ({@link ComposeOperationLogSnapshot}) captured this dirty session — the ID-anchored
   * (`paraId, runIndex, run-local-offset`) op stream the host sends on a dirty save of a LOADED doc, which
   * the server applies via `ComposeShadowPatchEngine`. This is the ONLY dirty-save capture path — it
   * replaced the retired paragraph-diff export `collectEditedParagraphs` (R3 FR-01, removed task 023).
   *
   * task 038 (zero-error guardrails): this is now NON-DESTRUCTIVE — it reads the log WITHOUT resetting it
   * or clearing the dirty flag, so a rejected (422) save leaves the document dirty with its op-log intact
   * and a retry re-sends the same edits (no batch loss). The host clears the persisted batch ONLY after a
   * confirmed 200 by calling {@link commitSaved}. Ops flagged `deletedContentFlag` (their anchor landed in
   * later-deleted content) are surfaced in the snapshot; the host excludes them from what it applies
   * (never-silently-drop).
   */
  serializeOperationLog(): ComposeOperationLogSnapshot;

  /**
   * task 038 (zero-error guardrails): commit the batch returned by the most recent
   * {@link serializeOperationLog} AFTER the save POST confirmed (HTTP 200). Drops exactly that batch from
   * the op-log while PRESERVING any edits made during the in-flight save, then recomputes the dirty flag
   * from whatever remains. The host MUST call this only on a 200 for a save that sent an op-log; a failed
   * save never calls it, so the op-log + dirty flag survive for a retry. No-op if the editor is unmounted.
   */
  commitSaved(): void;

  /**
   * C2 fix (UAT 2026-07-20): the ordered LOAD-TIME paraId map ({@link ComposeBaselineParaId}[]) the host
   * sends on save so the server can stamp minted ids physically onto the retained-original baseline's
   * id-less paragraphs before the synthesizer resolves. Read-only (no dirty-flag side effect) — sourced
   * from the load-time paraId snapshot, so its `text` is the baseline (reject-state) text the server
   * verifies against. Empty for a born-in-editor doc (no snapshot / the server renders its ids).
   */
  getBaselineParaIdMap(): ComposeBaselineParaId[];

  /**
   * R3 FR-01a (task 027): the full paraId-keyed {@link ComposeContentModel} for a BORN-IN-EDITOR save
   * (AI-drafted / blank / browse-local). The host sends it to create-on-save; the server RENDERS the
   * high-fidelity `.docx` (styles + style-linked multi-level numbering + tables). Resets the dirty flag.
   */
  buildContentModel(): ComposeContentModel;

  /**
   * R3 FR-04 (task 027): the current PENDING AI redlines mapped to {@link DocxAnnotationInput}[] (native
   * `w:ins`/`w:del`). The host appends these to the save `annotations` list; the server composes them
   * onto the authored baseline via `DocxAnnotationWriter` (task 023). Does NOT reset the dirty flag
   * (redlines are separate from settled-text edits). Empty when there are no pending redlines.
   */
  getRedlineAnnotations(): DocxAnnotationInput[];

  /** True when the editor currently holds one or more PENDING redlines (drives the Save path). */
  hasPendingRedlines(): boolean;

  /**
   * Task 040 (comment-export wiring fix): BOTH the FR-23 session Comments panel's own thread
   * instance AND the NDA-REVIEW advisory thread instance ({@link getAdvisoryCommentThreads}, task
   * 031), mapped to {@link ComposeAnchoredComment}[] via
   * `composeSessionCommentThreadsToAnchoredComments` — each thread's LIVE `commentAnchor` mark span
   * resolved to a durable `(paraId, run-local range)` (D2), no text-search (I-7). The host sends the
   * result in the Save request's `comments` field; `ComposeShadowPatchEngine.ApplyComment` bakes each
   * as a native `w:comment` (ADR-049). IMPORTED session threads (seeded from the retained original's
   * own `w:comment`s) are EXCLUDED — they already ride the retained baseline, so re-emitting them
   * would duplicate. REPLACES the retired {@link getCommentThreadAnnotations} (`DocxAnnotationInput`,
   * text-anchored via the stale `annotations` save field, which the server never deserialized — every
   * comment sent that way was silently dropped). Empty when no session/advisory comments exist.
   */
  getAnchoredComments(): ComposeAnchoredComment[];

  /**
   * Live character + word counters from the TipTap CharacterCount extension.
   * Host renders these in the toolbar or status bar (NFR-04).
   */
  getCounts(): { characters: number; words: number };

  /**
   * Returns true if the editor has unsaved changes since the last serialize().
   * Reset internally on each successful serialize() call.
   */
  isDirty(): boolean;

  /**
   * FR-04 draft-into-editor (spaarkeai-compose-r2 task 016). Materialize a
   * `compose`-disposition draft — re-read FROM the stored session-ledger entry
   * by ComposeWorkspace (ADR-040 render-follows-store) — into the TipTap
   * document, with provenance.
   *
   * R2/016 behaviour is a clean INSERTION of `draft.new_text` at the cursor and
   * marks the document dirty. Positioned `target_text` replacement, pending-
   * redline marks, and the provenance badge are task 031 (custom ProseMirror
   * marks) + FR-17 supersession — this method is the seam they build on. The
   * host calls it via ref only after the workspace has resolved the current
   * stored output; `provenance` fields are Tier 1 identifiers, `draft.new_text`
   * is Tier 3 and is never logged.
   *
   * R2/033 (FR-16): this now delegates to {@link materializePendingRedline} — a
   * draft with a `target_text` renders as a pending insertion/deletion redline
   * pair, an insertion-style draft as a pending insertion. Kept as the stable
   * seam ComposeWorkspace's render-follows-store path (task 016) already calls.
   */
  materializeComposeDraft(draft: ComposeDraftPayload, provenance: ComposeDraftProvenance): void;

  /**
   * FR-16 pending track-change materialization (spaarkeai-compose-r2 task 033).
   * Render the stored `compose`-disposition draft as a PENDING redline using the
   * FR-15 marks (task 031), tagged with `{bindingId}@t{n}` provenance, with inline
   * accept/reject. A `target_text` produces an insertion/deletion pair (resolved by
   * the payload's `match_mode`); an insertion-style draft produces a pending
   * insertion at the cursor. Idempotent per `ledgerRef`; a newer output for the same
   * binding supersedes the prior one (FR-17 alignment). Returns the outcome so the
   * host can distinguish applied vs an unresolved (`ambiguous`/`not_found`) target —
   * the FR-19 "do not guess" rule. The true ledger-supersession WRITE is FR-17/034.
   */
  materializePendingRedline(draft: ComposeDraftPayload, provenance: ComposeDraftProvenance): MaterializeStatus;

  /**
   * DEF-11 whole-document revision. Materialize a CHANGE LIST (`compose-revise-document.edits`) as a
   * MULTI-change pending redline: each edit `i` renders as its own insertion/deletion pair keyed by
   * the sub-provenance `{ledgerRef}#{i}` (so per-change on-click accept/reject stays granular), all
   * under the ONE stored compose output. Accept/Reject on the Assistant confirmation address the BASE
   * `ledgerRef` → Accept-all / Reject-all. Returns one status per edit (index-aligned).
   */
  materializeComposeEdits(edits: ComposeDraftEdit[], provenance: ComposeDraftProvenance): MaterializeStatus[];

  /**
   * DEF-12 — commit the pending redline addressed by `ledgerRef` (delegates to
   * `usePendingRedline.accept`): keep the inserted alternative as normal text, remove the struck
   * original. Called by the WORKSPACE's redline-accept bridge handler when the user clicks Accept on
   * the Assistant confirmation message (the Accept control moved to the Assistant; the accept LOGIC
   * stays here). Also invoked by the in-document per-change on-click affordance. No-op if unmounted.
   */
  acceptPendingRedline(ledgerRef: string): void;

  /**
   * DEF-12 — revert the pending redline addressed by `ledgerRef` in the DOCUMENT
   * (`usePendingRedline.reject`): restore the struck original, drop the insertion. This is the
   * in-document per-change granularity affordance; the Assistant's "Reject" is instead a durable
   * ledger supersession (useEditSupersession.undo). No-op if unmounted.
   */
  rejectPendingRedline(ledgerRef: string): void;

  /**
   * FR-35 Doc Q&A ephemeral highlight (spaarkeai-compose-r2 task 072, stretch).
   * Resolve `sourceText` (the cited excerpt from a grounded Text-path answer)
   * against the CURRENT document and, on a unique match, render a TRANSIENT
   * highlight decoration + scroll it into view. `sectionLabel` drives the
   * "Found in …" affordance. Returns the outcome (`'highlighted'` /
   * `'not_found'` / `'ambiguous'` / `'noop'`) so the caller can distinguish a
   * genuine miss (the citation belongs to a different source than this open
   * document) from success — never guesses (FR-19 sibling rule).
   */
  highlightCitedSpan(sourceText: string, sectionLabel?: string): QaHighlightStatus;

  /** Clear the active Doc Q&A ephemeral highlight immediately (no-op if none active). */
  clearCitedHighlight(): void;

  /**
   * NDA-REVIEW advisory comments (ai-advanced-capabilities-nda-r1 task 031). For each flagged
   * clause, resolves `targetText` against the CURRENT document via `resolveTargetSpans('strict')`
   * — the SAME anchoring primitive {@link highlightCitedSpan} uses — and, on a unique match,
   * creates a PERSISTENT comment thread (`useComposeCommentThreads.createThread`) carrying
   * `explanation` as the thread's text. Uses a DEDICATED `useComposeCommentThreads` instance
   * (author = the configurable `advisoryCommentAuthor` prop, task 052 — defaults to
   * `'AI Advisory Review'`), separate from the session Comments panel's own instance —
   * both apply the SAME `commentAnchor` mark to the document, so both are visible as
   * comment-anchor spans; a future right-gutter layout unifies the browsing UI across both
   * authors. Ranges that fail strict resolution (`not_found` / `ambiguous`) are reported via the
   * result's `failed` list — NEVER silently dropped (FR-19 "do not guess" rule). No-op (returns
   * every item as `not_found`) if the editor is unmounted.
   */
  placeAdvisoryComments(items: readonly AdvisoryCommentInput[]): AdvisoryCommentPlacementResult;

  /**
   * The advisory comment threads placed via {@link placeAdvisoryComments} so far, in creation
   * order. READ SURFACE task 031 exposed for task 040 to consume — {@link getAnchoredComments} now
   * maps these threads (alongside the session Comments panel's own thread instance) to native
   * `w:comment` save output. Kept as its own getter (rather than folded silently into
   * {@link getAnchoredComments}) so a caller can still inspect/count the advisory threads on their
   * own, independent of the export mapping.
   */
  getAdvisoryCommentThreads(): readonly ComposeCommentThreadModel[];
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021 dark-mode compliant)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
    overflow: 'hidden',
  },
  // FIX #9 — the scroll region that wraps the editor surface + the floating
  // "scroll for more" FAB. `position: relative` anchors the absolutely-positioned
  // FAB; it does NOT itself scroll (the inner `editorSurface` does).
  editorScrollWrap: {
    position: 'relative',
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  editorSurface: {
    flex: 1,
    overflow: 'auto',
    // FIX #9 — hide the native scrollbar while remaining scrollable. The floating
    // down-arrow FAB is the progressive-scroll affordance instead of a visible
    // scrollbar. Layout/visibility only (no color) — ADR-021-neutral.
    scrollbarWidth: 'none',
    '::-webkit-scrollbar': {
      display: 'none',
    },
    padding: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    // The actual ProseMirror content node renders inside `.ProseMirror`.
    // We can't reach it via CSS-in-JS class selectors easily, so we rely on
    // the host theme to drive font colors via inherited semantic tokens.
    // Direct ProseMirror styling (link color, table borders) uses semantic
    // tokens at the rule level below.
    '& .ProseMirror': {
      outline: 'none',
      minHeight: '100%',
      color: tokens.colorNeutralForeground1,
      // FR-08 (task 021, WS-2): xml:space="preserve" runs + consecutive spaces are stored verbatim by the
      // server projection (F-1 text exactness) but the browser's default `white-space: normal` collapses
      // runs of spaces at RENDER time — a purely visual loss, not a data loss (the reader already stores
      // the characters correctly; see task 021 notes). `pre-wrap` preserves consecutive spaces AND line
      // breaks in the source markup while still soft-wrapping at the container width (unlike `pre`, which
      // never wraps and would overflow the editor). Scoped to `.ProseMirror` only — this is the same rule
      // `prosemirror-view`'s own bundled `style/prosemirror.css` ships by default (this app does not import
      // that stylesheet, so the rule was previously absent here). Verified NOT to regress `.compose-tab`
      // (a single preserved space per tab — pre-wrap renders a lone space identically to `normal`),
      // `.compose-atom`/`.compose-atom-block` (block layout is orthogonal to white-space; pre-wrap still
      // wraps, it does not suppress wrapping like `pre` would), or `text-align`/`margin-left`/`text-indent`
      // (orthogonal CSS properties).
      whiteSpace: 'pre-wrap',
    },
    '& .ProseMirror a': {
      color: tokens.colorBrandForegroundLink,
      textDecoration: 'underline',
    },
    // FR-13 (task 032, fidelity-r4.5): the editor NEVER relies on the browser `<ol>` CSS auto-count for
    // a legal number (F-3 invariant) — `composeNumberAtomExtension.ts`'s decoration is the SOLE source
    // of a displayed number. Suppressing the native marker here (not per-item) is intentionally
    // unconditional: it applies even to the rare unresolvable-numId case where no atom renders (031's
    // "do not fabricate a number" fail-closed posture) — showing NO number is safer than a CSS-counted
    // one that would silently disagree with 031's computed label ("1." vs "4.2", the double-numbering
    // defect this task exists to fix). `<ul>` bullet lists are untouched (no legal number involved).
    '& .ProseMirror ol': {
      listStyleType: 'none',
    },
    '& .ProseMirror table': {
      borderCollapse: 'collapse',
      width: '100%',
    },
    '& .ProseMirror th, & .ProseMirror td': {
      border: `1px solid ${tokens.colorNeutralStroke1}`,
      padding: tokens.spacingHorizontalS,
    },
    '& .ProseMirror th': {
      backgroundColor: tokens.colorNeutralBackground2,
      fontWeight: tokens.fontWeightSemibold,
    },
    '& .ProseMirror blockquote': {
      borderLeft: `4px solid ${tokens.colorNeutralStroke1}`,
      paddingLeft: tokens.spacingHorizontalM,
      color: tokens.colorNeutralForeground2,
    },
    '& .ProseMirror hr': {
      border: 'none',
      borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
      margin: `${tokens.spacingVerticalM} 0`,
    },
    // R2 custom marks (task 031, FR-15) — pending track-change redline + comment anchor.
    // ADR-021: semantic tokens only (no hardcoded hex); the palette foreground/background tokens
    // are theme-aware, so added/removed/anchor colors stay legible in both light and dark.
    '& .compose-mark-insertion': {
      color: tokens.colorPaletteGreenForeground1,
      textDecorationLine: 'underline',
    },
    '& .compose-mark-deletion': {
      color: tokens.colorPaletteRedForeground1,
      textDecorationLine: 'line-through',
    },
    // Item 4 (UAT round-4): the LIVE Track Changes decoration overlay. Same green-underline /
    // red-strike redline look as the AI marks above, but DISTINCT classes so they do NOT inherit the
    // AI-rationale lightbulb `::before` (a user's own edit carries no rationale popover). These style
    // ProseMirror decoration spans/widgets, not schema marks — pure view (see TrackChangesExtension.ts).
    '& .compose-track-insertion': {
      color: tokens.colorPaletteGreenForeground1,
      textDecorationLine: 'underline',
    },
    '& .compose-track-deletion': {
      color: tokens.colorPaletteRedForeground1,
      textDecorationLine: 'line-through',
      // The deleted text is a non-editable widget reinserted for display only.
      userSelect: 'none',
    },
    // U1 R2 (UAT 2026-07-20): a small lightbulb at the FRONT of each pending redline signals "click me
    // for the rationale". A redline renders as a deletion span (struck original) immediately followed by
    // an insertion span (new text) — the rule below puts ONE bulb on whichever span comes first and
    // SUPPRESSES the duplicate on the insertion half of a deletion→insertion pair, so a pair shows a
    // single cue. Semantic token color; no text-decoration bleed onto the glyph.
    '& .compose-mark-insertion::before, & .compose-mark-deletion::before': {
      content: '"\\1F4A1"', // 💡
      fontSize: '0.8em',
      marginRight: '2px',
      color: tokens.colorNeutralForeground3,
      textDecorationLine: 'none',
      cursor: 'pointer',
      userSelect: 'none',
      verticalAlign: 'baseline',
    },
    '& .compose-mark-deletion + .compose-mark-insertion::before': {
      content: 'none', // the pair's cue already sits on the leading deletion span
    },
    // UAT round-5 #5 — the BASE advisory comment anchor is LIGHT GRAY; it turns YELLOW only when its
    // thread is selected (the `compose-mark-comment-anchor-selected` view decoration below, painted by
    // SelectedCommentExtension) — coordinated with the selected review note (also yellow). This makes
    // "highlighted (a finding is here)" clearly distinct from "selected (this is the one I'm on)".
    '& .compose-mark-comment-anchor': {
      backgroundColor: tokens.colorNeutralBackground3,
      color: tokens.colorNeutralForeground1,
      borderRadius: tokens.borderRadiusSmall,
    },
    // The SELECTED advisory anchor (view decoration, never serialized) overrides the base blue with
    // yellow. Higher specificity than the base rule so it wins wherever both classes apply.
    '& .compose-mark-comment-anchor.compose-mark-comment-anchor-selected, & .compose-mark-comment-anchor-selected': {
      backgroundColor: tokens.colorPaletteYellowBackground2,
      color: tokens.colorNeutralForeground1,
      borderRadius: tokens.borderRadiusSmall,
    },
    // R4 FR-02 opaque-atom placeholders (task 021) — non-editable SDT/field/object placeholders
    // (`composeBlockAtom` / `composeInlineAtom`, opaqueAtomNode.ts). Semantic tokens only (ADR-021:
    // no hardcoded hex; theme-adaptive in both light and dark). `userSelect: 'none'` is layout/
    // interaction only (not a color rule) — the placeholder is inspectable/selectable as a whole
    // ProseMirror node, but its label text is not itself a text-editing target.
    '& .compose-atom': {
      color: tokens.colorNeutralForeground2,
      backgroundColor: tokens.colorNeutralBackground3,
      border: `1px dashed ${tokens.colorNeutralStroke1}`,
      borderRadius: tokens.borderRadiusSmall,
      fontStyle: 'italic',
      userSelect: 'none',
      cursor: 'default',
    },
    '& .compose-atom-block': {
      display: 'block',
      padding: tokens.spacingVerticalXS,
      margin: `${tokens.spacingVerticalXS} 0`,
      textAlign: 'center',
    },
    '& .compose-atom-inline': {
      display: 'inline-block',
      padding: `0 ${tokens.spacingHorizontalXXS}`,
    },
    '& .ProseMirror-selectednode.compose-atom': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorBrandStroke1,
    },
    // FR-13 (task 032, fidelity-r4.5): the explicit non-editable number-atom prefix
    // (`composeNumberAtomExtension.ts` — a ProseMirror widget DECORATION, never a doc node). Semantic
    // tokens only (ADR-021: no hardcoded hex; theme-adaptive in both light and dark).
    // `userSelect: 'none'`/`pointerEvents: 'none'` are layout/interaction only (not color rules) — the
    // widget is not part of the doc model at all, so it is already structurally impossible to place a
    // caret inside; these are defense-in-depth so it never intercepts a click meant for the adjacent text.
    '& .compose-number-atom': {
      display: 'inline-block',
      marginRight: tokens.spacingHorizontalXS,
      color: tokens.colorNeutralForeground1,
      fontWeight: tokens.fontWeightSemibold,
      userSelect: 'none',
      pointerEvents: 'none',
      whiteSpace: 'nowrap',
    },
    // FR-35 Doc Q&A ephemeral highlight (task 072) — a ProseMirror view
    // decoration, NOT a doc Mark (never serializes to DOCX). Semantic tokens
    // only (ADR-021 dark-mode-correct).
    '& .compose-qa-highlight': {
      backgroundColor: tokens.colorPaletteMarigoldBackground2,
      borderRadius: tokens.borderRadiusSmall,
      transition: 'background-color 0.2s ease-out',
    },
    // FR-17 find/replace (task 040) — a ProseMirror view decoration (see
    // ComposeFindReplaceExtension in useComposeFindReplace.ts), NOT a doc Mark: highlighting never
    // serializes to DOCX. Two tiers: every match gets a subtle highlight; the current
    // (prev/next-navigated) match gets a stronger brand-colored one so it's visually distinct.
    // Semantic tokens only (ADR-021 dark-mode-correct).
    '& .compose-find-match': {
      backgroundColor: tokens.colorNeutralBackground3,
      borderRadius: tokens.borderRadiusSmall,
    },
    '& .compose-find-match-current': {
      backgroundColor: tokens.colorBrandBackground2,
      borderRadius: tokens.borderRadiusSmall,
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorBrandStroke1,
    },
  },
  loadingState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
  // task 038 (spaarkeai-compose-r4 zero-error guardrails) — the NON-BLOCKING, dismissible sticky notice
  // shown when a deferred/unrepresentable/refused step is seen (most importantly formatted/linked PASTE).
  // Calm + informative (a warning tone, NOT an error/blocker). Sticky so it stays visible above the
  // scrolling body until dismissed. Semantic tokens only (ADR-021 dark-mode-correct; no hardcoded hex).
  deferralNotice: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    flexShrink: 0,
    position: 'sticky',
    top: 0,
    zIndex: 2,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
    backgroundColor: tokens.colorStatusWarningBackground1,
    color: tokens.colorStatusWarningForeground1,
    borderBottom: `1px solid ${tokens.colorStatusWarningBorder1}`,
  },
  deferralNoticeText: {
    flex: 1,
    minWidth: 0,
  },
  // Wave 6 (DEF-G) — reference-only state for a non-docx file that reached the
  // editor. Calm + informative (NOT an error / not a blank editor). Semantic
  // tokens only (ADR-021 dark-mode-correct).
  referenceOnly: {
    display: 'flex',
    flex: 1,
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    rowGap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalXXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground2,
  },
  referenceOnlyIcon: {
    fontSize: '48px',
    width: '48px',
    height: '48px',
    color: tokens.colorNeutralForeground3,
  },
  referenceOnlyDetail: {
    maxWidth: '420px',
    color: tokens.colorNeutralForeground3,
  },
  // Task 013 (F-2 "one reader") — the mammoth client-side fallback reader was deleted, so a docx
  // mount with no server projection (BFF unreachable/failed) has no reader left. Distinct from
  // `referenceOnly` (a non-docx file, by design not editable) — this is a FAILURE state, so the
  // icon uses the danger semantic token while reusing the same calm, centered layout. Semantic
  // tokens only (ADR-021 dark-mode-correct).
  projectionUnavailableIcon: {
    fontSize: '48px',
    width: '48px',
    height: '48px',
    color: tokens.colorStatusDangerForeground1,
  },
  // DEF-17 (UAT-R3): the popup (AI-actions-ONLY since task 111) must present a
  // SINGLE row. The former `flexWrap: 'wrap'` + `maxWidth: '420px'` cap forced
  // a second wrapped row once the text-labelled buttons exceeded 420px — that
  // is exactly the two-line bug DEF-17 fixes. The popup now sizes to its
  // single-row content (`flexWrap: 'nowrap'`, no width cap); any action that
  // would not fit lives in ComposeAiToolbar's ⋯ overflow menu rather than
  // wrapping. `maxWidth: '100vw'` is a viewport safety net only (a floating
  // tippy popup should never exceed the screen), not a wrap trigger.
  bubbleMenu: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'nowrap',
    maxWidth: '100vw',
    columnGap: tokens.spacingHorizontalXXS,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
    padding: tokens.spacingHorizontalXXS,
  },
  // Task 111 — right-click (context-menu) AI-toolbar trigger. Reuses
  // `bubbleMenu`'s visual treatment (semantic tokens, dark-mode-correct) via
  // `mergeClasses`; only the positioning differs (fixed at the click point
  // instead of tippy-anchored to the selection).
  contextMenuPopup: {
    position: 'fixed',
    zIndex: 1000,
  },
  // FIX #9 — layout-only wrapper for the AI selection bubble + right-click popup.
  // The elevated light-grey SURFACE (background + shadow + radius) now lives on
  // `ComposeAiToolbar`'s own Toolbar so it spans the whole menu (was previously on
  // `bubbleMenu`, which produced the partial-background UAT finding). This wrapper
  // only positions/sizes the popup; it carries no surface of its own so there is
  // no double background. (`bubbleMenu` below is retained for the redline
  // accept/reject popover, whose buttons still need a surface.)
  aiBubbleWrap: {
    display: 'flex',
    alignItems: 'center',
    maxWidth: '100vw',
  },
  // FIX #9 — floating circular "scroll for more" button, pinned bottom-center of
  // the editor scroll region. Elevated (shadow) so it reads above the page; shown
  // only when the surface is not scrolled to the bottom. Semantic tokens only.
  scrollDownFab: {
    position: 'absolute',
    bottom: tokens.spacingVerticalL,
    left: '50%',
    transform: 'translateX(-50%)',
    zIndex: 2,
    boxShadow: tokens.shadow16,
  },
  // FR-23 (task 044) — floating "Comments" panel toggle, pinned top-right of the editor scroll
  // region (the top-center/bottom-center positions are taken by the format toolbar / scroll FAB).
  // Semantic tokens only (ADR-021 dark-mode-correct); mirrors `scrollDownFab`'s elevated-FAB treatment.
  commentsToggleFab: {
    position: 'absolute',
    top: tokens.spacingVerticalM,
    right: tokens.spacingHorizontalM,
    zIndex: 2,
    boxShadow: tokens.shadow16,
  },
  // task 032 (right-gutter comment layout) — reserves room for the right-rail comment gutter so its
  // cards sit in true margin space alongside the document rather than overlapping body text. Applied
  // ONLY while there is at least one advisory-comment thread to place (see the render section below);
  // an editor with no advisory comments keeps its full-width `editorSurface` padding.
  editorSurfaceWithGutter: {
    paddingRight: `calc(${COMMENT_GUTTER_WIDTH_PX}px + ${tokens.spacingHorizontalL})`,
  },
  // FR-22 (task 043) — floating "Styles" pane toggle, pinned top-LEFT of the editor scroll region
  // (the top-right corner is taken by `commentsToggleFab`). Semantic tokens only (ADR-021
  // dark-mode-correct); mirrors `commentsToggleFab`'s elevated-FAB treatment.
  stylesToggleFab: {
    position: 'absolute',
    top: tokens.spacingVerticalM,
    left: tokens.spacingHorizontalM,
    zIndex: 2,
    boxShadow: tokens.shadow16,
  },
  // FR-14 (task 031) — rationale-first popover restructure: the rationale is the visual HEADLINE
  // (bold, full foreground weight), the confidence band a SECONDARY row underneath (design §6.2 —
  // never a numeric score; coarse band only). Semantic tokens only (ADR-021 dark-mode-correct).
  // U1 fix (UAT 2026-07-20): the per-change popover is now a PROPER CARD (column layout, comfortable
  // width) so the cited rationale — the primary trust cue (design §6.2) — is fully READABLE instead of a
  // clipped single line in the compact single-row bubble the popover used to borrow. Semantic tokens only
  // (ADR-021 dark-mode-correct); positioned at the click point by `contextMenuPopup`.
  // U1 (UAT 2026-07-20 R2): a responsive CARD — sizes to its content (up to a viewport-safe max),
  // never the former clipped single-row bubble. Semantic tokens only (ADR-021 dark-mode-correct);
  // positioned at the click point by `contextMenuPopup`.
  redlinePopover: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    width: 'max-content',
    minWidth: '220px',
    maxWidth: 'min(360px, 92vw)',
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow16,
  },
  // U1 R2: the confidence band is now the compact HEADER (the "Suggested edit" label was removed at the
  // operator's request), with a little padding + a hairline divider below it separating it from the
  // rationale body. The §6.2 anti-rubber-stamp safeguards are UNCHANGED — a low-band edit still shows its
  // explicit "Needs review" cue here and its Accept button stays demoted below.
  redlineTopBar: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
    rowGap: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalXXS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // U1: the cited rationale — WRAPS to as many lines as needed (up to a scrollable cap) instead of the
  // former single-line ellipsis truncation that hid most of the explanation.
  redlineHeadline: {
    display: 'block',
    width: '100%',
    maxHeight: '9.5em',
    overflowY: 'auto',
    whiteSpace: 'normal',
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
  },
  // The Accept/Reject actions row — right-aligned, comfortably spaced (a proper card footer).
  redlineActions: {
    display: 'flex',
    justifyContent: 'flex-end',
    columnGap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
  redlineSecondaryRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
  // The explicit-review affordance a low-band redline carries (design §6.2 anti-rubber-stamp —
  // never pre-selected/auto-accepted; each low-band edit surfaces this cue).
  redlineNeedsReview: {
    color: tokens.colorStatusWarningForeground1,
  },
  // FR-14 (task 031) — the pending-redlines summary bar: count + "Accept all" (excludes low-band by
  // construction) + the SEPARATE, always-explicit "include low-confidence" action (design §6.2 —
  // accept-all MUST NOT silently include low-band edits; including them is a deliberate second click).
  redlineSummaryBar: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  redlineSummaryText: {
    flex: 1,
    minWidth: 0,
  },
  redlineError: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorStatusWarningBackground1,
    color: tokens.colorStatusWarningForeground1,
    borderBottom: `1px solid ${tokens.colorStatusWarningBorder1}`,
  },
  redlineErrorText: {
    flex: 1,
    minWidth: 0,
  },
  // FR-35 Doc Q&A ephemeral highlight banner (task 072, stretch). Semantic
  // tokens only (ADR-021 dark-mode-correct) — transient, dismissible-by-timeout.
  qaHighlightBanner: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorPaletteMarigoldBackground1,
    color: tokens.colorPaletteMarigoldForeground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
});

/**
 * FR-14 (task 031) — coarse, qualitative confidence label for the SECONDARY band badge (design §6.2
 * anti-false-precision: NEVER a numeric/percentage score). Mirrors {@link deriveConfidenceBand}'s
 * three-value output verbatim.
 */
function confidenceBandLabel(band: ConfidenceBand): string {
  switch (band) {
    case 'high':
      return 'High confidence';
    case 'medium':
      return 'Medium confidence';
    case 'low':
    default:
      return 'Low confidence';
  }
}

/** Fluent v9 semantic `Badge` color per band (design tokens under the hood — no hard-coded hex, ADR-021). */
function confidenceBandColor(band: ConfidenceBand): 'success' | 'informative' | 'warning' {
  switch (band) {
    case 'high':
      return 'success';
    case 'medium':
      return 'informative';
    case 'low':
    default:
      return 'warning';
  }
}

// ---------------------------------------------------------------------------
// Heartbeat hook — REMOVED in R2/R3 refactor (FU-1 fix, 2026-06-29).
// ---------------------------------------------------------------------------
//
// Previously this file owned `useComposeHeartbeat` (3-min sliding, visibility-
// gated, defensive). The hook fired regardless of the Dataverse-side check-out
// state, which produced wasted HTTP traffic after a force-close / cancel
// (FU-1 — cancelled tab continues heart-beating a lock it no longer holds).
//
// The heartbeat has been HOISTED to the workspace level and gated on
// `checkoutStatus === 'acquired'`:
//
//   src/solutions/SpaarkeAi/src/components/compose/hooks/useComposeHeartbeatGate.ts
//
// The workspace (`ComposeWorkspace.tsx`) owns the checkout reducer, so the
// gating signal is local to the timer effect there. The editor is now a pure
// drafting surface with no lock-lifecycle concerns.
//
// The `bffBaseUrl` prop is retained on this editor for shape-compatibility
// (some consumers thread it for future telemetry) but is otherwise unused
// here.

// ---------------------------------------------------------------------------
// Selection-event dispatcher (Flow 1 + Flow 2 — debounced)
// ---------------------------------------------------------------------------

/**
 * Wire TipTap selection-update events to PaneEventBus dispatches.
 *
 * Flow 1 (`context.compose_selection_changed`):
 *  - Fires on every selection change after 250ms debounce
 *  - Carries `selectionText` (Tier 3 — subscribers consume; logger strips)
 *
 * Flow 2 (`conversation.compose_selection_offer`):
 *  - Fires only when selection is non-collapsed AND ≥10 chars
 *  - Carries the JPS scope name `compose-selection`
 *  - Subscribers (ConversationPane) render the action menu
 *
 * Both flows are dispatched on the existing PaneEventBus via
 * `useDispatchPaneEvent` from `@spaarke/ai-widgets`. The discriminated event
 * types are additive on `context` + `conversation` channels per ADR-030.
 */
function useSelectionEventDispatch(
  editor: Editor | null,
  documentRef: ComposeEditorDocumentRef | undefined,
  sessionId: string,
  dispatch: DispatchPaneEvent
): void {
  // Track a per-instance debounce timer.
  const debounceTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  React.useEffect(() => {
    if (!editor || !documentRef) return;

    const handler = () => {
      if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = setTimeout(() => {
        const { from, to } = editor.state.selection;
        const rawText = editor.state.doc.textBetween(from, to, ' ');
        const selectionText = rawText.length > SELECTION_TEXT_CAP ? rawText.slice(0, SELECTION_TEXT_CAP) : rawText;

        const timestamp = new Date().toISOString();

        // Flow 1 — always fires on selection-change (even collapsed selections,
        // because subscribers may want to update precedent context as cursor
        // moves through clauses).
        // Per ADR-030: `compose_selection_changed` is now a TYPED additive
        // discriminant on the `context` channel (ContextPaneEvent — enumerated at
        // the shared-lib bus layer by task 104). No `as any` — the literal is
        // type-checked against the channel union.
        dispatch('context', {
          type: 'compose_selection_changed',
          documentRef,
          selection: {
            from,
            to,
            selectionText, // Tier 3 — subscribers strip before logging
          },
          sessionId,
          timestamp,
        });

        // Flow 2 — fires only when selection is meaningful (non-collapsed +
        // ≥10 chars) to avoid noise on click-only cursor moves.
        // `compose_selection_offer` is a TYPED additive discriminant on the
        // `conversation` channel (ConversationPaneEvent — task 104).
        const isCollapsed = from === to;
        if (!isCollapsed && selectionText.length >= FLOW2_MIN_CHARS) {
          dispatch('conversation', {
            type: 'compose_selection_offer',
            documentRef,
            selection: {
              from,
              to,
              selectionText,
            },
            jpsScope: 'compose-selection',
            sessionId,
            timestamp,
          });
        }
      }, SELECTION_DEBOUNCE_MS);
    };

    editor.on('selectionUpdate', handler);
    return () => {
      editor.off('selectionUpdate', handler);
      if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
    };
  }, [editor, documentRef, sessionId, dispatch]);
}

// ---------------------------------------------------------------------------
// Advisory-comment anchor resolution (UAT round-3 S1)
// ---------------------------------------------------------------------------

/**
 * A distinctive leading fragment of an excerpt — likelier to be VERBATIM (and unique) than the full,
 * possibly lightly-paraphrased/truncated excerpt. First-sentence within a window, min ~24 chars so it
 * stays distinctive, capped ~120 at a word boundary.
 */
function distinctiveAnchorPrefix(text: string): string {
  const trimmed = text.trim();
  if (trimmed.length <= 40) return trimmed;
  const window = trimmed.slice(0, 120);
  const sentenceEnd = window.search(/[.;]\s/);
  if (sentenceEnd >= 24) return window.slice(0, sentenceEnd);
  const cap = trimmed.slice(0, 100);
  const lastSpace = cap.lastIndexOf(' ');
  return lastSpace >= 24 ? cap.slice(0, lastSpace) : cap;
}

/**
 * UAT round-3 S1 — resolve an advisory comment's anchor with graceful fallbacks so a flagged clause is
 * still highlighted/commented when its verbatim `targetText` doesn't STRICTLY resolve (the model lightly
 * paraphrased or truncated it, it spans a paragraph boundary, or it recurs in the document). Unlike a
 * redline EDIT — which MUST stay strict, since a wrong anchor corrupts the document — an advisory COMMENT
 * only needs to sit on the flagged text, so anchoring to a verbatim PREFIX or the FIRST occurrence is a
 * safe, useful fallback, NOT a blind guess (the prefix / first match is real document text that actually
 * appears). Returns null only when even a distinctive prefix cannot be located — the finding then stays
 * in the Review Summary, unanchored (surfaced via the placement-failure count), never silently forced
 * onto the wrong text.
 */
function resolveAdvisoryAnchorSpan(editor: Editor, targetText: string): { from: number; to: number } | null {
  const strict = resolveTargetSpans(editor, targetText, 'strict');
  if (strict.ok) return strict.spans[0];
  // Recurs verbatim (ambiguous) → anchor the FIRST occurrence of the flagged clause.
  if (strict.kind === 'ambiguous') {
    const first = resolveTargetSpans(editor, targetText, 'first');
    if (first.ok) return first.spans[0];
  }
  // Paraphrased / truncated / cross-paragraph (not_found) → retry with a distinctive verbatim PREFIX,
  // which anchors the comment at the flagged clause's start.
  const prefix = distinctiveAnchorPrefix(targetText);
  if (prefix && prefix !== targetText) {
    const p = resolveTargetSpans(editor, prefix, 'strict');
    if (p.ok) return p.spans[0];
    if (p.kind === 'ambiguous') {
      const pf = resolveTargetSpans(editor, prefix, 'first');
      if (pf.ok) return pf.spans[0];
    }
  }
  return null;
}

// ---------------------------------------------------------------------------
// ComposeEditor — the component
// ---------------------------------------------------------------------------

/**
 * The Compose editor. See file-level JSDoc for the contract.
 *
 * Mounting pattern:
 *   <ComposeEditor
 *     ref={editorRef}
 *     docxBytes={docxBytes}
 *     documentRef={docRef}
 *     bffBaseUrl={getBffBaseUrl()}
 *     sessionId={chatSessionId}
 *     onDirtyChange={(dirty) => setDirty(dirty)}
 *     onImportWarnings={(msgs) => setWarnings(msgs)}
 *   />
 *
 * Saving:
 *   const bytes = await editorRef.current?.serialize();
 *   // upload bytes via ComposeDocumentService POST /api/compose/document/.../save
 */
export const ComposeEditor = React.forwardRef<ComposeEditorHandle, ComposeEditorProps>(
  function ComposeEditor(props, ref) {
    const {
      docxBytes,
      initialHtml,
      paraIdMap,
      importedRevisions,
      importedComments,
      projection,
      documentRef,
      bffBaseUrl,
      sessionId = '',
      onDirtyChange,
      onImportWarnings,
      enqueueComposeAction,
      onOpenInWord,
      onOpenInWordDesktop,
      wordActionsDisabled,
      onSave,
      canSave,
      isSaving,
      onRefreshProfile,
      onReloadFromSource,
      onOpenDocument,
      isRefreshingProfile,
      commentAuthor = 'You',
      advisoryCommentAuthor = 'AI Advisory Review',
      reviewSummary,
      activeWorkType = '*',
    } = props;

    const styles = useStyles();
    const dispatch = useDispatchPaneEvent();

    const [isImporting, setIsImporting] = React.useState<boolean>(false);
    const dirtyRef = React.useRef<boolean>(false);

    // task 038 (spaarkeai-compose-r4 zero-error guardrails): a NON-BLOCKING, dismissible sticky notice
    // surfaced when a deferred/unrepresentable/refused step is seen (most importantly formatted or linked
    // PASTE, which bypasses the toolbar gate). Declared BEFORE the op-log init below so the op-log's
    // classifier callbacks can close over the stable `setDeferralNotice` setter. Non-null ⇒ the banner
    // is shown; the save path is unaffected (informs only, never blocks).
    const [deferralNotice, setDeferralNotice] = React.useState<string | null>(null);
    // R3 FR-01 (task 027): the LOAD-TIME `{ paraId → reject-state text }` snapshot, captured right after
    // stampParaIds (and after a born-in-editor seed). Feeds `getBaselineParaIdMap()` (the C2 minted-id
    // stamp) and the live Track Changes decoration baseline; the retired paragraph-diff export that used
    // to diff against it (`collectEditedParagraphs`) was removed in task 023.
    const paraIdSnapshotRef = React.useRef<Map<string, string>>(new Map());

    // R4 FR-06 (task 032, the write-path cutover): the per-dirty-session ordered, rebased task-003 OPERATION
    // LOG (task 022). Instantiated ONCE (lazy) and driven by the rebased-op-log ProseMirror plugin below; the
    // classifier callbacks surface refused/deferred/unrepresentable steps (never silently dropped, per the
    // interceptor's discipline). `serializeOperationLog()` on the handle reads it at save time; the load effect
    // + serialize both `reset()` it (a fresh document / a completed save must not carry stale ops).
    const opLogRef = React.useRef<RebasedOperationLog | null>(null);
    // task 038: the op-log high-water mark captured by `serializeOperationLog()` so a later
    // `commitSaved()` (invoked ONLY after a confirmed 200) drops exactly the persisted batch while
    // preserving any edits made during the in-flight save. `-Infinity` before the first serialize.
    const committedBoundaryRef = React.useRef<number>(Number.NEGATIVE_INFINITY);
    if (opLogRef.current === null) {
      opLogRef.current = new RebasedOperationLog({
        onStructuralStep: (_step, reason) => {
          // A block-boundary step the four structural ops cannot cleanly carry (forward-merge, multi-paragraph
          // rewrite, list wrap/unwrap). Recognized, not mis-mapped — surfaced for diagnosis (never dropped).
          // eslint-disable-next-line no-console
          console.debug('[ComposeEditor] op-log: deferred structural step', reason);
          // task 038: inform the user (non-blocking). Catches formatted/linked PASTE that slips past the
          // disabled toolbar controls. The representable text edits in this batch still save.
          setDeferralNotice(DEFERRED_FORMAT_NOTICE);
        },
        onUnrepresentableStep: (_step, reason) => {
          // A step whose shape the closed op set genuinely cannot represent (e.g. a mark outside the closed
          // ComposeMarkType set) — the escalation seam (root §6/§6.5). Surfaced, never silently dropped.
          // eslint-disable-next-line no-console
          console.warn('[ComposeEditor] op-log: unrepresentable step (surfaced, not applied)', reason);
          // task 038: same non-blocking notice — a pasted hyperlink/mark outside the closed set lands here.
          setDeferralNotice(DEFERRED_FORMAT_NOTICE);
        },
        onRefusedAtomEdit: () => {
          // An edit whose range entered an opaque atom (field/content-control) — refused by contract (FR-02).
          // eslint-disable-next-line no-console
          console.debug('[ComposeEditor] op-log: refused opaque-atom edit');
          // task 038: inform the user that the edit over a protected field/content-control was not applied.
          setDeferralNotice(DEFERRED_FORMAT_NOTICE);
        },
      });
    }
    // A headless TipTap extension that registers the rebased-op-log ProseMirror plugin (drives
    // `recordTransaction` per doc-changing transaction). Built ONCE (the log instance is stable in the ref).
    const opLogExtension = React.useMemo(
      () =>
        Extension.create({
          name: 'composeRebasedOpLog',
          addProseMirrorPlugins() {
            return [createRebasedOperationLogPlugin(opLogRef.current!)];
          },
        }),
      []
    );

    // Item 4 (UAT round-4): live Track Changes decoration overlay. The extension is configured ONCE
    // (stable options) — `getBaseline` reads the load-time snapshot ref live, so the redline tracks
    // edits without re-registering the plugin. Enabled state is driven via a transaction meta (below),
    // NOT via re-configuring the extension. See TrackChangesExtension.ts for the decoration-not-mark
    // design rationale (edits stay real content → persist via the step-interceptor operation-log path).
    const trackChangesExtension = React.useMemo(
      () =>
        TrackChangesExtension.configure({
          // UAT round-4: Track Changes is ON by default — user edits show as redlines immediately (a
          // freshly-loaded doc shows nothing until the first edit, since current == baseline).
          initialEnabled: true,
          getBaseline: () => paraIdSnapshotRef.current,
        }),
      []
    );
    const [trackChangesEnabled, setTrackChangesEnabled] = React.useState<boolean>(true);

    // ----- Wave 6 (DEF-G) — non-docx reference-only state ------------------
    // Non-null when a NON-DOCX buffer reached the editor (detected by byte
    // signature before the mount, or via a fail-closed projection). The editor
    // then renders an explicit reference-only surface instead of a silent empty
    // `<p></p>`. `fileName` is the UI label only (Tier 1 identifier).
    const [referenceOnly, setReferenceOnly] = React.useState<{ fileName?: string } | null>(null);

    // ----- Task 013 (F-2 "one reader") — projection-unavailable state ------
    // Non-null when a valid, editable DOCX buffer reached the editor but NO server projection was
    // supplied (`projection` prop is null) — i.e. the `POST /api/compose/{project,upload}` round-trip
    // failed, was unreachable, or the host is an older BFF build without projection support. Before
    // task 013 this fell back to a client-side mammoth conversion; that second reader is now deleted
    // (F-2 — exactly one docx→editor reader). There is nothing left to fall back to, so the editor
    // surfaces this explicit, calm error/unavailable state — NEVER a silent blank editor and NEVER a
    // second client-side docx parser. `fileName` is the UI label only (Tier 1 identifier).
    const [projectionUnavailable, setProjectionUnavailable] = React.useState<{ fileName?: string } | null>(null);

    // ----- Task 111 — right-click (context-menu) AI-toolbar trigger --------
    // The screen point (viewport coords) of the last suppressed native context
    // menu; non-null while the point-insertion AI-toolbar popup is open. Set
    // via the ProseMirror `handleDOMEvents.contextmenu` hook below (the
    // TipTap-idiomatic way to intercept a raw DOM event scoped to the editor's
    // contenteditable region — NOT a React `onContextMenu` on an ancestor,
    // which would also catch right-clicks on the surrounding chrome). This
    // popup is independent of TipTap's tippy-driven `BubbleMenu` (which stays
    // selection-only, per its default `shouldShow`): a plain, React-state-
    // driven popup positioned at the click point, so it works even when there
    // is NO selection (a caret / point-insertion), per task 111 requirement 2.
    const [contextMenuAnchor, setContextMenuAnchor] = React.useState<{ x: number; y: number } | null>(null);
    const contextMenuRef = React.useRef<HTMLDivElement | null>(null);

    // ----- Task 040 — FR-17 in-editor find/replace panel toggle ------------
    // Ctrl/Cmd+F (intercepted below via editorProps.handleKeyDown, overriding the browser's native
    // find) opens the panel; the panel's own Escape handling (ComposeFindReplace.tsx) closes it and
    // clears the search state/highlight. Mounted between the format toolbar and the AI BubbleMenu —
    // see the render section below.
    const [findReplaceOpen, setFindReplaceOpen] = React.useState<boolean>(false);

    // ----- Task 044 — FR-23 comment-thread panel toggle + "create on selection" capture -----------
    // The panel toggle button lives in this file (see the render section below) rather than the
    // format toolbar (out of this task's file scope — additive-only). Opening the panel captures the
    // CURRENT selection range + preview text BEFORE focus moves into the panel's composer textbox
    // (mirrors the redline/context-menu popups' click-time capture above); a collapsed selection
    // yields `null` and the panel shows a "select text" hint instead of guessing an anchor.
    const [commentsOpen, setCommentsOpen] = React.useState<boolean>(false);
    const [pendingCommentRange, setPendingCommentRange] = React.useState<ComposeCommentPendingRange | null>(null);
    // ai-advanced-capabilities-nda-r1 (UAT round-2 item #2) — "Review Notes" visibility: whether the
    // right-gutter advisory comment cards are shown. Defaults ON (the prior always-on-when-threads-exist
    // behavior); the "Review" toolbar dropdown toggles it without discarding the placed threads.
    const [reviewNotesVisible, setReviewNotesVisible] = React.useState<boolean>(true);
    // UAT round-3 D1 — user-resizable comment-pane width, persisted for the session so it survives tab
    // switches. The gutter's left-edge drag handle reports the new (clamped) width here.
    const [gutterWidth, setGutterWidth] = React.useState<number>(() => {
      // UAT round-4 #7 — DEFAULT the review-notes pane to its widest so cards sit closer to their
      // corresponding sections; a saved (user-dragged) width still wins.
      try {
        const saved = sessionStorage.getItem('spaarke.compose.commentGutterWidth');
        const n = saved ? parseInt(saved, 10) : Number.NaN;
        return Number.isFinite(n) ? n : MAX_COMMENT_GUTTER_WIDTH_PX;
      } catch {
        return MAX_COMMENT_GUTTER_WIDTH_PX;
      }
    });
    const handleGutterWidthChange = React.useCallback((w: number): void => {
      setGutterWidth(w);
      try {
        sessionStorage.setItem('spaarke.compose.commentGutterWidth', String(w));
      } catch {
        // sessionStorage unavailable (private mode) — width still applies for this session.
      }
    }, []);
    // UAT round-3 D3 — fetch the full standard-clause text behind a review comment's standardRef on
    // demand (GET /api/ai/nda-standard/clauses/{ref}). Only wired when a bffBaseUrl is present; a
    // standalone/library mount without a backend renders standardRef as plain text.
    const resolveStandardText = React.useCallback(
      async (standardRef: string): Promise<string | null> => {
        if (!bffBaseUrl) return null;
        try {
          const url = `${bffBaseUrl}/api/ai/nda-standard/clauses/${encodeURIComponent(standardRef)}`;
          const res = await authenticatedFetch(url, { method: 'GET' });
          const data = (await res.json()) as { text?: string };
          return typeof data.text === 'string' ? data.text : null;
        } catch {
          // authenticatedFetch throws ApiError on non-2xx (e.g. an unknown ref → 404) — treat as
          // "unavailable" so the popover shows a graceful message rather than crashing the gutter.
          return null;
        }
      },
      [bffBaseUrl]
    );

    // ----- Task 051 — FR-25 imported Word comments, seeded into the FR-23 thread panel --------------
    // PURE grouping (no editor dependency) so the threads are ready for `ComposeCommentThread`'s
    // `initialThreads` prop at ITS OWN first render — before the docx-mount effect below (which applies
    // the visual anchor mark) has had a chance to run. See importedComments.ts file header for the
    // two-concerns split. Recomputed only when the host supplies a new `importedComments` reference
    // (set atomically with `docxBytes` on a fresh mount, per the prop's own contract).
    const initialCommentThreads = React.useMemo(() => groupImportedComments(importedComments), [importedComments]);

    // Item 5b (UAT round-4, FR-23): the comment-thread panel owns its thread state (survives
    // open/close). To PERSIST panel comments on save, the panel reports its live threads up via
    // `onThreadsChanged` into this ref; the imperative `getAnchoredComments()` (task 040) maps the
    // SESSION-authored ones (excluding imported threads, which ride the retained original) to
    // `w:comment`-baking `ComposeAnchoredComment`s. A ref (not state) keeps this off the render path
    // — save reads the latest value imperatively. The imported-id set is derived from `initialCommentThreads`.
    const commentThreadsRef = React.useRef<readonly ComposeCommentThreadModel[]>(initialCommentThreads);
    const handleCommentThreadsChanged = React.useCallback((threads: readonly ComposeCommentThreadModel[]): void => {
      commentThreadsRef.current = threads;
    }, []);

    // ----- FIX #9 — hidden-scrollbar editor surface + "scroll for more" FAB ----
    // The editor scroll region hides its native scrollbar (see `editorSurface`
    // style: `scrollbarWidth: none` + `::-webkit-scrollbar { display: none }`)
    // while staying scrollable (`overflow: auto`). To keep the "there is more
    // below" affordance a docx editor needs, a floating circular down-arrow button
    // appears at the bottom whenever the surface is NOT scrolled to the end; it
    // scrolls the content down one viewport-ish on click. (This is the
    // progressive-scroll interpretation of the UAT "lazy load" note — a docx
    // editor loads its whole document into ProseMirror; there is no paged data to
    // lazily fetch, so the affordance is a scroll cue, not a data pager.)
    const editorScrollRef = React.useRef<HTMLDivElement | null>(null);
    const [showScrollDown, setShowScrollDown] = React.useState<boolean>(false);

    const scrollEditorDown = React.useCallback((): void => {
      const el = editorScrollRef.current;
      if (!el) return;
      el.scrollBy({ top: Math.round(el.clientHeight * 0.8), behavior: 'smooth' });
    }, []);

    // ----- DEF-12 — per-change on-click accept/reject affordance ------------
    // The cramped fixed `compose-redline-controls` bar (scroll-hidden, no reason-wrap) was REMOVED
    // as the primary control — that role moved to the Assistant confirmation message. Per-change
    // granularity stays: clicking a redline span (`<span data-compose-mark data-ledger-ref>`, the
    // FR-15 marks) opens a small popover at the click point with Accept / Reject for THAT change,
    // wired to the same `usePendingRedline.accept/reject` handlers the bar used. The visual redline
    // marks themselves are untouched.
    const [redlineClickAnchor, setRedlineClickAnchor] = React.useState<{
      x: number;
      y: number;
      ledgerRef: string;
    } | null>(null);
    const redlinePopoverRef = React.useRef<HTMLDivElement | null>(null);

    // Dismiss the context-menu popup on an outside click or Escape. Mirrors
    // the standard "click-away" pattern; the BubbleMenu's own dismissal
    // (blur/click-outside) is unaffected — this effect only governs the
    // separate point-insertion popup.
    React.useEffect(() => {
      if (!contextMenuAnchor) return;
      const handlePointerDown = (event: MouseEvent): void => {
        if (contextMenuRef.current && !contextMenuRef.current.contains(event.target as Node)) {
          setContextMenuAnchor(null);
        }
      };
      const handleKeyDown = (event: KeyboardEvent): void => {
        if (event.key === 'Escape') setContextMenuAnchor(null);
      };
      document.addEventListener('mousedown', handlePointerDown);
      document.addEventListener('keydown', handleKeyDown);
      return () => {
        document.removeEventListener('mousedown', handlePointerDown);
        document.removeEventListener('keydown', handleKeyDown);
      };
    }, [contextMenuAnchor]);

    // DEF-12 — dismiss the per-change redline popover on an outside click / Escape (mirrors the
    // context-menu dismissal above; the two popups never coexist meaningfully).
    React.useEffect(() => {
      if (!redlineClickAnchor) return;
      const handlePointerDown = (event: MouseEvent): void => {
        if (redlinePopoverRef.current && !redlinePopoverRef.current.contains(event.target as Node)) {
          setRedlineClickAnchor(null);
        }
      };
      const handleKeyDown = (event: KeyboardEvent): void => {
        if (event.key === 'Escape') setRedlineClickAnchor(null);
      };
      document.addEventListener('mousedown', handlePointerDown);
      document.addEventListener('keydown', handleKeyDown);
      return () => {
        document.removeEventListener('mousedown', handlePointerDown);
        document.removeEventListener('keydown', handleKeyDown);
      };
    }, [redlineClickAnchor]);

    // ----- TipTap editor instance -----------------------------------------
    const editor = useEditor({
      // LOCKED Spike #1 set + the ADDITIVE R2 custom marks (task 031) + the R3 paraId identity
      // extension (task 011) — the locked list itself is unchanged (spread, not mutated), honoring
      // the "do not touch the locked list" constraint.
      extensions: [
        ...LOCKED_EXTENSIONS,
        ...COMPOSE_R2_MARKS,
        ...COMPOSE_R2_QA_HIGHLIGHT,
        ...COMPOSE_NDA_SELECTED_COMMENT,
        ...COMPOSE_R3_PARAID,
        ...COMPOSE_R3_FIND_REPLACE,
        ...COMPOSE_R3_STYLES,
        ...COMPOSE_INDENT,
        ...COMPOSE_NUMBER_ATOM,
        ...COMPOSE_R4_OPAQUE_ATOMS,
        opLogExtension, // R4 FR-03/FR-06 (task 020/022/032) — the WIRED rebased op-log (supersedes the bare
        // COMPOSE_R4_STEP_INTERCEPTOR registration; supplies the classifier callbacks + feeds
        // the log `serializeOperationLog()` sends on save). Read-only step→operation capture.
        trackChangesExtension, // Item 4 — live Track Changes decoration overlay (additive, view-only)
      ],
      content: '<p></p>',
      // editorProps to apply Fluent v9 inherited foreground; semantic-token
      // styling on `.ProseMirror` lives in useStyles above.
      editorProps: {
        attributes: {
          // role: textbox + aria-multiline for accessibility (Fluent v9 input
          // contract parity).
          role: 'textbox',
          'aria-multiline': 'true',
        },
        // Task 111 requirement 2 — suppress the browser's native context menu
        // inside the Compose editor region and open the AI toolbar at the
        // click point instead. `handleDOMEvents` is ProseMirror's supported
        // raw-DOM-event seam (fires for every native event on the editor's
        // DOM, scoped to that element); returning `true` tells ProseMirror the
        // event was handled. Works for a collapsed selection (caret) too —
        // this handler doesn't touch selection at all, it only opens the
        // popup at (clientX, clientY).
        //
        // DUAL-POPUP DEDUPE (task 111 requirement 3): the selection BubbleMenu
        // already shows the AI toolbar whenever there is a NON-EMPTY selection.
        // A right-click at that moment must NOT open a second, redundant popup.
        // We always `preventDefault` (suppress the browser's native menu) but
        // only open the right-click popup when the selection is COLLAPSED
        // (`from === to`) — i.e. the BubbleMenu is not showing. With a live
        // selection we leave the BubbleMenu as the sole popup.
        handleDOMEvents: {
          contextmenu: (view, event) => {
            event.preventDefault();
            const { from, to } = view.state.selection;
            if (from === to) {
              setContextMenuAnchor({ x: event.clientX, y: event.clientY });
            }
            return true;
          },
          // DEF-12 — per-change on-click accept/reject. A left-click that lands on a redline mark
          // span (identified by its `data-ledger-ref` provenance attribute) opens the small
          // accept/reject popover for THAT change. Clicks elsewhere fall through unchanged (normal
          // caret placement / editing). Read the ledgerRef off the DOM target's nearest mark span —
          // the most robust way to map a click to a specific pending redline.
          click: (_view, event) => {
            const target = event.target as HTMLElement | null;
            const span = target?.closest?.('[data-compose-mark][data-ledger-ref]') as HTMLElement | null;
            const ledgerRef = span?.getAttribute('data-ledger-ref') ?? '';
            if (ledgerRef) {
              setRedlineClickAnchor({ x: event.clientX, y: event.clientY, ledgerRef });
            }
            // UAT round-4 #8 — a click on a highlighted advisory clause SELECTS that thread (the linked
            // gutter card turns gray; this anchor turns yellow). A click that lands on NO advisory
            // anchor deselects (the card returns to its base state) — so the selection follows where the
            // reviewer is looking. Uses the anchor mark's `data-comment-id` provenance attribute.
            const anchor = target?.closest?.('[data-compose-mark="comment-anchor"]') as HTMLElement | null;
            const commentId = anchor?.getAttribute('data-comment-id') ?? '';
            setSelectedThreadId(commentId || null);
            // Return false: never swallow the click — the caret still moves and normal editing works.
            return false;
          },
        },
        // Task 040 (FR-17) — Ctrl/Cmd+F opens the find/replace panel INSTEAD of the browser's native
        // page-find (which would search the whole page's rendered text, not just this document, and
        // cannot see ProseMirror decorations). Escape while the panel is open closes it; the panel
        // itself also handles Escape when focus is inside one of its fields (ComposeFindReplace.tsx) —
        // this top-level handler is the fallback for when focus is still in the editor body.
        handleKeyDown: (_view, event) => {
          const isFindShortcut = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'f';
          if (isFindShortcut) {
            event.preventDefault();
            setFindReplaceOpen(true);
            return true;
          }
          if (event.key === 'Escape' && findReplaceOpen) {
            setFindReplaceOpen(false);
            return true;
          }
          return false;
        },
      },
      onUpdate: () => {
        if (!dirtyRef.current) {
          dirtyRef.current = true;
          onDirtyChange?.(true);
        }
      },
    });

    // ----- DOCX import on docxBytes change --------------------------------
    React.useEffect(() => {
      if (!editor) return;
      if (!docxBytes) {
        // Any prior reference-only / projection-unavailable state is cleared when we leave the docx path.
        setReferenceOnly(null);
        setProjectionUnavailable(null);
        // DEF-08: an AI-drafted full-document seed sets the editor content DIRECTLY from HTML
        // (no docx decode) — the draft body IS the editor content. A draft is a transient working
        // draft (never yet saved), so report dirty=true to the workspace (create-on-save first Save
        // must be reachable), while the editor's OWN dirtyRef stays clean (an untouched seed still
        // Saves the pristine draft). Mirrors the transient-mount dirty semantics below.
        if (initialHtml && initialHtml.length > 0) {
          editor.commands.setContent(initialHtml);
          // Born-in-editor seed: capture a snapshot so an edit-then-save can still diff (though a born-in-
          // editor save normally sends the full content model, not edited-paragraph deltas).
          paraIdSnapshotRef.current = captureParaIdSnapshot(editor);
          opLogRef.current?.reset(); // R4 FR-06 (task 032): the seed is not a user edit — start the log empty.
          dirtyRef.current = false;
          onDirtyChange?.(true);
          return;
        }
        // Reset to empty paragraph if cleared.
        editor.commands.setContent('<p></p>');
        paraIdSnapshotRef.current = captureParaIdSnapshot(editor);
        opLogRef.current?.reset();
        dirtyRef.current = false;
        onDirtyChange?.(false);
        return;
      }
      // Wave 6 (DEF-G): a NON-DOCX buffer (e.g. a chat-uploaded PDF reaching the
      // editor via Wave-3 "Open in Compose") cannot be edited. Detect it from the
      // byte signature BEFORE attempting to mount and render an explicit
      // reference-only state. The editable DOCX path below is unchanged. Nothing
      // is editable here, so report dirty=false (no create-on-save for a
      // reference-only file).
      if (!isEditableDocx(docxBytes, documentRef?.fileName)) {
        editor.commands.setContent('<p></p>');
        dirtyRef.current = false;
        setReferenceOnly({ fileName: documentRef?.fileName });
        setProjectionUnavailable(null);
        onDirtyChange?.(false);
        return;
      }
      // A valid DOCX cleared any prior reference-only / projection-unavailable state (e.g. a
      // subsequent real .docx mount replacing a non-docx one, or a retry that succeeded).
      setReferenceOnly(null);
      setProjectionUnavailable(null);
      // gap 1.6 / DEF-01: a TRANSIENT (Browse/Upload) mount has no SPE pointer yet (empty
      // speDriveItemId). Its create-on-save first Save must be reachable, so we report
      // dirty=true to the workspace (there IS unsaved work — the draft has never been
      // persisted). The editor's OWN dirtyRef stays FALSE below so an *untouched* transient
      // Save still persists the pristine ORIGINAL bytes byte-identical (FR-06a, task 015) —
      // triggerSave keys the byte-branch off `editorRef.current.isDirty()` (= dirtyRef), NOT
      // the workspace-facing onDirtyChange signal. A stored (non-transient) load reports clean.
      const isTransientMount = !documentRef?.speDriveItemId;

      // Phase-1 mammoth removal COMPLETE (task 013, F-2 "one reader"): the host ALWAYS supplies a
      // SERVER PROJECTION for every entry path (Load, Upload, Browse, open-in-Compose) — mount its
      // paraId-tagged HTML DIRECTLY. The paraId extension parses `data-paraid` on setContent, so
      // there is NO client-side docx conversion and NO position-based `stampParaIds` (the two-engine
      // drift that caused the recurring save-abort bug class). If `projection` is null here, there is
      // NO second reader to fall back to (see the `else` branch below) — the editor surfaces an
      // explicit error/unavailable state instead of a silent blank editor.
      if (projection) {
        // Fail-closed (design §4 / GPT §11): a failed or non-editable projection MUST NOT mount a blank
        // editable doc over a non-empty retained baseline — render the reference-only "Open in Word" state.
        if (!projection.canEdit || projection.status === 'failed') {
          editor.commands.setContent('<p></p>');
          opLogRef.current?.reset(); // R4 FR-06 (task 032): no user edits on a reference-only mount.
          dirtyRef.current = false;
          setReferenceOnly({ fileName: documentRef?.fileName });
          setProjectionUnavailable(null);
          onDirtyChange?.(false);
          return;
        }
        setProjectionUnavailable(null);
        editor.commands.setContent(projection.html);
        // paraIds arrive in the HTML (data-paraid) — NO stampParaIds. Overlays + snapshot apply
        // AFTER setContent, BEFORE the snapshot, addToHistory:false inside the helpers.
        const projectionRevisionResult = applyImportedRevisions(editor, importedRevisions);
        applyImportedCommentAnchors(editor, importedComments);
        // FR-10 / I-7 (task 053): a revision whose paraId is unresolvable (Word regenerated it on an
        // external save AND the fuzzy fallback also missed) MUST surface for review, never be silently
        // dropped. Appended AFTER the resolved revisions/comments are placed (never during — an earlier
        // placeholder must not pollute a later revision's fuzzy-anchor search) and BEFORE the paraId
        // snapshot/op-log reset below, so it folds into the load-time baseline like every other import mark.
        renderUnresolvedRevisionPlaceholders(editor, projectionRevisionResult.unresolvedItems);
        paraIdSnapshotRef.current = captureParaIdSnapshot(editor);
        // R4 FR-06 (task 032): drop any ops the setContent/import transactions produced — the load is NOT a
        // user edit; the op-log must start empty, aligned to this load-time reject-state baseline.
        opLogRef.current?.reset();
        dirtyRef.current = false; // fresh load: editor's internal dirty flag is clean (FR-06a)
        onDirtyChange?.(isTransientMount);
        // Surface Partial fidelity gaps via the existing banner (codes only — no document content, ADR-015).
        const projectionImportWarnings: Array<{ type: string; message: string }> = [];
        if (projection.status === 'partial' && projection.warnings.length > 0) {
          projectionImportWarnings.push({
            type: 'warning',
            message: 'Some formatting may not display fully in Compose — open in Word to review the complete document.',
          });
        }
        if (projectionRevisionResult.unresolvedItems.length > 0) {
          projectionImportWarnings.push({
            type: 'warning',
            message: `${projectionRevisionResult.unresolvedItems.length} imported revision(s) could not be automatically placed — see the review marker(s) at the end of the document.`,
          });
        }
        if (projectionImportWarnings.length > 0) {
          onImportWarnings?.(projectionImportWarnings);
        }
        return;
      }

      // Task 013 (F-2 "one reader"): `projection` is null here — the client mammoth fallback
      // reader has been DELETED, so there is no second docx→editor reader left to try. This is a
      // valid, editable DOCX (the `isEditableDocx` gate above already passed) whose server
      // projection round-trip failed, was unreachable (BFF down / network error), or was never
      // attempted (an older host build). Surface a clear, explicit error/unavailable state — NEVER
      // a silent blank `<p></p>` editor and NEVER a second client-side parser. The document remains
      // available to the Assistant for reference (same "Open in Word" escape hatch the reference-only
      // state offers), and Track Changes/redline features are moot since nothing mounted.
      editor.commands.setContent('<p></p>');
      opLogRef.current?.reset();
      dirtyRef.current = false;
      setReferenceOnly(null);
      setProjectionUnavailable({ fileName: documentRef?.fileName });
      onDirtyChange?.(false);
      // `documentRef?.speDriveItemId` is read (transient-vs-stored, `isTransientMount` above) but
      // intentionally NOT a dep: the effect must re-run ONLY on a new `docxBytes` mount. Adding it
      // would re-run on save-success (when speDriveItemId gets populated on the same bytes) and
      // clobber the user's edits by re-mounting the original mount bytes. The captured value is
      // correct because `mountTransient` sets docxBytes + documentRef atomically in one render.
      // `paraIdMap` / `importedRevisions` / `importedComments` are likewise read-but-not-a-dep for the
      // same reason: the host sets them atomically with `docxBytes` per mount, so the captured value
      // is correct for THESE bytes; adding them as deps would risk a re-mount (edit clobber) on an
      // identity change. `projection` follows the identical mount contract for the same reason.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [editor, docxBytes, initialHtml, onDirtyChange, onImportWarnings]);

    // ----- Selection dispatch (heartbeat hoisted to ComposeWorkspace) -----
    useSelectionEventDispatch(editor, documentRef, sessionId, dispatch);

    // ----- FR-16 pending-redline materialization (task 033) ---------------
    // Owns materialize-from-ledger → FR-15 marks + accept/reject + supersession.
    const redline = usePendingRedline(editor);

    // ----- FR-14 (task 031) — anti-rubber-stamp accept-all gating ----------
    // "Accept all" MUST NOT include low-band edits without an explicit confirmation step (design
    // §6.2). Splitting the pending set here — rather than inside usePendingRedline's accept/reject
    // (the mark/range apply layer, left untouched) — keeps the gating a UI/selection-layer concern:
    // `acceptAllEligible` is what the primary "Accept all" button commits; `lowBandPending` is NEVER
    // touched by it and only moves on the SEPARATE, always-visible "include low-confidence" action.
    const acceptAllEligible = React.useMemo(
      () => redline.pending.filter(p => p.confidenceBand !== 'low'),
      [redline.pending]
    );
    const lowBandPending = React.useMemo(
      () => redline.pending.filter(p => p.confidenceBand === 'low'),
      [redline.pending]
    );
    const handleAcceptAllExcludingLowBand = React.useCallback(() => {
      for (const p of acceptAllEligible) redline.accept(p.ledgerRef);
    }, [acceptAllEligible, redline]);
    const handleIncludeLowBandInAcceptAll = React.useCallback(() => {
      // The deliberate SECOND action (design §6.2) — never bundled into handleAcceptAllExcludingLowBand.
      for (const p of lowBandPending) redline.accept(p.ledgerRef);
    }, [lowBandPending, redline]);

    // ----- FR-35 Doc Q&A ephemeral highlight (task 072, stretch) -----------
    const qaHighlight = useDocQaHighlight(editor);

    // ----- NDA-REVIEW advisory comments (ai-advanced-capabilities-nda-r1 task 031) -----------
    // A DEDICATED useComposeCommentThreads instance (author = the configurable `advisoryCommentAuthor`
    // prop, task 052 — default 'AI Advisory Review' preserves the pre-existing hardcoded behavior),
    // separate from ComposeCommentThread's own panel instance below — see placeAdvisoryComments'
    // JSDoc on ComposeEditorHandle for why the two stay independent.
    const advisoryComments = useComposeCommentThreads(editor, advisoryCommentAuthor);

    // ----- UAT round-4 #8 — bidirectional linked highlight (doc anchor ↔ gutter card) --------------
    // A single "selected advisory thread" id, shared between the in-document anchor (turns yellow via
    // SelectedCommentExtension) and the right-rail gutter card (turns gray). Set from EITHER side: a
    // click on a gutter card (`selectThread`) or on a highlighted clause in the document (the editor
    // `click` DOM handler below). `null` = nothing selected (every anchor stays base light-blue).
    const [selectedThreadId, setSelectedThreadId] = React.useState<string | null>(null);
    // Mirror the current selection into a ref so the stable `selectThread` callback can toggle without
    // re-subscribing (avoids churning the gutter's `onSelectThread` identity every selection change).
    const selectedThreadIdRef = React.useRef<string | null>(null);
    React.useEffect(() => {
      selectedThreadIdRef.current = selectedThreadId;
    }, [selectedThreadId]);

    // Push the selection into the ProseMirror decoration plugin whenever it changes (the plugin paints
    // the selected clause yellow; clearing repaints it base blue). A no-op meta dispatch is cheap and
    // keeps React state as the single source of truth.
    React.useEffect(() => {
      if (!editor) return;
      editor.view.dispatch(
        editor.state.tr.setMeta(
          selectedCommentPluginKey,
          selectedThreadId ? { type: 'select', commentId: selectedThreadId } : { type: 'clear' }
        )
      );
    }, [editor, selectedThreadId]);

    // Gutter-card click → TOGGLE selection (re-clicking the selected card deselects; clicking another
    // switches). On SELECT, scroll the document to the clause so the linked yellow anchor is in view —
    // reusing the coordsAtPos ancestor-scroll technique useDocQaHighlight/the gutter already use.
    const selectThread = React.useCallback(
      (threadId: string): void => {
        const willSelect = selectedThreadIdRef.current !== threadId;
        setSelectedThreadId(willSelect ? threadId : null);
        if (!willSelect || !editor) return;
        const span = findCommentAnchorRange(editor.state.doc, threadId);
        const scroller = editorScrollRef.current;
        if (!span || !scroller) return;
        try {
          const coords = editor.view.coordsAtPos(span.from);
          const rect = scroller.getBoundingClientRect();
          const target = scroller.scrollTop + (coords.top - rect.top) - rect.height / 3;
          scroller.scrollTo({ top: Math.max(0, target), behavior: 'smooth' });
        } catch {
          // coordsAtPos measures real DOM layout and can throw in a detached/not-yet-painted view —
          // selection still applied above; the next paint shows the highlight without the scroll.
        }
      },
      [editor]
    );

    // ----- UAT round-5 #1 — Review Summary hosted INSIDE the editor -----------------------------
    // Enrich each finding with a doc-derived location label (section heading + ordinal, which the
    // model's sectionRef lacks — clauseLocation.ts) by strict-resolving its quotedText to a document
    // position, then walking to the governing heading. Recomputed only when the finding set changes
    // (a snapshot at review time — a later edit that shifts headings does not re-label, which is fine
    // for an advisory digest). Falls back to the model-only label when a quote can't be resolved.
    const reviewFindings = reviewSummary?.findings;
    const enrichedReviewFindings = React.useMemo((): readonly NdaReviewFindingSummary[] => {
      if (!reviewFindings || reviewFindings.length === 0) return reviewFindings ?? [];
      if (!editor) return reviewFindings;
      return reviewFindings.map(finding => {
        let pos: number | null = null;
        if (finding.quotedText) {
          const resolved = resolveTargetSpans(editor, finding.quotedText, 'strict');
          if (resolved.ok) pos = resolved.spans[0].from;
        }
        return {
          ...finding,
          locationLabel: deriveClauseLocationLabel(editor.state.doc, pos, finding.sectionRef),
          // UAT round-8 #2 — the resolved document position drives the summary's "by section" sort.
          docPosition: pos ?? undefined,
        };
      });
      // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional review-time snapshot (see note)
    }, [editor, reviewFindings]);

    // Summary-row navigation: reuse the editor's own cited-span primitive (strict resolve + ephemeral
    // highlight + scrollIntoView), the SAME anchoring the gutter comment uses — no host round-trip.
    const handleReviewNavigate = React.useCallback(
      (finding: NdaReviewFindingSummary): void => {
        if (finding.quotedText) qaHighlight.highlight(finding.quotedText, finding.sectionRef);
      },
      [qaHighlight]
    );

    // ----- UAT round-8 #3/#4/#5 — per-Review-Note AI edit tools (the gutter ⋮ menu) --------------
    // The tools are the binding-wired compose EDIT actions (materializesInEditor) from the shared
    // AI-toolbar registry — "Draft compliant alternative" today; the list grows automatically as more
    // edit bindings are seeded (extensibility #5). Reactive to registry changes.
    const readNoteTools = React.useCallback(
      () =>
        // Contextual AI Tool Library: the review-note ⋮ menu draws from the SAME registry as the
        // BubbleMenu — a tool appears here because its definition declares `surfaces ∋ 'review-note'`
        // (round-8 #4: one Draft-alternative definition, two surfaces). `bindingId` filters out
        // still-stubbed tools; the label may be surface-overridden (NOTE_TOOL_SURFACE_LABELS).
        getToolsForSurface('review-note', activeWorkType || NOTE_TOOL_FALLBACK_WORKTYPE)
          .filter(a => a.bindingId)
          .map(a => ({ id: a.id, label: NOTE_TOOL_SURFACE_LABELS[a.id] ?? a.label })),
      [activeWorkType]
    );
    const [noteTools, setNoteTools] = React.useState(readNoteTools);
    React.useEffect(() => {
      setNoteTools(readNoteTools());
      return subscribeComposeAiToolbarActions(() => setNoteTools(readNoteTools()));
    }, [readNoteTools]);
    const noteToolSeqRef = React.useRef(0);

    // ----- Contextual AI Tool Library (phase 3) — free-text instruction dialog -------------
    // ONE shared dialog serves BOTH the BubbleMenu (ComposeAiToolbar via `onRequestInstruction`)
    // and the Review-Note ⋮ menu (runNoteTool). A tool that declares `inputPrompt` (e.g. "Describe
    // a change…") opens this dialog to collect the user's free-text `instruction` before dispatch;
    // it resolves to the entered text, or `null` if cancelled.
    const [instructionPrompt, setInstructionPrompt] = React.useState<{
      open: boolean;
      action: ComposeAiToolbarAction | null;
      value: string;
    }>({ open: false, action: null, value: '' });
    const instructionResolveRef = React.useRef<((v: string | null) => void) | null>(null);

    const promptForInstruction = React.useCallback(
      (action: ComposeAiToolbarAction): Promise<string | null> =>
        new Promise<string | null>(resolve => {
          instructionResolveRef.current = resolve;
          setInstructionPrompt({ open: true, action, value: '' });
        }),
      []
    );
    const settleInstruction = React.useCallback((result: string | null): void => {
      setInstructionPrompt(prev => ({ ...prev, open: false }));
      const resolve = instructionResolveRef.current;
      instructionResolveRef.current = null;
      resolve?.(result);
    }, []);

    // Run a note tool: dispatch the compose EDIT action against the NOTE's live clause span — the SAME
    // slot shape `ComposeAiToolbar.handleActionClick` builds for a selection — routed to the document
    // session so the result materializes as an inline redline (DEF-09) and the Assistant confirms with
    // the model's rationale (round-8 #7).
    const runNoteTool = React.useCallback(
      async (threadId: string, toolId: string): Promise<void> => {
        if (!editor || !enqueueComposeAction || !sessionId) return;
        const action = getComposeAiToolbarActions().find(a => a.id === toolId);
        // Guard: must be a wired tool that declares the review-note surface (Contextual AI Tool Library).
        if (!action?.bindingId || !(action.surfaces ?? ['selection']).includes('review-note')) return;
        // Free-text tool (inputPrompt): collect the instruction BEFORE resolving the span/dispatch.
        let instruction: string | undefined;
        if (action.inputPrompt) {
          const entered = await promptForInstruction(action);
          if (!entered || !entered.trim()) return;
          instruction = entered.trim();
        }
        const span = findCommentAnchorRange(editor.state.doc, threadId);
        if (!span) return;
        const rawText = editor.state.doc.textBetween(span.from, span.to, ' ');
        const selectionText = rawText.length > 16000 ? rawText.slice(0, 16000) : rawText;
        void enqueueComposeAction({
          id: `${action.id}#note-${threadId}#${(noteToolSeqRef.current += 1)}`,
          bindingId: action.bindingId,
          args: {
            slots: {
              selectionText,
              selectionAnchorStart: span.from,
              selectionAnchorEnd: span.to,
              documentSpeId: documentRef?.speDriveItemId,
              documentRecordId: documentRef?.sprkDocumentId,
              sessionId,
              // Contextual AI Tool Library (phase 3): the free-text instruction for an inputPrompt tool.
              ...(instruction ? { instruction } : {}),
            },
          },
          // Every note tool is an in-editor EDIT action, so ALWAYS route to the document session (DEF-09)
          // so the result materializes as an inline redline — independent of the registry's
          // `materializesInEditor` flag (which the catalog seed may not preserve).
          documentSessionId: sessionId,
        }).catch(() => undefined);
      },
      [editor, enqueueComposeAction, sessionId, documentRef, promptForInstruction]
    );

    // ----- Task 044 — FR-23 "Comments" panel toggle -------------------------
    // Toggling OPEN captures the editor's live selection at click time (see the state declaration
    // above); toggling CLOSED just hides the panel — thread state persists (ComposeCommentThread
    // stays mounted, its `open` prop only gates its own render, mirroring ComposeFindReplace).
    const handleToggleComments = React.useCallback((): void => {
      setCommentsOpen(prevOpen => {
        const next = !prevOpen;
        if (next && editor) {
          const { from, to } = editor.state.selection;
          setPendingCommentRange(
            from === to ? null : { from, to, preview: editor.state.doc.textBetween(from, to, ' ') }
          );
        }
        return next;
      });
    }, [editor]);

    // Item 4 (UAT round-4): flip the Track Changes overlay. The enabled flag lives in BOTH React
    // state (drives the toolbar's pressed style) AND the plugin state (drives `decorations`), kept in
    // sync here — the toggle dispatches a transaction meta so ProseMirror re-runs `decorations`
    // immediately (a ref change alone would not repaint).
    const toggleTrackChanges = React.useCallback((): void => {
      setTrackChangesEnabled(prev => {
        const next = !prev;
        if (editor) editor.view.dispatch(editor.state.tr.setMeta(trackChangesPluginKey, { enabled: next }));
        return next;
      });
    }, [editor]);

    // ----- FIX #9 — track whether the editor surface has more content below ---
    // Show the down-arrow FAB only when NOT scrolled to the bottom. Re-measure on
    // scroll, on content-size changes (ResizeObserver — guarded for jsdom), and on
    // editor transactions (typing/import grows the doc). A small epsilon avoids a
    // flickering button at the exact bottom.
    React.useEffect(() => {
      const el = editorScrollRef.current;
      if (!el) return;
      const measure = (): void => {
        const remaining = el.scrollHeight - el.scrollTop - el.clientHeight;
        setShowScrollDown(remaining > 8);
      };
      measure();
      el.addEventListener('scroll', measure, { passive: true });
      let ro: ResizeObserver | undefined;
      if (typeof ResizeObserver !== 'undefined') {
        ro = new ResizeObserver(measure);
        ro.observe(el);
      }
      if (editor) editor.on('transaction', measure);
      return () => {
        el.removeEventListener('scroll', measure);
        ro?.disconnect();
        if (editor) editor.off('transaction', measure);
      };
    }, [editor, referenceOnly, projectionUnavailable, isImporting]);

    // ----- Imperative handle ----------------------------------------------
    React.useImperativeHandle(
      ref,
      (): ComposeEditorHandle => ({
        // R4 FR-06 (task 032, the write-path cutover): the ordered, rebased task-003 operation-log snapshot
        // the host sends on a dirty save of a LOADED doc (the server applies it via ComposeShadowPatchEngine).
        // This is the ONLY dirty-save capture path (task 023 removed the retired paragraph-diff export
        // `collectEditedParagraphs`).
        // task 038 (zero-error guardrails): NON-DESTRUCTIVE — reads the log WITHOUT resetting it or clearing
        // the dirty flag. It records the current high-water mark so `commitSaved()` can later drop exactly
        // this batch (after a confirmed 200) while preserving concurrent edits. A rejected save leaves the
        // log + dirty intact so a retry re-sends the same edits (no batch loss).
        serializeOperationLog: () => {
          if (!editor) {
            throw new Error('ComposeEditor: cannot serialize operation log — editor not mounted');
          }
          committedBoundaryRef.current = opLogRef.current!.nextSeq;
          return opLogRef.current!.serialize(editor.state.doc);
        },
        // task 038 (zero-error guardrails): clear the just-persisted op-log batch + recompute the dirty flag
        // AFTER the save POST confirmed (200). Preserves any edits appended during the in-flight save; a
        // failed save never calls this, so the batch survives for a retry.
        commitSaved: () => {
          if (!opLogRef.current) return;
          opLogRef.current.commitSaved(committedBoundaryRef.current);
          const stillDirty = opLogRef.current.size > 0;
          dirtyRef.current = stillDirty;
          onDirtyChange?.(stillDirty);
        },
        // C2 fix (UAT 2026-07-20): the ordered load-time paraId map (from the snapshot) the host sends on
        // save so the server can stamp minted ids onto the baseline. Read-only — no dirty-flag reset.
        getBaselineParaIdMap: () => buildBaselineParaIdMap(paraIdSnapshotRef.current),
        // R3 FR-01a (task 027): the full content model for a born-in-editor save — the server renders it.
        buildContentModel: () => {
          if (!editor) {
            throw new Error('ComposeEditor: cannot build content model — editor not mounted');
          }
          const model = buildContentModel(editor);
          dirtyRef.current = false;
          onDirtyChange?.(false);
          return model;
        },
        // R3 FR-04 (task 027): pending AI redlines → native-markup annotations the server composes onto
        // the authored baseline (task 023). Does NOT reset dirty (separate from settled-text edits).
        getRedlineAnnotations: () =>
          editor ? redlineMarksToDocxAnnotations(editor.getJSON(), 'Spaarke Assistant', new Date().toISOString()) : [],
        hasPendingRedlines: () => redline.pending.length > 0,
        // Task 040 (comment-export wiring fix): BOTH the session Comments panel's own thread
        // instance (commentThreadsRef, excluding IMPORTED threads — they already ride the
        // retained-original baseline) AND the NDA-REVIEW advisory thread instance
        // (advisoryComments.threads) resolve their live commentAnchor mark span to a durable
        // (paraId, run-local range) — no text-search (I-7). The imported id set is the load-time
        // `initialCommentThreads` (seeded from the doc's own comments); advisory threads have no
        // imported counterpart, so nothing is excluded for that instance.
        getAnchoredComments: () => {
          if (!editor) return [];
          const importedIds = new Set(initialCommentThreads.map(t => t.id));
          return [
            ...composeSessionCommentThreadsToAnchoredComments(editor.state.doc, commentThreadsRef.current, importedIds),
            ...composeSessionCommentThreadsToAnchoredComments(editor.state.doc, advisoryComments.threads, new Set()),
          ];
        },
        getCounts: () => {
          if (!editor) return { characters: 0, words: 0 };
          // The CharacterCount extension hangs storage off editor.storage.
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const storage = (editor.storage as any).characterCount;
          return {
            characters: storage?.characters?.() ?? 0,
            words: storage?.words?.() ?? 0,
          };
        },
        isDirty: () => dirtyRef.current,
        // FR-04 seam (task 016) now delegates to the FR-16 redline path (task 033):
        // the stored ledger draft renders as a PENDING redline, not a committed
        // insertion. ComposeWorkspace's render-follows-store path calls this.
        materializeComposeDraft: (draft, provenance) => {
          redline.materialize(draft, provenance);
        },
        materializePendingRedline: (draft, provenance) => redline.materialize(draft, provenance),
        materializeComposeEdits: (edits, provenance) => redline.materializeMany(edits, provenance),
        acceptPendingRedline: ledgerRef => redline.accept(ledgerRef),
        rejectPendingRedline: ledgerRef => redline.reject(ledgerRef),
        highlightCitedSpan: (sourceText, sectionLabel) => qaHighlight.highlight(sourceText, sectionLabel),
        clearCitedHighlight: () => qaHighlight.clear(),
        // NDA-REVIEW advisory comments (ai-advanced-capabilities-nda-r1 task 031): resolve each
        // flagged clause via the SAME resolveTargetSpans('strict') anchoring highlightCitedSpan
        // uses, then createThread (the dedicated `advisoryComments` instance above) instead of an
        // ephemeral highlight. FR-19 "do not guess" — a failed resolution is reported, never
        // silently dropped.
        placeAdvisoryComments: items => {
          if (!editor) {
            return {
              placed: 0,
              failed: items.map(item => ({ targetText: item.targetText, kind: 'not_found' as const })),
            };
          }
          let placed = 0;
          const failed: AdvisoryCommentFailure[] = [];
          for (const item of items) {
            // UAT round-3 S1: strict-first, then graceful fallbacks (first-occurrence / verbatim prefix)
            // so a finding whose excerpt lightly diverges or recurs still highlights instead of being
            // dropped — see resolveAdvisoryAnchorSpan. A comment mis-anchor is non-destructive (unlike a
            // redline edit), so this is a safe relaxation of strict placement, scoped to advisory comments.
            const span = resolveAdvisoryAnchorSpan(editor, item.targetText);
            if (!span) {
              failed.push({ targetText: item.targetText, kind: 'not_found' });
              continue;
            }
            const threadId = advisoryComments.createThread(item.explanation, span, {
              riskLevel: item.riskLevel,
              sectionRef: item.sectionRef,
              standardRef: item.standardRef,
              // task 052 (FR-15): additive passthrough — undefined today until the upstream
              // bridge supplies them (see AdvisoryCommentInput's own JSDoc).
              flaggedClause: item.flaggedClause,
              assessment: item.assessment,
              standardText: item.standardText,
            });
            if (threadId) {
              placed += 1;
            } else {
              // createThread returns null only for a collapsed range/empty text — resolveTargetSpans
              // already guaranteed a non-collapsed span, so this is defensive, not expected.
              failed.push({ targetText: item.targetText, kind: 'not_found' });
            }
          }
          return { placed, failed };
        },
        // Read surface for task 040 (comment-export wiring) — see the handle JSDoc.
        getAdvisoryCommentThreads: () => advisoryComments.threads,
      }),
      // NOTE: `advisoryComments.createThread` (a `React.useCallback` memoized on `[editor, author]`
      // inside useComposeCommentThreads), NOT the whole `advisoryComments` object — that hook
      // returns a fresh object literal every render (unlike `redline`/`qaHighlight`, which memoize
      // via `React.useMemo`), so depending on it directly would rebuild this handle every render.
      // `advisoryComments.threads` (read by getAdvisoryCommentThreads) is intentionally included so
      // the handle refreshes when new threads are added — it changes only on an actual createThread
      // call (React state), not every render.
      [editor, redline, qaHighlight, advisoryComments.createThread, advisoryComments.threads]
    );

    // ----- Render ---------------------------------------------------------
    if (!editor) {
      return (
        <div className={styles.container}>
          <div className={styles.loadingState} role="status" aria-live="polite">
            <Spinner size="small" />
            <Text size={200}>Loading editor…</Text>
          </div>
        </div>
      );
    }

    // Wave 6 (DEF-G) — a non-docx file reached the editor: show an explicit,
    // calm reference-only surface INSTEAD of the editable ProseMirror surface
    // (never a silent empty `<p></p>`). Editable content is DOCX-only by design;
    // the file remains available to the Assistant for reference (summarize,
    // extract, Q&A). Semantic tokens only (ADR-021 dark-mode-correct).
    if (referenceOnly) {
      return (
        <div
          className={styles.container}
          role="region"
          aria-label={referenceOnly.fileName ?? 'Compose editor'}
          data-compose-editor-spe-id={documentRef?.speDriveItemId ?? ''}
        >
          <div className={styles.referenceOnly} role="status" data-testid="compose-reference-only">
            <DocumentProhibited24Regular className={styles.referenceOnlyIcon} aria-hidden="true" />
            <Text weight="semibold">
              {referenceOnly.fileName
                ? `“${referenceOnly.fileName}” can’t be edited in Compose`
                : 'This file can’t be edited in Compose'}
            </Text>
            <Text size={200} className={styles.referenceOnlyDetail}>
              Only Word (.docx) documents can be edited here. This file is available to the Assistant for reference —
              ask it to summarize, extract from, or answer questions about it.
            </Text>
          </div>
        </div>
      );
    }

    // Task 013 (F-2 "one reader") — a valid, editable DOCX reached the editor but the server
    // projection round-trip failed/was unreachable, and the client mammoth fallback reader has
    // been DELETED. Show an explicit, calm error/unavailable surface INSTEAD of a silent blank
    // editor and INSTEAD of a second client-side parser. The document remains available to the
    // Assistant for reference. Semantic tokens only (ADR-021 dark-mode-correct).
    if (projectionUnavailable) {
      return (
        <div
          className={styles.container}
          role="region"
          aria-label={projectionUnavailable.fileName ?? 'Compose editor'}
          data-compose-editor-spe-id={documentRef?.speDriveItemId ?? ''}
        >
          <div className={styles.referenceOnly} role="status" data-testid="compose-projection-unavailable">
            <ErrorCircle24Regular className={styles.projectionUnavailableIcon} aria-hidden="true" />
            <Text weight="semibold">
              {projectionUnavailable.fileName
                ? `Couldn’t prepare “${projectionUnavailable.fileName}” for editing`
                : 'Couldn’t prepare this document for editing'}
            </Text>
            <Text size={200} className={styles.referenceOnlyDetail}>
              Something went wrong preparing this document — try opening it again. This file is still available to the
              Assistant for reference — ask it to summarize, extract from, or answer questions about it.
            </Text>
          </div>
        </div>
      );
    }

    return (
      <div
        className={styles.container}
        role="region"
        aria-label={documentRef?.fileName ?? 'Compose editor'}
        data-compose-editor-document-id={documentRef?.sprkDocumentId ?? ''}
        data-compose-editor-spe-id={documentRef?.speDriveItemId ?? ''}
      >
        {/* Persistent top toolbar — headings/lists/blockquote/align/undo-redo.
            UNCHANGED by task 111 (bold/italic/underline/strike/link have no
            control here today — see ComposeFormatToolbar.tsx's own docstring;
            they are ONLY reachable via the selection-triggered AI-actions
            popup's host, the BubbleMenu below, which task 111 made AI-actions
            ONLY — this is a known, flagged trade-off, not a silent removal). */}
        <ComposeFormatToolbar
          editor={editor}
          disabled={isImporting}
          // task 037 (owner Path C — IMPORT-ONLY tables): a from-scratch born-in-editor draft
          // (blank / AI-draft via `initialHtml`) mounts with NO retained original (`docxBytes`
          // null, no server `projection`) — disable NEW table authoring there. A loaded/imported
          // doc (uploaded `.docx`, opened template, or stored-doc projection) keeps full tables.
          hasLoadedBaseline={docxBytes !== null || projection != null}
          onOpenInWord={onOpenInWord}
          onOpenInWordDesktop={onOpenInWordDesktop}
          wordActionsDisabled={wordActionsDisabled}
          onSave={onSave}
          canSave={canSave}
          isSaving={isSaving}
          onRefreshProfile={onRefreshProfile}
          onReloadFromSource={onReloadFromSource}
          onOpenDocument={onOpenDocument}
          isRefreshingProfile={isRefreshingProfile}
          trackChangesEnabled={trackChangesEnabled}
          onToggleTrackChanges={toggleTrackChanges}
          // UAT round-2 items #1/#2 — the "Review" dropdown. Shown only when an NDA advisory review is
          // present (in-document advisory threads OR summary findings the host reports). "Review Summary"
          // toggles the host's docked panel; "Review Notes" toggles the right-gutter cards (local state).
          hasReview={advisoryComments.threads.length > 0 || Boolean(reviewSummary?.hasFindings)}
          // UAT round-6 #4 — the not-legal-advice warning behind the toolbar's info button (moved out of
          // the Review Summary body); present only when an NDA advisory review is.
          reviewDisclaimer={
            advisoryComments.threads.length > 0 || reviewSummary?.hasFindings ? NDA_REVIEW_DISCLAIMER_TEXT : undefined
          }
          reviewSummaryOpen={reviewSummary?.open ?? false}
          onToggleReviewSummary={reviewSummary?.onToggle}
          reviewNotesOpen={reviewNotesVisible}
          onToggleReviewNotes={() => setReviewNotesVisible(v => !v)}
        />
        {/* task 038 (spaarkeai-compose-r4 zero-error guardrails) — non-blocking, dismissible notice for a
            deferred/unrepresentable/refused step (most importantly formatted or linked PASTE that slips
            past the disabled toolbar controls). Informs only; the representable edits still save. */}
        {/* UAT round-7 #8 — the "Some formatting … isn't saved yet" deferral banner is SUPPRESSED per
            reviewer request (it read as noise). The deferral BEHAVIOR is unchanged (unrepresentable
            edits are still deferred + the op-log still records them); only the banner no longer renders.
            `deferralNotice`/`setDeferralNotice` remain wired so it can be re-surfaced trivially later. */}
        {false && deferralNotice ? (
          <div className={styles.deferralNotice} role="status" aria-live="polite" data-testid="compose-deferral-notice">
            <Text size={200} className={styles.deferralNoticeText}>
              {deferralNotice}
            </Text>
            <Button
              appearance="subtle"
              size="small"
              icon={<Dismiss16Regular />}
              aria-label="Dismiss notice"
              onClick={() => setDeferralNotice(null)}
              data-testid="compose-deferral-notice-dismiss"
            />
          </div>
        ) : null}
        {/* UAT round-5 #1 — the Review Summary now lives HERE, inside the editor's top region (below the
            toolbar), replacing the former host-rendered docked panel. Toggling "Review Summary" in the
            toolbar's Review dropdown expands/collapses this in-flow area. The editor owns navigation
            (its own highlightCitedSpan primitive) and enriches each finding's location with the section
            heading/ordinal from the live document (clauseLocation.ts). */}
        {reviewSummary ? (
          <AgreementReviewSummaryPanel
            open={reviewSummary.open}
            onClose={reviewSummary.onToggle}
            findings={enrichedReviewFindings}
            placementFailureCount={reviewSummary.placementFailureCount}
            onNavigate={handleReviewNavigate}
          />
        ) : null}
        {/* ===================================================================
            FR-17 in-editor find/replace panel — task 040. Toggled by Ctrl/Cmd+F
            inside the editor (see editorProps.handleKeyDown above) and dismissed
            by Escape or its own close button; closing clears the search state +
            highlight decoration (never leaves a stale highlight behind).
            =================================================================== */}
        <ComposeFindReplace editor={editor} open={findReplaceOpen} onClose={() => setFindReplaceOpen(false)} />
        {/* ===================================================================
            FR-23 comment-thread panel — task 044. Toggled by the floating
            "Comments" button pinned to the editor scroll region (see below);
            dismissed by its own close button. `pendingCommentRange` is captured
            at toggle-open time (see handleToggleComments above) so the "new
            comment" composer anchors to the selection the user had BEFORE
            focus moved into this panel, not whatever the live selection is by
            the time they finish typing.
            =================================================================== */}
        <ComposeCommentThread
          editor={editor}
          open={commentsOpen}
          onClose={() => setCommentsOpen(false)}
          author={commentAuthor}
          pendingRange={pendingCommentRange}
          onThreadCreated={() => setPendingCommentRange(null)}
          onThreadsChanged={handleCommentThreadsChanged}
          initialThreads={initialCommentThreads}
          // G9 (FR-08, task 031): the editor scroll container so the pane scroll-tracks the document
          // (doc→pane) — the pane highlights + scrolls to the comment whose anchor is at the viewport top.
          scrollContainerRef={editorScrollRef}
        />
        {/* UAT round-4: the FR-22 styles pane mount was REMOVED per user request (the "Show styles"
            toggle above it is gone too). Component + hook retained, unmounted. */}
        {/* NOTE (UAT 2026-07-19 P1 fix): the AI-actions <BubbleMenu> was RELOCATED to be
            the LAST child of this container (see just before the container's closing tag
            below). Rationale: TipTap's BubbleMenu plugin calls `this.element.remove()` on
            mount, detaching its wrapper <div> from the DOM while React's fiber still records
            it as a live child. When ANY earlier conditional sibling (the Comments/Styles
            panes, the redline banners, the context-menu popup, the importing spinner)
            toggled null→<div>, React's getHostSibling resolved its insert-anchor to that
            detached node and called `container.insertBefore(newNode, detachedBubbleDiv)` —
            which throws "Failed to execute 'insertBefore' … not a child of this node" and
            trips the WidgetErrorBoundary ("Compose failed to load"). Making the BubbleMenu
            the LAST child means every conditional sibling anchors on `editorScrollWrap`
            (always mounted, never detached) instead. tippy positions the popup via popper
            relative to the selection, so its position in the React child order is purely
            structural and has no visual effect. */}

        {/* ===================================================================
            TASK 111 requirement 2 — right-click (context-menu) AI-toolbar
            trigger. Independent of the tippy-driven BubbleMenu above (see the
            `handleDOMEvents.contextmenu` hook in the `useEditor` call): a
            plain, React-state-positioned popup at the click point, open for
            BOTH a selection and a collapsed caret (`forceVisible`). Reuses
            `styles.bubbleMenu`'s visual treatment via `mergeClasses` so it's
            visually consistent with the selection-triggered popup.
            =================================================================== */}
        {contextMenuAnchor ? (
          <div
            ref={contextMenuRef}
            className={mergeClasses(styles.aiBubbleWrap, styles.contextMenuPopup)}
            style={{ left: contextMenuAnchor.x, top: contextMenuAnchor.y }}
            data-testid="compose-ai-context-menu"
          >
            <ComposeAiToolbar
              editor={editor}
              documentRef={documentRef}
              sessionId={sessionId}
              bffBaseUrl={bffBaseUrl}
              dispatch={dispatch}
              activeWorkType={activeWorkType}
              onRequestInstruction={promptForInstruction}
              enqueueComposeAction={enqueueComposeAction}
              forceVisible
            />
          </div>
        ) : null}

        {/* ===================================================================
            FR-35 Doc Q&A ephemeral highlight banner — task 072 (stretch).
            Renders ONLY while a cited answer's source span is highlighted
            (auto-clears after HIGHLIGHT_TTL_MS or on the next Q&A / clear).
            =================================================================== */}
        {qaHighlight.activeHighlight ? (
          <div className={styles.qaHighlightBanner} role="status" data-testid="compose-qa-highlight-banner">
            <Text size={200}>Found in {qaHighlight.activeHighlight.sectionLabel ?? 'this document'}</Text>
          </div>
        ) : null}

        {/* ===================================================================
            PENDING REDLINE affordances — task 033 (FR-16). Unresolved-target
            banner (FR-19 "do not guess") + per-suggestion accept/reject. Driven
            by usePendingRedline; semantic tokens only (ADR-021 dark-mode).
            =================================================================== */}
        {redline.error ? (
          <div className={styles.redlineError} role="status" data-testid="compose-redline-error">
            <Text size={200} className={styles.redlineErrorText}>
              {/* Item 1 (UAT round-4): a table-heavy / cross-extractor document can leave several
                  exact-but-cross-cell targets unplaceable. Prefer a CALM batched summary (N of M)
                  over an alarming single-edit "not found" — the document is fully usable regardless.
                  `ambiguous` keeps its actionable reselect guidance. */}
              {redline.error.kind === 'ambiguous'
                ? `This suggested edit matches ${redline.error.matchCount} places in the document. Select the exact passage and try again.`
                : (redline.error.failedCount ?? 0) > 1
                  ? `${redline.error.failedCount} of ${redline.error.totalCount} suggested edits couldn't be placed automatically — their wording differs slightly from this document. You can still review, edit, and save.`
                  : `A suggested edit couldn't be placed automatically — its wording differs slightly from this document. You can still edit and save.`}
            </Text>
            <Button
              size="small"
              appearance="subtle"
              icon={<Dismiss16Regular />}
              aria-label="Dismiss"
              onClick={redline.clearError}
            />
          </div>
        ) : null}
        {/* ===================================================================
            FR-14 (task 031) — pending-redlines summary bar: count + "Accept
            all" (built ONLY from acceptAllEligible — low-band items are never
            in that set, so this button structurally cannot include them) + the
            SEPARATE, always-explicit "include low-confidence" action (design
            §6.2 anti-rubber-stamp — a deliberate second click, never a silent
            inclusion). Semantic tokens only (ADR-021 dark-mode-correct).
            =================================================================== */}
        {redline.pending.length > 0 ? (
          <div className={styles.redlineSummaryBar} role="status" data-testid="compose-redline-summary">
            <Text size={200} className={styles.redlineSummaryText}>
              {redline.pending.length} suggested edit{redline.pending.length === 1 ? '' : 's'} pending
              {lowBandPending.length > 0 ? ` — ${lowBandPending.length} low-confidence, needs review` : ''}
            </Text>
            {acceptAllEligible.length > 0 ? (
              <Button
                size="small"
                appearance="primary"
                onClick={handleAcceptAllExcludingLowBand}
                data-testid="compose-redline-accept-all"
              >
                Accept all ({acceptAllEligible.length})
              </Button>
            ) : null}
            {lowBandPending.length > 0 ? (
              <Button
                size="small"
                appearance="subtle"
                onClick={handleIncludeLowBandInAcceptAll}
                data-testid="compose-redline-accept-all-include-low"
              >
                Also accept {lowBandPending.length} low-confidence edit{lowBandPending.length === 1 ? '' : 's'}
              </Button>
            ) : null}
          </div>
        ) : null}
        {/* ===================================================================
            DEF-12 — per-change on-click accept/reject POPOVER. Replaces the
            removed fixed `compose-redline-controls` bar (the primary control is
            now the Assistant confirmation message). Opens at the click point on
            a redline span; Accept/Reject route to the SAME
            usePendingRedline.accept/reject handlers, scoped to the clicked
            change's ledgerRef. Reuses `styles.bubbleMenu`'s dark-mode-correct
            treatment (semantic tokens; ADR-021).
            FR-14 (task 031) — RESTRUCTURED so the cited rationale is the
            visual HEADLINE (primary trust cue, design §6.2) and the derived
            `confidenceBand` renders as a SECONDARY coarse badge underneath —
            never a numeric/percentage score. A low-band redline additionally
            carries an explicit "Needs review" cue and its Accept button is
            demoted from `primary` to `secondary` appearance (never visually
            pushed as the default fast action) — the per-edit explicit-review
            affordance design §6.2 requires; it does NOT block the click, an
            explicit single-item accept is still the user's call.
            =================================================================== */}
        {redlineClickAnchor
          ? (() => {
              const clicked = redline.pending.find(p => p.ledgerRef === redlineClickAnchor.ledgerRef);
              const isLowBand = clicked?.confidenceBand === 'low';
              return (
                <div
                  ref={redlinePopoverRef}
                  className={mergeClasses(styles.redlinePopover, styles.contextMenuPopup)}
                  style={{ left: redlineClickAnchor.x, top: redlineClickAnchor.y }}
                  role="group"
                  aria-label="Accept or reject this suggested edit"
                  data-testid="compose-redline-onclick"
                  data-ledger-ref={redlineClickAnchor.ledgerRef}
                >
                  {clicked ? (
                    <>
                      {/* U1 R2 (UAT 2026-07-20): the confidence band is the compact header (the "Suggested
                          edit" label was removed at the operator's request), with padding + a divider below
                          it. The §6.2 anti-rubber-stamp safeguards are unchanged — a low-band edit still
                          shows "Needs review" and its Accept stays demoted below. */}
                      <div className={styles.redlineTopBar} data-testid="compose-redline-confidence-band">
                        <Badge size="small" appearance="tint" color={confidenceBandColor(clicked.confidenceBand)}>
                          {confidenceBandLabel(clicked.confidenceBand)}
                        </Badge>
                        {isLowBand ? (
                          <Text
                            size={100}
                            className={styles.redlineNeedsReview}
                            data-testid="compose-redline-needs-review"
                          >
                            Needs review before accepting
                          </Text>
                        ) : null}
                      </div>
                      {/* The FULL cited rationale (wraps, scrolls if long) — no internal ledger id. */}
                      <Text size={300} className={styles.redlineHeadline} data-testid="compose-redline-rationale">
                        {clicked.rationale && clicked.rationale.trim().length > 0
                          ? clicked.rationale.trim()
                          : 'Suggested edit'}
                      </Text>
                    </>
                  ) : null}
                  <div className={styles.redlineActions}>
                    <Button
                      size="small"
                      appearance="subtle"
                      icon={<Dismiss16Regular />}
                      onClick={() => {
                        redline.reject(redlineClickAnchor.ledgerRef);
                        setRedlineClickAnchor(null);
                      }}
                      data-testid={`compose-redline-reject-${redlineClickAnchor.ledgerRef}`}
                    >
                      Reject
                    </Button>
                    <Button
                      size="small"
                      appearance={isLowBand ? 'secondary' : 'primary'}
                      icon={<Checkmark16Regular />}
                      onClick={() => {
                        redline.accept(redlineClickAnchor.ledgerRef);
                        setRedlineClickAnchor(null);
                      }}
                      data-testid={`compose-redline-accept-${redlineClickAnchor.ledgerRef}`}
                    >
                      Accept
                    </Button>
                  </div>
                </div>
              );
            })()
          : null}
        {isImporting ? (
          <div className={styles.loadingState} role="status" aria-live="polite">
            <Spinner size="small" />
            <Text size={200}>Importing document…</Text>
          </div>
        ) : null}
        {/* FIX #9 — scroll region: native scrollbar hidden (see `editorSurface`
            style), with a floating circular down-arrow FAB that appears only when
            more content sits below the fold and scrolls the surface down on click.
            The FAB is a sibling of the scroller (not inside it) so it stays pinned
            at the bottom instead of scrolling away with the content. */}
        <div className={styles.editorScrollWrap}>
          <div
            ref={editorScrollRef}
            className={styles.editorSurface}
            // UAT round-3 D1: reserve room on the right for the (resizable) comment rail so document
            // text never runs under the cards. Dynamic width replaces the former fixed
            // `editorSurfaceWithGutter` padding class.
            style={
              advisoryComments.threads.length > 0 && reviewNotesVisible
                ? { paddingRight: `calc(${gutterWidth}px + ${tokens.spacingHorizontalL})` }
                : undefined
            }
            data-testid="compose-editor-surface"
          >
            <EditorContent editor={editor} />
          </div>
          {/* task 032 (right-gutter comment layout, FR-16) — NDA-REVIEW advisory comment cards,
              vertically aligned to their live anchor position (coordsAtPos), right of the document.
              Renders nothing while there are no advisory comment threads. UAT round-2 item #2: the
              "Review" toolbar dropdown's "Review Notes" toggle hides them without discarding the placed
              threads — passing an empty list makes the gutter render null while the threads persist. */}
          <ComposeCommentGutter
            editor={editor}
            threads={reviewNotesVisible ? advisoryComments.threads : []}
            scrollContainerRef={editorScrollRef}
            width={gutterWidth}
            onWidthChange={handleGutterWidthChange}
            resolveStandardText={bffBaseUrl ? resolveStandardText : undefined}
            selectedThreadId={selectedThreadId}
            onSelectThread={selectThread}
            noteTools={enqueueComposeAction ? noteTools : undefined}
            onRunNoteTool={enqueueComposeAction ? runNoteTool : undefined}
          />
          {/* UAT round-6 #3b — the floating "Comments" (TipTap OOB session-comments) toggle FAB was
              REMOVED per reviewer request (those comments aren't used in the NDA advisory flow; the
              advisory Review Notes gutter is the comment surface). The ComposeCommentThread panel +
              useComposeCommentThreads instance remain in the codebase (now unreachable from the UI) so
              the capability can be re-exposed later without re-plumbing. */}
          {/* UAT round-4: the "Show styles" toggle was REMOVED per user request — the apply-existing-
              styles pane added little value over the Body/Paragraph/Font toolbar dropdowns. The
              ComposeStylesPane component + hook remain in the codebase (unmounted) in case it returns. */}
          {showScrollDown ? (
            <Button
              appearance="primary"
              shape="circular"
              size="large"
              className={styles.scrollDownFab}
              icon={<ArrowDown20Regular />}
              aria-label="Scroll down for more"
              onClick={scrollEditorDown}
              data-testid="compose-editor-scroll-down"
            />
          ) : null}
        </div>

        {/* ===================================================================
            AI-actions <BubbleMenu> — RELOCATED here (UAT 2026-07-19 P1 fix) to be
            the LAST child of the container. See the NOTE where ComposeStylesPane is
            rendered above for the full insertBefore-crash rationale. tippy detaches
            this wrapper on mount and positions it relative to the selection, so its
            place in the child order is structural only (no visual effect).

            AI TOOLBAR MOUNT — task 030 (FR-14), AI-actions-ONLY per task 111
            (UAT-R2 layout fix): the sibling formatting Toolbar that used to render
            here was REMOVED (task 111) — ComposeAiToolbar is now the popup's ONLY
            content, owning its own single Toolbar.
            =================================================================== */}
        {editor ? (
          <BubbleMenu
            editor={editor}
            tippyOptions={{ duration: 100, placement: 'top' }}
            className={styles.aiBubbleWrap}
          >
            <ComposeAiToolbar
              editor={editor}
              documentRef={documentRef}
              sessionId={sessionId}
              bffBaseUrl={bffBaseUrl}
              dispatch={dispatch}
              activeWorkType={activeWorkType}
              onRequestInstruction={promptForInstruction}
              enqueueComposeAction={enqueueComposeAction}
            />
          </BubbleMenu>
        ) : null}

        {/* ===================================================================
            Contextual AI Tool Library (phase 3) — shared free-text INSTRUCTION
            dialog. Opened by any `inputPrompt` tool from EITHER the BubbleMenu
            (ComposeAiToolbar.onRequestInstruction) or a Review Note's ⋮ menu
            (runNoteTool). Resolves the promise from `promptForInstruction` with
            the entered text (Apply) or null (Cancel / dismiss).
            =================================================================== */}
        <Dialog
          open={instructionPrompt.open}
          onOpenChange={(_, data) => {
            if (!data.open) settleInstruction(null);
          }}
        >
          <DialogSurface aria-describedby={undefined} data-testid="compose-instruction-dialog">
            <DialogBody>
              <DialogTitle>{instructionPrompt.action?.label ?? 'Describe a change'}</DialogTitle>
              <DialogContent>
                <Textarea
                  value={instructionPrompt.value}
                  placeholder={instructionPrompt.action?.inputPrompt}
                  onChange={(_, data) => setInstructionPrompt(prev => ({ ...prev, value: data.value }))}
                  resize="vertical"
                  textarea={{ 'aria-label': 'Change instruction', autoFocus: true }}
                  style={{ width: '100%', minHeight: '96px' }}
                  data-testid="compose-instruction-input"
                  onKeyDown={e => {
                    // Ctrl/Cmd+Enter submits (a plain Enter inserts a newline in the textarea).
                    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter' && instructionPrompt.value.trim()) {
                      settleInstruction(instructionPrompt.value);
                    }
                  }}
                />
              </DialogContent>
              <DialogActions>
                <Button
                  appearance="secondary"
                  onClick={() => settleInstruction(null)}
                  data-testid="compose-instruction-cancel"
                >
                  Cancel
                </Button>
                <Button
                  appearance="primary"
                  disabled={!instructionPrompt.value.trim()}
                  onClick={() => settleInstruction(instructionPrompt.value)}
                  data-testid="compose-instruction-submit"
                >
                  Apply
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
      </div>
    );
  }
);

ComposeEditor.displayName = 'ComposeEditor';
