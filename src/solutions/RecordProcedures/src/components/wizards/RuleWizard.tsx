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
 * Fix round 1: the form is prefilled from the read model's `ruleBody` (no second read; with no body the step says why and Next stays off,
 * F1-2). A rule already changed here can be changed again (F1-1): the form starts from this record's values, and "Start from the published
 * values" loads the base rule's (`baseRuleBody`).
 *
 *   - P-3: a rule a parent excluded (or changed) shows that as applied, and "Run the published rule" is disabled with the reason.
 *   - O-20: a rule that names one firm can only be excluded for that firm; the firm field is fixed.
 *
 * The wizard does not know whether the caller may write: the route decides, and a refusal is shown with its title with the wizard still open.
 * The panel says which kinds of call the lists' answers allow (task 084g, `allowed`): a choice that needs another kind is disabled with the
 * reason and without its fields; with no choice left that makes a call, the wizard is read-only and the frame has no Confirm (fix round 1, B1).
 */

import * as React from 'react';
import { Button, Caption1, Dropdown, Field, MessageBar, MessageBarBody, Option, Text, Textarea } from '@fluentui/react-components';
import { StatusBadge } from '@spaarke/ui-components';
import type { CoreRoute, RuleItem, RuleMark } from '../../services/recordProceduresApi';
import type { OverridesApi, WriteFailure } from '../../services/overridesApi';
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
import {
  FEE_KIND_LOCKED,
  buildBody,
  consequenceProblems,
  describeBody,
  formFor,
  lockedFields,
  sameBody,
  serverFieldErrors,
  toFormValues,
  type FormValues,
  type ServerFieldErrors,
} from './ruleForms';
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

const isRecord = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

/**
 * The body the Change form starts from (084c fix round 1, F1-2): the row's own version, from the read model. On a rule changed here it is
 * this record's change (F1-1). Null when the server could not give it: the form then does not start (a change replaces the whole body, so a
 * form built without the published optional values would drop them).
 */
export const startBodyOf = (r: RuleItem): Record<string, unknown> | null => (isRecord(r.ruleBody) ? r.ruleBody : null);

/** The published rule's body and version number: the base rule's in force for a rule changed here, else the row's own. Null when not given. */
export function publishedOf(r: RuleItem): { body: Record<string, unknown>; versionNumber: number | null } | null {
  const changed = ownChange(r);
  if (changed) return isRecord(changed.baseRuleBody) ? { body: changed.baseRuleBody, versionNumber: changed.currentBase?.number ?? null } : null;
  const own = startBodyOf(r);
  return own ? { body: own, versionNumber: r.versionNumber } : null;
}

/** Why the New values step cannot start: the read model gave no body for the rule (an older BFF, or a body that is not an object). */
export const NO_VALUES_TEXT =
  "The rule's values could not be read, so it cannot be changed here: a change replaces the whole rule, and values that are not shown would be lost. Close this window and open the rule again, or exclude the rule instead.";

const initialForm = (r: RuleItem): Form => ({
  choice: currentRuleChoice(r),
  reason: ownExclude(r)?.reason ?? '',
  firmId: fixedFirmId(r) ?? ownExclude(r)?.firm?.id ?? '',
  values: formFor(r.ruleType) ? toFormValues(formFor(r.ruleType)!, startBodyOf(r)) : {},
  touched: false,
});

