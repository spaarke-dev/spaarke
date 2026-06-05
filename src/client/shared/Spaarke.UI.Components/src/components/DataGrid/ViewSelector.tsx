/**
 * ViewSelector — reusable view-picker primitive for any grid surface backed by
 * Dataverse savedqueries.
 *
 * Renders:
 * ```
 *   [ ← ]   View Name ⌄
 * ```
 *
 * Click anywhere on the `View Name ⌄` cluster opens a Fluent v9 `<Menu>` with
 * one `MenuItemRadio` per available saved query. The active view is checked.
 * Selection emits `onViewChange(viewId)` to the parent — the parent owns the
 * actual data refetch + state reset (this component is purely presentational).
 *
 * **Reuse scope** (ADR-012):
 * - `DataGrid` framework — drives the modal-header view picker (Phase C)
 * - `EventsPage` (Phase D migration target)
 * - `SearchResultsGrid` (Phase E migration target)
 * - Any future workspace widget or stand-alone grid surface
 *
 * Hosts fetch the view list once via `IDataverseClient.retrieveSavedQueriesForEntity`
 * and pass it down through `views`. This component does NOT call Dataverse.
 *
 * **ADR**: ADR-012 (shared-library home), ADR-021 (Fluent v9 + tokens),
 *          ADR-022 (React-16-safe — no `useId`/`useSyncExternalStore`/`createRoot`),
 *          NFR-03 (`applyStylesToPortals` on the Menu popover)
 */

import * as React from 'react';
import {
  Button,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemRadio,
  Text,
  FluentProvider,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  webLightTheme,
  type Theme,
} from '@fluentui/react-components';
import { ArrowLeft20Regular, ChevronDown16Regular } from '@fluentui/react-icons';

/** Summary shape for a single saved view, used in the picker. */
export interface SavedView {
  /** GUID of the savedquery record (the value emitted via `onViewChange`). */
  id: string;
  /** User-visible view name. */
  name: string;
  /** Whether this is the entity's default view. */
  isDefault?: boolean;
}

export interface ViewSelectorProps {
  /** All views to choose from. Order is preserved in the menu. */
  views: ReadonlyArray<SavedView>;
  /** Currently active view id. The menu radio is checked for this id. */
  activeViewId: string;
  /** Fires when the user picks a different view from the menu. */
  onViewChange: (viewId: string) => void;
  /**
   * Optional back-arrow callback. When undefined the back arrow is hidden
   * (R1 default — Custom Pages don't have a native back stack). When set, the
   * caller is responsible for the back navigation.
   */
  onBack?: () => void;
  /**
   * Active Fluent v9 theme — re-wraps the Menu popover surface so dark mode
   * resolves correctly when it portals out of the host's root provider
   * (NFR-03). Defaults to `webLightTheme`.
   */
  theme?: Theme;
  /** Additional class merged after component classes (Spaarke convention). */
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  /** Vertical divider between the back-arrow button and the view label. */
  divider: {
    width: '1px',
    height: '24px',
    backgroundColor: tokens.colorNeutralStroke2,
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  trigger: {
    // Make the entire "View Name ⌄" cluster behave as one button: subtle
    // background, padding-left small so the label sits close to the back arrow,
    // chevron on the right inside the same button.
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    minHeight: '32px',
    columnGap: tokens.spacingHorizontalXS,
  },
  triggerLabel: {
    // Match Power Apps OOB grid title prominence — `fontSizeBase500` (20px) +
    // `fontWeightSemibold`. See
    // `projects/spaarke-datagrid-framework-r1/notes/testing-screenshots/oob-view-modal.jpg`.
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    maxWidth: '320px',
  },
  popoverSurface: {
    minWidth: '240px',
    ...shorthands.padding(0),
  },
  defaultBadge: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    marginLeft: tokens.spacingHorizontalS,
  },
});

/**
 * ViewSelector — see file-level JSDoc for the contract.
 *
 * Empty `views` array renders the active view name only (no chevron, no menu).
 * Active view not present in `views` renders the active view name and the menu
 * still opens — selecting any view emits the change.
 */
export const ViewSelector: React.FC<ViewSelectorProps> = ({
  views,
  activeViewId,
  onViewChange,
  onBack,
  theme = webLightTheme,
  className,
}) => {
  const styles = useStyles();

  const activeView = React.useMemo(() => views.find(v => v.id === activeViewId), [views, activeViewId]);
  const activeLabel = activeView?.name ?? '';
  const hasChoices = views.length > 1;

  const checkedValues = React.useMemo(() => ({ view: [activeViewId] }), [activeViewId]);

  const handleCheckedValueChange = React.useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name !== 'view') return;
      const next = data.checkedItems[0];
      if (next && next !== activeViewId) onViewChange(next);
    },
    [activeViewId, onViewChange]
  );

  const triggerContent = (
    <Button
      appearance="subtle"
      className={styles.trigger}
      aria-label={`Select view (currently ${activeLabel || 'unset'})`}
      aria-haspopup={hasChoices ? 'menu' : undefined}
      // When there are no alternative views, render as a plain non-button label.
      disabled={!hasChoices}
      iconPosition="after"
      icon={hasChoices ? <ChevronDown16Regular /> : undefined}
    >
      <Text className={styles.triggerLabel}>{activeLabel}</Text>
    </Button>
  );

  return (
    <div className={mergeClasses(styles.root, className)}>
      {onBack && (
        <>
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} aria-label="Back" onClick={onBack} />
          <span aria-hidden="true" className={styles.divider} />
        </>
      )}
      {hasChoices ? (
        <Menu checkedValues={checkedValues} onCheckedValueChange={handleCheckedValueChange} positioning="below-start">
          <MenuTrigger disableButtonEnhancement>{triggerContent}</MenuTrigger>
          <MenuPopover className={styles.popoverSurface}>
            {/* NFR-03: re-wrap portal surface so dark mode resolves inside the menu. */}
            <FluentProvider theme={theme} applyStylesToPortals={true}>
              <MenuList>
                {views.map(v => (
                  <MenuItemRadio key={v.id} name="view" value={v.id}>
                    {v.name}
                    {v.isDefault && <Text className={styles.defaultBadge}>Default</Text>}
                  </MenuItemRadio>
                ))}
              </MenuList>
            </FluentProvider>
          </MenuPopover>
        </Menu>
      ) : (
        triggerContent
      )}
    </div>
  );
};

export default ViewSelector;
