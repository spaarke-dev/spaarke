/**
 * RecordDetailModal — the read-only detail view a grid row opens in the external SPA and the Teams tab
 * (R3 task 050, FR-01 / FR-02 / FR-03; R2 UAT 3E: "row click does nothing").
 *
 * ENVELOPE (ADR-050). A thin config of the canonical `SprkModal` (`@spaarke/ui-components`): its OWN header carries the
 * "‹ N of M ›" browse group, the title, the window controls and ×, and its footer carries Close. This is the in-app
 * launch the ADR requires — it needs no `Xrm`, so it behaves identically in the browser host and in the Teams host.
 * `RecordNavigationModalShell` is deliberately NOT nested: ADR-050 (amended 2026-10-07) keeps it as the dirty-check
 * protocol library behind `nav.onBeforeNavigate` and forbids nesting its envelope (it would double the chrome — two
 * counters, two titles). That protocol exists for an iframe form with unsaved edits; this view is read-only and has no
 * iframe, so there is nothing to guard and `onBeforeNavigate` is not wired.
 *
 * DATA (no new server read). Every call goes through the external plane via `bffApiCall` (token from the active
 * acquirer; the URL is the active backend's (task 041) — never a link or query string):
 *   - fields:   the id-filtered module fetch (POST …/fetch, `<idAttribute> = {id}`) for ALL six entities — one read path,
 *               which also carries `@formattedValues` (option-set labels); see RecordDetailFields.tsx for why.
 *   - invoices: POST /api/v1/external/api/dataverse/fetch on sprk_invoice filtered by this record's lookup. That is the
 *               SAME Tier-2-scoped module read the Invoices tab runs, so a row shows here exactly when the tab shows it
 *               for this record (FR-02).
 *   - documents (task 054, FR-24): the shared `DocumentLibrary` over the task 031 routes
 *               `/api/v1/external/{projects|matters|workassignments|servicerequests|invoices}/{id}/documents` - list,
 *               download, version history and upload (Create, gated by the list's `canCreate`). A service request's
 *               documents are reachable ONLY there.
 *   - document file (task 066): a document's detail shows Download, over the task 031 content route of the parent the row
 *               is filed to (project, matter, work assignment - tried in the BFF's order, moving on only on a 403); the
 *               same download code as the Documents section. No new route. Inline preview is deferred to R4.
 *   - delete-own (task 054, FR-26): `GET /api/v1/external/me` says who the caller is; a document, to-do or event whose
 *               creator stamp is that principal shows Delete (asks first, 204 removes it, a 403 shows an error). Messages
 *               have no Delete. An invoice's documents use the `invoices` root (owner decision 2026-10-11, option A).
 *   - service request actions (owner decisions 2026-10-11): the REQUESTER sees "Delete request" (`ServiceRequestActions`:
 *               DELETE /api/v1/external/servicerequests/{id}; 409 `has_related_records` offers "Send recall request", a
 *               post to the request's own message thread through the existing message client). A 204 shows the request as
 *               deleted in place of its sections; the grid behind lists it until it reloads.
 *   - messages: GET/POST /api/v1/external/{root}/{id}/messages for project, matter, work assignment, invoice and
 *               service request (`RecordMessages`, task 051; a document has no thread, so no section).
 *   - events/tasks: the existing `EventsCalendar` / `SmartTodo` (read-only: `AccessLevel.ViewOnly` hides create and
 *               toggle) for a Project, Matter and Work Assignment (task 057): GET /api/v1/external/{projects|matters|
 *               workassignments}/{id}/events and /todos, the server applying the record's rights (and, for a row filed to
 *               more than one root, the every-root rule: the client filters nothing). A 403 shows the shared denied state
 *               and a failed read a retryable error, inside the section only. Document, Invoice and Service Request show
 *               no events section (notes/decisions.md).
 *
 * DENIED (FR-01). An id that returns no row from the fetch seam (or a 403) shows "You don't have access to this record"
 * and renders NO section and NO field, so nothing of the record is on screen.
 *
 * ADR-021: Fluent UI v9 semantic tokens only; the modal inherits the host FluentProvider (light/dark/Teams theme).
 */
