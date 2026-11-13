/**
 * Assign a Procedure the record does not have yet, in the shared WizardShell (ebilling-r1 task 084d-5b; notes/084d-dry-run-design.md
 * "084d-5 design" section 5, EB-D-227..230):
 *
 *   Procedure (Choose) -> On this record -> Impact -> Confirm
 *
 * Choose reads task 084d-5a's assignable list (`recordProceduresApi.readAssignable`, a read) for the lists' own firm and date. Each offered
 * Procedure shows what it would be if assigned now, in the server's words; a Gap one carries its gap text and the server's warning (EB-D-230);
 * a Procedure the record already has an override row for is shown disabled with the reason. More than the bound match: no list, a name
 * search instead (EB-D-227), re-asked after a pause under the same timeout and abort as the dry run. A refused or failed read shows the panel's
 * words and no list. The note under the list says what is never listed (ontology-r1 K3).
 *
 * "On this record" is Follow latest or Pin a version (task 081's versions of the Procedure, filtered to the pinnable ones, with a reason).
 * Impact is 084d-4's (the dry run of the plan's one `assign` call); Confirm makes that one call through `executePlan` (task 089). A refusal is
 * shown with the wizard still open; a success re-reads both lists (`onChanged`).
 *
 * The panel opens this wizard only when the lists' answers allow a create (task 084g); nothing here decides who may write: the route does.
 * Fluent v9 semantic tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Caption1, Dropdown, Field, Input, MessageBar, MessageBarActions, MessageBarBody, MessageBarTitle, Option, Radio, RadioGroup, Spinner, Text, Textarea } from '@fluentui/react-components';
import { StatusBadge, type StatusBadgeTone } from '@spaarke/ui-components';
import {
  MAX_NAME_SEARCH_CHARS,
  failureKind,
  failureMessage,
  nameSearchOf,
  type ApiFailure,
  type AssignableProcedureItem,
  type CoreRoute,
  type NotOfferedProcedure,
  type ProceduresQuery,
  type RecordAssignableProceduresView,
  type RecordProceduresApi,
  type VersionView,
} from '../../services/recordProceduresApi';
import { CODE_ABANDONED, CODE_TIMED_OUT, failureText, type OverridesApi, type WriteFailure } from '../../services/overridesApi';
import type { DryRunApi } from '../../services/dryRunApi';
import { recordWords, shortDate, versionLabel } from '../../services/model';
import { MAX_REASON_CHARS, planAssign, type AssignChoice } from './plan';
import { ChoiceGroup, useWizardStyles, type ChoiceOption } from './steps';
import { WizardFrame, type OnChanged } from './WizardFrame';
import { isPinnable, versionOption, versionWindow } from './words';

export interface AssignProcedureWizardProps {
  core: CoreRoute;
  recordId: string;
  /** The form's record, for the words ("matter 2026-LIT-4471"). */
  record: { table: string; name: string | null };
  /** The lists' own query (the firm only when the user picked it, and the work date): the assignable list and the dry run are read with it. */
  query: ProceduresQuery;
  /** The panel's read client: the assignable list. */
  listsApi: RecordProceduresApi;
  api: OverridesApi;
  dryRunApi: DryRunApi;
  onChanged: OnChanged;
  onClose: () => void;
}

/** ontology-r1 K3: what the list never shows, and how to reach it. Always shown under Choose. */
export const ASSIGN_NOT_LISTED_NOTE = "Procedures with only draft versions, or for another firm, aren't listed. Publish a version, or switch the panel's firm.";

/** The reason of a `notOffered` item the server calls `OwnRow`. */
export const OWN_ROW_TEXT = 'This record already has an override for it.';

/** EB-D-227: more Procedures match than the list shows; the server's bound is in the sentence. */
export const tooManyText = (bound: number): string => `More than ${bound} Procedures match. Type part of a name to narrow the list.`;

