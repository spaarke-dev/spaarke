/**
 * recordAccess — what the caller may do on one record, from what the BFF answered for it
 * (spaarke-SPA-external-access-platform-r4 task 006, FR-06 / FR-07 / NFR-04).
 *
 * ONE PLACE FOR:
 *   (a) the right behind each of the record's four create actions (Upload, New message, Add to-do, Add event):
 *       `createRightOf`, used by every list inside the record template, so the four follow one rule; and the list-host
 *       rule of SmartTodo, EventsCalendar and a project's DocumentLibrary, the same in and out of the template
 *       (`levelVerdictOf`, `listCreateRightOf`; D-9: the project page follows it too);
 *   (a2) whether "Submit invoice" is offered (task 007, FR-10): `submitInvoiceRightOf`, from `/me` `firms` and the
 *       firm-records answers (read by `actions/SubmitInvoiceAction.tsx`);
 *   (b) the header's "Your access" value: `yourAccessOf`, from `recordAccessFactsOf` and the lists' rights;
 *   (c) the one short "why" line a read-only section shows: `readOnlyNoteOf`;
 *   and the texts of (b) and (c): `RECORD_ACCESS_TEXT` (the level labels: `ACCESS_LEVEL_LABELS`).
 *
 * WHERE THE ANSWERS COME FROM (never a guess, ADR-003 fail closed):
 *   - each list's `canCreate`: the BFF's answer of the matching POST for this caller on this record (task 002: the
 *     documents, to-do, event and message lists of projects, matters and work assignments; the message thread of every
 *     record). It is the ONLY source for a matter, a work assignment, an invoice and a service request.
 *   - a project's `/me` level, for a caller WITHOUT `writeAccessPerRecord` (the one exception): `/me` carries levels for
 *     projects only. When the project's list also answered, both must allow (on the project page too, D-9).
 * Unknown (the list still loading, failed, or silent; `/me` not read) is `null`: the action is hidden and nothing is said.
 *
 * The client decides only what is SHOWN. It never sends a level, and every action is still checked by the server
 * (ADR-028 A1); a 403 from an offered action shows the action's existing error and changes nothing.
 */
import type { InvoiceCoreRecordKind } from '@spaarke/ui-components/components/InvoiceSubmission';
import type { InvoiceFirmRecordsResponse } from '../../api/web-api-client';
import type { ExternalUserContextResponse } from '../../auth/bff-client';
import { canCreate as levelCanCreate, mapAccessLevelString } from '../../hooks/useAccessLevel';
import { AccessLevel } from '../../types';
import type { RecordHostAction, RecordSectionId, RecordToolbarActionId } from './recordTemplate.types';

/** The four create actions a record's lists offer to the toolbar (task 004). */
export type RecordCreateActionId = Extract<RecordToolbarActionId, 'upload' | 'newMessage' | 'addTodo' | 'addEvent'>;

export const RECORD_CREATE_ACTIONS: readonly RecordCreateActionId[] = ['upload', 'newMessage', 'addTodo', 'addEvent'];

/**
 * A right a section reports WITHOUT a toolbar action (task 006 fix round 1): the invoice Files list's own Upload, which
 * stays the list's button (no toolbar Upload on an invoice) but is still part of what the caller may do on the record,
 * so "Your access" and the Files card's read-only line follow it.
 */
export type RecordSectionRightId = 'filesUpload';

/** May the caller create: `true`, `false`, or `null` = not known (yet). Only `true` offers the action. */
export type CreateRight = boolean | null;

// ---------------------------------------------------------------------------
// Texts (FR-07): the header labels and the read-only lines, in one map
// ---------------------------------------------------------------------------

/** The read-only line of a section, per what is read-only in it. */
interface ReadOnlyLines {
  documents: string;
  /** The invoice's Files card. */
  files: string;
  messages: string;
  /** Tasks & events, both lists read-only. */
  tasksEvents: string;
  /** Tasks & events, only the to-do list read-only. */
  todos: string;
  /** Tasks & events, only the event list read-only. */
  events: string;
}

