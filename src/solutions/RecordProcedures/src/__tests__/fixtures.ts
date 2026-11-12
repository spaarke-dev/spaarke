/**
 * Jest fixtures for the Record Procedures page (ebilling-r1 task 084a): task 084's DTOs, mirroring the prototype's three mock records
 * (`spaarke-prototype/projects/ebilling.r1/src/procedures/mockProcedures.ts`; read, never imported):
 *
 *   - Matter 2026-LIT-4471 (Acme v. Northwind), billing firm Fenwick & Calder LLP: the all-records OCG, a Fenwick rate card PINNED to v1
 *     (v2 available), the Fenwick travel policy EXCLUDED here, a manual billing-instructions Procedure, a fee schedule whose only version
 *     ended 30 Jun 2026 (GAP), and a Procedure that is NOT YET in force. Rules: BLOCK runs, VAGUE is EXCLUDED, HOURS is CHANGED to 12 h and
 *     STALE (based on v1 while v2 is current).
 *   - Project PRJ-20418: pins the Fenwick rate card to the newest version; a record rule for RUSH, a rule dropped from the 2026 OCG, is
 *     BASE RETIRED.
 *   - Work assignment WA-30552, filed under BOTH of them: the rate card shows CONFLICTING PINS (a gap), the travel policy is excluded on the
 *     matter (distance 1), and the project's RUSH record rule is inherited.
 *
 * Shapes follow `RecordProcedureReadService.cs` (camelCase). Dates are yyyy-MM-dd.
 */

import type {
  ChainRecord,
  FirmRef,
  GapView,
  OverrideView,
  ProcedureItem,
  RecordProceduresView,
  RecordRulesInForceView,
  RuleItem,
  RuleMark,
  SourceView,
  VersionView,
} from '../services/recordProceduresApi';

export const TODAY = '2026-10-21';

export const MATTER_ID = '4471aaaa-0000-4000-8000-000000000001';
export const PROJECT_ID = '20418bbb-0000-4000-8000-000000000002';
export const WA_ID = '30552ccc-0000-4000-8000-000000000003';

export const FENWICK: FirmRef = { id: 'f0f0f0f0-0000-4000-8000-0000000000f1', name: 'Fenwick & Calder LLP' };
export const OKAFOR: FirmRef = { id: '0a0a0a0a-0000-4000-8000-0000000000a2', name: 'Okafor Lane LLP' };

export const MATTER: ChainRecord = { table: 'sprk_matter', id: MATTER_ID, name: '2026-LIT-4471', distance: 0 };
export const PROJECT: ChainRecord = { table: 'sprk_project', id: PROJECT_ID, name: 'PRJ-20418', distance: 0 };
export const WA: ChainRecord = { table: 'sprk_workassignment', id: WA_ID, name: 'WA-30552', distance: 0 };
const asParent = (r: ChainRecord): ChainRecord => ({ ...r, distance: 1 });

const ver = (id: string, number: number, from: string, to: string | null = null, status = 'Active'): VersionView => ({ id, number, status, from, to });
export const V = {
  ocg1: ver('ocg-v1', 1, '2025-01-01', '2026-01-01', 'Superseded'),
  ocg2: ver('ocg-v2', 2, '2026-01-01'),
  rate1: ver('rate-v1', 1, '2025-01-01', '2026-01-01', 'Superseded'),
  rate2: ver('rate-v2', 2, '2026-01-01'),
  travel1: ver('travel-v1', 1, '2026-01-01'),
  instr1: ver('instr-v1', 1, '2026-03-01'),
  fee1: ver('fee-v1', 1, '2026-01-01', '2026-07-01'),
};

const stamp = { byId: 'u-1', byName: 'Dana Whitfield', on: '2026-09-02T10:00:00Z' };

export function override(o: Partial<OverrideView> & Pick<OverrideView, 'rowId' | 'mode' | 'chainRecord'>): OverrideView {
  return {
    decisive: true,
    distance: o.chainRecord.distance,
    reason: null,
    pinnedVersion: null,
    newerVersion: null,
    stamp,
    ...o,
  };
}

