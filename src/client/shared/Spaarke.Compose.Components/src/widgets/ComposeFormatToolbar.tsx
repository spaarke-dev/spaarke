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
 *                 when the host wires no Word handlers.
 *   - Save      — icon button (right-aligned); rendered only when `onSave` is wired.
 *   - Undo/Redo — icon buttons (right-aligned).
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
  Button,
  Tooltip,
  makeStyles,
  tokens,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Text,
} from '@fluentui/react-components';
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

  // ---- Save (FIX #5) — icon button, right-aligned; rendered only when `onSave` set ----
  /** Save handler (create-on-save first Save, or update). */
  onSave?: () => void;
  /** True when Save should be enabled (unsaved edit OR unpersisted transient draft). */
  canSave?: boolean;
  /** True while a save is in flight. */
  isSaving?: boolean;

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
    trackChangesEnabled,
    onToggleTrackChanges,
    hasReview,
    reviewSummaryOpen,
    onToggleReviewSummary,
    reviewNotesOpen,
    onToggleReviewNotes,
    reviewDisclaimer,
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
  // Hyperlinks are not representable in EITHER mode in R4 (R5 G5).
  const hyperlinkDisabled = true;

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
              // task 038: hyperlinks are not representable in R4 in EITHER mode (no mark op, no
              // content-model href — R5 G5). Disabled everywhere; controlDisabled is subsumed.
              disabled={controlDisabled || hyperlinkDisabled}
              deferredReason={FUTURE_RELEASE_TOOLTIP}
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

      {/* ---- Word (Open web / Open desktop handoff) ----
             task 039 P3: rendered as a VERTICAL, labelled dropdown (icon + text row per action).
             task 039 P4: there is NO "Push to Word" control here — the DocxAnnotationWriter push-to-Word
             surface was fully retired in task 036, so the only Word items are the Open-in-Web/Desktop
             SPE launch handoff (+ the deliberate Save duplicate). Nothing to remove. */}
      {showWordMenu ? (
        <Menu positioning="below-start">
          <MenuTrigger disableButtonEnhancement>
            <DropdownButton label="Word" disabled={controlDisabled} testId="compose-format-word-menu" />
          </MenuTrigger>
          <MenuPopover>
            <div className={styles.wordMenuColumn} role="group" aria-label="Word document actions">
              {/* UX-1 (UAT 2026-07-19): a deliberate DUPLICATE of the right-aligned Save
                  button — users instinctively look in the Word menu to save. Same handler
                  (`onSave`) + same enable predicate (`saveDisabled`); rendered only when
                  the host wires Save. */}
              {onSave ? (
                <Button
                  appearance="subtle"
                  size="small"
                  className={styles.wordMenuItem}
                  icon={<SaveRegular />}
                  disabled={saveDisabled}
                  onClick={onSave}
                  data-testid="compose-format-word-save"
                >
                  {isSaving ? 'Saving…' : 'Save'}
                </Button>
              ) : null}
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
                  Open web
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
                  Open desktop
                </Button>
              ) : null}
            </div>
          </MenuPopover>
        </Menu>
      ) : null}

      {/* Spacer — pushes Review + Track Changes + Save + Undo/Redo to the right edge. */}
      <div className={styles.spacer} />

      {/* ---- Review Summary + Review Notes (UAT round-7 #5) — TWO SEPARATE icon toggles (the former
             single dropdown was split per reviewer request). Each shows/hides its own surface; the
             primary/subtle appearance + aria-pressed carry the on/off state. Rendered ONLY when an NDA
             advisory review is present. ---- */}
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

      {/* ---- Track Changes toggle (item 4, UAT round-4) — task 039 P1: ICON-ONLY, right-aligned.
             The visible "Track changes" text label was dropped for an icon-only toggle; the accessible
             NAME (aria-label) + pressed state (aria-pressed) + the descriptive Tooltip are all retained
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

      {/* ---- Save (icon-only, right-aligned) ---- */}
      {onSave ? (
        <Tooltip content={isSaving ? 'Saving…' : 'Save changes'} relationship="label" withArrow>
          <ToolbarButton
            appearance="subtle"
            icon={<SaveRegular />}
            aria-label={isSaving ? 'Saving' : 'Save changes'}
            disabled={saveDisabled}
            onClick={onSave}
            data-testid="compose-format-save"
          />
        </Tooltip>
      ) : null}

      {/* ---- Undo / Redo (icon-only, right-aligned) ---- */}
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
