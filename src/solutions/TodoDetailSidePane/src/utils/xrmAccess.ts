/**
 * xrmAccess — Utility for accessing Xrm context from within a side pane iframe.
 *
 * Side pane content runs inside an iframe. The parent Dataverse page provides
 * Xrm on window.parent.Xrm. We try parent first, then current window.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

export interface IXrmWebApi {
  retrieveRecord(entityName: string, id: string, options?: string): Promise<any>;
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: any[] }>;
  updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<any>;
}

export interface IXrmNavigation {
  openForm(options: Record<string, unknown>): Promise<any>;
  navigateTo(pageInput: Record<string, unknown>, navigationOptions?: Record<string, unknown>): Promise<any>;
}

export interface IXrm {
  WebApi: IXrmWebApi;
  Navigation: IXrmNavigation;
}

export function getXrm(): IXrm | null {
  const parentXrm = (window.parent as any)?.Xrm;
  if (parentXrm?.WebApi) return parentXrm as IXrm;

  const windowXrm = (window as any)?.Xrm;
  if (windowXrm?.WebApi) return windowXrm as IXrm;

  return null;
}

export function getXrmWebApi(): IXrmWebApi | null {
  return getXrm()?.WebApi ?? null;
}
