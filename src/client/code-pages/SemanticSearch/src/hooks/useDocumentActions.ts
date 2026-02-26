/**
 * useDocumentActions — Document-specific action handlers for command bar
 *
 * Actions:
 *   1. Open in Web: GET /api/documents/{id}/open-links → open webUrl
 *   2. Open in Desktop: GET /api/documents/{id}/open-links → open desktopUrl
 *   3. Download: GET /api/documents/{id}/download → browser download
 *   4. Delete: confirm → DELETE /api/documents/{id}
 *   5. Email a Link: GET open-links → compose mailto:
 *   6. Send to Index: POST /api/documents/{id}/analyze
 *
 * @see ADR-013: All document operations go through BFF API
 * @see FileAccessEndpoints.cs — open-links endpoint
 * @see DocumentOperationsEndpoints.cs — analyze, delete endpoints
 */

import { useState, useCallback } from "react";
import { BFF_API_BASE_URL, buildAuthHeaders } from "../services/apiBase";

// =============================================
// Types
// =============================================

interface OpenLinksResponse {
    webUrl: string;
    desktopUrl?: string;
    mimeType: string;
    fileName: string;
}

export interface UseDocumentActionsResult {
    openInWeb: (documentId: string) => Promise<void>;
    openInDesktop: (documentId: string) => Promise<void>;
    download: (documentId: string) => Promise<void>;
    deleteDocuments: (documentIds: string[], onSuccess: () => void) => Promise<void>;
    emailLink: (documentId: string) => Promise<void>;
    sendToIndex: (documentIds: string[]) => Promise<void>;
    isActing: boolean;
    actionError: string | null;
}

// =============================================
// Helpers
// =============================================

async function getDocumentLinks(documentId: string): Promise<OpenLinksResponse> {
    const headers = await buildAuthHeaders();
    const response = await fetch(
        `${BFF_API_BASE_URL}/api/documents/${documentId}/open-links`,
        { headers }
    );

    if (!response.ok) {
        throw new Error(`Failed to get document links: ${response.status}`);
    }

    return response.json() as Promise<OpenLinksResponse>;
}

async function deleteDocument(documentId: string): Promise<void> {
    const headers = await buildAuthHeaders();
    const response = await fetch(
        `${BFF_API_BASE_URL}/api/documents/${documentId}`,
        { method: "DELETE", headers }
    );

    if (!response.ok) {
        throw new Error(`Failed to delete document: ${response.status}`);
    }
}

async function analyzeDocument(documentId: string): Promise<void> {
    const headers = await buildAuthHeaders();
    const response = await fetch(
        `${BFF_API_BASE_URL}/api/documents/${documentId}/analyze`,
        { method: "POST", headers }
    );

    if (!response.ok && response.status !== 202) {
        throw new Error(`Failed to send document to index: ${response.status}`);
    }
}

// =============================================
// Hook
// =============================================

export function useDocumentActions(): UseDocumentActionsResult {
    const [isActing, setIsActing] = useState(false);
    const [actionError, setActionError] = useState<string | null>(null);

    const openInWeb = useCallback(async (documentId: string) => {
        setIsActing(true);
        setActionError(null);
        try {
            const links = await getDocumentLinks(documentId);
            window.open(links.webUrl, "_blank");
        } catch (err) {
            setActionError(err instanceof Error ? err.message : "Failed to open document");
        } finally {
            setIsActing(false);
        }
    }, []);

    const openInDesktop = useCallback(async (documentId: string) => {
        setIsActing(true);
        setActionError(null);
        try {
            const links = await getDocumentLinks(documentId);
            if (links.desktopUrl) {
                window.open(links.desktopUrl);
            } else {
                // Fallback to web URL if no desktop protocol URL available
                window.open(links.webUrl, "_blank");
            }
        } catch (err) {
            setActionError(err instanceof Error ? err.message : "Failed to open in desktop");
        } finally {
            setIsActing(false);
        }
    }, []);

    const download = useCallback(async (documentId: string) => {
        setIsActing(true);
        setActionError(null);
        try {
            const headers = await buildAuthHeaders();
            const url = `${BFF_API_BASE_URL}/api/documents/${documentId}/download`;
            // Use a hidden link to trigger browser download
            const response = await fetch(url, { headers });
            if (!response.ok) {
                throw new Error(`Download failed: ${response.status}`);
            }
            const blob = await response.blob();
            const blobUrl = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = blobUrl;
            // Extract filename from Content-Disposition or use fallback
            const disposition = response.headers.get("Content-Disposition");
            const match = disposition?.match(/filename\*?=(?:UTF-8'')?["']?([^"';\n]+)/i);
            a.download = match?.[1] ?? `document-${documentId}`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(blobUrl);
        } catch (err) {
            setActionError(err instanceof Error ? err.message : "Failed to download document");
        } finally {
            setIsActing(false);
        }
    }, []);

    const deleteDocuments = useCallback(
        async (documentIds: string[], onSuccess: () => void) => {
            const count = documentIds.length;
            const confirmed = window.confirm(
                `Are you sure you want to delete ${count} document${count !== 1 ? "s" : ""}? This action cannot be undone.`
            );
            if (!confirmed) return;

            setIsActing(true);
            setActionError(null);
            try {
                await Promise.all(documentIds.map(deleteDocument));
                onSuccess();
            } catch (err) {
                setActionError(err instanceof Error ? err.message : "Failed to delete documents");
            } finally {
                setIsActing(false);
            }
        },
        []
    );

    const emailLink = useCallback(async (documentId: string) => {
        setIsActing(true);
        setActionError(null);
        try {
            const links = await getDocumentLinks(documentId);
            const subject = encodeURIComponent(`Document: ${links.fileName}`);
            const body = encodeURIComponent(`View this document:\n${links.webUrl}`);
            window.location.href = `mailto:?subject=${subject}&body=${body}`;
        } catch (err) {
            setActionError(err instanceof Error ? err.message : "Failed to create email link");
        } finally {
            setIsActing(false);
        }
    }, []);

    const sendToIndex = useCallback(async (documentIds: string[]) => {
        setIsActing(true);
        setActionError(null);
        try {
            await Promise.all(documentIds.map(analyzeDocument));
        } catch (err) {
            setActionError(err instanceof Error ? err.message : "Failed to send to index");
        } finally {
            setIsActing(false);
        }
    }, []);

    return {
        openInWeb,
        openInDesktop,
        download,
        deleteDocuments,
        emailLink,
        sendToIndex,
        isActing,
        actionError,
    };
}
