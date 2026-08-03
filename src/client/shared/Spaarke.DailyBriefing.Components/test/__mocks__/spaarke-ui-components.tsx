/**
 * Test-local mock for `@spaarke/ui-components`.
 *
 * R2 task 019 / NFR-05:
 *   `NarrativeBullet.tsx` imports `MicrosoftToDoIcon` from `@spaarke/ui-components`.
 *   The smoke test transitively mounts that component, so we stub the icon as a
 *   no-op SVG. This keeps the test independent of the @spaarke/ui-components
 *   peer dep (which isn't installed at the daily-briefing-components package level).
 *
 * R2 Option D (2026-06-18):
 *   `legalWorkspaceSectionRegistry.test.ts` imports
 *   `src/solutions/LegalWorkspace/src/sectionRegistry.ts` which references
 *   `SectionRegistration`, `SectionCategory`, `NarrateRequest`, and
 *   `SECTION_METADATA_CATALOG` from `@spaarke/ui-components`. We provide
 *   minimal stand-ins so ts-jest can type-check; runtime values are also
 *   provided so the dev-mode metadata-drift guard inside the registry
 *   factory has a non-empty catalog to compare against.
 */
import * as React from 'react';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const MicrosoftToDoIcon: React.FC<any> = props => (
  <svg role="img" aria-label="Microsoft To Do" className={props?.className} width={16} height={16} />
);

// ---------------------------------------------------------------------------
// r5 email-share (2026-07-09): DailyBriefingApp imports SendEmailDialog +
// ISendEmailPayload from `@spaarke/ui-components`. Stub the dialog as a no-op so
// importing DailyBriefingApp resolves in test context (the pure-helper test
// under test/ does not mount it).
// ---------------------------------------------------------------------------

export interface ISendEmailPayload {
  to: { id: string; name: string };
  subject: string;
  body: string;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const SendEmailDialog: React.FC<any> = () => null;

// ---------------------------------------------------------------------------
// spaarke-modal-system P7 task 090 (FR-11/FR-18): SubRowLink.tsx,
// NarrativeBullet.tsx, and DailyBriefingApp.tsx import OOB_MODAL_SIZES from
// `@spaarke/ui-components` (the single OOB size-constants module) so their
// navigateTo record-opens can't drift from the shared `record` (85%×85%)
// size. Mirror the real module's values exactly — see
// utils/adapters/oobModalSizes.ts.
// ---------------------------------------------------------------------------

export const OOB_MODAL_SIZES = {
  record: {
    width: { value: 85, unit: '%' as const },
    height: { value: 85, unit: '%' as const },
  },
  createForm: {
    width: { value: 70, unit: '%' as const },
    height: { value: 80, unit: '%' as const },
  },
  wizard: {
    width: { value: 60, unit: '%' as const },
    height: { value: 70, unit: '%' as const },
  },
};

// ---------------------------------------------------------------------------
// Types referenced by the LegalWorkspace registry factory under test
// (`createLegalWorkspaceSectionRegistry`, R2 Option D).
// ---------------------------------------------------------------------------

export type SectionCategory = string;

export interface SectionRegistration {
  id: string;
  category: SectionCategory;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  factory: (...args: any[]) => any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

export interface NarrateRequest {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

// Metadata catalog stand-in. Contains entries for every section the factory
// builds (matching the ids the per-section mocks emit in the test) so the
// dev-mode metadata-drift guard finds no drift in tests.
export const SECTION_METADATA_CATALOG: ReadonlyArray<{ id: string }> = [
  { id: 'get-started' },
  { id: 'quick-summary' },
  { id: 'latest-updates' },
  { id: 'todo' },
  { id: 'documents' },
  { id: 'matters' },
  { id: 'projects' },
  { id: 'invoices' },
  { id: 'work-assignments' },
  { id: 'daily-briefing' },
  { id: 'calendar' },
];
