/**
 * One rule in force on the record, in the shared WizardShell (ebilling-r1 task 084b; prototype `RuleWizard.tsx`):
 *
 *   Rule -> On this record -> Impact -> Confirm
 *
 * "On this record" offers Run the published rule and Exclude here (with a firm scope and a reason), on task 089's `rule-overrides/exclude`
 * route and the override removal. There is NO "Change for this record" (task 084c) and no "New values" step.
 *
 *   - P-3: a rule a parent excluded (or changed) shows that as applied, and "Run the published rule" is disabled with the reason.
 *   - O-20: a rule that names one firm can only be excluded for that firm; the firm field is fixed.
 *
 * The wizard does not know whether the caller may write: the route decides, and a refusal is shown with its title with the wizard still open.
 */

import * as React from 'react';
import { Caption1, Dropdown, Field, MessageBar, MessageBarBody, Option, Text, Textarea } from '@fluentui/react-components';
import { StatusBadge } from '@spaarke/ui-components';
import type { CoreRoute, RuleItem, RuleMark } from '../../services/recordProceduresApi';
import type { OverridesApi } from '../../services/overridesApi';
import type { FirmOption } from '../../services/firmChoices';
import { levelLabel, recordWords, ruleBadges, versionLabel } from '../../services/model';
import {
  currentRuleChoice,
  excludeBlocker,
  fixedFirmId,
  inheritedOverride,
  isBaseRetired,
  keepBlocker,
  ownChange,
  ownExclude,
  planRule,
  type RuleChoice,
} from './plan';
import { ChoiceGroup, KeyValues, useWizardStyles, type ChoiceOption } from './steps';
import { WizardFrame, type WizardNav } from './WizardFrame';
import { stampWords } from './words';

export interface RuleWizardProps {
  row: RuleItem;
  core: CoreRoute;
  recordId: string;
  record: { table: string; name: string | null };
  /** The firms the "For invoices from" field offers besides "All firms". */
  firms: readonly FirmOption[];
  api: OverridesApi;
  nav?: WizardNav;
  onChanged: () => void;
  onClose: () => void;
}

interface Form {
  choice: RuleChoice;
  reason: string;
  /** '' = every firm. */
  firmId: string;
}

const initialForm = (r: RuleItem): Form => ({
  choice: currentRuleChoice(r),
  reason: ownExclude(r)?.reason ?? '',
  firmId: fixedFirmId(r) ?? ownExclude(r)?.firm?.id ?? '',
});

const ruleKey = (r: RuleItem): string => `${r.policyId}:${r.policyVersionId ?? ''}`;

/** Where a mark applies: "on matter 2026-LIT-4471" for a parent's, nothing for this record's own. */
function whereWords(m: RuleMark): string {
  return (m.distance ?? 0) > 0 && m.chainRecord ? ` on ${recordWords(m.chainRecord.table, m.chainRecord.name)}` : '';
}

/** What the rule shows now, in one line (the Impact step's "before"). */
export function ruleNow(r: RuleItem): string {
  const badges = ruleBadges(r).map(b => b.label).join(', ');
  return r.parameterSummary ? `${badges} · ${r.parameterSummary}` : badges;
}

