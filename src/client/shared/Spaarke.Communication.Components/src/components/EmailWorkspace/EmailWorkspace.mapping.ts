/**
 * EmailWorkspace.mapping.ts
 *
 * Pure(ish) host-wiring helpers for `<EmailWorkspace />` (task 040) — the
 * "one place in the tree" (mirrors the `TrackingFieldTrio` PCF `index.ts`
 * convention, task 023) that knows the actual `sprk_communication` Dataverse
 * field names this composition root reads/writes. Field names verified
 * against `docs/data-model/sprk_communication.md` (2026-07-21) and the BFF
 * `CommunicationService.cs` document-archive query — EXCEPT the three
 * tracking fields, which are a documented placeholder (see
 * `EMAIL_TRACKING_FIELDS` below).
 *
 * No React import (this file is safe to unit-test without a DOM/provider).
 */
import type { IDataService } from '@spaarke/ui-components';
import { sanitizeEmailHtml } from '@spaarke/ui-components';
import type { IAccessPermissionOption } from '@spaarke/ui-components';
import {
  COMMUNICATION_REGARDING_FIELDS,
  derivePrimaryReview,
  summarizePrimaryReview,
  type FiledAssociation,
} from '../../logic/connections';
import type { EmailCardItem } from '../EmailCardList/EmailCardList.types';
import { EMAIL_COMMUNICATION_TYPE } from '../EmailCardList/EmailCardList.types';

/** Entity this workspace is scoped to. */
export const COMMUNICATION_ENTITY = 'sprk_communication';

/**
 * ⚠️ PLACEHOLDER pending Dataverse schema creation. `docs/data-model/
 * sprk_communication.md` (reviewed 2026-07-21) does NOT yet document a
 * Monitor / High Priority / Access Permission field on `sprk_communication` —
 * design.md (Lens 2/4) flags the `TrackingFieldTrio` placement on this form as
 * "net-new to the form." These three logical names are the best-available
 * placeholders (matching the `TrackingFieldTrio` PCF's own Standard(100000000)/
 * Limited(100000001)/Restricted(100000002) fallback for Access Permission);
 * update this ONE constant once the columns exist and are confirmed via live
 * Dataverse metadata — no other file in this component needs to change.
 * Filed as a defer/issue (see `projects/email-communication-solution-r5/
 * notes/defer-issues.md`) rather than invented as new schema in this task
 * (out of scope — this task's outputs are code-only, no `dataverse` tag).
 */
export const EMAIL_TRACKING_FIELDS = {
  monitor: 'sprk_ismonitored',
  highPriority: 'sprk_ishighpriority',
  accessPermission: 'sprk_accesspermission',
} as const;

/** Same fallback triple the `TrackingFieldTrio` PCF caller uses when live OptionSet metadata isn't available (task 023 `index.ts`). */
export const DEFAULT_ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: 100000000, label: 'Standard' },
  { value: 100000001, label: 'Limited' },
  { value: 100000002, label: 'Restricted' },
];

/** Envelope + tracking + association columns this workspace reads per selection (no `$select` — mirrors `CommunicationConnectionsApp`'s "an unknown regarding field name makes the whole $select throw" note; the regarding lookups vary by deployment). */
export interface RawCommunicationRecord {
  [key: string]: unknown;
}

function asString(v: unknown): string {
  return typeof v === 'string' ? v : '';
}

function asNullableString(v: unknown): string | null {
  return typeof v === 'string' && v.length > 0 ? v : null;
}

function asNullableNumber(v: unknown): number | null {
  return typeof v === 'number' ? v : null;
}

function asBoolean(v: unknown): boolean {
  return v === true;
}

