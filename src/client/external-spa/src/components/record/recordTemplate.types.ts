/**
 * Record template contract (spaarke-SPA-external-access-platform-r4 task 003, FR-01 / FR-02).
 *
 * One template renders every external record type (Project, Matter, Work assignment, Invoice, Service request,
 * Document). A type supplies a `RecordTemplateConfig`; `RecordTemplateModal` renders ONLY from it (no branch on the
 * entity name): the title, up to three header facts, optional tabs, the sections in the left (66%) and right (34%)
 * columns, and the toolbar actions. Adding a type changes only the config map (`recordTemplates.ts`) and, from task 005,
 * its section renderers.
 *
 * What a config does NOT carry: which columns are read. The record is read through the detail description of the same
 * entity (`RECORD_DETAIL_ENTITIES`, `detail/RecordDetailFields.tsx`), whose `fields` are the module allow-list. Every
 * attribute a config names (title number, name, header facts) is one of those fields, so the template adds no column
 * and no read (asserted in `tests/RecordTemplateModal.test.tsx`).
 */
import type { RecordActionIconId } from '@spaarke/ui-components/components/RecordCommandBar';
import type { CallerIdentity } from '../../api/delete-own';
import type { ExternalUserContextResponse } from '../../auth/bff-client';
import type { DetailFieldDef, DetailRecord, RecordDetailEntity } from '../detail/RecordDetailFields';

/** The body sections a record can show. Each renders through the modal's `renderSection` (task 005 supplies them). */
export type RecordSectionId =
  | 'information'
  | 'documents'
  | 'invoices'
  | 'files'
  | 'preview'
  | 'messages'
  | 'tasksEvents';

/** Heading of each section. */
export const RECORD_SECTION_LABELS: Readonly<Record<RecordSectionId, string>> = {
  information: 'Information',
  documents: 'Documents',
  invoices: 'Invoices',
  files: 'Files',
  preview: 'Preview',
  messages: 'Messages',
  tasksEvents: 'Tasks & events',
};

/**
 * A toolbar action. Ids are keys of the shared icon map (`RECORD_ACTION_ICONS`, FR-05), so an action can only use an
 * icon from the map. Order in a config is the order on screen.
 */
export type RecordToolbarActionId = RecordActionIconId;

/**
 * A tab. `overview` shows the two-column layout (`left` / `right`); a section id shows that one section full width.
 * Tabs are off by default (owner 2026-11-08): a config with no `tabs` renders the overview as the whole body.
 */
export interface RecordTemplateTab {
  id: 'overview' | RecordSectionId;
  label: string;
}

/** One header fact: a read field shown value-over-label beside the record name. */
export type RecordHeaderFact = Pick<DetailFieldDef, 'attribute' | 'label' | 'kind'>;

export interface RecordTemplateConfig {
  /** Dataverse logical name; the key of the entity's `RECORD_DETAIL_ENTITIES` entry (its read). */
  entity: string;
  /** Singular display label ("Matter"); the first word of the modal title. */
  label: string;
  /**
   * The record-number column: the title is "{label} {number}" (owner 2026-11-08: title = type + number). `null` for a
   * type that has no number column (Document); the title then falls back to the name, then to the label alone.
   */
  titleNumberAttribute: string | null;
  /** The record-name column: the header's heading and the title's first fallback. */
  nameAttribute: string;
  /** Up to three facts shown in the header (the "Your access" line is separate; task 006 fills it). */
  headerFacts: readonly RecordHeaderFact[];
  /** Optional tabs; absent = no TabList (the default for every type today). */
  tabs?: readonly RecordTemplateTab[];
  /** Sections in the left (66%) column, top to bottom. */
  left: readonly RecordSectionId[];
  /** Sections in the right (34%) column, top to bottom. Empty = one full-width column. */
  right: readonly RecordSectionId[];
  /** Toolbar actions this type can offer, in order. Each is still shown only when the caller may use it. */
  toolbar: readonly RecordToolbarActionId[];
}

// ---------------------------------------------------------------------------
// What the modal hands a section and an action host (task 005)
// ---------------------------------------------------------------------------

