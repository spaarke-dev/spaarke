/**
 * FieldUpdateReconcileTab.tsx
 *
 * The Pillar E **Fields** reconcile tab (Job B) — email-communication-intelligence-r2
 * task 055, spec FR-E4 (§104) + NFR-10 (§119). For the CONFIRMED record only, it
 * lists that record's OPEN Job B field-update proposals from the FR-17 queue-feed —
 * each showing the CURRENT value → the AI's MATCHED value (EDITABLE before Accept),
 * a clickable citation (jumps the task-053 reader via task 054), and confidence —
 * with **Accept / Reject / Hold**:
 *
 *   • Accept → `POST /api/communications/proposals/{reviewLogId}/apply` with the
 *     (possibly-edited) value in `{ overrideValue }` (task 055a). Applied under
 *     audit; the row leaves Proposed.
 *   • Reject → `POST /api/communications/proposals/{reviewLogId}/dismiss` (task
 *     055b). Terminal-dismiss under audit; the row leaves Proposed and does NOT
 *     reappear.
 *   • Hold  → NO API call — "leave Proposed" (a client-only skip; the proposal
 *     deliberately reappears on the next queue-feed load).
 *
 * NFR-10 GATING: proposals are actionable ONLY once the record's Related-to
 * association is confirmed (task 052). With no `regarding`, the tab renders a
 * "confirm the related record first" state and no proposal is actionable. When
 * the confirmed record CHANGES (an override), the list RE-SCOPES (re-fetches) to
 * the new record — a proposal is NEVER applied against an unconfirmed/overridden
 * record (the apply/dismiss endpoints also re-gate server-side; this is the UI
 * half of the invariant).
 *
 * DUAL-USE (ADR-022): `FieldUpdateReconcileTab` renders its proposal cards INLINE
 * — the browse-shell right-pane mount (task 053), where a citation click must
 * highlight the VISIBLE left reader (so the edit surface must NOT be a modal over
 * the reader). `FieldUpdateReconcileModal` wraps the SAME body in the ADR-050
 * `FormModal` preset for the email-record-form mount (opened by a form command,
 * where there is no reader pane). Both are exported from this file (the POML's
 * single named output). See notes/055-*.
 *
 * ADR-021 (Fluent v9 semantic tokens; light + dark), ADR-022 (`React.FC` +
 * standard hooks — dual-use on the PCF email-form mount), ADR-012 (the tab fetches
 * a KNOWN BFF endpoint via injected `authenticatedFetch`, mirroring `EmailBodyView`
 * — the host supplies the confirmed record + the fetch seam; the tab owns only the
 * proposal reconcile flow).
 */
import * as React from 'react';
import {
  Badge,
  Body1,
  Button,
  Caption1,
  Input,
  Link,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkRegular, DismissRegular, ClockRegular, Link16Regular } from '@fluentui/react-icons';
import { FormModal } from '@spaarke/ui-components';
import { authenticatedFetch as defaultAuthenticatedFetch } from '@spaarke/auth';
import type { AuthenticatedFetchFn } from '../EmailBody/EmailBodyView.types';
import type { EmailCitation } from '../../logic/citations';

/** The confirmed association a proposal is scoped to (NFR-10). */
export interface ReconcileRegarding {
  /** The regarding entity logical name (e.g. `sprk_matter`). */
  entityType: string;
  /** The regarding record id (GUID string). */
  recordId: string;
}

/** The terminal/interim outcome reported to the host after a decision. */
export type ProposalOutcome = 'applied' | 'rejected' | 'held';

/** One Job B field-update proposal projected from the FR-17 queue-feed. */
export interface FieldUpdateProposal {
  /** `sprk_emailreviewlog` id — the apply/dismiss endpoints' target row. */
  reviewLogId: string;
  /** Proposal target entity logical name. */
  targetEntity?: string | null;
  /** Proposal target field logical name (allow-listed). */
  targetField?: string | null;
  /** Field type hint (Text/DateTime/Number/…) — display + input affordance only; the server coerces fail-loud. */
  fieldType?: string | null;
  /** Current (old) value on the record at proposal time. */
  oldValue?: string | null;
  /** The AI's matched (new) value — the editable seed. */
  newValue?: string | null;
  /** Proposal extraction confidence (DISPLAY ONLY). */
  confidence?: number | null;
  /** Human-readable reason the Action gave. */
  reason?: string | null;
  /** The proposal citation (task 054 anchor). */
  citation?: EmailCitation;
}

