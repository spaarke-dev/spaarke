/**
 * One rule set under review (ebilling-r1 task 077, FR-28): its rules in a grid beside the selected rule and the clause it was drafted
 * from; "Draft rules" (075), Accept / Reject / Edit per rule, Publish, assignment and "visible to firms".
 *
 * Every state change is a BFF call and is followed by a reload of the rule set, so what the admin sees is what the server stored. A
 * decision names the version of the rule that was displayed; when it is refused because the rule changed, the page reloads, says what
 * changed and decides nothing (`decideProposal`, 076 re-check K3).
 */

import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { ArrowLeftRegular, ArrowClockwiseRegular } from '@fluentui/react-icons';
import { PanelSplitter, useTwoPanelLayout } from '@spaarke/ui-components';
import { AssignmentPanel } from './AssignmentPanel';
import { NoticeBar, RuleSetStatusBadge, failureNotice, type Notice } from './Notices';
import { ProposalDetailPane } from './ProposalDetailPane';
import { ProposalsGrid } from './ProposalsGrid';
import { PublishModal } from './PublishModal';
import { RejectModal } from './RejectModal';
import { useFirmNames } from './useFirmNames';
import type { FirmLookup } from '../services/firmLookup';
import {
  countProposals,
  decideProposal,
  runDraft,
  saveEdit,
} from '../services/reviewActions';
import type {
  ApiFailure,
  NotConvertedClause,
  ProposalView,
  RuleEdit,
  RuleSetApi,
  RuleSetDetail,
  RuleSetScope,
} from '../services/ruleSetApi';

