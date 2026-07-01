/**
 * ComposeFormatToolbar.tsx — block-level formatting toolbar for ComposeEditor.
 *
 * Renders a persistent Fluent v9 Toolbar above the TipTap editor with the
 * block-level controls users cannot access via selection-only UI (headings,
 * lists, blockquote, alignment, undo/redo).
 *
 * Inline controls (Bold / Italic / Underline / Strike / Link) live in a
 * TipTap BubbleMenu overlayed on the selection — see ComposeEditor.tsx.
 *
 * Extensions consumed here MUST match the LOCKED_EXTENSIONS list in
 * ComposeEditor.tsx (StarterKit headings 1–3 subset, BulletList, OrderedList,
 * Blockquote, TextAlign, History). Adding a button here without loading the
 * corresponding extension will make TipTap silently ignore the command.
 *
 * @see ComposeEditor.tsx (host + BubbleMenu wiring)
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
