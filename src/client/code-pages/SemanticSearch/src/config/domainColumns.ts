/**
 * Domain-specific grid column configurations
 *
 * Defines columns for each search domain (Documents, Matters, Projects, Invoices).
 * Column field names aligned with BFF API response types and records-index spike findings.
 *
 * Fields marked "enriched" require post-search Dataverse lookup (not in index).
 * Fields marked "indexed" are available directly from Azure AI Search results.
 *
 * @see notes/spikes/records-index-coverage.md — field availability per domain
 * @see spec.md Section 6.4 — column specifications
 */

import type { SearchDomain, GridColumnDef } from "../types";

// =============================================
// Custom cell renderers
// =============================================

/** Format similarity/confidence score as percentage string. */
function renderSimilarity(value: unknown): string {
    const score = typeof value === "number" ? value : 0;
    return `${Math.round(score * 100)}%`;
}

/** Format ISO date string as localized short date. */
function renderDate(value: unknown): string {
    if (!value || typeof value !== "string") return "";
    try {
        return new Date(value).toLocaleDateString(undefined, {
            year: "numeric",
            month: "short",
            day: "numeric",
        });
    } catch {
        return "";
    }
}

/** Format array values as comma-separated string. */
function renderArray(value: unknown): string {
    if (Array.isArray(value)) return value.join(", ");
    return typeof value === "string" ? value : "";
}

/** Format currency value. */
function renderCurrency(value: unknown): string {
    if (typeof value !== "number") return "";
    return new Intl.NumberFormat(undefined, {
        style: "currency",
        currency: "USD",
        minimumFractionDigits: 2,
    }).format(value);
}

// =============================================
// Column definitions per domain
// =============================================

/** Documents domain columns (indexed: name, type, fileType, parentEntity, dates) */
const DOCUMENT_COLUMNS: GridColumnDef[] = [
    {
        key: "name",
        label: "Document",
        width: 400,
        minWidth: 250,
        sortable: true,
    },
    {
        key: "combinedScore",
        label: "Similarity",
        width: 100,
        minWidth: 80,
        sortable: true,
        render: renderSimilarity,
    },
    {
        key: "documentType",
        label: "Type",
        width: 120,
        minWidth: 80,
        sortable: true,
    },
    {
        key: "fileType",
        label: "File Type",
        width: 90,
        minWidth: 70,
        sortable: true,
    },
    {
        key: "parentEntityName",
        label: "Parent Entity",
        width: 180,
        minWidth: 120,
        sortable: true,
    },
    {
        key: "updatedAt",
        label: "Modified",
        width: 120,
        minWidth: 100,
        sortable: true,
        render: renderDate,
    },
];

/**
 * Matters domain columns.
 * - recordName, confidenceScore, referenceNumbers, modifiedAt: indexed (available from search)
 * - matterType, practiceArea: enriched (requires Dataverse lookup — not in records index)
 * - organizations: schema available but currently empty in pipeline
 */
const MATTER_COLUMNS: GridColumnDef[] = [
    {
        key: "recordName",
        label: "Matter Name",
        width: 240,
        minWidth: 160,
        sortable: true,
    },
    {
        key: "confidenceScore",
        label: "Similarity",
        width: 100,
        minWidth: 80,
        sortable: true,
        render: renderSimilarity,
    },
    {
        key: "referenceNumbers",
        label: "Matter Number",
        width: 130,
        minWidth: 100,
        sortable: false, // referenceNumbers is an array
        render: (value: unknown) => {
            if (Array.isArray(value)) return value[0] ?? "";
            return typeof value === "string" ? value : "";
        },
    },
    {
        // enriched — not in records index, requires Dataverse lookup
        key: "matterType",
        label: "Matter Type",
        width: 130,
        minWidth: 90,
        sortable: true,
    },
    {
        // enriched — not in records index, requires Dataverse lookup
        key: "practiceArea",
        label: "Practice Area",
        width: 130,
        minWidth: 90,
        sortable: true,
    },
    {
        // schema available in index but pipeline populates as empty []
        key: "organizations",
        label: "Organizations",
        width: 180,
        minWidth: 120,
        sortable: false,
        render: renderArray,
    },
    {
        key: "modifiedAt",
        label: "Modified",
        width: 120,
        minWidth: 100,
        sortable: true,
        render: renderDate,
    },
];

/**
 * Projects domain columns.
 * - recordName, confidenceScore, modifiedAt: indexed
 * - status, parentMatter: enriched (requires Dataverse lookup)
 */
const PROJECT_COLUMNS: GridColumnDef[] = [
    {
        key: "recordName",
        label: "Project Name",
        width: 260,
        minWidth: 180,
        sortable: true,
    },
    {
        key: "confidenceScore",
        label: "Similarity",
        width: 100,
        minWidth: 80,
        sortable: true,
        render: renderSimilarity,
    },
    {
        // enriched — not in records index
        key: "status",
        label: "Status",
        width: 120,
        minWidth: 80,
        sortable: true,
    },
    {
        // enriched — not in records index
        key: "parentMatter",
        label: "Parent Matter",
        width: 200,
        minWidth: 140,
        sortable: true,
    },
    {
        key: "modifiedAt",
        label: "Modified",
        width: 120,
        minWidth: 100,
        sortable: true,
        render: renderDate,
    },
];

/**
 * Invoices domain columns.
 * - recordName, confidenceScore, referenceNumbers, modifiedAt: indexed
 * - amount, vendor, parentMatter, invoiceDate: enriched (requires Dataverse lookup)
 */
const INVOICE_COLUMNS: GridColumnDef[] = [
    {
        key: "recordName",
        label: "Invoice",
        width: 220,
        minWidth: 160,
        sortable: true,
    },
    {
        key: "confidenceScore",
        label: "Similarity",
        width: 100,
        minWidth: 80,
        sortable: true,
        render: renderSimilarity,
    },
    {
        // enriched — not in records index
        key: "amount",
        label: "Amount",
        width: 120,
        minWidth: 90,
        sortable: true,
        render: renderCurrency,
    },
    {
        // enriched — not in records index
        key: "vendor",
        label: "Vendor",
        width: 160,
        minWidth: 100,
        sortable: true,
    },
    {
        // enriched — not in records index
        key: "parentMatter",
        label: "Parent Matter",
        width: 180,
        minWidth: 120,
        sortable: true,
    },
    {
        key: "modifiedAt",
        label: "Date",
        width: 120,
        minWidth: 100,
        sortable: true,
        render: renderDate,
    },
];

// =============================================
// Public API
// =============================================

const DOMAIN_COLUMNS: Record<SearchDomain, GridColumnDef[]> = {
    documents: DOCUMENT_COLUMNS,
    matters: MATTER_COLUMNS,
    projects: PROJECT_COLUMNS,
    invoices: INVOICE_COLUMNS,
};

/**
 * Get grid column definitions for the specified search domain.
 */
export function getColumnsForDomain(domain: SearchDomain): GridColumnDef[] {
    return DOMAIN_COLUMNS[domain];
}

export default getColumnsForDomain;
