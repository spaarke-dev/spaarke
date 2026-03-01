/**
 * ActionSelector — Dropdown for selecting an analysis action from Dataverse.
 *
 * Queries sprk_analysisaction records via the scopeStore and renders a
 * Fluent UI v9 Dropdown. Shown for node types that support actions
 * (aiAnalysis, aiCompletion). Saves the selected actionId to the canvas
 * store which then syncs to sprk_playbooknode.sprk_actionid.
 *
 * @see spec.md Section 9.1 — ActionSelector Component
 * @see ADR-021 — Fluent UI v9 Design System
 */

import React, { useEffect, useCallback, useMemo } from "react";
import {
    Dropdown,
    Option,
    Label,
    Spinner,
    makeStyles,
    tokens,
    type OptionOnSelectData,
    type SelectionEvents,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import { useScopeStore } from "../../stores/scopeStore";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 makeStyles — uses design tokens for dark mode)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    label: {
        fontWeight: tokens.fontWeightSemibold,
    },
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        marginTop: tokens.spacingVerticalXXS,
    },
    dropdown: {
        width: "100%",
    },
    loadingContainer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
    },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ActionSelectorProps {
    /** The currently selected action ID (Dataverse GUID). */
    selectedActionId?: string;
    /** Callback when the selected action changes. */
    onActionChange: (actionId: string | undefined) => void;
    /** Whether the selector is disabled. */
    disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ActionSelector: React.FC<ActionSelectorProps> = ({
    selectedActionId,
    onActionChange,
    disabled = false,
}) => {
    const styles = useStyles();
    const actions = useScopeStore((s) => s.actions);
    const isLoadingActions = useScopeStore((s) => s.isLoadingActions);
    const actionsError = useScopeStore((s) => s.actionsError);
    const loadActions = useScopeStore((s) => s.loadActions);

    // Load actions on mount if not already loaded
    useEffect(() => {
        if (actions.length === 0 && !isLoadingActions && !actionsError) {
            loadActions();
        }
    }, [actions.length, isLoadingActions, actionsError, loadActions]);

    // Find the selected action for display
    const selectedAction = useMemo(
        () => actions.find((a) => a.id === selectedActionId),
        [actions, selectedActionId],
    );

    // Handle selection change
    const handleSelect = useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            const selectedValue = data.optionValue;
            if (selectedValue === "__none__") {
                onActionChange(undefined);
            } else {
                onActionChange(selectedValue ?? undefined);
            }
        },
        [onActionChange],
    );

    // Loading state
    if (isLoadingActions) {
        return (
            <div className={styles.container}>
                <Label className={styles.label}>Action</Label>
                <div className={styles.loadingContainer}>
                    <Spinner size="tiny" />
                    <span>Loading actions...</span>
                </div>
            </div>
        );
    }

    // Error state
    if (actionsError) {
        return (
            <div className={styles.container}>
                <Label className={styles.label}>Action</Label>
                <MessageBar intent="error">
                    <MessageBarBody>{actionsError}</MessageBarBody>
                </MessageBar>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <Label className={styles.label} htmlFor="action-selector">
                Action
            </Label>
            <Dropdown
                id="action-selector"
                className={styles.dropdown}
                placeholder="Select an action..."
                value={selectedAction?.name ?? ""}
                selectedOptions={selectedActionId ? [selectedActionId] : []}
                onOptionSelect={handleSelect}
                disabled={disabled}
            >
                <Option key="__none__" value="__none__">
                    (None)
                </Option>
                {actions.map((action) => (
                    <Option key={action.id} value={action.id} text={action.name}>
                        <div>
                            <div>{action.name}</div>
                            {action.description && (
                                <div className={styles.description}>{action.description}</div>
                            )}
                        </div>
                    </Option>
                ))}
            </Dropdown>
        </div>
    );
};

export default ActionSelector;
