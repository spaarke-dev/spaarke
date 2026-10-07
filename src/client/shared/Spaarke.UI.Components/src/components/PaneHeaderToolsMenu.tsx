/**
 * PaneHeaderToolsMenu.tsx — the shared pane-header "⋮ tools" trigger + dropdown
 * (C-12, spaarke-ontology-platform-r1 reuse audit, item D5).
 *
 * # Why this exists
 *
 * The pane-header "⋮ tools" menu was built three times in `src/solutions/SpaarkeAi`:
 * `AssistantToolMenu.tsx`, `ContextPaneMenu.tsx` and `WorkspacePaneMenu.tsx`. All
 * three cross-reference each other and CLAUDE.md §11 in their own comments — and it
 * was still never extracted (the audit's own "self-aware" finding). This component
 * is that extraction.
 *
 * # Two pieces, because the three copies are not identical
 *
 * - `PaneHeaderMenuTriggerButton` — the icon-only vertical three-dots (⋮) button
 *   (subtle, small, `MoreVerticalRegular`, `minWidth: auto`) common to ALL THREE
 *   copies. `WorkspacePaneMenu` uses only this piece: it has no dropdown — it opens
 *   `ManageWorkspacesPane` directly on click (a deliberate UAT 2026-07-20 change,
 *   see that file's header) — so wrapping it in a `Menu` would be the wrong shape,
 *   not a faithful migration.
 * - `PaneHeaderToolsMenu` — the full `Menu` + `MenuTrigger` + `Tooltip` +
 *   `PaneHeaderMenuTriggerButton` + `MenuPopover` + `MenuList` + optional
 *   `MenuGroupHeader` + `MenuItem[]` composition, for the two copies that ARE a
 *   real dropdown (`AssistantToolMenu`, `ContextPaneMenu`).
 *
 * Standards:
 *   - ADR-021: Fluent v9 semantic tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional components.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *
 * @see src/solutions/SpaarkeAi/src/components/conversation/AssistantToolMenu.tsx
 * @see src/solutions/SpaarkeAi/src/components/context/ContextPaneMenu.tsx
 * @see src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuGroupHeader,
  Button,
  Tooltip,
  CounterBadge,
} from '@fluentui/react-components';
import { MoreVerticalRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: 'auto',
  },
  triggerWrap: {
    position: 'relative',
    display: 'inline-flex',
  },
  badge: {
    position: 'absolute',
    top: '2px',
    right: '2px',
    pointerEvents: 'none',
  },
  menuItemBadge: {
    marginLeft: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// PaneHeaderMenuTriggerButton — the shared ⋮ button, usable standalone (no
// Menu) or as a MenuTrigger child (wrap it in its own <Tooltip> either way —
// MenuTrigger clones props onto its immediate child, and Fluent's Tooltip
// forwards them onto its own child, which is how the three originals compose
// it; this component does not itself render a Tooltip so both call shapes work).
// ---------------------------------------------------------------------------

export interface PaneHeaderMenuTriggerButtonProps {
  /** aria-label for the icon-only button. Also used as the default tooltip text. */
  ariaLabel: string;
  onClick?: () => void;
  /** Decorates the trigger with a small dot badge (e.g. an incomplete-profile nudge). */
  highlight?: boolean;
  testId?: string;
  /** Overrides the default `${testId}-badge` testid for the highlight badge. */
  badgeTestId?: string;
}

export const PaneHeaderMenuTriggerButton = React.forwardRef<HTMLButtonElement, PaneHeaderMenuTriggerButtonProps>(
  ({ ariaLabel, onClick, highlight = false, testId, badgeTestId }, ref) => {
    const styles = useStyles();
    return (
      <span className={styles.triggerWrap}>
        <Button
          ref={ref}
          appearance="subtle"
          size="small"
          icon={<MoreVerticalRegular />}
          aria-label={ariaLabel}
          className={styles.trigger}
          onClick={onClick}
          data-testid={testId}
        />
        {highlight ? (
          <CounterBadge
            size="tiny"
            appearance="filled"
            color="danger"
            dot
            className={styles.badge}
            data-testid={badgeTestId ?? (testId ? `${testId}-badge` : undefined)}
          />
        ) : null}
      </span>
    );
  }
);
PaneHeaderMenuTriggerButton.displayName = 'PaneHeaderMenuTriggerButton';

// ---------------------------------------------------------------------------
// PaneHeaderToolsMenu — the full dropdown, for pane headers whose ⋮ opens a
// list of tools (as opposed to WorkspacePaneMenu's direct-open behaviour).
// ---------------------------------------------------------------------------

export interface PaneHeaderToolsMenuItem<TId extends string = string> {
  id: TId;
  label: string;
  icon?: React.ReactElement;
  /** Shows a small trailing dot badge on this item (e.g. an incomplete-profile nudge). */
  trailingBadge?: boolean;
  testId?: string;
}

export interface PaneHeaderToolsMenuProps<TId extends string = string> {
  /** aria-label + default tooltip text for the ⋮ trigger, e.g. "Assistant tools". */
  triggerAriaLabel: string;
  /** Optional `MenuGroupHeader` text shown above the item list. */
  groupHeader?: string;
  items: readonly PaneHeaderToolsMenuItem<TId>[];
  onSelect: (id: TId) => void;
  /** Decorates the trigger with a small dot badge (e.g. an incomplete-profile nudge). */
  highlightTrigger?: boolean;
  triggerTestId?: string;
  /** Overrides the default `${triggerTestId}-badge` testid for the highlight badge. */
  triggerBadgeTestId?: string;
  popoverTestId?: string;
}

export function PaneHeaderToolsMenu<TId extends string = string>({
  triggerAriaLabel,
  groupHeader,
  items,
  onSelect,
  highlightTrigger = false,
  triggerTestId,
  triggerBadgeTestId,
  popoverTestId,
}: PaneHeaderToolsMenuProps<TId>): React.ReactElement {
  const styles = useStyles();
  const [open, setOpen] = React.useState(false);

  const handleOpenChange = React.useCallback((_e: unknown, data: { open: boolean }) => {
    setOpen(data.open);
  }, []);

  const handleSelect = React.useCallback(
    (id: TId) => {
      setOpen(false);
      onSelect(id);
    },
    [onSelect]
  );

  return (
    <Menu open={open} onOpenChange={handleOpenChange} positioning="below-end">
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content={triggerAriaLabel} relationship="label">
          <PaneHeaderMenuTriggerButton
            ariaLabel={triggerAriaLabel}
            highlight={highlightTrigger}
            testId={triggerTestId}
            badgeTestId={triggerBadgeTestId}
          />
        </Tooltip>
      </MenuTrigger>

      <MenuPopover data-testid={popoverTestId}>
        <MenuList>
          {groupHeader ? <MenuGroupHeader>{groupHeader}</MenuGroupHeader> : null}
          {items.map(item => (
            <MenuItem key={item.id} icon={item.icon} onClick={() => handleSelect(item.id)} data-testid={item.testId}>
              {item.label}
              {item.trailingBadge ? (
                <CounterBadge size="small" appearance="filled" color="danger" dot className={styles.menuItemBadge} />
              ) : null}
            </MenuItem>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
}