/**
 * The label of each `/me` access level, as the SPA shows it. Exported so the other level-label copies can move here
 * (task 006 fix round 1: `ProjectPage`, `ContactsOrganizations`, `OutsideCounselDashboard` and `InviteUserDialog` still
 * hold their own; that consolidation is filed separately).
 */
export const ACCESS_LEVEL_LABELS: Readonly<Record<AccessLevel, string>> = {
  [AccessLevel.ViewOnly]: 'View Only',
  [AccessLevel.Collaborate]: 'Collaborate',
  [AccessLevel.FullAccess]: 'Full Access',
};

export const RECORD_ACCESS_TEXT: {
  /** "Your access" for a project, from its `/me` level (the labels the rest of the SPA uses). */
  level: Readonly<Record<AccessLevel, string>>;
  canContribute: string;
  readOnly: string;
  /**
   * For a caller whose deciding `/me` level is KNOWN to be View Only (a project): the line names the level the action
   * needs. Only then is the level known to be the reason.
   */
  grantLines: ReadOnlyLines;
  /**
   * Every other read-only answer (a Collaborate / Full Access caller refused by a role ceiling, a matter, a work
   * assignment, an invoice, a service request, a workforce contact, a `writeAccessPerRecord` caller): the list said only
   * `false`, not why, so no grant level is named.
   */
  neutralLines: ReadOnlyLines;
} = {
  level: ACCESS_LEVEL_LABELS,
  canContribute: 'Can contribute',
  readOnly: 'Read only',
  grantLines: {
    documents: 'You can view these documents. Uploading needs Collaborate access.',
    files: 'You can view these files. Uploading needs Collaborate access.',
    messages: 'You can read this thread. Posting needs Collaborate access.',
    tasksEvents: 'You can view these tasks and events. Adding them needs Collaborate access.',
    todos: 'You can view these tasks. Adding one needs Collaborate access.',
    events: 'You can view these events. Adding one needs Collaborate access.',
  },
  neutralLines: {
    documents: 'You can view these documents, but not upload to this record.',
    files: 'You can view these files, but not upload to this record.',
    messages: 'You can read this thread, but not post to it.',
    tasksEvents: 'You can view these tasks and events, but not add to them.',
    todos: 'You can view these tasks, but not add one.',
    events: 'You can view these events, but not add one.',
  },
};

// ---------------------------------------------------------------------------
// (a) One create action's right
// ---------------------------------------------------------------------------

export interface CreateRightInput {
  /** The list has answered (it is not on its first read and the read did not fail). */
  loaded: boolean;
  /** The list's `canCreate`; `null` / `undefined` when it said nothing. */
  listCanCreate: boolean | null | undefined;
  /**
   * What the caller's `/me` level says, for a record `/me` gives a level for (a project) and a caller WITHOUT
   * `writeAccessPerRecord`; `undefined` otherwise (the list alone decides).
   */
  levelAllows?: boolean;
}

/**
 * The right behind one create action inside the record template. Not loaded = unknown. With no level to go by, the list's
 * `canCreate` decides, and a missing one is unknown. With a level (a project, a level-decided caller), the level decides
 * where the list said nothing and both must allow where it did.
 */
export function createRightOf({ loaded, listCanCreate, levelAllows }: CreateRightInput): CreateRight {
  if (!loaded) return null;
  if (levelAllows === undefined) return typeof listCanCreate === 'boolean' ? listCanCreate : null;
  return levelAllows && listCanCreate !== false;
}

/**
 * What a list host's `/me` level says about a create: a level the host passed (a project) for a caller whose write
 * access is NOT per record, Collaborate / Full Access allowing; `undefined` otherwise (the list's `canCreate` decides).
 */