/**
 * Raw FR-17 queue-feed shape (the subset this tab reads). Minimal API returns
 * camelCase JSON; only `pending-proposal` items carry the proposal fields.
 */
interface RawQueueFeedItem {
  communicationId: string;
  kind: string;
  reviewLogId?: string | null;
  targetEntity?: string | null;
  targetField?: string | null;
  fieldType?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  proposalConfidence?: number | null;
  reason?: string | null;
  citationSource?: string | null;
  citationLocator?: string | null;
  citationQuotedText?: string | null;
}

interface RawQueueFeedResult {
  items?: ReadonlyArray<RawQueueFeedItem> | null;
  count?: number;
}

const PENDING_PROPOSAL_KIND = 'pending-proposal';

export interface FieldUpdateReconcileTabProps {
  /**
   * The confirmed association (NFR-10). `null`/`undefined` ⇒ GATE: the tab shows
   * "confirm the related record first" and no proposal is actionable. Changing it
   * (an override) RE-SCOPES the proposal list (re-fetch for the new record).
   */
  regarding?: ReconcileRegarding | null;
  /**
   * The communication under review — the tab filters the feed to THIS
   * communication's Job B `pending-proposal` items.
   */
  communicationId: string;
  /**
   * Injected authenticated fetch (defaults to `@spaarke/auth`). Test/host seam,
   * mirroring `EmailBodyView`. Pass a relative path (no `/api` prefix) — the fn
   * resolves the BFF base URL and throws on non-2xx.
   */
  authenticatedFetch?: AuthenticatedFetchFn;
  /**
   * Fired when a proposal's citation is clicked — the host lifts it into the
   * browse-shell's `activeCitation` so the reader jumps to + highlights the exact
   * passage (task 054). Omitted on the email-form mount (no reader pane).
   */
  onCitationClick?: (citation: EmailCitation) => void;
  /** Fired after a proposal reaches an outcome — the host may refresh other surfaces. */
  onProposalResolved?: (reviewLogId: string, outcome: ProposalOutcome) => void;
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
  header: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
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
    gap: tokens.spacingVerticalXS,
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
  fieldName: { fontWeight: tokens.fontWeightSemibold, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' },
  valueRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  oldValue: { color: tokens.colorNeutralForeground3, textDecorationLine: 'line-through' },
  arrow: { color: tokens.colorNeutralForeground3 },
  editInput: { minWidth: '160px', flex: '1 1 160px' },
  meta: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, marginTop: tokens.spacingVerticalXS },
  rowError: { color: tokens.colorPaletteRedForeground1 },
});

/** Project a `pending-proposal` feed item into the view model. */
function toProposal(item: RawQueueFeedItem): FieldUpdateProposal {
  return {
    reviewLogId: item.reviewLogId as string,
    targetEntity: item.targetEntity,
    targetField: item.targetField,
    fieldType: item.fieldType,
    oldValue: item.oldValue,
    newValue: item.newValue,
    confidence: item.proposalConfidence,
    reason: item.reason,
    citation: {
      source: item.citationSource,
      locator: item.citationLocator,
      quotedText: item.citationQuotedText,
    },
  };
}

/** `{entityType}:{recordId}` scope token, or null when unconfirmed. */
function regardingParam(regarding?: ReconcileRegarding | null): string | null {
  if (!regarding || !regarding.entityType || !regarding.recordId) return null;
  return `${regarding.entityType}:${regarding.recordId}`;
}

type LoadState = 'gated' | 'loading' | 'error' | 'ready';

