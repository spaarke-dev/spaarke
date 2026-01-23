/**
 * SemanticSearchApiService
 *
 * API service for calling the semantic search endpoint.
 * Handles request building, authentication, and response parsing.
 *
 * @see spec.md for API contract details
 */

import { MsalAuthProvider } from "./auth/MsalAuthProvider";
import {
    SearchRequest,
    SearchResponse,
    SearchError,
} from "../types";

/**
 * API error response structure
 */
interface ApiErrorResponse {
    error?: string;
    message?: string;
    code?: string;
}

/**
 * Service for semantic search API calls
 */
export class SemanticSearchApiService {
    private readonly apiBaseUrl: string;
    private readonly authProvider: MsalAuthProvider;

    /**
     * Create a new SemanticSearchApiService
     * @param apiBaseUrl - Base URL for the BFF API (e.g., https://api.example.com)
     * @param authProvider - MSAL auth provider for token acquisition
     */
    constructor(apiBaseUrl: string, authProvider: MsalAuthProvider) {
        this.apiBaseUrl = apiBaseUrl.replace(/\/$/, ""); // Remove trailing slash
        this.authProvider = authProvider;
    }

    /**
     * Execute a semantic search
     * @param request - Search request parameters
     * @returns Search response with results
     * @throws SearchError on failure
     */
    async search(request: SearchRequest): Promise<SearchResponse> {
        const endpoint = `${this.apiBaseUrl}/api/ai/search`;

        try {
            // Get access token
            const token = await this.authProvider.getAccessToken();
            if (!token) {
                throw this.createError(
                    "Authentication required. Please sign in.",
                    "AUTH_REQUIRED",
                    true
                );
            }

            // Transform PCF request format to API format
            const apiRequest = this.transformRequest(request);

            // Build request
            const response = await fetch(endpoint, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    Authorization: `Bearer ${token}`,
                },
                body: JSON.stringify(apiRequest),
            });

            // Handle HTTP errors
            if (!response.ok) {
                return await this.handleHttpError(response);
            }

            // Parse response
            const data: SearchResponse = await response.json();
            return this.validateResponse(data);
        } catch (error) {
            // Re-throw SearchError as-is
            if (this.isSearchError(error)) {
                throw error;
            }

            // Handle network errors
            if (error instanceof TypeError && error.message.includes("fetch")) {
                throw this.createError(
                    "Unable to connect to the search service. Please check your connection.",
                    "NETWORK_ERROR",
                    true
                );
            }

            // Unknown error
            throw this.createError(
                "An unexpected error occurred while searching.",
                "UNKNOWN_ERROR",
                true
            );
        }
    }

    /**
     * Handle HTTP error responses
     */
    private async handleHttpError(response: Response): Promise<never> {
        let errorMessage = "Search failed";
        let errorCode = `HTTP_${response.status}`;
        let retryable = false;

        try {
            const errorData: ApiErrorResponse = await response.json();
            errorMessage = errorData.message ?? errorData.error ?? errorMessage;
            if (errorData.code) {
                errorCode = errorData.code;
            }
        } catch {
            // Response not JSON, use status text
            errorMessage = response.statusText || errorMessage;
        }

        switch (response.status) {
            case 400:
                throw this.createError(
                    "Invalid search request. Please modify your query.",
                    errorCode,
                    false
                );
            case 401:
                throw this.createError(
                    "Your session has expired. Please sign in again.",
                    "AUTH_EXPIRED",
                    true
                );
            case 403:
                throw this.createError(
                    "You do not have permission to perform this search.",
                    "FORBIDDEN",
                    false
                );
            case 404:
                throw this.createError(
                    "Search service not found. Please contact support.",
                    "NOT_FOUND",
                    false
                );
            case 429:
                throw this.createError(
                    "Too many requests. Please wait a moment and try again.",
                    "RATE_LIMITED",
                    true
                );
            case 500:
            case 502:
            case 503:
            case 504:
                retryable = true;
                throw this.createError(
                    "The search service is temporarily unavailable. Please try again.",
                    errorCode,
                    retryable
                );
            default:
                throw this.createError(errorMessage, errorCode, retryable);
        }
    }

    /**
     * Validate and normalize the API response
     */
    private validateResponse(data: unknown): SearchResponse {
        // Type guard for response structure
        if (!data || typeof data !== "object") {
            throw this.createError(
                "Invalid response from search service.",
                "INVALID_RESPONSE",
                true
            );
        }

        const response = data as SearchResponse;

        // Ensure required fields exist
        if (!Array.isArray(response.results)) {
            throw this.createError(
                "Invalid response format from search service.",
                "INVALID_RESPONSE",
                true
            );
        }

        // Normalize response
        return {
            results: response.results,
            totalCount: response.totalCount ?? response.results.length,
            metadata: response.metadata ?? {
                searchTimeMs: 0,
                query: "",
            },
        };
    }

    /**
     * Create a SearchError
     */
    private createError(
        message: string,
        code: string,
        retryable: boolean
    ): SearchError {
        return {
            message,
            code,
            retryable,
        };
    }

    /**
     * Type guard for SearchError
     */
    private isSearchError(error: unknown): error is SearchError {
        return (
            typeof error === "object" &&
            error !== null &&
            "message" in error &&
            "retryable" in error
        );
    }

    /**
     * Transform PCF request format to API request format.
     *
     * PCF uses: scope = "all" | "matter" | "custom" with scopeId
     * API uses: scope = "entity" | "documentIds" with entityType/entityId
     *
     * Mapping:
     * - PCF "matter" + scopeId → API "entity" + entityType="matter" + entityId=scopeId
     * - PCF "all" → Not supported in R1
     * - PCF "custom" → Would need documentIds (not implemented)
     */
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    private transformRequest(request: SearchRequest): Record<string, any> {
        // Map PCF scope to API scope
        let apiScope: string;
        let entityType: string | undefined;
        let entityId: string | undefined;

        switch (request.scope) {
            case "matter":
                // Matter scope maps to entity scope with entityType=matter
                apiScope = "entity";
                entityType = "matter";
                entityId = request.scopeId ?? undefined;
                break;
            case "all":
                // scope=all is not supported in R1, but API will return proper error
                apiScope = "all";
                break;
            case "custom":
                // Custom scope would use documentIds, not implemented yet
                apiScope = "documentIds";
                break;
            default:
                // Pass through unknown scopes
                apiScope = request.scope;
        }

        // Build API request
        return {
            query: request.query,
            scope: apiScope,
            entityType,
            entityId,
            filters: request.filters ? {
                documentTypes: request.filters.documentTypes,
                matterTypes: request.filters.matterTypes,
                fileTypes: request.filters.fileTypes,
                dateFrom: request.filters.dateRange?.from,
                dateTo: request.filters.dateRange?.to,
            } : undefined,
            options: request.options ? {
                top: request.options.limit,
                skip: request.options.offset,
                includeHighlights: request.options.includeHighlights,
            } : undefined,
        };
    }
}

export default SemanticSearchApiService;
