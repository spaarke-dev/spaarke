/**
 * AssociateToStep.tsx
 * Shared wizard step for optionally associating a new record with an existing parent.
 *
 * Layout:
 *   ┌────────────────────────────────────────────────────────────────────┐
 *   │  Associate To                                                      │
 *   │  Link this record to an existing record.                           │
 *   │                                                                    │
 *   │  Record Type:  [ Matter  ▼ ]      [ Select Record 🔍 ]           │
 *   │                                                                    │
 *   │  ┌────────────────────────────────────────────────────────┐       │
 *   │  │  ✅ Smith v. Jones (MAT-2024-001)       [ ✕ Clear ]  │       │
 *   │  └────────────────────────────────────────────────────────┘       │
 *   │                                                                    │
 *   │  You can always link records later.                                │
 *   └────────────────────────────────────────────────────────────────────┘
 *
 * The component is fully controlled: the caller owns `value` / `onChange`.
 * When the user clicks "Select Record", the Dataverse lookup side pane is
 * opened via `INavigationService.openLookup()`.  When "Skip" is clicked,
 * `onSkip()` is invoked so the wizard can advance the step index.
 *
 * @see ADR-012 — Shared Component Library (reusable across all create wizards)
 * @see ADR-021 — Fluent UI v9 design system; semantic tokens only
 */

import * as React from "react";
import {
    Button,
    Dropdown,
    MessageBar,
    MessageBarBody,
    Option,
    Spinner,
    Text,
} from "@fluentui/react-components";
import {
    CheckmarkCircleRegular,
    DismissRegular,
    SearchRegular,
} from "@fluentui/react-icons";

import type {
    AssociateToStepProps,
    AssociationResult,
    EntityTypeOption,
} from "./types";
import { useAssociateToStepStyles } from "./AssociateToStep.styles";

// ---------------------------------------------------------------------------
// AssociateToStep
// ---------------------------------------------------------------------------

/**
 * Renders a wizard step that lets the user optionally associate the record
 * being created with an existing Dataverse record via a lookup dialog.
 *
 * @example
 * ```tsx
 * <AssociateToStep
 *   entityTypes={[
 *     { label: "Matter",  entityType: "sprk_matter" },
 *     { label: "Project", entityType: "sprk_project" },
 *   ]}
 *   navigationService={navigationService}
 *   value={association}
 *   onChange={setAssociation}
 *   onSkip={handleSkip}
 * />
 * ```
 */
