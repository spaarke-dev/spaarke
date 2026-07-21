/**
 * CommunicationAttachmentsApp — the React root for the attachment-list PCF.
 *
 * Replaces the OOB `sprk_communicationattachment` subgrid on the
 * `sprk_communication` form (UAT #2). Responsibilities:
 *   1. Resolve the host communication id from the page context.
 *   2. Bootstrap `@spaarke/auth` in a useEffect and gate render on `authReady`
 *      (ADR-028 async-init-in-component; no `PublicClientApplication` new — INV-7).
 *      Config comes from manifest input props, falling back to Dataverse env vars.
 *   3. Read the communication's attachment rows via `context.webAPI`
 *      (see the binding-mode design note in `CommunicationAttachmentsService`)
 *      and render name + type, filtering out inline-image attachments.
 *   4. On row click, open the shared `RichFilePreviewDialog` for that
 *      attachment's `sprk_document`, resolving the preview URL via the BFF.
 *      A `.eml`/`message-rfc822` document routes to download/open instead
 *      (owner decision #4 — Graph cannot inline-preview an email message).
 *
 * No new preview modal + no new BFF endpoint (root §11) — the rich modal is the
 * shared `RichFilePreviewDialog` and the URLs come from the existing
 * `/api/documents/{id}/preview-url` + `/open-links` endpoints.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  Badge,
} from '@fluentui/react-components';
// Deep-path import (NOT the barrel) — the barrel pulls in RichTextEditor →
// `@lexical/react` ESM modules that don't resolve `react/jsx-runtime` under
// React 16's resolution (PCF target per ADR-022). Matches SemanticSearchControl.
import { RichFilePreviewDialog } from '@spaarke/ui-components/dist/components/FilePreview/RichFilePreviewDialog';
import { IInputs } from './generated/ManifestTypes';
import { IAttachmentItem } from './types';
import {
  CommunicationAttachmentsService,
  IAttachmentsWebApi,
  isEmailMessageAttachment,
  fileTypeLabel,
} from './services/CommunicationAttachmentsService';
import { AttachmentApiService } from './services/AttachmentApiService';
import { AttachmentList } from './AttachmentList';
import { initializeAuth } from './authInit';
import { getEnvironmentVariable, getApiBaseUrl } from '../../shared/utils/environmentVariables';

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column' },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
  },
  // Spaarke section-header standard (docs/standards/UI-DESIGN-STANDARDS.md):
  // 14px (fontSizeBase300) · semibold (fontWeightSemibold) · neutral foreground 1
  // (colorNeutralForeground1 — #242424 in light, theme-correct in dark). Token
  // names ONLY (ADR-021) — no hardcoded hex/px.
  sectionHeader: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  grow: { flex: 1 },
  empty: { padding: tokens.spacingHorizontalL, color: tokens.colorNeutralForeground3 },
  errorWrap: { padding: tokens.spacingHorizontalM },
  center: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingHorizontalL),
  },
  footer: {
    marginTop: 'auto',
    paddingTop: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingVerticalS,
    display: 'flex',
    justifyContent: 'flex-end',
  },
  versionText: { fontSize: tokens.fontSizeBase100, color: tokens.colorNeutralForeground3 },
});

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function getXrm(): any {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;
    return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
  } catch {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (window as any).Xrm;
  }
}

/** Resolve the host communication record GUID from the page context / Xrm.Page. */
function resolveCommunicationId(context: ComponentFramework.Context<IInputs>): string | undefined {
  const page = (context as unknown as { page?: { entityId?: string } }).page;
  if (page?.entityId && page.entityId.length > 0) return page.entityId.replace(/[{}]/g, '');
  const xrm = getXrm();
  try {
    const id = xrm?.Page?.data?.entity?.getId?.();
    if (typeof id === 'string' && id.length > 0) return id.replace(/[{}]/g, '');
  } catch {
    /* ignore */
  }
  return undefined;
}

/**
 * Normalize a BFF base URL entered via the manifest: strip trailing slashes and
 * a trailing `/api` segment, so the `${base}/api/...` endpoints never double up
 * to `/api/api/...` (404). Mirrors the private `normalizeBffUrl` the env-var path
 * applies inside `getApiBaseUrl` — both config paths now produce a host-only base.
 */
function normalizeBffBaseUrl(raw: string): string {
  return raw
    .trim()
    .replace(/\/+$/, '')
    .replace(/\/api$/i, '');
}

