/**
 * rowUpdateHandler Utility Tests
 * Task 018: Add Unit Tests for Grid Enhancements
 *
 * Tests cover:
 * - validateUpdateRequest: Validation of optimistic update requests
 * - createRowSnapshot/getRowSnapshot/clearRowSnapshot: Snapshot management
 * - applyFieldUpdates: Field update application
 * - createErrorResult/createSuccessResult: Result creation helpers
 * - createNoOpRollback: No-op rollback function
 *
 * @see rowUpdateHandler.ts
 */

import {
    validateUpdateRequest,
    createRowSnapshot,
    getRowSnapshot,
    clearRowSnapshot,
    applyFieldUpdates,
    createErrorResult,
    createSuccessResult,
    createNoOpRollback,
    logOptimisticUpdate
} from '../rowUpdateHandler';
import { OptimisticRowUpdateRequest, RowFieldUpdate } from '../../types';

// Mock logger to prevent console output during tests
jest.mock('../logger', () => ({
    logger: {
        debug: jest.fn(),
        info: jest.fn(),
        warn: jest.fn(),
        error: jest.fn()
    }
}));

describe('rowUpdateHandler utilities', () => {
    beforeEach(() => {
        // Clear any existing snapshots between tests
        jest.clearAllMocks();
    });

    describe('validateUpdateRequest', () => {
        it('returns null for valid request', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { fieldName: 'sprk_eventname', formattedValue: 'New Event' }
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBeNull();
        });

        it('returns error for null request', () => {
            // @ts-expect-error Testing null case
            const error = validateUpdateRequest(null);

            expect(error).toBe('Update request is required');
        });

        it('returns error for undefined request', () => {
            // @ts-expect-error Testing undefined case
            const error = validateUpdateRequest(undefined);

            expect(error).toBe('Update request is required');
        });

        it('returns error for missing recordId', () => {
            const request = {
                updates: [{ fieldName: 'test', formattedValue: 'value' }]
            } as OptimisticRowUpdateRequest;

            const error = validateUpdateRequest(request);

            expect(error).toBe('recordId is required and must be a string');
        });

        it('returns error for non-string recordId', () => {
            const request = {
                recordId: 12345 as unknown as string,
                updates: [{ fieldName: 'test', formattedValue: 'value' }]
            } as OptimisticRowUpdateRequest;

            const error = validateUpdateRequest(request);

            expect(error).toBe('recordId is required and must be a string');
        });

        it('returns error for invalid GUID format', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: 'not-a-valid-guid',
                updates: [{ fieldName: 'test', formattedValue: 'value' }]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('Invalid recordId format: not-a-valid-guid');
        });

        it('accepts valid GUID formats', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
                updates: [{ fieldName: 'test', formattedValue: 'value' }]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBeNull();
        });

        it('accepts uppercase GUID', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: 'A1B2C3D4-E5F6-7890-ABCD-EF1234567890',
                updates: [{ fieldName: 'test', formattedValue: 'value' }]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBeNull();
        });

        it('returns error for missing updates array', () => {
            const request = {
                recordId: '12345678-1234-1234-1234-123456789012'
            } as OptimisticRowUpdateRequest;

            const error = validateUpdateRequest(request);

            expect(error).toBe('updates array is required');
        });

        it('returns error for non-array updates', () => {
            const request = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: 'not an array' as unknown as RowFieldUpdate[]
            } as OptimisticRowUpdateRequest;

            const error = validateUpdateRequest(request);

            expect(error).toBe('updates array is required');
        });

        it('returns error for empty updates array', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: []
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('updates array must not be empty');
        });

        it('returns error for update without fieldName', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { formattedValue: 'value' } as RowFieldUpdate
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('Each update must have a fieldName string');
        });

        it('returns error for update with non-string fieldName', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { fieldName: 123 as unknown as string, formattedValue: 'value' }
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('Each update must have a fieldName string');
        });

        it('returns error for update without formattedValue', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { fieldName: 'test' } as RowFieldUpdate
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('formattedValue for test must be a string');
        });

        it('returns error for update with non-string formattedValue', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { fieldName: 'test', formattedValue: 123 as unknown as string }
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('formattedValue for test must be a string');
        });

        it('validates multiple updates in array', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { fieldName: 'field1', formattedValue: 'value1' },
                    { fieldName: 'field2', formattedValue: 'value2' },
                    { fieldName: 'field3', formattedValue: 'value3' }
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBeNull();
        });

        it('returns error for second invalid update', () => {
            const request: OptimisticRowUpdateRequest = {
                recordId: '12345678-1234-1234-1234-123456789012',
                updates: [
                    { fieldName: 'field1', formattedValue: 'value1' },
                    { fieldName: '', formattedValue: 'value2' }
                ]
            };

            const error = validateUpdateRequest(request);

            expect(error).toBe('Each update must have a fieldName string');
        });
    });

    describe('snapshot management', () => {
        const testRecordId = '12345678-1234-1234-1234-123456789012';

        beforeEach(() => {
            // Clear any existing snapshot for the test record
            clearRowSnapshot(testRecordId);
        });

        describe('createRowSnapshot', () => {
            it('creates a snapshot with provided values', () => {
                const currentValues: RowFieldUpdate[] = [
                    { fieldName: 'sprk_eventname', formattedValue: 'Original Name' },
                    { fieldName: 'statuscode', formattedValue: 'Open' }
                ];

                createRowSnapshot(testRecordId, currentValues);

                const snapshot = getRowSnapshot(testRecordId);
                expect(snapshot).toBeDefined();
                expect(snapshot?.recordId).toBe(testRecordId);
                expect(snapshot?.previousValues).toEqual(currentValues);
            });

            it('updates timestamp when creating snapshot', () => {
                const before = Date.now();
                createRowSnapshot(testRecordId, []);
                const after = Date.now();

                const snapshot = getRowSnapshot(testRecordId);
                expect(snapshot?.timestamp).toBeGreaterThanOrEqual(before);
                expect(snapshot?.timestamp).toBeLessThanOrEqual(after);
            });
        });

        describe('getRowSnapshot', () => {
            it('returns undefined for non-existent snapshot', () => {
                const snapshot = getRowSnapshot('nonexistent-1234-1234-1234-123456789012');

                expect(snapshot).toBeUndefined();
            });

            it('returns snapshot if it exists', () => {
                createRowSnapshot(testRecordId, [
                    { fieldName: 'test', formattedValue: 'value' }
                ]);

                const snapshot = getRowSnapshot(testRecordId);

                expect(snapshot).toBeDefined();
                expect(snapshot?.recordId).toBe(testRecordId);
            });
        });

        describe('clearRowSnapshot', () => {
            it('removes snapshot for record', () => {
                createRowSnapshot(testRecordId, []);
                expect(getRowSnapshot(testRecordId)).toBeDefined();

                clearRowSnapshot(testRecordId);

                expect(getRowSnapshot(testRecordId)).toBeUndefined();
            });

            it('does not throw for non-existent snapshot', () => {
                expect(() => {
                    clearRowSnapshot('nonexistent-1234-1234-1234-123456789012');
                }).not.toThrow();
            });
        });
    });

    describe('applyFieldUpdates', () => {
        it('applies updates to row and returns previous values', () => {
            const row: Record<string, unknown> = {
                sprk_eventname: 'Original Name',
                statuscode: 'Draft'
            };
            const updates: RowFieldUpdate[] = [
                { fieldName: 'sprk_eventname', formattedValue: 'New Name' },
                { fieldName: 'statuscode', formattedValue: 'Open' }
            ];

            const previousValues = applyFieldUpdates(row, updates);

            // Check row is updated
            expect(row.sprk_eventname).toBe('New Name');
            expect(row.statuscode).toBe('Open');

            // Check previous values are captured
            expect(previousValues).toHaveLength(2);
            expect(previousValues[0]).toEqual({
                fieldName: 'sprk_eventname',
                formattedValue: 'Original Name',
                rawValue: 'Original Name'
            });
            expect(previousValues[1]).toEqual({
                fieldName: 'statuscode',
                formattedValue: 'Draft',
                rawValue: 'Draft'
            });
        });

        it('handles undefined previous values', () => {
            const row: Record<string, unknown> = {};
            const updates: RowFieldUpdate[] = [
                { fieldName: 'newField', formattedValue: 'value' }
            ];

            const previousValues = applyFieldUpdates(row, updates);

            expect(row.newField).toBe('value');
            expect(previousValues[0].formattedValue).toBe('');
        });

        it('handles null previous values', () => {
            const row: Record<string, unknown> = {
                nullField: null
            };
            const updates: RowFieldUpdate[] = [
                { fieldName: 'nullField', formattedValue: 'value' }
            ];

            const previousValues = applyFieldUpdates(row, updates);

            expect(row.nullField).toBe('value');
            expect(previousValues[0].formattedValue).toBe('');
        });

        it('handles numeric previous values', () => {
            const row: Record<string, unknown> = {
                numericField: 42
            };
            const updates: RowFieldUpdate[] = [
                { fieldName: 'numericField', formattedValue: '100' }
            ];

            const previousValues = applyFieldUpdates(row, updates);

            expect(previousValues[0].formattedValue).toBe('42');
            expect(previousValues[0].rawValue).toBe(42);
        });

        it('updates lookup IDs when rawValue is provided', () => {
            const row: Record<string, unknown> = {
                sprk_eventtype: 'Event Type A',
                _lookupIds: { sprk_eventtype: 'guid-old' }
            };
            const updates: RowFieldUpdate[] = [
                {
                    fieldName: 'sprk_eventtype',
                    formattedValue: 'Event Type B',
                    rawValue: 'guid-new'
                }
            ];

            applyFieldUpdates(row, updates);

            expect(row.sprk_eventtype).toBe('Event Type B');
            expect((row._lookupIds as Record<string, string>).sprk_eventtype).toBe('guid-new');
        });
    });

    describe('createErrorResult', () => {
        it('creates error result with message', () => {
            const result = createErrorResult('Something went wrong');

            expect(result.success).toBe(false);
            expect(result.error).toBe('Something went wrong');
            expect(typeof result.rollback).toBe('function');
        });

        it('rollback is no-op function', () => {
            const result = createErrorResult('Error');

            // Should not throw when called
            expect(() => result.rollback()).not.toThrow();
        });
    });

    describe('createSuccessResult', () => {
        it('creates success result with rollback function', () => {
            const rollbackFn = jest.fn();
            const result = createSuccessResult(rollbackFn);

            expect(result.success).toBe(true);
            expect(result.error).toBeUndefined();
            expect(result.rollback).toBe(rollbackFn);
        });

        it('rollback function is callable', () => {
            const rollbackFn = jest.fn();
            const result = createSuccessResult(rollbackFn);

            result.rollback();

            expect(rollbackFn).toHaveBeenCalledTimes(1);
        });
    });

    describe('createNoOpRollback', () => {
        it('returns a function', () => {
            const noOp = createNoOpRollback();

            expect(typeof noOp).toBe('function');
        });

        it('function does not throw when called', () => {
            const noOp = createNoOpRollback();

            expect(() => noOp()).not.toThrow();
        });
    });

    describe('logOptimisticUpdate', () => {
        it('logs update information', () => {
            const { logger } = require('../logger');

            const updates: RowFieldUpdate[] = [
                { fieldName: 'field1', formattedValue: 'value1' },
                { fieldName: 'field2', formattedValue: 'value2' }
            ];

            logOptimisticUpdate('test-record-id-1234-1234-123456789012', updates);

            expect(logger.info).toHaveBeenCalledWith(
                'RowUpdateHandler',
                expect.stringContaining('Optimistic update'),
                expect.objectContaining({
                    fieldCount: 2,
                    fields: ['field1', 'field2']
                })
            );
        });
    });
});
