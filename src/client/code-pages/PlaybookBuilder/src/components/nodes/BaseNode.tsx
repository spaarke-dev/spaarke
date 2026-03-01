/**
 * BaseNode — Common structure for all playbook node types
 *
 * Migrated to @xyflow/react v12 for React 19 Code Page.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 *
 * Node color schemes are applied via inline styles on the icon wrapper
 * using Fluent design tokens instead of hard-coded hex values.
 */

import React from "react";
import { Handle, Position } from "@xyflow/react";
import {
    makeStyles,
    tokens,
    Text,
    mergeClasses,
    shorthands,
} from "@fluentui/react-components";
import type { PlaybookNodeData, PlaybookNodeType } from "../../types/canvas";

const useStyles = makeStyles({
    container: {
        minWidth: "140px",
        maxWidth: "180px",
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
        boxShadow: tokens.shadow4,
        ...shorthands.overflow("hidden"),
        transitionProperty: "box-shadow, border-color",
        transitionDuration: "0.2s",
        transitionTimingFunction: "ease",
    },
    selected: {
        ...shorthands.borderColor(tokens.colorBrandStroke1),
        boxShadow: tokens.shadow16,
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    iconWrapper: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "24px",
        height: "24px",
        borderRadius: tokens.borderRadiusSmall,
        flexShrink: 0,
    },
    headerText: {
        flex: 1,
        overflow: "hidden",
    },
    label: {
        display: "block",
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
    },
    typeLabel: {
        display: "block",
        color: tokens.colorNeutralForeground3,
    },
    body: {
        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    },
    validationIndicator: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    validationError: {
        color: tokens.colorPaletteRedForeground1,
    },
    validationOk: {
        color: tokens.colorPaletteGreenForeground1,
    },
});

/**
 * Node color schemes by type, using Fluent design tokens.
 *
 * Semantic mapping from R4 hex values:
 *   aiAnalysis/aiCompletion=#0078D4 -> colorBrandBackground (blue)
 *   condition=#FFB900             -> colorPaletteYellowBackground3 (yellow)
 *   deliverOutput=#107C10         -> colorPaletteGreenBackground3 (green)
 *   createTask/sendEmail=#8764B8  -> colorPaletteBerryBackground2 (purple)
 *   wait=#E3008C                  -> colorPaletteMagentaBackground2 (magenta)
 *   start                        -> colorNeutralForeground2 (neutral)
 */
export const nodeColorSchemes: Record<
    PlaybookNodeType,
    { background: string; iconColor: string }
> = {
    start: {
        background: tokens.colorNeutralBackground5,
        iconColor: tokens.colorNeutralForeground2,
    },
    aiAnalysis: {
        background: tokens.colorBrandBackground,
        iconColor: tokens.colorNeutralForegroundOnBrand,
    },
    aiCompletion: {
        background: tokens.colorBrandBackground,
        iconColor: tokens.colorNeutralForegroundOnBrand,
    },
    condition: {
        background: tokens.colorPaletteYellowBackground3,
        iconColor: tokens.colorPaletteYellowForeground2,
    },
    deliverOutput: {
        background: tokens.colorPaletteGreenBackground3,
        iconColor: tokens.colorNeutralForegroundOnBrand,
    },
    createTask: {
        background: tokens.colorPaletteBerryBackground2,
        iconColor: tokens.colorNeutralForegroundOnBrand,
    },
    sendEmail: {
        background: tokens.colorPaletteBerryBackground2,
        iconColor: tokens.colorNeutralForegroundOnBrand,
    },
    wait: {
        background: tokens.colorPaletteMagentaBackground2,
        iconColor: tokens.colorNeutralForegroundOnBrand,
    },
};

export interface BaseNodeProps {
    data: PlaybookNodeData;
    selected?: boolean;
    icon: React.ReactNode;
    typeLabel: string;
    children?: React.ReactNode;
    sourceHandleCount?: number;
    targetHandleCount?: number;
}

/**
 * Base node component providing common structure for all node types.
 * Includes header with icon/label, body content area, validation indicator,
 * and connection handles.
 */
export const BaseNode = React.memo(function BaseNode({
    data,
    selected,
    icon,
    typeLabel,
    children,
    sourceHandleCount = 1,
    targetHandleCount = 1,
}: BaseNodeProps) {
    const styles = useStyles();
    const colorScheme = nodeColorSchemes[data.type];
    const hasErrors =
        data.validationErrors && data.validationErrors.length > 0;

    return (
        <div
            className={mergeClasses(
                styles.container,
                selected && styles.selected
            )}
        >
            {/* Target handle (input) */}
            {targetHandleCount > 0 && (
                <Handle type="target" position={Position.Top} />
            )}

            {/* Header */}
            <div className={styles.header}>
                <div
                    className={styles.iconWrapper}
                    style={{
                        backgroundColor: colorScheme.background,
                        color: colorScheme.iconColor,
                    }}
                >
                    {icon}
                </div>
                <div className={styles.headerText}>
                    <Text
                        size={300}
                        weight="semibold"
                        className={styles.label}
                    >
                        {data.label}
                    </Text>
                    <Text size={100} className={styles.typeLabel}>
                        {typeLabel}
                    </Text>
                </div>
            </div>

            {/* Body content */}
            {children && <div className={styles.body}>{children}</div>}

            {/* Validation indicator */}
            {data.isConfigured !== undefined && (
                <div className={styles.validationIndicator}>
                    <Text
                        size={100}
                        className={
                            hasErrors
                                ? styles.validationError
                                : styles.validationOk
                        }
                    >
                        {hasErrors ? "Needs configuration" : "Configured"}
                    </Text>
                </div>
            )}

            {/* Source handle (output) */}
            {sourceHandleCount > 0 && (
                <Handle type="source" position={Position.Bottom} />
            )}
        </div>
    );
});