/** EB-D-230 (O-22): said of a Gap Procedure when the server sent no gap text with it. The server's own warning is used when it is there. */
export const GAP_FALLBACK_WARNING = 'No version of this Procedure would apply on this date. If you assign it, invoices checked from then on are held until a version applies.';

/** How long the name search waits after the last keystroke before it asks. */
export const SEARCH_DEBOUNCE_MS = 400;

/** A read the client gave up on after its timeout. */
export const LIST_TIMED_OUT_TEXT = 'The list of Procedures took too long to come back.';

const APPLIES_TO_WORDS: Readonly<Record<string, string>> = { AllRecords: 'All records', Firm: 'One firm', AssignedRecordsOnly: 'Assigned records only' };

/** What the Procedure would be on this record if assigned now, as a badge (an unknown status is neutral with its own text). */
export function statusIfAssignedBadge(item: Pick<AssignableProcedureItem, 'statusIfAssigned'>): { label: string; tone: StatusBadgeTone } {
  switch (item.statusIfAssigned) {
    case 'InForce':
      return { label: 'In force', tone: 'success' };
    case 'Gap':
      return { label: 'No version', tone: 'critical' };
    case 'NotYetInForce':
      return { label: 'Not yet in force', tone: 'neutral' };
    default:
      return { label: item.statusIfAssigned, tone: 'neutral' };
  }
}

/** "Guideline · Assigned records only · Fenwick & Calder LLP invoices only" (type, appliesTo, firm). */
export function itemDetails(item: AssignableProcedureItem): string {
  const appliesTo = APPLIES_TO_WORDS[item.appliesTo] ?? item.appliesTo;
  const firm = item.firm ? `${item.firm.name ?? 'One firm'} invoices only` : 'Every firm';
  return [item.type ?? '—', appliesTo, firm].join(' · ');
}

/** The version it would follow: "v2 · from 1 Jan 2026", or that none would apply on the date. */
export const itemVersion = (v: VersionView | null): string => (v ? `${versionLabel(v)} · ${versionWindow(v)}` : 'No version on this date');

/** The gap sentence and warning of a Gap item, or null. */
export function gapWarningOf(item: AssignableProcedureItem): string | null {
  if (item.gap) return [item.gap.text, item.gap.warning].filter(Boolean).join(' ');
  return item.statusIfAssigned === 'Gap' ? GAP_FALLBACK_WARNING : null;
}

const notOfferedText = (n: NotOfferedProcedure): string => (n.reason === 'OwnRow' ? OWN_ROW_TEXT : n.reason);

/** The assignable list: being read, read, or refused (the panel's words, no list). */
type ListState =
  | { state: 'loading'; q: string | null }
  | { state: 'ready'; q: string | null; value: RecordAssignableProceduresView }
  | { state: 'failed'; q: string | null; failure: ApiFailure };

type Versions = { id: string; state: 'loading' } | { id: string; state: 'ready'; versions: VersionView[] } | { id: string; state: 'failed'; failure: WriteFailure };

interface Form {
  choice: AssignChoice;
  versionId: string;
  reason: string;
}

const NOT_OFFERED_PREFIX = 'not-offered:';
const EMPTY_FORM: Form = { choice: 'follow', versionId: '', reason: '' };

