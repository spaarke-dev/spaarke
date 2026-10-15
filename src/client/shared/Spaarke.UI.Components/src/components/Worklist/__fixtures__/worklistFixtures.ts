/**
 * Fixtures shaped like the BUILT schema (task 007): a `WorklistItem` is a `sprk_signal` row's display columns and a
 * `WorklistCore` is the catalog-driven core record (`sprk_corerecordtype` / `sprk_corerecordid`). Not v4's mock types.
 * Test/harness use only.
 */

import type { WorklistCore, WorklistItem } from '../types';

/** The fixed "today" every test renders against: 9 Oct 2026, the viewer's local calendar. */
export const TODAY = new Date(2026, 9, 9, 10, 30);

/** An ISO instant at local noon of the given calendar day, so the local date is that day in every time zone. */
export function localNoonIso(year: number, monthIndex: number, day: number): string {
  return new Date(year, monthIndex, day, 12, 0, 0).toISOString();
}

// Decide lane ---------------------------------------------------------------------------------------------------------

/** Cross-source (Path B): a communication classified fee or scope change, no budget revision since. */
export const pathBItem: WorklistItem = {
  signalId: '11111111-1111-1111-1111-111111111111',
  lane: 'Decide',
  subjectName: 'Invoice 4411 from Acme LLP',
  title: 'Fee or scope change with no budget revision',
  ruleShortName: 'Fee or scope change',
  raisedOn: localNoonIso(2026, 9, 6),
};

/** Threshold: spend over budget. */
export const thresholdItem: WorklistItem = {
  signalId: '22222222-2222-2222-2222-222222222222',
  lane: 'Decide',
  subjectName: 'Spend to date against budget',
  ruleShortName: 'Spend over budget',
  raisedOn: localNoonIso(2026, 9, 9),
};

// Do lane -------------------------------------------------------------------------------------------------------------

/** Overdue task: 5 days past due on 9 Oct 2026. */
export const overdueTaskItem: WorklistItem = {
  signalId: '33333333-3333-3333-3333-333333333333',
  lane: 'Do',
  subjectName: 'Send engagement letter',
  ruleShortName: 'Overdue task',
  dueDate: '2026-10-04',
};

/** Work assignment past its response date: 3 days late. */
export const lateAssignmentItem: WorklistItem = {
  signalId: '44444444-4444-4444-4444-444444444444',
  lane: 'Do',
  subjectName: 'Outside counsel response',
  ruleShortName: 'Response overdue',
  dueDate: '2026-10-06',
  pastDueWording: 'late',
};

/** Coming due, not yet past. */
export const dueSoonItem: WorklistItem = {
  signalId: '55555555-5555-5555-5555-555555555555',
  lane: 'Do',
  subjectName: 'File the status report',
  ruleShortName: 'Coming due',
  dueDate: '2026-10-12',
};

export const dueTodayItem: WorklistItem = {
  signalId: '66666666-6666-6666-6666-666666666666',
  lane: 'Do',
  subjectName: 'Confirm hearing date',
  ruleShortName: 'Coming due',
  dueDate: '2026-10-09',
};

// Core records (D-34 / D-36): label, name and number come from the catalog, never from a per-type branch --------------

export const matterCore: WorklistCore = {
  recordType: 'sprk_matter',
  recordId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  typeLabel: 'Matter',
  name: 'Acme v. Beta',
  number: 'MTR-00042',
};

export const projectCore: WorklistCore = {
  recordType: 'sprk_project',
  recordId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  typeLabel: 'Project',
  name: 'Contract refresh 2026',
  number: 'PRJT-00007',
};

export const workAssignmentCore: WorklistCore = {
  recordType: 'sprk_workassignment',
  recordId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  typeLabel: 'Work assignment',
  name: 'Review vendor agreement',
  number: 'WRK-00019',
};

export const serviceRequestCore: WorklistCore = {
  recordType: 'sprk_servicerequest',
  recordId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
  typeLabel: 'Service request',
  name: 'Onboarding request from Northwind',
  number: 'SVCR-00003',
};
