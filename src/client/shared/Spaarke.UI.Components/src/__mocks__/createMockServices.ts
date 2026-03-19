/**
 * Factory helper that creates all mock service adapters in one call.
 *
 * Useful for tests that need the full set of service mocks without
 * importing each factory individually.
 *
 * @example
 * ```typescript
 * const { dataService, uploadService, navigationService } = createMockServices();
 * dataService.createRecord.mockResolvedValue("custom-guid");
 * ```
 */
import type {
  IDataService,
  IUploadService,
  INavigationService,
} from "../types/serviceInterfaces";
import { createMockDataService } from "./mockDataService";
import { createMockUploadService } from "./mockUploadService";
import { createMockNavigationService } from "./mockNavigationService";

/**
 * Aggregate container for all mock service instances.
 */
export interface MockServices {
  /** Mocked IDataService with jest.fn() stubs */
  dataService: jest.Mocked<IDataService>;
  /** Mocked IUploadService with jest.fn() stubs */
  uploadService: jest.Mocked<IUploadService>;
  /** Mocked INavigationService with jest.fn() stubs */
  navigationService: jest.Mocked<INavigationService>;
}

/**
 * Creates all mock service adapters in a single call.
 *
 * Each service is an independent mock instance with sensible default
 * return values. Override individual stubs as needed for specific tests.
 *
 * @returns An object containing all three mocked service instances
 */
export function createMockServices(): MockServices {
  return {
    dataService: createMockDataService(),
    uploadService: createMockUploadService(),
    navigationService: createMockNavigationService(),
  };
}
