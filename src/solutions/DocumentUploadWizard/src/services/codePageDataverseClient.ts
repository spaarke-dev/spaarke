/**
 * codePageDataverseClient.ts
 *
 * Factory for creating a Dataverse client for Code Page context.
 *
 * Uses Xrm.WebApi from the parent Dataverse frame — the same API that PCF
 * controls use via ComponentFramework.WebApi. This avoids the need for a
 * separate Dataverse-scoped MSAL token (the BFF token has a different
 * audience and cannot authenticate to Dataverse OData directly).
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 */

import type {
    IDataverseClient,
    ILogger,
    DataverseRecordRef,
} from "@spaarke/ui-components/services/document-upload";
import { consoleLogger } from "@spaarke/ui-components/services/document-upload";

// ---------------------------------------------------------------------------
// Xrm.WebApi type shims (minimal subset used by this client)
// ---------------------------------------------------------------------------

interface XrmWebApi {
    createRecord(
        entityLogicalName: string,
        data: Record<string, unknown>
    ): Promise<{ id: string }>;
    updateRecord(
        entityLogicalName: string,
        id: string,
        data: Record<string, unknown>
    ): Promise<void>;
}

// ---------------------------------------------------------------------------
// XrmDataverseClient
// ---------------------------------------------------------------------------

/**
 * IDataverseClient that delegates to Xrm.WebApi from the parent Dataverse frame.
 *
 * Code Pages run as web resources inside a Dataverse iframe — the parent
 * frame exposes `Xrm.WebApi` which handles authentication automatically.
 */
class XrmDataverseClient implements IDataverseClient {
    private readonly webApi: XrmWebApi;
    private readonly logger: ILogger;

    constructor(webApi: XrmWebApi, logger: ILogger) {
        this.webApi = webApi;
        this.logger = logger;
    }

    async createRecord(
        entityLogicalName: string,
        data: Record<string, unknown>
    ): Promise<DataverseRecordRef> {
        this.logger.info('XrmDataverseClient', `Creating ${entityLogicalName} record`);
        const result = await this.webApi.createRecord(entityLogicalName, data);
        this.logger.info('XrmDataverseClient', `Created ${entityLogicalName} record: ${result.id}`);
        return { id: result.id };
    }

    async updateRecord(
        entityLogicalName: string,
        id: string,
        data: Record<string, unknown>
    ): Promise<void> {
        const sanitizedId = id.replace(/[{}]/g, '').toLowerCase();
        this.logger.info('XrmDataverseClient', `Updating ${entityLogicalName} record: ${sanitizedId}`);
        await this.webApi.updateRecord(entityLogicalName, sanitizedId, data);
        this.logger.info('XrmDataverseClient', `Updated ${entityLogicalName} record: ${sanitizedId}`);
    }
}

// ---------------------------------------------------------------------------
// Xrm.WebApi Resolution
// ---------------------------------------------------------------------------

/**
 * Resolve Xrm.WebApi from the frame hierarchy.
 *
 * Walks: window → parent → top to find Xrm.WebApi.
 * This is available because Code Pages are webresources loaded inside
 * a Dataverse dialog iframe.
 */
function resolveXrmWebApi(): XrmWebApi {
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            /* eslint-disable @typescript-eslint/no-explicit-any */
            const xrm = (frame as any).Xrm;
            if (xrm?.WebApi?.createRecord) {
                return xrm.WebApi as XrmWebApi;
            }
            /* eslint-enable @typescript-eslint/no-explicit-any */
        } catch {
            // Cross-origin frame — skip
        }
    }

    throw new Error(
        "[codePageDataverseClient] Xrm.WebApi not found in frame hierarchy. " +
        "Ensure the Code Page is running inside a Dataverse webresource dialog."
    );
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Create a Dataverse client for Code Page context.
 *
 * Uses Xrm.WebApi from the parent frame — no separate token needed.
 *
 * @param logger - Optional logger
 * @returns IDataverseClient backed by Xrm.WebApi
 */
export function createCodePageDataverseClient(
    logger?: ILogger
): IDataverseClient {
    const webApi = resolveXrmWebApi();
    return new XrmDataverseClient(webApi, logger ?? consoleLogger);
}
