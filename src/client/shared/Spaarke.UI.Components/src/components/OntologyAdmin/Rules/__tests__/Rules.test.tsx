/**
 * Ontology admin Rules area (task 100; FR-57): service, list, rule page, On/Off, Retire.
 *
 *  - the service calls the four routes, never throws, and reads a failure whether the fetch THROWS (an ApiError) or RETURNS it;
 *  - the list shows lane, On/Off, version in force and the numbers, and the All / Decide / Do / Needs a look filters narrow it;
 *  - the rule page is read-only: its only controls are On/Off and Retire; a retired rule has neither;
 *  - Retire needs a reason (the button stays disabled), shows what the closure did, and shows a refusal with the rule unchanged;
 *  - tokens only (ADR-021): no colour literal in the sources.
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RuleList } from '../RuleList';
import { RulePage } from '../RulePage';
import { RulesArea } from '../RulesArea';
import { RetireRuleDialog, describeClosure } from '../RetireRuleDialog';
import { createOntologyRulesService } from '../rulesService';
import type { OntologyRulesService, RuleDetail, RuleListResponse, RuleSummary } from '../rulesService';

const metrics = (over: Partial<NonNullable<RuleSummary['metrics']>> = {}): NonNullable<RuleSummary['metrics']> => ({
  openItems: 2,
  lastFiredOn: '2026-10-15T09:30:00Z',
  actionRate: { acted: 5, dismissed: 3, rate: 0.625, tooFewToJudge: false },
  needsALook: false,
  ...over,
});

const summary = (over: Partial<RuleSummary> = {}): RuleSummary => ({
  policyId: '11111111-1111-4111-8111-111111111111',
  code: 'POL-A',
  name: 'Rule A',
  lane: 'Do',
  state: 'On',
  enabled: true,
  versionNumber: 2,
  headline: 'Open task past its due date',
  severity: 'Warning',
  subjectType: 'Event',
  metrics: metrics(),
  ...over,
});

const list = (rules: RuleSummary[], over: Partial<RuleListResponse> = {}): RuleListResponse => ({
  rules,
  metricsAvailable: true,
  actionRateWindowDays: 90,
  minimumJudged: 5,
  ...over,
});

const detail = (over: Partial<RuleDetail> = {}): RuleDetail => ({
  summary: summary(),
  condition: {
    ruleType: 'Existence',
    clauses: [{ clauseId: 'c1', kind: 'when', text: 'The task is open and past due', formal: 'statuscode = 1 and sprk_duedate <= now-1d' }],
    refusal: null,
  },
  wording: { headline: 'Open task past its due date', messageTemplate: 'This task is open.', proposedAction: 'mark-complete: finish it.' },
  plan: {
    actions: [{ code: 'mark-complete', label: 'Mark complete', effectLines: [] }],
    nextSteps: [],
    dismissalReasons: ['Not relevant'],
    refusal: null,
  },
  scope: { tenant: null, matterId: null, matterName: null, priority: 500 },
  noise: { countsTowardSuppression: true, suppressAfterDismissals: 3, suppressionWindowDays: 30, quietWindowDays: 14 },
  behaviour: { lane: 'Do', severity: 'Warning', workType: 'Finish or reschedule', subjectType: 'Event', state: 'On', retiredReason: null },
  versions: [
    { versionId: 'v2', number: 2, headline: 'Open task past its due date', authoredBy: 'Ada Author', createdOn: '2026-10-14T17:30:00Z', inForceFrom: '2026-10-14T00:00:00Z', inForceTo: null, inForce: true },
    { versionId: 'v1', number: 1, headline: 'Old', authoredBy: 'Ada Author', createdOn: '2026-10-01T10:00:00Z', inForceFrom: null, inForceTo: '2026-10-14T00:00:00Z', inForce: false },
  ],
  history: [{ when: '2026-10-14T17:30:00Z', by: 'Ada Author', event: 'Version 2 authored' }],
  metricsAvailable: true,
  actionRateWindowDays: 90,
  minimumJudged: 5,
  ...over,
});

const fail = (status: number | null, message = 'No.') => ({ ok: false as const, status, reasonCode: null, message });

const makeService = (over: Partial<OntologyRulesService> = {}): OntologyRulesService => ({
  list: jest.fn(async () => ({ ok: true as const, data: list([summary()]) })),
  get: jest.fn(async () => ({ ok: true as const, data: detail() })),
  setEnabled: jest.fn(async (policyId: string, enabled: boolean) => ({ ok: true as const, data: { policyId, enabled } })),
  retire: jest.fn(async (policyId: string, reason: string) => ({
    ok: true as const,
    data: { policyId, state: 'Retired', reason, closure: { closed: 3, changedSinceRead: 0, failed: 0, policyFailures: 0, complete: true, unavailable: false } },
  })),
  ...over,
});

const renderUi = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

// ── the service ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

describe('createOntologyRulesService', () => {
  const ok = (json: unknown) => ({ ok: true, status: 200, json: async () => json }) as unknown as Response;
  const bad = (status: number, json: unknown) => ({ ok: false, status, json: async () => json }) as unknown as Response;

  it('calls the four routes on the base URL, as the caller, with the right verbs and bodies', async () => {
    const fetch = jest.fn(async () => ok({}));
    const service = createOntologyRulesService({ fetch, baseUrl: 'https://bff.example/' });

    await service.list();
    await service.get('abc');
    await service.setEnabled('abc', false);
    await service.retire('abc', 'Replaced');

    const calls = fetch.mock.calls as unknown as Array<[string, RequestInit]>;
    expect(calls.map(c => [c[1].method, c[0]])).toEqual([
      ['GET', 'https://bff.example/api/v1/ontology/admin/rules'],
      ['GET', 'https://bff.example/api/v1/ontology/admin/rules/abc'],
      ['PUT', 'https://bff.example/api/v1/ontology/admin/rules/abc/enabled'],
      ['POST', 'https://bff.example/api/v1/ontology/admin/rules/abc/retire'],
    ]);
    expect(calls[2][1].body).toBe('{"enabled":false}');
    expect(calls[3][1].body).toBe('{"reason":"Replaced"}');
  });

  it('reads a RETURNED failure: a Console User write is the Dataverse refusal, not a silent success', async () => {
    const service = createOntologyRulesService({
      fetch: jest.fn(async () => bad(403, { reasonCode: 'ontology.admin.forbidden' })),
      baseUrl: 'https://bff.example',
    });

    const answer = await service.setEnabled('abc', false);

    expect(answer).toMatchObject({ ok: false, status: 403, reasonCode: 'ontology.admin.forbidden' });
    expect(answer.ok ? '' : answer.message).toContain('Ontology Administrator');
  });

  it('reads a THROWN ApiError (what @spaarke/auth throws for a non-2xx) the same way', async () => {
    const apiError = Object.assign(new Error('Forbidden'), {
      name: 'ApiError',
      status: 403,
      problemDetails: { reasonCode: 'sdap.access.deny.caller_unresolved' },
    });
    const service = createOntologyRulesService({ fetch: jest.fn(async () => Promise.reject(apiError)), baseUrl: 'https://bff.example' });

    const answer = await service.retire('abc', 'x');

    expect(answer).toMatchObject({ ok: false, status: 403, reasonCode: 'sdap.access.deny.caller_unresolved' });
    expect(answer.ok ? '' : answer.message).toContain('could not be matched');
  });

  it('shows the validation message for a 400 and says "nothing was changed" for a network failure', async () => {
    const validation = createOntologyRulesService({
      fetch: jest.fn(async () => bad(400, { errors: { reason: ['A reason is required to retire a rule.'] } })),
      baseUrl: 'https://bff.example',
    });
    const network = createOntologyRulesService({ fetch: jest.fn(async () => Promise.reject(new TypeError('offline'))), baseUrl: 'https://bff.example' });

    expect((await validation.retire('abc', '')).ok).toBe(false);
    const v = await validation.retire('abc', '');
    expect(v.ok ? '' : v.message).toBe('A reason is required to retire a rule.');
    const n = await network.list();
    expect(n).toMatchObject({ ok: false, status: null });
    expect(n.ok ? '' : n.message).toContain('Nothing was changed');
  });

  it('never rejects, even for an unreadable success body', async () => {
    const service = createOntologyRulesService({
      fetch: jest.fn(async () => ({ ok: true, status: 200, json: async () => Promise.reject(new SyntaxError('bad')) }) as unknown as Response),
      baseUrl: 'https://bff.example',
    });

    await expect(service.list()).resolves.toMatchObject({ ok: false });
  });
});

// ── the list ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

describe('RuleList', () => {
  const rules = [
    summary({ policyId: 'p1', code: 'POL-DECIDE', lane: 'Decide', versionNumber: 1 }),
    summary({ policyId: 'p2', code: 'POL-DO', lane: 'Do', versionNumber: 4, state: 'Off', enabled: false }),
    summary({
      policyId: 'p3',
      code: 'POL-LOOK',
      lane: 'Do',
      metrics: metrics({ actionRate: { acted: 1, dismissed: 5, rate: 1 / 6, tooFewToJudge: false }, needsALook: true }),
    }),
    summary({ policyId: 'p4', code: 'POL-FEW', lane: 'Decide', metrics: metrics({ actionRate: { acted: 1, dismissed: 1, rate: null, tooFewToJudge: true } }) }),
  ];

  it('shows lane, On / Off, the version in force and the numbers for every rule', async () => {
    renderUi(<RuleList service={makeService({ list: jest.fn(async () => ({ ok: true as const, data: list(rules) })) })} onOpen={jest.fn()} />);

    const decide = await screen.findByTestId('rule-row-POL-DECIDE');
    expect(decide).toHaveTextContent('Decide');
    expect(decide).toHaveTextContent('On');
    expect(decide).toHaveTextContent('v1');
    expect(decide).toHaveTextContent('63% (5 of 8)');
    expect(screen.getByTestId('rule-row-POL-DO')).toHaveTextContent('Off');
    expect(screen.getByTestId('rule-row-POL-DO')).toHaveTextContent('v4');
    expect(screen.getByTestId('rule-row-POL-FEW')).toHaveTextContent('Too few to judge');
  });

  it('filters: All, Decide, Do and Needs a look', async () => {
    const user = userEvent.setup();
    renderUi(<RuleList service={makeService({ list: jest.fn(async () => ({ ok: true as const, data: list(rules) })) })} onOpen={jest.fn()} />);
    await screen.findByTestId('rule-row-POL-DECIDE');
    const shown = () => screen.queryAllByRole('row').filter(r => r.dataset.testid?.startsWith('rule-row-')).map(r => r.dataset.testid);

    expect(shown()).toHaveLength(4);
    await user.click(screen.getByTestId('rules-filter-decide'));
    expect(shown()).toEqual(['rule-row-POL-DECIDE', 'rule-row-POL-FEW']);
    await user.click(screen.getByTestId('rules-filter-do'));
    expect(shown()).toEqual(['rule-row-POL-DO', 'rule-row-POL-LOOK']);
    await user.click(screen.getByTestId('rules-filter-needsALook'));
    expect(shown()).toEqual(['rule-row-POL-LOOK']);
    await user.click(screen.getByTestId('rules-filter-all'));
    expect(shown()).toHaveLength(4);
  });

  it('says so when the numbers could not be read, and still lists every rule', async () => {
    const stripped = rules.map(r => ({ ...r, metrics: null }));
    renderUi(<RuleList service={makeService({ list: jest.fn(async () => ({ ok: true as const, data: list(stripped, { metricsAvailable: false }) })) })} onOpen={jest.fn()} />);

    expect(await screen.findByTestId('rules-metrics-unavailable')).toBeInTheDocument();
    expect(screen.getByTestId('rule-row-POL-DECIDE')).toBeInTheDocument();
  });

  it('shows an error with a retry, never an empty list, when the rules cannot be read', async () => {
    const service = makeService({ list: jest.fn().mockResolvedValueOnce(fail(503, 'The rules could not be reached.')).mockResolvedValue({ ok: true as const, data: list(rules) }) });
    const user = userEvent.setup();
    renderUi(<RuleList service={service} onOpen={jest.fn()} />);

    expect(await screen.findByTestId('rules-list-error')).toHaveTextContent('could not be reached');
    expect(screen.queryByTestId('rules-list-empty')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByTestId('rule-row-POL-DECIDE')).toBeInTheDocument();
  });

  it('opens the rule page for the clicked row', async () => {
    const onOpen = jest.fn();
    const user = userEvent.setup();
    renderUi(<RuleList service={makeService({ list: jest.fn(async () => ({ ok: true as const, data: list(rules) })) })} onOpen={onOpen} />);

    await user.click(await screen.findByTestId('rule-row-POL-DO'));

    expect(onOpen).toHaveBeenCalledWith('p2');
  });
});

// ── the rule page ────────────────────────────────────────────────────────────────────────────────────────────────────────────

describe('RulePage', () => {
  it('shows every section the owner listed, read-only', async () => {
    renderUi(<RulePage service={makeService()} policyId="p1" onBack={jest.fn()} />);

    const page = await screen.findByTestId('ontology-rule-page');
    expect(page).toHaveTextContent('The task is open and past due');
    expect(page).toHaveTextContent('statuscode = 1 and sprk_duedate <= now-1d');
    for (const section of ['Condition', 'Wording', 'Decision plan', 'Scope', 'Noise', 'Behaviour', 'Versions', 'Change history', 'Numbers']) {
      expect(screen.getByRole('region', { name: section })).toBeInTheDocument();
    }
    expect(screen.getAllByTestId('rule-version-row')).toHaveLength(2);
    expect(screen.getByTestId('rule-history-row')).toHaveTextContent('Version 2 authored');
    // Read-only: the only controls are back, On/Off and Retire (no text input or edit control).
    expect(screen.queryAllByRole('textbox')).toHaveLength(0);
    expect(screen.getAllByRole('button').map(b => b.textContent)).toEqual(['All rules', 'Retire']);
    expect(screen.getByTestId('rule-enabled-switch')).toBeInTheDocument();
  });

  it('shows the refusal, not a guess, for a condition the describer refused', async () => {
    const service = makeService({
      get: jest.fn(async () => ({
        ok: true as const,
        data: detail({ condition: { ruleType: 'Existence', clauses: [], refusal: 'This rule body cannot be described.' } }),
      })),
    });
    renderUi(<RulePage service={service} policyId="p1" onBack={jest.fn()} />);

    expect(await screen.findByTestId('rule-condition-refusal')).toHaveTextContent('cannot be described');
  });

  it('Off writes only the switch value, then reloads the page', async () => {
    const service = makeService();
    const user = userEvent.setup();
    renderUi(<RulePage service={service} policyId="p1" onBack={jest.fn()} />);

    await user.click(await screen.findByRole('switch'));

    await waitFor(() => expect(service.setEnabled).toHaveBeenCalledWith('p1', false));
    expect(service.retire).not.toHaveBeenCalled();
    await waitFor(() => expect(service.get).toHaveBeenCalledTimes(2));
  });

  it('shows the refusal, and leaves the switch as it was, when Dataverse refuses the write', async () => {
    const service = makeService({
      setEnabled: jest.fn(async () => fail(403, 'Dataverse refused this change. It needs the Spaarke Ontology Administrator role.')),
    });
    const user = userEvent.setup();
    renderUi(<RulePage service={service} policyId="p1" onBack={jest.fn()} />);

    await user.click(await screen.findByRole('switch'));

    expect(await screen.findByTestId('rule-action-error')).toHaveTextContent('Ontology Administrator');
    expect(screen.getByRole('switch')).toBeChecked();
    expect(service.get).toHaveBeenCalledTimes(1);
  });

  it('a retired rule shows its reason and has neither On/Off nor Retire', async () => {
    const retired = detail({
      summary: summary({ state: 'Retired', enabled: false }),
      behaviour: { ...detail().behaviour, state: 'Retired', retiredReason: 'Replaced by POL-NEW' },
    });
    renderUi(<RulePage service={makeService({ get: jest.fn(async () => ({ ok: true as const, data: retired })) })} policyId="p1" onBack={jest.fn()} />);

    expect(await screen.findByTestId('rule-retired-note')).toHaveTextContent('Replaced by POL-NEW');
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    expect(screen.queryByTestId('rule-retire-button')).not.toBeInTheDocument();
  });

  it('a 404 is an error with the way back, not a blank page', async () => {
    const onBack = jest.fn();
    const user = userEvent.setup();
    renderUi(<RulePage service={makeService({ get: jest.fn(async () => fail(404, 'That rule no longer exists, or it is not an Ontology rule.')) })} policyId="p1" onBack={onBack} />);

    expect(await screen.findByTestId('rule-page-error')).toHaveTextContent('no longer exists');
    await user.click(screen.getByTestId('rule-page-back'));
    expect(onBack).toHaveBeenCalled();
  });
});

// ── Retire ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────

describe('Retire', () => {
  // fireEvent, not user.type: the Fluent dialog's focus trap drops focus in jsdom mid-typing, which is a test-environment artefact.
  const typeReason = async (text: string) => fireEvent.change(await screen.findByTestId('retire-rule-reason'), { target: { value: text } });
  const dialog = (over: Partial<React.ComponentProps<typeof RetireRuleDialog>> = {}) => {
    const props = {
      open: true,
      ruleCode: 'POL-A',
      ruleName: 'Rule A',
      onRetire: (reason: string) => makeService().retire('p1', reason),
      onCancel: jest.fn(),
      onDone: jest.fn(),
      ...over,
    };
    renderUi(<RetireRuleDialog {...props} />);
    return props;
  };

  it('keeps Retire disabled until there is a reason, and never calls the service without one', async () => {
    const onRetire = jest.fn();
    const user = userEvent.setup();
    dialog({ onRetire });

    const button = await screen.findByRole('button', { name: 'Retire rule' });
    expect(button).toBeDisabled();
    await typeReason('   ');
    expect(button).toBeDisabled();
    await user.click(button);
    expect(onRetire).not.toHaveBeenCalled();
  });

  it('retires with the trimmed reason and then shows what the closure did', async () => {
    const onRetire = jest.fn(async (reason: string) => ({
      ok: true as const,
      data: { policyId: 'p1', state: 'Retired', reason, closure: { closed: 3, changedSinceRead: 0, failed: 0, policyFailures: 0, complete: true, unavailable: false } },
    }));
    const onDone = jest.fn();
    const user = userEvent.setup();
    dialog({ onRetire, onDone });

    await typeReason('  Replaced by POL-NEW  ');
    await user.click(await screen.findByRole('button', { name: 'Retire rule' }));

    expect(onRetire).toHaveBeenCalledWith('Replaced by POL-NEW');
    expect(await screen.findByTestId('retire-rule-result')).toHaveTextContent('3 open items were closed');
    await user.click(screen.getByRole('button', { name: 'Done' }));
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('shows the refusal in the dialog and stays on the form when the retire is refused', async () => {
    const onRetire = jest.fn(async () => fail(403, 'Dataverse refused this change. It needs the Spaarke Ontology Administrator role.'));
    const onDone = jest.fn();
    const user = userEvent.setup();
    dialog({ onRetire, onDone });

    await typeReason('No longer needed');
    await user.click(await screen.findByRole('button', { name: 'Retire rule' }));

    expect(await screen.findByTestId('retire-rule-error')).toHaveTextContent('Ontology Administrator');
    expect(screen.queryByTestId('retire-rule-result')).not.toBeInTheDocument();
    expect(onDone).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Retire rule' })).toBeEnabled();
  });

  it('describeClosure: complete, incomplete, failed and unavailable are told apart', () => {
    const base = { closed: 0, changedSinceRead: 0, failed: 0, policyFailures: 0, complete: true, unavailable: false };
    expect(describeClosure({ ...base })).toMatchObject({ intent: 'success', text: expect.stringContaining('no open items') });
    expect(describeClosure({ ...base, closed: 1 }).text).toContain('1 open item was closed');
    expect(describeClosure({ ...base, closed: 2, complete: false })).toMatchObject({ intent: 'warning', text: expect.stringContaining('some are still open') });
    expect(describeClosure({ ...base, closed: 2, failed: 1, complete: false }).text).toContain('1 could not be closed');
    expect(describeClosure({ ...base, unavailable: true, complete: false })).toMatchObject({ intent: 'warning', text: expect.stringContaining('could not be closed just now') });
  });
});

// ── the area, and the tokens rule ────────────────────────────────────────────────────────────────────────────────────────────

describe('RulesArea', () => {
  it('opens the page from the list and returns to a reloaded list', async () => {
    const service = makeService();
    const user = userEvent.setup();
    renderUi(<RulesArea service={service} />);

    await user.click(await screen.findByTestId('rule-row-POL-A'));
    expect(await screen.findByTestId('ontology-rule-page')).toBeInTheDocument();
    await user.click(screen.getByTestId('rule-page-back'));

    expect(await screen.findByTestId('ontology-rules-list')).toBeInTheDocument();
    expect(service.list).toHaveBeenCalledTimes(2);
  });
});

describe('Rules sources - Fluent tokens only (ADR-021)', () => {
  it('contains no colour literal', () => {
    const dir = path.resolve(__dirname, '..');
    const sources = fs.readdirSync(dir).filter(f => /\.(ts|tsx)$/.test(f));
    expect(sources.length).toBeGreaterThan(4);
    for (const file of sources) {
      const text = fs.readFileSync(path.join(dir, file), 'utf8');
      expect({ file, hex: /#[0-9a-fA-F]{3,8}\b/.test(text), rgb: /\brgba?\(/.test(text) }).toEqual({ file, hex: false, rgb: false });
    }
  });
});
