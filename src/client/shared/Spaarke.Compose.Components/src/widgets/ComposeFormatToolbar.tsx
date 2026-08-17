/**
 * ComposeFormatToolbar.tsx — single-row document command toolbar for ComposeEditor.
 *
 * FIX #5 (spaarkeai-compose-r2 UAT): the toolbar was consolidated from TWO wrapping
 * rows of individual icon buttons into ONE row grouped behind labelled Fluent v9
 * `Menu` dropdowns. The dropdown TRIGGER buttons carry WORD labels (Body / Paragraph
 * / Font / Word); the tools INSIDE the Paragraph/Font/Table dropdowns are ICON-only
 * buttons with a hover Tooltip naming each command, while the Word dropdown (task 039
 * P3) is a VERTICAL list of icon+label rows (Open web / Open desktop). The Track
 * Changes toggle is icon-only (task 039 P1). `Save` + `Undo`/`Redo` are icon-only buttons pushed
 * to the RIGHT edge (a `flex:1` spacer). This is a REORGANIZATION only — every
 * command previously reachable stays wired with its disabled/active state intact.
 *
 * Dropdown map:
 *   - Body      — block/heading style (unchanged from task 111).
 *   - Paragraph — bullet list, numbered list, blockquote, align left/center/right.
 *   - Font      — bold, italic, underline, strikethrough, link.
 *   - Table     — insert table (2x2 + header row), add/delete row, add/delete column,
 *                 delete table (task 041, FR-18). Row/column/delete-table commands are
 *                 disabled outside a table (`editor.can().<cmd>()` dry-run).
 *   - Word      — Open in Word Web, Open in Word Desktop. These were
 *                 previously top-level actions on the separate `ComposeToolbar`
 *                 command bar (rendered by ComposeWorkspace). The host now binds the
 *                 handlers (`onOpenInWord` / `onOpenInWordDesktop`)
 *                 and threads them here via ComposeEditor. The dropdown is omitted
 *                 when the host wires no Word handlers. UAT round-1 #6 (2026-08-03):
 *                 relocated OUT of this format-menus row to the action side (right,
 *                 near Save) as an icon-only dropdown (`DocumentWordRegular`). UAT
 *                 round-4 #11 (2026-08-04): the popover's former Save-duplicate entries
 *                 were REMOVED — it now contains ONLY "Open in Word (web)" / "Open in
 *                 Word (desktop)".
 *   - Save      — icon-only button (right-aligned); rendered only when `onSave` is
 *                 wired. UAT round-1 #5 (2026-08-03): the visible "Save Version" text
 *                 was dropped (restoring the icon-only intent this header already
 *                 documented) — the accessible name lives on `aria-label` + a Tooltip.
 *   - Undo/Redo — icon buttons (right-aligned).
 *
 * UAT round-1 #3 (2026-08-03): "Create Summary Memo" (see the FR-14 group below) is now an
 * icon-only dropdown trigger (`DocumentBulletList24Regular`, same glyph as before) repositioned
 * to the FAR LEFT of the toolbar — the very first control, ahead of Body/Paragraph/Font/Table.
 * The dropdown's two menu items (Generate memo / Email memo) are unchanged.
 *
 * UAT round-4 #11/#12 (2026-08-04) — SUPERSEDES the round-1 #3 far-left Memo placement above and
 * the round-1 #6 Word-menu Save duplicate:
 *   #11 (menu allocation) — the Word dropdown's "Save Version" / "Save New Document" duplicate
 *     buttons (the round-1 "UX-1 parity affordance") are REMOVED. The owner rejected the overlap:
 *     Word menu now contains ONLY "Open in Word (web)" / "Open in Word (desktop)"; Save stays the
 *     ONE split-button (Save Version primary / Save New Document caret). Zero functional overlap
 *     between the two menus.
 *   #12 (regroup + spacing) — the format-menu group (Body/Paragraph/Font/Table) is unchanged at
 *     the left. The Create Summary Memo trigger moves OFF the far-left position (superseding round-1
 *     #3) into the regrouped action-icon area on the right, which now reads, left→right, as FOUR
 *     `ToolbarDivider`-separated groups: [Review Summary toggle · Create Summary Memo] |
 *     [Word · Save] | [Review Notes toggle · Track Changes toggle] | [Undo · Redo · Info]. The
 *     three controls the owner's round-4 order did not mention — Open Document, Reload from
 *     source, Refresh Profile — are NOT deleted; they sit immediately after the format-menu group
 *     and before the (now right-aligned) action groups, keeping their pre-round-4 relative order.
 *     This placement is a judgment call (flagged for the owner to adjust) — see the inline comment
 *     at that render site.
 *
 * The inline character-format controls (Bold / Italic / Underline / Strikethrough /
 * Link) were RELOCATED here from the TipTap selection BubbleMenu at task 111 (the
 * BubbleMenu is AI-actions ONLY now). The active-state highlighting
 * (`isActive('bold')` etc.) and the Link add/edit `window.prompt` flow are preserved
 * byte-for-byte from that relocation — no formatting capability was lost.
 *
 * FR-18 (spaarkeai-compose-r3 task 041) — the "Table" dropdown adds a basic-table
 * affordance on top of the MIT `@tiptap/extension-table` family, which is ALREADY
 * part of the LOCKED_EXTENSIONS list in ComposeEditor.tsx (Table/TableRow/
 * TableHeader/TableCell were registered there since the R1 spike; this task adds the
 * missing UI surface). Insert Table is always reachable; the row/column edit +
 * delete-table commands are disabled outside a table via `editor.can().<cmd>()`
 * dry-run checks (the same idiom `compose-format-undo`/`redo` already use). Cell
 * paragraphs carry `paraId` via the SAME `COMPOSE_R3_PARAID` UniqueID scheme as body
 * paragraphs (task 011) — no separate cell-identity mechanism (FR-08/FR-10).
 *
 * Extensions consumed here MUST match the LOCKED_EXTENSIONS list in
 * ComposeEditor.tsx. Adding a button here without loading the corresponding
 * extension will make TipTap silently ignore the command.
 *
 * @see ComposeEditor.tsx (host + AI-only BubbleMenu wiring + Word/Save prop source)
 * @see ADR-021 — Fluent v9 semantic tokens, dark-mode-correct
 */