export const AssociateToStep: React.FC<AssociateToStepProps> = ({
    entityTypes,
    navigationService,
    value,
    onChange,
    onSkip,
    disabled = false,
}) => {
    const styles = useAssociateToStepStyles();

    // ── State ────────────────────────────────────────────────────────────────
    const [selectedEntityType, setSelectedEntityType] = React.useState<string>(
        () => (entityTypes.length > 0 ? entityTypes[0].entityType : "")
    );
    const [isLookupPending, setIsLookupPending] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);

    // Sync selectedEntityType default when entityTypes prop changes (e.g., async load)
    React.useEffect(() => {
        if (!selectedEntityType && entityTypes.length > 0) {
            setSelectedEntityType(entityTypes[0].entityType);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [entityTypes]);

    // ── Derived values ───────────────────────────────────────────────────────
    const selectedTypeDef: EntityTypeOption | undefined = entityTypes.find(
        (et) => et.entityType === selectedEntityType
    );

    const hasSelection = Boolean(value?.recordId);

    // ── Handlers ─────────────────────────────────────────────────────────────

    /** Open Dataverse lookup dialog for the currently selected entity type. */
    const handleSelectRecord = React.useCallback(async () => {
        if (!selectedEntityType) return;

        setError(null);
        setIsLookupPending(true);

        try {
            const results = await navigationService.openLookup({
                entityType: selectedEntityType,
                entityTypes: [selectedEntityType],
                defaultEntityType: selectedEntityType,
                allowMultiSelect: false,
                defaultViewId: selectedTypeDef?.defaultViewId,
            });

            if (!results || results.length === 0) {
                // User cancelled the lookup — no-op
                return;
            }

            const picked = results[0];
            const cleanId = picked.id.replace(/[{}]/g, "").toLowerCase();

            const result: AssociationResult = {
                entityType: picked.entityType || selectedEntityType,
                recordId: cleanId,
                recordName: picked.name,
            };

            onChange?.(result);
        } catch (err) {
            console.error("[AssociateToStep] Lookup failed:", err);
            setError(
                err instanceof Error
                    ? err.message
                    : "Failed to open record lookup. Please try again."
            );
        } finally {
            setIsLookupPending(false);
        }
    }, [selectedEntityType, selectedTypeDef, navigationService, onChange]);

    /** Clear the current selection. */
    const handleClear = React.useCallback(() => {
        setError(null);
        onChange?.(null);
    }, [onChange]);

    /** Update selected entity type; clear existing selection if type changes. */
    const handleEntityTypeChange = React.useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            const next = data.optionValue ?? "";
            if (next !== selectedEntityType) {
                setSelectedEntityType(next);
                if (hasSelection) {
                    onChange?.(null);
                }
            }
        },
        [selectedEntityType, hasSelection, onChange]
    );

    /** Skip association entirely — advance wizard without selecting a record. */
    const handleSkip = React.useCallback(() => {
        setError(null);
        onChange?.(null);
        onSkip?.();
    }, [onChange, onSkip]);

    // ── Render ────────────────────────────────────────────────────────────────

    const isInteractionDisabled = disabled || isLookupPending;

    return (
        <div className={styles.root}>
            {/* Step header */}
            <div className={styles.header}>
                <Text as="h2" size={500} weight="semibold" className={styles.title}>
                    Associate To
                </Text>
                <Text size={200} className={styles.subtitle}>
                    Link this record to an existing record.
                </Text>
            </div>

            {/* Error banner */}
            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
            )}

            {/* Record type dropdown + Select Record button */}
            <div className={styles.formRow}>
                <div className={styles.dropdownWrapper}>
                    <Text size={200} weight="semibold" className={styles.fieldLabel}>
                        Record Type
                    </Text>
                    <Dropdown
                        value={selectedTypeDef?.label ?? ""}
                        selectedOptions={selectedEntityType ? [selectedEntityType] : []}
                        onOptionSelect={handleEntityTypeChange}
                        disabled={isInteractionDisabled}
                    >
                        {entityTypes.map((et) => (
                            <Option key={et.entityType} value={et.entityType}>
                                {et.label}
                            </Option>
                        ))}
                    </Dropdown>
                </div>

                <Button
                    appearance="primary"
                    icon={
                        isLookupPending
                            ? <Spinner size="tiny" />
                            : <SearchRegular />
                    }
                    onClick={handleSelectRecord}
                    disabled={!selectedEntityType || isInteractionDisabled}
                >
                    {isLookupPending ? "Opening…" : "Select Record"}
                </Button>
            </div>

            {/* Selected record display */}
            {hasSelection && value && (
                <div className={styles.selectedRecord}>
                    <CheckmarkCircleRegular fontSize={20} className={styles.selectedIcon} />
                    <Text size={300} weight="semibold" className={styles.selectedName}>
                        {value.recordName}
                    </Text>
                    <Text size={200} className={styles.selectedType}>
                        ({selectedTypeDef?.label ?? value.entityType})
                    </Text>
                    <Button
                        appearance="subtle"
                        icon={<DismissRegular />}
                        size="small"
                        onClick={handleClear}
                        disabled={isInteractionDisabled}
                        aria-label="Clear selection"
                    />
                </div>
            )}

            {/* Helper text */}
            <Text size={200} className={styles.skipHint}>
                You can always link records later.
            </Text>
        </div>
    );
};
