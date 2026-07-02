/**
 * PolymorphicPicker.tsx
 *
 * Shared Fluent v9 picker for polymorphic regarding / association scenarios.
 *
 * Layout (single row):
 *
 *   ┌────────────────────────────────────────────────────────┐
 *   │  {title}                                          [🔍] │
 *   └────────────────────────────────────────────────────────┘
 *
 * Clicking the search icon opens a Fluent v9 `Menu` listing each
 * `RecordTypeCatalogEntry.displayName`. Choosing an entity delegates
 * to `Xrm.Utility.lookupObjects` for record selection; the picked
 * record is returned to the caller via `onSelect(entityType, recordId, recordName)`.
 *
 * # Contract
 *
 * The component is context-agnostic (ADR-012): it does NOT import
 * `ComponentFramework` types. The `webApi` prop is a narrow shim used
 * only for future record queries; today only `Xrm.Utility.lookupObjects`
 * is invoked via a `window`/`window.parent` bridge (unavoidable — PCFs
 * and Code Pages both live in iframes that must reach up to the host).
 *
 * Caller-owned responsibilities (deliberately NOT in this component):
 *   - Any field writes on the host record (call your own resolver handler
 *     from `onSelect`)
 *   - Read-only surface variants (host wraps or replaces the picker)
 *   - Toast / status feedback
 *   - Field mapping refresh flows
 *
 * # Constraints
 *
 *  - ADR-012 (shared component library): no `ComponentFramework.*` imports
 *  - ADR-021 (Fluent v9): semantic tokens only, no hardcoded colors
 *  - ADR-022 (PCF platform libs): React 16/17-safe, no `createRoot`,
 *    no React-18-only hooks
 */

import * as React from 'react';
import {
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MessageBar,
  MessageBarBody,
  Text,
  Toolbar,
  ToolbarButton,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { SearchRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * A single entry in the picker's catalog. Shape is derived from Spaarke's
 * `sprk_recordtype_ref` Dataverse table, but the type is intentionally
 * decoupled from Dataverse (no Xrm imports, no lookup value nesting).
 *
 * @remarks
 * `regardingRecordNumberField` is optional because a subset of catalog
 * targets (contact, account, some legacy sprk_ entities) do not have a
 * canonical "record number" text field to write back to the host record.
 * Callers should handle the undefined case as a graceful no-op per
 * project NFR-06.
 */
export interface RecordTypeCatalogEntry {
  /** GUID of the `sprk_recordtype_ref` row (primary key). */
  readonly recordTypeRefId: string;
  /** User-facing display name (from `sprk_recorddisplayname` / `sprk_recordtypename`). */
  readonly displayName: string;
  /** Target entity logical name (e.g. `sprk_matter`, `contact`). */
  readonly logicalName: string;
  /** Host-side lookup field name (e.g. `sprk_regardingmatter`). */
  readonly regardingField: string;
  /**
   * Target-side "record number" text field name. Optional because some
   * catalog targets (contact, account) have no canonical record-number
   * field. When absent, callers should skip the record-number write.
   */
  readonly regardingRecordNumberField?: string;
}

/**
 * Minimal `webApi` shim accepted by {@link PolymorphicPicker}. The component
 * does not currently call any of these methods — the prop exists so consumers
 * standardize on injecting a webApi bridge from day one. Future extensions
 * (e.g. inline enrichment of the picked record with additional fields) will
 * live behind this contract without breaking callers.
 *
 * @remarks
 * The shape matches `ComponentFramework.WebApi` structurally but does NOT
 * import from `ComponentFramework` to satisfy ADR-012. PCF callers can pass
 * `context.webAPI as unknown as IPolymorphicPickerWebApi`; Code Page callers
 * pass a Dataverse client adapter.
 */
export interface IPolymorphicPickerWebApi {
  retrieveRecord?(entityLogicalName: string, id: string, options?: string): Promise<unknown>;
  retrieveMultipleRecords?(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ): Promise<unknown>;
}

/**
 * Props for {@link PolymorphicPicker}.
 */
export interface PolymorphicPickerProps {
  /** Catalog of allowed entity targets. Rendered as one menu item each. */
  readonly catalog: readonly RecordTypeCatalogEntry[];
  /**
   * Invoked after the user picks a record from `Xrm.Utility.lookupObjects`.
   * `entityType` is the logical name of the picked entity; `recordId` is the
   * cleaned GUID (no curly braces, lowercase); `recordName` is the primary
   * name attribute value.
   */
  readonly onSelect: (entityType: string, recordId: string, recordName: string) => void;
  /**
   * Narrow webApi bridge. Not currently invoked by the component itself but
   * accepted as a prop so consumers standardize on injection. See
   * {@link IPolymorphicPickerWebApi}.
   */
  readonly webApi: IPolymorphicPickerWebApi;
  /**
   * Optional error notifier — invoked when `Xrm.Utility.lookupObjects` is
   * unavailable or throws. When omitted, errors surface only in the
   * component's inline MessageBar.
   */
  readonly onError?: (message: string) => void;
  /** Non-interactive state (menu trigger disabled; no lookup possible). */
  readonly disabled?: boolean;
  /**
   * Read-only display: title still renders but the icon button is hidden.
   * When both `disabled` and `readOnly` are true, `readOnly` wins.
   */
  readonly readOnly?: boolean;
  /** Optional title. Default: "Related Record". */
  readonly title?: string;
  /** Optional extra className merged after the component's own root class. */
  readonly className?: string;
}

// ---------------------------------------------------------------------------
// Styles (module scope per fluent-v9-component-authoring pattern)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    minHeight: '32px',
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  toolbar: {
    // Zero horizontal padding — Toolbar's default padding pushes the icon off-center
    // in a compact single-button header row.
    paddingLeft: 0,
    paddingRight: 0,
    minHeight: 'auto',
  },
});

