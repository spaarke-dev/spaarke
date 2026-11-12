/**
 * Tasks & events — the record's events and to-dos (R3 task 057 `EventsAndTasksSection`, moved from R3's record-detail modal;
 * task 005).
 *
 * The existing `EventsCalendar` / `SmartTodo` on the record's own routes (GET /api/v1/external/{projects|matters|
 * workassignments}/{id}/events and /todos; the server applies the record's rights and the every-root rule, the client
 * filters nothing). An event or to-do the caller created shows Delete (task 054). A 403 shows the shared denied state and
 * a failed read a retryable error, inside the section only. Refresh (or Try again) remounts both lists, which re-read.
 *
 * ADD EVENT / ADD TO-DO (task 004, FR-03). Each list offers its toolbar action through `offerAction` and renders its
 * create dialog (a `FormModal`) inside this section, so the dialog is a descendant of the record modal; a create in
 * flight holds the modal.
 *
 * WHO MAY ADD (task 006, FR-06 / FR-07). No level is assumed: the lists follow `createRightOf` (recordAccess.ts) - each
 * list's `canCreate` (task 002: every caller, every root), and for a project caller without `writeAccessPerRecord` the
 * `/me` level, which must agree with it. A create posts to the record's own route (`createRootTodo` / `createRootEvent`).
 * The to-do status toggle is offered only where a server answer says the caller may PATCH (the project level); a matter
 * or work assignment has none, so the toggle is not offered there. When either list is read-only the card shows one line
 * saying why (`readOnlyNote`).
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import type { EventsTodosRoot } from '../../../api/web-api-client';
import { EventsCalendar } from '../../EventsCalendar';
import { SmartTodo } from '../../SmartTodo';
import { decidingLevelOf } from '../recordAccess';
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

export const TasksEventsSection: React.FC<RecordSectionContext> = ({
  detail,
  recordId,
  callerContext,
  caller,
  refreshSignal,
  onBusyChange,
  offerAction,
  readOnlyNote,
}) => {
  const styles = useStyles();
  // Both lists report one busy flag to the template, which keeps a single hold per section. Each list is its own holder
  // here: one list going idle (its create dialog unmounting, say) must not release the other's hold on an in-flight post.
  const holds = React.useRef({ events: false, todos: false });
  const reportEvents = React.useCallback(
    (busy: boolean) => {
      holds.current.events = busy;
      onBusyChange(holds.current.events || holds.current.todos);
    },
    [onBusyChange]
  );
  const reportTodos = React.useCallback(
    (busy: boolean) => {
      holds.current.todos = busy;
      onBusyChange(holds.current.events || holds.current.todos);
    },
    [onBusyChange]
  );
  // An entity with no events/to-do routes renders nothing (no config offers one; this keeps a wrong config inert).
  const root = Object.prototype.hasOwnProperty.call(EVENTS_TODOS_ROOTS, detail.entity)
    ? EVENTS_TODOS_ROOTS[detail.entity]
    : undefined;
  if (!root) return null;
  // The caller's `/me` level, only where it decides (a project, a caller whose write access is not per record; View Only
  // until `/me` has answered, as Upload); none otherwise, so the lists follow their `canCreate`.
  const level = decidingLevelOf(root, recordId, callerContext);
  const writeAccessPerRecord = callerContext?.writeAccessPerRecord === true;
  return (
    <RecordSectionCard id="tasksEvents" note={readOnlyNote}>
      <div className={styles.body} data-testid="record-detail-events" key={refreshSignal}>
        <EventsCalendar
          projectId={recordId}
          root={root}
          accessLevel={level}
          writeAccessPerRecord={writeAccessPerRecord}
          caller={caller}
          onCreateActionChange={offerAction('addEvent')}
          onBusyChange={reportEvents}
        />
        <SmartTodo
          projectId={recordId}
          root={root}
          accessLevel={level}
          writeAccessPerRecord={writeAccessPerRecord}
          caller={caller}
          onCreateActionChange={offerAction('addTodo')}
          onBusyChange={reportTodos}
        />
      </div>
    </RecordSectionCard>
  );
};
