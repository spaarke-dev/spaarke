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
import { getXrm as realGetXrm } from '../../../Spaarke.UI.Components/src/utils/xrmContext';
// #1416: the REAL cleanGuid (ADR-044: strip braces, trim, lowercase) — dependency-free deep import,
// same pattern as realGetXrm. Never re-implement it here: an identity stub hides brace/case bugs.
import { cleanGuid as realCleanGuid } from '../../../Spaarke.UI.Components/src/utils/guid';

// C-9 (spaarke-ontology-platform-r1 task 052): NarrativeBullet and HighPrioritySection render their row menu through the
// shared RowActionMenu. Re-export the REAL component (dependency-free deep import, same pattern as getXrm / cleanGuid):
// a stub would hide the menu behaviour these suites assert.
export { RowActionMenu } from '../../../Spaarke.UI.Components/src/components/RowActionMenu/RowActionMenu';
export type {
  RowActionDescriptor,
  RowActionMenuProps,
} from '../../../Spaarke.UI.Components/src/components/RowActionMenu/RowActionMenu';

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

// `onError` typed explicitly (task 092, 2026-10-04) — DailyBriefingApp.tsx's
// inline `onError={err => ... err.detail ...}` needs a contextual type for
// `err`; a bare `React.FC<any>` doesn't propagate one to callback PARAMETERS
// the way it does to other props (TS doesn't contextually type an inline
// function assigned through an `any`-typed call site unless the callback
// prop itself has a real function signature).
export interface ISendEmailDialogMockProps {
  onError?: (err: { detail?: string }) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export const SendEmailDialog: React.FC<ISendEmailDialogMockProps> = () => null;

// ---------------------------------------------------------------------------
// messaging-communication-app (task 092, 2026-10-04): `sectionRegistry.ts`
// transitively imports `communications.registration.ts`, which imports
// `CommunicationsWorkspaceWidget` from `@spaarke/communication-components`,
// which in turn imports these from `@spaarke/ui-components`. Same
// permissive-stand-in pattern — `React.FC<any>` / loosely-typed functions so
// the type checker is satisfied (incl. the implicit-any TS7006 errors on
// `ConversationView`'s inline `onOpenRecord` / `onOpenEmail` callback props,
// which only existed because this component's props were unresolvable, not
// because of anything this test exercises).
// ---------------------------------------------------------------------------

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const ConversationWorkspace: React.FC<any> = () => null;

// `React.FC<any>` alone left `onOpenRecord`/`onOpenEmail`'s inline-arrow
// callers in `CommunicationsWorkspaceWidget.tsx` reporting TS7006 implicit-any
// on their parameters — an explicit (if loose) props interface resolves it.
export interface IConversationViewMockProps {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onOpenRecord?: (entityType: string, id: string) => any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onOpenEmail?: (msg: any) => any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export const ConversationView: React.FC<IConversationViewMockProps> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const getCurrentUserId: (...args: any[]) => string = () => '';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createXrmNavigationService: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const openEmailRecord: (...args: any[]) => Promise<any> = async () => undefined;

export interface IConversationRendererProps {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

// `ConnectionsWriteHandler.ts` (another file `CommunicationsWorkspaceWidget.tsx`
// transitively reaches, task 092, 2026-10-04) imports these 5.
export interface INavPropEntry {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface IPolymorphicWebApi {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface ITodoRegardingTargetCatalogEntry {
  entityType: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export const TODO_REGARDING_CATALOG: ReadonlyArray<ITodoRegardingTargetCatalogEntry> = [];
export interface IAttachmentItem {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const SprkModal: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const PreviewModal: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const PanelSplitter: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const useTwoPanelLayout: (...args: any[]) => any = () => ({});

// `RichFilePreviewDialog` — also the originally-reported missing export for
// DailyBriefingApp.smoke.test.tsx / CountReconciliation.smoke.test.tsx
// (DailyBriefingApp.tsx itself imports it directly).
export interface IRichFilePreviewDialogMockProps {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onOpenFile?: (mode: string) => any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export const RichFilePreviewDialog: React.FC<IRichFilePreviewDialogMockProps> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const sanitizeEmailHtml: (...args: any[]) => string = () => '';

export interface SavedView {
  id: string;
  name: string;
  isDefault?: boolean;
}
// Minimal method surface `useEmailViews.ts` actually calls — enough to kill
// the implicit-any cascade on its `.then()`/`.catch()` callback params
// without replicating the real (much larger) IDataverseClient interface.
export interface IDataverseClient {
  retrieveSavedQueriesForEntity: (entity: string) => Promise<SavedView[]>;
  retrieveSavedQuery: (id: string) => Promise<{ entityName?: string; fetchXml: string }>;
  retrieveRecord: (entity: string, id: string) => Promise<Record<string, unknown>>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  retrieveMultipleRecords: <T = Record<string, unknown>>(
    entity: string,
    fetchXml: string
  ) => Promise<{ entities: T[] }>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const DataGridViewSelector: React.FC<any> = () => null;
// `EntityCreationService` / `cleanGuid` (task 092, 2026-10-04) —
// `composeEditor.registration.ts` imports both. `EntityCreationService` is a
// class with static methods in the real module; a minimal class with the one
// static method this registration calls is enough to type-check.
export class EntityCreationService {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  static async resolveUserBuDefaults(..._args: any[]): Promise<any> {
    return {};
  }
}
export const cleanGuid = realCleanGuid;

// `email.registration.ts` (another LegalWorkspace section, task 092,
// 2026-10-04) imports these 6. `resolveCurrentUserEmail` typed to return
// `Promise<string>` specifically — the registration's own
// `.then((email) => ...)` needs that contextual type to avoid TS7006.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const XrmDataverseClient: any = class {};
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createXrmDataService: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const createXrmEmailComposeHandlers: (...args: any[]) => any = () => ({});
export const resolveCurrentUserEmail: (...args: unknown[]) => Promise<string> = async () => '';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const searchUsersAndContacts: (...args: any[]) => Promise<any[]> = async () => [];
// `getXrm` is the REAL shared walker (task 081 — see the note further down);
// bound here so the `_driftGuard` object below can reference it.
export const getXrm = realGetXrm;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const getXrmForPicker: (...args: any[]) => any = () => undefined;
export interface RecordTypeCatalogEntry {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const TrackingFieldTrio: React.FC<any> = () => null;
export interface IDataService {
  retrieveMultipleRecords: (
    entityName: string,
    options?: string
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ) => Promise<{ entities: Record<string, unknown>[] }>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface ICommunicationAssociation {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

// Spaarke DataGrid Framework surface — `ReconciliationGrid.tsx` (yet another
// Communication.Components file reached transitively via `sectionRegistry.ts`
// → ... → `CommunicationsWorkspaceWidget`, task 092, 2026-10-04) imports all
// of these. `DataGridOverrides`'s cell-renderer signature is typed (not bare
// `any`) specifically to stop the implicit-any cascade on inline renderer
// callbacks in that file — same rationale as `ConversationView` above.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const DataGrid: React.FC<any> = () => null;
export interface DataGridProps {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onRecordsLoaded?: (records: Record<string, unknown>[]) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onRowClick?: (recordId: string, record: Record<string, unknown>) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  onRecordOpen?: (recordId: string, record: Record<string, unknown>) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface DataGridOverrides {
  columnRenderers?: {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    [field: string]: (value: any, record: any) => React.ReactNode;
  };
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface HostFilterCondition {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface MembershipResolver {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
export interface DataGridParentContext {
  id: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const FormModal: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const applyResolverFields: (...args: any[]) => any = () => ({});
// unified-access-control-r2 task 147: the connections write handler (Spaarke.Communication.Components, which the
// legal-workspace section registry pulls in) re-files a communication through the BFF instead of Xrm.WebApi.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const updateChildRecordViaBff: (...args: any[]) => Promise<void> = async () => undefined;

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
// task 081 (C-8 / C-11 / C-13): DailyBriefing now imports `getXrm`,
// `formatRelativeTime`, `parseDueDate`, `daysBetweenLocalMidnight` and
// `EmptyState` from `@spaarke/ui-components`.
//
// `getXrm`, `formatRelativeTime` and the `dateLocal` helpers are the REAL implementations, re-exported
// from the UI.Components TypeScript source (both are dependency-free pure
// modules), so tests exercise the code under change and mock only at the
// boundary (the `window.Xrm` object / the clock). Only the presentational
// `EmptyState` stays a stub (the real one would pull a second Fluent/React
// copy from the sibling package's node_modules).
// ---------------------------------------------------------------------------

export { formatRelativeTime } from '../../../Spaarke.UI.Components/src/utils/relativeTime';
export { parseDueDate, daysBetweenLocalMidnight } from '../../../Spaarke.UI.Components/src/utils/dateLocal';

export interface EmptyStateProps {
  icon?: React.ReactElement;
  heading: string;
  description?: string;
  footer?: React.ReactNode;
  ariaLabel?: string;
  size?: 'compact' | 'default';
  className?: string;
  headingClassName?: string;
  descriptionClassName?: string;
}

export const EmptyState: React.FC<EmptyStateProps> = ({ heading, description, footer }) => (
  <div role="status" aria-live="polite">
    <span>{heading}</span>
    {description && <span>{description}</span>}
    {footer}
  </div>
);

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

// Structurally matches the REAL `NarrateRequest` (task 092, 2026-10-04):
// `src/widgets/dailyBriefing.registration.ts`'s OWN `NarrateRequest`
// (categories/priorityItems/totalNotificationCount/channels) must be
// assignable to THIS mock's type when `sectionRegistry.ts` composes both —
// a bare `{[k:string]:any}` index signature is NOT structurally compatible
// with a type that has specific REQUIRED fields (TS2345 at the composition
// call site), so this mirrors the field names/shapes (loosely typed) rather
// than just indexing.
export interface NarrateRequest {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  categories: any[];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  priorityItems: any[];
  totalNotificationCount: number;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  channels: any[];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

// `SectionFactoryContext` / `ContentSectionConfig` added (task 092,
// 2026-10-04): `communications.registration.ts` (one of the section
// registration files `sectionRegistry.ts` imports) type-imports both. Same
// permissive-stand-in pattern as `SectionRegistration` / `NarrateRequest`
// above — this mock only needs to satisfy the type checker, not replicate
// the real shape.
export interface SectionFactoryContext {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

export interface ContentSectionConfig {
  id: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

// Metadata catalog stand-in. Contains entries for every section the factory
// builds (matching the ids the per-section mocks emit in the test) so the
// dev-mode metadata-drift guard finds no drift in tests.
//
// `widthPreference` added (task 092, 2026-10-04): the real `SectionMetadata`
// interface gained this optional field in commit 8aad2037c1 (2026-07-02,
// "datagrid-framework: R2 complete"), which also added
// `warnOnWidthPreferenceViolations()` to `sectionRegistry.ts` — the file this
// mock exists to let ts-jest type-check. ts-jest resolves `@spaarke/ui-components`
// via this package's jest.config.js `moduleNameMapper` for BOTH runtime and
// type-checking, so `meta.widthPreference` failed to compile against this
// mock's un-widened `{ id: string }` shape ever since, independent of
// anything the test itself asserts.
export const SECTION_METADATA_CATALOG: ReadonlyArray<{ id: string; widthPreference?: 'full' | 'half' | 'any' }> = [
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
  // 4 sections added to LegalWorkspace's SECTION_REGISTRY after this mock's
  // creation (2026-06-18) without a corresponding entry here — task 092,
  // 2026-10-04. The test itself passed regardless (the registry's
  // metadata-drift guard only `console.error`s, never throws), but these
  // belong here per this file's own stated purpose ("non-empty catalog ...
  // so the dev-mode metadata-drift guard finds no drift in tests").
  { id: 'compose-editor' },
  { id: 'email' },
  { id: 'reconciliation' },
  { id: 'analysis' },
  { id: 'communications' },
];

// Drift guard (task 092, 2026-10-04, added per reviewer request after this
// exact file was found to have drifted silently since 2026-06-18). Every
// RUNTIME export above, checked against the REAL `@spaarke/ui-components`
// module's current type via `satisfies`. `import type` — erased at runtime.
const _driftGuard = {
  MicrosoftToDoIcon,
  SendEmailDialog,
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
  updateChildRecordViaBff,
  OOB_MODAL_SIZES,
  // SECTION_METADATA_CATALOG deliberately EXCLUDED — see note below.
} satisfies Partial<typeof import('@spaarke/ui-components')>;
void _driftGuard;

// `SECTION_METADATA_CATALOG` can't go through the same `satisfies` check:
// the real `SectionMetadata` interface has several more REQUIRED fields
// (displayName, category, factory, etc.) this mock's array entries
// deliberately omit — this mock only needs `id` (+ the `widthPreference`
// this file documents above) to satisfy the registry factory's dev-mode
// drift guard and `sectionRegistry.ts`'s width-preference check, not to
// BE a real catalog. A `satisfies` check against the full interface would
// force padding every entry with fields nothing here reads.
