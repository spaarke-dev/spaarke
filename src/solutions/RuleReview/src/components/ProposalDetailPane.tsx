/**
 * The detail of one drafted rule (ebilling-r1 task 077, FR-28): the proposed rule on the left, the clause it came from on the right
 * (the verbatim quote and its location), the edit controls, and Accept / Reject.
 *
 * The pane never decides anything itself: Accept and Reject call back to the page, which sends the version of the rule this pane
 * DISPLAYED (`proposal.rowVersion`) so the server refuses a decision on content the admin was not shown (076 re-check K3).
 * A form is keyed by the rule and its version, so a reload replaces it with the stored values.
 */

import * as React from 'react';
import {
  Button,
  Dropdown,
  Field,
  Input,
  Option,
  Text,
  Textarea,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { thinScrollbarStyle } from '@spaarke/ui-components';
import { ParamEditor } from './ParamEditor';
import { ProposalStateBadge } from './Notices';
import type { Consequence, ProposalView, RuleEdit } from '../services/ruleSetApi';
import {
  CONSEQUENCES,
  CONSEQUENCE_LABELS,
  buildBody,
  ruleTypeDef,
  ruleTypeLabel,
  sameBody,
  toParamValues,
  type ParamValues,
} from '../services/ruleTypes';

const useStyles = makeStyles({
  pane: {
    display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, height: '100%', overflowY: 'auto',
    padding: tokens.spacingHorizontalL, boxSizing: 'border-box', ...thinScrollbarStyle,
  },
  empty: {
    display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%', color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalXXL, textAlign: 'center',
  },
  header: { display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
  title: { flexGrow: 1, minWidth: 0 },
  columns: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalL, alignItems: 'stretch' },
  card: {
    flex: '1 1 280px', minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM, borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`, backgroundColor: tokens.colorNeutralBackground1,
  },
  clauseCard: { backgroundColor: tokens.colorNeutralBackground2 },
  reasonCard: {
    display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS, padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium, border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  reasonText: { whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' },
  cardTitle: { color: tokens.colorNeutralForeground2 },
  quote: {
    margin: 0, padding: tokens.spacingHorizontalM, borderLeft: `${tokens.spacingHorizontalXS} solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere',
  },
  muted: { color: tokens.colorNeutralForeground3 },
  warn: { color: tokens.colorStatusWarningForeground1 },
  pre: {
    margin: 0, padding: tokens.spacingHorizontalS, backgroundColor: tokens.colorNeutralBackground3, borderRadius: tokens.borderRadiusSmall,
    fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, overflowX: 'auto',
  },
  list: { margin: 0, paddingLeft: tokens.spacingHorizontalL },
  actions: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS, alignItems: 'center' },
  spacer: { flexGrow: 1 },
});

export interface ProposalDetailPaneProps {
  proposal: ProposalView | null;
  /** False when a call returned 403: the controls are hidden and the rule is only shown. */
  canWrite: boolean;
  /** False for a retired rule set (the server refuses changes to it). */
  editable: boolean;
  busy: boolean;
  onAccept: (proposal: ProposalView) => void;
  onReject: (proposal: ProposalView) => void;
  onSave: (proposal: ProposalView, edit: RuleEdit) => Promise<void>;
}

export const ProposalDetailPane: React.FC<ProposalDetailPaneProps> = ({ proposal, ...rest }) => {
  const s = useStyles();
  if (!proposal) {
    return (
      <div className={s.empty} data-testid="detail-empty">
        <Text>Select a rule to see it beside the clause it came from.</Text>
      </div>
    );
  }

  // Keyed by the rule and the version it was read at: a reload (after a conflict, a save or a decision) resets the form.
  return <RuleForm key={`${proposal.policyId}:${proposal.versionId}:${proposal.rowVersion ?? 'none'}`} proposal={proposal} {...rest} />;
};

interface FormProps extends Omit<ProposalDetailPaneProps, 'proposal'> {
  proposal: ProposalView;
}

