/**
 * One rule in force on the record, in the shared WizardShell (ebilling-r1 task 084b; prototype `RuleWizard.tsx`):
 *
 *   Rule -> On this record -> [New values] -> Impact -> Confirm
 *
 * "On this record" offers Run the published rule, Exclude here and Change for this record (the last two with a firm scope and a reason), on
 * task 089's `rule-overrides/exclude` and `rule-overrides/change` routes and the override removal. "New values" (task 084c) is only there
 * while Change is chosen: a typed form for the rule's billing type (`ruleForms/`), prefilled from the published values. The server's
 * validation is the authority: a refusal is shown on the fields it names.
 *
 *   - P-3: a rule a parent excluded (or changed) shows that as applied, and "Run the published rule" is disabled with the reason.
 *   - O-20: a rule that names one firm can only be excluded for that firm; the firm field is fixed.
 *
 * The wizard does not know whether the caller may write: the route decides, and a refusal is shown with its title with the wizard still open.
 * The panel says which kinds of call the lists' answers allow (task 084g, `allowed`): a choice that needs another kind is disabled with the
 * reason and without its fields; with no choice left that makes a call, the wizard is read-only and the frame has no Confirm (fix round 1, B1).
 */

import * as React from 'react';
import { Button, Caption1, Dropdown, Field, MessageBar, MessageBarBody, Option, Spinner, Text, Textarea } from '@fluentui/react-components';
import { StatusBadge } from '@spaarke/ui-components';
import type { CoreRoute, RuleItem, RuleMark } from '../../services/recordProceduresApi';
import { failureText, type OverridesApi, type WriteFailure } from '../../services/overridesApi';
import type { FirmOption } from '../../services/firmChoices';
import { levelLabel, recordWords, ruleBadges, versionLabel, type AllowedSteps } from '../../services/model';
import {
  MAX_REASON_CHARS,
  allowedBlocker,
  currentRuleChoice,
  noAllowedCall,
  planWithin,
  ruleChoiceNeeds,
  excludeBlocker,
  fixedFirmId,
  inheritedOverride,
  isBaseRetired,
  keepBlocker,
  changeBlocker,
  ownChange,
  ownExclude,
  planRule,
  reasonValid,
  type RuleChoice,
} from './plan';
import { RuleValuesForm } from './ruleForms/RuleValuesForm';
import { buildBody, describeBody, formFor, sameBody, serverFieldErrors, toFormValues, type FormValues, type ServerFieldErrors } from './ruleForms';
import { ChoiceGroup, KeyValues, useWizardStyles, type ChoiceOption } from './steps';
import { WizardFrame, type OnChanged, type WizardNav } from './WizardFrame';
import { stampWords } from './words';

export interface RuleWizardProps {
  row: RuleItem;
  core: CoreRoute;
  recordId: string;
  record: { table: string; name: string | null };
  /** The firms the "For invoices from" field offers besides "All firms". */
  firms: readonly FirmOption[];
  /** Task 084g: the kinds of call the lists' answers allow; a choice needing another is disabled, and none left is read-only. */
  allowed: AllowedSteps;
  api: OverridesApi;
  nav?: WizardNav;
  onChanged: OnChanged;
  onClose: () => void;
}

interface Form {
  choice: RuleChoice;
  reason: string;
  /** '' = every firm. */
  firmId: string;
  /** Change: the inputs of the rule type's form. */
  values: FormValues;
  /** The user changed a value: the published values no longer seed the form, and the form's own problems are shown. */
  touched: boolean;
}

const initialForm = (r: RuleItem): Form => ({
  choice: currentRuleChoice(r),
  reason: ownExclude(r)?.reason ?? '',
  firmId: fixedFirmId(r) ?? ownExclude(r)?.firm?.id ?? '',
  values: formFor(r.ruleType) ? toFormValues(formFor(r.ruleType)!, null) : {},
  touched: false,
});

/** The published rule's body, as the Change form starts from it. */
type Published =
  | { state: 'idle' }
  | { state: 'loading' }
  | { state: 'ready'; body: Record<string, unknown>; versionNumber: number | null }
  | { state: 'failed'; text: string };

/** 089 answers 409 `assignment.duplicate` for a second exclusion of the rule on the record, whichever firm the first one names. */
export const DUPLICATE_HINT = 'An exclusion for another firm may exist on this record; switch the firm to see it.';

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

