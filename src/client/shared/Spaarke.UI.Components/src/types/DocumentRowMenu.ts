/**
 * DocumentRowMenu.ts
 *
 * Shared types for the {@link DocumentRowMenu} shared component
 * (see `../components/DocumentRowMenu.tsx`).
 *
 * Per spec FR-SC-02 + FR-DOC-01:
 * - 12 leaf actions + 2 dividers between groups
 * - camelCase action codes for TypeScript-friendly switches in consumer code
 * - generic target shape (consumers may pass richer objects; the menu only
 *   needs identifying info)
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9), ADR-022
 * (React 16/17 compatibility).
 */

/**
 * Document identity passed to {@link DocumentRowMenu}. Consumers may pass
 * richer objects — this is the minimum the menu needs.
 *
 * NOTE: kept intentionally minimal so the menu is not coupled to any specific
 * dataset/PCF shape (Semantic Search, Documents Relationship Viewer, etc.).
 */
export interface IDocumentRowMenuTarget {
  /** Dataverse `sprk_documentid` or other stable identifier. */
  id: string;
  /** Human-readable document name (used in trigger `aria-label`). */
  name: string;
  /** Optional document type (consumers may use for additional context). */
  documentType?: string;
  /** Free-form additional properties consumers may attach. */
  [k: string]: unknown;
}

/**
 * Union of every action the {@link DocumentRowMenu} can dispatch.
 *
 * Order matches spec FR-DOC-01:
 *   Group A: Preview · AI summary · Open file · Find similar
 *   --- divider ---
 *   Group B: Download · Copy link · Email · Open record
 *   --- divider ---
 *   Group C: Toggle workspace · Pin to top · Rename · Delete
 *
 * 12 leaf actions total.
 */
export type DocumentRowAction =
  | 'preview'
  | 'aiSummary'
  | 'openFile'
  | 'findSimilar'
  | 'download'
  | 'copyLink'
  | 'email'
  | 'openRecord'
  | 'toggleWorkspace'
  | 'pinToTop'
  | 'rename'
  | 'delete';
