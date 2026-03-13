/**
 * filePreviewTypes.ts
 * Type definitions for the FilePreviewDialog shared component.
 *
 * External dependencies (preview URL fetching, open links, navigation,
 * clipboard, workspace flags) are expressed as callback props so the
 * shared component does not import environment-specific services.
 */

/** Response shape from the document open-links endpoint. */
export interface IOpenLinksResponse {
  webUrl: string;
  desktopUrl: string | null;
  mimeType: string;
  fileName: string;
}

/**
 * Service callbacks that the FilePreviewDialog requires.
 * Consumers inject implementations that call their own BFF / Xrm layer.
 */
export interface IFilePreviewServices {
  /** Fetch an ephemeral iframe preview URL for a document. Returns null on failure. */
  getDocumentPreviewUrl: (documentId: string) => Promise<string | null>;
  /** Fetch desktop + web open links for a document. Returns null on failure. */
  getDocumentOpenLinks: (
    documentId: string,
  ) => Promise<IOpenLinksResponse | null>;
  /** Navigate to a Dataverse entity record. */
  navigateToEntity: (params: {
    action: "openRecord";
    entityName: string;
    entityId: string;
    openInNewWindow?: boolean;
  }) => void;
  /** Copy a document link to the clipboard. Returns true on success. */
  copyDocumentLink: (documentId: string) => Promise<boolean>;
  /** Set or clear the workspace flag on a document record. Returns true on success. */
  setWorkspaceFlag: (documentId: string, flag: boolean) => Promise<boolean>;
}
