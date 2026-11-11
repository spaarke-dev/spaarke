/**
 * Tasks & events — the record's events and to-dos (R3 task 057 `EventsAndTasksSection`, moved from R3's record-detail modal;
 * task 005).
 *
 * The existing `EventsCalendar` / `SmartTodo` on the record's own routes (GET /api/v1/external/{projects|matters|
 * workassignments}/{id}/events and /todos; the server applies the record's rights and the every-root rule, the client
 * filters nothing). Read-only, as in R3: `AccessLevel.ViewOnly` hides create and toggle, and an event or to-do the caller
 * created shows Delete (task 054). Task 006 replaces the two ViewOnly props with the per-record right (FR-06). A 403 shows
 * the shared denied state and a failed read a retryable error, inside the section only. Refresh (or Try again) remounts
 * both lists, which re-read.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { AccessLevel } from '../../../types';
import type { EventsTodosRoot } from '../../../api/web-api-client';
import { EventsCalendar } from '../../EventsCalendar';
import { SmartTodo } from '../../SmartTodo';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
});

/** The entities whose record shows events and tasks, and the `{root}` of their task 039 / 007 routes. */
const EVENTS_TODOS_ROOTS: Readonly<Record<string, EventsTodosRoot>> = {
  sprk_project: 'projects',
  sprk_matter: 'matters',
  sprk_workassignment: 'workassignments',
};

export const TasksEventsSection: React.FC<RecordSectionContext> = ({ detail, recordId, caller, refreshSignal }) => {
  const styles = useStyles();
  // An entity with no events/to-do routes renders nothing (no config offers one; this keeps a wrong config inert).
  const root = Object.prototype.hasOwnProperty.call(EVENTS_TODOS_ROOTS, detail.entity)
    ? EVENTS_TODOS_ROOTS[detail.entity]
    : undefined;
  if (!root) return null;
  return (
    <RecordSectionCard id="tasksEvents">
      <div className={styles.body} data-testid="record-detail-events" key={refreshSignal}>
        <EventsCalendar projectId={recordId} root={root} accessLevel={AccessLevel.ViewOnly} caller={caller} />
        <SmartTodo projectId={recordId} root={root} accessLevel={AccessLevel.ViewOnly} caller={caller} />
      </div>
    </RecordSectionCard>
  );
};
