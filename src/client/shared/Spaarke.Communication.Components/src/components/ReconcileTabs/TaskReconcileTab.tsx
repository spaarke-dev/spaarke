/**
 * TaskReconcileTab.tsx
 *
 * The Pillar E **Tasks** reconcile tab (Job C) — email-communication-intelligence-r2
 * task 056, spec FR-E5 (§105) + ADR-015 + NFR-10. For the CONFIRMED record only, it
 * renders `kind:"create-task"` proposals from the FR-17 queue-feed as an EDITABLE
 * task form — name · description · base date · due date · final due date ·
 * assigned-to · status · completed date — each with **Accept / Reject / Hold**, and
 * a **"+ New task"** that adds an AD-HOC task the engine never proposed.
 *
 * ── Accept routing (the 034/056b/055b contract) ─────────────────────────────────
 *   • Proposal, name+description UNCHANGED → `POST /proposals/{reviewLogId}/create-task/apply`
 *     (task 034) with the FR-E5 fields (+ due-date override). The apply closes the
 *     proposal. Create-and-complete = set status=Completed + completed date inline
 *     (the endpoint PATCHes them on the created task in the SAME audited apply).
 *   • Proposal, name OR description EDITED → the reviewer authored a different task,
 *     so route through the AD-HOC path `POST /{communicationId}/create-task` (task
 *     056b) with the full edited form, THEN best-effort `POST /proposals/{reviewLogId}/dismiss`
 *     (task 055b) to close the original. The 034 apply request carries no
 *     subject/description (they come from the extraction), so editing them there
 *     would be silently dropped — this routing loses nothing. Create-first so a
 *     dismiss failure leaves the task created + the proposal re-appearing (no loss).
 *   • "+ New task" (ad-hoc) → `POST /{communicationId}/create-task` (056b). No
 *     reviewLogId, no dismiss.
 *   • Reject → `POST /proposals/{reviewLogId}/dismiss` (055b). Hold → NO API call
 *     (leave Proposed; reappears next load).
 *
 * ADR-015: nothing deadline-bearing auto-finalizes — a task is created ONLY on an
 * explicit Accept (the tab never auto-POSTs). NFR-10: actionable only for a
 * confirmed record; the list re-scopes (re-fetches) when the confirmed record
 * changes; an ad-hoc task always attaches to the confirmed record.
 *
 * DUAL-USE (ADR-022): renders inline (browse-shell right pane, where a citation
 * click highlights the visible reader); `TaskReconcileModal` wraps the same body in
 * the ADR-050 `FormModal` for the email-record-form mount. Mirrors the task-055
 * `FieldUpdateReconcileTab` structure.
 *
 * ADR-021 (Fluent v9 tokens; light + dark), ADR-012 (fetches KNOWN BFF endpoints
 * via injected `authenticatedFetch`, like `EmailBodyView`).
 */
import * as React from 'react';
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Field,
  Input,
  Link,
  Select,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { AddRegular, CheckmarkRegular, DismissRegular, ClockRegular, Link16Regular } from '@fluentui/react-icons';
import { FormModal } from '@spaarke/ui-components';
import { authenticatedFetch as defaultAuthenticatedFetch } from '@spaarke/auth';
import type { AuthenticatedFetchFn } from '../EmailBody/EmailBodyView.types';
import type { EmailCitation } from '../../logic/citations';
import type { ReconcileRegarding, ProposalOutcome } from './FieldUpdateReconcileTab';

/** sprk_eventstatus Choice values used by the reconcile form (as-built: Open=1, Completed=2). */
export const EVENT_STATUS = { OPEN: 1, COMPLETED: 2 } as const;
const STATUS_OPTIONS: ReadonlyArray<{ value: number; label: string }> = [
  { value: EVENT_STATUS.OPEN, label: 'Open' },
  { value: EVENT_STATUS.COMPLETED, label: 'Completed' },
];

const CREATE_TASK_KIND = 'create-task';

/** The editable task-form state (mirrors the FR-E5 field set + the identity fields). */
export interface TaskFormState {
  subject: string;
  description: string;
  baseDate: string; // 'YYYY-MM-DD' | ''
  dueDate: string;
  finalDueDate: string;
  completedDate: string;
  status: string; // stringified Choice int | ''
  assignedTo: string; // systemuser GUID | ''
}

/** One Job C create-task proposal projected from the FR-17 queue-feed. */
export interface CreateTaskProposal {
  reviewLogId: string;
  form: TaskFormState;
  citation?: EmailCitation;
  confidence?: number | null;
}

