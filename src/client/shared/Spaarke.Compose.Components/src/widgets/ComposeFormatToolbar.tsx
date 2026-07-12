/**
 * ComposeFormatToolbar.tsx — block-level formatting toolbar for ComposeEditor.
 *
 * Renders a persistent Fluent v9 Toolbar above the TipTap editor with the
 * block-level controls users cannot access via selection-only UI (headings,
 * lists, blockquote, alignment, undo/redo) PLUS the inline character-format
 * controls (bold / italic / underline / strikethrough / link).
 *
 * TASK 111 (UAT-R2, 2026-07-10): the inline character-format controls
 * (Bold / Italic / Underline / Strikethrough / Link) were RELOCATED here from
 * the TipTap selection BubbleMenu (owner decision — the BubbleMenu is now
 * AI-actions ONLY; see ComposeEditor.tsx). They are always-visible top-toolbar
 * controls now, grouped after the align controls behind a `ToolbarDivider`.
 * The active-state highlighting (`isActive('bold')` etc.) and the Link
 * add/edit `window.prompt` flow are preserved byte-for-byte from the former
 * BubbleMenu implementation — no formatting capability was lost, only relocated.
 *
 * Extensions consumed here MUST match the LOCKED_EXTENSIONS list in
 * ComposeEditor.tsx (StarterKit headings 1–3 subset + Bold/Italic/Strike,
 * BulletList, OrderedList, Blockquote, TextAlign, History; Underline + Link
 * are the additive `@tiptap/extension-underline` / `@tiptap/extension-link`).
 * Adding a button here without loading the corresponding extension will make
 * TipTap silently ignore the command.
 *
 * @see ComposeEditor.tsx (host + AI-only BubbleMenu wiring)
 */

import * as React from 'react';
import { type Editor } from '@tiptap/react';
import {
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  makeStyles,
  tokens,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Button,
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
    flexWrap: 'wrap',
    // DEF-16 (UAT-R3): pin the formatting toolbar to the top of the editor
    // scroll region so it stays reachable while a long document body scrolls
    // beneath it. `position: sticky; top: 0` pins the bar to the top of
    // whichever ancestor actually scrolls: in the healthy layout the sibling
    // `.editorSurface` is the scroller and the bar (its preceding sibling)
    // never moves — sticky is inert; but when the height chain collapses in an
    // embedded host and an OUTER container scrolls with the toolbar inside the
    // flow, sticky keeps it visible. The opaque background + z-index prevent
    // scrolled content bleeding through the pinned bar. Semantic tokens only
    // (ADR-021 dark-mode-correct).
    position: 'sticky',
    top: 0,
    zIndex: 1,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  headingMenuButton: {
    minWidth: '96px',
  },
});

export interface ComposeFormatToolbarProps {
  editor: Editor | null;
  /** Applies a disabled visual + non-interactive state to every control. */
  disabled?: boolean;
}

/**
 * Currently-selected block level, derived from the editor. Drives the label
 * on the heading menu button so operators see what block their cursor is in.
 */
function currentBlockLabel(editor: Editor | null): string {
  if (!editor) return 'Body';
  if (editor.isActive('heading', { level: 1 })) return 'Heading 1';
  if (editor.isActive('heading', { level: 2 })) return 'Heading 2';
  if (editor.isActive('heading', { level: 3 })) return 'Heading 3';
  return 'Body';
}

