/**
 * Xrm Access Utilities for EventDetailSidePane
 *
 * Provides access to the Xrm global object from within a web resource iframe,
 * via the shared cross-frame walker in @spaarke/ui-components.
 */

import { getXrm, type XrmCapability } from '@spaarke/ui-components';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Xrm-like interface with the APIs we need
 */
export interface IXrmContext {
  WebApi: {
    retrieveRecord(
      entityType: string,
      id: string,
      options?: string
    ): Promise<Record<string, unknown>>;
    retrieveMultipleRecords(
      entityType: string,
      options?: string,
      maxPageSize?: number
    ): Promise<{ entities: Record<string, unknown>[]; nextLink?: string }>;
    updateRecord(
      entityType: string,
      id: string,
      data: Record<string, unknown>
    ): Promise<Record<string, unknown>>;
    createRecord(
      entityType: string,
      data: Record<string, unknown>
    ): Promise<Record<string, unknown>>;
  };
  Utility: {
    lookupObjects(
      lookupOptions: {
        defaultEntityType: string;
        entityTypes: string[];
        allowMultiSelect: boolean;
        defaultViewId?: string;
      }
    ): Promise<Array<{ id: string; name: string; entityType: string }>>;
    getEntityMetadata(
      entityName: string,
      attributes?: string[]
    ): Promise<{
      Attributes: {
        get(name: string): {
          attributeType: string;
          OptionSet?: {
            Options: Array<{
              Value: number;
              Label: { UserLocalizedLabel: { Label: string } };
            }>;
          };
        } | undefined;
      };
    }>;
  };
}

/**
 * The nearest Xrm that has `WebApi` AND the capability the caller uses
 * (`'metadata'` for `Utility.getEntityMetadata`, `'lookupObjects'` for the
 * lookup dialog; default `'webApi'` alone), typed as this side pane's
 * {@link IXrmContext}; null when no frame has both.
 *
 * The frame walk is the shared `getXrm` (task 081 / C-8) with the requirement
 * checked PER FRAME. Round 5 (review R4-3): this used to require only that
 * `Utility` exist, so a frame with a partial `Utility` could be picked for a
 * metadata or lookup call; each caller now names the member it calls.
 */
export function getXrmWithWebApiAnd(capability: XrmCapability = 'webApi'): IXrmContext | null {
  const xrm: any = getXrm(['webApi', capability]);
  return xrm ? (xrm as IXrmContext) : null;
}
