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
 *  - Dispatch AI actions directly. Per refined ADR-013 (2026-05-20), AI dispatch
 *    flows through `IConsumerRoutingService` + `IInvokePlaybookAi` via
 *    `POST /api/compose/action/{consumerType}` (W3-024). The editor emits
 *    `compose_selection_offer` on the `conversation` channel; the
 *    ConversationPane (or sibling toolbar) is the dispatcher.
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

import { makeStyles, tokens, Spinner, Text, Toolbar, ToolbarButton } from '@fluentui/react-components';
import {
  TextBold24Regular,
  TextItalic24Regular,
  TextUnderline24Regular,
  TextStrikethrough24Regular,
  Link24Regular,
  LinkDismiss24Regular,
} from '@fluentui/react-icons';
import { ComposeFormatToolbar } from './ComposeFormatToolbar';
import { useDispatchPaneEvent, type DispatchPaneEvent } from '@spaarke/ai-widgets';

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
  },
  loadingState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
  bubbleMenu: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
    padding: tokens.spacingHorizontalXXS,
  },
});

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
        // Per ADR-030: the Compose discriminants are additive on the existing
        // `context` channel typed union; we cast through `unknown` because the
        // PaneEventChannelMap doesn't yet enumerate these Compose additions at
        // the shared-lib level (compose-contracts.ts is solution-local until a
        // second consumer needs the types — see Spike #2 §11 / task 041).
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
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
        } as any);

        // Flow 2 — fires only when selection is meaningful (non-collapsed +
        // ≥10 chars) to avoid noise on click-only cursor moves.
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
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
          } as any);
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
    const { docxBytes, documentRef, bffBaseUrl, sessionId = '', onDirtyChange, onImportWarnings } = props;

    const styles = useStyles();
    const dispatch = useDispatchPaneEvent();

    const [isImporting, setIsImporting] = React.useState<boolean>(false);
    const dirtyRef = React.useRef<boolean>(false);

    // ----- TipTap editor instance -----------------------------------------
    const editor = useEditor({
      extensions: LOCKED_EXTENSIONS,
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
      let cancelled = false;
      setIsImporting(true);
      docxToTipTapHtml(docxBytes)
        .then(({ html, messages }) => {
          if (cancelled) return;
          editor.commands.setContent(html);
          dirtyRef.current = false; // fresh load is clean
          onDirtyChange?.(false);
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
    }, [editor, docxBytes, onDirtyChange, onImportWarnings]);

    // ----- Selection dispatch (heartbeat hoisted to ComposeWorkspace) -----
    useSelectionEventDispatch(editor, documentRef, sessionId, dispatch);

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
      }),
      [editor, onDirtyChange]
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

    // BubbleMenu handler for the "Link" button — prompts for a URL and applies
    // it as a link mark to the current selection. Removing an existing link
    // uses the same button when a link is already active.
    const toggleLink = React.useCallback((): void => {
      if (!editor) return;
      if (editor.isActive('link')) {
        editor.chain().focus().unsetLink().run();
        return;
      }
      const previousUrl = editor.getAttributes('link').href as string | undefined;
      // eslint-disable-next-line no-alert
      const url = window.prompt('Enter URL', previousUrl ?? 'https://');
      if (url === null) return; // cancelled
      if (url.trim() === '') {
        editor.chain().focus().unsetLink().run();
        return;
      }
      editor.chain().focus().extendMarkRange('link').setLink({ href: url.trim() }).run();
    }, [editor]);

    return (
      <div
        className={styles.container}
        role="region"
        aria-label={documentRef?.fileName ?? 'Compose editor'}
        data-compose-editor-document-id={documentRef?.sprkDocumentId ?? ''}
        data-compose-editor-spe-id={documentRef?.speDriveItemId ?? ''}
      >
        <ComposeFormatToolbar editor={editor} disabled={isImporting} />
        {editor ? (
          <BubbleMenu editor={editor} tippyOptions={{ duration: 100, placement: 'top' }} className={styles.bubbleMenu}>
            <Toolbar size="small" aria-label="Selection formatting" data-testid="compose-bubble-menu">
              <ToolbarButton
                appearance={editor.isActive('bold') ? 'primary' : 'subtle'}
                icon={<TextBold24Regular />}
                aria-label="Bold"
                aria-pressed={editor.isActive('bold')}
                onClick={() => editor.chain().focus().toggleBold().run()}
              />
              <ToolbarButton
                appearance={editor.isActive('italic') ? 'primary' : 'subtle'}
                icon={<TextItalic24Regular />}
                aria-label="Italic"
                aria-pressed={editor.isActive('italic')}
                onClick={() => editor.chain().focus().toggleItalic().run()}
              />
              <ToolbarButton
                appearance={editor.isActive('underline') ? 'primary' : 'subtle'}
                icon={<TextUnderline24Regular />}
                aria-label="Underline"
                aria-pressed={editor.isActive('underline')}
                onClick={() => editor.chain().focus().toggleUnderline().run()}
              />
              <ToolbarButton
                appearance={editor.isActive('strike') ? 'primary' : 'subtle'}
                icon={<TextStrikethrough24Regular />}
                aria-label="Strikethrough"
                aria-pressed={editor.isActive('strike')}
                onClick={() => editor.chain().focus().toggleStrike().run()}
              />
              <ToolbarButton
                appearance={editor.isActive('link') ? 'primary' : 'subtle'}
                icon={editor.isActive('link') ? <LinkDismiss24Regular /> : <Link24Regular />}
                aria-label={editor.isActive('link') ? 'Remove link' : 'Add link'}
                aria-pressed={editor.isActive('link')}
                onClick={toggleLink}
              />
            </Toolbar>
          </BubbleMenu>
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
