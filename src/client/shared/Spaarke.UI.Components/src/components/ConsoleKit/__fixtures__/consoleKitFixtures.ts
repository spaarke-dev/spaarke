/**
 * Fixtures shaped like the BUILT schema (task 007): `sprk_signal` / `sprk_decisionrecord` column names and
 * the `sprk_evidencerefs` JSON entries. Not v4's mock types. Test/harness use only.
 */

import type { RecordClass, ResolutionType } from '../consoleStatus';

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

export interface DecisionRecordFixture {
  sprk_name: string;
  recordId: string;
  resolution: ResolutionType;
  sprk_recordclass: RecordClass;
  decidedBy: string;
  sprk_decidedon: string;
}

export const decisionRecords: DecisionRecordFixture[] = [
  {
    sprk_name: 'Invoice 4411 over budget: approved',
    recordId: 'DR-0042',
    resolution: 'Acted',
    sprk_recordclass: 'Judgement',
    decidedBy: 'A. Reviewer',
    sprk_decidedon: '2026-10-01T14:30:00Z',
  },
  {
    sprk_name: 'Reschedule own task',
    recordId: 'DR-0043',
    resolution: 'Acted',
    sprk_recordclass: 'Routine',
    decidedBy: 'A. Reviewer',
    sprk_decidedon: '2026-10-02T08:00:00Z',
  },
  {
    sprk_name: 'Late filing: dismissed',
    recordId: 'DR-0044',
    resolution: 'Dismissed',
    sprk_recordclass: 'Dismissal',
    decidedBy: 'A. Reviewer',
    sprk_decidedon: '2026-10-03T10:00:00Z',
  },
];
