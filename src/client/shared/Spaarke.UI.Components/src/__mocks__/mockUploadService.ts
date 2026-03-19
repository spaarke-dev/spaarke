/**
 * Mock adapter for {@link IUploadService}.
 *
 * Provides a factory that returns a fully-mocked IUploadService with jest.fn()
 * stubs and sensible default resolved values for use in unit tests.
 *
 * @example
 * ```typescript
 * const uploadService = createMockUploadService();
 * uploadService.uploadFile.mockResolvedValue({
 *   id: "custom-id", name: "report.docx", size: 2048, url: "https://example.com/report.docx",
 * });
 * ```
 */
import type { IUploadService } from "../types/serviceInterfaces";

/**
 * Creates a mock IUploadService with jest.fn() stubs and sensible defaults.
 *
 * Default return values:
 * - `uploadFile` → `{ id: "file-001", name: "test.pdf", size: 1024, url: "https://example.com/test.pdf" }`
 * - `getContainerIdForEntity` → `"container-001"`
 *
 * @returns A fully-mocked IUploadService instance
 */
export function createMockUploadService(): jest.Mocked<IUploadService> {
  return {
    uploadFile: jest.fn().mockResolvedValue({
      id: "file-001",
      name: "test.pdf",
      size: 1024,
      url: "https://example.com/test.pdf",
    }),
    getContainerIdForEntity: jest.fn().mockResolvedValue("container-001"),
  };
}
