/**
 * codePageDataverseClient.ts
 *
 * Factory for creating an ODataDataverseClient wired with Code Page auth.
 *
 * In a Code Page context, we do not have access to ComponentFramework.WebApi,
 * so we use ODataDataverseClient (direct OData fetch with bearer tokens)
 * instead of PcfDataverseClient.
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - All SPE operations through BFF API
 */

import {
    ODataDataverseClient,
} from "@spaarke/ui-components/services/document-upload";
import type {
    ILogger,
} from "@spaarke/ui-components/services/document-upload";

import {
    createDataverseTokenProvider,
    resolveDataverseUrl,
} from "./codePageTokenProvider";

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Options for creating the Code Page Dataverse client.
 */
export interface CodePageDataverseClientOptions {
    /** Override Dataverse URL (defaults to Xrm context resolution). */
    dataverseUrl?: string;

    /** Logger implementation (defaults to consoleLogger from shared types). */
    logger?: ILogger;
}

/**
 * Create an ODataDataverseClient configured for Code Page context.
 *
 * Wires:
 *   - Token provider: @spaarke/auth (BFF-scoped, Dataverse-delegated)
 *   - Dataverse URL: resolved from Xrm context or fallback
 *
 * @param options - Optional overrides
 * @returns Configured ODataDataverseClient instance
 *
 * @example
 * ```ts
 * const client = createCodePageDataverseClient();
 * const result = await client.createRecord('sprk_document', payload);
 * ```
 */
export function createCodePageDataverseClient(
    options?: CodePageDataverseClientOptions
): ODataDataverseClient {
    const dataverseUrl = options?.dataverseUrl ?? resolveDataverseUrl();
    const tokenProvider = createDataverseTokenProvider();

    return new ODataDataverseClient({
        dataverseUrl,
        getAccessToken: tokenProvider,
        logger: options?.logger,
    });
}
