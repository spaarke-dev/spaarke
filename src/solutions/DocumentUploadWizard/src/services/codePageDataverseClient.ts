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
import { cleanGuid, getXrm } from "@spaarke/ui-components";

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
        const sanitizedId = cleanGuid(id);
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
 * This is available because Code Pages are webresources loaded inside
 * a Dataverse dialog iframe.
 */
function resolveXrmWebApi(): XrmWebApi {
    // Shared cross-frame walker (task 081 / C-8) with the createRecord check
    // applied PER FRAME (as the former local walk did).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const webApi = getXrm((x: any) => typeof x.WebApi?.createRecord === "function")?.WebApi;
    if (webApi?.createRecord) {
        return webApi as unknown as XrmWebApi;
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
