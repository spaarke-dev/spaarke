/**
 * FilePreviewServiceAdapter — Bridges the viewer's auth context to the
 * shared FilePreviewDialog's IFilePreviewServices interface.
 *
 * Uses @spaarke/auth authenticatedFetch for API calls.
 */

import { authenticatedFetch } from "./authInit";
import type {
  IFilePreviewServices,
  IOpenLinksResponse,
} from "../../../../shared/Spaarke.UI.Components/dist/components/FilePreview";

/**
 * Create an IFilePreviewServices adapter for the DocumentRelationshipViewer.
 *
 * @param apiBaseUrl - BFF API base URL
 */
export function createFilePreviewServices(
  apiBaseUrl: string,
): IFilePreviewServices {
  return {
    getDocumentPreviewUrl: async (
      documentId: string,
    ): Promise<string | null> => {
      try {
        const res = await authenticatedFetch(
          `${apiBaseUrl}/api/documents/${documentId}/preview-url`,
        );
        if (!res.ok) return null;
        const data = await res.json();
        return data.previewUrl ?? data.url ?? null;
      } catch {
        console.error("[FilePreview] Failed to get preview URL:", documentId);
        return null;
      }
    },

    getDocumentOpenLinks: async (
      documentId: string,
    ): Promise<IOpenLinksResponse | null> => {
      try {
        const res = await authenticatedFetch(
          `${apiBaseUrl}/api/documents/${documentId}/open-links`,
        );
        if (!res.ok) return null;
        return await res.json();
      } catch {
        console.error("[FilePreview] Failed to get open links:", documentId);
        return null;
      }
    },

    navigateToEntity: (params: {
      action: string;
      entityName: string;
      entityId: string;
      openInNewWindow?: boolean;
    }) => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        if (xrm?.Navigation?.openForm) {
          xrm.Navigation.openForm({
            entityName: params.entityName,
            entityId: params.entityId,
          });
        } else {
          // Fallback: open in new window
          const clientUrl =
            xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ??
            window.location.origin;
          window.open(
            `${clientUrl}/main.aspx?etn=${params.entityName}&id=${params.entityId}&pagetype=entityrecord`,
            params.openInNewWindow ? "_blank" : "_self",
          );
        }
      } catch (err) {
        console.error("[FilePreview] Failed to navigate:", err);
      }
    },

    copyDocumentLink: async (documentId: string): Promise<boolean> => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        const clientUrl =
          xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ??
          window.location.origin;
        const url = `${clientUrl}/main.aspx?etn=sprk_document&id=${documentId}&pagetype=entityrecord`;
        await navigator.clipboard.writeText(url);
        return true;
      } catch {
        return false;
      }
    },

    setWorkspaceFlag: async (
      _documentId: string,
      _flag: boolean,
    ): Promise<boolean> => {
      // Stub — workspace flag not yet wired in the viewer context
      return true;
    },
  };
}
