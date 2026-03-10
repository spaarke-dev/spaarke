/**
 * NextStepsStep.tsx
 * Step 3 of the Document Upload Wizard — checkbox card selection for
 * optional follow-on actions after file upload.
 *
 * Layout:
 *   ┌─────────────────────────────────────────────────────────────────────┐
 *   │  Next Steps                                                         │
 *   │  Optionally select follow-on actions after uploading your files.    │
 *   │                                                                     │
 *   │  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐      │
 *   │  │ ☐  Send Email   │ │ ☐  Work on      │ │ ☐  Find         │      │
 *   │  │    Share docs   │ │    Analysis      │ │    Similar      │      │
 *   │  └─────────────────┘ └─────────────────┘ └─────────────────┘      │
 *   └─────────────────────────────────────────────────────────────────────┘
 *
 * Selecting "Send Email" dynamically injects a follow-on email step into
 * the wizard sidebar (via addDynamicStep on WizardShell). Deselecting
 * removes that step.
 *
 * "Work on Analysis" and "Find Similar" are stored in parent state for
 * the success screen to use (no dynamic step injection — they launch
 * post-wizard).
 *
 * Constraints:
 *   - Fluent v9: Card, Text — ZERO hardcoded colors
 *   - makeStyles with semantic tokens throughout (ADR-021, dark mode)
 *   - Icons: MailRegular, BrainCircuitRegular, DocumentSearchRegular
 *
 * @see ADR-021  - Fluent UI v9 design system
 * @see ADR-006  - Code Pages for standalone dialogs
 */

import * as React from "react";
import {
    Card,
    Text,
    makeStyles,
    tokens,
    mergeClasses,
} from "@fluentui/react-components";
import {
    MailRegular,
    BrainCircuitRegular,
    DocumentSearchRegular,
    CheckboxCheckedRegular,
    CheckboxUncheckedRegular,
} from "@fluentui/react-icons";

import type { IWizardShellHandle, IWizardStepConfig } from "@spaarke/ui-components/components/Wizard";
import type { NextStepActionId } from "../types";
import { DocumentEmailStep } from "./DocumentEmailStep";
import type { IDocumentEmailStepProps } from "./DocumentEmailStep";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface INextStepCardDef {
    id: NextStepActionId;
    label: string;
    description: string;
    icon: React.ReactNode;
}

export interface INextStepsStepProps {
    /** Currently selected next-step action IDs. */
    selectedNextSteps: NextStepActionId[];
    /** Called when the selection changes. */
    onNextStepsChanged: (selected: NextStepActionId[]) => void;
    /** Ref to the WizardShell handle for dynamic step injection (Send Email). */
    wizardShellRef: React.RefObject<IWizardShellHandle | null>;
    /** Props for the DocumentEmailStep rendered inside the dynamic Send Email step. */
    emailStepProps: IDocumentEmailStepProps;
}

// ---------------------------------------------------------------------------
// Card definitions
// ---------------------------------------------------------------------------

const CARD_DEFS: INextStepCardDef[] = [
    {
        id: "send-email",
        label: "Send Email",
        description: "Share uploaded documents via email.",
        icon: <MailRegular fontSize={28} />,
    },
    {
        id: "work-on-analysis",
        label: "Work on Analysis",
        description: "Start an AI analysis on uploaded documents.",
        icon: <BrainCircuitRegular fontSize={28} />,
    },
    {
        id: "find-similar",
        label: "Find Similar",
        description: "Search for similar documents across the tenant.",
        icon: <DocumentSearchRegular fontSize={28} />,
    },
];

// ---------------------------------------------------------------------------
// Dynamic step constants
// ---------------------------------------------------------------------------

/** Step ID for the dynamically injected Send Email step. */
export const DYNAMIC_SEND_EMAIL_STEP_ID = "dynamic-send-email";

/** Step ID for the dynamically injected Work on Analysis step. */
export const DYNAMIC_WORK_ON_ANALYSIS_STEP_ID = "dynamic-work-on-analysis";