const src = (kind: string, winning: boolean, extra: Partial<SourceView> = {}): SourceView => ({
  kind,
  winning,
  distance: 0,
  via: null,
  mode: null,
  rowId: null,
  ...extra,
});

export function procedure(p: Partial<ProcedureItem> & Pick<ProcedureItem, 'id' | 'name'>): ProcedureItem {
  return {
    area: 'Billing',
    type: 'Guideline',
    appliesTo: 'AllRecords',
    firm: null,
    status: 'InForce',
    level: 'AllRecords',
    sources: [src('AllRecords', true)],
    versionInForce: V.ocg2,
    pinned: false,
    newerVersion: null,
    overrides: [],
    ...p,
  };
}

export function rule(r: Partial<RuleItem> & Pick<RuleItem, 'policyId' | 'code'>): RuleItem {
  return {
    policyVersionId: `${r.policyId}-v`,
    name: null,
    ruleType: 'Time-entry check',
    parameterSummary: null,
    versionNumber: 1,
    inForceFrom: '2026-01-01',
    inForce: true,
    recordRule: false,
    ruleSetId: 'rs-1',
    procedure: { id: 'p-ocg', name: 'Acme Outside Counsel Guidelines', version: V.ocg2, pinned: false },
    level: 'AllRecords',
    distance: 0,
    firmScope: null,
    marks: [],
    ...r,
  };
}

export const mark = (kind: string, extra: Partial<RuleMark> = {}): RuleMark => ({ kind, ...extra });

// ── 084e: the precedence marks, as RecordProcedureReadService sends them (ontology-r1 wording) ──────────────

export const OUTRANKED_MARK: RuleMark = mark('Outranked', {
  text: 'Outranked by FIRM-RATE',
  note: 'does not run while FIRM-RATE is in force; it can run if FIRM-RATE is refused when an invoice is checked; a line can differ (dates, fallback)',
  by: [
    { policyId: 'pol-firm-rate', policyVersionId: 'pv-firm-rate', code: 'FIRM-RATE', name: 'Approved rate', versionNumber: 1, source: 'Procedure', procedureId: 'p-firm', level: 'Firm', distance: 0 },
  ],
});

export const CAP_OUTRANKED_MARK: RuleMark = mark('CapOutranked', {
  text: 'Cap outranked by FIRM-EXP and LEGACY-EXP',
  note: 'its code check always runs; its cap yields to those of FIRM-EXP and LEGACY-EXP and can apply if all of them are refused when an invoice is checked; a line can differ (dates, fallback)',
  by: [
    { policyId: 'pol-firm-exp', policyVersionId: 'pv-firm-exp', code: 'FIRM-EXP', name: 'Expenses', versionNumber: 1, source: 'Procedure', procedureId: 'p-firm', level: 'Firm', distance: 0 },
    { policyId: 'pol-legacy-exp', policyVersionId: 'pv-legacy-exp', code: 'LEGACY-EXP', name: 'Expenses', versionNumber: 2, source: 'Legacy', procedureId: null, level: 'Firm', distance: 0 },
  ],
});

// ── Matter 2026-LIT-4471 ─────────────────────────────────────────────────────

const pinRow = override({ rowId: 'row-pin', mode: 'Pinned', chainRecord: MATTER, pinnedVersion: V.rate1, newerVersion: V.rate2, reason: 'Grandfathered 2025 rates' });
const excludeRow = override({ rowId: 'row-excl', mode: 'Excluded', chainRecord: MATTER, reason: 'Not travelling on this matter' });

export const MATTER_GAP: GapView = {
  procedureId: 'p-fee',
  procedureName: 'Northwind fee schedule',
  kind: 'AfterLastVersion',
  workDate: TODAY,
  text: `No version of this Procedure is in force on ${TODAY}: its last version has ended.`,
  overrides: [],
};

