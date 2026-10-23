/**
 * Test-local mock for `@spaarke/ai-widgets` (task 092, 2026-10-04).
 *
 * `legalWorkspaceSectionRegistry.test.ts` imports `sectionRegistry.ts`, which
 * imports `analysisHub.registration.ts` — another LegalWorkspace section the
 * registry factory aggregates — which imports `AnalysisHubWidget`. Same
 * minimal-stand-in pattern as the other `test/__mocks__/spaarke-*` files.
 */
import * as React from 'react';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const AnalysisHubWidget: React.FC<any> = () => null;

// `worklist.registration.ts` (ontology task 156; 158 adds the side-pane lookup and record modal) imports these. The real ones are exercised via the spies in
// `worklistSectionRegistration.test.tsx`; here they are inert.
export interface WorklistLookupDataSource {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createWorklistLookup: (source: WorklistLookupDataSource) => any = () => async () => [];
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createWorklistOpenLookup: (navigation: unknown) => any = () => async () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createWorklistOpenRecord: (navigation: unknown) => any = () => undefined;
export const useOptionalDispatchPaneEvent: () => (channel: string, event: unknown) => void = () => () => undefined;