/** Step ID for the dynamically injected Find Similar step. */
export const DYNAMIC_FIND_SIMILAR_STEP_ID = "dynamic-find-similar";

/** Map NextStepActionId → dynamic step ID used in the sidebar. */
export const DYNAMIC_STEP_ID_MAP: Record<NextStepActionId, string> = {
    "send-email": DYNAMIC_SEND_EMAIL_STEP_ID,
    "work-on-analysis": DYNAMIC_WORK_ON_ANALYSIS_STEP_ID,
    "find-similar": DYNAMIC_FIND_SIMILAR_STEP_ID,
};

/** Map NextStepActionId → step label shown in the sidebar. */
export const DYNAMIC_STEP_LABEL_MAP: Record<NextStepActionId, string> = {
    "send-email": "Send Email",
    "work-on-analysis": "Work on Analysis",
    "find-similar": "Find Similar",
};

/** Canonical order for dynamic steps injected after "next-steps". */
const DYNAMIC_CANONICAL_ORDER = [
    DYNAMIC_SEND_EMAIL_STEP_ID,
    DYNAMIC_WORK_ON_ANALYSIS_STEP_ID,
    DYNAMIC_FIND_SIMILAR_STEP_ID,
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

    // ── Step header ──────────────────────────────────────────────────────
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

    // ── Card row ─────────────────────────────────────────────────────────
    cardRow: {
        display: "grid",
        gridTemplateColumns: "repeat(3, 1fr)",
        gap: tokens.spacingHorizontalM,
    },

    // ── Individual card ──────────────────────────────────────────────────
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

    // ── Card inner layout ────────────────────────────────────────────────
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

    // ── Skip message ─────────────────────────────────────────────────────
    skipMessage: {
        color: tokens.colorNeutralForeground3,
        textAlign: "center",
        paddingTop: tokens.spacingVerticalS,
    },
});

// ---------------------------------------------------------------------------
// CheckboxCard sub-component
// ---------------------------------------------------------------------------

interface ICheckboxCardProps {
    def: INextStepCardDef;
    selected: boolean;
    onToggle: (id: NextStepActionId) => void;
}