export const AssignProcedureWizard: React.FC<AssignProcedureWizardProps> = ({ core, recordId, record, query, listsApi, api, dryRunApi, onChanged, onClose }) => {
  const s = useWizardStyles();

  // ── The assignable list (task 084d-5a's route) ───────────────────────────
  // One read at a time: a newer ask abandons the one in flight, and an answer that arrives after that (or after the wizard closed) is dropped.
  const [list, setList] = React.useState<ListState>({ state: 'loading', q: null });
  const inFlight = React.useRef<AbortController | null>(null);
  const seq = React.useRef(0);
  const lastAsked = React.useRef<string | null | undefined>(undefined);
  const live = React.useRef({ listsApi, core, recordId, query });
  live.current = { listsApi, core, recordId, query };

  const ask = React.useCallback((q: string | null) => {
    const p = live.current;
    inFlight.current?.abort();
    const ctrl = new AbortController();
    inFlight.current = ctrl;
    const asked = ++seq.current;
    lastAsked.current = q;
    setList({ state: 'loading', q });
    const settled = (next: ListState | null) => {
      if (asked !== seq.current || ctrl.signal.aborted) return;
      inFlight.current = null;
      if (next) setList(next);
    };
    void p.listsApi.readAssignable(p.core, p.recordId, p.query, q, ctrl.signal).then(
      r => settled(r.ok ? { state: 'ready', q, value: r.value } : r.failure.errorCode === CODE_ABANDONED ? null : { state: 'failed', q, failure: r.failure }),
      (err: unknown) => settled({ state: 'failed', q, failure: { status: 0, errorCode: null, detail: err instanceof Error ? err.message : null, signInFailed: false } })
    );
  }, []);

  React.useEffect(
    () => () => {
      // Closed (or unmounted): abandon the read in flight; a remount asks again.
      inFlight.current?.abort();
      inFlight.current = null;
      lastAsked.current = undefined;
    },
    []
  );

  // ── The name search, re-asked after a pause ──────────────────────────────
  const [search, setSearch] = React.useState('');
  const searchKey = nameSearchOf(search);
  React.useEffect(() => {
    if (lastAsked.current === undefined) {
      ask(searchKey);
      return;
    }
    if (lastAsked.current === searchKey) return;
    const timer = setTimeout(() => ask(searchKey), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchKey, ask]);

  // The search box appears once the list said "too many", and stays while the user narrows it.
  const [searchShown, setSearchShown] = React.useState(false);
  if (!searchShown && list.state === 'ready' && list.value.tooMany) setSearchShown(true);

  // ── The choice ───────────────────────────────────────────────────────────
  const [chosenId, setChosenId] = React.useState<string | null>(null);
  const offered = list.state === 'ready' ? (list.value.procedures ?? []) : [];
  const chosen = chosenId ? offered.find(p => p.id === chosenId) : undefined;

  const [form, setForm] = React.useState<Form>(EMPTY_FORM);
  const [formOf, setFormOf] = React.useState<string | null>(null);
  if (formOf !== (chosen?.id ?? null)) {
    // Another Procedure was chosen: its "On this record" starts afresh.
    setFormOf(chosen?.id ?? null);
    setForm(EMPTY_FORM);
  }
  const set = (patch: Partial<Form>) => setForm(f => ({ ...f, ...patch }));

  // ── The Pin picker's versions (task 081's read), asked for when Pin is the selected choice ──
  const [versions, setVersions] = React.useState<Versions | null>(null);
  const chosenKey = chosen?.id ?? null;
  const wantsVersions = chosenKey !== null && form.choice === 'pin';
  const versionsAskedFor = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!wantsVersions || chosenKey === null || versionsAskedFor.current === chosenKey) return;
    const id = chosenKey;
    versionsAskedFor.current = id;
    let alive = true;
    setVersions({ id, state: 'loading' });
    void api.procedureVersions(id).then(r => {
      if (alive) setVersions(r.ok ? { id, state: 'ready', versions: r.value } : { id, state: 'failed', failure: r.failure });
    });
    return () => {
      alive = false;
      versionsAskedFor.current = null;
    };
  }, [wantsVersions, api, chosenKey]);

  const pinnable = chosen && versions?.id === chosen.id && versions.state === 'ready' ? versions.versions.filter(isPinnable) : [];
  const pinned = pinnable.find(v => v.id === form.versionId);
  const plan = planAssign(chosen?.id, form.choice, { versionId: form.versionId, reason: form.reason, pinnable: pinnable.map(v => v.id) });
  const recordLabel = recordWords(record.table, record.name);

  // ── Step 1: Choose ───────────────────────────────────────────────────────
  const failure = list.state === 'failed' ? list.failure : null;
  const failureWords = failure
    ? failure.errorCode === CODE_TIMED_OUT
      ? { title: LIST_TIMED_OUT_TEXT, body: 'Retry in a moment.' }
      : failureMessage(failureKind(failure))
    : null;
  const ready = list.state === 'ready' ? list.value : null;
  const notOffered = ready && !ready.tooMany ? ready.notOffered : [];

  const chooseStep = (
    <div className={s.step} data-testid="assign-choose-step">
      {(searchShown || search.length > 0) && (
        <Field label="Procedure name" hint={`Part of the name, at most ${MAX_NAME_SEARCH_CHARS} characters.`}>
          <Input value={search} maxLength={MAX_NAME_SEARCH_CHARS} onChange={(_, d) => setSearch(d.value)} data-testid="assign-search" />
        </Field>
      )}
      <div aria-live="polite" aria-busy={list.state === 'loading'} className={s.step}>
        {list.state === 'loading' && <Spinner size="tiny" label="Reading the Procedures you can assign…" data-testid="assign-loading" />}
        {failureWords && (
          <MessageBar intent="warning" layout="multiline" data-testid="assign-failed">
            <MessageBarBody>
              <MessageBarTitle>{failureWords.title}</MessageBarTitle>
              {failureWords.body}
            </MessageBarBody>
            <MessageBarActions>
              <Button size="small" onClick={() => ask(lastAsked.current ?? null)} data-testid="assign-retry">
                Retry
              </Button>
            </MessageBarActions>
          </MessageBar>
        )}
        {ready?.tooMany && (
          <MessageBar intent="info" layout="multiline" data-testid="assign-too-many">
            <MessageBarBody>{tooManyText(ready.bound)}</MessageBarBody>
          </MessageBar>
        )}
        {ready && !ready.tooMany && (
          <div className={s.stack} data-testid="assign-list">
            <Caption1 className={s.muted} data-testid="assign-label">
              {ready.label}
            </Caption1>
            {offered.length === 0 && notOffered.length === 0 ? (
              <Text data-testid="assign-empty">
                {ready.q ? `No Procedure whose name contains "${ready.q}" can be assigned here.` : 'No Procedure can be assigned to this record for this firm and date.'}
              </Text>
            ) : (
              <RadioGroup
                aria-label="Procedure"
                value={chosen?.id ?? ''}
                onChange={(_, d) => {
                  // A Procedure that is not offered cannot be chosen, however the event arrived.
                  if (d.value.startsWith(NOT_OFFERED_PREFIX)) return;
                  setChosenId(d.value);
                }}
                className={s.choice}
              >
                {offered.map(p => {
                  const badge = statusIfAssignedBadge(p);
                  const warning = gapWarningOf(p);
                  return (
                    <div key={p.id} data-testid={`assign-item-${p.id}`}>
                      <Radio
                        value={p.id}
                        label={
                          <span className={s.stack}>
                            <Text weight="semibold">{p.name}</Text>
                            <Caption1 className={s.muted}>{itemDetails(p)}</Caption1>
                            <span className={s.change}>
                              <StatusBadge label={badge.label} tone={badge.tone} />
                              <Caption1>{itemVersion(p.versionInForce)}</Caption1>
                            </span>
                          </span>
                        }
                      />
                      {warning && (
                        <div className={s.choiceBody}>
                          <MessageBar intent="warning" layout="multiline" data-testid={`assign-gap-${p.id}`}>
                            <MessageBarBody>{warning}</MessageBarBody>
                          </MessageBar>
                        </div>
                      )}
                    </div>
                  );
                })}
                {notOffered.map(n => (
                  <div key={n.id} data-testid={`assign-not-offered-${n.id}`}>
                    <Radio
                      value={`${NOT_OFFERED_PREFIX}${n.id}`}
                      disabled
                      label={
                        <span className={s.stack}>
                          <Text>{n.name ?? 'A Procedure'}</Text>
                          <Caption1 className={s.muted}>{notOfferedText(n)}</Caption1>
                        </span>
                      }
                    />
                  </div>
                ))}
              </RadioGroup>
            )}
          </div>
        )}
      </div>
      <Caption1 className={s.muted} data-testid="assign-not-listed-note">
        {ASSIGN_NOT_LISTED_NOTE}
      </Caption1>
    </div>
  );

  // ── Step 2: On this record ───────────────────────────────────────────────
  const latest = chosen?.versionInForce ?? null;
  const options: Array<ChoiceOption<AssignChoice>> = [
    {
      value: 'follow',
      label: 'Follow latest',
      caption: `The version in force on the work date${latest ? ` (${versionLabel(latest)} on ${shortDate(query.date)})` : ''}.`,
    },
    {
      value: 'pin',
      label: 'Pin a version',
      caption: 'This version applies whatever is newer, e.g. to keep grandfathered rates.',
      body: (
        <>
          <Field
            label="Version"
            required
            validationState={versions?.state === 'failed' ? 'error' : 'none'}
            validationMessage={versions?.state === 'failed' ? `The versions could not be read. ${failureText(versions.failure)}` : undefined}
          >
            {versions?.state === 'loading' ? (
              <Spinner size="tiny" label="Reading the versions…" />
            ) : (
              <Dropdown
                value={pinned ? versionOption(pinned) : ''}
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
          <Field label="Reason" required hint={`At most ${MAX_REASON_CHARS} characters.`}>
            <Textarea value={form.reason} onChange={(_, d) => set({ reason: d.value })} resize="vertical" maxLength={MAX_REASON_CHARS} />
          </Field>
        </>
      ),
    },
  ];

  const modeStep = (
    <div className={s.step} data-testid="mode-step">
      {chosen && <Text weight="semibold">{chosen.name}</Text>}
      <ChoiceGroup ariaLabel="On this record" options={options} value={form.choice} onChange={choice => set({ choice })} />
      {wantsVersions && versions?.state === 'ready' && pinnable.length === 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>No version of this Procedure has been in force yet, so there is nothing to pin.</MessageBarBody>
        </MessageBar>
      )}
    </div>
  );

  // ── Impact and Confirm ────────────────────────────────────────────────────
  const before = 'Not on this record';
  const after = form.choice === 'pin' ? (pinned ? `Pinned · ${versionLabel(pinned)}` : 'Pinned') : `Follows the latest version${latest ? ` · ${versionLabel(latest)}` : ''}`;
  const chosenWarning = chosen ? gapWarningOf(chosen) : null;
  const confirmRows: Array<readonly [string, React.ReactNode]> = [
    ['Record', recordLabel],
    ['Procedure', chosen?.name ?? '—'],
    ['Change', `${before} → ${after}`],
    ...(form.choice === 'pin' && form.reason.trim().length > 0 ? ([['Reason', form.reason.trim()]] as const) : []),
  ];

  return (
    <WizardFrame
      title="Assign Procedure"
      resetKey="assign"
      api={api}
      dryRunApi={dryRunApi}
      query={query}
      core={core}
      recordId={recordId}
      readOnly={false}
      reviewLabel="Procedure"
      reviewStep={chooseStep}
      reviewCanAdvance={!!chosen}
      modeStep={modeStep}
      canChoose={plan.steps.length > 0}
      plan={plan}
      before={before}
      after={after}
      impactExtra={
        chosenWarning ? (
          <MessageBar intent="warning" layout="multiline" data-testid="assign-impact-gap">
            <MessageBarBody>{chosenWarning}</MessageBarBody>
          </MessageBar>
        ) : undefined
      }
      confirmRows={confirmRows}
      finishLabel="Assign Procedure"
      successTitle={form.choice === 'pin' ? 'Procedure assigned, version pinned' : 'Procedure assigned'}
      successBody={`${chosen?.name ?? 'The Procedure'} on ${recordLabel}: ${after}.`}
      partialNote="The Procedure was not assigned. Close this window and try again."
      onChanged={onChanged}
      onClose={onClose}
    />
  );
};
