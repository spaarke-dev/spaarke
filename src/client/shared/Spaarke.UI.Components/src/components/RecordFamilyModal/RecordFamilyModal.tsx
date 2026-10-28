/**
 * RecordFamilyModal — lists a record's Family and hands CONFIRMED rows to an action handler
 * (field-mapping-server-write-path-r1 task 073; spec FR-32; design §12.3 E).
 *
 * Built on the `SprkModal` `BrowseModal` preset (ADR-050 Layout 2, launched in-app from a React
 * surface; docs/standards/MODAL-DECISION-CRITERIA.md) — no new modal shell. The preset's header owns
 * the `‹ N of M ›` navigator, which here steps a highlight through the rows in display order, and its
 * meta column shows the counts of what is on screen and the highlighted row's table and level.
 *
 * Data: `GET /api/v1/records/{table}/{id}/family` (FR-31, task 072) via the host's injected
 * `authenticatedFetch` (ADR-028). The Family view is the full Filing family, up and down (owner,
 * 2026-10-27), as the CALLER may read it. The modal shows exactly what the API returned: a record
 * the caller cannot read is simply absent, and nothing here counts, names or hints at one. Every
 * failure (404, 400, 401, network, malformed body) is ONE fixed error state — the 404 is uniform on
 * purpose (unknown and unreadable anchors look the same) and the modal must not tell them apart.
 *
 * Columns (owner, 2026-10-27): name, record number (blank where the table has none), record type,
 * status, modified on. Rows are grouped by level, then table; a record the server returned a null
 * display value for shows a blank cell.
 *
 * Actions (FR-32/FR-34): "Push field updates" and "Update Assigned To". The modal NEVER performs an
 * action itself and never acts on open: it calls the caller-supplied `onAction(action, rows)` with
 * exactly the (table, id) pairs the person ticked, and the server re-checks everything. Until the
 * push preview ships (task 040) a host passes no `onAction`, and both actions render disabled with
 * the tooltip "Available after the push update".
 */

import * as React from 'react';
import {
  Badge,
  Button,
  Checkbox,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Tooltip,
  tokens,
} from '@fluentui/react-components';
import { BrowseModal } from '../SprkModal';
import type { PreviewModalMetadataItem } from '../SprkModal';
import type { ResponseFetchFn } from '../../utils/fetchTypes';
import {
  familyKey,
  fetchRecordFamily,
  mergeFamilyMembers,
  type IFamilyMember,
  type IFamilyRef,
  type IFamilySelection,
} from './recordFamilyService';

/** The two toolbar actions (FR-34). More come later, each with its own review. */
export type RecordFamilyAction = 'push' | 'assignedTo';

/** Called with the action and exactly the rows the person ticked. */
export type RecordFamilyActionHandler = (action: RecordFamilyAction, rows: IFamilySelection[]) => void;

export interface RecordFamilyModalProps {
  /** Whether the modal is open. The family is read when it opens, never while closed. */
  open: boolean;
  onClose: () => void;
  /** Logical name of the record the Family is opened from (the anchor), e.g. `sprk_matter`. */
  table: string;
  /** The anchor's id (braces and case are normalised before it enters the URL). */
  recordId: string;
  /** Host-supplied `authenticatedFetch` (ADR-028: never a token). */
  authenticatedFetch: ResponseFetchFn;
  /**
   * Runs an action on the ticked rows. When omitted, both actions render disabled with the tooltip
   * "Available after the push update" (the modal is then a read-only Family view).
   */
  onAction?: RecordFamilyActionHandler;
  /** Header title (default "Family"). */
  title?: string;
  /** The `--sprk-ui-scale` factor, forwarded to the shell. */
  uiScale?: number;
}

const ACTIONS: ReadonlyArray<{ action: RecordFamilyAction; label: string }> = [
  { action: 'push', label: 'Push field updates' },
  { action: 'assignedTo', label: 'Update Assigned To' },
];

const NO_HANDLER_TOOLTIP = 'Available after the push update';
const NO_SELECTION_TOOLTIP = 'Select one or more records first';
/** ADR-019: fixed, actionable text; the cause is logged with `console.warn` and never rendered. */
const LOAD_ERROR_TEXT = 'The family could not be loaded. Close this window and try again.';
const MORE_ERROR_TEXT = 'More records could not be loaded. Try again.';
const TRUNCATED_TEXT = 'Showing part of this family. Use Load more to see further records.';

