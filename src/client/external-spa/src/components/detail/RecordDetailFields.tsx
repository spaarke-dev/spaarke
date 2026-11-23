/**
 * RecordDetailFields — the read-only field block of the record detail view (R3 task 050, FR-01), plus the per-entity
 * description of what a detail view shows (`RECORD_DETAIL_ENTITIES`).
 *
 * READ-ONLY by construction: this renders a definition list of values. There is no input, no edit control and no
 * save path (project constraint: no editing of core records; spec "Out of scope").
 *
 * COLUMNS ARE THE SERVER ALLOW-LIST, NOT A WISH LIST. The external module read seam (`ExternalModuleDataEndpoints`)
 * refuses (HTTP 400, `DV_RECORD_COLUMN_NOT_PERMITTED` / `DV_FETCHXML_COLUMN_NOT_PERMITTED`) any column outside the
 * module's `ReadableColumns` (`Infrastructure/DI/ExternalAccessModule.cs`, task 134 / 157). Each `fields` list below
 * is therefore a subset of that module's list — to show another column, add it THERE first (a column never on the
 * allow-list would turn the whole detail view into a 400). No new server read is added by this task.
 *
 * ONE READ PATH: the id-filtered module fetch (POST /api/v1/external/api/dataverse/fetch with `<idAttribute> = {id}`) for
 * all six entities. It is the same Tier-2-scoped read the grids and tabs run: for a root module (Project, Matter, Work
 * Assignment) the scope dimension is the record's own id, so the BFF ANDs the caller's accessible-set filter onto the
 * id filter and re-checks every row; for a child module (Document, Invoice, Service Request) it scopes ROWS by the
 * parent / requester lookup. Either way an id outside the caller's set returns no row, which is the denied state.
 * The record route (GET …/record/{entity}/{id}) is NOT used: it unwraps option sets to a bare integer and emits no
 * `@formattedValues`, so a status or priority would render as "1" / "100000001"; it also always answers 403 for child
 * entities (its own doc comment: "child single-record reads … fail closed"). The fetch seam returns `@formattedValues`.
 *
 * ADR-021: Fluent UI v9 semantic tokens only (no hard-coded colours). ADR-012: composes Fluent primitives only.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { parseDueDate } from '@spaarke/ui-components/utils/dateLocal';
import type { DocumentRoot } from '../../api/web-api-client';
import { eventRegardingLink, readEventRecord } from '../events/eventDetail';
import { childRecordAttributesOf } from '../record/childRecord';
import { REVIEW_DETAIL_COLUMN, reviewDetailFieldDisplay } from '../record/reviewDetail';

// ---------------------------------------------------------------------------
// Per-entity description
// ---------------------------------------------------------------------------

/** How one field's value is turned into text when Dataverse supplied no formatted value. */
export type DetailFieldKind = 'text' | 'date' | 'money' | 'lookup';

/**
 * Where a field's value leads, when the value is another record (R4 task 015: an event's Regarding). `entity` is the record
 * template entity to open, `noun` how it reads in the control's accessible name ("project").
 */
export interface DetailFieldLink {
  entity: string;
  id: string;
  noun: string;
}

/** What a gated field shows: its value text (if any) and an explanation line under it. */
export interface DetailFieldDisplay {
  text?: string;
  note?: string;
}

export interface DetailFieldDef {
  /** Dataverse logical name of the column (must be on the module's allow-list). */
  attribute: string;
  /** Row label. */
  label: string;
  kind?: DetailFieldKind;
  /**
   * Task 020 (FR-11). A field whose display is decided by more than its own value (the invoice's Review detail, gated on the
   * invoice status). Given the read row, returns what to show, or `null` for a blank field: the whole row is then left out
   * (no label with an empty value). Absent = the generic `formatDetailValue` row, as for every other field.
   */
  display?: (record: DetailRecord) => DetailFieldDisplay | null;
  /**
   * Task 015. Resolves the record this field's value points at, or null (no link for this row). The value is drawn as a
   * link-style button, only where the host passes `onOpenLink` to `RecordDetailFields`; with no handler (or a null link) it is
   * plain text. Generic: the entity description decides, never the renderer.
   */
  link?: (record: DetailRecord) => DetailFieldLink | null;
}

