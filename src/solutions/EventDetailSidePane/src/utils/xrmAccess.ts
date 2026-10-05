/**
 * Xrm Access Utilities for EventDetailSidePane
 *
 * Provides access to the Xrm global object from within a web resource iframe,
 * via the shared cross-frame walker in @spaarke/ui-components.
 */

import { getXrm } from '@spaarke/ui-components';

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
 * The nearest Xrm that has BOTH `WebApi` and `Utility`, typed as this side
 * pane's {@link IXrmContext}; null when no frame has both.
 *
 * The frame walk is the shared `getXrm` (task 081 / C-8) with the requirement
 * checked PER FRAME, so a child frame whose Xrm has only `WebApi` is skipped
 * in favour of an outer frame that has both. (Named for its contract; was
 * `getXrm`, then `getSidePaneXrm`.)
 */
export function getXrmWithWebApiAndUtility(): IXrmContext | null {
  const xrm: any = getXrm(['webApi', x => !!x.Utility]);
  return xrm ? (xrm as IXrmContext) : null;
}
