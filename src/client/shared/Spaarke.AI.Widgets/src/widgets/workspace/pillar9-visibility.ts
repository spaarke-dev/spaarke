/**
 * @spaarke/ai-widgets — Pillar 9 widget-visibility derivations (task 073, D-C-28)
 *
 * One pure derivation per `WorkspaceTabWidgetType` category — Summary,
 * DocumentViewer, Dashboard, Table. Each function takes the live tab's
 * `widgetData` payload and returns the matching `SerializedWidgetState`
 * variant (or `null` to opt out for this invocation).
 *
 * These four derivations are wired into the `WorkspaceWidgetRegistry` via
 * each widget's `register-*.ts` file (per task 072 / D-C-27 extension). The
 * registrations carry the derivation as the 4th argument of
 * `registerWorkspaceWidget`; the Pillar 9 prompt builder (task 074) reads
 * them back via `getWorkspaceWidgetVisibleStateFn(type)`.
 *
 * ## Category → concrete widget mapping (Pillar 9 design)
 *
 * The `WorkspaceTabWidgetType` union (4 variants: Summary, DocumentViewer,
 * Dashboard, Table) is intentionally DISTINCT from the registry's widget-type
 * strings (16+ entries like `'document-viewer'`, `'workspace'`,
 * `'documents-list'`, `'structured-output-stream'`, etc.) — see
 * `WorkspaceTab.ts` design notes. The four categories drive **agent-visible
 * state shape**; registered widgets MAP to one of these categories. The
 * mapping applied here:
 *
 *   - **Summary** category    → `StructuredOutputStreamWidget`
 *     (renders summarize/TL;DR text — the only widget that produces a
 *     summary-flavored payload in R6 baseline).
 *   - **DocumentViewer**       → `DocumentViewerWidget`
 *     (file preview + selection — the canonical R4 task 042 widget).
 *   - **Dashboard**            → `WorkspaceLayoutWidget`
 *     (embedded `LegalWorkspaceApp` — single point of contact with the
 *     LegalWorkspace dashboard surface per OC-R4-07).
 *   - **Table**                → `DataverseEntityViewWidget`
 *     (DataGrid framework — Documents / Matters / Projects / Invoices /
 *     WorkAssignments share this same component via per-registration
 *     `configId`; all five table-shaped registrations reuse the same
 *     derivation).
 *
 * Future Pillar 9 mappings: when a new widget contributes to one of these
 * four categories, extend its own `register-*.ts` to attach the matching
 * derivation. When a brand-new category is needed, BOTH `WorkspaceTab.ts`
 * AND `SerializedWidgetState.ts` AND this file must be updated in a
 * coordinated change (the `_DiscriminatorAlignment` type-level guard in
 * `SerializedWidgetState.ts` will surface the drift at compile time).
 *
 * ## Privacy defaults (ADR-015 binding)
 *
 * Each derivation:
 *   - Returns `null` (opt out) when the input payload is missing or fails
 *     structural narrowing. Pillar 9's prompt builder treats `null` as
 *     "contribute nothing to the prompt for this tab" — equivalent to the
 *     widget never opting in.
 *   - Favors metadata over content. Per FR-57 / CLAUDE.md §9:
 *     • Summary       — exposes agent-derived `summary` + `tldr` text (already
 *       safety-pipeline-governed at generation time per NFR-13). Withholds
 *       raw widget body when no agent-derived content is yet available.
 *     • DocumentViewer — exposes file metadata (`filename`, `mimeType`,
 *       `sizeBytes`) + selection STATE (`hasSelection`). The optional
 *       `selectionText` is the ONLY content-bearing field and is **capped at
 *       200 characters** when present (task 073 acceptance).
 *     • Dashboard      — exposes ONLY `dashboardName` + optional
 *       `lastViewedSection`. DELIBERATELY OMITS section payloads / chart
 *       data (token economy + privacy — section payloads frequently contain
 *       PII like matter rosters / financial data the user did NOT consent to
 *       share with the LLM by mounting the layout).
 *     • Table          — exposes structural state (`rowCount`, `sortColumn`,
 *       `filteredColumns`, `selectedRows` AS A COUNT). NEVER row payloads or
 *       row IDs — counts only per `SerializedTableState` contract.
 *
 * ## Self-limiting (per FR-55 token budget)
 *
 * Each derivation self-limits long fields to stay within Pillar 9's ~200-
 * tokens-per-tab budget (the prompt builder enforces a hard cap, but widgets
 * should pre-trim):
 *   - `summary`        — capped at 500 chars (~125 tokens for English text)
 *   - `tldr`           — capped at 5 entries × 200 chars each
 *   - `selectionText`  — capped at 200 chars (per FR-57 acceptance + task POML)
 *
 * @see SerializedWidgetState.ts — per-variant rationale + privacy classes
 * @see WorkspaceTab.ts          — discriminator union + widget-data shapes
 * @see FR-55, FR-56, FR-57      — visibility contract per R6 spec
 * @see ADR-015                  — AI data governance (data minimization)
 */