import * as React from 'react';
import { type Editor } from '@tiptap/react';
import {
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Button,
  SplitButton,
  Tooltip,
  makeStyles,
  tokens,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuItemCheckbox,
  MenuDivider,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Text,
  Spinner,
  type MenuButtonProps,
} from '@fluentui/react-components';
// G7 (task 022): the Save split-button choice ('version' | 'new').
import type { ComposeSaveMode } from '../types/compose-contracts';
import {
  TextBold24Regular,
  TextItalic24Regular,
  TextUnderline24Regular,
  TextStrikethrough24Regular,
  Link24Regular,
  LinkDismiss24Regular,
  TextBulletListLtr24Regular,
  TextNumberListLtr24Regular,
  TextQuote24Regular,
  TextAlignLeft24Regular,
  TextAlignCenter24Regular,
  TextAlignRight24Regular,
  ArrowUndo24Regular,
  ArrowRedo24Regular,
  Info24Regular,
  CommentMultiple24Regular,
  CommentAdd24Regular,
  ChevronDown16Regular,
  DocumentEdit24Regular,
  OpenRegular,
  DesktopRegular,
  SaveRegular,
  TableAdd24Regular,
  TableInsertRow24Regular,
  TableInsertColumn24Regular,
  TableDeleteRow24Regular,
  TableDeleteColumn24Regular,
  TableDismiss24Regular,
  ClipboardTaskListLtr24Regular,
  DocumentSync24Regular,
  ArrowClockwise24Regular,
  DocumentText24Regular,
  DocumentBulletList24Regular,
  ArrowDownload24Regular,
  Mail24Regular,
  DocumentWordRegular,
  PaintBrush24Regular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  toolbar: {
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingInlineStart: tokens.spacingHorizontalS,
    paddingInlineEnd: tokens.spacingHorizontalS,
    paddingBlockStart: tokens.spacingVerticalXXS,
    paddingBlockEnd: tokens.spacingVerticalXXS,
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
    // FIX #5: a SINGLE row now — the dropdowns keep the control count low enough
    // that the bar never needs to wrap (was `flexWrap: 'wrap'`, the source of the
    // "two rows" UAT finding).
    flexWrap: 'nowrap',
    // DEF-16 (UAT-R3): pin the formatting toolbar to the top of the editor scroll
    // region so it stays reachable while a long document body scrolls beneath it.
    // The opaque background + z-index prevent scrolled content bleeding through the
    // pinned bar. Semantic tokens only (ADR-021 dark-mode-correct).
    position: 'sticky',
    top: 0,
    zIndex: 1,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  menuButton: {
    minWidth: '96px',
  },
  // FR-03 (task 041): the save-state indicator (Saving… / Unsaved / Saved + Auto Save On/Off).
  // Subtle, single-line, Fluent v9 semantic tokens only (ADR-021 dark-mode-correct).
  saveStateIndicator: {
    display: 'inline-flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
    marginInlineStart: tokens.spacingHorizontalXS,
    marginInlineEnd: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    whiteSpace: 'nowrap',
    userSelect: 'none',
  },
  // FIX #5: the icon-only tool palette rendered INSIDE a Paragraph/Font/Word
  // dropdown popover — a comfortable, even horizontal gap (not cramped, not
  // spread). Semantic tokens only.
  dropdownPalette: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    paddingInline: tokens.spacingHorizontalXS,
    paddingBlock: tokens.spacingVerticalXS,
  },
  // Pushes Save + Undo/Redo to the right edge of the single row.
  spacer: {
    flexGrow: 1,
  },
  // UAT round-6 #4 — the not-legal-advice disclaimer popover (opened from the toolbar info button).
  disclaimerPopover: {
    maxWidth: '320px',
    color: tokens.colorNeutralForeground2,
  },
  // task 039 P3: the Word dropdown is now a VERTICAL, labelled list (was a horizontal icon-only
  // palette) — each action reads as a full-width menu row with an icon + text label. Semantic tokens only.
  wordMenuColumn: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'stretch',
    minWidth: '176px',
    paddingInline: tokens.spacingHorizontalXS,
    paddingBlock: tokens.spacingVerticalXS,
    rowGap: tokens.spacingVerticalXXS,
  },
  // Left-align the icon + label inside each vertical Word action (Fluent Button centers by default).
  wordMenuItem: {
    justifyContent: 'flex-start',
  },
});

export interface ComposeFormatToolbarProps {
  editor: Editor | null;
  /** Applies a disabled visual + non-interactive state to every control. */
  disabled?: boolean;

  // ---- Word group (FIX #5) — host-bound handlers; dropdown hidden if all absent ----
  /** Open the current document in Word for the Web. */
  onOpenInWord?: () => void;
  /** Open the current document in the Word desktop app. */
  onOpenInWordDesktop?: () => void;
  /** Disables the two Open-in-Word items (no persisted document, or an action is in flight). */
  wordActionsDisabled?: boolean;

  // ---- Track Changes (item 4, UAT round-4) — labelled toggle, rendered only when handler set ----
  /** True when the live Track Changes decoration overlay is on (user edits render as redlines). */
  trackChangesEnabled?: boolean;
  /** Toggle the live Track Changes overlay. Rendered only when supplied. */
  onToggleTrackChanges?: () => void;

  // ---- Add Comment (FR-10 / R6 D7, task 072) — the UI entry point onto the SHIPPED comment
  //      round-trip machinery (useComposeCommentThreads / ComposeCommentThread, R6 tasks 024/026).
  //      The prior "Comments" FAB was removed (UAT round-6 #3b), leaving the panel reachable-less;
  //      this re-exposes it as a toolbar toggle. Rendered only when the handler is supplied. ----
  /** True when the Comments composer/panel is open (drives the toggle's pressed state). */
  commentsOpen?: boolean;
  /** Open the Comments composer for the current selection (host captures the range). Rendered only when supplied. */
  onToggleComments?: () => void;

  // ---- Deferred edit-path gate (task 038, supersedes task 037 — R4 zero-error guardrails) ----
  /**
   * True when the editor is over a LOADED/imported baseline (an uploaded `.docx`, a stored
   * document, or an opened template — anything with a retained original). False/undefined for a
   * from-scratch BORN-IN-EDITOR draft (blank page / AI-draft) that has NO retained original.
   *
   * Task 038 (R4 zero-error release gate) uses this to gate the controls whose edit-path support is
   * DEFERRED to R5 (projects/spaarkeai-compose-r5), so R4 ships with no user-triggerable errors and no
   * silent data loss:
   *  - When `true` (LOADED doc): the alignment buttons, the heading dropdown, the bullet/ordered list
   *    buttons, AND "Insert table" are DISABLED — the tracked-edit engine either 422s (alignment) or
   *    silently drops (heading/list/table) these constructs. Each shows an "Available in a future
   *    release" tooltip.
   *  - When falsy (BORN-IN-EDITOR draft): those controls are ENABLED — the ComposeDocumentRenderer
   *    authors headings/lists/tables/alignment cleanly for a from-scratch document.
   *
   * NOTE the table polarity is INVERTED vs task 037 (which disabled born-in-editor tables): the renderer
   * authors born-in-editor tables cleanly, but the engine has no table op and would silently drop a
   * loaded-doc table — so table-insert is enabled born-in-editor and disabled loaded. Hyperlinks are
   * disabled in BOTH modes independently of this flag (not representable in R4 — R5 G5).
   *
   * Defaults to falsy (undefined ⇒ born-in-editor treatment ⇒ these controls enabled), so a standalone/
   * library mount that never threads the flag keeps full authoring.
   */
  hasLoadedBaseline?: boolean;