/**
 * Map one raw saved-view row (whatever columns the maker-authored FetchXML
 * selects — see `useEmailViews`'s own "host owns mapping" doc comment) into
 * the `EmailCardList` presentational shape. Defensive against missing
 * columns: a view that omits a field degrades to an empty string, never a
 * crash. `preview` is derived from `sprk_body` (HTML) via the shared
 * `sanitizeEmailHtml` (task 001) THEN stripped to plain text — `sprk_body`
 * is client-sanitized before any tag-stripping, and the result is rendered
 * by `EmailCardList` as plain text only (never `dangerouslySetInnerHTML`).
 *
 * `isUnread` — `sprk_communication` has no documented read/unread column
 * (verified against `docs/data-model/sprk_communication.md`, 2026-07-21).
 * Defaults to `false` (no unread signal) until a real field exists; flagged
 * alongside the tracking-fields gap above rather than invented here.
 */
export function mapRowToEmailCardItem(row: Record<string, unknown>): EmailCardItem {
  const rawBody = asString(row['sprk_body']);
  const preview = rawBody ? htmlToPlainTextPreview(sanitizeEmailHtml(rawBody)) : '';
  const communicationType =
    typeof row['sprk_communicationtype'] === 'number'
      ? (row['sprk_communicationtype'] as number)
      : EMAIL_COMMUNICATION_TYPE;

  return {
    id: asString(row['sprk_communicationid']),
    from: asString(row['sprk_from']),
    subject: asString(row['sprk_subject']) || '(no subject)',
    preview,
    date:
      asNullableString(row['sprk_receiveddate']) ?? asNullableString(row['sprk_sentat']) ?? asString(row['createdon']),
    isUnread: false,
    communicationType,
    reviewTone: deriveCardReviewTone(row),
  };
}

/**
 * Derive the left-of-sender status dot tone (🔴/🟡/🟢) for a card from the row's
 * association fields — using the SAME `derivePrimaryReview` the reading pane uses,
 * so the card dot and the open email's section dot never disagree. Returns
 * `undefined` when the row carries NO association data (a view whose FetchXML
 * omits the association columns → no dot, rather than a misleading all-red list).
 */
function deriveCardReviewTone(row: Record<string, unknown>): 'red' | 'yellow' | 'green' | undefined {
  const filed = readFiledAssociations(row);
  const hasAssociationData =
    row['sprk_associationstatus'] != null ||
    (typeof row['sprk_associationprovenance'] === 'string' && row['sprk_associationprovenance'].length > 0) ||
    filed.length > 0;
  if (!hasAssociationData) return undefined;
  return summarizePrimaryReview(
    derivePrimaryReview(asNullableString(row['sprk_associationprovenance']), asNullableNumber(row['sprk_associationstatus']), filed, {
      recordName: asNullableString(row['sprk_regardingrecordname']),
      recordNumber: asNullableString(row['sprk_regardingrecordnumber']),
    })
  ).tone;
}

