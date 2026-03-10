/**
 * ScopeList Component
 *
 * Generic checkbox/radio list for selecting scope items (actions, skills,
 * knowledge, tools). Supports multi-select (Checkbox) and single-select
 * (RadioGroup) modes, as well as a read-only locked state.
 *
 * Ported from src/client/pcf/AnalysisBuilder/control/components/ScopeList.tsx
 * and adapted for React 18 / Code Page usage with external selectedIds state.
 */

import React from "react";
import {
    Checkbox,
    Radio,
    RadioGroup,
    Text,
    Spinner,
    makeStyles,
    tokens,
    mergeClasses,
} from "@fluentui/react-components";
import { IScopeItem } from "./types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IScopeListProps<T extends IScopeItem> {
    items: T[];
    selectedIds: string[];
    onSelectionChange: (selectedIds: string[]) => void;
    isLoading: boolean;
    /** When true, render Radio inputs (single select). Default: true (multi-select). */
    multiSelect?: boolean;
    /** Message shown when items array is empty. Default: "No items available". */
    emptyMessage?: string;
    /** When true, all inputs are disabled (scopes are locked). Default: false. */
    readOnly?: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: "8px",
    },
    item: {
        display: "flex",
        alignItems: "flex-start",
        gap: "12px",
        paddingTop: "12px",
        paddingBottom: "12px",
        paddingLeft: "12px",
        paddingRight: "12px",
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        cursor: "pointer",
    },
    itemSelected: {
        backgroundColor: tokens.colorBrandBackground2,
    },
    itemReadOnly: {
        cursor: "default",
        opacity: "0.7",
    },
    selector: {
        flexShrink: 0,
        marginTop: "2px",
    },
    content: {
        flex: 1,
        minWidth: 0,
        display: "flex",
        flexDirection: "column",
        gap: "2px",
    },
    name: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: "1.4",
    },
    loading: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "48px",
        gap: "16px",
    },
    empty: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "48px",
        color: tokens.colorNeutralForeground3,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ScopeList<T extends IScopeItem>({
    items,
    selectedIds,
    onSelectionChange,
    isLoading,
    multiSelect = true,
    emptyMessage = "No items available",
    readOnly = false,
}: IScopeListProps<T>): React.ReactElement {
    const styles = useStyles();

    // ------------------------------------------------------------------
    // Handlers
    // ------------------------------------------------------------------

    const handleCheckboxChange = (itemId: string, checked: boolean): void => {
        if (readOnly) return;

        let newSelected: string[];
        if (checked) {
            newSelected = [...selectedIds, itemId];
        } else {
            newSelected = selectedIds.filter((id) => id !== itemId);
        }
        onSelectionChange(newSelected);
    };

    const handleRadioChange = (_event: unknown, data: { value: string }): void => {
        if (readOnly) return;
        onSelectionChange([data.value]);
    };

    // ------------------------------------------------------------------
    // Loading state
    // ------------------------------------------------------------------

    if (isLoading) {
        return (
            <div className={styles.loading}>
                <Spinner size="medium" label="Loading items..." />
            </div>
        );
    }

    // ------------------------------------------------------------------
    // Empty state
    // ------------------------------------------------------------------

    if (items.length === 0) {
        return (
            <div className={styles.empty}>
                <Text>{emptyMessage}</Text>
            </div>
        );
    }

    // ------------------------------------------------------------------
    // Single-select — RadioGroup
    // ------------------------------------------------------------------

    if (!multiSelect) {
        const selectedValue = selectedIds.length > 0 ? selectedIds[0] : "";

        return (
            <RadioGroup
                value={selectedValue}
                onChange={handleRadioChange}
                disabled={readOnly}
                className={styles.container}
            >
                {items.map((item) => {
                    const isSelected = selectedIds.includes(item.id);
                    return (
                        <div
                            key={item.id}
                            className={mergeClasses(
                                styles.item,
                                isSelected && styles.itemSelected,
                                readOnly && styles.itemReadOnly
                            )}
                        >
                            <Radio
                                value={item.id}
                                disabled={readOnly}
                                className={styles.selector}
                            />
                            <div className={styles.content}>
                                <Text className={styles.name}>{item.name}</Text>
                                {item.description && (
                                    <Text className={styles.description}>
                                        {item.description}
                                    </Text>
                                )}
                            </div>
                        </div>
                    );
                })}
            </RadioGroup>
        );
    }

    // ------------------------------------------------------------------
    // Multi-select — Checkboxes
    // ------------------------------------------------------------------

    return (
        <div className={styles.container}>
            {items.map((item) => {
                const isSelected = selectedIds.includes(item.id);
                return (
                    <div
                        key={item.id}
                        className={mergeClasses(
                            styles.item,
                            isSelected && styles.itemSelected,
                            readOnly && styles.itemReadOnly
                        )}
                    >
                        <Checkbox
                            checked={isSelected}
                            disabled={readOnly}
                            onChange={(_e, data) =>
                                handleCheckboxChange(item.id, !!data.checked)
                            }
                            className={styles.selector}
                        />
                        <div className={styles.content}>
                            <Text className={styles.name}>{item.name}</Text>
                            {item.description && (
                                <Text className={styles.description}>
                                    {item.description}
                                </Text>
                            )}
                        </div>
                    </div>
                );
            })}
        </div>
    );
}

export default ScopeList;
