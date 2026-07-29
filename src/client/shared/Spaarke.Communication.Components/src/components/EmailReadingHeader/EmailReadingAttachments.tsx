/**
 * EmailReadingAttachments — the reading-pane attachments sub-view
 * (email-communication-solution-r5 task 034, spec FR-12). Fills the
 * `EmailReadingPaneShell` (task 032) `renderAttachments` slot.
 *
 * Reuse-only (FR-12) — this view forks NOTHING:
 *   - Data + inline-image filtering: the promoted (task 021)
 *     `CommunicationAttachmentsService.getFileAttachments()` — inline
 *     (`cid:`) images are excluded INSIDE that service (`filterFileAttachments`),
 *     not re-implemented here.
 *   - Presentational list: the promoted (task 021) `<AttachmentList />`.
 *   - Preview: the shared `<RichFilePreviewDialog />` (`@spaarke/ui-components`).
 *
 * Mirrors the wiring in the production `CommunicationAttachmentsApp.tsx`
 * (`src/client/pcf/CommunicationAttachments/CommunicationAttachments/CommunicationAttachmentsApp.tsx`)
 * — the PCF's React-16 host for the exact same service + list + dialog — adapted
 * to this package's host-agnostic `IEmailAttachmentsDataService` /
 * `IEmailAttachmentsNavigationService` props (ADR-012) instead of
 * `context.webAPI` / a raw `Xrm` frame-walk.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard hooks only — no
 * React-18/19-only runtime API and no `as React.ComponentType` cast. Fluent v9
 * semantic tokens only (ADR-021) — themes correctly via the host `FluentProvider`.
 */
import * as React from 'react';
import { makeStyles, tokens, shorthands, Text, Spinner, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { RichFilePreviewDialog } from '@spaarke/ui-components';
import {
  CommunicationAttachmentsService,
  AttachmentApiService,
  fileTypeLabel,
  type IAttachmentItem,
  type IAttachmentRecord,
  type IAttachmentsWebApi,
} from '../../logic/attachments';
import { AttachmentList } from '../AttachmentList';
import type { IEmailReadingAttachmentsProps } from './EmailReadingHeader.types';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column' },
  center: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingHorizontalL),
  },
  empty: { padding: tokens.spacingHorizontalL, color: tokens.colorNeutralForeground3 },
  errorWrap: { padding: tokens.spacingHorizontalM },
});

/**
 * Adapts the host's `IEmailAttachmentsDataService` (OData-shaped
 * `retrieveMultipleRecords`, the same surface `context.webAPI` exposes) into
 * the `IAttachmentsWebApi` the promoted `CommunicationAttachmentsService`
 * expects — mirrors the adapter pattern documented on `IDataService` in
 * `@spaarke/ui-components/types/serviceInterfaces.ts`. One controlled cast
 * (the host's raw record shape is asserted to the service's own `IAttachmentRecord`
 * contract, same as the PCF host does implicitly via `context.webAPI`'s loose typing).
 */
function toAttachmentsWebApi(dataService: {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}): IAttachmentsWebApi {
  return {
    async retrieveMultipleRecords<T = IAttachmentRecord>(entityLogicalName: string, options?: string) {
      const result = await dataService.retrieveMultipleRecords(entityLogicalName, options);
      return { entities: result.entities as unknown as T[] };
    },
  };
}

/** Open a URL in a new tab. Best-effort — no-op when both URLs are absent. */
function openExternal(desktopUrl: string | null, webUrl: string | null): void {
  const url = desktopUrl || webUrl;
  if (!url) return;
  window.open(url, '_blank', 'noopener');
}

export const EmailReadingAttachments: React.FC<IEmailReadingAttachmentsProps> = ({
  selectedId,
  dataService,
  navigation,
  apiBaseUrl,
  onCountChange,
}) => {
  const s = useStyles();

  const service = React.useMemo(() => new CommunicationAttachmentsService(toAttachmentsWebApi(dataService)), [
    dataService,
  ]);
  const apiService = React.useMemo(() => new AttachmentApiService(apiBaseUrl), [apiBaseUrl]);

  const [items, setItems] = React.useState<IAttachmentItem[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!selectedId) {
      setLoading(false);
      setItems([]);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    void service
      .getFileAttachments(selectedId)
      .then(rows => {
        if (cancelled) return;
        setItems(rows);
        onCountChange?.(rows.length);
      })
      .catch(err => {
        console.error('[EmailReadingAttachments] attachment load failed:', err);
        if (cancelled) return;
        setLoadError('Could not load attachments for this email.');
        onCountChange?.(0);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onCountChange is a stable host callback; re-fetch keyed only on service/selection.
  }, [service, selectedId]);

  // ── Preview modal (mirrors CommunicationAttachmentsApp.tsx) ────────────────
  const [previewItem, setPreviewItem] = React.useState<IAttachmentItem | null>(null);

  const navItems = React.useMemo(() => items.filter(i => !!i.documentId), [items]);
  const previewIndex = React.useMemo(
    () => (previewItem ? navItems.findIndex(i => i.documentId === previewItem.documentId) : -1),
    [navItems, previewItem]
  );
  const handlePreviewNavigate = React.useCallback(
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

  if (loadError) {
    return (
      <div className={s.root}>
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{loadError}</MessageBarBody>
          </MessageBar>
        </div>
      </div>
    );
  }

  if (loading) {
    return (
      <div className={s.root}>
        <div className={s.center}>
          <Spinner label="Loading attachments…" />
        </div>
      </div>
    );
  }

  return (
    <div className={s.root} data-testid="email-reading-attachments">
      {items.length === 0 ? (
        <Text className={s.empty}>No attachments on this email.</Text>
      ) : (
        <AttachmentList items={items} onActivate={handleRowActivate} />
      )}

      {previewItem && previewItem.documentId && (
        <RichFilePreviewDialog
          open={!!previewItem}
          documentName={previewItem.name || previewItem.documentName || 'Attachment'}
          documentId={previewItem.documentId}
          documentType={fileTypeLabel(previewItem.name)}
          onClose={() => setPreviewItem(null)}
          navigationTotal={navItems.length}
          currentIndex={previewIndex}
          onNavigate={handlePreviewNavigate}
          fetchPreviewUrl={() => apiService.getPreviewUrl(previewItem.documentId as string)}
          onOpenFile={mode => {
            void (async () => {
              const links = await apiService.getOpenLinks(previewItem.documentId as string);
              if (!links) return;
              openExternal(mode === 'desktop' ? links.desktopUrl : null, links.webUrl);
            })();
          }}
          onOpenRecord={() => {
            void navigation.openRecord('sprk_document', previewItem.documentId as string);
          }}
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

EmailReadingAttachments.displayName = 'EmailReadingAttachments';