  // ---- Save (FIX #5 / G7 task 022) — split-button, right-aligned; rendered only when `onSave` set ----
  /** Save handler. G7 (task 022): receives the split-button choice — `'version'` (default, replace/dedup)
   *  or `'new'` (fork a new document). */
  onSave?: (mode?: ComposeSaveMode) => void;
  /** True when Save should be enabled (unsaved edit OR unpersisted transient draft). */
  canSave?: boolean;
  /** True while a save is in flight. */
  isSaving?: boolean;
  /** FR-01/FR-03 (task 020/040): current Auto Save state, surfaced as a checkable menu item in the Save
   *  dropdown. The actual draft-safe autosave behavior is implemented in Phase 4 (040/041); this toolbar
   *  only renders the control + reports toggles. Undefined → the Auto Save item is not rendered (a host
   *  that has not wired autosave yet keeps the plain Save / Save As menu). */
  autoSaveEnabled?: boolean;
  /** FR-01/FR-03 (task 020/040): invoked with the NEXT Auto Save state when the user toggles the menu
   *  item. Rendered only when both this and {@link autoSaveEnabled} are set. */
  onAutoSaveToggle?: (enabled: boolean) => void;
  /** FR-03 (task 041): true when the document has unsaved edits (dirty OR an unpersisted transient
   *  draft). Drives the save-state indicator (Saving… while {@link isSaving}, Unsaved when true, Saved
   *  otherwise). Undefined → the indicator is not rendered (a host that does not track dirty state). */
  hasUnsavedEdits?: boolean;
  /** G10 (FR-09, task 040): manual "Refresh Profile" handler. Renders the button when set (the host
   *  wires it only for a promoted doc — one with a sprk_document record to re-profile). */
  onRefreshProfile?: () => void;
  /** UAT #5 (task 053): "Reload from source" handler. Renders the button when set (the host wires it only
   *  for a doc with an SPE source). Pulls the latest SPE bytes on demand — e.g. after an external Word-web
   *  edit the change-check missed. Distinct from Refresh Profile (which re-profiles, not reloads bytes). */
  onReloadFromSource?: () => void;
  /** "Open Document" handler. Renders the button when set (the host wires it only for a doc with a preview
   *  source — a promoted sprk_document). Opens the source Dataverse Document in the shared preview modal.
   *  Undefined → the button hides (mirrors the onRefreshProfile gating pattern). */
  onOpenDocument?: () => void;
  /** UAT #9 (task 054): true while a manual profile re-run is in flight — shows a spinner on the
   *  Refresh-Profile button so the (otherwise silent 202) click gives visible feedback. */
  isRefreshingProfile?: boolean;
  /** FR-05 (spaarkeai-compose-r6 task 032): "Apply firm template" handler — opens the host's
   *  template-select dialog. Renders the button when set (the host wires it only for a PERSISTED
   *  doc — the server merges the SAVED bytes). Undefined → the button hides (mirrors the
   *  onRefreshProfile gating pattern). */
  onApplyTemplate?: () => void;
  /** FR-05 (task 032): when set, the Apply-firm-template button is DISABLED and this text becomes
   *  its tooltip (e.g. "Save your changes first…" while the editor is dirty/transient — the merge
   *  applies to the persisted bytes, never unsaved editor state). */
  applyTemplateDisabledReason?: string;

  // ---- Review (ai-advanced-capabilities-nda-r1 UAT round-2 items #1/#2) — icon-only dropdown,
  //      right-aligned, rendered ONLY when an NDA advisory review is present. Two independent
  //      toggles: "Review Summary" (the docked TL;DR panel) and "Review Notes" (the right-gutter
  //      advisory comment cards). Both surfaces already exist; this is a single toolbar control that
  //      shows/hides each without dismissing the review data. ----
  /** True when an NDA advisory review has run (summary findings or in-document advisory comments exist). */
  hasReview?: boolean;
  /** Whether the review-summary docked panel is currently shown. */
  reviewSummaryOpen?: boolean;
  /** Toggle the review-summary panel. */
  onToggleReviewSummary?: () => void;
  /** Whether the right-gutter advisory comment cards ("Review Notes") are currently shown. */
  reviewNotesOpen?: boolean;
  /** Toggle the right-gutter advisory comments. */
  onToggleReviewNotes?: () => void;
  /**
   * UAT round-6 #4 — the not-legal-advice warning text. When provided (an NDA advisory review is
   * present), an info (ⓘ) button appears at the far right of the toolbar; clicking it shows this text in
   * a popover. Replaces the standing disclaimer banner that used to sit inside the Review Summary.
   */
  reviewDisclaimer?: string;

  // ---- Create Summary Memo (FR-14, ai-advanced-capabilities-agreements-r1 task 051) — a dropdown with
  //      two actions, rendered ONLY when a review is present (same `hasReview` gate as the Review
  //      Summary/Notes toggles above) AND at least one handler is threaded. Both actions READ the
  //      PERSISTED review-memo record server-side (render-from-persisted, project-binding constraint) —
  //      a record that hasn't been generated yet surfaces the host's "generate the review/memo first"
  //      negative state, never a silent empty export. Pure forwarder (mirrors onSave/onOpenDocument): the
  //      host (ComposeWorkspace) owns the fetch/download/EmailComposer-open logic. ----
  /** Generate + download the memo as a .docx. Rendered only when set. */
  onGenerateMemo?: () => void;
  /** Read the persisted memo and open the EmailComposer prefilled with its body + subject. Rendered only when set. */
  onEmailMemo?: () => void;
  /** True while a memo generate/email fetch is in flight — disables both actions and shows a spinner on the trigger. */
  isMemoActionInFlight?: boolean;
}

/**
 * Currently-selected block level, derived from the editor. Drives the label on the
 * Body menu button so operators see what block their cursor is in.
 */
function currentBlockLabel(editor: Editor | null): string {
  if (!editor) return 'Body';
  if (editor.isActive('heading', { level: 1 })) return 'Heading 1';
  if (editor.isActive('heading', { level: 2 })) return 'Heading 2';
  if (editor.isActive('heading', { level: 3 })) return 'Heading 3';
  return 'Body';
}

/**
 * FR-18 (task 041) — dry-run one of the `@tiptap/extension-table` commands via
 * `editor.can()` WITHOUT executing it, guarding against an `editor.can()` shape that
 * does not expose the table commands at all (a hand-rolled test double without the
 * Table extension mounted — see `ComposeFormatToolbar.test.tsx`'s chainable mock).
 * Production `ComposeEditor` always mounts the MIT table family (LOCKED_EXTENSIONS),
 * so this only matters for lighter-weight test/host editors.
 */