/** Open a URL in a new tab, preferring the desktop protocol URL when present. */
function openExternal(desktopUrl: string | null, webUrl: string | null): void {
  const url = desktopUrl || webUrl;
  if (!url) return;
  const xrm = getXrm();
  try {
    if (typeof xrm?.Navigation?.openUrl === 'function') {
      xrm.Navigation.openUrl(url);
      return;
    }
  } catch {
    /* fall through to window.open */
  }
  window.open(url, '_blank', 'noopener');
}

export interface ICommunicationAttachmentsAppProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationAttachmentsApp: React.FC<ICommunicationAttachmentsAppProps> = ({ context, version }) => {
  const s = useStyles();

  const showVersionFooter = context.parameters.showVersionFooter?.raw !== false;
  const communicationId = React.useMemo(() => resolveCommunicationId(context), [context]);

  // ── Auth bootstrap (ADR-028 async-init-in-component; INV-7) ───────────────
  const [authReady, setAuthReady] = React.useState(false);
  const [authError, setAuthError] = React.useState<string | null>(null);
  const [resolvedApiBaseUrl, setResolvedApiBaseUrl] = React.useState('');

  React.useEffect(() => {
    let cancelled = false;
    const webApi = context.webAPI;
    const manifestApiBaseUrl = context.parameters.apiBaseUrl?.raw ?? '';
    const manifestTenantId = context.parameters.tenantId?.raw ?? '';
    const manifestClientAppId = context.parameters.clientAppId?.raw ?? '';
    const manifestBffAppId = context.parameters.bffAppId?.raw ?? '';

    let dataverseUrl: string;
    try {
      const xrm = getXrm();
      dataverseUrl =
        typeof xrm?.Utility?.getGlobalContext === 'function'
          ? xrm.Utility.getGlobalContext().getClientUrl()
          : window.location.origin;
    } catch {
      dataverseUrl = window.location.origin;
    }

    const doAuth = async () => {
      // Manifest value is normalized with the SAME rule the env path applies
      // (getApiBaseUrl → normalizeBffUrl), so a maker-entered `https://host/api`
      // does not double the `/api` segment.
      const apiBaseUrlResolved = manifestApiBaseUrl
        ? normalizeBffBaseUrl(manifestApiBaseUrl)
        : await getApiBaseUrl(webApi);
      const tenantId = manifestTenantId || (await getEnvironmentVariable(webApi, 'sprk_TenantId')) || '';
      const clientAppId = manifestClientAppId || (await getEnvironmentVariable(webApi, 'sprk_MsalClientId')) || '';
      const bffAppId = manifestBffAppId || (await getEnvironmentVariable(webApi, 'sprk_BffApiAppId')) || '';

      await initializeAuth(tenantId, clientAppId, bffAppId, apiBaseUrlResolved, dataverseUrl);
      if (!cancelled) {
        setResolvedApiBaseUrl(apiBaseUrlResolved);
        setAuthReady(true);
      }
    };

    doAuth().catch(err => {
      if (!cancelled) {
        console.error('[CommunicationAttachments] Auth initialization failed:', err);
        setAuthError('Could not initialize authentication for file preview. Check the BFF configuration.');
      }
    });

    return () => {
      cancelled = true;
    };
    // Run once on mount — context.webAPI + parameters are stable for the control's lifetime.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Attachment data ───────────────────────────────────────────────────────
  const service = React.useMemo(
    () => new CommunicationAttachmentsService(context.webAPI as unknown as IAttachmentsWebApi),
    [context.webAPI]
  );
  const apiService = React.useMemo(() => new AttachmentApiService(resolvedApiBaseUrl), [resolvedApiBaseUrl]);

  const [items, setItems] = React.useState<IAttachmentItem[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!communicationId) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    void service
      .getFileAttachments(communicationId)
      .then(rows => {
        if (!cancelled) setItems(rows);
      })
      .catch(err => {
        console.error('[CommunicationAttachments] attachment load failed:', err);
        if (!cancelled) setLoadError('Could not load attachments for this communication.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [service, communicationId]);

  // ── Preview modal ─────────────────────────────────────────────────────────
  const [previewItem, setPreviewItem] = React.useState<IAttachmentItem | null>(null);

  // Navigation set for the modal's prev/next "N of M" browse (A1).
  // Built from the already-filtered `items` (inline images excluded upstream by
  // the service). Email-message attachments (.eml/.msg) route to download/open
  // and NEVER render in the inline modal (owner decision #4), so they are
  // excluded from the modal nav sequence too — every navigable index resolves
  // to a document the modal can actually preview. Rows without a document
  // lookup are likewise unnavigable.
  const navItems = React.useMemo(
    () => items.filter(i => !!i.documentId && !isEmailMessageAttachment(i)),
    [items]
  );

  // 0-based position of the currently-previewed attachment inside `navItems`.
  const previewIndex = React.useMemo(
    () => (previewItem ? navItems.findIndex(i => i.attachmentId === previewItem.attachmentId) : -1),
    [previewItem, navItems]
  );

  // Move the modal to a different attachment. The shell clamps prev/next at the
  // ends (no wrap — matches SemanticSearchControl); we guard bounds defensively.
  // Swapping `previewItem` changes the dialog's `documentId` prop, which the
  // renderer uses to re-resolve the preview URL for the new document.
  const handlePreviewNavigate = React.useCallback(
    (nextIndex: number): void => {
      if (nextIndex < 0 || nextIndex >= navItems.length) return;
      setPreviewItem(navItems[nextIndex]);
    },
    [navItems]
  );

  const handleRowActivate = React.useCallback(
    (item: IAttachmentItem): void => {
      if (!item.documentId) return;
      // `.eml`/`.msg` documents cannot render in Graph inline preview (owner
      // decision #4) — route to download/open instead of the modal.
      if (isEmailMessageAttachment(item)) {
        void (async () => {
          const links = await apiService.getOpenLinks(item.documentId as string);
          openExternal(links?.desktopUrl ?? null, links?.webUrl ?? null);
        })();
        return;
      }
      setPreviewItem(item);
    },
    [apiService]
  );

  const openDocumentRecord = React.useCallback((documentId: string): void => {
    const xrm = getXrm();
    try {
      if (typeof xrm?.Navigation?.openForm === 'function') {
        Promise.resolve(
          xrm.Navigation.openForm({ entityName: 'sprk_document', entityId: documentId, openInNewWindow: true })
        ).catch((err: unknown) => console.warn('[CommunicationAttachments] openForm failed:', err));
      }
    } catch (err) {
      console.warn('[CommunicationAttachments] openDocumentRecord failed:', err);
    }
  }, []);

  // ── Render gates ──────────────────────────────────────────────────────────
  const versionFooter = showVersionFooter ? (
    <div className={s.footer}>
      <Text className={s.versionText}>v{version}</Text>
    </div>
  ) : null;

  if (authError) {
    return (
      <div className={s.root}>
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{authError}</MessageBarBody>
          </MessageBar>
        </div>
        {versionFooter}
      </div>
    );
  }

  if (!authReady) {
    return (
      <div className={s.root}>
        <div className={s.center}>
          <Spinner label="Initializing…" />
        </div>
        {versionFooter}
      </div>
    );
  }

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Text className={s.sectionHeader}>Attachments</Text>
        <div className={s.grow} />
        {items.length > 0 && (
          <Badge appearance="tint" color="informative">
            {items.length}
          </Badge>
        )}
      </div>

      {loadError && (
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{loadError}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      {loading ? (
        <div className={s.center}>
          <Spinner label="Loading attachments…" />
        </div>
      ) : items.length === 0 ? (
        <Text className={s.empty}>No attachments on this communication.</Text>
      ) : (
        <AttachmentList items={items} onActivate={handleRowActivate} />
      )}

      {versionFooter}

      {previewItem && previewItem.documentId && (
        <RichFilePreviewDialog
          open={!!previewItem}
          documentName={previewItem.name || previewItem.documentName || 'Attachment'}
          documentId={previewItem.documentId}
          documentType={fileTypeLabel(previewItem.name)}
          onClose={() => setPreviewItem(null)}
          navigationTotal={navItems.length}
          currentIndex={previewIndex >= 0 ? previewIndex : undefined}
          onNavigate={handlePreviewNavigate}
          fetchPreviewUrl={() => apiService.getPreviewUrl(previewItem.documentId as string)}
          onOpenFile={mode => {
            void (async () => {
              const links = await apiService.getOpenLinks(previewItem.documentId as string);
              if (!links) return;
              openExternal(mode === 'desktop' ? links.desktopUrl : null, links.webUrl);
            })();
          }}
          onOpenRecord={() => openDocumentRecord(previewItem.documentId as string)}
          onEmailDocument={() => {
            /* Emailing an attachment as a new communication is out of scope for this surface. */
          }}
          onCopyLink={() => {
            void (async () => {
              const links = await apiService.getOpenLinks(previewItem.documentId as string);
              const url = links?.webUrl;
              if (url && navigator.clipboard?.writeText) {
                await navigator.clipboard.writeText(url);
              }
            })();
          }}
        />
      )}
    </div>
  );
};
