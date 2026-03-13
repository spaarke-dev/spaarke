/**
 * SDAP (SharePoint Document Access Platform) API Client
 *
 * Provides type-safe methods for interacting with the SDAP BFF API
 * for file operations (upload, download, delete, replace) via SharePoint Embedded.
 *
 * Authentication: Accepts an ITokenProvider function -- works with both
 * PCF (MSAL OBO) and Code Page (MSAL browser) contexts.
 *
 * ADR Compliance:
 * - ADR-007: All SPE operations through BFF API
 * - ADR-008: Requires authentication (Bearer token via ITokenProvider)
 *
 * @version 1.0.0
 */

import type {
  ITokenProvider,
  ILogger,
  SpeFileMetadata,
  FileUploadApiRequest,
  FileDownloadRequest,
  FileDeleteRequest,
  FileReplaceRequest,
} from "./types";
import { consoleLogger } from "./types";

/**
 * Optional callback invoked on 401 responses before retry.
 * Allows the caller to clear token caches (e.g., MSAL cache).
 */
export type OnUnauthorizedCallback = () => void;

/**
 * Configuration for SdapApiClient.
 */
export interface SdapApiClientOptions {
  /** SDAP BFF API base URL */
  baseUrl: string;

  /** Token provider function -- returns a bearer token */
  getAccessToken: ITokenProvider;

  /** Request timeout in milliseconds (default: 300000 = 5 minutes) */
  timeout?: number;

  /** Logger implementation (default: consoleLogger) */
  logger?: ILogger;

  /**
   * Callback invoked when a 401 response is received, before retrying.
   * Use this to clear MSAL or other token caches.
   */
  onUnauthorized?: OnUnauthorizedCallback;
}

/**
 * SDAP API Client
 *
 * Stateless HTTP client for SDAP BFF API file operations.
 * Authentication is delegated to the injected ITokenProvider.
 */
export class SdapApiClient {
  private readonly baseUrl: string;
  private readonly timeout: number;
  private readonly getAccessToken: ITokenProvider;
  private readonly logger: ILogger;
  private readonly onUnauthorized?: OnUnauthorizedCallback;

  constructor(options: SdapApiClientOptions) {
    this.baseUrl = options.baseUrl.endsWith("/")
      ? options.baseUrl.slice(0, -1)
      : options.baseUrl;
    this.getAccessToken = options.getAccessToken;
    this.timeout = options.timeout ?? 300000;
    this.logger = options.logger ?? consoleLogger;
    this.onUnauthorized = options.onUnauthorized;

    this.logger.info("SdapApiClient", "Initialized", {
      baseUrl: this.baseUrl,
      timeout: this.timeout,
    });
  }

  // -----------------------------------------------------------------------
  // Public API
  // -----------------------------------------------------------------------

  /**
   * Upload file to SharePoint Embedded.
   * API: PUT /api/obo/containers/{containerId}/files/{fileName}
   */
  async uploadFile(request: FileUploadApiRequest): Promise<SpeFileMetadata> {
    this.logger.info("SdapApiClient", "Uploading file", {
      fileName: request.fileName,
      fileSize: request.file.size,
      containerId: request.driveId,
    });

    try {
      const token = await this.getAccessToken();

      const url = `${this.baseUrl}/obo/containers/${encodeURIComponent(request.driveId)}/files/${encodeURIComponent(request.fileName)}`;

      const response = await this.fetchWithTimeout(url, {
        method: "PUT",
        headers: {
          Authorization: `Bearer ${token}`,
        },
        body: request.file,
      });

      const result = await this.handleResponse<SpeFileMetadata>(response);

      this.logger.info("SdapApiClient", "File uploaded successfully", result);
      return result;
    } catch (error) {
      this.logger.error("SdapApiClient", "File upload failed", error);
      throw this.enhanceError(error, "File upload failed");
    }
  }

