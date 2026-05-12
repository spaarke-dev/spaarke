/**
 * findSimilarService.ts
 * Service layer for the Find Similar wizard.
 *
 * 1. Extracts text from uploaded files via BFF
 * 2. Uses extracted text as a semantic search query
 * 3. Searches both documents and records (matters + projects)
 *
 * NOTE: This shared version accepts a service config object so it does not
 * hard-code environment-specific imports (authenticatedFetch, BFF base URL).
 */
import type { IFindSimilarServiceConfig, IFindSimilarResults } from './findSimilarTypes';
/** File shape needed by the service — matches IUploadedFile from FileUpload. */
interface IUploadableFile {
    name: string;
    file: File;
}
/**
 * Runs the full Find Similar pipeline:
 * 1. Extract text from uploaded files
 * 2. Search for similar documents
 * 3. Search for similar records (matters + projects)
 *
 * All three search calls run in parallel after text extraction.
 *
 * @param files  - Uploaded files with name and File object.
 * @param config - Service configuration (BFF base URL, authenticated fetch).
 * @param signal - Optional AbortSignal for cancellation.
 */
export declare function runFindSimilar(files: IUploadableFile[], config: IFindSimilarServiceConfig, signal?: AbortSignal): Promise<IFindSimilarResults>;
export {};
//# sourceMappingURL=findSimilarService.d.ts.map