const RuleForm: React.FC<FormProps> = ({ proposal, canWrite, editable, busy, onAccept, onReject, onSave }) => {
  const s = useStyles();
  const def = ruleTypeDef(proposal.ruleType);
  const mayEdit = canWrite && editable;

  const [params, setParams] = React.useState<ParamValues>(() => (def ? toParamValues(def, proposal.ruleBody) : {}));
  const [consequence, setConsequence] = React.useState<string>(proposal.consequence ?? '');
  const [headline, setHeadline] = React.useState(proposal.shortHeadline ?? '');
  const [template, setTemplate] = React.useState(proposal.messageTemplate ?? '');
  const [quote, setQuote] = React.useState(proposal.clauseQuote ?? '');
  const [location, setLocation] = React.useState(proposal.clauseLocation ?? '');
  const [errors, setErrors] = React.useState<Record<string, string>>({});

  // What the form would send: only the members that differ from the stored rule (an empty PATCH is refused by the server).
  const built = def ? buildBody(def, params) : null;
  const edit = React.useMemo<RuleEdit>(() => {
    const e: RuleEdit = {};
    if (built?.ok && !sameBody(built.body, proposal.ruleBody)) e.ruleBody = built.body;
    if (consequence && consequence !== (proposal.consequence ?? '')) e.consequence = consequence as Consequence;
    if (headline !== (proposal.shortHeadline ?? '') && headline.trim()) e.shortHeadline = headline;
    if (template !== (proposal.messageTemplate ?? '') && template.trim()) e.messageTemplate = template;
    if (quote !== (proposal.clauseQuote ?? '') && quote.trim()) e.clauseReference = quote;
    if (location !== (proposal.clauseLocation ?? '') && location.trim()) e.clauseLocation = location;
    return e;
  }, [built, consequence, headline, template, quote, location, proposal]);
  // A text field emptied by hand cannot be sent (the server keeps the stored text), but it IS an unsaved change: it keeps Accept off
  // and Discard on, and Save says why nothing was sent.
  const blanked = React.useMemo<Record<string, string>>(() => {
    const b: Record<string, string> = {};
    const emptied = (now: string, stored: string | null): boolean => !now.trim() && !!stored?.trim();
    if (emptied(headline, proposal.shortHeadline)) b.headline = 'The headline cannot be empty.';
    if (emptied(template, proposal.messageTemplate)) b.template = 'The message cannot be empty.';
    if (emptied(quote, proposal.clauseQuote)) b.quote = 'The quote cannot be empty.';
    if (emptied(location, proposal.clauseLocation)) b.location = 'The location cannot be empty.';
    return b;
  }, [headline, template, quote, location, proposal]);
  const dirty = Object.keys(edit).length > 0 || Object.keys(blanked).length > 0;
  const clauseEdited = edit.clauseReference !== undefined || edit.clauseLocation !== undefined;

  const reset = (): void => {
    setParams(def ? toParamValues(def, proposal.ruleBody) : {});
    setConsequence(proposal.consequence ?? '');
    setHeadline(proposal.shortHeadline ?? '');
    setTemplate(proposal.messageTemplate ?? '');
    setQuote(proposal.clauseQuote ?? '');
    setLocation(proposal.clauseLocation ?? '');
    setErrors({});
  };

  const save = async (): Promise<void> => {
    if ((built && !built.ok) || Object.keys(blanked).length > 0) {
      setErrors({ ...(built && !built.ok ? built.errors : {}), ...blanked });
      return;
    }

    setErrors({});
    await onSave(proposal, edit);
  };

  const decidable = mayEdit && !proposal.inForce && !dirty;
  const decideHint = proposal.inForce
    ? 'This version is in force. Edit it to draft a new version.'
    : dirty
      ? 'Save or discard your edits before deciding.'
      : null;

  return (
    <div className={s.pane} data-testid="detail-pane">
      <div className={s.header}>
        <div className={s.title}>
          <Text weight="semibold" size={400} block>
            {proposal.name}
          </Text>
          <Text size={200} className={s.muted}>
            {proposal.policyCode} · {ruleTypeLabel(proposal.ruleType)} · v{proposal.versionNumber}
          </Text>
        </div>
        <ProposalStateBadge proposal={proposal} />
      </div>

      {proposal.state === 'Rejected' && (
        <section className={s.reasonCard} aria-label="Rejection reason" data-testid="rejection-reason">
          <Text weight="semibold" block>
            Rejected: reason
          </Text>
          <Text block className={s.reasonText}>
            {proposal.rejectionReason ?? 'No reason was recorded.'}
          </Text>
        </section>
      )}

      {proposal.validationErrors.length > 0 && (
        <div data-testid="detail-validation">
          <Text className={s.warn} weight="semibold" block>
            This rule would be refused as it stands:
          </Text>
          <ul className={s.list}>
            {proposal.validationErrors.map(e => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        </div>
      )}

      <div className={s.columns}>
        <section className={s.card} aria-label="Proposed rule" data-testid="detail-rule">
          <Text weight="semibold" className={s.cardTitle}>
            Proposed rule
          </Text>
          {proposal.descriptionText ? (
            <Text data-testid="detail-description">{proposal.descriptionText}</Text>
          ) : (
            <Text className={s.muted}>{proposal.descriptionRefusal ?? 'No plain-language description is available for this rule.'}</Text>
          )}

          <Field label="Consequence" hint="What happens when an invoice breaks this rule.">
            <Dropdown
              value={consequence ? (CONSEQUENCE_LABELS[consequence] ?? consequence) : ''}
              selectedOptions={consequence ? [consequence] : []}
              disabled={!mayEdit}
              onOptionSelect={(_, d) => setConsequence(d.optionValue ?? '')}
              data-testid="edit-consequence"
            >
              {CONSEQUENCES.map(c => (
                <Option key={c} value={c}>
                  {CONSEQUENCE_LABELS[c]}
                </Option>
              ))}
            </Dropdown>
          </Field>

          {def ? (
            <ParamEditor
              def={def}
              values={params}
              errors={errors}
              disabled={!mayEdit}
              onChange={(key, value) => setParams(prev => ({ ...prev, [key]: value }))}
            />
          ) : (
            <Field label="Parameters" hint="This rule type is not known to the page, so its parameters are shown as stored.">
              <pre className={s.pre}>{JSON.stringify(proposal.ruleBody, null, 2)}</pre>
            </Field>
          )}

          <Field label="Headline" validationMessage={errors.headline} validationState={errors.headline ? 'error' : 'none'}>
            <Input value={headline} disabled={!mayEdit} onChange={(_, d) => setHeadline(d.value)} data-testid="edit-headline" />
          </Field>
          <Field
            label="Message shown with a finding"
            validationMessage={errors.template}
            validationState={errors.template ? 'error' : 'none'}
            hint="Placeholders such as {{rule.maxHoursPerDay}} are filled in from the parameters.">
            <Textarea
              resize="vertical"
              value={template}
              disabled={!mayEdit}
              onChange={(_, d) => setTemplate(d.value)}
              data-testid="edit-template"
            />
          </Field>
        </section>

        <section className={mergeClasses(s.card, s.clauseCard)} aria-label="Quoted clause" data-testid="detail-clause">
          <Text weight="semibold" className={s.cardTitle}>
            Quoted clause
          </Text>
          {mayEdit ? (
            <>
              <Field label="Quote" hint="The verbatim text of the guideline." validationMessage={errors.quote} validationState={errors.quote ? 'error' : 'none'}>
                <Textarea resize="vertical" rows={6} value={quote} onChange={(_, d) => setQuote(d.value)} data-testid="edit-quote" />
              </Field>
              <Field label="Location" hint="Section or page." validationMessage={errors.location} validationState={errors.location ? 'error' : 'none'}>
                <Input value={location} onChange={(_, d) => setLocation(d.value)} data-testid="edit-location" />
              </Field>
              {clauseEdited && (
                <Text size={200} className={s.warn}>
                  Changing the clause sends the rule back for review. Firms may be shown this text once the rule set is published.
                </Text>
              )}
            </>
          ) : (
            <>
              {proposal.clauseQuote ? (
                <blockquote className={s.quote} data-testid="clause-quote">
                  {proposal.clauseQuote}
                </blockquote>
              ) : (
                <Text className={s.muted}>No clause was quoted for this rule.</Text>
              )}
              {proposal.clauseLocation && (
                <Text size={200} className={s.muted} data-testid="clause-location">
                  {proposal.clauseLocation}
                </Text>
              )}
            </>
          )}
        </section>
      </div>

      {mayEdit && (
        <div className={s.actions} data-testid="detail-actions">
          <Button appearance="primary" disabled={busy || !dirty} onClick={() => void save()} data-testid="save-edit">
            Save changes
          </Button>
          <Button disabled={busy || !dirty} onClick={reset} data-testid="reset-edit">
            Discard changes
          </Button>
          <span className={s.spacer} />
          <Button
            appearance="secondary"
            disabled={busy || !decidable || proposal.state === 'Rejected'}
            onClick={() => onReject(proposal)}
            data-testid="reject"
          >
            Reject
          </Button>
          <Button
            appearance="primary"
            disabled={busy || !decidable || proposal.state === 'Accepted'}
            onClick={() => onAccept(proposal)}
            data-testid="accept"
          >
            Accept
          </Button>
        </div>
      )}
      {mayEdit && decideHint && (
        <Text size={200} className={s.muted}>
          {decideHint}
        </Text>
      )}
      {!canWrite && (
        <Text size={200} className={s.muted} data-testid="detail-readonly">
          Read only: you do not have the billing administrator right, so this rule cannot be changed.
        </Text>
      )}
    </div>
  );
};
