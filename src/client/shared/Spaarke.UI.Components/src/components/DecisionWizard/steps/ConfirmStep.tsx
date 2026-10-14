/**
 * ConfirmStep - the last step: every action the plan offered, taken or skipped; the Next steps added; the record class the
 * catalog derives and why; "Also resolve" (Decide lane only); and the Record decision button (the shell's primary button).
 *
 * Skip every action and this becomes a dismissal: a reason from the lane's closed list is required (Other also needs detail),
 * and the suppression effect is shown from task 034's (policy, core record) count.
 *
 * Wording is binding: "a new Decision Record", never a predicted number (#6); no claim that the record counts in a Report Card
 * (#37). Nothing has been written while this step shows (D-17). On a closed item the same step is the read-only record of what
 * was decided.
 */

import * as React from 'react';
import {
  Badge,
  Body1,
  Caption1,
  Checkbox,
  Field,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Select,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  actionScope,
  deriveRecordClass,
  excludedBy,
  fieldLabel,
  isDismissal,
  nextScope,
  stepValues,
  takenActions,
  takenNextSteps,
  undecidedSteps,
  visibleActions,
  withAlsoResolve,
  withDismissal,
} from '../decisionDraft';
import type { StepScope } from '../decisionDraft';
import {
  describeValues,
  describeWarning,
  describeWrites,
  failureHeading,
  failureMessage,
  suppressionEffect,
} from '../decisionDisplay';
import { useDecisionItem } from '../decisionItemContext';
import { composeIssueLine } from '../../Worklist/composeIssueLine';
import type { DecisionAction, DecisionCommitFailure, DecisionPlan } from '../decisionWizardTypes';
import { LockedNote } from './ParameterFields';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL },
  meta: { color: tokens.colorNeutralForeground3 },
  list: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    margin: 0,
    padding: 0,
    listStyle: 'none',
  },
  row: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  rowHead: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  values: {
    display: 'grid',
    gridTemplateColumns: 'minmax(110px, max-content) minmax(0, 1fr)',
    columnGap: tokens.spacingHorizontalM,
    margin: 0,
  },
  valueText: { margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' },
  box: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  landed: { margin: 0, paddingLeft: tokens.spacingHorizontalXL },
});

