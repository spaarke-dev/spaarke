/**
 * Mock adapter for {@link INavigationService}.
 *
 * Provides a factory that returns a fully-mocked INavigationService with jest.fn()
 * stubs and sensible default resolved values for use in unit tests.
 *
 * @example
 * ```typescript
 * const navService = createMockNavigationService();
 * navService.openDialog.mockResolvedValue({ confirmed: false });
 * navService.openLookup.mockResolvedValue([{ id: "guid-1", name: "Matter A", entityType: "sprk_matter" }]);
 * ```
 */
import type { INavigationService } from "../types/serviceInterfaces";

/**
 * Creates a mock INavigationService with jest.fn() stubs and sensible defaults.
 *
 * Default return values:
 * - `openRecord` → `undefined`
 * - `openDialog` → `{ confirmed: true }`
 * - `closeDialog` → synchronous no-op
 * - `openLookup` → `[]` (empty selection — no records chosen)
 *
 * @returns A fully-mocked INavigationService instance
 */
export function createMockNavigationService(): jest.Mocked<INavigationService> {
  return {
    openRecord: jest.fn().mockResolvedValue(undefined),
    openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
    closeDialog: jest.fn(),
    openLookup: jest.fn().mockResolvedValue([]),
  };
}
