/**
 * RowActionMenu.tsx
 *
 * The ONE shared row-action menu: a Fluent v9 `Menu` whose items come from an action-descriptor table, grouped, with
 * dividers only between groups that have a visible item on each side. Extracted from `DocumentRowMenu` (C-9) so the
 * bespoke row menus (Daily Briefing bullets and high-priority rows, the Manage Workspaces pane, the Email connections
 * review) do not each re-implement the Menu / MenuTrigger / MenuPopover / MenuList scaffolding.
 *
 * What it owns: the menu scaffold, group + divider logic, `hidden` filtering, per-item `disabled`, an optional per-item
 * Tooltip, and the item `data-testid`. What it leaves to the caller: which actions exist, what they do (`onAction`
 * receives the key) and, optionally, the trigger (`trigger`) when the default 3-dot MenuButton does not fit.
 *
 * Defaults are deliberately the least invasive ones (no click stop-propagation, no extra positioning); a caller that
 * needs the old behaviour of its own menu asks for it explicitly, so migrating a menu changes nothing it did before.
 *
 * Fluent v9 portal gotcha (`.claude/patterns/ui/fluent-v9-portal-gotcha.md`): the popover renders in a portal. The
 * consuming surface mounts a single root `FluentProvider`; this component does not mount its own.
 *
 * Standards: ADR-012 (shared, generic), ADR-021 (Fluent v9 tokens only; nothing here sets a colour), ADR-022 (React
 * 16/17 compatible: no React 18-only API).
 */

import * as React from 'react';
import {
  Menu,
  MenuTrigger,
  MenuButton,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Tooltip,
} from '@fluentui/react-components';
import type { MenuProps } from '@fluentui/react-components';
import { MoreVertical20Regular } from '@fluentui/react-icons';

/** One action in the menu. */
export interface RowActionDescriptor<K extends string = string> {
  /** Stable key; passed to `onAction`. */
  key: K;
  /** Visible label. */
  label: React.ReactNode;
  /** Optional leading icon. */
  icon?: React.ReactElement;
  /** Omit the item from the rendered menu entirely (and from divider accounting). */
  hidden?: boolean;
  /** Render the item disabled (also sets `aria-disabled`); its click does not fire `onAction`. */
  disabled?: boolean;
  /** When set, the item is wrapped in a Fluent Tooltip (relationship `description`) with this text. */
  tooltip?: string;
  /** Optional `data-testid` on the item. */
  testId?: string;
}

/** Props for {@link RowActionMenu}. */
export interface RowActionMenuProps<K extends string = string> {
  /** Groups of actions, in display order. A divider is rendered between two groups that both have a visible item. */
  groups: ReadonlyArray<ReadonlyArray<RowActionDescriptor<K>>>;
  /** Invoked with the key of the chosen action. */
  onAction: (key: K) => void;
  /** Accessible label of the DEFAULT trigger (ignored when `trigger` is supplied). */
  ariaLabel?: string;
  /**
   * Custom trigger element, rendered inside `MenuTrigger`. When omitted the default is an icon-only subtle small
   * Fluent `MenuButton`.
   */
  trigger?: React.ReactElement;
  /** Icon of the default trigger. Defaults to the vertical 3-dot icon. */
  triggerIcon?: React.ReactElement;
  /** `className` of the default trigger. */
  triggerClassName?: string;
  /** `data-testid` of the default trigger. */
  triggerTestId?: string;
  /**
   * Stop a click on the default trigger from reaching the row's own `onClick` (for example a row that opens a
   * preview). Default false.
   */
  stopTriggerPropagation?: boolean;
  /** Stop clicks inside the popover from bubbling through the React tree to the row. Default false. */
  stopPopoverPropagation?: boolean;
  /** Popover placement, passed to the Fluent `Menu`. */
  positioning?: MenuProps['positioning'];
}

function visibleOnly<K extends string>(group: ReadonlyArray<RowActionDescriptor<K>>): RowActionDescriptor<K>[] {
  return group.filter(a => !a.hidden);
}

/**
 * Descriptor-driven row-action menu.
 *
 * @example
 * ```tsx
 * <RowActionMenu
 *   ariaLabel={`More actions for ${row.name}`}
 *   groups={[[{ key: 'open', label: 'Open', icon: <Open20Regular /> }], [{ key: 'delete', label: 'Delete', hidden: !canDelete }]]}
 *   onAction={key => handle(key, row)}
 *   stopTriggerPropagation
 * />
 * ```
 */
export function RowActionMenu<K extends string = string>({
  groups,
  onAction,
  ariaLabel,
  trigger,
  triggerIcon,
  triggerClassName,
  triggerTestId,
  stopTriggerPropagation = false,
  stopPopoverPropagation = false,
  positioning,
}: RowActionMenuProps<K>): React.ReactElement {
  const handleTriggerClick = React.useCallback(
    (e: React.MouseEvent<HTMLButtonElement>) => {
      if (stopTriggerPropagation) e.stopPropagation();
    },
    [stopTriggerPropagation]
  );

  const handlePopoverClick = React.useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (stopPopoverPropagation) e.stopPropagation();
    },
    [stopPopoverPropagation]
  );

  const renderItem = (a: RowActionDescriptor<K>): React.ReactElement => {
    const item = (
      <MenuItem
        key={a.key}
        icon={a.icon}
        disabled={a.disabled}
        aria-disabled={a.disabled ? true : undefined}
        onClick={() => {
          if (!a.disabled) onAction(a.key);
        }}
        data-testid={a.testId}
      >
        {a.label}
      </MenuItem>
    );
    return a.tooltip ? (
      <Tooltip key={a.key} content={a.tooltip} relationship="description">
        {item}
      </Tooltip>
    ) : (
      item
    );
  };

  // A divider is rendered only between two groups that each have a visible item: no orphaned, doubled or leading
  // dividers when a whole group is hidden.
  const visibleGroups = groups.map(visibleOnly).filter(g => g.length > 0);
  const content: React.ReactNode[] = [];
  visibleGroups.forEach((group, i) => {
    if (i > 0) content.push(<MenuDivider key={`divider-${i}`} />);
    group.forEach(a => content.push(renderItem(a)));
  });

  return (
    <Menu positioning={positioning}>
      <MenuTrigger disableButtonEnhancement>
        {trigger ?? (
          <MenuButton
            appearance="subtle"
            size="small"
            icon={triggerIcon ?? <MoreVertical20Regular />}
            aria-label={ariaLabel}
            className={triggerClassName}
            onClick={handleTriggerClick}
            data-testid={triggerTestId}
          />
        )}
      </MenuTrigger>
      <MenuPopover onClick={stopPopoverPropagation ? handlePopoverClick : undefined}>
        <MenuList>{content}</MenuList>
      </MenuPopover>
    </Menu>
  );
}

RowActionMenu.displayName = 'RowActionMenu';
