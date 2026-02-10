/**
 * dateFilter Utility Tests
 * Task 018: Add Unit Tests for Grid Enhancements
 *
 * Tests cover:
 * - buildDateFilter: Calendar filter to PCF FilterExpression conversion
 * - applyDateFilter: Apply filter to PCF dataset
 * - Single date filtering
 * - Range date filtering
 * - Clear filter handling
 *
 * @see dateFilter.ts
 */

import {
    buildDateFilter,
    applyDateFilter,
    DATE_FILTER_FIELD,
    DateFilterResult
} from '../dateFilter';
import {
    parseCalendarFilter,
    CalendarFilter,
    ICalendarFilterSingle,
    ICalendarFilterRange,
    ICalendarFilterClear
} from '../../types';

// PCF ConditionOperator values for verification
const ConditionOperator = {
    On: 27,
    OnOrAfter: 25,
    OnOrBefore: 26
};

// PCF FilterOperator values
const FilterOperator = {
    And: 0
};

describe('dateFilter utilities', () => {
    describe('buildDateFilter', () => {
        describe('null/undefined handling', () => {
            it('returns shouldFilter=false for null filter', () => {
                const result = buildDateFilter(null);

                expect(result.shouldFilter).toBe(false);
                expect(result.filterExpression).toBeNull();
            });

            it('returns shouldFilter=false for undefined filter', () => {
                const result = buildDateFilter(undefined);

                expect(result.shouldFilter).toBe(false);
                expect(result.filterExpression).toBeNull();
            });
        });

        describe('clear filter handling', () => {
            it('returns shouldFilter=false for clear filter type', () => {
                const clearFilter: ICalendarFilterClear = { type: 'clear' };
                const result = buildDateFilter(clearFilter);

                expect(result.shouldFilter).toBe(false);
                expect(result.filterExpression).toBeNull();
            });

            it('handles clear filter from parseCalendarFilter', () => {
                const filter = parseCalendarFilter('{"type":"clear"}');
                const result = buildDateFilter(filter);

                expect(result.shouldFilter).toBe(false);
            });
        });

        describe('single date filter', () => {
            it('creates single date filter expression', () => {
                const singleFilter: ICalendarFilterSingle = {
                    type: 'single',
                    date: '2026-02-10'
                };
                const result = buildDateFilter(singleFilter);

                expect(result.shouldFilter).toBe(true);
                expect(result.filterExpression).not.toBeNull();
                expect(result.filterExpression!.filterOperator).toBe(FilterOperator.And);
                expect(result.filterExpression!.conditions).toHaveLength(1);
                expect(result.filterExpression!.conditions[0]).toEqual({
                    attributeName: DATE_FILTER_FIELD,
                    conditionOperator: ConditionOperator.On,
                    value: '2026-02-10'
                });
            });

            it('handles single date from parseCalendarFilter', () => {
                const filter = parseCalendarFilter('{"type":"single","date":"2026-02-15"}');
                const result = buildDateFilter(filter);

                expect(result.shouldFilter).toBe(true);
                expect(result.filterExpression!.conditions[0].value).toBe('2026-02-15');
            });

            it('uses sprk_duedate as filter field', () => {
                const singleFilter: ICalendarFilterSingle = {
                    type: 'single',
                    date: '2026-03-01'
                };
                const result = buildDateFilter(singleFilter);

                expect(result.filterExpression!.conditions[0].attributeName).toBe('sprk_duedate');
            });
        });

        describe('range date filter', () => {
            it('creates range filter with two conditions', () => {
                const rangeFilter: ICalendarFilterRange = {
                    type: 'range',
                    start: '2026-02-01',
                    end: '2026-02-07'
                };
                const result = buildDateFilter(rangeFilter);

                expect(result.shouldFilter).toBe(true);
                expect(result.filterExpression).not.toBeNull();
                expect(result.filterExpression!.filterOperator).toBe(FilterOperator.And);
                expect(result.filterExpression!.conditions).toHaveLength(2);
            });

            it('uses OnOrAfter for start date condition', () => {
                const rangeFilter: ICalendarFilterRange = {
                    type: 'range',
                    start: '2026-02-01',
                    end: '2026-02-07'
                };
                const result = buildDateFilter(rangeFilter);

                expect(result.filterExpression!.conditions[0]).toEqual({
                    attributeName: DATE_FILTER_FIELD,
                    conditionOperator: ConditionOperator.OnOrAfter,
                    value: '2026-02-01'
                });
            });

            it('uses OnOrBefore for end date condition', () => {
                const rangeFilter: ICalendarFilterRange = {
                    type: 'range',
                    start: '2026-02-01',
                    end: '2026-02-07'
                };
                const result = buildDateFilter(rangeFilter);

                expect(result.filterExpression!.conditions[1]).toEqual({
                    attributeName: DATE_FILTER_FIELD,
                    conditionOperator: ConditionOperator.OnOrBefore,
                    value: '2026-02-07'
                });
            });

            it('handles range filter from parseCalendarFilter', () => {
                const filter = parseCalendarFilter('{"type":"range","start":"2026-02-01","end":"2026-02-28"}');
                const result = buildDateFilter(filter);

                expect(result.shouldFilter).toBe(true);
                expect(result.filterExpression!.conditions).toHaveLength(2);
                expect(result.filterExpression!.conditions[0].value).toBe('2026-02-01');
                expect(result.filterExpression!.conditions[1].value).toBe('2026-02-28');
            });

            it('handles week range (7 days)', () => {
                const filter = parseCalendarFilter('{"type":"range","start":"2026-02-01","end":"2026-02-07"}');
                const result = buildDateFilter(filter);

                expect(result.shouldFilter).toBe(true);
                expect(result.filterExpression!.conditions[0].value).toBe('2026-02-01');
                expect(result.filterExpression!.conditions[1].value).toBe('2026-02-07');
            });

            it('handles same-day range', () => {
                const rangeFilter: ICalendarFilterRange = {
                    type: 'range',
                    start: '2026-02-10',
                    end: '2026-02-10'
                };
                const result = buildDateFilter(rangeFilter);

                expect(result.shouldFilter).toBe(true);
                expect(result.filterExpression!.conditions[0].value).toBe('2026-02-10');
                expect(result.filterExpression!.conditions[1].value).toBe('2026-02-10');
            });
        });

        describe('unknown filter types', () => {
            it('returns shouldFilter=false for unknown filter type', () => {
                // Cast to CalendarFilter to simulate unknown type
                const unknownFilter = { type: 'unknown' } as CalendarFilter;
                const result = buildDateFilter(unknownFilter);

                expect(result.shouldFilter).toBe(false);
                expect(result.filterExpression).toBeNull();
            });

            it('returns shouldFilter=false for malformed filter', () => {
                // Malformed filter missing required fields
                const malformedFilter = { type: 'single' } as CalendarFilter;
                const result = buildDateFilter(malformedFilter);

                // Type guard will fail, so it falls through to unknown
                expect(result.shouldFilter).toBe(false);
            });
        });
    });

    describe('applyDateFilter', () => {
        // Mock dataset
        const createMockDataset = () => ({
            filtering: {
                setFilter: jest.fn(),
                clearFilter: jest.fn()
            },
            refresh: jest.fn(),
            sortedRecordIds: [],
            records: {}
        } as unknown as ComponentFramework.PropertyTypes.DataSet);

        beforeEach(() => {
            jest.clearAllMocks();
        });

        it('applies filter and refreshes dataset for single date', () => {
            const mockDataset = createMockDataset();
            const singleFilter: ICalendarFilterSingle = {
                type: 'single',
                date: '2026-02-10'
            };

            const result = applyDateFilter(mockDataset, singleFilter);

            expect(result).toBe(true);
            expect(mockDataset.filtering.setFilter).toHaveBeenCalledTimes(1);
            expect(mockDataset.refresh).toHaveBeenCalledTimes(1);
        });

        it('applies filter for range', () => {
            const mockDataset = createMockDataset();
            const rangeFilter: ICalendarFilterRange = {
                type: 'range',
                start: '2026-02-01',
                end: '2026-02-07'
            };

            const result = applyDateFilter(mockDataset, rangeFilter);

            expect(result).toBe(true);
            expect(mockDataset.filtering.setFilter).toHaveBeenCalledTimes(1);
            expect(mockDataset.refresh).toHaveBeenCalledTimes(1);
        });

        it('clears filter and refreshes for null', () => {
            const mockDataset = createMockDataset();

            const result = applyDateFilter(mockDataset, null);

            expect(result).toBe(false);
            expect(mockDataset.filtering.clearFilter).toHaveBeenCalledTimes(1);
            expect(mockDataset.filtering.setFilter).not.toHaveBeenCalled();
            expect(mockDataset.refresh).toHaveBeenCalledTimes(1);
        });

        it('clears filter for clear type', () => {
            const mockDataset = createMockDataset();
            const clearFilter: ICalendarFilterClear = { type: 'clear' };

            const result = applyDateFilter(mockDataset, clearFilter);

            expect(result).toBe(false);
            expect(mockDataset.filtering.clearFilter).toHaveBeenCalledTimes(1);
            expect(mockDataset.filtering.setFilter).not.toHaveBeenCalled();
        });

        it('returns false if dataset filtering API not available', () => {
            const mockDataset = {
                refresh: jest.fn()
            } as unknown as ComponentFramework.PropertyTypes.DataSet;

            const singleFilter: ICalendarFilterSingle = {
                type: 'single',
                date: '2026-02-10'
            };

            const result = applyDateFilter(mockDataset, singleFilter);

            expect(result).toBe(false);
        });

        it('returns false if dataset is undefined', () => {
            // @ts-expect-error Testing undefined case
            const result = applyDateFilter(undefined, null);

            expect(result).toBe(false);
        });
    });

    describe('parseCalendarFilter integration', () => {
        it('parses and builds single date filter end-to-end', () => {
            const json = '{"type":"single","date":"2026-02-15"}';
            const filter = parseCalendarFilter(json);
            const result = buildDateFilter(filter);

            expect(result.shouldFilter).toBe(true);
            expect(result.filterExpression!.conditions).toHaveLength(1);
            expect(result.filterExpression!.conditions[0].value).toBe('2026-02-15');
        });

        it('parses and builds range filter end-to-end', () => {
            const json = '{"type":"range","start":"2026-02-01","end":"2026-02-28"}';
            const filter = parseCalendarFilter(json);
            const result = buildDateFilter(filter);

            expect(result.shouldFilter).toBe(true);
            expect(result.filterExpression!.conditions).toHaveLength(2);
        });

        it('handles invalid JSON gracefully', () => {
            const filter = parseCalendarFilter('not json');
            const result = buildDateFilter(filter);

            expect(result.shouldFilter).toBe(false);
        });

        it('handles empty string gracefully', () => {
            const filter = parseCalendarFilter('');
            const result = buildDateFilter(filter);

            expect(result.shouldFilter).toBe(false);
        });

        it('handles whitespace string gracefully', () => {
            const filter = parseCalendarFilter('   ');
            const result = buildDateFilter(filter);

            expect(result.shouldFilter).toBe(false);
        });
    });
});
