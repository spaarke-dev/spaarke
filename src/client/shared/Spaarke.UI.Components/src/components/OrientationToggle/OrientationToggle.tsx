/**
 * OrientationToggle — horizontal ↔ vertical orientation toggle icon button.
 *
 * Single Fluent v9 icon button whose icon swaps based on current state:
 *   - orientation === 'horizontal' → icon `LayoutColumnTwo20Regular`
 *     (columns side-by-side, the current state)
 *   - orientation === 'vertical'   → icon `LayoutRowTwo20Regular`
 *     (rows stacked top-to-bottom, the current state)
 *
 * Note on icon choice: smart-todo-r4 spec FR-28 originally prescribed
 * `LayoutRowTwoSplit20Regular`, but that name does not exist in
 * `@fluentui/react-icons` v2.0.320 (verified empirically). The closest
 * available analog is `LayoutRowTwo20Regular`, which is the proper visual
 * mirror of `LayoutColumnTwo20Regular`.
 *
 * The user clicks to flip — the icon shows what they're currently in (NOT
 * what they'll get) — matching the smart-todo-r4 spec FR-28 intent.
 *
 * Initial consumers: smart-todo-r4 SmartTodo Code Page (tasks 070, 071) —
 * Kanban horizontal columns vs vertical stacked sections.
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens. No v8, no inline styles.
 * WCAG 2.1 AA: `aria-pressed` reflects "vertical mode active" so SR users get
 * a meaningful toggle state; `aria-label` describes the current orientation +
 * action.
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-012 Shared component library
 * @see smart-todo-r4 spec FR-28
 * @see smart-todo-r4 spec NFR-07 (WCAG 2.1 AA)
 *
 * @example
 * ```tsx
 * const [orientation, setOrientation] = React.useState<'horizontal' | 'vertical'>('horizontal');
 * <OrientationToggle orientation={orientation} onChange={setOrientation} />
 * ```
 */
import * as React from 'react';
import { Button, Tooltip, mergeClasses } from '@fluentui/react-components';
import { LayoutColumnTwo20Regular, LayoutRowTwo20Regular } from '@fluentui/react-icons';
import { useOrientationToggleStyles } from './OrientationToggle.styles';

export type Orientation = 'horizontal' | 'vertical';

export interface OrientationToggleProps {
  /** Current orientation. */
  orientation: Orientation;
  /** Called with the new orientation when clicked. */
  onChange: (orientation: Orientation) => void;
  /** Optional extra className applied to the button. */
  className?: string;
}

const NEXT_ORIENTATION: Record<Orientation, Orientation> = {
  horizontal: 'vertical',
  vertical: 'horizontal',
};

const ORIENTATION_LABEL: Record<Orientation, string> = {
  horizontal: 'Horizontal layout',
  vertical: 'Vertical layout',
};

export const OrientationToggle: React.FC<OrientationToggleProps> = ({
  orientation,
  onChange,
  className,
}) => {
  const styles = useOrientationToggleStyles();

  const handleClick = React.useCallback(() => {
    onChange(NEXT_ORIENTATION[orientation]);
  }, [orientation, onChange]);

  const currentLabel = ORIENTATION_LABEL[orientation];
  const nextLabel = ORIENTATION_LABEL[NEXT_ORIENTATION[orientation]];
  const tooltip = `${currentLabel} — click to switch to ${nextLabel.toLowerCase()}`;
  const ariaLabel = `Current layout: ${currentLabel}. Click to switch to ${nextLabel.toLowerCase()}.`;

  // Show the icon representing CURRENT orientation (FR-28: the icon shows
  // the current state; pressing flips it).
  const Icon = orientation === 'horizontal' ? LayoutColumnTwo20Regular : LayoutRowTwo20Regular;

  return (
    <Tooltip content={tooltip} relationship="label">
      <Button
        appearance="subtle"
        icon={<Icon />}
        onClick={handleClick}
        aria-label={ariaLabel}
        aria-pressed={orientation === 'vertical'}
        className={mergeClasses(styles.toggleButton, className)}
      />
    </Tooltip>
  );
};

OrientationToggle.displayName = 'OrientationToggle';
