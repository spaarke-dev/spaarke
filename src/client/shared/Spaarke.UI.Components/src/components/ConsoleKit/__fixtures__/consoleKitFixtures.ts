/**
 * Fixtures shaped like the BUILT schema (task 007): `sprk_signal` / `sprk_decisionrecord` column names and
 * the `sprk_evidencerefs` JSON entries. Not v4's mock types. Test/harness use only.
 */

import type { DecisionOutcome, RecordClass } from '../consoleStatus';

/** One `sprk_evidencerefs` entry (kind/ref/tier from SignalEvidenceRef; clause/asOf/text/source per FR-48/FR-50). */
export interface EvidenceRefFixture {
  kind: string;
  ref: string;
  tier: 'Fact' | 'Observation';
  text: string | null;
  source?: string;
  asOf?: string;
  clause?: string;
}

export const evidenceRefs: Record<string, EvidenceRefFixture> = {
  fact: {
    kind: 'communication',
    ref: 'sprk_communication:00000000-0000-0000-0000-000000000001',
    tier: 'Fact',
    text: 'Invoice 4411 received from Acme LLP',
    source: 'Email',
    asOf: '2026-09-30T09:15:00Z',
    clause: 'exists: invoice received',
  },
  interpretation: {
    kind: 'communication',
    ref: 'sprk_communication:00000000-0000-0000-0000-000000000002',
    tier: 'Observation',
    text: 'Email was classified as an invoice',
    source: 'Email classifier',
    asOf: '2026-09-30T09:16:00Z',
    clause: 'exists: invoice received',
  },
  context: {
    kind: 'snapshot',
    ref: 'sprk_spendsnapshot:00000000-0000-0000-0000-000000000003',
    tier: 'Fact',
    text: 'Spend to date 41,200',
    source: 'Spend snapshot',
    asOf: '2026-09-29T00:00:00Z',
  },
  nullFact: {
    kind: 'snapshot',
    ref: 'sprk_spendsnapshot:00000000-0000-0000-0000-000000000004',
    tier: 'Fact',
    text: null,
    clause: 'threshold: spend over budget',
  },
};

/** A `sprk_decisionrecord` row: it has `sprk_decisionoutcome` and `sprk_recordclass`, NOT `sprk_resolutiontype` (that is on `sprk_signal`). */
export interface DecisionRecordFixture {
  sprk_name: string;
  /** `sprk_decisionnumber` autonumber, DR-{SEQNUM:00000}. */
  sprk_decisionnumber: string;
  sprk_decisionoutcome: DecisionOutcome;
  sprk_recordclass: RecordClass;
  /** Display name of the `sprk_confirmedby` systemuser lookup. */
  confirmedByName: string;
  sprk_decidedon: string;
}

export const decisionRecords: DecisionRecordFixture[] = [
  {
    sprk_name: 'Invoice 4411 over budget: approved',
    sprk_decisionnumber: 'DR-00042',
    sprk_decisionoutcome: 'Authorized',
    sprk_recordclass: 'Judgement',
    confirmedByName: 'A. Reviewer',
    sprk_decidedon: '2026-10-01T14:30:00Z',
  },
  {
    sprk_name: 'Reschedule own task',
    sprk_decisionnumber: 'DR-00043',
    sprk_decisionoutcome: 'Authorized',
    sprk_recordclass: 'Routine',
    confirmedByName: 'A. Reviewer',
    sprk_decidedon: '2026-10-02T08:00:00Z',
  },
  {
    sprk_name: 'Late filing: dismissed',
    sprk_decisionnumber: 'DR-00044',
    sprk_decisionoutcome: 'Dismissed',
    sprk_recordclass: 'Dismissal',
    confirmedByName: 'A. Reviewer',
    sprk_decidedon: '2026-10-03T10:00:00Z',
  },
];