export const matterProcedures: RecordProceduresView = {
  record: MATTER,
  date: TODAY,
  firm: FENWICK,
  firmSource: 'Record',
  procedures: [
    procedure({ id: 'p-ocg', name: 'Acme Outside Counsel Guidelines' }),
    procedure({
      id: 'p-rate',
      name: 'Fenwick rate card',
      type: 'RateCard',
      appliesTo: 'Firm',
      firm: FENWICK,
      level: 'ManualOwn',
      sources: [src('ManualOwn', true, { mode: 'Pinned', rowId: 'row-pin' }), src('Firm', false)],
      versionInForce: V.rate1,
      pinned: true,
      newerVersion: V.rate2,
      overrides: [pinRow],
    }),
    procedure({
      id: 'p-travel',
      name: 'Fenwick travel policy',
      type: 'InternalStandard',
      appliesTo: 'Firm',
      firm: FENWICK,
      status: 'Excluded',
      level: 'Firm',
      sources: [src('Firm', true)],
      versionInForce: null,
      overrides: [excludeRow],
    }),
    procedure({
      id: 'p-instr',
      name: 'Acme v. Northwind billing instructions',
      type: 'BillingInstructions',
      appliesTo: 'AssignedRecordsOnly',
      level: 'ManualOwn',
      sources: [src('ManualOwn', true, { mode: 'FollowLatest', rowId: 'row-instr' })],
      versionInForce: V.instr1,
      overrides: [override({ rowId: 'row-instr', mode: 'FollowLatest', chainRecord: MATTER })],
    }),
    procedure({
      id: 'p-fee',
      name: 'Northwind fee schedule',
      type: 'Other',
      appliesTo: 'AssignedRecordsOnly',
      status: 'Gap',
      level: 'ManualOwn',
      sources: [src('ManualOwn', true, { mode: 'FollowLatest', rowId: 'row-fee' })],
      versionInForce: null,
      overrides: [override({ rowId: 'row-fee', mode: 'FollowLatest', chainRecord: MATTER })],
    }),
    procedure({
      id: 'p-next',
      name: 'Acme 2027 guidelines',
      status: 'NotYetInForce',
      versionInForce: null,
    }),
  ],
  gaps: [MATTER_GAP],
};

export const matterRules: RecordRulesInForceView = {
  record: MATTER,
  date: TODAY,
  firm: FENWICK,
  firmSource: 'Record',
  gaps: [MATTER_GAP],
  rules: [
    rule({ policyId: 'r-block', code: 'BLOCK', name: 'Block billing', parameterSummary: 'Reduce by 20%' }),
    rule({
      policyId: 'r-hours-rec',
      code: 'HOURS',
      name: 'Excessive daily hours',
      ruleType: 'Daily hours limit',
      parameterSummary: '12 hours per timekeeper per day',
      recordRule: true,
      marks: [
        mark('Changed', { recordRuleId: 'r-hours-rec', basedOn: { id: 'h1', number: 1 }, currentBase: { id: 'h2', number: 2 }, baseCode: 'HOURS', baseParameterSummary: '11 hours per timekeeper per day', reason: 'Matter staffing' }),
        mark('Stale', { recordRuleId: 'r-hours-rec', basedOn: { id: 'h1', number: 1 }, currentBase: { id: 'h2', number: 2 } }),
      ],
    }),
    rule({ policyId: 'r-meals', code: 'MEALS', name: 'Meal over per-person cap', ruleType: 'Expense cap (meals)', parameterSummary: 'Up to $50 per person' }),
    rule({
      policyId: 'r-vague',
      code: 'VAGUE',
      name: 'Vague description',
      parameterSummary: 'Flag for review',
      inForce: false,
      marks: [mark('Excluded', { reason: 'Fenwick bills in a fixed format', firm: FENWICK, distance: 0, chainRecord: MATTER, stamp, overrideRowId: 'row-vague' })],
    }),
  ],
};

// ── Project PRJ-20418 ────────────────────────────────────────────────────────

const projectPin = override({ rowId: 'row-ppin', mode: 'Pinned', chainRecord: PROJECT, pinnedVersion: V.rate2, newerVersion: null });