/** Strip tags from already-sanitized HTML and collapse whitespace into a short plain-text preview (never used with `dangerouslySetInnerHTML`). */
function htmlToPlainTextPreview(safeHtml: string, maxLength = 140): string {
  const text = safeHtml
    .replace(/<[^>]*>/g, ' ')
    .replace(/&nbsp;/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
  return text.length > maxLength ? `${text.slice(0, maxLength).trimEnd()}…` : text;
}

/**
 * Read the record's actually-filed typed regarding lookups into
 * `FiledAssociation[]` — mirrors `CommunicationConnectionsApp.tsx`'s own
 * `_${field}_value` / `_${field}_value@OData.Community.Display.V1.FormattedValue`
 * annotation read (the same Web-API-shaped payload `IDataService.retrieveRecord`
 * returns when called WITHOUT `$select`). Uses the COMMUNICATION regarding
 * field names (`sprk_regardingperson` for Contact, etc.), not the
 * `sprk_todo` `TODO_REGARDING_CATALOG` names.
 */
export function readFiledAssociations(raw: RawCommunicationRecord): FiledAssociation[] {
  const filed: FiledAssociation[] = [];
  for (const { field, entityType } of COMMUNICATION_REGARDING_FIELDS) {
    const val = raw[`_${field}_value`];
    if (typeof val === 'string' && val) {
      const nm = raw[`_${field}_value@OData.Community.Display.V1.FormattedValue`];
      filed.push({
        entityType,
        recordId: val.replace(/[{}]/g, '').toLowerCase(),
        recordName: typeof nm === 'string' && nm ? nm : val.replace(/[{}]/g, '').toLowerCase(),
      });
    }
  }
  return filed;
}

/**
 * Envelope/association/tracking fields this workspace derives from one
 * `retrieveRecord` (no `$select`) call per selection.
 *
 * `subject`/`from`/`to`/`cc`/`bcc`/`sentAt`/`receivedDate` were added
 * (email-communication-solution-r5, reading-pane layout redesign) to retire
 * `EmailReadingHeader`'s OWN internal `retrieveRecord(..., HEADER_SELECT)`
 * call — that second read used a `$select` string against `sprk_communication`
 * and failed at runtime ("Could not load this email header"). These fields
 * are read DEFENSIVELY (`asNullableString`) from the SAME no-`$select` record
 * this hook already fetches, so a field absent on a given deployment degrades
 * to `null` rather than throwing — there is exactly ONE per-selection read in
 * this component tree now.
 */
export interface EmailWorkspaceRecordState {
  subject: string | null;
  from: string | null;
  to: string | null;
  cc: string | null;
  bcc: string | null;
  sentAt: string | null;
  receivedDate: string | null;
  sprk_body: string;
  regardingRecordName: string | null;
  regardingRecordNumber: string | null;
  associationStatus: number | null;
  associationProvenanceJson: string | null;
  filedAssociations: FiledAssociation[];
  monitor: boolean;
  highPriority: boolean;
  accessPermission: number | null;
}

/** Derive the full workspace record state from one raw `retrieveRecord` payload. */
export function toWorkspaceRecordState(raw: RawCommunicationRecord): EmailWorkspaceRecordState {
  return {
    subject: asNullableString(raw['sprk_subject']),
    from: asNullableString(raw['sprk_from']),
    to: asNullableString(raw['sprk_to']),
    cc: asNullableString(raw['sprk_cc']),
    bcc: asNullableString(raw['sprk_bcc']),
    sentAt: asNullableString(raw['sprk_sentat']),
    receivedDate: asNullableString(raw['sprk_receiveddate']),
    sprk_body: asString(raw['sprk_body']),
    regardingRecordName: asNullableString(raw['sprk_regardingrecordname']),
    regardingRecordNumber: asNullableString(raw['sprk_regardingrecordnumber']),
    associationStatus: asNullableNumber(raw['sprk_associationstatus']),
    associationProvenanceJson: asNullableString(raw['sprk_associationprovenance']),
    filedAssociations: readFiledAssociations(raw),
    monitor: asBoolean(raw[EMAIL_TRACKING_FIELDS.monitor]),
    highPriority: asBoolean(raw[EMAIL_TRACKING_FIELDS.highPriority]),
    accessPermission: asNullableNumber(raw[EMAIL_TRACKING_FIELDS.accessPermission]),
  };
}

/**
 * Resolve the `.eml` archive document id for a communication — the related
 * `sprk_document` flagged `sprk_isemailarchive = true` (verified against
 * `CommunicationService.cs` `FindExistingArchiveDocumentAsync`: entity
 * `sprk_document`, lookup field `sprk_communication`). Returns `null` (never
 * throws) when no archive exists or the lookup fails — an archive-less email
 * is a normal degradation state (task 033), not an error.
 */
export async function resolveEmlArchiveDocumentId(
  dataService: Pick<IDataService, 'retrieveMultipleRecords'>,
  communicationId: string
): Promise<string | null> {
  try {
    const result = await dataService.retrieveMultipleRecords(
      'sprk_document',
      `?$select=sprk_documentid&$filter=_sprk_communication_value eq ${communicationId} and sprk_isemailarchive eq true&$top=1`
    );
    const id = result.entities[0]?.['sprk_documentid'];
    return typeof id === 'string' && id.length > 0 ? id : null;
  } catch {
    return null;
  }
}