import * as React from 'react';
import { makeStyles, tokens, Button, MessageBar, MessageBarBody, Spinner, Text } from '@fluentui/react-components';
import { ArrowDownloadRegular, LockClosedRegular } from '@fluentui/react-icons';
import { SprkModal } from '@spaarke/ui-components/components/SprkModal';
import { bffApiCall } from '../../auth/bff-client';
import { ApiError, AccessLevel } from '../../types';
import { callerIdentityOf, type CallerIdentity } from '../../api/delete-own';
import type { DocumentRoot, EventsTodosRoot } from '../../api/web-api-client';
import type { ExternalUserContextResponse } from '../../auth/bff-client';
import { useCallerContext } from '../../hooks/useCallerContext';
import { canUpload as levelCanUpload, mapAccessLevelString } from '../../hooks/useAccessLevel';
import {
  DocumentLibrary,
  downloadDocumentFromParents,
  downloadFailureLogText,
  downloadFailureMessage,
} from '../DocumentLibrary';
import { EventsCalendar } from '../EventsCalendar';
import { SmartTodo } from '../SmartTodo';
import { TruncatedListNotice } from '../TruncatedListNotice';
import { RecordMessages } from './RecordMessages';
import { ServiceRequestActions } from './ServiceRequestActions';
import {
  EMPTY_VALUE,
  RecordDetailFields,
  documentParentCandidates,
  formatDetailValue,
  getRecordDetailEntity,
  recordDetailTitle,
  type DetailRecord,
  type RecordDetailEntity,
} from './RecordDetailFields';

// ---------------------------------------------------------------------------
// Reads
// ---------------------------------------------------------------------------

/** The module read seam, mounted under the external group (`ExternalModuleDataEndpoints`). */
const EXTERNAL_DATA_PATH = '/api/v1/external/api/dataverse';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The module fetch response (`FetchResponseDto`). */
interface FetchResult {
  entities?: DetailRecord[];
  moreRecords?: boolean;
}

function xmlAttr(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/**
 * Single-entity, join-free FetchXML over allow-listed columns only — the only shape the seam's guard admits.
 * Every interpolated value is a GUID checked by the caller or a constant from this file, and is escaped anyway.
 */
function buildFetchXml(
  entity: string,
  attributes: readonly string[],
  where: { attribute: string; value: string },
  orderBy?: { attribute: string; descending: boolean }
): string {
  const attrs = attributes.map(a => `<attribute name="${xmlAttr(a)}"/>`).join('');
  const order = orderBy
    ? `<order attribute="${xmlAttr(orderBy.attribute)}" descending="${orderBy.descending ? 'true' : 'false'}"/>`
    : '';
  return (
    `<fetch><entity name="${xmlAttr(entity)}">${attrs}` +
    `<filter type="and"><condition attribute="${xmlAttr(where.attribute)}" operator="eq" value="${xmlAttr(where.value)}"/></filter>` +
    `${order}</entity></fetch>`
  );
}

function postFetch(entity: string, fetchXml: string): Promise<FetchResult> {
  return bffApiCall<FetchResult>(`${EXTERNAL_DATA_PATH}/fetch`, {
    method: 'POST',
    body: JSON.stringify({ entityName: entity, fetchXml }),
  });
}

/** A thrown read that means "the caller may not see this record". */
class DeniedError extends Error {}

/** Reads one record's fields through the id-filtered module fetch. Throws DeniedError / ApiError. */
async function readRecord(cfg: RecordDetailEntity, id: string): Promise<DetailRecord> {
  const columns = Array.from(new Set([...cfg.fields.map(f => f.attribute), ...(cfg.extraColumns ?? [])]));
  const result = await postFetch(
    cfg.entity,
    buildFetchXml(cfg.entity, [cfg.idAttribute, ...columns], { attribute: cfg.idAttribute, value: id })
  );
  const row = result.entities?.[0];
  // The fetch seam returns only rows inside the caller's Tier-2 set, so no row means "not yours" (or not there).
  if (!row) throw new DeniedError('record not in the caller set');
  return row;
}

type RecordState =
  | { status: 'loading' }
  | { status: 'ready'; record: DetailRecord }
  | { status: 'denied' }
  | { status: 'notfound' }
  | { status: 'error'; message: string; retryable: boolean };

/** Loads the record for `id`; a state is only ever shown for the id it was read for (no flash of the previous record). */
function useRecord(cfg: RecordDetailEntity | undefined, id: string | undefined, attempt: number): RecordState {
  const [loaded, setLoaded] = React.useState<{ key: string; state: RecordState } | null>(null);
  const key = `${cfg?.entity ?? ''}:${id ?? ''}:${attempt}`;

  React.useEffect(() => {
    if (!cfg || !id) return;
    let cancelled = false;
    const done = (state: RecordState): void => {
      if (!cancelled) setLoaded({ key, state });
    };
    if (!GUID.test(id)) {
      // A malformed id is never sent anywhere, and retrying cannot make it valid.
      done({ status: 'error', message: 'This record could not be opened.', retryable: false });
      return () => {
        cancelled = true;
      };
    }
    readRecord(cfg, id).then(
      record => done({ status: 'ready', record }),
      (err: unknown) => {
        if (err instanceof DeniedError || (err instanceof ApiError && err.statusCode === 403)) {
          done({ status: 'denied' });
        } else if (err instanceof ApiError && err.statusCode === 404) {
          done({ status: 'notfound' });
        } else {
          done({ status: 'error', message: 'This record could not be loaded just now.', retryable: true });
        }
      }
    );
    return () => {
      cancelled = true;
    };
  }, [cfg, id, key]);

  if (!cfg || !id) return { status: 'notfound' };
  return loaded && loaded.key === key ? loaded.state : { status: 'loading' };
}

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; rows: DetailRecord[]; truncated: boolean }
  | { status: 'error' };

