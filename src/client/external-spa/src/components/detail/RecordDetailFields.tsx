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

// ---------------------------------------------------------------------------
// Per-entity description
// ---------------------------------------------------------------------------

/** How one field's value is turned into text when Dataverse supplied no formatted value. */
export type DetailFieldKind = 'text' | 'date' | 'money' | 'lookup';

export interface DetailFieldDef {
  /** Dataverse logical name of the column (must be on the module's allow-list). */
  attribute: string;
  /** Row label. */
  label: string;
  kind?: DetailFieldKind;
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
   * documents section. An invoice uses the `invoices` root (owner decision 2026-10-11, option A: access inherited from the
   * invoice's matter and project).
   */
  documentsRoot?: DocumentRoot;
  /** `sprk_invoice` lookup that ties invoices to this record; absent = no invoices section. */
  invoicesBy?: string;
  /** Project, Matter and Work Assignment only (notes/decisions.md): Document, Invoice and Service Request omit it. */
  showsEventsAndTasks: boolean;
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
      { attribute: 'sprk_totalamount', label: 'Total amount', kind: 'money' },
      { attribute: 'sprk_project', label: 'Project', kind: 'lookup' },
      { attribute: 'sprk_matter', label: 'Matter', kind: 'lookup' },
    ],
    documentsRoot: 'invoices',
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
  },
});

export interface RecordDetailFieldsProps {
  cfg: RecordDetailEntity;
  record: DetailRecord;
}

export const RecordDetailFields: React.FC<RecordDetailFieldsProps> = ({ cfg, record }) => {
  const styles = useStyles();
  return (
    <dl className={styles.list} aria-label={`${cfg.label} details`} data-testid="record-detail-fields">
      {cfg.fields.map(def => (
        <React.Fragment key={def.attribute}>
          <dt className={styles.term}>{def.label}</dt>
          <dd className={styles.definition} data-testid={`record-detail-field-${def.attribute}`}>
            {formatDetailValue(record, def)}
          </dd>
        </React.Fragment>
      ))}
    </dl>
  );
};

export default RecordDetailFields;