import type { RegistryGetAgentVisibleState } from '../../registry/WorkspaceWidgetRegistry';
import type {
  SerializedDashboardState,
  SerializedDocumentViewerState,
  SerializedSummaryState,
  SerializedTableState,
} from '../../types/SerializedWidgetState';

// ---------------------------------------------------------------------------
// Self-limiting caps (per FR-55 + FR-57 + ADR-015)
// ---------------------------------------------------------------------------

/** Per FR-57 acceptance + task 073 POML: cap selectionText at 200 chars. */
export const SELECTION_TEXT_CAP_CHARS = 200;

/** Self-limit for Summary `summary` field (~125 tokens for English text). */
export const SUMMARY_TEXT_CAP_CHARS = 500;

/** Self-limit for Summary `tldr` list length (max 5 bullets per FR-55 budget). */
export const TLDR_MAX_BULLETS = 5;

/** Self-limit for each Summary `tldr` bullet's character length. */
export const TLDR_BULLET_CAP_CHARS = 200;

// ---------------------------------------------------------------------------
// Helpers — narrow + truncate
// ---------------------------------------------------------------------------

function isObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isString(value: unknown): value is string {
  return typeof value === 'string';
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isBoolean(value: unknown): value is boolean {
  return typeof value === 'boolean';
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(isString);
}

/** Truncate `value` to `cap` characters with no ellipsis suffix (we surface the truncation by length). */
function truncate(value: string, cap: number): string {
  return value.length > cap ? value.slice(0, cap) : value;
}

// ---------------------------------------------------------------------------
// Summary derivation — StructuredOutputStreamWidget
//
// The StructuredOutputStream widget produces summarize-flavored output via
// the `prefilledFields` map (keyed by schema field path). The summarize
// schema (per `SUMMARIZE_SCHEMA` / `SUM_CHAT_OUTPUT_SCHEMA`) exposes:
//   - `tldr`     — `string[]` of bullet lines
//   - `summary`  — `string` markdown body
// Streaming mode populates these fields PROGRESSIVELY into `prefilledFields`
// after streaming-complete; static mode receives them pre-populated. Either
// way we read from `prefilledFields[path]`.
//
// hasUserEdits: this widget is read-only (no inline editing in R6), so the
// flag is always false. Q8 conflict resolution applies to the editable
// Summary tabs whose widget data lives elsewhere (e.g. AnalysisEditor
// future migration); StructuredOutputStream does not contribute that signal.
// ---------------------------------------------------------------------------

/**
 * Derive `SerializedSummaryState` from `StructuredOutputStreamWidgetData`.
 *
 * - Reads `prefilledFields.summary` and `prefilledFields.tldr` (the two
 *   summarize-schema fields). Both can be empty during early streaming.
 * - `tldr` may arrive as a JSON-stringified array, an actual array, or a
 *   newline-joined string per the StructuredOutputStream renderer's three
 *   accept-paths. We narrow to a `string[]` for the agent prompt.
 * - Returns `null` if no summarize content exists yet (privacy default — no
 *   point contributing an empty summary).
 *
 * Self-limits per FR-55:
 *   - `summary` capped at 500 chars
 *   - `tldr` capped at 5 entries × 200 chars each
 *
 * @see SerializedSummaryState — exact output shape
 * @see FR-57 — `{ widgetType: "Summary", summary, tldr, hasUserEdits }`
 */
export const summaryWidgetVisibility: RegistryGetAgentVisibleState = (
  widgetData: unknown
): SerializedSummaryState | null => {
  if (!isObject(widgetData)) return null;

  const prefilled = widgetData.prefilledFields;
  if (!isObject(prefilled)) return null;

  // `summary` — read string field; default empty when not yet populated.
  const rawSummary = prefilled.summary;
  const summary = isString(rawSummary) ? truncate(rawSummary, SUMMARY_TEXT_CAP_CHARS) : '';

  // `tldr` — three accept-paths (array, JSON-stringified array, or
  // newline-joined string). Normalize to `string[]` defensively.
  const rawTldr = prefilled.tldr;
  let tldr: string[] = [];
  if (isStringArray(rawTldr)) {
    tldr = rawTldr;
  } else if (isString(rawTldr) && rawTldr.length > 0) {
    const trimmed = rawTldr.trim();
    if (trimmed.startsWith('[')) {
      try {
        const parsed = JSON.parse(trimmed);
        if (isStringArray(parsed)) tldr = parsed;
      } catch {
        // Fall through to newline-split fallback below.
      }
    }
    if (tldr.length === 0) {
      tldr = trimmed
        .split(/\r?\n/)
        .map(s => s.trim())
        .filter(s => s.length > 0);
    }
  }
  // Self-limit: max 5 bullets × 200 chars each.
  tldr = tldr.slice(0, TLDR_MAX_BULLETS).map(s => truncate(s, TLDR_BULLET_CAP_CHARS));

  // Opt out when nothing meaningful would land in the prompt.
  if (summary.length === 0 && tldr.length === 0) return null;

  // StructuredOutputStream is read-only in R6; the hasUserEdits signal
  // belongs to editable summary tabs (future AnalysisEditor migration).
  // We surface `false` to keep the FR-57 shape conformant; the field is
  // also tolerant of widget-level `hasUserEdits` if upstream attaches it.
  const hasUserEdits = isBoolean(widgetData.hasUserEdits) ? widgetData.hasUserEdits : false;

  return {
    widgetType: 'Summary',
    summary,
    tldr,
    hasUserEdits,
  };
};

// ---------------------------------------------------------------------------
// DocumentViewer derivation — DocumentViewerWidget
//
// The widget data carries R4 fields (filename, contentType, sizeBytes,
// textContent) + R5 additions (documentId, previewUrl, fetchPreviewUrl).
//
// hasSelection / selectionText — the R6 DocumentViewerWidget's underlying
// RichFilePreview renderer surfaces text selection state through the future
// Pillar 9 selection feedback path. For the registry-level derivation we
// honor the live `widgetData.hasSelection` + `widgetData.selectionText`
// fields when present (the canonical task-050 DocumentViewerTabWidgetData
// shape exposes them); when absent we default `hasSelection: false`.
//
// selectionText is CAPPED at 200 chars per FR-57 acceptance + task 073 POML.
// ---------------------------------------------------------------------------

/**
 * Derive `SerializedDocumentViewerState` from `DocumentViewerWidgetData`
 * (or the canonical `DocumentViewerTabWidgetData` shape — both share the
 * relevant fields).
 *
 * - Reads `filename`, `contentType` / `mimeType`, `sizeBytes`.
 * - When `hasSelection === true` AND `selectionText` is a non-empty string,
 *   emits `selectionText` TRUNCATED to {@link SELECTION_TEXT_CAP_CHARS}
 *   (200 chars per FR-57 acceptance).
 * - Returns `null` only when no `filename` is present (cannot construct a
 *   meaningful agent-visible record without a file identifier).
 *
 * @see SerializedDocumentViewerState — exact output shape
 * @see FR-57 — `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }`
 */
export const documentViewerWidgetVisibility: RegistryGetAgentVisibleState = (
  widgetData: unknown
): SerializedDocumentViewerState | null => {
  if (!isObject(widgetData)) return null;

  const filenameRaw = widgetData.filename;
  if (!isString(filenameRaw) || filenameRaw.length === 0) return null;

  // MIME comes through under `mimeType` (task-050 canonical shape) OR
  // `contentType` (R4 DocumentViewerWidgetData). Prefer the canonical name.
  const mimeTypeRaw = isString(widgetData.mimeType)
    ? widgetData.mimeType
    : isString(widgetData.contentType)
      ? widgetData.contentType
      : '';

  const sizeBytes = isNumber(widgetData.sizeBytes) ? widgetData.sizeBytes : 0;

  const hasSelection = isBoolean(widgetData.hasSelection) ? widgetData.hasSelection : false;

  const result: SerializedDocumentViewerState = {
    widgetType: 'DocumentViewer',
    filename: filenameRaw,
    mimeType: mimeTypeRaw,
    sizeBytes,
    hasSelection,
  };

  // Only emit selectionText when hasSelection is true AND the string is
  // non-empty (privacy default — no point exposing an empty selection).
  // Cap at 200 chars per FR-57.
  if (hasSelection) {
    const selectionTextRaw = widgetData.selectionText;
    if (isString(selectionTextRaw) && selectionTextRaw.length > 0) {
      result.selectionText = truncate(selectionTextRaw, SELECTION_TEXT_CAP_CHARS);
    }
  }

  return result;
};

// ---------------------------------------------------------------------------
// Dashboard derivation — WorkspaceLayoutWidget
//
// The widget data is `{ layoutId, layoutName }` (see WorkspaceLayoutWidgetData).
// Per FR-57 the agent receives only `dashboardName` + optional
// `lastViewedSection` — NEVER section payloads or chart data.
//
// `lastViewedSection` is sourced from `widgetData.lastViewedSection` when
// the host updates it (e.g. on user interaction). R6 baseline does not yet
// push this signal, so the field is typically absent — that's by design
// per the canonical `DashboardTabWidgetData` shape ("Optional because the
// user may not have interacted with any section yet").
// ---------------------------------------------------------------------------

/**
 * Derive `SerializedDashboardState` from `WorkspaceLayoutWidgetData` (or the
 * canonical `DashboardTabWidgetData` shape — both expose `layoutName` /
 * `dashboardName` for the display name).
 *
 * - Reads `dashboardName` (canonical) or `layoutName` (R4 widget data).
 * - Reads `lastViewedSection` when present (deterministic section id only).
 * - **NEVER reads chart data, section payloads, or any aggregate values** —
 *   per Pillar 9 design + ADR-015. The agent receives navigational context
 *   only.
 * - Returns `null` when no dashboard name is present (cannot construct a
 *   meaningful agent-visible record without a dashboard identifier).
 *
 * @see SerializedDashboardState — exact output shape
 * @see FR-57 — `{ widgetType, dashboardName, lastViewedSection }` (NO chart data)
 */
export const dashboardWidgetVisibility: RegistryGetAgentVisibleState = (
  widgetData: unknown
): SerializedDashboardState | null => {
  if (!isObject(widgetData)) return null;

  // Prefer canonical name; fall back to R4 WorkspaceLayoutWidgetData's `layoutName`.
  const dashboardNameRaw = isString(widgetData.dashboardName)
    ? widgetData.dashboardName
    : isString(widgetData.layoutName)
      ? widgetData.layoutName
      : '';
  if (dashboardNameRaw.length === 0) return null;

  const lastViewedSectionRaw = widgetData.lastViewedSection;
  const lastViewedSection = isString(lastViewedSectionRaw) && lastViewedSectionRaw.length > 0
    ? lastViewedSectionRaw
    : undefined;

  const result: SerializedDashboardState = {
    widgetType: 'Dashboard',
    dashboardName: dashboardNameRaw,
  };
  if (lastViewedSection !== undefined) result.lastViewedSection = lastViewedSection;
  return result;
};

// ---------------------------------------------------------------------------
// Table derivation — DataverseEntityViewWidget
//
// The widget data is `{ configId, title? }` at the registration boundary,
// but the live tab's widgetData (matching the canonical `TableTabWidgetData`
// shape from task 050) carries `{ rowCount, sortColumn?, filteredColumns,
// selectedRows[] }`. We read those structural fields.
//
// IMPORTANT (FR-57 + SerializedTableState contract):
//   - `selectedRows` is a COUNT (number), NOT a list of row IDs and NOT row
//     content. Per the canonical SerializedTableState JSDoc: "Surfacing the
//     count lets the agent reason about the user's working set size ('you
//     have 3 documents selected — would you like to summarize all 3?')
//     without exposing the row identities (some matter ids / document ids
//     encode case context the user did not consent to share with the LLM)."
//   - We accept the canonical task-050 shape (`selectedRows: string[]`) and
//     CONVERT to its cardinality. We NEVER pass row IDs through.
//   - filteredColumns are column IDs (Class 1 identifiers per ADR-015 — OK).
//
// All 5 system table widgets (documents-list, matters-list, projects-list,
// invoices-list, work-assignments-list) share this single derivation.
// ---------------------------------------------------------------------------

/**
 * Derive `SerializedTableState` from `TableTabWidgetData` (or the
 * widget-level data shape the DataverseEntityViewWidget surfaces).
 *
 * - Reads `rowCount`, `sortColumn?`, `filteredColumns?`.
 * - **Converts `selectedRows` array → COUNT (number)** per the canonical
 *   `SerializedTableState` contract. Row IDs are NEVER passed through.
 * - Omits structural fields when their "no current state" sentinel applies
 *   (e.g. `sortColumn === undefined` when unsorted; `filteredColumns ===
 *   undefined` when no filters; `selectedRows === undefined` when no
 *   selection).
 * - Returns `null` when `rowCount` is absent (cannot construct a meaningful
 *   agent-visible record without a count).
 *
 * @see SerializedTableState — exact output shape (selectedRows = COUNT)
 * @see FR-57 — `{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows }`
 * @see ADR-015 — data minimization: counts OK; row IDs withheld
 */
export const tableWidgetVisibility: RegistryGetAgentVisibleState = (
  widgetData: unknown
): SerializedTableState | null => {
  if (!isObject(widgetData)) return null;

  if (!isNumber(widgetData.rowCount)) return null;
  const rowCount = widgetData.rowCount;

  const result: SerializedTableState = {
    widgetType: 'Table',
    rowCount,
  };

  if (isString(widgetData.sortColumn) && widgetData.sortColumn.length > 0) {
    result.sortColumn = widgetData.sortColumn;
  }

  if (isStringArray(widgetData.filteredColumns) && widgetData.filteredColumns.length > 0) {
    result.filteredColumns = widgetData.filteredColumns;
  }

  // Convert row-id array → count per SerializedTableState privacy contract.
  // We NEVER pass row IDs through; the agent receives cardinality only.
  if (Array.isArray(widgetData.selectedRows) && widgetData.selectedRows.length > 0) {
    result.selectedRows = widgetData.selectedRows.length;
  }

  return result;
};
