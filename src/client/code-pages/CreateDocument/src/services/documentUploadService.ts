/**
 * documentUploadService.ts
 * Uploads files to SharePoint Embedded via the BFF API.
 *
 * Uses authenticatedFetch from @spaarke/auth for Bearer token attachment.
 * Endpoint: PUT /api/obo/containers/{containerId}/files/{path}
 *
 * Supports multi-file upload with per-file progress tracking via
 * XMLHttpRequest (fetch does not support upload progress).
 *
 * @see ADR-007 - Document access through BFF API (SpeFileStore facade)
 * @see ADR-008 - Endpoint filters for auth (Bearer token)
 */

import { getAuthProvider } from "@spaarke/auth";
import type { IUploadedFile, IUploadResult } from "../types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Callback for per-file upload progress. */
export type UploadProgressCallback = (fileId: string, progress: number) => void;

// ---------------------------------------------------------------------------
// Upload single file
// ---------------------------------------------------------------------------

/**
 * Upload a single file to SPE via the BFF PUT endpoint.
 * Uses XMLHttpRequest for upload progress reporting.
 *
 * @param containerId  SPE container ID
 * @param file         The uploaded file descriptor
 * @param onProgress   Optional progress callback (0-100)
 * @returns Upload result with driveItemId on success
 */
async function uploadSingleFile(
    containerId: string,
    file: IUploadedFile,
    onProgress?: (progress: number) => void,
): Promise<IUploadResult> {
    const provider = getAuthProvider();
    const token = await provider.getAccessToken();
    const bffBaseUrl = provider.getConfig().bffBaseUrl;

    // Encode the file path for the URL
    const encodedPath = encodeURIComponent(file.name);
    const url = `${bffBaseUrl}/api/obo/containers/${containerId}/files/${encodedPath}`;

    return new Promise<IUploadResult>((resolve) => {
        const xhr = new XMLHttpRequest();
        xhr.open("PUT", url, true);
        if (token) {
            xhr.setRequestHeader("Authorization", `Bearer ${token}`);
        }
        xhr.setRequestHeader("Content-Type", file.file.type || "application/octet-stream");

        // Progress tracking
        xhr.upload.addEventListener("progress", (event) => {
            if (event.lengthComputable && onProgress) {
                const percent = Math.round((event.loaded / event.total) * 100);
                onProgress(percent);
            }
        });

        // Completion
        xhr.addEventListener("load", () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                let driveItemId: string | undefined;
                try {
                    const response = JSON.parse(xhr.responseText);
                    driveItemId = response.id ?? response.driveItemId;
                } catch {
                    // Response may not be JSON — that's acceptable
                }
                resolve({
                    success: true,
                    fileName: file.name,
                    driveItemId,
                });
            } else {
                let errorMessage = `Upload failed (HTTP ${xhr.status})`;
                try {
                    const errorBody = JSON.parse(xhr.responseText);
                    errorMessage = errorBody.detail ?? errorBody.title ?? errorMessage;
                } catch {
                    // Non-JSON error response
                }
                resolve({
                    success: false,
                    fileName: file.name,
                    error: errorMessage,
                });
            }
        });

        xhr.addEventListener("error", () => {
            resolve({
                success: false,
                fileName: file.name,
                error: "Network error during upload",
            });
        });

        xhr.addEventListener("abort", () => {
            resolve({
                success: false,
                fileName: file.name,
                error: "Upload was cancelled",
            });
        });

        xhr.send(file.file);
    });
}

// ---------------------------------------------------------------------------
// Upload multiple files
// ---------------------------------------------------------------------------

/**
 * Upload multiple files to SPE with progress tracking.
 *
 * Files are uploaded sequentially to avoid overwhelming the BFF.
 * Per-file progress is reported via the onProgress callback.
 *
 * @param containerId   SPE container ID
 * @param files         Array of files to upload
 * @param onProgress    Per-file progress callback (fileId, 0-100)
 * @returns Array of results, one per file
 */
export async function uploadFiles(
    containerId: string,
    files: IUploadedFile[],
    onProgress?: UploadProgressCallback,
): Promise<IUploadResult[]> {
    const results: IUploadResult[] = [];

    for (const file of files) {
        const result = await uploadSingleFile(
            containerId,
            file,
            (progress) => onProgress?.(file.id, progress),
        );
        results.push(result);
    }

    return results;
}
