/**
 * AssociateToStep.tsx
 * Conditional Step 1 of the Document Upload Wizard — parent record association.
 *
 * Appears only in "standalone" mode (wizard opened without a parent context).
 * Allows the user to select a record type + specific record to associate
 * uploaded documents with. The step is skippable via the Skip button —
 * skipping uploads to the general container without a parent record link.
 *
 * Layout:
 *   ┌─────────────────────────────────────────────────────────────────────┐
 *   │  Associate To                                                       │
 *   │  Select a record to associate uploaded documents with.              │
 *   │                                                                     │
 *   │  Record Type:  [ Account  ▼ ]     [ Select Record 🔍 ]            │
 *   │                                                                     │
 *   │  You can always link records later.                                 │
 *   └─────────────────────────────────────────────────────────────────────┘
 *
 * Entity types are loaded dynamically from sprk_recordtype_ref (data-driven,
 * follows the polymorphic resolver pattern — ADR-024).
 *
 * Container resolution: NONE — deliberately (task 076, 2026-09-03). This step now returns only the
 * IDENTITY of the parent record. The server derives the storage container from that record after
 * authorizing the caller against it, so the authorization key and the destination are one value.
 * The fail-OPEN client resolver that used to live here is documented where it was deleted, below.
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
    Spinner,
    MessageBar,
    MessageBarBody,
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
    /** Resolved parent context (null until user selects a record). */
    resolvedParent: IResolvedParentContext | null;
    /** Called when parent context changes (record selected or cleared). */
    onParentResolved: (ctx: IResolvedParentContext | null) => void;
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
// Narrowed 2026-09-03 (task 076) to exactly what this file calls. `retrieveRecord` and
// `getGlobalContext` were dropped with the two container resolvers that used them — they were the
// only readers. A structural type that still advertises members nothing reaches is how a deleted
// mechanism keeps looking alive.
interface XrmWebApi {
    retrieveMultipleRecords: (entity: string, options: string) => Promise<{ entities: Record<string, unknown>[] }>;
}

interface XrmUtility {
    lookupObjects: (options: Record<string, unknown>) => Promise<Array<{ id: string; name: string; entityType: string }>>;
}

interface XrmHandle {
    WebApi: XrmWebApi;
    Utility: XrmUtility;
}

export function resolveXrm(): XrmHandle | null {
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
// Container ID resolution — DELETED 2026-09-03 (unified-access-control-r2 task 076, finding F-5)
// ---------------------------------------------------------------------------
//
// TWO functions stood here and both are gone: `resolveBusinessUnitContainerId` and
// `resolveContainerIdForRecord`. They are DELETED, not corrected, because the correct behaviour is
// to not ask the question at all. The server resolves the container from the record it authorizes
// the caller against — see `services/uploadOrchestrator.ts#resolveUploadTarget`.
//
// 🔴 What `resolveContainerIdForRecord` actually did, and why deleting it is the point.
// It read the selected record's `sprk_containerid` inside a bare `try { } catch { }` and, on ANY
// failure, returned the CURRENT USER's business-unit container. That catch could not distinguish
// "this entity has no such column" (the case its comment claimed) from a 403, a 404, or a dropped
// connection. So for a SECURE record whose container read was DENIED to this user, it silently
// answered with the SHARED container — and the wizard then uploaded the bytes there. SPE
// permissions are additive-only, so nothing retracts that afterwards.
//
// It was also documented as doing the opposite of what it did: the neighbouring note below used to
// claim the container resolver "THROWS when no container is found". It did not — only the business
// unit helper threw, and only when the BU itself had no container. A record-level denial produced
// the shared container with no error at all. (FAILURE-MODES AP-12: a comment that outlived, and
// then misdescribed, its mechanism.)
//
// Do not reintroduce either function. A client-side container lookup here cannot be made safe: the
// client is not the authorization boundary, so any answer it computes is at best redundant with the
// server's and at worst — exactly in the secure-record case — wrong in the unsafe direction.

// ---------------------------------------------------------------------------
// Search index name resolution (FR-WIZ-06)
// ---------------------------------------------------------------------------
//
// `resolveSearchIndexNameForRecord` lives in a standalone module
// (`./searchIndexResolver.ts`) because it pulls in no JSX / Fluent /
// Xrm-full-handle dependencies — keeping it pure-TS makes the FR-WIZ-06 unit
// tests (3-step chain) trivially runnable without a DOM. Re-exported here so
// the public symbol surface stays at `AssociateToStep.tsx` as the task
// contract requires, and task 027 has a single canonical import location.
//
// It falls back to the PARENT RECORD's owning BU and never throws — empty
// string is a legitimate result that defers to the server-side BFF tenant
// default (FR-BFF-04). It is NOT an authorization decision: an index name
// scopes a search, it does not grant access to anything.
//
export { resolveSearchIndexNameForRecord } from "./searchIndexResolver";
export type { IXrmWebApiLike } from "./searchIndexResolver";

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
});

// ---------------------------------------------------------------------------
// AssociateToStep (exported)
// ---------------------------------------------------------------------------

export const AssociateToStep: React.FC<IAssociateToStepProps> = ({
    resolvedParent,
    onParentResolved,
}) => {
    const styles = useStyles();

    // ── State ───────────────────────────────────────────────────────────────
    const [recordTypes, setRecordTypes] = React.useState<IRecordTypeDef[]>([]);
    const [isLoadingTypes, setIsLoadingTypes] = React.useState(true);
    const [selectedEntityType, setSelectedEntityType] = React.useState<string>("");
    // `isResolving` was DELETED 2026-09-03 (task 076). It gated the controls while the client
    // resolved a container for the selected record. There is no such resolution any more — record
    // selection is now synchronous once the lookup returns.
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

            // No container resolution here any more (task 076). The record IS the answer: the
            // upload names `(selectedEntityType, cleanId)` and the server derives the container
            // from that same record after authorizing the caller against it.
            onParentResolved({
                parentEntityType: selectedEntityType,
                parentEntityId: cleanId,
                parentEntityName: selected.name,
                isUnassociated: false,
            });
        } catch (err) {
            console.error("[AssociateToStep] Record selection failed:", err);
            setError(err instanceof Error ? err.message : "Failed to select record.");
        }
    }, [selectedEntityType, onParentResolved]);

    // NOTE: "Upload without association" is now handled by the Skip button
    // in the wizard dialog (DocumentUploadWizardDialog.tsx), not by a checkbox.

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
                    Select a record to associate uploaded documents with.
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
                        disabled={!selectedEntityType}
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

            {/* The "Resolving container..." spinner was DELETED 2026-09-03 (task 076) — there is no
                client-side container resolution left to wait on. */}

            {/* Hint text */}
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                You can always link records later.
            </Text>
        </div>
    );
};