export interface RecordDetailEntity {
  /** Dataverse logical name of the entity. */
  entity: string;
  /** Singular display label ("Matter"). */
  label: string;
  /** The entity's primary id column (also the key of its row in the grid's records). */
  idAttribute: string;
  /** Columns tried, in order, for the modal title (first non-empty wins). */
  titleAttributes: readonly string[];
  /** The read-only field rows, in display order. */
  fields: readonly DetailFieldDef[];
  /**
   * Columns read with the record but NOT shown as a field row (a fact an action needs: the service request's `statecode`
   * hides Delete once staff have closed it). Must be on the module's allow-list like `fields`.
   */
  extraColumns?: readonly string[];
  /**
   * The task 031 route root (`/api/v1/external/{root}/{id}/documents`) that serves this record's documents; absent = no
   * documents section. An invoice has none: its Files section reads ebilling's `/invoices/{id}/files` routes (R4 task 021).
   */
  documentsRoot?: DocumentRoot;
  /** `sprk_invoice` lookup that ties invoices to this record; absent = no invoices section. */
  invoicesBy?: string;
  /** Project, Matter and Work Assignment only (notes/decisions.md): Document, Invoice and Service Request omit it. */
  showsEventsAndTasks: boolean;
  /**
   * Task 015. The entity's own reader, for a record the module fetch seam cannot give (an event: its names and rights come from
   * `GET /events/{id}`). `useRecord` uses, in order, a reader supplied for the opening, this, then the module fetch. It throws
   * an `ApiError` (403 denied, 404 not found) or `RecordNotFoundError`, which `useRecord` maps to the matching state.
   */
  read?: (id: string) => Promise<DetailRecord>;
}

