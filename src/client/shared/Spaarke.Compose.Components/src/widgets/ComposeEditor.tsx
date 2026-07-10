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
 *  2. Import DOCX bytes via mammoth (lazy-loaded) on `docxBytes` prop change.
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

import { makeStyles, mergeClasses, tokens, Spinner, Text, Button } from '@fluentui/react-components';
import { Checkmark16Regular, Dismiss16Regular } from '@fluentui/react-icons';
import { ComposeFormatToolbar } from './ComposeFormatToolbar';
import { ComposeAiToolbar, type ComposeActionEnqueue } from './ComposeAiToolbar';
import { InsertionMark } from './marks/InsertionMark';
import { DeletionMark } from './marks/DeletionMark';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { QaHighlightExtension } from './marks/QaHighlightExtension';
import { usePendingRedline, type MaterializeStatus } from './hooks/usePendingRedline';
import { useDocQaHighlight, type QaHighlightStatus } from './hooks/useDocQaHighlight';
// spaarkeai-compose-r1 task 093: deep-import from `@spaarke/ai-widgets/events`
// rather than the barrel `@spaarke/ai-widgets` to skip the side-effect widget
// registration (`register-workspace-widgets.ts` transitively pulls in
// `@spaarke/ai-outputs` subpaths that LegalWorkspace standalone Rollup cannot
// resolve). Same rationale as ComposeWorkspace.tsx above.
import { useDispatchPaneEvent, type DispatchPaneEvent } from '@spaarke/ai-widgets/events';

import { docxToTipTapHtml, tipTapToDocxBytes } from '../utils/docxBridge';

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
 * All packages MIT-licensed; mammoth is BSD-2-Clause; docx is MIT. NO TipTap
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
  /** The drafted content to materialize into the editor (load-bearing field). */
  new_text: string;
  /** How the client resolves `target_text` (Compose vocabulary, e.g. `strict` / `insert`). */
  match_mode?: string;
  /** Optional model-supplied rationale (provenance/explanation). */
  rationale?: string;
  /** Citations / source ids the draft was grounded on (ids only). */
  sources?: string[];
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
   * triggers a mammoth re-import.
   */
  docxBytes: ArrayBuffer | null;

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
   * Called with mammoth's conversion-warning array after each DOCX import.
   * The host can surface a "this document was simplified on load" banner
   * (deferred to R2 per Spike #1 §5.4; R1 only logs to console).
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
}

/**
 * Imperative handle exposed by ComposeEditor — host calls these via ref.
 */