  /**
   * Download file from SharePoint Embedded.
   * API: GET /obo/drives/{driveId}/items/{itemId}/content
   */
  async downloadFile(request: FileDownloadRequest): Promise<Blob> {
    this.logger.info("SdapApiClient", "Downloading file", {
      driveId: request.driveId,
      itemId: request.itemId,
    });

    try {
      const token = await this.getAccessToken();

      const url = `${this.baseUrl}/obo/drives/${encodeURIComponent(request.driveId)}/items/${encodeURIComponent(request.itemId)}/content`;

      const response = await this.fetchWithTimeout(url, {
        method: "GET",
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Download failed: ${response.status} ${errorText}`);
      }

      const blob = await response.blob();

      this.logger.info("SdapApiClient", "File downloaded successfully", {
        size: blob.size,
        type: blob.type,
      });

      return blob;
    } catch (error) {
      this.logger.error("SdapApiClient", "File download failed", error);
      throw this.enhanceError(error, "File download failed");
    }
  }

  /**
   * Delete file from SharePoint Embedded.
   * API: DELETE /obo/drives/{driveId}/items/{itemId}
   */
  async deleteFile(request: FileDeleteRequest): Promise<void> {
    this.logger.info("SdapApiClient", "Deleting file", {
      driveId: request.driveId,
      itemId: request.itemId,
    });

    try {
      const token = await this.getAccessToken();

      const url = `${this.baseUrl}/obo/drives/${encodeURIComponent(request.driveId)}/items/${encodeURIComponent(request.itemId)}`;

      const response = await this.fetchWithTimeout(url, {
        method: "DELETE",
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      await this.handleResponse<void>(response);

      this.logger.info("SdapApiClient", "File deleted successfully");
    } catch (error) {
      this.logger.error("SdapApiClient", "File delete failed", error);
      throw this.enhanceError(error, "File delete failed");
    }
  }

  /**
   * Replace file in SharePoint Embedded.
   * Implemented as: DELETE old file + UPLOAD new file.
   */
  async replaceFile(request: FileReplaceRequest): Promise<SpeFileMetadata> {
    this.logger.info("SdapApiClient", "Replacing file", {
      fileName: request.fileName,
      fileSize: request.file.size,
      driveId: request.driveId,
      itemId: request.itemId,
    });

    try {
      // Step 1: Delete old file
      await this.deleteFile({
        driveId: request.driveId,
        itemId: request.itemId,
      });

      this.logger.info("SdapApiClient", "Old file deleted, uploading new file");

      // Step 2: Upload new file
      const result = await this.uploadFile({
        file: request.file,
        driveId: request.driveId,
        fileName: request.fileName,
      });

      this.logger.info("SdapApiClient", "File replaced successfully", result);
      return result;
    } catch (error) {
      this.logger.error("SdapApiClient", "File replace failed", error);
      throw this.enhanceError(error, "File replace failed");
    }
  }

  // -----------------------------------------------------------------------
  // Private helpers
  // -----------------------------------------------------------------------

  /**
   * Fetch with timeout support and automatic 401 retry.
   *
   * On 401: invokes onUnauthorized callback (if provided), refreshes token, retries once.
   */
  private async fetchWithTimeout(
    url: string,
    options: RequestInit,
  ): Promise<Response> {
    let attempt = 0;
    const maxAttempts = 2;

    while (attempt < maxAttempts) {
      attempt++;

      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), this.timeout);

      try {
        const response = await fetch(url, {
          ...options,
          signal: controller.signal,
        });

        clearTimeout(timeoutId);

        // Success or last attempt -- return response
        if (response.ok || attempt === maxAttempts) {
          return response;
        }

        // 401 Unauthorized -- token may have expired during request
        if (response.status === 401 && attempt < maxAttempts) {
          this.logger.warn(
            "SdapApiClient",
            "401 Unauthorized response -- clearing token cache and retrying",
            {
              url,
              attempt,
              maxAttempts,
            },
          );

          // Allow caller to clear token caches
          this.onUnauthorized?.();

          // Get fresh token for retry
          const newToken = await this.getAccessToken();

          // Update Authorization header with fresh token
          if (options.headers) {
            (options.headers as Record<string, string>)["Authorization"] =
              `Bearer ${newToken}`;
          }

          this.logger.info(
            "SdapApiClient",
            "Retrying request with fresh token",
          );
          continue;
        }

        // Other errors (non-401) -- return immediately
        return response;
      } catch (error) {
        clearTimeout(timeoutId);

        if (error instanceof Error && error.name === "AbortError") {
          throw new Error(`Request timeout after ${this.timeout}ms`);
        }

        throw error;
      }
    }

    // Should never reach here, but TypeScript needs a return
    throw new Error("Unexpected error in fetchWithTimeout retry logic");
  }

  /**
   * Handle API response and parse JSON.
   */
  private async handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
      let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
      let errorDetails = "";

      try {
        const errorData = await response.json();
        errorMessage = errorData.error || errorMessage;
        errorDetails = errorData.details || "";
      } catch {
        try {
          errorDetails = await response.text();
        } catch {
          // Ignore text parsing errors
        }
      }

      const userFriendlyMessage = this.getUserFriendlyErrorMessage(
        response.status,
        errorMessage,
      );

      const error = new Error(userFriendlyMessage) as Error & {
        details?: string;
        status?: number;
        originalMessage?: string;
      };
      error.details = errorDetails;
      error.status = response.status;
      error.originalMessage = errorMessage;

      throw error;
    }

    // For 204 No Content or empty responses
    if (
      response.status === 204 ||
      response.headers.get("content-length") === "0"
    ) {
      return undefined as T;
    }

    try {
      return await response.json();
    } catch (error) {
      this.logger.error(
        "SdapApiClient",
        "Failed to parse response JSON",
        error,
      );
      throw new Error("Invalid JSON response from server");
    }
  }

  /**
   * Get user-friendly error message based on HTTP status code.
   */
  private getUserFriendlyErrorMessage(
    status: number,
    originalMessage: string,
  ): string {
    switch (status) {
      case 401:
        return "Authentication failed. Your session may have expired. Please refresh the page and try again.";
      case 403:
        return "Access denied. You do not have permission to perform this operation. Please contact your administrator.";
      case 404:
        return "The requested file was not found. It may have been deleted or moved.";
      case 408:
      case 504:
        return "Request timeout. The server took too long to respond. Please try again.";
      case 429:
        return "Too many requests. Please wait a moment and try again.";
      case 500:
        return "Server error occurred. Please try again later. If the problem persists, contact your administrator.";
      case 502:
      case 503:
        return "The service is temporarily unavailable. Please try again in a few minutes.";
      default:
        return originalMessage;
    }
  }

  /**
   * Enhance error with context.
   */
  private enhanceError(error: unknown, context: string): Error {
    if (error instanceof Error) {
      error.message = `${context}: ${error.message}`;
      return error;
    }
    return new Error(`${context}: ${String(error)}`);
  }
}