/** Said with a refused change's field errors when nothing was saved: the fields are marked on New values. */
export const GO_BACK_HINT = 'Go back to New values: the fields the server refused are marked.';

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

  // ── Change: the form of the rule's type, prefilled from the read model (task 084c; fix round 1, F1-1 and F1-2: no second read) ──
  const def = formFor(row.ruleType);
  const key = ruleKey(row);
  const refusal = refusalState?.key === key ? refusalState.errors : null;
  const wantsValues = form.choice === 'change' && !!def;
  const start = startBodyOf(row);
  const published = publishedOf(row);
  const publishedBody = published?.body ?? null;
  // Item 9: AddedFee's precedence key cannot move; item 6: what the rule's consequence makes the server refuse, before anything is sent.
  const locked: Record<string, string> = def && start ? Object.fromEntries(lockedFields(def, start).map(k => [k, FEE_KIND_LOCKED])) : {};
  const ruleProblems = def ? consequenceProblems(def, form.values, row.consequence) : {};

  const current = currentRuleChoice(row);
  const inherited = inheritedOverride(row);
  const retired = isBaseRetired(row);
  const keepBlock = keepBlocker(row);
  const excludeBlock = excludeBlocker(row);
  const fixedFirm = row.firmScope;
  // ── What each choice needs, and whether the lists' answers allow it (084g fix round 1, B1) ──
  const blockerOf = (c: RuleChoice) => allowedBlocker(ruleChoiceNeeds(row, c), allowed);
  const changeBlock = changeBlocker(row);
  // The new body: complete, read from a body the server gave (F1-2), different from this record's current values and from the published
  // ones (nothing to send otherwise; a multi-choice field compares as a set, F1-3), and nothing its consequence makes the server refuse.
  const draft = def && form.choice === 'change' ? buildBody(def, form.values) : null;
  const sameCurrent = draft?.ok === true && start !== null && sameBody(draft.body, start, def);
  const samePublished = draft?.ok === true && publishedBody !== null && sameBody(draft.body, publishedBody, def);
  const ruleBody =
    draft?.ok === true && start !== null && !sameCurrent && !samePublished && Object.keys(ruleProblems).length === 0 ? JSON.stringify(draft.body) : undefined;
  const plan = planRule(row, form.choice, { reason: form.reason, firmId: form.firmId, ruleBody });
  // A plan goes on only when its choice is allowed and every call it makes is one the choice was said to need. A change is chosen with its
  // reason (and firm) on "On this record" and its values on "New values", so this step needs only the choice and the reason. Its needs are
  // empty when it cannot be planned at all (an own override the server named no row for; fix round 1 widens 084c's own-Exclude check).
  const choiceReady =
    form.choice === 'change'
      ? changeBlock === null && blockerOf('change') === null && reasonValid(form.reason) && ruleChoiceNeeds(row, 'change').change
      : plan.steps.length > 0 && blockerOf(form.choice) === null && planWithin(plan, ruleChoiceNeeds(row, form.choice));
  const canChoose = choiceReady;
  const valuesReady = form.choice === 'change' && ruleBody !== undefined && blockerOf('change') === null && plan.steps.length > 0 && planWithin(plan, ruleChoiceNeeds(row, 'change'));
  /** What the plan removes first: this record's exclusion, or its earlier change (F1-1). */
  const removedWord = ownExclude(row) ? 'exclusion' : ownChange(row) ? 'change' : 'override';
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
    caption:
      changeBlock ??
      (changedHere
        ? "Replaces this record's change with new values: the change is removed, then the published rule is changed again. You set the values next."
        : 'A copy of the rule with your values runs in its place. You set the values next.'),
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
  // The form's own problems once edited; what the rule's consequence makes the server refuse (item 6) always; the server's refusal last.
  const fieldProblems: Record<string, string | undefined> = { ...(form.touched && draft && !draft.ok ? draft.errors : {}), ...ruleProblems, ...serverByField };
  // "Start from the published values" keeps the fields this change may not edit (item 9) as they are.
  const fromPublished = (body: Record<string, unknown>): FormValues => {
    const values = def ? toFormValues(def, body) : {};
    for (const k of Object.keys(locked)) if (k in form.values) values[k] = form.values[k];
    return values;
  };
  const valuesStep = def ? (
    <div className={s.step} data-testid="values-step">
      <Text>
        {row.code} for {recordLabel}
        {firmName ? `, ${firmName} invoices` : sendsFirmId ? ', one firm’s invoices' : ', every firm'}. It takes the published rule’s place and rank.
      </Text>
      {start === null ? (
        <MessageBar intent="warning" layout="multiline" data-testid="values-unread">
          <MessageBarBody>{NO_VALUES_TEXT}</MessageBarBody>
        </MessageBar>
      ) : (
        <>
          {changedHere && <Caption1 className={s.muted} data-testid="values-current">The form shows this record’s current values.</Caption1>}
          {publishedBody && !sameBody(draft?.ok ? draft.body : null, publishedBody, def) && (
            <MessageBar intent="info" layout="multiline" data-testid="published-values">
              <MessageBarBody>
                The published rule{published?.versionNumber != null ? ` (v${published.versionNumber})` : ''} has {describeBody(def, publishedBody) || 'no values'}.
              </MessageBarBody>
              <Button size="small" onClick={() => set({ values: fromPublished(publishedBody), touched: true })} data-testid="start-from-published">
                Start from the published values
              </Button>
            </MessageBar>
          )}
          {refusal?.general.map((m, i) => (
            <MessageBar key={`${i}-${m}`} intent="error" layout="multiline" role="alert" data-testid="values-refused">
              <MessageBarBody>{m}</MessageBarBody>
            </MessageBar>
          ))}
          <RuleValuesForm def={def} values={form.values} onChange={setValue} errors={fieldProblems} published={publishedBody} locked={locked} />
          {samePublished ? (
            <Caption1 className={s.muted}>
              {changedHere ? 'These are the published values: to run them, choose Run the published rule.' : 'These are the published values. Change at least one.'}
            </Caption1>
          ) : (
            sameCurrent && <Caption1 className={s.muted}>These are this record’s current values. Change at least one.</Caption1>
          )}
        </>
      )}
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
            `An exclusion of ${row.code} and a record rule with these values, saved together.${twoCalls ? ` This record's current ${removedWord} is removed first.` : ''}`,
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
      refusalHint={(f, partial) =>
        f.errorCode === 'assignment.duplicate' && (form.choice === 'exclude' || form.choice === 'change')
          ? DUPLICATE_HINT
          : // F3-4: after a partial save the wizard is held on a row that is out of date, so "go back" would contradict the partial note.
            form.choice === 'change' && f.fieldErrors.length > 0 && !partial
            ? GO_BACK_HINT
            : null
      }
      valuesStep={valuesStep ? { active: wantsValues, content: valuesStep, canAdvance: valuesReady } : undefined}
      onRefused={(f: WriteFailure) => {
        if (form.choice === 'change' && def) setRefusal({ key, errors: serverFieldErrors(def, f.fieldErrors) });
      }}
      partialNote={
        form.choice === 'change'
          ? `The record's earlier ${removedWord} was removed but the change was not saved. Close this window and open the rule again.`
          : "The record's earlier exclusion was removed but the new one was not saved. Close this window and open the rule again."
      }
      onChanged={onChanged}
      onClose={onClose}
    />
  );
};