function canRunTableCommand(
  editor: Editor,
  cmd: 'addRowAfter' | 'addColumnAfter' | 'deleteRow' | 'deleteColumn' | 'deleteTable'
): boolean {
  const can = editor.can() as unknown as Partial<Record<typeof cmd, () => boolean>>;
  const fn = can[cmd];
  return typeof fn === 'function' ? fn() : false;
}

/**
 * Labelled dropdown trigger button (Paragraph / Font / Word). Uses `forwardRef`
 * and spreads `...rest` so the props Fluent `MenuTrigger` injects on its child
 * (onClick, ref, aria-expanded/haspopup, id) reach the underlying `Button` — a
 * plain function-component child would swallow them and the menu would never open.
 */
const DropdownButton = React.forwardRef<
  HTMLButtonElement,
  { label: string; disabled?: boolean; testId: string } & React.ComponentProps<typeof Button>
>(function DropdownButton({ label, disabled, testId, ...rest }, ref) {
  return (
    <Button
      {...rest}
      ref={ref}
      appearance="subtle"
      size="small"
      disabled={disabled}
      icon={<ChevronDown16Regular />}
      iconPosition="after"
      data-testid={testId}
    >
      {label}
    </Button>
  );
});

/**
 * Icon-only tool inside a dropdown palette. Hover Tooltip names the command; the
 * `active` flag drives the Fluent primary/subtle highlight + `aria-pressed`.
 */
function PaletteIconButton(props: {
  icon: React.JSX.Element;
  label: string;
  active?: boolean;
  disabled?: boolean;
  onClick: () => void;
  testId: string;
  /**
   * task 038 (spaarkeai-compose-r4 zero-error guardrails): when the control is disabled because its
   * underlying feature is DEFERRED (not merely read-only), the hover tooltip explains why instead of
   * naming the unavailable command. The accessible NAME (`aria-label`) stays the command so assistive
   * tech still announces what the control is; only the descriptive Tooltip changes.
   */
  deferredReason?: string;
}): React.JSX.Element {
  // The Button carries the accessible NAME via `aria-label`; the Tooltip is therefore a
  // `description` (not a second `label`) so the two do not both claim the accessible name.
  const tooltipContent = props.disabled && props.deferredReason ? props.deferredReason : props.label;
  return (
    <Tooltip content={tooltipContent} relationship="description" withArrow>
      <Button
        appearance={props.active ? 'primary' : 'subtle'}
        size="small"
        icon={props.icon}
        aria-label={props.label}
        aria-pressed={props.active}
        disabled={props.disabled}
        onClick={props.onClick}
        data-testid={props.testId}
      />
    </Tooltip>
  );
}

