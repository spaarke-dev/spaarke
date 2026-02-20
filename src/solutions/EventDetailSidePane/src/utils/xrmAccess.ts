/**
 * Xrm Access Utilities for EventDetailSidePane
 *
 * Provides access to the Xrm global object from within a web resource iframe.
 * Tries window.parent.Xrm first (Custom Page in iframe), then window.Xrm.
 */

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
 * Get the Xrm object from the window context.
 * Tries parent window first (web resource in iframe), then current window.
 */
export function getXrm(): IXrmContext | null {
  try {
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.WebApi && parentXrm?.Utility) {
      return parentXrm as IXrmContext;
    }

    const windowXrm = (window as any)?.Xrm;
    if (windowXrm?.WebApi && windowXrm?.Utility) {
      return windowXrm as IXrmContext;
    }

    return null;
  } catch {
    return null;
  }
}
