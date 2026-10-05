/**
 * Runtime-only mock factory body for `@spaarke/ui-components`, used via
 * `jest.doMock('@spaarke/ui-components', () => require(...))` from
 * `register-workspace-widgets.test.ts`'s `loadRegistrations()` (task 092,
 * 2026-10-04).
 *
 * Why this exists: `register-workspace-widgets.ts` itself needs exactly one
 * export (`safeRegister`) from `@spaarke/ui-components`, but this package's
 * jest.config.ts maps that specifier to SOURCE (not `dist`), so without a
 * mock, every `jest.resetModules()` + re-`require()` cycle in
 * `loadRegistrations()` (called in `beforeEach` across several `describe`
 * blocks, so dozens of times in this one file) re-evaluates the entire real
 * barrel — OOMing a default-size heap. See the long comment above
 * `loadRegistrations()` in the test file for the full before/after trace.
 *
 * `resolveWorkspaceWidget('communications-list')` (a handful of tests in
 * this same file) dynamically imports the REAL `CommunicationsWorkspaceWidget`
 * from `@spaarke/communication-components` (not mocked — the test's whole
 * point is proving that resolution reaches the real, upgraded component), so
 * this factory also needs to cover whatever THAT component's own import
 * graph needs from `@spaarke/ui-components`. That list was independently
 * discovered and verified (iteratively, file-by-file, down to zero missing
 * exports) while fixing the near-identical `legalWorkspaceSectionRegistry
 * .test.ts` cross-package-mock problem in
 * `Spaarke.DailyBriefing.Components/test/__mocks__/spaarke-ui-components.tsx`
 * — this file is that same list, trimmed to runtime values only (no `export
 * interface`/`export type` — a `jest.doMock` factory is a plain runtime
 * object; TS types are erased and irrelevant to it).
 */
import * as React from 'react';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const safeRegister = <T>(_registryName: string, _label: string, action: () => T): T | undefined => {
  try {
    return action();
  } catch {
    return undefined;
  }
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const ConversationWorkspace: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const ConversationView: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const getCurrentUserId: (...args: any[]) => string = () => '';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createXrmNavigationService: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const openEmailRecord: (...args: any[]) => Promise<any> = async () => undefined;
export const TODO_REGARDING_CATALOG: ReadonlyArray<{ entityType: string }> = [];
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const SprkModal: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const PreviewModal: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const PanelSplitter: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const useTwoPanelLayout: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const RichFilePreviewDialog: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const sanitizeEmailHtml: (...args: any[]) => string = () => '';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const DataGridViewSelector: React.FC<any> = () => null;
export class EntityCreationService {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  static async resolveUserBuDefaults(..._args: any[]): Promise<any> {
    return {};
  }
}
export const cleanGuid: (id: string) => string = id => id;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const XrmDataverseClient: any = class {};
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createXrmDataService: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createXrmEmailComposeHandlers: (...args: any[]) => any = () => ({});
export const resolveCurrentUserEmail: (...args: unknown[]) => Promise<string> = async () => '';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const searchUsersAndContacts: (...args: any[]) => Promise<any[]> = async () => [];
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const getXrm: (...args: any[]) => any = () => undefined;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const getXrmForPicker: (...args: any[]) => any = () => undefined;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const TrackingFieldTrio: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const DataGrid: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const FormModal: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const applyResolverFields: (...args: any[]) => any = () => ({});
export const OOB_MODAL_SIZES = {
  record: { width: { value: 85, unit: '%' as const }, height: { value: 85, unit: '%' as const } },
  createForm: { width: { value: 70, unit: '%' as const }, height: { value: 80, unit: '%' as const } },
  wizard: { width: { value: 60, unit: '%' as const }, height: { value: 70, unit: '%' as const } },
};

// Drift guard (task 092, 2026-10-04, added per reviewer request after
// DailyBriefing.Components' hand-mock drifted silently for months): every
// export above, checked against the REAL `@spaarke/ui-components` module's
// current type via `satisfies`. A rename/removal of any of these, or an
// incompatible signature change, now fails ts-jest's diagnostics at compile
// time instead of surfacing as a confusing runtime failure later. This
// import is `import type` — erased at runtime, so it does NOT reintroduce
// the barrel-re-evaluation cost this mock exists to avoid.
const _driftGuard = {
  safeRegister,
  ConversationWorkspace,
  ConversationView,
  getCurrentUserId,
  createXrmNavigationService,
  openEmailRecord,
  TODO_REGARDING_CATALOG,
  SprkModal,
  PreviewModal,
  PanelSplitter,
  useTwoPanelLayout,
  RichFilePreviewDialog,
  sanitizeEmailHtml,
  DataGridViewSelector,
  EntityCreationService,
  cleanGuid,
  XrmDataverseClient,
  createXrmDataService,
  createXrmEmailComposeHandlers,
  resolveCurrentUserEmail,
  searchUsersAndContacts,
  getXrm,
  getXrmForPicker,
  TrackingFieldTrio,
  DataGrid,
  FormModal,
  applyResolverFields,
  OOB_MODAL_SIZES,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
} satisfies Partial<typeof import('@spaarke/ui-components')>;
void _driftGuard;