interface RawQueueFeedItem {
  communicationId: string;
  kind: string;
  reviewLogId?: string | null;
  taskName?: string | null;
  taskDescription?: string | null;
  taskBaseDate?: string | null;
  taskDueDate?: string | null;
  taskFinalDueDate?: string | null;
  taskCompletedDate?: string | null;
  taskStatus?: number | null;
  taskAssignedTo?: string | null;
  proposalConfidence?: number | null;
  citationSource?: string | null;
  citationLocator?: string | null;
  citationQuotedText?: string | null;
}

interface RawQueueFeedResult {
  items?: ReadonlyArray<RawQueueFeedItem> | null;
  count?: number;
}

export interface TaskReconcileTabProps {
  /** The confirmed association (NFR-10). Null/undefined ⇒ gate. Changing it re-scopes. */
  regarding?: ReconcileRegarding | null;
  /** The communication under review — filters the feed + is the ad-hoc endpoint's route key. */
  communicationId: string;
  /** Injected authenticated fetch (defaults to `@spaarke/auth`). Test/host seam. */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** Fired when a proposal's citation is clicked (host lifts to the reader's activeCitation, task 054). */
  onCitationClick?: (citation: EmailCitation) => void;
  /** Fired after a task reaches an outcome — 'applied' (created), 'rejected', or 'held'. */
  onTaskResolved?: (reviewLogId: string | null, outcome: ProposalOutcome) => void;
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    minWidth: 0,
  },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalS },
  headerText: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  state: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  dateGrid: { display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: tokens.spacingHorizontalS },
  meta: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, marginTop: tokens.spacingVerticalXS, flexWrap: 'wrap' },
  rowError: { color: tokens.colorPaletteRedForeground1 },
});

function emptyForm(): TaskFormState {
  return {
    subject: '',
    description: '',
    baseDate: '',
    dueDate: '',
    finalDueDate: '',
    completedDate: '',
    status: '',
    assignedTo: '',
  };
}

/** Map a `create-task` feed item into an editable proposal (dates already 'YYYY-MM-DD' from DateOnly). */
function toProposal(item: RawQueueFeedItem): CreateTaskProposal {
  return {
    reviewLogId: item.reviewLogId as string,
    confidence: item.proposalConfidence,
    citation: { source: item.citationSource, locator: item.citationLocator, quotedText: item.citationQuotedText },
    form: {
      subject: item.taskName ?? '',
      description: item.taskDescription ?? '',
      baseDate: item.taskBaseDate ?? '',
      dueDate: item.taskDueDate ?? '',
      finalDueDate: item.taskFinalDueDate ?? '',
      completedDate: item.taskCompletedDate ?? '',
      status: typeof item.taskStatus === 'number' ? String(item.taskStatus) : '',
      assignedTo: item.taskAssignedTo ?? '',
    },
  };
}

function regardingParam(regarding?: ReconcileRegarding | null): string | null {
  if (!regarding || !regarding.entityType || !regarding.recordId) return null;
  return `${regarding.entityType}:${regarding.recordId}`;
}

/** FR-E5 fields for the 034 apply body — only set fields (omit empties). */
function buildApplyBody(form: TaskFormState): Record<string, unknown> {
  const b: Record<string, unknown> = {};
  if (form.dueDate) b.dueDate = form.dueDate;
  if (form.baseDate) b.baseDate = form.baseDate;
  if (form.finalDueDate) b.finalDueDate = form.finalDueDate;
  if (form.completedDate) b.completedDate = form.completedDate;
  if (form.status) b.status = Number(form.status);
  if (form.assignedTo) b.assignedTo = form.assignedTo;
  return b;
}

/** Full ad-hoc create body (056b) — subject + regarding + the FR-E5 fields. */
function buildAdHocBody(form: TaskFormState, regarding: ReconcileRegarding): Record<string, unknown> {
  return {
    subject: form.subject,
    description: form.description || undefined,
    regardingEntity: regarding.entityType,
    regardingRecordId: regarding.recordId,
    ...buildApplyBody(form),
  };
}

type LoadState = 'gated' | 'loading' | 'error' | 'ready';

