/**
 * AssociateToStep.tsx
 * Conditional Step 1 of the Document Upload Wizard — parent record association.
 *
 * Appears only in "standalone" mode (wizard opened without a parent context).
 * Allows the user to:
 *   1. Select a record type + specific record to associate uploaded documents with
 *   2. OR check "Upload without association" for general-purpose uploads
 *
 * Layout:
 *   ┌─────────────────────────────────────────────────────────────────────┐
 *   │  Associate To                                                       │
 *   │  Select a record to associate documents with, or upload without.    │
 *   │                                                                     │
 *   │  Record Type:  [ Matter  ▼ ]     [ Select Record 🔍 ]             │
 *   │                                                                     │
 *   │  ┌─────────────────────────────────────────────────────────┐       │
 *   │  │  ✅ Smith v. Jones (MAT-2024-001)          [ ✕ Clear ] │       │
 *   │  └─────────────────────────────────────────────────────────┘       │
 *   │                                                                     │
 *   │  ── or ──                                                           │
 *   │                                                                     │
 *   │  ☐  Upload without association                                      │
 *   │     Documents will be stored without a parent record link.          │
 *   └─────────────────────────────────────────────────────────────────────┘
 *
 * Entity types are loaded dynamically from sprk_recordtype_ref (data-driven,
 * follows the polymorphic resolver pattern — ADR-024).
 *
 * Container ID resolution:
 *   - Associated: selected record's sprk_containerid → fallback to business unit
 *   - Unassociated: always from business unit
 *
 * @see ADR-021  - Fluent UI v9 design system
 * @see ADR-024  - Polymorphic resolver pattern
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Dropdown,
    Option,
    Button,
    Checkbox,
    Spinner,
    MessageBar,
    MessageBarBody,
    Divider,
} from "@fluentui/react-components";
import {
    SearchRegular,
    DismissRegular,
    CheckmarkCircleRegular,
} from "@fluentui/react-icons";

import type { IResolvedParentContext } from "../types";
import { SUPPORTED_ENTITY_TYPES } from "../services/uploadOrchestrator";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IAssociateToStepProps {
    /** Resolved parent context (null until user selects or opts out). */
    resolvedParent: IResolvedParentContext | null;
    /** Called when parent context changes (selection or "no association" toggle). */
    onParentResolved: (ctx: IResolvedParentContext | null) => void;
    /** Whether "upload without association" is checked. */
    isUnassociated: boolean;
    /** Toggle handler for the checkbox. */
    onUnassociatedChanged: (checked: boolean) => void;
}

/** Record type definition loaded from sprk_recordtype_ref. */
interface IRecordTypeDef {
    id: string;
    logicalName: string;
    displayName: string;
}

/**
 * Entity types excluded from the Associate To dropdown.
 * These don't make sense as document upload targets.
 */
const EXCLUDED_ENTITY_TYPES = new Set([
    "sprk_document",        // Cannot associate document to document
    "sprk_billinganalysis", // Billing Analysis is not a valid upload target
]);

/**
 * Hardcoded entity types to include if missing from sprk_recordtype_ref.
 * These are added as fallback entries when the Dataverse data doesn't include them.
 */
const REQUIRED_ENTITY_TYPES: IRecordTypeDef[] = [
    { id: "fallback-workassignment", logicalName: "sprk_workassignment", displayName: "Work Assignment" },
];

// ---------------------------------------------------------------------------
// Xrm helpers (frame-walking pattern from DocumentEmailStep.tsx)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
interface XrmWebApi {
    retrieveMultipleRecords: (entity: string, options: string) => Promise<{ entities: Record<string, unknown>[] }>;
    retrieveRecord: (entity: string, id: string, options: string) => Promise<Record<string, unknown>>;
}

interface XrmUtility {
    lookupObjects: (options: Record<string, unknown>) => Promise<Array<{ id: string; name: string; entityType: string }>>;
    getGlobalContext: () => { userSettings: { userId: string } };
}

interface XrmHandle {
    WebApi: XrmWebApi;
    Utility: XrmUtility;
}

function resolveXrm(): XrmHandle | null {
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            if (xrm?.WebApi?.retrieveMultipleRecords && xrm?.Utility?.lookupObjects) {
                return xrm as XrmHandle;
            }
        } catch {
            // Cross-origin frame — skip
        }
    }
    return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Container ID resolution (business unit fallback)
// ---------------------------------------------------------------------------

async function resolveBusinessUnitContainerId(xrm: XrmHandle): Promise<string> {
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    const userResult = await xrm.WebApi.retrieveRecord(
        "systemuser", userId, "?$select=_businessunitid_value"
    );
    const buId = userResult["_businessunitid_value"] as string;
    if (!buId) throw new Error("Could not resolve business unit for current user");

    const buResult = await xrm.WebApi.retrieveRecord(
        "businessunit", buId, "?$select=sprk_containerid"
    );
    const containerId = buResult["sprk_containerid"] as string;
    if (!containerId) throw new Error("Business unit does not have a container ID configured");

    return containerId;
}

