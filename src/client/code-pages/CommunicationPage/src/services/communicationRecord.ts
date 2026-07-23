/**
 * Reads a single `sprk_communication` record via `Xrm.WebApi` (host-context
 * Dataverse access — DATA-ACCESS-DECISION-CRITERIA: reading a single record the
 * host already has session for is a `Xrm.WebApi` case, not a BFF call).
 *
 * The selected columns include `sprk_communicationtype` (drives the layout
 * switch) plus the task-001 reply-thread columns (`sprk_internetmessageid`,
 * `sprk_inreplyto`) that reply/forward modes rely on.
 */

import type { ICommunicationRecord } from '../types/communication';
import { resolveXrm } from './xrm';

/** Columns fetched for the record. Keep in sync with `ICommunicationRecord`. */
const SELECT_COLUMNS = [
  'sprk_communicationid',
  'sprk_communicationtype',
  'sprk_direction',
  'sprk_subject',
  'sprk_from',
  'sprk_to',
  'sprk_cc',
  'sprk_bcc',
  'sprk_body',
  'sprk_bodyformat',
  'sprk_sentat',
  'sprk_receiveddate',
  'sprk_associationstatus',
  // task-001 reply-thread columns
  'sprk_internetmessageid',
  'sprk_inreplyto',
] as const;

/** Formatted-value suffix exposes option-set `*name` + status labels. */
const FORMATTED = 'OData.Community.Display.V1.FormattedValue';

function readFormatted(record: Record<string, unknown>, column: string): string | null {
  const key = `${column}@${FORMATTED}`;
  const value = record[key];
  return typeof value === 'string' ? value : null;
}

/**
 * Retrieve a `sprk_communication` record by GUID.
 * @throws if Xrm is unavailable or the retrieve fails (caller surfaces the error).
 */
export async function readCommunicationRecord(id: string): Promise<ICommunicationRecord> {
  const xrm = resolveXrm();
  if (!xrm?.WebApi?.retrieveRecord) {
    throw new Error('[CommunicationPage] Xrm.WebApi is not available — cannot read the communication record.');
  }

  const options = `?$select=${SELECT_COLUMNS.join(',')}`;
  const raw = (await xrm.WebApi.retrieveRecord('sprk_communication', id, options)) as Record<string, unknown>;

  return {
    sprk_communicationid: String(raw.sprk_communicationid ?? id),
    sprk_communicationtype: (raw.sprk_communicationtype as number | null) ?? null,
    sprk_communicationtypename: readFormatted(raw, 'sprk_communicationtype'),
    sprk_direction: (raw.sprk_direction as number | null) ?? null,
    sprk_directionname: readFormatted(raw, 'sprk_direction'),
    sprk_subject: (raw.sprk_subject as string | null) ?? null,
    sprk_from: (raw.sprk_from as string | null) ?? null,
    sprk_to: (raw.sprk_to as string | null) ?? null,
    sprk_cc: (raw.sprk_cc as string | null) ?? null,
    sprk_bcc: (raw.sprk_bcc as string | null) ?? null,
    sprk_body: (raw.sprk_body as string | null) ?? null,
    sprk_bodyformat: (raw.sprk_bodyformat as number | null) ?? null,
    sprk_bodyformatname: readFormatted(raw, 'sprk_bodyformat'),
    statuscode: (raw.statuscode as number | null) ?? null,
    statecode: (raw.statecode as number | null) ?? null,
    sprk_sentat: (raw.sprk_sentat as string | null) ?? null,
    sprk_receiveddate: (raw.sprk_receiveddate as string | null) ?? null,
    sprk_associationstatus: (raw.sprk_associationstatus as number | null) ?? null,
    sprk_internetmessageid: (raw.sprk_internetmessageid as string | null) ?? null,
    sprk_inreplyto: (raw.sprk_inreplyto as string | null) ?? null,
  };
}
