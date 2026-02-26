/**
 * Test Fixtures - Reusable test data builders for AnalysisWorkspace tests.
 *
 * Follows the test data builder pattern from testing.md constraints.
 * All fixtures use deterministic values for reproducible tests.
 */

import type { AnalysisRecord, DocumentMetadata, AnalysisError } from "../../types";

// ---------------------------------------------------------------------------
// Analysis Record Fixtures
// ---------------------------------------------------------------------------

export function buildAnalysisRecord(
    overrides: Partial<AnalysisRecord> = {}
): AnalysisRecord {
    return {
        id: "analysis-001-test",
        title: "Contract Compliance Analysis",
        content: "<p>Analysis output content</p>",
        status: "completed",
        sourceDocumentId: "doc-001-test",
        createdOn: "2026-01-15T10:00:00.000Z",
        modifiedOn: "2026-01-15T11:30:00.000Z",
        playbookId: "playbook-default",
        createdBy: "Test User",
        ...overrides,
    };
}

export function buildDraftAnalysis(
    overrides: Partial<AnalysisRecord> = {}
): AnalysisRecord {
    return buildAnalysisRecord({
        id: "analysis-draft-001",
        title: "Draft Analysis",
        content: "",
        status: "draft",
        ...overrides,
    });
}

export function buildInProgressAnalysis(
    overrides: Partial<AnalysisRecord> = {}
): AnalysisRecord {
    return buildAnalysisRecord({
        id: "analysis-inprogress-001",
        title: "In-Progress Analysis",
        content: "<p>Partial content...</p>",
        status: "in_progress",
        ...overrides,
    });
}

// ---------------------------------------------------------------------------
// Document Metadata Fixtures
// ---------------------------------------------------------------------------

export function buildDocumentMetadata(
    overrides: Partial<DocumentMetadata> = {}
): DocumentMetadata {
    return {
        id: "doc-001-test",
        name: "Contract_v2.pdf",
        mimeType: "application/pdf",
        size: 1_250_000,
        viewUrl: "https://spaarkedev1.sharepoint.com/embed/doc-001-test",
        fileExtension: "pdf",
        containerId: "container-test-001",
        ...overrides,
    };
}

export function buildWordDocument(
    overrides: Partial<DocumentMetadata> = {}
): DocumentMetadata {
    return buildDocumentMetadata({
        id: "doc-word-001",
        name: "Agreement_Final.docx",
        mimeType:
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        size: 450_000,
        viewUrl: "https://spaarkedev1.sharepoint.com/embed/doc-word-001",
        fileExtension: "docx",
        ...overrides,
    });
}

// ---------------------------------------------------------------------------
// Error Fixtures
// ---------------------------------------------------------------------------

export function buildAnalysisError(
    overrides: Partial<AnalysisError> = {}
): AnalysisError {
    return {
        errorCode: "ANALYSIS_NOT_FOUND",
        message: "The requested analysis was not found.",
        detail: "Analysis with ID 'analysis-missing' does not exist.",
        correlationId: "corr-test-001",
        status: 404,
        ...overrides,
    };
}

export function buildNetworkError(): AnalysisError {
    return {
        errorCode: "NETWORK_ERROR",
        message: "Failed to fetch",
        status: 0,
    };
}

export function buildUnauthorizedError(): AnalysisError {
    return {
        errorCode: "HTTP_401",
        message: "Unauthorized",
        detail: "The access token has expired.",
        status: 401,
    };
}

// ---------------------------------------------------------------------------
// Common Constants
// ---------------------------------------------------------------------------

export const TEST_ANALYSIS_ID = "analysis-001-test";
export const TEST_DOCUMENT_ID = "doc-001-test";
export const TEST_TENANT_ID = "tenant-test-001";
export const TEST_TOKEN = "mock-bearer-token-abc-123";
export const TEST_CONTEXT = "test-workspace-context";
