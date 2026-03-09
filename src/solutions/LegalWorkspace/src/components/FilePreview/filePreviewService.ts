/**
 * filePreviewService.ts
 * Utility functions for the FilePreviewDialog toolbar actions.
 */

import { getXrm } from '../../services/xrmProvider';

/**
 * Copy a shareable document link to the clipboard.
 * Constructs the URL from the Dataverse client URL + entity route.
 */
export async function copyDocumentLink(documentId: string): Promise<boolean> {
  try {
    const xrm = getXrm();
    const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';
    const link = clientUrl
      ? `${clientUrl}/main.aspx?etn=sprk_document&id=${encodeURIComponent(documentId)}&pagetype=entityrecord`
      : documentId;

    // Modern clipboard API
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(link);
      return true;
    }

    // Fallback: execCommand
    const textarea = document.createElement('textarea');
    textarea.value = link;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    const success = document.execCommand('copy');
    document.body.removeChild(textarea);
    return success;
  } catch (err) {
    console.error('[filePreviewService] copyDocumentLink failed:', err);
    return false;
  }
}

/**
 * Set or clear the workspace flag on a document record.
 */
export async function setWorkspaceFlag(
  documentId: string,
  flag: boolean,
): Promise<boolean> {
  try {
    const xrm = getXrm();
    if (!xrm?.WebApi?.updateRecord) {
      console.warn('[filePreviewService] Xrm.WebApi not available');
      return false;
    }
    await xrm.WebApi.updateRecord('sprk_document', documentId, {
      sprk_workspaceflag: flag,
    });
    return true;
  } catch (err) {
    console.error('[filePreviewService] setWorkspaceFlag failed:', err);
    return false;
  }
}
