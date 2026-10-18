/**
 * Fixtures for the rule wizard tests (task 102). The catalog is deliberately NOT the real one: its codes, labels, columns and tables are
 * invented ("frobnicate", "zz_widget", "Gold"), so a wizard that held a list of its own would fail to show them (D-149).
 */

import type {
  AuthoringCatalog,
  AuthoringResult,
  DryRunResult,
  OntologyAuthoringService,
  PolicySaved,
  RuleValidation,
  VersionPublished,
} from '../authoringService';

export const catalog: AuthoringCatalog = {
  ruleTypes: ['Existence', 'Threshold'],
  subjects: [
    {
      entity: 'zz_widget',
      dateOnlyFields: ['zz_due'],
      thresholdFields: [{ field: 'zz_load', min: 0, max: 250 }],
      authorableFields: ['zz_state', 'zz_due', 'zz_colour'],
      joins: [
        {
          related: 'zz_note',
          path: 'zz_widgetref',
          dateOnlyFields: [],
          authorableFields: ['zz_kind', 'zz_noted'],
        },
        {
          related: 'zz_mail',
          path: 'zz_widgetlink',
          dateOnlyFields: [],
          authorableFields: ['zz_triage', 'zz_noted'],
        },
      ],
    },
    {
      entity: 'zz_gadget',
      dateOnlyFields: [],
      thresholdFields: [],
      authorableFields: ['zz_size'],
      joins: [],
    },
  ],
  operators: {
    comparison: ['=', '<>', '>=', '<=', '>', '<'],
    threshold: ['>=', '<='],
    rangeLower: ['>=', '>'],
    rangeUpper: ['<=', '<'],
    nullTests: ['null', 'notNull'],
    maxInListLength: 3,
  },
  relativeDates: { forms: ['now', 'now-{N}d', 'now+{N}d'], maxDays: 9999 },
  limits: { maxClauses: 2, maxRuleBodyLength: 4000, defaultQuietWindowDays: 21, maxQuietWindowDays: 90 },
  triage: {
    entity: 'zz_mail',
    field: 'zz_triage',
    categories: [
      { id: 'cat-aaa', name: 'Cheese', enabled: true },
      { id: 'cat-bbb', name: 'Biscuits', enabled: false },
    ],
  },
  lanes: [
    {
      lane: 'Decide',
      actions: [
        { code: 'frobnicate', label: 'Frobnicate it', excludes: ['defrobnicate'] },
        { code: 'defrobnicate', label: 'Un-frobnicate it', excludes: ['frobnicate'] },
        { code: 'both-ways', label: 'Works both ways', excludes: [] },
      ],
      nextSteps: [
        { code: 'both-ways', label: 'Works both ways', excludes: [] },
        { code: 'ring-bell', label: 'Ring the bell', excludes: [] },
      ],
      dismissalReasons: [
        { value: 'because-tuesday', label: 'It is Tuesday' },
        { value: 'because-rain', label: 'It is raining' },
      ],
      severities: [
        { value: 11, label: 'Gold' },
        { value: 22, label: 'Silver' },
      ],
      workTypes: [{ value: 7, label: 'Polish' }],
    },
    {
      lane: 'Do',
      actions: [{ code: 'tidy-up', label: 'Tidy up', excludes: [] }],
      nextSteps: [{ code: 'ring-bell', label: 'Ring the bell', excludes: [] }],
      dismissalReasons: [{ value: 'because-moon', label: 'The moon is up' }],
      severities: [{ value: 11, label: 'Gold' }],
      workTypes: [{ value: 7, label: 'Polish' }],
    },
  ],
};

export const dryRunResult = (over: Partial<DryRunResult> = {}): DryRunResult => ({
  rows: [
    {
      subjectEntity: 'zz_widget',
      subjectId: 'w-1',
      name: 'Widget One',
      number: 'W-001',
      coreEntity: null,
      coreId: null,
      status: 'new',
    },
  ],
  open: 4,
  suppressed: 1,
  new: 6,
  wouldSupersede: 3,
  wouldReraise: 2,
  notPlaceable: 0,
  policyEnabled: true,
  windowAnchorUtc: '2026-10-17T00:00:00Z',
  recorded: true,
  ...over,
});

export const validation = (over: Partial<RuleValidation> = {}): RuleValidation => ({
  valid: true,
  errors: [],
  reasonCode: null,
  templateEligibleFields: ['zz_state'],
  ...over,
});

export const failure = (
  over: Partial<Extract<AuthoringResult<never>, { ok: false }>> = {}
): Extract<AuthoringResult<never>, { ok: false }> => ({
  ok: false,
  status: 400,
  reasonCode: null,
  message: 'The request was not accepted.',
  fieldErrors: {},
  policyId: null,
  outcomeUnknown: false,
  ...over,
});

export const saved: PolicySaved = {
  policyId: 'policy-new',
  policyVersionId: 'version-1',
  versionNumber: 1,
  enabled: false,
};

export const published: VersionPublished = {
  policyId: 'policy-1',
  policyVersionId: 'version-3',
  versionNumber: 3,
  previousVersionId: 'version-2',
  reevaluation: 'completed',
  supersededClosed: 3,
  raised: 2,
};

/** A service whose every call succeeds. Each is a jest mock so a test can read its calls or replace its answer. */
export function makeAuthoringService(over: Partial<OntologyAuthoringService> = {}): jest.Mocked<OntologyAuthoringService> {
  return {
    getCatalog: jest.fn(async () => ({ ok: true as const, data: catalog })),
    validate: jest.fn(async () => ({ ok: true as const, data: validation() })),
    dryRun: jest.fn(async () => ({ ok: true as const, data: dryRunResult() })),
    savePolicy: jest.fn(async () => ({ ok: true as const, data: saved })),
    publishVersion: jest.fn(async () => ({ ok: true as const, data: published })),
    ...over,
  } as jest.Mocked<OntologyAuthoringService>;
}
