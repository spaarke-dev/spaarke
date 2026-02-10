/**
 * Tests for daysUntilDue Utility Functions
 *
 * Tests:
 * - calculateDaysDifference: Day calculation between dates
 * - getUrgencyLevel: Urgency level determination
 * - formatDaysDisplay: Display value formatting
 * - generateAccessibleLabel: Accessibility label generation
 * - getDaysUntilDue: Full result object
 * - getUrgencyColors: Color configuration retrieval
 */

import {
    calculateDaysDifference,
    getUrgencyLevel,
    formatDaysDisplay,
    generateAccessibleLabel,
    getDaysUntilDue,
    getUrgencyColors,
    URGENCY_THRESHOLDS,
    urgencyColorConfigs,
    UrgencyLevel
} from '../../utils/daysUntilDue';

describe('daysUntilDue utilities', () => {
    // Helper to create a date at local noon for consistent tests
    const createLocalDate = (year: number, month: number, day: number): Date => {
        return new Date(year, month - 1, day, 12, 0, 0);
    };

    // Fixed reference date for deterministic tests (Feb 4, 2026 at noon local)
    const referenceDate = createLocalDate(2026, 2, 4);

    describe('calculateDaysDifference', () => {
        it('returns 0 for same day', () => {
            const dueDate = createLocalDate(2026, 2, 4);
            expect(calculateDaysDifference(dueDate, referenceDate)).toBe(0);
        });

        it('returns positive number for future dates', () => {
            const dueDate = createLocalDate(2026, 2, 7);
            expect(calculateDaysDifference(dueDate, referenceDate)).toBe(3);
        });

        it('returns negative number for past dates (overdue)', () => {
            const dueDate = createLocalDate(2026, 2, 1);
            expect(calculateDaysDifference(dueDate, referenceDate)).toBe(-3);
        });

        it('handles dates exactly 1 day apart', () => {
            const tomorrow = createLocalDate(2026, 2, 5);
            const yesterday = createLocalDate(2026, 2, 3);

            expect(calculateDaysDifference(tomorrow, referenceDate)).toBe(1);
            expect(calculateDaysDifference(yesterday, referenceDate)).toBe(-1);
        });

        it('normalizes dates to midnight for accurate calculation', () => {
            // Create due date at different times of day
            const dueMorning = new Date(2026, 1, 5, 6, 0, 0);  // 6 AM
            const dueEvening = new Date(2026, 1, 5, 23, 59, 59); // 11:59 PM

            expect(calculateDaysDifference(dueMorning, referenceDate)).toBe(1);
            expect(calculateDaysDifference(dueEvening, referenceDate)).toBe(1);
        });

        it('uses current date when referenceDate not provided', () => {
            const tomorrow = new Date();
            tomorrow.setDate(tomorrow.getDate() + 1);

            const result = calculateDaysDifference(tomorrow);
            expect(result).toBe(1);
        });
    });

    describe('getUrgencyLevel', () => {
        it('returns "overdue" for negative days', () => {
            expect(getUrgencyLevel(-1)).toBe('overdue');
            expect(getUrgencyLevel(-10)).toBe('overdue');
            expect(getUrgencyLevel(-100)).toBe('overdue');
        });

        it('returns "critical" for 0-1 days', () => {
            expect(getUrgencyLevel(0)).toBe('critical');
            expect(getUrgencyLevel(1)).toBe('critical');
        });

        it('returns "urgent" for 2-3 days', () => {
            expect(getUrgencyLevel(2)).toBe('urgent');
            expect(getUrgencyLevel(3)).toBe('urgent');
        });

        it('returns "warning" for 4-7 days', () => {
            expect(getUrgencyLevel(4)).toBe('warning');
            expect(getUrgencyLevel(7)).toBe('warning');
        });

        it('returns "normal" for 8+ days', () => {
            expect(getUrgencyLevel(8)).toBe('normal');
            expect(getUrgencyLevel(30)).toBe('normal');
            expect(getUrgencyLevel(365)).toBe('normal');
        });

        it('respects URGENCY_THRESHOLDS constants', () => {
            expect(getUrgencyLevel(URGENCY_THRESHOLDS.CRITICAL)).toBe('critical');
            expect(getUrgencyLevel(URGENCY_THRESHOLDS.URGENT)).toBe('urgent');
            expect(getUrgencyLevel(URGENCY_THRESHOLDS.WARNING)).toBe('warning');
            expect(getUrgencyLevel(URGENCY_THRESHOLDS.WARNING + 1)).toBe('normal');
        });
    });

    describe('formatDaysDisplay', () => {
        it('returns "Today" for 0 days', () => {
            expect(formatDaysDisplay(0, false)).toBe('Today');
            expect(formatDaysDisplay(0, true)).toBe('Today');
        });

        it('returns numeric string for future dates', () => {
            expect(formatDaysDisplay(1, false)).toBe('1');
            expect(formatDaysDisplay(5, false)).toBe('5');
            expect(formatDaysDisplay(30, false)).toBe('30');
        });

        it('returns "+N" format for overdue dates', () => {
            expect(formatDaysDisplay(-1, true)).toBe('+1');
            expect(formatDaysDisplay(-5, true)).toBe('+5');
            expect(formatDaysDisplay(-30, true)).toBe('+30');
        });
    });

    describe('generateAccessibleLabel', () => {
        it('returns "Due today" for 0 days', () => {
            expect(generateAccessibleLabel(0, false)).toBe('Due today');
        });

        it('returns "Due in 1 day" for tomorrow', () => {
            expect(generateAccessibleLabel(1, false)).toBe('Due in 1 day');
        });

        it('returns "Due in X days" for future dates', () => {
            expect(generateAccessibleLabel(5, false)).toBe('Due in 5 days');
            expect(generateAccessibleLabel(30, false)).toBe('Due in 30 days');
        });

        it('returns "1 day overdue" for 1 day overdue', () => {
            expect(generateAccessibleLabel(-1, true)).toBe('1 day overdue');
        });

        it('returns "X days overdue" for multiple days overdue', () => {
            expect(generateAccessibleLabel(-5, true)).toBe('5 days overdue');
            expect(generateAccessibleLabel(-30, true)).toBe('30 days overdue');
        });
    });

    describe('getDaysUntilDue', () => {
        it('calculates complete result for future date', () => {
            const dueDate = createLocalDate(2026, 2, 7); // 3 days from reference
            const result = getDaysUntilDue(dueDate, referenceDate);

            expect(result.days).toBe(3);
            expect(result.absoluteDays).toBe(3);
            expect(result.isOverdue).toBe(false);
            expect(result.isDueToday).toBe(false);
            expect(result.displayValue).toBe('3');
            expect(result.urgency).toBe('urgent');
            expect(result.accessibleLabel).toBe('Due in 3 days');
        });

        it('calculates complete result for overdue date', () => {
            const dueDate = createLocalDate(2026, 2, 1); // 3 days overdue
            const result = getDaysUntilDue(dueDate, referenceDate);

            expect(result.days).toBe(-3);
            expect(result.absoluteDays).toBe(3);
            expect(result.isOverdue).toBe(true);
            expect(result.isDueToday).toBe(false);
            expect(result.displayValue).toBe('+3');
            expect(result.urgency).toBe('overdue');
            expect(result.accessibleLabel).toBe('3 days overdue');
        });

        it('calculates complete result for today', () => {
            const dueDate = createLocalDate(2026, 2, 4); // Same day
            const result = getDaysUntilDue(dueDate, referenceDate);

            expect(result.days).toBe(0);
            expect(result.absoluteDays).toBe(0);
            expect(result.isOverdue).toBe(false);
            expect(result.isDueToday).toBe(true);
            expect(result.displayValue).toBe('Today');
            expect(result.urgency).toBe('critical');
            expect(result.accessibleLabel).toBe('Due today');
        });

        it('calculates result for various urgency levels', () => {
            // Tomorrow - critical
            const tomorrow = createLocalDate(2026, 2, 5);
            expect(getDaysUntilDue(tomorrow, referenceDate).urgency).toBe('critical');

            // 3 days - urgent
            const threeDays = createLocalDate(2026, 2, 7);
            expect(getDaysUntilDue(threeDays, referenceDate).urgency).toBe('urgent');

            // 5 days - warning
            const fiveDays = createLocalDate(2026, 2, 9);
            expect(getDaysUntilDue(fiveDays, referenceDate).urgency).toBe('warning');

            // 10 days - normal
            const tenDays = createLocalDate(2026, 2, 14);
            expect(getDaysUntilDue(tenDays, referenceDate).urgency).toBe('normal');
        });
    });

    describe('getUrgencyColors', () => {
        it('returns color config for urgency level string', () => {
            const urgencyLevels: UrgencyLevel[] = ['overdue', 'critical', 'urgent', 'warning', 'normal'];

            urgencyLevels.forEach(level => {
                const colors = getUrgencyColors(level);
                expect(colors).toHaveProperty('background');
                expect(colors).toHaveProperty('foreground');
                expect(colors).toBe(urgencyColorConfigs[level]);
            });
        });

        it('returns color config for days count', () => {
            // Overdue
            const overdueColors = getUrgencyColors(-5);
            expect(overdueColors).toBe(urgencyColorConfigs.overdue);

            // Critical
            const criticalColors = getUrgencyColors(0);
            expect(criticalColors).toBe(urgencyColorConfigs.critical);

            // Urgent
            const urgentColors = getUrgencyColors(3);
            expect(urgentColors).toBe(urgencyColorConfigs.urgent);

            // Warning
            const warningColors = getUrgencyColors(5);
            expect(warningColors).toBe(urgencyColorConfigs.warning);

            // Normal
            const normalColors = getUrgencyColors(10);
            expect(normalColors).toBe(urgencyColorConfigs.normal);
        });
    });

    describe('urgencyColorConfigs', () => {
        it('has all required urgency levels', () => {
            expect(urgencyColorConfigs).toHaveProperty('overdue');
            expect(urgencyColorConfigs).toHaveProperty('critical');
            expect(urgencyColorConfigs).toHaveProperty('urgent');
            expect(urgencyColorConfigs).toHaveProperty('warning');
            expect(urgencyColorConfigs).toHaveProperty('normal');
        });

        it('each config has background and foreground tokens', () => {
            Object.values(urgencyColorConfigs).forEach(config => {
                expect(config.background).toBeDefined();
                expect(config.foreground).toBeDefined();
                expect(typeof config.background).toBe('string');
                expect(typeof config.foreground).toBe('string');
            });
        });
    });

    describe('URGENCY_THRESHOLDS', () => {
        it('defines correct threshold values', () => {
            expect(URGENCY_THRESHOLDS.CRITICAL).toBe(1);
            expect(URGENCY_THRESHOLDS.URGENT).toBe(3);
            expect(URGENCY_THRESHOLDS.WARNING).toBe(7);
        });
    });
});
