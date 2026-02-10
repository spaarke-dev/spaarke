/**
 * Tests for useUpcomingEvents Hook Helper Functions
 *
 * Tests the pure functions exported from the hook:
 * - getUrgencyLevel
 * - formatDaysUntilDue
 *
 * Note: Testing the hook itself requires proper React 16 test setup
 * which is complex with async data fetching. The hook implementation
 * is tested indirectly through the component tests.
 */

import {
    getUrgencyLevel,
    formatDaysUntilDue
} from '../../hooks/useUpcomingEvents';

describe('useUpcomingEvents helper functions', () => {
    describe('getUrgencyLevel', () => {
        it('returns "overdue" for negative days and isOverdue true', () => {
            expect(getUrgencyLevel(-5, true)).toBe('overdue');
            expect(getUrgencyLevel(-1, true)).toBe('overdue');
            expect(getUrgencyLevel(-100, true)).toBe('overdue');
        });

        it('returns "urgent" for today (0 days)', () => {
            expect(getUrgencyLevel(0, false)).toBe('urgent');
        });

        it('returns "soon" for 1-3 days', () => {
            expect(getUrgencyLevel(1, false)).toBe('soon');
            expect(getUrgencyLevel(2, false)).toBe('soon');
            expect(getUrgencyLevel(3, false)).toBe('soon');
        });

        it('returns "normal" for 4+ days', () => {
            expect(getUrgencyLevel(4, false)).toBe('normal');
            expect(getUrgencyLevel(7, false)).toBe('normal');
            expect(getUrgencyLevel(100, false)).toBe('normal');
        });

        it('handles edge case at boundary', () => {
            // Day 3 is still "soon"
            expect(getUrgencyLevel(3, false)).toBe('soon');
            // Day 4 becomes "normal"
            expect(getUrgencyLevel(4, false)).toBe('normal');
        });
    });

    describe('formatDaysUntilDue', () => {
        describe('overdue formatting', () => {
            it('formats 1 day overdue (singular)', () => {
                expect(formatDaysUntilDue(-1, true)).toBe('1 day overdue');
            });

            it('formats multiple days overdue (plural)', () => {
                expect(formatDaysUntilDue(-2, true)).toBe('2 days overdue');
                expect(formatDaysUntilDue(-5, true)).toBe('5 days overdue');
                expect(formatDaysUntilDue(-30, true)).toBe('30 days overdue');
            });

            it('handles large overdue values', () => {
                expect(formatDaysUntilDue(-365, true)).toBe('365 days overdue');
            });
        });

        describe('today formatting', () => {
            it('formats today as "Due today"', () => {
                expect(formatDaysUntilDue(0, false)).toBe('Due today');
            });
        });

        describe('tomorrow formatting', () => {
            it('formats tomorrow as "Due tomorrow"', () => {
                expect(formatDaysUntilDue(1, false)).toBe('Due tomorrow');
            });
        });

        describe('future date formatting', () => {
            it('formats 2 days as "Due in 2 days"', () => {
                expect(formatDaysUntilDue(2, false)).toBe('Due in 2 days');
            });

            it('formats various future values', () => {
                expect(formatDaysUntilDue(5, false)).toBe('Due in 5 days');
                expect(formatDaysUntilDue(7, false)).toBe('Due in 7 days');
                expect(formatDaysUntilDue(30, false)).toBe('Due in 30 days');
            });

            it('handles large future values', () => {
                expect(formatDaysUntilDue(365, false)).toBe('Due in 365 days');
            });
        });

        describe('edge cases', () => {
            it('correctly pluralizes days', () => {
                // 1 day is singular
                expect(formatDaysUntilDue(-1, true)).toContain('1 day');
                expect(formatDaysUntilDue(-1, true)).not.toContain('days');

                // 2+ days is plural
                expect(formatDaysUntilDue(-2, true)).toContain('2 days');
            });

            it('handles the overdue flag correctly', () => {
                // With isOverdue true, negative days show overdue message
                expect(formatDaysUntilDue(-3, true)).toBe('3 days overdue');

                // Note: The function uses Math.abs, so the sign doesn't matter
                // when isOverdue is true
            });
        });
    });

    describe('getUrgencyLevel and formatDaysUntilDue consistency', () => {
        it('urgent level corresponds to "Due today"', () => {
            expect(getUrgencyLevel(0, false)).toBe('urgent');
            expect(formatDaysUntilDue(0, false)).toBe('Due today');
        });

        it('soon level corresponds to tomorrow/days', () => {
            expect(getUrgencyLevel(1, false)).toBe('soon');
            expect(formatDaysUntilDue(1, false)).toBe('Due tomorrow');

            expect(getUrgencyLevel(3, false)).toBe('soon');
            expect(formatDaysUntilDue(3, false)).toBe('Due in 3 days');
        });

        it('overdue level corresponds to overdue messages', () => {
            expect(getUrgencyLevel(-5, true)).toBe('overdue');
            expect(formatDaysUntilDue(-5, true)).toContain('overdue');
        });
    });
});
