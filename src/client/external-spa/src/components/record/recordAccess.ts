/**
 * recordAccess — what the caller may do on one record, from what the BFF answered for it
 * (spaarke-SPA-external-access-platform-r4 task 006, FR-06 / FR-07 / NFR-04).
 *
 * ONE PLACE FOR:
 *   (a) the right behind each of the record's create actions (Upload, New message, Add task, Add event; each in its own
 *       section's header since task 009): `createRightOf`, used by every list inside the record template, so they follow
 *       one rule; and the list-host rule of EventsCalendar and a project's DocumentLibrary, the same in and out of the
 *       template (`levelVerdictOf`, `listCreateRightOf`; D-9: the project page follows it too). Add task adds the list's
 *       `canCreateTask` (`createTaskRightOf`, task 011);
 *   (a2) whether "Submit invoice" is offered (task 007, FR-10): `submitInvoiceRightOf`, from `/me` `firms` and the
 *       firm-records answers (read by `actions/SubmitInvoiceAction.tsx`);
 *   (b) the header's "Your access" value: `yourAccessOf`, from `recordAccessFactsOf` (the caller's `/me` level for the
 *       record, nothing else);
 *   (c) the one short "why" line a read-only section shows: `readOnlyNoteOf`;
 *   and the texts of (b) and (c): `RECORD_ACCESS_TEXT` (the level labels: `ACCESS_LEVEL_LABELS`, the ONE label map of the
 *   SPA since task 011 / D-11).
 *
 * WHERE THE ANSWERS COME FROM (never a guess, ADR-003 fail closed):
 *   - each list's `canCreate` (and, for Add task, `canCreateTask`): the BFF's answer of the matching POST for this caller on
 *     this record (task 002 / 008: the documents, event and message lists of projects, matters and work assignments; the
 *     message thread of every record). It is the ONLY source for a matter, a work assignment, an invoice and a service
 *     request.
 *   - a project's `/me` level, for a caller WITHOUT `writeAccessPerRecord` (the ONE exception: a level decides writes
 *     only there): it must also allow a create. When the project's list also answered, both must allow (on the project
 *     page too, D-9). A matter's or work assignment's `/me` `records` level never decides a write.
 *   - the LABEL ("Your access", task 011): `/me` `projects` for a project, `/me` `records` for a matter or work assignment
 *     (task 008). Display only. A `writeAccessPerRecord` caller is the composed READ view (uac-r2 A2), which would
 *     contradict their working create actions, so the line is not shown for them at all and their actions keep following
 *     the lists' `canCreate` / `canCreateTask`. A record `/me` has no entry for (an invoice, a service request, a
 *     document, or a matter the BFF did not list) shows no level.
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

/** The create actions a record's lists offer to the template (task 004; in their sections' headers since task 009). */
export type RecordCreateActionId = Extract<RecordToolbarActionId, 'upload' | 'newMessage' | 'addTask' | 'addEvent'>;

export const RECORD_CREATE_ACTIONS: readonly RecordCreateActionId[] = ['upload', 'newMessage', 'addTask', 'addEvent'];

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
  /** Tasks & events: its event list is read-only. */
  events: string;
}

/**
 * The label of each access level, as Manage Access shows it ("View Only" / "Collaborate" / "Full Access"; owner
 * 2026-11-13). The ONE label map of the SPA (task 011, D-11 #2718): `ProjectPage`, `ContactsOrganizations`,
 * `OutsideCounselDashboard` and `InviteUserDialog` read it too.
 */
export const ACCESS_LEVEL_LABELS: Readonly<Record<AccessLevel, string>> = {
  [AccessLevel.ViewOnly]: 'View Only',
  [AccessLevel.Collaborate]: 'Collaborate',
  [AccessLevel.FullAccess]: 'Full Access',
};