export function levelVerdictOf(
  accessLevel: AccessLevel | undefined,
  writeAccessPerRecord: boolean
): boolean | undefined {
  return accessLevel !== undefined && !writeAccessPerRecord ? levelCanCreate(accessLevel) : undefined;
}

/**
 * The create rule of a list that owns a create dialog (SmartTodo, EventsCalendar, DocumentLibrary's Upload on a project),
 * in one place and the same in and out of the record template:
 *   - `right`: the shared rule (`createRightOf`), reported with the toolbar offer;
 *   - `allowed`: whether the list offers its create at all: only a known `true`.
 * D-9 (#2716): the project page used to offer on the `/me` level alone (`levelAllows ?? …`), so a level-decided caller
 * whose list answered `canCreate: false` (a workforce role ceiling the level does not show) was offered a create the POST
 * refuses with 403. Now the level and the list must both allow there too, and nothing is offered before the list answers.
 */
export function listCreateRightOf(input: CreateRightInput): { right: CreateRight; allowed: boolean } {
  const right = createRightOf(input);
  return { right, allowed: right === true };
}

/** Strict `/me` label → level: anything else is not an answer (unlike `mapAccessLevelString`, which defaults to ViewOnly). */
function levelOf(label: string | null | undefined): AccessLevel | undefined {
  switch (label) {
    case 'ViewOnly':
      return AccessLevel.ViewOnly;
    case 'Collaborate':
      return AccessLevel.Collaborate;
    case 'FullAccess':
      return AccessLevel.FullAccess;
    default:
      return undefined;
  }
}

/**
 * The `/me` level that decides a create on this record for a level-decided caller: a project (the only records `/me`
 * carries a level for) unless the caller has `writeAccessPerRecord`. Fail-safe, as `useAccessLevel` and the R3 record
 * view: while `/me` is unknown (or failed) and when the project has no entry, it is View Only, so nothing is offered on
 * a project before `/me` has answered. `undefined` for every other record and every per-record caller (the list decides).
 */
export function decidingLevelOf(
  root: string | undefined,
  recordId: string,
  callerContext: ExternalUserContextResponse | null
): AccessLevel | undefined {
  if (root !== 'projects' || callerContext?.writeAccessPerRecord === true) return undefined;
  const entry = callerContext?.projects.find(p => p.projectId.toLowerCase() === recordId.toLowerCase());
  return mapAccessLevelString(entry?.accessLevel);
}

/**
 * The caller's `/me` level for this record as an ANSWER, for the header's "Your access": a project, a caller WITHOUT
 * `writeAccessPerRecord` (whose level says only what they may see), and a recognised label. `undefined` for every other
 * record, every per-record caller, while `/me` is unknown and when the project has no entry (nothing is guessed).
 */
export function recordLevelOf(
  root: string | undefined,
  recordId: string,
  callerContext: ExternalUserContextResponse | null
): AccessLevel | undefined {
  if (root !== 'projects' || !callerContext || callerContext.writeAccessPerRecord === true) return undefined;
  const entry = callerContext.projects.find(p => p.projectId.toLowerCase() === recordId.toLowerCase());
  return levelOf(entry?.accessLevel);
}

/** What the template knows about the caller on this record, for "Your access" and the read-only lines. */
export interface RecordAccessFacts {
  /** `/me`, or null while it is unknown (not read, or its read failed). */
  callerContext: ExternalUserContextResponse | null;
  /** The caller's `/me` level for this record as an answer (`recordLevelOf`); undefined when there is none. */
  level: AccessLevel | undefined;
  /**
   * The level that gates Upload, Add to-do and Add event is a fail-safe GUESS, not an answer: a level-decided project
   * caller whose `/me` has no (recognisable) entry for the project. `decidingLevelOf` then hides those actions as View
   * Only, but what the caller may do there is unknown, so nothing is said about it (FR-07: unknown shows nothing).
   */
  levelGuessed: boolean;
}

