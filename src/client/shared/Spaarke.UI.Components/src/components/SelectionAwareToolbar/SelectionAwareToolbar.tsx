/**
 * SelectionAwareToolbar — selection-driven action toolbar.
 *
 * Renders `null` when `selectedCount === 0`. When `selectedCount ≥ 1`, renders
 * a Fluent v9 `<Toolbar>` containing:
 *  - an optional "N selected" leading count label (default: shown)
 *  - one `<ToolbarButton>` per action from `actions[]`
 *
 * Initial consumers: smart-todo-r4 SmartTodo Code Page (tasks 030–033, 070).
 * Designed to be domain-agnostic — any surface that needs an "action bar
 * appears when you select something" pattern can use it.
 *
 * Visual reference: `SemanticSearchControl/components/BulkActionBar.tsx`
 * (icon-only, subtle, gentle gap).
 *
 * @see ADR-021 Fluent UI v9 design system (Griffel tokens, no inline styles)
 * @see ADR-012 Shared component library
 * @see smart-todo-r4 spec FR-08 (Open / Delete / Email / Pin actions)
 * @see smart-todo-r4 spec NFR-07 (WCAG 2.1 AA)
 *
 * @example
 * ```tsx
 * <SelectionAwareToolbar
 *   selectedCount={selectedIds.size}
 *   actions={[
 *     { id: 'open',   label: 'Open',   icon: <Open20Regular />,   onClick: handleOpen },
 *     { id: 'delete', label: 'Delete', icon: <Delete20Regular />, onClick: handleDelete },
 *     { id: 'email',  label: 'Email',  icon: <Mail20Regular />,   onClick: handleEmail },
 *     { id: 'pin',    label: 'Pin',    icon: <Pin20Regular />,    onClick: handlePin },
 *   ]}
 * />
 * ```
 */
import * as React from 'react';
import { Toolbar, Button, Tooltip, mergeClasses } from '@fluentui/react-components';
import { useSelectionAwareToolbarStyles } from './SelectionAwareToolbar.styles';
import type { SelectionAwareToolbarProps } from './types';

export const SelectionAwareToolbar: React.FC<SelectionAwareToolbarProps> = ({
  selectedCount,
  actions,
  showCountLabel = true,
  className,
}) => {
  const styles = useSelectionAwareToolbarStyles();

  // Spec FR-08 / API contract: render nothing at zero selection.
  if (selectedCount <= 0) {
    return null;
  }

  const countText = `${selectedCount} selected`;

  return (
    <Toolbar
      aria-label="Selection actions"
      className={mergeClasses(styles.toolbar, className)}
    >
      {showCountLabel && (
        <span
          className={styles.countLabel}
          // Live region so SR users hear when count changes within an open toolbar.
          aria-live="polite"
          aria-atomic="true"
        >
          {countText}
        </span>
      )}

      <div className={styles.actionGroup}>
        {actions.map(action => {
          const appearance = action.appearance ?? 'subtle';

          const button = (
            <Button
              key={action.id}
              icon={action.icon as React.ReactElement | undefined}
              appearance={appearance}
              size="small"
              disabled={action.disabled}
              onClick={action.disabled ? undefined : action.onClick}
              aria-label={action.label}
            >
              {action.label}
            </Button>
          );

          // Tooltip provides a hover hint while keeping the visible label.
          return (
            <Tooltip key={action.id} content={action.label} relationship="label">
              {button}
            </Tooltip>
          );
        })}
      </div>
    </Toolbar>
  );
};

SelectionAwareToolbar.displayName = 'SelectionAwareToolbar';
