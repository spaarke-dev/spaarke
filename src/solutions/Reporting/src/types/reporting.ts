/**
 * reporting.ts
 * Shared type definitions for the Reporting module.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode
 */

// ---------------------------------------------------------------------------
// Privilege types
// ---------------------------------------------------------------------------

/** The user's privilege level within the Reporting module. */
export type UserPrivilege = "Viewer" | "Author" | "Admin";

// ---------------------------------------------------------------------------
// Report mode types
// ---------------------------------------------------------------------------

/**
 * Power BI report render mode.
 * - "view": read-only with filter pane
 * - "edit": full authoring toolbar with visuals and formatting
 */
export type ReportMode = "view" | "edit";

// ---------------------------------------------------------------------------
// Export types
// ---------------------------------------------------------------------------

/** Supported export formats for Power BI reports. */
export type ExportFormat = "PDF" | "PPTX";

/**
 * Lifecycle status of a report export operation.
 * Maps to PBI ExportToFile states returned by the BFF.
 */
export type ExportStatus = "pending" | "running" | "completed" | "failed";
