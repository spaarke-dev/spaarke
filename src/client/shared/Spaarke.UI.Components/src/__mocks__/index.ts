/**
 * Mock adapters barrel — re-exports all mock service factories.
 *
 * @example
 * ```typescript
 * import { createMockServices, createMockDataService } from "../__mocks__";
 * ```
 */
export { createMockDataService } from "./mockDataService";
export { createMockUploadService } from "./mockUploadService";
export { createMockNavigationService } from "./mockNavigationService";
export { createMockServices } from "./createMockServices";
export type { MockServices } from "./createMockServices";
