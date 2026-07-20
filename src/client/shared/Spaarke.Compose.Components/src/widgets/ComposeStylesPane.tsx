/**
 * ComposeStylesPane.tsx — FR-22 styles pane, apply-existing-only (spaarkeai-compose-r3 task 043).
 *
 * A small, self-contained Fluent v9 panel listing the loaded document's EXISTING paragraph styles
 * with a single apply-to-selection action per style. All catalog-derivation + apply LOGIC lives in
 * {@link useComposeDocumentStyles} — this component is a thin, presentational binding (mirrors
 * {@link ComposeFormatToolbar}'s theming conventions: `makeStyles` + semantic `tokens.*` only, no
 * hardcoded colors — ADR-021; and {@link ComposeFindReplace}'s `editor`/`open`/`onClose` panel shape).
 *
 * SCOPE GUARD (binding, FR-22 + spec Assumptions): apply EXISTING styles ONLY. This component
 * DELIBERATELY renders NO create/rename/delete/manage-style affordance — no "New style" button, no
 * inline rename, no delete icon, no options menu. Full style management is Word-parity, out of scope,
 * deferred to Open-in-Word (design §7.5). A scope-guard test in `ComposeStylesPane.test.tsx` asserts
 * this absence directly (queries for common authoring-verb strings/testids and expects none found).
 *
 * Mount pattern (ComposeEditor.tsx): rendered conditionally (`open` prop), docked between the format
 * toolbar and the editor surface — the SAME slot convention as `ComposeFindReplace` /
 * `ComposeCommentThread` — toggled by its own floating "Styles" button (mirrors `commentsToggleFab`,
 * pinned to the opposite top-left corner so the two toggles never collide).
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: no existing styles pane. `ComposeFormatToolbar` applies INLINE character formatting
 *     (bold/italic) — not named paragraph styles. `docxBridge.ts` exposes no style-catalog UI. No
 *     overlap.
 *   - Extension: cannot fold into `ComposeFormatToolbar` (inline formatting is a distinct concern from
 *     applying a named document style) — this is a cohesive apply-only surface with its own list
 *     state that warrants a dedicated component + hook, reusing the document's own live style usage
 *     rather than adding a new data source.
 *   - Cost-of-doing-nothing: without it, a user cannot reapply the document's own paragraph styles
 *     (e.g. "Heading 2") to newly typed or reformatted text — FR-22's acceptance ("applying a style
 *     changes the paragraph's `pStyle`") cannot be met.
 *
 * @see ./hooks/useComposeDocumentStyles.ts — the existing-style catalog + apply-to-selection logic
 * @see ./ComposeFormatToolbar.tsx — the sibling toolbar this mirrors theming conventions from
 * @see ./ComposeFindReplace.tsx — the sibling docked panel this mirrors shape/props conventions from
 */
import * as React from 'react';
import { type Editor } from '@tiptap/react';
import { Button, Text, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import { Dismiss16Regular } from '@fluentui/react-icons';
import { useComposeDocumentStyles } from './hooks/useComposeDocumentStyles';

const useStyles = makeStyles({
  panel: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  list: {
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalXS,
    rowGap: tokens.spacingVerticalXS,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalS,
  },
});

export interface ComposeStylesPaneProps {
  editor: Editor | null;
  /** Whether the pane is mounted/visible. When false, this component renders nothing. */
  open: boolean;
  /** Called when the user dismisses the pane (close button). */
  onClose: () => void;
}

/**
 * The FR-22 styles pane. See file-level JSDoc for the SCOPE GUARD contract: this component renders
 * ONLY a list + an apply action per existing style — no authoring control of any kind.
 */
export function ComposeStylesPane(props: ComposeStylesPaneProps): React.JSX.Element | null {
  const styles = useStyles();
  const { editor, open, onClose } = props;
  const doc = useComposeDocumentStyles(editor);

  if (!open) return null;

  return (
    <div className={styles.panel} role="region" aria-label="Styles" data-testid="compose-styles-pane">
      <div className={styles.header}>
        <Text size={200} weight="semibold">
          Styles
        </Text>
        <Tooltip content="Close styles pane" relationship="description" withArrow>
          <Button
            appearance="subtle"
            size="small"
            icon={<Dismiss16Regular />}
            aria-label="Close styles pane"
            onClick={onClose}
            data-testid="compose-styles-pane-close"
          />
        </Tooltip>
      </div>
      {doc.styles.length === 0 ? (
        <Text size={200} className={styles.empty} data-testid="compose-styles-pane-empty">
          This document has no styled paragraphs yet.
        </Text>
      ) : (
        <div className={styles.list} role="listbox" aria-label="Existing document styles">
          {doc.styles.map(style => (
            <Tooltip key={style.styleId} content={`Apply “${style.displayName}”`} relationship="description" withArrow>
              <Button
                appearance={doc.activeStyleId === style.styleId ? 'primary' : 'secondary'}
                size="small"
                aria-pressed={doc.activeStyleId === style.styleId}
                role="option"
                aria-selected={doc.activeStyleId === style.styleId}
                onClick={() => doc.applyStyle(style.styleId)}
                data-testid={`compose-styles-pane-apply-${style.styleId}`}
              >
                {style.displayName}
              </Button>
            </Tooltip>
          ))}
        </div>
      )}
    </div>
  );
}

export default ComposeStylesPane;
