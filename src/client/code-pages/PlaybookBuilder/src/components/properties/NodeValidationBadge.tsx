/**
 * NodeValidationBadge - Inline validation status indicator for node configuration.
 *
 * Displays a small badge/indicator showing the validation state:
 * - Green check: All required fields configured (no errors, no warnings)
 * - Yellow warning: Optional fields missing (no errors, has warnings)
 * - Red error: Required fields missing or invalid (has errors)
 *
 * Designed to appear inline on nodes and in the properties panel.
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useMemo, memo, useState, useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Badge,
    Tooltip,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    shorthands,
} from "@fluentui/react-components";
import {
    CheckmarkCircle20Filled,
    Warning20Filled,
    ErrorCircle20Filled,
} from "@fluentui/react-icons";
import type { PopoverProps } from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    badge: {
        display: "inline-flex",
        alignItems: "center",
        cursor: "default",
    },
    badgeClickable: {
        cursor: "pointer",
    },
    validIcon: {
        color: tokens.colorPaletteGreenForeground1,
    },
    warningIcon: {
        color: tokens.colorPaletteYellowForeground1,
    },
    errorIcon: {
        color: tokens.colorPaletteRedForeground1,
    },
    popoverContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        maxWidth: "280px",
    },
    section: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    sectionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
    },
    errorText: {
        color: tokens.colorPaletteRedForeground1,
        fontSize: tokens.fontSizeBase200,
    },
    warningText: {
        color: tokens.colorPaletteYellowForeground1,
        fontSize: tokens.fontSizeBase200,
    },
    listItem: {
        display: "flex",
        alignItems: "flex-start",
        gap: tokens.spacingHorizontalXS,
        ...shorthands.padding("2px", "0"),
    },
    bullet: {
        flexShrink: 0,
        lineHeight: "1",
    },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface NodeValidationBadgeProps {
    /** Error messages for required fields that are missing or invalid. */
    validationErrors: string[];
    /** Warning messages for optional fields that are missing. */
    warnings?: string[];
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NodeValidationBadge = memo(function NodeValidationBadge({
    validationErrors,
    warnings = [],
}: NodeValidationBadgeProps) {
    const styles = useStyles();
    const [popoverOpen, setPopoverOpen] = useState(false);

    const handleOpenChange: PopoverProps["onOpenChange"] = useCallback(
        (_e, data) => {
            setPopoverOpen(data.open);
        },
        [],
    );

    const hasErrors = validationErrors.length > 0;
    const hasWarnings = warnings.length > 0;
    const hasMessages = hasErrors || hasWarnings;

    // Determine status
    const status = useMemo(() => {
        if (hasErrors) return "error" as const;
        if (hasWarnings) return "warning" as const;
        return "valid" as const;
    }, [hasErrors, hasWarnings]);

    // Tooltip text for quick summary
    const tooltipText = useMemo(() => {
        if (status === "error") {
            return `${validationErrors.length} error${validationErrors.length !== 1 ? "s" : ""}: ${validationErrors[0]}${validationErrors.length > 1 ? "..." : ""}`;
        }
        if (status === "warning") {
            return `${warnings.length} warning${warnings.length !== 1 ? "s" : ""}: ${warnings[0]}${warnings.length > 1 ? "..." : ""}`;
        }
        return "Configuration valid";
    }, [status, validationErrors, warnings]);

    // Icon and badge rendering
    const icon = useMemo(() => {
        switch (status) {
            case "error":
                return <ErrorCircle20Filled className={styles.errorIcon} />;
            case "warning":
                return <Warning20Filled className={styles.warningIcon} />;
            case "valid":
                return <CheckmarkCircle20Filled className={styles.validIcon} />;
        }
    }, [status, styles]);

    const badgeColor = useMemo(() => {
        switch (status) {
            case "error": return "danger" as const;
            case "warning": return "warning" as const;
            case "valid": return "success" as const;
        }
    }, [status]);

    // If no messages, show simple valid badge with tooltip
    if (!hasMessages) {
        return (
            <Tooltip content="Configuration valid" relationship="label">
                <Badge
                    className={styles.badge}
                    appearance="ghost"
                    color={badgeColor}
                    icon={icon}
                    size="small"
                />
            </Tooltip>
        );
    }

    // With messages, show popover on click for detailed list
    return (
        <Popover
            open={popoverOpen}
            onOpenChange={handleOpenChange}
            positioning="below-start"
        >
            <PopoverTrigger>
                <Tooltip content={tooltipText} relationship="description">
                    <Badge
                        className={`${styles.badge} ${styles.badgeClickable}`}
                        appearance="ghost"
                        color={badgeColor}
                        icon={icon}
                        size="small"
                        role="button"
                        tabIndex={0}
                        aria-label={tooltipText}
                    />
                </Tooltip>
            </PopoverTrigger>
            <PopoverSurface>
                <div className={styles.popoverContent}>
                    {/* Errors section */}
                    {hasErrors && (
                        <div className={styles.section}>
                            <Text className={styles.sectionTitle}>
                                Errors ({validationErrors.length})
                            </Text>
                            {validationErrors.map((error, idx) => (
                                <div key={`error-${idx}`} className={styles.listItem}>
                                    <ErrorCircle20Filled
                                        className={`${styles.errorIcon} ${styles.bullet}`}
                                        style={{ fontSize: "14px" }}
                                    />
                                    <Text className={styles.errorText}>
                                        {error}
                                    </Text>
                                </div>
                            ))}
                        </div>
                    )}

                    {/* Warnings section */}
                    {hasWarnings && (
                        <div className={styles.section}>
                            <Text className={styles.sectionTitle}>
                                Warnings ({warnings.length})
                            </Text>
                            {warnings.map((warning, idx) => (
                                <div key={`warning-${idx}`} className={styles.listItem}>
                                    <Warning20Filled
                                        className={`${styles.warningIcon} ${styles.bullet}`}
                                        style={{ fontSize: "14px" }}
                                    />
                                    <Text className={styles.warningText}>
                                        {warning}
                                    </Text>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </PopoverSurface>
        </Popover>
    );
});
