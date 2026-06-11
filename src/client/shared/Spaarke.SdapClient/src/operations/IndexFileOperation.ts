import { AuthenticatedFetchFn, IndexFileRequest, IndexFileResult } from '../types';

/**
 * Triggers sync OBO indexing of a SPE-resident file into Azure AI Search by
 * calling `POST /api/ai/rag/index-file` on the BFF.
 *
 * This is the canonical "post-upload indexing" operation for user-OBO-written
 * files. It satisfies the writer-identity matching rule (Pattern 4): the same
 * user identity that uploaded the file to SPE is the identity that triggers
 * indexing — the BFF performs OBO download → extract → chunk → embed → write
 * to AI Search, all inside the user's request scope.
 *
 * @remarks
 * Pair with {@link UploadOperation}: after a successful PUT to
 * `/api/obo/containers/{containerId}/files/{path}`, call this operation with
 * the returned `DriveItem`'s `driveId` + `id` to make the file searchable.
 *
 * Non-fatal by design — callers should `try/catch` and continue if indexing
 * fails; the file is already safely in SPE. Operators can re-trigger via the
 * "Send to Index" ribbon command, which uses the same endpoint.
 *
 * Auth: uses the injected `AuthenticatedFetchFn` (typically
 * `@spaarke/auth.authenticatedFetch`) per ADR-028.
 */
export class IndexFileOperation {
  constructor(
    private readonly baseUrl: string,
    private readonly timeout: number,
    private readonly authenticatedFetch: AuthenticatedFetchFn
  ) {}

  /**
   * Index a single file via the BFF's RAG indexing pipeline.
   *
   * @param request - File location, tenant, optional parent entity + index targeting
   * @returns Indexing outcome with chunk count or error message
   * @throws Error only if the BFF endpoint is unreachable; HTTP errors are returned
   *         as `{ success: false, errorMessage }` so callers can decide their policy.
   */
  public async indexFile(request: IndexFileRequest): Promise<IndexFileResult> {
    const url = `${this.baseUrl}/api/ai/rag/index-file`;

    const body = {
      driveId: request.driveId,
      itemId: request.itemId,
      fileName: request.fileName,
      tenantId: request.tenantId,
      documentId: request.documentId,
      parentEntity: request.parentEntity,
      searchIndexName: request.searchIndexName,
    };

    const response = await this.authenticatedFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(this.timeout),
    });

    if (!response.ok) {
      const errorText = await response.text().catch(() => response.statusText);
      return {
        success: false,
        errorMessage: `HTTP ${response.status}: ${errorText.substring(0, 500)}`,
      };
    }

    const payload = (await response.json().catch(() => ({}))) as {
      success?: boolean;
      chunksIndexed?: number;
      errorMessage?: string;
    };

    return {
      success: payload.success === true,
      chunksIndexed: payload.chunksIndexed,
      errorMessage: payload.errorMessage,
    };
  }
}