/** Display names for the tables a Filing family contains; any other table falls back to its logical name. */
const TABLE_LABELS: Readonly<Record<string, string>> = {
  // The three roots, and every child table of the Filing family (SecureChildLineage.cs).
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_workassignment: 'Work Assignment',
  sprk_agreement: 'Agreement',
  sprk_analysis: 'Analysis',
  sprk_analysisoutput: 'Analysis Output',
  sprk_attachmentartifact: 'Attachment Artifact',
  sprk_billingevent: 'Billing Event',
  sprk_budget: 'Budget',
  sprk_budgetbucket: 'Budget Bucket',
  sprk_budgetrevision: 'Budget Revision',
  sprk_communication: 'Communication',
  sprk_communicationattachment: 'Communication Attachment',
  sprk_communicationparticipant: 'Communication Participant',
  sprk_communicationthread: 'Communication Thread',
  sprk_decisionrecord: 'Decision Record',
  sprk_document: 'Document',
  sprk_emailartifact: 'Email Artifact',
  sprk_emailreviewlog: 'Email Review Log',
  sprk_event: 'Event',
  sprk_eventlog: 'Event Log',
  sprk_fileversion: 'File Version',
  sprk_invoice: 'Invoice',
  sprk_kpiassessment: 'KPI Assessment',
  sprk_memo: 'Memo',
  sprk_policy: 'Policy',
  sprk_policyversion: 'Policy Version',
  sprk_reportcard: 'Report Card',
  sprk_signal: 'Signal',
  sprk_spendsignal: 'Spend Signal',
  sprk_spendsnapshot: 'Spend Snapshot',
  sprk_todo: 'To Do',
};

