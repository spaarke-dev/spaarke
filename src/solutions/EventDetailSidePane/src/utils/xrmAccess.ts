/**
 * Xrm Access Utilities for EventDetailSidePane
 *
 * Provides access to the Xrm global object from within a web resource iframe,
 * via the shared cross-frame walker in @spaarke/ui-components.
 */

import { getXrm as getSharedXrm } from '@spaarke/ui-components';

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
 * Get the Xrm object (WebApi + Utility) for this side pane.
 * Thin wrapper kept for this package's importers; the frame walk itself is
 * the shared cross-frame walker (task 081 / C-8). Named `getSidePaneXrm`
 * (was `getXrm`) so no second export shares the shared walker's name.
 */
export function getSidePaneXrm(): IXrmContext | null {
  const xrm: any = getSharedXrm();
  if (xrm?.WebApi && xrm?.Utility) {
    return xrm as IXrmContext;
  }
  return null;
}
