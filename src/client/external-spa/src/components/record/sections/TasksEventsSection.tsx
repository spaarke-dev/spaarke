/**
 * Tasks & events — the record's events (R3 task 057 `EventsAndTasksSection`, moved from R3's record-detail modal; task 005).
 *
 * The existing `EventsCalendar` on the record's own route (GET /api/v1/external/{projects|matters|workassignments}/{id}/
 * events; the server applies the record's rights and the every-root rule, the client filters nothing). An event the caller
 * created shows Delete (task 054). A 403 shows the shared denied state and a failed read a retryable error, inside the
 * section only. Refresh (or Try again) remounts the list, which re-reads.
 *
 * Owner 2026-11-12 (task 009): external users work only with `sprk_event`; the section lists events only. Task 011 adds
 * the Task-typed event list and its create action here.
 *
 * ADD EVENT (task 004, FR-03; task 009). The list offers its action through `offerAction('addEvent')`, the template hands
 * it back as `headerActions` (the button in this card's header), and the list renders its create dialog (a `FormModal`)
 * inside this section, so the dialog is a descendant of the record modal; a create in flight holds the modal.
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
          onBusyChange={onBusyChange}
        />
      </div>
    </RecordSectionCard>
  );
};