/** The Tier-2-scoped module read the matching tab runs, narrowed to the rows tied to this record. */
function useRelatedRows(
  childEntity: 'sprk_invoice',
  attributes: readonly string[],
  parentAttribute: string | undefined,
  parentId: string,
  orderBy: { attribute: string; descending: boolean }
): ListState {
  const [loaded, setLoaded] = React.useState<{ key: string; state: ListState } | null>(null);
  const key = `${childEntity}:${parentAttribute ?? ''}:${parentId}`;
  const attrsKey = attributes.join(',');

  React.useEffect(() => {
    if (!parentAttribute) return;
    let cancelled = false;
    const done = (state: ListState): void => {
      if (!cancelled) setLoaded({ key, state });
    };
    postFetch(
      childEntity,
      buildFetchXml(childEntity, attrsKey.split(','), { attribute: parentAttribute, value: parentId }, orderBy)
    ).then(
      result => done({ status: 'ready', rows: result.entities ?? [], truncated: result.moreRecords === true }),
      () => done({ status: 'error' })
    );
    return () => {
      cancelled = true;
    };
    // orderBy is a module constant at every call site.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [childEntity, parentAttribute, parentId, attrsKey, key]);

  return loaded && loaded.key === key ? loaded.state : { status: 'loading' };
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXL,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  heading: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    margin: 0,
  },
  center: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '160px',
    gap: tokens.spacingHorizontalM,
  },
  denied: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '200px',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
    textAlign: 'center',
  },
  deniedIcon: {
    fontSize: '40px',
    color: tokens.colorNeutralForeground3,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
  },
  list: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  item: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  itemPrimary: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    overflowWrap: 'anywhere',
  },
  itemMeta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'nowrap',
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Sections
// ---------------------------------------------------------------------------

interface RelatedListProps {
  testId: string;
  idAttribute: string;
  heading: string;
  noun: string;
  emptyText: string;
  state: ListState;
  primary: (row: DetailRecord) => string;
  meta: (row: DetailRecord) => string;
}

