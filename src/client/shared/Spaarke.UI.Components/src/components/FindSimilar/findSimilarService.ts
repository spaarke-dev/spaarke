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
import type {
  IFindSimilarServiceConfig,
  IDocumentResult,
  IRecordResult,
  IFindSimilarResults,
} from "./findSimilarTypes";

/** File shape needed by the service — matches IUploadedFile from FileUpload. */
interface IUploadableFile {
  name: string;
  file: File;
}

const LOG_PREFIX = "[FindSimilarService]";

/**
 * Extracts text content from uploaded files using the BFF text extraction endpoint.
 * Returns a concatenated text string suitable as a search query.
 */
async function extractTextFromFiles(
  files: IUploadableFile[],
  config: IFindSimilarServiceConfig,
  signal?: AbortSignal,
): Promise<string> {
  const bffBaseUrl = config.getBffBaseUrl();
  const url = `${bffBaseUrl}/workspace/files/extract-text`;

  const formData = new FormData();
  for (const f of files) {
    formData.append("files", f.file, f.name);
  }

  console.info(`${LOG_PREFIX} Extracting text from ${files.length} file(s)`);

  const response = await config.authenticatedFetch(url, {
    method: "POST",
    body: formData,
    signal,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => "Unknown error");
    throw new Error(
      `Text extraction failed (${response.status}): ${errorText}`,
    );
  }

  const json = await response.json();
  return json.text as string;
}

/**
 * Searches for similar documents using POST /api/ai/search.
 */
async function searchDocuments(
  query: string,
  config: IFindSimilarServiceConfig,
  signal?: AbortSignal,
): Promise<{ results: IDocumentResult[]; totalCount: number }> {
  const bffBaseUrl = config.getBffBaseUrl();
  const url = `${bffBaseUrl}/ai/search`;

  const response = await config.authenticatedFetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    signal,
    body: JSON.stringify({
      query,
      scope: "all",
      options: {
        limit: 20,
        offset: 0,
        includeHighlights: true,
        hybridMode: "rrf",
      },
    }),
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => "Unknown error");
    throw new Error(
      `Document search failed (${response.status}): ${errorText}`,
    );
  }

  const json = await response.json();
  return {
    results: json.results as IDocumentResult[],
    totalCount: json.metadata?.totalResults ?? json.results?.length ?? 0,
  };
}

/**
 * Searches for similar records (matters and/or projects) using POST /api/ai/search/records.
 */
async function searchRecords(
  query: string,
  recordTypes: string[],
  config: IFindSimilarServiceConfig,
  signal?: AbortSignal,
): Promise<{ results: IRecordResult[]; totalCount: number }> {
  const bffBaseUrl = config.getBffBaseUrl();
  const url = `${bffBaseUrl}/ai/search/records`;

  const response = await config.authenticatedFetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    signal,
    body: JSON.stringify({
      query,
      recordTypes,
      options: {
        limit: 20,
        offset: 0,
        hybridMode: "rrf",
      },
    }),
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => "Unknown error");
    throw new Error(`Record search failed (${response.status}): ${errorText}`);
  }

  const json = await response.json();
  return {
    results: json.results as IRecordResult[],
    totalCount: json.metadata?.totalCount ?? json.results?.length ?? 0,
  };
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
export async function runFindSimilar(
  files: IUploadableFile[],
  config: IFindSimilarServiceConfig,
  signal?: AbortSignal,
): Promise<IFindSimilarResults> {
  // Step 1: Extract text
  const extractedText = await extractTextFromFiles(files, config, signal);

  if (!extractedText || extractedText.trim().length === 0) {
    throw new Error("No text could be extracted from the uploaded files.");
  }

  // Truncate to max 1000 chars for search query
  const query = extractedText.trim().substring(0, 1000);
  console.info(
    `${LOG_PREFIX} Running semantic search with ${query.length} chars`,
  );

  // Step 2: Search in parallel
  const [docResults, matterResults, projectResults] = await Promise.all([
    searchDocuments(query, config, signal),
    searchRecords(query, ["sprk_matter"], config, signal),
    searchRecords(query, ["sprk_project"], config, signal),
  ]);

  return {
    documents: docResults.results,
    documentsTotalCount: docResults.totalCount,
    matters: matterResults.results,
    mattersTotalCount: matterResults.totalCount,
    projects: projectResults.results,
    projectsTotalCount: projectResults.totalCount,
  };
}
