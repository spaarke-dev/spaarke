/**
 * Jest Test Setup for LegalWorkspace PCF Control
 *
 * Configures:
 *   - @testing-library/jest-dom matchers (toBeInTheDocument, etc.)
 *   - Mock for ComponentFramework.WebApi
 *   - Mock for Xrm global
 *   - Test data factories for IEvent, IMatter
 */

import '@testing-library/jest-dom';
import { IEvent } from '../types/entities';

// ---------------------------------------------------------------------------
// ComponentFramework.WebApi mock factory
// ---------------------------------------------------------------------------

export interface IMockWebApiOptions {
  entities?: unknown[];
  updateResult?: unknown;
  error?: Error;
}

export function createMockWebApi(
  options: IMockWebApiOptions = {}
): ComponentFramework.WebApi {
  const { entities = [], error } = options;

  return {
    retrieveMultipleRecords: jest.fn().mockImplementation(() => {
      if (error) {
        return Promise.reject(error);
      }
      return Promise.resolve({ entities });
    }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    createRecord: jest.fn().mockResolvedValue({ id: 'new-record-id' }),
    updateRecord: jest.fn().mockResolvedValue({ id: 'updated-record-id' }),
    deleteRecord: jest.fn().mockResolvedValue({ id: 'deleted-record-id' }),
  } as unknown as ComponentFramework.WebApi;
}

// ---------------------------------------------------------------------------
// IEvent test data factory
// ---------------------------------------------------------------------------

let _eventIdCounter = 1;

export function createMockEvent(overrides: Partial<IEvent> = {}): IEvent {
  const id = overrides.sprk_eventid ?? `event-id-${_eventIdCounter++}`;
  return {
    sprk_eventid: id,
    sprk_subject: `Mock Event ${id}`,
    sprk_type: 'task',
    sprk_priority: 2,         // High
    sprk_priorityscore: 75,
    sprk_effortscore: 40,
    sprk_todoflag: false,
    sprk_todostatus: 0,       // Open
    sprk_todosource: 'System',
    sprk_duedate: undefined,
    sprk_priorityreason: 'High-priority mock event',
    sprk_effortreason: 'Medium effort required',
    createdon: '2026-01-15T10:00:00Z',
    modifiedon: '2026-02-01T14:00:00Z',
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Global Xrm mock (minimal — only what LegalWorkspace uses)
// ---------------------------------------------------------------------------

(global as unknown as Record<string, unknown>).Xrm = {};

// ---------------------------------------------------------------------------
// Auto-reset mocks between tests
// ---------------------------------------------------------------------------

beforeEach(() => {
  jest.clearAllMocks();
  _eventIdCounter = 1;
});