/** What the modal knows about the shown record and the caller; shared by sections and action hosts. */
export interface RecordViewContext {
  config: RecordTemplateConfig;
  /** The entity's read description (fields, documents root, invoices lookup...). */
  detail: RecordDetailEntity;
  recordId: string;
  record: DetailRecord;
  /** The modal title ("Matter MAT-001"). */
  title: string;
  /** The caller's `/me` context, read once per opening; null while unknown or when the read failed (fail closed). */
  callerContext: ExternalUserContextResponse | null;
  /** Who the caller is (for the delete-own and requester checks); null while unknown. */
  caller: CallerIdentity | null;
  /**
   * Report an operation in flight (a message send, a recall post). While anything is busy, browsing, Close and Refresh
   * are held so the operation's result is not lost. Clear it when the operation settles and on unmount.
   */
  onBusyChange: (busy: boolean) => void;
  /** The record was deleted by an action in this view: the modal shows the deleted state and tells its opener. */
  onRecordDeleted: () => void;
  /** Ask one section of this record to re-read (the service request recall re-reads the Messages thread). */
  refreshSection: (section: RecordSectionId) => void;
}

/** What a section renderer gets. */
export interface RecordSectionContext extends RecordViewContext {
  /** True when the section is shown alone on its own tab, full width. */
  full: boolean;
  /**
   * Changes when the user presses Refresh (or Try again), or when an action asks this section to re-read
   * (`refreshSection`). A section with its own reads re-reads then.
   */
  refreshSignal: number;
  /**
   * Task 004 (FR-03). For a section that owns a create dialog (Documents: `upload`; Messages: `newMessage`; Tasks & events:
   * `addTodo` / `addEvent`): the reporter through which it offers that toolbar action, or `undefined` when this record's
   * toolbar does not list the action. A section that receives a reporter shows no create button of its own and renders its
   * dialog itself (so the dialog is a React descendant of the record modal); one that receives `undefined` keeps its own
   * button. The stable reporter per action id is the action hosts' one (`onActionChange`).
   */
  offerAction: (actionId: RecordToolbarActionId) => ((action: RecordHostAction | null) => void) | undefined;
  /**
   * Task 006 (FR-07). The one short line saying why this section is read-only for the caller (`readOnlyNoteOf` in
   * `recordAccess.ts`), or null/absent when it is not read-only or that is not known yet.
   */
  readOnlyNote?: string | null;
  /**
   * Task 006 fix round 1 (FR-07). For a section whose list has a create right WITHOUT a toolbar action (the invoice Files
   * list's own Upload, `'filesUpload'`): the stable reporter through which it tells the template that right (`true` /
   * `false`, or `null` while unknown and on unmount), so "Your access" and its read-only line follow it. It adds nothing
   * to the toolbar.
   */
  reportRight?: (id: 'filesUpload') => (right: boolean | null) => void;
}

/** A toolbar action an action host offers: hidden unless `visible` (hide, don't disable). */
export interface RecordHostAction {
  visible: boolean;
  onClick: () => void;
  /** Overrides the action's default tooltip (= accessible name); must contain the action's label (WCAG 2.5.3). */
  tooltip?: string;
  /**
   * Task 006 (FR-06). A create action (Upload, New message, Add to-do, Add event) reports the right behind it from the
   * BFF's answers: `true` / `false`, or `null` while unknown. The toolbar shows a create action only when it is `true`.
   */
  right?: boolean | null;
}

/**
 * What an action host gets. An action host owns one toolbar action that needs more than a click handler (its dialogs,
 * its notices): it renders those in the record's content and reports the action through `onActionChange`.
 */
export interface RecordActionHostContext extends RecordViewContext {
  /** Offer (or withdraw, with `null`) this host's toolbar action. Stable for the host's lifetime. */
  onActionChange: (action: RecordHostAction | null) => void;
  /**
   * Task 007 (FR-10). The host's in-app navigation, handed down by whoever opened the record (`RecordOpenerProvider`, a grid
   * widget); the template imports no router. Absent (a test, a surface with no router): an action that navigates is hidden.
   */
  onNavigate?: (path: string) => void;
  /**
   * Close the record view before leaving it. Returns false, and closes nothing, while an operation holds the view
   * (`onBusyChange`): an action must not navigate away from an operation in flight.
   */
  requestClose: () => boolean;
}