export const RECORD_DETAIL_ENTITIES: Readonly<Record<string, RecordDetailEntity>> = {
  sprk_project: {
    entity: 'sprk_project',
    label: 'Project',
    idAttribute: 'sprk_projectid',
    titleAttributes: ['sprk_projectname', 'sprk_projectnumber'],
    fields: [
      { attribute: 'sprk_projectname', label: 'Name' },
      { attribute: 'sprk_projectnumber', label: 'Number' },
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'modifiedon', label: 'Last modified', kind: 'date' },
    ],
    documentsRoot: 'projects',
    invoicesBy: 'sprk_project',
    showsEventsAndTasks: true,
  },
  sprk_matter: {
    entity: 'sprk_matter',
    label: 'Matter',
    idAttribute: 'sprk_matterid',
    titleAttributes: ['sprk_mattername', 'sprk_matternumber'],
    fields: [
      { attribute: 'sprk_mattername', label: 'Name' },
      { attribute: 'sprk_matternumber', label: 'Number' },
      { attribute: 'statuscode', label: 'Status' },
    ],
    documentsRoot: 'matters',
    invoicesBy: 'sprk_matter',
    showsEventsAndTasks: true,
  },
  sprk_workassignment: {
    entity: 'sprk_workassignment',
    label: 'Work assignment',
    idAttribute: 'sprk_workassignmentid',
    titleAttributes: ['sprk_name', 'sprk_workassignmentnumber'],
    fields: [
      { attribute: 'sprk_name', label: 'Name' },
      { attribute: 'sprk_workassignmentnumber', label: 'Number' },
      { attribute: 'sprk_priority', label: 'Priority' },
      { attribute: 'sprk_responseduedate', label: 'Response due', kind: 'date' },
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'sprk_regardingproject', label: 'Project', kind: 'lookup' },
    ],
    documentsRoot: 'workassignments',
    // sprk_invoice has no work-assignment lookup (ExternalAccessModule.cs "invoices" dimensions: matter, project).
    showsEventsAndTasks: true,
  },
  sprk_document: {
    entity: 'sprk_document',
    label: 'Document',
    idAttribute: 'sprk_documentid',
    titleAttributes: ['sprk_documentname'],
    fields: [
      { attribute: 'sprk_documentname', label: 'Name' },
      { attribute: 'sprk_documenttype', label: 'Type' },
      { attribute: 'createdon', label: 'Created', kind: 'date' },
      { attribute: 'sprk_project', label: 'Project', kind: 'lookup' },
      { attribute: 'sprk_matter', label: 'Matter', kind: 'lookup' },
      { attribute: 'sprk_workassignment', label: 'Work assignment', kind: 'lookup' },
    ],
    // Read, not shown (R4 task 022, FR-14): the file type decides whether the Preview section can show the file inline. On the
    // documents module's allow-list (ExternalAccessModule.cs; the documents grid reads it), so it adds no new BFF column.
    extraColumns: ['sprk_filetype'],
    showsEventsAndTasks: false,
  },
  sprk_invoice: {
    entity: 'sprk_invoice',
    label: 'Invoice',
    idAttribute: 'sprk_invoiceid',
    titleAttributes: ['sprk_invoicenumber', 'sprk_name'],
    fields: [
      { attribute: 'sprk_invoicenumber', label: 'Invoice number' },
      { attribute: 'sprk_name', label: 'Name' },
      { attribute: 'sprk_invoicedate', label: 'Invoice date', kind: 'date' },
      { attribute: 'statuscode', label: 'Status' },
      // R4 task 020 (FR-11, 082c): read with the record, shown only while the invoice is In Review (`record/reviewDetail.ts`).
      { attribute: REVIEW_DETAIL_COLUMN, label: 'Review detail', display: reviewDetailFieldDisplay },
      { attribute: 'sprk_totalamount', label: 'Total amount', kind: 'money' },
      { attribute: 'sprk_project', label: 'Project', kind: 'lookup' },
      { attribute: 'sprk_matter', label: 'Matter', kind: 'lookup' },
    ],
    showsEventsAndTasks: false,
  },
  sprk_servicerequest: {
    entity: 'sprk_servicerequest',
    label: 'Service request',
    idAttribute: 'sprk_servicerequestid',
    titleAttributes: ['sprk_name', 'sprk_servicerequestnumber'],
    fields: [
      { attribute: 'sprk_name', label: 'Name' },
      { attribute: 'sprk_servicerequestnumber', label: 'Number' },
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'createdon', label: 'Created', kind: 'date' },
      { attribute: 'sprk_requestedby', label: 'Requested by', kind: 'lookup' },
    ],
    // Read, not shown: a closed request (statecode 1) cannot be deleted, so ServiceRequestActions hides Delete (the BFF's
    // service-request module allows the column; the status row above already shows the user the status).
    extraColumns: ['statecode'],
    // A service request's documents are reachable ONLY through the servicerequests route (task 031 K3/K4).
    documentsRoot: 'servicerequests',
    showsEventsAndTasks: false,
  },
  // R4 task 015 (FR-36). An event is read from the BFF's event DTO, not the module fetch seam (`read`, `events/eventDetail.ts`):
  // the columns below are the record `toEventDetailRecord` builds, so they add no allow-listed column and no module read.
  // Every value shows a name or a label (never an id) and "—" when empty. Title repeats the heading: the template names its
  // `nameAttribute` among the read fields, and the row is a plain way to read a long title in full.
  sprk_event: {
    entity: 'sprk_event',
    label: 'Event',
    idAttribute: 'sprk_eventid',
    titleAttributes: ['sprk_name'],
    fields: [
      { attribute: 'sprk_name', label: 'Title' },
      { attribute: 'sprk_eventtype', label: 'Event Type', kind: 'lookup' },
      { attribute: 'statuscode', label: 'Status' },
      { attribute: 'sprk_duedate', label: 'Due Date', kind: 'date' },
      { attribute: 'sprk_priority', label: 'Priority' },
      { attribute: 'sprk_assignedto', label: 'Assigned To', kind: 'lookup' },
      { attribute: 'sprk_description', label: 'Description' },
      { attribute: 'createdon', label: 'Created On', kind: 'date' },
      { attribute: 'sprk_createdby', label: 'Created By', kind: 'lookup' },
      { attribute: 'sprk_regarding', label: 'Regarding', kind: 'lookup', link: eventRegardingLink },
    ],
    showsEventsAndTasks: false,
    read: readEventRecord,
  },
};

