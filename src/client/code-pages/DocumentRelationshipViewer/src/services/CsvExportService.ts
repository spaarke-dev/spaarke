/**
 * CsvExportService — Export document relationship grid data to CSV
 *
 * Generates a UTF-8 BOM CSV file for Excel compatibility and triggers
 * a browser download via a hidden anchor element.
 */

import type { DocumentNodeData } from "../types/graph";

interface ExportableRow {
  id: string;
  data: DocumentNodeData;
}

const CSV_HEADERS = [
  "Document Name",
  "Type",
  "Similarity %",
  "Relationship Type",
  "Parent Entity",
  "Modified Date",
] as const;

function escapeCell(value: string): string {
  if (
    value.includes('"') ||
    value.includes(",") ||
    value.includes("\n") ||
    value.includes("\r")
  ) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

function formatDate(isoDate: string | undefined): string {
  if (!isoDate) return "";
  try {
    return new Date(isoDate).toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
    });
  } catch {
    return "";
  }
}

function formatSimilarity(
  similarity: number | undefined,
  isSource: boolean | undefined,
): string {
  if (isSource) return "";
  if (similarity == null) return "";
  return `${Math.round(similarity * 100)}%`;
}

function formatRelationshipType(row: DocumentNodeData): string {
  if (row.isSource) return "Source";
  if (row.relationshipTypes && row.relationshipTypes.length > 0) {
    return row.relationshipTypes.map((r) => r.label).join("; ");
  }
  return row.relationshipLabel ?? "";
}

function rowToCsv(row: ExportableRow): string {
  const d = row.data;
  const cells = [
    escapeCell(d.name),
    escapeCell(d.documentType ?? d.fileType?.toUpperCase() ?? ""),
    escapeCell(formatSimilarity(d.similarity, d.isSource)),
    escapeCell(formatRelationshipType(d)),
    escapeCell(d.parentEntityName ?? ""),
    escapeCell(formatDate(d.modifiedOn)),
  ];
  return cells.join(",");
}

function formatDateForFilename(): string {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

function sanitizeFilename(name: string): string {
  return name
    .replace(/[^a-zA-Z0-9_\-. ]/g, "")
    .replace(/\s+/g, "-")
    .toLowerCase();
}

/**
 * Export document relationship rows to a CSV file and trigger download.
 *
 * @param rows - Grid rows to export (already filtered by search)
 * @param sourceDocumentName - Name of the source document for the filename
 */
export function exportToCsv(
  rows: ExportableRow[],
  sourceDocumentName: string,
): void {
  const BOM = "\uFEFF";
  const headerLine = CSV_HEADERS.join(",");
  const dataLines = rows.map(rowToCsv);
  const csvContent = BOM + [headerLine, ...dataLines].join("\r\n") + "\r\n";

  const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);

  const safeName = sanitizeFilename(sourceDocumentName) || "documents";
  const filename = `related-documents-${safeName}-${formatDateForFilename()}.csv`;

  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.style.display = "none";
  document.body.appendChild(anchor);
  anchor.click();

  // Cleanup
  setTimeout(() => {
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }, 100);
}