export function recordAccessFactsOf(
  root: string | undefined,
  recordId: string,
  callerContext: ExternalUserContextResponse | null
): RecordAccessFacts {
  const level = recordLevelOf(root, recordId, callerContext);
  const levelGuessed =
    callerContext !== null && level === undefined && decidingLevelOf(root, recordId, callerContext) !== undefined;
  return { callerContext, level, levelGuessed };
}

/** The rights a project caller's `/me` level gates (the message thread is not one: it follows the thread alone). */
const LEVEL_GATED_RIGHTS: readonly (RecordCreateActionId | RecordSectionRightId)[] = ['upload', 'addTodo', 'addEvent'];

// ---------------------------------------------------------------------------
// What the template knows: the rights the record's lists reported
// ---------------------------------------------------------------------------

export type RecordCreateRights = Readonly<Partial<Record<RecordCreateActionId | RecordSectionRightId, CreateRight>>>;

/**
 * The rights the record's lists reported: with their toolbar offers (only the create actions this record's toolbar
 * lists; a list that has not reported yet is unknown), plus the rights sections reported without a toolbar action
 * (`sectionRights`, e.g. the invoice Files list's Upload).
 */
export function createRightsOf(
  toolbar: readonly RecordToolbarActionId[],
  offers: Partial<Record<RecordToolbarActionId, RecordHostAction>>,
  sectionRights: Partial<Record<RecordSectionRightId, CreateRight>> = {}
): RecordCreateRights {
  const rights: Partial<Record<RecordCreateActionId | RecordSectionRightId, CreateRight>> = { ...sectionRights };
  for (const id of RECORD_CREATE_ACTIONS) {
    if (toolbar.includes(id)) rights[id] = offers[id]?.right ?? null;
  }
  return rights;
}

/** Whether the toolbar shows a create action: its list offers it AND reported the right as `true` (never a guess). */
export function isCreateActionOffered(offer: RecordHostAction | undefined): boolean {
  return offer?.visible === true && offer.right === true;
}

export function isRecordCreateAction(id: RecordToolbarActionId): id is RecordCreateActionId {
  return (RECORD_CREATE_ACTIONS as readonly string[]).includes(id);
}

// ---------------------------------------------------------------------------
// (a2) "Submit invoice" (R4 task 007, FR-10)
// ---------------------------------------------------------------------------

/** The invoice-submission kind of each record entity that offers "Submit invoice"; every other entity has none. */
const INVOICE_KIND_OF_ENTITY: Readonly<Record<string, InvoiceCoreRecordKind>> = {
  sprk_matter: 'matter',
  sprk_project: 'project',
  sprk_workassignment: 'workassignment',
};

export function invoiceKindOfEntity(entity: string): InvoiceCoreRecordKind | undefined {
  return Object.prototype.hasOwnProperty.call(INVOICE_KIND_OF_ENTITY, entity)
    ? INVOICE_KIND_OF_ENTITY[entity]
    : undefined;
}

/**
 * One firm's firm-records answer as the rule reads it: `'failed'` for a read that failed, `undefined` while it has not
 * answered. The answer's own `partial` / `truncated` flags are not used: a record LISTED is linked, whatever else is
 * flagged, and one that is NOT listed is not shown even when the answer says it may be incomplete (hide, don't guess:
 * the action is never offered on `firms` alone, task 007 escalation 2).
 */
export type FirmRecordsRead = InvoiceFirmRecordsResponse | 'failed' | undefined;

/** The ids of the caller's firms from `/me`: `[]` when `firms` is absent, null or empty (the `InvoicesWidget` rule). */
export function callerFirmIdsOf(callerContext: ExternalUserContextResponse | null): readonly string[] {
  const firms = callerContext?.firms;
  return Array.isArray(firms) ? firms.map(f => f.firmId) : [];
}

