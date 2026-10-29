/**
 * RulePage - the read-only Ontology rule page (task 100; spec FR-57; #43, #44).
 *
 * Shows one rule as the owner listed it: the condition in words and in its formal form, the wording, the decision plan, the scope,
 * the noise settings, how it behaves, its versions and its change history. The controls are On / Off, Retire and, when the host offers
 * the rule wizard (task 102), New version: editing a rule is the wizard's job, not this page's. Both controls call routes that run as the caller, so Dataverse refuses a
 * user without the Spaarke Ontology Administrator role; a refusal is shown here and the rule is left as it was.
 *
 * Off stops the rule from being evaluated and leaves its open items for the next pass; Retire closes them at once and needs a reason.
 * A retired rule shows its reason, and a rule that is inactive for any reason has neither control (a switch there would be refused). A condition or plan the BFF refused to describe is shown as that refusal,
 * never as a guess. Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  Switch,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowLeftRegular } from '@fluentui/react-icons';
import { StatusBadge } from '../../StatusBadge';
import { RetireRuleDialog } from './RetireRuleDialog';
import {
  describeActionRate,
  describeLastFired,
  describeOpenItems,
  formatWhen,
  lastFiredLabel,
  severityTone,
  stateTone,
  unreadNumbers,
} from './ruleDisplay';
import type { OntologyRulesService, RuleDetail, RuleRetiredResult } from './rulesService';

export interface RulePageProps {
  service: OntologyRulesService;
  policyId: string;
  /** Back to the list. */
  onBack: () => void;
  /** Open the rule wizard on a new version of this rule. When omitted the page offers no New version control. */
  onNewVersion?: (detail: RuleDetail) => void;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    minWidth: 0,
  },
  top: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  titleBlock: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minWidth: 0,
  },
  badges: {
    display: 'flex',
    columnGap: tokens.spacingHorizontalS,
    alignItems: 'center',
  },
  controls: {
    display: 'flex',
    columnGap: tokens.spacingHorizontalM,
    alignItems: 'center',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    borderTopStyle: 'solid',
    borderTopWidth: tokens.strokeWidthThin,
    borderTopColor: tokens.colorNeutralStroke2,
  },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'max-content minmax(0, 1fr)',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXXS,
    margin: 0,
  },
  fact: {
    margin: 0,
  },
  term: {
    color: tokens.colorNeutralForeground3,
  },
  clause: {
    display: 'flex',
    flexDirection: 'column',
    paddingBottom: tokens.spacingVerticalXS,
  },
  formal: {
    fontFamily: tokens.fontFamilyMonospace,
    color: tokens.colorNeutralForeground3,
    overflowWrap: 'anywhere',
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  list: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalXL,
  },
  centered: {
    display: 'flex',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXL,
  },
});

const Facts: React.FC<{ rows: ReadonlyArray<readonly [string, React.ReactNode]> }> = ({ rows }) => {
  const styles = useStyles();
  return (
    <dl className={styles.facts}>
      {rows.map(([term, value]) => (
        <React.Fragment key={term}>
          <dt className={styles.fact}>
            <Text size={200} className={styles.term}>
              {term}
            </Text>
          </dt>
          <dd className={styles.fact}>
            <Text>{value}</Text>
          </dd>
        </React.Fragment>
      ))}
    </dl>
  );
};

