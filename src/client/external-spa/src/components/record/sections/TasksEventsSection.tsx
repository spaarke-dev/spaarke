/**
 * Tasks & events — the record's tasks and events (R3 task 057 `EventsAndTasksSection`, moved from R3's record-detail modal; task 005).
 *
 * The existing `EventsCalendar` on the record's own route (GET /api/v1/external/{projects|matters|workassignments}/{id}/
 * events; the server applies the record's rights and the every-root rule, the client filters nothing). An event the caller
 * created shows Delete (task 054). A 403 shows the shared denied state and a failed read a retryable error, inside the
 * section only. Refresh (or Try again) remounts the list, which re-reads.
 *
 * Owner 2026-11-12 (task 009): external users work only with `sprk_event`; a Task is an event of type Task (task 011). The
 * list shows one read in two groups: Tasks (the events the BFF flags `isTask`) and Events (the rest).
 *
 * ADD TASK / ADD EVENT (task 004, FR-03; tasks 009, 011). The list offers its actions through `offerAction('addTask')` and
 * `offerAction('addEvent')`, the template hands them back as `headerActions` (the buttons in this card's header), and the
 * list renders both create dialogs (a `FormModal` each) inside this section, so a dialog is a descendant of the record
 * modal. A create in flight holds the modal: the list reports busy while EITHER dialog is posting, so one going idle never
 * releases the other's hold. Add task is shown only when the list's `canCreateTask` is true; Add event follows
 * `canCreate`. Add task sends `eventType: "task"`; Add event sends no type.
 *
 * WHO MAY ADD (task 006, FR-06 / FR-07). No level is assumed: the list follows `createRightOf` (recordAccess.ts) - its
 * `canCreate` (task 002: every caller, every root), and for a project caller without `writeAccessPerRecord` the `/me`
 * level, which must agree with it. A create posts to the record's own route (`createRootEvent`). When the list is
 * read-only the card shows one line saying why (`readOnlyNote`).
 */
import * as React from 'react';
import type { EventsRoot } from '../../../api/web-api-client';
import { EventsCalendar } from '../../EventsCalendar';
import { decidingLevelOf } from '../recordAccess';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

/** The entities whose record shows events, and the `{root}` of their task 039 / 007 routes. */
const EVENTS_ROOTS: Readonly<Record<string, EventsRoot>> = {
  sprk_project: 'projects',
  sprk_matter: 'matters',
  sprk_workassignment: 'workassignments',
};

export const TasksEventsSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  callerContext,
  caller,
  refreshSignal,
  onBusyChange,
  offerAction,
  headerActions,
  readOnlyNote,
}) => {
  // An entity with no events route renders nothing (no config offers one; this keeps a wrong config inert).
  const root = Object.prototype.hasOwnProperty.call(EVENTS_ROOTS, detail.entity)
    ? EVENTS_ROOTS[detail.entity]
    : undefined;
  if (!root) return null;
  // The caller's `/me` level, only where it decides (a project, a caller whose write access is not per record; View Only
  // until `/me` has answered, as Upload); none otherwise, so the list follows its `canCreate`.
  const level = decidingLevelOf(root, recordId, callerContext);
  const writeAccessPerRecord = callerContext?.writeAccessPerRecord === true;
  return (
    <RecordSectionCard id="tasksEvents" actions={headerActions} note={readOnlyNote}>
      <div data-testid="record-detail-events" key={refreshSignal}>
        <EventsCalendar
          projectId={recordId}
          root={root}
          accessLevel={level}
          writeAccessPerRecord={writeAccessPerRecord}
          caller={caller}
          onCreateActionChange={offerAction('addEvent')}
          onCreateTaskActionChange={offerAction('addTask')}
          onBusyChange={onBusyChange}
        />
      </div>
    </RecordSectionCard>
  );
};
