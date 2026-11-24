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
import {
  makeStyles,
  tokens,
  shorthands,
  Badge,
  Button,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import { RichFilePreviewDialog, openInDesktopApp, OPEN_IN_DESKTOP_FAILED_MESSAGE } from '@spaarke/ui-components';
import {
  CommunicationAttachmentsService,
  AttachmentApiService,
  isInvoiceDocumentType,
  type IAttachmentItem,
  type IAttachmentRecord,
  type IAttachmentsWebApi,
} from '../../logic/attachments';
import { AttachmentList } from '../AttachmentList';
import { useAttachmentPreview } from '../AttachmentPreview';
import { isPdfFileName } from '../SubmitInvoice/invoiceAttachments';
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

export const EmailReadingAttachments: React.FC<IEmailReadingAttachmentsProps> = ({
  selectedId,
  dataService,
  navigation,
  apiBaseUrl,
  onCountChange,
  onSubmitAsInvoice,
}) => {
  const s = useStyles();

  const service = React.useMemo(
    () => new CommunicationAttachmentsService(toAttachmentsWebApi(dataService)),
    [dataService]
  );
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

  // Preview / open logic is shared with the CommunicationAttachments PCF (#2967).
  // Set when "Open in desktop" fails; shown above the list until the next attempt.
  const [openError, setOpenError] = React.useState<string | null>(null);
  const { handleRowActivate, dialogProps } = useAttachmentPreview({
    items,
    apiService,
    apiBaseUrl,
    openInDesktopApp,
    openInDesktopFailedMessage: OPEN_IN_DESKTOP_FAILED_MESSAGE,
    logTag: 'EmailReadingAttachments',
    onOpenRecord: id => {
      void navigation.openRecord('sprk_document', id);
    },
    onOpenDesktopError: setOpenError,
    // The bar renders above the list; close the modal preview so the message is not hidden behind it.
    closeOnOpenDesktopError: true,
  });

  // ebilling-r1 task 118: the profile's Invoice flag (`sprk_documenttype`, read with the upload status: no extra query)
  // shows an "Invoice" badge. It is a hint only (any PDF can be submitted); a document not yet profiled shows none.
  const renderInvoiceAccessory = React.useCallback(
    (item: IAttachmentItem): React.ReactNode => {
      if (!item.documentId || !isInvoiceDocumentType(item.documentType)) return null;
      const documentId = item.documentId;
      return (
        <>
          <Badge appearance="tint" color="success" size="small" data-testid="attachment-invoice-badge">
            Invoice
          </Badge>
          {/* The Invoice mark is PDF only: a flagged non-PDF keeps the badge but offers no action that cannot work. */}
          {onSubmitAsInvoice && isPdfFileName(item.name || item.documentName) && (
            <Button
              size="small"
              appearance="subtle"
              data-testid="attachment-submit-as-invoice"
              onClick={() => onSubmitAsInvoice(documentId)}
            >
              Submit as invoice
            </Button>
          )}
        </>
      );
    },
    [onSubmitAsInvoice]
  );

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
      {openError && (
        <div className={s.errorWrap}>
          <MessageBar intent="error" data-testid="attachment-open-error">
            <MessageBarBody>{openError}</MessageBarBody>
          </MessageBar>
        </div>
      )}
      {items.length === 0 ? (
        <Text className={s.empty}>No attachments on this email.</Text>
      ) : (
        <AttachmentList items={items} onActivate={handleRowActivate} renderRowAccessory={renderInvoiceAccessory} />
      )}

      {dialogProps && <RichFilePreviewDialog {...dialogProps} />}
    </div>
  );
};

EmailReadingAttachments.displayName = 'EmailReadingAttachments';
