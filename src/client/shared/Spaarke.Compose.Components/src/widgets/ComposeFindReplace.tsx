/**
 * ComposeFindReplace.tsx — FR-17 in-editor find/replace panel for ComposeEditor
 * (spaarkeai-compose-r3 task 040).
 *
 * A small, self-contained Fluent v9 panel: find field + live match count, prev/next
 * navigation, a case-sensitivity toggle, a replace field, and Replace / Replace All. All
 * search/highlight/replace LOGIC lives in {@link useComposeFindReplace} — this component is a
 * thin, presentational binding (mirrors {@link ComposeFormatToolbar}'s component conventions +
 * theming: `makeStyles` + semantic `tokens.*` only, no hardcoded colors — ADR-021).
 *
 * Mount pattern (ComposeEditor.tsx): rendered conditionally (`open` prop) between the format
 * toolbar and the editor surface, toggled by Ctrl/Cmd+F inside the editor (see the
 * `editorProps.handleKeyDown` hook in ComposeEditor.tsx) and dismissed by Escape or the close
 * button. Closing clears the search state + highlight (never leaves stale decorations behind).
 *
 * @see ./hooks/useComposeFindReplace.ts — search/highlight/replace/replace-all logic
 * @see ./ComposeFormatToolbar.tsx — the sibling toolbar this mirrors conventions from
 * @see ../marks/InsertionMark.ts · ../marks/DeletionMark.ts · ../marks/CommentAnchorMark.ts — the
 *      tracked-changes marks a replace must preserve (enforced inside the hook, not here)
 */
import * as React from 'react';
import { type Editor } from '@tiptap/react';
import { Button, Input, Text, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import { ChevronUp16Regular, ChevronDown16Regular, Dismiss16Regular } from '@fluentui/react-icons';
import { useComposeFindReplace } from './hooks/useComposeFindReplace';

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
  row: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
  findInput: {
    minWidth: '200px',
  },
  replaceInput: {
    minWidth: '200px',
  },
  count: {
    minWidth: '64px',
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'nowrap',
  },
  spacer: {
    flexGrow: 1,
  },
  caseToggleActive: {
    fontWeight: tokens.fontWeightBold,
  },
});

export interface ComposeFindReplaceProps {
  editor: Editor | null;
  /** Whether the panel is mounted/visible. When false, this component renders nothing. */
  open: boolean;
  /** Called when the user dismisses the panel (close button or Escape). */
  onClose: () => void;
}

/**
 * Small icon-only nav button (prev/next), matching {@link ComposeFormatToolbar}'s
 * `PaletteIconButton` convention: a hover Tooltip carries the accessible description.
 */
function NavButton(props: {
  icon: React.JSX.Element;
  label: string;
  disabled?: boolean;
  onClick: () => void;
  testId: string;
}): React.JSX.Element {
  return (
    <Tooltip content={props.label} relationship="description" withArrow>
      <Button
        appearance="subtle"
        size="small"
        icon={props.icon}
        aria-label={props.label}
        disabled={props.disabled}
        onClick={props.onClick}
        data-testid={props.testId}
      />
    </Tooltip>
  );
}

export function ComposeFindReplace(props: ComposeFindReplaceProps): React.JSX.Element | null {
  const styles = useStyles();
  const { editor, open, onClose } = props;
  const fr = useComposeFindReplace(editor);

  if (!open) return null;

  const hasMatches = fr.matchCount > 0;

  const countLabel =
    fr.searchTerm.length === 0 ? '' : hasMatches ? `${fr.currentMatchIndex + 1} of ${fr.matchCount}` : '0 found';

  const handleClose = (): void => {
    fr.clear();
    onClose();
  };

  const handleKeyDown = (event: React.KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      handleClose();
      return;
    }
    if (event.key === 'Enter') {
      event.preventDefault();
      if (event.shiftKey) {
        fr.findPrevious();
      } else {
        fr.findNext();
      }
    }
  };

  return (
    <div
      className={styles.panel}
      role="search"
      aria-label="Find and replace"
      data-testid="compose-find-replace-panel"
      onKeyDown={handleKeyDown}
    >
      <div className={styles.row}>
        <Input
          className={styles.findInput}
          value={fr.searchTerm}
          onChange={(_, data) => fr.setSearchTerm(data.value)}
          placeholder="Find"
          aria-label="Find"
          data-testid="compose-find-replace-find-input"
        />
        <Text size={200} className={styles.count} data-testid="compose-find-replace-count">
          {countLabel}
        </Text>
        <NavButton
          icon={<ChevronUp16Regular />}
          label="Previous match"
          disabled={!hasMatches}
          onClick={fr.findPrevious}
          testId="compose-find-replace-prev"
        />
        <NavButton
          icon={<ChevronDown16Regular />}
          label="Next match"
          disabled={!hasMatches}
          onClick={fr.findNext}
          testId="compose-find-replace-next"
        />
        {/* Case-sensitivity toggle — "Aa" (the common find-widget affordance, e.g. VS Code / Word)
            rather than a guessed icon name, so the control's meaning is unambiguous at a glance. */}
        <Tooltip content="Match case" relationship="description" withArrow>
          <Button
            appearance={fr.caseSensitive ? 'primary' : 'subtle'}
            size="small"
            aria-label="Match case"
            aria-pressed={fr.caseSensitive}
            className={fr.caseSensitive ? styles.caseToggleActive : undefined}
            onClick={() => fr.setCaseSensitive(!fr.caseSensitive)}
            data-testid="compose-find-replace-case-toggle"
          >
            Aa
          </Button>
        </Tooltip>
        <div className={styles.spacer} />
        <NavButton
          icon={<Dismiss16Regular />}
          label="Close find and replace"
          onClick={handleClose}
          testId="compose-find-replace-close"
        />
      </div>
      <div className={styles.row}>
        <Input
          className={styles.replaceInput}
          value={fr.replaceTerm}
          onChange={(_, data) => fr.setReplaceTerm(data.value)}
          placeholder="Replace"
          aria-label="Replace"
          data-testid="compose-find-replace-replace-input"
        />
        <Button
          appearance="subtle"
          size="small"
          disabled={!hasMatches}
          onClick={fr.replaceCurrent}
          data-testid="compose-find-replace-replace"
        >
          Replace
        </Button>
        <Button
          appearance="subtle"
          size="small"
          disabled={!hasMatches}
          onClick={fr.replaceAll}
          data-testid="compose-find-replace-replace-all"
        >
          Replace All
        </Button>
      </div>
    </div>
  );
}

export default ComposeFindReplace;
