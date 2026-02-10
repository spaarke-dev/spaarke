/**
 * Types Module Tests
 * Task 018: Add Unit Tests for Grid Enhancements
 *
 * Tests cover:
 * - parseCalendarFilter: JSON parsing for calendar filter input
 * - isSingleDateFilter: Type guard for single date filter
 * - isRangeFilter: Type guard for range filter
 * - isClearFilter: Type guard for clear filter
 * - CalendarFilter types
 *
 * @see types/index.ts
 */

import {
    parseCalendarFilter,
    isSingleDateFilter,
    isRangeFilter,
    isClearFilter,
    CalendarFilter,
    ICalendarFilterSingle,
    ICalendarFilterRange,
    ICalendarFilterClear,
    DEFAULT_GRID_CONFIG,
    GridConfiguration
} from '../index';

describe('parseCalendarFilter', () => {
    describe('valid inputs', () => {
        it('parses single date filter', () => {
            const json = '{"type":"single","date":"2026-02-10"}';
            const result = parseCalendarFilter(json);

            expect(result).not.toBeNull();
            expect(result!.type).toBe('single');
            expect((result as ICalendarFilterSingle).date).toBe('2026-02-10');
        });

        it('parses range filter', () => {
            const json = '{"type":"range","start":"2026-02-01","end":"2026-02-07"}';
            const result = parseCalendarFilter(json);

            expect(result).not.toBeNull();
            expect(result!.type).toBe('range');
            expect((result as ICalendarFilterRange).start).toBe('2026-02-01');
            expect((result as ICalendarFilterRange).end).toBe('2026-02-07');
        });

        it('parses clear filter', () => {
            const json = '{"type":"clear"}';
            const result = parseCalendarFilter(json);

            expect(result).not.toBeNull();
            expect(result!.type).toBe('clear');
        });
    });

    describe('null/empty inputs', () => {
        it('returns null for null input', () => {
            const result = parseCalendarFilter(null);
            expect(result).toBeNull();
        });

        it('returns null for undefined input', () => {
            const result = parseCalendarFilter(undefined);
            expect(result).toBeNull();
        });

        it('returns null for empty string', () => {
            const result = parseCalendarFilter('');
            expect(result).toBeNull();
        });

        it('returns null for whitespace-only string', () => {
            const result = parseCalendarFilter('   ');
            expect(result).toBeNull();
        });

        it('returns null for string with only tabs and newlines', () => {
            const result = parseCalendarFilter('\t\n');
            expect(result).toBeNull();
        });
    });

    describe('invalid JSON', () => {
        it('returns null for invalid JSON syntax', () => {
            const result = parseCalendarFilter('not json');
            expect(result).toBeNull();
        });

        it('returns null for incomplete JSON', () => {
            const result = parseCalendarFilter('{"type":');
            expect(result).toBeNull();
        });

        it('returns null for JSON array', () => {
            const result = parseCalendarFilter('[1, 2, 3]');
            expect(result).toBeNull();
        });

        it('returns null for JSON with only number', () => {
            const result = parseCalendarFilter('123');
            expect(result).toBeNull();
        });

        it('returns null for JSON with only string', () => {
            const result = parseCalendarFilter('"just a string"');
            expect(result).toBeNull();
        });
    });

    describe('missing required fields', () => {
        it('returns null for object without type', () => {
            const result = parseCalendarFilter('{"date":"2026-02-10"}');
            expect(result).toBeNull();
        });

        it('returns null for single filter without date', () => {
            const result = parseCalendarFilter('{"type":"single"}');
            expect(result).toBeNull();
        });

        it('returns null for range filter without start', () => {
            const result = parseCalendarFilter('{"type":"range","end":"2026-02-07"}');
            expect(result).toBeNull();
        });

        it('returns null for range filter without end', () => {
            const result = parseCalendarFilter('{"type":"range","start":"2026-02-01"}');
            expect(result).toBeNull();
        });
    });

    describe('wrong field types', () => {
        it('returns null for single filter with numeric date', () => {
            const result = parseCalendarFilter('{"type":"single","date":12345}');
            expect(result).toBeNull();
        });

        it('returns null for range filter with numeric start', () => {
            const result = parseCalendarFilter('{"type":"range","start":12345,"end":"2026-02-07"}');
            expect(result).toBeNull();
        });

        it('returns null for range filter with numeric end', () => {
            const result = parseCalendarFilter('{"type":"range","start":"2026-02-01","end":12345}');
            expect(result).toBeNull();
        });
    });

    describe('unknown filter types', () => {
        it('returns null for unknown type', () => {
            const result = parseCalendarFilter('{"type":"unknown","date":"2026-02-10"}');
            expect(result).toBeNull();
        });

        it('returns null for misspelled type', () => {
            const result = parseCalendarFilter('{"type":"singl","date":"2026-02-10"}');
            expect(result).toBeNull();
        });
    });

    describe('extra fields (allowed)', () => {
        it('accepts single filter with extra fields', () => {
            const json = '{"type":"single","date":"2026-02-10","extra":"field"}';
            const result = parseCalendarFilter(json);

            expect(result).not.toBeNull();
            expect(result!.type).toBe('single');
        });

        it('accepts range filter with extra fields', () => {
            const json = '{"type":"range","start":"2026-02-01","end":"2026-02-07","extra":123}';
            const result = parseCalendarFilter(json);

            expect(result).not.toBeNull();
            expect(result!.type).toBe('range');
        });

        it('accepts clear filter with extra fields', () => {
            const json = '{"type":"clear","ignored":"value"}';
            const result = parseCalendarFilter(json);

            expect(result).not.toBeNull();
            expect(result!.type).toBe('clear');
        });
    });
});

