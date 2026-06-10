/**
 * RegardingResolverApp — main UI for the polymorphic regarding picker.
 *
 * Layout (single-row form-line):
 *
 *   ┌────────────────────────────────────────────────────────────────────────┐
 *   │  Record Type:  [ Matter  ▼ ]      [ Select Record 🔍 ]                │
 *   │                                                                        │
 *   │  ✅ Smith v. Jones (Matter)                  [ Open ]  [ ✕ Clear ]   │
 *   │                                                                        │
 *   │                                              v1.0.0                    │
 *   └────────────────────────────────────────────────────────────────────────┘
 *
 * When read-only (FR-24), the dropdown / Select Record / Clear are hidden;
 * only the selected target's clickable link is rendered.
 *
 * The component:
 *  - Renders the 11-entity picker (via TODO_REGARDING_CATALOG)
 *  - Calls `applyRegardingSelection` from the local handler on selection.
 *    That handler wraps `applyResolverFields` (ADR-024 / FR-21) — there is
 *    NO field-write logic in this component.
 *  - Notifies the PCF class via `onRecordTypeChanged` so the bound lookup
 *    output is kept in sync (the form picks it up via getOutputs()).
 *  - Auto-seeds the picker on mount from the bound `regardingRecordType`
 *    lookup if it's already populated (mirrors AssociationResolver's
 *    "auto-detect existing parent" pattern).
 */

import * as React from 'react';
import {
  Button,
  Dropdown,
  Label,
  Link,
  MessageBar,
  MessageBarBody,
  Option,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular, Open16Regular, SearchRegular } from '@fluentui/react-icons';
