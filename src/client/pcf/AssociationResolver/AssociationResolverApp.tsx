/**
 * AssociationResolver React App Component
 *
 * Main UI for selecting parent entity type and record.
 * Integrates with Field Mapping Service for auto-population.
 *
 * Task 022: Integrated FieldMappingService to auto-apply field mappings
 * after record selection.
 * Task 024: Added toast notifications for mapping results.
 */

import * as React from "react";
import {
    Dropdown,
    Option,
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
    Toaster
} from "@fluentui/react-components";
import { Search20Regular, ArrowSync20Regular, Dismiss20Regular, Open16Regular } from "@fluentui/react-icons";
import { IInputs } from "./generated/ManifestTypes";
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
    EntityLookupConfig
} from "./handlers/RecordSelectionHandler";
import {
    FieldMappingHandler,
    createFieldMappingHandler,
    IFieldMappingApplicationResult
} from "./handlers/FieldMappingHandler";
import { useMappingToast } from "./hooks/useMappingToast";

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
            entityId: recordId.replace(/[{}]/g, '')
        });
    } else {
        console.error("[AssociationResolver] Xrm.Navigation.openForm not available");
    }
}

/**
 * Record Type lookup reference from bound property
 */
interface RecordTypeReference {
    id: string;
    name: string;
    entityLogicalName?: string;  // The entity this record type represents (e.g., "sprk_matter")
}

interface AssociationResolverAppProps {
    context: ComponentFramework.Context<IInputs>;
    regardingRecordType: RecordTypeReference | null;  // Now a lookup to sprk_recordtype_ref
    apiBaseUrl: string;
    onRecordSelected: (recordId: string, recordName: string) => void;
    version: string;
}

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingHorizontalM,
        height: "100%"
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS
    },
    searchSection: {
        display: "flex",
        gap: tokens.spacingHorizontalS,
        alignItems: "flex-end"
    },
    dropdown: {
        minWidth: "200px"
    },
    selectedRecord: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium
    },
    footer: {
        marginTop: "auto",
        paddingTop: tokens.spacingVerticalS,
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center"
    },
    versionText: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3
    }
});

