/**
 * <CommandBar /> — the DataGrid framework's command bar primitive.
 *
 * Renders a Power Apps OOB-style grid command bar: a small number of
 * `inlineLimit` subtle icon+text buttons followed by a `…` overflow menu that
 * holds the remainder. Built-in actions resolved from `CommandBarConfig` are
 * `refresh`, `delete-selected`, `create-form`, `export-excel`, `edit-columns`,
 * `edit-filters`, plus host-registered `custom` actions.
 *
 * **Layout (matches Power Apps OOB grid header)**:
 *   [↻ Refresh] [🗑 Delete]  │  [⋯]
 *                            │   └─ MenuPopover with remaining items
 *                            │
 *                            └─ thin vertical Divider
 *
 * All inline triggers AND menu items use `appearance="subtle"` — no primary
 * blue, no background fill. `+New` no longer gets prominent CTA styling; it
 * lives in the overflow menu by default (callers can raise `inlineLimit` if
 * they need it inline).
 *
 * **Lifted from**: `src/solutions/EventsPage/src/App.tsx` (`openNewEventForm`,
 * `deleteSelectedEvents`, `executeBulkStatusUpdate`). All three are now generic
 * over `entityName` (see `defaults.ts`).
 *
 * **Key behavior preserved from the lifted code**:
 *  - Confirmation for `delete-selected` uses a Fluent v9 `<Dialog>` (NOT `window.confirm`).
 *  - Bulk operations use `Promise.all` + a `<Spinner>` overlay for >10 records.
 *  - The Dialog surface is re-wrapped in `<FluentProvider applyStylesToPortals theme={…} />`
 *    so dark mode renders correctly inside the portal (NFR-03).
 *  - `onCommandInvoke?(commandId, selectedIds)` fires for every invocation.
 *  - `requiresSelection` ('single' | 'multi') drives the disabled state in both
 *    inline buttons AND menu items.
 *  - Custom handler registry (`getCommandHandler`) is consulted for `custom` actions.
 *
 * **ADR**: ADR-021 (Fluent v9 + dark mode, tokens-only), ADR-022 (React-16-safe),
 *          NFR-03 (`applyStylesToPortals` on Dialog).
 * **FR**: FR-DG-08, FR-DG-14, FR-DG-17.
 *
 * @see CommandBarConfig
 * @see registerCommandHandler
 */

import * as React from 'react';
import {
  Button,
  Divider,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  FluentProvider,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  webLightTheme,
  type Theme,
} from '@fluentui/react-components';
import {
  Add20Regular,
  Delete20Regular,
  ArrowSync20Regular,
  ArrowDownload20Regular,
  Column20Regular,
  Filter20Regular,
  MoreHorizontal20Regular,
} from '@fluentui/react-icons';

import { dataGridTokens } from '../tokens';
import type { CommandBarConfig, CommandBarItem, CommandBarAction } from '../../../types/DataGridConfiguration';
import type { ResolvedColumn } from '../configResolution';
import {
  DEFAULT_ACTION_META,
  DEFAULT_ACTION_HANDLERS,
  type DefaultHandler,
  type DefaultHandlerContext,
} from './defaults';
import { getCommandHandler } from './registry';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Threshold above which bulk delete shows a progress spinner. */
const BULK_PROGRESS_THRESHOLD = 10;

/**
 * Default number of inline subtle buttons before the overflow `…` menu.
 * Matches Power Apps OOB: `Refresh + Delete` inline, everything else in the menu.
 */
const DEFAULT_INLINE_LIMIT = 2;

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for {@link CommandBar}.
 *
 * The grid passes `entityName`, `selectedIds`, `records`, `columns`, `currentView`,
 * `parentContext`, and `refresh` directly so the command bar remains usable
 * standalone (e.g., in a Storybook story) without requiring the full
 * `DataGridContext`.
 */