const FIRM_RECORD_LIST_OF_KIND: Readonly<Record<InvoiceCoreRecordKind, 'matters' | 'projects' | 'workAssignments'>> = {
  matter: 'matters',
  project: 'projects',
  workassignment: 'workAssignments',
};

/**
 * Whether the record view offers "Submit invoice" on this record. `/me` `firms` must be a non-empty array AND the
 * firm-records answer of at least one of those firms must list this record's id under its kind. A failed, unanswered or
 * non-listing answer is a no (hidden), for every firm. The server re-checks Read, Create and the firm link on submit; this
 * is the display rule only.
 */
export function submitInvoiceRightOf(input: {
  callerContext: ExternalUserContextResponse | null;
  kind: InvoiceCoreRecordKind | undefined;
  recordId: string;
  reads: Readonly<Record<string, FirmRecordsRead>>;
}): boolean {
  const { callerContext, kind, recordId, reads } = input;
  if (!kind) return false;
  const wanted = recordId.toLowerCase();
  return callerFirmIdsOf(callerContext).some(firmId => {
    const read = reads[firmId];
    if (!read || read === 'failed') return false;
    return (read[FIRM_RECORD_LIST_OF_KIND[kind]] ?? []).some(id => id.toLowerCase() === wanted);
  });
}

// ---------------------------------------------------------------------------
// (b) "Your access"
// ---------------------------------------------------------------------------

/**
 * The header's "Your access" value (FR-07), or `null` to show nothing: a project's `/me` level label for a level-decided
 * caller; otherwise "Can contribute" once any of the record's lists answered `canCreate: true`, and "Read only" once
 * every one of them answered and all said no. Unknown (`/me` not read, a list not loaded or failed, a project level that
 * is only a fail-safe guess) shows nothing.
 */
export function yourAccessOf(
  { callerContext, level, levelGuessed }: RecordAccessFacts,
  rights: RecordCreateRights
): string | null {
  if (!callerContext || levelGuessed) return null;
  if (level !== undefined) return RECORD_ACCESS_TEXT.level[level];
  const answers = Object.values(rights);
  if (answers.length === 0) return null;
  if (answers.some(a => a === true)) return RECORD_ACCESS_TEXT.canContribute;
  if (answers.every(a => a === false)) return RECORD_ACCESS_TEXT.readOnly;
  return null;
}

// ---------------------------------------------------------------------------
// (c) The read-only line of a section
// ---------------------------------------------------------------------------

/**
 * The one line saying why a section is read-only (FR-07), or `null` when it is not, or not known to be. Only a section
 * that owns a create right has one (Documents, Files, Messages, Tasks & events). The line names a grant level only when
 * the deciding `/me` level is KNOWN to be View Only; every other "no" (a ceiling, a per-record caller, a record `/me`
 * has no level for) gets the neutral wording. A right decided by a guessed level (`levelGuessed`) is not known, so it
 * gets no line.
 */
export function readOnlyNoteOf(
  section: RecordSectionId,
  rights: RecordCreateRights,
  facts: RecordAccessFacts
): string | null {
  if (!facts.callerContext) return null;
  const lines = facts.level === AccessLevel.ViewOnly ? RECORD_ACCESS_TEXT.grantLines : RECORD_ACCESS_TEXT.neutralLines;
  const known: RecordCreateRights = facts.levelGuessed
    ? Object.fromEntries(
        Object.entries(rights).filter(([id]) => !(LEVEL_GATED_RIGHTS as readonly string[]).includes(id))
      )
    : rights;
  switch (section) {
    case 'documents':
      return known.upload === false ? lines.documents : null;
    case 'files':
      return known.filesUpload === false ? lines.files : null;
    case 'messages':
      return known.newMessage === false ? lines.messages : null;
    case 'tasksEvents': {
      const todos = known.addTodo === false;
      const events = known.addEvent === false;
      if (todos && events) return lines.tasksEvents;
      if (todos) return lines.todos;
      if (events) return lines.events;
      return null;
    }
    default:
      return null;
  }
}
