/**
 * One Procedure on the record, in the shared WizardShell (ebilling-r1 task 084b; prototype `ProcedureWizard.tsx`):
 *
 *   Procedure -> On this record -> Impact -> Confirm
 *
 * "On this record" offers As set on the parent, Follow latest, Pin a version, Exclude here and Remove from this record, each on task 089's
 * `procedure-assignments` route or its override removal (`plan.ts` says which calls a choice makes). There is no Change choice (084c).
 * Impact reads the dry run of the plan (task 084d-4, WizardFrame); the row's own before/after shows only while it has no answer.
 *
 * The wizard does not know whether the caller may write: the route decides, and a refusal is shown with its title with the wizard still open.
 * The panel says which kinds of call the lists' answers allow (task 084g, `allowed`): a choice that needs another kind is disabled with the
 * reason and without its fields; with no choice left that makes a call, the wizard is read-only and the frame has no Confirm (fix round 1, B1).
 * The Pin picker's versions come from task 081's read of the Procedure (084's view lists the version in force and the newer one only).
 */

import * as React from 'react';
import { Caption1, Dropdown, Field, MessageBar, MessageBarBody, Option, Spinner, Text, Textarea } from '@fluentui/react-components';
import { StatusBadge } from '@spaarke/ui-components';
import type { CoreRoute, GapView, ProcedureItem, VersionView } from '../../services/recordProceduresApi';
import { failureText, type OverridesApi, type WriteFailure } from '../../services/overridesApi';
import type { DryRunApi } from '../../services/dryRunApi';
import { gapFor, procedureState, recordNoun, recordWords, shortDate, sourceLabel, versionLabel, versionLines, type AllowedSteps } from '../../services/model';
import {
  MAX_REASON_CHARS,
  allowedBlocker,
  currentProcedureChoice,
  noAllowedCall,
  ownOverride,
  parentOverrides,
  planProcedure,
  planWithin,
  procedureChoiceNeeds,
  procedureChoices,
  type ProcedureChoice,
} from './plan';
import { ChoiceGroup, KeyValues, useWizardStyles, type ChoiceOption } from './steps';
import { WizardFrame, type OnChanged, type WizardNav } from './WizardFrame';
import { isPinnable, overrideWords, stampWords, versionOption } from './words';

export interface ProcedureWizardProps {
  row: ProcedureItem;
  gaps: readonly GapView[];
  core: CoreRoute;
  recordId: string;
  /** The form's record, for the words ("matter 2026-LIT-4471"). */
  record: { table: string; name: string | null };
  /** The work date the lists were read for (yyyy-MM-dd). */
  date: string;
  /** Task 084g: the kinds of call the lists' answers allow; a choice needing another is disabled, and none left is read-only. */
  allowed: AllowedSteps;
  api: OverridesApi;
  /** Task 084d-4: the dry run the Impact step reads, and the firm the lists were read for (the date is `date`). */
  dryRunApi: DryRunApi;
  firmId: string | null;
  nav?: WizardNav;
  onChanged: OnChanged;
  onClose: () => void;
}

interface Form {
  choice: ProcedureChoice;
  versionId: string;
  reason: string;
}

const initialForm = (p: ProcedureItem): Form => {
  const own = ownOverride(p);
  return { choice: currentProcedureChoice(p), versionId: own?.pinnedVersion?.id ?? '', reason: own?.reason ?? '' };
};

type Versions = { id: string; state: 'loading' } | { id: string; state: 'ready'; versions: VersionView[] } | { id: string; state: 'failed'; failure: WriteFailure };

/** What the Procedure shows now, in one line (the Impact step's "before"). */
export function procedureNow(p: ProcedureItem, gaps: readonly GapView[]): string {
  const state = procedureState(p, gaps).label;
  const v = versionLines(p);
  return p.versionInForce ? `${state} · ${v.main}${p.pinned && p.newerVersion ? ` (${versionLabel(p.newerVersion)} available)` : ''}` : state;
}