/** A known table's display name; any other table is its logical name without `sprk_`, title-cased. */
function tableLabel(table: string): string {
  const known = TABLE_LABELS[table];
  if (known) return known;
  return table
    .replace(/^sprk_/, '')
    .split('_')
    .filter(w => w !== '')
    .map(w => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ');
}

function formatModifiedOn(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

interface IFamilyGroup {
  key: string;
  depth: number;
  table: string;
  members: IFamilyMember[];
}

/** Groups by level (ascending), then by table in order of first appearance; members keep the server's order. */
function groupFamily(members: readonly IFamilyMember[]): IFamilyGroup[] {
  const groups = new Map<string, IFamilyGroup>();
  for (const m of members) {
    const key = `${m.depth}|${m.table}`;
    let g = groups.get(key);
    if (!g) {
      g = { key, depth: m.depth, table: m.table, members: [] };
      groups.set(key, g);
    }
    g.members.push(m);
  }
  // Array.prototype.sort is stable, so tables keep their first-appearance order within a level.
  return Array.from(groups.values()).sort((a, b) => a.depth - b.depth);
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | {
      kind: 'ready';
      anchor: IFamilyRef;
      members: IFamilyMember[];
      truncated: boolean;
      continuation: string | null;
    };

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    alignSelf: 'stretch',
    width: '100%',
    minWidth: 0,
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  toolbarCount: {
    marginLeft: 'auto',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  notice: {
    margin: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL} 0`,
  },
  scroll: {
    flexGrow: 1,
    minHeight: 0,
    overflow: 'auto',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
  },
  selectCell: {
    flexGrow: 0,
    minWidth: '40px',
  },
  groupRow: {
    backgroundColor: tokens.colorNeutralBackground3,
  },
  groupLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  currentRow: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
  },
  nameCell: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  nameText: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  footer: {
    display: 'flex',
    justifyContent: 'center',
    padding: tokens.spacingVerticalS,
  },
  center: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
});

/** A toolbar button. Disabled buttons stay focusable so their tooltip can be reached by keyboard. */
const ActionButton: React.FC<{
  label: string;
  disabledReason: string | null;
  onClick: () => void;
}> = ({ label, disabledReason, onClick }) => {
  const button = (
    <Button appearance="secondary" size="small" disabledFocusable={disabledReason !== null} onClick={onClick}>
      {label}
    </Button>
  );
  return disabledReason === null ? (
    button
  ) : (
    <Tooltip content={disabledReason} relationship="description">
      {button}
    </Tooltip>
  );
};

export const RecordFamilyModal: React.FC<RecordFamilyModalProps> = ({
  open,
  onClose,
  table,
  recordId,
  authenticatedFetch,
  onAction,
  title = 'Family',
  uiScale,
}) => {
  const styles = useStyles();

  const [state, setState] = React.useState<LoadState>({ kind: 'loading' });
  const [loadingMore, setLoadingMore] = React.useState(false);
  const [moreFailed, setMoreFailed] = React.useState(false);
  const [selected, setSelected] = React.useState<ReadonlySet<string>>(() => new Set());
  const [currentKey, setCurrentKey] = React.useState<string | null>(null);

  // A host may pass a new fetch function every render; it must not re-trigger the load.
  const fetchRef = React.useRef(authenticatedFetch);
  fetchRef.current = authenticatedFetch;
  // The number of the latest load. An older load's answer (another record, closed, unmounted) is dropped.
  const loadSeq = React.useRef(0);

  React.useEffect(() => {
    const seq = ++loadSeq.current;
    setSelected(new Set());
    setCurrentKey(null);
    setLoadingMore(false);
    setMoreFailed(false);
    if (!open) {
      setState({ kind: 'loading' });
      return () => {
        loadSeq.current += 1;
      };
    }
    setState({ kind: 'loading' });
    fetchRecordFamily(fetchRef.current, table, recordId).then(
      res => {
        if (seq !== loadSeq.current) return;
        setState({
          kind: 'ready',
          anchor: res.anchor,
          members: res.members,
          truncated: res.truncated,
          continuation: res.continuation ?? null,
        });
      },
      err => {
        if (seq !== loadSeq.current) return;
        // The cause goes to the console only; the person sees the fixed text (ADR-019).
        console.warn('[RecordFamilyModal] the family could not be loaded', err);
        setState({ kind: 'error' });
      }
    );
    return () => {
      loadSeq.current += 1;
    };
  }, [open, table, recordId]);

  const members = React.useMemo(() => (state.kind === 'ready' ? state.members : []), [state]);
  const groups = React.useMemo(() => groupFamily(members), [members]);
  const ordered = React.useMemo(() => groups.flatMap(g => g.members), [groups]);

  const anchorKey = state.kind === 'ready' ? familyKey(state.anchor) : null;
  // The highlight starts on the opened record; the header navigator moves it.
  const activeKey = currentKey ?? anchorKey;
  const activeIndex = Math.max(
    0,
    ordered.findIndex(m => familyKey(m) === activeKey)
  );
  const activeMember = ordered[activeIndex];

  const loadMore = React.useCallback(() => {
    if (state.kind !== 'ready' || !state.continuation || loadingMore) return;
    const seq = loadSeq.current;
    setLoadingMore(true);
    setMoreFailed(false);
    fetchRecordFamily(fetchRef.current, table, recordId, state.continuation).then(
      res => {
        if (seq !== loadSeq.current) return;
        setLoadingMore(false);
        setState(prev =>
          prev.kind !== 'ready'
            ? prev
            : {
                ...prev,
                members: mergeFamilyMembers(prev.members, res.members),
                truncated: res.truncated,
                continuation: res.continuation ?? null,
              }
        );
      },
      err => {
        if (seq !== loadSeq.current) return;
        console.warn('[RecordFamilyModal] more of the family could not be loaded', err);
        setLoadingMore(false);
        setMoreFailed(true);
      }
    );
  }, [state, loadingMore, table, recordId]);

  const toggle = React.useCallback((key: string, checked: boolean) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (checked) next.add(key);
      else next.delete(key);
      return next;
    });
  }, []);

  const runAction = (action: RecordFamilyAction) => {
    if (!onAction) return;
    // Exactly the ticked rows, in display order. Nothing that was not ticked, nothing no longer loaded.
    const rows = ordered.filter(m => selected.has(familyKey(m))).map(m => ({ table: m.table, id: m.id }));
    if (rows.length === 0) return;
    onAction(action, rows);
  };

  const selectedCount = ordered.filter(m => selected.has(familyKey(m))).length;
  const disabledReason = !onAction ? NO_HANDLER_TOOLTIP : selectedCount === 0 ? NO_SELECTION_TOOLTIP : null;

  const metadata: PreviewModalMetadataItem[] =
    state.kind === 'ready'
      ? [
          { label: 'Records shown', value: String(ordered.length) },
          { label: 'Selected', value: String(selectedCount) },
          ...(activeMember
            ? [
                { label: 'Table', value: tableLabel(activeMember.table) },
                { label: 'Level', value: String(activeMember.depth) },
              ]
            : []),
        ]
      : [];

  const renderBody = () => {
    if (state.kind === 'loading') {
      return (
        <div className={styles.center}>
          <Spinner size="medium" label="Loading the family" />
        </div>
      );
    }
    if (state.kind === 'error') {
      return (
        <div className={styles.center}>
          <Text>{LOAD_ERROR_TEXT}</Text>
        </div>
      );
    }
    return (
      <>
        <div className={styles.toolbar} role="toolbar" aria-label="Family actions">
          {ACTIONS.map(a => (
            <ActionButton
              key={a.action}
              label={a.label}
              disabledReason={disabledReason}
              onClick={() => runAction(a.action)}
            />
          ))}
          <span className={styles.toolbarCount}>{selectedCount} selected</span>
        </div>
        {state.truncated && (
          <MessageBar intent="info" className={styles.notice}>
            <MessageBarBody>{TRUNCATED_TEXT}</MessageBarBody>
          </MessageBar>
        )}
        <div className={styles.scroll}>
          <Table size="small" aria-label="Family members">
            <TableHeader>
              <TableRow>
                <TableHeaderCell className={styles.selectCell} aria-label="Select" />
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Record number</TableHeaderCell>
                <TableHeaderCell>Record type</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell>Modified on</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {groups.map(g => (
                <React.Fragment key={g.key}>
                  <TableRow className={styles.groupRow}>
                    <TableCell colSpan={6}>
                      <span className={styles.groupLabel}>{`Level ${g.depth} · ${tableLabel(g.table)}`}</span>
                    </TableCell>
                  </TableRow>
                  {g.members.map(m => {
                    const key = familyKey(m);
                    const isCurrent = key === activeKey;
                    return (
                      <TableRow
                        key={key}
                        className={isCurrent ? styles.currentRow : undefined}
                        aria-current={isCurrent ? 'true' : undefined}
                        onClick={() => setCurrentKey(key)}
                      >
                        <TableCell className={styles.selectCell}>
                          <Checkbox
                            aria-label={`Select ${m.name ?? 'record'}`}
                            checked={selected.has(key)}
                            onChange={(_e, data) => toggle(key, data.checked === true)}
                            onClick={e => e.stopPropagation()}
                          />
                        </TableCell>
                        <TableCell>
                          <div className={styles.nameCell}>
                            <span className={styles.nameText}>{m.name ?? ''}</span>
                            {key === anchorKey && (
                              <Badge appearance="tint" size="small">
                                This record
                              </Badge>
                            )}
                          </div>
                        </TableCell>
                        <TableCell>{m.recordNumber ?? ''}</TableCell>
                        <TableCell>{m.recordType ?? ''}</TableCell>
                        <TableCell>{m.status ?? ''}</TableCell>
                        <TableCell>{formatModifiedOn(m.modifiedOn)}</TableCell>
                      </TableRow>
                    );
                  })}
                </React.Fragment>
              ))}
            </TableBody>
          </Table>
        </div>
        {(state.truncated || moreFailed) && (
          <div className={styles.footer}>
            {moreFailed && (
              <MessageBar intent="error">
                <MessageBarBody>{MORE_ERROR_TEXT}</MessageBarBody>
              </MessageBar>
            )}
            {state.truncated && state.continuation && (
              <Button appearance="secondary" disabled={loadingMore} onClick={loadMore}>
                {loadingMore ? 'Loading…' : 'Load more'}
              </Button>
            )}
          </div>
        )}
      </>
    );
  };

  return (
    <BrowseModal
      open={open}
      onClose={onClose}
      title={title}
      uiScale={uiScale}
      metadata={metadata}
      nav={{
        index: activeIndex,
        total: ordered.length,
        onNavigate: dir => {
          const next = ordered[dir === 'next' ? activeIndex + 1 : activeIndex - 1];
          if (next) setCurrentKey(familyKey(next));
        },
      }}
    >
      <div className={styles.body} data-testid="record-family-body">
        {renderBody()}
      </div>
    </BrowseModal>
  );
};

export default RecordFamilyModal;