export const TaskReconcileTab: React.FC<TaskReconcileTabProps> = ({
  regarding,
  communicationId,
  authenticatedFetch = defaultAuthenticatedFetch,
  onCitationClick,
  onTaskResolved,
  className,
}) => {
  const s = useStyles();
  const scope = regardingParam(regarding);

  const [state, setState] = React.useState<LoadState>(scope ? 'loading' : 'gated');
  const [proposals, setProposals] = React.useState<CreateTaskProposal[]>([]);
  // Local edits keyed by reviewLogId (proposals) or 'adhoc'.
  const [forms, setForms] = React.useState<Record<string, TaskFormState>>({});
  const [adhoc, setAdhoc] = React.useState<TaskFormState | null>(null);
  const [busyId, setBusyId] = React.useState<string | null>(null);
  const [rowError, setRowError] = React.useState<Record<string, string>>({});

  const load = React.useCallback(async () => {
    if (!scope) {
      setState('gated');
      setProposals([]);
      return;
    }
    setState('loading');
    try {
      const res = await authenticatedFetch(`/communications/queue-feed?regarding=${encodeURIComponent(scope)}`);
      const data = (await res.json()) as RawQueueFeedResult;
      const mapped = (data.items ?? [])
        .filter(i => i.kind === CREATE_TASK_KIND && i.communicationId === communicationId && i.reviewLogId)
        .map(toProposal);
      setProposals(mapped);
      setForms(Object.fromEntries(mapped.map(p => [p.reviewLogId, p.form])));
      setAdhoc(null);
      setRowError({});
      setState('ready');
    } catch {
      setState('error');
    }
  }, [scope, communicationId, authenticatedFetch]);

  React.useEffect(() => {
    void load();
  }, [load]);

  const patchForm = React.useCallback((key: string, patch: Partial<TaskFormState>) => {
    if (key === 'adhoc') {
      setAdhoc(prev => ({ ...(prev ?? emptyForm()), ...patch }));
    } else {
      setForms(prev => ({ ...prev, [key]: { ...prev[key], ...patch } }));
    }
  }, []);

  const removeProposal = React.useCallback((reviewLogId: string) => {
    setProposals(prev => prev.filter(p => p.reviewLogId !== reviewLogId));
  }, []);

  const post = React.useCallback(
    (url: string, body: Record<string, unknown>) =>
      authenticatedFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      }),
    [authenticatedFetch]
  );

  // Accept a PROPOSAL. Unchanged identity → 034 apply; edited identity → 056b ad-hoc + 055b dismiss.
  const acceptProposal = React.useCallback(
    async (p: CreateTaskProposal) => {
      const form = forms[p.reviewLogId] ?? p.form;
      if (!form.subject.trim()) {
        setRowError(prev => ({ ...prev, [p.reviewLogId]: 'A task name is required.' }));
        return;
      }
      const identityEdited = form.subject !== p.form.subject || form.description !== p.form.description;
      setBusyId(p.reviewLogId);
      setRowError(prev => ({ ...prev, [p.reviewLogId]: '' }));
      try {
        if (identityEdited && regarding) {
          // The reviewer authored a different task → create ad-hoc, then close the original proposal.
          await post(
            `/communications/${encodeURIComponent(communicationId)}/create-task`,
            buildAdHocBody(form, regarding)
          );
          try {
            await authenticatedFetch(`/communications/proposals/${encodeURIComponent(p.reviewLogId)}/dismiss`, {
              method: 'POST',
            });
          } catch {
            /* best-effort: the task was created; a failed dismiss just leaves the proposal to be rejected manually. */
          }
        } else {
          await post(
            `/communications/proposals/${encodeURIComponent(p.reviewLogId)}/create-task/apply`,
            buildApplyBody(form)
          );
        }
        removeProposal(p.reviewLogId);
        onTaskResolved?.(p.reviewLogId, 'applied');
      } catch {
        setRowError(prev => ({ ...prev, [p.reviewLogId]: 'Create failed — try again.' }));
      } finally {
        setBusyId(null);
      }
    },
    [forms, regarding, communicationId, post, authenticatedFetch, removeProposal, onTaskResolved]
  );

  const rejectProposal = React.useCallback(
    async (p: CreateTaskProposal) => {
      setBusyId(p.reviewLogId);
      setRowError(prev => ({ ...prev, [p.reviewLogId]: '' }));
      try {
        await authenticatedFetch(`/communications/proposals/${encodeURIComponent(p.reviewLogId)}/dismiss`, {
          method: 'POST',
        });
        removeProposal(p.reviewLogId);
        onTaskResolved?.(p.reviewLogId, 'rejected');
      } catch {
        setRowError(prev => ({ ...prev, [p.reviewLogId]: 'Reject failed — try again.' }));
      } finally {
        setBusyId(null);
      }
    },
    [authenticatedFetch, removeProposal, onTaskResolved]
  );

  const holdProposal = React.useCallback(
    (p: CreateTaskProposal) => {
      removeProposal(p.reviewLogId);
      onTaskResolved?.(p.reviewLogId, 'held');
    },
    [removeProposal, onTaskResolved]
  );

  // Create an AD-HOC task (056b). No reviewLogId, no dismiss.
  const createAdHoc = React.useCallback(async () => {
    const form = adhoc ?? emptyForm();
    if (!form.subject.trim()) {
      setRowError(prev => ({ ...prev, adhoc: 'A task name is required.' }));
      return;
    }
    if (!regarding) return;
    setBusyId('adhoc');
    setRowError(prev => ({ ...prev, adhoc: '' }));
    try {
      await post(`/communications/${encodeURIComponent(communicationId)}/create-task`, buildAdHocBody(form, regarding));
      setAdhoc(null);
      onTaskResolved?.(null, 'applied');
    } catch {
      setRowError(prev => ({ ...prev, adhoc: 'Create failed — try again.' }));
    } finally {
      setBusyId(null);
    }
  }, [adhoc, regarding, communicationId, post, onTaskResolved]);

  const rootClass = className ? `${s.root} ${className}` : s.root;

  if (state === 'gated') {
    return (
      <div className={rootClass} data-testid="task-reconcile-gated">
        <div className={s.state} role="note">
          <Text>Confirm the related record first.</Text>
          <Caption1>Tasks become available once this email&apos;s Related-to record is confirmed.</Caption1>
        </div>
      </div>
    );
  }
  if (state === 'loading') {
    return (
      <div className={rootClass}>
        <div className={s.state} role="status">
          <Spinner size="tiny" label="Loading tasks…" />
        </div>
      </div>
    );
  }
  if (state === 'error') {
    return (
      <div className={rootClass}>
        <div className={s.state} role="alert" data-testid="task-reconcile-error">
          <Text>Couldn&apos;t load tasks.</Text>
          <Button appearance="secondary" size="small" onClick={() => void load()}>
            Retry
          </Button>
        </div>
      </div>
    );
  }

  const renderFields = (key: string, form: TaskFormState) => (
    <>
      <Field label="Task name" required>
        <Input
          value={form.subject}
          disabled={busyId === key}
          data-testid="task-reconcile-name"
          onChange={(_, d) => patchForm(key, { subject: d.value })}
        />
      </Field>
      <Field label="Description">
        <Textarea
          value={form.description}
          disabled={busyId === key}
          data-testid="task-reconcile-description"
          onChange={(_, d) => patchForm(key, { description: d.value })}
        />
      </Field>
      <div className={s.dateGrid}>
        <Field label="Base date">
          <Input
            type="date"
            value={form.baseDate}
            disabled={busyId === key}
            data-testid="task-reconcile-basedate"
            onChange={(_, d) => patchForm(key, { baseDate: d.value })}
          />
        </Field>
        <Field label="Due date">
          <Input
            type="date"
            value={form.dueDate}
            disabled={busyId === key}
            data-testid="task-reconcile-duedate"
            onChange={(_, d) => patchForm(key, { dueDate: d.value })}
          />
        </Field>
        <Field label="Final due date">
          <Input
            type="date"
            value={form.finalDueDate}
            disabled={busyId === key}
            data-testid="task-reconcile-finalduedate"
            onChange={(_, d) => patchForm(key, { finalDueDate: d.value })}
          />
        </Field>
        <Field label="Completed date">
          <Input
            type="date"
            value={form.completedDate}
            disabled={busyId === key}
            data-testid="task-reconcile-completeddate"
            onChange={(_, d) => patchForm(key, { completedDate: d.value })}
          />
        </Field>
      </div>
      <div className={s.dateGrid}>
        <Field label="Status">
          <Select
            value={form.status}
            disabled={busyId === key}
            data-testid="task-reconcile-status"
            onChange={(_, d) => patchForm(key, { status: d.value })}
          >
            <option value="">(unset)</option>
            {STATUS_OPTIONS.map(o => (
              <option key={o.value} value={String(o.value)}>
                {o.label}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Assigned to (user id)">
          <Input
            value={form.assignedTo}
            disabled={busyId === key}
            data-testid="task-reconcile-assignedto"
            onChange={(_, d) => patchForm(key, { assignedTo: d.value })}
          />
        </Field>
      </div>
    </>
  );

  return (
    <div className={rootClass} data-testid="task-reconcile-list">
      <div className={s.header}>
        <div className={s.headerText}>
          <Body1>Tasks</Body1>
          <Caption1>Review a proposed task, edit it, then accept — or add your own.</Caption1>
        </div>
        <Button
          appearance="secondary"
          size="small"
          icon={<AddRegular />}
          data-testid="task-reconcile-new"
          disabled={adhoc !== null}
          onClick={() => setAdhoc(emptyForm())}
        >
          New task
        </Button>
      </div>

      {/* Ad-hoc form (opened by "+ New task") — standard FormModal (ADR-050). */}
      <FormModal
        open={adhoc !== null}
        onClose={() => {
          if (busyId !== 'adhoc') setAdhoc(null);
        }}
        onSubmit={() => void createAdHoc()}
        title="New task"
        submitLabel="Create task"
        cancelLabel="Cancel"
        busy={busyId === 'adhoc'}
      >
        {adhoc !== null ? (
          <div data-testid="task-reconcile-adhoc-card">
            {renderFields('adhoc', adhoc)}
            {rowError.adhoc ? (
              <Caption1 className={s.rowError} role="alert">
                {rowError.adhoc}
              </Caption1>
            ) : null}
          </div>
        ) : null}
      </FormModal>

      {proposals.length === 0 && adhoc === null ? (
        <div className={s.state} role="note" data-testid="task-reconcile-empty">
          <Text>No tasks proposed for this record.</Text>
        </div>
      ) : null}

      {proposals.map(p => {
        const key = p.reviewLogId;
        const form = forms[key] ?? p.form;
        const busy = busyId === key;
        const err = rowError[key];
        const hasCitation = Boolean(p.citation?.quotedText || p.citation?.locator || p.citation?.source);
        return (
          <div key={key} className={s.card} data-testid="task-reconcile-card">
            <div className={s.cardHeader}>
              <Text weight="semibold">Proposed task</Text>
              {typeof p.confidence === 'number' ? (
                <Badge appearance="tint" color="informative" data-testid="task-reconcile-confidence">
                  {Math.round(p.confidence * 100)}%
                </Badge>
              ) : null}
            </div>

            {renderFields(key, form)}

            {hasCitation ? (
              <div className={s.meta}>
                <Link16Regular aria-hidden />
                <Link
                  as="button"
                  data-testid="task-reconcile-citation"
                  onClick={() => p.citation && onCitationClick?.(p.citation)}
                >
                  {p.citation?.locator || p.citation?.source || 'View source'}
                </Link>
              </div>
            ) : null}

            {err ? (
              <Caption1 className={s.rowError} role="alert">
                {err}
              </Caption1>
            ) : null}

            <div className={s.actions}>
              <Button
                appearance="primary"
                size="small"
                icon={busy ? <Spinner size="tiny" /> : <CheckmarkRegular />}
                disabled={busy}
                data-testid="task-reconcile-accept"
                onClick={() => void acceptProposal(p)}
              >
                Create
              </Button>
              <Button
                appearance="secondary"
                size="small"
                icon={<DismissRegular />}
                disabled={busy}
                data-testid="task-reconcile-reject"
                onClick={() => void rejectProposal(p)}
              >
                Reject
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<ClockRegular />}
                disabled={busy}
                data-testid="task-reconcile-hold"
                onClick={() => holdProposal(p)}
              >
                Hold
              </Button>
            </div>
          </div>
        );
      })}
    </div>
  );
};
TaskReconcileTab.displayName = 'TaskReconcileTab';

export interface TaskReconcileModalProps extends TaskReconcileTabProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  uiScale?: number;
}

/**
 * The email-record-form dual-use mount (ADR-050). Wraps {@link TaskReconcileTab} in
 * the `FormModal` preset. Per-card Accept/Reject/Hold + "+ New task" act immediately
 * inside the body, so the footer primary is a plain "Done" (it closes).
 */
export const TaskReconcileModal: React.FC<TaskReconcileModalProps> = ({
  open,
  onClose,
  title = 'Tasks',
  uiScale,
  ...tabProps
}) => (
  <FormModal
    open={open}
    onClose={onClose}
    onSubmit={onClose}
    title={title}
    submitLabel="Done"
    cancelLabel="Close"
    uiScale={uiScale}
  >
    <TaskReconcileTab {...tabProps} />
  </FormModal>
);
TaskReconcileModal.displayName = 'TaskReconcileModal';

export default TaskReconcileTab;
