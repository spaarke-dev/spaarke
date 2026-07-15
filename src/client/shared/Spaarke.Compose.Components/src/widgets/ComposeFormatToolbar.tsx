/**
 * ComposeFormatToolbar.tsx — single-row document command toolbar for ComposeEditor.
 *
 * FIX #5 (spaarkeai-compose-r2 UAT): the toolbar was consolidated from TWO wrapping
 * rows of individual icon buttons into ONE row grouped behind labelled Fluent v9
 * `Menu` dropdowns. The dropdown TRIGGER buttons carry WORD labels (Body / Paragraph
 * / Font / Word); the tools INSIDE each dropdown are ICON-only buttons with a hover
 * Tooltip naming each command. `Save` + `Undo`/`Redo` are icon-only buttons pushed
 * to the RIGHT edge (a `flex:1` spacer). This is a REORGANIZATION only — every
 * command previously reachable stays wired with its disabled/active state intact.
 *
 * Dropdown map:
 *   - Body      — block/heading style (unchanged from task 111).
 *   - Paragraph — bullet list, numbered list, blockquote, align left/center/right.
 *   - Font      — bold, italic, underline, strikethrough, link.
 *   - Word      — Open in Word Web, Open in Word Desktop, Push to Word. These were
 *                 previously top-level actions on the separate `ComposeToolbar`
 *                 command bar (rendered by ComposeWorkspace). The host now binds the
 *                 handlers (`onOpenInWord` / `onOpenInWordDesktop` / `onPushToWord`)
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
  ChevronDown16Regular,
  OpenRegular,
  DesktopRegular,
  ArrowUploadRegular,
  SaveRegular,
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
  /** Render accepted annotations into the .docx as native Word track-changes + comments. */
  onPushToWord?: () => void;
  /** Disables the two Open-in-Word items (no persisted document, or an action is in flight). */
  wordActionsDisabled?: boolean;
  /** True when there is something to push (persisted doc + ≥1 accepted annotation). */
  canPushToWord?: boolean;
  /** True while a push-to-Word is in flight. */
  isPushingToWord?: boolean;

  // ---- Save (FIX #5) — icon button, right-aligned; rendered only when `onSave` set ----
  /** Save handler (create-on-save first Save, or update). */
  onSave?: () => void;
  /** True when Save should be enabled (unsaved edit OR unpersisted transient draft). */
  canSave?: boolean;
  /** True while a save is in flight. */
  isSaving?: boolean;
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
}): React.JSX.Element {
  return (
    // The Button carries the accessible NAME via `aria-label`; the Tooltip is therefore a
    // `description` (not a second `label`) so the two do not both claim the accessible name.
    <Tooltip content={props.label} relationship="description" withArrow>
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
    onPushToWord,
    wordActionsDisabled,
    canPushToWord,
    isPushingToWord,
    onSave,
    canSave,
    isSaving,
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

  const showWordMenu = Boolean(onOpenInWord || onOpenInWordDesktop || onPushToWord);
  const openInWordDisabled = controlDisabled || wordActionsDisabled === true;
  const pushDisabled = controlDisabled || canPushToWord !== true || isPushingToWord === true;
  const saveDisabled = controlDisabled || canSave !== true || isSaving === true;

  return (
    <Toolbar
      className={styles.toolbar}
      size="small"
      aria-label="Document formatting"
      data-testid="compose-format-toolbar"
    >
      {/* ---- Body (block/heading style) ---- */}
      <Menu positioning="below-start">
        <MenuTrigger disableButtonEnhancement>
          <Button
            appearance="subtle"
            size="small"
            disabled={controlDisabled}
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
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().toggleBulletList().run()}
              testId="compose-format-bullet-list"
            />
            <PaletteIconButton
              icon={<TextNumberListLtr24Regular />}
              label="Numbered list"
              active={editor.isActive('orderedList')}
              disabled={controlDisabled}
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
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().setTextAlign('left').run()}
              testId="compose-format-align-left"
            />
            <PaletteIconButton
              icon={<TextAlignCenter24Regular />}
              label="Align center"
              active={editor.isActive({ textAlign: 'center' })}
              disabled={controlDisabled}
              onClick={() => editor.chain().focus().setTextAlign('center').run()}
              testId="compose-format-align-center"
            />
            <PaletteIconButton
              icon={<TextAlignRight24Regular />}
              label="Align right"
              active={editor.isActive({ textAlign: 'right' })}
              disabled={controlDisabled}
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
              disabled={controlDisabled}
              onClick={toggleLink}
              testId="compose-format-link"
            />
          </div>
        </MenuPopover>
      </Menu>

      {/* ---- Word (Open in Web / Desktop / Push to Word) ---- */}
      {showWordMenu ? (
        <Menu positioning="below-start">
          <MenuTrigger disableButtonEnhancement>
            <DropdownButton label="Word" disabled={controlDisabled} testId="compose-format-word-menu" />
          </MenuTrigger>
          <MenuPopover>
            <div className={styles.dropdownPalette} role="group" aria-label="Word document actions">
              {onOpenInWord ? (
                <PaletteIconButton
                  icon={<OpenRegular />}
                  label="Open in Word for Web"
                  disabled={openInWordDisabled}
                  onClick={onOpenInWord}
                  testId="compose-format-open-word-web"
                />
              ) : null}
              {onOpenInWordDesktop ? (
                <PaletteIconButton
                  icon={<DesktopRegular />}
                  label="Open in Word Desktop"
                  disabled={openInWordDisabled}
                  onClick={onOpenInWordDesktop}
                  testId="compose-format-open-word-desktop"
                />
              ) : null}
              {onPushToWord ? (
                <PaletteIconButton
                  icon={<ArrowUploadRegular />}
                  label={isPushingToWord ? 'Pushing to Word…' : 'Push to Word'}
                  disabled={pushDisabled}
                  onClick={onPushToWord}
                  testId="compose-format-push-to-word"
                />
              ) : null}
            </div>
          </MenuPopover>
        </Menu>
      ) : null}

      {/* Spacer — pushes Save + Undo/Redo to the right edge. */}
      <div className={styles.spacer} />

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
    </Toolbar>
  );
}
