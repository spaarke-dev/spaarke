/**
 * AssociationResolver React App Component
 *
 * Main UI for selecting parent entity type and record.
 * Integrates with Field Mapping Service for auto-population.
 *
 * Task 022: Integrated FieldMappingService to auto-apply field mappings
 * after record selection.
 * Task 024: Added toast notifications for mapping results.
 * Task SRFR-052 (Wave 5, 2026-07-02): Refactored to consume the shared
 * `PolymorphicPicker` Fluent v9 component from `@spaarke/ui-components`.
 * The private Dropdown+SearchButton picker (and its `handleEntityTypeChange`
 * + `handleLookupClick` handlers) is deleted; entity-type selection and
 * `Xrm.Utility.lookupObjects` invocation now live inside the shared component.
 * A thin `handlePickerSelect` handler wires `onSelect(entityType, recordId,
 * recordName)` back into `handleRecordSelection` (the SRFR-051 refactored
 * entry point) so the write path is unchanged. See notes/task-052-inventory.md
 * for the mapping between the retired private picker and the shared contract.
 */

import * as React from 'react';
import {
  Button,
  Text,
  Link,
  Spinner,
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Toaster,
} from '@fluentui/react-components';
import { ArrowSync20Regular, Dismiss20Regular, Open16Regular } from '@fluentui/react-icons';
import {
  PolymorphicPicker as PolymorphicPickerRaw,
  type IPolymorphicPickerWebApi,
  type PolymorphicPickerProps,
  type RecordTypeCatalogEntry,
} from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import {
  handleRecordSelection,
  clearAllRegardingFields,
  detectPrePopulatedParent,
  completeAutoDetectedAssociation,
  loadEntityConfigs,
  getEntityConfigs,
  IRecordSelection,
  IRecordSelectionResult,
  IDetectedParentContext,
  EntityLookupConfig,
} from './handlers/RecordSelectionHandler';
import { useMappingToast } from './hooks/useMappingToast';
import {
  createLogger,
  FieldMappingHandler,
  createFieldMappingHandler,
  IFieldMappingApplicationResult,
} from '@spaarke/ui-components';

/**
 * Cast at the seam per SRFR-030 pattern (see wave-3-task-030.log §Divergences 1).
 * The shared lib bundles `React.FC` from React 19 types; PCF pins React 16 per
 * ADR-022. The React 19 `React.FC` return type is not a valid React 16 JSX
 * element type. Casting to `React.ComponentType<PolymorphicPickerProps>` at the
 * import seam typechecks the JSX use-site against the local React 16 types.
 * Runtime is unaffected — the compiled JS module is the same regardless of
 * which type version emitted the `.d.ts`.
 */
const PolymorphicPicker =
  PolymorphicPickerRaw as unknown as React.ComponentType<PolymorphicPickerProps>;

const logger = createLogger('AssociationResolver');

// Build date for UI footer per src/client/pcf/CLAUDE.md Version Update Checklist.
// Bump alongside CONTROL_VERSION (see index.ts) whenever a new deploy ships.
const BUILD_DATE = '2026-07-02';

// Entity configuration type - now loaded dynamically from sprk_recordtype_ref
// Using EntityLookupConfig from RecordSelectionHandler for consistency
type EntityConfig = EntityLookupConfig;

/**
 * Navigate to a record using Xrm.Navigation.openForm
 */
function navigateToRecord(entityLogicalName: string, recordId: string): void {
  const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
  if (xrm?.Navigation?.openForm) {
    xrm.Navigation.openForm({
      entityName: entityLogicalName,
      entityId: recordId.replace(/[{}]/g, ''),
    });
  } else {
    logger.logError('AssociationResolver', 'Xrm.Navigation.openForm not available');
  }
}

/**
 * Record Type lookup reference from bound property
 */
interface RecordTypeReference {
  id: string;
  name: string;
  entityLogicalName?: string; // The entity this record type represents (e.g., "sprk_matter")
}

