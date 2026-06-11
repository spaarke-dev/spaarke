/**
 * ViewToggle — list / card segmented control.
 *
 * Two icon buttons in a segmented group. Mutually-exclusive selection is
 * controlled by the parent via `mode` + `onChange`.
 *
 * Icon set matches `SemanticSearchControl` (smart-todo-r4 FR-09):
 *   - list view → `AppsList20Regular`
 *   - card view → `Grid20Regular`
 * (verified in `SemanticSearchControl/components/CommandBar.tsx`).
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens. No v8, no inline styles.
 * WCAG 2.1 AA: each segment has `aria-pressed` reflecting its selection state,
 * `aria-label` for SR, and Fluent v9 `<Button>` provides keyboard activation
 * (Enter / Space) + visible focus ring.
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-012 Shared component library
 * @see smart-todo-r4 spec FR-09 (matches SemanticSearchControl icon set)
 * @see smart-todo-r4 spec NFR-07 (WCAG 2.1 AA)
 *
 * @example
 * ```tsx
 * const [view, setView] = React.useState<'list' | 'card'>('list');
 * <ViewToggle mode={view} onChange={setView} />
 * ```
 */
import * as React from 'react';
import { Button, Tooltip, mergeClasses } from '@fluentui/react-components';
import { AppsList20Regular, Grid20Regular } from '@fluentui/react-icons';
import { useViewToggleStyles } from './ViewToggle.styles';

export type ViewToggleMode = 'list' | 'card';

export interface ViewToggleProps {
  /** Current view mode. */
  mode: ViewToggleMode;
  /** Called with the new mode when the user clicks the opposite segment. */
  onChange: (mode: ViewToggleMode) => void;
  /** Optional extra className applied to the segmented group container. */
  className?: string;
}

export const ViewToggle: React.FC<ViewToggleProps> = ({ mode, onChange, className }) => {
  const styles = useViewToggleStyles();

  const handleClick = React.useCallback(
    (next: ViewToggleMode) => () => {
      if (next !== mode) {
        onChange(next);
      }
    },
    [mode, onChange]
  );

  const listSelected = mode === 'list';
  const cardSelected = mode === 'card';

  return (
    <div role="group" aria-label="View mode" className={mergeClasses(styles.group, className)}>
      <Tooltip content="List view" relationship="label">
        <Button
          appearance="subtle"
          icon={<AppsList20Regular />}
          aria-label="List view"
          aria-pressed={listSelected}
          onClick={handleClick('list')}
          className={mergeClasses(styles.segment, listSelected && styles.segmentSelected)}
        />
      </Tooltip>
      <Tooltip content="Card view" relationship="label">
        <Button
          appearance="subtle"
          icon={<Grid20Regular />}
          aria-label="Card view"
          aria-pressed={cardSelected}
          onClick={handleClick('card')}
          className={mergeClasses(styles.segment, cardSelected && styles.segmentSelected)}
        />
      </Tooltip>
    </div>
  );
};

ViewToggle.displayName = 'ViewToggle';