export function ComposeFormatToolbar(props: ComposeFormatToolbarProps): React.JSX.Element | null {
  const styles = useStyles();
  const { editor, disabled } = props;

  // Re-render on selection/transaction to keep the "active" highlight in sync.
  // TipTap doesn't force a parent re-render on selection change, so subscribe
  // to the editor's transaction event and bump a local counter.
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

  // Task 111 — Link add/edit handler, relocated verbatim from the former
  // BubbleMenu implementation (ComposeEditor.tsx `toggleLink`): prompts for a
  // URL and applies it as a link mark to the current selection; removing an
  // existing link uses the same button when a link is already active.
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

  return (
    <Toolbar
      className={styles.toolbar}
      size="small"
      aria-label="Document formatting"
      data-testid="compose-format-toolbar"
    >
      <Menu positioning="below-start">
        <MenuTrigger disableButtonEnhancement>
          <Button
            appearance="subtle"
            size="small"
            disabled={controlDisabled}
            className={styles.headingMenuButton}
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

      <ToolbarDivider />

      <ToolbarButton
        appearance={editor.isActive('bulletList') ? 'primary' : 'subtle'}
        icon={<TextBulletListLtr24Regular />}
        aria-label="Bullet list"
        aria-pressed={editor.isActive('bulletList')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleBulletList().run()}
        data-testid="compose-format-bullet-list"
      />

      <ToolbarButton
        appearance={editor.isActive('orderedList') ? 'primary' : 'subtle'}
        icon={<TextNumberListLtr24Regular />}
        aria-label="Numbered list"
        aria-pressed={editor.isActive('orderedList')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleOrderedList().run()}
        data-testid="compose-format-ordered-list"
      />

      <ToolbarButton
        appearance={editor.isActive('blockquote') ? 'primary' : 'subtle'}
        icon={<TextQuote24Regular />}
        aria-label="Blockquote"
        aria-pressed={editor.isActive('blockquote')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleBlockquote().run()}
        data-testid="compose-format-blockquote"
      />

      <ToolbarDivider />

      <ToolbarButton
        appearance={editor.isActive({ textAlign: 'left' }) ? 'primary' : 'subtle'}
        icon={<TextAlignLeft24Regular />}
        aria-label="Align left"
        aria-pressed={editor.isActive({ textAlign: 'left' })}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().setTextAlign('left').run()}
        data-testid="compose-format-align-left"
      />

      <ToolbarButton
        appearance={editor.isActive({ textAlign: 'center' }) ? 'primary' : 'subtle'}
        icon={<TextAlignCenter24Regular />}
        aria-label="Align center"
        aria-pressed={editor.isActive({ textAlign: 'center' })}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().setTextAlign('center').run()}
        data-testid="compose-format-align-center"
      />

      <ToolbarButton
        appearance={editor.isActive({ textAlign: 'right' }) ? 'primary' : 'subtle'}
        icon={<TextAlignRight24Regular />}
        aria-label="Align right"
        aria-pressed={editor.isActive({ textAlign: 'right' })}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().setTextAlign('right').run()}
        data-testid="compose-format-align-right"
      />

      <ToolbarDivider />

      {/* ===================================================================
          INLINE CHARACTER-FORMAT GROUP — task 111 (UAT-R2). Relocated from the
          selection BubbleMenu (now AI-only). Active-state highlight + Link
          add/edit `window.prompt` flow preserved verbatim from the former
          BubbleMenu impl. See file header "TASK 111".
          =================================================================== */}
      <ToolbarButton
        appearance={editor.isActive('bold') ? 'primary' : 'subtle'}
        icon={<TextBold24Regular />}
        aria-label="Bold"
        aria-pressed={editor.isActive('bold')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleBold().run()}
        data-testid="compose-format-bold"
      />

      <ToolbarButton
        appearance={editor.isActive('italic') ? 'primary' : 'subtle'}
        icon={<TextItalic24Regular />}
        aria-label="Italic"
        aria-pressed={editor.isActive('italic')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleItalic().run()}
        data-testid="compose-format-italic"
      />

      <ToolbarButton
        appearance={editor.isActive('underline') ? 'primary' : 'subtle'}
        icon={<TextUnderline24Regular />}
        aria-label="Underline"
        aria-pressed={editor.isActive('underline')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleUnderline().run()}
        data-testid="compose-format-underline"
      />

      <ToolbarButton
        appearance={editor.isActive('strike') ? 'primary' : 'subtle'}
        icon={<TextStrikethrough24Regular />}
        aria-label="Strikethrough"
        aria-pressed={editor.isActive('strike')}
        disabled={controlDisabled}
        onClick={() => editor.chain().focus().toggleStrike().run()}
        data-testid="compose-format-strike"
      />

      <ToolbarButton
        appearance={editor.isActive('link') ? 'primary' : 'subtle'}
        icon={editor.isActive('link') ? <LinkDismiss24Regular /> : <Link24Regular />}
        aria-label={editor.isActive('link') ? 'Remove link' : 'Add link'}
        aria-pressed={editor.isActive('link')}
        disabled={controlDisabled}
        onClick={toggleLink}
        data-testid="compose-format-link"
      />

      <ToolbarDivider />

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
