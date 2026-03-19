/**
 * Mock adapter for {@link IDataService}.
 *
 * Provides a factory that returns a fully-mocked IDataService with jest.fn()
 * stubs and sensible default resolved values for use in unit tests.
 *
 * @example
 * ```typescript
 * const dataService = createMockDataService();
 * dataService.retrieveRecord.mockResolvedValue({ sprk_name: "Custom" });
 * ```
 */
import type { IDataService } from "../types/serviceInterfaces";

/**
 * Creates a mock IDataService with jest.fn() stubs and sensible defaults.
 *
 * Default return values:
 * - `createRecord` → `"00000000-0000-0000-0000-000000000001"`
 * - `retrieveRecord` → `{}`
 * - `retrieveMultipleRecords` → `{ entities: [] }`
 * - `updateRecord` → `undefined`
 * - `deleteRecord` → `undefined`
 *
 * @returns A fully-mocked IDataService instance
 */
export function createMockDataService(): jest.Mocked<IDataService> {
  return {
    createRecord: jest
      .fn()
      .mockResolvedValue("00000000-0000-0000-0000-000000000001"),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}
