/**
 * RECORD_TEMPLATES — the per-type record template configs (spaarke-SPA-external-access-platform-r4 task 003, FR-01).
 *
 * Left, right and toolbar are the spec FR-01 table, exactly:
 *
 * | Type             | Left (66%)                       | Right (34%)              | Toolbar                                                                  |
 * |------------------|----------------------------------|--------------------------|--------------------------------------------------------------------------|
 * | Matter, Project  | Information, Documents, Invoices | Messages, Tasks & events | Refresh, Upload, New message, Add to-do, Add event, Submit invoice, Copy link |
 * | Work assignment  | Information, Documents           | Messages, Tasks & events | same as Matter                                                           |
 * | Invoice          | Information, Files               | Messages                 | Refresh, New message, Copy link                                          |
 * | Service request  | Information, Documents           | Messages                 | Refresh, Upload, New message, Copy link, Delete request                  |
 * | Document         | Information, Preview             | —                        | Refresh, Download, Copy link                                             |
 *
 * HEADER FACTS come only from the entity's existing read fields (`RECORD_DETAIL_ENTITIES`), so they add no column and no
 * read. The prototype's other facts have no data source today and are left out (POML escalation 2, FR-18 / task 031 owns
 * new columns): Matter "Responsible attorney" and "Last modified" (not in the matter read), Project "Project lead".
 *
 * Tabs are off for every type (owner 2026-11-08).
 */
import { EMPTY_VALUE, formatDetailValue, type DetailRecord } from '../detail/RecordDetailFields';
import type { RecordTemplateConfig, RecordToolbarActionId } from './recordTemplate.types';

/** Matter, Project and Work assignment offer the same toolbar (FR-01). */
const ROOT_TOOLBAR: readonly RecordToolbarActionId[] = [
  'refresh',
  'upload',
  'newMessage',
  'addTodo',
  'addEvent',
  'submitInvoice',
  'copyLink',
];

export const RECORD_TEMPLATES: Readonly<Record<string, RecordTemplateConfig>> = {
  sprk_project: {
    entity: 'sprk_project',
    label: 'Project',
    titleNumberAttribute: 'sprk_projectnumber',
    nameAttribute: 'sprk_projectname',
    headerFacts: [
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'modifiedon', label: 'Last modified', kind: 'date' },
    ],
    left: ['information', 'documents', 'invoices'],
    right: ['messages', 'tasksEvents'],
    toolbar: ROOT_TOOLBAR,
  },
  sprk_matter: {
    entity: 'sprk_matter',
    label: 'Matter',
    titleNumberAttribute: 'sprk_matternumber',
    nameAttribute: 'sprk_mattername',
    headerFacts: [{ attribute: 'statuscode', label: 'Status' }],
    left: ['information', 'documents', 'invoices'],
    right: ['messages', 'tasksEvents'],
    toolbar: ROOT_TOOLBAR,
  },
  sprk_workassignment: {
    entity: 'sprk_workassignment',
    label: 'Work assignment',
    titleNumberAttribute: 'sprk_workassignmentnumber',
    nameAttribute: 'sprk_name',
    headerFacts: [
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'sprk_priority', label: 'Priority' },
      { attribute: 'sprk_responseduedate', label: 'Response due', kind: 'date' },
    ],
    left: ['information', 'documents'],
    right: ['messages', 'tasksEvents'],
    toolbar: ROOT_TOOLBAR,
  },
  sprk_invoice: {
    entity: 'sprk_invoice',
    label: 'Invoice',
    titleNumberAttribute: 'sprk_invoicenumber',
    nameAttribute: 'sprk_name',
    headerFacts: [
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'sprk_totalamount', label: 'Total amount', kind: 'money' },
      { attribute: 'sprk_invoicedate', label: 'Invoice date', kind: 'date' },
    ],
    left: ['information', 'files'],
    right: ['messages'],
    toolbar: ['refresh', 'newMessage', 'copyLink'],
  },
  sprk_servicerequest: {
    entity: 'sprk_servicerequest',
    label: 'Service request',
    titleNumberAttribute: 'sprk_servicerequestnumber',
    nameAttribute: 'sprk_name',
    headerFacts: [
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'sprk_requestedby', label: 'Requested by', kind: 'lookup' },
      { attribute: 'createdon', label: 'Created', kind: 'date' },
    ],
    left: ['information', 'documents'],
    right: ['messages'],
    // `delete` is "Delete request", shown to the requester only (task 005/006 wire it; R3 `ServiceRequestActions`).
    toolbar: ['refresh', 'upload', 'newMessage', 'copyLink', 'delete'],
  },
  sprk_document: {
    entity: 'sprk_document',
    label: 'Document',
    // A document has no number column: the title is "Document {name}".
    titleNumberAttribute: null,
    nameAttribute: 'sprk_documentname',
    headerFacts: [
      { attribute: 'sprk_documenttype', label: 'Type' },
      { attribute: 'createdon', label: 'Created', kind: 'date' },
    ],
    left: ['information', 'preview'],
    right: [],
    toolbar: ['refresh', 'download', 'copyLink'],
  },
};

/**
 * The visible label and the tooltip (= accessible name) of each toolbar action. The tooltip contains the label (WCAG 2.5.3
 * label in name). Keyed by action id, never by entity.
 */
export const RECORD_ACTION_TEXT: Readonly<Record<RecordToolbarActionId, { label: string; tooltip: string }>> = {
  refresh: { label: 'Refresh', tooltip: 'Refresh' },
  upload: { label: 'Upload', tooltip: 'Upload documents' },
  newMessage: { label: 'New message', tooltip: 'New message' },
  addTodo: { label: 'Add to-do', tooltip: 'Add to-do' },
  addEvent: { label: 'Add event', tooltip: 'Add event' },
  submitInvoice: { label: 'Submit invoice', tooltip: 'Submit invoice' },
  copyLink: { label: 'Copy link', tooltip: 'Copy link' },
  expandSection: { label: 'Expand', tooltip: 'Expand section' },
  download: { label: 'Download', tooltip: 'Download file' },
  delete: { label: 'Delete', tooltip: 'Delete request' },
  history: { label: 'History', tooltip: 'Version history' },
  send: { label: 'Send', tooltip: 'Send' },
};

/**
 * The modal title for a read row (owner 2026-11-08: title = type + number): "{label} {number}"; with no number,
 * "{label} {name}"; with neither (or no row yet), "{label}".
 */
export function recordTemplateTitle(config: RecordTemplateConfig, record: DetailRecord | null): string {
  if (record) {
    for (const attribute of [config.titleNumberAttribute, config.nameAttribute]) {
      if (!attribute) continue;
      const text = recordText(record, attribute);
      if (text) return `${config.label} ${text}`;
    }
  }
  return config.label;
}

/** A column's display text, trimmed; `null` when it is empty or only whitespace (so it never makes a blank title). */
export function recordText(record: DetailRecord, attribute: string): string | null {
  const text = formatDetailValue(record, { attribute }).trim();
  return text && text !== EMPTY_VALUE ? text : null;
}

/** The template for an entity, or `undefined` when the entity has none (no own-property lookups through the prototype). */
export function getRecordTemplate(entityName: string | undefined): RecordTemplateConfig | undefined {
  if (!entityName) return undefined;
  return Object.prototype.hasOwnProperty.call(RECORD_TEMPLATES, entityName) ? RECORD_TEMPLATES[entityName] : undefined;
}