export const projectProcedures: RecordProceduresView = {
  record: PROJECT,
  date: TODAY,
  firm: FENWICK,
  firmSource: 'Record',
  gaps: [],
  procedures: [
    procedure({ id: 'p-ocg', name: 'Acme Outside Counsel Guidelines' }),
    procedure({
      id: 'p-rate',
      name: 'Fenwick rate card',
      type: 'RateCard',
      appliesTo: 'Firm',
      firm: FENWICK,
      level: 'ManualOwn',
      sources: [src('ManualOwn', true, { mode: 'Pinned', rowId: 'row-ppin' })],
      versionInForce: V.rate2,
      pinned: true,
      newerVersion: null,
      overrides: [projectPin],
    }),
  ],
};

export const projectRules: RecordRulesInForceView = {
  record: PROJECT,
  date: TODAY,
  firm: FENWICK,
  firmSource: 'Record',
  gaps: [],
  rules: [
    rule({ policyId: 'r-block', code: 'BLOCK', name: 'Block billing', parameterSummary: 'Reduce by 20%' }),
    rule({
      policyId: 'r-rush-rec',
      code: 'RUSH',
      name: 'Rush premium',
      parameterSummary: 'Up to 10%',
      inForce: false,
      recordRule: true,
      marks: [mark('BaseRetired', { recordRuleId: 'r-rush-rec', basedOn: { id: 'ru1', number: 1 }, baseCode: 'RUSH' })],
    }),
  ],
};

// ── Work assignment WA-30552 (filed under the matter AND the project) ────────

const waPinA = override({ rowId: 'row-wa-a', mode: 'Pinned', chainRecord: asParent(MATTER), pinnedVersion: V.rate1 });
const waPinB = override({ rowId: 'row-wa-b', mode: 'Pinned', chainRecord: asParent(PROJECT), pinnedVersion: V.rate2 });

export const WA_GAP: GapView = {
  procedureId: 'p-rate',
  procedureName: 'Fenwick rate card',
  kind: 'ConflictingParents',
  workDate: TODAY,
  text: 'The records this one is filed under choose different versions of this Procedure.',
  overrides: [waPinA, waPinB],
};

export const waProcedures: RecordProceduresView = {
  record: WA,
  date: TODAY,
  firm: FENWICK,
  firmSource: 'Record',
  gaps: [WA_GAP],
  procedures: [
    procedure({ id: 'p-ocg', name: 'Acme Outside Counsel Guidelines' }),
    procedure({
      id: 'p-rate',
      name: 'Fenwick rate card',
      type: 'RateCard',
      appliesTo: 'Firm',
      firm: FENWICK,
      status: 'Gap',
      level: 'ManualParent',
      sources: [
        src('ManualParent', true, { distance: 1, via: asParent(MATTER), mode: 'Pinned', rowId: 'row-wa-a' }),
        src('ManualParent', true, { distance: 1, via: asParent(PROJECT), mode: 'Pinned', rowId: 'row-wa-b' }),
        src('Firm', false),
      ],
      versionInForce: null,
      overrides: [waPinA, waPinB],
    }),
    procedure({
      id: 'p-travel',
      name: 'Fenwick travel policy',
      type: 'InternalStandard',
      appliesTo: 'Firm',
      firm: FENWICK,
      status: 'Excluded',
      level: 'Firm',
      sources: [src('Firm', true)],
      versionInForce: null,
      overrides: [override({ rowId: 'row-wa-x', mode: 'Excluded', chainRecord: asParent(MATTER), reason: 'Not travelling' })],
    }),
  ],
};

export const waRules: RecordRulesInForceView = {
  record: WA,
  date: TODAY,
  firm: FENWICK,
  firmSource: 'Record',
  gaps: [WA_GAP],
  rules: [
    rule({ policyId: 'r-block', code: 'BLOCK', name: 'Block billing', parameterSummary: 'Reduce by 20%' }),
    rule({
      policyId: 'r-rush-rec',
      code: 'RUSH',
      name: 'Rush premium',
      parameterSummary: 'Up to 10%',
      inForce: false,
      recordRule: true,
      level: 'ManualParent',
      distance: 1,
      marks: [mark('BaseRetired', { recordRuleId: 'r-rush-rec', basedOn: { id: 'ru1', number: 1 }, baseCode: 'RUSH' })],
    }),
  ],
};