export const RuleWizard: React.FC<RuleWizardProps> = ({ row, core, recordId, record, firms, api, nav, onChanged, onClose }) => {
  const s = useWizardStyles();
  const [form, setForm] = React.useState<Form>(() => initialForm(row));
  const [seen, setSeen] = React.useState(ruleKey(row));
  if (seen !== ruleKey(row)) {
    // The wizard moved to another rule (‹ ›): its form starts from that row.
    setSeen(ruleKey(row));
    setForm(initialForm(row));
  }
  const set = (patch: Partial<Form>) => setForm(f => ({ ...f, ...patch }));

  const current = currentRuleChoice(row);
  const inherited = inheritedOverride(row);
  const retired = isBaseRetired(row);
  const keepBlock = keepBlocker(row);
  const excludeBlock = excludeBlocker(row);
  const fixedFirm = row.firmScope;
  const plan = planRule(row, form.choice, { reason: form.reason, firmId: form.firmId });
  const canChoose = plan.steps.length > 0;
  const recordLabel = recordWords(record.table, record.name);
  const sendsFirmId = fixedFirmId(row) ?? (form.firmId || null);
  const firmName = fixedFirm?.name ?? firms.find(f => f.id.toLowerCase() === form.firmId.toLowerCase())?.name ?? null;

  // ── Step 1: the rule as the server describes it ──────────────────────────
  const excludedMarks = row.marks.filter(m => m.kind === 'Excluded');
  const changed = row.marks.find(m => m.kind === 'Changed');
  const stale = row.marks.find(m => m.kind === 'Stale');
  const reviewStep = (
    <div className={s.step} data-testid="rule-step">
      <KeyValues
        rows={[
          [
            'State',
            <span key="state" className={s.change}>
              {ruleBadges(row).map((b, i) => (
                <StatusBadge key={`${b.label}-${i}`} label={b.label} tone={b.tone} />
              ))}
            </span>,
          ],
          ['Type', row.ruleType ?? '—'],
          [
            'From',
            <span key="from" className={s.stack}>
              <Text>{[row.procedure?.name, row.procedure?.version ? versionLabel(row.procedure.version) : null].filter(Boolean).join(' · ') || '—'}</Text>
              <Caption1 className={s.muted}>{[row.versionNumber !== null ? `rule v${row.versionNumber}` : null, levelLabel(row.level, row.firmScope?.name)].filter(Boolean).join(' · ')}</Caption1>
            </span>,
          ],
          ['Applies to', row.firmScope ? `${row.firmScope.name ?? 'One firm'} invoices only` : 'Every firm'],
          ['Runs here', row.inForce ? (row.parameterSummary ?? '—') : 'Nothing: this rule does not run on this record'],
          ...(changed?.baseParameterSummary ? ([['Published values', changed.baseParameterSummary]] as const) : []),
        ]}
      />
      {changed && (
        <MessageBar intent="info" layout="multiline">
          <MessageBarBody>
            Changed for this record{whereWords(changed)}
            {changed.firm?.name ? `, for ${changed.firm.name} invoices` : ''}.{changed.stamp ? ` ${stampWords({ stamp: changed.stamp })}` : ''}
            {changed.reason ? `: "${changed.reason}"` : ''}
          </MessageBarBody>
        </MessageBar>
      )}
      {stale && changed && (
        <MessageBar intent="warning" layout="multiline">
          <MessageBarBody>
            The published rule has been updated since: this record rule was copied from v{stale.basedOn?.number}, and the published rule is now v{stale.currentBase?.number}. The record
            rule keeps running until it is updated or removed.
          </MessageBarBody>
        </MessageBar>
      )}
      {retired && (
        <MessageBar intent="warning" layout="multiline">
          <MessageBarBody>The published rule is no longer in this Procedure's version. The record rule keeps running until it is removed.</MessageBarBody>
        </MessageBar>
      )}
      {excludedMarks.map((m, i) => (
        <MessageBar key={`${m.overrideRowId ?? i}`} intent="info" layout="multiline" data-testid="excluded-note">
          <MessageBarBody>
            Excluded{m.firm?.name ? ` for ${m.firm.name} invoices` : ''}
            {whereWords(m)}.{m.stamp ? ` ${stampWords({ stamp: m.stamp })}` : ''}
            {m.reason ? `: "${m.reason}"` : ''}
          </MessageBarBody>
        </MessageBar>
      ))}
    </div>
  );

  // ── Step 2: the choices ──────────────────────────────────────────────────
  const firmField = fixedFirm ? (
    <Field label="For invoices from" hint={`The rule names ${fixedFirm.name ?? 'one firm'}, so it can only be excluded for that firm (O-20).`}>
      <Dropdown value={fixedFirm.name ?? 'The rule’s firm'} selectedOptions={[fixedFirm.id]} disabled data-testid="rule-firm">
        <Option value={fixedFirm.id} text={fixedFirm.name ?? 'The rule’s firm'}>
          {fixedFirm.name ?? 'The rule’s firm'}
        </Option>
      </Dropdown>
    </Field>
  ) : (
    <Field label="For invoices from">
      <Dropdown
        value={form.firmId ? (firms.find(f => f.id.toLowerCase() === form.firmId.toLowerCase())?.name ?? 'One firm') : 'All firms'}
        selectedOptions={[form.firmId]}
        onOptionSelect={(_, d) => set({ firmId: d.optionValue ?? '' })}
        data-testid="rule-firm"
      >
        <Option value="" text="All firms">
          All firms
        </Option>
        {firms.map(f => (
          <Option key={f.id} value={f.id} text={f.name}>
            {f.name}
          </Option>
        ))}
      </Dropdown>
    </Field>
  );

  const options: Array<ChoiceOption<RuleChoice>> = [];
  if (inherited) {
    const verb = inherited.kind === 'Excluded' ? 'excluded' : 'changed';
    options.push({
      value: 'parent',
      label: 'As set on the parent',
      caption: `The rule is ${verb}${whereWords(inherited)}${inherited.reason ? `: "${inherited.reason}"` : ''}. That ${inherited.kind === 'Excluded' ? 'exclusion' : 'change'} applies to this record. Change it on the parent.`,
    });
  }
  options.push({
    value: 'keep',
    label: retired ? 'Remove the change' : 'Run the published rule',
    caption:
      keepBlock ??
      (current === 'keep'
        ? `As published${row.parameterSummary ? `: ${row.parameterSummary}` : ''}.`
        : `Removes this record's ${ownChange(row) ? 'change' : 'exclusion'}. The published rule runs again for invoices checked from now on.`),
    disabled: keepBlock !== null,
  });
  options.push({
    value: 'exclude',
    label: 'Exclude here',
    caption: excludeBlock ?? "The rule does not run for this record's invoices, or for one firm's.",
    disabled: excludeBlock !== null,
    body: (
      <>
        {firmField}
        <Field label="Reason" required>
          <Textarea value={form.reason} onChange={(_, d) => set({ reason: d.value })} resize="vertical" />
        </Field>
      </>
    ),
  });

  const modeStep = (
    <div className={s.step} data-testid="mode-step">
      <ChoiceGroup ariaLabel="On this record" options={options} value={form.choice} onChange={choice => set({ choice })} />
    </div>
  );

  // ── Impact and Confirm ────────────────────────────────────────────────────
  const before = ruleNow(row);
  const afterKeep = ownChange(row)?.baseParameterSummary ?? row.parameterSummary;
  const after =
    form.choice === 'exclude'
      ? `Excluded${firmName ? ` for ${firmName} invoices` : ''}: the rule does not run`
      : form.choice === 'keep'
        ? `In force${afterKeep ? ` · ${afterKeep}` : ''}`
        : before;
  const removeOnly = plan.steps.length > 0 && plan.steps.every(x => x.kind === 'remove');
  const twoCalls = plan.steps.length > 1;

  const confirmRows: Array<readonly [string, React.ReactNode]> = [
    ['Record', recordLabel],
    ['Rule', `${row.code}${row.name ? ` · ${row.name}` : ''}${row.procedure ? ` (${row.procedure.name})` : ''}`],
    ['Change', `${before} → ${after}`],
    ...(form.choice === 'exclude'
      ? ([
          ['For invoices from', firmName ?? (sendsFirmId ? 'One firm' : 'All firms')],
          ['Reason', form.reason.trim()],
        ] as const)
      : []),
    ...(twoCalls ? ([['Saved as', "This record's current exclusion is removed, then the new one is saved."]] as const) : []),
  ];

  return (
    <WizardFrame
      title={`${row.code}${row.name ? ` · ${row.name}` : ''}`}
      resetKey={`rule:${ruleKey(row)}`}
      nav={nav}
      api={api}
      core={core}
      recordId={recordId}
      reviewLabel="Rule"
      reviewStep={reviewStep}
      modeStep={modeStep}
      canChoose={canChoose}
      plan={plan}
      before={before}
      after={after}
      confirmRows={confirmRows}
      finishLabel={removeOnly ? 'Remove override' : 'Exclude rule'}
      successTitle={removeOnly ? 'Override removed' : 'Rule excluded'}
      successBody={`${row.code} on ${recordLabel}: ${after}.`}
      partialNote="The record's earlier exclusion was removed but the new one was not saved. Close this window and open the rule again."
      onChanged={onChanged}
      onClose={onClose}
    />
  );
};