/** What the Procedure will show after the choice, from the row's own data (nothing is resolved here). */
export function procedureAfter(p: ProcedureItem, choice: ProcedureChoice, versionNumber: number | null): string {
  switch (choice) {
    case 'parent':
      return 'As set on the parent';
    case 'remove':
      return 'Does not apply to this record';
    case 'pin':
      return versionNumber !== null ? `Pinned · v${versionNumber}` : 'Pinned';
    case 'exclude':
      return 'Excluded here';
    case 'follow': {
      const latest = p.pinned ? (p.newerVersion ?? p.versionInForce) : p.versionInForce;
      return latest ? `Follows the latest version · ${versionLabel(latest)}` : 'Follows the latest version';
    }
  }
}

export const ProcedureWizard: React.FC<ProcedureWizardProps> = ({ row, gaps, core, recordId, record, date, allowed, api, dryRunApi, firmId, nav, onChanged, onClose }) => {
  const s = useWizardStyles();
  const [form, setForm] = React.useState<Form>(() => initialForm(row));
  const [seen, setSeen] = React.useState(row.id);
  if (seen !== row.id) {
    // The wizard moved to another Procedure (‹ ›): its form starts from that row.
    setSeen(row.id);
    setForm(initialForm(row));
  }
  const set = (patch: Partial<Form>) => setForm(f => ({ ...f, ...patch }));

  // ── What each choice needs, and whether the lists' answers allow it (084g fix round 1, B1) ──
  const offered = procedureChoices(row);
  const blockerOf = (c: ProcedureChoice) => allowedBlocker(procedureChoiceNeeds(row, c), allowed);
  // Read-only only when some offered choice makes a call and none of those is allowed (R1: a row with no such choice is not read-only).
  const readOnly = noAllowedCall(offered, c => procedureChoiceNeeds(row, c), blockerOf);

  // ── The Pin picker's versions (task 081's read), asked for when Pin is the selected choice ──
  const [versions, setVersions] = React.useState<Versions | null>(null);
  const wantsVersions = !readOnly && form.choice === 'pin' && blockerOf('pin') === null;
  const askedFor = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!wantsVersions || askedFor.current === row.id) return;
    askedFor.current = row.id;
    let live = true;
    setVersions({ id: row.id, state: 'loading' });
    void api.procedureVersions(row.id).then(r => {
      if (live) setVersions(r.ok ? { id: row.id, state: 'ready', versions: r.value } : { id: row.id, state: 'failed', failure: r.failure });
    });
    return () => {
      live = false;
      askedFor.current = null;
    };
  }, [wantsVersions, api, row.id]);

  const own = ownOverride(row);
  const parents = parentOverrides(row);
  const current = currentProcedureChoice(row);
  const gap = gapFor(row, gaps);
  const pinnable = versions?.id === row.id && versions.state === 'ready' ? versions.versions.filter(isPinnable) : [];
  const chosen = pinnable.find(v => v.id === form.versionId) ?? (own?.pinnedVersion && own.pinnedVersion.id === form.versionId ? own.pinnedVersion : undefined);

  // The version the form carries (the record's own pin, pre-filled) is not one the route accepts: the plan stays empty until another is chosen.
  const versionRefused = versions?.id === row.id && versions.state === 'ready' && !!form.versionId && !pinnable.some(v => v.id.toLowerCase() === form.versionId.toLowerCase());

  const plan = planProcedure(row, form.choice, { versionId: form.versionId, reason: form.reason, pinnable: pinnable.map(v => v.id) });
  // A plan goes on only when its choice is allowed and every call it makes is one the choice was said to need.
  const canChoose = plan.steps.length > 0 && blockerOf(form.choice) === null && planWithin(plan, procedureChoiceNeeds(row, form.choice));
  const recordLabel = recordWords(record.table, record.name);

  // ── Step 1: the Procedure as the server describes it ─────────────────────
  const state = procedureState(row, gaps);
  const version = versionLines(row);
  const reviewStep = (
    <div className={s.step} data-testid="procedure-step">
      {gap && (
        <MessageBar intent="error" layout="multiline">
          <MessageBarBody>{gap.text}</MessageBarBody>
        </MessageBar>
      )}
      {row.status === 'NotYetInForce' && (
        <MessageBar intent="info">
          <MessageBarBody>Work dated before this Procedure's first version starts is not subject to it. That is not a gap.</MessageBarBody>
        </MessageBar>
      )}
      <KeyValues
        rows={[
          [
            'State',
            <span key="state" className={s.change}>
              <StatusBadge label={state.label} tone={state.tone} />
              <Text>{version.main}</Text>
              {version.note && <Caption1 className={s.muted}>· {version.note}</Caption1>}
            </span>,
          ],
          ['Type', row.type ?? '—'],
          ['Invoices', row.firm ? `${row.firm.name ?? 'One firm'} invoices only` : 'Every firm'],
          [
            'Applies through',
            <span key="through" className={s.stack}>
              {row.sources.length > 0
                ? row.sources.map((x, i) => (
                    <Text key={i}>
                      {sourceLabel(x, row.firm?.name)}
                      {x.winning && row.sources.length > 1 ? ' (wins)' : ''}
                    </Text>
                  ))
                : '—'}
            </span>,
          ],
          [
            'On this record',
            row.overrides.length > 0 ? (
              <span key="overrides" className={s.stack}>
                {row.overrides.map(o => (
                  <span key={o.rowId} className={s.stack}>
                    <Text>
                      {overrideWords(o)} · {stampWords(o)}
                    </Text>
                    {o.reason && <Caption1 className={s.muted}>"{o.reason}"</Caption1>}
                  </span>
                ))}
              </span>
            ) : (
              'No override: follows the latest version'
            ),
          ],
        ]}
      />
    </div>
  );

  // ── Step 2: the choices ──────────────────────────────────────────────────
  const reasonField = (
    <Field label="Reason" required hint={`At most ${MAX_REASON_CHARS} characters.`}>
      <Textarea value={form.reason} onChange={(_, d) => set({ reason: d.value })} resize="vertical" maxLength={MAX_REASON_CHARS} />
    </Field>
  );

  const latestNow = row.pinned ? (row.newerVersion ?? row.versionInForce) : row.versionInForce;
  const parentText = parents.map(o => overrideWords(o)).join('; ');

  const options: Array<ChoiceOption<ProcedureChoice>> = offered.map(value => {
    switch (value) {
      case 'parent':
        return {
          value,
          label: 'As set on the parent',
          caption: `${parentText}.${gap?.kind === 'ConflictingParents' && current === 'parent' ? ' The parents conflict, so invoices are held.' : ''}`,
        };
      case 'follow':
        return {
          value,
          label: 'Follow latest',
          caption: `The version in force on the work date${latestNow ? ` (${versionLabel(latestNow)} on ${shortDate(date)})` : ''}.${
            parents.some(o => o.mode !== 'FollowLatest') ? " Overrides the parent's setting." : ''
          }${own && own.mode !== 'FollowLatest' ? " Removes this record's override." : ''}`,
        };
      case 'pin':
        return {
          value,
          label: 'Pin a version',
          caption: 'This version applies whatever is newer, e.g. to keep grandfathered rates.',
          body: (
            <>
              <Field
                label="Version"
                required
                validationState={versions?.state === 'failed' || versionRefused ? 'error' : 'none'}
                validationMessage={
                  versions?.state === 'failed'
                    ? `The versions could not be read. ${failureText(versions.failure)}`
                    : versionRefused
                      ? 'This version can no longer be pinned (it has not been in force, or is retired). Choose another.'
                      : undefined
                }
              >
                {versions?.state === 'loading' ? (
                  <Spinner size="tiny" label="Reading the versions…" />
                ) : (
                  <Dropdown
                    value={chosen ? versionOption(chosen) : ''}
                    selectedOptions={form.versionId ? [form.versionId] : []}
                    onOptionSelect={(_, d) => set({ versionId: d.optionValue ?? '' })}
                    disabled={versions?.state === 'failed'}
                    data-testid="pin-version"
                  >
                    {pinnable.map(v => (
                      <Option key={v.id} value={v.id} text={versionOption(v)}>
                        {versionOption(v)}
                      </Option>
                    ))}
                  </Dropdown>
                )}
              </Field>
              {reasonField}
            </>
          ),
        };
      case 'exclude':
        return {
          value,
          label: 'Exclude here',
          caption: `Does not apply to this ${recordNoun(record.table)}${row.firm ? '' : " for any firm's invoices"}, or to records filed directly under it. Its rules stop running here.`,
          body: reasonField,
        };
      case 'remove':
        return { value, label: 'Remove from this record', caption: 'Deletes the assignment. The Procedure and its rules stop applying here.' };
    }
  });

  // 084g: a choice the answers do not allow is disabled with the reason and without its fields; read-only, every choice is disabled.
  const shown = options.map(o => {
    const blocker = blockerOf(o.value);
    return blocker !== null ? { ...o, caption: blocker, disabled: true, body: undefined } : readOnly ? { ...o, disabled: true, body: undefined } : o;
  });

  const modeStep = (
    <div className={s.step} data-testid="mode-step">
      <ChoiceGroup ariaLabel="On this record" options={shown} value={form.choice} onChange={choice => set({ choice })} />
      {wantsVersions && versions?.state === 'ready' && pinnable.length === 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>No version of this Procedure has been in force yet, so there is nothing to pin.</MessageBarBody>
        </MessageBar>
      )}
    </div>
  );

  // ── Impact and Confirm ────────────────────────────────────────────────────
  const before = procedureNow(row, gaps);
  const after = procedureAfter(row, form.choice, chosen?.number ?? null);
  const twoCalls = plan.steps.length > 1;
  const removeOnly = plan.steps.length > 0 && plan.steps.every(x => x.kind === 'remove');
  const showReason = (form.choice === 'pin' || form.choice === 'exclude') && form.reason.trim().length > 0;

  const confirmRows: Array<readonly [string, React.ReactNode]> = [
    ['Record', recordLabel],
    ['Procedure', row.name],
    ['Change', `${before} → ${after}`],
    ...(showReason ? ([['Reason', form.reason.trim()]] as const) : []),
    ...(twoCalls ? ([['Saved as', "This record's current override is removed, then the new one is saved."]] as const) : []),
  ];

  return (
    <WizardFrame
      title={row.name}
      resetKey={`procedure:${row.id}`}
      nav={nav}
      api={api}
      dryRunApi={dryRunApi}
      query={{ firmId, date }}
      core={core}
      recordId={recordId}
      readOnly={readOnly}
      reviewLabel="Procedure"
      reviewStep={reviewStep}
      modeStep={modeStep}
      canChoose={canChoose}
      plan={plan}
      before={before}
      after={after}
      impactExtra={
        gap && form.choice !== current ? (
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>Held today: {gap.text} The lists show the result once the change is saved.</MessageBarBody>
          </MessageBar>
        ) : undefined
      }
      confirmRows={confirmRows}
      finishLabel={removeOnly ? 'Remove override' : 'Save override'}
      successTitle={removeOnly ? 'Override removed' : form.choice === 'pin' ? 'Version pinned' : form.choice === 'exclude' ? 'Procedure excluded here' : 'Override saved'}
      successBody={`${row.name} on ${recordLabel}: ${after}.`}
      partialNote="The record's earlier override was removed but the new one was not saved. Close this window and open the Procedure again."
      onChanged={onChanged}
      onClose={onClose}
    />
  );
};