const RelatedList: React.FC<RelatedListProps> = ({
  testId,
  idAttribute,
  heading,
  noun,
  emptyText,
  state,
  primary,
  meta,
}) => {
  const styles = useStyles();
  return (
    <section className={styles.section} aria-label={heading} data-testid={testId}>
      <h3 className={styles.heading}>{heading}</h3>
      {state.status === 'loading' && <Spinner size="small" label={`Loading ${noun}...`} />}
      {state.status === 'error' && (
        <MessageBar intent="error">
          <MessageBarBody>The {noun} could not be loaded just now.</MessageBarBody>
        </MessageBar>
      )}
      {state.status === 'ready' && (
        <>
          <TruncatedListNotice truncated={state.truncated} shown={state.rows.length} noun={noun} />
          {state.rows.length === 0 && !state.truncated && (
            <div className={styles.empty} role="status">
              {emptyText}
            </div>
          )}
          {state.rows.length > 0 && (
            <ul className={styles.list}>
              {state.rows.map((row, i) => (
                <li key={String(row[idAttribute] ?? i)} className={styles.item}>
                  <span className={styles.itemPrimary}>{primary(row)}</span>
                  <span className={styles.itemMeta}>{meta(row)}</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
};

const INVOICE_COLUMNS = [
  'sprk_invoiceid',
  'sprk_name',
  'sprk_invoicenumber',
  'sprk_invoicedate',
  'statuscode',
  'sprk_totalamount',
] as const;
const INVOICE_ORDER = { attribute: 'sprk_invoicedate', descending: true };

/**
 * Whether the host decides the Upload control. Only a project does: its level is in `/me`. Every other record leaves it
 * to `DocumentLibrary`, which follows the BFF's `canCreate` on that record's document list (the right the upload route
 * checks - a ViewOnly reader of a matter or work assignment is not offered an upload that can only fail; a service
 * request's requester always holds Create). The server still enforces Create on every upload.
 */
function uploadOverride(
  root: DocumentRoot,
  id: string,
  context: ExternalUserContextResponse | null
): boolean | undefined {
  if (root !== 'projects') return undefined;
  // Task 036 fix round 1: a per-record caller's level says only what they may see; the list's `canCreate` decides.
  if (context?.writeAccessPerRecord === true) return undefined;
  const entry = context?.projects.find(p => p.projectId.toLowerCase() === id.toLowerCase());
  return levelCanUpload(mapAccessLevelString(entry?.accessLevel));
}

/**
 * Documents (task 054): the shared `DocumentLibrary` on the record's task 031 route. Download needs only Read, which
 * opening the detail already required; Upload follows `showsUpload`; Delete follows the creator stamp.
 */
const DocumentsSection: React.FC<{
  root: DocumentRoot;
  id: string;
  context: ExternalUserContextResponse | null;
  caller: CallerIdentity | null;
}> = ({ root, id, context, caller }) => {
  const styles = useStyles();
  return (
    <section className={styles.section} aria-label="Documents" data-testid="record-detail-documents">
      <h3 className={styles.heading}>Documents</h3>
      <DocumentLibrary
        root={root}
        recordId={id}
        canUpload={uploadOverride(root, id, context)}
        canDownload
        caller={caller}
        writeAccessPerRecord={context?.writeAccessPerRecord === true}
      />
    </section>
  );
};

/**
 * Download (task 066, owner check 3; inline preview is deferred to R4): the document detail's one file action, on the same
 * download code the record's Documents section uses (`downloadDocumentFromParents` over the existing content route; no
 * new route, no signed link). It asks the parents the row is filed to in the BFF's order and the server decides which one
 * the caller may read. A document with no usable parent link offers nothing. A failure is shown here, never only logged.
 */
const DocumentDownload: React.FC<{ record: DetailRecord; documentId: string }> = ({ record, documentId }) => {
  const styles = useStyles();
  const parents = React.useMemo(() => documentParentCandidates(record), [record]);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  if (parents.length === 0) return null;
  // R3 ISS-011 (#1886): the BFF derives `hasFile` on a documents-module row. A document with no file is still shown (its
  // fields above), but offers no Download: it could only answer 409 "No file attached". Only an explicit false hides it;
  // absent (an older BFF) keeps today's behaviour.
  if (record.hasFile === false) return null;

  const shownName = formatDetailValue(record, { attribute: 'sprk_documentname' });
  const name = shownName !== EMPTY_VALUE ? shownName : 'document';

  const handleDownload = async (): Promise<void> => {
    setBusy(true);
    setError(null);
    try {
      await downloadDocumentFromParents(parents, documentId, name);
    } catch (err) {
      console.error(`[RecordDetailModal] Download failed for document ${documentId}: ${downloadFailureLogText(err)}`);
      setError(downloadFailureMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.section} aria-label="File" data-testid="record-detail-download">
      <h3 className={styles.heading}>File</h3>
      {error && (
        <MessageBar intent="error" data-testid="record-detail-download-error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      <div>
        <Button
          appearance="secondary"
          icon={busy ? <Spinner size="tiny" /> : <ArrowDownloadRegular />}
          disabled={busy}
          onClick={() => void handleDownload()}
          aria-label={`Download ${name}`}
        >
          Download
        </Button>
      </div>
    </section>
  );
};

const InvoicesSection: React.FC<{ parentAttribute: string; parentId: string }> = ({ parentAttribute, parentId }) => {
  const state = useRelatedRows('sprk_invoice', INVOICE_COLUMNS, parentAttribute, parentId, INVOICE_ORDER);
  return (
    <RelatedList
      testId="record-detail-invoices"
      idAttribute="sprk_invoiceid"
      heading="Invoices"
      noun="invoices"
      emptyText="No invoices for this record."
      state={state}
      primary={row => {
        const number = formatDetailValue(row, { attribute: 'sprk_invoicenumber' });
        return number !== EMPTY_VALUE ? number : formatDetailValue(row, { attribute: 'sprk_name' });
      }}
      meta={row =>
        [
          formatDetailValue(row, { attribute: 'statuscode' }),
          formatDetailValue(row, { attribute: 'sprk_totalamount', kind: 'money' }),
        ].join(' · ')
      }
    />
  );
};

/** The entities whose detail shows events and tasks, and the `{root}` of their task 039 / 007 routes. */
const EVENTS_TODOS_ROOTS: Readonly<Record<string, EventsTodosRoot>> = {
  sprk_project: 'projects',
  sprk_matter: 'matters',
  sprk_workassignment: 'workassignments',
};

/**
 * Events and tasks: the existing components on the record's own routes (Project, Matter, Work Assignment), read-only (no
 * create or toggle), except that an event or task the caller created shows Delete (task 054). An entity with no such
 * route renders nothing (the config's `showsEventsAndTasks` never offers one).
 */
const EventsAndTasksSection: React.FC<{ entity: string; id: string; caller: CallerIdentity | null }> = ({
  entity,
  id,
  caller,
}) => {
  const styles = useStyles();
  const root = EVENTS_TODOS_ROOTS[entity];
  if (!root) return null;
  return (
    <section className={styles.section} aria-label="Events and tasks" data-testid="record-detail-events">
      <h3 className={styles.heading}>Events and tasks</h3>
      <EventsCalendar projectId={id} root={root} accessLevel={AccessLevel.ViewOnly} caller={caller} />
      <SmartTodo projectId={id} root={root} accessLevel={AccessLevel.ViewOnly} caller={caller} />
    </section>
  );
};

// ---------------------------------------------------------------------------
// Modal
// ---------------------------------------------------------------------------

export interface RecordDetailModalProps {
  open: boolean;
  onClose: () => void;
  /** Logical name of the opened entity (a key of `RECORD_DETAIL_ENTITIES`). */
  entityName: string;
  /** The grid's row ids, in grid order — the "1 of N" set. */
  recordIds: readonly string[];
  /** 0-based position of the shown record in `recordIds`. */
  index: number;
  /** Called with the new position when the user pages to the previous / next row. */
  onIndexChange: (index: number) => void;
  /**
   * Called once when the caller deleted the record shown here (the service request, on a 204). The opener uses it to
   * re-read its list: this modal unmounts on close, so what it knows about deleted rows does not outlive it.
   */
  onRecordDeleted?: () => void;
  /**
   * Called when the shown record reaches a settled state: loaded, denied, or not found (NOT while loading, and not for a
   * retryable error, where nothing was learned yet). The shared opener tells its caller (the `?record=` deep link, which
   * keeps the link in the address bar until then so a token redirect during the read still carries it).
   */
  onSettled?: () => void;
}

export const RecordDetailModal: React.FC<RecordDetailModalProps> = ({
  open,
  onClose,
  entityName,
  recordIds,
  index,
  onIndexChange,
  onRecordDeleted,
  onSettled,
}) => {
  const styles = useStyles();
  const cfg = getRecordDetailEntity(entityName);
  const [attempt, setAttempt] = React.useState(0);
  // True while the Messages section has a send in flight. Paging away or closing then would unmount the thread and lose
  // the send's result (success, error, kept draft), so both are held until it settles (review 051 K3).
  const [messageSending, setMessageSending] = React.useState(false);
  // True while a service request recall post is in flight: same hold as a message send.
  const [recallSending, setRecallSending] = React.useState(false);
  // Service requests the caller deleted in this session (a 204): shown as deleted, never re-read.
  const [deletedIds, setDeletedIds] = React.useState<ReadonlySet<string>>(() => new Set());
  // Bumped when a recall message was posted, so the visible thread re-reads (never appends the 201 body).
  const [threadRefresh, setThreadRefresh] = React.useState(0);
  const id = recordIds[index];
  // A record deleted in this session is never read again: its read would answer 403/404 and show as "no access".
  const isDeleted = !!id && deletedIds.has(id);

  const state = useRecord(cfg, open && !isDeleted ? id : undefined, attempt);
  const settled = !isDeleted && (state.status === 'ready' || state.status === 'denied' || state.status === 'notfound');
  React.useEffect(() => {
    if (settled) onSettled?.();
  }, [settled, id, onSettled]);
  // Who the caller is (for Delete on their own rows) and their project level (for Upload): read once per opening. Identity
  // does not change from record to record, so paging does not read it again; `caller` is memoised so the sections that
  // take it as a prop see a new object only when the identity changes.
  const callerContext = useCallerContext(open);
  const caller = React.useMemo(() => callerIdentityOf(callerContext), [callerContext]);
  // `cfg` is undefined only for an entity with no detail view, which the opener never offers; render nothing then.
  if (!cfg) return null;

  const title = recordDetailTitle(cfg, state.status === 'ready' ? state.record : null);
  const holdNavigation = messageSending || recallSending;

  const handleNavigate = (dir: 'prev' | 'next'): void => {
    const next = dir === 'prev' ? index - 1 : index + 1;
    if (next >= 0 && next < recordIds.length) onIndexChange(next);
  };

  // SprkModal routes the x, Escape and the backdrop through `onClose`, so one guard here covers all three. The footer
  // Close is also disabled so the hold is visible. SprkModal has no disabled state for its x or its "<" ">" buttons
  // (shared component); the browse buttons are held by the `onBeforeNavigate` guard, which blocks the move.
  const handleClose = (): void => {
    if (!holdNavigation) onClose();
  };

  return (
    <SprkModal
      open={open}
      onClose={handleClose}
      title={title}
      size="lg"
      nav={{
        index,
        total: recordIds.length,
        onNavigate: handleNavigate,
        onBeforeNavigate: () => !holdNavigation,
      }}
      footer={
        <Button appearance="primary" onClick={handleClose} disabled={holdNavigation}>
          Close
        </Button>
      }
    >
      <div className={styles.body} data-testid="record-detail-body">
        {isDeleted && (
          <div className={styles.denied} role="status" data-testid="record-detail-deleted">
            <Text size={400} weight="semibold">
              This {cfg.label.toLowerCase()} was deleted
            </Text>
          </div>
        )}

        {!isDeleted && state.status === 'loading' && (
          <div className={styles.center}>
            <Spinner size="medium" label="Loading record..." />
          </div>
        )}

        {!isDeleted && state.status === 'denied' && (
          <div className={styles.denied} role="alert" data-testid="record-detail-denied">
            <LockClosedRegular className={styles.deniedIcon} aria-hidden />
            <Text size={400} weight="semibold">
              You don't have access to this record
            </Text>
          </div>
        )}

        {!isDeleted && state.status === 'notfound' && (
          <div className={styles.denied} role="status" data-testid="record-detail-notfound">
            <Text size={400} weight="semibold">
              This record is no longer available
            </Text>
          </div>
        )}

        {!isDeleted && state.status === 'error' && (
          <div className={styles.denied} role="alert" data-testid="record-detail-error">
            <Text size={300}>{state.message}</Text>
            {state.retryable && (
              <Button appearance="secondary" onClick={() => setAttempt(n => n + 1)}>
                Try again
              </Button>
            )}
          </div>
        )}

        {!isDeleted && state.status === 'ready' && id && (
          // Keyed by record so every section's state resets when the user pages to another row.
          <React.Fragment key={`${cfg.entity}:${id}`}>
            <>
              <RecordDetailFields cfg={cfg} record={state.record} />
              {/* The document's file (task 066): Download on the existing content route; preview is R4. */}
              {cfg.entity === 'sprk_document' && <DocumentDownload record={state.record} documentId={id} />}
              {/* The requester's actions on their own service request (owner decisions 2026-10-11). */}
              {cfg.entity === 'sprk_servicerequest' && (
                <ServiceRequestActions
                  requestId={id}
                  record={state.record}
                  caller={caller}
                  label={title}
                  onDeleted={() => {
                    setDeletedIds(prev => new Set(prev).add(id));
                    onRecordDeleted?.();
                  }}
                  onRecalled={() => setThreadRefresh(n => n + 1)}
                  onBusyChange={setRecallSending}
                />
              )}
              {cfg.documentsRoot && (
                <DocumentsSection root={cfg.documentsRoot} id={id} context={callerContext} caller={caller} />
              )}
              {cfg.invoicesBy && <InvoicesSection parentAttribute={cfg.invoicesBy} parentId={id} />}
              {cfg.showsEventsAndTasks && <EventsAndTasksSection entity={cfg.entity} id={id} caller={caller} />}
              {/* Messages (task 051): the record's thread + compose, for the entities the server has a thread for. */}
              <RecordMessages
                entity={cfg.entity}
                recordId={id}
                onSendingChange={setMessageSending}
                refreshSignal={threadRefresh}
              />
            </>
          </React.Fragment>
        )}
      </div>
    </SprkModal>
  );
};

export default RecordDetailModal;