export const RECORD_ACCESS_TEXT: {
  /** "Your access" for a record, from its `/me` level (the labels the rest of the SPA uses). */
  level: Readonly<Record<AccessLevel, string>>;
  /**
   * For a caller whose deciding `/me` level is KNOWN to be View Only (a project): the line names the level the action
   * needs. Only then is the level known to be the reason.
   */
  grantLines: ReadOnlyLines;
  /**
   * Every other read-only answer (a Collaborate / Full Access caller refused by a role ceiling, a record `/me` has no
   * level for - an invoice, a service request -, a workforce contact, a `writeAccessPerRecord` caller): the list said only
   * `false`, not why, so no grant level is named.
   */
  neutralLines: ReadOnlyLines;
} = {
  level: ACCESS_LEVEL_LABELS,
  grantLines: {
    documents: 'You can view these documents. Uploading needs Collaborate access.',
    files: 'You can view these files. Uploading needs Collaborate access.',
    messages: 'You can read this thread. Posting needs Collaborate access.',
    events: 'You can view these events. Adding one needs Collaborate access.',
  },
  neutralLines: {
    documents: 'You can view these documents, but not upload to this record.',
    files: 'You can view these files, but not upload to this record.',
    messages: 'You can read this thread, but not post to it.',
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
   * What the caller's `/me` level says. Set ONLY for a project caller WITHOUT `writeAccessPerRecord` (the one case where
   * a level decides a write); `undefined` for every other record and caller (the list alone decides; a matter's or work
   * assignment's level is display only).
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
 * The create rule of a list that owns a create dialog (EventsCalendar, DocumentLibrary's Upload on a project),
 * in one place and the same in and out of the record template:
 *   - `right`: the shared rule (`createRightOf`), reported with the action offer;
 *   - `allowed`: whether the list offers its create at all: only a known `true`.
 * D-9 (#2716): the project page used to offer on the `/me` level alone (`levelAllows ?? …`), so a level-decided caller
 * whose list answered `canCreate: false` (a workforce role ceiling the level does not show) was offered a create the POST
 * refuses with 403. Now the level and the list must both allow there too, and nothing is offered before the list answers.
 */
export function listCreateRightOf(input: CreateRightInput): { right: CreateRight; allowed: boolean } {
  const right = createRightOf(input);
  return { right, allowed: right === true };
}

/**
 * The right behind "Add task" (task 011): the event right (`createRightOf`: the list's `canCreate`, and the `/me` level
 * where it decides) AND the list's own `canCreateTask` (the BFF's answer for the Task type, task 008). Only an explicit
 * `true` from both is a yes: a list that says nothing about tasks (an older BFF, a failed read) is unknown, never "can",
 * so the action stays hidden (hide, don't guess). `false` from either is a no; an unknown event right is unknown.
 */
export function createTaskRightOf(eventRight: CreateRight, listCanCreateTask: boolean | null | undefined): CreateRight {
  if (eventRight === false) return false;
  if (eventRight === null) return null;
  return typeof listCanCreateTask === 'boolean' ? listCanCreateTask : null;
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

/** `/me` `records[].recordType` of each route root that has an entry there (task 008): matters and work assignments. */
const RECORD_TYPE_OF_ROOT: Readonly<Record<string, string>> = {
  matters: 'matter',
  workassignments: 'workassignment',
};

/**
 * The caller's `/me` level for this record as an ANSWER, for the header's "Your access" (task 011): a project's `/me`
 * `projects` entry, or a matter's / work assignment's `/me` `records` entry (task 008), with a recognised label.
 * `undefined` for every other record (an invoice, a service request, a document: `/me` has no level for them), while
 * `/me` is unknown, and when the record has no entry (nothing is guessed).
 *
 * DISPLAY ONLY (uac-r2 A2). A level decides a write only for a project caller without `writeAccessPerRecord`
 * (`decidingLevelOf`); a matter's or work assignment's `records` level never does. For a `writeAccessPerRecord` caller
 * the level is the composed READ view: `yourAccessOf` does not show it, `decidingLevelOf` answers `undefined`, and their
 * create actions keep following the lists' `canCreate` / `canCreateTask`.
 */
export function recordLevelOf(
  root: string | undefined,
  recordId: string,
  callerContext: ExternalUserContextResponse | null
): AccessLevel | undefined {
  if (!root || !callerContext) return undefined;
  const wanted = recordId.toLowerCase();
  if (root === 'projects') {
    return levelOf(callerContext.projects.find(p => p.projectId.toLowerCase() === wanted)?.accessLevel);
  }
  const recordType = Object.prototype.hasOwnProperty.call(RECORD_TYPE_OF_ROOT, root)
    ? RECORD_TYPE_OF_ROOT[root]
    : undefined;
  if (!recordType || !Array.isArray(callerContext.records)) return undefined;
  const entry = callerContext.records.find(r => r.recordType === recordType && r.recordId.toLowerCase() === wanted);
  return levelOf(entry?.accessLevel);
}

/** What the template knows about the caller on this record, for "Your access" and the read-only lines. */
export interface RecordAccessFacts {
  /** `/me`, or null while it is unknown (not read, or its read failed). */
  callerContext: ExternalUserContextResponse | null;
  /** The caller's `/me` level for this record as an answer (`recordLevelOf`), to display; undefined when there is none. */
  level: AccessLevel | undefined;
  /**
   * The level that gates Upload, Add task and Add event is a fail-safe GUESS, not an answer: a level-decided project
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
const LEVEL_GATED_RIGHTS: readonly (RecordCreateActionId | RecordSectionRightId)[] = ['upload', 'addTask', 'addEvent'];

// ---------------------------------------------------------------------------
// What the template knows: the rights the record's lists reported
// ---------------------------------------------------------------------------

export type RecordCreateRights = Readonly<Partial<Record<RecordCreateActionId | RecordSectionRightId, CreateRight>>>;

/**
 * The rights the record's lists reported: with their action offers (only the create actions this record's config lists,
 * in its toolbar or a section's header; a list that has not reported yet is unknown), plus the rights sections reported
 * without an action of the template's (`sectionRights`, e.g. the invoice Files list's Upload).
 */
export function createRightsOf(
  actionIds: readonly RecordToolbarActionId[],
  offers: Partial<Record<RecordToolbarActionId, RecordHostAction>>,
  sectionRights: Partial<Record<RecordSectionRightId, CreateRight>> = {}
): RecordCreateRights {
  const rights: Partial<Record<RecordCreateActionId | RecordSectionRightId, CreateRight>> = { ...sectionRights };
  for (const id of RECORD_CREATE_ACTIONS) {
    if (actionIds.includes(id)) rights[id] = offers[id]?.right ?? null;
  }
  return rights;
}

/** Whether a create action is shown: its list offers it AND reported the right as `true` (never a guess). */
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
 * The header's "Your access" value (FR-07), or `null` to show nothing: the caller's `/me` level for this record, as the
 * Manage Access label ("View Only" / "Collaborate" / "Full Access"; task 011, owner 2026-11-13). Nothing is inferred from
 * the lists: a record `/me` has no level for (an invoice, a service request, a document, a matter or work assignment the
 * BFF did not list), `/me` not read, or a project level that is only a fail-safe guess, shows nothing. So does a
 * `writeAccessPerRecord` caller (review round 1): their level is the composed READ view, and "View Only" beside create
 * actions that work would contradict itself. The lists' rights decide the actions, never this label.
 */
export function yourAccessOf({ callerContext, level, levelGuessed }: RecordAccessFacts): string | null {
  if (!callerContext || levelGuessed || level === undefined) return null;
  if (callerContext.writeAccessPerRecord === true) return null;
  return RECORD_ACCESS_TEXT.level[level];
}

// ---------------------------------------------------------------------------
// (c) The read-only line of a section
// ---------------------------------------------------------------------------

/**
 * The one line saying why a section is read-only (FR-07), or `null` when it is not, or not known to be. Only a section
 * that owns a create right has one (Documents, Files, Messages, Tasks & events). The line names a grant level only when
 * the deciding `/me` level is KNOWN to be View Only; every other "no" (a ceiling, a per-record caller, a record `/me`
 * has no level for) gets the neutral wording. (Task 011: a level decides only for a project caller without
 * `writeAccessPerRecord`; a per-record caller's level is the read view, so it never triggers the grant wording.) A right decided by a guessed level (`levelGuessed`) is not known, so it
 * gets no line.
 */
export function readOnlyNoteOf(
  section: RecordSectionId,
  rights: RecordCreateRights,
  facts: RecordAccessFacts
): string | null {
  if (!facts.callerContext) return null;
  // The grant wording needs the level to be THE reason: a View Only level that decides (a caller without
  // `writeAccessPerRecord`). For a per-record caller the label is only the read view (A2): neutral wording.
  const levelDecides = facts.callerContext.writeAccessPerRecord !== true;
  const lines =
    facts.level === AccessLevel.ViewOnly && levelDecides
      ? RECORD_ACCESS_TEXT.grantLines
      : RECORD_ACCESS_TEXT.neutralLines;
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
    case 'tasksEvents':
      return known.addEvent === false ? lines.events : null;
    default:
      return null;
  }
}
