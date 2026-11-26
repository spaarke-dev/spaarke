/**
 * RecordCommandBar — shared record-level action bar.
 *
 * FR-04 (spaarke-SPA-external-access-platform-r4). Renders a record's actions
 * from plain descriptors:
 *   - an action with `visible !== true` is not rendered at all (hide, don't
 *     disable — no DOM node, no tab stop);
 *   - inline actions become `HeaderToolbar` icon slots (icon-only button,
 *     Tooltip relationship="label", aria-label = tooltip);
 *   - actions with `overflow: true` render in a Fluent v9 Menu behind one
 *     "More actions" slot, which exists only when at least one visible action
 *     overflows.
 *
 * Composition, not duplication (ADR-012 Path C, spec §ADR Tensions): the
 * inline buttons and the overflow trigger are HeaderToolbar slots. The menu is
 * anchored to the trigger through the slot's `buttonRef`. HeaderToolbar's
 * public contract is unchanged.
 *
 * Standards:
 *  - ADR-012  Context-agnostic: no host platform objects, no data
 *             fetching, no entity names. The host decides visibility.
 *  - ADR-021  Fluent v9 semantic tokens only; icons use currentColor.
 *  - ADR-022  React 16/17 safe: plain hooks only (no useId, no
 *             useSyncExternalStore, no createRoot).
 */

import * as React from 'react';
import {
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  Tooltip,
  makeStyles,
  mergeClasses,
  type MenuProps,
} from '@fluentui/react-components';
import { MoreHorizontal20Regular } from '@fluentui/react-icons';

import { HeaderToolbar } from '../HeaderToolbar/HeaderToolbar';
import type { IHeaderToolbarSlot } from '../HeaderToolbar/types';
import type { RecordCommandBarAction, RecordCommandBarProps } from './types';

// ---------------------------------------------------------------------------
// Styles — module scope per fluent-v9-component-authoring.md
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    width: '100%',
    minWidth: 0,
  },
});

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Accessible name and tooltip of the overflow trigger. */
const RECORD_COMMAND_BAR_MORE_LABEL = 'More actions';

/**
 * Slot key of the overflow trigger. The prefix keeps it from colliding with a
 * caller's action id.
 */
