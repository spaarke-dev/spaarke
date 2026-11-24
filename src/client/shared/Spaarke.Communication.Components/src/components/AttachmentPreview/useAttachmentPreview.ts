/**
 * `useAttachmentPreview` — the attachment preview / open logic shared by the CommunicationAttachments PCF and the
 * reading-pane `EmailReadingAttachments` (#2967; it was copied between them, "mirrors the PCF"). React 16-safe.
 *
 * It owns the selection, the prev/next browse list, "Open in web / desktop", and "Copy link". It does NOT render
 * the dialog and imports nothing from `@spaarke/ui-components`: the host renders
 * `<RichFilePreviewDialog {...dialogProps} />` (deep-imported in a PCF, from the barrel in a code page) and passes
 * `openInDesktopApp` / `openInDesktopFailedMessage` in, because a PCF cannot import that package's barrel and a
 * code page that aliases it to `src/` cannot import a `dist/` deep path.
 */
import * as React from 'react';
import { authenticatedFetch } from '@spaarke/auth';
import { fileTypeLabel } from '../../logic/attachments/CommunicationAttachmentsService';
import type { AttachmentApiService } from '../../logic/attachments/AttachmentApiService';
import type { IAttachmentItem } from '../../logic/attachments/types';

type OpenLinks = NonNullable<Awaited<ReturnType<AttachmentApiService['getOpenLinks']>>>;

export interface IOpenInDesktopAppArgs {
  bffBaseUrl: string;
  documentId: string;
  links: OpenLinks;
  fetch: (url: string) => Promise<Response>;
  fallbackFileName?: string;
}

export interface IUseAttachmentPreviewOptions {
  items: IAttachmentItem[];
  apiService: Pick<AttachmentApiService, 'getPreviewUrl' | 'getOpenLinks'>;
  /** Host-only BFF base URL for the desktop download. */
  apiBaseUrl: string;
  /** `openInDesktopApp` from `@spaarke/ui-components`. */
  openInDesktopApp: (args: IOpenInDesktopAppArgs) => Promise<unknown>;
  /** `OPEN_IN_DESKTOP_FAILED_MESSAGE` from `@spaarke/ui-components`. */
  openInDesktopFailedMessage: string;
  /** Log prefix, e.g. `CommunicationAttachments`. */
  logTag: string;
  /** Opens the `sprk_document` record behind the previewed attachment. */
  onOpenRecord: (documentId: string) => void;
  /** Called when "Open in desktop" fails (host decides how to surface it); also called with `null` before each attempt. */
  onOpenDesktopError?: (message: string | null) => void;
  /** Close the dialog when "Open in desktop" fails (use when the host shows the error behind the dialog). */
  closeOnOpenDesktopError?: boolean;
  /** Opens the web URL ("Open in web"). Default: a new browser tab; a PCF passes its Xrm `openUrl`. */
  openExternal?: (url: string) => void;
}

export interface IAttachmentPreviewDialogProps {
  open: boolean;
  documentName: string;
  documentId: string;
  documentType: string;
  onClose: () => void;
  navigationTotal: number;
  currentIndex: number;
  onNavigate: (nextIndex: number) => void;
  fetchPreviewUrl: () => Promise<string | null>;
  onOpenFile: (mode: 'web' | 'desktop') => void;
  onOpenRecord: () => void;
  onEmailDocument: () => void;
  onCopyLink: () => void;
}

export interface IUseAttachmentPreviewResult {
  /** Row activation handler for `<AttachmentList onActivate>`. */
  handleRowActivate: (item: IAttachmentItem) => void;
  /** Props for `<RichFilePreviewDialog>`; `null` while nothing is previewed. */
  dialogProps: IAttachmentPreviewDialogProps | null;
}

function openInNewTab(url: string): void {
  window.open(url, '_blank', 'noopener');
}

export function useAttachmentPreview(opts: IUseAttachmentPreviewOptions): IUseAttachmentPreviewResult {
  const {
    items,
    apiService,
    apiBaseUrl,
    openInDesktopApp,
    openInDesktopFailedMessage,
    logTag,
    onOpenRecord,
    onOpenDesktopError,
    closeOnOpenDesktopError,
    openExternal = openInNewTab,
  } = opts;
  const [previewItem, setPreviewItem] = React.useState<IAttachmentItem | null>(null);

  // Every row with a resolvable `sprk_document` (including .eml/.msg) takes part in the prev/next browse.
  const navItems = React.useMemo(() => items.filter(i => !!i.documentId), [items]);
  const previewIndex = React.useMemo(
    () => (previewItem ? navItems.findIndex(i => i.documentId === previewItem.documentId) : -1),
    [navItems, previewItem]
  );
  const handleNavigate = React.useCallback(
    (nextIndex: number): void => {
      if (nextIndex < 0 || nextIndex >= navItems.length) return;
      setPreviewItem(navItems[nextIndex]);
    },
    [navItems]
  );
  const handleRowActivate = React.useCallback((item: IAttachmentItem): void => {
    if (!item.documentId) return;
    setPreviewItem(item);
  }, []);

  if (!previewItem || !previewItem.documentId) return { handleRowActivate, dialogProps: null };
  const documentId = previewItem.documentId;
  const fileName = previewItem.name || previewItem.documentName || undefined;

  const dialogProps: IAttachmentPreviewDialogProps = {
    open: true,
    documentName: fileName || 'Attachment',
    documentId,
    documentType: fileTypeLabel(previewItem.name),
    onClose: () => setPreviewItem(null),
    navigationTotal: navItems.length,
    currentIndex: previewIndex,
    onNavigate: handleNavigate,
    fetchPreviewUrl: () => apiService.getPreviewUrl(documentId),
    onOpenFile: mode => {
      void (async () => {
        onOpenDesktopError?.(null);
        const links = await apiService.getOpenLinks(documentId);
        if (mode !== 'desktop') {
          if (links?.webUrl) openExternal(links.webUrl);
          return;
        }
        // Office files open in the Office app; anything else downloads through the BFF (ISS-004) — not the
        // SharePoint webUrl, which a read-only user cannot open.
        try {
          if (!links) throw new Error('The open-links request returned nothing.');
          await openInDesktopApp({
            bffBaseUrl: apiBaseUrl,
            documentId,
            links,
            fetch: authenticatedFetch,
            fallbackFileName: fileName,
          });
        } catch (err) {
          console.error(`[${logTag}] Open in desktop failed:`, err);
          onOpenDesktopError?.(openInDesktopFailedMessage);
          if (closeOnOpenDesktopError) setPreviewItem(null);
        }
      })();
    },
    onOpenRecord: () => onOpenRecord(documentId),
    onEmailDocument: () => {
      /* Emailing an attachment as a new communication is out of scope for these surfaces. */
    },
    onCopyLink: () => {
      void (async () => {
        const links = await apiService.getOpenLinks(documentId);
        const url = links?.webUrl;
        if (url && navigator.clipboard?.writeText) {
          await navigator.clipboard.writeText(url);
        }
      })();
    },
  };
  return { handleRowActivate, dialogProps };
}