export interface ComposeEditorHandle {
  /**
   * Serialize current editor state to DOCX bytes (for SPE upload).
   *
   * Lazy-loads `docx` on first call. Round-trip fidelity per Spike #1 §3
   * inventory ("Preserved" rows survive; "Degraded" rows survive with
   * documented loss).
   *
   * @returns ArrayBuffer of DOCX bytes ready for upload.
   * @throws  Error if no editor is mounted or if docx packing fails.
   */
  serialize(): Promise<ArrayBuffer>;

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
  editorSurface: {
    flex: 1,
    overflow: 'auto',
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
    },
    '& .ProseMirror a': {
      color: tokens.colorBrandForegroundLink,
      textDecoration: 'underline',
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
    '& .compose-mark-comment-anchor': {
      backgroundColor: tokens.colorPaletteYellowBackground2,
      color: tokens.colorNeutralForeground1,
      borderRadius: tokens.borderRadiusSmall,
    },
    // FR-35 Doc Q&A ephemeral highlight (task 072) — a ProseMirror view
    // decoration, NOT a doc Mark (never serializes to DOCX). Semantic tokens
    // only (ADR-021 dark-mode-correct).
    '& .compose-qa-highlight': {
      backgroundColor: tokens.colorPaletteMarigoldBackground2,
      borderRadius: tokens.borderRadiusSmall,
      transition: 'background-color 0.2s ease-out',
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
  // Task 111: the popup is now AI-actions-ONLY (ComposeAiToolbar owns the
  // single Toolbar rendered inside it). `flexWrap` + `maxWidth` are ADDITIVE
  // overflow-prevention (previously absent) — the text-labelled AI buttons
  // wrap onto a second row instead of overflowing the popup width.
  bubbleMenu: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    maxWidth: '420px',
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
  // R2 pending-redline accept/reject affordances (task 033, FR-16). Semantic tokens only
  // (ADR-021 dark-mode-correct) — no hardcoded hex.
  redlineControls: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  redlineItem: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
  },
  redlineLabel: {
    flex: 1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground2,
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

/** Truncated, log-safe label for a pending redline (rationale is Tier 3 — shown, never logged). */
function redlineLabelText(rationale: string | undefined, ledgerRef: string): string {
  const base = rationale && rationale.trim().length > 0 ? rationale.trim() : 'Suggested edit';
  const label = base.length > 80 ? `${base.slice(0, 80)}…` : base;
  return `${label} (${ledgerRef})`;
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
      documentRef,
      bffBaseUrl,
      sessionId = '',
      onDirtyChange,
      onImportWarnings,
      enqueueComposeAction,
    } = props;

    const styles = useStyles();
    const dispatch = useDispatchPaneEvent();

    const [isImporting, setIsImporting] = React.useState<boolean>(false);
    const dirtyRef = React.useRef<boolean>(false);

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

    // ----- TipTap editor instance -----------------------------------------
    const editor = useEditor({
      // LOCKED Spike #1 set + the ADDITIVE R2 custom marks (task 031) — the locked list itself
      // is unchanged (spread, not mutated), honoring the "do not touch the locked list" constraint.
      extensions: [...LOCKED_EXTENSIONS, ...COMPOSE_R2_MARKS, ...COMPOSE_R2_QA_HIGHLIGHT],
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
        // Reset to empty paragraph if cleared.
        editor.commands.setContent('<p></p>');
        dirtyRef.current = false;
        onDirtyChange?.(false);
        return;
      }
      // gap 1.6 / DEF-01: a TRANSIENT (Browse/Upload) mount has no SPE pointer yet (empty
      // speDriveItemId). Its create-on-save first Save must be reachable, so we report
      // dirty=true to the workspace (there IS unsaved work — the draft has never been
      // persisted). The editor's OWN dirtyRef stays FALSE below so an *untouched* transient
      // Save still persists the pristine ORIGINAL bytes byte-identical (FR-06a, task 015) —
      // triggerSave keys the byte-branch off `editorRef.current.isDirty()` (= dirtyRef), NOT
      // the workspace-facing onDirtyChange signal. A stored (non-transient) load reports clean.
      const isTransientMount = !documentRef?.speDriveItemId;
      let cancelled = false;
      setIsImporting(true);
      docxToTipTapHtml(docxBytes)
        .then(({ html, messages }) => {
          if (cancelled) return;
          editor.commands.setContent(html);
          dirtyRef.current = false; // fresh load: editor's internal dirty flag is clean (FR-06a)
          onDirtyChange?.(isTransientMount);
          // Privacy: messages are Tier 1 safe (configuration metadata).
          // Document HTML itself is Tier 3 — NEVER logged.
          if (messages.length > 0) {
            // eslint-disable-next-line no-console
            console.info(`[ComposeEditor] mammoth surfaced ${messages.length} warning(s) on import`);
          }
          onImportWarnings?.(messages);
        })
        .catch(err => {
          // eslint-disable-next-line no-console
          console.error('[ComposeEditor] DOCX import failed', err instanceof Error ? err.message : String(err));
          // Caller can detect via onImportWarnings empty + ProseMirror empty;
          // R2 will add a structured error callback.
        })
        .finally(() => {
          if (!cancelled) setIsImporting(false);
        });
      return () => {
        cancelled = true;
      };
      // `documentRef?.speDriveItemId` is read (transient-vs-stored) but intentionally NOT a dep:
      // the effect must re-run ONLY on a new `docxBytes` mount. Adding it would re-run on
      // save-success (when speDriveItemId gets populated on the same bytes) and clobber the
      // user's edits by re-importing the original mount bytes. The captured value is correct
      // because `mountTransient` sets docxBytes + documentRef atomically in one render.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [editor, docxBytes, onDirtyChange, onImportWarnings]);

    // ----- Selection dispatch (heartbeat hoisted to ComposeWorkspace) -----
    useSelectionEventDispatch(editor, documentRef, sessionId, dispatch);

    // ----- FR-16 pending-redline materialization (task 033) ---------------
    // Owns materialize-from-ledger → FR-15 marks + accept/reject + supersession.
    const redline = usePendingRedline(editor);

    // ----- FR-35 Doc Q&A ephemeral highlight (task 072, stretch) -----------
    const qaHighlight = useDocQaHighlight(editor);

    // ----- Imperative handle ----------------------------------------------
    React.useImperativeHandle(
      ref,
      (): ComposeEditorHandle => ({
        serialize: async () => {
          if (!editor) {
            throw new Error('ComposeEditor: cannot serialize — editor not mounted');
          }
          const bytes = await tipTapToDocxBytes(editor);
          // Successful serialize resets the dirty flag (host typically calls
          // serialize() then uploads; after upload completes, the doc is clean).
          dirtyRef.current = false;
          onDirtyChange?.(false);
          return bytes;
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
        highlightCitedSpan: (sourceText, sectionLabel) => qaHighlight.highlight(sourceText, sectionLabel),
        clearCitedHighlight: () => qaHighlight.clear(),
      }),
      [editor, redline, qaHighlight]
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
        <ComposeFormatToolbar editor={editor} disabled={isImporting} />
        {editor ? (
          <BubbleMenu editor={editor} tippyOptions={{ duration: 100, placement: 'top' }} className={styles.bubbleMenu}>
            {/* ===================================================================
                AI TOOLBAR MOUNT — task 030 (FR-14), AI-actions-ONLY per task 111
                (UAT-R2 layout fix): the sibling formatting Toolbar that used to
                render here was REMOVED (task 111) — ComposeAiToolbar is now the
                popup's ONLY content, owning its own single Toolbar (no orphaned
                divider, no double-Toolbar padding). Task 031 (custom marks)
                edits AFTER this block — keep this region self-contained so that
                follow-on edits stay a clean insertion.
                =================================================================== */}
            <ComposeAiToolbar
              editor={editor}
              documentRef={documentRef}
              sessionId={sessionId}
              bffBaseUrl={bffBaseUrl}
              dispatch={dispatch}
              enqueueComposeAction={enqueueComposeAction}
            />
            {/* =================== END AI TOOLBAR MOUNT (task 030) =================== */}
          </BubbleMenu>
        ) : null}

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
            className={mergeClasses(styles.bubbleMenu, styles.contextMenuPopup)}
            style={{ left: contextMenuAnchor.x, top: contextMenuAnchor.y }}
            data-testid="compose-ai-context-menu"
          >
            <ComposeAiToolbar
              editor={editor}
              documentRef={documentRef}
              sessionId={sessionId}
              bffBaseUrl={bffBaseUrl}
              dispatch={dispatch}
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
          <div className={styles.redlineError} role="alert" data-testid="compose-redline-error">
            <Text size={200} className={styles.redlineErrorText}>
              {redline.error.kind === 'ambiguous'
                ? `Couldn't place this suggested edit: its target text appears ${redline.error.matchCount} times in the document. Reselect the exact passage and try again.`
                : `Couldn't place this suggested edit: its target text was not found in the current document.`}
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
        {redline.pending.length > 0 ? (
          <div
            className={styles.redlineControls}
            role="group"
            aria-label="Pending suggested edits"
            data-testid="compose-redline-controls"
          >
            {redline.pending.map(p => (
              <div key={p.ledgerRef} className={styles.redlineItem} data-testid={`compose-redline-${p.ledgerRef}`}>
                <Text size={200} className={styles.redlineLabel} title={p.rationale ?? undefined}>
                  {redlineLabelText(p.rationale, p.ledgerRef)}
                </Text>
                <Button
                  size="small"
                  appearance="primary"
                  icon={<Checkmark16Regular />}
                  onClick={() => redline.accept(p.ledgerRef)}
                  data-testid={`compose-redline-accept-${p.ledgerRef}`}
                >
                  Accept
                </Button>
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<Dismiss16Regular />}
                  onClick={() => redline.reject(p.ledgerRef)}
                  data-testid={`compose-redline-reject-${p.ledgerRef}`}
                >
                  Reject
                </Button>
              </div>
            ))}
          </div>
        ) : null}
        {isImporting ? (
          <div className={styles.loadingState} role="status" aria-live="polite">
            <Spinner size="small" />
            <Text size={200}>Importing document…</Text>
          </div>
        ) : null}
        <EditorContent editor={editor} className={styles.editorSurface} />
      </div>
    );
  }
);

ComposeEditor.displayName = 'ComposeEditor';