export function ComposeFormatToolbar(props: ComposeFormatToolbarProps): React.JSX.Element | null {
  const styles = useStyles();
  const {
    editor,
    disabled,
    onOpenInWord,
    onOpenInWordDesktop,
    wordActionsDisabled,
    hasLoadedBaseline,
    onSave,
    canSave,
    isSaving,
    autoSaveEnabled,
    onAutoSaveToggle,
    hasUnsavedEdits,
    onRefreshProfile,
    onReloadFromSource,
    onOpenDocument,
    isRefreshingProfile,
    onApplyTemplate,
    applyTemplateDisabledReason,
    trackChangesEnabled,
    onToggleTrackChanges,
    commentsOpen,
    onToggleComments,
    hasReview,
    reviewSummaryOpen,
    onToggleReviewSummary,
    reviewNotesOpen,
    onToggleReviewNotes,
    reviewDisclaimer,
    onGenerateMemo,
    onEmailMemo,
    isMemoActionInFlight,
  } = props;

  // Re-render on selection/transaction to keep the "active" highlight in sync.
  const [, forceUpdate] = React.useReducer((x: number) => x + 1, 0);
  React.useEffect(() => {
    if (!editor) return;
    const handler = (): void => forceUpdate();
    editor.on('selectionUpdate', handler);
    editor.on('transaction', handler);
    return () => {
      editor.off('selectionUpdate', handler);
      editor.off('transaction', handler);
    };
  }, [editor]);

  if (!editor) return null;

  const controlDisabled = disabled === true;

  const setHeading = (level: 1 | 2 | 3 | null): void => {
    if (controlDisabled) return;
    const chain = editor.chain().focus();
    if (level === null) {
      chain.setParagraph().run();
    } else {
      chain.toggleHeading({ level }).run();
    }
  };

  // Link add/edit handler, relocated verbatim from the former BubbleMenu impl:
  // prompts for a URL and applies it as a link mark; removing an existing link
  // uses the same button when a link is already active.
  const toggleLink = (): void => {
    if (controlDisabled) return;
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
  };

  // FR-18 (task 041) — basic-table insert + edit commands. `insertTable` seeds a
  // 2x2 table with a header row (matches the ui-test "choose a 2x2 table" flow);
  // the row/column commands operate on whichever cell the caret is in, mirroring
  // Word's "insert above/below the current row" semantics.
  const insertTable = (): void => {
    if (controlDisabled) return;
    editor.chain().focus().insertTable({ rows: 2, cols: 2, withHeaderRow: true }).run();
  };
  const addTableRow = (): void => {
    if (controlDisabled) return;
    editor.chain().focus().addRowAfter().run();
  };
  const addTableColumn = (): void => {
    if (controlDisabled) return;
    editor.chain().focus().addColumnAfter().run();
  };
  const deleteTableRow = (): void => {
    if (controlDisabled) return;
    editor.chain().focus().deleteRow().run();
  };
  const deleteTableColumn = (): void => {
    if (controlDisabled) return;
    editor.chain().focus().deleteColumn().run();
  };
  const deleteTable = (): void => {
    if (controlDisabled) return;
    editor.chain().focus().deleteTable().run();
  };

  // Row/column/table edit commands only apply inside a table — `can()` dry-runs the
  // command against the current selection without executing it (same idiom as the
  // Undo/Redo `editor.can().undo()` disabled checks below). `canRunTableCommand` guards
  // against an `editor.can()` shape that does not expose the table commands (e.g. a
  // hand-rolled test double without the Table extension mounted) so the toolbar never
  // throws — it just reports the command as unavailable.
  const canAddRow = canRunTableCommand(editor, 'addRowAfter');
  const canAddColumn = canRunTableCommand(editor, 'addColumnAfter');
  const canDeleteRow = canRunTableCommand(editor, 'deleteRow');
  const canDeleteColumn = canRunTableCommand(editor, 'deleteColumn');
  const canDeleteTable = canRunTableCommand(editor, 'deleteTable');

  const showWordMenu = Boolean(onOpenInWord || onOpenInWordDesktop);
  const openInWordDisabled = controlDisabled || wordActionsDisabled === true;
  const saveDisabled = controlDisabled || canSave !== true || isSaving === true;

  // ---- task 038 guardrails, progressively lifted by R5 (spaarkeai-compose-r5 G3/G4) ----------------
  // The tracked-edit path (loaded/imported docs) originally had NO representation for alignment/heading/
  // list/table, so those controls were DISABLED on a LOADED doc and kept only for a BORN-IN-EDITOR draft
  // (the ComposeDocumentRenderer authors them cleanly). R5 lifts the guards one construct at a time as the
  // ComposeShadowPatchEngine gains each applier:
  //   - R5 task 010 (G3 alignment): alignment RE-ENABLED on loaded docs (tracked w:pPrChange).
  //   - R5 task 011 (G3 heading/list): heading + bullet/ordered list RE-ENABLED on loaded docs (tracked
  //     w:pPrChange for Style/ListOrdered/ListLevel; list numbering reuses R4.5's numbering engine).
  //   - R5 task 014 (G4 table op): STRUCTURAL EDITS of an existing table — add/delete row, add/delete column,
  //     delete table, and cell-content edits — now RE-ENABLED + round-tripping on loaded docs (the client
  //     captures them as the closed-catalog `table` op; the engine emits full tracked table structure —
  //     w:trPr/w:ins+del, w:tcPr/w:cellIns+cellDel, w:tblGridChange, w:tblPrChange). These edit buttons are
  //     already gated only by the read-only `controlDisabled` + `editor.can()` (in-a-table) checks — no
  //     isLoadedBaseline gate — so on a loaded doc they were reachable-but-silently-dropped before task 014;
  //     they now round-trip.
  //   - Insert-table (a BRAND-NEW table) stays loaded-gated: whole-table CREATE is a whole-block author, NOT a
  //     structural edit of an existing table, and is deliberately OUTSIDE the task-004 closed table-op catalog
  //     (which covers InsertRow/DeleteRow/InsertColumn/DeleteColumn/SetCellContent/SetTableProps). Enabling it
  //     on a loaded doc would reintroduce the exact silent-loss NFR-08 forbids, so it remains disabled (honest
  //     "future release" tooltip). Born-in-editor tables stay enabled — the renderer authors them cleanly.
  //   - Hyperlinks remain unrepresentable in BOTH modes until R5 G5 (no mark op, no content-model href).
  const isLoadedBaseline = hasLoadedBaseline === true;
  /** Hover tooltip on a control disabled because its feature is deferred to a future release. */
  const FUTURE_RELEASE_TOOLTIP = 'Available in a future release';
  const deferredIfLoaded = isLoadedBaseline ? FUTURE_RELEASE_TOOLTIP : undefined;
  // Heading + bullet/ordered list — RE-ENABLED on loaded docs (R5 task 011); read-only gate still applies.
  const headingListEditDisabled = controlDisabled;
  // Alignment — re-enabled on loaded docs (R5 task 010); read-only gate still applies.
  const alignmentEditDisabled = controlDisabled;
  // Insert-table (whole-table CREATE) — still loaded-gated (out of the R5 task-014 closed table-op catalog:
  // that op covers structural EDITS of an existing table, not authoring a new one). Enabled born-in-editor
  // (renderer), disabled loaded. Row/column/delete-table EDIT commands are NOT gated here — they round-trip
  // via the table op (G4).
  const tableInsertDisabled = controlDisabled || isLoadedBaseline;
  // G5 (FR-05, task 033): hyperlinks are now representable on BOTH paths — authored (clean w:hyperlink via
  // ComposeDocumentRenderer) and edit (the `Link` mark op → ComposeShadowPatchEngine tracked w:hyperlink).
  // The SDL-4/5 R4 guard is removed; the control follows the same read-only gate as Bold/Italic.
  const hyperlinkDisabled = controlDisabled;

  return (
    <Toolbar
      className={styles.toolbar}
      size="small"
      aria-label="Document formatting"
      data-testid="compose-format-toolbar"
    >
      {/* ---- Body (block/heading style) ---- */}
      {/* R5 task 011 (G3 heading/list): the heading menu is RE-ENABLED on loaded docs — the engine now applies
          a setBlockAttr Style op as a tracked w:pPrChange. Gated only by the read-only `controlDisabled`. */}
      <Menu positioning="below-start">
        <MenuTrigger disableButtonEnhancement>
          <Button
            appearance="subtle"
            size="small"
            disabled={headingListEditDisabled}
            className={styles.menuButton}
            icon={<ChevronDown16Regular />}
            iconPosition="after"
            data-testid="compose-format-heading-menu"
          >
            {currentBlockLabel(editor)}
          </Button>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            <MenuItem onClick={() => setHeading(null)}>Body</MenuItem>
            <MenuItem onClick={() => setHeading(1)}>Heading 1</MenuItem>
            <MenuItem onClick={() => setHeading(2)}>Heading 2</MenuItem>
            <MenuItem onClick={() => setHeading(3)}>Heading 3</MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* ---- Paragraph (lists / blockquote / alignment) ---- */}
      <Menu positioning="below-start">
        <MenuTrigger disableButtonEnhancement>
          <DropdownButton label="Paragraph" disabled={controlDisabled} testId="compose-format-paragraph-menu" />
        </MenuTrigger>
        <MenuPopover>
          <div className={styles.dropdownPalette} role="group" aria-label="Paragraph formatting">
            <PaletteIconButton
              icon={<TextBulletListLtr24Regular />}
              label="Bullet list"
              active={editor.isActive('bulletList')}
              disabled={headingListEditDisabled}
              onClick={() => editor.chain().focus().toggleBulletList().run()}
              testId="compose-format-bullet-list"
            />
            <PaletteIconButton
              icon={<TextNumberListLtr24Regular />}
              label="Numbered list"
              active={editor.isActive('orderedList')}
              disabled={headingListEditDisabled}
              onClick={() => editor.chain().focus().toggleOrderedList().run()}
              testId="compose-format-ordered-list"
            />
            <PaletteIconButton
              icon={<TextQuote24Regular />}
              label="Blockquote"
              active={editor.isActive('blockquote')}
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().toggleBlockquote().run()}
              testId="compose-format-blockquote"
            />
            <PaletteIconButton
              icon={<TextAlignLeft24Regular />}
              label="Align left"
              active={editor.isActive({ textAlign: 'left' })}
              disabled={alignmentEditDisabled}
              onClick={() => editor.chain().focus().setTextAlign('left').run()}
              testId="compose-format-align-left"
            />
            <PaletteIconButton
              icon={<TextAlignCenter24Regular />}
              label="Align center"
              active={editor.isActive({ textAlign: 'center' })}
              disabled={alignmentEditDisabled}
              onClick={() => editor.chain().focus().setTextAlign('center').run()}
              testId="compose-format-align-center"
            />
            <PaletteIconButton
              icon={<TextAlignRight24Regular />}
              label="Align right"
              active={editor.isActive({ textAlign: 'right' })}
              disabled={alignmentEditDisabled}
              onClick={() => editor.chain().focus().setTextAlign('right').run()}
              testId="compose-format-align-right"
            />
          </div>
        </MenuPopover>
      </Menu>

      {/* ---- Font (character formatting) ---- */}
      <Menu positioning="below-start">
        <MenuTrigger disableButtonEnhancement>
          <DropdownButton label="Font" disabled={controlDisabled} testId="compose-format-font-menu" />
        </MenuTrigger>
        <MenuPopover>
          <div className={styles.dropdownPalette} role="group" aria-label="Character formatting">
            <PaletteIconButton
              icon={<TextBold24Regular />}
              label="Bold"
              active={editor.isActive('bold')}
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().toggleBold().run()}
              testId="compose-format-bold"
            />
            <PaletteIconButton
              icon={<TextItalic24Regular />}
              label="Italic"
              active={editor.isActive('italic')}
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().toggleItalic().run()}
              testId="compose-format-italic"
            />
            <PaletteIconButton
              icon={<TextUnderline24Regular />}
              label="Underline"
              active={editor.isActive('underline')}
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().toggleUnderline().run()}
              testId="compose-format-underline"
            />
            <PaletteIconButton
              icon={<TextStrikethrough24Regular />}
              label="Strikethrough"
              active={editor.isActive('strike')}
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().toggleStrike().run()}
              testId="compose-format-strike"
            />
            <PaletteIconButton
              icon={editor.isActive('link') ? <LinkDismiss24Regular /> : <Link24Regular />}
              label={editor.isActive('link') ? 'Remove link' : 'Add link'}
              active={editor.isActive('link')}
              // G5 (FR-05, task 033): hyperlinks now round-trip on both paths — the control follows the
              // read-only gate only (hyperlinkDisabled === controlDisabled), no longer deferred.
              disabled={hyperlinkDisabled}
              onClick={toggleLink}
              testId="compose-format-link"
            />
          </div>
        </MenuPopover>
      </Menu>

      {/* ---- Table (FR-18, task 041) — insert + basic row/column edit ---- */}
      <Menu positioning="below-start">
        <MenuTrigger disableButtonEnhancement>
          <DropdownButton label="Table" disabled={controlDisabled} testId="compose-format-table-menu" />
        </MenuTrigger>
        <MenuPopover>
          <div className={styles.dropdownPalette} role="group" aria-label="Table">
            <PaletteIconButton
              icon={<TableAdd24Regular />}
              label="Insert table"
              disabled={tableInsertDisabled}
              deferredReason={deferredIfLoaded}
              onClick={insertTable}
              testId="compose-format-table-insert"
            />
            <PaletteIconButton
              icon={<TableInsertRow24Regular />}
              label="Add row"
              disabled={controlDisabled || !canAddRow}
              onClick={addTableRow}
              testId="compose-format-table-add-row"
            />
            <PaletteIconButton
              icon={<TableInsertColumn24Regular />}
              label="Add column"
              disabled={controlDisabled || !canAddColumn}
              onClick={addTableColumn}
              testId="compose-format-table-add-column"
            />
            <PaletteIconButton
              icon={<TableDeleteRow24Regular />}
              label="Delete row"
              disabled={controlDisabled || !canDeleteRow}
              onClick={deleteTableRow}
              testId="compose-format-table-delete-row"
            />
            <PaletteIconButton
              icon={<TableDeleteColumn24Regular />}
              label="Delete column"
              disabled={controlDisabled || !canDeleteColumn}
              onClick={deleteTableColumn}
              testId="compose-format-table-delete-column"
            />
            <PaletteIconButton
              icon={<TableDismiss24Regular />}
              label="Delete table"
              disabled={controlDisabled || !canDeleteTable}
              onClick={deleteTable}
              testId="compose-format-table-delete-table"
            />
          </div>
        </MenuPopover>
      </Menu>

      {/* ---- UAT round-4 #12 (2026-08-04): Open Document / Reload from source / Refresh Profile
             are NOT named in the owner's regrouped left→right order below (Review Summary·Memo |
             Word·Save | Review Notes·Track Changes | Undo·Redo·Info). PLACEMENT DECISION — FLAGGED
             for the owner to adjust: kept immediately after the format-menu group (Body/Paragraph/
             Font/Table) and before the (now right-aligned) regrouped action-icon area, preserving
             their pre-round-4 relative order. Nothing was deleted. ---- */}
      {onOpenDocument ? (
        <Tooltip content="Open document" relationship="label" withArrow>
          <ToolbarButton
            appearance="subtle"
            icon={<DocumentText24Regular />}
            aria-label="Open document"
            disabled={controlDisabled}
            onClick={onOpenDocument}
            data-testid="compose-format-open-document"
          />
        </Tooltip>
      ) : null}

      {onReloadFromSource ? (
        <Tooltip content="Reload from source" relationship="label" withArrow>
          <ToolbarButton
            appearance="subtle"
            icon={<ArrowClockwise24Regular />}
            aria-label="Reload from source"
            disabled={controlDisabled}
            onClick={onReloadFromSource}
            data-testid="compose-format-reload-from-source"
          />
        </Tooltip>
      ) : null}

      {/* FR-05 (spaarkeai-compose-r6 task 032) — "Apply firm template": opens the host's
          template-select dialog (ComposeApplyTemplateDialog). Gated like its neighbors (hidden when
          the host wires no handler — an unpersisted doc has nothing saved to merge onto); DISABLED
          with an explanatory tooltip while dirty/transient/saving (`applyTemplateDisabledReason` —
          the server merges the PERSISTED bytes). Semantic tokens only (ADR-021). */}
      {onApplyTemplate ? (
        <Tooltip content={applyTemplateDisabledReason ?? 'Apply firm template'} relationship="description" withArrow>
          <ToolbarButton
            appearance="subtle"
            icon={<PaintBrush24Regular />}
            aria-label="Apply firm template"
            disabled={controlDisabled || Boolean(applyTemplateDisabledReason)}
            onClick={onApplyTemplate}
            data-testid="compose-format-apply-template"
          />
        </Tooltip>
      ) : null}

      {onRefreshProfile ? (
        <Tooltip
          content={isRefreshingProfile ? 'Refreshing document profile…' : 'Refresh document profile'}
          relationship="label"
          withArrow
        >
          <ToolbarButton
            appearance="subtle"
            icon={isRefreshingProfile ? <Spinner size="tiny" /> : <DocumentSync24Regular />}
            aria-label={isRefreshingProfile ? 'Refreshing document profile' : 'Refresh document profile'}
            disabled={controlDisabled || isRefreshingProfile}
            onClick={onRefreshProfile}
            data-testid="compose-format-refresh-profile"
          />
        </Tooltip>
      ) : null}

      {/* Spacer — pushes the regrouped action-icon area (below) to the right edge. */}
      <div className={styles.spacer} />

      {/* ==== Group 1 (UAT round-4 #12): Show/Hide Review Summary · Create Summary Memo ==== */}
      {hasReview && onToggleReviewSummary ? (
        <Tooltip
          content={reviewSummaryOpen ? 'Hide Review Summary' : 'Show Review Summary'}
          relationship="label"
          withArrow
        >
          <ToolbarButton
            appearance={reviewSummaryOpen ? 'primary' : 'subtle'}
            icon={<ClipboardTaskListLtr24Regular />}
            aria-label="Toggle Review Summary"
            aria-pressed={Boolean(reviewSummaryOpen)}
            disabled={controlDisabled}
            onClick={onToggleReviewSummary}
            data-testid="compose-format-review-summary-toggle"
          />
        </Tooltip>
      ) : null}

      {/* ---- Create Summary Memo (FR-14, task 051) — MOVED (UAT round-4 #12, supersedes the round-1
             #3 far-left placement) into Group 1 alongside Review Summary. Rendered ONLY when a review
             is present AND at least one handler is threaded; the trigger shows a spinner + disables
             both items while a memo fetch is in flight — never a silent empty export. The accessible
             NAME lives on `aria-label` (icon-only); a Tooltip carries the full label on hover
             (ADR-021/a11y — icon-only needs both). ---- */}
      {hasReview && (onGenerateMemo || onEmailMemo) ? (
        <Menu positioning="below-start">
          <MenuTrigger disableButtonEnhancement>
            {(triggerProps: MenuButtonProps) => (
              <Tooltip content="Create Summary Memo" relationship="label" withArrow>
                <Button
                  {...triggerProps}
                  appearance="subtle"
                  size="small"
                  icon={isMemoActionInFlight ? <Spinner size="tiny" /> : <DocumentBulletList24Regular />}
                  aria-label="Create Summary Memo"
                  disabled={controlDisabled || isMemoActionInFlight}
                  data-testid="compose-format-memo-menu"
                />
              </Tooltip>
            )}
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem
                icon={<ArrowDownload24Regular />}
                disabled={!onGenerateMemo || controlDisabled || isMemoActionInFlight}
                onClick={() => onGenerateMemo?.()}
                data-testid="compose-format-memo-generate"
              >
                Generate memo (.docx)
              </MenuItem>
              <MenuItem
                icon={<Mail24Regular />}
                disabled={!onEmailMemo || controlDisabled || isMemoActionInFlight}
                onClick={() => onEmailMemo?.()}
                data-testid="compose-format-memo-email"
              >
                Email memo
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      ) : null}

      <ToolbarDivider data-testid="compose-format-divider-1" />

      {/* ==== Group 2 (UAT round-4 #11/#12): Word · Save ====
             MENU ALLOCATION FIX (#11): the Word dropdown's former "Save Version"/"Save New Document"
             duplicate (the round-1 "UX-1 parity affordance") is REMOVED — the owner explicitly
             rejected that overlap. Word now contains ONLY the two Open-in-Word handoff actions; Save
             is the ONE split-button (Save Version primary / Save New Document caret). Zero functional
             overlap between the two menus. ---- */}
      {showWordMenu ? (
        <Menu positioning="below-end">
          <MenuTrigger disableButtonEnhancement>
            {(triggerProps: MenuButtonProps) => (
              <Tooltip content="Word" relationship="label" withArrow>
                <Button
                  {...triggerProps}
                  appearance="subtle"
                  size="small"
                  icon={<DocumentWordRegular />}
                  aria-label="Word"
                  disabled={controlDisabled}
                  data-testid="compose-format-word-menu"
                />
              </Tooltip>
            )}
          </MenuTrigger>
          <MenuPopover>
            <div className={styles.wordMenuColumn} role="group" aria-label="Word document actions">
              {onOpenInWord ? (
                <Button
                  appearance="subtle"
                  size="small"
                  className={styles.wordMenuItem}
                  icon={<OpenRegular />}
                  aria-label="Open in Word for the Web"
                  disabled={openInWordDisabled}
                  onClick={onOpenInWord}
                  data-testid="compose-format-open-word-web"
                >
                  Open in Word (web)
                </Button>
              ) : null}
              {onOpenInWordDesktop ? (
                <Button
                  appearance="subtle"
                  size="small"
                  className={styles.wordMenuItem}
                  icon={<DesktopRegular />}
                  aria-label="Open in the Word desktop app"
                  disabled={openInWordDisabled}
                  onClick={onOpenInWordDesktop}
                  data-testid="compose-format-open-word-desktop"
                >
                  Open in Word (desktop)
                </Button>
              ) : null}
            </div>
          </MenuPopover>
        </Menu>
      ) : null}

      {/* ---- Save split-button (G7 task 022) — the ONE save entry point (#11: no longer duplicated
             inside the Word menu). UAT round-1 #5 (2026-08-03): ICON-ONLY — the visible "Save
             Version" text was dropped. Primary action = "Save Version" (replace in place /
             transient-key dedup); the caret menu carries "Save New Document" (a deliberate fork).
             Fluent v9 SplitButton, theme tokens only (ADR-021 dark-mode). Mirrors the blessed
             ComposerActionBar Send split-button pattern. The accessible NAME survives via the
             primaryActionButton's `aria-label`; a Tooltip carries the full label on hover (icon-only
             buttons need both — ADR-021/a11y). ---- */}
      {onSave ? (
        <Menu
          positioning="below-end"
          // FR-01 (task 020): the Auto Save toggle is a checkable menu item. Its checked state is
          // controlled by the host (autoSaveEnabled); rendered only when the host wired autosave
          // (both autoSaveEnabled + onAutoSaveToggle set — the FR-03 Phase-4 behavior lives there).
          checkedValues={
            autoSaveEnabled !== undefined && onAutoSaveToggle
              ? { autosave: autoSaveEnabled ? ['on'] : [] }
              : undefined
          }
          onCheckedValueChange={(_e, data) => {
            if (data.name === 'autosave') onAutoSaveToggle?.(data.checkedItems.includes('on'));
          }}
        >
          <MenuTrigger disableButtonEnhancement>
            {(triggerProps: MenuButtonProps) => (
              <Tooltip content={isSaving ? 'Saving…' : 'Save'} relationship="label" withArrow>
                <SplitButton
                  appearance="subtle"
                  data-testid="compose-format-save"
                  menuButton={{ ...triggerProps, 'aria-label': 'Save options' }}
                  primaryActionButton={{
                    onClick: () => onSave('version'),
                    disabled: saveDisabled,
                    icon: <SaveRegular />,
                    'aria-label': isSaving ? 'Saving' : 'Save',
                  }}
                />
              </Tooltip>
            )}
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {/* FR-01 (task 020): Save = append an SPE version to the SAME document (ADR-049).
                  Explicit item so the split-button's primary action is discoverable in the menu too. */}
              <MenuItem
                icon={<SaveRegular />}
                disabled={saveDisabled}
                onClick={() => onSave('version')}
                data-testid="compose-format-save-version"
              >
                Save
              </MenuItem>
              {/* FR-01/FR-07a (task 020/012): Save As = a REAL fork — a distinct new sprk_document with a
                  uniquified filename (never a silent re-version of the original). Replaces R6's
                  "Save New Document". */}
              <MenuItem
                icon={<SaveRegular />}
                disabled={saveDisabled}
                onClick={() => onSave('new')}
                data-testid="compose-format-save-new"
              >
                Save As
              </MenuItem>
              {autoSaveEnabled !== undefined && onAutoSaveToggle ? (
                <>
                  <MenuDivider />
                  <MenuItemCheckbox
                    name="autosave"
                    value="on"
                    data-testid="compose-format-autosave-toggle"
                  >
                    Auto Save
                  </MenuItemCheckbox>
                </>
              ) : null}
            </MenuList>
          </MenuPopover>
        </Menu>
      ) : null}

      {/* FR-03 (task 041): save-state indicator (absorbs R6 D6). Reflects task-040 draft/dirty state:
          Saving… while a save is in flight, Unsaved when there are dirty edits, Saved otherwise — plus
          the Auto Save On/Off state. Rendered only when the host tracks save state (Save wired +
          hasUnsavedEdits provided). Fluent v9 semantic tokens only (ADR-021 dark-mode); `aria-live` so
          the state change is announced to assistive tech. */}
      {onSave && hasUnsavedEdits !== undefined ? (
        <Text
          className={styles.saveStateIndicator}
          data-testid="compose-save-state-indicator"
          data-save-state={isSaving ? 'saving' : hasUnsavedEdits ? 'unsaved' : 'saved'}
          aria-live="polite"
        >
          {isSaving ? <Spinner size="extra-tiny" aria-hidden /> : null}
          {isSaving ? 'Saving…' : hasUnsavedEdits ? 'Unsaved' : 'Saved'}
          {autoSaveEnabled !== undefined ? ` · Auto Save ${autoSaveEnabled ? 'On' : 'Off'}` : ''}
        </Text>
      ) : null}

      <ToolbarDivider data-testid="compose-format-divider-2" />

      {/* ==== Group 3 (UAT round-4 #12): Add Comment · Show/Hide Review Notes · Track Changes ==== */}
      {/* ---- Add Comment (FR-10 / R6 D7, task 072) — the re-exposed UI entry point onto the SHIPPED
             comment machinery (host `onToggleComments` = handleToggleComments, which captures the live
             selection into a pendingRange and opens the ComposeCommentThread composer). ICON-ONLY toggle
             mirroring Track Changes: primary/subtle appearance carries the on/off state (ADR-021 —
             semantic tokens only, dark-mode-correct), accessible name + pressed state + tooltip retained. ---- */}
      {onToggleComments ? (
        <Tooltip content="Add a comment on the selected text" relationship="label" withArrow>
          <ToolbarButton
            appearance={commentsOpen ? 'primary' : 'subtle'}
            icon={<CommentAdd24Regular />}
            aria-label="Add comment"
            aria-pressed={commentsOpen === true}
            disabled={controlDisabled}
            onClick={onToggleComments}
            data-testid="compose-format-add-comment"
          />
        </Tooltip>
      ) : null}

      {hasReview && onToggleReviewNotes ? (
        <Tooltip content={reviewNotesOpen ? 'Hide Review Notes' : 'Show Review Notes'} relationship="label" withArrow>
          <ToolbarButton
            appearance={reviewNotesOpen ? 'primary' : 'subtle'}
            icon={<CommentMultiple24Regular />}
            aria-label="Toggle Review Notes"
            aria-pressed={Boolean(reviewNotesOpen)}
            disabled={controlDisabled}
            onClick={onToggleReviewNotes}
            data-testid="compose-format-review-notes-toggle"
          />
        </Tooltip>
      ) : null}

      {/* ---- Track Changes toggle (item 4, UAT round-4) — task 039 P1: ICON-ONLY. The visible
             "Track changes" text label was dropped for an icon-only toggle; the accessible NAME
             (aria-label) + pressed state (aria-pressed) + the descriptive Tooltip are all retained
             (ADR-021 — the primary/subtle appearance carries the on/off state, dark-mode-correct). ---- */}
      {onToggleTrackChanges ? (
        <Tooltip
          content={
            trackChangesEnabled
              ? 'Track changes is on — your edits show as redlines and save as tracked changes'
              : 'Track changes is off — turn on to see your edits as redlines'
          }
          relationship="label"
          withArrow
        >
          <ToolbarButton
            appearance={trackChangesEnabled ? 'primary' : 'subtle'}
            icon={<DocumentEdit24Regular />}
            aria-pressed={trackChangesEnabled === true}
            aria-label="Toggle track changes"
            disabled={controlDisabled}
            onClick={onToggleTrackChanges}
            data-testid="compose-format-track-changes"
          />
        </Tooltip>
      ) : null}

      <ToolbarDivider data-testid="compose-format-divider-3" />

      {/* ==== Group 4 (UAT round-4 #12): Undo · Redo · Info ==== */}
      <ToolbarButton
        appearance="subtle"
        icon={<ArrowUndo24Regular />}
        aria-label="Undo"
        disabled={controlDisabled || !editor.can().undo()}
        onClick={() => editor.chain().focus().undo().run()}
        data-testid="compose-format-undo"
      />

      <ToolbarButton
        appearance="subtle"
        icon={<ArrowRedo24Regular />}
        aria-label="Redo"
        disabled={controlDisabled || !editor.can().redo()}
        onClick={() => editor.chain().focus().redo().run()}
        data-testid="compose-format-redo"
      />

      {/* UAT round-6 #4 — the not-legal-advice warning, moved OUT of the Review Summary body to a far-
          right info (ⓘ) button. Shown only when an NDA advisory review is present (reviewDisclaimer set). */}
      {hasReview && reviewDisclaimer ? (
        <Popover withArrow positioning="below-end" size="small">
          <PopoverTrigger disableButtonEnhancement>
            <ToolbarButton
              appearance="subtle"
              icon={<Info24Regular />}
              aria-label="About this review"
              data-testid="compose-format-review-info"
            />
          </PopoverTrigger>
          <PopoverSurface data-testid="compose-format-review-info-popover">
            <Text size={200} className={styles.disclaimerPopover}>
              {reviewDisclaimer}
            </Text>
          </PopoverSurface>
        </Popover>
      ) : null}
    </Toolbar>
  );
}