interface AssociationResolverAppProps {
  context: ComponentFramework.Context<IInputs>;
  regardingRecordType: RecordTypeReference | null; // Now a lookup to sprk_recordtype_ref
  apiBaseUrl: string;
  onRecordSelected: (recordId: string, recordName: string) => void;
  version: string;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    height: '100%',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  selectedRecord: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  footer: {
    marginTop: 'auto',
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * Adapt `EntityLookupConfig[]` (loaded from `sprk_recordtype_ref`) into the
 * shared `RecordTypeCatalogEntry[]` shape consumed by `PolymorphicPicker`.
 *
 * The shared component only needs a stable key, display label, and logical
 * name; `recordTypeRefId` is populated from `logicalName` since the picker
 * doesn't touch the actual `sprk_recordtype_ref` GUID. Regarding-field
 * metadata is passed through unchanged (harmless — shared picker ignores it).
 * Matches the adapter shape used by RegardingResolverApp per SRFR-030.
 */
function adaptCatalogForPicker(catalog: readonly EntityLookupConfig[]): RecordTypeCatalogEntry[] {
  return catalog.map(cfg => ({
    recordTypeRefId: cfg.logicalName,
    displayName: cfg.displayName,
    logicalName: cfg.logicalName,
    regardingField: cfg.regardingField,
    regardingRecordNumberField: cfg.regardingRecordNumberField,
  }));
}

export const AssociationResolverApp: React.FC<AssociationResolverAppProps> = ({
  context,
  regardingRecordType,
  apiBaseUrl: _apiBaseUrl,
  onRecordSelected,
  version,
}) => {
  const styles = useStyles();

  const [selectedEntityType, setSelectedEntityType] = React.useState<string | null>(null);
  const [selectedRecord, setSelectedRecord] = React.useState<{
    id: string;
    name: string;
  } | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [isApplyingMappings, setIsApplyingMappings] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [mappingStatus, setMappingStatus] = React.useState<string | null>(null);
  const [showRefreshConfirm, setShowRefreshConfirm] = React.useState(false);
  const [hasProfileForEntity, setHasProfileForEntity] = React.useState(false);

  // Auto-detection state
  const [isAutoDetected, setIsAutoDetected] = React.useState(false);
  const [autoDetectionComplete, setAutoDetectionComplete] = React.useState(false);
  const [detectedParent, setDetectedParent] = React.useState<IDetectedParentContext | null>(null);

  // Dynamic entity configs - loaded from sprk_recordtype_ref
  const [entityConfigs, setEntityConfigs] = React.useState<EntityConfig[]>(getEntityConfigs());
  const [configsLoaded, setConfigsLoaded] = React.useState(false);

  // Task 024: Toast notifications for mapping results
  const { toasterId, showMappingResult, showError: showErrorToast } = useMappingToast();

  // Field mapping handler - memoized to avoid recreating on every render
  const fieldMappingHandler = React.useMemo<FieldMappingHandler | null>(() => {
    if (context?.webAPI) {
      return createFieldMappingHandler(context.webAPI);
    }
    return null;
  }, [context?.webAPI]);

  // SRFR-052: Adapter memo — EntityLookupConfig[] → RecordTypeCatalogEntry[]
  // for the shared PolymorphicPicker.
  const pickerCatalog = React.useMemo<RecordTypeCatalogEntry[]>(
    () => adaptCatalogForPicker(entityConfigs),
    [entityConfigs]
  );

  // Load entity configs dynamically from sprk_recordtype_ref on mount
  React.useEffect(() => {
    const loadConfigs = async () => {
      if (!context?.webAPI || configsLoaded) return;

      try {
        logger.logInfo('AssociationResolver', 'Loading dynamic entity configs...');
        const configs = await loadEntityConfigs(context.webAPI);
        setEntityConfigs(configs);
        setConfigsLoaded(true);
        logger.logInfo('AssociationResolver', ` Loaded ${configs.length} entity configs`);
      } catch (error) {
        logger.logError('AssociationResolver', 'Error loading entity configs:', error);
        // Keep using fallback configs
        setConfigsLoaded(true);
      }
    };

    loadConfigs();
  }, [context?.webAPI, configsLoaded]);

  // Auto-detect parent context on mount
  // Checks if any regarding lookup field is pre-populated (from subgrid creation)
  // If detected, auto-completes the association and applies field mappings
  React.useEffect(() => {
    const autoDetectAndInitialize = async () => {
      if (autoDetectionComplete || !context?.webAPI) {
        return;
      }

      logger.logInfo('AssociationResolver', 'Running auto-detection...');
      setIsLoading(true);

      try {
        // Step 1: Check for pre-populated regarding field (from subgrid context)
        const detected = detectPrePopulatedParent();

        if (detected) {
          logger.logInfo(
            'AssociationResolver',
            `Auto-detected parent: ${detected.entityDisplayName} - ${detected.recordName}`
          );
          setIsAutoDetected(true);
          setDetectedParent(detected);
          setSelectedEntityType(detected.entityType);
          setSelectedRecord({
            id: detected.recordId,
            name: detected.recordName,
          });

          // Complete the association (set denormalized fields)
          const result = await completeAutoDetectedAssociation(detected, context.webAPI);

          if (result.success) {
            // Notify parent component
            onRecordSelected(detected.recordId, detected.recordName);

            // Apply field mappings automatically
            if (fieldMappingHandler) {
              setIsApplyingMappings(true);
              try {
                const targetRecord: Record<string, unknown> = {};
                const mappingResult = await fieldMappingHandler.applyMappingsForSelection(
                  detected.entityType,
                  detected.recordId,
                  targetRecord
                );

                if (mappingResult.profileFound && mappingResult.fieldsMapped > 0) {
                  fieldMappingHandler.applyToForm(targetRecord, true);
                  setMappingStatus(
                    `Auto-populated from ${detected.entityDisplayName}: ${mappingResult.fieldsMapped} fields mapped`
                  );
                  showMappingResult(mappingResult, detected.entityDisplayName);
                } else {
                  setMappingStatus(`Associated with ${detected.entityDisplayName}: ${detected.recordName}`);
                }
              } catch (mappingErr) {
                logger.logError('AssociationResolver', 'Auto field mapping error:', mappingErr);
              } finally {
                setIsApplyingMappings(false);
              }
            } else {
              setMappingStatus(`Associated with ${detected.entityDisplayName}: ${detected.recordName}`);
            }
          } else {
            logger.logWarn('AssociationResolver', 'Auto-detection completion had errors:', result.errors);
            setMappingStatus(`Associated with ${detected.entityDisplayName}: ${detected.recordName}`);
          }
        } else {
          // No auto-detection - check if bound Record Type is set (fallback)
          if (regardingRecordType?.id) {
            try {
              const recordTypeId = regardingRecordType.id.replace(/[{}]/g, '');
              const result = await context.webAPI.retrieveRecord(
                'sprk_recordtype_ref',
                recordTypeId,
                '?$select=sprk_recordlogicalname,sprk_recorddisplayname'
              );

              const entityLogicalName = result.sprk_recordlogicalname as string;
              if (entityLogicalName) {
                const config = entityConfigs.find(c => c.logicalName === entityLogicalName);
                if (config) {
                  logger.logInfo(
                    'AssociationResolver',
                    ` Initialized entity type from Record Type: ${entityLogicalName}`
                  );
                  setSelectedEntityType(config.logicalName);
                }
              }
            } catch (err) {
              logger.logError('AssociationResolver', 'Error initializing from Record Type:', err);
            }
          }
        }
      } finally {
        setIsLoading(false);
        setAutoDetectionComplete(true);
      }
    };

    autoDetectAndInitialize();
  }, [
    context?.webAPI,
    fieldMappingHandler,
    autoDetectionComplete,
    regardingRecordType,
    onRecordSelected,
    showMappingResult,
  ]);

  // Check if a field mapping profile exists for the current entity type
  // Used to enable/disable the "Refresh from Parent" button
  React.useEffect(() => {
    const checkProfileExists = async () => {
      if (!fieldMappingHandler || !selectedEntityType || !selectedRecord) {
        setHasProfileForEntity(false);
        return;
      }

      try {
        const hasProfile = await fieldMappingHandler.hasProfileForEntity(selectedEntityType);
        setHasProfileForEntity(hasProfile);
        logger.logInfo('AssociationResolver', ` Profile check for ${selectedEntityType}: ${hasProfile}`);
      } catch (err) {
        logger.logError('AssociationResolver', 'Error checking profile:', err);
        setHasProfileForEntity(false);
      }
    };

    checkProfileExists();
  }, [fieldMappingHandler, selectedEntityType, selectedRecord]);

  /**
   * Apply field mappings from source entity to Event (sprk_event)
   * Task 022: Integrates with FieldMappingService after record selection
   *
   * @param sourceEntity - Source entity logical name (e.g., "sprk_matter")
   * @param sourceRecordId - GUID of the selected source record
   * @returns Mapping result or null if handler not available
   */
  const applyFieldMappings = async (
    sourceEntity: string,
    sourceRecordId: string
  ): Promise<IFieldMappingApplicationResult | null> => {
    if (!fieldMappingHandler) {
      logger.logWarn('AssociationResolver', 'FieldMappingHandler not initialized - webAPI not available');
      return null;
    }

    setIsApplyingMappings(true);

    try {
      // Create target record object to receive mapped values
      const targetRecord: Record<string, unknown> = {};

      // Apply mappings from source record to target record object
      const result = await fieldMappingHandler.applyMappingsForSelection(sourceEntity, sourceRecordId, targetRecord);

      if (result.profileFound) {
        // Apply mapped values to the form (skipping user-modified fields)
        const fieldsSetOnForm = fieldMappingHandler.applyToForm(targetRecord, true);

        logger.logInfo(
          'AssociationResolver',
          `Field mappings applied: ${result.fieldsMapped} mapped, ${fieldsSetOnForm} set on form`
        );

        // Get entity display name for toast message
        const entityConfig = entityConfigs.find(c => c.logicalName === sourceEntity);
        const entityName = entityConfig?.displayName || sourceEntity;

        // Update status message
        if (result.fieldsMapped > 0) {
          setMappingStatus(`${result.fieldsMapped} fields populated from ${entityName}`);
        }

        // Task 024: Show toast notification for mapping result
        showMappingResult(result, entityName);

        // Log any warnings/errors
        if (result.errors.length > 0) {
          logger.logWarn('AssociationResolver', 'Mapping warnings:', result.errors);
        }
      } else {
        logger.logInfo('AssociationResolver', ` No field mapping profile found for ${sourceEntity} -> sprk_event`);
      }

      return result;
    } catch (error) {
      logger.logError('AssociationResolver', 'Failed to apply field mappings:', error);
      // Task 024: Show error toast for mapping failures
      showErrorToast('Failed to apply field mappings. Please try again.');
      // Don't set error state - field mapping failure shouldn't block record selection
      return null;
    } finally {
      setIsApplyingMappings(false);
    }
  };

  /**
   * SRFR-052: Handle record selection from the shared `PolymorphicPicker`.
   *
   * `PolymorphicPicker` internally invokes `Xrm.Utility.lookupObjects` scoped to
   * the picked entity type and returns the picked record's `entityType`,
   * `recordId` (cleaned GUID), and `recordName` here.
   *
   * This handler is a thin adapter: it constructs the `IRecordSelection`
   * payload and delegates to `handleRecordSelection` (the SRFR-051 refactored
   * entry point), preserving the existing partial-success semantics + field
   * mapping side effects. Replaces the pre-SRFR-052 `handleLookupClick` /
   * `handleEntityTypeChange` handler pair.
   */
  const handlePickerSelect = async (
    entityType: string,
    recordId: string,
    recordName: string
  ): Promise<void> => {
    setSelectedEntityType(entityType);
    setError(null);
    setMappingStatus(null);
    setIsLoading(true);

    try {
      const selection: IRecordSelection = {
        entityType,
        recordId,
        recordName,
      };

      // Call handler to populate regarding fields and clear others (async - queries Record Type)
      const result: IRecordSelectionResult = await handleRecordSelection(selection, context.webAPI);

      if (result.success) {
        setSelectedRecord({
          id: recordId,
          name: recordName,
        });
        onRecordSelected(recordId, recordName);

        // Show initial success message
        const clearedCount = result.otherLookupsCleared;
        setMappingStatus(`Regarding fields set. ${clearedCount} other lookups cleared.`);

        // Task 022: Apply field mappings from source entity to Event
        // This auto-populates Event fields based on mapping profiles
        const mappingResult = await applyFieldMappings(entityType, recordId);
        if (mappingResult && mappingResult.fieldsMapped > 0) {
          // Status already updated by applyFieldMappings
          // Append to show both actions
          const entityConfig = entityConfigs.find(c => c.logicalName === entityType);
          const entityName = entityConfig?.displayName || entityType;
          setMappingStatus(
            `Regarding fields set. ${mappingResult.fieldsMapped} fields auto-populated from ${entityName}.`
          );
        }
      } else {
        // Partial success - fields may have been set but with errors
        setSelectedRecord({
          id: recordId,
          name: recordName,
        });
        onRecordSelected(recordId, recordName);

        if (result.errors.length > 0) {
          setError(`Warning: ${result.errors.join(', ')}`);
        }

        // Still try to apply field mappings even on partial success
        await applyFieldMappings(entityType, recordId);
      }
    } catch (err) {
      logger.logError('AssociationResolver', 'Selection error:', err);
      setError(err instanceof Error ? err.message : 'Failed to process selection');
    } finally {
      setIsLoading(false);
    }
  };

  /**
   * Handle clearing the selected record
   */
  const handleClearSelection = () => {
    setIsLoading(true);
    setError(null);
    setMappingStatus(null);

    try {
      // Clear all regarding fields on the form
      clearAllRegardingFields();

      // Clear local state
      setSelectedRecord(null);
      onRecordSelected('', '');

      setMappingStatus('Selection cleared');
    } catch (err) {
      logger.logError('AssociationResolver', 'Clear error:', err);
      setError(err instanceof Error ? err.message : 'Failed to clear selection');
    } finally {
      setIsLoading(false);
    }
  };

  /**
   * Handle "Refresh from Parent" button click
   * Shows confirmation dialog before refreshing
   * Task 023: Added confirmation flow
   */
  const handleRefreshClick = () => {
    if (!selectedRecord || !selectedEntityType) {
      setError('Please select a record first');
      return;
    }

    if (!hasProfileForEntity) {
      setError('No field mapping profile configured for this entity type');
      return;
    }

    // Show confirmation dialog
    setShowRefreshConfirm(true);
  };

  /**
   * Confirm refresh and apply field mappings
   * Re-applies field mappings from the currently selected parent record
   * Task 022: Integrated with FieldMappingService
   * Task 023: Called from confirmation dialog with skipDirtyFields=false
   */
  const confirmRefresh = async () => {
    // Close dialog first
    setShowRefreshConfirm(false);

    if (!selectedRecord || !selectedEntityType) {
      setError('Please select a record first');
      return;
    }

    if (!fieldMappingHandler) {
      setError('Field mapping service not available');
      return;
    }

    setIsLoading(true);
    setMappingStatus(null);
    setError(null);

    try {
      // Create target record object to receive mapped values
      const targetRecord: Record<string, unknown> = {};

      // Apply mappings from source record to target record object
      const mappingResult = await fieldMappingHandler.applyMappingsForSelection(
        selectedEntityType,
        selectedRecord.id,
        targetRecord
      );

      if (mappingResult) {
        if (mappingResult.profileFound) {
          // Apply mapped values to the form, overwriting user changes (skipDirtyFields=false)
          // Task 023: Refresh from Parent should overwrite all fields
          const fieldsSetOnForm = fieldMappingHandler.applyToForm(targetRecord, false);

          // Get entity display name for messages
          const entityConfig = entityConfigs.find(c => c.logicalName === selectedEntityType);
          const entityName = entityConfig?.displayName || selectedEntityType;

          if (mappingResult.fieldsMapped > 0) {
            setMappingStatus(`Refreshed ${fieldsSetOnForm} fields from ${entityName}`);
          } else {
            setMappingStatus('No fields to update - all values are current');
          }

          // Task 024: Show toast notification for refresh result
          showMappingResult(mappingResult, entityName);

          // Show any warnings
          if (mappingResult.errors.length > 0) {
            logger.logWarn('AssociationResolver', 'Refresh warnings:', mappingResult.errors);
          }
        } else {
          setMappingStatus('No field mapping profile configured for this entity type');
        }
      } else {
        setError('Failed to refresh fields from parent');
        // Task 024: Show error toast for refresh failure
        showErrorToast('Failed to refresh fields from parent. Please try again.');
      }
    } catch (err) {
      logger.logError('AssociationResolver', 'Refresh error:', err);
      setError(err instanceof Error ? err.message : 'Failed to refresh from parent');
    } finally {
      setIsLoading(false);
    }
  };

  const selectedEntityConfig = entityConfigs.find(c => c.logicalName === selectedEntityType);

  // If still loading/detecting, show minimal loading state
  if (isLoading && !autoDetectionComplete) {
    return (
      <div className={styles.container}>
        <Toaster toasterId={toasterId} position="top-end" />
        <div className={styles.header}>
          <Spinner size="tiny" style={{ marginRight: '8px' }} />
          <Text>Detecting parent context...</Text>
        </div>
      </div>
    );
  }

  // Auto-detected mode: Show read-only association display
  if (isAutoDetected && detectedParent) {
    return (
      <div className={styles.container}>
        <Toaster toasterId={toasterId} position="top-end" />

        {error && (
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        )}

        {isApplyingMappings && (
          <MessageBar intent="info">
            <MessageBarBody>
              <Spinner size="tiny" style={{ marginRight: '8px' }} />
              Applying field mappings...
            </MessageBarBody>
          </MessageBar>
        )}

        {mappingStatus && !isApplyingMappings && (
          <MessageBar intent="success">
            <MessageBarBody>{mappingStatus}</MessageBarBody>
          </MessageBar>
        )}

        {/* Read-only association display */}
        <div className={styles.selectedRecord}>
          <Text weight="semibold">{detectedParent.entityDisplayName}:</Text>
          <Link
            onClick={e => {
              e.preventDefault();
              navigateToRecord(detectedParent.entityType, detectedParent.recordId);
            }}
            style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}
          >
            {detectedParent.recordName}
            <Open16Regular />
          </Link>
          <Button
            appearance="subtle"
            icon={<ArrowSync20Regular />}
            onClick={handleRefreshClick}
            disabled={!hasProfileForEntity || isLoading || isApplyingMappings}
            title={hasProfileForEntity ? 'Refresh fields from parent record' : 'No field mapping profile available'}
          >
            {isApplyingMappings ? <Spinner size="tiny" /> : 'Refresh'}
          </Button>
        </div>

        {/* Refresh Confirmation Dialog */}
        <Dialog open={showRefreshConfirm} onOpenChange={(_, data) => setShowRefreshConfirm(data.open)}>
          <DialogSurface>
            <DialogBody>
              <DialogTitle>Refresh from Parent?</DialogTitle>
              <DialogContent>
                This will overwrite current field values with values from the parent record.
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" onClick={() => setShowRefreshConfirm(false)}>
                  Cancel
                </Button>
                <Button appearance="primary" onClick={confirmRefresh}>
                  Refresh
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>

        <div className={styles.footer}>
          <Text className={styles.versionText}>v{version} • Built {BUILD_DATE} • Auto</Text>
        </div>
      </div>
    );
  }

  // Manual selection mode: Show full selection UI backed by shared PolymorphicPicker
  return (
    <div className={styles.container}>
      {/* Task 024: Toaster for mapping result notifications */}
      <Toaster toasterId={toasterId} position="top-end" />

      <div className={styles.header}>
        <Text weight="semibold" size={400}>
          Select Parent Record
        </Text>
      </div>

      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {isApplyingMappings && (
        <MessageBar intent="info">
          <MessageBarBody>
            <Spinner size="tiny" style={{ marginRight: '8px' }} />
            Applying field mappings...
          </MessageBarBody>
        </MessageBar>
      )}

      {mappingStatus && !isApplyingMappings && (
        <MessageBar intent="success">
          <MessageBarBody>{mappingStatus}</MessageBarBody>
        </MessageBar>
      )}

      {/*
        SRFR-052: Shared PolymorphicPicker replaces the private Dropdown +
        Search button + Xrm.Utility.lookupObjects wiring. The picker renders
        title + toolbar-icon; clicking the icon shows a Menu of entities from
        `pickerCatalog`; picking an entity opens lookupObjects and, on
        selection, invokes `handlePickerSelect(entityType, id, name)`. Errors
        from the picker's internal lookup path surface via `onError` into
        the shared MessageBar above.
      */}
      <PolymorphicPicker
        catalog={pickerCatalog}
        webApi={context.webAPI as unknown as IPolymorphicPickerWebApi}
        title="Select Parent Record"
        onSelect={handlePickerSelect}
        disabled={isLoading || isApplyingMappings}
        onError={setError}
      />

      {selectedRecord && selectedEntityType && (
        <div className={styles.selectedRecord}>
          <Text weight="semibold">{selectedEntityConfig?.displayName}:</Text>
          <Link
            onClick={e => {
              e.preventDefault();
              navigateToRecord(selectedEntityType, selectedRecord.id);
            }}
            style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}
          >
            {selectedRecord.name}
            <Open16Regular />
          </Link>
          <Button
            appearance="subtle"
            icon={<ArrowSync20Regular />}
            onClick={handleRefreshClick}
            disabled={!hasProfileForEntity || isLoading || isApplyingMappings}
            title={
              hasProfileForEntity
                ? 'Refresh fields from parent record'
                : 'No field mapping profile available for this entity type'
            }
          >
            {isApplyingMappings ? <Spinner size="tiny" /> : 'Refresh from Parent'}
          </Button>
          <Button
            appearance="subtle"
            icon={<Dismiss20Regular />}
            onClick={handleClearSelection}
            disabled={isLoading || isApplyingMappings}
            title="Clear selection and regarding fields"
          >
            Clear
          </Button>
        </div>
      )}

      {/* Refresh Confirmation Dialog - Task 023 */}
      <Dialog open={showRefreshConfirm} onOpenChange={(_, data) => setShowRefreshConfirm(data.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Refresh from Parent?</DialogTitle>
            <DialogContent>
              This will overwrite current field values with values from the parent record. Any changes you've made will
              be lost.
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setShowRefreshConfirm(false)}>
                Cancel
              </Button>
              <Button appearance="primary" onClick={confirmRefresh}>
                Refresh
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <div className={styles.footer}>
        <Text className={styles.versionText}>v{version} • Built {BUILD_DATE}</Text>
      </div>
    </div>
  );
};