async function resolveContainerIdForRecord(
    xrm: XrmHandle,
    entityLogicalName: string,
    recordId: string
): Promise<string> {
    try {
        const record = await xrm.WebApi.retrieveRecord(
            entityLogicalName, recordId, "?$select=sprk_containerid"
        );
        if (record["sprk_containerid"]) {
            return record["sprk_containerid"] as string;
        }
    } catch {
        // Record may not have sprk_containerid field — fall through to BU
    }
    return resolveBusinessUnitContainerId(xrm);
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalL,
    },
    headerText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    formRow: {
        display: "flex",
        alignItems: "flex-end",
        gap: tokens.spacingHorizontalM,
    },
    dropdownWrapper: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        flex: 1,
        maxWidth: "300px",
    },
    fieldLabel: {
        color: tokens.colorNeutralForeground2,
    },
    selectedRecord: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground3,
    },
    selectedIcon: {
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    selectedText: {
        flex: 1,
        color: tokens.colorNeutralForeground1,
    },
    dividerRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
    },
    checkboxSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    checkboxHint: {
        color: tokens.colorNeutralForeground3,
        paddingLeft: "30px",
    },
});

// ---------------------------------------------------------------------------
// AssociateToStep (exported)
// ---------------------------------------------------------------------------

export const AssociateToStep: React.FC<IAssociateToStepProps> = ({
    resolvedParent,
    onParentResolved,
    isUnassociated,
    onUnassociatedChanged,
}) => {
    const styles = useStyles();

    // ── State ───────────────────────────────────────────────────────────────
    const [recordTypes, setRecordTypes] = React.useState<IRecordTypeDef[]>([]);
    const [isLoadingTypes, setIsLoadingTypes] = React.useState(true);
    const [selectedEntityType, setSelectedEntityType] = React.useState<string>("");
    const [isResolving, setIsResolving] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);

    // ── Load entity types from sprk_recordtype_ref on mount ────────────────
    React.useEffect(() => {
        let cancelled = false;
        (async () => {
            const xrm = resolveXrm();
            if (!xrm) {
                setError("Xrm not available — cannot load record types.");
                setIsLoadingTypes(false);
                return;
            }

            try {
                const query =
                    "?$filter=statecode eq 0" +
                    "&$select=sprk_recordtype_refid,sprk_recordlogicalname,sprk_recorddisplayname" +
                    "&$orderby=sprk_recorddisplayname";
                const result = await xrm.WebApi.retrieveMultipleRecords("sprk_recordtype_ref", query);
                if (cancelled) return;

                let defs: IRecordTypeDef[] = result.entities
                    .map((e) => ({
                        id: e["sprk_recordtype_refid"] as string,
                        logicalName: e["sprk_recordlogicalname"] as string,
                        displayName: e["sprk_recorddisplayname"] as string,
                    }))
                    .filter((d) =>
                        !EXCLUDED_ENTITY_TYPES.has(d.logicalName) &&
                        SUPPORTED_ENTITY_TYPES.has(d.logicalName)
                    );

                // Add required entity types that may be missing from Dataverse data
                const existingLogicalNames = new Set(defs.map((d) => d.logicalName));
                for (const required of REQUIRED_ENTITY_TYPES) {
                    if (!existingLogicalNames.has(required.logicalName)) {
                        defs.push(required);
                    }
                }

                // Re-sort by display name after adding fallbacks
                defs.sort((a, b) => a.displayName.localeCompare(b.displayName));

                setRecordTypes(defs);
                if (defs.length > 0 && !selectedEntityType) {
                    setSelectedEntityType(defs[0].logicalName);
                }
            } catch (err) {
                if (!cancelled) {
                    console.error("[AssociateToStep] Failed to load record types:", err);
                    setError("Failed to load record types. Please try again.");
                }
            } finally {
                if (!cancelled) setIsLoadingTypes(false);
            }
        })();
        return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // ── Handle record selection via Xrm.Utility.lookupObjects ──────────────
    const handleSelectRecord = React.useCallback(async () => {
        if (!selectedEntityType) return;

        const xrm = resolveXrm();
        if (!xrm) {
            setError("Xrm not available — cannot open record picker.");
            return;
        }

        try {
            setError(null);
            const results = await xrm.Utility.lookupObjects({
                defaultEntityType: selectedEntityType,
                entityTypes: [selectedEntityType],
                allowMultiSelect: false,
            });

            if (!results || results.length === 0) return; // User cancelled

            const selected = results[0];
            const cleanId = selected.id.replace(/[{}]/g, "").toLowerCase();

            // Resolve container ID from the selected record (fallback to BU)
            setIsResolving(true);
            const containerId = await resolveContainerIdForRecord(xrm, selectedEntityType, cleanId);

            onParentResolved({
                parentEntityType: selectedEntityType,
                parentEntityId: cleanId,
                parentEntityName: selected.name,
                containerId,
                isUnassociated: false,
            });
        } catch (err) {
            console.error("[AssociateToStep] Record selection failed:", err);
            setError(err instanceof Error ? err.message : "Failed to select record.");
        } finally {
            setIsResolving(false);
        }
    }, [selectedEntityType, onParentResolved]);

    // ── Handle "upload without association" toggle ──────────────────────────
    const handleUnassociatedToggle = React.useCallback(
        async (_ev: unknown, data: { checked: boolean | "mixed" }) => {
            const checked = data.checked === true;
            onUnassociatedChanged(checked);

            if (checked) {
                const xrm = resolveXrm();
                if (!xrm) {
                    setError("Xrm not available — cannot resolve container.");
                    return;
                }

                try {
                    setIsResolving(true);
                    setError(null);
                    const containerId = await resolveBusinessUnitContainerId(xrm);
                    onParentResolved({
                        parentEntityType: "",
                        parentEntityId: "",
                        parentEntityName: "",
                        containerId,
                        isUnassociated: true,
                    });
                } catch (err) {
                    console.error("[AssociateToStep] BU container resolution failed:", err);
                    setError(err instanceof Error ? err.message : "Failed to resolve container.");
                    onUnassociatedChanged(false);
                } finally {
                    setIsResolving(false);
                }
            } else {
                // Unchecked — clear resolved parent
                onParentResolved(null);
            }
        },
        [onParentResolved, onUnassociatedChanged]
    );

    // ── Handle clear selection ─────────────────────────────────────────────
    const handleClear = React.useCallback(() => {
        onParentResolved(null);
    }, [onParentResolved]);

    // ── Derived state ──────────────────────────────────────────────────────
    const hasSelection = resolvedParent !== null && !resolvedParent.isUnassociated;
    const selectedRecordTypeDef = recordTypes.find((rt) => rt.logicalName === selectedEntityType);

    return (
        <div className={styles.root}>
            {/* Step header */}
            <div className={styles.headerText}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Associate To
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Select a record to associate uploaded documents with, or upload
                    without association.
                </Text>
            </div>

            {/* Error banner */}
            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
            )}

            {/* Entity type dropdown + Select Record button */}
            {isLoadingTypes ? (
                <Spinner size="small" label="Loading record types..." />
            ) : (
                <div className={styles.formRow}>
                    <div className={styles.dropdownWrapper}>
                        <Text size={200} weight="semibold" className={styles.fieldLabel}>
                            Record Type
                        </Text>
                        <Dropdown
                            value={selectedRecordTypeDef?.displayName ?? ""}
                            selectedOptions={selectedEntityType ? [selectedEntityType] : []}
                            onOptionSelect={(_ev, data) => {
                                setSelectedEntityType(data.optionValue ?? "");
                                // Clear previous selection when entity type changes
                                if (hasSelection) onParentResolved(null);
                            }}
                            disabled={isUnassociated || isResolving}
                        >
                            {recordTypes.map((rt) => (
                                <Option key={rt.logicalName} value={rt.logicalName}>
                                    {rt.displayName}
                                </Option>
                            ))}
                        </Dropdown>
                    </div>
                    <Button
                        appearance="primary"
                        icon={<SearchRegular />}
                        onClick={handleSelectRecord}
                        disabled={!selectedEntityType || isUnassociated || isResolving}
                    >
                        Select Record
                    </Button>
                </div>
            )}

            {/* Selected record display */}
            {hasSelection && resolvedParent && (
                <div className={styles.selectedRecord}>
                    <CheckmarkCircleRegular fontSize={20} className={styles.selectedIcon} />
                    <Text size={300} weight="semibold" className={styles.selectedText}>
                        {resolvedParent.parentEntityName}
                    </Text>
                    <Text size={200} className={styles.fieldLabel}>
                        ({selectedRecordTypeDef?.displayName ?? resolvedParent.parentEntityType})
                    </Text>
                    <Button
                        appearance="subtle"
                        icon={<DismissRegular />}
                        size="small"
                        onClick={handleClear}
                        aria-label="Clear selection"
                    />
                </div>
            )}

            {/* Resolving spinner */}
            {isResolving && (
                <Spinner size="tiny" label="Resolving container..." />
            )}

            {/* Divider */}
            <Divider>or</Divider>

            {/* Upload without association checkbox */}
            <div className={styles.checkboxSection}>
                <Checkbox
                    label="Upload without association"
                    checked={isUnassociated}
                    onChange={handleUnassociatedToggle}
                    disabled={isResolving}
                />
                <Text size={200} className={styles.checkboxHint}>
                    Documents will be stored in the general container without a
                    parent record link. You can associate them later.
                </Text>
            </div>
        </div>
    );
};
