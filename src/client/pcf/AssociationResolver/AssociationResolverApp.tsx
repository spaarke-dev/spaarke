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
import { Search20Regular, ArrowSync20Regular, Dismiss20Regular } from "@fluentui/react-icons";
import { IInputs } from "./generated/ManifestTypes";
import {
    handleRecordSelection,
    clearAllRegardingFields,
    IRecordSelection,
    IRecordSelectionResult
} from "./handlers/RecordSelectionHandler";
import {
    FieldMappingHandler,
    createFieldMappingHandler,
    IFieldMappingApplicationResult
} from "./handlers/FieldMappingHandler";
import { useMappingToast } from "./hooks/useMappingToast";

// Entity configuration for the 8 supported entity types
interface EntityConfig {
    logicalName: string;
    displayName: string;
    regardingField: string;
    regardingRecordTypeValue: number;
}

// STUB: [CONFIG] - S002: Hardcoded entity configs - should be loaded from Dataverse or config service (Task 020)
// These values must match the optionset values in sprk_event.sprk_regardingrecordtype
const ENTITY_CONFIGS: EntityConfig[] = [
    { logicalName: "sprk_matter", displayName: "Matter", regardingField: "sprk_regardingmatter", regardingRecordTypeValue: 1 },
    { logicalName: "sprk_project", displayName: "Project", regardingField: "sprk_regardingproject", regardingRecordTypeValue: 0 },
    { logicalName: "sprk_invoice", displayName: "Invoice", regardingField: "sprk_regardinginvoice", regardingRecordTypeValue: 2 },
    { logicalName: "sprk_analysis", displayName: "Analysis", regardingField: "sprk_regardinganalysis", regardingRecordTypeValue: 3 },
    { logicalName: "account", displayName: "Account", regardingField: "sprk_regardingaccount", regardingRecordTypeValue: 4 },
    { logicalName: "contact", displayName: "Contact", regardingField: "sprk_regardingcontact", regardingRecordTypeValue: 5 },
    { logicalName: "sprk_workassignment", displayName: "Work Assignment", regardingField: "sprk_regardingworkassignment", regardingRecordTypeValue: 6 },
    { logicalName: "sprk_budget", displayName: "Budget", regardingField: "sprk_regardingbudget", regardingRecordTypeValue: 7 }
];

interface AssociationResolverAppProps {
    context: ComponentFramework.Context<IInputs>;
    regardingRecordType: number | null;
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

    // Task 024: Toast notifications for mapping results
    const { toasterId, showMappingResult, showError: showErrorToast } = useMappingToast();

    // Field mapping handler - memoized to avoid recreating on every render
    const fieldMappingHandler = React.useMemo<FieldMappingHandler | null>(() => {
        if (context?.webAPI) {
            return createFieldMappingHandler(context.webAPI);
        }
        return null;
    }, [context?.webAPI]);

    // Initialize from bound optionset value
    React.useEffect(() => {
        if (regardingRecordType !== null && regardingRecordType !== undefined) {
            const config = ENTITY_CONFIGS.find(c => c.regardingRecordTypeValue === regardingRecordType);
            if (config) {
                setSelectedEntityType(config.logicalName);
            }
        }
    }, [regardingRecordType]);

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
                const entityConfig = ENTITY_CONFIGS.find(c => c.logicalName === sourceEntity);
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

                // Call handler to populate regarding fields and clear others
                const result: IRecordSelectionResult = handleRecordSelection(selection);

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
                        const entityConfig = ENTITY_CONFIGS.find(c => c.logicalName === selectedEntityType);
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
                    const entityConfig = ENTITY_CONFIGS.find(c => c.logicalName === selectedEntityType);
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

    const selectedEntityConfig = ENTITY_CONFIGS.find(c => c.logicalName === selectedEntityType);

    return (
        <div className={styles.container}>
            {/* Task 024: Toaster for mapping result notifications */}
            <Toaster toasterId={toasterId} position="top-end" />

            <div className={styles.header}>
                <Text weight="semibold" size={400}>Association Resolver</Text>
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
                    {ENTITY_CONFIGS.map(config => (
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

            {selectedRecord && (
                <div className={styles.selectedRecord}>
                    <Text weight="semibold">{selectedEntityConfig?.displayName}:</Text>
                    <Text>{selectedRecord.name}</Text>
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