import { TODO_REGARDING_CATALOG, type ITodoRegardingTargetCatalogEntry } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import {
  applyRegardingSelection,
  clearRegarding,
  resolveAllowedCatalog,
  type IRegardingSelection,
  type IResolverWriteContext,
} from './handlers/ResolverWriteHandler';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalS,
    height: '100%',
    boxSizing: 'border-box',
  },
  searchSection: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'flex-end',
    flexWrap: 'wrap',
  },
  dropdownWrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  dropdown: {
    minWidth: '200px',
  },
  selectedRecord: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  selectedLabel: {
    fontWeight: tokens.fontWeightSemibold,
  },
  readOnlyDisplay: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
  },
  footer: {
    marginTop: 'auto',
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Walk through window / parent frames to locate Xrm. PCF runs in an iframe,
 * so the form host is exposed via window.parent or window.top.
 */
function getXrm(): {
  Utility?: {
    lookupObjects: (opts: unknown) => Promise<Array<{ id: string; name: string; entityType?: string }>>;
    getGlobalContext?: () => unknown;
  };
  Navigation?: { openForm: (opts: unknown) => void };
  Page?: Xrm.Page;
} | undefined {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
}

/** Open the host record's regarding parent in a new form. */
function navigateToRecord(entityLogicalName: string, recordId: string): void {
  const xrm = getXrm();
  if (xrm?.Navigation?.openForm) {
    xrm.Navigation.openForm({
      entityName: entityLogicalName,
      entityId: recordId.replace(/[{}]/g, ''),
    });
  } else {
    console.warn('[RegardingResolver] Xrm.Navigation.openForm not available');
  }
}

/** Try to resolve the host record's GUID from `Xrm.Page`. */
function getHostRecordId(): string | undefined {
  const xrm = getXrm();
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data = (xrm?.Page as any)?.data?.entity;
    const id = data?.getId?.();
    if (typeof id === 'string' && id.length > 0) {
      return id.replace(/[{}]/g, '');
    }
  } catch {
    /* ignore */
  }
  return undefined;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface IRegardingResolverAppProps {
  context: ComponentFramework.Context<IInputs>;
  readOnly: boolean;
  onRecordTypeChanged: (value: ComponentFramework.LookupValue | null) => void;
  version: string;
}

export const RegardingResolverApp: React.FC<IRegardingResolverAppProps> = ({
  context,
  readOnly,
  onRecordTypeChanged,
  version,
}) => {
  const styles = useStyles();

  // Host entity (FR-22 lever — single config point, no code branching).
  const hostEntity = (context.parameters.entity?.raw ?? '').trim();

  // Allowed regarding targets (subset of TODO_REGARDING_CATALOG).
  const catalog = React.useMemo<ReadonlyArray<ITodoRegardingTargetCatalogEntry>>(
    () => resolveAllowedCatalog(context.parameters.regardingTargets?.raw),
    [context.parameters.regardingTargets?.raw]
  );

  // The default selection comes from the bound `regardingRecordType` lookup if set.
  // The bound lookup is a Lookup.Simple to sprk_recordtype_ref; we use its `name`
  // (display name) to display the existing selection, but the catalog entry for
  // a given record type is keyed on the parent entity logical name. Since we don't
  // have that mapping from the lookup alone, we render whatever name is bound; the
  // user can re-select to overwrite.
  const boundRecordType = (() => {
    const raw = context.parameters.regardingRecordType?.raw;
    if (!raw) return null;
    const ref = Array.isArray(raw) ? raw[0] : raw;
    if (!ref || !ref.id) return null;
    return { id: ref.id, name: ref.name ?? '' };
  })();

  const [selectedEntityType, setSelectedEntityType] = React.useState<string>(
    () => catalog[0]?.entityType ?? ''
  );
  const [selectedTarget, setSelectedTarget] = React.useState<IRegardingSelection | null>(null);
  const [isLookupPending, setIsLookupPending] = React.useState(false);
  const [isWriting, setIsWriting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [statusMsg, setStatusMsg] = React.useState<string | null>(null);

  // Sync default when catalog changes (e.g. user changes the regardingTargets input).
  React.useEffect(() => {
    if (!catalog.find(c => c.entityType === selectedEntityType)) {
      setSelectedEntityType(catalog[0]?.entityType ?? '');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalog]);

  // ---------------- Write context ----------------
  const writeCtx = React.useMemo<IResolverWriteContext>(
    () => ({
      // context.webAPI is IPolymorphicWebApi-compatible AND has updateRecord.
      webApi: context.webAPI as unknown as IResolverWriteContext['webApi'],
      hostEntity,
      hostRecordId: getHostRecordId(),
    }),
    [context.webAPI, hostEntity]
  );

  // ---------------- Handlers ----------------

  const handleEntityTypeChange = (
    _ev: unknown,
    data: { optionValue?: string }
  ): void => {
    if (data.optionValue) {
      setSelectedEntityType(data.optionValue);
      setError(null);
      setStatusMsg(null);
    }
  };

  const handleSelectRecord = async (): Promise<void> => {
    if (!selectedEntityType) {
      setError('Please select an entity type first.');
      return;
    }
    if (!hostEntity) {
      setError("Host entity is not configured (manifest 'entity' input property is empty).");
      return;
    }

    setIsLookupPending(true);
    setError(null);
    setStatusMsg(null);

    try {
      const xrm = getXrm();
      if (!xrm?.Utility?.lookupObjects) {
        throw new Error('Xrm.Utility.lookupObjects is not available.');
      }

      const results = await xrm.Utility.lookupObjects({
        defaultEntityType: selectedEntityType,
        entityTypes: [selectedEntityType],
        allowMultiSelect: false,
      });

      if (!results || results.length === 0) {
        // User cancelled lookup.
        return;
      }

      const picked = results[0];
      const cleanId = picked.id.replace(/[{}]/g, '').toLowerCase();

      const selection: IRegardingSelection = {
        entityType: selectedEntityType,
        recordId: cleanId,
        recordName: picked.name,
      };

      setIsWriting(true);
      const result = await applyRegardingSelection(writeCtx, selection);
      if (!result.success) {
        setError(result.error ?? 'Failed to apply regarding fields.');
        return;
      }

      setSelectedTarget(selection);

      // Notify PCF class so the bound lookup output is kept in sync. The
      // sprk_regardingrecordtype was written by applyResolverFields via
      // @odata.bind; for the form we surface it as a LookupValue using the
      // existing bound value (if any) — the form's next refresh will pick up
      // the new sprk_recordtype_ref id from the host record itself.
      onRecordTypeChanged({
        id: cleanId,
        name: picked.name,
        entityType: selectedEntityType,
      });

      setStatusMsg(`Associated with ${selection.recordName}.`);
    } catch (err) {
      console.error('[RegardingResolver] handleSelectRecord error:', err);
      setError(err instanceof Error ? err.message : 'Lookup failed.');
    } finally {
      setIsLookupPending(false);
      setIsWriting(false);
    }
  };

  const handleClear = async (): Promise<void> => {
    if (!hostEntity) {
      setError("Host entity is not configured (manifest 'entity' input property is empty).");
      return;
    }

    setIsWriting(true);
    setError(null);
    setStatusMsg(null);

    try {
      const result = await clearRegarding(writeCtx);
      if (!result.success) {
        setError(result.error ?? 'Failed to clear regarding fields.');
        return;
      }
      setSelectedTarget(null);
      onRecordTypeChanged(null);
      setStatusMsg('Regarding cleared.');
    } catch (err) {
      console.error('[RegardingResolver] handleClear error:', err);
      setError(err instanceof Error ? err.message : 'Clear failed.');
    } finally {
      setIsWriting(false);
    }
  };

  // ---------------- Render: read-only (FR-24) ----------------
  if (readOnly) {
    return (
      <div className={styles.container} data-testid="regarding-resolver-readonly">
        {selectedTarget || boundRecordType ? (
          <div className={styles.readOnlyDisplay}>
            <Text className={styles.selectedLabel}>Regarding:</Text>
            {selectedTarget ? (
              <Link
                onClick={(e: React.MouseEvent) => {
                  e.preventDefault();
                  navigateToRecord(selectedTarget.entityType, selectedTarget.recordId);
                }}
                style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}
              >
                {selectedTarget.recordName}
                <Open16Regular />
              </Link>
            ) : (
              <Text>{boundRecordType?.name ?? '(unknown)'}</Text>
            )}
          </div>
        ) : (
          <Text className={styles.selectedLabel}>No regarding selected.</Text>
        )}
        <div className={styles.footer}>
          <Text className={styles.versionText}>v{version}</Text>
        </div>
      </div>
    );
  }

  // ---------------- Render: edit mode ----------------
  const selectedCatalogEntry = catalog.find(c => c.entityType === selectedEntityType);
  const selectedEntityTypeLabel = selectedCatalogEntry?.entityType ?? '';

  return (
    <div className={styles.container} data-testid="regarding-resolver-edit">
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      {statusMsg && !error && (
        <MessageBar intent="success">
          <MessageBarBody>{statusMsg}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.searchSection}>
        <div className={styles.dropdownWrapper}>
          <Label
            id="regarding-resolver-entity-type-label"
            size="small"
            weight="semibold"
          >
            Record Type
          </Label>
          <Dropdown
            className={styles.dropdown}
            aria-labelledby="regarding-resolver-entity-type-label"
            data-testid="regarding-resolver-entity-type-dropdown"
            value={
              catalog.find(c => c.entityType === selectedEntityType)
                ? selectedEntityType
                : ''
            }
            selectedOptions={selectedEntityType ? [selectedEntityType] : []}
            onOptionSelect={handleEntityTypeChange}
            disabled={isLookupPending || isWriting}
          >
            {catalog.map(c => (
              <Option key={c.entityType} value={c.entityType}>
                {c.entityType}
              </Option>
            ))}
          </Dropdown>
        </div>

        <Button
          data-testid="regarding-resolver-select-record-button"
          appearance="primary"
          icon={isLookupPending ? <Spinner size="tiny" /> : <SearchRegular />}
          onClick={handleSelectRecord}
          disabled={!selectedEntityType || isLookupPending || isWriting}
        >
          {isLookupPending ? 'Opening…' : 'Select Record'}
        </Button>
      </div>

      {selectedTarget && (
        <div className={styles.selectedRecord}>
          <Text className={styles.selectedLabel}>{selectedTarget.entityType}:</Text>
          <Link
            onClick={(e: React.MouseEvent) => {
              e.preventDefault();
              navigateToRecord(selectedTarget.entityType, selectedTarget.recordId);
            }}
            style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}
          >
            {selectedTarget.recordName}
            <Open16Regular />
          </Link>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            size="small"
            onClick={handleClear}
            disabled={isLookupPending || isWriting}
            aria-label="Clear selection"
          >
            Clear
          </Button>
        </div>
      )}

      {!selectedTarget && boundRecordType && (
        <div className={styles.selectedRecord}>
          <Text className={styles.selectedLabel}>Currently:</Text>
          <Text>{boundRecordType.name}</Text>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            size="small"
            onClick={handleClear}
            disabled={isLookupPending || isWriting}
            aria-label="Clear current regarding"
          >
            Clear
          </Button>
        </div>
      )}

      <div className={styles.footer}>
        <Text
          className={styles.versionText}
          data-testid="regarding-resolver-version"
        >
          v{version}
          {selectedEntityTypeLabel ? ` • ${selectedEntityTypeLabel}` : ''}
        </Text>
      </div>
    </div>
  );
};
