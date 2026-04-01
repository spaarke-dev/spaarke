/**
 * types/index.ts
 * Shared type definitions for the Reporting Code Page.
 *
 * Re-exports from reporting.ts and adds report catalog types used by
 * ReportDropdown, useReportCatalog, and the parent App component.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode
 */

// Re-export domain types from reporting.ts
export type { UserPrivilege, ReportMode, ExportFormat, ExportStatus } from "./reporting";

// ---------------------------------------------------------------------------
// Report catalog types
// ---------------------------------------------------------------------------

/**
 * Categories for grouping reports in the dropdown selector.
 * Ordered to match the expected display order (Financial first, Custom last).
 */
export type ReportCategory =
  | "Financial"
  | "Operational"
  | "Compliance"
  | "Documents"
  | "Custom";

/**
 * A single report entry from the BFF report catalog endpoint.
 * Maps to the sprk_report Dataverse entity.
 *
 * @see GET /api/reporting/reports
 */
export interface ReportCatalogItem {
  /** Unique report identifier (sprk_report entity ID). */
  id: string;
  /** Display name for the report. */
  name: string;
  /** Direct Power BI embed URL for this report. */
  embedUrl: string;
  /** Power BI dataset ID linked to this report. */
  datasetId: string;
  /** Grouping category shown in the dropdown. */
  category: ReportCategory;
  /** True when this report was created by an end user (not an admin template). */
  isCustom: boolean;
}

// ---------------------------------------------------------------------------
// Dropdown prop types
// ---------------------------------------------------------------------------

/**
 * Props for the ReportDropdown component.
 */
export interface ReportDropdownProps {
  /** Full report catalog fetched from BFF. */
  reports: ReportCatalogItem[];
  /** The currently selected report ID, or null if nothing is selected. */
  selectedReportId: string | null;
  /** Called when the user selects a different report. */
  onReportSelect: (reportId: string) => void;
  /** When true, shows a loading spinner inside the dropdown trigger. */
  loading?: boolean;
}