export const AssociationResolverApp: React.FC<AssociationResolverAppProps> = ({
    context,
    regardingRecordType,
    apiBaseUrl,
    onRecordSelected,
    version
}) => {
    const styles = useStyles();

    const [selectedEntityType, setSelectedEntityType] = React.useState<string | null>(null);
    const [selectedRecord, setSelectedRecord] = React.useState<{ id: string; name: string } | null>(null);
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

    // Load entity configs dynamically from sprk_recordtype_ref on mount
    React.useEffect(() => {
        const loadConfigs = async () => {
            if (!context?.webAPI || configsLoaded) return;

            try {
                console.log("[AssociationResolver] Loading dynamic entity configs...");
                const configs = await loadEntityConfigs(context.webAPI);
                setEntityConfigs(configs);
                setConfigsLoaded(true);
                console.log(`[AssociationResolver] Loaded ${configs.length} entity configs`);
            } catch (error) {
                console.error("[AssociationResolver] Error loading entity configs:", error);
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

            console.log("[AssociationResolver] Running auto-detection...");
            setIsLoading(true);

            try {
                // Step 1: Check for pre-populated regarding field (from subgrid context)
                const detected = detectPrePopulatedParent();

                if (detected) {
                    console.log(`[AssociationResolver] Auto-detected parent: ${detected.entityDisplayName} - ${detected.recordName}`);
                    setIsAutoDetected(true);
                    setDetectedParent(detected);
                    setSelectedEntityType(detected.entityType);
                    setSelectedRecord({ id: detected.recordId, name: detected.recordName });

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
                                console.error("[AssociationResolver] Auto field mapping error:", mappingErr);
                            } finally {
                                setIsApplyingMappings(false);
                            }
                        } else {
                            setMappingStatus(`Associated with ${detected.entityDisplayName}: ${detected.recordName}`);
                        }
                    } else {
                        console.warn("[AssociationResolver] Auto-detection completion had errors:", result.errors);
                        setMappingStatus(`Associated with ${detected.entityDisplayName}: ${detected.recordName}`);
                    }
                } else {
                    // No auto-detection - check if bound Record Type is set (fallback)
                    if (regardingRecordType?.id) {
                        try {
                            const recordTypeId = regardingRecordType.id.replace(/[{}]/g, '');
                            const result = await context.webAPI.retrieveRecord(
                                "sprk_recordtype_ref",
                                recordTypeId,
                                "?$select=sprk_recordlogicalname,sprk_recorddisplayname"
                            );

                            const entityLogicalName = result.sprk_recordlogicalname as string;
                            if (entityLogicalName) {
                                const config = entityConfigs.find(c => c.logicalName === entityLogicalName);
                                if (config) {
                                    console.log(`[AssociationResolver] Initialized entity type from Record Type: ${entityLogicalName}`);
                                    setSelectedEntityType(config.logicalName);
                                }
                            }
                        } catch (err) {
                            console.error("[AssociationResolver] Error initializing from Record Type:", err);
                        }
                    }
                }
            } finally {
                setIsLoading(false);
                setAutoDetectionComplete(true);
            }
        };

        autoDetectAndInitialize();
    }, [context?.webAPI, fieldMappingHandler, autoDetectionComplete, regardingRecordType, onRecordSelected, showMappingResult]);

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
                console.log(`[AssociationResolver] Profile check for ${selectedEntityType}: ${hasProfile}`);
            } catch (err) {
                console.error("[AssociationResolver] Error checking profile:", err);
                setHasProfileForEntity(false);
            }
        };

        checkProfileExists();
    }, [fieldMappingHandler, selectedEntityType, selectedRecord]);

    const handleEntityTypeChange = (_event: unknown, data: { optionValue?: string; optionText?: string }) => {
        if (data.optionValue) {
            setSelectedEntityType(data.optionValue);
            setSelectedRecord(null);
            setError(null);
            setMappingStatus(null);
        }
    };

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
            console.warn("[AssociationResolver] FieldMappingHandler not initialized - webAPI not available");
            return null;
        }

        setIsApplyingMappings(true);

        try {
            // Create target record object to receive mapped values
            const targetRecord: Record<string, unknown> = {};

            // Apply mappings from source record to target record object
            const result = await fieldMappingHandler.applyMappingsForSelection(
                sourceEntity,
                sourceRecordId,
                targetRecord
            );

            if (result.profileFound) {
                // Apply mapped values to the form (skipping user-modified fields)
                const fieldsSetOnForm = fieldMappingHandler.applyToForm(targetRecord, true);

                console.log(
                    `[AssociationResolver] Field mappings applied: ` +
                    `${result.fieldsMapped} mapped, ${fieldsSetOnForm} set on form`
                );

                // Get entity display name for toast message
                const entityConfig = entityConfigs.find(c => c.logicalName === sourceEntity);
                const entityName = entityConfig?.displayName || sourceEntity;

                // Update status message
                if (result.fieldsMapped > 0) {
                    setMappingStatus(
                        `${result.fieldsMapped} fields populated from ${entityName}`
                    );
                }

                // Task 024: Show toast notification for mapping result
                showMappingResult(result, entityName);

                // Log any warnings/errors
                if (result.errors.length > 0) {
                    console.warn("[AssociationResolver] Mapping warnings:", result.errors);
                }
            } else {
                console.log(`[AssociationResolver] No field mapping profile found for ${sourceEntity} -> sprk_event`);
            }

            return result;

        } catch (error) {
            console.error("[AssociationResolver] Failed to apply field mappings:", error);
            // Task 024: Show error toast for mapping failures
            showErrorToast("Failed to apply field mappings. Please try again.");
            // Don't set error state - field mapping failure shouldn't block record selection
            return null;
        } finally {
            setIsApplyingMappings(false);
        }
    };

    const handleLookupClick = async () => {
        if (!selectedEntityType) {
            setError("Please select an entity type first");
            return;
        }

        setIsLoading(true);
        setError(null);
        setMappingStatus(null);

        try {
            // Open lookup dialog using Xrm.Utility.lookupObjects
            const lookupOptions = {
                defaultEntityType: selectedEntityType,
                entityTypes: [selectedEntityType],
                allowMultiSelect: false
            };

            // Access Xrm from parent window (PCF runs in iframe)
            const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
            if (!xrm?.Utility?.lookupObjects) {
                throw new Error("Xrm.Utility.lookupObjects not available");
            }

            const results = await xrm.Utility.lookupObjects(lookupOptions);
            if (results && results.length > 0) {
                const selected = results[0];
                const recordId = selected.id.replace(/[{}]/g, '');
                const recordName = selected.name;

                // Create selection object for handler
                const selection: IRecordSelection = {
                    entityType: selectedEntityType,
                    recordId: recordId,
                    recordName: recordName
                };

                // Call handler to populate regarding fields and clear others (async - queries Record Type)
                const result: IRecordSelectionResult = await handleRecordSelection(selection, context.webAPI);

                if (result.success) {
                    setSelectedRecord({
                        id: recordId,
                        name: recordName
                    });
                    onRecordSelected(recordId, recordName);

                    // Show initial success message
                    const clearedCount = result.otherLookupsCleared;
                    setMappingStatus(
                        `Regarding fields set. ${clearedCount} other lookups cleared.`
                    );

                    // Task 022: Apply field mappings from source entity to Event
                    // This auto-populates Event fields based on mapping profiles
                    const mappingResult = await applyFieldMappings(selectedEntityType, recordId);
                    if (mappingResult && mappingResult.fieldsMapped > 0) {
                        // Status already updated by applyFieldMappings
                        // Append to show both actions
                        const entityConfig = entityConfigs.find(c => c.logicalName === selectedEntityType);
                        const entityName = entityConfig?.displayName || selectedEntityType;
                        setMappingStatus(
                            `Regarding fields set. ${mappingResult.fieldsMapped} fields auto-populated from ${entityName}.`
                        );
                    }
                } else {
                    // Partial success - fields may have been set but with errors
                    setSelectedRecord({
                        id: recordId,
                        name: recordName
                    });
                    onRecordSelected(recordId, recordName);

                    if (result.errors.length > 0) {
                        setError(`Warning: ${result.errors.join(", ")}`);
                    }

                    // Still try to apply field mappings even on partial success
                    await applyFieldMappings(selectedEntityType, recordId);
                }
            }
        } catch (err) {
            console.error("[AssociationResolver] Lookup error:", err);
            setError(err instanceof Error ? err.message : "Failed to open lookup");
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
            onRecordSelected("", "");

            setMappingStatus("Selection cleared");
        } catch (err) {
            console.error("[AssociationResolver] Clear error:", err);
            setError(err instanceof Error ? err.message : "Failed to clear selection");
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
            setError("Please select a record first");
            return;
        }

        if (!hasProfileForEntity) {
            setError("No field mapping profile configured for this entity type");
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
            setError("Please select a record first");
            return;
        }

        if (!fieldMappingHandler) {
            setError("Field mapping service not available");
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
                        setMappingStatus(
                            `Refreshed ${fieldsSetOnForm} fields from ${entityName}`
                        );
                    } else {
                        setMappingStatus("No fields to update - all values are current");
                    }

                    // Task 024: Show toast notification for refresh result
                    showMappingResult(mappingResult, entityName);

                    // Show any warnings
                    if (mappingResult.errors.length > 0) {
                        console.warn("[AssociationResolver] Refresh warnings:", mappingResult.errors);
                    }
                } else {
                    setMappingStatus("No field mapping profile configured for this entity type");
                }
            } else {
                setError("Failed to refresh fields from parent");
                // Task 024: Show error toast for refresh failure
                showErrorToast("Failed to refresh fields from parent. Please try again.");
            }
        } catch (err) {
            console.error("[AssociationResolver] Refresh error:", err);
            setError(err instanceof Error ? err.message : "Failed to refresh from parent");
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
                        onClick={(e) => {
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
                        title={hasProfileForEntity
                            ? "Refresh fields from parent record"
                            : "No field mapping profile available"}
                    >
                        {isApplyingMappings ? <Spinner size="tiny" /> : "Refresh"}
                    </Button>
                </div>

                {/* Refresh Confirmation Dialog */}
                <Dialog
                    open={showRefreshConfirm}
                    onOpenChange={(_, data) => setShowRefreshConfirm(data.open)}
                >
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
                    <Text className={styles.versionText}>v{version} • Auto</Text>
                </div>
            </div>
        );
    }

    // Manual selection mode: Show full selection UI
    return (
        <div className={styles.container}>
            {/* Task 024: Toaster for mapping result notifications */}
            <Toaster toasterId={toasterId} position="top-end" />

            <div className={styles.header}>
                <Text weight="semibold" size={400}>Select Parent Record</Text>
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

            <div className={styles.searchSection}>
                <Dropdown
                    className={styles.dropdown}
                    placeholder="Select entity type"
                    value={selectedEntityConfig?.displayName || ""}
                    onOptionSelect={handleEntityTypeChange}
                >
                    {entityConfigs.map(config => (
                        <Option key={config.logicalName} value={config.logicalName}>
                            {config.displayName}
                        </Option>
                    ))}
                </Dropdown>

                <Button
                    appearance="primary"
                    icon={<Search20Regular />}
                    onClick={handleLookupClick}
                    disabled={!selectedEntityType || isLoading || isApplyingMappings}
                >
                    {isLoading || isApplyingMappings ? <Spinner size="tiny" /> : "Select Record"}
                </Button>
            </div>

            {selectedRecord && selectedEntityType && (
                <div className={styles.selectedRecord}>
                    <Text weight="semibold">{selectedEntityConfig?.displayName}:</Text>
                    <Link
                        onClick={(e) => {
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
                        title={hasProfileForEntity
                            ? "Refresh fields from parent record"
                            : "No field mapping profile available for this entity type"}
                    >
                        {isApplyingMappings ? <Spinner size="tiny" /> : "Refresh from Parent"}
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
            <Dialog
                open={showRefreshConfirm}
                onOpenChange={(_, data) => setShowRefreshConfirm(data.open)}
            >
                <DialogSurface>
                    <DialogBody>
                        <DialogTitle>Refresh from Parent?</DialogTitle>
                        <DialogContent>
                            This will overwrite current field values with values from the parent record.
                            Any changes you've made will be lost.
                        </DialogContent>
                        <DialogActions>
                            <Button
                                appearance="secondary"
                                onClick={() => setShowRefreshConfirm(false)}
                            >
                                Cancel
                            </Button>
                            <Button
                                appearance="primary"
                                onClick={confirmRefresh}
                            >
                                Refresh
                            </Button>
                        </DialogActions>
                    </DialogBody>
                </DialogSurface>
            </Dialog>

            <div className={styles.footer}>
                <Text className={styles.versionText}>
                    v{version}
                </Text>
            </div>
        </div>
    );
};