const CheckboxCard: React.FC<ICheckboxCardProps> = ({ def, selected, onToggle }) => {
    const styles = useStyles();

    const handleClick = React.useCallback(() => {
        onToggle(def.id);
    }, [def.id, onToggle]);

    const handleKeyDown = React.useCallback(
        (e: React.KeyboardEvent) => {
            if (e.key === " " || e.key === "Enter") {
                e.preventDefault();
                onToggle(def.id);
            }
        },
        [def.id, onToggle]
    );

    return (
        <Card
            className={mergeClasses(styles.card, selected && styles.cardSelected)}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
            role="checkbox"
            aria-checked={selected}
            tabIndex={0}
            aria-label={`${def.label}: ${def.description}${selected ? " — selected" : ""}`}
        >
            {/* Top row: icon + checkbox */}
            <div className={styles.cardTopRow}>
                <span
                    className={mergeClasses(
                        styles.cardIcon,
                        !selected && styles.cardIconNeutral
                    )}
                    aria-hidden="true"
                >
                    {def.icon}
                </span>
                <span
                    className={mergeClasses(
                        styles.checkboxIcon,
                        !selected && styles.checkboxIconNeutral
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

            {/* Label */}
            <Text size={300} weight="semibold" className={styles.cardLabel}>
                {def.label}
            </Text>

            {/* Description */}
            <Text size={200} className={styles.cardDescription}>
                {def.description}
            </Text>
        </Card>
    );
};

// ---------------------------------------------------------------------------
// buildDynamicStepConfig — creates an IWizardStepConfig for a dynamic step.
// ---------------------------------------------------------------------------

/**
 * Builds a dynamic step config for a given next-step action.
 *
 * - Send Email: renders the DocumentEmailStep form
 * - Work on Analysis / Find Similar: placeholder steps (actions launch post-wizard from success screen)
 */
function buildDynamicStepConfig(
    actionId: NextStepActionId,
    emailStepProps: IDocumentEmailStepProps
): IWizardStepConfig {
    const stepId = DYNAMIC_STEP_ID_MAP[actionId];
    const stepLabel = DYNAMIC_STEP_LABEL_MAP[actionId];

    if (actionId === "send-email") {
        return {
            id: stepId,
            label: stepLabel,
            canAdvance: () => true,
            renderContent: () => (
                <DocumentEmailStep {...emailStepProps} />
            ),
        };
    }

    // Work on Analysis and Find Similar are launched from the success screen,
    // but they appear in the sidebar to show the user's selected workflow.
    return {
        id: stepId,
        label: stepLabel,
        canAdvance: () => true,
        renderContent: () => (
            <div style={{ padding: "24px" }}>
                <Text size={300}>
                    {actionId === "work-on-analysis"
                        ? "Analysis will be available after the wizard completes."
                        : "Find Similar will be available after the wizard completes."}
                </Text>
            </div>
        ),
    };
}

// ---------------------------------------------------------------------------
// NextStepsStep (exported)
// ---------------------------------------------------------------------------

export const NextStepsStep: React.FC<INextStepsStepProps> = ({
    selectedNextSteps,
    onNextStepsChanged,
    wizardShellRef,
    emailStepProps,
}) => {
    const styles = useStyles();

    // Track previous selection to diff adds/removes for dynamic steps
    const prevSelectedRef = React.useRef<NextStepActionId[]>([]);

    const handleToggle = React.useCallback(
        (id: NextStepActionId) => {
            let next: NextStepActionId[];
            if (selectedNextSteps.includes(id)) {
                next = selectedNextSteps.filter((a) => a !== id);
            } else {
                // Maintain canonical order: send-email, work-on-analysis, find-similar
                const orderedIds = CARD_DEFS.map((d) => d.id);
                next = orderedIds.filter(
                    (orderedId) => selectedNextSteps.includes(orderedId) || orderedId === id
                );
            }
            onNextStepsChanged(next);
        },
        [selectedNextSteps, onNextStepsChanged]
    );

    // ── Sync dynamic steps with selected actions (via wizardShellRef) ────
    React.useEffect(() => {
        const prev = prevSelectedRef.current;
        const next = selectedNextSteps;

        // Add newly selected follow-on steps
        for (const actionId of next) {
            if (!prev.includes(actionId)) {
                wizardShellRef.current?.addDynamicStep(
                    buildDynamicStepConfig(actionId, emailStepProps),
                    DYNAMIC_CANONICAL_ORDER
                );
            }
        }

        // Remove deselected follow-on steps
        for (const actionId of prev) {
            if (!next.includes(actionId)) {
                wizardShellRef.current?.removeDynamicStep(DYNAMIC_STEP_ID_MAP[actionId]);
            }
        }

        prevSelectedRef.current = next;
    }, [selectedNextSteps, wizardShellRef, emailStepProps]);

    return (
        <div className={styles.root}>
            {/* Step header */}
            <div className={styles.headerText}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Next steps
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Optionally select follow-on actions to perform after uploading your
                    files. You can skip all and handle these later.
                </Text>
            </div>

            {/* 3-card grid */}
            <div className={styles.cardRow} role="group" aria-label="Follow-on actions">
                {CARD_DEFS.map((def) => (
                    <CheckboxCard
                        key={def.id}
                        def={def}
                        selected={selectedNextSteps.includes(def.id)}
                        onToggle={handleToggle}
                    />
                ))}
            </div>

            {/* Optional skip hint */}
            {selectedNextSteps.length === 0 && (
                <Text size={200} className={styles.skipMessage}>
                    No actions selected — click Finish to complete the upload without
                    follow-on steps.
                </Text>
            )}
        </div>
    );
};