const MORE_SLOT_KEY = '__recordCommandBar_more';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RecordCommandBar: React.FC<RecordCommandBarProps> = ({ actions, ariaLabel, className }) => {
  const styles = useStyles();

  // Hide, don't disable: only an explicit `visible: true` renders (fails
  // closed for a descriptor built from untyped data).
  const visibleActions = actions.filter((action: RecordCommandBarAction) => action.visible === true);
  const inlineActions = visibleActions.filter(action => action.overflow !== true);
  const overflowActions = visibleActions.filter(action => action.overflow === true);
  const hasOverflow = overflowActions.length > 0;

  const [menuOpen, setMenuOpen] = React.useState(false);
  // State (not a ref) so the Menu re-positions once the trigger mounts.
  const [moreButton, setMoreButton] = React.useState<HTMLButtonElement | null>(null);
  const popoverRef = React.useRef<HTMLDivElement>(null);
  // State so the focus-first effect runs once the portalled list has mounted.
  const [menuList, setMenuList] = React.useState<HTMLDivElement | null>(null);
  const wasOpenRef = React.useRef(false);
  const focusedOnOpenRef = React.useRef(false);

  // If the last overflow action becomes hidden while the menu is open, the
  // menu unmounts; reset so it does not reopen when one becomes visible again.
  React.useEffect(() => {
    if (!hasOverflow && menuOpen) {
      setMenuOpen(false);
    }
  }, [hasOverflow, menuOpen]);

  // The trigger is a HeaderToolbar slot, whose contract has no aria props, so
  // the menu-button semantics are set on the element directly.
  React.useEffect(() => {
    if (!moreButton) return;
    moreButton.setAttribute('aria-haspopup', 'menu');
    moreButton.setAttribute('aria-expanded', menuOpen ? 'true' : 'false');
  }, [moreButton, menuOpen]);

  // Focus first item on open. Fluent's own focus-first runs when `open` flips,
  // which can be before the portalled list exists; without it a keyboard user
  // who opened the menu with Enter/Space would be left on the trigger, where
  // the arrow keys do nothing. Runs once per opening.
  React.useEffect(() => {
    if (!menuOpen) {
      focusedOnOpenRef.current = false;
      return;
    }
    if (focusedOnOpenRef.current || !menuList) return;
    focusedOnOpenRef.current = true;
    if (menuList.contains(menuList.ownerDocument.activeElement)) return;
    const firstItem = menuList.querySelector<HTMLElement>('[role="menuitem"]');
    firstItem?.focus();
  }, [menuOpen, menuList]);

  // Focus return. Fluent's Menu restores focus only to a <MenuTrigger> child;
  // this trigger is anchored by `positioning.target`, so mirror Fluent's rule:
  // on open → closed, if focus is lost (on <body>) or still inside the closing
  // popover, return it to the trigger. A dialog an item opened keeps its focus.
  React.useEffect(() => {
    if (menuOpen) {
      wasOpenRef.current = true;
      return;
    }
    if (!wasOpenRef.current) return;
    wasOpenRef.current = false;
    const doc = moreButton?.ownerDocument;
    if (!moreButton || !doc) return;
    const active = doc.activeElement;
    const focusLost = active === null || active === doc.body;
    const focusInPopover = popoverRef.current !== null && popoverRef.current.contains(active);
    if (focusLost || focusInPopover) {
      moreButton.focus();
    }
  }, [menuOpen, moreButton]);

  const handleOpenChange: MenuProps['onOpenChange'] = (event, data) => {
    // Fluent's outside-click listener runs in the capture phase, before the
    // trigger's own click. A click on the trigger is not "outside": the
    // trigger's onClick toggles the menu instead.
    if (
      !data.open &&
      data.type === 'clickOutside' &&
      moreButton !== null &&
      event.target instanceof Node &&
      moreButton.contains(event.target)
    ) {
      return;
    }
    setMenuOpen(data.open);
  };

  const iconSlots: IHeaderToolbarSlot[] = inlineActions.map(action => ({
    key: action.id,
    icon: action.icon,
    onClick: action.onClick,
    tooltip: action.tooltip,
  }));

  if (hasOverflow) {
    iconSlots.push({
      key: MORE_SLOT_KEY,
      icon: <MoreHorizontal20Regular />,
      onClick: () => setMenuOpen(open => !open),
      tooltip: RECORD_COMMAND_BAR_MORE_LABEL,
      buttonRef: setMoreButton,
    });
  }

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="toolbar"
      aria-label={ariaLabel}
      data-testid="record-command-bar"
    >
      <HeaderToolbar iconSlots={iconSlots} />

      {hasOverflow ? (
        <Menu
          open={menuOpen}
          onOpenChange={handleOpenChange}
          positioning={{ target: moreButton ?? undefined, position: 'below', align: 'end' }}
          hasIcons
        >
          <MenuPopover ref={popoverRef}>
            <MenuList ref={setMenuList} aria-label={RECORD_COMMAND_BAR_MORE_LABEL}>
              {overflowActions.map(action => (
                <Tooltip key={action.id} content={action.tooltip} relationship="label" withArrow={false}>
                  <MenuItem
                    icon={action.icon}
                    aria-label={action.tooltip}
                    data-testid={`record-command-bar-item-${action.id}`}
                    onClick={() => {
                      void action.onClick();
                    }}
                  >
                    {action.label}
                  </MenuItem>
                </Tooltip>
              ))}
            </MenuList>
          </MenuPopover>
        </Menu>
      ) : null}
    </div>
  );
};

RecordCommandBar.displayName = 'RecordCommandBar';