export const FieldUpdateReconcileTab: React.FC<FieldUpdateReconcileTabProps> = ({
  regarding,
  communicationId,
  authenticatedFetch = defaultAuthenticatedFetch,
  onCitationClick,
  onProposalResolved,
  className,
}) => {
  const s = useStyles();
  const scope = regardingParam(regarding);

  const [state, setState] = React.useState<LoadState>(scope ? 'loading' : 'gated');
  const [proposals, setProposals] = React.useState<FieldUpdateProposal[]>([]);
  const [edited, setEdited] = React.useState<Record<string, string>>({});
  const [busyId, setBusyId] = React.useState<string | null>(null);
  const [rowError, setRowError] = React.useState<Record<string, string>>({});

  // Fetch (and RE-SCOPE on `scope`/`communicationId` change). Keyed strictly on the
  // CONFIRMED scope — when the association is overridden, `scope` changes and the
  // list reloads for the new record; the old record's proposals are dropped (NFR-10).
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
        .filter(i => i.kind === PENDING_PROPOSAL_KIND && i.communicationId === communicationId && i.reviewLogId)
        .map(toProposal);
      setProposals(mapped);
      setEdited({});
      setRowError({});
      setState('ready');
    } catch {
      setState('error');
    }
  }, [scope, communicationId, authenticatedFetch]);

  React.useEffect(() => {
    void load();
  }, [load]);

  const removeRow = React.useCallback((reviewLogId: string) => {
    setProposals(prev => prev.filter(p => p.reviewLogId !== reviewLogId));
  }, []);

  const handleAccept = React.useCallback(
    async (p: FieldUpdateProposal) => {
      const overrideValue = edited[p.reviewLogId] ?? p.newValue ?? '';
      setBusyId(p.reviewLogId);
      setRowError(prev => ({ ...prev, [p.reviewLogId]: '' }));
      try {
        await authenticatedFetch(`/communications/proposals/${encodeURIComponent(p.reviewLogId)}/apply`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ overrideValue }),
        });
        removeRow(p.reviewLogId);
        onProposalResolved?.(p.reviewLogId, 'applied');
      } catch {
        setRowError(prev => ({ ...prev, [p.reviewLogId]: 'Apply failed — try again.' }));
      } finally {
        setBusyId(null);
      }
    },
    [edited, authenticatedFetch, removeRow, onProposalResolved]
  );

  const handleReject = React.useCallback(
    async (p: FieldUpdateProposal) => {
      setBusyId(p.reviewLogId);
      setRowError(prev => ({ ...prev, [p.reviewLogId]: '' }));
      try {
        await authenticatedFetch(`/communications/proposals/${encodeURIComponent(p.reviewLogId)}/dismiss`, {
          method: 'POST',
        });
        removeRow(p.reviewLogId);
        onProposalResolved?.(p.reviewLogId, 'rejected');
      } catch {
        setRowError(prev => ({ ...prev, [p.reviewLogId]: 'Reject failed — try again.' }));
      } finally {
        setBusyId(null);
      }
    },
    [authenticatedFetch, removeRow, onProposalResolved]
  );

  // Hold = "leave Proposed": NO API call. Skip it from the current review; the
  // proposal deliberately reappears on the next queue-feed load.
  const handleHold = React.useCallback(
    (p: FieldUpdateProposal) => {
      removeRow(p.reviewLogId);
      onProposalResolved?.(p.reviewLogId, 'held');
    },
    [removeRow, onProposalResolved]
  );

  const rootClass = className ? `${s.root} ${className}` : s.root;

  if (state === 'gated') {
    return (
      <div className={rootClass} data-testid="field-reconcile-gated">
        <div className={s.state} role="note">
          <Text>Confirm the related record first.</Text>
          <Caption1>Field updates become available once this email&apos;s Related-to record is confirmed.</Caption1>
        </div>
      </div>
    );
  }

  if (state === 'loading') {
    return (
      <div className={rootClass}>
        <div className={s.state} role="status">
          <Spinner size="tiny" label="Loading field updates…" />
        </div>
      </div>
    );
  }

  if (state === 'error') {
    return (
      <div className={rootClass}>
        <div className={s.state} role="alert" data-testid="field-reconcile-error">
          <Text>Couldn&apos;t load field updates.</Text>
          <Button appearance="secondary" size="small" onClick={() => void load()}>
            Retry
          </Button>
        </div>
      </div>
    );
  }

  if (proposals.length === 0) {
    return (
      <div className={rootClass}>
        <div className={s.state} role="note" data-testid="field-reconcile-empty">
          <Text>No field updates proposed for this record.</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={rootClass} data-testid="field-reconcile-list">
      <div className={s.header}>
        <Body1>Field updates</Body1>
        <Caption1>Review each proposed change. You can edit a value before accepting it.</Caption1>
      </div>

      {proposals.map(p => {
        const busy = busyId === p.reviewLogId;
        const value = edited[p.reviewLogId] ?? p.newValue ?? '';
        const err = rowError[p.reviewLogId];
        const hasCitation = Boolean(p.citation?.quotedText || p.citation?.locator || p.citation?.source);
        return (
          <div key={p.reviewLogId} className={s.card} data-testid="field-reconcile-card">
            <div className={s.cardHeader}>
              <Text className={s.fieldName} title={p.targetField ?? undefined}>
                {p.targetField ?? '(field)'}
              </Text>
              {typeof p.confidence === 'number' ? (
                <Badge appearance="tint" color="informative" data-testid="field-reconcile-confidence">
                  {Math.round(p.confidence * 100)}%
                </Badge>
              ) : null}
            </div>

            <div className={s.valueRow}>
              <Text className={s.oldValue} data-testid="field-reconcile-old">
                {p.oldValue ? p.oldValue : '—'}
              </Text>
              <Text className={s.arrow} aria-hidden>
                →
              </Text>
              <Input
                className={s.editInput}
                value={value}
                disabled={busy}
                aria-label={`New value for ${p.targetField ?? 'field'}`}
                data-testid="field-reconcile-edit"
                onChange={(_, d) => setEdited(prev => ({ ...prev, [p.reviewLogId]: d.value }))}
              />
            </div>

            {hasCitation ? (
              <div className={s.meta}>
                <Link16Regular aria-hidden />
                <Link
                  as="button"
                  data-testid="field-reconcile-citation"
                  onClick={() => p.citation && onCitationClick?.(p.citation)}
                >
                  {p.citation?.locator || p.citation?.source || 'View source'}
                </Link>
              </div>
            ) : null}

            {p.reason ? <Caption1>{p.reason}</Caption1> : null}
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
                data-testid="field-reconcile-accept"
                onClick={() => void handleAccept(p)}
              >
                Accept
              </Button>
              <Button
                appearance="secondary"
                size="small"
                icon={<DismissRegular />}
                disabled={busy}
                data-testid="field-reconcile-reject"
                onClick={() => void handleReject(p)}
              >
                Reject
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<ClockRegular />}
                disabled={busy}
                data-testid="field-reconcile-hold"
                onClick={() => handleHold(p)}
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
FieldUpdateReconcileTab.displayName = 'FieldUpdateReconcileTab';

export interface FieldUpdateReconcileModalProps extends FieldUpdateReconcileTabProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — wired to the FormModal × / Cancel / Done. */
  onClose: () => void;
  /** Modal title (default "Field updates"). */
  title?: string;
  /** `--sprk-ui-scale` forwarded to the shell. */
  uiScale?: number;
}

/**
 * The email-record-form dual-use mount (ADR-050). Wraps {@link FieldUpdateReconcileTab}
 * in the canonical `FormModal` preset (Cancel-left, explicit dismiss). Per-proposal
 * Accept/Reject/Hold act immediately inside the body, so the footer primary is a
 * plain "Done" (it closes) — the modal is a container for the reconcile rows, not a
 * single deferred submit. On the form there is no reader pane, so `onCitationClick`
 * is typically omitted.
 */
export const FieldUpdateReconcileModal: React.FC<FieldUpdateReconcileModalProps> = ({
  open,
  onClose,
  title = 'Field updates',
  uiScale,
  ...tabProps
}) => {
  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={onClose}
      title={title}
      submitLabel="Done"
      cancelLabel="Close"
      uiScale={uiScale}
    >
      <FieldUpdateReconcileTab {...tabProps} />
    </FormModal>
  );
};
FieldUpdateReconcileModal.displayName = 'FieldUpdateReconcileModal';

export default FieldUpdateReconcileTab;