describe('Type Guards', () => {
    describe('isSingleDateFilter', () => {
        it('returns true for single date filter', () => {
            const filter: ICalendarFilterSingle = { type: 'single', date: '2026-02-10' };
            expect(isSingleDateFilter(filter)).toBe(true);
        });

        it('returns false for range filter', () => {
            const filter: ICalendarFilterRange = { type: 'range', start: '2026-02-01', end: '2026-02-07' };
            expect(isSingleDateFilter(filter)).toBe(false);
        });

        it('returns false for clear filter', () => {
            const filter: ICalendarFilterClear = { type: 'clear' };
            expect(isSingleDateFilter(filter)).toBe(false);
        });
    });

    describe('isRangeFilter', () => {
        it('returns true for range filter', () => {
            const filter: ICalendarFilterRange = { type: 'range', start: '2026-02-01', end: '2026-02-07' };
            expect(isRangeFilter(filter)).toBe(true);
        });

        it('returns false for single date filter', () => {
            const filter: ICalendarFilterSingle = { type: 'single', date: '2026-02-10' };
            expect(isRangeFilter(filter)).toBe(false);
        });

        it('returns false for clear filter', () => {
            const filter: ICalendarFilterClear = { type: 'clear' };
            expect(isRangeFilter(filter)).toBe(false);
        });
    });

    describe('isClearFilter', () => {
        it('returns true for clear filter', () => {
            const filter: ICalendarFilterClear = { type: 'clear' };
            expect(isClearFilter(filter)).toBe(true);
        });

        it('returns false for single date filter', () => {
            const filter: ICalendarFilterSingle = { type: 'single', date: '2026-02-10' };
            expect(isClearFilter(filter)).toBe(false);
        });

        it('returns false for range filter', () => {
            const filter: ICalendarFilterRange = { type: 'range', start: '2026-02-01', end: '2026-02-07' };
            expect(isClearFilter(filter)).toBe(false);
        });
    });

    describe('Type narrowing', () => {
        it('narrows CalendarFilter to ICalendarFilterSingle', () => {
            const filter = parseCalendarFilter('{"type":"single","date":"2026-02-10"}');

            if (filter && isSingleDateFilter(filter)) {
                // TypeScript should know filter.date exists here
                expect(filter.date).toBe('2026-02-10');
            } else {
                fail('Expected single date filter');
            }
        });

        it('narrows CalendarFilter to ICalendarFilterRange', () => {
            const filter = parseCalendarFilter('{"type":"range","start":"2026-02-01","end":"2026-02-07"}');

            if (filter && isRangeFilter(filter)) {
                // TypeScript should know filter.start and filter.end exist here
                expect(filter.start).toBe('2026-02-01');
                expect(filter.end).toBe('2026-02-07');
            } else {
                fail('Expected range filter');
            }
        });

        it('narrows CalendarFilter to ICalendarFilterClear', () => {
            const filter = parseCalendarFilter('{"type":"clear"}');

            if (filter && isClearFilter(filter)) {
                // TypeScript should know this is a clear filter
                expect(filter.type).toBe('clear');
            } else {
                fail('Expected clear filter');
            }
        });
    });
});

describe('DEFAULT_GRID_CONFIG', () => {
    it('has expected field mappings', () => {
        expect(DEFAULT_GRID_CONFIG.fieldMappings).toEqual({
            hasFile: 'sprk_hasfile',
            fileName: 'sprk_filename',
            fileSize: 'sprk_filesize',
            mimeType: 'sprk_mimetype',
            graphItemId: 'sprk_graphitemid',
            graphDriveId: 'sprk_graphdriveid'
        });
    });

    it('enables checkbox selection by default', () => {
        expect(DEFAULT_GRID_CONFIG.enableCheckboxSelection).toBe(true);
    });

    it('has SDAP config with base URL and timeout', () => {
        expect(DEFAULT_GRID_CONFIG.sdapConfig.baseUrl).toBe('https://spe-api-dev-67e2xz.azurewebsites.net');
        expect(DEFAULT_GRID_CONFIG.sdapConfig.timeout).toBe(300000);
    });

    it('has custom commands array', () => {
        expect(Array.isArray(DEFAULT_GRID_CONFIG.customCommands)).toBe(true);
        expect(DEFAULT_GRID_CONFIG.customCommands.length).toBeGreaterThan(0);
    });

    it('has addFile command', () => {
        const addFileCmd = DEFAULT_GRID_CONFIG.customCommands.find(c => c.id === 'addFile');
        expect(addFileCmd).toBeDefined();
        expect(addFileCmd!.label).toBe('Add File');
    });

    it('has downloadFile command', () => {
        const downloadCmd = DEFAULT_GRID_CONFIG.customCommands.find(c => c.id === 'downloadFile');
        expect(downloadCmd).toBeDefined();
        expect(downloadCmd!.label).toBe('Download');
    });
});

describe('GridConfiguration type', () => {
    it('accepts valid configuration', () => {
        const config: GridConfiguration = {
            fieldMappings: {
                hasFile: 'custom_hasfile',
                fileName: 'custom_filename',
                fileSize: 'custom_filesize',
                mimeType: 'custom_mimetype',
                graphItemId: 'custom_graphitemid',
                graphDriveId: 'custom_graphdriveid'
            },
            customCommands: [],
            sdapConfig: {
                baseUrl: 'https://example.com',
                timeout: 60000
            },
            enableCheckboxSelection: false
        };

        expect(config.fieldMappings.hasFile).toBe('custom_hasfile');
        expect(config.enableCheckboxSelection).toBe(false);
    });
});