export const RulePage: React.FC<RulePageProps> = ({ service, policyId, onBack, onNewVersion }) => {
  const styles = useStyles();
  const [detail, setDetail] = React.useState<RuleDetail | undefined>(undefined);
  const [error, setError] = React.useState<string | undefined>(undefined);
  const [loading, setLoading] = React.useState(true);
  const [actionError, setActionError] = React.useState<string | undefined>(undefined);
  const [saving, setSaving] = React.useState(false);
  const [retireOpen, setRetireOpen] = React.useState(false);

  const load = React.useCallback(async () => {
    setLoading(true);
    setError(undefined);
    const answer = await service.get(policyId);
    if (answer.ok) {
      setDetail(answer.data);
    } else {
      setDetail(undefined);
      setError(answer.message);
    }
    setLoading(false);
  }, [service, policyId]);

  React.useEffect(() => {
    void load();
  }, [load]);

  const onToggle = React.useCallback(
    async (enabled: boolean) => {
      setSaving(true);
      setActionError(undefined);
      const answer = await service.setEnabled(policyId, enabled);
      if (answer.ok) {
        await load();
      } else {
        setActionError(answer.message);
      }
      setSaving(false);
    },
    [service, policyId, load]
  );

  const onRetire = React.useCallback((reason: string) => service.retire(policyId, reason), [service, policyId]);

  const onRetired = React.useCallback(
    (_result: RuleRetiredResult) => {
      setRetireOpen(false);
      void load();
    },
    [load]
  );

  const back = (
    <Button appearance="subtle" icon={<ArrowLeftRegular />} onClick={onBack} data-testid="rule-page-back">
      All rules
    </Button>
  );

  if (loading && !detail) {
    return (
      <div className={styles.root}>
        {back}
        <div className={styles.centered}>
          <Spinner label="Loading rule" />
        </div>
      </div>
    );
  }

  if (!detail) {
    return (
      <div className={styles.root}>
        {back}
        <MessageBar intent="error" data-testid="rule-page-error">
          <MessageBarBody>
            {error ?? 'The rule could not be read.'}{' '}
            <Button appearance="transparent" size="small" onClick={() => void load()}>
              Try again
            </Button>
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  const { summary, condition, wording, plan, scope, noise, behaviour, versions, history } = detail;
  const retired = summary.state === 'Retired';
  // Retired, or inactive for any other reason (statecode 1): the BFF refuses a switch there (409), so the controls are not offered.
  const inactive = summary.state === 'Retired' || summary.state === 'Inactive';

  return (
    <div className={styles.root} data-testid="ontology-rule-page">
      {back}

      <div className={styles.top}>
        <div className={styles.titleBlock}>
          <Text size={500} weight="semibold">
            {summary.code}
          </Text>
          <Text className={styles.muted}>{summary.name}</Text>
        </div>
        <div className={styles.badges}>
          <StatusBadge label={summary.lane} tone="info" />
          <StatusBadge label={summary.state} tone={stateTone(summary.state)} />
          <StatusBadge label={summary.severity} tone={severityTone(summary.severity)} />
        </div>
        {inactive ? null : (
          <div className={styles.controls}>
            {onNewVersion === undefined ? null : (
              <Button appearance="primary" onClick={() => onNewVersion(detail)} disabled={saving} data-testid="rule-new-version-button">
                New version
              </Button>
            )}
            <Switch
              label={summary.enabled ? 'On' : 'Off'}
              checked={summary.enabled}
              disabled={saving}
              onChange={(_event, data) => void onToggle(data.checked)}
              data-testid="rule-enabled-switch"
            />
            <Button appearance="secondary" onClick={() => setRetireOpen(true)} disabled={saving} data-testid="rule-retire-button">
              Retire
            </Button>
          </div>
        )}
      </div>

      {actionError ? (
        <MessageBar intent="error" data-testid="rule-action-error">
          <MessageBarBody>{actionError}</MessageBarBody>
        </MessageBar>
      ) : null}

      {retired ? (
        <MessageBar intent="warning" data-testid="rule-retired-note">
          <MessageBarBody>
            This rule is retired{behaviour.retiredReason ? `: ${behaviour.retiredReason}` : '.'}
          </MessageBarBody>
        </MessageBar>
      ) : inactive ? (
        <MessageBar intent="info" data-testid="rule-inactive-note">
          <MessageBarBody>This rule is inactive, so it cannot be switched or retired here.</MessageBarBody>
        </MessageBar>
      ) : (
        <Text size={200} className={styles.muted}>
          Off stops the rule; its open items close at the next pass. Retire closes them now and needs a reason.
        </Text>
      )}

      <section className={styles.section} aria-label="Numbers">
        <Text weight="semibold">Numbers</Text>
        {detail.metricsAvailable ? null : (
          <Text size={200} className={styles.muted} data-testid="rule-metrics-unavailable">
            {`These numbers could not be read just now: ${unreadNumbers(detail.metricsAvailability).join(', ')}.`}
          </Text>
        )}
        <Facts
          rows={[
            ['Open items', describeOpenItems(summary.metrics, detail.metricsAvailability)],
            [lastFiredLabel(detail.lastFiredWindowDays), describeLastFired(summary.metrics, detail.metricsAvailability, detail.lastFiredWindowDays)],
            [
              `Action rate (${detail.actionRateWindowDays} days, this version)`,
              describeActionRate(summary.metrics, detail.minimumJudged, detail.metricsAvailability),
            ],
          ]}
        />
      </section>

      <section className={styles.section} aria-label="Condition">
        <Text weight="semibold">Condition</Text>
        {condition.refusal !== null ? (
          <MessageBar intent="warning" data-testid="rule-condition-refusal">
            <MessageBarBody>{condition.refusal}</MessageBarBody>
          </MessageBar>
        ) : (
          condition.clauses.map(clause => (
            <div key={clause.clauseId} className={styles.clause} data-testid="rule-clause">
              <Text>{clause.text}</Text>
              <Text size={200} className={styles.formal}>
                {clause.formal}
              </Text>
            </div>
          ))
        )}
      </section>

      <section className={styles.section} aria-label="Wording">
        <Text weight="semibold">Wording</Text>
        <Facts
          rows={[
            ['Headline', wording.headline ?? '-'],
            ['Message', wording.messageTemplate ?? '-'],
            ['Proposed action', wording.proposedAction ?? '-'],
          ]}
        />
      </section>

      <section className={styles.section} aria-label="Decision plan">
        <Text weight="semibold">Decision plan</Text>
        {plan.refusal !== null ? (
          <MessageBar intent="warning" data-testid="rule-plan-refusal">
            <MessageBarBody>{plan.refusal}</MessageBarBody>
          </MessageBar>
        ) : (
          <>
            <Text size={200} className={styles.term}>Actions offered</Text>
            <ul className={styles.list}>
              {plan.actions.map(action => (
                <li key={action.code}>
                  <Text>{action.label}</Text>
                </li>
              ))}
            </ul>
            {plan.nextSteps.length > 0 ? (
              <>
                <Text size={200} className={styles.term}>Next steps</Text>
                <ul className={styles.list}>
                  {plan.nextSteps.map(step => (
                    <li key={step.code}>
                      <Text>{step.label}</Text>
                    </li>
                  ))}
                </ul>
              </>
            ) : null}
            <Text size={200} className={styles.term}>Dismissal reasons</Text>
            <ul className={styles.list}>
              {plan.dismissalReasons.map(reason => (
                <li key={reason}>
                  <Text>{reason}</Text>
                </li>
              ))}
            </ul>
          </>
        )}
      </section>

      <section className={styles.section} aria-label="Scope">
        <Text weight="semibold">Scope</Text>
        <Facts
          rows={[
            ['Tenant', scope.tenant ?? 'All'],
            ['Matter', scope.matterName ?? (scope.matterId === null ? 'All' : scope.matterId)],
            ['Priority', scope.priority === null ? '-' : String(scope.priority)],
          ]}
        />
      </section>

      <section className={styles.section} aria-label="Noise">
        <Text weight="semibold">Noise</Text>
        <Facts
          rows={[
            [
              'Dismissals count toward suppression',
              noise.countsTowardSuppression
                ? `Yes: ${noise.suppressAfterDismissals} dismissals in ${noise.suppressionWindowDays} days quiet the rule for that record`
                : 'No',
            ],
            ['Quiet window', `${noise.quietWindowDays} days`],
          ]}
        />
      </section>

      <section className={styles.section} aria-label="Behaviour">
        <Text weight="semibold">Behaviour</Text>
        <Facts
          rows={[
            ['Lane', behaviour.lane],
            ['Severity', behaviour.severity],
            ['Work type', behaviour.workType ?? '-'],
            ['Applies to', behaviour.subjectType],
            ['State', behaviour.state],
          ]}
        />
      </section>

      <section className={styles.section} aria-label="Versions">
        <Text weight="semibold">Versions</Text>
        {versions.map(version => (
          <div key={version.versionId} data-testid="rule-version-row">
            <Text>
              v{version.number}
              {version.inForce ? ' (in force)' : ''}
              {version.headline ? `: ${version.headline}` : ''}
            </Text>{' '}
            <Text size={200} className={styles.muted}>
              {version.authoredBy ?? 'Unknown author'}, {formatWhen(version.createdOn)}
              {version.inForceTo === null ? '' : `, superseded ${formatWhen(version.inForceTo)}`}
            </Text>
          </div>
        ))}
      </section>

      <section className={styles.section} aria-label="Change history">
        <Text weight="semibold">Change history</Text>
        {history.map(entry => (
          <div key={`${entry.when ?? ''}|${entry.event}`} data-testid="rule-history-row">
            <Text>{entry.event}</Text>{' '}
            <Text size={200} className={styles.muted}>
              {entry.by ?? 'Unknown'}, {formatWhen(entry.when)}
            </Text>
          </div>
        ))}
      </section>

      <RetireRuleDialog
        open={retireOpen}
        ruleCode={summary.code}
        ruleName={summary.name}
        onRetire={onRetire}
        onCancel={() => setRetireOpen(false)}
        onDone={onRetired}
      />
    </div>
  );
};

RulePage.displayName = 'RulePage';
