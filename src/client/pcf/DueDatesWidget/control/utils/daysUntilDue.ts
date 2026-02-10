/**
 * Days Until Due Utility
 *
 * Calculates and formats the days until an event is due.
 * Handles future dates, today, and overdue (past) dates.
 *
 * ADR Compliance:
 * - ADR-021: Uses Fluent tokens for urgency color mapping
 */

import { tokens } from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Urgency level based on days until due.
 */
export type UrgencyLevel = "overdue" | "critical" | "urgent" | "warning" | "normal";

/**
 * Result of calculating days until due.
 */
export interface IDaysUntilDueResult {
    /** Raw number of days (negative if overdue) */
    days: number;
    /** Absolute value of days */
    absoluteDays: number;
    /** Whether the event is overdue */
    isOverdue: boolean;
    /** Whether the event is due today */
    isDueToday: boolean;
    /** Formatted display string (e.g., "3", "+2", "Today") */
    displayValue: string;
    /** Urgency level for styling */
    urgency: UrgencyLevel;
    /** Accessible label for screen readers */
    accessibleLabel: string;
}

/**
 * Color configuration based on urgency.
 */
export interface IUrgencyColorConfig {
    /** Background color token */
    background: string;
    /** Foreground (text) color token */
    foreground: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Milliseconds per day */
const MS_PER_DAY = 24 * 60 * 60 * 1000;

/** Urgency thresholds in days */
export const URGENCY_THRESHOLDS = {
    /** Critical: 0-1 days (today or tomorrow) */
    CRITICAL: 1,
    /** Urgent: 2-3 days */
    URGENT: 3,
    /** Warning: 4-7 days */
    WARNING: 7
};

// ─────────────────────────────────────────────────────────────────────────────
// Urgency Color Configurations (ADR-021: Design tokens only)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Color configurations for each urgency level.
 * Uses Fluent UI v9 semantic tokens for dark mode compatibility.
 */
export const urgencyColorConfigs: Record<UrgencyLevel, IUrgencyColorConfig> = {
    overdue: {
        // Deep red for overdue items - most attention-grabbing
        background: tokens.colorStatusDangerBackground3,
        foreground: tokens.colorNeutralForegroundOnBrand
    },
    critical: {
        // Red for critical (today/tomorrow)
        background: tokens.colorPaletteRedBackground3,
        foreground: tokens.colorNeutralForegroundOnBrand
    },
    urgent: {
        // Dark orange/red for urgent (2-3 days)
        background: tokens.colorPaletteDarkOrangeBackground3,
        foreground: tokens.colorNeutralForegroundOnBrand
    },
    warning: {
        // Orange for warning (4-7 days)
        background: tokens.colorPaletteMarigoldBackground3,
        foreground: tokens.colorNeutralForeground1
    },
    normal: {
        // Neutral for normal (8+ days)
        background: tokens.colorNeutralBackground5,
        foreground: tokens.colorNeutralForeground1
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// Calculation Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Calculate the number of days between two dates.
 * Returns positive for future dates, negative for past dates.
 *
 * @param dueDate - The due date
 * @param referenceDate - The reference date (defaults to today)
 * @returns Number of days (can be negative for overdue)
 */
export function calculateDaysDifference(dueDate: Date, referenceDate?: Date): number {
    const reference = referenceDate ?? new Date();

    // Normalize both dates to midnight for accurate day calculation
    const dueMidnight = new Date(dueDate.getFullYear(), dueDate.getMonth(), dueDate.getDate());
    const refMidnight = new Date(reference.getFullYear(), reference.getMonth(), reference.getDate());

    const diffMs = dueMidnight.getTime() - refMidnight.getTime();
    return Math.round(diffMs / MS_PER_DAY);
}

/**
 * Determine the urgency level based on days until due.
 *
 * @param days - Number of days until due (negative if overdue)
 * @returns The urgency level
 */
export function getUrgencyLevel(days: number): UrgencyLevel {
    if (days < 0) return "overdue";
    if (days <= URGENCY_THRESHOLDS.CRITICAL) return "critical";
    if (days <= URGENCY_THRESHOLDS.URGENT) return "urgent";
    if (days <= URGENCY_THRESHOLDS.WARNING) return "warning";
    return "normal";
}

/**
 * Format the days value for display.
 *
 * @param days - Number of days until due
 * @param isOverdue - Whether the event is overdue
 * @returns Formatted display string
 */
export function formatDaysDisplay(days: number, isOverdue: boolean): string {
    if (days === 0) return "Today";
    if (isOverdue) return `+${Math.abs(days)}`;
    return String(days);
}

/**
 * Generate an accessible label for screen readers.
 *
 * @param days - Number of days until due
 * @param isOverdue - Whether the event is overdue
 * @returns Accessible label string
 */
export function generateAccessibleLabel(days: number, isOverdue: boolean): string {
    const absoluteDays = Math.abs(days);

    if (days === 0) {
        return "Due today";
    }

    if (isOverdue) {
        return absoluteDays === 1
            ? "1 day overdue"
            : `${absoluteDays} days overdue`;
    }

    return days === 1
        ? "Due in 1 day"
        : `Due in ${days} days`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main Function
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Calculate days until due with all formatting and accessibility information.
 *
 * @param dueDate - The event due date
 * @param referenceDate - Optional reference date (defaults to today)
 * @returns Complete days-until-due result with formatting
 *
 * @example
 * // For an event due in 3 days
 * const result = getDaysUntilDue(futureDate);
 * // result.days = 3
 * // result.displayValue = "3"
 * // result.urgency = "urgent"
 * // result.accessibleLabel = "Due in 3 days"
 *
 * @example
 * // For an overdue event
 * const result = getDaysUntilDue(pastDate);
 * // result.days = -2
 * // result.isOverdue = true
 * // result.displayValue = "+2"
 * // result.urgency = "overdue"
 * // result.accessibleLabel = "2 days overdue"
 */
export function getDaysUntilDue(dueDate: Date, referenceDate?: Date): IDaysUntilDueResult {
    const days = calculateDaysDifference(dueDate, referenceDate);
    const isOverdue = days < 0;
    const isDueToday = days === 0;
    const absoluteDays = Math.abs(days);

    return {
        days,
        absoluteDays,
        isOverdue,
        isDueToday,
        displayValue: formatDaysDisplay(days, isOverdue),
        urgency: getUrgencyLevel(days),
        accessibleLabel: generateAccessibleLabel(days, isOverdue)
    };
}

/**
 * Get the color configuration for a given urgency level or days count.
 *
 * @param urgencyOrDays - Either an urgency level or number of days
 * @returns Color configuration with background and foreground tokens
 */
export function getUrgencyColors(urgencyOrDays: UrgencyLevel | number): IUrgencyColorConfig {
    const urgency = typeof urgencyOrDays === "number"
        ? getUrgencyLevel(urgencyOrDays)
        : urgencyOrDays;

    return urgencyColorConfigs[urgency];
}
