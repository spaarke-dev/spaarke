/**
 * CreateDocumentNextStepsStep.tsx
 * Step 3 of the Create Document wizard — checklist of optional follow-on actions.
 *
 * This step is always advanceable (canAdvance = true). Users may select
 * zero or more follow-on actions to queue after document creation.
 *
 * Based on LegalWorkspace NextStepsStep pattern.
 *
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Card,
    mergeClasses,
} from "@fluentui/react-components";
import {
    DocumentSearchRegular,
    ShareRegular,
    TaskListSquareAddRegular,
    CheckboxCheckedRegular,
    CheckboxUncheckedRegular,
} from "@fluentui/react-icons";
import type { NextStepActionId } from "../types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateDocumentNextStepsStepProps {
    /** Currently selected action IDs. */
    selectedActions: NextStepActionId[];
    /** Called when selection changes. */
    onSelectionChange: (selected: NextStepActionId[]) => void;
}

// ---------------------------------------------------------------------------
// Card definitions
// ---------------------------------------------------------------------------

interface IActionCardDef {
    id: NextStepActionId;
    label: string;
    description: string;
    icon: JSX.Element;
}

const ACTION_CARDS: IActionCardDef[] = [
    {
        id: "run-analysis",
        label: "Run AI Analysis",
        description: "Automatically analyze the uploaded document using the AI pipeline.",
        icon: <DocumentSearchRegular fontSize={28} />,
    },
    {
        id: "share-document",
        label: "Share Document",
        description: "Share the document with team members or external contacts.",
        icon: <ShareRegular fontSize={28} />,
    },
    {
        id: "create-task",
        label: "Create Follow-up Task",
        description: "Create a task to track follow-up actions for this document.",
        icon: <TaskListSquareAddRegular fontSize={28} />,
    },
];

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
    cardRow: {
        display: "grid",
        gridTemplateColumns: "repeat(3, 1fr)",
        gap: tokens.spacingHorizontalM,
    },
    card: {
        cursor: "pointer",
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalL,
        borderTopWidth: "2px",
        borderRightWidth: "2px",
        borderBottomWidth: "2px",
        borderLeftWidth: "2px",
        borderTopStyle: "solid",
        borderRightStyle: "solid",
        borderBottomStyle: "solid",
        borderLeftStyle: "solid",
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        userSelect: "none",
        transition: "border-color 0.1s ease, background-color 0.1s ease",
        boxShadow: "none",
        ":hover": {
            borderTopColor: tokens.colorBrandStroke1,
            borderRightColor: tokens.colorBrandStroke1,
            borderBottomColor: tokens.colorBrandStroke1,
            borderLeftColor: tokens.colorBrandStroke1,
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    cardSelected: {
        borderTopColor: tokens.colorBrandStroke1,
        borderRightColor: tokens.colorBrandStroke1,
        borderBottomColor: tokens.colorBrandStroke1,
        borderLeftColor: tokens.colorBrandStroke1,
        backgroundColor: tokens.colorBrandBackground2,
        ":hover": {
            borderTopColor: tokens.colorBrandStroke1,
            borderRightColor: tokens.colorBrandStroke1,
            borderBottomColor: tokens.colorBrandStroke1,
            borderLeftColor: tokens.colorBrandStroke1,
            backgroundColor: tokens.colorBrandBackground2Hover,
        },
    },
    cardTopRow: {
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalS,
    },
    cardIcon: {
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
    },
    cardIconNeutral: {
        color: tokens.colorNeutralForeground3,
    },
    checkboxIcon: {
        color: tokens.colorBrandForeground1,
        fontSize: "20px",
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
    },
    checkboxIconNeutral: {
        color: tokens.colorNeutralForeground3,
    },
    cardLabel: {
        color: tokens.colorNeutralForeground1,
        marginTop: tokens.spacingVerticalXS,
    },
    cardDescription: {
        color: tokens.colorNeutralForeground2,
    },
    skipMessage: {
        color: tokens.colorNeutralForeground3,
        textAlign: "center",
        paddingTop: tokens.spacingVerticalS,
    },
});

// ---------------------------------------------------------------------------
// CreateDocumentNextStepsStep component
// ---------------------------------------------------------------------------

export function CreateDocumentNextStepsStep({
    selectedActions,
    onSelectionChange,
}: ICreateDocumentNextStepsStepProps): JSX.Element {
    const styles = useStyles();

    const handleToggle = useCallback(
        (id: NextStepActionId) => {
            if (selectedActions.includes(id)) {
                onSelectionChange(selectedActions.filter((a) => a !== id));
            } else {
                // Maintain canonical order
                const orderedIds = ACTION_CARDS.map((d) => d.id);
                const next = orderedIds.filter(
                    (orderedId) => selectedActions.includes(orderedId) || orderedId === id,
                );
                onSelectionChange(next);
            }
        },
        [selectedActions, onSelectionChange],
    );

    return (
        <div className={styles.root}>
            <div className={styles.headerText}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Next steps
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Optionally select follow-on actions to complete after creating the document.
                    You can skip all and handle these from the document record.
                </Text>
            </div>

            <div className={styles.cardRow} role="group" aria-label="Follow-on actions">
                {ACTION_CARDS.map((def) => {
                    const selected = selectedActions.includes(def.id);
                    return (
                        <Card
                            key={def.id}
                            className={mergeClasses(styles.card, selected && styles.cardSelected)}
                            onClick={() => handleToggle(def.id)}
                            onKeyDown={(e: React.KeyboardEvent) => {
                                if (e.key === " " || e.key === "Enter") {
                                    e.preventDefault();
                                    handleToggle(def.id);
                                }
                            }}
                            role="checkbox"
                            aria-checked={selected}
                            tabIndex={0}
                            aria-label={`${def.label}: ${def.description}${selected ? " — selected" : ""}`}
                        >
                            <div className={styles.cardTopRow}>
                                <span
                                    className={mergeClasses(
                                        styles.cardIcon,
                                        !selected && styles.cardIconNeutral,
                                    )}
                                    aria-hidden="true"
                                >
                                    {def.icon}
                                </span>
                                <span
                                    className={mergeClasses(
                                        styles.checkboxIcon,
                                        !selected && styles.checkboxIconNeutral,
                                    )}
                                    aria-hidden="true"
                                >
                                    {selected ? (
                                        <CheckboxCheckedRegular fontSize={22} />
                                    ) : (
                                        <CheckboxUncheckedRegular fontSize={22} />
                                    )}
                                </span>
                            </div>

                            <Text size={300} weight="semibold" className={styles.cardLabel}>
                                {def.label}
                            </Text>

                            <Text size={200} className={styles.cardDescription}>
                                {def.description}
                            </Text>
                        </Card>
                    );
                })}
            </div>

            {selectedActions.length === 0 && (
                <Text size={200} className={styles.skipMessage}>
                    No actions selected — click Finish to create the document without follow-on steps.
                </Text>
            )}
        </div>
    );
}