const useStyles = makeStyles({
  root: {
    display: 'flex', flexDirection: 'column', height: '100%', boxSizing: 'border-box', gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL, backgroundColor: tokens.colorNeutralBackground1, color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
  },
  header: { display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
  title: { flexGrow: 1, minWidth: 0 },
  muted: { color: tokens.colorNeutralForeground3 },
  workspace: {
    display: 'flex', flexGrow: 1, minHeight: '320px', overflow: 'hidden',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`, borderRadius: tokens.borderRadiusMedium,
  },
  primary: { minWidth: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' },
  detail: { minWidth: 0, overflow: 'hidden', backgroundColor: tokens.colorNeutralBackground2 },
  notConverted: { flexShrink: 0, maxHeight: '30%', overflow: 'auto' },
  clause: { margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' },
  centered: { display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' },
});

export interface RuleSetDetailViewProps {
  api: RuleSetApi;
  firmLookup: FirmLookup;
  ruleSetId: string;
  /** False after a 403: the controls that write are hidden. */
  canWrite: boolean;
  onBack: () => void;
  /** Every failure is reported up, so a 403 turns the page read-only and a disabled eBilling flag shows its notice. */
  onFailure: (failure: ApiFailure) => void;
  /** The list is out of date (a rule set was drafted, published, assigned ...). */
  onListChanged: () => void;
}

export const RuleSetDetailView: React.FC<RuleSetDetailViewProps> = ({
  api, firmLookup, ruleSetId, canWrite, onBack, onFailure, onListChanged,
}) => {
  const s = useStyles();
  const layout = useTwoPanelLayout({ defaultDetailWidth: 640, minPrimaryWidth: 360, minDetailWidth: 420, storageKey: 'ebilling-rulereview-panels' });

  const [detail, setDetail] = React.useState<RuleSetDetail | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [selected, setSelected] = React.useState<string | null>(null);
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [drafting, setDrafting] = React.useState(false);
  const [publishOpen, setPublishOpen] = React.useState(false);
  const [publishNotice, setPublishNotice] = React.useState<Notice | null>(null);
  const [rejecting, setRejecting] = React.useState<ProposalView | null>(null);
  const mounted = React.useRef(true);
  const firmNames = useFirmNames(firmLookup, [detail?.ruleSet.firmId ?? null]);

  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const fail = React.useCallback(
    (failure: ApiFailure, title?: string): void => {
      onFailure(failure);
      setNotice(failureNotice(failure, title));
    },
    [onFailure]
  );

  const apply = React.useCallback((next: RuleSetDetail): void => {
    setDetail(next);
    setSelected(prev => (prev && next.proposals.some(p => p.policyId === prev) ? prev : (next.proposals[0]?.policyId ?? null)));
  }, []);

  const load = React.useCallback(async (): Promise<void> => {
    setLoading(true);
    const result = await api.get(ruleSetId);
    if (!mounted.current) return;
    setLoading(false);
    if (result.ok) apply(result.value);
    else fail(result.failure, 'The rule set could not be loaded');
  }, [api, ruleSetId, apply, fail]);

  React.useEffect(() => {
    setDetail(null);
    setNotice(null);
    void load();
  }, [load]);

  const counts = detail ? countProposals(detail) : null;
  const proposal = detail?.proposals.find(p => p.policyId === selected) ?? null;
  const retired = detail?.ruleSet.status === 'Retired';

  // ── Draft rules (075) ──────────────────────────────────────────────────────
  const handleDraft = async (): Promise<void> => {
    setDrafting(true);
    setNotice({ intent: 'info', text: 'Drafting rules from the document. This can take a few minutes; the rules appear as they are stored.' });
    const outcome = await runDraft(api, ruleSetId, { onProgress: d => mounted.current && apply(d), isCancelled: () => !mounted.current });
    if (!mounted.current) return;
    setDrafting(false);
    onListChanged();
    if (outcome.kind === 'finished') {
      apply(outcome.detail);
      const left = Array.isArray(outcome.detail.notConverted) ? outcome.detail.notConverted.length : 0;
      setNotice({
        intent: 'success',
        title: 'Drafting finished',
        text: `${outcome.detail.proposals.length} rule(s) proposed${left > 0 ? `; ${left} clause(s) could not be converted and are listed below` : ''}. Review each rule beside its clause.`,
      });
    } else if (outcome.kind === 'timeout') {
      if (outcome.detail) apply(outcome.detail);
      setNotice({ intent: 'warning', title: 'Drafting is still running', text: 'Reload in a few minutes to see the rest of the rules.' });
    } else {
      fail(outcome.failure, 'Drafting could not be started');
    }
  };

  // ── Accept / Reject ────────────────────────────────────────────────────────
  const decide = async (shown: ProposalView, accept: boolean, reason = ''): Promise<void> => {
    setBusy(true);
    setNotice(null);
    const outcome = await decideProposal(api, ruleSetId, shown, accept, reason);
    if (!mounted.current) return;
    setBusy(false);
    setRejecting(null);
    const verb = accept ? 'accepted' : 'rejected';
    if (outcome.kind === 'decided') {
      if (outcome.detail) apply(outcome.detail);
      else void load();
      setNotice({ intent: 'success', text: `${shown.name} was ${verb}.` });
    } else if (outcome.kind === 'stale') {
      apply(outcome.detail);
      setNotice({
        intent: 'warning',
        title: `Nothing was ${verb}: the rule changed after you opened it`,
        text:
          outcome.changes.length > 0
            ? 'It has been reloaded. Review the change, then decide again:'
            : 'It has been reloaded. Review it again, then decide.',
        changes: outcome.changes,
      });
    } else {
      fail(outcome.failure, `The rule was not ${verb}`);
    }
  };

  // ── Edit ───────────────────────────────────────────────────────────────────
  const save = async (shown: ProposalView, edit: RuleEdit): Promise<void> => {
    setBusy(true);
    setNotice(null);
    const outcome = await saveEdit(api, ruleSetId, shown.policyId, edit);
    if (!mounted.current) return;
    setBusy(false);
    if (outcome.kind === 'saved') {
      if (outcome.detail) apply(outcome.detail);
      else void load();
      setNotice({
        intent: 'success',
        text: outcome.createdNewVersion
          ? 'Saved as a new version: the published version is unchanged. Accept the new version, then publish.'
          : 'Saved. The rule is back in review until you accept it.',
      });
    } else if (outcome.kind === 'stale') {
      apply(outcome.detail);
      setNotice({
        intent: 'warning',
        title: 'Your changes were not saved: the rule changed while you were editing',
        text: 'It has been reloaded. Make your change again if it is still needed.',
      });
    } else {
      fail(outcome.failure, 'The rule was not saved');
    }
  };

  // ── Publish ────────────────────────────────────────────────────────────────
  const publish = async (effectiveDate: string): Promise<void> => {
    setBusy(true);
    setPublishNotice(null);
    const result = await api.publish(ruleSetId, effectiveDate);
    if (!mounted.current) return;
    setBusy(false);
    if (result.ok) {
      setPublishOpen(false);
      onListChanged();
      setNotice({
        intent: 'success',
        title: 'Rule set published',
        text: `${result.value.published.length} rule(s) in force from ${result.value.effectiveDate}. ${result.value.clauseVisibilityNotice}`,
      });
      await load();
    } else {
      onFailure(result.failure);
      setPublishNotice(failureNotice(result.failure, 'Nothing was published'));
    }
  };

  // ── Assignment and visibility ──────────────────────────────────────────────
  const assign = async (scope: RuleSetScope, firmId: string | null): Promise<void> => {
    setBusy(true);
    const result = await api.assign(ruleSetId, scope, firmId);
    if (!mounted.current) return;
    setBusy(false);
    if (result.ok) {
      setDetail(prev => (prev ? { ...prev, ruleSet: result.value } : prev));
      onListChanged();
      setNotice({ intent: 'success', text: 'Assignment saved.' });
    } else {
      fail(result.failure, 'The assignment was not saved');
    }
  };

  const setVisibility = async (visible: boolean): Promise<void> => {
    setBusy(true);
    const result = await api.setVisibility(ruleSetId, visible);
    if (!mounted.current) return;
    setBusy(false);
    if (result.ok) {
      setDetail(prev => (prev ? { ...prev, ruleSet: result.value } : prev));
      onListChanged();
      setNotice({
        intent: 'success',
        text: result.value.visibleToFirms ? 'Firms can now see the clause quotes of this rule set.' : 'Firms will not see clause quotes from this rule set.',
      });
    } else {
      fail(result.failure, 'Visibility was not changed');
    }
  };

  if (loading && !detail) {
    return (
      <div className={mergeClasses(s.root, s.centered)} data-testid="detail-loading">
        <Spinner label="Loading rule set" />
      </div>
    );
  }

  if (!detail) {
    return (
      <div className={s.root}>
        <div className={s.header}>
          <Button icon={<ArrowLeftRegular />} appearance="subtle" onClick={onBack}>
            Rule sets
          </Button>
        </div>
        {notice && <NoticeBar notice={notice} />}
        <Button onClick={() => void load()} icon={<ArrowClockwiseRegular />}>
          Try again
        </Button>
      </div>
    );
  }

  const summary = detail.ruleSet;
  const notConverted = detail.notConverted;
  const draftable = canWrite && !retired && notConverted === null && !drafting;
  const notConvertedList: NotConvertedClause[] = Array.isArray(notConverted) ? notConverted : [];

  return (
    <div className={s.root} data-testid="ruleset-detail">
      <div className={s.header}>
        <Button icon={<ArrowLeftRegular />} appearance="subtle" onClick={onBack} data-testid="back-to-list">
          Rule sets
        </Button>
        <div className={s.title}>
          <Text as="h1" size={500} weight="semibold" block>
            {summary.name}
          </Text>
          <Text size={200} className={s.muted} data-testid="detail-counts">
            {counts ? `${counts.total} rule(s): ${counts.proposed} proposed, ${counts.accepted} accepted, ${counts.rejected} rejected` : ''}
          </Text>
        </div>
        <RuleSetStatusBadge status={summary.status} />
        <Button icon={<ArrowClockwiseRegular />} appearance="subtle" disabled={loading || busy} onClick={() => void load()} aria-label="Reload the rule set" />
        {draftable && (
          <Button disabled={busy} onClick={() => void handleDraft()} data-testid="draft-rules">
            Draft rules
          </Button>
        )}
        {drafting && <Spinner size="tiny" label="Drafting rules" labelPosition="after" data-testid="drafting" />}
        {canWrite && !retired && (
          <Button
            appearance="primary"
            disabled={busy || drafting || (counts?.toPublish ?? 0) === 0}
            onClick={() => {
              setPublishNotice(null);
              setPublishOpen(true);
            }}
            data-testid="publish"
          >
            Publish
          </Button>
        )}
      </div>

      {notice && <NoticeBar notice={notice} onDismiss={() => setNotice(null)} />}

      <AssignmentPanel
        ruleSet={summary}
        firmName={summary.firmId ? (firmNames[summary.firmId.toLowerCase()] ?? null) : null}
        firmLookup={firmLookup}
        canWrite={canWrite}
        busy={busy || drafting}
        onAssign={(scope, firmId) => void assign(scope, firmId)}
        onVisibility={visible => void setVisibility(visible)}
      />

      <div className={s.workspace} ref={layout.containerRef as React.RefObject<HTMLDivElement>}>
        <div className={s.primary} style={{ width: layout.primaryWidth }}>
          <ProposalsGrid
            proposals={detail.proposals}
            selectedPolicyId={selected}
            onSelect={setSelected}
            emptyMessage={
              drafting
                ? 'Drafting rules from the document...'
                : notConverted === null
                  ? canWrite
                    ? 'No rules yet. Choose "Draft rules" to propose rules from the guideline document.'
                    : 'No rules yet.'
                  : 'The document produced no rules.'
            }
          />
        </div>
        <PanelSplitter
          onMouseDown={layout.splitterHandlers.onMouseDown}
          onKeyDown={layout.splitterHandlers.onKeyDown}
          onDoubleClick={layout.splitterHandlers.onDoubleClick}
          isDragging={layout.isDragging}
          currentRatio={layout.currentRatio}
        />
        <div className={s.detail} style={{ width: layout.detailWidth }}>
          <ProposalDetailPane
            proposal={proposal}
            canWrite={canWrite}
            editable={!retired}
            busy={busy || drafting || loading}
            onAccept={p => void decide(p, true)}
            onReject={p => setRejecting(p)}
            onSave={save}
          />
        </div>
      </div>

      {notConverted !== null && (
        <Accordion collapsible className={s.notConverted} data-testid="not-converted">
          <AccordionItem value="nc">
            <AccordionHeader>Not converted to rules ({notConvertedList.length || (typeof notConverted === 'string' ? 1 : 0)})</AccordionHeader>
            <AccordionPanel>
              {typeof notConverted === 'string' ? (
                <Text>{notConverted}</Text>
              ) : notConvertedList.length === 0 ? (
                <Text className={s.muted}>Every clause the drafting could use became a rule.</Text>
              ) : (
                notConvertedList.map((c, i) => (
                  <div key={`${c.location ?? ''}-${i}`}>
                    {c.text && <p className={s.clause}>{c.text}</p>}
                    <Text size={200} className={s.muted}>
                      {c.location ? `${c.location} - ` : ''}
                      {c.reason}
                    </Text>
                  </div>
                ))
              )}
            </AccordionPanel>
          </AccordionItem>
        </Accordion>
      )}

      {counts && (
        <PublishModal
          open={publishOpen}
          ruleSetName={summary.name}
          visibleToFirms={summary.visibleToFirms}
          counts={counts}
          busy={busy}
          notice={publishNotice}
          onClose={() => !busy && setPublishOpen(false)}
          onPublish={date => void publish(date)}
        />
      )}

      <RejectModal
        open={rejecting !== null}
        ruleName={rejecting?.name ?? 'The rule'}
        busy={busy}
        onClose={() => !busy && setRejecting(null)}
        onReject={reason => rejecting && void decide(rejecting, false, reason)}
      />
    </div>
  );
};
