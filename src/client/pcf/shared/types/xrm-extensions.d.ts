/**
 * Extended type declarations for PCF controls
 *
 * These types cover common PCF context extensions used across
 * Spaarke PCF controls. They provide typed alternatives to `any` casts
 * for runtime-available properties not in @types/powerapps-component-framework.
 *
 * NOTE: Xrm global types (XrmGlobalObject, Window.Xrm) are NOT included here
 * because they conflict with @types/powerapps-component-framework and per-control
 * XrmTypes.d.ts files. Use eslint-disable comments for unavoidable Xrm `any` casts.
 */

/** Extended PCF context mode with contextInfo (available at runtime but not in types) */
interface PcfContextModeExtended {
  contextInfo?: {
    entityId?: string;
    entityTypeName?: string;
    entityRecordName?: string;
  };
  isAuthoringMode?: boolean;
}

/** Extended PCF context with fluentDesignLanguage (available at runtime but not in types) */
interface PcfContextExtended<T> extends ComponentFramework.Context<T> {
  mode: ComponentFramework.Context<T>['mode'] & PcfContextModeExtended;
  fluentDesignLanguage?: {
    isDarkTheme: boolean;
    tokenTheme: Record<string, string>;
  };
  page?: {
    getClientUrl?: () => string;
  };
}

/** Grid customizer cell renderer props (from Power Apps Grid) */
interface GridCellRendererProps {
  columnInfo?: {
    name: string;
    displayName?: string;
    dataType?: string;
  };
  colDefs?: { name: string }[];
  columnIndex?: number;
  rowData?: Record<string, unknown>;
  value?: unknown;
  [key: string]: unknown;
}