/** The detail description for an entity, or `undefined` when the entity has no detail view (row-open is a no-op). */
export function getRecordDetailEntity(entityName: string | undefined): RecordDetailEntity | undefined {
  if (!entityName) return undefined;
  return Object.prototype.hasOwnProperty.call(RECORD_DETAIL_ENTITIES, entityName)
    ? RECORD_DETAIL_ENTITIES[entityName]
    : undefined;
}

// ---------------------------------------------------------------------------
// Value formatting
// ---------------------------------------------------------------------------

/** What a read row looks like: columns, plus the BFF's `@formattedValues` map of display strings. */
export type DetailRecord = Record<string, unknown>;

/** What a column with no value shows. */
export const EMPTY_VALUE = '—';

function formattedValueOf(record: DetailRecord, attribute: string): string | undefined {
  const map = record['@formattedValues'];
  if (map && typeof map === 'object') {
    const v = (map as Record<string, unknown>)[attribute];
    if (typeof v === 'string' && v.trim()) return v;
  }
  return undefined;
}

/** The display text of one column of a read row; an em dash when there is none. Never a raw GUID. */
export function formatDetailValue(record: DetailRecord, def: Pick<DetailFieldDef, 'attribute' | 'kind'>): string {
  // Dataverse's own display string (option-set labels, currency, localised dates) is always preferred.
  const formatted = formattedValueOf(record, def.attribute);
  if (formatted) return formatted;

  const raw = record[def.attribute];
  if (raw === null || raw === undefined || raw === '') return EMPTY_VALUE;

  if (def.kind === 'lookup') {
    // A lookup normally has a formatted value (handled above); otherwise an EntityReference object carries its name.
    if (typeof raw === 'object') {
      const name = (raw as { name?: unknown }).name;
      return typeof name === 'string' && name.trim() ? name : EMPTY_VALUE;
    }
    return EMPTY_VALUE;
  }
  if (def.kind === 'date' && typeof raw === 'string') {
    const d = parseDueDate(raw);
    return d ? d.toLocaleDateString() : raw;
  }
  if (def.kind === 'money' && typeof raw === 'number') {
    return raw.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
  if (typeof raw === 'string' || typeof raw === 'number' || typeof raw === 'boolean') return String(raw);
  return EMPTY_VALUE;
}

// ---------------------------------------------------------------------------
// Document -> parent (R3 task 066)
// ---------------------------------------------------------------------------

/** A record a document is filed to, as the content route names it: `/api/v1/external/{root}/{id}/documents/...`. */
export interface DocumentParentCandidate {
  root: DocumentRoot;
  id: string;
}

/**
 * The typed parent links of a `sprk_document`, in the order the BFF reads them: project, matter, work assignment
 * (`DocumentRootLinks.Links`, ExternalDataService.cs:482-490; the documents module's scope dimensions in
 * ExternalAccessModule.cs:401-406 are in the same order). The content route accepts only the TYPED lookup (not the
 * `sprk_related*` twin: `DocumentScopeRead.IsUnder` reads the route's own lookup), so only these three are candidates.
 */
const DOCUMENT_PARENT_LINKS: ReadonlyArray<{ attribute: string; root: DocumentRoot }> = [
  { attribute: 'sprk_project', root: 'projects' },
  { attribute: 'sprk_matter', root: 'matters' },
  { attribute: 'sprk_workassignment', root: 'workassignments' },
];

const LINK_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_LINK_GUID = '00000000-0000-0000-0000-000000000000';

/** The id out of a lookup value: an entity reference (`{ id }`, as the module fetch serialises it) or a bare id string. */
function lookupId(raw: unknown): string | null {
  let id: unknown = raw;
  if (raw && typeof raw === 'object') {
    const reference = raw as { id?: unknown; Id?: unknown };
    id = reference.id ?? reference.Id;
  }
  if (typeof id !== 'string') return null;
  const trimmed = id.trim();
  return LINK_GUID.test(trimmed) && trimmed !== EMPTY_LINK_GUID ? trimmed : null;
}

/**
 * The parents a document row can be downloaded through, in the BFF's order. Empty = the row names no usable parent, so no
 * Download is offered. The client cannot tell which parent the caller may READ (a document can be filed to several), so
 * the download tries these in order and moves on only on a 403 (`downloadDocumentFromParents`); the server decides.
 */
export function documentParentCandidates(record: DetailRecord): DocumentParentCandidate[] {
  const candidates: DocumentParentCandidate[] = [];
  for (const { attribute, root } of DOCUMENT_PARENT_LINKS) {
    const id = lookupId(record[attribute] ?? record[`_${attribute}_value`]);
    if (id) candidates.push({ root, id });
  }
  return candidates;
}

/** The modal title for a read row: the first non-empty title column, else the entity label. */
export function recordDetailTitle(cfg: RecordDetailEntity, record: DetailRecord | null): string {
  if (record) {
    for (const attribute of cfg.titleAttributes) {
      const text = formatDetailValue(record, { attribute });
      if (text !== EMPTY_VALUE) return text;
    }
  }
  return cfg.label;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  list: {
    display: 'grid',
    gridTemplateColumns: 'minmax(120px, max-content) 1fr',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalS,
    margin: 0,
  },
  term: {
    margin: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
  },
  definition: {
    margin: 0,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    overflowWrap: 'anywhere',
    // A long description keeps its line breaks.
    whiteSpace: 'pre-wrap',
  },
  // The explanation line under a gated field's value (task 020).
  note: {
    display: 'block',
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
    whiteSpace: 'normal',
  },
  // A value that opens another record: link-style, the same as a document name in a grid.
  linkButton: {
    margin: 0,
    padding: 0,
    border: 'none',
    backgroundColor: 'transparent',
    font: 'inherit',
    textAlign: 'start',
    color: tokens.colorBrandForeground1,
    cursor: 'pointer',
    ':hover': { textDecoration: 'underline' },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
});

export interface RecordDetailFieldsProps {
  cfg: RecordDetailEntity;
  record: DetailRecord;
  /**
   * Task 015. Opens the record a linked field points at (`DetailFieldDef.link`), with the control that was activated (focus
   * returns to it on close). Absent = every value is plain text (a child record: one level only).
   */
  onOpenLink?: (link: DetailFieldLink, trigger: HTMLElement) => void;
}

export const RecordDetailFields: React.FC<RecordDetailFieldsProps> = ({ cfg, record, onOpenLink }) => {
  const styles = useStyles();
  return (
    <dl className={styles.list} aria-label={`${cfg.label} details`} data-testid="record-detail-fields">
      {cfg.fields.map(def => {
        if (def.display) {
          // A gated field (task 020): its own rule decides what shows; a blank one leaves no row at all.
          const shown = def.display(record);
          if (!shown) return null;
          return (
            <React.Fragment key={def.attribute}>
              <dt className={styles.term}>{def.label}</dt>
              <dd className={styles.definition} data-testid={`record-detail-field-${def.attribute}`}>
                {shown.text}
                {shown.note && (
                  <span className={styles.note} data-testid={`record-detail-field-${def.attribute}-note`}>
                    {shown.note}
                  </span>
                )}
              </dd>
            </React.Fragment>
          );
        }
        const text = formatDetailValue(record, def);
        const link = onOpenLink && def.link ? def.link(record) : null;
        return (
          <React.Fragment key={def.attribute}>
            <dt className={styles.term}>{def.label}</dt>
            <dd className={styles.definition} data-testid={`record-detail-field-${def.attribute}`}>
              {link && onOpenLink ? (
                <button
                  type="button"
                  className={styles.linkButton}
                  aria-label={`Open ${link.noun} ${text}`}
                  {...childRecordAttributesOf(link.entity, link.id)}
                  onClick={e => onOpenLink(link, e.currentTarget)}
                >
                  {text}
                </button>
              ) : (
                text
              )}
            </dd>
          </React.Fragment>
        );
      })}
    </dl>
  );
};

export default RecordDetailFields;