export interface CommandBarProps {
  /** Resolved `commandBar` slice from the grid configuration. */
  config: CommandBarConfig;
  /** Logical name of the entity (`'sprk_event'`, `'account'`). */
  entityName: string;
  /** Currently-selected primary id values. */
  selectedIds: ReadonlyArray<string>;
  /** Currently-loaded records — used by `export-excel`. */
  records: ReadonlyArray<Record<string, unknown>>;
  /** Visible columns — used by `export-excel`. */
  columns: ReadonlyArray<ResolvedColumn>;
  /** Display name of the active savedquery / configjson title. */
  currentView: string;
  /** Trigger a hard refresh of the grid. */
  refresh: () => void;
  /** Optional drill-through parent context. */
  parentContext?: { entityType: string; id: string; name: string };
  /**
   * Active Fluent v9 theme — used to re-wrap the Dialog's portal surface so dark
   * mode resolves correctly (NFR-03). Defaults to `webLightTheme`.
   */
  theme?: Theme;
  /** Fires when the user invokes ANY command (default or custom). */
  onCommandInvoke?: (commandId: string, selectedIds: ReadonlyArray<string>) => void;
  /**
   * Number of items rendered as inline subtle buttons before the overflow `…` menu.
   * Defaults to 2 to match Power Apps OOB (Refresh + Delete inline; rest in overflow).
   * Set to 0 to push EVERYTHING into the overflow menu.
   */
  inlineLimit?: number;
  /** Optional className appended after component classes (Spaarke convention). */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — `makeStyles` at MODULE SCOPE per ADR-021
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    // Right-align items by default — matches Power Apps OOB grid header chrome.
    // The outer DataGrid composition puts the title on the left and the command
    // bar on the right of a single flex band.
    justifyContent: 'flex-end',
    columnGap: tokens.spacingHorizontalXS,
    // Width auto so the bar shrinks to its content; the parent flex container
    // owns the left/right layout.
    width: 'auto',
    backgroundColor: dataGridTokens.commandBar.background,
    ...shorthands.border('1px', 'solid', dataGridTokens.commandBar.border),
    borderRadius: dataGridTokens.commandBar.borderRadius,
    boxShadow: dataGridTokens.commandBar.shadow,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  inlineButton: {
    // Icon + label, subtle appearance. Fluent v9's `Button` places the icon
    // slot on the LEFT of the children by default — exactly what OOB shows.
    // No extra rules needed; we rely on tokens via `appearance="subtle"`.
    minWidth: 'auto',
  },
  divider: {
    // Fluent v9 vertical Divider needs an explicit height to render inside a
    // flex row. We target the OOB ~20px tall thin separator.
    height: '20px',
    flexGrow: 0,
  },
  dialogStatus: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Icon registry — string → component. The CommandBarItem schema stores the
// icon as a *string* (matches the configjson hand-authoring contract) so the
// runtime resolves it through this table.
// ─────────────────────────────────────────────────────────────────────────────

const ICON_REGISTRY: Record<string, React.ReactElement> = {
  Add20Regular: <Add20Regular />,
  Delete20Regular: <Delete20Regular />,
  ArrowSync20Regular: <ArrowSync20Regular />,
  ArrowDownload20Regular: <ArrowDownload20Regular />,
  Column20Regular: <Column20Regular />,
  Filter20Regular: <Filter20Regular />,
  MoreHorizontal20Regular: <MoreHorizontal20Regular />,
};

function resolveIcon(name: string | undefined): React.ReactElement | undefined {
  if (!name) return undefined;
  return ICON_REGISTRY[name];
}

// ─────────────────────────────────────────────────────────────────────────────
// Build the effective item list — merges configjson `primary` + `secondary`
// with `showDefaultCommands` toggles.
//
// Auto-injection order is intentionally [refresh, delete-selected, create-form,
// export-excel, edit-columns, edit-filters] so the OOB inlineLimit=2 default
// renders `Refresh + Delete` inline (matches reference screenshot). Hosts that
// author explicit `primary`/`secondary` keep their authored ordering.
// ─────────────────────────────────────────────────────────────────────────────

const ALL_DEFAULT_ACTIONS: CommandBarAction[] = [
  'refresh',
  'delete-selected',
  'create-form',
  'export-excel',
  'edit-columns',
  'edit-filters',
];

/**
 * Map a default `CommandBarAction` id to the boolean key inside
 * `showDefaultCommands` so we can honor host opt-outs.
 */
const SHOW_DEFAULT_KEY: Record<string, keyof NonNullable<CommandBarConfig['showDefaultCommands']>> = {
  'create-form': 'newRecord',
  'delete-selected': 'delete',
  refresh: 'refresh',
  'export-excel': 'exportExcel',
  'edit-columns': 'editColumns',
  'edit-filters': 'editFilters',
};

/**
 * Synthesize a {@link CommandBarItem} for a default action when configjson
 * does not author it explicitly. R1 special case: `edit-columns` is hidden by
 * default per design.md §11.5 — callers must opt in via `showDefaultCommands.editColumns = true`.
 */
function makeDefaultItem(action: CommandBarAction): CommandBarItem {
  const meta = DEFAULT_ACTION_META[action];
  return {
    id: action,
    label: meta?.label ?? action,
    icon: meta?.icon ?? '',
    action,
    appearance: meta?.appearance ?? 'subtle',
    requiresSelection: action === 'delete-selected' ? 'multi' : false,
  };
}

function buildEffectiveItems(config: CommandBarConfig): CommandBarItem[] {
  const explicitItems: CommandBarItem[] = [...(config.primary ?? []), ...(config.secondary ?? [])];

  // Determine which built-ins to add — by default ON, except `edit-columns` (off in R1).
  const showDefault = config.showDefaultCommands ?? {};
  const explicitActions = new Set(explicitItems.map(it => it.action));

  for (const action of ALL_DEFAULT_ACTIONS) {
    if (explicitActions.has(action)) continue; // already authored explicitly

    const key = SHOW_DEFAULT_KEY[action];
    const explicitFlag = key !== undefined ? showDefault[key] : undefined;

    // edit-columns: R1 default OFF unless explicitly true
    if (action === 'edit-columns') {
      if (explicitFlag !== true) continue;
    } else {
      // Every other default is ON unless explicitly false
      if (explicitFlag === false) continue;
    }

    explicitItems.push(makeDefaultItem(action));
  }

  return explicitItems;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Renders the grid's command bar in the Power Apps OOB pattern — the first
 * `inlineLimit` items as subtle icon+text buttons, then a `…` overflow menu
 * containing everything else.
 *
 * See module JSDoc for the visual mental model.
 */
export const CommandBar: React.FC<CommandBarProps> = props => {
  const {
    config,
    entityName,
    selectedIds,
    records,
    columns,
    currentView,
    refresh,
    parentContext,
    theme = webLightTheme,
    onCommandInvoke,
    inlineLimit = DEFAULT_INLINE_LIMIT,
    className,
  } = props;

  const styles = useStyles();

  // ── Dialog state for bulk-delete confirmation ────────────────────────────
  const [deleteDialogOpen, setDeleteDialogOpen] = React.useState<boolean>(false);
  const [isDeleting, setIsDeleting] = React.useState<boolean>(false);

  // ── Build effective item list (defaults + configjson + showDefaultCommands) ─
  const effectiveItems = React.useMemo(() => buildEffectiveItems(config), [config]);

  // ── Partition: first inlineLimit go inline, rest go in the overflow menu ─
  // Clamp the limit so 0 / negative / too-large all behave intuitively.
  const normalizedLimit = Math.max(0, Math.min(inlineLimit, effectiveItems.length));
  const inlineItems = React.useMemo(() => effectiveItems.slice(0, normalizedLimit), [effectiveItems, normalizedLimit]);
  const overflowItems = React.useMemo(() => effectiveItems.slice(normalizedLimit), [effectiveItems, normalizedLimit]);

  // Shared context passed to default + custom handlers.
  const handlerContext: DefaultHandlerContext = React.useMemo(
    () => ({
      entityName,
      selectedIds,
      records,
      columns,
      currentView,
      refresh,
      parentContext,
    }),
    [entityName, selectedIds, records, columns, currentView, refresh, parentContext]
  );

  // ── Resolve a handler for a given item ──────────────────────────────────
  const resolveHandler = React.useCallback((item: CommandBarItem): DefaultHandler | undefined => {
    if (item.action === 'custom') {
      if (!item.customHandlerId) {
        // eslint-disable-next-line no-console
        console.warn(`[CommandBar] custom action "${item.id}" missing customHandlerId.`);
        return undefined;
      }
      const handler = getCommandHandler(item.customHandlerId);
      if (!handler) {
        // eslint-disable-next-line no-console
        console.warn(
          `[CommandBar] No registered handler for "${item.customHandlerId}". ` +
            `Register one with registerCommandHandler() before rendering.`
        );
      }
      return handler;
    }
    return DEFAULT_ACTION_HANDLERS[item.action];
  }, []);

  // ── Invocation flow — bulk delete short-circuits to Dialog ─────────────
  const invokeItem = React.useCallback(
    async (item: CommandBarItem) => {
      // Fire the host hook FIRST so observers see every click, including ones the
      // Dialog will then intercept.
      onCommandInvoke?.(item.id, selectedIds);

      if (item.action === 'delete-selected') {
        if (selectedIds.length === 0) return;
        setDeleteDialogOpen(true);
        return;
      }

      const handler = resolveHandler(item);
      if (!handler) return;
      try {
        await handler(handlerContext);
      } catch (err) {
        // eslint-disable-next-line no-console
        console.error(`[CommandBar] Handler for "${item.id}" threw:`, err);
      }
    },
    [onCommandInvoke, selectedIds, resolveHandler, handlerContext]
  );

  // ── Bulk delete confirmation handler — only runs after Dialog confirm ──
  const performBulkDelete = React.useCallback(async () => {
    const handler = DEFAULT_ACTION_HANDLERS['delete-selected'];
    setIsDeleting(true);
    try {
      await handler(handlerContext);
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('[CommandBar] Bulk delete failed:', err);
    } finally {
      setIsDeleting(false);
      setDeleteDialogOpen(false);
    }
  }, [handlerContext]);

  // ── Compute whether a given item is disabled based on selection requirement ─
  const isDisabled = React.useCallback(
    (item: CommandBarItem): boolean => {
      if (item.requiresSelection === 'single') {
        return selectedIds.length !== 1;
      }
      if (item.requiresSelection === 'multi') {
        return selectedIds.length === 0;
      }
      return false;
    },
    [selectedIds]
  );

  // ── Render a single inline subtle button (icon + Text label) ───────────
  const renderInlineButton = (item: CommandBarItem): React.ReactNode => {
    const icon = resolveIcon(item.icon);
    const disabled = isDisabled(item);
    // OOB pattern: every inline button is subtle. We intentionally ignore
    // `item.appearance` here — primary blue CTA buttons are NOT part of the
    // Power Apps grid pattern. Hosts that need a primary CTA should render it
    // outside the CommandBar.
    const button = (
      <Button
        key={item.id}
        appearance="subtle"
        icon={icon}
        disabled={disabled}
        onClick={() => {
          void invokeItem(item);
        }}
        aria-label={item.label}
        className={styles.inlineButton}
      >
        <Text>{item.label}</Text>
      </Button>
    );
    // Tooltip on enabled items only — disabled buttons swallow pointer events
    // in some browsers, which breaks Fluent's Tooltip relationship="label".
    return disabled ? (
      button
    ) : (
      <Tooltip key={`tip-${item.id}`} content={item.label} relationship="label" withArrow>
        {button}
      </Tooltip>
    );
  };

  // ── Render the overflow `…` menu containing everything past inlineLimit ─
  const renderOverflowMenu = (): React.ReactNode => {
    if (overflowItems.length === 0) return null;
    return (
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <Button appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More commands" />
        </MenuTrigger>
        <MenuPopover>
          {/*
           * NFR-03 belt-and-suspenders: re-wrap the popover surface so dark
           * mode applies inside the portal. `MenuList` already inherits from
           * the page-level FluentProvider when `applyStylesToPortals` is set,
           * but the inner provider guarantees correct rendering even when
           * a host forgets to opt in at the root.
           */}
          <FluentProvider applyStylesToPortals theme={theme}>
            <MenuList>
              {overflowItems.map(item => (
                <MenuItem
                  key={item.id}
                  icon={resolveIcon(item.icon)}
                  disabled={isDisabled(item)}
                  onClick={() => {
                    void invokeItem(item);
                  }}
                >
                  {item.label}
                </MenuItem>
              ))}
            </MenuList>
          </FluentProvider>
        </MenuPopover>
      </Menu>
    );
  };

  // The divider between inline buttons and the overflow trigger only appears
  // when BOTH sides have content — otherwise it's visual noise.
  const showDivider = inlineItems.length > 0 && overflowItems.length > 0;

  return (
    <div className={mergeClasses(styles.root, className)} role="toolbar" aria-label="Grid actions">
      {inlineItems.map(item => renderInlineButton(item))}
      {showDivider ? <Divider vertical className={styles.divider} /> : null}
      {renderOverflowMenu()}

      {/* ── Bulk-delete confirmation Dialog ─────────────────────────────── */}
      <Dialog
        open={deleteDialogOpen}
        onOpenChange={(_ev, data) => {
          // Disallow dismissing the dialog mid-delete (would orphan the spinner).
          if (isDeleting) return;
          setDeleteDialogOpen(data.open);
        }}
        modalType="modal"
      >
        {/*
         * NFR-03: re-wrap portal surface in FluentProvider so dark mode resolves.
         * `applyStylesToPortals` on the OUTER provider already handles the common
         * case, but we belt-and-suspenders this surface because the Dialog is the
         * most user-visible popover in the framework.
         */}
        <DialogSurface>
          <FluentProvider applyStylesToPortals theme={theme}>
            <DialogBody>
              <DialogTitle>
                Delete {selectedIds.length} record{selectedIds.length === 1 ? '' : 's'}?
              </DialogTitle>
              <DialogContent>
                {isDeleting ? (
                  <div className={styles.dialogStatus}>
                    <Spinner size="tiny" />
                    <Text>
                      Deleting {selectedIds.length} record{selectedIds.length === 1 ? '' : 's'}…
                    </Text>
                  </div>
                ) : (
                  <Text>
                    {selectedIds.length > BULK_PROGRESS_THRESHOLD
                      ? `You are about to delete ${selectedIds.length} ${entityName} records. ` +
                        'This action cannot be undone.'
                      : `Are you sure you want to delete the selected ${entityName} record${selectedIds.length === 1 ? '' : 's'}? ` +
                        'This action cannot be undone.'}
                  </Text>
                )}
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" disabled={isDeleting} onClick={() => setDeleteDialogOpen(false)}>
                  Cancel
                </Button>
                <Button
                  appearance="primary"
                  disabled={isDeleting}
                  onClick={() => {
                    void performBulkDelete();
                  }}
                >
                  {isDeleting ? 'Deleting…' : 'Delete'}
                </Button>
              </DialogActions>
            </DialogBody>
          </FluentProvider>
        </DialogSurface>
      </Dialog>
    </div>
  );
};

export default CommandBar;