export const RuleWizard: React.FC<RuleWizardProps> = ({ row, core, recordId, record, firms, allowed, api, nav, onChanged, onClose }) => {
  const s = useWizardStyles();
  const [form, setForm] = React.useState<Form>(() => initialForm(row));
  const [seen, setSeen] = React.useState(ruleKey(row));
  if (seen !== ruleKey(row)) {
    // The wizard moved to another rule (‹ ›): its form starts from that row.
    setSeen(ruleKey(row));
    setForm(initialForm(row));
  }
  const set = (patch: Partial<Form>) => {
    setForm(f => ({ ...f, ...patch }));
    setRefusal(null);
  };
  /** The server's field errors of the last refused change, for the rule it was made on; cleared by any edit. */
  const [refusalState, setRefusal] = React.useState<{ key: string; errors: ServerFieldErrors } | null>(null);

  // ── Change: the form of the rule's type, and the published values it starts from (task 084c) ──
  const def = formFor(row.ruleType);
  const key = ruleKey(row);
  const refusal = refusalState?.key === key ? refusalState.errors : null;
  const keyRef = React.useRef(key);
  keyRef.current = key;
  const [pub, setPub] = React.useState<{ key: string; result: Published }>({ key: '', result: { state: 'idle' } });
  const asked = React.useRef<string | null>(null);
  const published: Published = pub.key === key ? pub.result : { state: 'idle' };
  const wantsValues = form.choice === 'change' && !!def;
  React.useEffect(() => {
    if (!wantsValues || !def || asked.current === key) return;
    asked.current = key;
    const done = (result: Published) => {
      if (keyRef.current !== key) return;
      setPub({ key, result });
      if (result.state === 'ready') setForm(f => (f.touched ? f : { ...f, values: toFormValues(def, result.body) }));
    };
    if (!row.ruleSetId) {
      done({ state: 'failed', text: 'the rule set is not known' });
      return;
    }
    setPub({ key, result: { state: 'loading' } });
    void api.ruleValues(row.ruleSetId, row.policyId).then(r => {
      if (!r.ok) return done({ state: 'failed', text: failureText(r.failure) });
      if (!r.value.inForce || !r.value.body) return done({ state: 'failed', text: 'a newer draft of the rule exists, so the version in force could not be read' });
      done({ state: 'ready', body: r.value.body, versionNumber: r.value.versionNumber });
    });
  }, [wantsValues, def, key, row.ruleSetId, row.policyId, api]);

  const current = currentRuleChoice(row);
  const inherited = inheritedOverride(row);
  const retired = isBaseRetired(row);
  const keepBlock = keepBlocker(row);
  const excludeBlock = excludeBlocker(row);
  const fixedFirm = row.firmScope;
  // ── What each choice needs, and whether the lists' answers allow it (084g fix round 1, B1) ──
  const blockerOf = (c: RuleChoice) => allowedBlocker(ruleChoiceNeeds(row, c), allowed);
  const changeBlock = changeBlocker(row);
  // The new body: complete, and different from the published one (nothing to send otherwise).
  const draft = def && form.choice === 'change' ? buildBody(def, form.values) : null;
  const samePublished = draft?.ok === true && published.state === 'ready' && sameBody(draft.body, published.body);
  const ruleBody = draft?.ok === true && !samePublished ? JSON.stringify(draft.body) : undefined;
  const plan = planRule(row, form.choice, { reason: form.reason, firmId: form.firmId, ruleBody });
  // A plan goes on only when its choice is allowed and every call it makes is one the choice was said to need. A change is chosen with its
  // reason (and firm) on "On this record" and its values on "New values", so this step needs only the choice and the reason.
  const choiceReady =
    form.choice === 'change'
      ? changeBlock === null && blockerOf('change') === null && reasonValid(form.reason) && !(ownExclude(row) && !ownExclude(row)?.overrideRowId)
      : plan.steps.length > 0 && blockerOf(form.choice) === null && planWithin(plan, ruleChoiceNeeds(row, form.choice));
  const canChoose = choiceReady;
  const valuesReady = form.choice === 'change' && draft?.ok === true && !samePublished && blockerOf('change') === null && published.state !== 'loading' && plan.steps.length > 0 && planWithin(plan, ruleChoiceNeeds(row, 'change'));
  const recordLabel = recordWords(record.table, record.name);
  const sendsFirmId = fixedFirmId(row) ?? (form.firmId || null);
  const firmName = fixedFirm?.name ?? firms.find(f => f.id.toLowerCase() === form.firmId.toLowerCase())?.name ?? null;

  // ── Step 1: the rule as the server describes it ──────────────────────────
  const excludedMarks = row.marks.filter(m => m.kind === 'Excluded');
  const changed = row.marks.find(m => m.kind === 'Changed');
  const changedHere = ownChange(row);
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
    <Field label="For invoices from" hint={`The rule names ${fixedFirm.name ?? 'one firm'}, so it can only be excluded or changed for that firm (O-20).`}>
      <Dropdown value={fixedFirm.name ?? 'The rule’s firm'} selectedOptions={[fixedFirm.id]} disabled data-testid="rule-firm">
        <Option value={fixedFirm.id} text={fixedFirm.name ?? 'The rule’s firm'}>
          {fixedFirm.name ?? 'The rule’s firm'}
        </Option>
      </Dropdown>
    </Field>
  ) : (
    <Field label="For invoices from" validationMessage={refusal?.firm.join(' ') || undefined} validationState={refusal && refusal.firm.length > 0 ? 'error' : 'none'}>
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

  const reasonField = (
    <Field label="Reason" required hint={`At most ${MAX_REASON_CHARS} characters.`}>
      <Textarea value={form.reason} onChange={(_, d) => set({ reason: d.value })} resize="vertical" maxLength={MAX_REASON_CHARS} />
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
        : current === 'changed'
          ? `Removes this record's change: the published${changedHere?.baseParameterSummary ? ` values (${changedHere.baseParameterSummary})` : ' rule'} run again instead of the changed${row.parameterSummary ? ` values (${row.parameterSummary})` : ' ones'} for invoices checked from now on, unless a parent excludes the rule. The lists show the result.`
          : `Removes this record's exclusion. The published rule runs again for invoices checked from now on, unless a parent excludes it. The lists show the result.`),
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
        {reasonField}
      </>
    ),
  });
  options.push({
    value: 'change',
    label: 'Change for this record',
    caption: changeBlock ?? 'A copy of the rule with your values runs in its place. You set the values next.',
    disabled: changeBlock !== null,
    body: (
      <>
        {firmField}
        {reasonField}
      </>
    ),
  });

  // 084g: read-only only when some offered choice makes a call and none of those is allowed (R1: a row whose choices all make no call, e.g.
  // a rule changed on the parent, keeps the normal frame). A choice the answers do not allow is disabled with the reason and without its
  // fields (a choice its own blocker refuses keeps that reason); read-only, every choice is disabled.
  const readOnly = noAllowedCall(options.map(o => o.value), c => ruleChoiceNeeds(row, c), blockerOf);
  const shown = options.map(o => {
    const blocker = blockerOf(o.value);
    return blocker !== null ? { ...o, caption: blocker, disabled: true, body: undefined } : readOnly ? { ...o, disabled: true, body: undefined } : o;
  });

  const modeStep = (
    <div className={s.step} data-testid="mode-step">
      <ChoiceGroup ariaLabel="On this record" options={shown} value={form.choice} onChange={choice => set({ choice })} />
    </div>
  );

  // ── Step 3 (Change only): the new values ──────────────────────────────────
  const setValue = (k: string, v: string | string[]) => set({ values: { ...form.values, [k]: v }, touched: true });
  const serverByField = refusal ? Object.fromEntries(Object.entries(refusal.byField).map(([k, msgs]) => [k, msgs.join(' ')])) : {};
  const fieldProblems: Record<string, string | undefined> = { ...(form.touched && draft && !draft.ok ? draft.errors : {}), ...serverByField };
  const publishedBody = published.state === 'ready' ? published.body : null;
  const valuesStep = def ? (
    <div className={s.step} data-testid="values-step">
      <Text>
        {row.code} for {recordLabel}
        {firmName ? `, ${firmName} invoices` : sendsFirmId ? ', one firm’s invoices' : ', every firm'}. It takes the published rule’s place and rank.
      </Text>
      {published.state === 'loading' && <Spinner size="tiny" label="Reading the published values…" data-testid="values-loading" />}
      {published.state === 'failed' && (
        <MessageBar intent="warning" layout="multiline" data-testid="values-unread">
          <MessageBarBody>The published values could not be read ({published.text}). Enter the values below.</MessageBarBody>
        </MessageBar>
      )}
      {publishedBody && form.touched && !sameBody(draft?.ok ? draft.body : null, publishedBody) && (
        <MessageBar intent="info" layout="multiline">
          <MessageBarBody>
            The published rule{published.state === 'ready' && published.versionNumber !== null ? ` (v${published.versionNumber})` : ''} has {describeBody(def, publishedBody) || 'no values'}.
          </MessageBarBody>
          <Button size="small" onClick={() => set({ values: toFormValues(def, publishedBody), touched: true })} data-testid="start-from-published">
            Start from the published values
          </Button>
        </MessageBar>
      )}
      {refusal?.general.map((m, i) => (
        <MessageBar key={`${i}-${m}`} intent="error" layout="multiline" role="alert" data-testid="values-refused">
          <MessageBarBody>{m}</MessageBarBody>
        </MessageBar>
      ))}
      <RuleValuesForm def={def} values={form.values} onChange={setValue} errors={fieldProblems} published={publishedBody} />
      {samePublished && <Caption1 className={s.muted}>These are the published values. Change at least one.</Caption1>}
    </div>
  ) : null;

  // ── Impact and Confirm ────────────────────────────────────────────────────
  const before = ruleNow(row);
  const afterKeep = ownChange(row)?.baseParameterSummary ?? row.parameterSummary;
  const after =
    form.choice === 'change'
      ? `Changed for this record${draft?.ok && def ? `: ${describeBody(def, draft.body)}` : ''}`
      : form.choice === 'exclude'
      ? `Excluded${firmName ? ` for ${firmName} invoices` : ''}: the rule does not run`
      : form.choice === 'keep'
        ? // Rule overrides are decided by the nearest record only, so a parent's exclusion can still apply once this record's is gone.
          `Runs again unless a parent excludes it${afterKeep ? ` · ${afterKeep}` : ''}`
        : before;
  const removeOnly = plan.steps.length > 0 && plan.steps.every(x => x.kind === 'remove');
  const twoCalls = plan.steps.length > 1;

  const confirmRows: Array<readonly [string, React.ReactNode]> = [
    ['Record', recordLabel],
    ['Rule', `${row.code}${row.name ? ` · ${row.name}` : ''}${row.procedure ? ` (${row.procedure.name})` : ''}`],
    ['Change', `${before} → ${after}`],
    ...(form.choice === 'exclude' || form.choice === 'change'
      ? ([
          ['For invoices from', firmName ?? (sendsFirmId ? 'One firm' : 'All firms')],
          ['Reason', form.reason.trim()],
        ] as const)
      : []),
    ...(form.choice === 'change' && def && draft?.ok ? ([['New values', describeBody(def, draft.body)]] as const) : []),
    ...(form.choice === 'change'
      ? ([
          [
            'Saved as',
            `An exclusion of ${row.code} and a record rule with these values, saved together.${twoCalls ? " This record's current exclusion is removed first." : ''}`,
          ],
        ] as const)
      : twoCalls
        ? ([['Saved as', "This record's current exclusion is removed, then the new one is saved."]] as const)
        : []),
  ];

  return (
    <WizardFrame
      title={`${row.code}${row.name ? ` · ${row.name}` : ''}`}
      resetKey={`rule:${ruleKey(row)}`}
      nav={nav}
      api={api}
      core={core}
      recordId={recordId}
      readOnly={readOnly}
      reviewLabel="Rule"
      reviewStep={reviewStep}
      modeStep={modeStep}
      canChoose={canChoose}
      plan={plan}
      before={before}
      after={after}
      confirmRows={confirmRows}
      finishLabel={removeOnly ? 'Remove override' : form.choice === 'change' ? 'Save record rule' : 'Exclude rule'}
      successTitle={removeOnly ? 'Override removed' : form.choice === 'change' ? 'Rule changed for this record' : 'Rule excluded'}
      successBody={`${row.code} on ${recordLabel}: ${after}. The lists show the result.`}
      refusalHint={f =>
        f.errorCode === 'assignment.duplicate' && (form.choice === 'exclude' || form.choice === 'change')
          ? DUPLICATE_HINT
          : form.choice === 'change' && f.fieldErrors.length > 0
            ? 'Go back to New values: the fields the server refused are marked.'
            : null
      }
      valuesStep={valuesStep ? { active: wantsValues, content: valuesStep, canAdvance: valuesReady } : undefined}
      onRefused={(f: WriteFailure) => {
        if (form.choice === 'change' && def) setRefusal({ key, errors: serverFieldErrors(def, f.fieldErrors) });
      }}
      partialNote={
        form.choice === 'change'
          ? "The record's earlier exclusion was removed but the change was not saved. Close this window and open the rule again."
          : "The record's earlier exclusion was removed but the new one was not saved. Close this window and open the rule again."
      }
      onChanged={onChanged}
      onClose={onClose}
    />
  );
};
