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
import type { DetailFieldDef } from '../detail/RecordDetailFields';

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