// ---------------------------------------------------------------------------
// Xrm bridge (unavoidable window traversal — PCFs and Code Pages both live
// in iframes that must reach up to the host to call `Xrm.Utility.*`).
// ---------------------------------------------------------------------------

interface IXrmUtility {
  lookupObjects: (opts: {
    entityTypes: string[];
    defaultEntityType?: string;
    allowMultiSelect: boolean;
  }) => Promise<Array<{ id: string; name: string; entityType?: string }>>;
}

interface IXrmBridge {
  Utility?: IXrmUtility;
}

/**
 * Locate Xrm on the current window or one of its parents. Exported for tests
 * to override via mocking; not part of the public component API surface.
 *
 * @internal
 */
export function getXrmForPicker(): IXrmBridge | undefined {
  if (typeof window === 'undefined') return undefined;
  const w = window as unknown as { Xrm?: IXrmBridge; parent?: { Xrm?: IXrmBridge }; top?: { Xrm?: IXrmBridge } };
  return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Shared polymorphic entity/record picker.
 *
 * @example
 * ```tsx
 * const catalog: RecordTypeCatalogEntry[] = await loadCatalog(webApi);
 * <PolymorphicPicker
 *   catalog={catalog}
 *   webApi={webApi}
 *   title="Regarding"
 *   onSelect={(entityType, id, name) => applyRegarding(entityType, id, name)}
 * />
 * ```
 */
export const PolymorphicPicker: React.FC<PolymorphicPickerProps> = ({
  catalog,
  onSelect,
  webApi: _webApi,
  onError,
  disabled = false,
  readOnly = false,
  title = 'Related Record',
  className,
}) => {
  const styles = useStyles();
  const [error, setError] = React.useState<string | null>(null);
  const [isLookingUp, setIsLookingUp] = React.useState(false);

  const handleSelect = React.useCallback(
    async (entityType: string) => {
      if (readOnly || disabled) return;
      setError(null);
      setIsLookingUp(true);
      try {
        const xrm = getXrmForPicker();
        if (!xrm?.Utility?.lookupObjects) {
          const msg = 'Xrm.Utility.lookupObjects is not available.';
          setError(msg);
          onError?.(msg);
          return;
        }
        const results = await xrm.Utility.lookupObjects({
          entityTypes: [entityType],
          defaultEntityType: entityType,
          allowMultiSelect: false,
        });
        if (!results || results.length === 0) {
          // User cancelled — silent no-op per contract.
          return;
        }
        const picked = results[0];
        const cleanId = picked.id.replace(/[{}]/g, '').toLowerCase();
        onSelect(entityType, cleanId, picked.name);
      } catch (err) {
        const msg = err instanceof Error ? err.message : 'Lookup failed.';
        setError(msg);
        onError?.(msg);
      } finally {
        setIsLookingUp(false);
      }
    },
    [readOnly, disabled, onError, onSelect]
  );

  const triggerDisabled = disabled || isLookingUp || catalog.length === 0;

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="polymorphic-picker">
      {error && (
        <MessageBar intent="error" data-testid="polymorphic-picker-error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      <div className={styles.headerRow}>
        <Text className={styles.title} data-testid="polymorphic-picker-title">
          {title}
        </Text>
        {!readOnly && (
          <Toolbar className={styles.toolbar} size="small" aria-label={`${title} actions`}>
            <Menu positioning="below-end">
              <MenuTrigger disableButtonEnhancement>
                <Tooltip content={`Select ${title.toLowerCase()}`} relationship="label" withArrow>
                  <ToolbarButton
                    icon={<SearchRegular />}
                    disabled={triggerDisabled}
                    aria-label={`Select ${title.toLowerCase()}`}
                    data-testid="polymorphic-picker-trigger"
                  />
                </Tooltip>
              </MenuTrigger>
              <MenuPopover>
                <MenuList data-testid="polymorphic-picker-menu">
                  {catalog.map(entry => (
                    <MenuItem
                      key={entry.recordTypeRefId}
                      onClick={() => {
                        void handleSelect(entry.logicalName);
                      }}
                      data-testid={`polymorphic-picker-item-${entry.logicalName}`}
                    >
                      {entry.displayName}
                    </MenuItem>
                  ))}
                </MenuList>
              </MenuPopover>
            </Menu>
          </Toolbar>
        )}
      </div>
    </div>
  );
};