/** D-17: the user sees which writes landed. A failure writes no record, so the record is not listed as a landed write. */
export const FailurePanel: React.FC<{ failure: DecisionCommitFailure; plan: DecisionPlan }> = ({ failure, plan }) => {
  const styles = useStyles();
  const landed = describeWrites(failure.landed, failure, plan);
  const possibly = describeWrites(failure.possiblyLanded, failure, plan);
  return (
    <MessageBar intent="error" role="alert" data-testid="commit-failure">
      <MessageBarBody>
        <MessageBarTitle>{failureHeading(failure)}</MessageBarTitle>
        <div>{failureMessage(failure)}</div>
        {landed.length > 0 ? (
          <div data-testid="landed-writes">
            <Text weight="semibold">These were already carried out:</Text>
            <ul className={styles.landed}>
              {landed.map(line => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          </div>
        ) : null}
        {possibly.length > 0 ? (
          <div data-testid="possibly-landed-writes">
            <Text weight="semibold">These may have been carried out (no clear answer):</Text>
            <ul className={styles.landed}>
              {possibly.map(line => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          </div>
        ) : null}
        <div data-testid="record-status">
          {failure.recordMayExist
            ? 'The Decision Record may exist; check the item before trying again.'
            : failure.httpStatus === 0
              ? 'It is not known whether the decision was recorded.'
              : 'No Decision Record was written.'}
        </div>
        {failure.sentButNotRecorded ? <div>A message was sent but the decision was not recorded.</div> : null}
        {failure.correlationId ? <Caption1 className={styles.meta}>Reference: {failure.correlationId}</Caption1> : null}
      </MessageBarBody>
    </MessageBar>
  );
};

export const ConfirmStep: React.FC = () => {
  const {
    item,
    plan,
    draft,
    update,
    resolution,
    suppression,
    suppressionFailed,
    failure,
    locked,
    closedIds,
    recordResult,
    today,
  } = useDecisionItem();
  const styles = useStyles();
  const coreName = item.core?.name?.trim() || null;

  // What the route said while closing: its own warnings and each Signal's (an item it could not close is named, not hidden).
  const warnings = recordResult
    ? Array.from(
        new Set(
          [...recordResult.warnings, ...recordResult.signals.map(s => s.warning).filter((w): w is string => !!w)].map(
            describeWarning
          )
        )
      )
    : [];

  // ---- Closed item: the read-only record of the decision ---------------------------------------
  if (resolution) {
    const nameOf = (scope: StepScope) => (code: string) => fieldLabel(draft, scope, code);
    return (
      <div className={styles.root} data-testid="step-confirm">
        <Text as="h2" size={500} weight="semibold">
          {resolution.resolutionType === 'Dismissed' ? 'Dismissal' : 'Decision'}
        </Text>
        {resolution.recordClass ? (
          <Body1 data-testid="record-class">
            Recorded as{' '}
            {resolution.recordClass === 'Judgement' ? 'a judgement' : `a ${resolution.recordClass.toLowerCase()}`}.
          </Body1>
        ) : null}
        {resolution.reason ? <Body1 data-testid="dismissal-reason">Reason: {resolution.reason}</Body1> : null}
        {resolution.leftOpen ? (
          <Body1 data-testid="left-open">
            The inquiry was sent and the decision recorded. This item stays open until the work is done.
          </Body1>
        ) : null}
        {warnings.length > 0 ? (
          <MessageBar intent="warning" data-testid="record-warnings">
            <MessageBarBody>
              <MessageBarTitle>The decision was recorded. Please check:</MessageBarTitle>
              <ul className={styles.landed}>
                {warnings.map(w => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            </MessageBarBody>
          </MessageBar>
        ) : null}
        <ul className={styles.list} data-testid="offered-actions">
          {plan.actions.map(a => {
            const step = resolution.steps?.find(s => s.code === a.code);
            const rows = step?.taken ? describeValues(a, step.values ?? null, nameOf(actionScope(a.code))) : [];
            return (
              <li key={a.code} className={styles.row}>
                <div className={styles.rowHead}>
                  <Text weight="semibold">{a.label}</Text>
                  <Badge appearance="tint" color={step?.taken ? 'success' : 'subtle'}>
                    {step?.taken ? 'Taken' : 'Skipped'}
                  </Badge>
                </div>
                {rows.map(r => (
                  <Caption1 key={r.code} className={styles.meta}>
                    {r.label}: {r.text}
                  </Caption1>
                ))}
              </li>
            );
          })}
        </ul>
        {(resolution.followOns ?? []).length > 0 ? (
          <div>
            <Text weight="semibold">Next steps created</Text>
            <ul className={styles.list}>
              {(resolution.followOns ?? []).map((f, i) => (
                <li key={`${f.code}-${f.id ?? i}`}>
                  <Body1>{plan.nextSteps.find(n => n.code === f.code)?.label ?? f.code}</Body1>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </div>
    );
  }

  // ---- Open item: the review ---------------------------------------------------------------------
  const dismissal = isDismissal(plan, draft);
  const derived = deriveRecordClass(plan, draft);
  const visible = visibleActions(plan, draft);
  const taken = takenActions(plan, draft);
  const next = takenNextSteps(plan, draft);
  const missing = undecidedSteps(plan, draft);
  const reason = plan.dismissalReasons.find(r => r.code === draft.dismissalCode);
  const others = plan.lane === 'Decide' && item.core ? item.otherOpenItems.filter(o => !closedIds.has(o.signalId)) : [];

  const rowsFor = (a: DecisionAction, scope: ReturnType<typeof actionScope>) =>
    describeValues(a, stepValues(a, draft, scope), code => fieldLabel(draft, scope, code));

  return (
    <div className={styles.root} data-testid="step-confirm">
      <Text as="h2" size={500} weight="semibold">
        {dismissal ? 'Dismiss and record' : 'Confirm'}
      </Text>
      <Body1>
        {dismissal
          ? 'You skipped every action, so this is recorded as a dismissal. Nothing else will be written.'
          : 'Recording writes a new Decision Record and carries out what you took. Nothing has been written yet.'}
      </Body1>

      <ul className={styles.list} data-testid="offered-actions">
        {plan.actions.map(a => {
          const isTaken = taken.some(t => t.code === a.code);
          const removedBy = visible.some(v => v.code === a.code) ? null : excludedBy(plan, draft, a.code);
          const rows = isTaken ? rowsFor(a, actionScope(a.code)) : [];
          return (
            <li key={a.code} className={styles.row} data-testid={`offered-${a.code}`}>
              <div className={styles.rowHead}>
                <Text weight="semibold">{a.label}</Text>
                <Badge appearance="tint" color={isTaken ? 'success' : 'subtle'}>
                  {isTaken ? 'Taken' : 'Skipped'}
                </Badge>
                {removedBy ? (
                  <Caption1 className={styles.meta}>Cannot be combined with {removedBy.label}.</Caption1>
                ) : null}
              </div>
              {rows.length > 0 ? (
                <dl className={styles.values}>
                  {rows.map(r => (
                    <React.Fragment key={r.code}>
                      <dt>
                        <Caption1 className={styles.meta}>{r.label}</Caption1>
                      </dt>
                      <dd className={styles.valueText}>
                        <Body1>{r.text}</Body1>
                      </dd>
                    </React.Fragment>
                  ))}
                </dl>
              ) : null}
            </li>
          );
        })}
      </ul>

      {next.length > 0 ? (
        <div data-testid="next-steps-added">
          <Text weight="semibold">Next steps added</Text>
          <ul className={styles.list}>
            {next.map(n => {
              const rows = rowsFor(n, nextScope(n.code));
              return (
                <li key={n.code} className={styles.row}>
                  <Body1>{n.label}</Body1>
                  {rows.map(r => (
                    <Caption1 key={r.code} className={styles.meta}>
                      {r.label}: {r.text}
                    </Caption1>
                  ))}
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}

      <div className={styles.box} data-testid="record-class">
        <Text weight="semibold">
          Recorded as:{' '}
          {derived.recordClass === 'Judgement'
            ? 'a judgement'
            : derived.recordClass === 'Routine'
              ? 'routine work'
              : 'a dismissal'}
        </Text>
        <Caption1 className={styles.meta} data-testid="record-class-why">
          {derived.why}
        </Caption1>
      </div>

      {dismissal ? (
        <div className={styles.box} data-testid="dismissal">
          <Field label="Reason for dismissing" required>
            <Select
              disabled={locked}
              value={draft.dismissalCode}
              onChange={(_e, data) => update(d => withDismissal(d, data.value))}
              aria-label="Reason for dismissing"
            >
              <option value="">Choose a reason...</option>
              {plan.dismissalReasons.map(r => (
                <option key={r.code} value={r.code}>
                  {r.label}
                </option>
              ))}
            </Select>
          </Field>
          {reason?.requiresDetail ? (
            <Field label="Detail" required>
              <Textarea
                rows={3}
                disabled={locked}
                value={draft.dismissalDetail}
                onChange={(_e, data) => update(d => withDismissal(d, d.dismissalCode, data.value))}
              />
            </Field>
          ) : null}
          <Caption1 className={styles.meta} data-testid="suppression-effect">
            {suppressionEffect(suppression, suppressionFailed, reason, coreName)}
          </Caption1>
        </div>
      ) : null}

      {others.length > 0 ? (
        <div data-testid="also-resolve">
          <Text weight="semibold">Also resolve</Text>
          <div>
            <Caption1 className={styles.meta}>
              Close these other open items{coreName ? ` on ${coreName}` : ''} with this decision.
            </Caption1>
          </div>
          {others.map(o => (
            <Checkbox
              key={o.signalId}
              disabled={locked}
              checked={draft.alsoResolve.includes(o.signalId)}
              onChange={(_e, data) => update(d => withAlsoResolve(d, o.signalId, data.checked === true))}
              label={composeIssueLine(o, today).headline}
            />
          ))}
        </div>
      ) : null}

      {missing.length > 0 ? (
        <MessageBar intent="warning" data-testid="undecided">
          <MessageBarBody>Go back and take or skip: {missing.join(', ')}.</MessageBarBody>
        </MessageBar>
      ) : null}

      {locked ? <LockedNote /> : null}
      {failure ? <FailurePanel failure={failure} plan={plan} /> : null}
    </div>
  );
};
